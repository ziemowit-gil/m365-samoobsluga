using M365Samoobsluga.Core.Services;
using Microsoft.Graph.Models.ODataErrors;

namespace M365Samoobsluga.Graph.Services;

/// <summary>Tłumaczy błędy Microsoft Graph (ODataError) na komunikaty po polsku; inne wyjątki zwraca bez zmian.</summary>
public static class GraphErrorMapper
{
    public static Exception Map(Exception ex, string operation)
    {
        if (ex is UserFacingException) return ex;
        if (ex is ODataError oe)
        {
            var status = oe.ResponseStatusCode;
            var code = oe.Error?.Code ?? "";
            return status switch
            {
                400 when operation == "hasło" => new UserFacingException(
                    "Nie udało się zmienić hasła. Sprawdź, czy aktualne hasło jest poprawne, a nowe spełnia zasady organizacji (długość, złożoność, brak użycia wcześniej).", ex),
                400 => new UserFacingException("Microsoft odrzucił te dane. Sprawdź wprowadzone wartości i spróbuj ponownie.", ex),
                401 => new UserFacingException("Sesja wygasła. Zaloguj się ponownie.", ex),
                403 => new UserFacingException($"Brak uprawnień do operacji „{operation}”. Administrator musi udzielić zgody aplikacji albo polityka organizacji blokuje tę zmianę.", ex),
                404 => new UserFacingException("Nie znaleziono skrzynki pocztowej. Sprawdź, czy Twoje konto ma licencję Exchange Online.", ex),
                429 => new UserFacingException("Usługa Microsoft jest chwilowo przeciążona. Spróbuj ponownie za minutę.", ex),
                >= 500 => new UserFacingException("Usługa Microsoft jest chwilowo niedostępna. Spróbuj ponownie później.", ex),
                _ => new UserFacingException($"Operacja „{operation}” nie powiodła się (kod: {code}).", ex),
            };
        }
        // Pozostałe wyjątki (np. wymagane ponowne logowanie/zgoda z MSAL) przepuszczamy do warstwy UI, która je obsłuży.
        return ex;
    }
}
