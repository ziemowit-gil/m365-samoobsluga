namespace M365Samoobsluga.Web.Components.Layout;

public sealed record RibbonItem(string Href, string Label, string Icon, string Description);
public sealed record RibbonGroup(string Title, IReadOnlyList<RibbonItem> Items);
public sealed record RibbonTab(string Id, string Title, bool AdminOnly, IReadOnlyList<RibbonGroup> Groups);

/// <summary>Układ wstążki: karty, grupy i przyciski. Adresy są względne (jak w NavLink), pusty adres to strona startowa.</summary>
public static class RibbonDefinition
{
    public static readonly IReadOnlyList<RibbonTab> Tabs =
    [
        new("konto", "Moje konto", false,
        [
            new("Konto", [
                new("", "Start", "home", "Strona startowa"),
                new("haslo", "Hasło", "key", "Zmiana hasła do konta"),
                new("2fa", "Weryfikacja 2FA", "shield", "Metody potwierdzania logowania"),
            ]),
            new("Adresy", [new("aliasy", "Aliasy", "at", "Dodatkowe adresy e-mail")]),
        ]),
        new("poczta", "Poczta", false,
        [
            new("Wiadomości", [new("stopka", "Stopka", "pen", "Stanowisko, telefon i podgląd stopki")]),
            new("Automatyzacja", [
                new("autoresponder", "Autoresponder", "reply", "Odpowiedź poza biurem"),
                new("przekierowanie", "Przekierowanie", "forward", "Przekazywanie poczty na inny adres"),
            ]),
        ]),
        new("administrator", "Administrator", true,
        [
            new("Stopka", [
                new("admin/stopka", "Organizacja", "building", "Dane i wygląd stopki organizacji"),
                new("admin/stopka-outlook", "W Outlooku", "outlook", "Reguła stopki w Exchange"),
                new("admin/mailing", "Powiadomienie", "megaphone", "Wiadomość do użytkowników o stopce"),
            ]),
            new("Połączenia", [
                new("admin/exchange", "Exchange", "plug", "Połączenie aplikacji z Exchange"),
                new("admin/azure", "Azure", "cloud", "Identyfikatory i uprawnienia aplikacji w Entra"),
            ]),
        ]),
    ];

    /// <summary>Karta, która ma być otwarta dla bieżącej strony (ścieżka względna). Domyślnie pierwsza.</summary>
    public static string ActiveTabId(string? relativePath, bool isAdmin)
    {
        var path = (relativePath ?? "").Split('?', '#')[0].Trim('/').ToLowerInvariant();
        foreach (var tab in Tabs.Where(t => isAdmin || !t.AdminOnly))
            if (tab.Groups.SelectMany(g => g.Items).Any(i => i.Href == path)) return tab.Id;
        return Tabs[0].Id;
    }

    /// <summary>Znaczniki ikon SVG (stroke, 24x24). Zwraca wyłącznie wbudowane, stałe ścieżki.</summary>
    public static string IconPaths(string key) => key switch
    {
        "home" => "<path d=\"M3 11l9-8 9 8\"/><path d=\"M5 10v10h14V10\"/><path d=\"M10 20v-6h4v6\"/>",
        "key" => "<circle cx=\"8\" cy=\"15\" r=\"4\"/><path d=\"M10.8 12.2L20 3\"/><path d=\"M16 7l3 3\"/><path d=\"M14 9l2 2\"/>",
        "shield" => "<path d=\"M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6z\"/><path d=\"M9 12l2 2 4-4\"/>",
        "at" => "<circle cx=\"12\" cy=\"12\" r=\"4\"/><path d=\"M16 8v5a3 3 0 0 0 6 0v-1a10 10 0 1 0-4 8\"/>",
        "pen" => "<path d=\"M4 21h16\"/><path d=\"M5 17l10-10 3 3-10 10H5z\"/>",
        "reply" => "<rect x=\"3\" y=\"5\" width=\"18\" height=\"14\" rx=\"2\"/><path d=\"M3 7l9 6 9-6\"/>",
        "forward" => "<path d=\"M4 12h14\"/><path d=\"M13 6l6 6-6 6\"/>",
        "building" => "<path d=\"M5 21V4l8-2v19\"/><path d=\"M13 9h6v12\"/><path d=\"M9 8h1M9 12h1M9 16h1M16 13h1M16 17h1\"/>",
        "outlook" => "<rect x=\"3\" y=\"5\" width=\"18\" height=\"14\" rx=\"2\"/><path d=\"M12 15V9\"/><path d=\"M9 12l3-3 3 3\"/>",
        "megaphone" => "<path d=\"M3 11v2a1 1 0 0 0 1 1h3l8 4V6L7 10H4a1 1 0 0 0-1 1z\"/><path d=\"M19 9a4 4 0 0 1 0 6\"/>",
        "plug" => "<path d=\"M9 3v5M15 3v5\"/><path d=\"M6 8h12v3a6 6 0 0 1-12 0z\"/><path d=\"M12 17v4\"/>",
        "cloud" => "<path d=\"M7 18a4 4 0 1 1 .9-7.9A6 6 0 0 1 19 12a3 3 0 0 1-1 6z\"/>",
        _ => "<circle cx=\"12\" cy=\"12\" r=\"8\"/>",
    };
}
