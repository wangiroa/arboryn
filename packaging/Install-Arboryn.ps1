<#
.SYNOPSIS
    Installe Arboryn en side-load depuis un paquet MSIX auto-signé (Inc 13, tranche B).

.DESCRIPTION
    Étape unique nécessaire pour un MSIX auto-signé : approuver le certificat public en le plaçant
    dans le magasin « Personnes autorisées » de la machine (LocalMachine\TrustedPeople), puis
    installer le paquet. À exécuter dans un PowerShell ADMINISTRATEUR sur le poste cible.

    Avec un vrai certificat de signature commercial (cf. roadmap « Futur »), l'import du .cer
    devient inutile.

.PARAMETER PackagePath
    Chemin du fichier .msix.

.PARAMETER CertPath
    Chemin du certificat public .cer (exporté par build-msix.ps1, à côté du .msix).

.EXAMPLE
    # PowerShell administrateur
    ./Install-Arboryn.ps1 -PackagePath .\Arboryn_0.13.0.0_x64.msix -CertPath .\Arboryn.cer
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$PackagePath,
    [Parameter(Mandatory)] [string]$CertPath
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $PackagePath)) { throw "Paquet introuvable : $PackagePath" }
if (-not (Test-Path $CertPath))    { throw "Certificat introuvable : $CertPath" }

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)
if (-not $isAdmin) {
    throw "Exécutez ce script dans un PowerShell administrateur (import du certificat machine requis)."
}

Write-Host "Import du certificat dans LocalMachine\TrustedPeople…"
Import-Certificate -FilePath $CertPath -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null

Write-Host "Installation du paquet…"
Add-AppxPackage -Path $PackagePath

Write-Host "Arboryn est installé. Lancez-le depuis le menu Démarrer."
