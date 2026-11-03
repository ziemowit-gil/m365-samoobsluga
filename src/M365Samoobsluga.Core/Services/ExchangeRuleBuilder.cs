using M365Samoobsluga.Core.Models;

namespace M365Samoobsluga.Core.Services;

public enum ExchangeRuleMode { Audit, Enforce }

/// <summary>
/// Reguła transportowa Exchange Online, która dopisuje stopkę do wiadomości wychodzących (działa dla każdego Outlooka:
/// komputer, przeglądarka, telefon). Szablon używa tokenów Exchange (%%DisplayName%% itd.) zamiast danych osoby.
/// Parametry odpowiadają regule z modułu stopek SZO: dopisanie na końcu, wiadomości od użytkowników organizacji, znacznik
/// w nagłówku chroniący przed drugą stopką w wątku, tryb Audit na start.
/// </summary>
public static class ExchangeRuleBuilder
{
    public const string RuleName = "Samoobsługa FEER: stopka e-mail";
    public const string MarkHeader = "X-Samoobsluga-Stopka";
    /// <summary>Limit długości tekstu stopki w regule (ApplyHtmlDisclaimerText), tak jak w SZO.</summary>
    public const int MaxChars = 5000;

    public static readonly SignaturePerson Tokens = new("%%DisplayName%%", "%%Title%%", "%%MobilePhone%%", "%%WindowsEmailAddress%%");

    public static string BuildTemplateHtml(SignatureOrg org, SignatureDesign design) => SignatureRenderer.RenderHtml(org, design, Tokens);

    /// <summary>Podgląd: tokeny zastąpione danymi przykładowej osoby.</summary>
    public static string Substitute(string html, SignaturePerson p) => html
        .Replace("%%DisplayName%%", System.Net.WebUtility.HtmlEncode(p.Name))
        .Replace("%%Title%%", System.Net.WebUtility.HtmlEncode(p.Title))
        .Replace("%%MobilePhone%%", System.Net.WebUtility.HtmlEncode(p.Phone))
        .Replace("%%WindowsEmailAddress%%", System.Net.WebUtility.HtmlEncode(p.Email));

    public static string? Validate(string html) =>
        html.Length > MaxChars ? $"Szablon ma {html.Length} znaków, a Exchange dopuszcza {MaxChars}. Skróć treść promocyjną albo ukryj część pól." : null;

    /// <summary>Skrypt PowerShell tworzący lub aktualizujący regułę. HTML trafia do here-stringa w apostrofach (bez rozwijania zmiennych).</summary>
    public static string BuildScript(string html, ExchangeRuleMode mode)
    {
        if (html.Contains("\n'@") || html.StartsWith("'@")) throw new ArgumentException("Szablon zawiera niedozwoloną sekwencję.", nameof(html));
        return $$"""
        # Skrypt wygenerowany przez Samoobsługę poczty Microsoft 365.
        # Wymaga modułu ExchangeOnlineManagement i uprawnień Exchange do reguł transportowych (rola Transport Rules).
        # Połącz się najpierw: Connect-ExchangeOnline -UserPrincipalName admin@twoja-domena.pl
        # Reguła startuje w trybie {{mode}}. W trybie Audit nic nie zmienia w wiadomościach, tylko zapisuje wyniki w dzienniku.

        $name = '{{RuleName}}'
        $html = @'
        {{html}}
        '@

        $params = @{
            ApplyHtmlDisclaimerLocation        = 'Append'
            ApplyHtmlDisclaimerText            = $html
            ApplyHtmlDisclaimerFallbackAction  = 'Wrap'
            FromScope                          = 'InOrganization'
            SetHeaderName                      = '{{MarkHeader}}'
            SetHeaderValue                     = '1'
            ExceptIfHeaderMatchesMessageHeader = '{{MarkHeader}}'
            ExceptIfHeaderMatchesPatterns      = '1'
            Mode                               = '{{mode}}'
            Comments                           = 'Zarządzane przez Samoobsługę poczty. Nie edytuj ręcznie.'
        }

        if (Get-TransportRule -Identity $name -ErrorAction SilentlyContinue) {
            Set-TransportRule -Identity $name @params
        } else {
            New-TransportRule -Name $name -Enabled $true @params
        }
        """.Replace("\r\n", "\n");
    }

    public static string BuildRemoveScript() => $"Remove-TransportRule -Identity '{RuleName}' -Confirm:$false";
}
