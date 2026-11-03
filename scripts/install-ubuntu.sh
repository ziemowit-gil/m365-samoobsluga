#!/usr/bin/env bash
# Instalacja i aktualizacja Samoobsługi poczty Microsoft 365 na Ubuntu (usługa systemd, opcjonalnie nginx).
# Uruchom z katalogu repozytorium:  sudo ./scripts/install-ubuntu.sh --admin twoj.login@feer.org.pl [--domain poczta.example.org]
# Skrypt można uruchamiać wielokrotnie: przy ponownym uruchomieniu publikuje nową wersję i restartuje usługę.
# Nie zapisuje żadnych sekretów. Sekrety wpisujesz sam do /etc/m365-samoobsluga.env.
set -euo pipefail

APP_DIR=/opt/m365-samoobsluga
SERVICE=m365-samoobsluga
ENV_FILE=/etc/m365-samoobsluga.env
APP_USER=m365app
PORT=30375
DOTNET_DIR=/usr/share/dotnet
ADMIN=""
DOMAIN=""

usage() {
  cat <<EOF
Użycie: sudo $0 --admin <login@domena> [--domain <host>] [--port <port>]
  --admin   konto, które zostanie administratorem ustawień (UPN, wymagane)
  --domain  domena aplikacji; jeśli podana, skrypt doda konfigurację nginx (HTTPS załatw certbotem)
  --port    port lokalny aplikacji (domyślnie $PORT)
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --admin)  ADMIN="${2:-}"; shift 2 ;;
    --domain) DOMAIN="${2:-}"; shift 2 ;;
    --port)   PORT="${2:-}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Nieznana opcja: $1" >&2; usage >&2; exit 2 ;;
  esac
done

