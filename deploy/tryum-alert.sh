#!/bin/bash
# Health-check + cảnh báo Telegram. Chạy cron mỗi 5 phút.
# Chỉ gửi khi ĐỔI trạng thái (tránh spam): ok→down, down→ok.
set -uo pipefail

ENV=/home/ubuntu/tryum/deploy/.env
TOKEN=$(grep '^TELEGRAM_BOT_TOKEN=' "$ENV" | cut -d= -f2- || true)
CHAT=$(grep '^TELEGRAM_CHAT_ID=' "$ENV" | cut -d= -f2- || true)
MT=$(grep '^METRICS_TOKEN=' "$ENV" | cut -d= -f2- || true)
API=https://tryum-api.duckdns.org
STATE=/home/ubuntu/.tryum-alert.state
PREV5XX=/home/ubuntu/.tryum-alert.5xx

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

HEALTH=$(curl -s -m 15 -o /dev/null -w '%{http_code}' "$API/health" 2>/dev/null || echo 000)
[ "$HEALTH" != "200" ] && problems+=("/health = $HEALTH")

MC=$(curl -s -m 10 -H "Authorization: Bearer $MT" "$API/metrics" 2>/dev/null || true)
CUR5XX=$(printf '%s\n' "$MC" | grep '^http_requests_received_total{code="5xx"' | awk '{s+=$2} END {printf "%d", s+0}')
PREV=$(cat "$PREV5XX" 2>/dev/null || echo "$CUR5XX")
DELTA=$(( CUR5XX - PREV ))
echo "$CUR5XX" > "$PREV5XX"
[ "$DELTA" -gt 20 ] && problems+=("5xx mới: $DELTA trong 5 phút")

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
