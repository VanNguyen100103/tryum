# Tryum — Security & Quality Plan (thống nhất)

Nguyên tắc: **KHÔNG auto-vá mù**. Mọi phát hiện đều qua 4 bước:

```text
DETECT  →  REPRODUCE  →  FIX  →  VERIFY
(tool tự động)  (bạn tự kiểm chứng thật/giả)  (AI/người đề xuất)  (chạy lại đúng lệnh đó)
```

Lý do: scanner có **false positive 20–40%**. AI vá dựa trên false positive = phá code thật.

## 1. Bảng tổng: 7 lớp bảo mật

| Lớp | Mục tiêu | Tool chính | Free? | Chạy ở đâu |
|---|---|---|---|---|
| **L0** | Dependency có CVE | Dependabot · Trivy · `dotnet list package --vulnerable` | ✅ | CI mỗi push |
| **L1** | Code sai (SAST) | Semgrep OSS | ✅ | CI mỗi push |
| **L2** | Secret lộ | Gitleaks · TruffleHog | ✅ | CI mỗi push |
| **L3** | Web/API lỗi thật (DAST) | OWASP ZAP · Nuclei · Nikto | ✅ | staging, weekly |
| **L4** | Mất cách ly tenant (AuthZ) | Custom test Playwright/API | ✅ | CI mỗi push |
| **L5** | UI/UX + accessibility | Playwright + axe-core · Lighthouse | ✅ | staging, weekly |
| **L6** | Lỗi runtime (monitoring) | Prometheus/Grafana · Sentry · UptimeRobot | ✅ free tier | 24/7 |
| **L7** | Pentest AI (chain lỗi) | Strix · HexStrike | ⚠️ tốn token | staging, monthly |

## 2. Quy trình kiểm chứng từng lớp

### L0 — Dependency CVE

| Bước | Việc làm |
|---|---|
| **Detect** | `dotnet list package --vulnerable` / Dependabot / Trivy |
| **Reproduce** | Mở GHSA advisory → xác định hàm bị lỗi. `grep` xem code có **thực sự gọi** hàm đó không. Nếu không dùng → false positive, đánh dấu "not affected" |
| **Fix** | Nâng version (Dependabot PR) hoặc thay package |
| **Verify** | `dotnet build` + `dotnet test` pass + chạy lại `--vulnerable` → 0 |

### L1 — SAST (Semgrep)

| Bước | Việc làm |
|---|---|
| **Detect** | `semgrep --config p/csharp --config p/security-audit` |
| **Reproduce** | Mở đúng `file:line`. Kiểm tra: input người dùng có **chảy tới sink** không (SQL/exec/path)? Endpoint có **requireAuthorization** không? Không có → false positive |
| **Fix** | Parameterized query / whitelist / bỏ interpolate |
| **Verify** | Chạy lại Semgrep → rule biến mất **+ viết unit test tái hiện lỗi cũ** (test phải fail trước, pass sau) |

### L2 — Secret lộ

| Bước | Việc làm |
|---|---|
| **Detect** | `gitleaks detect --source . -v` |
| **Reproduce** | `git log -S "<secret>"` xem vào lúc nào. Gọi thử API bằng secret đó: còn sống không? |
| **Fix** | **Rotate trước**, rồi mới xoá khỏi code, chuyển sang GitHub Secrets |
| **Verify** | Chạy lại gitleaks → 0 **+ xác nhận secret cũ đã bị từ chối** (401) |

### L3 — DAST (ZAP / Nuclei)

| Bước | Việc làm |
|---|---|
| **Detect** | ZAP baseline (prod-safe) · ZAP full + Nuclei (staging) |
| **Reproduce** | ZAP xuất **request/response đầy đủ** → copy sang `curl` chạy lại tay. Nếu không tái hiện được → false positive |
| **Fix** | Sửa validate/escape/header/CSP |
| **Verify** | Chạy lại **đúng request đó** (phải sạch) + chạy lại scan |

### L4 — AuthZ / tenant isolation (rủi ro cao nhất)

| Bước | Việc làm |
|---|---|
| **Detect** | Test: login tenant A → gọi `GET /api/v1/products/{id_cua_B}` |
| **Reproduce** | Kỳ vọng **404/403**. Nếu trả 200 → **lỗi thật, nghiêm trọng** |
| **Fix** | Global query filter / ownership check |
| **Verify** | Test pass + giữ lại làm **regression test vĩnh viễn** |

```csharp
// Mẫu test bắt buộc phải có
[Fact] public async Task Tenant_A_khong_doc_duoc_san_pham_cua_Tenant_B()
    => Assert.Equal(HttpStatusCode.NotFound, await GetAsTenantA($"/api/v1/products/{productIdOfB}"));
```

### L5 — Accessibility

| Bước | Việc làm |
|---|---|
| **Detect** | Playwright + `@axe-core/playwright` (contrast, alt, label, ARIA, tab order) |
| **Reproduce** | axe trả `target` selector → mở DevTools kiểm tra tay (màu, thuộc tính) |
| **Fix** | Thêm `alt`, sửa contrast token, thêm `aria-label` |
| **Verify** | Chạy lại axe → 0 violation cho rule đó |

### L6 — Runtime

| Bước | Việc làm |
|---|---|
| **Detect** | Sentry (error), UptimeRobot (uptime), Grafana (5xx/latency/RAM) |
| **Reproduce** | Mở Sentry → xem **stack trace + breadcrumbs + user** → chạy lại endpoint bằng curl |
| **Fix** | Sửa bug |
| **Verify** | Sentry không còn event mới + smoke test pass |

