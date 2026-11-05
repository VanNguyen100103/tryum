# Tryum — Troubleshooting (Bảng tra cứu sự cố)

Tổng hợp mọi lỗi đã gặp và cách sửa. Cột: **Triệu chứng → Nguyên nhân → Cách sửa**.

## 1. Database (Supabase / Postgres)

| Triệu chứng | Nguyên nhân | Cách sửa |
|---|---|---|
| Không kết nối được `db.*.supabase.co` | Host direct chỉ có IPv6, VM Oracle không có IPv6 | Dùng session pooler: `aws-0-ap-south-1.pooler.supabase.com:5432` |
| `Timeout during reading attempt` / `DbCommand (32,977ms)` | Dùng transaction pooler cổng **6543** | Chuyển sang **session pooler 5432**, `Maximum Pool Size=4` |
| Khởi động lỗi `The operation has timed out` khi CREATE TABLE | Supabase cold start chậm, `Timeout=15` quá thấp | Tăng `Timeout=60` trong `DB_CONNECTION_STRING` |
| API đọc/ghi chậm 150–500ms/query | Supabase ở Mumbai, VM ở Osaka (xa) | Chuyển Supabase sang **Singapore** |
| `relation "outbox_messages" does not exist` | Bảng outbox chưa tạo (DDL startup timeout) | Chạy `OutboxSchema.EnsureCreatedAsync` / tạo tay bằng psql |

## 2. Redis (Upstash)

| Triệu chứng | Nguyên nhân | Cách sửa |
|---|---|---|
| `RedisTimeoutException ... SET tryum:r2sync:leader` (19s) | Vòng leader-election R2 chạy 2–5s × 3 instance = ~100k+ lệnh/ngày | Tăng delay rảnh lên 60–90s, TTL lock 5 phút |
| Chạm trần quota free (~10k lệnh/ngày) | R2 leader + outbox relay chat quá nhiều | Giảm tần suất poll, dùng delay dài khi rảnh |
| Upstash CLI: `ERR Command is not available: 'TRYM:...'` | Gõ tên key trần, CLI chỉ hiểu **lệnh** | Dùng `GET <key>`, `TTL <key>`, `SCAN 0 MATCH tryum:* COUNT 100` |
| Key "biến mất" | TTL ngắn (OTP 5 phút, rate-limit 60s, lock 60s–5 phút) | Bình thường — key hết hạn tự xoá |
| Thấy key lạ không có prefix `tryum:` | Instance dùng chung với app `thuctaptotnghiep` | Đừng xoá key không phải `tryum:*` |

## 3. RabbitMQ (CloudAMQP / LavinMQ)

| Triệu chứng | Nguyên nhân | Cách sửa |
|---|---|---|
| `AuthenticationFailureException: ACCESS_REFUSED` | Password trong `.env` sai (thiếu/thừa ký tự) | Rotate/copy đúng password, cập nhật `RABBITMQ_PASSWORD` |
| Event publish nhưng không ai nhận | Exchange không có queue bind → message bị vứt | Khai báo queue + binding (`tryum.order.events` ← `order.created`) |
| `docker compose up -d api caddy` lại kéo/build `web` | `caddy.depends_on: [api, web]` | Đổi thành `depends_on: [api]` |
| LavinMQ "No messages in queue" | 3 consumer ack ngay, queue luôn rỗng | Xem log: `docker logs ... \| grep "Nhận event"` hoặc queue demo không consumer |
| Caddy VM2 không có cert / HTTPS 000 khi curl localhost | Check sai SNI (`localhost` không có site block) | Dùng `curl --resolve tryum-api.duckdns.org:443:127.0.0.1` |
| Consumer retry liên tục khi broker lỗi | Broker chưa kết nối được | Sửa credential, consumer tự reconnect |

## 4. Deploy / Docker / Load Balancer (Caddy)

| Triệu chứng | Nguyên nhân | Cách sửa |
|---|---|---|
| API lỗi sau khi recreate container | Caddy cache IP cũ của container | Recreate Caddy sau khi recreate API |
| 503 ~10–18s ngay sau recreate Caddy | Health-check warmup | Chờ vài giây, LB tự hồi |
| OCI 8080 bị chặn (curl 000) | Security List chưa mở cổng | Thêm ingress `10.0.0.0/16 TCP 8080` |
| Docker binary biến mất trên VM2 | Cài bị mất sau khi tạo VM | Cài lại qua `get.docker.com` |
| VM2 không tới VM1 public 443 / private 443 | Security list chặn 443 private, hairpin public | Publish VM1 api ra `10.0.0.232:8080`, check qua private 8080 |
| SSH Windows "bad permissions"/sandbox từ chối known_hosts | Key quyền sai, sandbox chặn | Dùng VM1 làm jump host, `chmod 600` |

