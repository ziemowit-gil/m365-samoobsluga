using M365Samoobsluga.Core.Models;

namespace M365Samoobsluga.Core.Services;

/// <summary>Reguły zarządzania metodami 2FA (czysta logika).</summary>
public static class TwoFactorPolicy
{
    public static string KindName(AuthMethodKind k) => k switch
    {
        AuthMethodKind.Password => "Hasło",
        AuthMethodKind.MicrosoftAuthenticator => "Aplikacja Microsoft Authenticator",
        AuthMethodKind.Phone => "Telefon (SMS lub połączenie)",
        AuthMethodKind.Fido2 => "Klucz bezpieczeństwa FIDO2",
        AuthMethodKind.WindowsHello => "Windows Hello dla firm",
        AuthMethodKind.SoftwareOath => "Aplikacja z kodami (OATH)",
        AuthMethodKind.Email => "Adres e-mail",
        AuthMethodKind.TemporaryAccessPass => "Tymczasowy kod dostępu",
        _ => "Inna metoda",
    };

    /// <summary>Hasło nie jest drugim składnikiem, więc nie liczy się do zabezpieczenia konta.</summary>
    public static int SecondFactorCount(IEnumerable<AuthMethod> methods) => methods.Count(m => m.Kind != AuthMethodKind.Password);

    /// <summary>Aplikacja pozwala usuwać tylko urządzenia Microsoft Authenticator i nie pozwala usunąć ostatniego drugiego składnika.</summary>
    public static string? CannotRemoveReason(AuthMethod target, IReadOnlyCollection<AuthMethod> all)
    {
        if (target.Kind != AuthMethodKind.MicrosoftAuthenticator)
            return "Tę metodę usuniesz na stronie Moje metody logowania Microsoft.";
        if (SecondFactorCount(all) <= 1)
            return "To Twoja jedyna metoda weryfikacji. Najpierw dodaj drugą na stronie Moje metody logowania Microsoft, inaczej możesz stracić dostęp do konta.";
        return null;
    }
}
