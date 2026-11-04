using M365Samoobsluga.Graph.Services;

namespace M365Samoobsluga.Web.Auth;

/// <summary>Co kilka minut próbuje wykonać wnioski o aliasy z kolejki, gdy Exchange jest skonfigurowany.</summary>
public sealed class AliasQueueSync(ExchangeRestAccess access, ILogger<AliasQueueSync> log, string queuePath) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { return; }
        var direct = new ExchangeAliasChangeExecutor(access.Client, () => access.IsConfigured, new UnavailableAliasChangeExecutor(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ExchangeAliasChangeExecutor>.Instance);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (access.IsConfigured)
                {
                    var r = await AliasQueueProcessor.ProcessAsync(queuePath, direct, log, stoppingToken);
                    if (r.Applied + r.Rejected > 0) log.LogInformation("Kolejka aliasów: wykonano {Applied}, odrzucono {Rejected}, zostało {Kept}.", r.Applied, r.Rejected, r.Kept);
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { log.LogWarning(ex, "Przebieg kolejki aliasów nie powiódł się."); }
            try { await Task.Delay(Every, stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
