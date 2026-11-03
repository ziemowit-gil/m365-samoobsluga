using M365Samoobsluga.Core.Services;

namespace M365Samoobsluga.Web.Auth;

/// <summary>
/// Automatyczne tworzenie i aktualizacja reguły w Exchange (bez użytkownika): przy starcie i co godzinę porównuje skrót szablonu
/// z ostatnio wdrożonym i, gdy włączono automatykę, zakłada lub aktualizuje regułę. Zapisuje wynik, żeby nie powtarzać nieudanych prób w pętli.
/// </summary>
public sealed class ExchangeRuleSync(IServiceScopeFactory scopes, ILogger<ExchangeRuleSync> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stop);
        while (!stop.IsCancellationRequested)
        {
            try { await SyncOnceAsync(stop); } catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Synchronizacja reguły Exchange nie powiodła się."); }
            await Task.Delay(TimeSpan.FromHours(1), stop);
        }
    }

    public static async Task<ExchangeOpResult?> DeployIfNeededAsync(IExchangeRuleDeployer deployer, IExchangeAutomationStore store, ISignatureTemplateStore templates, bool force, CancellationToken ct)
    {
        var auto = await store.GetAsync(ct);
        if (!auto.AutoDeploy || !deployer.IsConfigured) return null;
        var t = await templates.GetAsync(ct);
        var html = ExchangeRuleBuilder.BuildTemplateHtml(t.Org, t.Design);
        var hash = ExchangeCommandBuilder.TemplateHash(html, auto.Mode);
        if (!force && hash == auto.LastHash) return null;
        var r = await deployer.DeployAsync(html, auto.Mode, ct);
        // Przy błędzie zapisujemy wynik, ale nie hash: kolejna próba nastąpi przy następnym przebiegu lub zapisie szablonu.
        await store.SaveAsync(auto with { LastHash = r.Success ? hash : auto.LastHash, LastDeployedAt = DateTimeOffset.UtcNow, LastResult = (r.Success ? "OK: " : "Błąd: ") + (r.Output.Length > 300 ? r.Output[..300] : r.Output) }, ct);
        return r;
    }

    private async Task SyncOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        // Tło nie ma zalogowanego użytkownika, więc używa nieaudytowanych, bezpośrednich implementacji.
        var r = await DeployIfNeededAsync(sp.GetRequiredService<PowerShellDeployerAccessor>().Raw, sp.GetRequiredService<IExchangeAutomationStore>(), sp.GetRequiredService<TemplateStoreAccessor>().Raw, false, ct);
        if (r is not null) log.LogInformation("Automatyczne wdrożenie reguły Exchange: {Ok} {Output}", r.Success, r.Output.Length > 200 ? r.Output[..200] : r.Output);
    }
}

/// <summary>Dostęp do implementacji bez dekoratora audytu (zadanie w tle nie ma zalogowanej osoby). Zdarzenia z tła trafiają do zwykłego logu.</summary>
public sealed record PowerShellDeployerAccessor(IExchangeRuleDeployer Raw);
public sealed record TemplateStoreAccessor(ISignatureTemplateStore Raw);
