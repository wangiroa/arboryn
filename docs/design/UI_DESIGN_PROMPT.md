# Prompt — Conception UI Arboryn

> À copier-coller dans **Claude Design** (claude.ai/design) ou **Stitch**
> (stitch.withgoogle.com). Le corps du prompt est identique ; seuls les
> derniers paragraphes (« Format de sortie attendu ») changent selon l'outil.
> Choisis la section finale qui correspond à l'outil utilisé.

---

## Prompt commun (Claude Design *et* Stitch)

### 1. Contexte produit

Arboryn est une application **Windows 11 desktop** de gestion unifiée d'une
bibliothèque média multi-support. L'utilisateur possède des fichiers
répartis sur plusieurs PCs, NAS et disques externes USB (jusqu'à 4 To par
disque), avec actuellement des doublons, des nommages incohérents et des
arborescences divergentes. Arboryn fournit :

1. Un **catalogue logique unifié** (LogicalFile ↔ FileInstance) qui
   distingue l'œuvre/le document de ses copies physiques.
2. L'**uniformisation** de l'arborescence et du nommage par catégorie via
   des templates canoniques.
3. La **déduplication** intra-volume (exacte, floue, perceptuelle).
4. La **réplication multi-support** contrôlée : chaque volume a son propre
   périmètre de contenu (`ReplicationScope`).
5. Un **triage assisté** des documents officiels (factures, AG, appels de
   fonds, etc.) avec OCR et patterns appris.
6. Un **enrichissement online privacy-first** des métadonnées
   (OpenLibrary, TMDB, MusicBrainz…) — désactivable globalement.
7. Un **dashboard d'inventaire** type matrice volumes × catégories avec
   vues *gap* (manquants) et *surplus* (hors scope).

Catégories médias gérées : Livres audio, Livres, Vidéos, Photos, Documents
officiels (avec sous-catégories paramétrables), PDF divers.

### 2. Stack et contraintes de design

- **Framework UI** : WinUI 3 / .NET 8 → suivre strictement le langage
  **Fluent Design Windows 11** (Mica, acrylic, coins arrondis 4–8 px,
  Reveal hover, animations Connected/Implicit).
- **Iconographie** : Segoe Fluent Icons exclusivement (référencer les
  glyphes par leur nom de l'icon library Microsoft, ex. `Folder`,
  `BroomFilled`, `CloudArrowUp`, `DocumentSearch`, `Library`…).
- **Typographie** : Segoe UI Variable (Display / Text / Small).
- **Thèmes** : clair **et** sombre, équivalents.
- **Couleur d'accent** : ton naturel (vert forêt ou ambre) cohérent avec
  le nom « Arboryn » (racine, arbre). Propose une palette précise.
- **Langue UI** : français (FR-FR). Tous les textes des maquettes
  doivent être rédigés en français naturel, jamais en lorem ipsum.
- **Densité** : « confort » par défaut, mode « compact » disponible pour
  les vues tabulaires de grande taille (catalogue 50k+ lignes).
- **Accessibilité** : contraste AA minimum, navigation clavier complète,
  raccourcis explicités (les indiquer dans la maquette).
- **Responsive** : fenêtre principale dimensionnable de 1280×800 à
  4K ; les vues lourdes doivent encaisser jusqu'à 50 000 lignes.

### 3. Architecture de navigation

Coque principale type **NavigationView** (rail latéral à gauche,
collapsible en hamburger sous 900 px). Sections de navigation :

1. **Tableau de bord** — vue d'accueil
2. **Volumes** — liste, statut, enrôlement, scopes de réplication
3. **Catalogue** — explorateur des LogicalFiles + recherche cross-volume
4. **Doublons** — groupes de FileInstances en conflit
5. **Uniformisation** — preview des renames/moves par catégorie
6. **Triage** — workflow des documents officiels non classés
7. **Plan de placement** — revue et exécution des opérations cross-volume
8. **Taxonomie** — éditeur des templates par catégorie
9. **Historique** — journal des opérations, undo, rapports d'activité
10. **Réglages** — providers API, mode local-only, raccourcis, langue,
    chemins, télémétrie opt-in

Header global : nom du volume actif (ou « Tous »), barre de recherche
universelle, indicateur d'activité scan/job en cours, menu utilisateur.

### 4. Liste exhaustive des écrans à concevoir

Pour chaque écran, prévoir au minimum les **états** suivants : vide
(*empty state* avec illustration et CTA), chargement (skeleton ou
progress), peuplé (cas nominal), erreur (InfoBar + action de récupération).