### L7 — AI pentest (Strix / HexStrike)

| Bước | Việc làm |
|---|---|
| **Detect** | Strix chạy với LLM, tự chain nhiều tool |
| **Reproduce** | **BẮT BUỘC** làm tay: Strix đưa PoC → tự chạy lại bằng curl/Postman. AI hay "ảo giác" PoC |
| **Fix** | Chỉ vá sau khi đã tái hiện được |
| **Verify** | PoC cũ không còn chạy được + scan lại |

**Quy tắc vàng:** AI chỉ được **đề xuất patch**, không được merge. Patch phải qua CI test + 1 lần bạn chạy PoC cũ để xác nhận đã chặn.

## 3. Plan theo giai đoạn (chi phí tối thiểu)

### Giai đoạn 1 — Tuần 1 (FREE, 1–2 giờ)

```text
[ ] .github/dependabot.yml            (NuGet + npm + Docker)
[ ] Job Trivy scan image trước deploy
[ ] Job Semgrep OSS (SAST)
[ ] Job Gitleaks (secret scan)
[ ] Job "dotnet list package --vulnerable"
→ Tất cả báo Telegram khi fail. Chi phí: $0
```

### Giai đoạn 2 — Tuần 2 (FREE, 2–3 giờ)

```text
[ ] Tách staging (VM2 riêng hoặc docker-compose local)
[ ] Job ZAP baseline → production (passive, an toàn)
[ ] Job ZAP full + Nuclei + Nikto → staging (weekly)
[ ] Test AuthZ tenant isolation (L4) — 5 case
[ ] Playwright + axe-core (L5) — 3 màn chính
→ Chi phí: $0
```

### Giai đoạn 3 — Tuần 3 (RẺ, ~$1/tháng)

```text
[ ] Sentry free (FE + BE)          → cần bạn đăng ký
[ ] UptimeRobot free               → cần bạn đăng ký
[ ] DeepSeek tóm tắt report scanner → issue + gợi ý fix  (~$0.02/lần)
[ ] DeepSeek đọc log khi alert      → RCA Telegram       (~$0.001/lần)
```

### Giai đoạn 4 — Tuần 4 (tuỳ chọn)

```text
[ ] Strix + model rẻ (gpt-4o-mini hoặc DeepSeek)
    ~$0.05–0.3/lần scan, chạy monthly
[ ] MobSF nếu có app Android
```

## 4. Chi phí

| Giai đoạn | Nội dung | Chi phí/tháng |
|---|---|---|
| 1 | Dependabot, Trivy, Semgrep, Gitleaks | **$0** |
| 2 | ZAP, Nuclei, Nikto, Playwright/axe | **$0** |
| 3 | Sentry free, UptimeRobot free, DeepSeek triage | **~$1** |
| 4 | Strix (monthly) | **~$0.3–1** |
| | **TỔNG** | **≈ $1–2/tháng** |

## 5. Không nên tự động hoá những việc này

| Việc | Vì sao |
|---|---|
| Auto-merge patch bảo mật | False positive → phá code |
| AI sửa logic tiền/kho | Sai nghiệp vụ, không ai phát hiện |
| Chạy ZAP full scan lên production | Bắn payload thật, ghi rác DB, bị chặn IP |
| Tin PoC của AI mà không reproduce | AI hay bịa request không tồn tại |

## 6. Checklist xác nhận "một finding là thật"

```text
[ ] 1. Có đường dẫn từ INPUT người dùng tới chỗ nguy hiểm?
[ ] 2. Endpoint có yêu cầu đăng nhập / đúng quyền không?
[ ] 3. Dữ liệu có bị lọc theo tenant không?
[ ] 4. Tái hiện được bằng curl/PoC trong 2 phút không?
[ ] 5. Nếu KHÔNG tái hiện được → đóng, ghi "false positive"

→ Chỉ khi 1,4 đúng và 2,3 sai → mới là lỗi thật.
```

## 7. Lệnh chạy nhanh (copy-paste)

```bash
# L0 dependency
dotnet list package --vulnerable --include-transitive

# L1 SAST
semgrep --config=p/csharp --config=p/security-audit backend/

# L2 secret
gitleaks detect --source . -v

# L3 DAST (staging)
docker run --rm -t ghcr.io/zaproxy/zaproxy:stable zap-full-scan.py -t https://staging.local
docker run --rm projectdiscovery/nuclei:latest -u https://staging.local -severity critical,high

# L4 AuthZ (thay bằng token thật)
curl -s -o /dev/null -w "%{http_code}\n" -H "Authorization: Bearer <token_tenant_A>" \
  https://tryum-api.duckdns.org/api/v1/products/<id_san_pham_tenant_B>
# kỳ vọng: 404 hoặc 403 (KHÔNG được 200)

# L5 a11y
npx playwright test e2e/a11y.spec.ts
```

## 8. Kết quả kỳ vọng sau 4 tuần

```text
Mỗi push:      CVE + SAST + secret được chặn tự động
Mỗi tuần:      DAST + a11y + AuthZ chạy trên staging, báo Telegram
24/7:          Sentry + UptimeRobot + Grafana → alert
Khi có alert:  DeepSeek tóm tắt RCA, bạn xác nhận rồi mới vá
Khi có lỗi:    có PoC + có cách verify → không vá mù
```
