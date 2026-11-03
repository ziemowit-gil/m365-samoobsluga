using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using M365Samoobsluga.Core.Models;
using M365Samoobsluga.Core.Services;

namespace M365Samoobsluga.Graph.Services;

/// <summary>
/// Szablon podpisu przechowywany lokalnie (plik na użytkownika, nazwa = skrót identyfikatora obiektu).
/// Microsoft Graph nie ma wspieranego API do podpisów Outlooka, więc użytkownik kopiuje gotowy podpis do Outlooka.
/// </summary>
public sealed class FileSignatureService(string directory, ICurrentUser user) : ISignatureService
{
    public const int MaxLength = 20000;

    private string FilePath()
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.Current.ObjectId)))[..24];
        return Path.Combine(directory, hash + ".json");
    }

    public async Task<SignatureSet> GetAsync(CancellationToken ct = default)
    {
        var p = FilePath();
        if (!File.Exists(p)) return new SignatureSet("", "");
        return JsonSerializer.Deserialize<SignatureSet>(await File.ReadAllTextAsync(p, ct)) ?? new SignatureSet("", "");
    }

    public async Task SaveAsync(SignatureSet s, CancellationToken ct = default)
    {
        if (s.Html.Length > MaxLength || s.Text.Length > MaxLength) throw new UserFacingException("Podpis jest za długi.");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(FilePath(), JsonSerializer.Serialize(s), ct);
    }
}
