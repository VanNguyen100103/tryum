#!/bin/bash
# Failover DNS: chạy trên VM2 (backup). Nếu VM1 (entrypoint chính) chết → trỏ DuckDNS sang VM2.
# Cron mỗi phút. Chỉ đổi DNS khi ĐỔI trạng thái (tránh spam API DuckDNS).
set -uo pipefail

DOMAIN=tryum-api
TOKEN="${DUCKDNS_TOKEN:-}"          # đặt trong deploy/.env: DUCKDNS_TOKEN=...
VM1_IP=129.225.202.82
VM2_IP=141.147.145.6
ENV=/home/ubuntu/tryum/deploy/.env
STATE=/home/ubuntu/.failover.state
TG_TOKEN=$(grep '^TELEGRAM_BOT_TOKEN=' "$ENV" | cut -d= -f2- || true)
TG_CHAT=$(grep '^TELEGRAM_CHAT_ID=' "$ENV" | cut -d= -f2- || true)

if [ -z "$TOKEN" ]; then
  echo "Thiếu DUCKDNS_TOKEN trong .env"
  exit 1
fi

notify() {
  [ -z "$TG_TOKEN" ] && return 0
  curl -s -m 10 "https://api.telegram.org/bot$TG_TOKEN/sendMessage" \
    --data-urlencode "chat_id=$TG_CHAT" --data-urlencode "text=$1" >/dev/null 2>&1 || true
}

# Kiểm tra VM1 Caddy qua private IP (3 lần, cần cả 3 fail mới tính là down).
fails=0
for i in 1 2 3; do
  CODE=$(curl -sk -m 8 -o /dev/null -w '%{http_code}' -H "Host: tryum-api.duckdns.org" https://10.0.0.232/health || echo 000)
  [ "$CODE" = "200" ] && break
  fails=$((fails + 1))
  sleep 5
done

current=$(cat "$STATE" 2>/dev/null || echo vm1)

if [ "$fails" -lt 3 ]; then
  # VM1 khoẻ → đảm bảo DNS đang trỏ VM1
  if [ "$current" != "vm1" ]; then
    curl -s -m 15 "https://www.duckdns.org/update?domains=$DOMAIN&token=$TOKEN&ip=$VM1_IP" >/dev/null
    echo vm1 > "$STATE"
    notify "✅ <b>Failover RECOVERED</b> — DNS đã trỏ lại VM1 ($VM1_IP)"
  fi
else
  # VM1 chết → failover sang VM2
  if [ "$current" != "vm2" ]; then
    curl -s -m 15 "https://www.duckdns.org/update?domains=$DOMAIN&token=$TOKEN&ip=$VM2_IP" >/dev/null
    echo vm2 > "$STATE"
    notify "🚨 <b>FAILOVER</b> — VM1 down, DNS đã trỏ sang VM2 ($VM2_IP)"
  fi
fi
