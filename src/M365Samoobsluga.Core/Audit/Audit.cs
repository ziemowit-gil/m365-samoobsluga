using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace M365Samoobsluga.Core.Audit;

/// <summary>Wpis audytu: kto, co, kiedy. Nigdy nie zawiera haseł ani tokenów ani treści wiadomości.</summary>
public sealed record AuditEntry(DateTimeOffset At, string UserPrincipalName, string Action, string? Target, bool Success, string? Detail);

public interface IAuditLog { void Write(AuditEntry entry); }

public sealed class LoggerAuditLog(ILogger<LoggerAuditLog> logger) : IAuditLog
{
    public void Write(AuditEntry e) =>
        logger.LogInformation("AUDYT {Json}", JsonSerializer.Serialize(e with { Detail = AuditSanitizer.Clean(e.Detail) }));
}

public static class AuditSanitizer
{
    /// <summary>Ucina i usuwa znaki sterujące; obrona przed wstrzykiwaniem linii do logu.</summary>
    public static string? Clean(string? s)
    {
        if (s is null) return null;
        var t = new string(s.Where(c => !char.IsControl(c)).ToArray());
        return t.Length > 200 ? t[..200] : t;
    }
}
