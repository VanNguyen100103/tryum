import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { isDevMode } from '@angular/core';
import * as Sentry from '@sentry/angular';

// ── Sentry: bắt lỗi frontend (chỉ khi config.js có sentryDsn) ────────────────
const tryumConfig = (window as unknown as { __TRYUM_CONFIG__?: { sentryDsn?: string } })
  .__TRYUM_CONFIG__;
if (tryumConfig?.sentryDsn) {
  Sentry.init({
    dsn: tryumConfig.sentryDsn,
    environment: isDevMode() ? 'development' : 'production',
    // Chỉ gửi lỗi, không gửi performance (tiết kiệm quota free tier).
    tracesSampleRate: 0,
    sendDefaultPii: false,
    release: 'tryum-web@latest',
  });

  // Cho phép test nhanh từ console: Sentry.captureException(new Error('test'))
  (window as unknown as { Sentry: typeof Sentry }).Sentry = Sentry;
}

// PWA: đăng ký service worker (production) để cài được lên điện thoại.
if ('serviceWorker' in navigator && !isDevMode()) {
  navigator.serviceWorker.register('/sw.js').catch(() => {});
}

bootstrapApplication(App, appConfig)
  .then(() => {
    // Stagewise toolbar – chỉ chạy khi dev, không ảnh hưởng production
    if (isDevMode()) {
      import('@stagewise/toolbar').then(({ initToolbar }) => {
        initToolbar({
          plugins: [], // thêm plugin sau nếu cần (e.g. custom context)
        });
      });
    }
  })
  .catch((err) => {
    console.error(err);
    Sentry.captureException(err);
  });
