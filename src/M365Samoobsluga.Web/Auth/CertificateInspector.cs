using System.Security.Cryptography.X509Certificates;
using M365Samoobsluga.Core.Services;

namespace M365Samoobsluga.Web.Auth;

/// <summary>Sprawdza przy starcie certyfikat aplikacji (magazyn albo plik) i ostrzega, gdy wygasa. Nic nie zmienia i nie loguje kluczy.</summary>
public static class CertificateInspector
{
    public static void LogExpiry(IConfiguration config, ILogger log, DateTimeOffset now)
    {
        foreach (var c in config.GetSection("AzureAd:ClientCredentials").GetChildren())
        {
            var type = c["SourceType"] ?? "";
            try
            {
                X509Certificate2? cert = null;
                if (type.Equals("StoreWithThumbprint", StringComparison.OrdinalIgnoreCase))
                {
                    var path = (c["CertificateStorePath"] ?? "CurrentUser/My").Split('/');
                    using var store = new X509Store(Enum.Parse<StoreName>(path[^1]), Enum.Parse<StoreLocation>(path[0]));
                    store.Open(OpenFlags.ReadOnly);
                    cert = store.Certificates.Find(X509FindType.FindByThumbprint, c["CertificateThumbprint"] ?? "", false).OfType<X509Certificate2>().FirstOrDefault();
                }
                else if (type.Equals("Path", StringComparison.OrdinalIgnoreCase) && File.Exists(c["CertificateDiskPath"]))
                    cert = X509CertificateLoader.LoadPkcs12FromFile(c["CertificateDiskPath"]!, c["CertificatePassword"]);
                else continue;

                if (cert is null) { log.LogWarning("Nie znaleziono certyfikatu aplikacji ({Type}). Sprawdź konfigurację.", type); continue; }
                var days = CredentialPolicy.DaysToExpiry(cert.NotAfter, now);
                if (CredentialPolicy.ShouldWarnAboutExpiry(cert.NotAfter, now))
                    log.LogWarning("Certyfikat aplikacji {Subject} wygasa za {Days} dni ({NotAfter:yyyy-MM-dd}). Wymień go i dodaj nowy w rejestracji aplikacji.", cert.Subject, days, cert.NotAfter);
                else log.LogInformation("Certyfikat aplikacji {Subject} jest ważny jeszcze {Days} dni.", cert.Subject, days);
            }
            catch (Exception ex) { log.LogWarning(ex, "Nie udało się sprawdzić certyfikatu aplikacji ({Type}).", type); }
        }
    }
}
