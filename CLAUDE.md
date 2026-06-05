# CLAUDE.md — Arboryn

> Système personnel de gestion et d'uniformisation de bibliothèque média
> multi-support, sur Windows 11. Catalogue logique unifié à travers PCs, NAS
> et disques externes, avec déduplication, uniformisation d'arborescence et
> de nommage, classification assistée des documents officiels, et
> réplication multi-support sous contrôle.
>
> **Statut** : projet en bootstrap. Aucun code écrit. Ce document sert de
> brief architectural et de feuille de route.
>
> **Nom de projet** : `Arboryn` 

---

## 1. Vision et objectif

Arboryn répond à un besoin sous-jacent : posséder beaucoup de fichiers médias
et de documents officiels répartis sur plusieurs supports (PCs, NAS, disques
externes USB), avec actuellement des doublons, des nommages incohérents,
des arborescences divergentes selon le support, et l'incapacité de répondre
rapidement à la question « où ai-je tel fichier ? ».

Le but final est :

1. **Connaître** : disposer d'un catalogue logique unifié de tout ce que
   l'on possède, indépendamment du support physique.
2. **Uniformiser** : appliquer une arborescence et un nommage canoniques
   par catégorie, identiques sur tous les supports.
3. **Dédoublonner** : supprimer les copies indésirables, en préservant
   l'exemplaire de meilleure qualité.
4. **Répliquer** : faire converger chaque support vers le contenu qui lui
   est destiné (chaque support a son périmètre, pas forcément tout).
5. **Visualiser** : savoir instantanément ce qui est où, ce qui manque,
   ce qui est en surplus.

La déduplication n'est PLUS la finalité — c'est le substrat technique qui
permet le reste. La finalité est l'uniformisation et la maîtrise.

### Catégories médias supportées

Par ordre d'importance métier :

1. **Livres audio** (M4B, MP3 multi-fichiers)
2. **Livres** (PDF, EPUB, MOBI, AZW)
3. **Vidéos** (films, séries)
4. **Photos** (JPG, RAW, HEIC)
5. **Documents officiels** (PDF principalement, scans, avec sous-catégories
   personnalisables : investissements, fiscal, santé, etc.)
6. **PDF divers** (magazines, BD, manuels)

### Exemples d'arborescences canoniques cibles

```
Livres/
  Tolkien/
    Le Seigneur des Anneaux/
      Tolkien - Le Seigneur des Anneaux - 01 - La Communauté de l'Anneau.epub
      Tolkien - Le Seigneur des Anneaux - 02 - Les Deux Tours.epub
      ...

Livres audio/
  Asimov/
    Fondation/
      Asimov - Fondation - 01 - Fondation.m4b
      Asimov - Fondation - 02 - Fondation et Empire.m4b

Documents officiels/
  Investissements/
    Appartement Champigny/
      Factures/
        [EDF] - Facture - 202403.pdf
        [Veolia] - Facture - 202403.pdf
      Appels de fond/
        [Foncia] - Appel de fond - 202401.pdf
      Assemblées générales/
        [Foncia] - Convocation AG - 202405.pdf
        [Foncia] - PV AG - 202406.pdf
```

---

## 2. Contraintes structurantes

| Contrainte | Implication architecturale |
|---|---|
| Volume jusqu'à 4 To par disque | Indexation efficace ; re-scan incrémental via USN Journal NTFS |
| Multi-PC, multi-disque (interne, externe USB, NAS SMB) | Index portable ; identification stable de chaque volume |
| Chaque support a son propre périmètre de contenu | Modèle de scope de réplication par volume |
| Disque pouvant être physiquement débranché | Index volume persistant même hors-ligne ; opérations différées au rebranchement |
| Pas d'espace pour snapshot (usage perso) | Aucune copie préalable ; corbeille Windows obligatoire ; hard delete opt-in |
| Beaucoup de documents non catégorisés | Workflow de triage rapide intégré, avec assistance heuristique |
| Privacy-first enrichment | Jamais envoyer le path ou le nom de fichier brut à une API tierce — seulement les champs structurés extraits localement |
| Mode 100 % local possible | Toutes les fonctionnalités online doivent être désactivables |
| Usage perso → distribution future | Pas de hardcoding, config externalisée, archi prête au packaging MSIX |

---

## 3. Stack technique

### Cœur

