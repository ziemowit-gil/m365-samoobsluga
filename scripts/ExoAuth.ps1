# Wspólne łączenie z Exchange Online bez użytkownika (certyfikat aplikacji). Dołączane przez inne skrypty (dot-sourcing).
# Moduł ExchangeOnlineManagement 3.10.x potrafi zgłosić "Object reference not set..." wewnątrz Connect-ExchangeOnline przy
# uwierzytelnianiu certyfikatem na Linuksie. Dlatego najpierw pobieramy token sami (MSAL z modułu) i podajemy go przez -AccessToken
# (parametr udokumentowany od wersji 3.1.0, razem z -Organization). Gdy to się nie uda, wracamy do łączenia certyfikatem.

function Get-ExoAppToken {
    param([string]$AppId, [System.Security.Cryptography.X509Certificates.X509Certificate2]$Cert, [string]$Organization)
    $mod = Get-Module -ListAvailable -Name ExchangeOnlineManagement | Sort-Object Version -Descending | Select-Object -First 1
    $msal = Get-ChildItem -Path $mod.ModuleBase -Recurse -Filter 'Microsoft.Identity.Client.dll' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $msal) { throw 'Nie znaleziono biblioteki MSAL w module ExchangeOnlineManagement.' }
    if (-not ([System.AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'Microsoft.Identity.Client' })) { Add-Type -Path $msal.FullName }
    $app = [Microsoft.Identity.Client.ConfidentialClientApplicationBuilder]::Create($AppId).WithCertificate($Cert).WithTenantId($Organization).Build()
    $app.AcquireTokenForClient([string[]]@('https://outlook.office365.com/.default')).ExecuteAsync().GetAwaiter().GetResult().AccessToken
}

# Tylko polecenia, których używają skrypty. Mniejszy zestaw omija część inicjalizacji modułu (tymczasowego modułu poleceń),
# w której Connect-ExchangeOnline 3.10.x potrafi zgłosić NullReference przy tokenie aplikacji (bez konta użytkownika).
$script:ExoCommands = @('Get-TransportRule', 'New-TransportRule', 'Set-TransportRule', 'Remove-TransportRule',
                        'Get-Mailbox', 'Get-MailboxMessageConfiguration', 'Set-MailboxMessageConfiguration')

function Connect-ExoAppOnly {
    param([Parameter(Mandatory)][string]$AppId, [Parameter(Mandatory)][string]$CertificateFilePath, [Parameter(Mandatory)][string]$Organization)
    $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($CertificateFilePath, $env:M365_EXO_CERT_PASSWORD)
    try {
        $token = Get-ExoAppToken -AppId $AppId -Cert $cert -Organization $Organization
        Connect-ExchangeOnline -AccessToken $token -Organization $Organization -ShowBanner:$false -BypassMailboxAnchoring -SkipLoadingFormatData -CommandName $script:ExoCommands
        return
    } catch {
        Write-Warning ("Połączenie przez token nie powiodło się (" + $_.Exception.Message + "). Próbuję certyfikatem.")
    }
    Connect-ExchangeOnline -AppId $AppId -Certificate $cert -Organization $Organization -ShowBanner:$false -BypassMailboxAnchoring -SkipLoadingFormatData -CommandName $script:ExoCommands
}
