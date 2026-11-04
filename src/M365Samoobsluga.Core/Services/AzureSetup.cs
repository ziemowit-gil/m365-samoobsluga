using System.Text.RegularExpressions;

namespace M365Samoobsluga.Core.Services;

/// <summary>Uprawnienie delegowane Microsoft Graph, które trzeba dodać w rejestracji aplikacji, i po co.</summary>
public sealed record AzurePermission(string Name, string Purpose);

/// <summary>Adres, który trzeba dodać w rejestracji aplikacji w Entra ID (Azure).</summary>
public sealed record AzureUri(string Label, string Value, string Where);

/// <summary>Walidacja identyfikatorów aplikacji i wyliczanie adresów do rejestracji w Azure (czysta logika).</summary>
public static partial class AzureSetup
{
    [GeneratedRegex(@"^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$")] private static partial Regex GuidRx();
    [GeneratedRegex(@"^(?=.{4,253}$)([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,63}$")] private static partial Regex DomainRx();

    public static string? ValidateClientId(string? v) =>
        GuidRx().IsMatch((v ?? "").Trim()) ? null : "Client ID (Application ID) to identyfikator w formacie 00000000-0000-0000-0000-000000000000.";

    /// <summary>Tenant ID: GUID katalogu albo jego domena (np. firma.onmicrosoft.com). Wartości wielodzierżawne (common, organizations) są odrzucane, bo aplikacja jest jednodzierżawna.</summary>
    public static string? ValidateTenantId(string? v)
    {
        var t = (v ?? "").Trim();
        return GuidRx().IsMatch(t) || DomainRx().IsMatch(t) ? null : "Tenant ID to identyfikator katalogu (GUID) albo jego domena, na przykład firma.onmicrosoft.com.";
    }

    public static bool IsConfigured(string? tenantId, string? clientId) => ValidateTenantId(tenantId) is null && ValidateClientId(clientId) is null;

    /// <summary>Sprowadza adres do postaci schemat://host[:port]/. Http jest dozwolony tylko dla localhost.</summary>
    public static (string? Url, string? Error) NormalizeBaseUrl(string? input)
    {
        if (!Uri.TryCreate((input ?? "").Trim(), UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp))
            return (null, "Wpisz pełny adres aplikacji, na przykład https://poczta.example.org.");
        if (u.Scheme == Uri.UriSchemeHttp && !u.IsLoopback)
            return (null, "Poza localhostem Entra ID wymaga adresu https.");
        return (u.GetLeftPart(UriPartial.Authority) + "/", null);
    }

    /// <summary>Adres powrotu po zalogowaniu: tylko lokalna ścieżka (zaczyna się od pojedynczego "/"), w przeciwnym razie strona startowa. Chroni przed przekierowaniem na obcy adres.</summary>
    public static string SafeReturnUrl(string? returnUrl)
    {
        var u = (returnUrl ?? "").Trim();
        if (u.Length == 0 || u.Length > 500 || u[0] != '/' || u.StartsWith("//") || u.StartsWith("/\\") || u.Any(char.IsControl) || u.Contains("://")) return "/";
        return u;
    }

    /// <summary>Uprawnienia opcjonalne: aplikacja prosi o nie dopiero przy użyciu funkcji, więc zgodę wyraża tylko osoba, która z niej korzysta.</summary>
    public static readonly IReadOnlyList<AzurePermission> OptionalPermissions =
    [
        new("User.ReadBasic.All", "Wczytanie adresów osób z domeny jako odbiorców powiadomienia o stopce (strona Administrator: powiadomienie). Tylko odczyt podstawowych danych katalogu."),
        new("Mail.Send", "Powiadomienia o stopce (strona Administrator: powiadomienie) i potwierdzenie e-mailem po zapisaniu własnej stopki. Wiadomość wychodzi ze skrzynki zalogowanej osoby."),
    ];

    /// <summary>Uprawnienia delegowane Graph używane przez aplikację (zgodne z DownstreamApis:MicrosoftGraph:Scopes w appsettings.json).</summary>
    public static readonly IReadOnlyList<AzurePermission> RequiredPermissions =
    [
        new("User.Read", "Odczyt profilu zalogowanej osoby, jej aliasów i zweryfikowanych domen organizacji."),
        new("MailboxSettings.ReadWrite", "Autoresponder i przekierowanie poczty we własnej skrzynce."),
        new("User-PasswordProfile.ReadWrite.All", "Zmiana własnego hasła (wymaga zgody administratora; działa tylko na koncie zalogowanej osoby)."),
        new("UserAuthenticationMethod.ReadWrite", "Lista i usuwanie własnych metod weryfikacji dwuetapowej."),
    ];

    /// <summary>
    /// Adres aplikacji: najpierw ustawiony jawnie (Samoobsluga:PublicUrl), a gdy go brak lub jest niepoprawny, adres z bieżącego żądania
    /// (za reverse proxy poprawny dzięki nagłówkom X-Forwarded-*). Zwraca postać schemat://host[:port]/.
    /// </summary>
    public static string ResolveAppUrl(string? configured, string requestBaseUri)
    {
        var (url, _) = NormalizeBaseUrl(configured);
        if (url is not null) return url;
        return NormalizeBaseUrl(requestBaseUri).Url ?? requestBaseUri;
    }

    /// <summary>Adresy do wpisania w Azure dla danego adresu aplikacji (zob. README: rejestracja aplikacji).</summary>
    public static IReadOnlyList<AzureUri> RedirectUris(string baseUrl)
    {
        var b = baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
        return
        [
            new("Redirect URI (typ Web)", b + "signin-oidc", "Authentication → Add a platform → Web → Redirect URIs"),
            new("Redirect URI po wylogowaniu (typ Web)", b + "signout-callback-oidc", "Authentication → Web → Redirect URIs (kolejny wpis)"),
            new("Front-channel logout URL", b + "signout-oidc", "Authentication → Web → Front-channel logout URL"),
        ];
    }
}
