-- Accounts, cloud saves, server-owned offline sessions, telemetry.
create table accounts (
  id text primary key,
  token_hash text not null unique,
  created_at timestamptz not null
);

-- The save envelope is kept as text so its checksum still matches byte for byte.
create table cloud_saves (
  account_id text not null references accounts(id),
  slot text not null,
  revision bigint not null,
  body text not null,
  updated_at timestamptz not null,
  primary key (account_id, slot)
);

create table offline_sessions (
  id text primary key,
  account_id text not null references accounts(id),
  slot text not null,
  session jsonb not null,
  started_at timestamptz not null,
  claim_key text,
  claim_response text,
  claimed_at timestamptz
);
-- At most one unclaimed session per save slot, even if application code had a bug.
create unique index offline_sessions_one_open on offline_sessions (account_id, slot) where claimed_at is null;

create table telemetry_events (
  id bigserial primary key,
  account_id text not null,
  name text not null,
  at timestamptz not null,
  properties jsonb not null
);
create index telemetry_events_account_at on telemetry_events (account_id, at);
