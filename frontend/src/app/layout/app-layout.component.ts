import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { IconFieldModule } from 'primeng/iconfield';
import { AuthService } from '../core/services/auth.service';

interface NavItem {
  label: string;
  icon: string;
  route: string;
  badge?: number;
}

/** Menu điều hướng chính. Thêm mục mới khi mở các phase tiếp theo. */
const NAV: NavItem[] = [
  { label: 'Dashboard', icon: 'pi pi-chart-line', route: '/app/dashboard' },
  { label: 'Hàng hóa', icon: 'pi pi-box', route: '/app/products' },
  { label: 'Kho', icon: 'pi pi-warehouse', route: '/app/inventory' },
  { label: 'Bán hàng', icon: 'pi pi-shopping-cart', route: '/app/pos' },
  { label: 'Khách hàng', icon: 'pi pi-users', route: '/app/customers' },
  { label: 'Báo cáo', icon: 'pi pi-chart-bar', route: '/app/reports' },
  { label: 'Cài đặt', icon: 'pi pi-cog', route: '/app/settings' },
];

@Component({
  selector: 'app-layout',
  imports: [
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    ButtonModule,
    InputTextModule,
    IconFieldModule,
  ],
  templateUrl: './app-layout.component.html',
  styleUrl: './app-layout.component.scss',
})
export class AppLayoutComponent {
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);

  protected readonly navItems = NAV;
  protected readonly sidebarOpen = signal(true);

  protected readonly user = this.auth.user;
  protected readonly initials = this.auth.initials;
  protected readonly shopName = computed(() => this.auth.user()?.tenantName ?? '');

  protected isActive(route: string): boolean {
    const url = this.router.url;
    return url === route || url.startsWith(route + '/');
  }

  protected toggleSidebar(): void {
    this.sidebarOpen.update((v) => !v);
  }

  protected logout(): void {
    this.auth.logout().subscribe({
      next: () => void this.router.navigate(['/auth/login']),
      error: () => {
        this.auth.clearSession();
        void this.router.navigate(['/auth/login']);
      },
    });
  }
}
