import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../core/services/auth.service';

interface NavGroup {
  label: string;
  title: string;
  items: { label: string; href: string; badge?: string }[];
}

interface IndustryCol {
  label?: string;
  title: string;
  items: string[];
}

interface Benefit {
  icon: string;
  title: string;
  desc: string;
}

interface FeatureTab {
  label: string;
  icon: string;
  title: string;
  desc: string;
  bullets: string[];
}

interface Solution {
  icon: string;
  title: string;
  desc: string;
}

interface TabItem {
  label: string;
  icon: string;
  title: string;
  desc: string;
  bullets: string[];
}

interface OnlineItem {
  icon: string;
  title: string;
  desc: string;
}

interface FinanceItem {
  icon: string;
  title: string;
  desc: string;
  mock: string;
}

interface SecurityBadge {
  icon: string;
  label: string;
  sub: string;
}

interface Review {
  quote: string;
  name: string;
  role: string;
}

interface Device {
  icon: string;
  name: string;
  desc: string;
}

interface NewsItem {
  tag: string;
  date: string;
  title: string;
  icon: string;
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

interface ContactChannel {
  icon: string;
  title: string;
  main: string;
  sub: string;
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
  activeAccountingTab = signal(0);
  activeStaffTab = signal(0);
  openFaqIndex = signal(-1);
  mobileNav = signal(false);

  toggleFaq(i: number): void {
    this.openFaqIndex.set(this.openFaqIndex() === i ? -1 : i);
  }

  protected readonly solutionGroups: NavGroup[] = [
    {
      label: 'Giải pháp',
      title: 'Bán hàng',
      items: [
        { label: 'Bán buôn, bán lẻ', href: '#solutions' },
        { label: 'Ăn uống, giải trí', href: '#solutions' },
        { label: 'Sức khỏe, làm đẹp', href: '#solutions' },
        { label: 'Khách sạn, nhà nghỉ', href: '#solutions' },
      ],
    },
    {
      label: 'Giải pháp',
      title: 'Kế toán & Thuế',
      items: [
        { label: 'Kế toán hộ kinh doanh', href: '#accounting', badge: 'Miễn phí' },
        { label: 'Hoá đơn điện tử', href: '#accounting', badge: 'Miễn phí' },
        { label: 'Tư vấn thuế', href: '#accounting' },
      ],
    },
    {
      label: 'Giải pháp',
      title: 'Tài chính',
      items: [
        { label: 'Giải pháp thanh toán QR', href: '#finance' },
        { label: 'Giải pháp vay vốn kinh doanh', href: '#finance' },
      ],
    },
    {
      label: 'Giải pháp',
      title: 'Bán hàng Online',
      items: [
        { label: 'Đồng bộ sàn TMĐT và mạng xã hội', href: '#online' },
        { label: 'Tích hợp FoodApp', href: '#online' },
        { label: 'Tích hợp OTA', href: '#online' },
        { label: 'Tạo website bán hàng', href: '#online' },
        { label: 'Giải pháp giao hàng', href: '#online' },
      ],
    },
    {
      label: 'Giải pháp',
      title: 'Quản lý nhân viên',
      items: [
        { label: 'Bảng chấm công', href: '#staff' },
        { label: 'Bảng tính lương', href: '#staff' },
        { label: 'Lịch làm việc', href: '#staff' },
        { label: 'Bảng hoa hồng', href: '#staff' },
      ],
    },
  ];

  protected readonly industryMenu: IndustryCol[] = [
    {
      title: 'Bán buôn, bán lẻ',
      items: ['Tạp hóa & Siêu thị', 'Thời trang', 'Điện tử & Điện máy', 'Nhà thuốc', 'Xem tất cả →'],
    },
    {
      title: 'Ăn uống, giải trí',
      items: ['Quán ăn', 'Cafe & Trà sữa', 'Nhà hàng', 'Karaoke, Bida', 'Xem tất cả →'],
    },
    {
      title: 'Sức khỏe, làm đẹp',
      items: ['Thẩm mỹ viện', 'Hair Salon', 'Nail & Mi', 'Spa & Massage', 'Xem tất cả →'],
    },
    {
      title: 'Khách sạn, nhà nghỉ',
      items: ['Khách sạn', 'Homestay', 'Nhà nghỉ', 'Resort', 'Xem tất cả →'],
    },
  ];

  protected readonly benefits: Benefit[] = [
    {
      icon: 'pi pi-star',
      title: 'Đơn giản & Dễ sử dụng',
      desc: 'Giao diện đơn giản và thông minh. Chỉ mất 15 phút làm quen.',
    },
    {
      icon: 'pi pi-wallet',
      title: 'Tiết kiệm chi phí',
      desc: 'Miễn phí cài đặt, triển khai, nâng cấp và hỗ trợ.',
    },
    {
      icon: 'pi pi-th-large',
      title: 'Phù hợp từng ngành hàng',
      desc: 'Phù hợp cho hơn 20 ngành nghề kinh doanh khác nhau.',
    },
  ];