- **Langage** : C# / .NET 8 (LTS)
- **UI** : WinUI 3 (préférence) ; WPF acceptable si blocage WinUI
- **Persistance** : SQLite via `Microsoft.Data.Sqlite` + **Dapper**
- **Templates** : **Scriban** (sandboxé)
- **DI** : `Microsoft.Extensions.DependencyInjection`
- **Logging** : Serilog (fichier + console)
- **Configuration** : `Microsoft.Extensions.Configuration`
- **Tests** : xUnit + FluentAssertions + Verify

### Librairies métier

- **MetadataExtractor** — EXIF/IPTC/XMP
- **TagLib#** — ID3, FLAC, métadonnées audio
- **PdfPig** — texte et métadonnées PDF
- **VersOne.Epub** — OPF EPUB
- **CoenM.ImageHash** — pHash perceptuel images
- **Magick.NET** — miniatures, conversions
- **Tesseract.NET** — OCR pour documents scannés (triage)
- **ffprobe.exe** (embarqué) — métadonnées et keyframes vidéo
- **fpcalc.exe** (embarqué, Chromaprint) — empreinte acoustique

### Packaging

- **Initial** : exe portable + binaires embarqués
- **Cible distribution** : MSIX signé

---

## 4. Modèle conceptuel central

### 4.1 LogicalFile vs FileInstance

Distinction fondamentale qui structure tout le système :

- **`LogicalFile`** : l'œuvre ou le document, indépendamment de sa
  localisation. Identifié par son contenu (hash exact ou perceptuel) ou par
  une signature équivalente (titre + auteur normalisés). Porte les
  métadonnées canoniques. Possède un chemin canonique cible dans la
  bibliothèque (résultat du template de catégorie).
- **`FileInstance`** : une copie physique d'un LogicalFile sur un Volume
  donné. Plusieurs instances peuvent référencer un même LogicalFile :
  - sur des volumes différents (réplication intentionnelle ou redondance)
  - sur le même volume (doublon à résoudre)
  - avec un même contenu mais à des chemins différents (uniformisation requise)

**Conséquence importante** : la déduplication est un cas particulier du
problème général de placement. « Deux FileInstances du même LogicalFile sur
le même Volume → résolution requise ». Les autres cas (FileInstance dont le
chemin ne correspond pas au chemin canonique, FileInstance manquant sur un
volume qui devrait l'avoir, FileInstance en surplus sur un volume hors
scope) ressortent du même modèle.

### 4.2 Taxonomie canonique

Chaque catégorie média possède :

- Une **racine** de catégorie (par ex. `Livres audio/`)
- Un **template de chemin** Scriban (par ex. `{{ author }}/{{ if series }}{{ series }}/{{ end }}`)
- Un **template de nom de fichier** Scriban (par ex. `{{ author }} - {{ title }}{{ if series }} - {{ volume | format("00") }}{{ end }}.{{ ext }}`)
- Une liste de **champs requis** (validation)
- Une liste de **sous-catégories** optionnelles (notamment pour les documents officiels)

