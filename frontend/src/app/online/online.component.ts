import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { SocialService } from '../core/services/social.service';
import { ToastService } from '../core/services/toast.service';

@Component({
  selector: 'app-online',
  imports: [FormsModule, RouterLink],
  templateUrl: './online.component.html',
  styleUrl: './online.component.scss',
})
export class OnlineComponent implements OnInit {
  private readonly social = inject(SocialService);
  private readonly toast = inject(ToastService);

  protected readonly connected = signal(false);
  protected readonly pageId = signal('');
  protected readonly pageName = signal('');
  protected readonly tokenSet = signal(false);

  protected readonly connectPageId = signal('');
  protected readonly connectToken = signal('');
  protected readonly connecting = signal(false);

  protected readonly message = signal('');
  protected readonly title = signal('');
  protected readonly imageUrl = signal('');
  protected readonly topic = signal('Tryum - phần mềm quản lý bán hàng cho quán ăn và cửa hàng nhỏ');
  protected readonly generating = signal(false);
  protected readonly link = signal('');
  protected readonly publishing = signal(false);
  protected readonly lastPostId = signal('');

  protected readonly templates = [
    {
      label: 'Giới thiệu Tryum',
      text: `Quán nhỏ cũng cần một hệ thống bán hàng tử tế.

Tryum giúp bạn:
- Bán hàng tại quầy, quét mã vạch
- Quản lý hàng hóa, tồn kho
- Theo dõi khách hàng, điểm tích lũy, công nợ
- In hóa đơn, thanh toán QR
- Tạo vận đơn giao hàng Goship

Tất cả trong một màn hình, không cần cài đặt.

Dùng thử miễn phí tại: tryum.vn`,
    },
    {
      label: 'Nỗi đau cuối ngày',
      text: `Cuối ngày ngồi cộng sổ, bạn có chắc mình không thất thoát?

Một cuốn sổ, một file Excel và vài tin nhắn chuyển khoản… rất dễ sót đơn, sót tiền, sót khách.

Tryum ghi nhận từng đơn ngay khi bán:
- Doanh thu theo ngày/tháng
- Đơn nào đã thu, đơn nào còn nợ
- Khách nào mua nhiều, khách nào còn nợ

Bán xong là có số liệu, không phải chờ cuối ngày.

Dùng thử miễn phí: tryum.vn`,
    },
    {
      label: 'Giao hàng Goship',
      text: `Đừng tự chạy đi hỏi từng hãng vận chuyển.

Tryum so sánh giá nhiều hãng cùng lúc: GHTK, GHN, Viettel Post, J&T…

Chọn hãng xong là tạo vận đơn luôn, có COD, khai giá, người gửi trả phí.

Một màn hình cho cả bán tại quầy và bán giao hàng.

Trải nghiệm: tryum.vn`,
    },
    {
      label: 'Kêu gọi dùng thử',
      text: `Bạn đang quản lý cửa hàng bằng sổ tay hay Excel?

Tryum giúp bạn bán hàng, quản kho, khách hàng và báo cáo trong cùng một chỗ.

Không cần cài đặt, dùng thử miễn phí ngay tại tryum.vn.

Bạn muốn mình hướng dẫn phần nào trước?`,
    },
  ];

  ngOnInit(): void {
    this.social.status().subscribe({
      next: (status) => {
        this.connected.set(status.connected);
        this.pageId.set(status.pageId ?? '');
        this.pageName.set(status.pageName ?? '');
        this.tokenSet.set(status.tokenSet);
      },
    });
  }

  useTemplate(text: string): void {
    this.title.set('');
    this.imageUrl.set('');
    this.message.set(text);
  }

  generateAi(): void {
    if (this.generating()) return;
    this.generating.set(true);
    this.social.generate(this.topic().trim()).subscribe({
      next: (post) => {
        this.generating.set(false);
        this.title.set(post.title);
        this.message.set(post.content);
        this.imageUrl.set(post.imageUrl);
        this.toast.add({ severity: 'success', summary: 'AI đã tạo bài đăng' });
      },
      error: (error: unknown) => {
        this.generating.set(false);
        this.toast.add({ severity: 'error', summary: 'AI tạo bài thất bại', detail: this.messageOf(error) });
      },
    });
  }

  connect(): void {
    if (!this.connectPageId().trim() || !this.connectToken().trim()) {
      this.toast.add({ severity: 'warn', summary: 'Nhập Page ID và Page Access Token' });
      return;
    }
    this.connecting.set(true);
    this.social.connect(this.connectPageId().trim(), this.connectToken().trim()).subscribe({
      next: (result) => {
        this.connecting.set(false);
        this.connected.set(true);
        this.pageId.set(result.pageId);
        this.pageName.set(result.pageName);
        this.tokenSet.set(true);
        this.connectToken.set('');
        this.toast.add({ severity: 'success', summary: 'Đã kết nối fanpage', detail: result.pageName });
      },
      error: (error: unknown) => {
        this.connecting.set(false);
        this.toast.add({ severity: 'error', summary: 'Không kết nối được', detail: this.messageOf(error) });
      },
    });
  }

  disconnect(): void {
    this.social.disconnect().subscribe({
      next: () => {
        this.connected.set(false);
        this.pageId.set('');
        this.pageName.set('');
        this.tokenSet.set(false);
        this.toast.add({ severity: 'info', summary: 'Đã ngắt kết nối fanpage' });
      },
    });
  }

  publish(): void {
    const text = this.message().trim();
    if (!text) {
      this.toast.add({ severity: 'warn', summary: 'Nhập nội dung bài đăng' });
      return;
    }
    if (!this.connected()) {
      this.toast.add({ severity: 'warn', summary: 'Kết nối fanpage trước khi đăng' });
      return;
    }

    this.publishing.set(true);
    const fullMessage = this.title().trim()
      ? `${this.title().trim()}\n\n${text}`
      : text;
    this.social.publishWithImage(fullMessage, this.link().trim() || null, this.imageUrl().trim() || null).subscribe({
      next: (result) => {
        this.publishing.set(false);
        this.lastPostId.set(result.id);
        this.toast.add({ severity: 'success', summary: 'Đã đăng bài lên fanpage' });
      },
      error: (error: unknown) => {
        this.publishing.set(false);
        this.toast.add({ severity: 'error', summary: 'Đăng bài thất bại', detail: this.messageOf(error) });
      },
    });
  }

  private messageOf(error: unknown): string {
    const candidate = error as { error?: { message?: string }; message?: string };
    return candidate?.error?.message ?? candidate?.message ?? 'Vui lòng thử lại.';
  }
}
