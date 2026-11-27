import { Component } from '@angular/core';
import { NgFor } from '@angular/common';

interface ScreenDoc {
  title: string;
  route: string;
  desc: string;
  done: boolean;
}

/**
 * Trang tài liệu màn hình (kiểu trang "Hỗ trợ" của KiotViet).
 * Liệt kê các màn hình chính + trạng thái hoàn thiện.
 * Ảnh chụp từng màn sẽ được sinh tự động bằng Playwright (bước sau).
 */
@Component({
  selector: 'app-docs',
  standalone: true,
  imports: [NgFor],
  template: `
    <main class="docs">
      <header class="docs__head">
        <h1>Tài liệu màn hình</h1>
        <p>Danh sách các màn hình của Tryum (web + PWA). Ảnh chụp màn hình sẽ được cập nhật tự động.</p>
      </header>

      <section class="docs__grid">
        <article class="screen" *ngFor="let s of screens">
          <div class="screen__top">
            <span class="screen__route">{{ s.route }}</span>
            <span class="screen__badge" [class.is-wip]="!s.done">
              {{ s.done ? 'Hoàn thiện' : 'Đang phát triển' }}
            </span>
          </div>
          <h2 class="screen__title">{{ s.title }}</h2>
          <p class="screen__desc">{{ s.desc }}</p>
        </article>
      </section>
    </main>
  `,
  styles: [
    `
      .docs {
        max-width: 1080px;
        margin: 0 auto;
        padding: 32px 20px 64px;
        font-family: Inter, 'Segoe UI', Arial, sans-serif;
        color: #1d2635;
      }
      .docs__head h1 {
        margin: 0 0 8px;
        font-size: 26px;
        font-weight: 800;
      }
      .docs__head p {
        margin: 0 0 24px;
        color: #66788f;
        font-size: 14px;
      }
      .docs__grid {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
        gap: 14px;
      }
      .screen {
        background: #fff;
        border: 1px solid #e5eaf1;
        border-radius: 10px;
        padding: 16px;
        transition: box-shadow 0.15s ease, transform 0.15s ease;
      }
      .screen:hover {
        box-shadow: 0 6px 18px rgba(29, 38, 53, 0.08);
        transform: translateY(-2px);
      }
      .screen__top {
        display: flex;
        justify-content: space-between;
        align-items: center;
        gap: 8px;
      }
      .screen__route {
        font-family: ui-monospace, 'Cascadia Code', monospace;
        font-size: 12px;
        color: #3263b8;
        background: #f0f5ff;
        padding: 2px 7px;
        border-radius: 5px;
      }
      .screen__badge {
        font-size: 11px;
        font-weight: 600;
        color: #1a7f37;
        background: #e6f6ec;
        padding: 2px 8px;
        border-radius: 999px;
        white-space: nowrap;
      }
      .screen__badge.is-wip {
        color: #8a6d1a;
        background: #fbf3d9;
      }
      .screen__title {
        margin: 12px 0 4px;
        font-size: 15px;
        font-weight: 700;
      }
      .screen__desc {
        margin: 0;
        font-size: 13px;
        color: #66788f;
        line-height: 1.5;
      }
      @media (max-width: 640px) {
        .docs__grid {
          grid-template-columns: 1fr;
        }
      }
    `,
  ],
})
export class DocsComponent {
  screens: ScreenDoc[] = [
    { title: 'Trang chủ', route: '/', desc: 'Giới thiệu sản phẩm, đăng ký dùng thử.', done: true },
    { title: 'Đăng nhập', route: '/auth/login', desc: 'Đăng nhập bằng SĐT/email theo cửa hàng.', done: true },
    { title: 'Đăng ký', route: '/auth/register', desc: 'Tạo cửa hàng mới + tài khoản chủ.', done: true },
    { title: 'Dashboard', route: '/app/dashboard', desc: 'Tổng quan doanh thu, đơn hàng, tồn kho.', done: true },
    { title: 'Bán hàng (POS)', route: '/app/pos', desc: 'Tính tiền nhanh, quét QR, in hóa đơn.', done: true },
    { title: 'Hàng hóa', route: '/app/products', desc: 'Quản lý danh mục, nhập Excel, quản lý ảnh.', done: true },
    { title: 'Khách hàng', route: '/app/customers', desc: 'Khách hàng, điểm tích lũy, công nợ.', done: true },
    { title: 'Kho', route: '/app/inventory', desc: 'Kiểm kho, nhập xuất hàng hóa.', done: true },
    { title: 'Bán online', route: '/app/online', desc: 'Đăng bài Facebook, AI tạo nội dung.', done: true },
    { title: 'Báo cáo', route: '/app/reports', desc: 'Báo cáo bán hàng, lợi nhuận.', done: true },
    { title: 'Cài đặt', route: '/app/settings', desc: 'Cấu hình cửa hàng, thanh toán, vận chuyển.', done: true },
    { title: 'Nhân viên / Sổ quỹ / Thuế', route: '/app/*', desc: 'Tính năng đang phát triển.', done: false },
  ];
}
