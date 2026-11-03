using M365Samoobsluga.Core.Models;

namespace M365Samoobsluga.Core.Services;

/// <summary>Wyjątek z komunikatem po polsku, bezpiecznym do pokazania użytkownikowi.</summary>
public sealed class UserFacingException(string message, Exception? inner = null) : Exception(message, inner);

public interface IAliasService
{
    Task<MailboxAliases> GetAsync(CancellationToken ct = default);
    Task AddAsync(string address, CancellationToken ct = default);
    Task RemoveAsync(string address, CancellationToken ct = default);
    /// <summary>Czy zmiany są wykonywane od razu, czy trafiają jako wniosek do administratora.</summary>
    bool AppliesImmediately { get; }
}
public interface IAutoReplyService
{
    Task<AutoReplySettings> GetAsync(CancellationToken ct = default);
    Task SaveAsync(AutoReplySettings settings, CancellationToken ct = default);
}
public interface IForwardingService
{
    Task<ForwardingSettings> GetAsync(CancellationToken ct = default);
    Task SaveAsync(ForwardingSettings settings, CancellationToken ct = default);
}
public interface IPasswordService
{
    Task ChangeAsync(ChangePasswordRequest request, CancellationToken ct = default);
}
public interface ISignatureService
{
    /// <summary>Dane osoby do stopki: nadpisane przez użytkownika (stanowisko, telefon) albo z profilu Microsoft 365.</summary>
    Task<SignaturePerson> GetAsync(CancellationToken ct = default);
    /// <summary>Zapisuje stanowisko i telefon użytkownika (imię, nazwisko i e-mail zawsze z konta).</summary>
    Task SaveAsync(string title, string phone, CancellationToken ct = default);
    /// <summary>Pobiera stanowisko i telefon z profilu Microsoft 365 i zapisuje je jako własne ustawienia.</summary>
    Task<SignaturePerson> SyncFromDirectoryAsync(CancellationToken ct = default);
}

/// <summary>Szablon stopki organizacji i wygląd, ustawiane przez administratora.</summary>
public sealed record SignatureTemplate(SignatureOrg Org, SignatureDesign Design);

public interface ITwoFactorService
{
    Task<IReadOnlyList<AuthMethod>> ListAsync(CancellationToken ct = default);
    /// <summary>Usuwa urządzenie Microsoft Authenticator z własnego konta. Sprawdza <see cref="TwoFactorPolicy"/>.</summary>
    Task RemoveAuthenticatorAsync(string methodId, CancellationToken ct = default);
}
