-- =============================================================================
-- Arboryn — Migration 001 : Initial Schema
-- =============================================================================
-- Cette migration installe le schéma complet anticipé du projet, y compris
-- les tables nécessaires aux incréments futurs (taxonomie, triage, plans,
-- réplication). Les tables non utilisées par les incréments précoces
-- restent vides et n'ont aucun coût.
--
-- Conventions :
--   - IDs : TEXT (UUID v4 stockés en chaîne)
--   - Dates : TEXT au format ISO 8601 (datetime('now') en SQLite)
--   - Tailles : INTEGER (SQLite INTEGER = 64 bits signés, suffisant)
--   - JSON : TEXT (utiliser json_*() fonctions SQLite si besoin)
--   - Enums : TEXT avec CHECK constraints
--   - Booléens : INTEGER 0/1 avec CHECK
--   - Hashs perceptuels (pHash 64 bits unsigned) : TEXT hex 16 chars
-- =============================================================================

-- -----------------------------------------------------------------------------
-- PRAGMAs (à exécuter à chaque ouverture de connexion, pas seulement à la
-- création — gardés ici à titre documentaire)
-- -----------------------------------------------------------------------------
-- PRAGMA foreign_keys = ON;
-- PRAGMA journal_mode = WAL;
-- PRAGMA synchronous = NORMAL;
-- PRAGMA temp_store = MEMORY;

-- -----------------------------------------------------------------------------
-- Suivi des migrations
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS schema_versions (
    version     INTEGER PRIMARY KEY,
    applied_at  TEXT NOT NULL DEFAULT (datetime('now')),
    description TEXT
);

-- -----------------------------------------------------------------------------
-- 1. Volumes et scopes de réplication
-- -----------------------------------------------------------------------------

