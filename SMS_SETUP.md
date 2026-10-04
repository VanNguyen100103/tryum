# Gửi SMS thật cho OTP (production)

Trang đăng ký Tryum xác thực số điện thoại bằng **mã OTP 6 số**. Ở production, OTP phải
được gửi **qua SMS thật** tới người dùng.

- **Dev/demo**: `Sms:Provider=Log` → OTP chỉ ghi ra log, UI hiện mã trong banner (0đ).
- **Production**: `Sms:Provider=SmsGate` (SMS Gate) hoặc `Sms:Provider=Textbee` (textbee.dev) → gửi SMS thật.

> ⚠️ API **fail-fast**: nếu `Provider=SmsGate` mà thiếu `SendUrl`/`Username`/`Password`,
> hoặc `Provider=Textbee` mà thiếu `ApiKey`, ứng dụng **không khởi động** và log rõ lý do —
> tránh việc production chạy mà không gửi được SMS.

---

## Cách 1 (MIỄN PHÍ, mặc định) — SMS Gate for Android

Biến một điện thoại Android (kèm SIM) thành SMS gateway. Không phí thuê bao, không cần
brandname; SMS tính theo cước SIM của bạn.

### Bước 1 — Cài app lên điện thoại Android
- Tải **SMS Gate for Android** tại https://sms-gate.app (APK trên GitHub Releases / CH Play).
- Mở app, đặt **username/password** cho API (Basic Auth).
- Cấp quyền gửi SMS + tắt tối ưu pin cho app để chạy nền.

### Bước 2 — Chọn chế độ kết nối
| Chế độ | Khi nào dùng | `SMS_GATE_URL` |
|---|---|---|
| **Cloud** (khuyên dùng) | Server (VM/container) ở xa điện thoại | `https://api.sms-gate.app/3rdparty/v1/messages` |
| **Local** | Server và điện thoại cùng Wi-Fi/LAN | `http://<ip-dien-thoai>:8080/message` |

Cloud: tạo tài khoản tại https://sms-gate.app rồi đăng nhập trong app để thiết bị kết nối cloud.

### Bước 3 — Điền `deploy/.env` trên VM
```env
SMS_PROVIDER=SmsGate
SMS_GATE_URL=https://api.sms-gate.app/3rdparty/v1/messages
SMS_GATE_USERNAME=<username bạn đặt trong app>
SMS_GATE_PASSWORD=<password bạn đặt trong app>
SMS_GATE_SIM=1
DEMO_OTP_IN_RESPONSE=false
```

### Bước 4 — Deploy lại
```bash
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env up -d --build api
```

---

## Cách 1b (MIỄN PHÍ, free tier) — textbee.dev

