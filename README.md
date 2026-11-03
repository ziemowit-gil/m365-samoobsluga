# Samoobsługa poczty Microsoft 365

Aplikacja webowa (ASP.NET Core Blazor Server, .NET 10) dla wolontariuszy i współpracowników organizacji. Zalogowana osoba zarządza **wyłącznie własnym kontem** w Microsoft 365. Interfejs jest po polsku i zgodny z WCAG (etykiety pól, obsługa klawiatury, link „Przejdź do treści”, kontrast ≥ 4,5:1, komunikaty `role="status"` i `role="alert"`).

## Funkcje i co naprawdę da się zrobić (stan na 2026-11-03)

Zakres zbadano w dokumentacji Microsoft Learn. Nic nie jest zgadywane: tam, gdzie Microsoft nie daje wspieranego API, aplikacja tego nie udaje.

| Funkcja | Mechanizm | Uprawnienie delegowane | Uwagi |
|---|---|---|---|
| Autoresponder | `PATCH /me/mailboxSettings` (`automaticRepliesSetting`), Graph v1.0 | `MailboxSettings.ReadWrite` | Osobny tekst wewnętrzny i zewnętrzny, termin od–do (tylko przyszłość), odbiorcy zewnętrzni: nikt / kontakty / wszyscy. Daty przeliczane ze strefy skrzynki na UTC. |
| Przekierowanie | `POST/PATCH/DELETE /me/mailFolders/inbox/messageRules`, akcja `forwardTo` (+ `delete` gdy bez kopii) | `MailboxSettings.ReadWrite` | To reguła skrzynki. Aplikacja zarządza tylko regułą o nazwie „Samoobsługa FEER: przekierowanie”. Polityki Exchange Online mogą blokować przekierowanie na adresy zewnętrzne. |
| Zmiana hasła | `POST /me/changePassword`, Graph v1.0 | `User-PasswordProfile.ReadWrite.All` | Jedyne uprawnienie podane w dokumentacji dla tego endpointu. Wymaga **zgody administratora** na zakres, ale `/me` działa wyłącznie na koncie zalogowanego użytkownika i nie wymaga roli admina. Polityka haseł jest egzekwowana przez Entra ID. |
| Aliasy: odczyt | `GET /me?$select=proxyAddresses`, `GET /organization` (zweryfikowane domeny z możliwością poczty) | `User.Read` | Dla `User.Read` organizacja zwraca `id`, `displayName`, `verifiedDomains`. |
| Aliasy: zapis | **Brak w Graph**: `proxyAddresses` jest tylko do odczytu | n/d | Zapis robi się w Exchange Online (`Set-Mailbox -EmailAddresses @{add=...}`) z rolą Exchange. Aplikacja zapisuje **wniosek** w kolejce (`App_Data/alias-requests.jsonl`), który realizuje administrator. Podmiana na backend patrz niżej. |
| Stopka | **Brak wspieranego API** (Microsoft: brak planów wsparcia roaming signatures w Graph; `SignatureHTML` nie działa po włączeniu roaming signatures; EWS wygasa 10.2026) | n/d | Aplikacja przechowuje szablon (tekst + HTML) i pokazuje go do skopiowania w Outlooku. Stopka serwerowa (reguła transportu) to zadanie administratora. |

