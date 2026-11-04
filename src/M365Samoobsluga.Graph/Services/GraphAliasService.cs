using M365Samoobsluga.Core.Models;
using M365Samoobsluga.Core.Services;
using M365Samoobsluga.Core.Validation;
using Microsoft.Graph;

namespace M365Samoobsluga.Graph.Services;

/// <summary>
/// Odczyt aliasów: GET /me?$select=proxyAddresses (User.Read) i domen: GET /organization (User.Read zwraca verifiedDomains).
/// Zapis: proxyAddresses jest w Graph tylko do odczytu, więc zmiany idą przez <see cref="IAliasChangeExecutor"/>
/// (wniosek do administratora albo wąski backend z Exchange Online).
/// </summary>
public sealed class GraphAliasService(GraphServiceClient graph, IAliasChangeExecutor executor, ICurrentUser user, int maxAliases = 5) : IAliasService
{
    public bool AppliesImmediately => executor.AppliesImmediately;

    public async Task<MailboxAliases> GetAsync(CancellationToken ct = default)
    {
        try
        {
            var me = await graph.Me.GetAsync(rc => rc.QueryParameters.Select = ["proxyAddresses", "userPrincipalName", "mail"], cancellationToken: ct);
            var proxies = me?.ProxyAddresses ?? [];
            var primary = proxies.FirstOrDefault(p => p.StartsWith("SMTP:", StringComparison.Ordinal))?[5..] ?? me?.Mail ?? me?.UserPrincipalName ?? "";
            var secondary = proxies.Where(p => p.StartsWith("smtp:", StringComparison.Ordinal)).Select(p => p[5..].ToLowerInvariant()).OrderBy(p => p).ToList();
            var org = await graph.Organization.GetAsync(cancellationToken: ct);
            var domains = org?.Value?.FirstOrDefault()?.VerifiedDomains?
                .Where(d => d.Capabilities?.Contains("Email", StringComparison.OrdinalIgnoreCase) == true && d.Name is not null)
                .Select(d => d.Name!.ToLowerInvariant()).OrderBy(d => d).ToList() ?? [];
            return new MailboxAliases(primary.ToLowerInvariant(), secondary, domains);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw GraphErrorMapper.Map(ex, "aliasy"); }
    }

    public async Task<AliasOutcome> AddAsync(string address, CancellationToken ct = default)
    {
        var cur = await GetAsync(ct);
        var all = cur.SecondaryAddresses.Append(cur.PrimaryAddress).ToList();
        var at = address.LastIndexOf('@');
        var v = at > 0 ? AddressValidator.ValidateAlias(address[..at], address[(at + 1)..], cur.VerifiedDomains, all)
                       : ValidationResult.Fail("Nieprawidłowy adres aliasu.");
        if (!v.IsValid) throw new UserFacingException(v.Error!);
        if (cur.SecondaryAddresses.Count >= maxAliases) throw new UserFacingException($"Możesz mieć najwyżej {maxAliases} dodatkowych aliasów. Usuń jeden, żeby dodać nowy.");
        return await executor.ExecuteAsync(new AliasChange(user.Current.UserPrincipalName, AliasChangeKind.Add, v.Normalized!), ct);
    }

    public async Task<AliasOutcome> RemoveAsync(string address, CancellationToken ct = default)
    {
        var cur = await GetAsync(ct);
        var a = address.Trim().ToLowerInvariant();
        if (!cur.SecondaryAddresses.Contains(a)) throw new UserFacingException("Można usuwać tylko dodatkowe aliasy; adres główny jest zablokowany.");
        return await executor.ExecuteAsync(new AliasChange(user.Current.UserPrincipalName, AliasChangeKind.Remove, a), ct);
    }
}
