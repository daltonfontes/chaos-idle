#!/usr/bin/env bash
# Checks a running stack from deploy/docker-compose.yml started with DOMAIN=localhost and
# RATE_LIMIT_ANONYMOUS_PER_MINUTE=5 (CI does this). Run from deploy/.
set -euo pipefail

base=https://localhost
ca=$(mktemp)

echo "== waiting for HTTPS"
for i in $(seq 1 90); do
  # Caddy's local CA (used for DOMAIN=localhost): trusting only it proves the TLS chain is real.
  if docker compose exec -T caddy cat /data/caddy/pki/authorities/local/root.crt > "$ca" 2>/dev/null \
     && curl -sf --cacert "$ca" "$base/health" > /dev/null; then break; fi
  sleep 2
done

echo "== health over HTTPS, on PostgreSQL"
health=$(curl -sf --cacert "$ca" -D headers.txt "$base/health")
echo "$health"
grep -q '"status":"ok"' <<< "$health"
grep -q '"store":"postgres"' <<< "$health"
grep -qi '^strict-transport-security: max-age=31536000' headers.txt

echo "== plain http redirects to https"
code=$(curl -s -o /dev/null -w '%{http_code} %{redirect_url}' http://localhost/health)
echo "$code"
[[ "$code" == 30[178]" https://localhost/health" ]]

echo "== a guest account, then a signed-in call"
account=$(curl -sf --cacert "$ca" -X POST "$base/v1/accounts")
token=$(sed -E 's/.*"token":"([^"]+)".*/\1/' <<< "$account")
curl -sf --cacert "$ca" -H "Authorization: Bearer $token" "$base/v1/account" | grep -q '"isGuest":true'

echo "== account creation is rate limited per client (limit 5/min, one used above)"
for i in 1 2 3 4; do
  [[ $(curl -s --cacert "$ca" -o /dev/null -w '%{http_code}' -X POST "$base/v1/accounts") == 201 ]]
done
[[ $(curl -s --cacert "$ca" -o /dev/null -w '%{http_code}' -X POST "$base/v1/accounts") == 429 ]]

echo "== the database has the accounts and the migrations"
docker compose exec -T postgres psql -U chaosidle -d chaosidle -tAc "select count(*) from accounts" | grep -qx 5
docker compose exec -T postgres psql -U chaosidle -d chaosidle -tAc "select max(version) from schema_migrations"

echo "== an on-demand backup is written and restores"
docker compose exec -T backup /bin/sh /backup.sh once
dump=$(ls backups/chaosidle-*.sql.gz | sort | tail -1)
echo "$dump"
gunzip -c "$dump" | grep -q "CREATE TABLE public.accounts"
docker compose exec -T postgres psql -U chaosidle -d postgres -qc "create database restore_check"
gunzip -c "$dump" | docker compose exec -T postgres psql -U chaosidle -d restore_check -q > /dev/null
docker compose exec -T postgres psql -U chaosidle -d restore_check -tAc "select count(*) from device_tokens" | grep -qx 5

echo "SMOKE OK"
