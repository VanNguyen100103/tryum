#!/bin/bash
# Backup Supabase (Postgres) → gzip → R2 (bucket tryum/backups/)
# Cài trên VM1: /usr/local/bin/tryum-backup.sh + cron 03:00 hằng ngày.
# Yêu cầu: rclone đã cấu hình remote "r2" (provider Cloudflare).
set -euo pipefail

ENV=/home/ubuntu/tryum/deploy/.env
CONN=$(grep '^DB_CONNECTION_STRING=' "$ENV" | sed 's/^DB_CONNECTION_STRING=//')
DB_HOST=$(printf '%s' "$CONN" | grep -o 'Host=[^;]*' | cut -d= -f2)
DB_PORT=$(printf '%s' "$CONN" | grep -o 'Port=[^;]*' | cut -d= -f2)
DB_NAME=$(printf '%s' "$CONN" | grep -o 'Database=[^;]*' | cut -d= -f2)
DB_USER=$(printf '%s' "$CONN" | grep -o 'Username=[^;]*' | cut -d= -f2)
DB_PASS=$(printf '%s' "$CONN" | grep -o 'Password=[^;]*' | cut -d= -f2)

DATE=$(date +%F)
DIR=/home/ubuntu/backups
mkdir -p "$DIR"
FILE="$DIR/tryum-$DATE.sql.gz"

docker run --rm -e PGPASSWORD="$DB_PASS" postgres:17-alpine \
  pg_dump -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" \
  --no-owner --no-acl | gzip > "$FILE"

rclone copy "$FILE" r2:tryum/backups/ --s3-no-check-bucket

find "$DIR" -name 'tryum-*.sql.gz' -mtime +7 -delete
rclone delete --min-age 30d r2:tryum/backups/ 2>/dev/null || true

echo "[$(date '+%F %T')] backup ok: $FILE ($(du -h "$FILE" | cut -f1))"
