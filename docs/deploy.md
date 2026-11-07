# Putting the server online (HTTPS)

`deploy/` runs the game server on any Linux machine with Docker: a small VPS is enough to start.

```
phone ──https──▶ Caddy (:443) ──http, private network──▶ server (:8080) ──▶ PostgreSQL
                                                            backup (daily pg_dump) ──┘
```

- **Caddy** gets and renews the HTTPS certificate on its own (Let's Encrypt), redirects
  `http` to `https`, and adds HSTS.
- **The server** has no public port: only Caddy reaches it. That is why it can trust Caddy's
  `X-Forwarded-For` (`ForwardedHeaders__Enabled=true`). Rate limits then count each player's
  real IP.
- **Rate limits** (HTTP 429 with `Retry-After`):
  - creating guest accounts and signing in: 20 a minute per IP;
  - everything signed in: 120 a minute per device token.

  Change them in `.env`.
- **`/health`** answers 503 when the database doesn't.
- **Backups**:
  - one compressed `pg_dump` a day in `deploy/backups/`, keeping 14 days;
  - the first one 5 minutes after a start;
  - one on demand when you want.
- **The image** `ghcr.io/daltonfontes/chaos-idle-server` is built and tested on every push, and
  published (`latest` and the commit's short sha) on every push to `main`.

## First time

1. **A machine.** Any VPS with a public IPv4, 1 GB RAM or more, and Ubuntu or Debian (for
   example Hetzner, DigitalOcean, Lightsail, Oracle Free Tier). Open ports **80 and 443**.
   Install Docker:

   ```bash
   curl -fsSL https://get.docker.com | sh
   ```

2. **A domain name.** Create a DNS `A` record such as `api.yourgame.com` pointing to the
   machine's IP. Wait until it resolves, because Caddy needs it to get the certificate.
3. **The files.** Copy `deploy/` from this repository to the machine (`git clone`, or `scp` the
   folder).
4. **Settings.** In `deploy/`:

   ```bash
   cp .env.example .env
   ```

   Then edit `.env`:
   - `DOMAIN`;
   - `ACME_EMAIL`;
   - a long random `POSTGRES_PASSWORD` (`openssl rand -base64 32`);
   - `GOOGLE_WEB_CLIENT_ID` (`docs/google-sign-in.md`).
5. **Image access.** GitHub packages start private. Either make the package public (GitHub,
   your profile > Packages > chaos-idle-server > Package settings > Change visibility), or log
   the machine in with a token that has `read:packages`:

   ```bash
   docker login ghcr.io -u daltonfontes
   ```

6. **Start.**

   ```bash
   docker compose up -d
   ```

   ```bash
   curl https://api.yourgame.com/health
   ```

   `{"status":"ok","store":"postgres"}` means it's online. The tables are created at the first
   start.
7. **The game.** Set the repository variable `CHAOSIDLE_SERVER_URL=https://api.yourgame.com`.
   The next APK from CI uses it.

## Day to day

Update to the latest `main`, with a backup first:

```bash
docker compose exec backup /bin/sh /backup.sh once
```

```bash
docker compose pull server && docker compose up -d
```

See the logs:

```bash
docker compose logs -f server caddy
```

Restore a backup. Stop the server first, so nobody writes meanwhile.
This **replaces** the current data:

```bash
docker compose stop server
```

```bash
docker compose exec -T postgres psql -U chaosidle -d postgres -c "drop database chaosidle with (force)" -c "create database chaosidle"
```

```bash
gunzip -c backups/chaosidle-YYYYMMDD-HHMMSS.sql.gz | docker compose exec -T postgres psql -U chaosidle -d chaosidle
```

```bash
docker compose start server
```

**Copy `backups/` off the machine** regularly (for example `rsync` or `rclone` to other storage
on a schedule). A backup on the same disk doesn't survive losing the machine.

## Tested

The **Server deploy** workflow builds the image from `deploy/Dockerfile` and starts this exact
`docker-compose.yml` with `DOMAIN=localhost`, where Caddy uses its own local certificate. Then
`deploy/smoke-test.sh` checks:
- HTTPS, trusting only Caddy's certificate authority;
- HSTS;
- the `http` to `https` redirect;
- `/health` on PostgreSQL;
- a guest account and a signed-in call;
- the rate limit;
- the rows in the database;
- a backup that restores into a fresh database.
