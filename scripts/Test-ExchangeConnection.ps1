<#
.SYNOPSIS
  Sprawdza, czy aplikacja potrafi połączyć się z Exchange Online certyfikatem i odczytać reguły transportowe (tylko odczyt).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$AppId,
    [Parameter(Mandatory = $true)][string]$CertificateFilePath,
    [Parameter(Mandatory = $true)][string]$Organization
)
$ErrorActionPreference = 'Stop'
Import-Module ExchangeOnlineManagement
$conn = @{ ShowBanner = $false; AppId = $AppId; CertificateFilePath = $CertificateFilePath; Organization = $Organization }
if ($env:M365_EXO_CERT_PASSWORD) { $conn += @{ CertificatePassword = (ConvertTo-SecureString -String $env:M365_EXO_CERT_PASSWORD -AsPlainText -Force) } }
try {
    Connect-ExchangeOnline @conn
    $rules = @(Get-TransportRule -ResultSize 1000)
    $mine = $rules | Where-Object { $_.Name -eq 'Samoobsługa FEER: stopka e-mail' }
    Write-Host "Połączenie działa. Reguł transportowych: $($rules.Count). Reguła stopki: $(if ($mine) { "istnieje (stan $($mine.State), tryb $($mine.Mode))" } else { 'jeszcze nie istnieje' })."
}
finally { Disconnect-ExchangeOnline -Confirm:$false -ErrorAction SilentlyContinue }
