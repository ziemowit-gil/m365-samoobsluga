using System.Net;
using M365Samoobsluga.Core.Models;

namespace M365Samoobsluga.Core.Services;

/// <summary>Mapowanie dat i składanie treści autorespondera (czysta logika).</summary>
public static class AutoReplyComposer
{
    public const int MaxMessageLength = 4000;

    /// <summary>Konwersja czasu lokalnego w danej strefie na UTC. Obsługuje strefy IANA i Windows.</summary>
    public static DateTime ToUtc(DateTime local, string timeZoneId)
    {
        var tz = FindTimeZone(timeZoneId);
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), tz);
    }

    public static DateTime FromUtc(DateTime utc, string timeZoneId)
    {
        var tz = FindTimeZone(timeZoneId);
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz);
    }

    public static TimeZoneInfo FindTimeZone(string id)
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var tz)) return tz;
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana) && TimeZoneInfo.TryFindSystemTimeZoneById(iana, out tz)) return tz;
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var win) && TimeZoneInfo.TryFindSystemTimeZoneById(win, out tz)) return tz;
        throw new ArgumentException($"Nieznana strefa czasowa: {id}.", nameof(id));
    }

    /// <summary>Walidacja ustawień. Zwraca listę błędów po polsku (pusta = poprawne).</summary>
    public static IReadOnlyList<string> Validate(AutoReplySettings s, DateTime nowUtc)
    {
        var errors = new List<string>();
        if (!s.Enabled) return errors;

        if (string.IsNullOrWhiteSpace(s.InternalMessage))
            errors.Add("Wpisz treść odpowiedzi dla odbiorców wewnątrz organizacji.");
        if (s.ExternalAudience != AutoReplyAudience.None && string.IsNullOrWhiteSpace(s.ExternalMessage))
            errors.Add("Wpisz treść odpowiedzi dla odbiorców spoza organizacji albo wyłącz odpowiedzi zewnętrzne.");
        if (s.InternalMessage.Length > MaxMessageLength || s.ExternalMessage.Length > MaxMessageLength)
            errors.Add($"Treść odpowiedzi może mieć najwyżej {MaxMessageLength} znaków.");

        if ((s.StartLocal is null) != (s.EndLocal is null))
            errors.Add("Podaj obie daty (od i do) albo żadnej.");
        else if (s.StartLocal is { } st && s.EndLocal is { } en)
        {
            try
            {
                var su = ToUtc(st, s.TimeZoneId);
                var eu = ToUtc(en, s.TimeZoneId);
                if (eu <= su) errors.Add("Data zakończenia musi być późniejsza niż data rozpoczęcia.");
                else if (eu <= nowUtc) errors.Add("Data zakończenia musi być w przyszłości.");
            }
            catch (ArgumentException) { errors.Add("Nieprawidłowa strefa czasowa."); }
        }
        return errors;
    }

    /// <summary>Zamienia zwykły tekst na bezpieczny HTML (akapity i &lt;br&gt;), ze znakami specjalnymi zakodowanymi.</summary>
    public static string TextToHtml(string text)
    {
        var paragraphs = text.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        var body = string.Join("", paragraphs.Select(p => "<p>" + WebUtility.HtmlEncode(p.Trim()).Replace("\n", "<br>") + "</p>"));
        return "<html><body>" + body + "</body></html>";
    }

    /// <summary>Prosta konwersja HTML (z Graph) na tekst do edycji w polu formularza.</summary>
    public static string HtmlToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var t = System.Text.RegularExpressions.Regex.Replace(html, @"</p\s*>", "\n\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        t = System.Text.RegularExpressions.Regex.Replace(t, @"<\s*br\s*/?>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        t = System.Text.RegularExpressions.Regex.Replace(t, "<[^>]+>", "");
        t = WebUtility.HtmlDecode(t);
        return System.Text.RegularExpressions.Regex.Replace(t.Trim(), @"\n{3,}", "\n\n");
    }

    /// <summary>Składa gotowe pola do Graph: tryb Scheduled przy podanych datach, Always przy braku dat, Disabled gdy wyłączony.</summary>
    public static ComposedAutoReply Compose(AutoReplySettings s)
    {
        if (!s.Enabled)
            return new ComposedAutoReply("disabled", null, null, "none", TextToHtml(s.InternalMessage), TextToHtml(s.ExternalMessage));

        var scheduled = s.StartLocal is not null && s.EndLocal is not null;
        return new ComposedAutoReply(
            scheduled ? "scheduled" : "alwaysEnabled",
            scheduled ? ToUtc(s.StartLocal!.Value, s.TimeZoneId) : null,
            scheduled ? ToUtc(s.EndLocal!.Value, s.TimeZoneId) : null,
            s.ExternalAudience switch { AutoReplyAudience.All => "all", AutoReplyAudience.ContactsOnly => "contactsOnly", _ => "none" },
            TextToHtml(s.InternalMessage),
            TextToHtml(s.ExternalMessage));
    }
}

/// <summary>Wartości zgodne ze słownikiem Graph (status: disabled/alwaysEnabled/scheduled; externalAudience: none/contactsOnly/all). Daty w UTC.</summary>
public sealed record ComposedAutoReply(string Status, DateTime? StartUtc, DateTime? EndUtc, string ExternalAudience, string InternalHtml, string ExternalHtml);
