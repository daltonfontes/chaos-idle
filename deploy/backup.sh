#!/bin/sh
# Runs in the "backup" service: a compressed pg_dump every day into /backups (./backups on the
# host), keeping the last KEEP_DAYS days. Restore: docs/deploy.md.
#
# One backup right now (e.g. before an update):
#   docker compose exec backup /bin/sh /backup.sh once
set -u

backup_once() {
  file="/backups/chaosidle-$(date -u +%Y%m%d-%H%M%S).sql.gz"
  if pg_dump --no-owner | gzip > "$file.partial"; then
    mv "$file.partial" "$file"
    echo "backup: $file"
  else
    echo "backup failed" >&2
    return 1
  fi
}

if [ "${1:-}" = "once" ]; then
  backup_once
  exit $?
fi

sleep 300 # let the server create or migrate the tables after a (re)start
while true; do
  backup_once || true
  find /backups -name 'chaosidle-*.sql.gz' -mtime "+${KEEP_DAYS:-14}" -delete
  find /backups -name '*.partial' -delete
  sleep 86400
done
