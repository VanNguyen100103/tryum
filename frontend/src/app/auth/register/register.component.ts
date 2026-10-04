import { Component, DestroyRef, ElementRef, inject, signal, ViewChildren, QueryList } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { InputTextModule } from 'primeng/inputtext';
import { PasswordModule } from 'primeng/password';
import { MessageService } from 'primeng/api';
import { AuthService } from '../../core/services/auth.service';

type Step = 'form' | 'otp';

@Component({
  selector: 'app-register',
  imports: [InputTextModule, PasswordModule, FormsModule, RouterLink],
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
  protected readonly otpDigits = signal<string[]>(Array(6).fill(''));
  protected readonly resendIn = signal(0);
  protected readonly sentPhone = signal('');
  protected readonly demoOtp = signal('');

  // Captcha do server sinh (SVG data-uri). Đây là điều kiện bắt buộc để API
  // chịu gửi SMS — không có nó thì một bot có thể đốt hết quota gateway.
  protected readonly captchaId = signal('');
  protected readonly captchaImage = signal('');
  protected readonly otpTtlMinutes = signal(5);

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

  /**
   * Lấy ảnh captcha mới từ server. Mỗi lần gọi trả về captchaId mới và ảnh SVG,
   * nên nút "refresh" và lúc báo sai captcha đều gọi lại hàm này.
   */
  refreshCaptcha(): void {
    this.auth.getCaptcha().subscribe({
      next: (res) => {
        this.captchaId.set(res.captchaId);
        this.captchaImage.set(res.imageDataUrl);
        this.captchaInput = '';
      },
      error: () => {
        this.captchaId.set('');
        this.captchaImage.set('');
        this.messageService.add({
          severity: 'warn',
          summary: 'Không tải được mã xác thực',
          detail: 'Vui lòng kiểm tra kết nối rồi bấm biểu tượng làm mới.',
        });
      },
    });
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

    // Captcha được kiểm ở SERVER (xem /auth/otp/send). Ở đây chỉ kiểm "đã nhập chưa"
// để không gửi request vô ích; sai mã thì API trả captcha_invalid và ta tải ảnh mới.
    if (this.captchaInput.trim().length !== 4) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Thiếu mã xác thực',
        detail: 'Vui lòng nhập đúng 4 ký tự mã hiển thị trong ảnh.',
      });
      return;
    }

    const phoneForApi = this.normalizePhone(this.phone);
    if (!phoneForApi) {
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

    this.auth.sendOtp({
      phone: this.phone,
      captchaId: this.captchaId(),
      captchaCode: this.captchaInput.trim(),
    }).subscribe({
      next: (res) => {
        this.sendingOtp.set(false);
        this.sentPhone.set(this.formatPhone(this.phone));
        this.demoOtp.set(res.demoOtp ?? '');
        this.otpTtlMinutes.set(Math.max(1, Math.round(res.expiresInSeconds / 60)));
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

        // Mỗi captcha chỉ dùng 1 lần nên luôn tải ảnh mới cho lần nhập sau.
        this.refreshCaptcha();

        this.messageService.add({
          severity: 'error',
          summary: 'Không gửi được mã OTP',
          detail: AuthService.problemMessage(error),
        });
      },
    });
  }

  /**
   * Gửi lại mã OTP (đang ở bước 2).
   *
   * Không cần nhập lại captcha: người dùng đã vượt captcha ở lần gửi đầu và đang chủ động
   * bấm nút, đồng thời vẫn bị giới hạn bởi cooldown 30s + quota theo ngày ở server.
   */
  resendOtp(): void {
    if (this.sendingOtp() || this.resendIn() > 0) {
      return;
    }

    this.sendingOtp.set(true);

    this.auth
      .sendOtp({ phone: this.phone, isResend: true })
      .subscribe({
        next: (res) => {
          this.sendingOtp.set(false);
          this.demoOtp.set(res.demoOtp ?? '');
          this.otpTtlMinutes.set(Math.max(1, Math.round(res.expiresInSeconds / 60)));
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

  /**
   * Chuẩn hoá SĐT về dạng 0xxxxxxxxx trước khi gửi API.
   * Chấp nhận: 9 số (901234567), 0 + 9 số, 84..., +84... — trả về chuỗi rỗng nếu sai.
   */
  private normalizePhone(raw: string): string {
    let digits = raw.replace(/\D/g, '');

    if (digits.startsWith('84') && (digits.length === 11 || digits.length === 12)) {
      digits = digits.slice(2);
    } else if (digits.startsWith('0') && digits.length === 10) {
      digits = digits.slice(1);
    } else if (digits.length === 9) {
      return `0${digits}`;
    }

    return /^0\d{9}$/.test(digits) ? digits : '';
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
