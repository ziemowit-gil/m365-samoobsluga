using System.Text.Json;
using M365Samoobsluga.Core.Services;
using Microsoft.Extensions.Logging;

namespace M365Samoobsluga.Graph.Services;

public enum AliasChangeKind { Add, Remove }
public sealed record AliasChange(string UserPrincipalName, AliasChangeKind Kind, string Address);

/// <summary>Wykonanie zapisu aliasu. UPN pochodzi zawsze z tokenu zalogowanego użytkownika, nigdy z formularza.</summary>
public interface IAliasChangeExecutor
{
    bool AppliesImmediately { get; }
    Task<AliasOutcome> ExecuteAsync(AliasChange change, CancellationToken ct = default);
}

/// <summary>
/// Domyślny tryb bezpieczny: wniosek zapisany do kolejki (plik JSON Lines), który administrator realizuje przez Exchange Online
/// (Set-Mailbox -EmailAddresses @{add=...}). Zamiennik: backend z uprawnieniami Exchange ograniczonymi do jednej skrzynki.
/// </summary>
public sealed class FileQueueAliasChangeExecutor(string path, ILogger<FileQueueAliasChangeExecutor> logger) : IAliasChangeExecutor
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public bool AppliesImmediately => false;

    public async Task<AliasOutcome> ExecuteAsync(AliasChange change, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var line = JsonSerializer.Serialize(new { at = DateTimeOffset.UtcNow, change.UserPrincipalName, kind = change.Kind.ToString(), change.Address });
            await File.AppendAllTextAsync(path, line + Environment.NewLine, ct);
            logger.LogInformation("Wniosek o alias zapisany: {Kind} {Address}", change.Kind, change.Address);
            return AliasOutcome.Queued;
        }
        finally { Gate.Release(); }
    }
}
