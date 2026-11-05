-- Many devices per account, and platform sign-in (Google, Apple) linked to accounts.
create table device_tokens (
  token_hash text primary key,
  account_id text not null references accounts(id),
  created_at timestamptz not null,
  revoked_at timestamptz
);
create index device_tokens_account on device_tokens (account_id);

-- Existing device tokens keep working.
insert into device_tokens (token_hash, account_id, created_at)
select token_hash, id, created_at from accounts;
alter table accounts drop column token_hash;

create table linked_identities (
  provider text not null,
  subject text not null,          -- the provider's stable user id ("sub")
  account_id text not null references accounts(id),
  email text,
  linked_at timestamptz not null,
  primary key (provider, subject),
  unique (account_id, provider)   -- one Google and one Apple identity per account at most
);
