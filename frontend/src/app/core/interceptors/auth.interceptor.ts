import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

/**
 * Interceptor xác thực:
 * 1. Gắn `Authorization: Bearer <accessToken>` cho mọi request (trừ endpoint auth).
 * 2. Khi API trả 401, thử refresh token 1 lần rồi gửi lại request cũ.
 * 3. Refresh thất bại => xoá phiên và chuyển về trang đăng nhập.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const isAuthEndpoint = req.url.includes('/api/v1/auth');
  const token = auth.accessToken();
  const request = token && !isAuthEndpoint
    ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : req;

  return next(request).pipe(
    catchError((error: unknown) => {
      const isUnauthorized = error instanceof HttpErrorResponse && error.status === 401;

      if (!isUnauthorized || isAuthEndpoint || !auth.refreshToken()) {
        return throwError(() => error);
      }

      return auth.refreshAccessToken().pipe(
        switchMap((res) =>
          next(req.clone({ setHeaders: { Authorization: `Bearer ${res.accessToken}` } })),
        ),
        catchError((refreshError: unknown) => {
          auth.clearSession();
          void router.navigate(['/auth/login']);
          return throwError(() => refreshError);
        }),
      );
    }),
  );
};