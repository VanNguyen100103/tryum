import { Injectable, signal } from '@angular/core';

export type ToastSeverity = 'success' | 'info' | 'warn' | 'error';
export interface ToastMessage {
  severity: ToastSeverity;
  summary: string;
  detail?: string;
}

@Injectable({ providedIn: 'root' })
export class ToastService {
  readonly message = signal<ToastMessage | null>(null);
  private timer: ReturnType<typeof setTimeout> | null = null;

  add(message: ToastMessage): void {
    this.message.set(message);
    if (this.timer) clearTimeout(this.timer);
    this.timer = setTimeout(() => this.clear(), 5000);
  }

  clear(): void {
    this.message.set(null);
    if (this.timer) clearTimeout(this.timer);
    this.timer = null;
  }
}
