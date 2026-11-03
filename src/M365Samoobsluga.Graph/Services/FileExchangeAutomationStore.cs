using System.Text.Json;
using M365Samoobsluga.Core.Services;

namespace M365Samoobsluga.Graph.Services;

public sealed class FileExchangeAutomationStore(string path) : IExchangeAutomationStore
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<ExchangeAutomation> GetAsync(CancellationToken ct = default)
    {
        if (!File.Exists(path)) return ExchangeAutomation.Default;
        try { return JsonSerializer.Deserialize<ExchangeAutomation>(await File.ReadAllTextAsync(path, ct)) ?? ExchangeAutomation.Default; }
        catch (JsonException) { return ExchangeAutomation.Default; }
    }

    public async Task SaveAsync(ExchangeAutomation v, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var tmp = path + ".tmp";
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(v), ct);
            File.Move(tmp, path, overwrite: true);
        }
        finally { Gate.Release(); }
    }
}
