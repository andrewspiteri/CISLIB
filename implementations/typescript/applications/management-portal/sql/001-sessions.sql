CREATE TABLE portal_schema_version (version integer PRIMARY KEY CHECK (version = 1));
INSERT INTO portal_schema_version VALUES (1);

CREATE TABLE users (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  name varchar(256),
  email varchar(320) UNIQUE,
  "emailVerified" timestamptz,
  image text
);
CREATE TABLE accounts (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "userId" uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  provider varchar(64) NOT NULL CHECK (provider = 'cis-management'),
  type varchar(16) NOT NULL CHECK (type = 'oauth'),
  "providerAccountId" varchar(80) NOT NULL,
  access_token text CHECK (octet_length(access_token) <= 16384),
  expires_at bigint,
  refresh_token text CHECK (refresh_token IS NULL),
  id_token text CHECK (id_token IS NULL),
  scope varchar(2048),
  session_state varchar(512),
  token_type varchar(32),
  UNIQUE(provider, "providerAccountId")
);
CREATE TABLE sessions (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "userId" uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  expires timestamptz NOT NULL,
  "sessionToken" varchar(255) UNIQUE NOT NULL
);
CREATE INDEX sessions_expiry ON sessions(expires);
CREATE TABLE verification_token (
  identifier text NOT NULL,
  token text NOT NULL,
  expires timestamptz NOT NULL,
  PRIMARY KEY(identifier, token)
);
