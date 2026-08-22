-- Banco de contas do Nyxar Concord (Cloudflare D1)
-- Aplique com:
--   wrangler d1 execute nyxar-accounts --remote --file=schema.sql

CREATE TABLE IF NOT EXISTS accounts (
  email        TEXT PRIMARY KEY,      -- e-mail (identificador principal)
  username     TEXT UNIQUE,           -- nome de usuário
  handle       TEXT,                  -- @handle curto e legível
  display_name TEXT,                  -- nome de exibição
  pass_hash    TEXT,                  -- hash PBKDF2 da senha
  pass_salt    TEXT,                  -- salt do hash
  verified     INTEGER DEFAULT 0,     -- 1 = e-mail confirmado
  created_at   INTEGER                -- epoch (segundos)
);

CREATE TABLE IF NOT EXISTS codes (
  email      TEXT,                    -- e-mail do dono do código
  kind       TEXT,                    -- 'verify' (cadastro) ou 'reset' (senha)
  code       TEXT,                    -- código de 6 dígitos
  expires_at INTEGER,                 -- epoch de expiração
  tries      INTEGER DEFAULT 0,       -- tentativas erradas
  PRIMARY KEY (email, kind)
);

CREATE INDEX IF NOT EXISTS idx_accounts_username ON accounts(username);
CREATE INDEX IF NOT EXISTS idx_accounts_handle   ON accounts(handle);
