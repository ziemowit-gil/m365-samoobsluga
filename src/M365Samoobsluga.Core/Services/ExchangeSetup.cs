using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace M365Samoobsluga.Core.Services;

/// <summary>Wygenerowany certyfikat aplikacji do Exchange. Prywatny klucz zostaje w pliku .pfx na serwerze, do Entra wgrywa się tylko .cer.</summary>
public sealed record GeneratedCertificate(byte[] Pfx, byte[] Cer, string Password, string Thumbprint, DateTimeOffset NotAfter);

public static class ExchangeSetup
{
    public const string RegistrationName = "Samoobsługa poczty: Exchange";
    public const string ServicePrincipalName = "Samoobsluga stopka";
    public const string RoleGroupName = "Samoobsluga stopka";

    /// <summary>Samopodpisany certyfikat RSA 3072 (SHA-256) ważny przez podaną liczbę lat, z losowym hasłem do pliku .pfx. Samopodpisany certyfikat jest dozwolony dla uwierzytelniania aplikacji w Exchange.</summary>
    public static GeneratedCertificate GenerateCertificate(string commonName, int years = 2)
    {
        if (years is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(years));
        using var rsa = RSA.Create(3072);
        var req = new CertificateRequest($"CN={commonName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter = notBefore.AddYears(years);
        using var cert = req.CreateSelfSigned(notBefore, notAfter);
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', 'A').Replace('/', 'B').TrimEnd('=');
        var pfx = cert.Export(X509ContentType.Pfx, password);
        return new GeneratedCertificate(pfx, cert.Export(X509ContentType.Cert), password, cert.Thumbprint, cert.NotAfter);
    }

    /// <summary>Polecenia, które administrator uruchamia raz po zalogowaniu do Graph i Exchange: główna usługa i wąska grupa ról (bez Exchange Administrator).</summary>
    public static string RoleScript(string appId)
    {
        if (ExchangeCommandBuilder.Validate(new ExchangeSettings { Enabled = true, AppId = appId, Organization = "x.pl", CertificatePath = "/x" }) is { } e && e.Contains("AppId"))
            throw new ArgumentException("Nieprawidłowy Client ID.", nameof(appId));
        return $$"""
        # Skrypt wygenerowany przez Samoobsługę poczty Microsoft 365. Uruchom jednorazowo jako administrator Exchange i Entra.
        # Nadaje aplikacji wąski dostęp do Exchange: tylko reguły transportowe i opcje użytkownika (zob. dokumentację
        # "App-only authentication in Exchange Online PowerShell", opcja: custom role groups).
        # Wymaga modułów ExchangeOnlineManagement i Microsoft.Graph.Applications.
        $ErrorActionPreference = 'Stop'
        $appId = '{{appId}}'

        Connect-MgGraph -Scopes 'Application.Read.All'
        $app = Get-MgServicePrincipal -Filter "AppId eq '$appId'"
        if (-not $app) { throw "Nie znaleziono głównej usługi dla $appId. Czy dodano uprawnienie i udzielono zgody w Entra?" }

        Connect-ExchangeOnline
        New-ServicePrincipal -AppId $app.AppId -ObjectId $app.Id -DisplayName '{{ServicePrincipalName}}'
        $sp = Get-ServicePrincipal -Identity '{{ServicePrincipalName}}'

        # "Transport Rules": reguły stopki. "User Options": ustawienia podpisów skrzynek (czyszczenie podpisów).
        # Jeśli czyszczenie podpisów zgłosi brak uprawnień, dodaj też rolę "Mail Recipients".
        New-RoleGroup -Name '{{RoleGroupName}}' -Roles 'Transport Rules','User Options' -Description 'Samoobsługa poczty: stopki'
        Add-RoleGroupMember -Identity '{{RoleGroupName}}' -Member $sp.Identity

        Write-Host 'Gotowe. Uprawnienia w Exchange mogą się replikować 15-30 minut. Potem użyj przycisku "Sprawdź połączenie" w aplikacji.'
        Disconnect-ExchangeOnline -Confirm:$false
        """.Replace("\r\n", "\n");
    }
}