Źródła: [changePassword](https://learn.microsoft.com/graph/api/user-changepassword), [mailboxSettings](https://learn.microsoft.com/graph/api/user-update-mailboxsettings), [messageRules](https://learn.microsoft.com/graph/api/mailfolder-post-messagerules), [user (proxyAddresses)](https://learn.microsoft.com/graph/api/resources/user), [organization](https://learn.microsoft.com/graph/api/organization-get), [Set-Mailbox / aliasy](https://learn.microsoft.com/exchange/recipients-in-exchange-online/manage-user-mailboxes/add-or-remove-email-addresses).

### Dlaczego Blazor Server + OpenID Connect
Tokeny i sekrety zostają na serwerze (poufny klient, przepływ authorization code), przeglądarka dostaje tylko ciasteczko sesji. Tożsamość użytkownika pochodzi zawsze z tokenu, nigdy z formularza, więc nie da się ruszyć cudzej skrzynki. Dostęp do Graph idzie w imieniu użytkownika (delegowane zakresy), bez uprawnień aplikacyjnych do skrzynek wszystkich.

### Aliasy: bezpieczna ścieżka do pełnej automatyzacji (nie zaimplementowana)
Zapis aliasu wymaga uprawnień Exchange wyższych niż użytkownika. Zamiast dawać je użytkownikom, podmień `IAliasChangeExecutor` na wąski backend, który: przyjmuje wyłącznie UPN z zaufanego tokenu, waliduje alias (`AddressValidator`), ma uprawnienia ograniczone do pojedynczej skrzynki i zapisuje audyt. Szczegóły ograniczania uprawnień aplikacji w Exchange Online (RBAC for Applications) trzeba potwierdzić w bieżącej dokumentacji przed wdrożeniem. Do tego czasu obowiązuje tryb wniosków.

## Architektura

```
src/M365Samoobsluga.Core    logika bez zależności od M365: walidacja aliasu/adresu, daty i treść autorespondera, audyt (dekoratory)
src/M365Samoobsluga.Graph   usługi Microsoft Graph, mapowanie błędów na polski, kolejka wniosków o aliasy, magazyn szablonu stopki
src/M365Samoobsluga.Web     UI Blazor (po polsku), logowanie Entra ID, konfiguracja, tryb demo (mocki)
tests/M365Samoobsluga.Tests testy jednostkowe (xUnit)
```

Audyt: każda zmiana trafia do logu jako JSON (`AUDYT {...}`: czas UTC, UPN, akcja, cel, powodzenie). Hasła, tokeny i treści wiadomości nie są logowane (przy zmianie hasła cel jest pusty). Skieruj log aplikacji do trwałego magazynu (np. plik, Application Insights).

## Wymagania
- .NET SDK 10 (`brew install dotnet` na macOS)
- Tenant Microsoft 365 z Exchange Online i Entra ID, konto z rolą administratora do rejestracji aplikacji i udzielenia zgody

## Rejestracja aplikacji w Entra ID
1. Entra admin center → **Applications → App registrations → New registration**. Typ konta: *Accounts in this organizational directory only* (single tenant).
2. **Redirect URI** (platforma *Web*): `https://localhost:5001/signin-oidc` oraz adres produkcyjny `https://<host>/signin-oidc`. **Front-channel logout URL**: `https://<host>/signout-oidc`.
3. **Certificates & secrets**: dodaj certyfikat (zalecane) albo sekret klienta. Wartość trzymaj poza repozytorium (patrz niżej).
4. **API permissions → Microsoft Graph → Delegated**:
   - `User.Read`: odczyt profilu i zweryfikowanych domen,
   - `MailboxSettings.ReadWrite`: autoresponder i reguły przekierowania,
   - `User-PasswordProfile.ReadWrite.All`: zmiana własnego hasła.
5. **Grant admin consent** (przycisk w tym samym oknie), żeby użytkownicy nie widzieli próśb o zgodę. Zakres od hasła zawsze wymaga zgody administratora.
6. Przypisz użytkowników (Enterprise applications → *Assignment required* = Tak), aby dostęp mieli tylko wskazani wolontariusze.

## Konfiguracja (bez sekretów w repozytorium)
`appsettings.json` zawiera puste `TenantId` i `ClientId`. Wartości i sekrety ustaw lokalnie przez User Secrets, na serwerze przez zmienne środowiskowe:

```bash
cd src/M365Samoobsluga.Web
dotnet user-secrets set "AzureAd:TenantId" "<id-tenanta>"
dotnet user-secrets set "AzureAd:ClientId" "<id-aplikacji>"
dotnet user-secrets set "AzureAd:ClientCredentials:0:SourceType" "ClientSecret"
dotnet user-secrets set "AzureAd:ClientCredentials:0:ClientSecret" "<sekret>"
```

Zmienne środowiskowe: `AzureAd__TenantId`, `AzureAd__ClientId`, `AzureAd__ClientCredentials__0__SourceType`, `AzureAd__ClientCredentials__0__ClientSecret`. W produkcji preferuj certyfikat z magazynu (SourceType `StoreWithThumbprint`) lub Key Vault.

Ustawienia `Samoobsluga:*`: `UseMockServices` (tryb demo), `DataDirectory` (szablony stopek), `AliasRequestQueuePath` (kolejka wniosków), `BlockedForwardingDomains`.

## Uruchomienie lokalne
Tryb demo (bez tenanta, dane w pamięci, **tylko do prób**; domyślnie włączony w środowisku Development):

```bash
dotnet run --project src/M365Samoobsluga.Web
```

Z prawdziwym tenantem (po rejestracji i ustawieniu sekretów): `Samoobsluga__UseMockServices=false ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/M365Samoobsluga.Web`, potem wejdź na adres z konsoli. Testy: `dotnet test`.

## Wdrożenie
1. `dotnet publish src/M365Samoobsluga.Web -c Release -o publish`
2. Uruchom na hoście z HTTPS (Azure App Service, kontener lub serwer z reverse proxy). Ustaw zmienne środowiskowe z sekcji Konfiguracja i `ASPNETCORE_ENVIRONMENT=Production`.
3. Dodaj adres produkcyjny do Redirect URI w rejestracji aplikacji.
4. Udostępnij zapisywalny katalog `App_Data` (szablony stopek, kolejka wniosków) i zadbaj o jego kopię zapasową.
5. Przy wielu instancjach zamień pamięciowy cache tokenów (`AddInMemoryTokenCaches`) na rozproszony (`AddDistributedTokenCaches`) i użyj współdzielonego magazynu kluczy Data Protection.

## Bezpieczeństwo
- Brak przechowywania haseł i tokenów poza mechanizmami frameworka (ciasteczko sesji, cache tokenów MSAL w pamięci).
- Domyślna polityka autoryzacji wymaga zalogowania na każdej stronie; nagłówki `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`.
- Hasła nie są logowane ani zapisywane, a pola formularza są czyszczone po próbie zmiany.
- Alias i adres przekierowania są walidowane po stronie serwera (zweryfikowane domeny, ochrona przed pętlą przekierowania na własny adres).
