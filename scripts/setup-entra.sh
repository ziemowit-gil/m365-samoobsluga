#!/usr/bin/env bash
# Automatyczna konfiguracja rejestracji aplikacji w Entra ID dla Samoobsługi poczty Microsoft 365.
# Tworzy (albo aktualizuje) rejestrację, dodaje adresy przekierowania i uprawnienia delegowane Microsoft Graph,
# udziela zgody administratora, tworzy sekret klienta lub dodaje certyfikat i zapisuje identyfikatory dla aplikacji.
#
# Wymaga Azure CLI (az) i konta z uprawnieniami do rejestracji aplikacji oraz zgody administratora
# (Global Administrator, Privileged Role Administrator albo Cloud Application Administrator + zgoda).
# Zaloguj się wcześniej:  az login   (ten skrypt niczego nie loguje i nie zapisuje sekretów w repozytorium).
#
# Przykład (sprawdź plan bez zmian):
#   ./scripts/setup-entra.sh --base-url https://ms365webpanel.feer.org.pl --dry-run
# Przykład (wykonaj i zapisz sekret oraz identyfikatory na serwerze):
#   sudo ./scripts/setup-entra.sh --base-url https://ms365webpanel.feer.org.pl \
#        --write-env /etc/m365-samoobsluga.env --write-azuread /opt/m365-samoobsluga/App_Data/azuread.json
set -euo pipefail

NAME="Samoobsługa poczty Microsoft 365"
BASE_URL=""
CERT=""
WRITE_ENV=""
WRITE_AZUREAD=""
REQUIRE_ASSIGNMENT=0
SKIP_CONSENT=0
DRY_RUN=0
SECRET_YEARS=1
GRAPH_APP_ID="00000003-0000-0000-c000-000000000000"   # Microsoft Graph (stały identyfikator usługi)
# Uprawnienia delegowane (zakresy), zgodne z README: wartość -> po co
SCOPES=(User.Read MailboxSettings.ReadWrite User-PasswordProfile.ReadWrite.All UserAuthenticationMethod.ReadWrite)

usage() {
  cat <<EOF
Użycie: $0 --base-url <https://adres-aplikacji> [opcje]
  --base-url <adres>        adres aplikacji, np. https://ms365webpanel.feer.org.pl lub http://localhost:30375 (wymagane)
  --name <nazwa>            nazwa rejestracji (domyślnie: $NAME)
  --cert <plik.pem|.cer>    dodaj certyfikat zamiast tworzyć sekret klienta (zalecane na produkcji)
  --secret-years <n>        ważność sekretu w latach (domyślnie $SECRET_YEARS)
  --write-env <plik>        dopisz poświadczenia do pliku środowiskowego (np. /etc/m365-samoobsluga.env), prawa 600
  --write-azuread <plik>    zapisz Tenant ID i Client ID (np. .../App_Data/azuread.json)
  --require-assignment      wymagaj przypisania użytkowników do aplikacji (Enterprise applications)
  --skip-consent            nie udzielaj zgody administratora (zrobisz to ręcznie w Entra)
  --dry-run                 tylko pokaż plan, niczego nie zmieniaj
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --base-url) BASE_URL="${2:-}"; shift 2 ;;
    --name) NAME="${2:-}"; shift 2 ;;
    --cert) CERT="${2:-}"; shift 2 ;;
    --secret-years) SECRET_YEARS="${2:-}"; shift 2 ;;
    --write-env) WRITE_ENV="${2:-}"; shift 2 ;;
    --write-azuread) WRITE_AZUREAD="${2:-}"; shift 2 ;;
    --require-assignment) REQUIRE_ASSIGNMENT=1; shift ;;
    --skip-consent) SKIP_CONSENT=1; shift ;;
    --dry-run) DRY_RUN=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Nieznana opcja: $1" >&2; usage >&2; exit 2 ;;
  esac
done

