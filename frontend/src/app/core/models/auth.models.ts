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

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest {
  shopName: string;
  ownerName: string;
  email: string;
  password: string;
  phone?: string | null;
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