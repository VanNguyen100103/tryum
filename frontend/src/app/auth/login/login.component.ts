import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ToastService } from '../../core/services/toast.service';
import { AuthService } from '../../core/services/auth.service';
import type { StoreLookupResponse } from '../../core/models/auth.models';

@Component({
  selector: 'app-login',
  imports: [FormsModule, RouterLink],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly messageService = inject(ToastService);

  /** Miền cửa hàng được xác thực trước khi cho phép nhập thông tin đăng nhập. */
  storeSlug = '';
  identifier = '';
  password = '';

  protected readonly loading = signal(false);
  protected readonly resolvingStore = signal(false);
  protected readonly credentialsStep = signal(false);
  protected readonly selectedStore = signal<StoreLookupResponse | null>(null);

  resolveStore(): void {
    if (this.resolvingStore()) return;

    const slug = this.storeSlug
      .trim()
      .toLowerCase()
      .replace(/^https?:\/\//, '')
      .replace(/\/+$/, '')
      .replace(/\.tryum\.app$/, '');

    if (!/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(slug) || slug.length > 63) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Miền cửa hàng chưa đúng',
        detail: 'Nhập phần tên cửa hàng trước .tryum.app, ví dụ: ten-cua-hang.',
      });
      return;
    }

    this.resolvingStore.set(true);
    this.auth.resolveStore(slug).subscribe({
      next: (store) => {
        this.selectedStore.set(store);
        this.storeSlug = store.slug;
        this.resolvingStore.set(false);
        this.credentialsStep.set(true);
      },
      error: (error: unknown) => {
        this.resolvingStore.set(false);
        this.messageService.add({
          severity: 'error',
          summary: 'Không tìm thấy cửa hàng',
          detail: AuthService.problemMessage(error, 'Kiểm tra lại miền cửa hàng rồi thử lại.'),
        });
      },
    });
  }

  backToStore(): void {
    this.credentialsStep.set(false);
    this.selectedStore.set(null);
    this.password = '';
  }

  login(): void {
    if (this.loading()) {
      return;
    }

    if (!this.selectedStore() || !this.identifier.trim() || !this.password) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Thiếu thông tin',
        detail: 'Vui lòng nhập số điện thoại (hoặc email) và mật khẩu.',
      });
      return;
    }

    this.loading.set(true);

    this.auth
      .login({
        tenantSlug: this.selectedStore()!.slug,
        identifier: this.identifier.trim(),
        password: this.password,
      })
      .subscribe({
        next: (res) => {
          this.loading.set(false);
          this.messageService.add({
            severity: 'success',
            summary: `Chào ${res.user.fullName}`,
            detail: `Cửa hàng ${res.user.tenantName}`,
          });
          void this.router.navigate(['/app']);
        },
        error: (error: unknown) => {
          this.loading.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Đăng nhập thất bại',
            detail: AuthService.problemMessage(error, 'Email hoặc mật khẩu không đúng.'),
          });
        },
      });
  }
}
