using System.Text.RegularExpressions;

namespace M365Samoobsluga.Core.Validation;

/// <summary>Walidacja aliasów i adresów przekierowania (czysta logika, bez zależności od M365).</summary>
public static class AddressValidator
{
    private static readonly Regex LocalPart = new(@"^[a-z0-9]+(?:[._-][a-z0-9]+)*$", RegexOptions.Compiled);
    /// <summary>Nazwy ról i kont systemowych, których nie wolno przypisać jako alias (przejęcie skrzynki kontaktowej).</summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    { "admin", "administrator", "administrators", "postmaster", "abuse", "hostmaster", "webmaster", "root", "security", "noreply", "no-reply", "mailer-daemon", "info", "kontakt", "biuro", "zarzad", "rodo", "iod" };

    private static readonly Regex Domain = new(@"^(?=.{1,253}$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$", RegexOptions.Compiled);

    /// <summary>Jedyna domena, w której wolno tworzyć aliasy. Na sztywno, bez ustawień i bez zależności od domen tenanta.</summary>
    public const string AliasDomain = "feer.org.pl";

    /// <summary>
    /// Alias: wpisana część lokalna (przed @) + domena, która musi być <see cref="AliasDomain"/>.
    /// Zwraca znormalizowany adres małymi literami.
    /// </summary>
    public static ValidationResult ValidateAlias(string? localPart, string? domain,
        IReadOnlyCollection<string> existingAddresses)
    {
        var lp = (localPart ?? "").Trim().ToLowerInvariant();
        var dom = (domain ?? "").Trim().ToLowerInvariant();

        if (lp.Length == 0) return ValidationResult.Fail("Wpisz nazwę aliasu (część przed znakiem @).");
        if (lp.Contains('@')) return ValidationResult.Fail("Wpisz tylko część przed znakiem @; domena jest stała.");
        if (lp.Length > 64) return ValidationResult.Fail("Nazwa aliasu może mieć najwyżej 64 znaki.");
        if (!LocalPart.IsMatch(lp))
            return ValidationResult.Fail("Nazwa aliasu może zawierać tylko litery a-z bez polskich znaków, cyfry oraz pojedyncze kropki, myślniki lub podkreślniki w środku.");
        if (Reserved.Contains(lp)) return ValidationResult.Fail("Ta nazwa jest zarezerwowana. Wybierz inną.");
        if (dom != AliasDomain) return ValidationResult.Fail($"Aliasy można tworzyć tylko w domenie {AliasDomain}.");

        var address = $"{lp}@{dom}";
        if (existingAddresses.Contains(address, StringComparer.OrdinalIgnoreCase))
            return ValidationResult.Fail("Ten adres jest już przypisany do Twojej skrzynki.");
        return ValidationResult.Ok(address);
    }

    /// <summary>Adres docelowy przekierowania. <paramref name="ownAddresses"/> chroni przed pętlą (przekierowanie na siebie).</summary>
    public static ValidationResult ValidateForwardingAddress(string? address, IReadOnlyCollection<string> ownAddresses,
        IReadOnlyCollection<string>? blockedDomains = null)
    {
        var a = (address ?? "").Trim().ToLowerInvariant();
        if (a.Length == 0) return ValidationResult.Fail("Wpisz adres e-mail, na który mają trafiać wiadomości.");
        if (a.Length > 254) return ValidationResult.Fail("Adres e-mail jest za długi.");

        var at = a.IndexOf('@');
        if (at <= 0 || at != a.LastIndexOf('@'))
            return ValidationResult.Fail("Adres e-mail musi mieć dokładnie jeden znak @, na przykład imie@example.org.");
        var lp = a[..at];
        var dom = a[(at + 1)..];
        if (lp.Length > 64 || lp.StartsWith('.') || lp.EndsWith('.') || lp.Contains("..") || lp.Any(c => char.IsWhiteSpace(c) || c is '<' or '>' or ',' or ';' or '"'))
            return ValidationResult.Fail("Część adresu przed znakiem @ zawiera niedozwolone znaki.");
        if (!Domain.IsMatch(dom)) return ValidationResult.Fail("Domena adresu e-mail jest nieprawidłowa.");
        if (ownAddresses.Contains(a, StringComparer.OrdinalIgnoreCase))
            return ValidationResult.Fail("Nie można przekierować poczty na własny adres, bo wiadomości krążyłyby w pętli.");
        if (blockedDomains?.Contains(dom, StringComparer.OrdinalIgnoreCase) == true)
            return ValidationResult.Fail("Przekierowanie na tę domenę jest zablokowane przez zasady organizacji.");
        return ValidationResult.Ok(a);
    }
}
