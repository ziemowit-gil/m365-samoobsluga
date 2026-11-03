using M365Samoobsluga.Core.Models;
using M365Samoobsluga.Core.Services;

namespace M365Samoobsluga.Tests;

public class SignatureTests
{
    private static readonly SignatureOrg Org = new() { OrgName = "Fundacja FEER", Address = "ul. Testowa 1, Warszawa", Nip = "123", Krs = "456", Website = "feer.org.pl" };
    private static readonly SignaturePerson P = new("Jan <Kowalski>", "Koordynator", "+48 600 100 200", "jan@feer.org.pl");

    [Fact]
    public void Html_zawiera_dane_osoby_i_organizacji_z_kodowaniem()
    {
        var h = SignatureRenderer.RenderHtml(Org, new SignatureDesign(), P);
        Assert.Contains("Jan &lt;Kowalski&gt;", h);
        Assert.DoesNotContain("<Kowalski>", h);
        Assert.Contains("tel. +48 600 100 200", h);
        Assert.Contains("mailto:jan@feer.org.pl", h);
        Assert.Contains("NIP: 123", h);
        Assert.Contains("href=\"https://feer.org.pl\"", h);
    }

    [Fact]
    public void Ogolny_numer_telefonu_jest_w_stopce_html_i_tekscie_i_mozna_go_ukryc()
    {
        var org = Org with { Phone = "+48 22 123 45 67" };
        Assert.Contains("Tel. ogólny: +48 22 123 45 67", SignatureRenderer.RenderHtml(org, new SignatureDesign(), P));
        Assert.Contains("Tel. ogólny: +48 22 123 45 67", SignatureRenderer.RenderText(org, new SignatureDesign(), P));
        Assert.DoesNotContain("Tel. ogólny", SignatureRenderer.RenderHtml(org, new SignatureDesign { ShowOrgPhone = false }, P));
        Assert.DoesNotContain("Tel. ogólny", SignatureRenderer.RenderHtml(Org, new SignatureDesign(), P));
    }

    [Fact]
    public void Ogolny_numer_telefonu_jest_walidowany() =>
        Assert.Single(SignatureRenderer.ValidateOrg(new SignatureOrg { OrgName = "X", Phone = "abc" }));

    [Fact]
    public void Wylaczone_pola_nie_trafiaja_do_stopki()
    {
        var h = SignatureRenderer.RenderHtml(Org, new SignatureDesign { ShowPhone = false, ShowNip = false, ShowTitle = false }, P);
        Assert.DoesNotContain("tel.", h);
        Assert.DoesNotContain("NIP", h);
        Assert.DoesNotContain("Koordynator", h);
    }

    [Fact]
    public void Wyglad_jest_przycinany_do_dozwolonych_wartosci()
    {
        var d = SignatureRenderer.Sanitize(new SignatureDesign { Font = "Comic Sans", Size = 99, NameSize = 1, Color = "red; x:y", Accent = "#ABCDEF", LogoUrl = "javascript:alert(1)", SepStyle = "wavy", LogoWidth = 5000 });
        Assert.Equal("Arial", d.Font);
        Assert.Equal(16, d.Size);
        Assert.Equal(13, d.NameSize);
        Assert.Equal("#222222", d.Color);
        Assert.Equal("#abcdef", d.Accent);
        Assert.Equal("", d.LogoUrl);
        Assert.Equal("solid", d.SepStyle);
        Assert.Equal(300, d.LogoWidth);
    }

    [Fact]
    public void Logo_tylko_https() =>
        Assert.Equal("https://x.pl/l.png", SignatureRenderer.Sanitize(new SignatureDesign { LogoUrl = "https://x.pl/l.png" }).LogoUrl);

    [Fact]
    public void Promo_usuwa_skrypty_zdarzenia_i_niebezpieczne_linki()
    {
        var o = new SignatureOrg { OrgName = "X", PromoHtml = "<p onclick=\"x()\">Hej <script>alert(1)</script><a href=\"javascript:alert(1)\">zły</a> <a href=\"https://a.pl\">ok</a></p>" };
        var h = SignatureRenderer.RenderHtml(o, new SignatureDesign(), P);
        Assert.DoesNotContain("<script", h);
        Assert.DoesNotContain("onclick", h);
        Assert.DoesNotContain("javascript:", h);
        Assert.Contains("https://a.pl", h);
    }

    [Fact]
    public void Tekst_zawiera_podstawowe_pola()
    {
        var t = SignatureRenderer.RenderText(Org, new SignatureDesign(), P);
        Assert.Contains("Jan <Kowalski>", t);
        Assert.Contains("tel. +48 600 100 200 | jan@feer.org.pl", t);
        Assert.Contains("KRS: 456", t);
    }

    [Theory]
    [InlineData("+48 600 100 200", true)]
    [InlineData("(22) 123-45-67", true)]
    [InlineData("", true)]
    [InlineData("abc", false)]
    [InlineData("600;rm", false)]
    public void Telefon_walidacja(string phone, bool ok) => Assert.Equal(ok, SignatureRenderer.ValidatePhone(phone) is null);

    [Fact]
    public void Stanowisko_za_dlugie_odrzucone() => Assert.NotNull(SignatureRenderer.ValidateTitle(new string('a', 101)));
}

public class SignatureAdminTests
{
    [Fact]
    public void Admin_rozpoznawany_po_upn_i_oid_bez_wzgledu_na_wielkosc_liter()
    {
        var a = new AdminList(["Admin@Feer.org.pl", " 1111 "]);
        Assert.True(a.IsAdmin(new(" ", "admin@feer.org.pl", null)));
        Assert.True(a.IsAdmin(new("1111", "ktos@feer.org.pl", null)));
        Assert.False(a.IsAdmin(new("2222", "ktos@feer.org.pl", null)));
    }

    [Fact]
    public void Pusta_lista_adminow_nikogo_nie_dopuszcza() => Assert.False(new AdminList([]).IsAdmin(new("", "", null)));

    [Fact]
    public void Walidacja_organizacji_zglasza_bledy()
    {
        var e = SignatureRenderer.ValidateOrg(new SignatureOrg { OrgName = "", Nip = "abc", Krs = "x", Website = "a b\"" });
        Assert.Equal(4, e.Count);
        Assert.Empty(SignatureRenderer.ValidateOrg(new SignatureOrg { OrgName = "FEER", Nip = "123-45-67", Krs = "0000123456", Website = "feer.org.pl" }));
    }

    [Fact]
    public async Task Magazyn_zapisuje_i_czysci_wyglad_a_bez_pliku_zwraca_domyslne()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var def = new SignatureTemplate(new SignatureOrg { OrgName = "Domyślna" }, new SignatureDesign());
        try
        {
            var s = new M365Samoobsluga.Graph.Services.FileSignatureTemplateStore(path, def);
            Assert.Equal("Domyślna", (await s.GetAsync()).Org.OrgName);
            await s.SaveAsync(new SignatureTemplate(new SignatureOrg { OrgName = "Nowa" }, new SignatureDesign { Size = 99, Color = "zły" }));
            var t = await s.GetAsync();
            Assert.Equal("Nowa", t.Org.OrgName);
            Assert.Equal(16, t.Design.Size);
            Assert.Equal("#222222", t.Design.Color);
            await Assert.ThrowsAsync<UserFacingException>(() => s.SaveAsync(new SignatureTemplate(new SignatureOrg(), new SignatureDesign())));
        }
        finally { File.Delete(path); }
    }
}