#### 4.1. Tableau de bord (`/dashboard`)
- Carte « Santé du système » : volumes connectés / hors-ligne,
  opérations en attente, conflits non résolus, dernier scan trop ancien.
- Carte « Inventaire » : N LogicalFiles, N FileInstances, taux de
  redondance, espace par catégorie (donut chart).
- Carte « Activité récente » : 10 dernières opérations (renames, copies,
  deletes), avec bouton undo contextuel.
- Carte « À faire » : suggestions actionnables (« 142 documents en attente
  de triage », « 1 200 doublons détectés », « 3 volumes pas re-scannés
  depuis 14 j »).

#### 4.2. Volumes
- **Liste des volumes** (`/volumes`) : DataGrid avec colonnes nom,
  type (local NTFS / USB / SMB), statut (connecté / hors-ligne / inconnu),
  espace utilisé/libre (barre de progression), dernier scan, scope
  attribué. Filtres rapides. Bouton « Enrôler un nouveau volume ».
- **Détail d'un volume** (`/volumes/{id}`) : en-tête identité (label,
  serial, fingerprint, marqueur `.Arboryn`), tabs : *Vue d'ensemble*,
  *Scope de réplication*, *Historique des scans*, *Réglages*.
- **Enrôlement** (modal stepper) : (1) détection du disque, (2) choix
  friendly name, (3) écriture marqueur, (4) scope initial proposé,
  (5) confirmation et premier scan.
- **Éditeur de ReplicationScope** : éditeur d'expression visuel
  (constructeur de conditions `category IN (...)`,
  `subcategory = ...`, `year >= ...`) + aperçu *live* du nombre de
  LogicalFiles ciblés.

#### 4.3. Catalogue
- **Explorateur LogicalFile** (`/catalog`) : navigation par
  catégorie → sous-catégorie → auteur/série → œuvre. Vue liste *et* vue
  grille (couvertures pour livres/films). Sidebar filtres (catégorie,
  volume présent, format, année, source d'enrichissement). Toggle
  densité confort/compact.
- **Recherche cross-volume** (`/catalog/search`) : barre de recherche
  proéminente, résultats groupés par LogicalFile, chaque résultat
  détaille les FileInstances (volume, chemin, statut connecté/hors-ligne).
- **Détail d'un LogicalFile** (`/catalog/{id}`) : panneau métadonnées
  canoniques éditables, liste des FileInstances avec actions par ligne,
  miniature/cover, source des métadonnées (badge), bouton « Re-évaluer
  le chemin canonique ».

#### 4.4. Doublons
- **Liste des groupes** (`/duplicates`) : pivots ou segmented control par
  type de groupe (`exact_name`, `fuzzy_name`, `exact_hash`,
  `perceptual`), table avec confidence, nombre de membres, action en
  lot.
- **Comparaison de groupe** : trois layouts selon le média
  - *Documents/audio* : side-by-side métadonnées + bouton play/preview ;
  - *Images* : comparaison visuelle deux à deux (slider de diff) ;
  - *Audio musical* : waveforms superposées, score Chromaprint.
  Auto-suggestion « préférable » (badge), case à cocher par membre,
  actions « Garder N, supprimer les autres », « Tout vers la corbeille ».

#### 4.5. Uniformisation
- **Vue par catégorie** (`/normalize`) : tableau récapitulatif
  catégorie → N à renommer, N à déplacer, conflits potentiels.
- **Preview en lot** : DataGrid `Avant → Après` (chemin + nom), avec
  diff coloré et possibilité de désélection ligne par ligne. CommandBar :
  « Exécuter (intra-volume) », « Annuler », « Exporter CSV ».
- **Conflits** : panneau dédié quand `(2)`, `(3)` ou collisions
  ambiguës détectés.

#### 4.6. Triage documents officiels
- **Grille de triage** (`/triage`) : 20–50 documents par page, chaque
  ligne :
  - miniature première page (clic → zoom)
  - snippet texte extrait
  - trois champs éditables avec auto-complétion : **Source**, **Objet**,
    **Date**, chacun avec un *confidence badge* (vert/orange/rouge)
  - dropdown catégorie + sous-catégorie
  - checkbox « validé »
- Bouton flottant « Appliquer N validés » avec preview du renommage
  `[{source}] - {objet} - {date:yyyyMM}.{ext}`.
- **Mode focus** (drawer plein écran) : un document à la fois pour cas
  difficiles, avec viewer PDF intégré et OCR à la demande.
- **Apprentissage** : toast discret après chaque correction
  (« Nouveau pattern dérivé : `Foncia` reconnu comme Source »).

#### 4.7. Plan de placement
- **Synthèse** (`/placement-plan`) : carte par volume avec impact espace
  (gain/perte), nombre d'opérations par type (rename / move / copy /
  delete), volumes hors-ligne marqués `pending`.
