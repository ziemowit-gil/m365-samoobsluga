using System.Net;
using System.Text;
using System.Text.Json;
using M365Samoobsluga.Core.Services;
using M365Samoobsluga.Core.Validation;
using M365Samoobsluga.Graph.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace M365Samoobsluga.Tests;

public class AliasAutoTests
{
    [Fact]
    public void Dodanie_aliasu_zachowuje_adres_glowny_i_nie_dubluje()
    {
        var cur = new[] { "SMTP:jan@feer.org.pl", "smtp:jan.k@feer.org.pl" };
        var added = AliasList.Apply(cur, AliasChangeKind.Add, "Anna@feer.org.pl");
        Assert.Equal(["SMTP:jan@feer.org.pl", "smtp:jan.k@feer.org.pl", "smtp:anna@feer.org.pl"], added);
        Assert.Equal(added, AliasList.Apply(added, AliasChangeKind.Add, "anna@feer.org.pl"));
    }

    [Fact]
    public void Usuniecie_nie_rusza_adresu_glownego()
    {
        var cur = new[] { "SMTP:jan@feer.org.pl", "smtp:jan.k@feer.org.pl" };
        Assert.Equal(["SMTP:jan@feer.org.pl"], AliasList.Apply(cur, AliasChangeKind.Remove, "JAN.K@feer.org.pl"));
        Assert.Equal(cur, AliasList.Apply(cur, AliasChangeKind.Remove, "jan@feer.org.pl"));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("Postmaster")]
    [InlineData("noreply")]
    [InlineData("kontakt")]
    public void Nazwy_zarezerwowane_sa_odrzucane(string name)
    {
        var r = AddressValidator.ValidateAlias(name, "feer.org.pl", ["feer.org.pl"], []);
        Assert.False(r.IsValid);
        Assert.Contains("zarezerwowana", r.Error);
    }

