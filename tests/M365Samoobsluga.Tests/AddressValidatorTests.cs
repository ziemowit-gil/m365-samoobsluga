using M365Samoobsluga.Core.Validation;

namespace M365Samoobsluga.Tests;

public class AddressValidatorTests
{
    private static readonly string[] Domains = ["feer.org.pl", "feer.pl"];

    [Theory]
    [InlineData("jan.kowalski", "feer.org.pl", "jan.kowalski@feer.org.pl")]
    [InlineData("  Jan-K  ", "FEER.PL", "jan-k@feer.pl")]
    [InlineData("a_b", "feer.pl", "a_b@feer.pl")]
    public void Alias_poprawny_jest_normalizowany(string lp, string dom, string expected)
    {
        var r = AddressValidator.ValidateAlias(lp, dom, Domains, []);
        Assert.True(r.IsValid);
        Assert.Equal(expected, r.Normalized);
    }

    [Theory]
    [InlineData("", "feer.pl")]
    [InlineData("jan@x", "feer.pl")]
    [InlineData("żółw", "feer.pl")]
    [InlineData(".jan", "feer.pl")]
    [InlineData("jan..k", "feer.pl")]
    [InlineData("jan", "")]
    [InlineData("jan", "obca.com")]
    public void Alias_niepoprawny_zwraca_komunikat(string lp, string dom)
    {
        var r = AddressValidator.ValidateAlias(lp, dom, Domains, []);
        Assert.False(r.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(r.Error));
    }

    [Fact]
    public void Alias_za_dlugi_odrzucony() =>
        Assert.False(AddressValidator.ValidateAlias(new string('a', 65), "feer.pl", Domains, []).IsValid);

    [Fact]
    public void Alias_duplikat_bez_wzgledu_na_wielkosc_liter() =>
        Assert.False(AddressValidator.ValidateAlias("jan", "feer.pl", Domains, ["JAN@feer.pl"]).IsValid);

    [Theory]
    [InlineData("anna@example.org", "anna@example.org")]
    [InlineData(" Anna.B@Example.ORG ", "anna.b@example.org")]
    [InlineData("x+tag@sub.example.co.uk", "x+tag@sub.example.co.uk")]
    public void Przekierowanie_poprawne(string input, string expected)
    {
        var r = AddressValidator.ValidateForwardingAddress(input, []);
        Assert.True(r.IsValid);
        Assert.Equal(expected, r.Normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bez-malpy")]
    [InlineData("a@b@c.pl")]
    [InlineData("@example.org")]
    [InlineData("a@example")]
    [InlineData("a b@example.org")]
    [InlineData("a..b@example.org")]
    [InlineData("a@-example.org")]
    [InlineData("a@example.org, b@example.org")]
    public void Przekierowanie_niepoprawne(string input) =>
        Assert.False(AddressValidator.ValidateForwardingAddress(input, []).IsValid);

    [Fact]
    public void Przekierowanie_na_wlasny_adres_to_petla()
    {
        var r = AddressValidator.ValidateForwardingAddress("JAN@feer.pl", ["jan@feer.pl"]);
        Assert.False(r.IsValid);
        Assert.Contains("pętli", r.Error);
    }

    [Fact]
    public void Przekierowanie_zablokowana_domena() =>
        Assert.False(AddressValidator.ValidateForwardingAddress("a@zle.pl", [], ["zle.pl"]).IsValid);

    [Fact]
    public void Domena_domyslna_jest_pierwsza_a_reszta_alfabetycznie() =>
        Assert.Equal(["feer.org.pl", "feer.onmicrosoft.com", "inna.pl"], AddressValidator.OrderDomains(["inna.pl", "feer.onmicrosoft.com", "FEER.org.pl"], "feer.org.pl"));

    [Fact]
    public void Domyslna_spoza_listy_nie_dodaje_sie() =>
        Assert.Equal(["a.pl", "b.pl"], AddressValidator.OrderDomains(["b.pl", "a.pl"], "feer.org.pl"));
}
