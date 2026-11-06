# Backend (Milestone 5, part 2)

`src/ChaosIdle.Server` is an ASP.NET Core minimal API: a modular monolith on the same domain
and save code as the client. It stores data in PostgreSQL when a connection string is set,
and in memory otherwise (tests and quick local runs).

**Principle:** the client owns nothing that pays out rewards. The server owns:
- time;
- offline seeds;
- offline sessions and claims;
- the cloud copy of the save, which it checks against the game's own tables.

## Run it

In memory (data is lost when it stops):

```bash
dotnet run --project src/ChaosIdle.Server --urls http://127.0.0.1:5077
```

On PostgreSQL: start the database from `docker-compose.yml`, then point the server at it.
The server applies pending migrations at startup. `GET /health` reports `"store": "postgres"`.

```bash
docker compose up -d
```

```bash
ConnectionStrings__Postgres="Host=localhost;Username=chaosidle;Password=chaosidle;Database=chaosidle" dotnet run --project src/ChaosIdle.Server --urls http://127.0.0.1:5077
```

The PostgreSQL tests run when `CHAOSIDLE_TEST_POSTGRES` holds a connection string for a role
that may create databases. Each test gets a fresh database and drops it afterwards. Without the
variable they are skipped; CI sets `CHAOSIDLE_REQUIRE_POSTGRES=1`, so there they always run.

```bash
CHAOSIDLE_TEST_POSTGRES="Host=localhost;Username=chaosidle;Password=chaosidle;Database=chaosidle" dotnet test
```

## API

Every `/v1` route except `POST /v1/accounts` and `POST /v1/auth/{provider}` needs
`Authorization: Bearer <token>`.

| Method and path | Purpose | Responses |
|---|---|---|
| `GET /health` | liveness | 200 |
| `POST /v1/accounts` | create a **guest** account; returns `{accountId, token}` **once** (only a SHA-256 hash is stored) | 201 |
| `POST /v1/auth/{provider}` | `{idToken}` from Google or Apple: sign in to the linked account, or create one; returns `{accountId, token, created, signIns}` with a **new device token** | 200, 201 (new account), 401, 404 (provider off), 503 |
| `GET /v1/account` | `{accountId, createdAt, isGuest, signIns:[{provider, email, linkedAt}]}` | 200 |
| `POST /v1/account/link/{provider}` | `{idToken}`: link Google or Apple to this account (usually a guest), keeping its progress | 200, 401, 404, 409, 503 |
| `POST /v1/account/sign-out` | revoke this device's token | 200, 409 (guest) |
| `POST /v1/account/sign-out-everywhere` | revoke every token of the account | 200 `{tokensRevoked}`, 409 (guest) |
| `GET /v1/saves/{slot}` | download the cloud save; `ETag` is the revision | 200, 404 |
| `PUT /v1/saves/{slot}` | upload a save; header `X-Base-Revision` is the server revision this device last synced (0 for none) | 200 `{revision}`, 400, 409, 413, 422 |
| `POST /v1/saves/{slot}/offline` | `{mapId, modifierIds}`: start an offline session from the **cloud** copy of the character | 201 `{sessionId, startedAtUtc, revision}`, 400, 403, 404, 409 |
| `POST /v1/offline/{sessionId}/claim` | header `Idempotency-Key`: resolve at server time and pay into the cloud save | 200 with the report, 400, 404, 409 |
| `POST /v1/telemetry` | `{events:[{name, at, properties}]}`, at most 100 events | 202, 400 |

Slots match `^[a-z0-9][a-z0-9_-]{0,31}$`. Errors are JSON with `title`, `status` and, where
useful, `details`.

### Sign-in

Players start as **guests**: `POST /v1/accounts` gives a device token, and nothing else
identifies them. Losing the device loses the account. Linking a platform sign-in fixes that.

- **How.** The app signs in with the platform's own SDK and gets an OpenID Connect **ID
  token**: Google through Credential Manager on Android, Apple through Sign in with Apple on
  iOS. It sends that token to the server, which checks it in `OidcIdentityVerifier`:
  - the signature, against the provider's public keys (fetched from its discovery document and
    cached);
  - the issuer;
  - the audience (our client ids);
  - expiry and not-before, on server time.

  The account is keyed by the provider's stable `sub`. The email is kept only when the
  provider says it is verified.
- **Flows.**
  - A guest links Google (`/v1/account/link/google`) and keeps the same account and saves.
  - On a new phone, `/v1/auth/google` returns the same account and a **new token for that
    device**. Each device has its own token (`device_tokens`), so one can be signed out
    without the others.
  - If the Google identity already belongs to another game account, linking answers 409
    `identity_in_use`. The app can then offer "switch to that account", which signs in with
    `/v1/auth/google`; this device's guest progress stays behind.
  - One identity per provider per account (409 `provider_already_linked`).
