using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Ganss.Xss;
using M365Samoobsluga.Core.Models;

namespace M365Samoobsluga.Core.Services;

/// <summary>
/// Jedyny generator stopki (HTML i tekst), odpowiednik stopkaRenderHtml z modułu stopek SZO:
/// ten sam układ, te same pola, te same zasady oczyszczania wyglądu.
/// </summary>
public static partial class SignatureRenderer
{
    public static readonly string[] Fonts = ["Arial", "Calibri", "Verdana", "Tahoma", "Georgia", "Trebuchet MS", "Segoe UI"];
    private static readonly string[] SepStyles = ["solid", "dashed", "dotted", "none"];

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")] private static partial Regex HexRx();
    [GeneratedRegex(@"^https://[^\s""'<>]+$", RegexOptions.IgnoreCase)] private static partial Regex HttpsRx();
    [GeneratedRegex(@"^[0-9+()\-\s.]{3,30}$")] private static partial Regex PhoneRx();

    private static int Clamp(int v, int lo, int hi) => Math.Max(lo, Math.Min(hi, v));
    private static string Hex(string v, string def) => HexRx().IsMatch(v ?? "") ? v.ToLowerInvariant() : def;

    /// <summary>Przycina wartości do dozwolonych zakresów (konfiguracja może zawierać cokolwiek).</summary>
    public static SignatureDesign Sanitize(SignatureDesign i)
    {
        var d = new SignatureDesign();
        return new SignatureDesign
        {
            Font = Fonts.Contains(i.Font) ? i.Font : d.Font,
            Size = Clamp(i.Size, 11, 16),
            NameSize = Clamp(i.NameSize, 13, 24),
            Color = Hex(i.Color, d.Color),
            Accent = Hex(i.Accent, d.Accent),
            SepColor = Hex(i.SepColor, d.SepColor),
            NameAccent = i.NameAccent,
            SepStyle = SepStyles.Contains(i.SepStyle) ? i.SepStyle : d.SepStyle,
            Gap = Clamp(i.Gap, 0, 24),
            LogoWidth = Clamp(i.LogoWidth, 40, 300),
            LogoUrl = HttpsRx().IsMatch(i.LogoUrl?.Trim() ?? "") ? i.LogoUrl!.Trim() : "",
            ShowTitle = i.ShowTitle, ShowPhone = i.ShowPhone, ShowEmail = i.ShowEmail, ShowOrg = i.ShowOrg, ShowAddress = i.ShowAddress,
            ShowNip = i.ShowNip, ShowKrs = i.ShowKrs, ShowWebsite = i.ShowWebsite, ShowPromo = i.ShowPromo,
        };
    }

    /// <summary>Lista dozwolonych znaczników treści promocyjnej, jak w SZO (a, b, strong, i, em, u, br, p, span, img).</summary>
    public static string SanitizePromo(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var s = new HtmlSanitizer();
        s.AllowedTags.Clear();
        foreach (var t in new[] { "a", "b", "strong", "i", "em", "u", "br", "p", "span", "img" }) s.AllowedTags.Add(t);
        s.AllowedAttributes.Clear();
        foreach (var a in new[] { "href", "src", "alt", "width", "height" }) s.AllowedAttributes.Add(a);
        s.AllowedSchemes.Clear();
        foreach (var sc in new[] { "http", "https", "mailto" }) s.AllowedSchemes.Add(sc);
        s.PostProcessNode += (_, e) =>
        {
            if (e.Node is AngleSharp.Dom.IElement el && el.TagName.Equals("A", StringComparison.OrdinalIgnoreCase))
            { el.SetAttribute("target", "_blank"); el.SetAttribute("rel", "noopener"); }
        };
        return s.Sanitize(html).Trim();
    }

    /// <summary>Telefon: cyfry, plus, spacje, nawiasy, myślniki i kropki (od 3 do 30 znaków) albo pusty.</summary>
    public static string? ValidatePhone(string? phone) =>
        string.IsNullOrWhiteSpace(phone) || PhoneRx().IsMatch(phone.Trim()) ? null : "Numer telefonu może zawierać cyfry, spacje, plus, nawiasy, myślniki i kropki (od 3 do 30 znaków).";

    public static string? ValidateTitle(string? title) =>
        (title ?? "").Trim().Length <= 100 && !(title ?? "").Any(char.IsControl) ? null : "Stanowisko może mieć najwyżej 100 znaków i nie może zawierać znaków sterujących.";

    /// <summary>Walidacja danych organizacji z formularza administratora. Zwraca błędy po polsku (pusta lista = poprawne).</summary>
    public static IReadOnlyList<string> ValidateOrg(SignatureOrg o)
    {
        var e = new List<string>();
        if (string.IsNullOrWhiteSpace(o.OrgName)) e.Add("Wpisz nazwę organizacji.");
        if ((o.OrgName ?? "").Length > 150) e.Add("Nazwa organizacji może mieć najwyżej 150 znaków.");
        if ((o.Address ?? "").Length > 200) e.Add("Adres może mieć najwyżej 200 znaków.");
        if (!Regex.IsMatch(o.Nip ?? "", @"^[0-9\- ]{0,20}$")) e.Add("NIP może zawierać tylko cyfry, spacje i myślniki (do 20 znaków).");
        if (!Regex.IsMatch(o.Krs ?? "", @"^[0-9 ]{0,20}$")) e.Add("KRS może zawierać tylko cyfry i spacje (do 20 znaków).");
        var w = (o.Website ?? "").Trim();
        if (w.Length > 200 || Regex.IsMatch(w, @"[\s""'<>]")) e.Add("Adres strony nie może zawierać spacji ani znaków cudzysłowu i nawiasów ostrych (najwyżej 200 znaków).");
        if ((o.PromoHtml ?? "").Length > 4000) e.Add("Treść promocyjna może mieć najwyżej 4000 znaków.");
        return e;
    }

