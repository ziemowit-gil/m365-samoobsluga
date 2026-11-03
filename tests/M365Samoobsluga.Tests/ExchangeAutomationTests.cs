using M365Samoobsluga.Core.Models;
using M365Samoobsluga.Core.Services;
using M365Samoobsluga.Graph.Services;
using M365Samoobsluga.Web.Auth;

namespace M365Samoobsluga.Tests;

public class ExchangeAutomationTests
{
    private static ExchangeSettings Ok() => new() { Enabled = true, AppId = "11111111-2222-3333-4444-555555555555", Organization = "feer.org.pl", CertificatePath = "/etc/m365/exo.pfx" };

    [Fact]
    public void Poprawne_ustawienia_przechodza() => Assert.Null(ExchangeCommandBuilder.Validate(Ok()));

    [Fact]
    public void Wylaczone_ustawienia_sa_odrzucone() => Assert.NotNull(ExchangeCommandBuilder.Validate(new ExchangeSettings()));

    [Theory]
    [InlineData("zly-guid", "feer.org.pl", "/a.pfx")]
    [InlineData("11111111-2222-3333-4444-555555555555", "nie domena", "/a.pfx")]
    [InlineData("11111111-2222-3333-4444-555555555555", "feer.org.pl", "wzgledna.pfx")]
    [InlineData("11111111-2222-3333-4444-555555555555", "feer.org.pl", "/a.pfx\n-Evil")]
    public void Zle_ustawienia_sa_odrzucone(string app, string org, string path)
    {
        var s = Ok(); s.AppId = app; s.Organization = org; s.CertificatePath = path;
        Assert.NotNull(ExchangeCommandBuilder.Validate(s));
    }

    [Fact]
    public void Argumenty_wdrozenia_sa_lista_bez_hasla_i_z_trybem()
    {
        var a = ExchangeCommandBuilder.DeployArgs(Ok(), "/app/scripts/Set-StopkaRule.ps1", "/tmp/s.html", ExchangeRuleMode.Enforce);
        Assert.Equal("-NoProfile", a[0]);
        Assert.Equal("Enforce", a[a.IndexOf("-Mode") + 1]);
        Assert.Equal("/tmp/s.html", a[a.IndexOf("-HtmlFile") + 1]);
        Assert.Contains("-Force", a);
        Assert.DoesNotContain(a, x => x.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Argumenty_czyszczenia_wszystkich_i_jednej_skrzynki()
    {
        var all = ExchangeCommandBuilder.ClearArgs(Ok(), "/s.ps1", "/b.json", null, true);
        Assert.Contains("-All", all); Assert.DoesNotContain("-Mailbox", all);
        var one = ExchangeCommandBuilder.ClearArgs(Ok(), "/s.ps1", "/b.json", "jan@feer.org.pl", false);
        Assert.Equal("jan@feer.org.pl", one[one.IndexOf("-Mailbox") + 1]);
        Assert.Equal("/b.json", one[one.IndexOf("-BackupFile") + 1]);
    }

    [Theory]
    [InlineData("jan@feer.org.pl", true)]
    [InlineData("jan; Remove-Item", false)]
    [InlineData("", false)]
    public void Skrzynka_walidacja(string upn, bool ok) => Assert.Equal(ok, ExchangeCommandBuilder.IsValidMailbox(upn));

    [Fact]
    public void Hash_zalezy_od_tresci_i_trybu()
    {
        Assert.Equal(ExchangeCommandBuilder.TemplateHash("<p>a</p>", ExchangeRuleMode.Audit), ExchangeCommandBuilder.TemplateHash("<p>a</p>", ExchangeRuleMode.Audit));
        Assert.NotEqual(ExchangeCommandBuilder.TemplateHash("<p>a</p>", ExchangeRuleMode.Audit), ExchangeCommandBuilder.TemplateHash("<p>b</p>", ExchangeRuleMode.Audit));
        Assert.NotEqual(ExchangeCommandBuilder.TemplateHash("<p>a</p>", ExchangeRuleMode.Audit), ExchangeCommandBuilder.TemplateHash("<p>a</p>", ExchangeRuleMode.Enforce));
    }

    [Fact]
    public async Task Magazyn_automatyki_zapisuje_i_odczytuje()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var s = new FileExchangeAutomationStore(path);
            Assert.False((await s.GetAsync()).AutoDeploy);
            await s.SaveAsync(new ExchangeAutomation(true, ExchangeRuleMode.Enforce, "abc", DateTimeOffset.UtcNow, "OK"));
            var r = await s.GetAsync();
            Assert.True(r.AutoDeploy); Assert.Equal(ExchangeRuleMode.Enforce, r.Mode); Assert.Equal("abc", r.LastHash);
        }
        finally { File.Delete(path); }
    }

