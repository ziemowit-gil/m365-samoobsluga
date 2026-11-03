#!/usr/bin/env bash
# Zarządzanie administratorami aplikacji (kto widzi strony "Administrator: ..."). Uruchom na serwerze jako root.
#   sudo ./scripts/admin.sh add migracja@feer.org.pl
#   sudo ./scripts/admin.sh remove migracja@feer.org.pl
#   sudo ./scripts/admin.sh list
# Administratorzy są zapisani jako Samoobsluga__Admins__<n>: pierwszy w usłudze systemd (z install-ubuntu.sh --admin),
# kolejni w /etc/m365-samoobsluga.env. Skrypt zmienia tylko plik .env i restartuje usługę.
set -euo pipefail

SERVICE=m365-samoobsluga
ENV_FILE="${M365_ENV_FILE:-/etc/m365-samoobsluga.env}"   # M365_ENV_FILE służy do testów na innym pliku
PREFIX="Samoobsluga__Admins__"

usage() { echo "Użycie: sudo $0 add|remove <login@domena>  albo  sudo $0 list" >&2; }
[[ $EUID -eq 0 || -n "${M365_ENV_FILE:-}" ]] || { echo "Uruchom jako root: sudo $0 ..." >&2; exit 1; }
cmd="${1:-}"; upn="${2:-}"
case "$cmd" in add|remove) [[ -n "$upn" ]] || { usage; exit 2; } ;; list) ;; *) usage; exit 2 ;; esac
if [[ -n "$upn" && ! "$upn" =~ ^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+$ ]]; then echo "Nieprawidłowy login: $upn" >&2; exit 2; fi
upn="$(printf '%s' "$upn" | tr '[:upper:]' '[:lower:]')"
touch "$ENV_FILE"; chmod 600 "$ENV_FILE"

# Wszyscy dotychczasowi administratorzy: "indeks wartość", z usługi i z pliku .env.
current() {
  { systemctl show -p Environment --value "$SERVICE" 2>/dev/null | tr ' ' '\n'
    grep -E "^${PREFIX}[0-9]+=" "$ENV_FILE" || true; } \
  | grep -E "^${PREFIX}[0-9]+=" | sed -E "s/^${PREFIX}([0-9]+)=(.*)$/\1 \2/" | tr '[:upper:]' '[:lower:]' | sort -n -u
}

case "$cmd" in
  list)
    current | awk '{print $2}'
    ;;
  add)
    if current | awk '{print $2}' | grep -qx "$upn"; then echo "$upn już jest administratorem."; exit 0; fi
    next=$(( $(current | awk 'BEGIN{m=-1} {if ($1>m) m=$1} END{print m}') + 1 ))
    printf '%s%s=%s\n' "$PREFIX" "$next" "$upn" >> "$ENV_FILE"
    echo "Dodano administratora: $upn (indeks $next)."
    systemctl restart "$SERVICE"
    ;;
  remove)
    if grep -q -i -E "^${PREFIX}[0-9]+=${upn}\$" "$ENV_FILE"; then
      grep -v -i -E "^${PREFIX}[0-9]+=${upn}\$" "$ENV_FILE" > "$ENV_FILE.tmp" || true
      mv "$ENV_FILE.tmp" "$ENV_FILE"; chmod 600 "$ENV_FILE"
      echo "Usunięto administratora: $upn."
      systemctl restart "$SERVICE"
    elif systemctl show -p Environment --value "$SERVICE" | tr ' ' '\n' | grep -q -i -E "^${PREFIX}[0-9]+=${upn}\$"; then
      echo "To konto jest ustawione w pliku usługi (--admin z install-ubuntu.sh). Zmień je ponownym uruchomieniem install-ubuntu.sh." >&2; exit 1
    else
      echo "$upn nie jest administratorem."
    fi
    ;;
esac
