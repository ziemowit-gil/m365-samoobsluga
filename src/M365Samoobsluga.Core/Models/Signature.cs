namespace M365Samoobsluga.Core.Models;

/// <summary>Dane organizacji w stopce (ustawia administrator w konfiguracji, jak w module stopek SZO).</summary>
public sealed record SignatureOrg
{
    public string OrgName { get; set; } = "";
    public string Address { get; set; } = "";
    public string Nip { get; set; } = "";
    public string Krs { get; set; } = "";
    public string Website { get; set; } = "";
    public string PromoHtml { get; set; } = "";
}

/// <summary>Wygląd stopki. Wartości przechodzą przez <see cref="Services.SignatureRenderer.Sanitize"/>.</summary>
public sealed class SignatureDesign
{
    public string Font { get; set; } = "Arial";
    public int Size { get; set; } = 13;
    public int NameSize { get; set; } = 16;
    public string Color { get; set; } = "#222222";
    public string Accent { get; set; } = "#1d4ed8";
    public bool NameAccent { get; set; }
    public string SepStyle { get; set; } = "solid";
    public string SepColor { get; set; } = "#999999";
    public int Gap { get; set; } = 8;
    public string LogoUrl { get; set; } = "";
    public int LogoWidth { get; set; } = 120;
    public bool ShowTitle { get; set; } = true;
    public bool ShowPhone { get; set; } = true;
    public bool ShowEmail { get; set; } = true;
    public bool ShowOrg { get; set; } = true;
    public bool ShowAddress { get; set; } = true;
    public bool ShowNip { get; set; } = true;
    public bool ShowKrs { get; set; } = true;
    public bool ShowWebsite { get; set; } = true;
    public bool ShowPromo { get; set; } = true;
}

/// <summary>Dane osoby w stopce: imię i nazwisko oraz e-mail z konta, stanowisko i telefon edytowalne przez użytkownika.</summary>
public sealed record SignaturePerson(string Name, string Title, string Phone, string Email);
