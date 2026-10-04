/** Thông tin user hiện tại – khớp với `UserInfo` của API. */
export interface UserInfo {
  id: string;
  fullName: string;
  email: string;
  phone: string | null;
  tenantId: string;
  tenantName: string;
  roleCode: string;
  roleName: string;
}

/** Kết quả đăng nhập / đăng ký / refresh. */
export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAt: string;
  user: UserInfo;
}

/** Đăng nhập: nhận email HOẶC số điện thoại (giống KiotViet). */
export interface LoginRequest {
  identifier: string;
  password: string;
}

export interface RegisterRequest {
  shopName: string;
  ownerName: string;
  email: string;
  password: string;
  phone: string;
  otpCode: string;
}

export interface SendOtpRequest {
  phone: string;
  /** Bắt buộc ở lần gửi đầu (để chống bot đốt quota SMS). */
  captchaId?: string;
  captchaCode?: string;
  /** true = bấm "Gửi lại" ở bước OTP (đã vượt captcha, chỉ còn cooldown + quota/ngày). */
  isResend?: boolean;
}

export interface CaptchaResponse {
  captchaId: string;
  /** Ảnh SVG dạng data-uri, gán thẳng vào <img src>. */
  imageDataUrl: string;
}

export interface SendOtpResponse {
  sent: boolean;
  resendAfterSeconds: number;
  /** Mã OTP còn hiệu lực (giây) để UI hiển thị "Mã có hiệu lực trong N phút". */
  expiresInSeconds: number;
  /** Chỉ có khi server chạy demo mode (mô phỏng SMS). */
  demoOtp?: string;
}

/** ProblemDetails trả về khi lỗi (RFC 9457). */
export interface ApiProblem {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errorCode?: string;
  errors?: Record<string, string[]>;
}