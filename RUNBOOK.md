# Tryum — Runbook Vận Hành (Maintenance)

Tài liệu này ghi lại các thao tác bảo trì, deploy, backup và xử lý sự cố cho hệ thống Tryum.

## 1. Kiến trúc hiện tại

```text
Khách → Cloudflare (web SPA, R2 ảnh)
      → tryum-api.duckdns.org → Caddy (VM1, LB round-robin + health check)
          ├─ api  (VM1 container)
          ├─ api2 (VM1 container)
          └─ VM2  (10.0.0.164, container API)
      → Supabase (Postgres, Mumbai) · Upstash (Redis) · CloudAMQP (RabbitMQ/LavinMQ)
      → Grafana Cloud (metrics)
```

- **VM1**: `tryum` — Caddy, api, api2, otel-collector, web (SPA dry-run).
- **VM2**: `tryum-api2` (141.147.145.6) — chỉ chạy API.
- **Image**: `ghcr.io/vannguyen100103/tryum-api:otel` (build bởi GitHub Actions).

## 2. Deploy bản mới

```bash
# (1) Push code → CI tự build image mới (workflow build-api.yml)
git push origin main

# (2) VM1
docker pull ghcr.io/vannguyen100103/tryum-api:otel
docker tag ghcr.io/vannguyen100103/tryum-api:otel tryum-api:latest
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env \
  up -d --force-recreate api api2
# BẮT BUỘC: recreate Caddy để nạp lại IP mới của container
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env \
  up -d --no-deps --force-recreate caddy

# (3) VM2 (SSH từ máy mình)
# copy docker auth: scp ~/.docker/config.json VM2
docker pull ghcr.io/vannguyen100103/tryum-api:otel
docker tag ghcr.io/vannguyen100103/tryum-api:otel tryum-api:latest
docker compose -f deploy/docker-compose.prod.yml -f deploy/docker-compose.node.yml \
  --env-file deploy/.env up -d --force-recreate api

# (4) Verify
curl -s https://tryum-api.duckdns.org/health   # 200
```

## 3. Rollback

```bash
# Trước khi deploy, giữ bản cũ:
docker tag tryum-api:latest tryum-api:backup-$(date +%F)

# Khi cần quay lại:
docker tag tryum-api:backup-YYYY-MM-DD tryum-api:latest
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env \
  up -d --force-recreate api api2
docker compose ... up -d --no-deps --force-recreate caddy
# lặp lại trên VM2
```

## 4. Backup database (Supabase)

Backup tự động bằng cron trên VM1 (`/usr/local/bin/tryum-backup.sh`):

```text
Hằng ngày 03:00 → pg_dump Supabase → gzip → R2 (tryum/backups/)
Giữ: 7 ngày local + 30 ngày trên R2
```

Chạy thủ công:

```bash
/usr/local/bin/tryum-backup.sh
```

Restore:

```bash
gunzip -c /home/ubuntu/backups/tryum-YYYY-MM-DD.sql.gz | \
  docker run --rm -i -e PGPASSWORD="$PGPASS" postgres:17-alpine \
  psql -h aws-0-ap-south-1.pooler.supabase.com -p 5432 \
  -U postgres.hbxjhmhkbpcgxgyeezqu -d postgres
```

## 5. Giám sát hằng ngày

```bash
# Health
curl -s https://tryum-api.duckdns.org/health

# Log lỗi
docker logs --tail 100 tryum-api-1 2>&1 | grep -iE 'ERR|Exception' | tail -20

# Grafana Cloud → Explore:
#   up == 1  (cả 3 instance)
#   sum(rate(http_requests_received_total{code=~"5xx"}[5m])) ~ 0
#   process_working_set_bytes < 800MB
```

## 5b. Cảnh báo tự động (Telegram)

Bot: `@tryum_alerts_2026_bot` — chat_id `7429814751`.

```text
Cron VM1: */5 * * * * /usr/local/bin/tryum-alert.sh
Kiểm tra: /health != 200 · 5xx mới > 20/5phút · RAM > 90% · Disk > 90%
Chống spam: chỉ gửi khi ĐỔI trạng thái (ok→down, down→ok)
Creds: TELEGRAM_BOT_TOKEN / TELEGRAM_CHAT_ID trong deploy/.env
```

Chạy thủ công:

```bash
/usr/local/bin/tryum-alert.sh
```

## 6. Xử lý sự cố

| Sự cố | Biểu hiện | Hệ thống | Hành động |
|---|---|---|---|
| Supabase sập | API 500 khi đọc/ghi DB | chết (DB cốt lõi) | restore backup / quay Postgres local (`docker compose --profile local-db up -d postgres`) |
| Redis sập | OTP/captcha lỗi, rate-limit tắt | API vẫn bán | check Upstash, nâng plan |
| RabbitMQ sập | event chậm/trễ | không mất (outbox retry) | check CloudAMQP |
| VM1 chết | mất Caddy + 2 API | VM2 vẫn gánh | cần DNS failover (chưa có) |
| VM2 chết | mất 1/3 API | Caddy tự loại, VM1 gánh | khởi động lại VM2 |

## 7. Rotate secret (định kỳ hoặc khi lộ)

```text
CloudAMQP:  tryum-prod → Rotate password → cập nhật RABBITMQ_PASSWORD
Supabase:   Settings → Database → Reset password → cập nhật DB_CONNECTION_STRING
Grafana:    Access Policies → token mới → cập nhật GRAFANA_AUTH
GitHub:     Settings → Developer settings → regenerate token
SePay/Goship/AI: nếu từng lộ trong chat
```

## 8. Bảo trì OS/VM

```bash
sudo apt update && sudo apt upgrade -y   # hằng tháng
docker image prune -a --filter "until=168h"   # dọn image > 7 ngày
# Giới hạn log Docker (tránh đầy disk):
# /etc/docker/daemon.json:
#   {"log-driver":"json-file","log-opts":{"max-size":"10m","max-file":"3"}}
```

## 9. Kiểm tra quota free tier

```text
Supabase:   storage < 500MB, không pause 7 ngày (đã có cron keepalive)
Upstash:    lệnh/ngày < ~10k (đã giảm tần suất R2 leader + outbox relay)
CloudAMQP:  queue không backlog
Oracle VM:  RAM/CPU/disk còn trống
```

## 10. Địa chỉ / thông tin quan trọng

```text
API prod:       https://tryum-api.duckdns.org
Web prod:       https://tryum-web.nganhvan1609.workers.dev
VM1:            129.225.202.82 (private 10.0.0.232)
VM2:            141.147.145.6  (private 10.0.0.164)
GHCR image:     ghcr.io/vannguyen100103/tryum-api:otel
GitHub repo:    github.com/VanNguyen100103/tryum
Grafana Cloud:  stack greenswordfish3260
```
