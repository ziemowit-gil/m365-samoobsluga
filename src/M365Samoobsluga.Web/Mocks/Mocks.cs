using System.Security.Claims;
using System.Text.Encodings.Web;
using M365Samoobsluga.Core.Models;
using M365Samoobsluga.Core.Services;
using M365Samoobsluga.Core.Validation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace M365Samoobsluga.Web.Mocks;

/// <summary>Uwierzytelnianie demonstracyjne (tylko UseMockServices=true).</summary>
public sealed class MockAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e) : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var id = new ClaimsIdentity([new Claim("oid", "00000000-0000-0000-0000-000000000001"), new Claim("preferred_username", "demo@feer.org.pl"), new Claim("name", "Użytkownik Demo")], "Mock");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(id), "Mock")));
    }
}

public sealed class MockMailbox
{
    public string Primary = "demo@feer.org.pl";
    public List<string> Aliases = ["demo.kontakt@feer.org.pl"];
    public string[] Domains = ["feer.org.pl", "feer.pl", "feerns.onmicrosoft.com"];
    public AutoReplySettings AutoReply = new(false, null, null, "Europe/Warsaw", "", "", AutoReplyAudience.All);
    public ForwardingSettings Forwarding = new(false, null, true, null);
    public SignaturePerson Person = new("Użytkownik Demo", "", "", "demo@feer.org.pl");
}

public sealed class MockAliasService(MockMailbox m, IReadOnlyCollection<string>? allowed = null) : IAliasService
{
    public bool AppliesImmediately => true;
    public Task<MailboxAliases> GetAsync(CancellationToken ct = default) => Task.FromResult(new MailboxAliases(m.Primary, m.Aliases.ToList(), AddressValidator.FilterAllowedDomains(m.Domains, allowed)));
    public Task<AliasOutcome> AddAsync(string a, CancellationToken ct = default)
    {
        var at = a.LastIndexOf('@');
        var v = AddressValidator.ValidateAlias(at > 0 ? a[..at] : a, at > 0 ? a[(at + 1)..] : "", AddressValidator.FilterAllowedDomains(m.Domains, allowed), m.Aliases.Append(m.Primary).ToList());
        if (!v.IsValid) throw new UserFacingException(v.Error!);
        m.Aliases.Add(v.Normalized!); return Task.FromResult(AliasOutcome.Applied);
    }
    public Task<AliasOutcome> RemoveAsync(string a, CancellationToken ct = default) { m.Aliases.Remove(a); return Task.FromResult(AliasOutcome.Applied); }
}
public sealed class MockAutoReplyService(MockMailbox m) : IAutoReplyService
{
    public Task<AutoReplySettings> GetAsync(CancellationToken ct = default) => Task.FromResult(m.AutoReply);
    public Task SaveAsync(AutoReplySettings s, CancellationToken ct = default)
    {
        var errs = AutoReplyComposer.Validate(s, DateTime.UtcNow);
        if (errs.Count > 0) throw new UserFacingException(string.Join(" ", errs));
        m.AutoReply = s; return Task.CompletedTask;
    }
}
public sealed class MockForwardingService(MockMailbox m, IAliasService aliases) : IForwardingService
{
    public Task<ForwardingSettings> GetAsync(CancellationToken ct = default) => Task.FromResult(m.Forwarding);
    public async Task SaveAsync(ForwardingSettings s, CancellationToken ct = default)
    {
        if (s.Enabled)
        {
            var a = await aliases.GetAsync(ct);
            var v = AddressValidator.ValidateForwardingAddress(s.Address, a.SecondaryAddresses.Append(a.PrimaryAddress).ToList());
            if (!v.IsValid) throw new UserFacingException(v.Error!);
            s = s with { Address = v.Normalized };
        }
        m.Forwarding = s;
    }
}
public sealed class MockPasswordService : IPasswordService
{
    public Task ChangeAsync(ChangePasswordRequest r, CancellationToken ct = default)
    {
        if (r.NewPassword.Length < 8) throw new UserFacingException("Nowe hasło nie spełnia zasad organizacji (min. 8 znaków w trybie demo).");
        return Task.CompletedTask;
    }
}
public sealed class MockSignatureService(MockMailbox m) : ISignatureService
{
    public Task<SignaturePerson> GetAsync(CancellationToken ct = default) => Task.FromResult(m.Person);
    public Task SaveAsync(string title, string phone, CancellationToken ct = default)
    {
        var err = SignatureRenderer.ValidateTitle(title) ?? SignatureRenderer.ValidatePhone(phone);
        if (err is not null) throw new UserFacingException(err);
        m.Person = m.Person with { Title = title.Trim(), Phone = phone.Trim() }; return Task.CompletedTask;
    }
    public Task<SignaturePerson> SyncFromDirectoryAsync(CancellationToken ct = default)
    { m.Person = m.Person with { Title = "Koordynatorka wolontariatu", Phone = "+48 600 100 200" }; return Task.FromResult(m.Person); }
}

