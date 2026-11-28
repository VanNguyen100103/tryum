import { DecimalPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../core/services/auth.service';

interface BarDatum {
  label: string;
  /** Giá trị tính bằng triệu đồng, dùng để vẽ cột & tooltip. */
  value: number;
}

interface Ranking {
  rank: number;
  name: string;
  amount: string;
  pct: number;
}

interface Activity {
  icon: string;
  text: string;
  time: string;
}

type ChartView = 'ngay' | 'gio' | 'thu';

@Component({
  selector: 'app-dashboard',
  imports: [RouterLink, DecimalPipe],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent {
  protected readonly auth = inject(AuthService);
  protected readonly user = this.auth.user;

  protected readonly today = new Intl.DateTimeFormat('vi-VN', {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    year: 'numeric',
  }).format(new Date());

  // ── Kết quả bán hàng hôm nay ───────────────────────────────────────────────
  protected readonly summary = [
    { label: 'Doanh thu', value: '10,082,625', meta: '8 hóa đơn', tone: 'neutral' },
    { label: 'Trả hàng', value: '0', meta: 'Trong ngày', tone: 'neutral' },
    { label: 'Doanh thu thuần', value: '-20.92%', meta: 'So với hôm qua', tone: 'down' },
    { label: 'Doanh thu thuần', value: '+347.43%', meta: 'So với cùng kỳ tháng trước', tone: 'up' },
  ];

  // ── Biểu đồ Doanh thu thuần ────────────────────────────────────────────────
  protected readonly chartTotal = '285,311,921';
  protected readonly chartView = signal<ChartView>('ngay');
  protected readonly chartViews: { key: ChartView; label: string }[] = [
    { key: 'ngay', label: 'Theo ngày' },
    { key: 'gio', label: 'Theo giờ' },
    { key: 'thu', label: 'Theo thứ' },
  ];

  protected readonly branch = signal('Chi nhánh Hà Nội - 1A Yết Kiêu');
  protected readonly branchOpen = signal(false);
  protected readonly branches = [
    'Chi nhánh Hà Nội - 1A Yết Kiêu',
    'Chi nhánh trung tâm',
    'Chi nhánh Sài Gòn',
    'Chi nhánh mới',
  ];

  private readonly dayData: BarDatum[] = [
    { label: '1', value: 6.4 }, { label: '2', value: 9.1 }, { label: '3', value: 5.2 },
    { label: '4', value: 9.8 }, { label: '5', value: 11.2 }, { label: '6', value: 7.3 },
    { label: '7', value: 12.6 }, { label: '8', value: 10.4 }, { label: '9', value: 6.8 },
    { label: '10', value: 8.9 }, { label: '11', value: 14.2 }, { label: '12', value: 9.6 },
    { label: '13', value: 5.8 }, { label: '14', value: 7.1 }, { label: '15', value: 11.9 },
    { label: '16', value: 13.5 }, { label: '17', value: 8.4 }, { label: '18', value: 6.1 },
    { label: '19', value: 9.2 }, { label: '20', value: 12.1 }, { label: '21', value: 7.8 },
    { label: '22', value: 10.6 }, { label: '23', value: 5.4 }, { label: '24', value: 8.7 },
    { label: '25', value: 13.2 }, { label: '26', value: 11.1 }, { label: '27', value: 6.9 },
    { label: '28', value: 9.5 }, { label: '29', value: 12.8 }, { label: '30', value: 10.2 },
  ];

  private readonly hourData: BarDatum[] = [
    { label: '00', value: 1.2 }, { label: '01', value: 0.8 }, { label: '02', value: 0.5 },
    { label: '03', value: 0.3 }, { label: '04', value: 0.2 }, { label: '05', value: 0.4 },
    { label: '06', value: 1.1 }, { label: '07', value: 3.4 }, { label: '08', value: 6.8 },
    { label: '09', value: 9.2 }, { label: '10', value: 11.5 }, { label: '11', value: 12.4 },
    { label: '12', value: 10.8 }, { label: '13', value: 9.6 }, { label: '14', value: 11.2 },
    { label: '15', value: 12.1 }, { label: '16', value: 13.4 }, { label: '17', value: 12.8 },
    { label: '18', value: 11.6 }, { label: '19', value: 9.4 }, { label: '20', value: 7.2 },
    { label: '21', value: 5.1 }, { label: '22', value: 3.2 }, { label: '23', value: 1.8 },
  ];

  private readonly weekData: BarDatum[] = [
    { label: 'T2', value: 8.4 }, { label: 'T3', value: 9.1 }, { label: 'T4', value: 8.7 },
    { label: 'T5', value: 9.8 }, { label: 'T6', value: 12.6 }, { label: 'T7', value: 14.2 },
    { label: 'CN', value: 10.3 },
  ];

  protected readonly bars = computed<BarDatum[]>(() => {
    switch (this.chartView()) {
      case 'gio':
        return this.hourData;
      case 'thu':
        return this.weekData;
      default:
        return this.dayData;
    }
  });

  protected readonly maxBar = computed<number>(() =>
    this.bars().reduce((max, b) => Math.max(max, b.value), 0),
  );

  protected readonly chartTitle = computed<string>(() => {
    switch (this.chartView()) {
      case 'gio':
        return 'Theo giờ';
      case 'thu':
        return 'Theo thứ';
      default:
        return 'Theo ngày';
    }
  });

  // ── Top 10 hàng bán chạy ───────────────────────────────────────────────────
  protected readonly metric = signal<'qty' | 'revenue'>('qty');
  protected readonly topProducts: Ranking[] = [
    { rank: 1, name: 'ÁO KHOÁC GIÓ CÓ MŨ - Đen - S', amount: '96', pct: 100 },
    { rank: 2, name: 'Muối hảo hảo 120g (Lọ)', amount: '50', pct: 52 },
    { rank: 3, name: 'Bánh kẹo A (túi)', amount: '47', pct: 49 },
    { rank: 4, name: 'CHÂN VÁY CHỮ A PHỐI VIỀN HOA NỔI - Đen - M', amount: '33', pct: 34 },
    { rank: 5, name: 'ÁO THUN NAM CỔ TRÒN - Trắng - L', amount: '28', pct: 29 },
    { rank: 6, name: 'QUẦN JEANS ỐNG RỘNG - Xanh - 30', amount: '22', pct: 23 },
    { rank: 7, name: 'NƯỚC NGỌT COCA 330ml (Lon)', amount: '19', pct: 20 },
    { rank: 8, name: 'MÌ TÔM HẢO HẢO 75g (Gói)', amount: '17', pct: 18 },
    { rank: 9, name: 'SỮA TƯƠI VINAMILK 180ml (Hộp)', amount: '14', pct: 15 },
    { rank: 10, name: 'BÁNH QUY BƠ DANISA 240g (Hộp)', amount: '12', pct: 13 },
  ];

  // ── Top 10 khách mua nhiều nhất ────────────────────────────────────────────
  protected readonly topCustomers: Ranking[] = [
    { rank: 1, name: 'Hồng Sơn', amount: '10.9 tr', pct: 100 },
    { rank: 2, name: 'Trịnh', amount: '9.9 tr', pct: 91 },
    { rank: 3, name: 'Quốc Quý', amount: '5 tr', pct: 46 },
    { rank: 4, name: 'ANH NAM', amount: '4.9 tr', pct: 45 },
    { rank: 5, name: 'Lan Anh', amount: '4.2 tr', pct: 39 },
    { rank: 6, name: 'Minh Châu', amount: '3.6 tr', pct: 33 },
    { rank: 7, name: 'Thanh Hà', amount: '3.1 tr', pct: 28 },
    { rank: 8, name: 'Đức Anh', amount: '2.7 tr', pct: 25 },
    { rank: 9, name: 'Thu Huyền', amount: '2.3 tr', pct: 21 },
    { rank: 10, name: 'Văn Toàn', amount: '1.8 tr', pct: 17 },
  ];

  // ── Cảnh báo hệ thống ──────────────────────────────────────────────────────
  protected readonly alerts = [
    {
      icon: 'pi pi-exclamation-triangle',
      tone: 'warn',
      title: 'Có 32 hoạt động đăng nhập khác thường',
      body: 'Cần kiểm tra bảo mật tài khoản.',
      action: 'Kiểm tra',
    },
    {
      icon: 'pi pi-file',
      tone: 'info',
      title: 'Có 128 hóa đơn chưa gửi CQT',
      body: 'Hóa đơn cần được gửi CQT trong ngày để đảm bảo tuân thủ quy định về hóa đơn điện tử.',
      action: 'Kiểm tra',
    },
    {
      icon: 'pi pi-times-circle',
      tone: 'danger',
      title: 'Có 2 hóa đơn điện tử bị CQT từ chối hôm nay',
      body: 'Do không hợp lệ. Hãy kiểm tra và gửi hóa đơn mới.',
      action: 'Kiểm tra',
    },
    {
      icon: 'pi pi-id-card',
      tone: 'debt',
      title: 'Có 15 khách hàng đang còn nợ',
      body: 'Theo dõi và thu hồi công nợ kịp thời.',
      action: 'Xem danh sách',
    },
  ];

  // ── Hoạt động gần đây ──────────────────────────────────────────────────────
  protected readonly activities: Activity[] = [
    { icon: 'pi pi-receipt', text: 'AD vừa bán đơn hàng với giá trị 1,080,000', time: 'vài phút trước' },
    { icon: 'pi pi-shopping-bag', text: 'AD vừa nhận đặt hàng với giá trị 2,160,000', time: 'một phút trước' },
    { icon: 'pi pi-shopping-bag', text: 'AD vừa nhận đặt hàng với giá trị 4,225,000', time: '2 phút trước' },
    { icon: 'pi pi-shopping-bag', text: 'AD vừa nhận đặt hàng với giá trị 351,000', time: '2 phút trước' },
    { icon: 'pi pi-receipt', text: 'AD vừa bán đơn hàng với giá trị 975,000', time: '3 phút trước' },
  ];

  protected selectView(view: ChartView): void {
    this.chartView.set(view);
  }

  protected selectBranch(value: string): void {
    this.branch.set(value);
    this.branchOpen.set(false);
  }

  protected toggleBranch(): void {
    this.branchOpen.update((v) => !v);
  }

  protected selectMetric(value: 'qty' | 'revenue'): void {
    this.metric.set(value);
  }
}
