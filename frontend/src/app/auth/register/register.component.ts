import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { PasswordModule } from 'primeng/password';
import { CardModule } from 'primeng/card';
import { MessageService } from 'primeng/api';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-register',
  imports: [
    CardModule,
    ButtonModule,
    InputTextModule,
    PasswordModule,
    FormsModule,
    RouterLink,
  ],
  templateUrl: './register.component.html',
  styleUrl: './register.component.scss',
})
export class RegisterComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly messageService = inject(MessageService);

  shopName = '';
  ownerName = '';
  email = '';
  password = '';
  phone = '';

  protected readonly loading = signal(false);

  register(): void {
    if (this.loading()) {
      return;
    }

    if (!this.shopName.trim() || !this.ownerName.trim() || !this.email.trim() || !this.password) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Thiếu thông tin',
        detail: 'Vui lòng điền đủ tên cửa hàng, họ tên, email và mật khẩu.',
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
        },
      });
  }
}