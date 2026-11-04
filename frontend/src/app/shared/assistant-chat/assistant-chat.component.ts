import { HttpClient } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../core/services/auth.service';

interface ChatMessage {
  role: 'user' | 'assistant';
  text: string;
}

/**
 * Trợ lý AI dùng chung cho trang chủ và khu vực quản trị.
 * Backend tự fallback Groq -> Gemini nên component chỉ cần gọi một endpoint.
 */
@Component({
  selector: 'app-assistant-chat',
  imports: [FormsModule],
  template: `
    @if (open()) {
      <section class="ai-chat" aria-label="Trợ lý Tryum">
        <header class="ai-chat__head">
          <div>
            <strong>Trợ lý Tryum</strong>
            <span>Hỗ trợ vận hành · trả lời 24/7</span>
          </div>
          <button type="button" (click)="toggle()" aria-label="Đóng chat">×</button>
        </header>

        <div class="ai-chat__body">
          @for (message of messages(); track $index) {
            <div class="ai-chat__msg" [class.is-user]="message.role === 'user'">
              <p>{{ format(message.text) }}</p>
            </div>
          }
          @if (loading()) {
            <div class="ai-chat__msg is-bot is-typing"><span></span><span></span><span></span></div>
          }
        </div>

        @if (messages().length <= 1) {
          <div class="ai-chat__suggestions">
            @for (suggestion of suggestions; track suggestion) {
              <button type="button" (click)="send(suggestion)">{{ suggestion }}</button>
            }
          </div>
        }

        <div class="ai-chat__input">
          <input
            name="assistantChatInput"
            [ngModel]="input()"
            (ngModelChange)="input.set($event)"
            (keyup.enter)="send()"
            placeholder="Nhập câu hỏi..."
            maxlength="2000"
            autocomplete="off"
          />
          <button type="button" (click)="send()" [disabled]="loading() || !input().trim()">Gửi</button>
        </div>
      </section>
    }

    <button type="button" class="ai-chat-fab" (click)="toggle()" [attr.aria-label]="open() ? 'Đóng trợ lý' : 'Mở trợ lý'">
      <i class="pi" [class.pi-comments]="!open()" [class.pi-times]="open()"></i>
    </button>
  `,
  styles: [`
    .ai-chat {
      position: fixed;
      right: 26px;
      bottom: 96px;
      z-index: 90;
      display: flex;
      flex-direction: column;
      width: min(380px, calc(100vw - 32px));
      height: min(560px, calc(100vh - 130px));
      overflow: hidden;
      border: 1px solid #e1e8f2;
      border-radius: 16px;
      background: #fff;
      box-shadow: 0 24px 60px rgba(15, 35, 65, 0.22);
    }
    .ai-chat__head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 12px;
      padding: 15px 17px;
      background: linear-gradient(135deg, #1a73ff, #075bd8);
      color: #fff;
    }
    .ai-chat__head div { display: flex; flex-direction: column; gap: 2px; }
    .ai-chat__head strong { font-size: 14px; font-weight: 800; }
    .ai-chat__head span { color: #dce9ff; font-size: 10.5px; }
    .ai-chat__head button {
      border: 0; background: transparent; color: #fff; font-size: 20px; line-height: 1; cursor: pointer;
    }
    .ai-chat__body {
      flex: 1; display: flex; flex-direction: column; gap: 9px; padding: 14px;
      overflow-y: auto; background: #fbfcff;
    }
    .ai-chat__msg {
      display: flex; max-width: 86%; padding: 9px 12px;
      border-radius: 12px 12px 12px 3px;
      background: #fff; border: 1px solid #e7edf6;
      color: #33465d; font-size: 11.5px; line-height: 1.55; white-space: pre-wrap;
    }
    .ai-chat__msg.is-user {
      align-self: flex-end; border-color: #cfdef8;
      border-radius: 12px 12px 3px 12px; background: #eaf2ff; color: #1e3f76;
    }
    .ai-chat__msg.is-typing { align-items: center; gap: 4px; width: 54px; }
    .ai-chat__msg.is-typing span {
      width: 5px; height: 5px; border-radius: 50%; background: #8ba5c7;
      animation: ai-bounce 1s infinite ease-in-out;
    }
    .ai-chat__msg.is-typing span:nth-child(2) { animation-delay: 0.15s; }
    .ai-chat__msg.is-typing span:nth-child(3) { animation-delay: 0.3s; }
    @keyframes ai-bounce {
      0%, 80%, 100% { transform: translateY(0); opacity: 0.45; }
      40% { transform: translateY(-4px); opacity: 1; }
    }
    .ai-chat__suggestions {
      display: flex; flex-wrap: wrap; gap: 5px; padding: 0 14px 10px; background: #fbfcff;
    }
    .ai-chat__suggestions button {
      border: 1px solid #d4e2f7; border-radius: 999px; padding: 5px 9px;
      background: #f4f8ff; color: #3263b8; font: inherit; font-size: 10px; cursor: pointer;
    }
    .ai-chat__suggestions button:hover { background: #e8f1ff; }
    .ai-chat__input {
      display: flex; gap: 7px; padding: 10px 12px; border-top: 1px solid #edf1f7; background: #fff;
    }
    .ai-chat__input input {
      flex: 1; min-width: 0; height: 34px; border: 1px solid #dde5f0; border-radius: 6px;
      padding: 0 9px; color: #33465d; font: inherit; font-size: 11.5px; outline: 0;
    }
    .ai-chat__input input:focus { border-color: #5f8fe8; }
    .ai-chat__input button {
      min-width: 44px; border: 0; border-radius: 6px; background: #2f6fe5; color: #fff;
      font: inherit; font-size: 11px; font-weight: 700; cursor: pointer;
    }
    .ai-chat__input button:disabled { opacity: 0.45; cursor: default; }
    .ai-chat-fab {
      position: fixed; right: 26px; bottom: 26px; z-index: 91;
      display: grid; place-items: center; width: 54px; height: 54px;
      border: 0; border-radius: 50%; background: #1a73ff; color: #fff; font-size: 20px;
      box-shadow: 0 12px 26px rgba(26, 115, 255, 0.38); cursor: pointer;
    }
    .ai-chat-fab:hover { background: #075bd8; }
  `],
})
export class AssistantChatComponent {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  readonly open = signal(false);
  readonly loading = signal(false);
  readonly input = signal('');
  readonly messages = signal<ChatMessage[]>([{
    role: 'assistant',
    text: 'Chào bạn! Mình có thể tư vấn tính năng, hướng dẫn thao tác và cách xử lý các luồng bán hàng, kho, khách hàng, giao hàng của Tryum.',
  }]);
  readonly suggestions = [
    'Hướng dẫn tạo đơn giao hàng Goship?',
    'Cách xử lý thanh toán chuyển khoản?',
    'Cách quản lý tồn kho?',
  ];

