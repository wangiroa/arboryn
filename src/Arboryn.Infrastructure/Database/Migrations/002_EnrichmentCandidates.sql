-- =============================================================================
-- Arboryn — Migration 002 : Candidats d'enrichissement (suivi Inc 8)
-- =============================================================================
-- Les providers en ligne proposent des champs dont la confiance est sous le
-- seuil d'auto-application (cf. confidence_auto_apply). Jusqu'ici ces candidats
-- n'étaient que comptés puis perdus. Cette table les persiste afin de les
-- présenter à l'utilisateur dans une UI de révision (accepter / rejeter).
--
-- Unicité (file_instance_id, provider, key) : un re-enrichissement met à jour le
-- candidat existant plutôt que d'en créer un doublon ; un candidat déjà décidé
-- (accepted/rejected) n'est pas ressuscité tant que la valeur proposée ne change
-- pas (logique applicative dans l'UPSERT du dépôt).
-- =============================================================================

CREATE TABLE enrichment_candidates (
    id                  TEXT PRIMARY KEY,
    file_instance_id    TEXT NOT NULL,
    provider            TEXT NOT NULL,
    key                 TEXT NOT NULL,
    value               TEXT NOT NULL,
    confidence          REAL NOT NULL DEFAULT 0.0 CHECK (confidence >= 0.0 AND confidence <= 1.0),
    status              TEXT NOT NULL CHECK (status IN ('pending', 'accepted', 'rejected')) DEFAULT 'pending',
    created_at          TEXT NOT NULL DEFAULT (datetime('now')),
    decided_at          TEXT,
    UNIQUE (file_instance_id, provider, key),
    FOREIGN KEY (file_instance_id) REFERENCES file_instances(id) ON DELETE CASCADE
);

CREATE INDEX idx_enrichment_candidates_status   ON enrichment_candidates(status);
CREATE INDEX idx_enrichment_candidates_instance ON enrichment_candidates(file_instance_id);
