import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { PasswordModule } from 'primeng/password';
import { CheckboxModule } from 'primeng/checkbox';
import { CardModule } from 'primeng/card';
import { MessageService } from 'primeng/api';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-login',
  imports: [
    CardModule,
    ButtonModule,
    InputTextModule,
    PasswordModule,
    CheckboxModule,
    FormsModule,
    RouterLink,
  ],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly messageService = inject(MessageService);

  email = '';
  password = '';
  remember = false;

  protected readonly loading = signal(false);

  login(): void {
    if (this.loading()) {
      return;
    }

    if (!this.email.trim() || !this.password) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Thiếu thông tin',
        detail: 'Vui lòng nhập email và mật khẩu.',
      });
      return;
    }

    this.loading.set(true);

    this.auth
      .login({ email: this.email.trim(), password: this.password })
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