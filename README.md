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

Generator działa jak moduł stopek w SZO: jeden szablon HTML dla wszystkich. Dane organizacji i wygląd ustawia administrator na stronie „Administrator: stopka" (widoczna dla kont z `Samoobsluga:Admins`; zapis trafia do `App_Data/signature-template.json`, a wartości początkowe pochodzą z `Samoobsluga:Signature`): nazwa, adres, NIP, KRS, strona, logo, czcionka, rozmiary, kolory, separator, wybór widocznych pól oraz treść promocyjna. Wygląd jest przycinany do dozwolonych wartości (czcionki z listy, kolory `#rrggbb`, logo tylko przez https), a treść promocyjna przechodzi przez listę dozwolonych znaczników. Użytkownik wpisuje tylko stanowisko i telefon albo pobiera je z profilu Microsoft 365. Imię, nazwisko i e-mail biorą się z konta. Aplikacja pokazuje podgląd i kopiuje stopkę do schowka w wersji HTML i tekstowej. Stopkę dopisywaną po stronie serwera, do każdej wysyłanej wiadomości, ustawia administrator regułą transportu w Exchange.

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
2. Dodaj Redirect URI typu Web: `http://localhost:30375/signin-oidc` (lokalnie Entra dopuszcza http dla localhost) i adres produkcyjny `https://<host>/signin-oidc`. Front-channel logout URL: `https://<host>/signout-oidc`.
3. W Certificates & secrets dodaj certyfikat (patrz niżej). Sekret klienta jest dopuszczalny tylko lokalnie.
4. W API permissions dodaj uprawnienia delegowane Microsoft Graph: `User.Read`, `MailboxSettings.ReadWrite`, `User-PasswordProfile.ReadWrite.All`, `UserAuthenticationMethod.ReadWrite`.
5. Kliknij Grant admin consent. Zakres hasła zawsze wymaga zgody administratora, a bez zgody użytkownicy zobaczą prośbę o nią przy logowaniu.
6. W Enterprise applications ustaw Assignment required na Tak i przypisz wolontariuszy, którzy mają mieć dostęp.

### Automatyczna konfiguracja rejestracji

Skrypt `scripts/setup-entra.sh` robi kroki 1 do 6 za ciebie przez Azure CLI (`az`): tworzy lub uzupełnia rejestrację aplikacji (jedna dzierżawa), dodaje adresy przekierowania i adres wylogowania, dodaje cztery uprawnienia delegowane Graph (identyfikatory zakresów odczytuje z twojej dzierżawy, nie są wpisane na sztywno), udziela zgody administratora i tworzy sekret klienta albo dodaje certyfikat (`--cert`). Sekret trafia prosto do pliku z prawami 600 (`--write-env`) i nie jest wyświetlany. Skrypt pyta o potwierdzenie przed zmianami, a `--dry-run` pokazuje sam plan.

```bash
az login
./scripts/setup-entra.sh --base-url https://ms365webpanel.feer.org.pl --dry-run
sudo ./scripts/setup-entra.sh --base-url https://ms365webpanel.feer.org.pl \
     --write-env /etc/m365-samoobsluga.env --write-azuread /opt/m365-samoobsluga/App_Data/azuread.json
```

Na Ubuntu zainstaluj `az` skryptem `scripts/install-azure-cli-ubuntu.sh` (obsługuje też wersje, dla których Microsoft nie ma jeszcze własnego repozytorium, np. 26.04). Jeśli rejestrację zrobiłeś już ręcznie albo przez stronę /setup, podaj jej Client ID opcją `--app-id`, żeby skrypt nie tworzył drugiej. Potrzebujesz konta, które może rejestrować aplikacje i udzielać zgody administratora. Skrypt można uruchamiać ponownie: istniejące adresy zostają, a brakujące są dopisywane. Opcja `--require-assignment` włącza wymóg przypisania użytkowników do aplikacji. Skrypt napisałem na podstawie dokumentacji Azure CLI i sprawdziłem tylko składnię i tryb `--dry-run`, bez zmian w prawdziwej dzierżawie.

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

## Pierwsza konfiguracja w przeglądarce i adresy do Azure

