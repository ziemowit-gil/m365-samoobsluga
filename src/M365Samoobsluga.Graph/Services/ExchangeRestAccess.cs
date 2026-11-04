using System.Security.Cryptography.X509Certificates;
using M365Samoobsluga.Core.Services;
using Microsoft.Identity.Client;

namespace M365Samoobsluga.Graph.Services;

/// <summary>Wspólny dostęp do Exchange przez REST: ustawienia, certyfikat aplikacji i klient (używają go wdrażanie reguły i aliasy).</summary>
public sealed class ExchangeRestAccess(ExchangeSettingsProvider provider, IHttpClientFactory? factory = null)
{
    public bool IsConfigured => ExchangeCommandBuilder.Validate(provider.Get()) is null;

    public ExchangeRestClient Client()
    {
        var s = provider.Get();
        var pwd = provider.ReadPassword(s);
        return new ExchangeRestClient(async ct =>
        {
            using var cert = X509CertificateLoader.LoadPkcs12FromFile(s.CertificatePath, pwd, X509KeyStorageFlags.EphemeralKeySet);
            var app = ConfidentialClientApplicationBuilder.Create(s.AppId).WithCertificate(cert)
                .WithAuthority($"https://login.microsoftonline.com/{s.Organization}").Build();
            var r = await app.AcquireTokenForClient(["https://outlook.office365.com/.default"]).ExecuteAsync(ct);
            return r.AccessToken;
        }, s.Organization, factory?.CreateClient() ?? new HttpClient { Timeout = TimeSpan.FromMinutes(2) });
    }
}
