using M365Samoobsluga.Core.Services;

namespace M365Samoobsluga.Tests;

public class AzureSetupTests
{
    [Theory]
    [InlineData("11111111-2222-3333-4444-555555555555", true)]
    [InlineData(" 11111111-2222-3333-4444-555555555555 ", true)]
    [InlineData("firma.onmicrosoft.com", true)]
    [InlineData("feer.org.pl", true)]
    [InlineData("common", false)]
    [InlineData("organizations", false)]
    [InlineData("", false)]
    [InlineData("123", false)]
    [InlineData("a b.com", false)]
    public void Tenant_id(string v, bool ok) => Assert.Equal(ok, AzureSetup.ValidateTenantId(v) is null);

    [Theory]
    [InlineData("11111111-2222-3333-4444-555555555555", true)]
    [InlineData("firma.onmicrosoft.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Client_id(string? v, bool ok) => Assert.Equal(ok, AzureSetup.ValidateClientId(v) is null);

    [Fact]
    public void Konfiguracja_wymaga_obu_wartosci()
    {
        Assert.True(AzureSetup.IsConfigured("feer.org.pl", "11111111-2222-3333-4444-555555555555"));
        Assert.False(AzureSetup.IsConfigured("", "11111111-2222-3333-4444-555555555555"));
        Assert.False(AzureSetup.IsConfigured("feer.org.pl", ""));
    }

    [Theory]
    [InlineData("https://poczta.example.org/cokolwiek?x=1", "https://poczta.example.org/")]
    [InlineData("http://localhost:30375", "http://localhost:30375/")]
    [InlineData("HTTPS://Poczta.Example.org:8443/", "https://poczta.example.org:8443/")]
    public void Adres_aplikacji_jest_normalizowany(string input, string expected) => Assert.Equal(expected, AzureSetup.NormalizeBaseUrl(input).Url);

    [Theory]
    [InlineData("poczta.example.org")]
    [InlineData("http://poczta.example.org")]
    [InlineData("ftp://x.pl")]
    [InlineData("")]
    public void Zly_adres_aplikacji_odrzucony(string input) => Assert.NotNull(AzureSetup.NormalizeBaseUrl(input).Error);

    [Fact]
    public void Adresy_do_azure_dla_localhost()
    {
        var u = AzureSetup.RedirectUris("http://localhost:30375/");
        Assert.Equal("http://localhost:30375/signin-oidc", u[0].Value);
        Assert.Equal("http://localhost:30375/signout-callback-oidc", u[1].Value);
        Assert.Equal("http://localhost:30375/signout-oidc", u[2].Value);
    }

    [Fact]
    public void Lista_uprawnien_zgadza_sie_z_zakresami_w_konfiguracji()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "M365Samoobsluga.Web", "appsettings.json"));
        foreach (var p in AzureSetup.RequiredPermissions) Assert.Contains($"\"{p.Name}\"", json);
        Assert.Equal(4, AzureSetup.RequiredPermissions.Count);
    }

    [Theory]
    [InlineData("https://poczta.example.org", "http://localhost:30375/", "https://poczta.example.org/")]
    [InlineData("", "https://z-zadania.example.org/strona", "https://z-zadania.example.org/")]
    [InlineData(null, "http://localhost:30375/", "http://localhost:30375/")]
    [InlineData("http://zly-nie-localhost.pl", "https://z-zadania.example.org/", "https://z-zadania.example.org/")]
    [InlineData("nie adres", "https://z-zadania.example.org/", "https://z-zadania.example.org/")]
    public void Adres_aplikacji_z_konfiguracji_albo_z_zadania(string? configured, string request, string expected) =>
        Assert.Equal(expected, AzureSetup.ResolveAppUrl(configured, request));

    [Fact]
    public void Adresy_do_azure_dopisuja_ukosnik() =>
        Assert.Equal("https://p.example.org/signin-oidc", AzureSetup.RedirectUris("https://p.example.org")[0].Value);
}

public class ExchangeRuleBuilderTests
{
    private static readonly M365Samoobsluga.Core.Models.SignatureOrg Org = new() { OrgName = "FEER", Website = "feer.org.pl" };

    [Fact]
    public void Szablon_ma_tokeny_exchange_zamiast_danych_osoby()
    {
        var h = ExchangeRuleBuilder.BuildTemplateHtml(Org, new());
        Assert.Contains("%%DisplayName%%", h);
        Assert.Contains("%%Title%%", h);
        Assert.Contains("%%MobilePhone%%", h);
        Assert.Contains("mailto:%%WindowsEmailAddress%%", h);
    }