  toggle(): void {
    this.open.update((value) => !value);
  }

  send(text?: string): void {
    const message = (text ?? this.input()).trim();
    if (!message || this.loading()) return;

    const history = this.messages()
      .slice(-10)
      .map((item) => ({ role: item.role, text: item.text }));
    this.messages.update((items) => [...items, { role: 'user', text: message }]);
    this.input.set('');
    this.loading.set(true);

    const user = this.auth.user();
    const endpoint = this.auth.isAuthenticated() ? 'store-chat' : 'chat';
    this.http.post<{ reply: string; provider: string }>(
      `${environment.apiUrl}/api/v1/assistant/${endpoint}`,
      {
        message,
        history,
        context: user ? `Người dùng ${user.fullName} đang ở khu vực quản trị của cửa hàng ${user.tenantName ?? ''}.` : null,
      },
    ).subscribe({
      next: (response) => {
        this.loading.set(false);
        this.messages.update((items) => [...items, { role: 'assistant', text: response.reply }]);
        this.scroll();
      },
      error: () => {
        this.loading.set(false);
        this.messages.update((items) => [...items, {
          role: 'assistant',
          text: 'Mình đang mất kết nối tới trợ lý AI. Bạn thử lại sau ít giây, hoặc gửi câu hỏi tới hotro@tryum.vn nhé.',
        }]);
        this.scroll();
      },
    });
  }

  format(text: string): string {
    return text
      .replace(/\*\*(.*?)\*\*/g, '$1')
      .replace(/`([^`]+)`/g, '$1')
      .replace(/^[-*]\s+/gm, '• ');
  }

  private scroll(): void {
    setTimeout(() => {
      const body = document.querySelector('.ai-chat__body');
      body?.scrollTo({ top: body.scrollHeight, behavior: 'smooth' });
    }, 30);
  }
}
