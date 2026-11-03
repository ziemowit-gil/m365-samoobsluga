namespace M365Samoobsluga.Core.Services;

/// <summary>Ocena sposobu, w jaki aplikacja loguje się do Entra ID (certyfikat zamiast sekretu).</summary>
public static class CredentialPolicy
{
    private static readonly string[] CertificateTypes = ["Certificate", "StoreWithThumbprint", "StoreWithDistinguishedName", "KeyVault", "Path", "Base64Encoded", "SignedAssertionFromManagedIdentity"];

    public static bool UsesCertificate(IEnumerable<string> sourceTypes) =>
        sourceTypes.Any(t => CertificateTypes.Contains(t, StringComparer.OrdinalIgnoreCase));

    /// <summary>Zwraca ostrzeżenia po polsku (puste = konfiguracja poprawna).</summary>
    public static IReadOnlyList<string> Evaluate(IReadOnlyCollection<string> sourceTypes, bool isProduction)
    {
        var w = new List<string>();
        if (sourceTypes.Count == 0) w.Add("Nie skonfigurowano poświadczeń aplikacji (AzureAd:ClientCredentials). Logowanie do Entra ID nie zadziała.");
        else if (sourceTypes.Any(t => t.Equals("ClientSecret", StringComparison.OrdinalIgnoreCase)) && !UsesCertificate(sourceTypes) && isProduction)
            w.Add("Aplikacja używa sekretu klienta. Na produkcji zalecany jest certyfikat (StoreWithThumbprint, KeyVault lub Path).");
        return w;
    }

    /// <summary>Ile dni zostało do wygaśnięcia certyfikatu; wartość ujemna oznacza, że już wygasł.</summary>
    public static int DaysToExpiry(DateTimeOffset notAfter, DateTimeOffset now) => (int)Math.Floor((notAfter - now).TotalDays);

    public static bool ShouldWarnAboutExpiry(DateTimeOffset notAfter, DateTimeOffset now, int thresholdDays = 30) => DaysToExpiry(notAfter, now) <= thresholdDays;
}
