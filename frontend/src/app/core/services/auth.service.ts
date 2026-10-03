import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, tap, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import type {
  ApiProblem,
  AuthResponse,
  LoginRequest,
  RegisterRequest,
  UserInfo,
} from '../models/auth.models';
import { TokenStorageService } from './token-storage.service';

/**
 * Quản lý phiên đăng nhập: đăng nhập / đăng ký / refresh / đăng xuất.
 * State dùng Signals nên template tự cập nhật.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly storage = inject(TokenStorageService);
  private readonly router = inject(Router);

  private readonly baseUrl = `${environment.apiUrl}/api/v1/auth`;

  private readonly currentUser = signal<UserInfo | null>(this.storage.user);

  /** User hiện tại (null nếu chưa đăng nhập). */
  readonly user = this.currentUser.asReadonly();

  /** Đã đăng nhập hay chưa. */
  readonly isAuthenticated = computed(() => this.currentUser() !== null);

  readonly isOwner = computed(() => this.currentUser()?.roleCode === 'Owner');

  readonly initials = computed(() => {
    const parts = (this.currentUser()?.fullName ?? '').trim().split(/\s+/);
    const first = parts[0]?.charAt(0) ?? '';
    const last = parts.length > 1 ? parts[parts.length - 1].charAt(0) : '';
    return (first + last).toUpperCase();
  });

  accessToken(): string | null {
    return this.storage.accessToken;
  }

  refreshToken(): string | null {
    return this.storage.refreshToken;
  }

  login(request: LoginRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.baseUrl}/login`, request).pipe(
      tap((res) => this.applySession(res)),
    );
  }

  register(request: RegisterRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.baseUrl}/register`, request).pipe(
      tap((res) => this.applySession(res)),
    );
  }

  /** Đổi refresh token lấy access token mới (token cũ bị revoke – rotation). */
  refreshAccessToken(): Observable<AuthResponse> {
    const refreshToken = this.storage.refreshToken;
    if (!refreshToken) {
      return throwError(() => new Error('Không có refresh token'));
    }

    return this.http
      .post<AuthResponse>(`${this.baseUrl}/refresh`, { refreshToken })
      .pipe(tap((res) => this.applySession(res)));
  }

  logout(): Observable<unknown> {
    return this.http.post(`${this.baseUrl}/logout`, {}).pipe(
      tap({
        // Dù xác thực server có thành công hay không thì client vẫn phải xoá token.
        next: () => this.clearSession(),
        error: () => this.clearSession(),
      }),
    );
  }

  /** Đồng bộ lại user từ server khi mở app (kiểm tra token còn hạn). */
  loadCurrentUser(): Observable<UserInfo> {
    return this.http.get<UserInfo>(`${this.baseUrl}/me`).pipe(
      tap((user) => {
        this.currentUser.set(user);
        this.storage.saveUser(user);
      }),
    );
  }

  clearSession(): void {
    this.storage.clear();
    this.currentUser.set(null);
  }

  private applySession(res: AuthResponse): void {
    this.storage.save(res.accessToken, res.refreshToken, res.user);
    this.currentUser.set(res.user);
  }

  /** Lấy message thân thiện từ ProblemDetails của API. */
  static problemMessage(error: unknown, fallback = 'Đã có lỗi xảy ra. Vui lòng thử lại.'): string {
    const problem = (error as { error?: ApiProblem })?.error as ApiProblem | undefined;
    return problem?.detail || problem?.title || fallback;
  }
}