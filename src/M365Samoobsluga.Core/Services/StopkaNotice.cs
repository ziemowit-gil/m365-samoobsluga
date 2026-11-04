using System.Net;
using System.Text.RegularExpressions;
using M365Samoobsluga.Core.Models;

namespace M365Samoobsluga.Core.Services;

public interface INoticeMailer
{
    /// <summary>Wysyła jedną wiadomość HTML w imieniu zalogowanej osoby (delegowane Mail.Send, bez zapisu w elementach wysłanych).</summary>
    Task SendAsync(string to, string subject, string html, CancellationToken ct = default);
}

public sealed record ParsedRecipients(IReadOnlyList<string> Valid, IReadOnlyList<string> Invalid, int Duplicates);

/// <summary>Powiadomienie dla użytkowników, że ustawiono im jednolitą stopkę. Czysta logika: treść, podgląd, lista odbiorców.</summary>
public static partial class StopkaNotice
{
    public const int MaxRecipients = 200;
    public const string LinkToken = "{link}";
    public const string DefaultSubject = "Ustawiliśmy jednolitą stopkę w Twojej poczcie";
    public const string DefaultBody =
        "Dzień dobry,\n\n" +
        "do wiadomości, które wysyłasz z konta w naszej organizacji, dopisuje się teraz jednolita stopka fundacji. Imię i nazwisko, stanowisko i telefon program bierze z Twojego profilu w Microsoft 365.\n\n" +
        "Co zrobić: sprawdź, czy stanowisko i numer telefonu w profilu są aktualne. Jeśli w Outlooku masz własny podpis, usuń go, żeby stopka się nie powtarzała. Swoje dane i podgląd stopki zobaczysz tutaj: {link}\n\n" +
        "Tak wygląda stopka:";

    public const string SelfSubject = "Zapisano dane do Twojej stopki";

    /// <summary>Potwierdzenie dla osoby, która sama zapisała swoje dane do stopki: co zapisała i jak wygląda jej stopka.</summary>
    public static (string Subject, string Html) BuildSelfConfirmation(SignaturePerson person, string appLink, SignatureTemplate template)
    {
        var body = "Dzień dobry,\n\nzapisałeś dane do swojej stopki. Stanowisko: " + (person.Title == "" ? "brak" : person.Title)
                 + ". Telefon: " + (person.Phone == "" ? "brak" : person.Phone) + ".\n\nZmienisz je na stronie " + LinkToken + "\n\nTak wygląda Twoja stopka:";
        return (SelfSubject, BuildHtml(body, appLink, template, person));
    }

    [GeneratedRegex(@"^[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,63}$")] private static partial Regex EmailRx();

    public static bool IsValidEmail(string? v) => v is { Length: <= 254 } s && EmailRx().IsMatch(s.Trim());

    /// <summary>Dzieli tekst po przecinkach, średnikach i końcach linii. Odrzuca niepoprawne adresy i usuwa duplikaty (bez względu na wielkość liter).</summary>
    public static ParsedRecipients ParseRecipients(string? text)
    {
        var valid = new List<string>(); var invalid = new List<string>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var dup = 0;
        foreach (var raw in Regex.Split(text ?? "", @"[,;\r\n\t ]+"))
        {
            var t = raw.Trim().Trim('<', '>');
            if (t.Length == 0) continue;
            if (!IsValidEmail(t)) { invalid.Add(t); continue; }
            if (!seen.Add(t)) { dup++; continue; }
            valid.Add(t.ToLowerInvariant());
        }
        return new ParsedRecipients(valid, invalid, dup);
    }

    public static string? Validate(string subject, string body, ParsedRecipients r)
    {
        if (string.IsNullOrWhiteSpace(subject)) return "Wpisz temat wiadomości.";
        if (subject.Length > 200 || subject.Any(c => c is '\r' or '\n')) return "Temat może mieć najwyżej 200 znaków, w jednej linii.";
        if (string.IsNullOrWhiteSpace(body)) return "Wpisz treść wiadomości.";
        if (body.Length > 5000) return "Treść może mieć najwyżej 5000 znaków.";
        if (r.Invalid.Count > 0) return $"Niepoprawne adresy: {string.Join(", ", r.Invalid.Take(5))}{(r.Invalid.Count > 5 ? "…" : "")}.";
        if (r.Valid.Count == 0) return "Dodaj przynajmniej jednego odbiorcę.";
        if (r.Valid.Count > MaxRecipients) return $"Jednorazowo można wysłać najwyżej do {MaxRecipients} osób.";
        return null;
    }

    /// <summary>Składa wiadomość HTML. Tekst administratora jest kodowany, {link} zamieniany na adres aplikacji, a pod nim wstawiany przykład stopki.</summary>
    public static string BuildHtml(string body, string appLink, SignatureTemplate template, SignaturePerson? person = null)
    {
        var encodedLink = WebUtility.HtmlEncode(appLink);
        var paragraphs = body.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(p => WebUtility.HtmlEncode(p.Trim()).Replace("\n", "<br>").Replace(WebUtility.HtmlEncode(LinkToken), $"<a href=\"{encodedLink}\">{encodedLink}</a>"));
        var sample = person ?? new SignaturePerson("Imię Nazwisko", "Stanowisko", "+48 600 000 000", "twoj.adres@example.org");
        var signature = SignatureRenderer.RenderHtml(template.Org, template.Design, sample);
        return "<html><body style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:1.6;color:#1d1d1a;\">"
             + string.Concat(paragraphs.Select(p => "<p>" + p + "</p>"))
             + "<div style=\"border-left:4px solid #1d68f2;padding:4px 0 4px 14px;margin:8px 0;\">" + signature + "</div>"
             + "</body></html>";
    }
}