CREATE TABLE replication_scopes (
    id              TEXT PRIMARY KEY,
    name            TEXT NOT NULL,
    expression_json TEXT NOT NULL,
    created_at      TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE volumes (
    id                   TEXT PRIMARY KEY,
    name                 TEXT NOT NULL,
    kind                 TEXT NOT NULL CHECK (kind IN ('internal', 'external', 'nas', 'other', 'default')),
    serial               TEXT,
    fingerprint          TEXT,
    label                TEXT,
    mount_point          TEXT,                    -- chemin d'accès courant (peut changer entre branchements)
    last_usn             INTEGER,                 -- position du USN Journal NTFS au dernier scan
    last_seen_at         TEXT,
    last_scan_at         TEXT,
    status               TEXT NOT NULL CHECK (status IN ('online', 'offline', 'unknown')) DEFAULT 'unknown',
    replication_scope_id TEXT,
    created_at           TEXT NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (replication_scope_id) REFERENCES replication_scopes(id) ON DELETE SET NULL
);

CREATE INDEX idx_volumes_serial ON volumes(serial);
CREATE INDEX idx_volumes_status ON volumes(status);

-- -----------------------------------------------------------------------------
-- 2. Catalogue logique : LogicalFile et FileInstance
-- -----------------------------------------------------------------------------

CREATE TABLE logical_files (
    id                      TEXT PRIMARY KEY,
    category                TEXT NOT NULL,
    subcategory             TEXT,
    canonical_path          TEXT,
    canonical_filename      TEXT,
    content_signature_kind  TEXT CHECK (content_signature_kind IN ('sha256', 'phash', 'chromaprint', 'name_size')),
    content_signature       TEXT,
    primary_metadata_json   TEXT,
    created_at              TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at              TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX idx_logical_files_content_signature ON logical_files(content_signature);
CREATE INDEX idx_logical_files_category ON logical_files(category);

CREATE TABLE file_instances (
    id              TEXT PRIMARY KEY,
    logical_file_id TEXT,                                                    -- nullable pendant la phase d'identification
    volume_id       TEXT NOT NULL,
    relative_path   TEXT NOT NULL COLLATE NOCASE,                            -- Windows = case-insensitive
    canonical_name  TEXT NOT NULL,                                           -- nom canonique pour détection rapide (déjà lowercased)
    size            INTEGER NOT NULL,
    modified_at     TEXT NOT NULL,
    created_at      TEXT,
    sha256          TEXT,                                                    -- hex 64 chars
    phash           TEXT,                                                    -- hex 16 chars (pHash 64 bits)
    chromaprint     TEXT,                                                    -- empreinte acoustique
    category        TEXT,
    status          TEXT NOT NULL CHECK (status IN ('active', 'missing', 'deleted', 'pending_classification')) DEFAULT 'active',
    discovered_at   TEXT NOT NULL DEFAULT (datetime('now')),
    last_seen_at    TEXT NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (logical_file_id) REFERENCES logical_files(id) ON DELETE SET NULL,
    FOREIGN KEY (volume_id) REFERENCES volumes(id) ON DELETE CASCADE,
    UNIQUE (volume_id, relative_path)
);

CREATE INDEX idx_file_instances_canonical_name ON file_instances(canonical_name);
CREATE INDEX idx_file_instances_sha256         ON file_instances(sha256);
CREATE INDEX idx_file_instances_phash          ON file_instances(phash);       -- exact match seulement ; BK-tree externe pour Hamming
CREATE INDEX idx_file_instances_chromaprint    ON file_instances(chromaprint);
CREATE INDEX idx_file_instances_logical_file   ON file_instances(logical_file_id);
CREATE INDEX idx_file_instances_volume         ON file_instances(volume_id);
CREATE INDEX idx_file_instances_status         ON file_instances(status);
CREATE INDEX idx_file_instances_size           ON file_instances(size);
CREATE INDEX idx_file_instances_canonical_size ON file_instances(canonical_name, size);  -- détection Inc 1

-- -----------------------------------------------------------------------------
-- 3. Métadonnées multi-sources
-- -----------------------------------------------------------------------------

CREATE TABLE file_metadata (
    file_instance_id    TEXT NOT NULL,
    key                 TEXT NOT NULL,
    value               TEXT,
    source              TEXT NOT NULL,                                       -- filename, exif, id3, pdf, epub, online_<provider>, user, triage
    confidence          REAL NOT NULL DEFAULT 1.0 CHECK (confidence >= 0.0 AND confidence <= 1.0),
    extracted_at        TEXT NOT NULL DEFAULT (datetime('now')),
    PRIMARY KEY (file_instance_id, key, source),
    FOREIGN KEY (file_instance_id) REFERENCES file_instances(id) ON DELETE CASCADE
);

CREATE INDEX idx_file_metadata_key_value ON file_metadata(key, value);

-- -----------------------------------------------------------------------------
-- 4. Groupes de doublons
-- -----------------------------------------------------------------------------

CREATE TABLE duplicate_groups (
    id          TEXT PRIMARY KEY,
    kind        TEXT NOT NULL CHECK (kind IN ('exact_name', 'fuzzy_name', 'exact_hash', 'perceptual')),
    confidence  REAL NOT NULL DEFAULT 1.0,
    created_at  TEXT NOT NULL DEFAULT (datetime('now')),
    status      TEXT NOT NULL CHECK (status IN ('pending', 'reviewed', 'resolved', 'dismissed')) DEFAULT 'pending'
);

CREATE INDEX idx_duplicate_groups_status ON duplicate_groups(status);
CREATE INDEX idx_duplicate_groups_kind   ON duplicate_groups(kind);

CREATE TABLE group_members (
    group_id            TEXT NOT NULL,
    file_instance_id    TEXT NOT NULL,
    score               REAL NOT NULL DEFAULT 0.0,                           -- score "préférable" pour aide à la décision
    user_action         TEXT CHECK (user_action IN ('keep', 'delete', 'undecided')) DEFAULT 'undecided',
    PRIMARY KEY (group_id, file_instance_id),
    FOREIGN KEY (group_id) REFERENCES duplicate_groups(id) ON DELETE CASCADE,
    FOREIGN KEY (file_instance_id) REFERENCES file_instances(id) ON DELETE CASCADE
);

CREATE INDEX idx_group_members_file_instance ON group_members(file_instance_id);

-- -----------------------------------------------------------------------------
-- 5. Taxonomie canonique
-- -----------------------------------------------------------------------------

CREATE TABLE library_taxonomy (
    id                      TEXT PRIMARY KEY,
    category                TEXT NOT NULL,
    name_pattern            TEXT NOT NULL,                                   -- template Scriban
    path_pattern            TEXT NOT NULL,                                   -- template Scriban
    required_fields_json    TEXT NOT NULL DEFAULT '[]',                      -- liste des champs requis pour valider
    active                  INTEGER NOT NULL DEFAULT 1 CHECK (active IN (0, 1)),
    version                 INTEGER NOT NULL DEFAULT 1,
    created_at              TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at              TEXT NOT NULL DEFAULT (datetime('now')),
    UNIQUE (category, version)
);

CREATE INDEX idx_library_taxonomy_category_active ON library_taxonomy(category, active);

CREATE TABLE category_subcategories (
    id                  TEXT PRIMARY KEY,
    category            TEXT NOT NULL,
    subcategory_path    TEXT NOT NULL,                                       -- ex: 'Investissements/Appartement Champigny/Factures'
    label               TEXT NOT NULL,
    created_at          TEXT NOT NULL DEFAULT (datetime('now')),
    UNIQUE (category, subcategory_path)
);

CREATE INDEX idx_category_subcategories_category ON category_subcategories(category);

-- -----------------------------------------------------------------------------
-- 6. Triage de documents
-- -----------------------------------------------------------------------------

CREATE TABLE triage_patterns (
    id                  TEXT PRIMARY KEY,
    pattern_kind        TEXT NOT NULL CHECK (pattern_kind IN ('source', 'object', 'date')),
    regex               TEXT NOT NULL,                                       -- pattern .NET Regex
    template            TEXT,                                                -- template d'extraction (par ex. group capture)
    description         TEXT,
    learned_from_user   INTEGER NOT NULL DEFAULT 0 CHECK (learned_from_user IN (0, 1)),
    priority            INTEGER NOT NULL DEFAULT 0,
    active              INTEGER NOT NULL DEFAULT 1 CHECK (active IN (0, 1)),
    created_at          TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX idx_triage_patterns_kind_priority ON triage_patterns(pattern_kind, priority DESC) WHERE active = 1;

-- Table d'apprentissage : on enregistre chaque correction utilisateur pour
-- dériver périodiquement de nouveaux triage_patterns
CREATE TABLE triage_corrections (
    id                  TEXT PRIMARY KEY,
    file_instance_id    TEXT,
    snippet             TEXT NOT NULL,
    pattern_kind        TEXT NOT NULL CHECK (pattern_kind IN ('source', 'object', 'date')),
    extracted_value     TEXT,
    corrected_value     TEXT NOT NULL,
    created_at          TEXT NOT NULL DEFAULT (datetime('now')),
    derived_into_pattern_id TEXT,
    FOREIGN KEY (file_instance_id) REFERENCES file_instances(id) ON DELETE SET NULL,
    FOREIGN KEY (derived_into_pattern_id) REFERENCES triage_patterns(id) ON DELETE SET NULL
);

CREATE INDEX idx_triage_corrections_kind ON triage_corrections(pattern_kind);

-- -----------------------------------------------------------------------------
-- 7. Opérations et plans de placement
-- -----------------------------------------------------------------------------

CREATE TABLE operations (
    id                  TEXT PRIMARY KEY,
    kind                TEXT NOT NULL CHECK (kind IN ('rename', 'move', 'copy', 'delete', 'metadata_writeback')),
    file_instance_id    TEXT,
    source_volume_id    TEXT,
    target_volume_id    TEXT,
    old_path            TEXT,
    new_path            TEXT,
    old_metadata_json   TEXT,                                                -- pour undo du write-back
    status              TEXT NOT NULL CHECK (status IN ('pending', 'in_progress', 'completed', 'failed', 'cancelled', 'undone')) DEFAULT 'pending',
    error_message       TEXT,
    batch_id            TEXT,
    executed_at         TEXT,
    undone_at           TEXT,
    created_at          TEXT NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (file_instance_id) REFERENCES file_instances(id) ON DELETE SET NULL,
    FOREIGN KEY (source_volume_id) REFERENCES volumes(id) ON DELETE SET NULL,
    FOREIGN KEY (target_volume_id) REFERENCES volumes(id) ON DELETE SET NULL
);

CREATE INDEX idx_operations_batch        ON operations(batch_id);
CREATE INDEX idx_operations_status       ON operations(status);
CREATE INDEX idx_operations_file_instance ON operations(file_instance_id);
CREATE INDEX idx_operations_executed_at  ON operations(executed_at DESC);

CREATE TABLE placement_plans (
    id                          TEXT PRIMARY KEY,
    generated_at                TEXT NOT NULL DEFAULT (datetime('now')),
    status                      TEXT NOT NULL CHECK (status IN ('draft', 'reviewed', 'executing', 'completed', 'cancelled')) DEFAULT 'draft',
    total_operations            INTEGER NOT NULL DEFAULT 0,
    estimated_space_change_json TEXT,                                        -- {"volume_id": delta_bytes, ...}
    notes                       TEXT,
    completed_at                TEXT
);

CREATE INDEX idx_placement_plans_status ON placement_plans(status);

CREATE TABLE placement_plan_operations (
    id                      TEXT PRIMARY KEY,
    plan_id                 TEXT NOT NULL,
    operation_payload_json  TEXT NOT NULL,
    operation_id            TEXT,                                            -- rempli quand l'opération est créée et exécutée
    sequence_number         INTEGER NOT NULL,
    skip                    INTEGER NOT NULL DEFAULT 0 CHECK (skip IN (0, 1)), -- l'utilisateur peut décocher
    executed_at             TEXT,
    FOREIGN KEY (plan_id) REFERENCES placement_plans(id) ON DELETE CASCADE,
    FOREIGN KEY (operation_id) REFERENCES operations(id) ON DELETE SET NULL
);

CREATE INDEX idx_plan_operations_plan_seq ON placement_plan_operations(plan_id, sequence_number);

-- -----------------------------------------------------------------------------
-- 8. Cache et settings
-- -----------------------------------------------------------------------------

CREATE TABLE api_cache (
    provider        TEXT NOT NULL,
    query_hash      TEXT NOT NULL,
    response_json   TEXT NOT NULL,
    cached_at       TEXT NOT NULL DEFAULT (datetime('now')),
    expires_at      TEXT,
    PRIMARY KEY (provider, query_hash)
);

CREATE INDEX idx_api_cache_expires ON api_cache(expires_at);

CREATE TABLE settings (
    key         TEXT PRIMARY KEY,
    value       TEXT NOT NULL,
    updated_at  TEXT NOT NULL DEFAULT (datetime('now'))
);

-- =============================================================================
-- Seed initial
-- =============================================================================

-- Volume "default" pour les Inc 1-8. Une fois Inc 9 livré, les FileInstances
-- seront migrés vers leurs vrais volumes au branchement.
INSERT INTO volumes (id, name, kind, status, created_at)
VALUES (
    '00000000-0000-0000-0000-000000000000',
    'Volume par défaut',
    'default',
    'online',
    datetime('now')
);

-- Settings par défaut (peuvent être surchargés par l'UI)
INSERT INTO settings (key, value) VALUES
    ('fuzzy_threshold',          '0.85'),
    ('confidence_auto_apply',    '0.9'),
    ('online_mode_enabled',      'false'),
    ('hard_delete_allowed',      'false'),
    ('batch_size',               '50'),
    ('dry_run_threshold',        '10'),
    ('long_paths_enabled',       'true'),
    ('nas_timeout_seconds',      '30'),
    ('undo_window_days',         '30');

-- Marqueur de migration
INSERT INTO schema_versions (version, description)
VALUES (1, 'Initial schema');