- **Drill-down** : arbre catégorie → LogicalFile → opérations, avec
  toggle d'activation par ligne.
- **Conflits** : panneau séparé listant les LogicalFiles dont des
  versions divergentes existent sur plusieurs volumes ; pour chaque
  conflit, un wizard de résolution.
- **Exécution** : modal de confirmation avec récap final, barre de
  progression par volume, logs en temps réel, bouton pause/cancel.

#### 4.8. Taxonomie
- **Liste des catégories** (`/taxonomy`) : carte par catégorie avec
  template de chemin et de nom (Scriban), champs requis, sous-catégories,
  toggle actif/inactif, badge version.
- **Éditeur de template** : éditeur de code Scriban avec autocomplétion
  des variables disponibles, **preview live** sur 5 LogicalFiles
  réels, validation Sanitize Windows.
- **Sous-catégories** : éditeur d'arbre pour Documents officiels
  (Investissements > Appartement Champigny > Factures…).
- **Versioning** : historique des templates avec diff et rollback.

#### 4.9. Historique & rapports
- **Journal des opérations** (`/history`) : timeline ou DataGrid avec
  filtre par batch_id, type, volume, date. Undo par batch (fenêtre
  configurable).
- **Rapports d'activité** : graphique espace récupéré / mois,
  opérations exécutées, opérations annulées, gain de cohérence (% de
  FileInstances au chemin canonique).
- **Opérations en attente** : liste des opérations `pending` sur
  volumes hors-ligne, avec date de création et volume cible.

#### 4.10. Réglages
- Sections : *Général* (langue FR/EN, thème, raccourcis),
  *Volumes & scan* (parallélisme, USN, timeouts SMB),
  *Enrichissement* (toggle global local-only, clés API par provider,
  cache TTL, seuil de confiance auto-apply, toggle par catégorie),
  *Triage* (langues OCR, taille thumbnail, opt-in LLM),
  *Sécurité* (corbeille vs hard-delete, taille fenêtre undo, snapshot
  mode), *Avancé* (chemins index DB, logs, télémétrie opt-in).

#### 4.11. Composants transverses à concevoir
- **Barre de scan** : progress bar persistante en bas avec volume en
  cours, vitesse, ETA, bouton pause/cancel, expandable en panneau de
  détails.
- **Empty states** illustrés (style ligne, cohérent avec Fluent).
- **InfoBar globale** (succès / info / avertissement / erreur).
- **Confirmation destructive** : dialog modal avec récap, case « Je
  comprends », bouton danger.
- **Composant FileInstance row** réutilisé partout : icône statut,
  chemin tronqué intelligemment, volume badge, métadonnées en tooltip.

### 5. Principes UX directeurs

- **Reversibility first** : tout est annulable, l'UI le montre clairement
  (badge undo disponible, fenêtre temporelle visible).
- **Plan avant action** : aucune opération destructive sans preview en
  lot validé explicitement (≥ 10 → dry-run obligatoire).
