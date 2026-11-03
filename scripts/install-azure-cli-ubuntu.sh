#!/usr/bin/env bash
# Instalacja Azure CLI (az) na Ubuntu, także na wersjach, dla których Microsoft nie ma jeszcze własnego wydania repozytorium
# (np. 26.04 "resolute"). W takim przypadku używa repozytorium dla Ubuntu 24.04 (noble); pakiet azure-cli ma własny Python.
# Uruchom jako root:  sudo ./scripts/install-azure-cli-ubuntu.sh
set -euo pipefail
[[ $EUID -eq 0 ]] || { echo "Uruchom jako root: sudo $0" >&2; exit 1; }

if command -v az >/dev/null 2>&1; then echo "Azure CLI już jest: $(az version --query '"azure-cli"' -o tsv)"; exit 0; fi

# Usuń wpis dopisany wcześniej przez instalator Microsoftu z niewłaściwym wydaniem (jeśli istnieje).
rm -f /etc/apt/sources.list.d/azure-cli.sources /etc/apt/sources.list.d/azure-cli.list

apt-get install -y -qq ca-certificates curl gnupg apt-transport-https >/dev/null
mkdir -p /etc/apt/keyrings
curl -fsSL https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor --yes -o /etc/apt/keyrings/microsoft.gpg
chmod go+r /etc/apt/keyrings/microsoft.gpg

SUITE="$(lsb_release -cs 2>/dev/null || . /etc/os-release && echo "${VERSION_CODENAME:-noble}")"
# Sprawdź, czy repozytorium Microsoftu ma wydanie dla tej wersji Ubuntu; jeśli nie, użyj noble.
if ! curl -fsI "https://packages.microsoft.com/repos/azure-cli/dists/$SUITE/InRelease" >/dev/null 2>&1; then
  echo "Brak wydania '$SUITE' w repozytorium Azure CLI, używam 'noble' (Ubuntu 24.04)."
  SUITE=noble
fi
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/microsoft.gpg] https://packages.microsoft.com/repos/azure-cli/ $SUITE main" > /etc/apt/sources.list.d/azure-cli.list

# Błędy w cudzych, uszkodzonych repozytoriach nie powinny przerywać instalacji.
apt-get update -qq || echo "Ostrzeżenie: apt-get update zgłosił błędy w innych repozytoriach. Kontynuuję." >&2
apt-get install -y azure-cli
echo "Zainstalowano: $(az version --query '"azure-cli"' -o tsv)"