    sealed class FakeDeployer(bool configured, bool success) : IExchangeRuleDeployer
    {
        public int Calls; public string? LastHtml; public ExchangeRuleMode LastMode;
        public bool IsConfigured => configured;
        public Task<ExchangeOpResult> TestAsync(CancellationToken ct = default) => Task.FromResult(new ExchangeOpResult(true, ""));
        public Task<ExchangeOpResult> DeployAsync(string html, ExchangeRuleMode mode, CancellationToken ct = default) { Calls++; LastHtml = html; LastMode = mode; return Task.FromResult(new ExchangeOpResult(success, success ? "ok" : "błąd")); }
        public Task<ExchangeOpResult> RemoveAsync(CancellationToken ct = default) => Task.FromResult(new ExchangeOpResult(true, ""));
        public Task<ExchangeOpResult> ClearSignaturesAsync(string? m, bool a, CancellationToken ct = default) => Task.FromResult(new ExchangeOpResult(true, ""));
    }
    sealed class MemAuto(ExchangeAutomation v) : IExchangeAutomationStore
    {
        public ExchangeAutomation V = v;
        public Task<ExchangeAutomation> GetAsync(CancellationToken ct = default) => Task.FromResult(V);
        public Task SaveAsync(ExchangeAutomation x, CancellationToken ct = default) { V = x; return Task.CompletedTask; }
    }
    sealed class MemTemplate(SignatureTemplate t) : ISignatureTemplateStore
    {
        public SignatureTemplate T = t;
        public Task<SignatureTemplate> GetAsync(CancellationToken ct = default) => Task.FromResult(T);
        public Task SaveAsync(SignatureTemplate x, CancellationToken ct = default) { T = x; return Task.CompletedTask; }
    }
    private static MemTemplate Tpl(string org = "FEER") => new(new SignatureTemplate(new SignatureOrg { OrgName = org }, new SignatureDesign()));

    [Fact]
    public async Task Automatyka_wylaczona_nic_nie_wdraza()
    {
        var d = new FakeDeployer(true, true);
        Assert.Null(await ExchangeRuleSync.DeployIfNeededAsync(d, new MemAuto(ExchangeAutomation.Default), Tpl(), false, default));
        Assert.Equal(0, d.Calls);
    }

    [Fact]
    public async Task Pierwsze_wdrozenie_zaklada_regule_a_kolejne_bez_zmian_nie_powtarza()
    {
        var d = new FakeDeployer(true, true);
        var store = new MemAuto(new ExchangeAutomation(true, ExchangeRuleMode.Audit, null, null, null));
        var t = Tpl();
        Assert.True((await ExchangeRuleSync.DeployIfNeededAsync(d, store, t, false, default))!.Success);
        Assert.Equal(1, d.Calls);
        Assert.NotNull(store.V.LastHash);
        Assert.Null(await ExchangeRuleSync.DeployIfNeededAsync(d, store, t, false, default));
        Assert.Equal(1, d.Calls);
    }

    [Fact]
    public async Task Zmiana_szablonu_wywoluje_aktualizacje()
    {
        var d = new FakeDeployer(true, true);
        var store = new MemAuto(new ExchangeAutomation(true, ExchangeRuleMode.Audit, null, null, null));
        var t = Tpl();
        await ExchangeRuleSync.DeployIfNeededAsync(d, store, t, false, default);
        t.T = new SignatureTemplate(new SignatureOrg { OrgName = "Inna nazwa" }, new SignatureDesign());
        await ExchangeRuleSync.DeployIfNeededAsync(d, store, t, false, default);
        Assert.Equal(2, d.Calls);
        Assert.Contains("Inna nazwa", d.LastHtml);
    }

