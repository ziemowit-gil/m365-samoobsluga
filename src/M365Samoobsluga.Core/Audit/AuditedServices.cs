using M365Samoobsluga.Core.Models;
using M365Samoobsluga.Core.Services;

namespace M365Samoobsluga.Core.Audit;

/// <summary>Dekoratory dopisujące wpis audytu po każdej zmianie. Nie rejestrują haseł ani treści wiadomości.</summary>
public abstract class AuditBase(IAuditLog log, ICurrentUser user)
{
    protected async Task Run(string action, string? target, Func<Task> op)
    {
        try { await op(); log.Write(new AuditEntry(DateTimeOffset.UtcNow, user.Current.UserPrincipalName, action, target, true, null)); }
        catch (Exception ex)
        {
            log.Write(new AuditEntry(DateTimeOffset.UtcNow, user.Current.UserPrincipalName, action, target, false, ex is UserFacingException ? ex.Message : ex.GetType().Name));
            throw;
        }
    }
}

public sealed class AuditedAliasService(IAliasService inner, IAuditLog log, ICurrentUser user) : AuditBase(log, user), IAliasService
{
    public bool AppliesImmediately => inner.AppliesImmediately;
    public Task<MailboxAliases> GetAsync(CancellationToken ct = default) => inner.GetAsync(ct);
    public Task AddAsync(string a, CancellationToken ct = default) => Run(inner.AppliesImmediately ? "alias.dodaj" : "alias.dodaj.wniosek", a, () => inner.AddAsync(a, ct));
    public Task RemoveAsync(string a, CancellationToken ct = default) => Run(inner.AppliesImmediately ? "alias.usun" : "alias.usun.wniosek", a, () => inner.RemoveAsync(a, ct));
}

public sealed class AuditedAutoReplyService(IAutoReplyService inner, IAuditLog log, ICurrentUser user) : AuditBase(log, user), IAutoReplyService
{
    public Task<AutoReplySettings> GetAsync(CancellationToken ct = default) => inner.GetAsync(ct);
    public Task SaveAsync(AutoReplySettings s, CancellationToken ct = default) =>
        Run(s.Enabled ? "autoresponder.wlacz" : "autoresponder.wylacz", s.StartLocal is null ? null : $"{s.StartLocal:O}..{s.EndLocal:O}", () => inner.SaveAsync(s, ct));
}

public sealed class AuditedForwardingService(IForwardingService inner, IAuditLog log, ICurrentUser user) : AuditBase(log, user), IForwardingService
{
    public Task<ForwardingSettings> GetAsync(CancellationToken ct = default) => inner.GetAsync(ct);
    public Task SaveAsync(ForwardingSettings s, CancellationToken ct = default) =>
        Run(s.Enabled ? "przekierowanie.wlacz" : "przekierowanie.wylacz", s.Enabled ? s.Address : null, () => inner.SaveAsync(s, ct));
}

public sealed class AuditedPasswordService(IPasswordService inner, IAuditLog log, ICurrentUser user) : AuditBase(log, user), IPasswordService
{
    public Task ChangeAsync(ChangePasswordRequest r, CancellationToken ct = default) => Run("haslo.zmiana", null, () => inner.ChangeAsync(r, ct));
}

public sealed class AuditedSignatureService(ISignatureService inner, IAuditLog log, ICurrentUser user) : AuditBase(log, user), ISignatureService
{
    public Task<SignaturePerson> GetAsync(CancellationToken ct = default) => inner.GetAsync(ct);
    public Task SaveAsync(string title, string phone, CancellationToken ct = default) => Run("stopka.zapis", null, () => inner.SaveAsync(title, phone, ct));
    public async Task<SignaturePerson> SyncFromDirectoryAsync(CancellationToken ct = default)
    { SignaturePerson? r = null; await Run("stopka.synchronizacja", null, async () => r = await inner.SyncFromDirectoryAsync(ct)); return r!; }
}

public sealed class AuditedTwoFactorService(ITwoFactorService inner, IAuditLog log, ICurrentUser user) : AuditBase(log, user), ITwoFactorService
{
    public Task<IReadOnlyList<AuthMethod>> ListAsync(CancellationToken ct = default) => inner.ListAsync(ct);
    public Task RemoveAuthenticatorAsync(string id, CancellationToken ct = default) => Run("2fa.usun.authenticator", id, () => inner.RemoveAuthenticatorAsync(id, ct));
}
