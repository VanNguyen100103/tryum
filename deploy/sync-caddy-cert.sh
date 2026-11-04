#!/bin/bash
# Re-sync cert TLS từ VM1 → VM2 (chạy weekly). VM1 gia hạn cert ZeroSSL,
# VM2 (backup) cần cert mới để sẵn sàng failover.
set -e
KEY=/home/ubuntu/.ssh/vm2.key
[ -f "$KEY" ] || { echo "thiếu vm2.key"; exit 0; }

docker run --rm -v tryum_caddy_data:/data -v /tmp:/backup alpine tar czf /backup/caddy_data.tgz -C /data .
scp -i "$KEY" -o StrictHostKeyChecking=no /tmp/caddy_data.tgz ubuntu@10.0.0.164:/tmp/caddy_data.tgz
ssh -i "$KEY" -o StrictHostKeyChecking=no ubuntu@10.0.0.164 \
  'sudo -n /usr/bin/docker run --rm -v tryum_caddy_data:/data -v /tmp:/backup alpine tar xzf /backup/caddy_data.tgz -C /data && sudo -n /usr/bin/docker restart tryum-caddy-1 >/dev/null && rm -f /tmp/caddy_data.tgz'
sudo -n rm -f /tmp/caddy_data.tgz 2>/dev/null || true
echo "[$(date '+%F %T')] cert synced VM1 -> VM2"
