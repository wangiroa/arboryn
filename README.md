# Arboryn

Système personnel de gestion et d'uniformisation de bibliothèque média multi-support, sur Windows 11.

## Statut

En développement actif (catalogue logique, uniformisation, triage, enrichissement, multi-volume,
réplication, dashboard, déploiement multi-PC & packaging). Voir [`CLAUDE.md`](./CLAUDE.md) pour le
brief architectural complet et la roadmap par incréments.

## Prérequis

- Windows 11 22H2 ou plus récent
- .NET 8 SDK
- Windows App SDK 1.5+
- Visual Studio 2022 17.8+ (charge de travail « Développement Windows » ; ajouter les outils de
  packaging MSIX pour construire l'installeur signé)

## Build et test

```powershell
dotnet restore
dotnet build              # compile tous les projets + tests
dotnet test               # exécute les tests unitaires et d'intégration
```

> ⚠️ `dotnet build` (sans RID) produit un exe UI **framework-dependent qui ne se lance pas**
> (l'app WinUI est auto-contenue, `WindowsAppSDKSelfContained=true`). Il sert à compiler et
> tester, pas à lancer l'application. Pour obtenir un exe lançable, voir « Lancer l'application ».

Les projets non-UI (Domain, Application, Infrastructure, tests) compilent aussi depuis Linux/macOS.

## Lancer l'application

L'app WinUI 3 est auto-contenue : il faut builder **avec un RID explicite et une plateforme**
(le mode self-contained refuse `AnyCPU`), puis lancer l'exe produit.

```powershell
# Build de l'exe lançable (Windows uniquement)
dotnet build src/Arboryn.UI/Arboryn.UI.csproj -c Debug -r win-x64 /p:Platform=x64

# Lancement
& .\src\Arboryn.UI\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\Arboryn.UI.exe
```

> Après chaque modification du code, **rebuildez avec la commande RID ci-dessus** avant de
> relancer : un simple `dotnet build` ne met pas à jour l'exe lancé, et l'application
> continuerait de tourner sur d'anciens binaires.

## Distribuer l'application (packaging)

Pour déployer Arboryn ailleurs sans passer par la ligne de commande, deux formats (Inc 13).

### Artefact portable auto-contenu

`dotnet publish` en self-contained produit un dossier lançable **sans runtime .NET ni Windows App
SDK préinstallés** (un RID + une plateforme restent obligatoires) :

```powershell
dotnet publish src/Arboryn.UI/Arboryn.UI.csproj -c Release -r win-x64 --self-contained true `
  -p:Platform=x64 -p:PublishReadyToRun=true -o artifacts/win-x64
# → artifacts/win-x64/Arboryn.UI.exe (double-cliquable)
```

(variante `-r win-arm64 -p:Platform=arm64`). La CI [`release.yml`](.github/workflows/release.yml)
produit ces ZIP par architecture sur un tag `v*`.

### Installeur MSIX signé

[`packaging/build-msix.ps1`](packaging/build-msix.ps1) construit un MSIX auto-contenu et le signe
avec un certificat auto-signé « CN=Arboryn » (créé au besoin). Nécessite Visual Studio avec la
charge de travail de packaging desktop.

```powershell
pwsh packaging/build-msix.ps1 -Rid win-x64
```

Le build/CI par défaut restent inchangés : le packaging n'est activé que par
`/p:ArborynPackage=true` (l'app reste `WindowsPackageType=None` sinon). Sur le poste cible, un MSIX
auto-signé exige une étape unique (PowerShell **administrateur**) :

```powershell
packaging/Install-Arboryn.ps1 -PackagePath Arboryn_0.13.0.0_x64.msix -CertPath Arboryn.cer
```

Avec un vrai certificat commercial (roadmap « Futur »), l'import du `.cer` devient inutile.

> ⚠️ Sous identité MSIX, `%LOCALAPPDATA%` est **redirigé** vers
> `…\Packages\<PackageFamilyName>\LocalCache`. Épinglez l'emplacement de la base via
> `ARBORYN_DB_PATH` ou les Réglages pour qu'il soit indépendant du packaging.

## Structure

```
Arboryn/
├── Arboryn.sln
├── Directory.Build.props          # paramètres MSBuild communs
├── Directory.Packages.props       # versions de packages centralisées
├── CLAUDE.md                      # brief architectural et roadmap
├── src/
│   ├── Arboryn.Domain/              # cœur métier, zéro I/O
│   ├── Arboryn.Application/         # use cases, orchestration
│   ├── Arboryn.Infrastructure/      # adapters FS, SQLite, online
│   │   └── Database/Migrations/   # *.sql copiés au build
│   └── Arboryn.UI/                  # WinUI 3, composition root
└── tests/
    ├── Arboryn.Tests.Unit/
    └── Arboryn.Tests.Integration/
```

## Convention de commits

[Conventional Commits](https://www.conventionalcommits.org/) : `feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `chore:`, `perf:`.

## Base de données

SQLite, par défaut `%LOCALAPPDATA%\Arboryn\index.db`. L'emplacement est **configurable** (Inc 13)
pour partager le catalogue entre plusieurs PC (clé USB, dossier partagé), par ordre de priorité :

1. variable d'environnement `ARBORYN_DB_PATH` ;
2. choix via **Réglages → Emplacement de la base** (pointeur par-machine `db-location.json`) ;
3. `Database:FullPath`, puis `Database:PathRelativeToLocalAppData` dans `appsettings.json` ;
4. défaut `%LOCALAPPDATA%\Arboryn\index.db`.

Le partage se fait par **Export / Import sûrs** (copie cohérente via l'API SQLite Online Backup) :
n'ouvrez jamais la base en direct depuis deux PC à la fois, ni pendant une synchronisation cloud.
Un verrou mono-écrivain (`{base}.lock`) empêche deux instances/PC d'écrire simultanément. Le schéma
est appliqué au démarrage par le `Migrator` (voir `src/Arboryn.Infrastructure/Database/Migrations/`).

## Logs

`%LOCALAPPDATA%\Arboryn\logs\Arboryn-YYYYMMDD.log`, rotation quotidienne, rétention 14 jours.