Stockée dans la table `library_taxonomy`, versionnée, éditable via Settings.
C'est la « vérité » vers laquelle Arboryn fait converger l'arborescence
physique. Modifier la taxonomie déclenche une re-évaluation des chemins
cibles pour tous les LogicalFiles concernés (sans exécution automatique —
proposé à l'utilisateur).

### 4.3 Volumes et scopes de réplication

Chaque Volume porte un `ReplicationScope` — une expression qui définit
quels LogicalFiles sont en scope pour ce volume. Exemples :

- NAS : `category in ('all')` — tout
- Disque externe USB 1 : `category in ('Livres audio', 'Livres')`
- Disque externe USB 2 : `category in ('Vidéos')`
- PC perso : `category in ('Documents officiels')`

Les scopes peuvent inclure des prédicats plus fins :
- `category = 'Documents officiels' AND subcategory = 'Investissements'`
- `category = 'Photos' AND year >= 2020`

Du calcul du placement plan (cf. § 5.5) découle l'ensemble des opérations
de réplication, déplacement et suppression nécessaires pour que chaque
volume converge vers son contenu cible.

---

## 5. Architecture

### 5.1 Couches (Clean Architecture)

```
Arboryn.sln
├── src/
│   ├── Arboryn.Domain/             # C# pur, zéro I/O
│   ├── Arboryn.Application/        # Use cases, orchestration
│   ├── Arboryn.Infrastructure/     # Adapters FS, SQLite, APIs, OCR
│   └── Arboryn.UI/                 # WinUI 3, composition root
└── tests/
    ├── Arboryn.Tests.Unit/
    └── Arboryn.Tests.Integration/
```

Règle de dépendance : UI → Application → Domain ← Infrastructure.
Domain ne dépend de rien.

### 5.2 Modèle de données SQLite

Tables principales :

**Volumes et identification**
- `volumes` : id, name, kind, serial, fingerprint, label, last_seen_at, last_scan_at, status, replication_scope_id
- `replication_scopes` : id, name, expression_json, created_at

**Catalogue logique**
- `logical_files` : id, category, subcategory, canonical_path, canonical_filename, content_signature_kind, content_signature, primary_metadata_json, created_at, updated_at
- `file_instances` : id, logical_file_id (nullable pendant la phase d'identification), volume_id, relative_path, size, modified_at, created_at, sha256, phash, chromaprint, status (`active`/`missing`/`deleted`/`pending_classification`)

**Métadonnées et groupes**
- `file_metadata` : file_instance_id, key, value, source, confidence, extracted_at
- `duplicate_groups` : id, kind (`exact_name`/`fuzzy_name`/`exact_hash`/`perceptual`), confidence, created_at, status
- `group_members` : group_id, file_instance_id, score

**Taxonomie**
- `library_taxonomy` : id, category, name_pattern, path_pattern, required_fields_json, active, version
- `category_subcategories` : id, category, subcategory_path, label

**Triage de documents**
- `triage_patterns` : id, pattern_kind (`source`/`object`/`date`), regex, template, learned_from_user, priority

**Opérations et plan**
- `operations` : id, kind (`rename`/`move`/`copy`/`delete`/`metadata_writeback`), file_instance_id, source_volume_id, target_volume_id, old_path, new_path, status, executed_at, undone_at, batch_id
- `placement_plans` : id, generated_at, status, total_operations, estimated_space_change_json
- `placement_plan_operations` : plan_id, operation_payload_json, executed_at

**Cache et settings**
- `api_cache` : provider, query_hash, response_json, cached_at, expires_at
- `settings` : key, value

Index obligatoires : `file_instances(volume_id)`, `file_instances(sha256)`,
`file_instances(phash)` (BK-tree externe pour Hamming),
`file_instances(logical_file_id)`, `logical_files(content_signature)`,
`group_members(group_id)`, `operations(batch_id)`.

Localisation : `%LOCALAPPDATA%\Arboryn\index.db`.

### 5.3 Identification des volumes

Identique au design précédent :

- **NTFS local/USB** : Volume Serial Number + marqueur `.Arboryn` à la racine
- **NAS/SMB** : hostname + share name + marqueur `.Arboryn`
- Marqueur JSON contenant `volume_id`, `first_seen_at`, `friendly_name`
- Re-scan incrémental via USN Journal (NTFS), comparaison mtime (SMB/FAT)

### 5.4 Pipeline de catégorisation et d'extraction

1. Extension → catégorie préliminaire
2. Lecture des métadonnées locales (EXIF/ID3/PDF Info/EPUB OPF)
3. Cleanup filename (tags, années, qualité, séparateurs, suffixes de copie)
4. Affinement par contenu si ambigu
5. Fusion des sources avec scoring de confiance par champ
6. (Optionnel, Inc 8) Triage documents pour la catégorie « Documents officiels »
7. (Optionnel, Inc 9) Enrichissement online — privacy-first

Le résultat est un jeu de champs structurés associé au FileInstance, qui
permet :
- la création ou rattachement à un LogicalFile
- le calcul du chemin canonique cible
- la génération du nouveau nom de fichier

### 5.5 Pipeline de placement et de réplication

C'est le cœur de la fonctionnalité d'uniformisation multi-support.

**Étape 1 — Construire le catalogue logique**
Pour chaque FileInstance non encore attaché à un LogicalFile :
- Calculer la signature de contenu (hash exact ou perceptuel selon catégorie)
- Chercher un LogicalFile existant correspondant
- Si trouvé → attacher ; sinon → créer un nouveau LogicalFile

**Étape 2 — Calculer le placement cible**
Pour chaque LogicalFile :
- Évaluer le chemin canonique d'après la taxonomie
- Évaluer pour chaque Volume si le LogicalFile est dans son scope de réplication

**Étape 3 — Diff réel vs cible**
Pour chaque (LogicalFile, Volume) :
- État actuel : 0, 1 ou plusieurs FileInstances
- État cible : 0 ou 1 FileInstance au chemin canonique
- Diff → opérations à effectuer :
  - **rename** : FileInstance présent mais mauvais chemin sur le volume
  - **move** (intra-volume) : FileInstance présent mais mauvais sous-arbre
  - **copy** (cross-volume) : LogicalFile manquant sur ce volume mais en scope
  - **delete** : FileInstance présent mais hors scope ; ou doublon à résoudre

**Étape 4 — Produire le PlacementPlan**
- Agréger toutes les opérations
- Calculer l'impact espace par volume (gain/perte)
- Détecter les conflits (versions différentes du même LogicalFile sur différents volumes)
- Stocker le plan en base

**Étape 5 — Validation utilisateur**
- Vue récap par volume : N copies, M déplacements, K suppressions, espace nécessaire
- Drill-down par catégorie ou par LogicalFile
- Possibilité de désactiver certaines opérations
- Confirmation explicite avant exécution

**Étape 6 — Exécution**
- Par batch transactionnel
- Volumes hors-ligne : opérations marquées « pending », reprises au branchement
- Journal complet pour undo

### 5.6 Triage de documents officiels

Workflow dédié pour la catégorie Documents officiels, déclenché pour tout
FileInstance dont les métadonnées extraites localement sont insuffisantes
pour le placement canonique.

**Préparation**
- Render thumbnail de la première page (Magick.NET pour PDF via PdfPig + rasterization)
- Extraction texte première page (PdfPig) ; si vide → OCR Tesseract sur l'image rendue
- Application des `triage_patterns` (regex) pour extraire les candidats :
  - Source : entités capitalisées récurrentes en début de document, en-têtes ; patterns appris (« émis par », « expéditeur : »)
  - Objet : patterns par type (« Facture n° », « Appel de fonds », « Convocation à l'Assemblée Générale », « Procès-verbal de l'AG »...)
  - Date : regex dates en français (DD/MM/AAAA, JJ mois AAAA, MMAA, etc.) + parsing
- Pré-remplissage des trois champs avec score de confiance

**UI de triage en lot**
- Grille de 20-50 documents par page
- Chaque ligne : thumbnail, snippet texte, trois champs éditables (source, objet, date), case à cocher « validé »
- Sélection de catégorie/sous-catégorie pour le placement final
- Action « Appliquer » → renommage selon template `[{source}] - {objet} - {date | format yyyyMM}.{ext}` et placement sous-catégorie

**Apprentissage**
- À chaque correction utilisateur (par ex. l'utilisateur change la source de « Société X » à « Foncia »), on enregistre :
  - Le snippet d'origine
  - La valeur initiale extraite
  - La valeur corrigée
- Un job en arrière-plan dérive des regex génériques à partir de ces corrections et les ajoute à `triage_patterns` avec priorité élevée
- Pas de ML lourd ; système simple à base de patterns

**Mode assisté par LLM (optionnel, plus tard)**
- Pour les cas difficiles, envoi du snippet texte (jamais du fichier) à un LLM local (Ollama) ou paid API (opt-in explicite)
- Tag clair dans l'UI quand utilisé

### 5.7 Pipeline d'enrichissement (privacy-first)

Inchangé par rapport à la version précédente :

- Jamais de path ou de filename brut envoyé à une API tierce
- Seuls les champs structurés issus de l'extraction locale (titre nettoyé, ISBN, année) sortent
- Cache des réponses (hash de requête normalisée)
- Auto-application si confidence > seuil paramétrable, sinon présentation à l'utilisateur
- Mode 100 % local désactivable globalement et par catégorie
- Write-back des métadonnées enrichies dans le fichier (ID3, EPUB OPF, EXIF, PDF Info)

Providers : OpenLibrary, Google Books (fallback), TMDB, MusicBrainz.

### 5.8 Sécurité des opérations fichiers

Inchangé :

- Pré-vérification (espace, permissions, longueur, conflits)
- Dry-run preview obligatoire pour batch > 10
- Journal AVANT exécution
- Batchs transactionnels avec rollback partiel
- Suppression via Recycle Bin par défaut, hard delete opt-in
- Undo via rejouage inverse du journal, fenêtre configurable
- Opérations cross-volume (copy) : vérification d'intégrité post-copie (hash compare) avant marquer comme terminée

### 5.9 Inventory dashboard

Vue de pilotage de la bibliothèque :

- **Matrice volumes × catégories** : pour chaque cellule, nombre de LogicalFiles présents / nombre en scope
- **Vue gap** : LogicalFiles qui devraient être sur un volume mais ne le sont pas (donc à copier)
- **Vue surplus** : FileInstances présents sur un volume hors scope (à supprimer ou à déplacer)
- **Recherche cross-volume** : « où est tel livre ? » → liste tous les FileInstances avec volume + statut
- **Stats globales** : N LogicalFiles total, N FileInstances, espace par volume et par catégorie, taux de redondance
- **Indicateur de santé** : volumes hors-ligne depuis longtemps, opérations en attente, conflits non résolus

---

## 6. Spécificités Windows 11

- **Chemins longs** : `LongPathsEnabled` activé + préfixe `\\?\` sur API natives
- **Liens symboliques / jonctions** : ignorés par défaut, opt-in pour suivre
- **UNC paths** : timeout configurable + retry exponentiel
- **USN Journal NTFS** : Win32 P/Invoke (`USNJournalNet` ou wrapper maison)
- **Recycle Bin** : `IFileOperation` (COM, moderne) plutôt que `SHFileOperation` déprécié
- **File System Watcher** : monitoring post-scan seulement
- **Indexation Windows Search** : retry court avec backoff sur verrous transitoires
- **Permissions** : exécution utilisateur standard, élévation à la demande

---

## 7. Conventions

### Code
- async/await sur toute I/O
- CancellationToken sur méthodes longues
- Records pour types immutables
- Value Objects : `FilePath`, `Sha256`, `VolumeId`, `LogicalFileId`, `CanonicalName`, `Category`
- DI constructor injection
- `Result<T, Error>` pour erreurs métier ; exceptions pour erreurs techniques

### Naming
- Code en anglais
- Strings UI en français (FR par défaut, EN dès Inc 12)
- `.resx` pour i18n

### Git
- Conventional Commits
- `main` + `feat/<nom-court>`
- Chaque commit build + test verts

---

## 8. Tests

- **Unit** (Domain + Application) : rapides, sans I/O. 80 % couverture Domain.
- **Integration** : SQLite + FS réels (répertoire temp), enregistreurs HTTP pour providers
- **Snapshot** (Verify) : templates de renommage et de chemin canonique, sorties de catégorisation
- **Smoke test manuel** : sur jeu d'échantillons (~100 fichiers de toutes catégories) avant chaque merge sur `main`

---

## 9. Roadmap par incréments

Chaque incrément est livrable et utilisable seul. Effort : S = ~1 semaine
perso, M = 2-3 semaines, L = 3-4 semaines.

**Note sur le séquencement** : le multi-volume effectif (identification
stable cross-PC, USN Journal, support NAS, orchestration multi-volume) est
décalé en Inc 9, juste avant la réplication (Inc 10). Cela permet de
maximiser la valeur livrée tôt : uniformisation intra-volume, triage,
enrichissement utilisables avant l'effort multi-volume qui est en L. De
Inc 1 à Inc 8, Arboryn fonctionne sur **un volume à la fois**. Le modèle de
données est toutefois conçu multi-volume dès Inc 3 (table `volumes` avec
une ligne « default » créée automatiquement, `file_instances.volume_id` en
FK obligatoire) pour éviter tout refactor au moment du passage multi-volume.

### Increment 0 — Fondations

**Objectif** : Solution vide qui démarre proprement.

**Scope** : Solution + 6 projets, migration SQLite v1, bootstrap DI/logging/config, fenêtre WinUI 3 vide, CI GitHub Actions, README.

**Critères** : `dotnet build` clean, `dotnet test` passe, app lance.

**Effort** : S

---

### Increment 1 — MVP : doublons exacts sur un dossier

**Objectif** : Scanner un dossier racine, détecter les doublons exacts par nom canonique, supprimer via corbeille avec undo.

**Scope** :
- UI sélection répertoire
- Scanner mono-thread avec progression
- Canonicalisation du nom (lowercase, accents, parens, suffixes copie)
- Détection `group by canonical_name + size`
- Vue des groupes (liste + détail)
- Suppression → corbeille
- Journal `operations` + undo dernière action

**Hors scope** : multi-volume, fuzzy, hash, métadonnées, catalogue logique, templates.

**Critères** :
- Scan 500 Go en < 10 min
- « Mon Livre.pdf », « mon livre (1).pdf », « MON LIVRE.PDF » → même groupe
- Suppression réversible

**Effort** : M

---

### Increment 2 — Doublons flous + hash de confirmation

**Objectif** : Détecter les variantes orthographiques, confirmer par contenu.

**Scope** :
- Fuzzy : Levenshtein normalisé + Jaccard sur tokens, seuil paramétrable
- SHA-256 calculé à la demande sur un groupe
- Comparaison côte-à-côte
- Auto-scoring « préférable » (taille, profondeur, qualité du nom)
- Actions de groupe

**Critères** :
- « Hamlet.pdf » et « Hamlet_v2.pdf » détectés similaires
- Hash distingue copies de variantes

**Effort** : M

---

### Increment 3 — Catalogue logique

**Objectif** : Introduire LogicalFile / FileInstance. Toute la suite en dépend.

**Scope** :
- Migration SQLite : refactor `files` → `file_instances` + ajout `logical_files`
- **Ajout de la table `volumes` et de la FK `file_instances.volume_id`**. Une ligne « default » est créée automatiquement au premier scan ; tous les FileInstances y sont rattachés tant que le multi-volume n'est pas activé (Inc 9). Cela rend la base multi-volume-ready sans refactor ultérieur.
- Pipeline d'attachement : pour chaque FileInstance, déterminer s'il existe déjà un LogicalFile correspondant (par hash, ou par nom canonique + taille en attendant les métadonnées)
- Détection des doublons opère sur les LogicalFiles : plusieurs FileInstances rattachés au même LogicalFile sur le volume default → groupe « intra-volume »
- Vue inventaire minimale : liste des LogicalFiles + leurs FileInstances
- Métriques globales : N LogicalFiles, N FileInstances, ratio de redondance

**Hors scope** : taxonomie canonique, réplication active, triage docs, enrichissement online, multi-volume réel.

**Critères** :
- Catalogue cohérent après scan d'un dossier contenant des fichiers identiques (N FileInstances rattachés à 1 LogicalFile)
- Vue inventaire navigable sur 10k+ LogicalFiles
- Pas de régression sur cas Inc 1-2

**Effort** : L

---

### Increment 4 — Métadonnées locales + cleanup filename

**Objectif** : Extraire toutes les métadonnées locales utiles et nettoyer les noms.

**Scope** :
- Adapters MetadataExtractor / TagLib# / PdfPig / VersOne.Epub
- Categorizer pipeline (§ 5.4)
- Cleanup filename heuristique avancé (tags, années, qualité, encodage)
- Fusion sources avec scoring de confiance par champ
- Stockage dans `file_metadata` avec source tracée

**Critères** :
- ≥ 95 % MP3 ont artist + title extraits
- ≥ 90 % PDF ont title extrait
- Catégorisation correcte sur jeu de test

**Effort** : M

---

### Increment 5 — Hash perceptuel

**Objectif** : Reconnaître les vraies copies modifiées (recompression, redimensionnement).

**Scope** :
- pHash images (CoenM.ImageHash + BK-tree pour distance Hamming)
- Empreinte audio Chromaprint via fpcalc
- Hash agrégé vidéo via ffprobe sur keyframes
- Nouveau type de groupe `perceptual`
- UI comparaison preview deux images / waveform deux audios

**Critères** :
- JPG 100 % et JPG 80 % du même original → même LogicalFile
- MP3 et FLAC du même morceau → même LogicalFile via Chromaprint

**Effort** : M

---

### Increment 6 — Taxonomie canonique + uniformisation

**Objectif** : Définir l'arborescence cible par catégorie et faire converger les FileInstances vers leur chemin canonique sur leur volume actuel.

**Scope** :
- Modèle `library_taxonomy` + UI d'édition des templates (chemin et nom)
- Templates par défaut livrés pour chaque catégorie
- Sous-catégories paramétrables (notamment pour Documents officiels)
- Moteur Scriban avec contexte par catégorie
- Sanitization Windows
- Calcul du chemin cible pour chaque FileInstance (intra-volume seulement à ce stade)
- Preview en lot des renames + moves intra-volume
- Exécution transactionnelle + write-back métadonnées dans le fichier
- Gestion des conflits (`(2)`, `(3)`)

**Hors scope** : réplication cross-volume, triage documents.

**Critères** :
- 100 livres audio uniformisés sur un volume sans collision
- Annulation complète d'un batch
- Modification d'un template → re-évaluation proposée à l'utilisateur

**Effort** : L

---

### Increment 7 — Triage de documents

**Objectif** : Workflow rapide pour catégoriser et nommer les documents officiels non identifiés.

**Scope** :
- Génération thumbnail première page (PDF + images)
- Extraction texte première page (PdfPig)
- OCR Tesseract en fallback pour scans
- Pipeline heuristique : regex pour dates, patterns pour types de documents, détection d'entités capitalisées pour la source
- Table `triage_patterns` initiale (patterns livrés pour cas courants : Facture, Appel de fonds, Convocation AG, Procès-verbal, Avis, Relevé...)
- UI grille de triage : thumbnail + snippet + trois champs (source, objet, date) + catégorie/sous-catégorie + checkbox validation
- Action « Appliquer » → renommage selon template `[{source}] - {objet} - {date}.{ext}` + placement
- Apprentissage simple : enregistrement des corrections, dérivation périodique de nouveaux patterns

**Hors scope** : assistance LLM (à voir plus tard, opt-in).

**Critères** :
- Triage de 50 documents en < 15 min
- Pré-remplissage automatique correct sur 60 %+ des champs pour patterns courants
- Patterns appris améliorent visiblement le pré-remplissage sur les batchs suivants

**Effort** : L

---

### Increment 8 — Enrichissement online (opt-in)

**Objectif** : Compléter les métadonnées manquantes via APIs externes, privacy-first.

**Scope** :
- `IMetadataProvider` + adapters OpenLibrary, Google Books, TMDB, MusicBrainz
- Construction requête à partir des champs structurés UNIQUEMENT (test automatisé garantit zéro path/filename brut sortant)
- Cache `api_cache` (clé = hash requête normalisée)
- Auto-application si confidence > seuil paramétrable
- Toggle mode 100 % local global et par catégorie
- Settings UI pour clés API

**Critères** :
- Audit log : aucun filename ou path brut HTTP-out
- Toggle local-only désactive totalement les sorties réseau
- Cache hit rate > 50 % en usage répété

**Effort** : M

---

### Increment 9 — Multi-volume et identification stable

**Objectif** : Activer le scan et le suivi de plusieurs volumes, y compris débranchés ; reconnaître un disque branché sur un autre PC.

**Pré-requis acquis** : la table `volumes` et la FK `file_instances.volume_id` sont en place depuis Inc 3. Cet incrément ajoute la machinerie réelle d'identification, d'orchestration et de scan optimisé.

**Scope** :
- Identification stable : VSN sur NTFS, hostname + share sur SMB, marqueur `.Arboryn` à la racine de chaque volume
- Enrôlement guidé d'un nouveau volume (création du marqueur, choix du friendly name)
- Migration des FileInstances existants du volume « default » vers leurs volumes réels identifiés au branchement
- UI dédiée volumes : liste, statut (connecté / hors-ligne / inconnu), dernier scan, taille
- Scan multi-volume : queue séquentielle, parallélisme intra-volume borné
- Support UNC / NAS avec timeout configurable et retry exponentiel
- USN Journal pour re-scan incrémental sur NTFS (position USN persistée par volume)
- Fallback mtime pour volumes non-NTFS / SMB
- Filtres par volume source dans la vue catalogue et la vue doublons

**Critères** :
- Disque externe scanné sur PC1 reconnu sans rescan au branchement sur PC2
- Re-scan d'un volume 1 To en < 1 min via USN
- Migration des FileInstances « default » → vrais volumes sans perte de référence aux LogicalFiles

**Effort** : L

---

### Increment 10 — Moteur de réplication multi-support

**Objectif** : Faire converger chaque volume vers son contenu cible, défini par son ReplicationScope.

**Scope** :
- Modèle `ReplicationScope` + UI d'édition d'expressions de scope
- Calcul du PlacementPlan complet (cf. § 5.5)
- Détection de conflits (versions différentes du même LogicalFile sur volumes différents)
- UI de revue du plan : récap par volume, impact espace, drill-down
- Désactivation sélective d'opérations
- Exécution batch avec gestion des volumes hors-ligne (opérations « pending » reprises au branchement)
- Vérification d'intégrité post-copie (hash compare)
- Journal complet pour undo

**Hors scope** : sync continue automatique (manuel uniquement à ce stade).

**Critères** :
- Plan généré en < 5 min pour un catalogue de 50k LogicalFiles sur 4 volumes
- Branchement d'un volume hors-ligne reprend les opérations en attente
- Cohérence post-exécution : un re-scan ne révèle plus de diff vs cible

**Effort** : L

---

### Increment 11 — Dashboard inventaire

**Objectif** : Visualiser instantanément la bibliothèque et son état multi-support.

**Scope** :
- Vue matricielle volumes × catégories avec présent/en-scope
- Vue gap (à copier) et surplus (à supprimer ou déplacer)
- Recherche cross-volume sur un LogicalFile (« où est X ? »)
- Stats globales : taux de redondance, espace par catégorie, volumes hors-ligne
- Indicateurs de santé : conflits non résolus, opérations en attente, dernier scan trop ancien
- Export CSV/JSON de l'inventaire

**Critères** :
- Réponse < 200 ms à la recherche cross-volume sur 50k+ LogicalFiles
- Identification visuelle immédiate des gaps majeurs

**Effort** : M

---

### Increment 12 — Polish, performance, i18n

**Objectif** : Optimiser pour 4 To, finaliser l'UX, préparer la distribution.

**Scope** :
- Profiling et optimisation (mémoire, I/O, parallélisme)
- File System Watcher pour mise à jour incrémentale post-scan initial
- Raccourcis clavier, recherche avancée, filtres
- Rapport d'activité (espace récupéré, fichiers traités, opérations annulées)
- Aide intégrée / tooltips
- I18n EN + FR

**Critères** :
- Re-scan 4 To en < 5 min via USN
- UI réactive sur 50k+ LogicalFiles

**Effort** : M

---

### Futur — Préparation à la distribution

À planifier après stabilisation de l'usage personnel :

- Snapshot mode avec pré-check d'espace
- Packaging MSIX + signature
- Documentation utilisateur, site vitrine, FAQ
- Télémétrie opt-in
- Mise à jour automatique
- Assistance LLM pour triage (opt-in, local ou paid)
- Sync continue automatique sur connexion volume
- Multi-PC sync (cloud index ou peer-to-peer)
- Plugins providers tiers
- Beta privée

---

## 10. Hors scope (initial)

- Synchronisation cloud propriétaire (Drive, OneDrive)
- Comparaison de contenu PDF (texte similaire entre éditions différentes)
- Détection de visages dans photos
- Conversion de formats
- Interface web ou mobile
- Mode CLI
- Édition de métadonnées en bulk hors contexte
- Recherche full-text dans le contenu

---

## 11. Glossaire

- **LogicalFile** : œuvre ou document identifié par son contenu, indépendamment de sa localisation. Porte les métadonnées canoniques et le chemin canonique cible.
- **FileInstance** : copie physique d'un LogicalFile sur un Volume. Plusieurs instances peuvent pointer vers le même LogicalFile.
- **Canonical path / canonical filename** : chemin et nom cibles d'un LogicalFile, calculés par les templates de sa catégorie.
- **Volume** : disque physique ou NAS identifié de façon stable via marqueur `.Arboryn`.
- **ReplicationScope** : expression définissant les LogicalFiles destinés à un volume donné.
- **PlacementPlan** : ensemble d'opérations (rename / move / copy / delete) pour faire converger l'état physique vers la cible.
- **Triage** : workflow d'identification et de catégorisation rapide pour documents non classés.
- **Triage pattern** : règle (regex + template) servant à extraire automatiquement source / objet / date d'un document.
- **Write-back** : écriture des métadonnées enrichies dans le fichier lui-même.
- **USN Journal** : journal NTFS exploité pour re-scan incrémental rapide.
- **Privacy-first** : aucun nom de fichier brut ni chemin ne sort vers une API tierce.
- **Memorized volume** : volume non actuellement connecté dont l'index est conservé pour comparaison.
