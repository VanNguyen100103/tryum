#!/bin/sh
# Sinh config.js từ biến môi trường API_URL để 1 image dùng được cho mọi môi trường
# (dev, staging, production) mà không cần build lại frontend.
set -eu

CONFIG_FILE=/usr/share/nginx/html/config.js

cat > "$CONFIG_FILE" <<EOF
window.__TRYUM_CONFIG__ = {
  apiUrl: "${API_URL}"
};
EOF

echo "[tryum] config.js: API_URL=${API_URL}"