    [Fact]
    public void Szablon_exchange_zawiera_ogolny_numer_organizacji_jako_stala_tresc()
    {
        var h = ExchangeRuleBuilder.BuildTemplateHtml(new M365Samoobsluga.Core.Models.SignatureOrg { OrgName = "FEER", Phone = "+48 22 123 45 67" }, new());
        Assert.Contains("Tel. ogólny: +48 22 123 45 67", h);
        Assert.Contains("%%MobilePhone%%", h);
    }

    [Fact]
    public void Podglad_podstawia_dane_i_koduje_html()
    {
        var h = ExchangeRuleBuilder.Substitute(ExchangeRuleBuilder.BuildTemplateHtml(Org, new()), new("A <b>", "T", "1", "a@x.pl"));
        Assert.DoesNotContain("%%", h);
        Assert.Contains("A &lt;b&gt;", h);
    }

    [Fact]
    public void Limit_znakow_jest_pilnowany()
    {
        Assert.Null(ExchangeRuleBuilder.Validate(new string('a', 5000)));
        Assert.Contains("5000", ExchangeRuleBuilder.Validate(new string('a', 5001)));
    }

    [Theory]
    [InlineData(ExchangeRuleMode.Audit, "Mode                               = 'Audit'")]
    [InlineData(ExchangeRuleMode.Enforce, "Mode                               = 'Enforce'")]
    public void Skrypt_ma_tryb_i_kluczowe_parametry(ExchangeRuleMode mode, string expected)
    {
        var s = ExchangeRuleBuilder.BuildScript("<div>x</div>", mode);
        Assert.Contains(expected, s);
        Assert.Contains("ApplyHtmlDisclaimerLocation        = 'Append'", s);
        Assert.Contains("FromScope                          = 'InOrganization'", s);
        Assert.Contains("ExceptIfHeaderMatchesMessageHeader = 'X-Samoobsluga-Stopka'", s);
        Assert.Contains("FromAddressMatchesPatterns         = '" + ExchangeRuleBuilder.PersonalSenderPattern + "'", s);
        Assert.Contains("<div>x</div>", s);
        Assert.Contains("New-TransportRule", s);
        Assert.Contains("Set-TransportRule", s);
    }

    [Fact]
    public void Skrypt_odrzuca_niebezpieczny_szablon() =>
        Assert.Throws<ArgumentException>(() => ExchangeRuleBuilder.BuildScript("a\n'@\nRemove-Item x", ExchangeRuleMode.Audit));
}

public class LoginErrorsTests
{
    [Theory]
    [InlineData("AADSTS7000215: Invalid client secret provided", "AADSTS7000215", "nieprawidłowy")]
    [InlineData("... AADSTS65001: The user or administrator has not consented", "AADSTS65001", "zgody")]
    [InlineData("AADSTS50011: The redirect URI ... does not match", "AADSTS50011", "przekierowania")]
    [InlineData("AADSTS99999999 coś", "AADSTS9999999", "odrzucił")]
    public void Kody_aadsts_maja_czytelny_komunikat(string msg, string code, string fragment)
    {
        var d = LoginErrors.Describe(msg);
        Assert.Equal(code, d.Code);
        Assert.Contains(fragment, d.Title + d.Hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Brak_sekretu_jest_rozpoznany() =>
        Assert.Equal("BRAK_SEKRETU", LoginErrors.Describe("Microsoft.Identity.Client.MsalClientException: One client credential type required either: ClientSecret, Certificate").Code);

    [Fact]
    public void Blad_korelacji_to_sesja() => Assert.Equal("SESJA", LoginErrors.Describe("Correlation failed.").Code);

    [Fact]
    public void Nieznany_blad_ma_ogolny_komunikat() => Assert.Equal("NIEZNANY", LoginErrors.Describe("coś innego").Code);

    [Fact]
    public void Komunikat_nie_zawiera_tokenow_ani_tresci_bledu() =>
        Assert.DoesNotContain("eyJ", LoginErrors.Describe("AADSTS7000215 eyJhbGciOi.secret.token").Hint);
}

public class LoginAndRibbonTests
{
    [Theory]
    [InlineData("/stopka", "/stopka")]
    [InlineData("/admin/mailing?x=1", "/admin/mailing?x=1")]
    [InlineData("", "/")]
    [InlineData(null, "/")]
    [InlineData("https://zly.example.org/", "/")]
    [InlineData("//zly.example.org/", "/")]
    [InlineData("/\\zly.example.org", "/")]
    [InlineData("javascript:alert(1)", "/")]
    [InlineData("/a://b", "/")]
    public void Adres_powrotu_tylko_lokalny(string? input, string expected) => Assert.Equal(expected, AzureSetup.SafeReturnUrl(input));

    [Theory]
    [InlineData("", false, "konto")]
    [InlineData("haslo", false, "konto")]
    [InlineData("autoresponder", false, "poczta")]
    [InlineData("stopka", true, "poczta")]
    [InlineData("admin/exchange", true, "administrator")]
    [InlineData("admin/exchange", false, "konto")]
    [InlineData("nieznana", true, "konto")]
    public void Aktywna_karta_wynika_ze_strony(string path, bool admin, string expected) =>
        Assert.Equal(expected, M365Samoobsluga.Web.Components.Layout.RibbonDefinition.ActiveTabId(path, admin));

    [Fact]
    public void Kazdy_przycisk_wstazki_prowadzi_do_istniejacej_strony()
    {
        var routes = typeof(Program).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes(typeof(Microsoft.AspNetCore.Components.RouteAttribute), false).Cast<Microsoft.AspNetCore.Components.RouteAttribute>())
            .Select(r => r.Template.Trim('/').ToLowerInvariant()).ToHashSet();
        foreach (var item in M365Samoobsluga.Web.Components.Layout.RibbonDefinition.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Items))
            Assert.True(routes.Contains(item.Href), $"Brak strony dla przycisku „{item.Label}” ({item.Href}).");
    }

