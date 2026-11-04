using System.Text.RegularExpressions;

namespace M365Samoobsluga.Core.Services;

/// <summary>Zamienia błąd powrotu z logowania Microsoft (OpenID Connect) na czytelny komunikat po polsku. Nie zwraca tokenów ani danych osobowych.</summary>
public static partial class LoginErrors
{
    [GeneratedRegex(@"AADSTS(\d{4,7})")] private static partial Regex AadRx();

    public sealed record Described(string Code, string Title, string Hint);

    public static Described Describe(string? failureMessage)
    {
        var m = failureMessage ?? "";
        var aad = AadRx().Match(m);
        if (aad.Success)
        {
            var code = "AADSTS" + aad.Groups[1].Value;
            return aad.Groups[1].Value switch
            {
                "7000215" or "7000222" => new(code, "Sekret klienta aplikacji jest nieprawidłowy lub wygasł.", "Administrator ustawia nowy sekret w pliku /etc/m365-samoobsluga.env (wartość z kolumny Value w Entra, nie Secret ID) i restartuje usługę."),
                "7000218" => new(code, "Aplikacja nie ma sekretu klienta ani certyfikatu.", "Administrator uzupełnia poświadczenia aplikacji w pliku /etc/m365-samoobsluga.env i restartuje usługę."),
                "65001" or "65004" => new(code, "Brakuje zgody administratora na uprawnienia aplikacji.", "Administrator klika Udziel zgody administratora w Entra: Uprawnienia interfejsu API."),
                "50011" => new(code, "Adres przekierowania nie zgadza się z rejestracją aplikacji w Entra.", "Administrator dodaje adresy ze strony Administrator: Azure, dokładnie tak, jak w pasku przeglądarki (razem z https i portem)."),
                "700016" or "700025" => new(code, "Client ID aplikacji nie zgadza się z rejestracją w tej organizacji.", "Administrator sprawdza Client ID i Tenant ID na stronie Administrator: Azure."),
                "50105" or "50020" => new(code, "To konto nie ma dostępu do aplikacji.", "Poproś administratora o przypisanie Cię do aplikacji w Entra (Enterprise applications → Users and groups)."),
                _ => new(code, "Microsoft odrzucił logowanie.", "Pokaż kod błędu administratorowi."),
            };
        }
        if (m.Contains("Correlation failed", StringComparison.OrdinalIgnoreCase) || m.Contains("message.State", StringComparison.OrdinalIgnoreCase) || m.Contains("Unable to unprotect", StringComparison.OrdinalIgnoreCase))
            return new("SESJA", "Sesja logowania wygasła albo została użyta dwa razy.", "Zaloguj się jeszcze raz. Unikaj odświeżania strony i otwierania kilku kart w trakcie logowania.");
        if (m.Contains("Client_Credentials_Required", StringComparison.OrdinalIgnoreCase) || m.Contains("client credential type required", StringComparison.OrdinalIgnoreCase))
            return new("BRAK_SEKRETU", "Aplikacja nie ma sekretu klienta ani certyfikatu.", "Administrator uzupełnia poświadczenia aplikacji w pliku /etc/m365-samoobsluga.env i restartuje usługę.");
        return new("NIEZNANY", "Logowanie nie powiodło się.", "Spróbuj jeszcze raz. Jeśli błąd się powtarza, administrator znajdzie szczegóły w logu usługi.");
    }
}
