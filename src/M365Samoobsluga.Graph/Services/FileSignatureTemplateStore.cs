using System.Text.Json;
using M365Samoobsluga.Core.Models;
using M365Samoobsluga.Core.Services;

namespace M365Samoobsluga.Graph.Services;

/// <summary>
/// Wzór stopki w pliku JSON w katalogu danych. Gdy pliku nie ma, używa ustawień domyślnych z konfiguracji.
/// Zapis jest atomowy (plik tymczasowy i podmiana), a wygląd przechodzi przez Sanitize.
/// </summary>
public sealed class FileSignatureTemplateStore(string path, SignatureTemplate fallback) : ISignatureTemplateStore
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<SignatureTemplate> GetAsync(CancellationToken ct = default)
    {
        if (!File.Exists(path)) return fallback;
        try
        {
            var t = JsonSerializer.Deserialize<SignatureTemplate>(await File.ReadAllTextAsync(path, ct));
            return t is null ? fallback : new SignatureTemplate(t.Org ?? new(), SignatureRenderer.Sanitize(t.Design ?? new()));
        }
        catch (JsonException) { return fallback; }
    }

    public async Task SaveAsync(SignatureTemplate t, CancellationToken ct = default)
    {
        var errors = SignatureRenderer.ValidateOrg(t.Org);
        if (errors.Count > 0) throw new UserFacingException(string.Join(" ", errors));
        var clean = new SignatureTemplate(t.Org with { }, SignatureRenderer.Sanitize(t.Design));
        await Gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var tmp = path + ".tmp";
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(clean), ct);
            File.Move(tmp, path, overwrite: true);
        }
        finally { Gate.Release(); }
    }
}
