# Samoobsługa poczty Microsoft 365

Aplikacja webowa dla wolontariuszy i współpracowników organizacji. Zalogowana osoba zmienia ustawienia tylko własnego konta Microsoft 365: aliasy, hasło, weryfikację dwuetapową, stopkę, autoresponder i przekierowanie poczty. Interfejs jest po polsku. Pola mają etykiety, całość działa z klawiatury, kontrast tekstu wynosi co najmniej 4,5:1, a komunikaty czytają się czytnikiem ekranu.

To Blazor Server na .NET 10 z logowaniem przez Entra ID. Tokeny zostają na serwerze, a przeglądarka dostaje tylko ciasteczko sesji. Tożsamość zawsze pochodzi z tokenu, nigdy z formularza, więc nie da się zmienić cudzej skrzynki.

## Co aplikacja robi i czego nie może

Sprawdziłem to w dokumentacji Microsoft Learn (stan na 3 listopada 2026). Gdzie Microsoft nie daje wspieranego API, aplikacja tego nie udaje.

| Funkcja | Jak działa | Uprawnienie delegowane |
|---|---|---|
| Autoresponder | `PATCH /me/mailboxSettings`. Osobne teksty dla odbiorców wewnętrznych i zewnętrznych, termin od i do (tylko w przyszłości), odbiorcy zewnętrzni: nikt, kontakty albo wszyscy. | `MailboxSettings.ReadWrite` |
| Przekierowanie | Reguła skrzynki (`/me/mailFolders/inbox/messageRules`, akcja `forwardTo`). Aplikacja zarządza tylko regułą o nazwie „Samoobsługa FEER: przekierowanie". Bez kopii wiadomość trafia dodatkowo do Elementów usuniętych. Polityka Exchange Online może blokować adresy zewnętrzne. | `MailboxSettings.ReadWrite` |
| Hasło | `POST /me/changePassword`. Zasady haseł egzekwuje Entra ID. | `User-PasswordProfile.ReadWrite.All` (wymaga zgody administratora, ale endpoint działa tylko na własnym koncie) |
| Weryfikacja 2FA | Lista metod: `GET /me/authentication/methods`. Usuwanie urządzeń Microsoft Authenticator: `DELETE /me/authentication/microsoftAuthenticatorMethods/{id}`. Po 10 minutach od logowania Microsoft może poprosić o ponowne MFA. Aplikacja nie pozwala usunąć ostatniej metody. | `UserAuthenticationMethod.ReadWrite` |
| Aliasy, odczyt | `GET /me?$select=proxyAddresses` i `GET /organization` (zweryfikowane domeny z pocztą). | `User.Read` |
| Aliasy, zapis | W Graph `proxyAddresses` jest tylko do odczytu. Zapis robi się w Exchange Online (`Set-Mailbox -EmailAddresses`), więc aplikacja zapisuje wniosek w `App_Data/alias-requests.jsonl`, a administrator go realizuje. | brak |
| Stopka | Graph nie ma API do podpisów Outlooka (Microsoft nie planuje go dla roaming signatures). Aplikacja generuje stopkę według wzoru organizacji, a użytkownik kopiuje ją do Outlooka. | `User.Read` (stanowisko i telefon z profilu) |

Dodawania nowych metod 2FA nie da się zrobić z aplikacji: według dokumentacji użytkownik nie dodaje telefonu do własnego konta przez Graph. Strona 2FA odsyła do https://aka.ms/mysecurityinfo.

