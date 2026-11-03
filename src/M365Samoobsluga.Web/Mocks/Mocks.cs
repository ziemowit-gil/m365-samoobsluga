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
    public string[] Domains = ["feer.org.pl", "feer.pl"];
    public AutoReplySettings AutoReply = new(false, null, null, "Europe/Warsaw", "", "", AutoReplyAudience.All);
    public ForwardingSettings Forwarding = new(false, null, true, null);
    public SignatureSet Signature = new("", "");
}

public sealed class MockAliasService(MockMailbox m) : IAliasService
{
    public bool AppliesImmediately => false;
    public Task<MailboxAliases> GetAsync(CancellationToken ct = default) => Task.FromResult(new MailboxAliases(m.Primary, m.Aliases.ToList(), m.Domains));
    public Task AddAsync(string a, CancellationToken ct = default)
    {
        var at = a.LastIndexOf('@');
        var v = AddressValidator.ValidateAlias(at > 0 ? a[..at] : a, at > 0 ? a[(at + 1)..] : "", m.Domains, m.Aliases.Append(m.Primary).ToList());
        if (!v.IsValid) throw new UserFacingException(v.Error!);
        m.Aliases.Add(v.Normalized!); return Task.CompletedTask;
    }
    public Task RemoveAsync(string a, CancellationToken ct = default) { m.Aliases.Remove(a); return Task.CompletedTask; }
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
    public Task<SignatureSet> GetAsync(CancellationToken ct = default) => Task.FromResult(m.Signature);
    public Task SaveAsync(SignatureSet s, CancellationToken ct = default) { m.Signature = s; return Task.CompletedTask; }
}
