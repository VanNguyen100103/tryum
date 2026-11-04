import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SalesService, SePaySettings } from '../core/services/sales.service';
import { ToastService } from '../core/services/toast.service';

@Component({
  selector: 'app-settings',
  imports: [FormsModule],
  templateUrl: './settings.component.html',
  styleUrl: './settings.component.scss',
})
export class SettingsComponent implements OnInit {
  private readonly sales = inject(SalesService);
  private readonly toast = inject(ToastService);

  readonly settings = signal<SePaySettings | null>(null);
  readonly loading = signal(true);
  readonly error = signal('');

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set('');
    this.sales.sePaySettings().subscribe({
      next: (settings) => {
        this.settings.set(settings);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.error.set('Không tải được cấu hình thanh toán. Hãy đăng nhập lại hoặc kiểm tra API.');
      },
    });
  }

  copyWebhook(url: string): void {
    void navigator.clipboard
      ?.writeText(url)
      .then(() =>
        this.toast.add({
          severity: 'success',
          summary: 'Đã copy URL webhook',
          detail: 'Dán vào mục Webhooks của SePay.',
        }),
      )
      .catch(() =>
        this.toast.add({ severity: 'warn', summary: 'Trình duyệt không cho copy', detail: url }),
      );
  }
}
