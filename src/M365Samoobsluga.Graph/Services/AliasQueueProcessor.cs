using System.Text.Json;
using M365Samoobsluga.Core.Services;
using Microsoft.Extensions.Logging;

namespace M365Samoobsluga.Graph.Services;

public sealed record AliasQueueResult(int Applied, int Rejected, int Kept);

/// <summary>
/// Automatyczna obsługa kolejki wniosków o aliasy: gdy połączenie z Exchange zaczyna działać, wnioski zapisane wcześniej
/// są wykonywane bez udziału administratora. Wykonane i odrzucone (np. adres zajęty) znikają z kolejki, a te, które
/// nadal się nie udają (Exchange niedostępny), zostają i są ponawiane przy następnym przebiegu.
/// </summary>
public static class AliasQueueProcessor
{
    /// <param name="direct">Wykonawca, który przy braku połączenia z Exchange rzuca wyjątek zamiast zapisywać wniosek.</param>
    public static async Task<AliasQueueResult> ProcessAsync(string path, IAliasChangeExecutor direct, ILogger log, CancellationToken ct = default)
    {
        List<string> snapshot;
        await FileQueueAliasChangeExecutor.Gate.WaitAsync(ct);
        try { snapshot = File.Exists(path) ? (await File.ReadAllLinesAsync(path, ct)).Where(l => l.Trim() != "").ToList() : []; }
        finally { FileQueueAliasChangeExecutor.Gate.Release(); }
        if (snapshot.Count == 0) return new(0, 0, 0);

        var done = new List<string>();
        int applied = 0, rejected = 0;
        foreach (var line in snapshot)
        {
            var change = Parse(line);
            if (change is null) { done.Add(line); rejected++; log.LogWarning("Pominięto nieczytelny wpis kolejki aliasów."); continue; }
            try
            {
                await direct.ExecuteAsync(change, ct);
                done.Add(line); applied++;
                log.LogInformation("Wniosek z kolejki wykonany automatycznie: {Kind} {Address}", change.Kind, change.Address);
            }
            catch (UserFacingException ex)
            {
                done.Add(line); rejected++;
                log.LogWarning("Wniosek z kolejki odrzucony: {Kind} {Address}: {Reason}", change.Kind, change.Address, ex.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogInformation("Wniosek z kolejki zostaje na później ({Reason}).", ex.Message);
                break; // Exchange niedostępny: nie ma sensu próbować kolejnych
            }
        }
        if (done.Count == 0) return new(0, 0, snapshot.Count);

        await FileQueueAliasChangeExecutor.Gate.WaitAsync(ct);
        try
        {
            var current = File.Exists(path) ? (await File.ReadAllLinesAsync(path, ct)).Where(l => l.Trim() != "").ToList() : [];
            foreach (var d in done) current.Remove(d); // usuwa po jednym wystąpieniu, nowe wnioski dopisane w międzyczasie zostają
            await File.WriteAllLinesAsync(path, current, ct);
            return new(applied, rejected, current.Count);
        }
        finally { FileQueueAliasChangeExecutor.Gate.Release(); }
    }

    internal static AliasChange? Parse(string line)
    {
        try
        {
            using var d = JsonDocument.Parse(line);
            var r = d.RootElement;
            var upn = r.GetProperty("UserPrincipalName").GetString();
            var addr = r.GetProperty("Address").GetString();
            if (string.IsNullOrWhiteSpace(upn) || string.IsNullOrWhiteSpace(addr)) return null;
            return Enum.TryParse<AliasChangeKind>(r.GetProperty("kind").GetString(), out var kind) ? new AliasChange(upn, kind, addr) : null;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }
}

/// <summary>Zamiast zapisywać wniosek do kolejki (co zrobiłoby z niej pętlę), zgłasza brak połączenia z Exchange.</summary>
public sealed class UnavailableAliasChangeExecutor : IAliasChangeExecutor
{
    public bool AppliesImmediately => false;
    public Task<AliasOutcome> ExecuteAsync(AliasChange change, CancellationToken ct = default) =>
        throw new InvalidOperationException("Exchange jest niedostępny.");
}
