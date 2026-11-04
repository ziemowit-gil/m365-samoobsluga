using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace M365Samoobsluga.Core.Services;

/// <summary>Ustawienia łączenia z Exchange Online bez użytkownika (certyfikat aplikacji). Bez sekretów w repozytorium: hasło do pliku certyfikatu w zmiennej środowiskowej.</summary>
public sealed class ExchangeSettings
{
    public bool Enabled { get; set; }
    /// <summary>Client ID rejestracji z uprawnieniem Exchange.ManageAsApp (zalecana osobna rejestracja tylko do Exchange).</summary>
    public string AppId { get; set; } = "";
    /// <summary>Ścieżka do pliku .pfx na serwerze.</summary>
    public string CertificatePath { get; set; } = "";
    /// <summary>Domena organizacji, np. feer.org.pl (lub xxx.onmicrosoft.com).</summary>
    public string Organization { get; set; } = "";
    public string PowerShellPath { get; set; } = "pwsh";
    /// <summary>"Rest" (domyślnie, bez PowerShella: wywołania InvokeCommand z tokenem aplikacji) albo "PowerShell" (moduł ExchangeOnlineManagement).</summary>
    public string Mode { get; set; } = "Rest";
    /// <summary>Opcjonalnie: wersja modułu ExchangeOnlineManagement dla trybu PowerShell (np. 3.8.0), gdy najnowsza zgłasza błędy.</summary>
    public string ModuleVersion { get; set; } = "";
    /// <summary>Nazwa zmiennej środowiskowej z hasłem do pliku .pfx.</summary>
    public string CertificatePasswordEnv { get; set; } = "M365_EXO_CERT_PASSWORD";
}

/// <summary>Ustawienia automatyki: czy aplikacja sama zakłada i aktualizuje regułę po każdej zmianie szablonu. Plus ślad ostatniego wdrożenia.</summary>
public sealed record ExchangeAutomation(bool AutoDeploy, ExchangeRuleMode Mode, string? LastHash, DateTimeOffset? LastDeployedAt, string? LastResult)
{
    public static ExchangeAutomation Default => new(false, ExchangeRuleMode.Audit, null, null, null);
}

public sealed record ExchangeOpResult(bool Success, string Output);

public interface IExchangeAutomationStore
{
    Task<ExchangeAutomation> GetAsync(CancellationToken ct = default);
    Task SaveAsync(ExchangeAutomation value, CancellationToken ct = default);
}

public interface IExchangeRuleDeployer
{
    /// <summary>Czy Exchange jest skonfigurowany (certyfikat, AppId, domena) i włączony.</summary>
    bool IsConfigured { get; }
    /// <summary>Sprawdza połączenie z Exchange (tylko odczyt reguł transportowych).</summary>
    Task<ExchangeOpResult> TestAsync(CancellationToken ct = default);
    Task<ExchangeOpResult> DeployAsync(string html, ExchangeRuleMode mode, CancellationToken ct = default);
    Task<ExchangeOpResult> RemoveAsync(CancellationToken ct = default);
    /// <summary>Czyści zapisane podpisy w skrzynkach (jedna skrzynka albo wszystkie), po zapisaniu kopii zapasowej.</summary>
    Task<ExchangeOpResult> ClearSignaturesAsync(string? mailbox, bool all, CancellationToken ct = default);
}

/// <summary>Składa argumenty dla pwsh (lista, bez powłoki) i waliduje ustawienia. Czysta logika.</summary>
public static partial class ExchangeCommandBuilder
{
    [GeneratedRegex(@"^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$")] private static partial Regex GuidRx();
    [GeneratedRegex(@"^(?=.{4,253}$)([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,63}$")] private static partial Regex DomainRx();
    [GeneratedRegex(@"^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+$")] private static partial Regex UpnRx();

    public static string? Validate(ExchangeSettings s)
    {
        if (!s.Enabled) return "Wdrażanie w Exchange jest wyłączone w konfiguracji (Samoobsluga:Exchange:Enabled).";
        if (!GuidRx().IsMatch(s.AppId ?? "")) return "Samoobsluga:Exchange:AppId musi być identyfikatorem aplikacji (GUID).";
        if (!DomainRx().IsMatch(s.Organization ?? "")) return "Samoobsluga:Exchange:Organization musi być domeną organizacji, np. feer.org.pl.";
        if (string.IsNullOrWhiteSpace(s.CertificatePath) || s.CertificatePath.Any(char.IsControl) || !s.CertificatePath.StartsWith('/'))
            return "Samoobsluga:Exchange:CertificatePath musi być bezwzględną ścieżką do pliku .pfx.";
        if (string.IsNullOrWhiteSpace(s.PowerShellPath) || s.PowerShellPath.Any(char.IsControl)) return "Nieprawidłowa ścieżka do PowerShella.";
        return null;
    }

    public static bool IsValidMailbox(string? upn) => UpnRx().IsMatch(upn ?? "");

    private static List<string> Common(ExchangeSettings s, string script) =>
        ["-NoProfile", "-NonInteractive", "-File", script, "-AppId", s.AppId, "-CertificateFilePath", s.CertificatePath, "-Organization", s.Organization];

    public static List<string> DeployArgs(ExchangeSettings s, string script, string htmlFile, ExchangeRuleMode mode)
    { var a = Common(s, script); a.AddRange(["-HtmlFile", htmlFile, "-Mode", mode.ToString(), "-Force"]); return a; }

    public static List<string> TestArgs(ExchangeSettings s, string script) =>
        ["-NoProfile", "-NonInteractive", "-File", script, "-AppId", s.AppId, "-CertificateFilePath", s.CertificatePath, "-Organization", s.Organization];

    public static List<string> RemoveArgs(ExchangeSettings s, string script)
    { var a = Common(s, script); a.AddRange(["-Remove", "-Force"]); return a; }

    public static List<string> ClearArgs(ExchangeSettings s, string script, string backupFile, string? mailbox, bool all)
    {
        var a = Common(s, script);
        a.AddRange(["-BackupFile", backupFile, "-Force"]);
        if (all) a.Add("-All"); else a.AddRange(["-Mailbox", mailbox ?? ""]);
        return a;
    }

    /// <summary>Skrót szablonu: pozwala wykryć, że reguła w Exchange jest nieaktualna.</summary>
    public static string TemplateHash(string html, ExchangeRuleMode mode) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(mode + "|" + html)))[..16];
}
