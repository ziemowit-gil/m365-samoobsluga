using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using M365Samoobsluga.Core.Services;
using Microsoft.Identity.Client;

namespace M365Samoobsluga.Graph.Services;

/// <summary>Wdrażanie reguły stopki i czyszczenie podpisów przez REST (bez PowerShella). Uwierzytelnianie certyfikatem aplikacji (MSAL).</summary>
public sealed class ExchangeRestRuleDeployer(ExchangeRestAccess access, ExchangeSettingsProvider provider, string dataDir) : IExchangeRuleDeployer
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private ExchangeSettings S => provider.Get();
    public bool IsConfigured => ExchangeCommandBuilder.Validate(S) is null;

    private ExchangeRestClient Client() => access.Client();

    private async Task<ExchangeOpResult> Guarded(Func<ExchangeRestClient, Task<string>> op, CancellationToken ct)
    {
        if (ExchangeCommandBuilder.Validate(S) is { } err) return new(false, err);
        await Gate.WaitAsync(ct);
        try { return new(true, await op(Client())); }
        catch (MsalServiceException ex) { return new(false, $"Błąd pobrania tokenu ({ex.ErrorCode}): {Trim(ex.Message)}"); }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or IOException or System.Security.Cryptography.CryptographicException or TaskCanceledException)
        { return new(false, Trim(ex.Message)); }
        finally { Gate.Release(); }
    }

    private static string Trim(string s) => s.Length > 600 ? s[..600] + "…" : s;

    public Task<ExchangeOpResult> TestAsync(CancellationToken ct = default) => Guarded(async c =>
    {
        var rules = await c.InvokeAsync("Get-TransportRule", null, ct);
        var mine = rules.FirstOrDefault(r => r.TryGetProperty("Name", out var n) && n.GetString() == ExchangeRuleBuilder.RuleName);
        return $"Połączenie działa. Reguł transportowych: {rules.Count}. Reguła stopki: " +
               (mine.ValueKind == JsonValueKind.Undefined ? "jeszcze nie istnieje." : $"istnieje (stan {Prop(mine, "State")}, tryb {Prop(mine, "Mode")}).");
    }, ct);

    public Task<ExchangeOpResult> DeployAsync(string html, ExchangeRuleMode mode, CancellationToken ct = default)
    {
        if (ExchangeRuleBuilder.Validate(html) is { } tooLong) return Task.FromResult(new ExchangeOpResult(false, tooLong));
        return Guarded(async c =>
        {
            var existing = await c.InvokeAsync("Get-TransportRule", new Dictionary<string, object?> { ["Identity"] = ExchangeRuleBuilder.RuleName }, ct).ContinueWith(t => t.IsFaulted ? [] : t.Result);
            var p = new Dictionary<string, object?>
            {
                ["ApplyHtmlDisclaimerLocation"] = "Append",
                ["ApplyHtmlDisclaimerText"] = html,
                ["ApplyHtmlDisclaimerFallbackAction"] = "Wrap",
                ["FromScope"] = "InOrganization",
                ["SetHeaderName"] = ExchangeRuleBuilder.MarkHeader,
                ["SetHeaderValue"] = "1",
                ["ExceptIfHeaderMatchesMessageHeader"] = ExchangeRuleBuilder.MarkHeader,
                ["ExceptIfHeaderMatchesPatterns"] = new[] { "1" },
                ["Mode"] = mode.ToString(),
                ["Comments"] = "Zarządzane przez Samoobsługę poczty. Nie edytuj ręcznie.",
            };
            if (existing.Count > 0)
            { p["Identity"] = ExchangeRuleBuilder.RuleName; await c.InvokeAsync("Set-TransportRule", p, ct); }
            else
            { p["Name"] = ExchangeRuleBuilder.RuleName; p["Enabled"] = true; await c.InvokeAsync("New-TransportRule", p, ct); }
            var rule = (await c.InvokeAsync("Get-TransportRule", new Dictionary<string, object?> { ["Identity"] = ExchangeRuleBuilder.RuleName }, ct)).FirstOrDefault();
            return $"Gotowe. Reguła '{ExchangeRuleBuilder.RuleName}': stan {Prop(rule, "State")}, tryb {Prop(rule, "Mode")}.";
        }, ct);
    }

    public Task<ExchangeOpResult> RemoveAsync(CancellationToken ct = default) => Guarded(async c =>
    {
        var existing = await c.InvokeAsync("Get-TransportRule", new Dictionary<string, object?> { ["Identity"] = ExchangeRuleBuilder.RuleName }, ct).ContinueWith(t => t.IsFaulted ? [] : t.Result);
        if (existing.Count == 0) return $"Reguła '{ExchangeRuleBuilder.RuleName}' nie istnieje.";
        await c.InvokeAsync("Remove-TransportRule", new Dictionary<string, object?> { ["Identity"] = ExchangeRuleBuilder.RuleName }, ct);
        return $"Usunięto regułę '{ExchangeRuleBuilder.RuleName}'.";
    }, ct);

    public Task<ExchangeOpResult> ClearSignaturesAsync(string? mailbox, bool all, CancellationToken ct = default)
    {
        if (!all && !ExchangeCommandBuilder.IsValidMailbox(mailbox)) return Task.FromResult(new ExchangeOpResult(false, "Podaj poprawny adres skrzynki albo wybierz wszystkie."));
        return Guarded(async c =>
        {
            var boxes = all
                ? await c.InvokeAsync("Get-Mailbox", new Dictionary<string, object?> { ["RecipientTypeDetails"] = "UserMailbox", ["ResultSize"] = "Unlimited" }, ct)
                : await c.InvokeAsync("Get-Mailbox", new Dictionary<string, object?> { ["Identity"] = mailbox }, ct);
            var backup = new List<object>(); var changed = 0;
            var backupPath = Path.GetFullPath(Path.Combine(dataDir, "signature-backups", $"podpisy-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json"));
            foreach (var b in boxes)
            {
                var upn = Prop(b, "UserPrincipalName"); if (upn == "") continue;
                var cfg = (await c.InvokeAsync("Get-MailboxMessageConfiguration", new Dictionary<string, object?> { ["Identity"] = upn }, ct)).FirstOrDefault();
                if (cfg.ValueKind == JsonValueKind.Undefined) continue;
                var html = Prop(cfg, "SignatureHtml"); var text = Prop(cfg, "SignatureText");
                var a1 = Prop(cfg, "AutoAddSignature") == "True"; var a2 = Prop(cfg, "AutoAddSignatureOnReply") == "True";
                if (html == "" && text == "" && !a1 && !a2) continue;
                backup.Add(new { UserPrincipalName = upn, SignatureHtml = html, SignatureText = text, AutoAddSignature = a1, AutoAddSignatureOnReply = a2 });
                // kopia zapasowa przed zmianą
                Directory.CreateDirectory(Path.Combine(dataDir, "signature-backups"));
                await File.WriteAllTextAsync(backupPath, JsonSerializer.Serialize(backup), ct);
                await c.InvokeAsync("Set-MailboxMessageConfiguration", new Dictionary<string, object?>
                { ["Identity"] = upn, ["SignatureHtml"] = "", ["SignatureText"] = "", ["AutoAddSignature"] = false, ["AutoAddSignatureOnReply"] = false }, ct);
                changed++;
            }
            return $"Skrzynek z zapisanym podpisem: {backup.Count}. Wyczyszczono: {changed}." + (backup.Count > 0 ? $" Kopia zapasowa: {backupPath}" : "");
        }, ct);
    }

    private static string Prop(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? (v.ValueKind == JsonValueKind.Null ? "" : v.ToString()) : "";
}
