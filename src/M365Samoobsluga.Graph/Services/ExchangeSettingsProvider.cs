using System.Text.Json;
using M365Samoobsluga.Core.Services;

namespace M365Samoobsluga.Graph.Services;

/// <summary>
/// Ustawienia Exchange: wartości z konfiguracji (appsettings, zmienne środowiskowe) uzupełnione tym, co administrator zapisał w kreatorze
/// (App_Data/exchange-settings.json: Client ID i domena) oraz certyfikatem wygenerowanym w aplikacji (App_Data/exchange/exo.pfx).
/// Hasło do .pfx leży w osobnym pliku z prawami 600 obok certyfikatu, o ile nie ustawiono zmiennej środowiskowej.
/// </summary>
public sealed class ExchangeSettingsProvider(ExchangeSettings baseSettings, string dataDir)
{
    private sealed record Saved(string AppId, string Organization);
    private string SettingsFile => Path.Combine(dataDir, "exchange-settings.json");
    public string CertDir => Path.GetFullPath(Path.Combine(dataDir, "exchange"));
    public string PfxPath => Path.Combine(CertDir, "exo.pfx");
    public string PasswordFile => Path.Combine(CertDir, "exo.pfx.pwd");
    public string CerPath => Path.Combine(CertDir, "exo.cer");

    public ExchangeSettings Get()
    {
        var s = new ExchangeSettings
        {
            Enabled = baseSettings.Enabled, AppId = baseSettings.AppId, Organization = baseSettings.Organization,
            CertificatePath = baseSettings.CertificatePath, PowerShellPath = baseSettings.PowerShellPath, Mode = baseSettings.Mode, ModuleVersion = baseSettings.ModuleVersion, CertificatePasswordEnv = baseSettings.CertificatePasswordEnv,
        };
        if (File.Exists(SettingsFile))
        {
            try
            {
                var f = JsonSerializer.Deserialize<Saved>(File.ReadAllText(SettingsFile));
                if (f is not null) { s.AppId = f.AppId; s.Organization = f.Organization; s.Enabled = true; }
            }
            catch (JsonException) { /* uszkodzony plik: zostają wartości z konfiguracji */ }
        }
        if (string.IsNullOrWhiteSpace(s.CertificatePath) && File.Exists(PfxPath)) s.CertificatePath = PfxPath;
        return s;
    }

    public string? ReadPassword(ExchangeSettings s)
    {
        var env = Environment.GetEnvironmentVariable(s.CertificatePasswordEnv);
        if (!string.IsNullOrEmpty(env)) return env;
        return File.Exists(PasswordFile) ? File.ReadAllText(PasswordFile).Trim() : null;
    }

    public async Task SaveAsync(string appId, string organization, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetFullPath(dataDir));
        await File.WriteAllTextAsync(SettingsFile, JsonSerializer.Serialize(new Saved(appId.Trim(), organization.Trim())), ct);
    }

    /// <summary>Zapisuje certyfikat: .pfx i hasło z prawami 600 (tylko konto usługi), .cer do wgrania w Entra.</summary>
    public async Task SaveCertificateAsync(GeneratedCertificate c, CancellationToken ct = default)
    {
        Directory.CreateDirectory(CertDir);
        await File.WriteAllBytesAsync(PfxPath, c.Pfx, ct);
        await File.WriteAllTextAsync(PasswordFile, c.Password, ct);
        await File.WriteAllBytesAsync(CerPath, c.Cer, ct);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(PfxPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.SetUnixFileMode(PasswordFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    public bool HasCertificate => File.Exists(PfxPath) && File.Exists(PasswordFile);
}
