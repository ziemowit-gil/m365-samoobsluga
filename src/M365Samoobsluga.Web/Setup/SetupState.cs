using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace M365Samoobsluga.Web.Setup;

/// <summary>
/// Stan konfiguracji pierwszego uruchomienia. Dopóki aplikacja nie ma Tenant ID i Client ID, działa tylko strona /setup,
/// a zapis wymaga jednorazowego kodu z logu serwera (ochrona przed obcą osobą, która trafi na niezabezpieczoną instancję).
/// </summary>
public sealed class SetupState(bool active, string code)
{
    private int _failed;
    public bool Active { get; } = active;
    public string Code { get; } = code;
    public const int MaxAttempts = 5;

    public static string NewCode() => Convert.ToHexString(RandomNumberGenerator.GetBytes(4));

    /// <summary>Sprawdza kod w stałym czasie. Po 5 błędach blokuje dalsze próby do restartu aplikacji.</summary>
    public bool TryCode(string? given)
    {
        if (!Active || Volatile.Read(ref _failed) >= MaxAttempts) return false;
        var ok = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes((given ?? "").Trim().ToUpperInvariant()), Encoding.UTF8.GetBytes(Code));
        if (!ok) Interlocked.Increment(ref _failed);
        return ok;
    }

    public bool Locked => Volatile.Read(ref _failed) >= MaxAttempts;
}

/// <summary>Plik z identyfikatorami aplikacji (bez sekretów): App_Data/azuread.json, wczytywany przy starcie.</summary>
public static class AzureAdFile
{
    public static void Save(string path, string tenantId, string clientId)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(new { AzureAd = new { TenantId = tenantId.Trim(), ClientId = clientId.Trim() } }, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, path, overwrite: true);
    }
}
