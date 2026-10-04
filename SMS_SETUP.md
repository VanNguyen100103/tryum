# Gửi SMS thật cho OTP (production)

Trang đăng ký Tryum xác thực số điện thoại bằng **mã OTP 6 số**. Ở production, OTP phải
được gửi **qua SMS thật** tới người dùng.

- **Dev/demo**: `Sms:Provider=Log` → OTP chỉ ghi ra log, UI hiện mã trong banner (0đ).
- **Production**: `Sms:Provider=SmsGate` → gửi SMS thật qua **SMS Gate for Android** (MIỄN PHÍ).

> ⚠️ API **fail-fast**: nếu `Provider=SmsGate` mà thiếu `SendUrl`/`Username`/`Password`,
> ứng dụng **không khởi động** và log rõ lý do — tránh việc production chạy mà không gửi được SMS.

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
| `Sms:Provider` | `SMS_PROVIDER` | `Log` (dev) hoặc `SmsGate` (gửi thật) |
| `Sms:SmsGate:SendUrl` | `SMS_GATE_URL` | URL gửi tin (Cloud hoặc Local) |
| `Sms:SmsGate:Username` | `SMS_GATE_USERNAME` | Basic Auth user |
| `Sms:SmsGate:Password` | `SMS_GATE_PASSWORD` | Basic Auth password |
| `Sms:SmsGate:SimNumber` | `SMS_GATE_SIM` | SIM dùng để gửi (1-based) |
| `Sms:SmsGate:WithDeliveryReport` | — | Bật báo cáo trạng thái gửi |
| `Sms:SmsGate:TimeoutSeconds` | — | Timeout HTTP (giây) |
| `Auth:DemoOtpInResponse` | `DEMO_OTP_IN_RESPONSE` | `true` = trả mã trong response (chỉ dev) |
