// Cấu hình runtime cho bản dev (không có container sinh file này).
// Khi chạy docker/nginx, file này bị ghi đè bởi /docker-entrypoint.d/40-tryum-config.sh.
window.__TRYUM_CONFIG__ = {
  apiUrl: 'http://localhost:5111',
};