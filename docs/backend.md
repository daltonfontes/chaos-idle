# Backend (Milestone 5, part 2)

`src/ChaosIdle.Server` is an ASP.NET Core minimal API: a modular monolith on the same domain
and save code as the client. It runs on an in-memory store today. A PostgreSQL store is the
next step; the interface and schema are below.

**Principle:** the client owns nothing that pays out rewards. The server owns:
- time;
- offline seeds;
- offline sessions and claims;
- the cloud copy of the save, which it checks against the game's own tables.

## Run it

```bash
dotnet run --project src/ChaosIdle.Server --urls http://127.0.0.1:5077
```

## API

Every `/v1` route except `POST /v1/accounts` needs `Authorization: Bearer <token>`.

| Method and path | Purpose | Responses |
|---|---|---|
| `GET /health` | liveness | 200 |
| `POST /v1/accounts` | create an account; returns `{accountId, token}` **once** (only a SHA-256 hash is stored) | 201 |
| `GET /v1/saves/{slot}` | download the cloud save; `ETag` is the revision | 200, 404 |
| `PUT /v1/saves/{slot}` | upload a save; header `X-Base-Revision` is the server revision this device last synced (0 for none) | 200 `{revision}`, 400, 409, 413, 422 |
| `POST /v1/saves/{slot}/offline` | `{mapId, modifierIds}`: start an offline session from the **cloud** copy of the character | 201 `{sessionId, startedAtUtc, revision}`, 400, 403, 404, 409 |
| `POST /v1/offline/{sessionId}/claim` | header `Idempotency-Key`: resolve at server time and pay into the cloud save | 200 with the report, 400, 404, 409 |
| `POST /v1/telemetry` | `{events:[{name, at, properties}]}`, at most 100 events | 202, 400 |

Slots match `^[a-z0-9][a-z0-9_-]{0,31}$`. Errors are JSON with `title`, `status` and, where
useful, `details`.

### Saves

- **Optimistic concurrency.** An upload is accepted only if `X-Base-Revision` equals the
  server's revision and the save's own revision is higher. Otherwise the answer is 409 with
  `serverRevision`, and the client must pull, merge or choose, then upload again.
  Re-sending the exact current save is a no-op 200, so retries are safe.
- **Validation** (`Persistence/SaveValidator`). The save must:
  - pass the checksum and be in the current schema version;
  - load **without warnings**, meaning every ID is known;
  - describe things the game can produce:
    - every rolled value inside its tier, and tiers allowed at the item level;
    - affixes allowed on their item class, and rarity and prefix/suffix limits respected;
    - uniques carrying exactly their own modifiers;
    - item level, passive points and bag size within limits;
    - no duplicate item IDs;
    - no more tiers unlocked than bosses defeated.

  This stops impossible items. It can't prove an item was earned.
- **Offline sessions belong to the server.** An upload must carry exactly the open server
  session, or none when none is open. A device that didn't pull can't wipe a running session
  (409), and a client-made session is refused (422).
- **Size limit:** 256 KB.

### Offline sessions

1. **Start** reads the character from the cloud save. It checks the map tier is unlocked,
   takes **server time** and a **random server seed**, and freezes the loadout. It then
   writes the session into the cloud save (revision + 1) so every device sees it. Only one
   open session per slot is allowed.
2. **Claim** resolves at server time with the Milestone 4 rules: 8 h cap, 60% efficiency,
   packs only. It applies the save's loot filter and the storage cap, **puts the items into
   the cloud save** (revision + 1), and stores the response.
   - Same `Idempotency-Key` again: the identical response.
   - Any other key: 409 with the original response.
   - Concurrent claims are serialized per account, so exactly one pays out (tested with 8
     parallel claims).
3. After either call the client must pull: its base revision is now stale.

The device clock and the local save have no effect on offline rewards anymore. The
Milestone 4 local checks remain for playing without a connection.

### Telemetry

- Event names match `^[a-z][a-z0-9_.]{0,63}$`, with at most 20 properties of limited
  length.
- Timestamps in the future are clamped to server time.
- Events are kept in the store. A real sink (a database table or a log pipeline) comes with
  deployment.

## Store and PostgreSQL

`IServerStore` hides storage. Its one non-obvious requirement is that `WithAccountLock`
serializes writes for one account. That is what makes claims pay once and keeps uploads and
session starts consistent. In PostgreSQL, run each call in a transaction that first takes
`SELECT … FROM accounts WHERE id = $1 FOR UPDATE`.

Suggested schema:

```sql
create table accounts (
  id text primary key,
  token_hash text not null unique,
  created_at timestamptz not null
);

create table cloud_saves (
  account_id text not null references accounts(id),
  slot text not null,
  revision bigint not null,
  body jsonb not null,            -- the save envelope as uploaded/written
  updated_at timestamptz not null,
  primary key (account_id, slot)
);

create table offline_sessions (
  id text primary key,
  account_id text not null references accounts(id),
  slot text not null,
  session jsonb not null,          -- OfflineSessionSave: map, modifiers, start, seed, frozen loadout
  started_at timestamptz not null,
  claim_key text,
  claim_response jsonb,
  claimed_at timestamptz
);
create unique index one_open_session on offline_sessions (account_id, slot) where claimed_at is null;

create table telemetry_events (
  id bigserial primary key,
  account_id text not null,
  name text not null,
  at timestamptz not null,
  properties jsonb not null
);
```

Store the save body as **text** if the checksum must be re-verified byte for byte;
`jsonb` normalizes whitespace and key order. The server re-serializes before it ever
rewrites a save, so either works as long as reads go through `SaveSerializer`.

## Not done yet (before an external beta)

- **Real sign-in.** Google Play Games / Apple Game Center, or email, linked to the account.
  Device tokens are a placeholder and are lost with the device.
- **HTTPS, rate limiting** (`AddRateLimiter` per account and per IP), and request logging
  without tokens.
- **The PostgreSQL store** and its migrations, and backups.
- **Crash reporting.** Use a hosted service (Sentry, Firebase Crashlytics) from the Unity
  client; this API doesn't replace it.
- **Active-play authority.** Boss kills and tier unlocks still come from the client's save,
  checked only for consistency. If anything competitive or tradeable appears, active
  encounters must also be simulated or verified on the server: the simulator is
  deterministic, so the server can replay a fight from its seed.
- **Merge on conflict.** The client decides today: keep the cloud copy, or overwrite after
  pulling.
