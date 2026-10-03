import { TestBed } from '@angular/core/testing';
import type { UserInfo } from '../models/auth.models';
import { TokenStorageService } from './token-storage.service';

describe('TokenStorageService', () => {
  let storage: TokenStorageService;

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

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({});
    storage = TestBed.inject(TokenStorageService);
  });

  it('trả về null khi chưa lưu gì', () => {
    expect(storage.accessToken).toBeNull();
    expect(storage.refreshToken).toBeNull();
    expect(storage.user).toBeNull();
  });

  it('lưu và đọc lại token + user', () => {
    storage.save('access-1', 'refresh-1', user);

    expect(storage.accessToken).toBe('access-1');
    expect(storage.refreshToken).toBe('refresh-1');
    expect(storage.user).toEqual(user);
  });

  it('xoá sạch phiên khi clear()', () => {
    storage.save('access-1', 'refresh-1', user);

    storage.clear();

    expect(storage.accessToken).toBeNull();
    expect(storage.user).toBeNull();
  });

  it('tự xoá phiên khi dữ liệu user bị hỏng', () => {
    localStorage.setItem('tryum.accessToken', 'access-1');
    localStorage.setItem('tryum.user', '{khong-phai-json');

    expect(storage.user).toBeNull();
    expect(storage.accessToken).toBeNull();
  });
});