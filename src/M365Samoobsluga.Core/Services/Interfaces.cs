using M365Samoobsluga.Core.Models;

namespace M365Samoobsluga.Core.Services;

/// <summary>Wyjątek z komunikatem po polsku, bezpiecznym do pokazania użytkownikowi.</summary>
public sealed class UserFacingException(string message, Exception? inner = null) : Exception(message, inner);

public interface IAliasService
{
    Task<MailboxAliases> GetAsync(CancellationToken ct = default);
    Task AddAsync(string address, CancellationToken ct = default);
    Task RemoveAsync(string address, CancellationToken ct = default);
    /// <summary>Czy zmiany są wykonywane od razu, czy trafiają jako wniosek do administratora.</summary>
    bool AppliesImmediately { get; }
}
public interface IAutoReplyService
{
    Task<AutoReplySettings> GetAsync(CancellationToken ct = default);
    Task SaveAsync(AutoReplySettings settings, CancellationToken ct = default);
}
public interface IForwardingService
{
    Task<ForwardingSettings> GetAsync(CancellationToken ct = default);
    Task SaveAsync(ForwardingSettings settings, CancellationToken ct = default);
}
public interface IPasswordService
{
    Task ChangeAsync(ChangePasswordRequest request, CancellationToken ct = default);
}
public interface ISignatureService
{
    /// <summary>Szablon podpisu zapisany lokalnie dla użytkownika (bez zapisu do skrzynki — brak wspieranego API Graph).</summary>
    Task<SignatureSet> GetAsync(CancellationToken ct = default);
    Task SaveAsync(SignatureSet signature, CancellationToken ct = default);
}
