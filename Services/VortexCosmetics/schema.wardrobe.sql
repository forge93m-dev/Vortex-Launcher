-- Additive tables in ACCOUNT DB ONLY. Legacy cape DB is read-only to this service.
CREATE TABLE IF NOT EXISTS vortex_wardrobe_sessions (
 token_hash TEXT PRIMARY KEY NOT NULL,
 account_id TEXT NOT NULL REFERENCES vortex_accounts(id) ON DELETE CASCADE,
 parent_hash TEXT NOT NULL REFERENCES vortex_sessions(token_hash) ON DELETE CASCADE,
 profile_uuid TEXT NOT NULL,
 expires INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS vortex_wardrobe_sessions_owner ON vortex_wardrobe_sessions(account_id,expires);
CREATE INDEX IF NOT EXISTS vortex_wardrobe_sessions_expiry ON vortex_wardrobe_sessions(expires);
CREATE TABLE IF NOT EXISTS vortex_wardrobe (
 uuid TEXT PRIMARY KEY NOT NULL,
 account_id TEXT NOT NULL REFERENCES vortex_accounts(id) ON DELETE CASCADE,
 cape TEXT NOT NULL DEFAULT 'none', cape_pixels TEXT,
 skin_hash TEXT, skin_pixels TEXT, slim INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS vortex_wardrobe_skin ON vortex_wardrobe(skin_hash);
CREATE INDEX IF NOT EXISTS vortex_wardrobe_cape ON vortex_wardrobe(cape);
CREATE INDEX IF NOT EXISTS vortex_wardrobe_owner ON vortex_wardrobe(account_id);
