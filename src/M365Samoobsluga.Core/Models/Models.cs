namespace M365Samoobsluga.Core.Models;

public sealed record UserIdentity(string ObjectId, string UserPrincipalName, string? DisplayName);

public sealed record MailboxAliases(string PrimaryAddress, IReadOnlyList<string> SecondaryAddresses, IReadOnlyList<string> VerifiedDomains);

public enum AutoReplyAudience { None, ContactsOnly, All }

/// <summary>Ustawienia autorespondera w formie niezależnej od Graph. Daty w czasie lokalnym użytkownika (<see cref="TimeZoneId"/>).</summary>
public sealed record AutoReplySettings(
    bool Enabled,
    DateTime? StartLocal,
    DateTime? EndLocal,
    string TimeZoneId,
    string InternalMessage,
    string ExternalMessage,
    AutoReplyAudience ExternalAudience);

public sealed record ForwardingSettings(bool Enabled, string? Address, bool KeepCopy, string? RuleId);

public sealed record SignatureSet(string Html, string Text);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
