#!/usr/bin/env bash
# Instaluje PowerShell 7 i moduł ExchangeOnlineManagement na Ubuntu (bez repozytorium Microsoftu, więc działa też na 26.04),
# potrzebne do wdrażania reguły stopki z aplikacji. Uruchom jako root:  sudo ./scripts/install-exchange-tools-ubuntu.sh
set -euo pipefail
[[ $EUID -eq 0 ]] || { echo "Uruchom jako root: sudo $0" >&2; exit 1; }

if ! command -v pwsh >/dev/null 2>&1; then
  case "$(uname -m)" in x86_64) arch=x64 ;; aarch64) arch=arm64 ;; *) echo "Nieobsługiwana architektura: $(uname -m)" >&2; exit 1 ;; esac
  apt-get install -y -qq curl ca-certificates libicu-dev >/dev/null
  tag="$(curl -fsSL https://api.github.com/repos/PowerShell/PowerShell/releases/latest | sed -n 's/.*"tag_name": *"v\([^"]*\)".*/\1/p' | head -1)"
  [[ -n "$tag" ]] || { echo "Nie udało się ustalić wersji PowerShella (GitHub API)." >&2; exit 1; }
  url="https://github.com/PowerShell/PowerShell/releases/download/v$tag/powershell-$tag-linux-$arch.tar.gz"
  echo "==> Pobieram PowerShell $tag ($arch)"
  tmp="$(mktemp)"; curl -fsSL "$url" -o "$tmp"
  mkdir -p /opt/microsoft/powershell/7
  tar -xzf "$tmp" -C /opt/microsoft/powershell/7; rm -f "$tmp"
  chmod +x /opt/microsoft/powershell/7/pwsh
  ln -sf /opt/microsoft/powershell/7/pwsh /usr/local/bin/pwsh
else
  echo "==> PowerShell już jest: $(pwsh --version)"
fi

echo "==> Moduły ExchangeOnlineManagement i Microsoft.Graph.Applications"
pwsh -NoProfile -Command "Set-PSRepository PSGallery -InstallationPolicy Trusted; Install-Module ExchangeOnlineManagement -Scope AllUsers -Force; Install-Module Microsoft.Graph.Applications -Scope AllUsers -Force; (Get-Module -ListAvailable ExchangeOnlineManagement | Select-Object -First 1).Version.ToString()"
echo "Gotowe. Dalej: certyfikat aplikacji i ustawienia Samoobsluga:Exchange (README, sekcja o wdrażaniu reguły z aplikacji)."
