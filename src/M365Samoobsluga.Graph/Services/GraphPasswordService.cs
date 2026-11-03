using M365Samoobsluga.Core.Models;
using M365Samoobsluga.Core.Services;
using Microsoft.Graph;
using Microsoft.Graph.Me.ChangePassword;

namespace M365Samoobsluga.Graph.Services;

/// <summary>POST /me/changePassword (delegowane User-PasswordProfile.ReadWrite.All). Hasła nie są logowane ani zapisywane.</summary>
public sealed class GraphPasswordService(GraphServiceClient graph) : IPasswordService
{
    public async Task ChangeAsync(ChangePasswordRequest r, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(r.CurrentPassword)) throw new UserFacingException("Wpisz aktualne hasło.");
        if (string.IsNullOrEmpty(r.NewPassword)) throw new UserFacingException("Wpisz nowe hasło.");
        if (r.NewPassword == r.CurrentPassword) throw new UserFacingException("Nowe hasło musi różnić się od aktualnego.");
        try
        {
            await graph.Me.ChangePassword.PostAsync(
                new ChangePasswordPostRequestBody { CurrentPassword = r.CurrentPassword, NewPassword = r.NewPassword }, cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw GraphErrorMapper.Map(ex, "hasło"); }
    }
}
