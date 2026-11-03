using System.Security.Claims;
using M365Samoobsluga.Core.Models;
using M365Samoobsluga.Core.Services;
using Microsoft.AspNetCore.Components.Authorization;

namespace M365Samoobsluga.Web.Auth;

/// <summary>Tożsamość z uwierzytelnionego principala. W trybie testowym (mock) zwraca użytkownika demonstracyjnego.</summary>
public sealed class CurrentUser(AuthenticationStateProvider auth) : ICurrentUser
{
    public UserIdentity Current
    {
        get
        {
            var p = auth.GetAuthenticationStateAsync().GetAwaiter().GetResult().User;
            if (p.Identity?.IsAuthenticated != true) throw new UnauthorizedAccessException("Brak zalogowanego użytkownika.");
            var oid = p.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value ?? p.FindFirst("oid")?.Value ?? "";
            var upn = p.FindFirst("preferred_username")?.Value ?? p.FindFirst(ClaimTypes.Upn)?.Value ?? p.Identity.Name ?? "";
            return new UserIdentity(oid, upn, p.FindFirst("name")?.Value);
        }
    }
}
