<#
.SYNOPSIS
  Tworzy, aktualizuje lub usuwa regułę transportową Exchange Online, która dopisuje stopkę do wiadomości wychodzących.

.DESCRIPTION
  Szablon HTML (z tokenami %%DisplayName%%, %%Title%%, %%MobilePhone%%, %%WindowsEmailAddress%%) pobierz ze strony
  "Administrator: stopka w Outlooku" w aplikacji (przycisk "Pobierz szablon HTML").
  Reguła działa w każdym Outlooku (komputer, przeglądarka, telefon), bo stopka jest dodawana na serwerze.
  Parametry reguły odpowiadają modułowi stopek w SZO: dopisanie na końcu, wiadomości od użytkowników organizacji,
  znacznik w nagłówku chroniący przed drugą stopką w wątku, tryb Audit na start (nic nie zmienia w wiadomościach).
  Skrypt niczego nie zmienia bez potwierdzenia, chyba że podasz -Force. Z -WhatIf pokazuje tylko, co zrobiłby.

  Wymaga modułu ExchangeOnlineManagement oraz uprawnień Exchange do reguł transportowych (rola Transport Rules).

.EXAMPLE
  ./Set-StopkaRule.ps1 -HtmlFile ./stopka.html -Mode Audit -WhatIf

.EXAMPLE
  ./Set-StopkaRule.ps1 -HtmlFile ./stopka.html -Mode Enforce -UserPrincipalName admin@feer.org.pl

.EXAMPLE
  # Połączenie bez użytkownika (certyfikat aplikacji z rolą Exchange.ManageAsApp):
  ./Set-StopkaRule.ps1 -HtmlFile ./stopka.html -AppId <Client-ID> -CertificateThumbprint <odcisk> -Organization feer.org.pl

