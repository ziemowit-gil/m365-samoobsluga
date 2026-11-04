using System.Text.Json;
using System.Text.RegularExpressions;
using M365Samoobsluga.Core.Services;
using Microsoft.Extensions.Logging;

namespace M365Samoobsluga.Graph.Services;

/// <summary>Obliczanie nowej listy adresów skrzynki po dodaniu lub usunięciu aliasu. Adres główny (SMTP:) jest nietykalny.</summary>
public static class AliasList
{
    public static IReadOnlyList<string> Apply(IEnumerable<string> current, AliasChangeKind kind, string address)
    {
        var list = current.ToList();
        var secondary = "smtp:" + address.Trim().ToLowerInvariant();
        bool Is(string x) => string.Equals(x, secondary, StringComparison.OrdinalIgnoreCase);
        if (kind == AliasChangeKind.Add) { if (!list.Any(Is)) list.Add(secondary); return list; }
        return list.Where(x => !(Is(x) && !x.StartsWith("SMTP:", StringComparison.Ordinal))).ToList();
    }
}

/// <summary>
/// Samoobsługa aliasów: gdy adres jest wolny, zmiana jest wykonywana od razu w Exchange (Get-Recipient, Get-Mailbox, Set-Mailbox) na skrzynce
/// zalogowanej osoby. Gdy Exchange nie jest skonfigurowany albo połączenie zawodzi, wniosek trafia do kolejki administratora.
/// UPN zawsze pochodzi z tokenu użytkownika, nigdy z formularza.
/// </summary>
public sealed partial class ExchangeAliasChangeExecutor(Func<ExchangeRestClient> client, Func<bool> configured, IAliasChangeExecutor fallback, ILogger<ExchangeAliasChangeExecutor> log) : IAliasChangeExecutor
{
    [GeneratedRegex(@"^[a-z0-9]+([._-][a-z0-9]+)*@[a-z0-9.-]+\.[a-z]{2,63}$")] private static partial Regex SafeAddr();

    public bool AppliesImmediately => configured();

    public async Task<AliasOutcome> ExecuteAsync(AliasChange c, CancellationToken ct = default)
    {
        var addr = c.Address.Trim().ToLowerInvariant();
        if (!SafeAddr().IsMatch(addr)) throw new UserFacingException("Niepoprawny adres aliasu.");
        if (!configured()) return await fallback.ExecuteAsync(c, ct);
        try
        {
            var cl = client();
            if (c.Kind == AliasChangeKind.Add)
            {
                var taken = await cl.InvokeAsync("Get-Recipient", new Dictionary<string, object?> { ["Filter"] = $"EmailAddresses -eq 'smtp:{addr}'", ["ResultSize"] = 1 }, ct);
                if (taken.Count > 0) throw new UserFacingException("Ten adres jest już zajęty w organizacji. Wybierz inną nazwę aliasu.");
            }
            var mbx = (await cl.InvokeAsync("Get-Mailbox", new Dictionary<string, object?> { ["Identity"] = c.UserPrincipalName }, ct)).FirstOrDefault();
            if (mbx.ValueKind != JsonValueKind.Object || !mbx.TryGetProperty("EmailAddresses", out var ea) || ea.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Exchange nie zwrócił adresów skrzynki.");
            var current = ea.EnumerateArray().Select(e => e.GetString() ?? "").Where(x => x != "").ToList();
            var updated = AliasList.Apply(current, c.Kind, addr);
            if (updated.SequenceEqual(current)) return AliasOutcome.Applied; // nic do zmiany
            await cl.InvokeAsync("Set-Mailbox", new Dictionary<string, object?> { ["Identity"] = c.UserPrincipalName, ["EmailAddresses"] = updated }, ct);
            log.LogInformation("Alias wykonany w Exchange: {Kind} {Address}", c.Kind, addr);
            return AliasOutcome.Applied;
        }
        catch (UserFacingException) { throw; }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or IOException or TaskCanceledException or System.Security.Cryptography.CryptographicException or Microsoft.Identity.Client.MsalException)
        {
            log.LogWarning(ex, "Wykonanie aliasu w Exchange nie powiodło się, zapisuję wniosek dla administratora.");
            return await fallback.ExecuteAsync(c, ct);
        }
    }
}