    [Fact]
    public async Task Blad_nie_zapisuje_hasha_wiec_proba_sie_powtorzy()
    {
        var d = new FakeDeployer(true, false);
        var store = new MemAuto(new ExchangeAutomation(true, ExchangeRuleMode.Audit, null, null, null));
        var r = await ExchangeRuleSync.DeployIfNeededAsync(d, store, Tpl(), false, default);
        Assert.False(r!.Success);
        Assert.Null(store.V.LastHash);
        Assert.StartsWith("Błąd", store.V.LastResult);
    }

    [Fact]
    public async Task Brak_konfiguracji_exchange_nic_nie_wdraza()
    {
        var d = new FakeDeployer(false, true);
        Assert.Null(await ExchangeRuleSync.DeployIfNeededAsync(d, new MemAuto(new ExchangeAutomation(true, ExchangeRuleMode.Audit, null, null, null)), Tpl(), false, default));
        Assert.Equal(0, d.Calls);
    }
}

public class ExchangeSetupTests
{
    [Fact]
    public void Certyfikat_jest_poprawny_i_ma_haslo()
    {
        var c = ExchangeSetup.GenerateCertificate("test", 2);
        Assert.True(c.Password.Length >= 24);
        using var withKey = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(c.Pfx, c.Password);
        Assert.True(withKey.HasPrivateKey);
        Assert.Equal(c.Thumbprint, withKey.Thumbprint);
        using var pub = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificate(c.Cer);
        Assert.False(pub.HasPrivateKey);
        Assert.Equal(c.Thumbprint, pub.Thumbprint);
        Assert.InRange((c.NotAfter - DateTimeOffset.UtcNow).TotalDays, 700, 735);
    }

    [Fact]
    public void Plik_pfx_bez_hasla_nie_otwiera_sie()
    {
        var c = ExchangeSetup.GenerateCertificate("test", 1);
        Assert.ThrowsAny<Exception>(() => System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(c.Pfx, "zle-haslo"));
    }

    [Fact]
    public void Certyfikaty_maja_rozne_hasla_i_odciski()
    {
        var a = ExchangeSetup.GenerateCertificate("a"); var b = ExchangeSetup.GenerateCertificate("b");
        Assert.NotEqual(a.Password, b.Password); Assert.NotEqual(a.Thumbprint, b.Thumbprint);
    }

    [Fact]
    public void Skrypt_roli_ma_wasne_role_i_client_id()
    {
        var s = ExchangeSetup.RoleScript("11111111-2222-3333-4444-555555555555");
        Assert.Contains("$appId = '11111111-2222-3333-4444-555555555555'", s);
        Assert.Contains("New-ServicePrincipal", s);
        Assert.Contains("New-RoleGroup", s);
        Assert.Contains("'Transport Rules','User Options'", s);
        Assert.Contains("Add-RoleGroupMember", s);
        Assert.DoesNotContain("Exchange Administrator", s.Replace("nie rolę Exchange Administrator", ""));
    }

    [Theory]
    [InlineData("zly")]
    [InlineData("'; Remove-Item /")]
    public void Skrypt_roli_odrzuca_zly_client_id(string id) => Assert.Throws<ArgumentException>(() => ExchangeSetup.RoleScript(id));

    [Fact]
    public async Task Dostawca_ustawien_laczy_konfiguracje_z_zapisem_i_certyfikatem()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var p = new M365Samoobsluga.Graph.Services.ExchangeSettingsProvider(new ExchangeSettings(), dir);
            Assert.False(p.Get().Enabled);
            await p.SaveAsync("11111111-2222-3333-4444-555555555555", "firma.onmicrosoft.com");
            Assert.False(p.HasCertificate);
            await p.SaveCertificateAsync(ExchangeSetup.GenerateCertificate("t"));
            Assert.True(p.HasCertificate);
            var s = p.Get();
            Assert.True(s.Enabled);
            Assert.Equal("firma.onmicrosoft.com", s.Organization);
            Assert.Equal(p.PfxPath, s.CertificatePath);
            Assert.Null(ExchangeCommandBuilder.Validate(s));
            Assert.False(string.IsNullOrEmpty(p.ReadPassword(s)));
            if (!OperatingSystem.IsWindows())
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(p.PfxPath));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
