using System.Net;
using System.Text;
using System.Text.Json;
using M365Samoobsluga.Graph.Services;

namespace M365Samoobsluga.Tests;

public class ExchangeRestTests
{
    private static string Jwt(string tid) =>
        "e30." + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{{\"tid\":\"{tid}\",\"roles\":[\"Exchange.ManageAsApp\"]}}")).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".sig";

    sealed class FakeHandler(Func<HttpRequestMessage, string, (HttpStatusCode, string)> reply) : HttpMessageHandler
    {
        public List<(string Anchor, string Cmdlet, string Body, string Url, string? Auth)> Calls = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var body = await r.Content!.ReadAsStringAsync(ct);
            var anchor = r.Headers.GetValues("X-AnchorMailbox").Single();
            Calls.Add((anchor, r.Headers.GetValues("X-CmdletName").Single(), body, r.RequestUri!.ToString(), r.Headers.Authorization?.ToString()));
            var (code, text) = reply(r, anchor);
            return new HttpResponseMessage(code) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }
    }

    private static ExchangeRestClient Client(FakeHandler h, string org = "firma.onmicrosoft.com") =>
        new(_ => Task.FromResult(Jwt("11111111-2222-3333-4444-555555555555")), org, new HttpClient(h));

    [Fact]
    public void Tid_jest_czytany_z_tokenu() =>
        Assert.Equal("abc-123", ExchangeRestClient.TenantIdFromJwt(Jwt("abc-123")));

    [Fact]
    public void Zly_token_jest_odrzucany() => Assert.Throws<ArgumentException>(() => ExchangeRestClient.TenantIdFromJwt("nie-jwt"));

    [Fact]
    public void Cialo_zadania_ma_nazwe_polecenia_i_parametry()
    {
        var b = ExchangeRestClient.BuildBody("Get-TransportRule", new Dictionary<string, object?> { ["Identity"] = "R", ["Flag"] = true });
        using var d = JsonDocument.Parse(b);
        var ci = d.RootElement.GetProperty("CmdletInput");
        Assert.Equal("Get-TransportRule", ci.GetProperty("CmdletName").GetString());
        Assert.Equal("R", ci.GetProperty("Parameters").GetProperty("Identity").GetString());
        Assert.True(ci.GetProperty("Parameters").GetProperty("Flag").GetBoolean());
    }

    [Fact]
    public void Kotwica_ma_format_systemowej_skrzynki() =>
        Assert.Equal("APP:SystemMailbox{bb558c35-97f1-4cb9-8ff7-d53741dc928c}@firma.onmicrosoft.com", ExchangeRestClient.Anchor("APP", "firma.onmicrosoft.com"));

    [Fact]
    public async Task Wywolanie_ma_naglowki_adres_z_tid_i_zwraca_wartosci()
    {
        var h = new FakeHandler((_, _) => (HttpStatusCode.OK, "{\"value\":[{\"Name\":\"A\"},{\"Name\":\"B\"}]}"));
        var r = await Client(h).InvokeAsync("Get-TransportRule");
        Assert.Equal(2, r.Count);
        var c = h.Calls.Single();
        Assert.Equal("https://outlook.office365.com/adminapi/beta/11111111-2222-3333-4444-555555555555/InvokeCommand", c.Url);
        Assert.StartsWith("Bearer ", c.Auth);
        Assert.Equal("Get-TransportRule", c.Cmdlet);
    }

    [Fact]
    public async Task Pusty_403_przechodzi_do_nastepnej_kotwicy()
    {
        var h = new FakeHandler((_, anchor) => anchor.StartsWith("APP:") ? (HttpStatusCode.Forbidden, "") : (HttpStatusCode.OK, "{\"value\":[]}"));
        var c = Client(h);
        var r = await c.InvokeAsync("Get-TransportRule");
        Assert.Empty(r);
        Assert.True(h.Calls.Count >= 2);
        Assert.StartsWith("UPN:", h.Calls[^1].Anchor);
    }

    [Fact]
    public async Task Blad_z_trescia_jest_zglaszany_z_komunikatem()
    {
        var h = new FakeHandler((_, _) => (HttpStatusCode.BadRequest, "{\"error\":{\"code\":\"X\",\"message\":\"Zły parametr Identity\"}}"));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Client(h).InvokeAsync("Set-TransportRule"));
        Assert.Contains("Zły parametr Identity", ex.Message);
        Assert.Contains("Set-TransportRule", ex.Message);
    }

    [Fact]
    public async Task Wszystkie_kotwice_403_daja_czytelna_podpowiedz()
    {
        var h = new FakeHandler((_, _) => (HttpStatusCode.Forbidden, ""));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Client(h, "").InvokeAsync("Get-TransportRule"));
        Assert.Contains("403", ex.Message);
        Assert.Contains("Diagnostyka tokenu", ex.Message);
    }

    [Fact]
    public void Diagnostyka_wykrywa_brak_roli_exchange_administrator()
    {
        var d = ExchangeRestClient.TokenDiagnostics(Jwt("t"));
        Assert.Contains("Exchange.ManageAsApp", d);
        Assert.Contains("NIE MA aktywnej roli", d);
    }

    [Fact]
    public void Diagnostyka_widzi_role_w_wids()
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"tid\":\"t\",\"appidacr\":\"2\",\"wids\":[\"" + ExchangeRestClient.ExchangeAdminTemplateId + "\"]}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var d = ExchangeRestClient.TokenDiagnostics("e30." + payload + ".sig");
        Assert.Contains("appidacr) = 2", d);
        Assert.Contains("Rola Exchange Administrator jest w tokenie", d);
    }

    [Fact]
    public async Task Odpowiedz_bez_tablicy_value_jest_zwracana_jako_jeden_obiekt()
    {
        var h = new FakeHandler((_, _) => (HttpStatusCode.OK, "{\"Name\":\"Pojedynczy\"}"));
        var r = await Client(h).InvokeAsync("Get-Mailbox");
        Assert.Equal("Pojedynczy", r.Single().GetProperty("Name").GetString());
    }
}
