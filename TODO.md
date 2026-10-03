# TODO – SaaS quản lý bán hàng (kiểu KiotViet)

**Stack:** .NET 10 LTS · Angular (bản mới nhất) · PostgreSQL · Docker
**Deploy (0đ, chạy 24/7):** Cloudflare Pages (FE) · Oracle Cloud Always Free VM (API + DB) · DuckDNS · Caddy (HTTPS) · Cloudflare R2 (backup) · UptimeRobot · GitHub Actions + GHCR

---

## Phase 0 – Chuẩn bị tài khoản & hạ tầng (làm NGAY, Oracle hay khó đăng ký)
- [x] Tạo GitHub repo (monorepo: `backend/`, `frontend/`, `deploy/`) – *private `VanNguyen100103/tryum`, đã push (`.env`/`*.pem` excluded)*
- [x] Đăng ký Oracle Cloud – chọn Home Region: Tokyo / Osaka / Seoul (tránh Singapore) *(đã đăng ký, home region: ap-osaka-1)*
- [ ] Nâng tài khoản lên Pay As You Go (tránh bị thu hồi VM idle) + đặt Budget Alert 1 USD
- [x] Tạo VM (Ubuntu 24.04) – *thực tế: E2.1.Micro **x86_64** (không phải ARM), đang chạy 24/7*
- [x] Gán Reserved Public IP (`129.225.202.82`), cấu hình SSH key
- [x] Mở port 80/443: Security List + iptables *(đã verify Caddy nhận traffic từ ngoài)*
- [x] Cài Docker + Docker Compose plugin trên VM *(Docker 29.8 + compose v5.5)*
- [x] DuckDNS: tạo `tryum-api.duckdns.org` trỏ về IP VM *(A record + cron update 30p trên VM)*
- [x] Cloudflare: tạo tài khoản + project Pages/UI (`tryum-web.nganhvan1609.workers.dev`, deploy bằng wrangler CLI, CORS API đã mở)
- [x] Cloudflare: bucket R2 `tryum-backups` (APAC) + API token `tryum-backup` (Object Read & Write)
- [x] UptimeRobot: tạo tài khoản + 2 monitor HTTP(S) `Tryum API` (`/health`) và `Tryum UI` (interval 5p)

## Phase 1 – Khung dự án (tuần 1)
**Backend**
- [x] Solution Clean Architecture: `Domain`, `Application`, `Infrastructure`, `Api`, `Tests`
- [x] EF Core + Npgsql, migrations đầu tiên (`InitialCreate`)
- [x] Serilog, global error handling (ProblemDetails), endpoint `/health`
- [x] OpenAPI + Scalar/Swagger UI
- [x] `docker-compose.dev.yml` (Postgres local)
- [x] Unit test cho `BaseEntity` / `ApplicationDbContext` (audit timestamps)

**Frontend**
- [x] Angular standalone + Signals, PrimeNG (Aura theme)
- [x] Cấu hình environment (API URL dev/prod)
- [x] Layout chính (sidebar, topbar), lazy-loaded routes

**CI**
- [x] GitHub Actions: build + test BE/FE mỗi khi push/PR
- [x] Coverage (Cobertura → Codecov) + `dotnet format --verify-no-changes`

## Phase 2 – Auth & Multi-tenant (tuần 2)
- [x] Entity `Tenant`, `User`, `Role` (+ `RefreshToken`), EF Core configuration + migration `InitialCreate`
- [x] Đăng ký cửa hàng → tạo tenant (slug duy nhất) + tài khoản chủ (role Owner)
- [x] JWT access token + refresh token (rotation), hash mật khẩu BCrypt
- [x] `TenantId` + EF Core Global Query Filter, lấy tenant từ claim (middleware)
- [x] Phân quyền: Chủ cửa hàng / Quản lý / Thu ngân (policy-based)
- [ ] Quản lý nhân viên (mời, phân quyền, khoá) — *endpoint `/api/v1/users`*
- [x] FE: trang đăng nhập/đăng ký, HTTP interceptor (auto refresh token), route guard
- [x] Unit test tenant isolation (tenant A **không** đọc được dữ liệu tenant B)
- [ ] Integration test (Testcontainers) trên PostgreSQL thật — *cần Docker, chưa chạy được lúc này*

**Đã làm thêm (ngoài list trên):**
- Seeder 3 role dùng chung (`roles`) chạy idempotent lúc start API
- Rate limiting 20 req/phút cho endpoint `/auth/*` (chống brute force)
- CORS + `JwtOptions` validation lúc startup, ProblemDetails cho lỗi nghiệp vụ

