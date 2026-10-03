import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  RouterStateSnapshot,
  provideRouter,
} from '@angular/router';
import type { UserInfo } from '../models/auth.models';
import { AuthService } from '../services/auth.service';
import { authGuard, guestGuard } from './auth.guard';

describe('auth guards', () => {
  const user: UserInfo = {
    id: 'u1',
    fullName: 'Nguyễn Văn A',
    email: 'a@shop.vn',
    phone: null,
    tenantId: 't1',
    tenantName: 'Phở Thảo',
    roleCode: 'Owner',
    roleName: 'Chủ cửa hàng',
  };

  const route = {} as ActivatedRouteSnapshot;
  const state = { url: '/dashboard' } as RouterStateSnapshot;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideRouter([]), AuthService] });
  });

  function seedSession(): void {
    localStorage.setItem('tryum.accessToken', 'access-token');
    localStorage.setItem('tryum.refreshToken', 'refresh-token');
    localStorage.setItem('tryum.user', JSON.stringify(user));
  }

  it('authGuard chuyển hướng khách về /auth/login khi chưa đăng nhập', () => {
    const result = TestBed.runInInjectionContext(() => authGuard(route, state));

    expect(String(result)).toContain('/auth/login');
  });

  it('authGuard cho phép đi tiếp khi đã đăng nhập', () => {
    seedSession();

    const result = TestBed.runInInjectionContext(() => authGuard(route, state));

    expect(result).toBe(true);
  });

  it('guestGuard chuyển hướng người đã đăng nhập về /app', () => {
    seedSession();

    const result = TestBed.runInInjectionContext(() => guestGuard(route, state));

    expect(String(result)).toContain('/app');
  });

  it('guestGuard cho phép khách xem trang đăng nhập', () => {
    const result = TestBed.runInInjectionContext(() => guestGuard(route, state));

    expect(result).toBe(true);
  });
});