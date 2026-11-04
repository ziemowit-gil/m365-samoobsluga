using System.Diagnostics;
using System.Text;
using M365Samoobsluga.Core.Services;
using Microsoft.Extensions.Logging;

namespace M365Samoobsluga.Graph.Services;

/// <summary>
/// Wdraża regułę stopki i czyści podpisy przez PowerShell (moduł ExchangeOnlineManagement, uwierzytelnianie certyfikatem aplikacji bez użytkownika).
/// Proces dostaje argumenty jako lista (bez powłoki), szablon w pliku tymczasowym, a hasło certyfikatu tylko w zmiennej środowiskowej procesu.
/// </summary>
public sealed class PowerShellExchangeRuleDeployer(ExchangeSettingsProvider provider, string scriptsDir, string dataDir, ILogger<PowerShellExchangeRuleDeployer> log) : IExchangeRuleDeployer
{
    private ExchangeSettings settings => provider.Get();

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    public bool IsConfigured => ExchangeCommandBuilder.Validate(settings) is null;

    public Task<ExchangeOpResult> TestAsync(CancellationToken ct = default) =>
        ExchangeCommandBuilder.Validate(settings) is { } err ? Task.FromResult(new ExchangeOpResult(false, err))
            : RunAsync(ExchangeCommandBuilder.TestArgs(settings, Script("Test-ExchangeConnection.ps1")), ct);

    public async Task<ExchangeOpResult> DeployAsync(string html, ExchangeRuleMode mode, CancellationToken ct = default)
    {
        if (ExchangeCommandBuilder.Validate(settings) is { } err) return new(false, err);
        if (ExchangeRuleBuilder.Validate(html) is { } tooLong) return new(false, tooLong);
        var tmp = Path.Combine(Path.GetTempPath(), $"stopka-{Guid.NewGuid():N}.html");
        try
        {
            await File.WriteAllTextAsync(tmp, html, new UTF8Encoding(false), ct);
            return await RunAsync(ExchangeCommandBuilder.DeployArgs(settings, Script("Set-StopkaRule.ps1"), tmp, mode), ct);
        }
        finally { try { File.Delete(tmp); } catch { /* plik tymczasowy */ } }
    }

    public Task<ExchangeOpResult> RemoveAsync(CancellationToken ct = default) =>
        ExchangeCommandBuilder.Validate(settings) is { } err ? Task.FromResult(new ExchangeOpResult(false, err))
            : RunAsync(ExchangeCommandBuilder.RemoveArgs(settings, Script("Set-StopkaRule.ps1")), ct);

    public Task<ExchangeOpResult> ClearSignaturesAsync(string? mailbox, bool all, CancellationToken ct = default)
    {
        if (ExchangeCommandBuilder.Validate(settings) is { } err) return Task.FromResult(new ExchangeOpResult(false, err));
        if (!all && !ExchangeCommandBuilder.IsValidMailbox(mailbox)) return Task.FromResult(new ExchangeOpResult(false, "Podaj poprawny adres skrzynki albo wybierz wszystkie."));
        Directory.CreateDirectory(Path.Combine(dataDir, "signature-backups"));
        var backup = Path.GetFullPath(Path.Combine(dataDir, "signature-backups", $"podpisy-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json"));
        return RunAsync(ExchangeCommandBuilder.ClearArgs(settings, Script("Clear-UserSignatures.ps1"), backup, mailbox, all), ct);
    }

    private string Script(string name) => Path.Combine(scriptsDir, name);

    private async Task<ExchangeOpResult> RunAsync(List<string> args, CancellationToken ct)
    {
        var script = args[args.IndexOf("-File") + 1];
        if (!File.Exists(script)) return new(false, $"Nie znaleziono skryptu {Path.GetFileName(script)}.");
        await Gate.WaitAsync(ct);
        try
        {
            var cfg = settings;
            var psi = new ProcessStartInfo(cfg.PowerShellPath) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            var pwd = provider.ReadPassword(settings);
            if (!string.IsNullOrEmpty(pwd)) psi.Environment["M365_EXO_CERT_PASSWORD"] = pwd;
            if (System.Text.RegularExpressions.Regex.IsMatch(cfg.ModuleVersion ?? "", @"^\d+\.\d+\.\d+$")) psi.Environment["M365_EXO_MODULE_VERSION"] = cfg.ModuleVersion;
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("Nie udało się uruchomić PowerShella.");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);
            var stdout = p.StandardOutput.ReadToEndAsync(cts.Token);
            var stderr = p.StandardError.ReadToEndAsync(cts.Token);
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { try { p.Kill(true); } catch { } return new(false, "Przekroczono limit czasu (3 minuty)."); }
            var text = (await stdout + "\n" + await stderr).Trim();
            if (text.Length > 3000) text = text[..3000] + "…";
            if (p.ExitCode != 0) log.LogWarning("PowerShell zakończył się kodem {Code}", p.ExitCode);
            return new(p.ExitCode == 0, text.Length == 0 ? (p.ExitCode == 0 ? "Gotowe." : "Błąd bez komunikatu.") : text);
        }
        catch (System.ComponentModel.Win32Exception)
        { return new(false, $"Nie znaleziono PowerShella ({provider.Get().PowerShellPath}). Zainstaluj go skryptem scripts/install-exchange-tools-ubuntu.sh."); }
        finally { Gate.Release(); }
    }
}