## Phase 3 – Hàng hóa (tuần 3)
- [ ] Nhóm hàng (category, nhiều cấp)
- [ ] Sản phẩm: mã SKU, mã vạch, đơn vị tính, giá vốn, giá bán
- [ ] Biến thể (size/màu)
- [ ] API phân trang, tìm kiếm, lọc, sắp xếp
- [ ] FE: danh sách (DataTable), form thêm/sửa, validate
- [ ] *(tùy chọn)* Ảnh sản phẩm lưu trên R2
- [ ] *(tùy chọn)* Import/Export Excel

## Phase 4 – Kho (tuần 4)
- [ ] Nhà cung cấp
- [ ] Phiếu nhập hàng → cập nhật tồn kho + giá vốn bình quân
- [ ] Phiếu kiểm kho / điều chỉnh tồn
- [ ] Sổ kho / thẻ kho (`StockMovement` – ghi mọi biến động)
- [ ] Chống lỗi khi nhiều người sửa cùng lúc (optimistic concurrency – `xmin` của Postgres)
- [ ] Cảnh báo hàng sắp hết

## Phase 5 – Bán hàng POS & Khách hàng (tuần 5–6)
- [ ] Màn hình POS: tìm/quét mã vạch, giỏ hàng, giảm giá (theo món / cả đơn)
- [ ] Nhiều hóa đơn song song (tab), phím tắt (F2, F9…)
- [ ] Thanh toán: tiền mặt (tính tiền thừa), chuyển khoản (hiện QR VietQR)
- [ ] Tạo hóa đơn trong 1 transaction: trừ kho + ghi sổ kho + chặn âm kho
- [ ] In hóa đơn khổ K80
- [ ] Hủy hóa đơn / trả hàng (hoàn kho)
- [ ] Khách hàng: CRUD, lịch sử mua, công nợ, điểm tích lũy
- [ ] Danh sách hóa đơn: lọc theo ngày, nhân viên, trạng thái

## Phase 6 – Báo cáo & Realtime (tuần 7)
- [ ] Dashboard: doanh thu hôm nay, số đơn, lãi gộp
- [ ] Báo cáo doanh thu theo ngày/tháng, top sản phẩm bán chạy, tồn kho
- [ ] Biểu đồ (PrimeNG Chart)
- [ ] SignalR: dashboard cập nhật realtime khi có đơn mới

## Phase 7 – Deploy production (tuần 8)
- [x] Dockerfile API multi-stage (non-root + HEALTHCHECK) — *đã build & chạy local; chưa thử build `linux/arm64`*
- [x] `deploy/docker-compose.prod.yml`: Caddy + API + Postgres + web (volume, không expose port DB)
- [x] `deploy/Caddyfile`: 1 domain phục vụ `/api/*` + `/health` + SPA, tự cấp HTTPS, có security headers
- [x] Biến môi trường/secret qua `deploy/.env` (không commit) + `deploy/.env.example`
- [x] Chạy migration khi start API (`Database__ApplyMigrationsOnStartup`)
- [x] CORS cho domain frontend (`Cors__AllowedOrigins__0`)
- [x] **Dry-run giống production trên local** *(đã verify: migration, seed 3 role, register/login/refresh-rotation/logout, SPA fallback)*
  → `docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env up -d --build` rồi mở https://localhost:7080
- [ ] Build & push image **linux/amd64** lên GHCR *(VM E2.1.Micro là x86_64, không phải ARM)*
- [x] `Caddyfile` trỏ domain DuckDNS thật (đổi `API_DOMAIN` trong `.env`, bỏ `tls internal`, ACME email thật)
- [x] **Deploy production đầu tay** *(build local → docker load lên VM; API+DB+UI chạy trên `tryum-api.duckdns.org`, cert ZeroSSL, UImirror trên Cloudflare Workers, CORS 2 origin)*
- [ ] GitHub Actions: build image amd64 → push GHCR → SSH vào VM `docker compose pull && up -d`
- [ ] GitHub Actions: build Angular → deploy Cloudflare Pages (wrangler)
- [ ] Cron backup `pg_dump` hằng ngày → R2 (rclone), giữ 7–14 bản
- [ ] **Thử restore backup** ít nhất 1 lần
- [ ] UptimeRobot theo dõi `/health` + báo email
- [x] Rate limiting (built-in ASP.NET Core) cho endpoint đăng nhập *(làm sớm ở Phase 2)*

## Phase 8 – Hoàn thiện để xin việc (tuần 9–10)
- [ ] Seed tenant demo (vài trăm sản phẩm, đơn hàng 3 tháng) + nút **"Dùng thử tài khoản demo"**
- [ ] Job reset dữ liệu demo hằng đêm
- [ ] README: sơ đồ kiến trúc, screenshot, link demo, hướng dẫn `docker compose up`
- [ ] Mục "Quyết định kỹ thuật" (multi-tenant, concurrency tồn kho, auth…)
- [ ] Badge CI + test coverage
- [ ] Video demo 2–3 phút gắn vào README
- [ ] Ghi link demo + repo vào CV / LinkedIn