  protected readonly featureTabs: FeatureTab[] = [
    {
      label: 'Bán hàng POS',
      icon: 'pi pi-shopping-cart',
      title: 'Bán hàng siêu tốc',
      desc: 'Màn hình POS trực quan, quét mã vạch, nhiều hóa đơn song song. Thu ngân xử lý đơn hàng chỉ trong vài giây.',
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

  protected readonly solutions: Solution[] = [
    {
      icon: 'pi pi-shopping-bag',
      title: 'Bán buôn, bán lẻ',
      desc: 'Tối ưu quy trình bán hàng cả online và offline, tiết kiệm chi phí và thời gian quản lý vận hành.',
    },
    {
      icon: 'pi pi-receipt',
      title: 'Ăn uống, giải trí',
      desc: 'Order chính xác, vận hành tối ưu, quản lý ca kíp và nguyên liệu liền mạch trên mọi chi nhánh.',
    },
    {
      icon: 'pi pi-heart',
      title: 'Sức khỏe, làm đẹp',
      desc: 'Quản lý lịch hẹn, hồ sơ khách hàng và liệu trình một cách chuyên nghiệp và hiệu quả.',
    },
    {
      icon: 'pi pi-hotel',
      title: 'Khách sạn, nhà nghỉ',
      desc: 'Đặt lịch dễ dàng, tiết kiệm thời gian lễ tân, đồng bộ phòng và khách lưu trú một nơi duy nhất.',
    },
  ];

  protected readonly accountingTabs: TabItem[] = [
    {
      label: 'Kế toán',
      icon: 'pi pi-file',
      title: 'Kế toán hộ kinh doanh',
      desc: 'Miễn phí giải pháp kế toán hộ kinh doanh: sổ sách, kê khai, lưu trữ chứng từ theo chuẩn quy định.',
      bullets: ['Sổ sách tự động', 'Kê khai thuế GTGT, TSTN', 'Lưu trữ chứng từ 10 năm'],
    },
    {
      label: 'Hóa đơn',
      icon: 'pi pi-receipt',
      title: 'Hóa đơn điện tử',
      desc: 'Xuất hóa đơn điện tử từ chính máy tính tiền, ký số, tra cứu và theo dõi trạng thái gửi cơ quan thuế.',
      bullets: ['Xuất hóa đơn từ POS', 'Tích hợp chữ ký số', 'Theo dõi trạng thái invoice'],
    },
    {
      label: 'Tư vấn thuế',
      icon: 'pi pi-user-check',
      title: 'Tư vấn thuế',
      desc: 'Miễn phí hỗ trợ và tư vấn các vấn đề tuân thủ thuế với đội ngũ chuyên viên trên toàn quốc.',
      bullets: ['Hotline tư vấn 7:00–22:00', 'Kịch bản thuế theo ngành', 'Đồng hành mùa cao điểm'],
    },
  ];

  protected readonly onlineItems: OnlineItem[] = [
    {
      icon: 'pi pi-globe',
      title: 'Đồng bộ sàn TMĐT & MXH',
      desc: 'Tích hợp và bán hàng trên Shopee, TikTok Shop, Facebook, Zalo — tồn kho và đơn hàng đồng bộ một nơi.',
    },
    {
      icon: 'pi pi-bolt',
      title: 'Tích hợp FoodApp',
      desc: 'Nhận đơn ShopeeFood, GrabFood về thẳng hệ thống, không sót đơn, không nhập tay.',
    },
    {
      icon: 'pi pi-hotel',
      title: 'Tích hợp OTA',
      desc: 'Kết nối các kênh OTA như Agoda, Booking, Airbnb giúp quản lý đặt phòng và lưu trú dễ dàng.',
    },
    {
      icon: 'pi pi-desktop',
      title: 'Tạo website bán hàng',
      desc: 'Cung cấp Emenu, Ebooking, website bán hàng cho mọi ngành hàng, không cần lập trình.',
    },
    {
      icon: 'pi pi-truck',
      title: 'Giải pháp giao hàng',
      desc: 'So sánh giá và dịch vụ các hãng: giao TMĐT, giao tức thời cho FnB, giao hàng nặng theo kiện.',
    },
  ];

  protected readonly financeItems: FinanceItem[] = [
    {
      icon: 'pi pi-qrcode',
      title: 'Giải pháp thanh toán QR',
      desc: 'Tích hợp QR thanh toán, tiền về tức thì, theo dõi giao dịch ngay trên phần mềm, hỗ trợ thiết bị QR loa.',
      mock: 'qr',
    },
    {
      icon: 'pi pi-credit-card',
      title: 'Giải pháp vay vốn kinh doanh',
      desc: 'Vay tín chấp nhanh chóng với đa dạng gói phù hợp, kết nối trực tiếp các đối tác tài chính uy tín.',
      mock: 'card',
    },
  ];

  protected readonly staffTabs: TabItem[] = [
    {
      label: 'Chấm công',
      icon: 'pi pi-clock',
      title: 'Bảng chấm công',
      desc: 'Quản lý chấm công nhân viên chính xác, chấm qua ứng dụng, theo dõi và kiểm tra dễ dàng.',
      bullets: ['Chấm công trên điện thoại', 'Đánh dấu ca làm việc', 'Báo cáo công hàng tháng'],
    },
    {
      label: 'Tính lương',
      icon: 'pi pi-credit-card',
      title: 'Bảng tính lương',
      desc: 'Tự động tính lương dựa trên công, phụ cấp, thưởng phạt. Xuất bảng lương chi tiết nhanh chóng.',
      bullets: ['Công thức lương tùy chỉnh', 'Xuất bảng lương Excel', 'Lịch sử lương minh bạch'],
    },
    {
      label: 'Lịch làm việc',
      icon: 'pi pi-calendar',
      title: 'Lịch làm việc',
      desc: 'Sắp xếp ca làm việc linh hoạt, phân công nhân viên theo chi nhánh, theo dõi nghỉ phép dễ dàng.',
      bullets: ['Kéo thả xếp ca', 'Phân công theo chi nhánh', 'Đánh dấu nghỉ phép'],
    },
    {
      label: 'Hoa hồng',
      icon: 'pi pi-gift',
      title: 'Bảng hoa hồng',
      desc: 'Thiết lập chính sách hoa hồng đa dạng theo doanh số, sản phẩm. Tự động tính và báo cáo minh bạch.',
      bullets: ['Chính sách theo doanh số', 'Hoa hồng theo sản phẩm', 'Báo cáo chi tiết từng nhân viên'],
    },
  ];

  protected readonly securityBadges: SecurityBadge[] = [
    { icon: 'pi pi-lock', label: 'Mã hóa AES-256', sub: 'Dữ liệu & định danh nhiều lớp' },
    { icon: 'pi pi-refresh', label: 'Backup tự động', sub: 'Hàng ngày, cô lập theo từng cửa hàng' },
  ];

  protected readonly industryGroups: IndustryCol[] = [
    {
      title: 'Bán buôn, bán lẻ',
      items: [
        'Tạp hóa & Siêu thị',
        'Thời trang',
        'Điện tử & Điện máy',
        'Vật liệu xây dựng',
        'Nông sản & Thực phẩm',
        'Nhà thuốc',
        'Xe & Máy móc',
        'Mỹ phẩm',
        'Nội thất & Gia dụng',
        'Mẹ & Bé',
        'Sách & Văn phòng phẩm',
        'Hoa & Quà tặng',
      ],
    },
    {
      title: 'Ăn uống, giải trí',
      items: ['Quán ăn', 'Cafe, Trà sữa', 'Karaoke, Bida', 'Bar, Pub & Club', 'Căng tin & Trạm dừng nghỉ', 'Nhà hàng'],
    },
    {
      title: 'Sức khỏe, làm đẹp',
      items: ['Thẩm mỹ viện', 'Hair Salon', 'Nail & Mi', 'Phòng khám', 'Spa & Massage', 'Gym, Yoga & Pilates'],
    },
    {
      title: 'Khách sạn, nhà nghỉ',
      items: ['Khách sạn', 'Homestay', 'Nhà nghỉ', 'Nhà trọ', 'Villa', 'Resort', 'Camping & Glamping'],
    },
  ];

  protected readonly marqueeShops = [
    'Cà Phê Nhà Bè',
    'Tạp Hóa Minh Anh',
    'Thời Trang Mây',
    'Sạch & Đẹp Spa',
    'Nhà Thuốc Green',
    'Xây Dựng Đồng Tâm',
    'Trà Sữa Lá Dừa',
    'Mẹ & Bé Thùy Linh',
    'Điện Máy Hoàng Gia',
    'Du Lịch Mường Ráng',
  ];

  protected readonly reviews: Review[] = [
    {
      quote: 'Tính năng ra đúng thời điểm kê khai, rất tuyệt. Chốt sổ mỗi giờ chỉ mất vài phút.',
      name: 'Phương Linh',
      role: 'Chủ cửa hàng tạp hóa',
    },
    {
      quote: 'Phần mềm thân thiện với người dùng và tự động hóa mọi thứ. Nhân viên đánh giá rất cao.',
      name: 'Linh Phạm',
      role: 'Chủ shop thời trang công sở',
    },
    {
      quote: 'Dễ dùng, mọi quy trình đều được tự động hóa. Quản lý cửa hàng nhàn hơn hẳn trước đây.',
      name: 'Chị Hoa',
      role: 'Chủ cửa hàng thời trang',
    },
    {
      quote: 'Báo cáo doanh thu nhanh, theo dõi nguyên liệu và ca kíp rất tiện. Tiết kiệm cả tiếng đồng hồ mỗi tối chốt sổ.',
      name: 'Anh Tuấn',
      role: 'Chủ chuỗi 5 cửa hàng F&B',
    },
    {
      quote: 'Lịch hẹn, hồ sơ khách và tính lương nhân viên đều trong một ứng dụng. Rất đáng đồng tiền bát gạo.',
      name: 'Chị Mai',
      role: 'Spa & làm đẹp',
    },
  ];

  protected readonly devices: Device[] = [
    { icon: 'pi pi-desktop', name: 'Trên máy tính', desc: 'Trình duyệt Chrome, Edge, Safari — mở là chạy.' },
    { icon: 'pi pi-tablet', name: 'Trên iPad & tablet', desc: 'Giao diện POS tối ưu cho màn hình cảm ứng.' },
    { icon: 'pi pi-mobile', name: 'Trên Android', desc: 'Quản cửa hàng mọi lúc, nhận thông báo đơn mới.' },
    { icon: 'pi pi-mobile', name: 'Trên iPhone', desc: 'Cùng hệ sinh thái, đồng bộ dữ liệu tức thì.' },
  ];

  protected readonly news: NewsItem[] = [
    {
      tag: 'Kinh nghiệm',
      date: '03/10/2026',
      title: 'Mùa cao điểm cận kề: checklist chốt doanh thu cuối năm cho chủ cửa hàng',
      icon: 'pi pi-megaphone',
    },
    {
      tag: 'Hướng dẫn',
      date: '28/09/2026',
      title: 'Xuất báo cáo bán hàng theo ca trong 3 bước trên Tryum',
      icon: 'pi pi-file-excel',
    },
    {
      tag: 'Mẹo hay',
      date: '15/09/2026',
      title: '5 mẹo quản kho giúp giảm 30% hàng lỗi và thất thoát',
      icon: 'pi pi-box',
    },
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

  protected readonly contactChannels: ContactChannel[] = [
    {
      icon: 'pi pi-phone',
      title: 'Hotline',
      main: 'Tư vấn: 1900 6868 · CSKH: 1900 6869',
      sub: 'Hoạt động 7:00–22:00, 365 ngày/năm kể cả lễ, tết.',
    },
    {
      icon: 'pi pi-facebook',
      title: 'Tryum Fanpage',
      main: 'Trang chủ chính thức trên Facebook',
      sub: 'Cập nhật tính năng mới và khuyến mãi mỗi tuần.',
    },
    {
      icon: 'pi pi-youtube',
      title: 'Kênh hỗ trợ YouTube',
      main: 'Video hướng dẫn sử dụng trực quan',
      sub: 'Giúp người dùng mới sử dụng Tryum dễ dàng, hiệu quả.',
    },
    {
      icon: 'pi pi-comments',
      title: 'Chat trên web & mobile',
      main: 'Trực chat 24/7',
      sub: 'Luôn có người trực để trả lời bạn nhanh nhất.',
    },
  ];

  protected readonly footerCols = [
    {
      title: 'Sản phẩm',
      links: [
        { label: 'Bán hàng POS', href: '#features' },
        { label: 'Quản kho', href: '#features' },
        { label: 'Báo cáo', href: '#features' },
        { label: 'Khách hàng', href: '#features' },
      ],
    },
    {
      title: 'Giải pháp',
      links: [
        { label: 'Bán buôn, bán lẻ', href: '#solutions' },
        { label: 'Ăn uống, giải trí', href: '#solutions' },
        { label: 'Sức khỏe, làm đẹp', href: '#solutions' },
        { label: 'Khách sạn, nhà nghỉ', href: '#solutions' },
      ],
    },
    {
      title: 'Hỗ trợ',
      links: [
        { label: 'Câu hỏi thường gặp', href: '#faq' },
        { label: 'Bảng giá', href: '#pricing' },
        { label: 'Đăng nhập', href: '/auth/login' },
        { label: 'Dùng thử miễn phí', href: '/auth/register' },
      ],
    },
  ];
}
