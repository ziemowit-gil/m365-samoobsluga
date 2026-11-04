using M365Samoobsluga.Core.Validation;

namespace M365Samoobsluga.Tests;

public class AddressValidatorTests
{
    [Theory]
    [InlineData("jan.kowalski", "feer.org.pl", "jan.kowalski@feer.org.pl")]
    [InlineData("  Jan-K  ", "FEER.ORG.PL", "jan-k@feer.org.pl")]
    [InlineData("a_b", "feer.org.pl", "a_b@feer.org.pl")]
    public void Alias_poprawny_jest_normalizowany(string lp, string dom, string expected)
    {
        var r = AddressValidator.ValidateAlias(lp, dom, []);
        Assert.True(r.IsValid);
        Assert.Equal(expected, r.Normalized);
    }

    [Theory]
    [InlineData("", "feer.org.pl")]
    [InlineData("jan@x", "feer.org.pl")]
    [InlineData("żółw", "feer.org.pl")]
    [InlineData(".jan", "feer.org.pl")]
    [InlineData("jan..k", "feer.org.pl")]
    [InlineData("jan", "")]
    [InlineData("jan", "obca.com")]
    [InlineData("jan", "feer.pl")]
    [InlineData("jan", "feerns.onmicrosoft.com")]
    public void Alias_niepoprawny_zwraca_komunikat(string lp, string dom)
    {
        var r = AddressValidator.ValidateAlias(lp, dom, []);
        Assert.False(r.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(r.Error));
    }

    [Fact]
    public void Alias_za_dlugi_odrzucony() =>
        Assert.False(AddressValidator.ValidateAlias(new string('a', 65), "feer.org.pl", []).IsValid);

    [Fact]
    public void Alias_duplikat_bez_wzgledu_na_wielkosc_liter() =>
        Assert.False(AddressValidator.ValidateAlias("jan", "feer.org.pl", ["JAN@feer.org.pl"]).IsValid);

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
}