[[ $EUID -eq 0 ]] || { echo "Uruchom jako root: sudo $0 ..." >&2; exit 1; }
[[ -n "$ADMIN" ]] || { echo "Brak --admin." >&2; usage >&2; exit 2; }
[[ "$ADMIN" =~ ^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+$ ]] || { echo "Nieprawidłowy login administratora: $ADMIN" >&2; exit 2; }
[[ "$PORT" =~ ^[0-9]+$ ]] && (( PORT > 0 && PORT < 65536 )) || { echo "Nieprawidłowy port: $PORT" >&2; exit 2; }
[[ -z "$DOMAIN" || "$DOMAIN" =~ ^[A-Za-z0-9.-]+$ ]] || { echo "Nieprawidłowa domena: $DOMAIN" >&2; exit 2; }

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
[[ -f "$REPO_DIR/M365Samoobsluga.sln" ]] || { echo "Nie znaleziono repozytorium w $REPO_DIR." >&2; exit 1; }
if [[ "$REPO_DIR" == "$APP_DIR" || "$REPO_DIR" == "$APP_DIR"/* ]]; then
  echo "Repozytorium leży w $APP_DIR, czyli tam, gdzie skrypt publikuje aplikację. Przenieś je, np.:" >&2
  echo "  mv $REPO_DIR /usr/local/src/m365-samoobsluga && cd /usr/local/src/m365-samoobsluga && sudo ./scripts/install-ubuntu.sh ..." >&2
  exit 1
fi

echo "==> Pakiety systemowe"
apt-get update -qq
apt-get install -y -qq curl ca-certificates libicu-dev >/dev/null

if [[ ! -x "$DOTNET_DIR/dotnet" ]] || ! "$DOTNET_DIR/dotnet" --list-sdks | grep -q '^10\.'; then
  echo "==> Instalacja .NET SDK 10 w $DOTNET_DIR"
  tmp="$(mktemp)"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$tmp"
  bash "$tmp" --channel 10.0 --install-dir "$DOTNET_DIR" >/dev/null
  rm -f "$tmp"
  ln -sf "$DOTNET_DIR/dotnet" /usr/local/bin/dotnet
else
  echo "==> .NET 10 już jest zainstalowany"
fi
export DOTNET_ROOT="$DOTNET_DIR" DOTNET_CLI_TELEMETRY_OPTOUT=1

echo "==> Publikacja do $APP_DIR"
id -u "$APP_USER" >/dev/null 2>&1 || useradd --system --home "$APP_DIR" --shell /usr/sbin/nologin "$APP_USER"
systemctl stop "$SERVICE" 2>/dev/null || true
# App_Data (ustawienia, kolejka wniosków, szablony) zostaje przy aktualizacji, bo publikujemy obok niego.
dotnet publish "$REPO_DIR/src/M365Samoobsluga.Web" -c Release -o "$APP_DIR" --nologo -v q
mkdir -p "$APP_DIR/App_Data"
chown -R "$APP_USER": "$APP_DIR"

if [[ ! -f "$ENV_FILE" ]]; then
  echo "==> Tworzę pusty plik na sekrety: $ENV_FILE"
  cat > "$ENV_FILE" <<'EOF'
# Sekrety aplikacji. Plik ma prawa 600. Przykład (sekret klienta; na produkcji lepszy certyfikat, zob. README):
# AzureAd__ClientCredentials__0__SourceType=ClientSecret
# AzureAd__ClientCredentials__0__ClientSecret=wpisz-sekret
EOF
fi
chown root:root "$ENV_FILE"; chmod 600 "$ENV_FILE"

echo "==> Usługa systemd"
cat > "/etc/systemd/system/$SERVICE.service" <<EOF
[Unit]
Description=Samoobsluga poczty Microsoft 365
After=network.target

[Service]
WorkingDirectory=$APP_DIR
ExecStart=$DOTNET_DIR/dotnet $APP_DIR/M365Samoobsluga.Web.dll
Restart=always
RestartSec=5
User=$APP_USER
Environment=DOTNET_ROOT=$DOTNET_DIR
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://127.0.0.1:$PORT
Environment=Samoobsluga__UseMockServices=false
Environment=Samoobsluga__Admins__0=$ADMIN
EnvironmentFile=$ENV_FILE
NoNewPrivileges=true
ProtectSystem=full
ReadWritePaths=$APP_DIR/App_Data

[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload
systemctl enable --now "$SERVICE" >/dev/null

if [[ -n "$DOMAIN" ]]; then
  echo "==> nginx dla $DOMAIN"
  apt-get install -y -qq nginx >/dev/null
  cat > "/etc/nginx/sites-available/$SERVICE" <<EOF
server {
    listen 80;
    listen [::]:80;
    server_name $DOMAIN;

    location / {
        proxy_pass http://127.0.0.1:$PORT;
        proxy_http_version 1.1;
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host \$host;
        proxy_set_header X-Forwarded-For \$remote_addr;
        proxy_set_header X-Forwarded-Proto \$scheme;
        proxy_read_timeout 100s;
    }
}
EOF
  ln -sf "/etc/nginx/sites-available/$SERVICE" "/etc/nginx/sites-enabled/$SERVICE"
  nginx -t && systemctl reload nginx
  echo "    HTTPS: sudo apt install certbot python3-certbot-nginx && sudo certbot --nginx -d $DOMAIN"
fi

sleep 3
systemctl is-active --quiet "$SERVICE" && echo "==> Usługa działa" || { echo "Usługa nie wystartowała. Zobacz: journalctl -u $SERVICE -n 50" >&2; exit 1; }

BASE="${DOMAIN:+https://$DOMAIN}"; BASE="${BASE:-http://localhost:$PORT}"
cat <<EOF

Gotowe. Dalsze kroki:
 1. Kod pierwszej konfiguracji:  journalctl -u $SERVICE -n 50 --no-pager | grep "Kod konfiguracji"
 2. Otwórz $BASE/setup, wpisz Tenant ID, Client ID i kod, potem: sudo systemctl restart $SERVICE
 3. Sekret lub certyfikat aplikacji wpisz w $ENV_FILE i zrestartuj usługę.
 4. W Entra (Authentication → Web) dodaj:
      $BASE/signin-oidc
      $BASE/signout-callback-oidc
    oraz Front-channel logout URL: $BASE/signout-oidc
EOF
