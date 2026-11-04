using M365Samoobsluga.Core.Services;
using Microsoft.Graph;
using Microsoft.Identity.Web;

namespace M365Samoobsluga.Graph.Services;

/// <summary>
/// GET /users (delegowane User.ReadBasic.All, każdy użytkownik może wyrazić zgodę). Zakres jest dobierany dopiero przy użyciu tej funkcji,
/// dlatego kolejne strony wyników pobieramy ręcznie z tym samym zakresem.
/// </summary>
public sealed class GraphDirectoryUsers(GraphServiceClient graph) : IDirectoryUsers
{
    private const int MaxUsers = 5000;

    public async Task<IReadOnlyList<string>> ListAddressesAsync(string domain, CancellationToken ct = default)
    {
        try
        {
            var all = new List<DirectoryUser>();
            var page = await graph.Users.GetAsync(rc =>
            {
                rc.QueryParameters.Select = ["mail", "userPrincipalName", "userType"];
                rc.QueryParameters.Top = 999;
                rc.Options.WithScopes("User.ReadBasic.All");
            }, ct);
            while (page is not null)
            {
                foreach (var u in page.Value ?? []) all.Add(new DirectoryUser(u.Mail, u.UserPrincipalName, u.UserType));
                if (page.OdataNextLink is null || all.Count >= MaxUsers) break;
                page = await graph.Users.WithUrl(page.OdataNextLink).GetAsync(rc => rc.Options.WithScopes("User.ReadBasic.All"), ct);
            }
            return StopkaNotice.SelectDomainAddresses(all, domain);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw GraphErrorMapper.Map(ex, "lista użytkowników"); }
    }
}
