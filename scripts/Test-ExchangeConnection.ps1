<#
.SYNOPSIS
  Sprawdza, czy aplikacja potrafi połączyć się z Exchange Online certyfikatem i odczytać reguły transportowe (tylko odczyt).
  Przed połączeniem sprawdza osobno plik certyfikatu i hasło, żeby błąd wskazywał właściwą przyczynę.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$AppId,
    [Parameter(Mandatory = $true)][string]$CertificateFilePath,
    [Parameter(Mandatory = $true)][string]$Organization
)
$ErrorActionPreference = 'Stop'

function Fail([string]$msg) { Write-Host "BŁĄD: $msg" -ForegroundColor Red; exit 1 }

# 1. Moduł
$mod = Get-Module -ListAvailable -Name ExchangeOnlineManagement | Sort-Object Version -Descending | Select-Object -First 1
if (-not $mod) { Fail 'Brak modułu ExchangeOnlineManagement (scripts/install-exchange-tools-ubuntu.sh).' }
Write-Host "Moduł ExchangeOnlineManagement $($mod.Version), PowerShell $($PSVersionTable.PSVersion)."

# 2. Domena organizacji: dla uwierzytelniania certyfikatem Microsoft wymaga głównej domeny .onmicrosoft.com
if ($Organization -notlike '*.onmicrosoft.com') {
    Write-Host "UWAGA: domena '$Organization' nie kończy się na .onmicrosoft.com. Przy logowaniu certyfikatem użyj głównej domeny organizacji, np. firma.onmicrosoft.com." -ForegroundColor Yellow
}

# 3. Plik certyfikatu i hasło
if (-not (Test-Path -LiteralPath $CertificateFilePath)) { Fail "Nie ma pliku certyfikatu: $CertificateFilePath" }
if (-not $env:M365_EXO_CERT_PASSWORD) { Fail 'Brak hasła do pliku certyfikatu (zmienna M365_EXO_CERT_PASSWORD). Wygeneruj certyfikat w kreatorze albo ustaw hasło.' }
try {
    $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($CertificateFilePath, $env:M365_EXO_CERT_PASSWORD)
} catch { Fail "Nie można otworzyć pliku certyfikatu (złe hasło lub uszkodzony plik): $($_.Exception.Message)" }
if (-not $cert.HasPrivateKey) { Fail 'Certyfikat nie zawiera klucza prywatnego.' }
if ($cert.NotAfter -lt (Get-Date)) { Fail "Certyfikat wygasł $($cert.NotAfter.ToString('yyyy-MM-dd'))." }
Write-Host "Certyfikat OK: odcisk $($cert.Thumbprint), ważny do $($cert.NotAfter.ToString('yyyy-MM-dd')). Ten odcisk musi być widoczny w Entra (Certyfikaty i wpisy tajne) w rejestracji $AppId."

# 3b. Token bezpośrednio (MSAL z modułu): pokazuje prawdziwy błąd Microsoftu (AADSTS...), który moduł ukrywa pod "Object reference not set"
try {
    $msal = Get-ChildItem -Path $mod.ModuleBase -Recurse -Filter 'Microsoft.Identity.Client.dll' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($msal) {
        Add-Type -Path $msal.FullName -ErrorAction Stop
        $app = [Microsoft.Identity.Client.ConfidentialClientApplicationBuilder]::Create($AppId).WithCertificate($cert).WithTenantId($Organization).Build()
        $res = $app.AcquireTokenForClient([string[]]@('https://outlook.office365.com/.default')).ExecuteAsync().GetAwaiter().GetResult()
        $p = $res.AccessToken.Split('.')[1].Replace('-', '+').Replace('_', '/')
        $p = $p.PadRight($p.Length + (4 - $p.Length % 4) % 4, '=')
        $claims = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($p)) | ConvertFrom-Json
        $roles = if ($claims.roles) { $claims.roles -join ', ' } else { '(brak)' }
        Write-Host "Token uzyskany. Aplikacja: $($claims.appid), dzierżawa: $($claims.tid), role w tokenie: $roles"
        if ($roles -notlike '*Exchange.ManageAsApp*') {
            Write-Host 'UWAGA: w tokenie nie ma Exchange.ManageAsApp. Dodaj uprawnienie aplikacyjne Office 365 Exchange Online -> Exchange.ManageAsApp i udziel zgody administratora w Entra.' -ForegroundColor Yellow
        }
    }
} catch {
    $e = $_.Exception; while ($e.InnerException) { $e = $e.InnerException }
    $code = if ($e.PSObject.Properties['ErrorCode']) { $e.ErrorCode } else { $e.GetType().Name }
    $m = $e.Message; if ($m.Length -gt 600) { $m = $m.Substring(0, 600) + '...' }
    Write-Host "BŁĄD pobrania tokenu ($code): $m" -ForegroundColor Red
    Write-Host 'To jest prawdziwa przyczyna. Częste: AADSTS700027/700016 (certyfikat .cer nie jest wgrany w tej rejestracji albo zły Client ID), AADSTS90002 (zła domena organizacji), AADSTS7000112 (aplikacja wyłączona).' -ForegroundColor Yellow
    exit 1
}

# 4. Połączenie certyfikatem (obiekt certyfikatu zamiast ścieżki i hasła)
Import-Module ExchangeOnlineManagement
try {
    . (Join-Path $PSScriptRoot 'ExoAuth.ps1')
    Connect-ExoAppOnly -AppId $AppId -CertificateFilePath $CertificateFilePath -Organization $Organization
    $rules = @(Get-TransportRule -ResultSize 1000)
    $mine = $rules | Where-Object { $_.Name -eq 'Samoobsługa FEER: stopka e-mail' }
    Write-Host "Połączenie działa. Reguł transportowych: $($rules.Count). Reguła stopki: $(if ($mine) { "istnieje (stan $($mine.State), tryb $($mine.Mode))" } else { 'jeszcze nie istnieje' })."
}
catch {
    Write-Host ("BŁĄD połączenia: " + $_.Exception.GetType().FullName + ": " + $_.Exception.Message) -ForegroundColor Red
    if ($_.Exception.InnerException) { Write-Host ("Wewnętrzny: " + $_.Exception.InnerException.Message) -ForegroundColor Red }
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
    Write-Host 'Podpowiedź: błąd 401/AADSTS oznacza brak zgody lub niezreplikowaną rolę (poczekaj 15-30 minut); NullReference zwykle oznacza złą domenę organizacji albo nieudane uwierzytelnienie certyfikatem.' -ForegroundColor Yellow
    exit 1
}
finally { Disconnect-ExchangeOnline -Confirm:$false -ErrorAction SilentlyContinue }
