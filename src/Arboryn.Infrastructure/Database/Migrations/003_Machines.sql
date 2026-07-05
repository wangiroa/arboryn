-- Arboryn — Migration 003 : Identité machine (partage du catalogue entre PC)
--
-- Un catalogue partagé entre plusieurs PC doit pouvoir nommer le PC propriétaire
-- de chaque volume (deux « C: » distincts par leur VSN mais indiscernables à l'œil).
-- On rattache chaque volume à une machine (hostname capturé à l'enrôlement). Les
-- volumes NAS restent machine-agnostiques (machine_id NULL).

CREATE TABLE machines (
    id            TEXT PRIMARY KEY,
    name          TEXT NOT NULL,             -- libellé éditable (défaut = hostname)
    hostname      TEXT NOT NULL,             -- Environment.MachineName à l'enrôlement
    first_seen_at TEXT NOT NULL DEFAULT (datetime('now')),
    last_seen_at  TEXT
);

CREATE UNIQUE INDEX idx_machines_hostname ON machines(hostname);

-- FK nullable : NAS = NULL (agnostique), volume « default » = NULL à vie, et les
-- lignes préexistantes restent NULL (« machine inconnue ») jusqu'à leur prochain
-- enrôlement sur leur PC propriétaire — pas de backfill hasardeux.
ALTER TABLE volumes ADD COLUMN machine_id TEXT
    REFERENCES machines(id) ON DELETE SET NULL;

CREATE INDEX idx_volumes_machine ON volumes(machine_id);
