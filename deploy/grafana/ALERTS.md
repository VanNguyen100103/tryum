# L6 — Alert rules cho Tryum (Grafana Cloud)

Tài liệu này hướng dẫn tạo **6 alert rule** trên Grafana Cloud để phát hiện sự cố runtime
sớm hơn cron `tryum-alert.sh` (cron chỉ chạy trên VM1 và chỉ check health/RAM/disk).

Dashboard đi kèm: `deploy/grafana/tryum-api-dashboard.json` (import bằng UI).

---

## 1. Tạo contact point Telegram (làm 1 lần)

Grafana → **Alerting → Contact points → Add contact point**

| Trường | Giá trị |
|---|---|
| Name | `tryum-telegram` |
| Integration | **Telegram** |
| Bot Token | token bot `@tryum_alerts_2026_bot` (xem `deploy/.env` dòng `TELEGRAM_BOT_TOKEN`) |
| Chat ID | `7429814751` (xem `TELEGRAM_CHAT_ID`) |
| Disable notifications | bỏ trống |

→ **Test** để chắc bot gửi được.

---

## 2. Sáu alert rule

Grafana → **Alerting → Alert rules → New alert rule** → chọn datasource
`grafanacloud-greenswordfish3260-prom`, dán PromQL, đặt `for` + severity như bảng.

| # | Tên | PromQL | For | Severity | Ý nghĩa |
|---|---|---|---|---|---|
| 1 | `TryumApiDown` | `up == 0` | 5m | critical | 1 instance API chết quá 5 phút |
| 2 | `TryumInstanceMissing` | `count(up) < 3` | 5m | warning | Mất 1 trong 3 instance (VM2 hoặc api2) |
| 3 | `TryumHighErrorRate` | `100 * sum(rate(http_requests_received_total{code=~"5.."}[5m])) / clamp_min(sum(rate(http_requests_received_total[5m])), 0.0001) > 5` | 10m | critical | >5% request trả 5xx |
| 4 | `TryumHighLatencyP95` | `histogram_quantile(0.95, sum(rate(http_request_duration_seconds_bucket[5m])) by (le)) > 2` | 10m | warning | p95 > 2 giây (DB pool cạn / cold connection) |
| 5 | `TryumMemoryHigh` | `sum(process_working_set_bytes) > 1.2e9` | 15m | warning | Tổng RAM 3 instance > 1.2 GB |
| 6 | `TryumNoTraffic` | `sum(rate(http_requests_received_total[10m])) < 0.01` | 15m | warning | Không còn traffic (DNS/Caddy/CF hỏng dù `/health` vẫn 200) |

**Notification:** gán contact point `tryum-telegram` cho cả 6 rule.

---

## 3. Lưu ý chống báo giả

- **Rule 1 (`up == 0`)**: mỗi lần deploy `docker compose up --force-recreate`, container restart
  ~30–60s nên `up` về 0. Đặt `for: 5m` để bỏ qua dao động này (deploy bình thường không kéo dài 5 phút).
- **Rule 3 (error rate)**: dùng `clamp_min(..., 0.0001)` để tránh chia 0 khi chưa có traffic.
- **Rule 6 (no traffic)**: đặt `for: 15m` vì ban đêm quán đóng cửa, traffic có thể về 0 thật.
  Nếu thấy báo nhiều → tăng `for` hoặc bỏ rule này.

---

## 4. (Tuỳ chọn) Apply bằng API thay vì bấm UI

Nếu sau này bạn tạo **Grafana service account token** (quyền `alert.rules:write`), có thể tạo
rule bằng `curl`:

```bash
GRAFANA_URL=https://greenswordfish3260.grafana.net
GRAFANA_TOKEN=<service-account-token>

curl -s -X POST "$GRAFANA_URL/api/v1/provisioning/alert-rules" \
  -H "Authorization: Bearer $GRAFANA_TOKEN" \
  -H "Content-Type: application/json" \
  -H "X-Disable-Provenance: true" \
  -d '{
    "title": "TryumApiDown",
    "ruleGroup": "tryum",
    "folderUID": "tryum-alerts",
    "condition": "A",
    "data": [{
      "refId": "A",
      "relativeTimeRange": { "from": 300, "to": 0 },
      "datasourceUid": "<uid-cua-prometheus>",
      "model": { "expr": "up == 0", "instant": true, "refId": "A" }
    }],
    "for": "5m",
    "labels": { "severity": "critical" },
    "noDataState": "NoData",
    "execErrState": "Error"
  }'
```

Cần tạo folder `tryum-alerts` trước và lấy `datasourceUid` của Prometheus
(Grafana → Connections → Data sources → copy UID).

---

## 5. Đối chiếu với cron hiện có

| Kiểm tra | `tryum-alert.sh` (VM1) | Grafana alert (cloud) |
|---|---|---|
| VM1 chết hoàn toàn | ❌ không báo được | ✅ `up == 0` |
| API 5xx tăng | ❌ không check | ✅ rule 3 |
| Latency tăng | ❌ | ✅ rule 4 |
| RAM/disk VM | ✅ | ❌ (chưa có node_exporter) |
| DB chết | ⚠️ gián tiếp qua /health | ⚠️ gián tiếp qua error rate |

Hai lớp bổ sung nhau: cron bắt sự cố **trên máy**, Grafana bắt sự cố **từ ngoài**.
