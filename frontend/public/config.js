// Cấu hình runtime cho bản static trên Cloudflare Workers.
// Docker sẽ ghi đè file này lúc container start bằng biến API_URL (xem docker-entrypoint.sh).
// sentryDsn là PUBLIC (DSN client luôn công khai) nên commit được.
window.__TRYUM_CONFIG__ = {
  apiUrl: 'https://tryum-api.duckdns.org',
  sentryDsn:
    'https://264c7ff983ab909d711c05516c8df100@o4512506436124672.ingest.de.sentry.io/4512506586464336',
};
