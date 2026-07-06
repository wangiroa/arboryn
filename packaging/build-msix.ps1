<#
.SYNOPSIS
    Construit un paquet MSIX signé d'Arboryn (Inc 13, tranche B) — sans Visual Studio.

.DESCRIPTION
    Publie l'application en self-contained (runtime .NET + Windows App SDK embarqués), puis
    l'empaquette avec makeappx.exe et la signe avec signtool.exe (tous deux fournis par le NuGet
    Microsoft.Windows.SDK.BuildTools déjà référencé — aucun Visual Studio requis). La signature
    utilise un certificat auto-signé « CN=Arboryn » (créé au besoin, réutilisé ensuite) ; le
    certificat public (.cer) est exporté à côté du .msix pour l'installation en side-load
    (voir Install-Arboryn.ps1).

    Le build/dev/CI par défaut restent inchangés : l'app reste WindowsPackageType=None ; le MSIX
    est produit ici en dehors du build MSBuild.

.PARAMETER Rid
    Runtime identifier cible (win-x64 ou win-arm64). Défaut : win-x64.

.PARAMETER Version
    Version quad du paquet — doit correspondre au Package.appxmanifest. Défaut : 0.13.0.0.

.EXAMPLE
    pwsh packaging/build-msix.ps1 -Rid win-x64
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Rid = 'win-x64',
    [string]$Version = '0.13.0.0'
)

$ErrorActionPreference = 'Stop'
$repoRoot   = Split-Path -Parent $PSScriptRoot
$project    = Join-Path $repoRoot 'src/Arboryn.UI/Arboryn.UI.csproj'
$manifestIn = Join-Path $repoRoot 'src/Arboryn.UI/Package.appxmanifest'
$platform   = if ($Rid -eq 'win-arm64') { 'arm64' } else { 'x64' }
$subject    = 'CN=Arboryn'   # DOIT correspondre au Publisher du Package.appxmanifest
$artifacts  = Join-Path $repoRoot 'artifacts'
$stage      = Join-Path $artifacts "msix-stage-$Rid"
$msixOut    = Join-Path $artifacts "Arboryn_${Version}_${platform}.msix"
$cerOut     = Join-Path $artifacts 'Arboryn.cer'

New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

# --- 1. Certificat de signature auto-signé (créé si absent) ---------------------------------
$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $subject -and $_.HasPrivateKey } |
    Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $cert) {
    Write-Host "Création d'un certificat auto-signé $subject…"
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject `
        -KeyUsage DigitalSignature -FriendlyName 'Arboryn (auto-signé)' `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
}
Write-Host "Certificat : $($cert.Thumbprint)"

# --- 2. Outils makeappx/signtool (NuGet Microsoft.Windows.SDK.BuildTools) --------------------
$btRoot = Join-Path $env:USERPROFILE '.nuget/packages/microsoft.windows.sdk.buildtools'
if (-not (Test-Path $btRoot)) {
    throw "Microsoft.Windows.SDK.BuildTools introuvable dans le cache NuGet. Lancez d'abord 'dotnet restore'."
}
$hostArch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
function Find-Tool([string]$name) {
    $t = Get-ChildItem $btRoot -Recurse -Filter $name -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "\\$hostArch\\" } |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $t) { throw "$name introuvable (arch $hostArch) sous $btRoot." }
    return $t.FullName
}
$makeappx = Find-Tool 'makeappx.exe'
$signtool = Find-Tool 'signtool.exe'

# --- 3. Publication self-contained dans un dossier de staging propre -------------------------
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
Write-Host "Publication self-contained ($Rid)…"
& dotnet publish $project -c Release -r $Rid --self-contained true `
    -p:Platform=$platform -p:PublishReadyToRun=true -o $stage --nologo
if ($LASTEXITCODE -ne 0) { throw "Échec de dotnet publish (code $LASTEXITCODE)." }

# --- 4. AppxManifest.xml dans le staging (ProcessorArchitecture cible injectée) --------------
[xml]$manifest = Get-Content $manifestIn
$manifest.Package.Identity.SetAttribute('ProcessorArchitecture', $platform)
$manifest.Package.Identity.SetAttribute('Version', $Version)
$manifest.Save((Join-Path $stage 'AppxManifest.xml'))

# --- 5. Empaquetage + signature -------------------------------------------------------------
if (Test-Path $msixOut) { Remove-Item $msixOut -Force }
Write-Host "makeappx pack…"
& $makeappx pack /o /d $stage /p $msixOut
if ($LASTEXITCODE -ne 0) { throw "Échec de makeappx (code $LASTEXITCODE)." }

Write-Host "signtool sign…"
& $signtool sign /fd SHA256 /sha1 $cert.Thumbprint $msixOut
if ($LASTEXITCODE -ne 0) { throw "Échec de signtool (code $LASTEXITCODE)." }

Export-Certificate -Cert $cert -FilePath $cerOut -Type CERT | Out-Null

Write-Host ''
Write-Host "MSIX  : $msixOut"
Write-Host "Cert  : $cerOut"
Write-Host "Installer (poste cible, PowerShell admin) :"
Write-Host "  packaging/Install-Arboryn.ps1 -PackagePath '$msixOut' -CertPath '$cerOut'"
