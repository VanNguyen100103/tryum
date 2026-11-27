# ── Build stage ──────────────────────────────────────────────────────────────
FROM node:24-alpine AS build
WORKDIR /app

COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci

COPY frontend/ ./
RUN npm run build

# ── Runtime stage: nginx serve static + SPA fallback (chạy non-root) ─────────
FROM nginx:1.27-alpine AS runtime

# API_URL được ghi vào config.js lúc start container (xem docker-entrypoint.sh)
# nên cùng một image dùng được cho nhiều môi trường.
ENV API_URL=http://localhost:8080

COPY deploy/nginx/default.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/tryum-frontend/browser /usr/share/nginx/html
COPY frontend/docker-entrypoint.sh /docker-entrypoint.d/40-tryum-config.sh

# Bảo mật (Trivy DS-0002): chạy nginx bằng user "nginx" thay vì root.
# - setcap cap_net_bind_service để vẫn bind được cổng 80 khi không phải root
# - chown các thư mục nginx cần ghi (cache, pid, log) + html (để ghi config.js)
RUN apk add --no-cache libcap \
    && setcap 'cap_net_bind_service=+ep' /usr/sbin/nginx \
    && chmod +x /docker-entrypoint.d/40-tryum-config.sh \
    && chown -R nginx:nginx /usr/share/nginx/html /var/cache/nginx /var/run /var/log/nginx

USER nginx

EXPOSE 80
