using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using M365Samoobsluga.Core.Services;
using M365Samoobsluga.Core.Models;
using Microsoft.Graph;

namespace M365Samoobsluga.Graph.Services;

/// <summary>
/// Dane do stopki: imię, nazwisko i e-mail z profilu (GET /me, User.Read), stanowisko i telefon z profilu Microsoft 365
/// albo z nadpisania użytkownika. Nadpisanie leży w pliku (nazwa = skrót identyfikatora obiektu). Do skrzynki nic nie jest zapisywane.
/// </summary>
public sealed class GraphSignatureService(GraphServiceClient graph, string directory, ICurrentUser user) : ISignatureService
{
    private sealed record Override(string Title, string Phone);

    private string FilePath() =>
        Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.Current.ObjectId)))[..24] + ".json");

    private async Task<(SignaturePerson Dir, SignaturePerson Person)> LoadAsync(CancellationToken ct)
    {
        try
        {
            var me = await graph.Me.GetAsync(rc => rc.QueryParameters.Select = ["displayName", "givenName", "surname", "jobTitle", "mobilePhone", "businessPhones", "mail", "userPrincipalName"], cancellationToken: ct);
            var name = (me?.DisplayName ?? $"{me?.GivenName} {me?.Surname}").Trim();
            var dir = new SignaturePerson(name, (me?.JobTitle ?? "").Trim(), (me?.MobilePhone ?? me?.BusinessPhones?.FirstOrDefault() ?? "").Trim(), (me?.Mail ?? me?.UserPrincipalName ?? "").Trim());
            var p = FilePath();
            if (!File.Exists(p)) return (dir, dir);
            var o = JsonSerializer.Deserialize<Override>(await File.ReadAllTextAsync(p, ct));
            return (dir, o is null ? dir : dir with { Title = o.Title, Phone = o.Phone });
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw GraphErrorMapper.Map(ex, "stopka"); }
    }

    public async Task<SignaturePerson> GetAsync(CancellationToken ct = default) => (await LoadAsync(ct)).Person;

    public async Task SaveAsync(string title, string phone, CancellationToken ct = default)
    {
        var err = SignatureRenderer.ValidateTitle(title) ?? SignatureRenderer.ValidatePhone(phone);
        if (err is not null) throw new UserFacingException(err);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(FilePath(), JsonSerializer.Serialize(new Override(title.Trim(), phone.Trim())), ct);
    }

    public async Task<SignaturePerson> SyncFromDirectoryAsync(CancellationToken ct = default)
    {
        var (dir, cur) = await LoadAsync(ct);
        // jak w SZO: pusta wartość z katalogu nie kasuje tego, co użytkownik już ma
        var title = dir.Title != "" ? dir.Title : cur.Title;
        var phone = dir.Phone != "" ? dir.Phone : cur.Phone;
        await SaveAsync(title, phone, ct);
        return cur with { Title = title, Phone = phone };
    }
}
