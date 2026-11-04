using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using M365Samoobsluga.Core.Services;

namespace M365Samoobsluga.Graph.Services;

/// <summary>
/// Wywołania poleceń Exchange Online przez REST (InvokeCommand, tym samym kanałem, którym mówi moduł ExchangeOnlineManagement),
/// z tokenem aplikacji. Nie wymaga PowerShella ani modułu. UWAGA: to API administracyjne Exchange nie jest w pełni udokumentowane
/// przez Microsoft (odtworzone według działania modułu i modułu stopek w SZO), więc zachowanie może się zmienić.
/// Żądania są kierowane nagłówkiem X-AnchorMailbox: APP:SystemMailbox{...}@domena (domena organizacji albo GUID dzierżawy, prefiks APP lub UPN).
/// </summary>
public sealed class ExchangeRestClient(Func<CancellationToken, Task<string>> getToken, string organization, HttpClient http)
{
    private const string SystemMailbox = "SystemMailbox{bb558c35-97f1-4cb9-8ff7-d53741dc928c}";
    private static string? _okDomain, _okPrefix;

    /// <summary>Identyfikator dzierżawy (tid) z tokenu JWT. Podpisu nie sprawdzamy: token pochodzi od Microsoftu, a tid służy tylko do adresu żądania.</summary>
    public static string TenantIdFromJwt(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2) throw new ArgumentException("To nie jest token JWT.", nameof(jwt));
        var p = parts[1].Replace('-', '+').Replace('_', '/');
        p = p.PadRight(p.Length + (4 - p.Length % 4) % 4, '=');
        using var doc = JsonDocument.Parse(Convert.FromBase64String(p));
        return doc.RootElement.TryGetProperty("tid", out var t) ? t.GetString() ?? "" : "";
    }

    public static string BuildBody(string cmdlet, IReadOnlyDictionary<string, object?>? parameters) =>
        JsonSerializer.Serialize(new { CmdletInput = new { CmdletName = cmdlet, Parameters = parameters ?? new Dictionary<string, object?>() } });

    public static string Anchor(string prefix, string domain) => $"{prefix}:{SystemMailbox}@{domain}";

    public async Task<IReadOnlyList<JsonElement>> InvokeAsync(string cmdlet, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
    {
        var token = await getToken(ct);
        var tid = TenantIdFromJwt(token);
        if (tid == "") throw new InvalidOperationException("Token nie zawiera identyfikatora dzierżawy.");
        var body = BuildBody(cmdlet, parameters);
        var domains = new List<string>();
        if (_okDomain is not null) domains.Add(_okDomain);
        domains.AddRange(new[] { organization, tid }.Where(d => !string.IsNullOrWhiteSpace(d)));
        var prefixes = new List<string>();
        if (_okPrefix is not null) prefixes.Add(_okPrefix);
        prefixes.AddRange(new[] { "APP", "UPN" });
        string? last = null;
        foreach (var dom in domains.Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (var pfx in prefixes.Distinct())
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, $"https://outlook.office365.com/adminapi/beta/{tid}/InvokeCommand")
                { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                req.Headers.Accept.ParseAdd("application/json;odata.metadata=minimal");
                req.Headers.TryAddWithoutValidation("X-ResponseFormat", "json");
                req.Headers.TryAddWithoutValidation("X-CmdletName", cmdlet);
                req.Headers.TryAddWithoutValidation("X-ClientApplication", "ExoManagementModule");
                req.Headers.TryAddWithoutValidation("X-AnchorMailbox", Anchor(pfx, dom));
                using var res = await http.SendAsync(req, ct);
                var text = await res.Content.ReadAsStringAsync(ct);
                if ((res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) && !LooksLikeJson(text))
                { last = $"HTTP {(int)res.StatusCode} bez treści (kotwica {pfx}@{dom})"; continue; }
                JsonDocument doc;
                try { doc = JsonDocument.Parse(text); } catch (JsonException) { throw new InvalidOperationException($"{cmdlet}: odpowiedź HTTP {(int)res.StatusCode} nie jest JSON-em."); }
                using (doc)
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("error", out var err))
                    {
                        var msg = err.TryGetProperty("message", out var m) ? m.ToString() : err.ToString();
                        throw new InvalidOperationException($"{cmdlet}: {msg}");
                    }
                    if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"{cmdlet}: HTTP {(int)res.StatusCode}");
                    _okDomain = dom; _okPrefix = pfx;
                    if (root.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Array) return v.EnumerateArray().Select(e => e.Clone()).ToList();
                    return [root.Clone()];
                }
            }
        throw new InvalidOperationException((last ?? "Brak odpowiedzi z Exchange") + ". Sprawdź, czy aplikacja ma rolę Exchange w tokenie (Exchange Administrator lub grupa ról) i czy upłynęło 15 do 30 minut od jej nadania.");
    }

    private static bool LooksLikeJson(string text) { var t = text.TrimStart(); return t.StartsWith('{') || t.StartsWith('['); }
}
