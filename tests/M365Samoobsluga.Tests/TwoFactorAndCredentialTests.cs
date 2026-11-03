using M365Samoobsluga.Core.Models;
using M365Samoobsluga.Core.Services;
using M365Samoobsluga.Graph.Services;

namespace M365Samoobsluga.Tests;

public class TwoFactorAndCredentialTests
{
    private static AuthMethod M(string id, AuthMethodKind k) => new(id, k, id, null);

    [Fact]
    public void Haslo_nie_liczy_sie_jako_drugi_skladnik() =>
        Assert.Equal(1, TwoFactorPolicy.SecondFactorCount([M("p", AuthMethodKind.Password), M("a", AuthMethodKind.MicrosoftAuthenticator)]));

    [Fact]
    public void Nie_mozna_usunac_ostatniego_drugiego_skladnika()
    {
        var all = new[] { M("p", AuthMethodKind.Password), M("a", AuthMethodKind.MicrosoftAuthenticator) };
        Assert.Contains("jedyna", TwoFactorPolicy.CannotRemoveReason(all[1], all));
    }

    [Fact]
    public void Mozna_usunac_authenticator_gdy_jest_druga_metoda()
    {
        var all = new[] { M("a", AuthMethodKind.MicrosoftAuthenticator), M("b", AuthMethodKind.MicrosoftAuthenticator) };
        Assert.Null(TwoFactorPolicy.CannotRemoveReason(all[0], all));
    }

    [Fact]
    public void Inne_metody_usuwa_sie_na_stronie_microsoftu()
    {
        var all = new[] { M("f", AuthMethodKind.Fido2), M("a", AuthMethodKind.MicrosoftAuthenticator) };
        Assert.Contains("Moje metody logowania", TwoFactorPolicy.CannotRemoveReason(all[0], all));
    }

    [Theory]
    [InlineData("+48 600 100 221", "Numer kończący się na 21")]
    [InlineData("", "Numer telefonu")]
    public void Telefon_jest_maskowany(string n, string expected) => Assert.Equal(expected, GraphTwoFactorService.MaskPhone(n));

    [Fact]
    public void Maska_nie_ujawnia_pelnego_numeru() => Assert.DoesNotContain("600", GraphTwoFactorService.MaskPhone("+48 600 100 221"));

    [Theory]
    [InlineData("StoreWithThumbprint", true)]
    [InlineData("KeyVault", true)]
    [InlineData("ClientSecret", false)]
    public void Rozpoznaje_certyfikat(string type, bool cert) => Assert.Equal(cert, CredentialPolicy.UsesCertificate([type]));

    [Fact]
    public void Sekret_na_produkcji_daje_ostrzezenie() => Assert.Single(CredentialPolicy.Evaluate(["ClientSecret"], true));

    [Fact]
    public void Sekret_lokalnie_bez_ostrzezenia() => Assert.Empty(CredentialPolicy.Evaluate(["ClientSecret"], false));

    [Fact]
    public void Certyfikat_na_produkcji_bez_ostrzezenia() => Assert.Empty(CredentialPolicy.Evaluate(["StoreWithThumbprint"], true));

    [Fact]
    public void Brak_poswiadczen_to_ostrzezenie() => Assert.Single(CredentialPolicy.Evaluate([], false));

    [Fact]
    public void Wygasanie_certyfikatu()
    {
        var now = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(10, CredentialPolicy.DaysToExpiry(now.AddDays(10), now));
        Assert.True(CredentialPolicy.ShouldWarnAboutExpiry(now.AddDays(10), now));
        Assert.False(CredentialPolicy.ShouldWarnAboutExpiry(now.AddDays(90), now));
        Assert.True(CredentialPolicy.ShouldWarnAboutExpiry(now.AddDays(-1), now));
    }
}
