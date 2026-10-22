import { Component, inject } from '@angular/core';
import { RouterLink, ActivatedRoute } from '@angular/router';

@Component({
  selector: 'app-feature-placeholder',
  imports: [RouterLink],
  template: `
    <section class="feature-page">
      <div class="feature-card">
        <span class="feature-icon"><i class="pi pi-briefcase"></i></span>
        <p class="eyebrow">TRYUM · {{ title }}</p>
        <h1>{{ title }}</h1>
        <p class="description">Màn hình này chưa được kết nối dữ liệu nghiệp vụ. Tryum không hiển thị số liệu mẫu; khi chức năng và API sẵn sàng, dữ liệu cửa hàng sẽ xuất hiện tại đây.</p>
        <a routerLink="/app/dashboard"><i class="pi pi-arrow-left"></i> Về tổng quan</a>
      </div>
    </section>
  `,
  styles: `
    :host { display:block; }
    .feature-page { min-height:60vh; display:grid; place-items:center; padding:24px; }
    .feature-card { width:min(560px,100%); padding:34px; text-align:center; border:1px solid #e5eaf1; border-radius:10px; background:#fff; box-shadow:0 5px 20px #1735580d; }
    .feature-icon { width:52px; height:52px; margin:0 auto 16px; display:grid; place-items:center; border-radius:14px; color:#1760d0; background:#edf4ff; font-size:21px; }
    .eyebrow { margin:0 0 7px; color:#79869a; font-size:10px; font-weight:700; letter-spacing:.8px; text-transform:uppercase; }
    h1 { margin:0; color:#20324a; font-size:24px; }
    .description { max-width:430px; margin:12px auto 21px; color:#718096; font-size:13px; line-height:1.65; }
    a { display:inline-flex; align-items:center; gap:8px; color:#1458c8; font-size:12px; font-weight:600; text-decoration:none; }
    a:hover { text-decoration:underline; }
    @media (max-width:560px) { .feature-page { min-height:50vh; padding:12px; } .feature-card { padding:25px 18px; } h1 { font-size:21px; } }
  `,
})
export class FeaturePlaceholderComponent {
  protected readonly title = inject(ActivatedRoute).snapshot.data['featureTitle'] as string;
}