    [Fact]
    public void Etykiety_przyciskow_sa_unikalne_w_obrebie_karty()
    {
        foreach (var tab in M365Samoobsluga.Web.Components.Layout.RibbonDefinition.Tabs)
        {
            var labels = tab.Groups.SelectMany(g => g.Items).Select(i => i.Label).ToList();
            Assert.Equal(labels.Count, labels.Distinct().Count());
        }
    }
}

public class MetroTilesTests
{
    [Fact]
    public void Kazdy_kafelek_ma_opis_ikone_i_etykiete()
    {
        foreach (var i in M365Samoobsluga.Web.Components.Layout.RibbonDefinition.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Items))
        {
            Assert.False(string.IsNullOrWhiteSpace(i.Label));
            Assert.False(string.IsNullOrWhiteSpace(i.Description));
            Assert.DoesNotContain("<circle cx=\"12\" cy=\"12\" r=\"8\"/>", M365Samoobsluga.Web.Components.Layout.RibbonDefinition.IconPaths(i.Icon));
        }
    }

    [Theory]
    [InlineData("#000000", "#ffffff")]
    [InlineData("#3f3f3b", "#ffffff")]
    [InlineData("#000000", "#f3f3f3")]
    [InlineData("#ffffff", "#1d68f2")]
    [InlineData("#ffffff", "#1555c9")]
    [InlineData("#0b4fc4", "#e6edfb")]
    [InlineData("#3f3f3b", "#f3f3f3")]
    public void Kontrast_tekstu_na_kafelku_to_co_najmniej_4_5_do_1(string fg, string bg) =>
        Assert.True(Contrast(fg, bg) >= 4.5, $"{fg} na {bg}: {Contrast(fg, bg):F2}");

    private static double Lum(string hex)
    {
        double C(int s) { var v = Convert.ToInt32(hex.Substring(s, 2), 16) / 255.0; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        return 0.2126 * C(1) + 0.7152 * C(3) + 0.0722 * C(5);
    }
    private static double Contrast(string a, string b) { var (x, y) = (Lum(a), Lum(b)); return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05); }
}

public class PersonalSenderPatternTests
{
    private static bool Match(string a) => System.Text.RegularExpressions.Regex.IsMatch(a, ExchangeRuleBuilder.PersonalSenderPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    [Theory]
    [InlineData("jan.kowalski@feer.org.pl")]
    [InlineData("Anna.Nowak-Zielinska@FEER.org.pl")]
    [InlineData("jan.kowalski.2@feer.org.pl")]
    public void Konta_osobiste_dostaja_stopke(string a) => Assert.True(Match(a));

    [Theory]
    [InlineData("kontakt@feer.org.pl")]
    [InlineData("faktury@feer.org.pl")]
    [InlineData("szkolenia@feer.org.pl")]
    [InlineData(".jan@feer.org.pl")]
    [InlineData("jan.@feer.org.pl")]
    [InlineData("jan.kowalski@feer.pl")]
    [InlineData("jan.kowalski@feer.org.pl.evil.com")]
    [InlineData("x@evil.com, jan.kowalski@feer.org.pl")]
    public void Konta_funkcyjne_i_obce_nie_dostaja_stopki(string a) => Assert.False(Match(a));
}
