<#
.SYNOPSIS
    Construit un paquet MSIX signé d'Arboryn (Inc 13, tranche B).

.DESCRIPTION
    Produit un installeur MSIX auto-contenu (embarque le runtime .NET + Windows App SDK) et le
    signe avec un certificat auto-signé « CN=Arboryn » (créé au besoin, réutilisé ensuite). Le
    certificat public (.cer) est exporté à côté du .msix pour permettre l'installation en
    side-load (voir Install-Arboryn.ps1). Le build par défaut / la CI restent inchangés : le
    packaging n'est activé que par /p:ArborynPackage=true.

    Prérequis : Visual Studio (ou Build Tools) avec la charge de travail packaging desktop
    (MSBuild + makeappx/signtool). Le script localise MSBuild via vswhere.

.PARAMETER Rid
    Runtime identifier cible (win-x64 ou win-arm64). Défaut : win-x64.

.PARAMETER Configuration
    Configuration MSBuild. Défaut : Release.

.EXAMPLE
    pwsh packaging/build-msix.ps1 -Rid win-x64
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Rid = 'win-x64',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src/Arboryn.UI/Arboryn.UI.csproj'
$platform = if ($Rid -eq 'win-arm64') { 'arm64' } else { 'x64' }
$subject = 'CN=Arboryn'   # DOIT correspondre au Publisher du Package.appxmanifest

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

# --- 2. Localisation de MSBuild -------------------------------------------------------------
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path $vswhere)) {
    throw "vswhere introuvable. Installez Visual Studio (ou Build Tools) avec la charge de travail de packaging desktop."
}
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild/**/Bin/MSBuild.exe' |
    Select-Object -First 1
if (-not $msbuild) {
    throw "MSBuild introuvable via vswhere."
}
Write-Host "MSBuild : $msbuild"

# --- 3. Build MSIX packagé + signé ----------------------------------------------------------
& $msbuild $project `
    /restore `
    /p:ArborynPackage=true `
    /p:Configuration=$Configuration `
    /p:Platform=$platform `
    /p:RuntimeIdentifier=$Rid `
    /p:GenerateAppxPackageOnBuild=true `
    /p:AppxBundle=Never `
    /p:UapAppxPackageBuildMode=SideloadOnly `
    /p:AppxPackageSigningEnabled=true `
    /p:PackageCertificateThumbprint=$($cert.Thumbprint) `
    /nologo /verbosity:minimal
if ($LASTEXITCODE -ne 0) {
    throw "Échec du build MSIX (code $LASTEXITCODE)."
}

# --- 4. Localisation du .msix produit + export du certificat public -------------------------
$msix = Get-ChildItem (Join-Path $repoRoot 'src/Arboryn.UI') -Recurse -Filter '*.msix' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $msix) {
    throw "Aucun .msix produit — vérifiez la sortie MSBuild."
}

$cerPath = Join-Path $msix.DirectoryName 'Arboryn.cer'
Export-Certificate -Cert $cert -FilePath $cerPath -Type CERT | Out-Null

Write-Host ''
Write-Host "MSIX  : $($msix.FullName)"
Write-Host "Cert  : $cerPath"
Write-Host "Installer (poste cible, admin) : packaging/Install-Arboryn.ps1 -PackagePath <msix> -CertPath <cer>"