- **Distinction LogicalFile vs FileInstance** rendue visuelle partout
  (ex. icône d'œuvre vs icône de copie + volume).
- **Multi-volume sans friction** : un disque débranché n'est pas une
  erreur, c'est un état (`memorized`) avec actions différées explicites.
- **Privacy-first visible** : badge clair sur chaque champ enrichi
  online, toggle local-only proéminent.
- **Triage rapide** : la vue de triage doit permettre de traiter
  50 documents en < 15 min — privilégier le clavier, l'auto-completion,
  les actions en lot.

### 6. Design system à livrer

- **Tokens de couleur** (clair + sombre) : surfaces, texte, accent,
  semantic (success / warning / danger / info), états (hover, pressed,
  selected, disabled).
- **Échelle typographique** : Display / Title Large / Title / Subtitle /
  Body Strong / Body / Caption (tailles, weights, line-heights).
- **Espacement** : échelle 4 / 8 / 12 / 16 / 24 / 32 / 48.
- **Rayons** : 4 / 8 / 12.
- **Élévations / shadows** (Fluent layers).
- **Iconographie** : liste de tous les glyphes Segoe Fluent Icons
  utilisés, avec leur nom de référence.
- **Composants partagés** documentés : navigation rail, command bar,
  data grid, info bar, teaching tip, dialog, expander, scope expression
  builder, file instance row, logical file card.

---

## Format de sortie attendu — version **Claude Design**

Claude Design n'exporte qu'en HTML, PPTX ou PDF. On vise donc **un document
HTML unique, sémantique et auto-suffisant**, que je pourrai parser
directement avec un outil de lecture de fichiers.

### Structure du document HTML attendu

Produire **une seule page HTML** (`arboryn-ui.html`) avec la structure
suivante :

```html
<!doctype html>
<html lang="fr">
<head>
  <meta charset="utf-8">
  <title>Arboryn UI — Design</title>
</head>
<body>

  <section data-block="design-system" id="design-system">
    <h1>Design System</h1>

    <section data-token-group="colors-light">
      <h2>Couleurs — thème clair</h2>
      <dl>
        <dt data-token="surface-primary">Surface primaire</dt>
        <dd data-hex="#FFFFFF">#FFFFFF</dd>
        <!-- … tous les tokens -->
      </dl>
    </section>

    <section data-token-group="colors-dark"> … </section>
    <section data-token-group="typography"> … </section>
    <section data-token-group="spacing"> … </section>
    <section data-token-group="radius"> … </section>
    <section data-token-group="shadows"> … </section>

    <section data-token-group="icons">
      <h2>Icônes Segoe Fluent Icons</h2>
      <ul>
        <li data-icon-name="Folder" data-symbol="Symbol.Folder">📁 Folder</li>
        <!-- … -->
      </ul>
    </section>
  </section>

  <section data-block="navigation-map" id="navigation-map">
    <h1>Carte de navigation</h1>
    <pre data-format="mermaid">
graph LR
  dashboard --> volumes-list
  dashboard --> catalog
  …
    </pre>
  </section>

  <section data-block="components-library" id="components-library">
    <h1>Composants partagés</h1>

    <article data-component="file-instance-row">
      <h2>FileInstance Row</h2>
      <figure>
        <img src="data:image/png;base64,…" alt="FileInstance Row — light">
        <img src="data:image/png;base64,…" alt="FileInstance Row — dark">
      </figure>
      <section data-block="api">
        <h3>Props attendues</h3>
        <ul>
          <li><code>VolumeBadge</code> : couleur dérivée du volume_id</li>
          <li><code>StatusGlyph</code> : connected / offline / pending</li>
          …
        </ul>
      </section>
    </article>
    <!-- … autres composants -->
  </section>

  <section data-block="screens" id="screens">
    <h1>Écrans</h1>

    <article data-screen-id="dashboard" data-route="/dashboard"
             data-nav-section="Tableau de bord">
      <h2>Tableau de bord</h2>

      <section data-block="objective">
        <p>1–2 phrases sur l'objectif de l'écran.</p>
      </section>

      <figure data-block="mockup">
        <img src="data:image/png;base64,…"
             alt="Tableau de bord — thème clair"
             data-theme="light" width="1440" height="900">
        <img src="data:image/png;base64,…"
             alt="Tableau de bord — thème sombre"
             data-theme="dark" width="1440" height="900">
      </figure>

      <section data-block="root-layout">
        <h3>Layout racine</h3>
        <p>NavigationView page → Grid 2 colonnes (sidebar 280 px /
        contenu *).</p>
      </section>

      <section data-block="component-tree">
        <h3>Arbre des composants</h3>
        <ul>
          <li data-fluent="NavigationView">
            <ul>
              <li data-fluent="Grid" data-role="content">
                <ul>
                  <li data-fluent="StackPanel" data-role="cards"> … </li>
                </ul>
              </li>
            </ul>
          </li>
        </ul>
      </section>

      <section data-block="states">
        <h3>États</h3>
        <dl>
          <dt>vide</dt><dd>Description + CTA</dd>
          <dt>chargement</dt><dd>Skeleton / progress</dd>
          <dt>peuplé</dt><dd>Cas nominal</dd>
          <dt>erreur</dt><dd>Message + récupération</dd>
        </dl>
      </section>

      <section data-block="navigation">
        <h3>Interactions</h3>
        <ul>
          <li data-target="volumes-list">Clic « Voir tous les volumes »</li>
          …
        </ul>
      </section>

      <section data-block="shortcuts">
        <h3>Raccourcis clavier</h3>
        <dl>
          <dt>Ctrl+K</dt><dd>Recherche universelle</dd>
        </dl>
      </section>

      <section data-block="accessibility">
        <h3>Accessibilité</h3>
        <ul><li>Contraste AA validé sur tous les textes</li></ul>
      </section>

      <section data-block="viewmodel-mapping">
        <h3>Mapping ViewModel</h3>
        <table>
          <thead><tr><th>Champ UI</th><th>Propriété ViewModel</th></tr></thead>
          <tbody>
            <tr><td>Carte « Volumes connectés »</td>
                <td><code>HealthVM.ConnectedVolumesCount</code></td></tr>
            …
          </tbody>
        </table>
      </section>
    </article>

    <!-- … un <article data-screen-id="…"> par écran de §4 -->
  </section>

</body>
</html>
```

### Contraintes impératives sur le HTML

- **Une seule page**, aucune feuille de style externe nécessaire pour le
  parsing (un style inline minimal pour la lisibilité humaine est ok).
- **Toutes les images** (maquettes des écrans, composants) en
  `<img src="data:image/png;base64,…">` afin que le fichier soit
  auto-suffisant après export.
- **Chaque écran** = un `<article data-screen-id="…">` unique avec les
  sous-blocs `objective`, `mockup`, `root-layout`, `component-tree`,
  `states`, `navigation`, `shortcuts`, `accessibility`,
  `viewmodel-mapping` dans cet ordre.
- **Chaque maquette** : deux `<img>` avec `data-theme="light"` et
  `data-theme="dark"`, dimensions 1440×900 minimum.
- **Aucun texte significatif** ne doit vivre uniquement dans une image —
  toujours le dupliquer en texte HTML à côté (pour parsing et a11y).
- **Pas de JavaScript**, pas de iframes, pas de ressources externes.

### Livraison

Après production sur le canevas Claude Design, **exporter en HTML** et
me transmettre le fichier `arboryn-ui.html`. Je le lis et j'en extrais
spec + maquette par écran pour piloter l'implémentation XAML.

---

## Format de sortie attendu — version **Stitch (stitch.withgoogle.com)**

Stitch produit nativement des frames Figma-like. Adapter comme suit :

1. **Un projet Stitch unique** nommé `Arboryn UI`.
2. **Une page « Design System »** en première position contenant :
   - Palette clair + sombre sous forme de cartes couleur (hex visible).
   - Échelle typographique (un sample par niveau).
   - Échelle d'espacement et rayons.
   - Bibliothèque des icônes Segoe Fluent Icons utilisées (texte du
     glyphe + nom à côté).
3. **Une frame par écran** listé en §4 (taille 1440×900), **deux
   variantes** : `light` et `dark`.
4. **Une page « Navigation »** avec une frame contenant un schéma
   visuel des transitions entre écrans (flèches entre miniatures).
5. **Une page « Composants partagés »** avec une frame par composant
   réutilisable (FileInstance row, LogicalFile card,
   ScopeExpressionBuilder, ScanProgressBar, EmptyState illustré,
   InfoBar, ConfirmDestructiveDialog).
6. **Pour chaque frame d'écran**, ajouter en marge droite (zone hors
   canvas mais dans la même frame) un **bloc de notes texte** structuré
   ainsi (en français) :

   ```
   ID : <slug>
   Route : /...
   Layout racine : <type Fluent + zones>
   Composants :
     - <nom> : <type Fluent natif/custom>
   États : vide / chargement / peuplé / erreur (résumés en une ligne chacun)
   Navigation : <transitions sortantes>
   Raccourcis : <combos>
   Données : <champs UI ← propriétés ViewModel>
   ```

7. **Export attendu** : projet Stitch partagé + export ZIP de toutes les
   frames en PNG haute résolution (un PNG par variante light/dark),
   nommés `<screen-id>.<theme>.png`.

---

## Notes finales pour les deux outils

- Si un écran impose un trade-off (densité vs lisibilité, par exemple),
  **proposer deux variantes** plutôt que trancher seul ; je choisirai.
- Pour les composants spécifiques au domaine (ScopeExpressionBuilder,
  comparateur perceptuel images, waveforms audio), prendre le temps
  d'inventer un design original cohérent avec Fluent — pas de copie
  d'autres apps.
- L'application sera plus tard packagée MSIX et potentiellement
  distribuée : viser un niveau de finition « produit commercial ».
- Tu peux poser des questions de clarification avant de produire si une
  ambiguïté bloque, mais tente d'abord de proposer une réponse argumentée.
