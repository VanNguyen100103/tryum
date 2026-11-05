#!/bin/bash
# Health-check + cảnh báo Telegram. Chạy cron mỗi 5 phút.
# Chỉ gửi khi ĐỔI trạng thái (tránh spam): ok→down, down→ok.
set -uo pipefail

ENV=/home/ubuntu/tryum/deploy/.env
TOKEN=$(grep '^TELEGRAM_BOT_TOKEN=' "$ENV" | cut -d= -f2- || true)
CHAT=$(grep '^TELEGRAM_CHAT_ID=' "$ENV" | cut -d= -f2- || true)
API=https://tryum-api.duckdns.org
STATE=/home/ubuntu/.tryum-alert.state

if [ -z "$TOKEN" ] || [ -z "$CHAT" ]; then
  echo "Thiếu TELEGRAM_BOT_TOKEN / TELEGRAM_CHAT_ID trong .env"
  exit 1
fi

send() {
  curl -s -m 10 "https://api.telegram.org/bot$TOKEN/sendMessage" \
    --data-urlencode "chat_id=$CHAT" \
    --data-urlencode "parse_mode=HTML" \
    --data-urlencode "text=$1" >/dev/null 2>&1 || true
}

problems=()

# /health: thử 3 lần trước khi báo (tránh 503 thoáng qua do DB health check chậm).
HEALTH=000
for i in 1 2 3; do
  HEALTH=$(curl -s -m 10 -o /dev/null -w '%{http_code}' "$API/health" 2>/dev/null || echo 000)
  [ "$HEALTH" = "200" ] && break
  sleep 3
done
[ "$HEALTH" != "200" ] && problems+=("/health = $HEALTH (sau 3 lần thử)")

# RAM/disk. (Không check 5xx ở đây: /metrics qua LB trả counter của instance khác nhau
# mỗi lần nên delta không đáng tin — 5xx theo dõi trên Grafana bằng rate().)
RAM=$(free | awk '/Mem:/ {printf "%d", $3/$2*100}')
[ "$RAM" -gt 90 ] && problems+=("RAM ${RAM}%")

DISK=$(df --output=pcent / | tail -1 | tr -dc '0-9')
[ "${DISK:-0}" -gt 90 ] && problems+=("Disk ${DISK}%")

prev_state=$(cat "$STATE" 2>/dev/null || echo ok)

if [ ${#problems[@]} -gt 0 ]; then
  if [ "$prev_state" != "down" ]; then
    msg="🚨 <b>Tryum ALERT</b>"
    for p in "${problems[@]}"; do
      msg="$msg
• $p"
    done
    msg="$msg
$(date '+%F %T')"
    send "$msg"
    echo down > "$STATE"
  fi
else
  if [ "$prev_state" = "down" ]; then
    send "✅ <b>Tryum RECOVERED</b>
$(date '+%F %T')"
    echo ok > "$STATE"
  fi
fi
