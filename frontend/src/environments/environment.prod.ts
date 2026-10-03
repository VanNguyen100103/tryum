/**
 * Cấu hình runtime – được ghi đè bởi `/config.js` sinh lúc khởi động container
 * (xem frontend/docker-entrypoint.sh) nên cùng một image dùng được cho mọi môi trường.
 */
declare global {
  interface Window {
    __TRYUM_CONFIG__?: { apiUrl?: string };
  }
}

export interface AppEnvironment {
  production: boolean;
  apiUrl: string;
}

const DEFAULT_API_URL = 'http://localhost:8080';

export const environment: AppEnvironment = {
  production: true,
  apiUrl: window.__TRYUM_CONFIG__?.apiUrl?.replace(/\/$/, '') ?? DEFAULT_API_URL,
};