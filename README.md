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
dotnet build
dotnet test
```

L'exécution de l'app via `dotnet run --project src/Arboryn.UI/Arboryn.UI.csproj` nécessite Windows. Le squelette compile depuis Linux ou macOS pour les projets non-UI (Domain, Application, Infrastructure, tests).

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