Tenant ID i Client ID możesz wpisać w aplikacji zamiast w plikach. Gdy ich nie ma, aplikacja startuje w trybie konfiguracji i udostępnia tylko stronę `/setup`. Kod konfiguracji znajdziesz w logu serwera (linia „Kod konfiguracji"). Bez niego formularz nie zapisze zmian, a po 5 błędnych kodach blokuje się do restartu. Identyfikatory trafiają do `App_Data/azuread.json` (bez sekretów), a po zapisie trzeba uruchomić aplikację ponownie. Plik ma pierwszeństwo przed `appsettings.json`. Później zmienisz je na stronie Administrator: Azure (konta z `Samoobsluga:Admins`).

Obie strony pokazują adresy, które trzeba dodać w Azure (Entra → App registrations → aplikacja → Authentication). Dla adresu `https://poczta.example.org/` to:

| Wpis | Wartość |
|---|---|
| Redirect URI (Web) | `https://poczta.example.org/signin-oidc` |
| Redirect URI po wylogowaniu (Web) | `https://poczta.example.org/signout-callback-oidc` |
| Front-channel logout URL | `https://poczta.example.org/signout-oidc` |

Adres w Azure musi zgadzać się znak w znak z adresem w przeglądarce, razem z portem. Lokalnie (port 30375) działa `http://localhost:30375/...`; poza localhostem Entra wymaga https.

## Stopka w Outlookach (reguła Exchange Online)

Strona Administrator: stopka w Outlooku generuje regułę transportową, która dopisuje stopkę do każdej wiadomości wychodzącej od użytkowników organizacji. Działa w każdym Outlooku (komputer, przeglądarka, telefon), bo stopka jest dodawana na serwerze. Szablon bierze dane osoby z katalogu przez tokeny Exchange (`%%DisplayName%%`, `%%Title%%`, `%%MobilePhone%%`, `%%WindowsEmailAddress%%`), a wygląd z ustawień stopki. Aplikacja pokazuje podgląd, pilnuje limitu 5000 znaków i podaje skrypt PowerShell (`New-TransportRule` lub `Set-TransportRule`). Reguła startuje w trybie Audit, który niczego nie zmienia w wiadomościach. Po testach przełączasz ją na Enforce. Znacznik w nagłówku `X-Samoobsluga-Stopka` chroni przed drugą stopką w wątku. Aplikacja sama nie zmienia ustawień Exchange. Zamiast kopiować skrypt, możesz pobrać szablon HTML i uruchomić `scripts/Set-StopkaRule.ps1` (PowerShell; moduł ExchangeOnlineManagement; `-WhatIf` pokazuje plan, `-Mode Audit|Enforce`, `-Remove` usuwa regułę, połączenie interaktywne albo certyfikatem aplikacji). Przykład: `./scripts/Set-StopkaRule.ps1 -HtmlFile ./stopka-szablon.html -Mode Audit -WhatIf`. Przy stopce z serwera użytkownicy nie powinni dodawać własnej w Outlooku.

## Konfiguracja

`appsettings.json` ma puste `TenantId` i `ClientId`. Wpisz je na stronie `/setup` (patrz wyżej) albo ustaw przez User Secrets lub zmienne środowiskowe:

```bash
cd src/M365Samoobsluga.Web
dotnet user-secrets set "AzureAd:TenantId" "<id-tenanta>"
dotnet user-secrets set "AzureAd:ClientId" "<id-aplikacji>"
```

Zmienne środowiskowe mają postać `AzureAd__TenantId`, `AzureAd__ClientId`, `AzureAd__ClientCredentials__0__SourceType` i tak dalej. Sekretów i certyfikatów nie wpisuj do plików w repozytorium.

Domyślny adres aplikacji to `http://localhost:30375` (ustawienie `Urls`; na serwerze nadpisz zmienną `ASPNETCORE_URLS`). Sekcja `Samoobsluga` zawiera: `Admins` (lista UPN lub identyfikatorów obiektu kont, które mogą zmieniać wzór stopki na stronie Administrator: stopka), `UseMockServices` (tryb demo), `DataDirectory` (nadpisania stanowiska i telefonu), `AliasRequestQueuePath` (kolejka wniosków), `BlockedForwardingDomains` i `Signature` (wzór stopki).

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

## Ubuntu: uruchomienie i usługa systemowa

Najkrótsza droga to skrypt `scripts/install-ubuntu.sh`. Instaluje .NET 10, publikuje aplikację do `/opt/m365-samoobsluga`, zakłada użytkownika systemowego i usługę systemd, tworzy pusty plik na sekrety `/etc/m365-samoobsluga.env` (prawa 600) i opcjonalnie dodaje konfigurację nginx. Można go uruchamiać ponownie, żeby zaktualizować aplikację (`App_Data` zostaje):

```bash
sudo ./scripts/install-ubuntu.sh --admin twoj.login@feer.org.pl --domain poczta.example.org
```

Ręczna wersja tych kroków jest poniżej.

Instalacja .NET 10 (skrypt Microsoftu działa na każdej wersji Ubuntu):

```bash
sudo apt update && sudo apt install -y curl git libicu-dev
curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
bash dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet"
export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH"
```

Uruchomienie (jak w sekcji „Uruchomienie lokalne”): `git clone`, `cd m365-samoobsluga`, `dotnet run --project src/M365Samoobsluga.Web` i adres `http://localhost:30375`.

Na serwerze opublikuj aplikację i uruchom ją jako usługę systemd, a przed nią postaw nginx z HTTPS:

```bash
dotnet publish src/M365Samoobsluga.Web -c Release -o /opt/m365-samoobsluga
sudo useradd --system --home /opt/m365-samoobsluga m365app
sudo chown -R m365app: /opt/m365-samoobsluga
```

Plik `/etc/systemd/system/m365-samoobsluga.service`:

```ini
[Unit]
Description=Samoobsluga poczty Microsoft 365
After=network.target

[Service]
WorkingDirectory=/opt/m365-samoobsluga
ExecStart=/home/<uzytkownik>/.dotnet/dotnet /opt/m365-samoobsluga/M365Samoobsluga.Web.dll
Restart=always
User=m365app
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://127.0.0.1:30375
Environment=Samoobsluga__UseMockServices=false
Environment=Samoobsluga__Admins__0=twoj.login@example.org
EnvironmentFile=/etc/m365-samoobsluga.env

[Install]
WantedBy=multi-user.target
```

Sekrety daj do `/etc/m365-samoobsluga.env` (`chmod 600`, właściciel root), na przykład `AzureAd__ClientCredentials__0__SourceType=ClientSecret` i `AzureAd__ClientCredentials__0__ClientSecret=...`. Na produkcji lepszy jest certyfikat (sekcja „Logowanie aplikacji certyfikatem”). Potem:

```bash
sudo systemctl daemon-reload && sudo systemctl enable --now m365-samoobsluga
sudo journalctl -u m365-samoobsluga -f   # tu zobaczysz kod konfiguracji
```

Fragment nginx (certyfikat np. z Let's Encrypt). Blazor potrzebuje WebSocketów:

```nginx
location / {
    proxy_pass http://127.0.0.1:30375;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $remote_addr;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_read_timeout 100s;
}
```

Adresy w Entra podajesz wtedy z domeną: `https://twoja-domena/signin-oidc` i pozostałe z tabeli wyżej.

### Mikrus i Cloudflare

Na serwerze Mikrus (wiki.mikr.us) domenę z Cloudflare podpina się rekordem AAAA na adres IPv6 serwera, z włączonym proxy, a serwer WWW musi nasłuchiwać na porcie 80 po IPv6. Skrypt tak konfiguruje nginx. Mikrus zaleca tryb SSL/TLS Flexible, ale aplikacja obsługuje hasła i metody logowania, więc połączenie Cloudflare z serwerem lepiej szyfrować: zainstaluj certyfikat certbotem i ustaw Full (strict). Nginx przekazuje aplikacji schemat z nagłówka `X-Forwarded-Proto` od Cloudflare, więc adresy logowania są poprawne w obu trybach. Pętla przekierowań w trybie Flexible bierze się z tego, że przekierowanie na https robią jednocześnie Cloudflare i serwer; wtedy włącz Always Use HTTPS w Cloudflare i nie przekierowuj na https w nginx. Porty 20000+ID i 30000+ID z panelu Mikrusa są bezpośrednio otwarte, ale bez https, więc Entra ich nie przyjmie.

## Bezpieczeństwo

- Aplikacja nie przechowuje haseł ani tokenów poza mechanizmami frameworka (ciasteczko sesji, pamięciowy cache tokenów).
- Każda strona wymaga zalogowania. Odpowiedzi mają nagłówki `X-Content-Type-Options`, `X-Frame-Options` i `Referrer-Policy`.
- Hasła nie trafiają do logów, a pola formularza są czyszczone po próbie zmiany.
- Alias i adres przekierowania są walidowane na serwerze: tylko zweryfikowane domeny, a przekierowanie na własny adres jest odrzucane jako pętla.
- Numery telefonów w metodach 2FA są maskowane, widać tylko dwie ostatnie cyfry.
