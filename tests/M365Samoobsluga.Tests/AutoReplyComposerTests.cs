using M365Samoobsluga.Core.Models;
using M365Samoobsluga.Core.Services;

namespace M365Samoobsluga.Tests;

public class AutoReplyComposerTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
    private static AutoReplySettings S(bool en = true, DateTime? st = null, DateTime? en2 = null, string i = "Wewn.", string e = "Zewn.", AutoReplyAudience a = AutoReplyAudience.All, string tz = "Europe/Warsaw")
        => new(en, st, en2, tz, i, e, a);

    [Fact]
    public void Lato_i_zima_maja_rozne_przesuniecia_do_UTC()
    {
        Assert.Equal(new DateTime(2026, 7, 1, 8, 0, 0), AutoReplyComposer.ToUtc(new DateTime(2026, 7, 1, 10, 0, 0), "Europe/Warsaw"));
        Assert.Equal(new DateTime(2026, 12, 1, 9, 0, 0), AutoReplyComposer.ToUtc(new DateTime(2026, 12, 1, 10, 0, 0), "Europe/Warsaw"));
    }

    [Fact]
    public void Strefa_windows_jest_rozpoznawana() =>
        Assert.Equal(new DateTime(2026, 12, 1, 9, 0, 0), AutoReplyComposer.ToUtc(new DateTime(2026, 12, 1, 10, 0, 0), "Central European Standard Time"));

    [Fact]
    public void Round_trip_UTC_lokalny()
    {
        var local = new DateTime(2026, 11, 10, 8, 30, 0);
        Assert.Equal(local, AutoReplyComposer.FromUtc(AutoReplyComposer.ToUtc(local, "Europe/Warsaw"), "Europe/Warsaw"));
    }

    [Fact]
    public void Nieznana_strefa_rzuca_wyjatek() => Assert.Throws<ArgumentException>(() => AutoReplyComposer.FindTimeZone("Mars/Olympus"));

    [Fact]
    public void Wylaczony_nie_waliduje() => Assert.Empty(AutoReplyComposer.Validate(S(en: false, i: ""), Now));

    [Fact]
    public void Brak_tresci_wewnetrznej_to_blad() =>
        Assert.Contains(AutoReplyComposer.Validate(S(i: " "), Now), m => m.Contains("wewnątrz"));

    [Fact]
    public void Brak_tresci_zewnetrznej_ok_gdy_odbiorcy_zewnetrzni_wylaczeni() =>
        Assert.Empty(AutoReplyComposer.Validate(S(e: "", a: AutoReplyAudience.None), Now));

    [Fact]
    public void Brak_tresci_zewnetrznej_to_blad_gdy_wlaczeni() =>
        Assert.NotEmpty(AutoReplyComposer.Validate(S(e: "", a: AutoReplyAudience.All), Now));

    [Fact]
    public void Tylko_jedna_data_to_blad() =>
        Assert.Contains(AutoReplyComposer.Validate(S(st: new DateTime(2026, 12, 1, 8, 0, 0)), Now), m => m.Contains("obie daty"));

    [Fact]
    public void Koniec_przed_poczatkiem_to_blad() =>
        Assert.Contains(AutoReplyComposer.Validate(S(st: new DateTime(2026, 12, 5, 8, 0, 0), en2: new DateTime(2026, 12, 1, 8, 0, 0)), Now), m => m.Contains("późniejsza"));

    [Fact]
    public void Koniec_w_przeszlosci_to_blad() =>
        Assert.Contains(AutoReplyComposer.Validate(S(st: new DateTime(2026, 10, 1, 8, 0, 0), en2: new DateTime(2026, 10, 2, 8, 0, 0)), Now), m => m.Contains("w przyszłości"));

    [Fact]
    public void Poprawny_termin_bez_bledow() =>
        Assert.Empty(AutoReplyComposer.Validate(S(st: new DateTime(2026, 12, 1, 8, 0, 0), en2: new DateTime(2026, 12, 8, 8, 0, 0)), Now));

    [Fact]
    public void Compose_z_terminem_to_scheduled_w_UTC()
    {
        var c = AutoReplyComposer.Compose(S(st: new DateTime(2026, 12, 1, 8, 0, 0), en2: new DateTime(2026, 12, 8, 8, 0, 0)));
        Assert.Equal("scheduled", c.Status);
        Assert.Equal(new DateTime(2026, 12, 1, 7, 0, 0), c.StartUtc);
        Assert.Equal("all", c.ExternalAudience);
    }

    [Fact]
    public void Compose_bez_dat_to_alwaysEnabled()
    {
        var c = AutoReplyComposer.Compose(S(a: AutoReplyAudience.ContactsOnly));
        Assert.Equal("alwaysEnabled", c.Status);
        Assert.Null(c.StartUtc);
        Assert.Equal("contactsOnly", c.ExternalAudience);
    }

    [Fact]
    public void Compose_wylaczony() => Assert.Equal("disabled", AutoReplyComposer.Compose(S(en: false)).Status);

    [Fact]
    public void Tekst_do_HTML_koduje_znaki_specjalne_i_akapity()
    {
        var h = AutoReplyComposer.TextToHtml("Cześć <b>x</b> & co\nlinia 2\n\nDrugi akapit");
        Assert.Contains("&lt;b&gt;x&lt;/b&gt; &amp; co<br>linia 2", h);
        Assert.Contains("<p>Drugi akapit</p>", h);
        Assert.DoesNotContain("<b>", h);
    }

    [Fact]
    public void HTML_do_tekstu_round_trip()
    {
        var t = "Jestem poza biurem.\n\nWrócę w poniedziałek <3 & wtedy odpiszę.";
        Assert.Equal(t, AutoReplyComposer.HtmlToText(AutoReplyComposer.TextToHtml(t)));
    }

    [Fact]
    public void Za_dluga_tresc_to_blad() =>
        Assert.Contains(AutoReplyComposer.Validate(S(i: new string('a', 4001)), Now), m => m.Contains("4000"));
}
