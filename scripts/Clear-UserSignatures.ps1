<#
.SYNOPSIS
  Czyści zapisane przez użytkowników podpisy w skrzynkach (ustawienia podpisu Outlooka w przeglądarce), żeby nie dublowały stopki z reguły transportowej.

.DESCRIPTION
  Dla każdej skrzynki zapisuje najpierw kopię zapasową dotychczasowych podpisów do pliku JSON (-BackupFile), a potem
  wyłącza automatyczne dodawanie podpisu i usuwa jego treść (Set-MailboxMessageConfiguration).
  OGRANICZENIE: Microsoft zapisuje podpisy z Outlooka na komputerze i w nowym Outlooku jako "roaming signatures". Ten skrypt ich nie usuwa
  (zob. README). Skrypt nie zmienia podpisów lokalnych, zapisanych tylko na komputerze użytkownika.
  Z -WhatIf pokazuje, kogo dotknie zmiana, niczego nie zapisując.

.EXAMPLE
  ./Clear-UserSignatures.ps1 -All -BackupFile ./kopia-podpisow.json -WhatIf

.EXAMPLE
  ./Clear-UserSignatures.ps1 -Mailbox jan.kowalski@feer.org.pl -BackupFile ./kopia.json
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [string]$Mailbox,
    [switch]$All,
    [Parameter(Mandatory = $true)]
    [string]$BackupFile,

    [string]$ConnectUserPrincipalName,
    [string]$AppId,
    [string]$CertificateFilePath,
    [string]$Organization,
    # Bez certyfikatu skrypt loguje się kodem urządzenia (działa na serwerze bez przeglądarki). -Browser wymusza okno przeglądarki.
    [switch]$Browser,
    [switch]$SkipConnect,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
if (-not $All -and -not $Mailbox) { throw 'Podaj -Mailbox <adres> albo -All.' }
if ($All -and $Mailbox) { throw 'Użyj -All albo -Mailbox, nie obu naraz.' }
if ($Force -and -not $PSBoundParameters.ContainsKey('Confirm')) { $ConfirmPreference = 'None' }

if (-not $SkipConnect) {
    # -WhatIf nie może obejmować importu modułu ani logowania (inaczej wypisuje "What if: Set Alias" i nie łączy).
    $savedWhatIf = $WhatIfPreference; $WhatIfPreference = $false
    if (-not (Get-Module -ListAvailable -Name ExchangeOnlineManagement)) {
        throw 'Brak modułu ExchangeOnlineManagement. Zainstaluj: Install-Module ExchangeOnlineManagement -Scope CurrentUser'
    }
    Import-Module ExchangeOnlineManagement
    $conn = @{}
    if ($AppId -and $CertificateFilePath -and $Organization) {
        $pwd = $env:M365_EXO_CERT_PASSWORD
        $conn += @{ AppId = $AppId; CertificateFilePath = $CertificateFilePath; Organization = $Organization; ShowBanner = $false }
        if ($pwd) { $conn += @{ CertificatePassword = (ConvertTo-SecureString -String $pwd -AsPlainText -Force) } }
    } elseif ($ConnectUserPrincipalName) {
        $conn += @{ UserPrincipalName = $ConnectUserPrincipalName }
    }
    if (-not ($AppId -and $CertificateFilePath -and $Organization) -and -not ($AppId -and $CertificateThumbprint -and $Organization) -and -not $Browser) { $conn += @{ Device = $true } }
    Connect-ExchangeOnline @conn
    $WhatIfPreference = $savedWhatIf
}

try {
    $boxes = if ($All) { Get-Mailbox -RecipientTypeDetails UserMailbox -ResultSize Unlimited } else { @(Get-Mailbox -Identity $Mailbox) }
    $backup = @()
    $changed = 0
    foreach ($b in $boxes) {
        $upn = $b.UserPrincipalName
        $cfg = Get-MailboxMessageConfiguration -Identity $upn
        $has = $cfg.AutoAddSignature -or $cfg.AutoAddSignatureOnReply -or $cfg.SignatureHtml -or $cfg.SignatureText
        if (-not $has) { continue }
        $backup += [pscustomobject]@{
            UserPrincipalName       = $upn
            SignatureHtml           = $cfg.SignatureHtml
            SignatureText           = $cfg.SignatureText
            AutoAddSignature        = $cfg.AutoAddSignature
            AutoAddSignatureOnReply = $cfg.AutoAddSignatureOnReply
        }
        if ($PSCmdlet.ShouldProcess($upn, 'Wyczyszczenie zapisanego podpisu')) {
            # kopia zapasowa przed zmianą, żeby można było przywrócić
            $backup | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $BackupFile -Encoding UTF8
            Set-MailboxMessageConfiguration -Identity $upn -SignatureHtml '' -SignatureText '' -AutoAddSignature $false -AutoAddSignatureOnReply $false
            $changed++
        }
    }
    Write-Host "Skrzynek z zapisanym podpisem: $($backup.Count). Wyczyszczono: $changed. Kopia zapasowa: $BackupFile"
}
catch {
    # Dokładne miejsce błędu ułatwia diagnozę (np. wyjątki z wnętrza modułu Exchange).
    Write-Host ("BŁĄD: " + $_.Exception.GetType().FullName + ": " + $_.Exception.Message) -ForegroundColor Red
    if ($_.Exception.InnerException) { Write-Host ("Wewnętrzny: " + $_.Exception.InnerException.Message) -ForegroundColor Red }
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
    throw
}
finally {
    if (-not $SkipConnect) { Disconnect-ExchangeOnline -Confirm:$false -ErrorAction SilentlyContinue }
}
