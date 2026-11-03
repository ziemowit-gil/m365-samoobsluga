using M365Samoobsluga.Core.Models;
using M365Samoobsluga.Core.Services;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace M365Samoobsluga.Graph.Services;

/// <summary>
/// Metody uwierzytelniania własnego konta: GET /me/authentication/methods i DELETE /me/authentication/microsoftAuthenticatorMethods/{id}
/// (delegowane UserAuthenticationMethod.ReadWrite; po 10 minutach od logowania Microsoft może poprosić o ponowne MFA).
/// Dodawanie metod nie jest dostępne dla użytkownika w Graph (np. telefonu nie dodasz do własnego konta), więc użytkownik robi to na stronie Microsoftu.
/// </summary>
public sealed class GraphTwoFactorService(GraphServiceClient graph) : ITwoFactorService
{
    public async Task<IReadOnlyList<AuthMethod>> ListAsync(CancellationToken ct = default)
    {
        try
        {
            var r = await graph.Me.Authentication.Methods.GetAsync(cancellationToken: ct);
            return (r?.Value ?? []).Where(m => m.Id is not null).Select(Map).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw GraphErrorMapper.Map(ex, "metody logowania"); }
    }

    public async Task RemoveAuthenticatorAsync(string methodId, CancellationToken ct = default)
    {
        var all = await ListAsync(ct);
        var target = all.FirstOrDefault(m => m.Id == methodId) ?? throw new UserFacingException("Nie znaleziono tej metody logowania.");
        if (TwoFactorPolicy.CannotRemoveReason(target, all) is { } reason) throw new UserFacingException(reason);
        try { await graph.Me.Authentication.MicrosoftAuthenticatorMethods[methodId].DeleteAsync(cancellationToken: ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw GraphErrorMapper.Map(ex, "metody logowania"); }
    }

    internal static AuthMethod Map(AuthenticationMethod m) => m switch
    {
        MicrosoftAuthenticatorAuthenticationMethod a => new(a.Id!, AuthMethodKind.MicrosoftAuthenticator, a.DisplayName ?? "Urządzenie z Authenticatorem", a.CreatedDateTime),
        PhoneAuthenticationMethod p => new(p.Id!, AuthMethodKind.Phone, MaskPhone(p.PhoneNumber), null),
        Fido2AuthenticationMethod f => new(f.Id!, AuthMethodKind.Fido2, f.DisplayName ?? f.Model ?? "Klucz FIDO2", f.CreatedDateTime),
        WindowsHelloForBusinessAuthenticationMethod w => new(w.Id!, AuthMethodKind.WindowsHello, w.DisplayName ?? "Windows Hello", w.CreatedDateTime),
        SoftwareOathAuthenticationMethod => new(m.Id!, AuthMethodKind.SoftwareOath, "Aplikacja z kodami", null),
        EmailAuthenticationMethod => new(m.Id!, AuthMethodKind.Email, "Adres e-mail do odzyskiwania", null),
        PasswordAuthenticationMethod pw => new(m.Id!, AuthMethodKind.Password, "Hasło", pw.CreatedDateTime),
        TemporaryAccessPassAuthenticationMethod => new(m.Id!, AuthMethodKind.TemporaryAccessPass, "Tymczasowy kod dostępu", null),
        _ => new(m.Id!, AuthMethodKind.Other, "Inna metoda", null),
    };

    /// <summary>Pokazuje tylko ostatnie 2 cyfry numeru.</summary>
    public static string MaskPhone(string? number)
    {
        var digits = new string((number ?? "").Where(char.IsDigit).ToArray());
        return digits.Length < 2 ? "Numer telefonu" : $"Numer kończący się na {digits[^2..]}";
    }
}
