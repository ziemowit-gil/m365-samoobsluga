using M365Samoobsluga.Core.Models;
using M365Samoobsluga.Core.Services;
using M365Samoobsluga.Core.Validation;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace M365Samoobsluga.Graph.Services;

/// <summary>
/// Przekierowanie jako reguła skrzynki odbiorczej (messageRules, delegowane MailboxSettings.ReadWrite).
/// Zarządza wyłącznie regułą o stałej nazwie; innych reguł użytkownika nie rusza.
/// Gdy użytkownik nie chce kopii: reguła dodatkowo przenosi wiadomość do Elementów usuniętych (akcja delete).
/// </summary>
public sealed class GraphForwardingService(GraphServiceClient graph, IAliasService aliases) : IForwardingService
{
    public const string RuleName = "Samoobsługa FEER: przekierowanie";

    public async Task<ForwardingSettings> GetAsync(CancellationToken ct = default)
    {
        try
        {
            var rule = await FindAsync(ct);
            if (rule is null) return new ForwardingSettings(false, null, true, null);
            var addr = rule.Actions?.ForwardTo?.FirstOrDefault()?.EmailAddress?.Address;
            return new ForwardingSettings(rule.IsEnabled ?? false, addr, rule.Actions?.Delete != true, rule.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw GraphErrorMapper.Map(ex, "przekierowanie"); }
    }

    public async Task SaveAsync(ForwardingSettings s, CancellationToken ct = default)
    {
        try
        {
            var existing = await FindAsync(ct);
            if (!s.Enabled)
            {
                if (existing?.Id is { } id) await graph.Me.MailFolders["inbox"].MessageRules[id].DeleteAsync(cancellationToken: ct);
                return;
            }
            var a = await aliases.GetAsync(ct);
            var own = a.SecondaryAddresses.Append(a.PrimaryAddress).ToList();
            var v = AddressValidator.ValidateForwardingAddress(s.Address, own);
            if (!v.IsValid) throw new UserFacingException(v.Error!);

            var rule = new MessageRule
            {
                DisplayName = RuleName,
                Sequence = 1,
                IsEnabled = true,
                Actions = new MessageRuleActions
                {
                    ForwardTo = [new Recipient { EmailAddress = new EmailAddress { Address = v.Normalized } }],
                    Delete = s.KeepCopy ? null : true,
                    StopProcessingRules = false,
                },
            };
            if (existing?.Id is { } rid) await graph.Me.MailFolders["inbox"].MessageRules[rid].PatchAsync(rule, cancellationToken: ct);
            else await graph.Me.MailFolders["inbox"].MessageRules.PostAsync(rule, cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw GraphErrorMapper.Map(ex, "przekierowanie"); }
    }

    private async Task<MessageRule?> FindAsync(CancellationToken ct)
    {
        var rules = await graph.Me.MailFolders["inbox"].MessageRules.GetAsync(cancellationToken: ct);
        return rules?.Value?.FirstOrDefault(r => r.DisplayName == RuleName);
    }
}