## 5. CI / Git / Build

| Triệu chứng | Nguyên nhân | Cách sửa |
|---|---|---|
| `git push` `SEC_E_NO_CREDENTIALS` | Schannel TLS lỗi | `git -c http.sslBackend=openssl push` + `gh auth token` |
| `dotnet build` local `NU1301` | NuGet SSL/credentials | Dùng CI (GitHub Actions) làm nguồn build |
| `dotnet ef migrations add` không chạy local | NuGet SSL | Tạo bảng bằng raw SQL idempotent / migration tay |
| CI backend fail: `CS7036 ... required parameter 'redis'` | Thêm tham số constructor `AuthService` nhưng test chưa cập nhật | Sửa test truyền `RedisStore` no-op (Host rỗng) |
| CI fail vì `dotnet format --verify-no-changes` | Code chưa đúng format | Chạy `dotnet format` và commit lại |

## 6. Observability (Grafana / OpenTelemetry)

| Triệu chứng | Nguyên nhân | Cách sửa |
|---|---|---|
| Traces luôn 0 span | .NET 10 chưa tương thích OTel auto-instrument | Dùng **metrics** (Prometheus), bỏ traces |
| Grafana "Metrics Endpoint" test fail | Scrape qua collector mới hoạt động | Dùng collector targets, không test UI endpoint |
| `/metrics` trả HTML SPA | Caddy chưa route `/metrics` | Thêm route `/metrics` vào Caddyfile + recreate |
| Graph `up` nhìn như 1 đường (tưởng mất instance) | 3 series cùng giá trị 1 → chồng khít | `sum(up)` = 3, hoặc xem bảng Raw |

## 7. Alerting (Telegram)

| Triệu chứng | Nguyên nhân | Cách sửa |
|---|---|---|
| Báo lặp "5xx mới: 21" rồi "RECOVERED" mỗi 15 phút | So sánh counter **cộng dồn** của các instance khác nhau qua LB | Bỏ check 5xx delta ở cron; 5xx theo dõi bằng Grafana `rate()` |
| `/health` thỉnh thoảng 503 sau 8–16s | Health check probe DB (Supabase) chậm | Thêm retry 3 lần trước khi báo |
| Không nhận được tin Telegram | Token/chat_id sai, bot chưa `/start` | `/token` lấy token text, gửi `/start` cho bot rồi `getUpdates` |

## 8. Frontend / Cloudflare Workers

| Triệu chứng | Nguyên nhân | Cách sửa |
|---|---|---|
| CF token báo 401 "Invalid API Token" | Đọc token từ ảnh sai ký tự (O/0, I/l/1) hoặc dùng Global API Key | Tạo **API Token** (Workers Scripts: Edit), dán token dạng **text** |
| Workers AI 401 "Authentication error" | Token thiếu quyền "Workers AI: Read" | Thêm permission vào token |
| Route `/pos`, `/login` trả 307 | Static assets redirect trailing-slash | `html_handling="none"` + `not_found_handling="single-page-application"` |
| Ảnh preview bị cắt | `object-fit: cover` | Đổi sang `contain` |
| Ảnh gen xấu + watermark "pollinations.ai" | Pollinations free tier | Đổi sang Workers AI / Gemini image / ảnh sản phẩm thật |
| Gemini image "exceeded quota" | Free tier hết quota ảnh | Chờ reset / dùng Workers AI / ảnh thật |

## 9. Facebook / Social

| Triệu chứng | Nguyên nhân | Cách sửa |
|---|---|---|
| `Error validating access token: Session has expired` | Page token ngắn hạn hết hạn | Kết nối lại bằng long-lived / System User token |
| App ở chế độ "Đã hủy đăng" | Development mode | Publish app sang Live hoặc dùng System User |

## 10. Bảo mật / Secret

| Triệu chứng | Nguyên nhân | Cách sửa |
|---|---|---|
| Password/token lộ trong chat | Gửi ảnh chụp chứa secret | Rotate ngay: CloudAMQP, Supabase, Grafana, GitHub |
| Nhầm ký tự khi copy từ ảnh | Ảnh khó phân biệt `0/O`, `1/I/l` | Luôn **copy text**, không chụp ảnh |

## 11. SSH / VMs (Oracle)

| Triệu chứng | Nguyên nhân | Cách sửa |
|---|---|---|
| VM1→VM2:8080 trả 000 | Security list chưa mở private | Thêm ingress `10.0.0.0/16 TCP 8080` |
| VM1 tự curl private IP của chính nó (10.0.0.232) fail | Hairpin/routing | Test từ VM2 thay vì từ VM1 |
| Recreate API phải recreate Caddy | IP container đổi, Caddy cache | Luôn recreate Caddy sau API |