public sealed class MockTwoFactorService : ITwoFactorService
{
    private readonly List<AuthMethod> _m = [
        new("pw", AuthMethodKind.Password, "Hasło", null),
        new("a1", AuthMethodKind.MicrosoftAuthenticator, "Telefon służbowy", new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.Zero)),
        new("a2", AuthMethodKind.MicrosoftAuthenticator, "Stary telefon", new DateTimeOffset(2024, 9, 12, 10, 0, 0, TimeSpan.Zero)),
        new("ph", AuthMethodKind.Phone, "Numer kończący się na 21", null)];
    public Task<IReadOnlyList<AuthMethod>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AuthMethod>>(_m.ToList());
    public Task RemoveAuthenticatorAsync(string id, CancellationToken ct = default)
    {
        var t = _m.FirstOrDefault(x => x.Id == id) ?? throw new UserFacingException("Nie znaleziono tej metody logowania.");
        if (TwoFactorPolicy.CannotRemoveReason(t, _m) is { } r) throw new UserFacingException(r);
        _m.Remove(t); return Task.CompletedTask;
    }
}

public sealed class MockTemplateStore(SignatureTemplate initial) : ISignatureTemplateStore
{
    private SignatureTemplate _t = initial;
    public Task<SignatureTemplate> GetAsync(CancellationToken ct = default) => Task.FromResult(_t);
    public Task SaveAsync(SignatureTemplate t, CancellationToken ct = default)
    {
        var errs = SignatureRenderer.ValidateOrg(t.Org);
        if (errs.Count > 0) throw new UserFacingException(string.Join(" ", errs));
        _t = new SignatureTemplate(t.Org, SignatureRenderer.Sanitize(t.Design)); return Task.CompletedTask;
    }
}

public sealed class MockExchangeDeployer : IExchangeRuleDeployer
{
    public bool IsConfigured => true;
    public Task<ExchangeOpResult> TestAsync(CancellationToken ct = default) => Task.FromResult(new ExchangeOpResult(true, "(demo) Połączenie działa. Reguł transportowych: 3."));
    public Task<ExchangeOpResult> DeployAsync(string html, ExchangeRuleMode mode, CancellationToken ct = default) =>
        Task.FromResult(ExchangeRuleBuilder.Validate(html) is { } e ? new ExchangeOpResult(false, e) : new ExchangeOpResult(true, $"(demo) Reguła utworzona lub zaktualizowana, tryb {mode}, {html.Length} znaków."));
    public Task<ExchangeOpResult> RemoveAsync(CancellationToken ct = default) => Task.FromResult(new ExchangeOpResult(true, "(demo) Reguła usunięta."));
    public Task<ExchangeOpResult> ClearSignaturesAsync(string? mailbox, bool all, CancellationToken ct = default) =>
        Task.FromResult(new ExchangeOpResult(true, $"(demo) Wyczyszczono podpisy: {(all ? "wszystkie skrzynki" : mailbox)}. Kopia zapasowa zapisana."));
}

/// <summary>Tryb demo: niczego nie wysyła, tylko udaje sukces.</summary>
public sealed class MockNoticeMailer : INoticeMailer
{
    public Task SendAsync(string to, string subject, string html, CancellationToken ct = default) =>
        StopkaNotice.IsValidEmail(to) ? Task.CompletedTask : throw new UserFacingException("Niepoprawny adres odbiorcy.");
}

public sealed class MockDirectoryUsers : IDirectoryUsers
{
    public Task<IReadOnlyList<string>> ListAddressesAsync(string domain, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(StopkaNotice.SelectDomainAddresses(
        [
            new("anna.nowak@feer.org.pl", null, "Member"), new("jan.kowalski@feer.org.pl", null, "Member"),
            new(null, "ola.wolontariuszka@feer.org.pl", "Member"), new("gosc@example.org", "gosc_example.org#EXT#@feer.org.pl", "Guest"),
        ], domain));
}