[textbee.dev](https://textbee.dev) là SMS gateway **mã nguồn mở**, tự host được, free tier:
**1 máy · 50 tin/ngày · 300 tin/tháng** — đủ dùng cho MVP. Dùng điện thoại Android + app
textbee làm gateway, server gọi REST API (`x-api-key`).

> ⚠️ API key textbee có **toàn quyền trên tài khoản** (đọc/gửi tin, đổi webhook).
> Chỉ dán vào `deploy/.env` (file này bị `.gitignore`), **không commit, không gửi qua chat**.

### Bước 1 — Tạo tài khoản + xác minh email
- Đăng ký tại https://textbee.dev, mở email xác minh. **Chưa xác minh thì API trả 400.**

### Bước 2 — Cài app lên điện thoại Android
- Mở https://textbee.dev/download ngay trên máy → tải APK → cho phép "cài ứng dụng từ nguồn lạ".
- Yêu cầu: **Android 7.0+**, SIM còn hoạt động, có mạng.
- **Android 15/16**: quyền SMS bị khoá với app cài từ APK → *App info → ⋮ → Allow restricted settings → Permissions → SMS → Allow*.

### Bước 3 — Đăng ký máy thành gateway
1. Dashboard → **Devices → Add a device → Continue** (hiện QR code chứa API key).
2. Mở app textbee → **Get Started → Scan QR Code** (hoặc *Enter manually* dán API key).
3. Nhập tên máy (tuỳ chọn) → **Register Device** → cấp quyền.
4. Màn **"You're all set!"** hiện **Device ID** — lưu lại (cần cho API và để khi cài lại app).

### Bước 4 — Giữ máy online (bắt buộc)
- **Settings → Battery usage → Unrestricted** (realme/OPPO: *Settings → Battery → App battery management → Unrestricted*).
- Giữ app chạy nền + có sticky notification; bật "Forward received SMS" nếu muốn nhận SMS về dashboard.
- Máy offline ⇒ tin nằm `pending`, **không** gửi được.

### Bước 5 — Điền `deploy/.env` trên VM
```env
SMS_PROVIDER=Textbee
TEXTBEE_API_KEY=<key tạo trong dashboard – chỉ hiện 1 lần>
TEXTBEE_URL=https://api.textbee.dev/api/v1/gateway/send-sms
TEXTBEE_DEVICE_ID=6ac2112d48861886cf4ebbee
TEXTBEE_SIM=
DEMO_OTP_IN_RESPONSE=false
```

### Bước 6 — Deploy
```bash
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env up -d --build api
```

### Kiểm tra key + máy online
```bash
# 1) Key hợp lệ? (200 = OK)
curl https://api.textbee.dev/api/v1/gateway/stats -H "x-api-key: $TEXTBEE_API_KEY"

# 2) Máy online? xem enabled + lastHeartbeat
curl https://api.textbee.dev/api/v1/gateway/devices -H "x-api-key: $TEXTBEE_API_KEY"

# 3) Gửi thử 1 tin
curl -X POST https://api.textbee.dev/api/v1/gateway/send-sms \
  -H "x-api-key: $TEXTBEE_API_KEY" -H "Content-Type: application/json" \
  -d '{"recipients":["+84901234567"],"message":"Tryum test"}'
```
`200` ⇒ textbee đã nhận batch (trả `smsBatchId`). **Chưa** khẳng định đã gửi tới máy —
theo dõi `status`/`errorCode` ở `/gateway/messages?smsBatchId=...` hoặc dashboard.

### Lỗi thường gặp (textbee)
| Hiện tượng | Nguyên nhân | Cách sửa |
|---|---|---|
| `401 AUTH_INVALID` | Key thiếu/sai/đã thu hồi | Tạo key mới; nếu key của máy bị thu hồi thì reconnect máy bằng Device ID cũ |
| `400` | Chưa xác minh email, hoặc `No enabled device found` | Xác minh email; bật Gateway Enabled trên app |
| `404 Device not found` | `DeviceId` không thuộc tài khoản | Xem lại `TEXTBEE_DEVICE_ID` |
| `429` | Hết quota ngày/tháng (free: 50/ngày, 300/tháng) | Chờ reset ngày hoặc nâng plan |
| Tin kẹt `pending` | Máy offline / app bị tạm dừng nền | Mở app, tắt tối ưu pin, kiểm tra mạng |
| `errorCode "5"` nhiều tin | Android giới hạn số tin gửi/ngày của app | Chờ reset, giảm tần suất gửi |

> Nếu chạy hết quota free tier, chuyển `SMS_PROVIDER` sang `SmsGate` hoặc nhà cung cấp trả phí.

---

## Cách 2 (trả phí, ổn định cao) — eSMS / SpeedSMS / Twilio…

Nếu không muốn phụ thuộc điện thoại, dùng nhà cung cấp SMS (eSMS, SpeedSMS, VietGuys,
Twilio…). Cần viết thêm **1 class** cài `ISmsSender` (giống `SmsGateSmsSender`), đăng ký
thêm một nhánh `Provider` trong `DependencyInjection.cs`, rồi điền API key. **Không cần
sửa `OtpService`** — kiến trúc đã tách sẵn.

---

## Kiểm tra sau khi deploy

```bash
# 1) API sống
curl https://<domain>/health

# 2) Gửi thử OTP tới số của bạn (xem SMS có về máy không)
curl -X POST https://<domain>/api/v1/auth/otp/send \
  -H "Content-Type: application/json" \
  -d '{"phone":"0901234567"}'
```
Kỳ vọng: `200` với `{"sent":true,"resendAfterSeconds":30,"demoOtp":null}` và điện thoại nhận SMS.
(`demoOtp` phải là `null` ở production — nếu có mã tức là đang bật chế độ demo.)

## Xử lý lỗi thường gặp
| Hiện tượng | Nguyên nhân | Cách sửa |
|---|---|---|
| API không khởi động, log `Sms:Provider=SmsGate yêu cầu …` | Thiếu credential | Điền `SMS_GATE_*` trong `deploy/.env` |
| `502 sms_send_failed` | Gateway từ chối / sai user-pass / URL sai | Kiểm tra `SMS_GATE_URL/USERNAME/PASSWORD`; thử curl trực tiếp gateway |
| SMS không tới dù API trả 200 | Điện thoại offline / app bị kill nền / hết tiền SIM | Mở app, tắt tối ưu pin, kiểm tra SIM |
| Muốn tạm tắt SMS (dev) | — | Đặt `SMS_PROVIDER=Log` (+ `DEMO_OTP_IN_RESPONSE=true`) |

## Bảng biến cấu hình
| Key (appsettings) | Env (docker-compose) | Ý nghĩa |
|---|---|---|
| `Sms:Provider` | `SMS_PROVIDER` | `Log` (dev), `SmsGate` hoặc `Textbee` (gửi thật) |
| `Sms:SmsGate:SendUrl` | `SMS_GATE_URL` | URL gửi tin (Cloud hoặc Local) |
| `Sms:SmsGate:Username` | `SMS_GATE_USERNAME` | Basic Auth user |
| `Sms:SmsGate:Password` | `SMS_GATE_PASSWORD` | Basic Auth password |
| `Sms:SmsGate:SimNumber` | `SMS_GATE_SIM` | SIM dùng để gửi (1-based) |
| `Sms:SmsGate:WithDeliveryReport` | — | Bật báo cáo trạng thái gửi |
| `Sms:SmsGate:TimeoutSeconds` | — | Timeout HTTP (giây) |
| `Sms:Textbee:ApiKey` | `TEXTBEE_API_KEY` | API key textbee (header `x-api-key`) |
| `Sms:Textbee:SendUrl` | `TEXTBEE_URL` | `https://api.textbee.dev/api/v1/gateway/send-sms` |
| `Sms:Textbee:DeviceId` | `TEXTBEE_DEVICE_ID` | Máy gửi tin (bỏ trống = máy mặc định) |
| `Sms:Textbee:SimSubscriptionId` | `TEXTBEE_SIM` | SIM dùng để gửi (bỏ trống = SIM mặc định) |
| `Sms:Textbee:TimeoutSeconds` | — | Timeout HTTP (giây, mặc định 15) |
| `Auth:DemoOtpInResponse` | `DEMO_OTP_IN_RESPONSE` | `true` = trả mã trong response (chỉ dev) |
