# ROADMAP — Tryum (bản KiotViet)

Tài liệu này ánh xạ **module của KiotViet** (từ trang hỗ trợ `kiotviet.vn/ho-tro/`) sang
**trạng thái hiện tại của Tryum**, và kế hoạch phát triển theo tuần. Mỗi **Chủ nhật** người dùng
mở phiên để review — nội dung review đối chiếu trực tiếp với bảng dưới.

> Stack hiện tại: .NET 10 (Clean Architecture) · Angular 22 · PostgreSQL · Docker.
> Không đổi stack — chỉ bổ sung tính năng thiếu để "giống KiotViet".

---

## 1. Ánh xạ module KiotViet → Tryum

### A. Tối ưu vận hành (vận hành bán hàng hằng ngày)

| Module KiotViet | Trạng thái Tryum | Ghi chú |
|---|---|---|
| Tra cứu sản phẩm | ✅ Đã có | `/app/products`: danh sách, tìm, ảnh, chi tiết, import Excel |
| Bán hàng (POS) | ✅ Đã có | `/app/pos`: 3 chế độ, giỏ hàng, giảm giá, điểm, thanh toán đa kênh, VietQR |
| Trả hàng | ⚠️ Hủy đơn có hoàn kho, chưa có "phiếu trả hàng" riêng | Cần module trả hàng (hoàn kho + hoàn tiền từng phần) |
| Đặt hàng | ⚠️ Có trường `Channel=Delivery` + giao vận, chưa có màn hình đặt hàng riêng | Gom vào POS delivery là đủ giai đoạn 1 |
| Thanh toán | ✅ Đã có | Tiền mặt, chuyển khoản (SePay/VietQR), thẻ, ví, voucher; thu nợ qua QR |
| Kết ca | ❌ Chưa có | Cần: đóng ca, đối soát tiền mặt/chuyển khoản, tổng kết ca |
| Giao vận | ✅ Đã có | Goship + ShippingEndpoints + trạng thái vận đơn |
| Thiết bị phần cứng | ➖ Ngoài phạm vi | Không build phần cứng |
| Chuyên ngành | ➖ Ngoài phạm vi | Để sau |

### B. Tối ưu quản lý (quản trị dữ liệu & tiền)

| Module KiotViet | Trạng thái Tryum | Ghi chú |
|---|---|---|
| Hàng hóa | ✅ Đã có | Đầy đủ: nhóm, thuộc tính, biến thể, đơn vị, giá vốn/bán, tồn, ảnh R2 |
| Giao dịch | ⚠️ Một phần | Danh sách hóa đơn có ở `/app/pos` (tab lịch sử); cần màn hình giao dịch chung |
| Nhà cung cấp | ❌ Chưa có | Cần entity Supplier + CRUD + màn hình (gom vào "Mua hàng") |
| Khách hàng | ⚠️ Backend đủ, **frontend rỗng** | Cần nối `/app/customers` vào API đã có (list + tạo + lịch sử + công nợ) |
| Nhân viên | ❌ Placeholder | Backend có User/Role; cần endpoint quản lý nhân viên + màn hình |
| Sổ quỹ | ❌ Placeholder | **Đang build trong tuần này** |
| Báo cáo | ❌ Component rỗng | Cần API thống kê + màn hình (doanh thu, lãi gộp, top bán chạy, tồn) |
| Thiết lập | ⚠️ Chỉ có cấu hình SePay | Cần thêm: thông tin cửa hàng, thuế, nhóm khách, đơn vị |
| Kết nối API | ⚠️ Có OpenAPI/Scalar, chưa có token cho bên thứ 3 | Để giai đoạn sau |

### C. Thúc đẩy doanh thu (marketing & bán online)

| Module KiotViet | Trạng thái Tryum | Ghi chú |
|---|---|---|
| Cửa hàng Online trên Zalo | ➖ Chưa | Cần tích hợp Zalo OA |
| KiotViet Web | ➖ Chưa | Website bán hàng riêng |
| Bán Online (Sàn TMĐT, MXH) | ⚠️ Có `SocialEndpoints` (Facebook) | Mở rộng dần |
| Phân tích | ❌ Chưa | Gom vào Báo cáo |
| Khuyến mại | ⚠️ Có giảm giá theo đơn/dòng, chưa có chương trình khuyến mại tự động | Cần engine promotion |
| Voucher / Coupon | ⚠️ Có trường `CouponCode` trên Order, chưa có bảng voucher | Cần entity Voucher + áp dụng khi bán |
| Tích điểm | ✅ Đã có | Cộng/trừ điểm, quy đổi 1đ=1000đ, lịch sử theo khách |
| Chăm sóc khách hàng | ⚠️ Một phần | Điểm + công nợ; cần SMS/Zalo chăm sóc (có sẵn SMS sender) |

---

## 2. Kế hoạch theo tuần (review Chủ nhật)

Mỗi tuần làm 1–2 module hoàn chỉnh **backend → frontend → build xanh → commit**.

| Tuần | Module | Mục tiêu review |
|---|---|---|
| Tuần này | **Sổ quỹ** | Thu/chi tiền, số dư quỹ, lọc theo ngày/loại, liên kết hóa đơn |
| Kế tiếp | **Khách hàng (nối FE)** + **Nhân viên** | List/tạo khách hàng, lịch sử mua, công nợ; quản lý nhân viên + phân quyền |
| Sau đó | **Nhà cung cấp + Mua hàng** | CRUD NCC, phiếu nhập hàng → tăng tồn, giá vốn bình quân |
| Sau đó | **Trả hàng + Kết ca** | Phiếu trả hàng (hoàn kho, hoàn tiền); đóng ca đối soát |
| Sau đó | **Báo cáo** | Dashboard thật + báo cáo doanh thu/lãi/top/tồn |
| Sau đó | **Khuyến mại + Voucher** | Chương trình khuyến mại, bảng voucher, áp dụng khi bán |

---

## 3. Cách review mỗi Chủ nhật

1. Mở phiên DSH này (working dir `C:\Users\ASUS\Tryum`).
2. Yêu cầu: *"Tiếp tục mục tiêu"* hoặc *"Review tiến độ tuần"*.
3. Tôi sẽ báo: module đã xong, còn dở, build có xanh không, commit nào mới.
4. Bạn quyết định ưu tiên tuần sau (hoặc để tôi theo bảng trên).