Źródła: [changePassword](https://learn.microsoft.com/graph/api/user-changepassword), [mailboxSettings](https://learn.microsoft.com/graph/api/user-update-mailboxsettings), [messageRules](https://learn.microsoft.com/graph/api/mailfolder-post-messagerules), [metody uwierzytelniania](https://learn.microsoft.com/graph/api/authentication-list-methods), [usuwanie Authenticatora](https://learn.microsoft.com/graph/api/microsoftauthenticatorauthenticationmethod-delete), [dodawanie telefonu](https://learn.microsoft.com/graph/api/authentication-post-phonemethods), [user (proxyAddresses)](https://learn.microsoft.com/graph/api/resources/user), [organization](https://learn.microsoft.com/graph/api/organization-get), [aliasy w Exchange Online](https://learn.microsoft.com/exchange/recipients-in-exchange-online/manage-user-mailboxes/add-or-remove-email-addresses).

### Stopka według wzoru z SZO

Generator działa jak moduł stopek w SZO: jeden szablon HTML dla wszystkich. Dane organizacji i wygląd ustawia administrator w konfiguracji (`Samoobsluga:Signature:Org` i `:Design`): nazwa, adres, NIP, KRS, strona, logo, czcionka, rozmiary, kolory, separator, wybór widocznych pól oraz treść promocyjna. Wygląd jest przycinany do dozwolonych wartości (czcionki z listy, kolory `#rrggbb`, logo tylko przez https), a treść promocyjna przechodzi przez listę dozwolonych znaczników. Użytkownik wpisuje tylko stanowisko i telefon albo pobiera je z profilu Microsoft 365. Imię, nazwisko i e-mail biorą się z konta. Aplikacja pokazuje podgląd i kopiuje stopkę do schowka w wersji HTML i tekstowej. Stopkę dopisywaną po stronie serwera, do każdej wysyłanej wiadomości, ustawia administrator regułą transportu w Exchange.

### Aliasy: droga do pełnej automatyzacji

Zapis aliasu wymaga uprawnień Exchange wyższych niż użytkownika. Zamiast je rozdawać, podmień `IAliasChangeExecutor` na wąski backend. Ma przyjmować UPN wyłącznie z zaufanego tokenu, walidować alias (`AddressValidator`) i mieć uprawnienia ograniczone do jednej skrzynki. Sposób ograniczania uprawnień aplikacji w Exchange Online trzeba potwierdzić w aktualnej dokumentacji przed wdrożeniem. Do tego czasu działa tryb wniosków. Backend łączący się z Exchange Online bez użytkownika też loguje się certyfikatem, tak jak ta aplikacja (patrz niżej).

## Struktura

```
src/M365Samoobsluga.Core    logika bez zależności od M365: walidacja, daty i treść autorespondera, generator stopki, reguły 2FA, audyt
src/M365Samoobsluga.Graph   usługi Microsoft Graph, mapowanie błędów na polski, kolejka wniosków o aliasy
src/M365Samoobsluga.Web     interfejs Blazor, logowanie Entra ID, konfiguracja, tryb demo
tests/M365Samoobsluga.Tests testy jednostkowe (xUnit)
```

Każda zmiana trafia do dziennika jako JSON (`AUDYT {...}`: czas UTC, UPN, akcja, cel, wynik). Hasła, tokeny i treści wiadomości nie są logowane. Skieruj log do trwałego magazynu, na przykład do pliku albo Application Insights.

## Wymagania

- .NET SDK 10 (macOS: `brew install dotnet`)
- Tenant Microsoft 365 z Exchange Online i Entra ID oraz konto, które może zarejestrować aplikację i udzielić zgody administratora

## Rejestracja aplikacji w Entra ID

1. W centrum administracyjnym Entra wybierz Applications → App registrations → New registration. Typ konta: tylko ten katalog (single tenant).
2. Dodaj Redirect URI typu Web: `https://localhost:5001/signin-oidc` i adres produkcyjny `https://<host>/signin-oidc`. Front-channel logout URL: `https://<host>/signout-oidc`.
3. W Certificates & secrets dodaj certyfikat (patrz niżej). Sekret klienta jest dopuszczalny tylko lokalnie.
4. W API permissions dodaj uprawnienia delegowane Microsoft Graph: `User.Read`, `MailboxSettings.ReadWrite`, `User-PasswordProfile.ReadWrite.All`, `UserAuthenticationMethod.ReadWrite`.
5. Kliknij Grant admin consent. Zakres hasła zawsze wymaga zgody administratora, a bez zgody użytkownicy zobaczą prośbę o nią przy logowaniu.
6. W Enterprise applications ustaw Assignment required na Tak i przypisz wolontariuszy, którzy mają mieć dostęp.

## Logowanie aplikacji certyfikatem

Aplikacja wykazuje się przed Entra ID certyfikatem, a nie sekretem. Certyfikat można odnowić bez zmiany kodu, nie leży w pliku konfiguracji, a wyciek konfiguracji nie daje atakującemu tożsamości aplikacji.

1. Wygeneruj certyfikat (samopodpisany wystarcza, ważność do dwóch lat) i wyeksportuj klucz publiczny jako `.cer`:
   ```bash
   openssl req -x509 -newkey rsa:3072 -sha256 -days 730 -nodes -subj "/CN=m365-samoobsluga" -keyout app.key -out app.cer
   openssl pkcs12 -export -inkey app.key -in app.cer -out app.pfx
   ```
   Plików `app.key` i `app.pfx` nie dodawaj do repozytorium (`.gitignore` blokuje `*.pfx`). Przechowuj je w magazynie certyfikatów hosta albo w Key Vault.
2. W rejestracji aplikacji wgraj `app.cer` w Certificates & secrets → Certificates.
3. Wskaż certyfikat w konfiguracji (User Secrets lub zmienne środowiskowe):
   ```bash
   dotnet user-secrets set "AzureAd:ClientCredentials:0:SourceType" "StoreWithThumbprint"
   dotnet user-secrets set "AzureAd:ClientCredentials:0:CertificateStorePath" "CurrentUser/My"
   dotnet user-secrets set "AzureAd:ClientCredentials:0:CertificateThumbprint" "<odcisk-palca>"
   ```
   Inne rodzaje źródła: `KeyVault` (`KeyVaultUrl`, `KeyVaultCertificateName`) i `Path` (`CertificateDiskPath`, `CertificatePassword`).
4. Przy starcie aplikacja sprawdza certyfikat i zapisuje w logu, ile dni mu zostało. Ostrzega, gdy zostało 30 dni lub mniej, gdy certyfikatu nie ma w magazynie oraz gdy na produkcji używasz sekretu klienta.
5. Przy wymianie dodaj nowy certyfikat do rejestracji, zmień odcisk w konfiguracji, uruchom aplikację ponownie i dopiero wtedy usuń stary.

## Konfiguracja

`appsettings.json` ma puste `TenantId` i `ClientId`. Ustaw je lokalnie przez User Secrets, a na serwerze przez zmienne środowiskowe:

```bash
cd src/M365Samoobsluga.Web
dotnet user-secrets set "AzureAd:TenantId" "<id-tenanta>"
dotnet user-secrets set "AzureAd:ClientId" "<id-aplikacji>"
```

Zmienne środowiskowe mają postać `AzureAd__TenantId`, `AzureAd__ClientId`, `AzureAd__ClientCredentials__0__SourceType` i tak dalej. Sekretów i certyfikatów nie wpisuj do plików w repozytorium.

Sekcja `Samoobsluga` zawiera: `UseMockServices` (tryb demo), `DataDirectory` (nadpisania stanowiska i telefonu), `AliasRequestQueuePath` (kolejka wniosków), `BlockedForwardingDomains` i `Signature` (wzór stopki).

## Uruchomienie lokalne

Tryb demo działa bez tenanta, trzyma dane w pamięci i nie wymaga logowania. Służy tylko do prób. W środowisku Development jest włączony domyślnie:

```bash
dotnet run --project src/M365Samoobsluga.Web
```

Z prawdziwym tenantem, po rejestracji aplikacji i ustawieniu poświadczeń:

```bash
Samoobsluga__UseMockServices=false ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/M365Samoobsluga.Web
```

Testy: `dotnet test`.

## Wdrożenie

1. Zbuduj: `dotnet publish src/M365Samoobsluga.Web -c Release -o publish`.
2. Uruchom na hoście z HTTPS (Azure App Service, kontener albo serwer z reverse proxy), z `ASPNETCORE_ENVIRONMENT=Production` i zmiennymi z sekcji Konfiguracja.
3. Dodaj adres produkcyjny do Redirect URI.
4. Udostępnij zapisywalny katalog `App_Data` i rób jego kopie zapasowe.
5. Przy kilku instancjach zamień pamięciowy cache tokenów (`AddInMemoryTokenCaches`) na rozproszony (`AddDistributedTokenCaches`) i użyj wspólnego magazynu kluczy Data Protection.

## Bezpieczeństwo

- Aplikacja nie przechowuje haseł ani tokenów poza mechanizmami frameworka (ciasteczko sesji, pamięciowy cache tokenów).
- Każda strona wymaga zalogowania. Odpowiedzi mają nagłówki `X-Content-Type-Options`, `X-Frame-Options` i `Referrer-Policy`.
- Hasła nie trafiają do logów, a pola formularza są czyszczone po próbie zmiany.
- Alias i adres przekierowania są walidowane na serwerze: tylko zweryfikowane domeny, a przekierowanie na własny adres jest odrzucane jako pętla.
- Numery telefonów w metodach 2FA są maskowane, widać tylko dwie ostatnie cyfry.
