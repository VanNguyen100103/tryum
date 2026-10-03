import { Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/guards/auth.guard';

/**
 * Route tree của Tryum.
 *
 * - `/`        → Landing page (public, marketing — kiểu KiotViet).
 * - `/auth/*`  → full-page (không layout), dùng cho login/register, có `guestGuard`.
 * - `/app/*`   → AppLayoutComponent (sidebar + topbar), các page con lazy-loaded, `authGuard`.
 * - `**`       → redirect về /app (dashboard).
 *
 * Mỗi page nghiệp vụ sẽ được giới hạn quyền bằng `data: { roles: [...] }` (xem AuthGuard).
 */
export const routes: Routes = [
  // ── Landing (public) ──────────────────────────────────────────────────────
  {
    path: '',
    loadComponent: () => import('./home/home.component').then((c) => c.HomeComponent),
    title: 'Tryum – Phần mềm quản lý bán hàng',
  },

  // ── Auth (full-page) ──────────────────────────────────────────────────────
  {
    path: 'auth',
    children: [
      {
        path: 'login',
        canActivate: [guestGuard],
        loadComponent: () =>
          import('./auth/login/login.component').then((c) => c.LoginComponent),
        title: 'Đăng nhập - Tryum',
      },
      {
        path: 'register',
        canActivate: [guestGuard],
        loadComponent: () =>
          import('./auth/register/register.component').then((c) => c.RegisterComponent),
        title: 'Đăng ký - Tryum',
      },
      { path: '', redirectTo: '/auth/login', pathMatch: 'full' },
    ],
  },

  // ── App (có layout, cần đăng nhập) ───────────────────────────────────────
  {
    path: 'app',
    canActivate: [authGuard],
    loadComponent: () => import('./layout/app-layout.component').then((c) => c.AppLayoutComponent),
    children: [
      { path: '', redirectTo: '/app/dashboard', pathMatch: 'full' },

      // Phase 6
      {
        path: 'dashboard',
        loadComponent: () =>
          import('./dashboard/dashboard.component').then((c) => c.DashboardComponent),
        title: 'Dashboard - Tryum',
      },

      // Phase 3
      {
        path: 'products',
        loadComponent: () =>
          import('./products/product-list/product-list.component').then(
            (c) => c.ProductListComponent
          ),
        title: 'Hàng hóa - Tryum',
      },

      // Phase 4
      {
        path: 'inventory',
        loadComponent: () =>
          import('./inventory/inventory.component').then((c) => c.InventoryComponent),
        title: 'Kho - Tryum',
      },

      // Phase 5
      {
        path: 'pos',
        loadComponent: () => import('./pos/pos.component').then((c) => c.PosComponent),
        title: 'Bán hàng - Tryum',
      },
      {
        path: 'customers',
        loadComponent: () =>
          import('./customers/customer-list/customer-list.component').then(
            (c) => c.CustomerListComponent
          ),
        title: 'Khách hàng - Tryum',
      },

      // Phase 6
      {
        path: 'reports',
        loadComponent: () =>
          import('./reports/reports.component').then((c) => c.ReportsComponent),
        title: 'Báo cáo - Tryum',
      },

      // Phase 2
      {
        path: 'settings',
        loadComponent: () =>
          import('./settings/settings.component').then((c) => c.SettingsComponent),
        title: 'Cài đặt - Tryum',
      },
    ],
  },

  { path: '**', redirectTo: '/app' },
];