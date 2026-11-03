using M365Samoobsluga.Core.Models;
using M365Samoobsluga.Core.Services;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace M365Samoobsluga.Graph.Services;

/// <summary>PATCH /me/mailboxSettings (delegowane MailboxSettings.ReadWrite).</summary>
public sealed class GraphAutoReplyService(GraphServiceClient graph) : IAutoReplyService
{
    public async Task<AutoReplySettings> GetAsync(CancellationToken ct = default)
    {
        try
        {
            var m = await graph.Me.MailboxSettings.GetAsync(cancellationToken: ct);
            var a = m?.AutomaticRepliesSetting;
            var tz = string.IsNullOrWhiteSpace(m?.TimeZone) ? "Europe/Warsaw" : m!.TimeZone!;
            var enabled = a?.Status is AutomaticRepliesStatus.AlwaysEnabled or AutomaticRepliesStatus.Scheduled;
            DateTime? start = null, end = null;
            if (a?.Status == AutomaticRepliesStatus.Scheduled)
            {
                start = ToLocal(a.ScheduledStartDateTime, tz);
                end = ToLocal(a.ScheduledEndDateTime, tz);
            }
            return new AutoReplySettings(enabled, start, end, tz,
                AutoReplyComposer.HtmlToText(a?.InternalReplyMessage),
                AutoReplyComposer.HtmlToText(a?.ExternalReplyMessage),
                a?.ExternalAudience switch
                {
                    ExternalAudienceScope.All => AutoReplyAudience.All,
                    ExternalAudienceScope.ContactsOnly => AutoReplyAudience.ContactsOnly,
                    _ => AutoReplyAudience.None,
                });
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw GraphErrorMapper.Map(ex, "autoresponder"); }
    }

    public async Task SaveAsync(AutoReplySettings settings, CancellationToken ct = default)
    {
        var errors = AutoReplyComposer.Validate(settings, DateTime.UtcNow);
        if (errors.Count > 0) throw new UserFacingException(string.Join(" ", errors));
        var c = AutoReplyComposer.Compose(settings);
        var body = new MailboxSettings
        {
            AutomaticRepliesSetting = new AutomaticRepliesSetting
            {
                Status = c.Status switch
                {
                    "scheduled" => AutomaticRepliesStatus.Scheduled,
                    "alwaysEnabled" => AutomaticRepliesStatus.AlwaysEnabled,
                    _ => AutomaticRepliesStatus.Disabled,
                },
                ExternalAudience = c.ExternalAudience switch
                {
                    "all" => ExternalAudienceScope.All,
                    "contactsOnly" => ExternalAudienceScope.ContactsOnly,
                    _ => ExternalAudienceScope.None,
                },
                InternalReplyMessage = c.InternalHtml,
                ExternalReplyMessage = c.ExternalHtml,
                ScheduledStartDateTime = c.StartUtc is { } s ? Utc(s) : null,
                ScheduledEndDateTime = c.EndUtc is { } e ? Utc(e) : null,
            },
        };
        try { await graph.Me.MailboxSettings.PatchAsync(body, cancellationToken: ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw GraphErrorMapper.Map(ex, "autoresponder"); }
    }

    private static DateTimeTimeZone Utc(DateTime utc) => new() { DateTime = utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff"), TimeZone = "UTC" };

    private static DateTime? ToLocal(DateTimeTimeZone? d, string tz)
    {
        if (d?.DateTime is null || !DateTime.TryParse(d.DateTime, null, System.Globalization.DateTimeStyles.RoundtripKind, out var v)) return null;
        // Graph zwraca zwykle UTC; inna strefa jest traktowana jak zwrócona.
        var utc = AutoReplyComposer.ToUtc(DateTime.SpecifyKind(v, DateTimeKind.Unspecified), string.IsNullOrWhiteSpace(d.TimeZone) ? "UTC" : d.TimeZone);
        return AutoReplyComposer.FromUtc(utc, tz);
    }
}
