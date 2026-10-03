import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideAnimations } from '@angular/platform-browser/animations';
import { providePrimeNG } from 'primeng/config';
import { MessageService } from 'primeng/api';
import Aura from '@primeuix/themes/aura';
import { authInterceptor } from './core/interceptors/auth.interceptor';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(
      routes,
      withComponentInputBinding(),
      withInMemoryScrolling({ scrollPositionRestoration: 'top' })
    ),
    provideHttpClient(withInterceptors([authInterceptor])),
    provideAnimations(),
    providePrimeNG({
      theme: {
        preset: Aura,
        options: {
          darkModeSelector: '.dark', // bật/tắt dark mode bằng class .dark trên <html>
          // Thương hiệu Tryum: cam kiểu KiotViet (#ff4e00)
          primary: {
            colorScheme: {
              50: '#fff5ef',
              100: '#ffe8db',
              200: '#ffccb3',
              300: '#ffa480',
              400: '#ff7a4d',
              500: '#ff4e00',
              600: '#f04300',
              700: '#d63a00',
              800: '#b32f00',
              900: '#8f2600',
              950: '#5c1800',
            },
          },
        },
      },
      ripple: true,
    }),
    MessageService,
  ],
};