import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../core/services/auth.service';

interface Feature {
  icon: string;
  title: string;
  desc: string;
}

interface Solution {
  icon: string;
  title: string;
  desc: string;
}

interface Testimonial {
  quote: string;
  name: string;
  role: string;
}

interface Stat {
  value: string;
  label: string;
}

interface FeatureTab {
  label: string;
  icon: string;
  title: string;
  desc: string;
  bullets: string[];
}

interface PricingPlan {
  name: string;
  price: string;
  period: string;
  desc: string;
  highlight: boolean;
  features: string[];
  cta: string;
}

interface FaqItem {
  q: string;
  a: string;
}

@Component({
  selector: 'app-home',
  imports: [RouterLink],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss',
})
export class HomeComponent {
  private readonly auth = inject(AuthService);

  protected readonly isAuthenticated = this.auth.isAuthenticated;

  activeFeatureTab = signal(0);
  openFaqIndex = signal(-1);

  toggleFaq(i: number): void {
    this.openFaqIndex.set(this.openFaqIndex() === i ? -1 : i);
  }

  protected readonly stats: Stat[] = [
    { value: '10.000+', label: 'cửa hàng tin dùng' },
    { value: '0đ', label: 'phí cài đặt & triển khai' },
    { value: '24/7', label: 'hỗ trợ kỹ thuật' },
    { value: '100%', label: 'dữ liệu thuộc về bạn' },
  ];

  protected readonly featureTabs: FeatureTab[] = [
    {
      label: 'Bán hàng POS',
      icon: 'pi pi-shopping-cart',
      title: 'Bán hàng siêu tốc',
      desc: 'Màn hình POS trực quan, quét mã vạch, nhiều hóa đơn song song. Thu ngân xử lý đơn hàng chỉ trong vài giây. Hỗ trợ phím tắt F2, F9...',
      bullets: ['Quét mã vạch / nhập nhanh', 'Nhiều hóa đơn song song', 'Phím tắt chuyên nghiệp', 'In hóa đơn K80'],
    },
    {
      label: 'Quản kho',
      icon: 'pi pi-box',
      title: 'Kiểm soát kho chính xác',
      desc: 'Tự động cập nhật tồn kho mỗi khi bán hàng, nhập hàng. Cảnh báo sắp hết, chặn âm kho, lịch sử mọi biến động.',
      bullets: ['Nhập xuất kho tự động', 'Cảnh báo sắp hết hàng', 'Chặn âm kho', 'Sổ nhật ký kho chi tiết'],
    },
    {
      label: 'Báo cáo',
      icon: 'pi pi-chart-bar',
      title: 'Báo cáo thông minh',
      desc: 'Dashboard realtime, doanh thu theo ngày/tháng, top sản phẩm bán chạy. Lãi gộp, chi phí, công nợ — mọi con số trong tầm tay.',
      bullets: ['Dashboard realtime', 'Doanh thu theo ngày/tháng', 'Top sản phẩm bán chạy', 'Biểu đồ lãi gộp'],
    },
    {
      label: 'Khách hàng',
      icon: 'pi pi-users',
      title: 'Chăm sóc khách hàng tốt hơn',
      desc: 'Lưu thông tin, lịch sử mua hàng, điểm tích lũy và công nợ của từng khách. Khai thác upsell dễ dàng hơn bao giờ hết.',
      bullets: ['Hồ sơ khách hàng', 'Điểm tích lũy', 'Lịch sử mua hàng', 'Theo dõi công nợ'],
    },
  ];

  protected readonly features: Feature[] = [
    {
      icon: 'pi pi-shopping-cart',
      title: 'Bán hàng trực quan',
      desc: 'Màn hình POS quen tay, quét mã vạch, nhiều đơn hàng song song — thu ngân thao tác trong vài giây.',
    },
    {
      icon: 'pi pi-box',
      title: 'Quản kho chính xác',
      desc: 'Nhập – xuất – kiểm kho tự động, cảnh báo sắp hết hàng, chặn âm kho theo thời gian thực.',
    },
    {
      icon: 'pi pi-qrcode',
      title: 'Thanh toán linh hoạt',
      desc: 'Tiền mặt có tính tiền thừa, chuyển khoản QR (VietQR), in hóa đơn K80 ngay tại quầy.',
    },
    {
      icon: 'pi pi-users',
      title: 'Khách hàng & công nợ',
      desc: 'Lịch sử mua hàng, điểm tích lũy, theo dõi công nợ từng khách để khai thác upsell dễ dàng.',
    },
    {
      icon: 'pi pi-chart-line',
      title: 'Báo cáo doanh thu',
      desc: 'Doanh thu theo ngày/tháng, top sản phẩm bán chạy, lãi gộp — cập nhật realtime khi có đơn mới.',
    },
    {
      icon: 'pi pi-shield',
      title: 'An toàn & phân quyền',
      desc: 'Vai trò Chủ cửa hàng / Quản lý / Thu ngân, dữ liệu cô lập theo từng cửa hàng, truy cập an toàn.',
    },
  ];