.EXAMPLE
  ./Set-StopkaRule.ps1 -Remove
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High', DefaultParameterSetName = 'Apply')]
param(
    [Parameter(ParameterSetName = 'Apply', Mandatory = $true)]
    [string]$HtmlFile,

    [Parameter(ParameterSetName = 'Apply')]
    [ValidateSet('Audit', 'Enforce')]
    [string]$Mode = 'Audit',

    [Parameter(ParameterSetName = 'Remove', Mandatory = $true)]
    [switch]$Remove,

    # Połączenie z Exchange Online: interaktywne (domyślnie) albo przez certyfikat aplikacji.
    [string]$UserPrincipalName,
    [string]$AppId,
    [string]$CertificateThumbprint,
    # Linux: plik .pfx; hasło z zmiennej środowiskowej M365_EXO_CERT_PASSWORD (nie w argumentach procesu).
    [string]$CertificateFilePath,
    [string]$Organization,

    # Pomija łączenie, gdy sesja Exchange Online jest już otwarta.
    # Bez certyfikatu skrypt loguje się kodem urządzenia (działa na serwerze bez przeglądarki). -Browser wymusza okno przeglądarki.
    [switch]$Browser,
    [switch]$SkipConnect,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$RuleName   = 'Samoobsługa FEER: stopka e-mail'
$MarkHeader = 'X-Samoobsluga-Stopka'
$MaxChars   = 5000

if ($Force -and -not $PSBoundParameters.ContainsKey('Confirm')) { $ConfirmPreference = 'None' }

if (-not $SkipConnect) {
    # -WhatIf nie może obejmować importu modułu ani logowania (inaczej wypisuje "What if: Set Alias" i nie łączy).
    $savedWhatIf = $WhatIfPreference; $WhatIfPreference = $false
    if (-not (Get-Module -ListAvailable -Name ExchangeOnlineManagement)) {
        throw 'Brak modułu ExchangeOnlineManagement. Zainstaluj: Install-Module ExchangeOnlineManagement -Scope CurrentUser'
    }
    if ($env:M365_EXO_MODULE_VERSION) { Import-Module ExchangeOnlineManagement -RequiredVersion $env:M365_EXO_MODULE_VERSION } else { Import-Module ExchangeOnlineManagement }
    $conn = @{}; $useAppOnly = $false
    if ($AppId -and $CertificateFilePath -and $Organization) {
        $useAppOnly = $true
    } elseif ($AppId -and $CertificateThumbprint -and $Organization) {
        $conn += @{ AppId = $AppId; CertificateThumbprint = $CertificateThumbprint; Organization = $Organization; ShowBanner = $false }
    } elseif ($UserPrincipalName) {
        $conn += @{ UserPrincipalName = $UserPrincipalName }
    }
    if (-not ($AppId -and $CertificateFilePath -and $Organization) -and -not ($AppId -and $CertificateThumbprint -and $Organization) -and -not $Browser) { $conn += @{ Device = $true } }
    if ($useAppOnly) { . (Join-Path $PSScriptRoot 'ExoAuth.ps1'); Connect-ExoAppOnly -AppId $AppId -CertificateFilePath $CertificateFilePath -Organization $Organization }
    else { Connect-ExchangeOnline @conn }
    $WhatIfPreference = $savedWhatIf
}

try {
    $existing = Get-TransportRule -Identity $RuleName -ErrorAction SilentlyContinue

    if ($Remove) {
        if (-not $existing) { Write-Host "Reguła '$RuleName' nie istnieje."; return }
        if ($PSCmdlet.ShouldProcess($RuleName, 'Usunięcie reguły transportowej')) {
            Remove-TransportRule -Identity $RuleName -Confirm:$false
            Write-Host "Usunięto regułę '$RuleName'."
        }
        return
    }

    if (-not (Test-Path -LiteralPath $HtmlFile)) { throw "Nie znaleziono pliku szablonu: $HtmlFile" }
    $html = (Get-Content -LiteralPath $HtmlFile -Raw -Encoding UTF8).Trim()
    if ($html.Length -eq 0) { throw 'Plik szablonu jest pusty.' }
    if ($html.Length -gt $MaxChars) { throw "Szablon ma $($html.Length) znaków, a Exchange dopuszcza $MaxChars. Skróć treść promocyjną w aplikacji." }
    if ($html -notlike '*%%DisplayName%%*' -and $html -notlike '*%%FirstName%%*') {
        Write-Warning 'Szablon nie zawiera tokenu imienia i nazwiska (%%DisplayName%% ani %%FirstName%%). Czy to na pewno szablon z aplikacji?'
    }

    $params = @{
        ApplyHtmlDisclaimerLocation        = 'Append'
        ApplyHtmlDisclaimerText            = $html
        ApplyHtmlDisclaimerFallbackAction  = 'Wrap'
        FromScope                          = 'InOrganization'
        SetHeaderName                      = $MarkHeader
        SetHeaderValue                     = '1'
        ExceptIfHeaderMatchesMessageHeader = $MarkHeader
        ExceptIfHeaderMatchesPatterns      = '1'
        Mode                               = $Mode
        Comments                           = 'Zarządzane przez Samoobsługę poczty. Nie edytuj ręcznie.'
    }

    $action = if ($existing) { 'Aktualizacja' } else { 'Utworzenie' }
    if ($PSCmdlet.ShouldProcess($RuleName, "$action reguły transportowej (tryb $Mode, $($html.Length) znaków)")) {
        if ($existing) { Set-TransportRule -Identity $RuleName @params }
        else           { New-TransportRule -Name $RuleName -Enabled $true @params | Out-Null }
        $rule = Get-TransportRule -Identity $RuleName
        Write-Host "Gotowe. Reguła '$RuleName': stan $($rule.State), tryb $($rule.Mode)."
        if ($Mode -eq 'Audit') {
            Write-Host 'Tryb Audit nie zmienia wiadomości. Sprawdź śledzenie wiadomości, a potem uruchom skrypt z -Mode Enforce.'
        }
    }
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