- **Sign-out** is refused for guests (409 `guest_account`): with no sign-in linked, it would
  lose the account for good.
- **Configuration.** A provider is off until a client id is set. Use the **Web** client id
  from Google Cloud: the Android app requests ID tokens for it. For Apple, use the app's
  bundle id (or Services ID). Several ids can be listed.

  ```bash
  Auth__Google__ClientIds__0=1234-abc.apps.googleusercontent.com
  ```

  ```bash
  Auth__Apple__ClientIds__0=com.example.chaosidle
  ```

  If the provider's keys can't be fetched, sign-in answers 503 rather than rejecting the
  player.
- **Tests** sign real RS256 tokens with a key made in the test (`TestIdentityProvider`) and run
  them through the real verifier. They cover:
  - wrong audience, wrong issuer, expired, not yet valid, and forged signature;
  - an unverified email;
  - linking conflicts;
  - signing out one device or every device;
  - eight simultaneous first sign-ins ending on one account.

  All of these also run on PostgreSQL.

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

## Running online

`docs/deploy.md` covers it:
- Caddy for HTTPS (automatic certificates) in front of the server;
- PostgreSQL;
- daily backups;
- the Docker image published by CI.

The server itself (`Hosting.cs`):
- reads the client IP from Caddy's forwarded headers, when `ForwardedHeaders__Enabled=true`
  and the server is reachable only through the proxy;
- applies rate limits:
  - per IP for creating accounts and signing in (`RateLimits__AnonymousPerMinute`, default 20);
  - per device token for everything else (`RateLimits__AccountPerMinute`, default 120);
  - answering 429 with `Retry-After`;
- answers 503 from `/health` when the database doesn't respond.

## Store and PostgreSQL

`IServerStore` hides storage. It has two implementations: `InMemoryServerStore` and
`PostgresServerStore` (`src/ChaosIdle.Server/PostgresStore.cs`, on Npgsql). Both pass the same
tests. `StoreContractTests` covers the store itself, and every HTTP test in `ServerTests` runs
again on PostgreSQL as `PostgresServerTests`.

**Locking.** The one non-obvious requirement is that `WithAccountLock` serializes writes for one
account. That is what makes claims pay once and keeps uploads and session starts consistent.
The PostgreSQL store runs the whole action in one transaction:
1. It first runs `select 1 from accounts where id = $1 for update`.
2. Every store call inside the action reuses that transaction (carried in an `AsyncLocal`).

So the lock holds across server instances, and a failure rolls back every write the action made.
Locking the same account again inside the action is allowed; locking a second account
inside it is refused.

**Migrations.** SQL files in `src/ChaosIdle.Server/Migrations/` (named `NNNN_name.sql`) are
embedded in the server. At startup they are applied in order, each in its own transaction, and
recorded in `schema_migrations`. A PostgreSQL advisory lock stops two instances starting at
once from applying a migration twice. To change the schema, add a new file; never edit one
that has shipped.

**Schema** (`0001_initial.sql`, `0002_sign_in.sql`):
- `accounts`: `id`, `created_at`.
- `device_tokens`: `token_hash` (primary key), `account_id`, `created_at`, `revoked_at`.
  Migration 0002 moved the old per-account token here, so existing devices stay signed in.
- `linked_identities`: primary key `(provider, subject)`, plus `account_id`, `email` and
  `linked_at`. A unique `(account_id, provider)` allows one Google and one Apple identity per
  account.
- `cloud_saves`: primary key `(account_id, slot)`, `revision`, `body`, `updated_at`. `body` is
  **text**, not `jsonb`, so the save envelope keeps its exact bytes and its checksum still
  matches.
- `offline_sessions`:
  - `id`, `account_id`, `slot`;
  - `session` (`jsonb`), `started_at`;
  - `claim_key`, `claim_response`, `claimed_at`.

  A partial unique index allows **at most one unclaimed session per slot**, a database-level
  backstop to the service's own check.
- `telemetry_events`: `id` (bigserial), `account_id`, `name`, `at`, `properties` (`jsonb`), and
  an index on `(account_id, at)`.

## Not done yet (before an external beta)

- **Google Cloud credentials** for sign-in (`docs/google-sign-in.md`), and Sign in with Apple for
  an iOS build.
- **Off-machine copies of the backups** (`docs/deploy.md`), and a retention policy for telemetry.
- **Monitoring**: uptime checks on `/health`, and alerts on errors.
- **Crash reporting.** Use a hosted service (Sentry, Firebase Crashlytics) from the Godot
  client; this API doesn't replace it.
- **Active-play authority.** Boss kills and tier unlocks still come from the client's save,
  checked only for consistency. If anything competitive or tradeable appears, active
  encounters must also be simulated or verified on the server: the simulator is
  deterministic, so the server can replay a fight from its seed.
- **Merge on conflict.** The client decides today: keep the cloud copy, or overwrite after
  pulling.