  protected readonly solutions: Solution[] = [
    {
      icon: 'pi pi-shopping-bag',
      title: 'Bán lẻ & tạp hóa',
      desc: 'Tối ưu quy trình bán hàng cả offline lẫn online, tiết kiệm chi phí vận hành mỗi ngày.',
    },
    {
      icon: 'pi pi-receipt',
      title: 'Cafe & F&B',
      desc: 'Order chính xác, quản nguyên liệu, chốt ca nhanh — khách chờ ít hơn, doanh thu tăng hơn.',
    },
    {
      icon: 'pi pi-heart',
      title: 'Nhà thuốc & mỹ phẩm',
      desc: 'Quản hạn sử dụng, lô hàng và định mức giá theo từng sản phẩm tiêu dùng.',
    },
    {
      icon: 'pi pi-sparkles',
      title: 'Salon & spa',
      desc: 'Lịch hẹn, hồ sơ khách, liệu trình và tính lương nhân viên trong một ứng dụng.',
    },
  ];

  protected readonly industries = [
    'Tạp hóa & siêu thị',
    'Thời trang',
    'Cafe, trà sữa',
    'Quán ăn',
    'Nhà thuốc',
    'Mỹ phẩm',
    'Điện tử & điện máy',
    'Salon, spa',
    'Khách sạn',
    'Mẹ & bé',
    'Vật liệu xây dựng',
    'Xe & máy móc',
  ];

  protected readonly pricingPlans: PricingPlan[] = [
    {
      name: 'Miễn phí',
      price: '0đ',
      period: 'mãi mãi',
      highlight: false,
      desc: 'Phù hợp để khám phá và thử nghiệm hệ thống.',
      features: ['1 cửa hàng', '2 nhân viên', 'Bán hàng POS cơ bản', 'Quản kho cơ bản', 'Báo cáo theo ngày', 'Hỗ trợ qua email'],
      cta: 'Dùng thử ngay',
    },
    {
      name: 'Pro',
      price: '199.000đ',
      period: '/tháng',
      highlight: true,
      desc: 'Đầy đủ tính năng cho cửa hàng đang phát triển.',
      features: [
        'Không giới hạn cửa hàng',
        'Không giới hạn nhân viên',
        'POS nâng cao + phím tắt',
        'Kho nâng cao + cảnh báo',
        'Báo cáo realtime',
        'Xuất Excel',
        'Khách hàng & điểm tích lũy',
        'Hỗ trợ ưu tiên 24/7',
      ],
      cta: 'Bắt đầu dùng Pro',
    },
  ];

  protected readonly testimonials: Testimonial[] = [
    {
      quote: 'Quản lý cửa hàng nhàn hơn hẳn, mọi quy trình đều được tự động hóa. Chốt sổ mỗi tối nhanh hơn trước rất nhiều.',
      name: 'Chị Thảo',
      role: 'Chủ shop thời trang',
    },
    {
      quote: 'Báo cáo doanh thu nhanh, theo dõi nguyên liệu và ca kíp rất tiện. Tiết kiệm cả tiếng đồng hồ mỗi tối.',
      name: 'Anh Minh',
      role: 'Chủ chuỗi 3 tiệm cafe',
    },
    {
      quote: 'Tồn kho không còn lệch, nhân viên nào bán được gì là hiện rõ ngay trên bảng. Dễ kiểm soát hơn nhiều.',
      name: 'Anh Hùng',
      role: 'Chủ tạp hóa',
    },
  ];

  protected readonly faqItems: FaqItem[] = [
    {
      q: 'Tryum có miễn phí không?',
      a: 'Có! Gói Miễn phí không giới hạn thời gian, bao gồm các tính năng cơ bản. Nâng cấp lên Pro khi cần tính năng nâng cao.',
    },
    {
      q: 'Dữ liệu của tôi có an toàn không?',
      a: 'Dữ liệu được mã hóa, backup hàng ngày và cô lập hoàn toàn theo từng cửa hàng. Không ai khác có thể xem dữ liệu của bạn.',
    },
    {
      q: 'Tôi có thể dùng trên điện thoại không?',
      a: 'Tryum chạy trên trình duyệt, tương thích với máy tính, máy tính bảng và điện thoại — không cần cài đặt app.',
    },
    {
      q: 'Chuyển dữ liệu từ phần mềm cũ được không?',
      a: 'Hỗ trợ import danh sách sản phẩm, khách hàng từ file Excel. Đội ngũ hỗ trợ sẵn sàng hỗ trợ bạn qua quá trình chuyển đổi.',
    },
    {
      q: 'Có hỗ trợ in hóa đơn không?',
      a: 'Hỗ trợ in hóa đơn nhiệt khổ K80 và in từ máy in thông thường ngay tại trình duyệt.',
    },
  ];
}
