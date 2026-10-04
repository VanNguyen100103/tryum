import { Component, inject, signal } from '@angular/core';
import { NgStyle } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { InputTextModule } from 'primeng/inputtext';
import { PasswordModule } from 'primeng/password';
import { MessageService } from 'primeng/api';
import { AuthService } from '../../core/services/auth.service';

interface CaptchaChar {
  ch: string;
  rot: number;
  dy: number;
}

@Component({
  selector: 'app-register',
  imports: [InputTextModule, PasswordModule, FormsModule, RouterLink, NgStyle],
  templateUrl: './register.component.html',
  styleUrl: './register.component.scss',
})
export class RegisterComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly messageService = inject(MessageService);

  ownerName = '';
  shopName = '';
  email = '';
  phone = '';
  password = '';
  captchaInput = '';
  agreeTos = false;

  protected readonly loading = signal(false);
  protected readonly showPromo = signal(true);
  protected readonly captchaChars = signal<CaptchaChar[]>([]);

  constructor() {
    this.refreshCaptcha();
  }

  refreshCaptcha(): void {
    const safe = '23456789ABCDEFGHJKMNPQRSTUVWXYZ';
    this.captchaChars.set(
      Array.from({ length: 4 }, () => ({
        ch: safe[Math.floor(Math.random() * safe.length)],
        rot: Math.round((Math.random() * 24 - 12) * 10) / 10,
        dy: Math.round((Math.random() * 6 - 3) * 10) / 10,
      }))
    );
    this.captchaInput = '';
  }

  register(): void {
    if (this.loading()) {
      return;
    }

    if (!this.ownerName.trim() || !this.shopName.trim() || !this.email.trim() || !this.password) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Thiếu thông tin',
        detail: 'Vui lòng điền đủ họ tên, tên cửa hàng, email và mật khẩu.',
      });
      return;
    }

    const code = this.captchaChars().map((c) => c.ch).join('');
    if (this.captchaInput.trim().toUpperCase() !== code) {
      this.messageService.add({
        severity: 'error',
        summary: 'Mã xác thực sai',
        detail: 'Vui lòng nhập đúng mã hiển thị.',
      });
      this.refreshCaptcha();
      return;
    }

    if (!this.agreeTos) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Chưa đồng ý điều khoản',
        detail: 'Vui lòng đồng ý điều khoản sử dụng dịch vụ & chính sách bảo mật.',
      });
      return;
    }

    this.loading.set(true);

    this.auth
      .register({
        shopName: this.shopName.trim(),
        ownerName: this.ownerName.trim(),
        email: this.email.trim(),
        password: this.password,
        phone: this.phone.trim() || null,
      })
      .subscribe({
        next: (res) => {
          this.loading.set(false);
          this.messageService.add({
            severity: 'success',
            summary: 'Đăng ký thành công',
            detail: `Đã tạo cửa hàng ${res.user.tenantName}.`,
          });
          void this.router.navigate(['/app']);
        },
        error: (error: unknown) => {
          this.loading.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Đăng ký thất bại',
            detail: AuthService.problemMessage(error),
          });
          this.refreshCaptcha();
        },
      });
  }
}