    private static string E(string? v) => WebUtility.HtmlEncode(v ?? "");

    public static string RenderHtml(SignatureOrg org, SignatureDesign design, SignaturePerson p)
    {
        var d = Sanitize(design);
        var sm = d.Size - 1;
        static string Row(string t, string st) => t == "" ? "" : $"<div style=\"{st}\">{t}</div>";
        string Link(string href, string label) => $"<a href=\"{href}\" style=\"color:{d.Accent};text-decoration:none;\">{label}</a>";

        var h = new StringBuilder($"<div style=\"font-family:{d.Font},Arial,Helvetica,sans-serif;color:{d.Color};font-size:{d.Size}px;line-height:1.5;\">");
        if (d.LogoUrl != "")
            h.Append($"<div style=\"margin-bottom:{d.Gap}px;\"><img src=\"{E(d.LogoUrl)}\" width=\"{d.LogoWidth}\" alt=\"{E(org.OrgName)}\" style=\"display:block;border:0;\"></div>");
        h.Append(Row(E(p.Name), $"font-size:{d.NameSize}px;font-weight:bold;line-height:1.4;color:{(d.NameAccent ? d.Accent : d.Color)};"));
        if (d.ShowTitle) h.Append(Row(E(p.Title), $"font-size:{d.Size}px;"));
        var parts = new List<string>();
        if (d.ShowPhone && p.Phone != "") parts.Add("tel. " + E(p.Phone));
        if (d.ShowEmail && p.Email != "") parts.Add(Link("mailto:" + E(p.Email), E(p.Email)));
        h.Append(Row(string.Join(" &nbsp;|&nbsp; ", parts), $"font-size:{d.Size}px;"));
        if (d.ShowOrg) h.Append(Row(E(org.OrgName), $"margin-top:{d.Gap}px;font-size:{d.Size}px;font-weight:bold;"));
        if (d.ShowAddress) h.Append(Row(E(org.Address), $"font-size:{sm}px;"));
        if (d.ShowNip && org.Nip.Trim() != "") h.Append(Row("NIP: " + E(org.Nip), $"font-size:{sm}px;"));
        if (d.ShowKrs && org.Krs.Trim() != "") h.Append(Row("KRS: " + E(org.Krs), $"font-size:{sm}px;"));
        var url = org.Website.Trim();
        if (d.ShowWebsite && url != "")
        {
            var href = Regex.IsMatch(url, "^https?://", RegexOptions.IgnoreCase) ? url : "https://" + url;
            h.Append(Row(Link(E(href), E(url)), $"font-size:{sm}px;"));
        }
        var promo = SanitizePromo(org.PromoHtml);
        if (d.ShowPromo && promo != "")
        {
            var bd = d.SepStyle == "none" ? "" : $"border-top:1px {d.SepStyle} {d.SepColor};padding-top:8px;";
            h.Append($"<div style=\"margin-top:{d.Gap + 2}px;{bd}font-size:{sm}px;line-height:1.5;\">{promo}</div>");
        }
        return h.Append("</div>").ToString();
    }

    /// <summary>Wersja tekstowa (do wiadomości w zwykłym tekście).</summary>
    public static string RenderText(SignatureOrg org, SignatureDesign design, SignaturePerson p)
    {
        var d = Sanitize(design);
        var l = new List<string>();
        void Add(bool on, string? v) { if (on && !string.IsNullOrWhiteSpace(v)) l.Add(v.Trim()); }
        Add(true, p.Name);
        Add(d.ShowTitle, p.Title);
        var contact = new List<string>();
        if (d.ShowPhone && p.Phone != "") contact.Add("tel. " + p.Phone);
        if (d.ShowEmail && p.Email != "") contact.Add(p.Email);
        if (contact.Count > 0) l.Add(string.Join(" | ", contact));
        l.Add("");
        Add(d.ShowOrg, org.OrgName);
        Add(d.ShowAddress, org.Address);
        if (d.ShowNip && org.Nip.Trim() != "") l.Add("NIP: " + org.Nip.Trim());
        if (d.ShowKrs && org.Krs.Trim() != "") l.Add("KRS: " + org.Krs.Trim());
        Add(d.ShowWebsite, org.Website);
        if (d.ShowPromo && SanitizePromo(org.PromoHtml) != "") { l.Add(""); l.Add(AutoReplyComposer.HtmlToText(SanitizePromo(org.PromoHtml))); }
        return string.Join("\n", l).Trim();
    }
}
