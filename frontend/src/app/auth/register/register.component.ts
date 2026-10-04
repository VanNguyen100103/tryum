import { Component, DestroyRef, ElementRef, inject, signal, ViewChildren, QueryList } from '@angular/core';
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

type Step = 'form' | 'otp';

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
  private readonly destroyRef = inject(DestroyRef);

  ownerName = '';
  shopName = '';
  email = '';
  phone = '';
  password = '';
  captchaInput = '';
  agreeTos = false;

  protected readonly step = signal<Step>('form');
  protected readonly loading = signal(false);
  protected readonly sendingOtp = signal(false);
  protected readonly showPromo = signal(true);
  protected readonly captchaChars = signal<CaptchaChar[]>([]);
  protected readonly otpDigits = signal<string[]>(Array(6).fill(''));
  protected readonly resendIn = signal(0);
  protected readonly sentPhone = signal('');
  protected readonly demoOtp = signal('');

  @ViewChildren('otpBox') private otpBoxes!: QueryList<ElementRef<HTMLInputElement>>;

  private resendTimer: ReturnType<typeof setInterval> | null = null;

  constructor() {
    this.refreshCaptcha();
    this.destroyRef.onDestroy(() => this.clearResendTimer());
  }

  protected isOtpComplete(): boolean {
    return this.otpDigits().every((d) => d !== '');
  }

  protected otpCode(): string {
    return this.otpDigits().join('');
  }

  private focusBox(i: number): void {
    this.otpBoxes.get(i)?.nativeElement.focus();
  }

  protected onDigitInput(i: number, raw: string | null): void {
    const d = (raw ?? '').replace(/\D/g, '').slice(-1);
    const arr = [...this.otpDigits()];
    arr[i] = d;
    this.otpDigits.set(arr);

    if (d && i < 5) {
      this.focusBox(i + 1);
    } else if (d && i === 5) {
      document.getElementById('otp-submit')?.focus();
    }
  }

  protected onDigitKeydown(i: number, e: KeyboardEvent): void {
    if (e.key === 'Backspace' && this.otpDigits()[i] === '' && i > 0) {
      e.preventDefault();
      const arr = [...this.otpDigits()];
      arr[i - 1] = '';
      this.otpDigits.set(arr);
      this.focusBox(i - 1);
    } else if (e.key === 'ArrowLeft' && i > 0) {
      e.preventDefault();
      this.focusBox(i - 1);
    } else if (e.key === 'ArrowRight' && i < 5) {
      e.preventDefault();
      this.focusBox(i + 1);
    }
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

  /** Bước 1: validate form + captcha rồi gọi /auth/otp/send. */
  sendOtpCode(): void {
    if (this.sendingOtp() || this.loading()) {
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

    // Prefix +84 chỉ visual → user nhập 9 số; chuẩn hóa thành 0xxxxxxxxx trước khi validate.
    const digits = this.phone.replace(/\D/g, '');
    const phoneForApi = digits.length === 9 ? `0${digits}` : digits;
    if (!/^(0\d{9}|84\d{9})$/.test(phoneForApi)) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Số điện thoại chưa đúng',
        detail: 'Vui lòng nhập SĐT di động VN (vd: 0901234567 hoặc 901234567).',
      });
      return;
    }
    this.phone = phoneForApi;

    if (!this.agreeTos) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Chưa đồng ý điều khoản',
        detail: 'Vui lòng đồng ý điều khoản sử dụng dịch vụ & chính sách bảo mật.',
      });
      return;
    }

    this.sendingOtp.set(true);

    this.auth.sendOtp(this.phone).subscribe({
      next: (res) => {
        this.sendingOtp.set(false);
        this.sentPhone.set(this.formatPhone(this.phone));
        this.demoOtp.set(res.demoOtp ?? '');
        this.otpDigits.set(Array(6).fill(''));
        this.step.set('otp');
        this.startResendTimer(res.resendAfterSeconds);
        this.messageService.add({
          severity: 'info',
          summary: 'Đã gửi mã OTP',
          detail: res.demoOtp
            ? `Chế độ demo: mã OTP của bạn là ${res.demoOtp} (production sẽ nhận qua SMS).`
            : 'Vui lòng kiểm tra tin nhắn SMS.',
        });
        setTimeout(() => this.focusBox(0), 60);
      },
      error: (error: unknown) => {
        this.sendingOtp.set(false);
        this.messageService.add({
          severity: 'error',
          summary: 'Không gửi được mã OTP',
          detail: AuthService.problemMessage(error),
        });
      },
    });
  }

  /** Gửi lại mã OTP (đang ở bước 2). */
  resendOtp(): void {
    if (this.sendingOtp() || this.resendIn() > 0) {
      return;
    }

    this.sendingOtp.set(true);
    this.auth.sendOtp(this.phone).subscribe({
      next: (res) => {
        this.sendingOtp.set(false);
        this.demoOtp.set(res.demoOtp ?? '');
        this.otpDigits.set(Array(6).fill(''));
        this.startResendTimer(res.resendAfterSeconds);
        setTimeout(() => this.focusBox(0), 60);
      },
      error: (error: unknown) => {
        this.sendingOtp.set(false);
        this.messageService.add({
          severity: 'error',
          summary: 'Không gửi lại được',
          detail: AuthService.problemMessage(error),
        });
      },
    });
  }

  /** Bước 2: xác nhận OTP + tạo tài khoản. */
  register(): void {
    if (this.loading() || !this.isOtpComplete()) {
      return;
    }

    this.loading.set(true);

    this.auth
      .register({
        shopName: this.shopName.trim(),
        ownerName: this.ownerName.trim(),
        email: this.email.trim(),
        password: this.password,
        phone: this.phone,
        otpCode: this.otpCode(),
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
          this.otpDigits.set(Array(6).fill(''));
          this.startResendTimer(0);
          this.messageService.add({
            severity: 'error',
            summary: 'Đăng ký thất bại',
            detail: AuthService.problemMessage(error),
          });
          this.focusBox(0);
        },
      });
  }

  private startResendTimer(seconds: number): void {
    this.clearResendTimer();
    this.resendIn.set(Math.max(0, seconds));

    if (seconds <= 0) {
      return;
    }

    this.resendTimer = setInterval(() => {
      this.resendIn.update((s) => (s > 0 ? s - 1 : 0));
      if (this.resendIn() === 0) {
        this.clearResendTimer();
      }
    }, 1000);
  }

  private clearResendTimer(): void {
    if (this.resendTimer !== null) {
      clearInterval(this.resendTimer);
      this.resendTimer = null;
    }
  }

  private formatPhone(raw: string): string {
    let digits = raw.replace(/\D/g, '');
    if (digits.length === 11 && digits.startsWith('84')) digits = digits.slice(2);
    if (digits.length === 10 && digits.startsWith('0')) digits = digits.slice(1);
    return digits.length === 9
      ? `0${digits.slice(0, 3)}.${digits.slice(3, 6)}.${digits.slice(6)}`
      : raw;
  }
}
