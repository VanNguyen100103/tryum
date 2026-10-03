# ── Build stage ──────────────────────────────────────────────────────────────
FROM node:24-alpine AS build
WORKDIR /app

COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci

COPY frontend/ ./
RUN npm run build

# ── Runtime stage: nginx serve static + SPA fallback ─────────────────────────
FROM nginx:1.27-alpine AS runtime

# API_URL được ghi vào config.js lúc start container (xem docker-entrypoint.sh)
# nên cùng một image dùng được cho nhiều môi trường.
ENV API_URL=http://localhost:8080

COPY deploy/nginx/default.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/tryum-frontend/browser /usr/share/nginx/html
COPY frontend/docker-entrypoint.sh /docker-entrypoint.d/40-tryum-config.sh

RUN chmod +x /docker-entrypoint.d/40-tryum-config.sh

EXPOSE 80