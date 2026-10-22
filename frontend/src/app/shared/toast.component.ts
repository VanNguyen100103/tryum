import { Component, inject } from '@angular/core';
import { ToastService } from '../core/services/toast.service';

@Component({
  selector: 'app-toast',
  standalone: true,
  template: `
    @if (toast.message(); as message) {
      <div class="toast" [class]="'toast toast--' + message.severity" role="status" aria-live="polite">
        <span class="toast__mark" aria-hidden="true">{{ message.severity === 'success' ? '✓' : message.severity === 'error' ? '!' : 'i' }}</span>
        <span class="toast__copy"><strong>{{ message.summary }}</strong>@if (message.detail) { <span>{{ message.detail }}</span> }</span>
        <button type="button" aria-label="Đóng thông báo" (click)="toast.clear()">×</button>
      </div>
    }
  `,
  styles: [`
    :host { position: fixed; z-index: 1000; top: 18px; right: 18px; width: min(390px, calc(100vw - 36px)); }
    .toast { display:flex; align-items:flex-start; gap:12px; padding:15px 16px; border:1px solid #dce5f1; border-radius:12px; background:#fff; color:#1f2b3d; box-shadow:0 12px 34px #10233b24; font:14px/1.45 system-ui,sans-serif; }
    .toast__mark { display:grid; place-items:center; flex:none; width:24px; height:24px; border-radius:50%; background:#e8f1ff; color:#1769db; font-weight:800; }
    .toast--success .toast__mark { background:#e7f8ef; color:#17884c; }
    .toast--warn .toast__mark { background:#fff4dc; color:#a76700; }
    .toast--error .toast__mark { background:#ffebeb; color:#bd2a2a; }
    .toast__copy { display:grid; gap:3px; flex:1; }.toast__copy span { color:#65748a; }
    button { border:0; background:transparent; color:#8491a3; cursor:pointer; font-size:20px; line-height:1; }
  `],
})
export class ToastComponent {
  protected readonly toast = inject(ToastService);
}
