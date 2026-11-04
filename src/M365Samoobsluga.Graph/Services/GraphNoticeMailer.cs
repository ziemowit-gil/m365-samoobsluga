using M365Samoobsluga.Core.Services;
using Microsoft.Graph;
using Microsoft.Identity.Web;
using Microsoft.Graph.Me.SendMail;
using Microsoft.Graph.Models;

namespace M365Samoobsluga.Graph.Services;

/// <summary>
/// POST /me/sendMail (delegowane Mail.Send). Zakres jest dobierany dopiero przy wysyłce, więc zgodę na niego wyrażają tylko osoby,
/// które wysyłają powiadomienia, a nie wszyscy użytkownicy aplikacji. Wiadomość wychodzi ze skrzynki zalogowanej osoby.
/// </summary>
public sealed class GraphNoticeMailer(GraphServiceClient graph) : INoticeMailer
{
    public async Task SendAsync(string to, string subject, string html, CancellationToken ct = default)
    {
        if (!StopkaNotice.IsValidEmail(to)) throw new UserFacingException("Niepoprawny adres odbiorcy.");
        var body = new SendMailPostRequestBody
        {
            Message = new Message
            {
                Subject = subject,
                Body = new ItemBody { ContentType = BodyType.Html, Content = html },
                ToRecipients = [new Recipient { EmailAddress = new EmailAddress { Address = to } }],
            },
            SaveToSentItems = false,
        };
        try { await graph.Me.SendMail.PostAsync(body, rc => rc.Options.WithScopes("Mail.Send"), ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw GraphErrorMapper.Map(ex, "wysyłanie wiadomości"); }
    }
}
