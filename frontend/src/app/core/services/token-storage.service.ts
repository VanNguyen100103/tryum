import { Injectable } from '@angular/core';
import type { UserInfo } from '../models/auth.models';

const ACCESS_TOKEN_KEY = 'tryum.accessToken';
const REFRESH_TOKEN_KEY = 'tryum.refreshToken';
const USER_KEY = 'tryum.user';

/**
 * Lưu token & thông tin user ở localStorage.
 * Tách riêng khỏi AuthService để interceptor có thể đọc token mà không tạo phụ thuộc vòng.
 */
@Injectable({ providedIn: 'root' })
export class TokenStorageService {
  get accessToken(): string | null {
    return localStorage.getItem(ACCESS_TOKEN_KEY);
  }

  get refreshToken(): string | null {
    return localStorage.getItem(REFRESH_TOKEN_KEY);
  }

  get user(): UserInfo | null {
    const raw = localStorage.getItem(USER_KEY);
    if (!raw) {
      return null;
    }

    try {
      return JSON.parse(raw) as UserInfo;
    } catch {
      this.clear();
      return null;
    }
  }

  save(accessToken: string, refreshToken: string, user: UserInfo): void {
    localStorage.setItem(ACCESS_TOKEN_KEY, accessToken);
    localStorage.setItem(REFRESH_TOKEN_KEY, refreshToken);
    this.saveUser(user);
  }

  saveUser(user: UserInfo): void {
    localStorage.setItem(USER_KEY, JSON.stringify(user));
  }

  clear(): void {
    localStorage.removeItem(ACCESS_TOKEN_KEY);
    localStorage.removeItem(REFRESH_TOKEN_KEY);
    localStorage.removeItem(USER_KEY);
  }
}