[[ -n "$BASE_URL" ]] || { echo "Brak --base-url." >&2; usage >&2; exit 2; }
if [[ "$BASE_URL" =~ ^https://[A-Za-z0-9.-]+(:[0-9]+)?/?$ || "$BASE_URL" =~ ^http://(localhost|127\.0\.0\.1)(:[0-9]+)?/?$ ]]; then :; else
  echo "Adres musi mieć postać https://host[:port] (http tylko dla localhost): $BASE_URL" >&2; exit 2
fi
[[ "$SECRET_YEARS" =~ ^[1-2]$ ]] || { echo "--secret-years: 1 albo 2." >&2; exit 2; }
[[ -z "$CERT" || -f "$CERT" ]] || { echo "Nie znaleziono pliku certyfikatu: $CERT" >&2; exit 2; }
BASE_URL="${BASE_URL%/}"

REDIRECT_URIS=("$BASE_URL/signin-oidc" "$BASE_URL/signout-callback-oidc")
LOGOUT_URL="$BASE_URL/signout-oidc"

echo "Plan:"
echo "  Rejestracja:        $NAME (jedna dzierżawa)"
echo "  Redirect URI (Web): ${REDIRECT_URIS[*]}"
echo "  Front-channel logout URL: $LOGOUT_URL"
echo "  Uprawnienia Graph (delegowane): ${SCOPES[*]}"
echo "  Zgoda administratora: $([[ $SKIP_CONSENT -eq 1 ]] && echo pominięta || echo tak)"
echo "  Poświadczenie:      $([[ -n "$CERT" ]] && echo "certyfikat z $CERT" || echo "sekret klienta ($SECRET_YEARS r.)")"
[[ -n "$WRITE_ENV" ]] && echo "  Zapis poświadczeń do: $WRITE_ENV"
[[ -n "$WRITE_AZUREAD" ]] && echo "  Zapis Tenant ID i Client ID do: $WRITE_AZUREAD"
[[ $REQUIRE_ASSIGNMENT -eq 1 ]] && echo "  Wymagane przypisanie użytkowników: tak"
if [[ $DRY_RUN -eq 1 ]]; then echo; echo "Tryb --dry-run: niczego nie zmieniono."; exit 0; fi

command -v az >/dev/null || { echo "Brak Azure CLI (az). Instalacja: https://learn.microsoft.com/cli/azure/install-azure-cli" >&2; exit 1; }
az account show >/dev/null 2>&1 || { echo "Nie jesteś zalogowany. Uruchom: az login" >&2; exit 1; }
TENANT_ID="$(az account show --query tenantId -o tsv)"
echo; echo "Dzierżawa: $TENANT_ID, konto: $(az account show --query user.name -o tsv)"
read -r -p "Zmienić rejestrację aplikacji w tej dzierżawie? [t/N] " ans
[[ "$ans" =~ ^[tTyY]$ ]] || { echo "Przerwano."; exit 1; }

echo "==> Rejestracja aplikacji"
APP_ID="$(az ad app list --display-name "$NAME" --query "[0].appId" -o tsv)"
if [[ -z "$APP_ID" ]]; then
  APP_ID="$(az ad app create --display-name "$NAME" --sign-in-audience AzureADMyOrg --web-redirect-uris "${REDIRECT_URIS[@]}" --query appId -o tsv)"
  echo "    utworzono, Client ID: $APP_ID"
else
  # Zachowujemy istniejące adresy i dopisujemy brakujące (replace nadpisałoby inne środowiska).
  current=()
  while IFS= read -r line; do [[ -n "$line" ]] && current+=("$line"); done < <(az ad app show --id "$APP_ID" --query "web.redirectUris[]" -o tsv)
  merged=(${current[@]+"${current[@]}"})
  for u in "${REDIRECT_URIS[@]}"; do [[ " ${current[*]-} " == *" $u "* ]] || merged+=("$u"); done
  az ad app update --id "$APP_ID" --web-redirect-uris "${merged[@]}"
  echo "    istnieje, Client ID: $APP_ID (adresy uzupełnione)"
fi
az ad app update --id "$APP_ID" --set "web.logoutUrl=$LOGOUT_URL" >/dev/null \
  || echo "    Ostrzeżenie: nie ustawiono Front-channel logout URL. Wpisz go ręcznie: $LOGOUT_URL" >&2

echo "==> Uprawnienia Microsoft Graph (identyfikatory zakresów odczytane z Twojej dzierżawy)"
perms=()
for s in "${SCOPES[@]}"; do
  id="$(az ad sp show --id "$GRAPH_APP_ID" --query "oauth2PermissionScopes[?value=='$s'].id | [0]" -o tsv)"
  [[ -n "$id" ]] || { echo "Nie znaleziono zakresu $s w Microsoft Graph." >&2; exit 1; }
  perms+=("$id=Scope")
done
az ad app permission add --id "$APP_ID" --api "$GRAPH_APP_ID" --api-permissions "${perms[@]}" >/dev/null

echo "==> Jednostka usługi (Enterprise application)"
az ad sp show --id "$APP_ID" >/dev/null 2>&1 || az ad sp create --id "$APP_ID" >/dev/null
if [[ $REQUIRE_ASSIGNMENT -eq 1 ]]; then
  SP_OBJ="$(az ad sp show --id "$APP_ID" --query id -o tsv)"
  az ad sp update --id "$SP_OBJ" --set appRoleAssignmentRequired=true
  echo "    wymagane przypisanie użytkowników. Dodaj ich w Enterprise applications → Users and groups."
fi

if [[ $SKIP_CONSENT -eq 0 ]]; then
  echo "==> Zgoda administratora"
  ok=0
  for i in 1 2 3 4 5; do
    if az ad app permission admin-consent --id "$APP_ID" >/dev/null 2>&1; then ok=1; break; fi
    echo "    próba $i nieudana (replikacja w Entra może trwać chwilę), czekam…"; sleep 8
  done
  [[ $ok -eq 1 ]] && echo "    zgoda udzielona" || echo "    Nie udało się udzielić zgody. Zrób to ręcznie: API permissions → Grant admin consent." >&2
fi

echo "==> Poświadczenie aplikacji"
umask 077
upsert_env() { # klucz wartość: usuń stare wpisy i dopisz nowy (plik prywatny, wartość nie trafia na ekran)
  local key="$1" val="$2" file="$WRITE_ENV"
  touch "$file"; chmod 600 "$file"
  grep -v -E "^#?${key}=" "$file" > "$file.tmp" || true
  printf '%s=%s\n' "$key" "$val" >> "$file.tmp"
  mv "$file.tmp" "$file"; chmod 600 "$file"
}
if [[ -n "$CERT" ]]; then
  az ad app credential reset --id "$APP_ID" --cert "@$CERT" --append >/dev/null
  echo "    certyfikat dodany. Ustaw w konfiguracji aplikacji SourceType (np. StoreWithThumbprint), zob. README."
else
  SECRET="$(az ad app credential reset --id "$APP_ID" --display-name "m365-samoobsluga" --years "$SECRET_YEARS" --append --query password -o tsv)"
  if [[ -n "$WRITE_ENV" ]]; then
    upsert_env "AzureAd__ClientCredentials__0__SourceType" "ClientSecret"
    upsert_env "AzureAd__ClientCredentials__0__ClientSecret" "$SECRET"
    echo "    sekret zapisany w $WRITE_ENV (nie jest wyświetlany)"
  else
    echo "    Sekret utworzony, ale nie zapisany (brak --write-env). Wyświetlam go TYLKO TERAZ, skopiuj do menedżera haseł:"
    echo "    $SECRET"
  fi
  unset SECRET
fi

if [[ -n "$WRITE_AZUREAD" ]]; then
  mkdir -p "$(dirname "$WRITE_AZUREAD")"
  printf '{\n  "AzureAd": { "TenantId": "%s", "ClientId": "%s" }\n}\n' "$TENANT_ID" "$APP_ID" > "$WRITE_AZUREAD"
  echo "    Tenant ID i Client ID zapisane w $WRITE_AZUREAD"
fi

cat <<EOF

Gotowe.
  Tenant ID: $TENANT_ID
  Client ID: $APP_ID
Dalej: zrestartuj aplikację (sudo systemctl restart m365-samoobsluga) i zaloguj się pod $BASE_URL.
EOF
