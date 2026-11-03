namespace M365Samoobsluga.Core.Models;

public enum AuthMethodKind { Password, MicrosoftAuthenticator, Phone, Fido2, WindowsHello, SoftwareOath, Email, TemporaryAccessPass, Other }

/// <summary>Zarejestrowana metoda uwierzytelniania. Numer telefonu jest maskowany już w usłudze.</summary>
public sealed record AuthMethod(string Id, AuthMethodKind Kind, string Label, DateTimeOffset? CreatedAt);
