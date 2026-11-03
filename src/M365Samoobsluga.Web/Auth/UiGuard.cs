using M365Samoobsluga.Core.Services;
using Microsoft.Identity.Web;

namespace M365Samoobsluga.Web.Auth;

/// <summary>Wspólna obsługa błędów w UI: komunikaty po polsku, a gdy trzeba także ponowne logowanie/zgoda.</summary>
public sealed class UiGuard(IServiceProvider sp, ILogger<UiGuard> log)
{
    /// <summary>Wykonuje operację; zwraca komunikat błędu po polsku albo null, gdy się udało.</summary>
    public async Task<string?> RunAsync(Func<Task> op)
    {
        try { await op(); return null; }
        catch (UserFacingException ex) { return ex.Message; }
        catch (Exception ex) when (ex is MicrosoftIdentityWebChallengeUserException || ex.InnerException is MicrosoftIdentityWebChallengeUserException)
        {
            sp.GetRequiredService<MicrosoftIdentityConsentAndConditionalAccessHandler>().HandleException(ex);
            return "Potrzebna jest ponowna zgoda lub logowanie. Za chwilę nastąpi przekierowanie.";
        }
        catch (OperationCanceledException) { return "Operacja została przerwana."; }
        catch (Exception ex)
        {
            log.LogError(ex, "Nieoczekiwany błąd w operacji interfejsu");
            return "Wystąpił nieoczekiwany błąd. Spróbuj ponownie lub skontaktuj się z administratorem.";
        }
    }
}
