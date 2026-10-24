-- Account-only schema for dedicated staging/public databases. Never apply to the cape database.
CREATE TABLE IF NOT EXISTS vortex_accounts (
  id TEXT PRIMARY KEY NOT NULL,
  login TEXT UNIQUE NOT NULL,
  nickname TEXT NOT NULL,
  nickname_key TEXT UNIQUE NOT NULL,
  password_salt TEXT NOT NULL,
  password_hash TEXT NOT NULL,
  created INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS vortex_sessions (
  token_hash TEXT PRIMARY KEY NOT NULL,
  account_id TEXT NOT NULL REFERENCES vortex_accounts(id) ON DELETE CASCADE,
  created INTEGER NOT NULL,
  expires INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS vortex_sessions_account ON vortex_sessions(account_id,created);
CREATE INDEX IF NOT EXISTS vortex_sessions_expiry ON vortex_sessions(expires);
CREATE TABLE IF NOT EXISTS vortex_auth_limits (
  key TEXT PRIMARY KEY NOT NULL,
  hits INTEGER NOT NULL,
  reset_at INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS vortex_auth_limits_expiry ON vortex_auth_limits(reset_at);
