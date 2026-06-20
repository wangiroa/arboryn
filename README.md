# Arboryn

Système personnel de gestion et d'uniformisation de bibliothèque média multi-support, sur Windows 11.

## Statut

Incrément 0 — squelette de solution prêt à builder. Voir [`CLAUDE.md`](./CLAUDE.md) pour le brief architectural complet et la roadmap.

## Prérequis

- Windows 11 22H2 ou plus récent
- .NET 8 SDK
- Windows App SDK 1.5+
- Visual Studio 2022 17.8+ (avec charge de travail « Développement Windows »)

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

SQLite stocké dans `%LOCALAPPDATA%\Arboryn\index.db`. Le schéma est appliqué automatiquement au démarrage via le `Migrator`. Voir `src/Arboryn.Infrastructure/Database/Migrations/001_InitialSchema.sql`.

## Logs

`%LOCALAPPDATA%\Arboryn\logs\Arboryn-YYYYMMDD.log`, rotation quotidienne, rétention 14 jours.