    sealed class Fake(Func<string, string, (HttpStatusCode, string)> reply) : HttpMessageHandler
    {
        public List<(string Cmdlet, string Body)> Calls = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var cmd = r.Headers.GetValues("X-CmdletName").Single(); var body = await r.Content!.ReadAsStringAsync(ct);
            Calls.Add((cmd, body)); var (c, t) = reply(cmd, body);
            return new HttpResponseMessage(c) { Content = new StringContent(t, Encoding.UTF8, "application/json") };
        }
    }

    private static string Jwt() => "e30." + Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"tid\":\"t-1\"}")).TrimEnd('=') + ".s";
    private static ExchangeRestClient Rest(Fake f) => new(_ => Task.FromResult(Jwt()), "firma.onmicrosoft.com", new HttpClient(f));

    sealed class Queue : IAliasChangeExecutor
    {
        public List<AliasChange> Got = [];
        public bool AppliesImmediately => false;
        public Task<AliasOutcome> ExecuteAsync(AliasChange c, CancellationToken ct = default) { Got.Add(c); return Task.FromResult(AliasOutcome.Queued); }
    }

    private static ExchangeAliasChangeExecutor Exec(Fake f, Queue q, bool configured = true) =>
        new(() => Rest(f), () => configured, q, NullLogger<ExchangeAliasChangeExecutor>.Instance);

    private static (HttpStatusCode, string) Ok(string cmd) => cmd switch
    {
        "Get-Recipient" => (HttpStatusCode.OK, "{\"value\":[]}"),
        "Get-Mailbox" => (HttpStatusCode.OK, "{\"value\":[{\"EmailAddresses\":[\"SMTP:jan@feer.org.pl\",\"smtp:jan.k@feer.org.pl\"]}]}"),
        _ => (HttpStatusCode.OK, "{\"value\":[]}"),
    };

    [Fact]
    public async Task Wolny_adres_jest_dodawany_od_razu_na_skrzynce_zalogowanej_osoby()
    {
        var f = new Fake((c, _) => Ok(c)); var q = new Queue();
        var r = await Exec(f, q).ExecuteAsync(new AliasChange("jan@feer.org.pl", AliasChangeKind.Add, "nowy.alias@feer.org.pl"));
        Assert.Equal(AliasOutcome.Applied, r);
        Assert.Empty(q.Got);
        var set = f.Calls.Single(c => c.Cmdlet == "Set-Mailbox");
        using var d = JsonDocument.Parse(set.Body);
        var p = d.RootElement.GetProperty("CmdletInput").GetProperty("Parameters");
        Assert.Equal("jan@feer.org.pl", p.GetProperty("Identity").GetString());
        Assert.Equal(["SMTP:jan@feer.org.pl", "smtp:jan.k@feer.org.pl", "smtp:nowy.alias@feer.org.pl"], p.GetProperty("EmailAddresses").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Fact]
    public async Task Zajety_adres_jest_odrzucany_bez_zapisu()
    {
        var f = new Fake((c, _) => c == "Get-Recipient" ? (HttpStatusCode.OK, "{\"value\":[{\"Name\":\"ktos\"}]}") : Ok(c)); var q = new Queue();
        var ex = await Assert.ThrowsAsync<UserFacingException>(() => Exec(f, q).ExecuteAsync(new AliasChange("jan@feer.org.pl", AliasChangeKind.Add, "zajety@feer.org.pl")));
        Assert.Contains("zajęty", ex.Message);
        Assert.DoesNotContain(f.Calls, c => c.Cmdlet == "Set-Mailbox");
        Assert.Empty(q.Got);
    }

    [Fact]
    public async Task Awaria_exchange_zapisuje_wniosek_w_kolejce()
    {
        var f = new Fake((_, _) => (HttpStatusCode.Forbidden, "")); var q = new Queue();
        var r = await Exec(f, q).ExecuteAsync(new AliasChange("jan@feer.org.pl", AliasChangeKind.Add, "nowy@feer.org.pl"));
        Assert.Equal(AliasOutcome.Queued, r);
        Assert.Single(q.Got);
    }

    [Fact]
    public async Task Brak_konfiguracji_exchange_to_kolejka_bez_wywolan()
    {
        var f = new Fake((c, _) => Ok(c)); var q = new Queue();
        var e = Exec(f, q, configured: false);
        Assert.False(e.AppliesImmediately);
        Assert.Equal(AliasOutcome.Queued, await e.ExecuteAsync(new AliasChange("jan@feer.org.pl", AliasChangeKind.Add, "nowy@feer.org.pl")));
        Assert.Empty(f.Calls);
    }

    [Fact]
    public async Task Usuniecie_aliasu_zapisuje_liste_bez_niego()
    {
        var f = new Fake((c, _) => Ok(c)); var q = new Queue();
        Assert.Equal(AliasOutcome.Applied, await Exec(f, q).ExecuteAsync(new AliasChange("jan@feer.org.pl", AliasChangeKind.Remove, "jan.k@feer.org.pl")));
        using var d = JsonDocument.Parse(f.Calls.Single(c => c.Cmdlet == "Set-Mailbox").Body);
        Assert.Equal(["SMTP:jan@feer.org.pl"], d.RootElement.GetProperty("CmdletInput").GetProperty("Parameters").GetProperty("EmailAddresses").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Theory]
    [InlineData("zly' -or 1 -eq 1 @x.pl")]
    [InlineData("a b@feer.org.pl")]
    public async Task Niebezpieczny_adres_nie_trafia_do_filtra_exchange(string addr)
    {
        var f = new Fake((c, _) => Ok(c)); var q = new Queue();
        await Assert.ThrowsAsync<UserFacingException>(() => Exec(f, q).ExecuteAsync(new AliasChange("jan@feer.org.pl", AliasChangeKind.Add, addr)));
        Assert.Empty(f.Calls);
    }
}
