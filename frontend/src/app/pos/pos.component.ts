import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import {
  Component,
  ElementRef,
  HostListener,
  OnDestroy,
  OnInit,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { environment } from '../../environments/environment';
import { AuthService } from '../core/services/auth.service';
import {
  CreateOrderPayment,
  PaymentMethod,
  PosQrSession,
  SalesOrder,
  SalesService,
  StoreInfo,
  VietQrResponse,
} from '../core/services/sales.service';
import { readMoneyInWords } from '../shared/utils/vnd-words';
import { ToastService } from '../core/services/toast.service';
import {
  LocationOption,
  ShippingRate,
  ShippingService,
} from '../core/services/shipping.service';

type PosMode = 'Quick' | 'Regular' | 'Delivery';

interface ProductRow {
  id: string;
  sku: string;
  barcode: string | null;
  name: string;
  categoryPath: string | null;
  salePrice: number | null;
  costPrice: number | null;
  stock: number | null;
  imageId: string | null;
}

interface ProductPage {
  items: ProductRow[];
  total: number;
  page: number;
  pageSize: number;
}

interface ProductDraft {
  sku: string;
  name: string;
  barcode: string;
  salePrice: number;
  costPrice: number;
  stock: number;
}

const emptyProductDraft = (): ProductDraft => ({
  sku: '',
  name: '',
  barcode: '',
  salePrice: 0,
  costPrice: 0,
  stock: 0,
});

interface CartLine {
  product: ProductRow;
  quantity: number;
  unitPrice: number;
  discount: number;
}

interface CustomerRow {
  id: string;
  code: string;
  name: string;
  phone: string | null;
  email: string | null;
  address: string | null;
  region: string | null;
  ward: string | null;
  groupName: string | null;
  birthDate: string | null;
  gender: string | null;
  facebook: string | null;
  assignedTo: string | null;
  note: string | null;
  companyName: string | null;
  taxCode: string | null;
  invoiceAddress: string | null;
  points: number;
  debtAmount: number;
}

interface QuickCategory {
  label: string;
  value: string;
}

interface CustomerHistoryRow {
  id: string;
  code: string;
  createdAt: string;
  soldByName: string | null;
  total: number;
  paidAmount: number;
  status: string;
}

interface CustomerHistoryResponse {
  customer: CustomerRow;
  orders: CustomerHistoryRow[];
  total: number;
  page: number;
  pageSize: number;
  totalSales: number;
}

interface CustomerPage {
  items: CustomerRow[];
  total: number;
}

interface CustomerDraft {
  name: string;
  phone: string;
  email: string;
  address: string;
  region: string;
  ward: string;
  groupName: string;
  birthDate: string;
  gender: string;
  facebook: string;
  assignedTo: string;
  note: string;
  companyName: string;
  taxCode: string;
  invoiceAddress: string;
}

interface Ticket {
  id: string;
  label: string;
  cart: CartLine[];
  customer: CustomerRow | null;
  customerSearch: string;
  note: string;
  couponCode: string;
  discount: number;
  channel: PosMode;
  deliveryAddress: string;
}

interface SplitPayment {
  method: PaymentMethod;
  amount: number;
  /** Mã phiên QR đã trả (POS...) - BE dùng để đánh dấu khoản chuyển khoản đã xác nhận. */
  reference?: string;
}

interface PendingTransfer {
  order: SalesOrder;
  qr: VietQrResponse | null;
}

const emptyCustomerDraft = (): CustomerDraft => ({
  name: '',
  phone: '',
  email: '',
  address: '',
  region: '',
  ward: '',
  groupName: '',
  birthDate: '',
  gender: '',
  facebook: '',
  assignedTo: '',
  note: '',
  companyName: '',
  taxCode: '',
  invoiceAddress: '',
});

const newTicket = (index: number): Ticket => ({
  id: `ticket-${Date.now()}-${index}`,
  label: `Hóa đơn ${index}`,
  cart: [],
  customer: null,
  customerSearch: '',
  note: '',
  couponCode: '',
  discount: 0,
  channel: 'Quick',
  deliveryAddress: '',
});

const PAYMENT_METHODS: { value: PaymentMethod; label: string }[] = [
  { value: 'Cash', label: 'Tiền mặt' },
  { value: 'Transfer', label: 'Chuyển khoản' },
  { value: 'Card', label: 'Thẻ' },
  { value: 'Wallet', label: 'Ví' },
];

const ORDER_STATUS_LABEL: Record<string, string> = {
  Pending: 'Chờ thanh toán',
  PartiallyPaid: 'Thanh toán một phần',
  Completed: 'Đã thanh toán',
  Cancelled: 'Đã hủy',
};

@Component({
  selector: 'app-pos',
  imports: [FormsModule, DatePipe, RouterLink],
  templateUrl: './pos.component.html',
  styleUrl: './pos.component.scss',
})
export class PosComponent implements OnInit, OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly sales = inject(SalesService);
  private readonly shipping = inject(ShippingService);
  private readonly toast = inject(ToastService);
  readonly auth = inject(AuthService);

  /** Dùng trong template để tính số trang. */
  readonly Math = Math;

  private readonly searchInput = viewChild<ElementRef<HTMLInputElement>>('searchInput');
  private readonly money = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 0 });

  readonly methods = PAYMENT_METHODS;
  readonly pageSize = 40;

  // ── Hóa đơn đang mở (nhiều tab) ─────────────────────────────────────────
  readonly tickets = signal<Ticket[]>([newTicket(1)]);
  readonly activeIndex = signal(0);
  readonly activeTicket = computed(() => this.tickets()[this.activeIndex()] ?? this.tickets()[0]);
  readonly cart = computed(() => this.activeTicket()?.cart ?? []);
  readonly totalQuantity = computed(() =>
    this.cart().reduce((sum, line) => sum + line.quantity, 0),
  );
  readonly subtotal = computed(() =>
    this.cart().reduce((sum, line) => sum + line.unitPrice * line.quantity - line.discount, 0),
  );
  readonly discountAmount = computed(() =>
    Math.min(Math.max(0, this.activeTicket()?.discount ?? 0), this.subtotal()),
  );
  readonly usePoints = signal(false);
  readonly pointsRedeemed = computed(() => {
    if (!this.usePoints()) return 0;
    const customer = this.activeTicket()?.customer;
    if (!customer) return 0;
    const afterDiscount = Math.max(0, this.subtotal() - this.discountAmount());
    return Math.min(customer.points ?? 0, Math.floor(afterDiscount / 1_000));
  });
  readonly pointsDiscount = computed(() => this.pointsRedeemed() * 1_000);
  readonly total = computed(() =>
    Math.max(0, this.subtotal() - this.discountAmount() - this.pointsDiscount()
      + (this.activeTicket()?.channel === 'Delivery' ? this.shippingFee() : 0)),
  );

  // ── Giao hàng Goship ────────────────────────────────────────────────────
  readonly shippingFee = signal(0);
  readonly shippingCities = signal<LocationOption[]>([]);
  readonly shippingDistricts = signal<LocationOption[]>([]);
  readonly shippingWards = signal<LocationOption[]>([]);
  readonly deliveryCityId = signal('');
  readonly deliveryCityName = signal('');
  readonly deliveryDistrictId = signal('');
  readonly deliveryDistrictName = signal('');
  readonly deliveryWardId = signal('');
  readonly deliveryWardName = signal('');
  readonly shippingWeight = signal(500);
  readonly shippingLength = signal(20);
  readonly shippingWidth = signal(20);
  readonly shippingHeight = signal(10);
  readonly shippingRates = signal<ShippingRate[]>([]);
  readonly selectedShippingRate = signal<ShippingRate | null>(null);
  readonly shippingLoading = signal(false);
  readonly shippingError = signal('');
  readonly shippingLoaded = signal(false);
  readonly pickupOpen = signal(false);
  readonly pickupCities = signal<LocationOption[]>([]);
  readonly pickupDistricts = signal<LocationOption[]>([]);
  readonly pickupWards = signal<LocationOption[]>([]);
  readonly pickupCityId = signal('');
  readonly pickupDistrictId = signal('');
  readonly pickupWardId = signal('');
  readonly pickupSaving = signal(false);
  readonly pickupConfigured = signal(false);
  readonly deliveryPartnerTab = signal<'Goship' | 'Self'>('Goship');
  readonly selfShippingFee = signal(0);
  readonly insuranceEnabled = signal(false);
  readonly senderPaysShipping = signal(true);
  readonly selectedCategory = signal('');
  readonly quickCategories = computed<QuickCategory[]>(() => {
    const categories = new Map<string, string>();
    for (const product of this.products()) {
      const value = product.categoryPath?.trim();
      if (!value) continue;
      const label = value.split(/>>|>|\//).pop()?.trim() || value;
      categories.set(value, label);
    }
    return [...categories.entries()]
      .map(([value, label]) => ({ value, label }))
      .sort((a, b) => a.label.localeCompare(b.label, 'vi'))
      .slice(0, 18);
  });
  readonly quickEmpty = computed(() => this.activeTicket()?.channel === 'Quick' && this.cart().length === 0);
  readonly filteredProducts = computed(() =>
    this.selectedCategory()
      ? this.products().filter((product) => product.categoryPath === this.selectedCategory())
      : this.products(),
  );
  readonly clock = signal(new Date());

  // ── Danh mục sản phẩm ───────────────────────────────────────────────────
  readonly products = signal<ProductRow[]>([]);
  readonly productTotal = signal(0);
  readonly productPage = signal(1);
  readonly productsLoading = signal(false);
  readonly productsError = signal('');
  readonly search = signal('');
  readonly images = signal<Record<string, string>>({});
  readonly searchOpen = signal(false);
  readonly searchResults = signal<ProductRow[]>([]);
  readonly searchLoading = signal(false);
  readonly productDialogOpen = signal(false);
  readonly productDraft = signal<ProductDraft>(emptyProductDraft());
  readonly productSaving = signal(false);
  readonly productError = signal('');
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  // ── Khách hàng ──────────────────────────────────────────────────────────
  readonly customerPickerOpen = signal(false);
  readonly customerSearch = signal('');
  readonly customerResults = signal<CustomerRow[]>([]);
  readonly customerLoading = signal(false);
  readonly customerError = signal('');
  private customerSearchTimer: ReturnType<typeof setTimeout> | null = null;
  private customerCache = new Map<string, CustomerRow[]>();
  private customerRequestId = 0;

  readonly customerDialogOpen = signal(false);
  readonly customerDetailOpen = signal(false);
  readonly customerDetailLoading = signal(false);
  readonly customerDetail = signal<CustomerHistoryResponse | null>(null);
  readonly customerDetailTab = signal<'info' | 'invoice' | 'history' | 'debt'>('info');
  readonly customerHistoryPage = signal(1);
  readonly customerDialogTab = signal<'general' | 'invoice'>('general');
  readonly customerDraft = signal<CustomerDraft>(emptyCustomerDraft());
  readonly customerSaveLoading = signal(false);

  // ── Thanh toán ──────────────────────────────────────────────────────────
  readonly paymentMethod = signal<PaymentMethod>('Cash');
  readonly receivedOverride = signal<number | null>(null);
  readonly splitPayments = signal<SplitPayment[]>([]);
  readonly creditOverpayment = signal(false);
  readonly busy = signal(false);
  /** Sidebar thanh toán chỉ mở sau khi bấm THANH TOÁN (giống KiotViet/Bách Hoá Xanh). */
  readonly checkoutPanelOpen = signal(false);

  readonly paymentDialogOpen = signal(false);
  readonly draftMethod = signal<PaymentMethod>('Cash');
  readonly draftAmount = signal<number>(0);
  readonly draftLines = signal<SplitPayment[]>([]);
  readonly paymentMenuOpen = signal(false);

  readonly draftAllocated = computed(() =>
    this.draftLines().reduce((sum, line) => sum + line.amount, 0),
  );
  /** Số còn thiếu sau các khoản đã thêm - đây chính là số tiền QR động sẽ thu. */
  readonly draftRemaining = computed(() => Math.max(0, this.total() - this.draftAllocated()));

  // QR động cho phần còn lại trong thanh toán kết hợp (Bách Hóa Xanh style).
  readonly qrVisible = signal(false);
  readonly qrSession = signal<PosQrSession | null>(null);
  readonly qrLoading = signal(false);
  readonly qrError = signal('');
  readonly lastQrReceived = signal<number | null>(null);
  private qrPollTimer: ReturnType<typeof setInterval> | null = null;
  private qrActiveAmount = 0;
  private readonly handledQrSessions = new Set<string>();

  // QR hiện ngay trong panel khi chọn "Chuyển khoản" (một phương thức).
  readonly transferQr = signal<PosQrSession | null>(null);
  readonly transferQrLoading = signal(false);
  readonly transferQrError = signal('');
  private transferQrTimer: ReturnType<typeof setTimeout> | null = null;
  private transferQrPoll: ReturnType<typeof setInterval> | null = null;
  private transferQrAmount = 0;

  constructor() {
    effect(() => {
      const method = this.paymentMethod();
      const hasSplit = this.splitPayments().length > 0;
      const total = this.total();
      const received = this.receivedOverride() ?? total;
      if (method === 'Transfer' && !hasSplit && total > 0) {
        untracked(() => this.scheduleTransferQr(Math.min(Math.max(received, 0), total)));
      } else {
        untracked(() => this.stopTransferQr());
      }
    });
  }

  readonly paid = computed(() => {
    if (this.splitPayments().length) {
      return this.splitPayments().reduce((sum, line) => sum + line.amount, 0);
    }
    return this.receivedOverride() ?? this.total();
  });
  readonly change = computed(() => Math.max(0, this.paid() - this.total()));
  readonly remaining = computed(() => Math.max(0, this.total() - this.paid()));

  // ── VietQR / chờ chuyển khoản ───────────────────────────────────────────
  readonly pendingTransfers = signal<PendingTransfer[]>([]);
  readonly vietQrOpen = signal(false);
  readonly vietQrLoading = signal(false);
  readonly vietQrError = signal('');
  readonly activeTransfer = signal<PendingTransfer | null>(null);
  readonly checkingTransfer = signal(false);
  private pollTimer: ReturnType<typeof setInterval> | null = null;

  readonly successOrder = signal<SalesOrder | null>(null);
  readonly lastPrintedOrder = signal<SalesOrder | null>(null);
  readonly store = signal<StoreInfo | null>(null);

  private clockTimer: ReturnType<typeof setInterval> | null = null;

  ngOnInit(): void {
    this.loadProducts();
    this.sales.getStore().subscribe({ next: (store) => this.store.set(store) });
    this.clockTimer = setInterval(() => this.clock.set(new Date()), 30_000);
  }

  ngOnDestroy(): void {
    if (this.searchTimer) clearTimeout(this.searchTimer);
    if (this.customerSearchTimer) clearTimeout(this.customerSearchTimer);
    if (this.clockTimer) clearInterval(this.clockTimer);
    this.stopQr(true);
    this.stopTransferQr();
    this.stopPolling();
    for (const url of Object.values(this.images())) URL.revokeObjectURL(url);
  }

  // ── Helpers ─────────────────────────────────────────────────────────────

  fmt(value: number | null | undefined): string {
    return this.money.format(Math.round(value ?? 0));
  }

  toNumber(value: unknown): number {
    const parsed = Number(String(value ?? '').replace(/[^\d.-]/g, ''));
    return Number.isFinite(parsed) ? parsed : 0;
  }

  statusLabel(status: string): string {
    return ORDER_STATUS_LABEL[status] ?? status;
  }

  private messageOf(error: unknown, fallback: string): string {
    const body = (error as { error?: { message?: string; detail?: string } })?.error;
    return body?.message || body?.detail || fallback;
  }

  get sellerName(): string {
    return this.auth.user()?.fullName ?? 'Thu ngân';
  }

  // ── Tabs hóa đơn ────────────────────────────────────────────────────────

  selectTicket(index: number): void {
    this.activeIndex.set(index);
    this.resetPaymentDraft();
  }

  addTicket(): void {
    const next = [...this.tickets(), newTicket(this.tickets().length + 1)];
    this.tickets.set(next);
    this.activeIndex.set(next.length - 1);
    this.resetPaymentDraft();
    setTimeout(() => this.searchInput()?.nativeElement.focus());
  }

  closeTicket(index: number): void {
    if (this.tickets().length === 1) {
      this.clearCart();
      return;
    }
    const next = this.tickets().filter((_, i) => i !== index);
    this.tickets.set(next);
    this.activeIndex.set(Math.max(0, Math.min(this.activeIndex(), next.length - 1)));
  }

  private patchTicket(patch: Partial<Ticket>): void {
    const index = this.activeIndex();
    this.tickets.set(this.tickets().map((ticket, i) => (i === index ? { ...ticket, ...patch } : ticket)));
  }

  setChannel(channel: PosMode): void {
    this.patchTicket({ channel });
    if (channel === 'Delivery') {
      this.initShipping();
    } else {
      this.shippingFee.set(0);
      this.selectedShippingRate.set(null);
    }
  }

  selectQuickCategory(category: string): void {
    this.selectedCategory.set(category);
  }

  // ── Sản phẩm ────────────────────────────────────────────────────────────

  loadProducts(): void {
    this.productsLoading.set(true);
    this.productsError.set('');
    const params: Record<string, string | number> = {
      page: this.productPage(),
      pageSize: this.pageSize,
    };
    const term = this.search().trim();
    if (term) params['search'] = term;

    this.http
      .get<ProductPage>(`${environment.apiUrl}/api/v1/products`, { params })
      .subscribe({
        next: (result) => {
          for (const url of Object.values(this.images())) URL.revokeObjectURL(url);
          this.images.set({});
          this.products.set(result.items);
          this.productTotal.set(result.total);
          this.productsLoading.set(false);
          for (const product of result.items) {
            if (!product.imageId || product.imageId === '00000000-0000-0000-0000-000000000000') continue;
            this.http
              .get(
                `${environment.apiUrl}/api/v1/products/${product.id}/images/${product.imageId}`,
                { responseType: 'blob' },
              )
              .subscribe({
                next: (blob) => {
                  this.images.update((current) => ({
                    ...current,
                    [product.id]: URL.createObjectURL(blob),
                  }));
                },
              });
          }
        },
        error: () => {
          this.products.set([]);
          this.productTotal.set(0);
          this.productsLoading.set(false);
          this.productsError.set('Không tải được hàng hóa. Kiểm tra kết nối rồi thử lại.');
        },
      });
  }

  onSearchChanged(value: string): void {
    this.search.set(value);
    const term = value.trim();
    this.searchOpen.set(term.length > 0);
    if (this.searchTimer) clearTimeout(this.searchTimer);
    if (!term) {
      this.searchResults.set([]);
      this.searchLoading.set(false);
      this.productPage.set(1);
      this.loadProducts();
      return;
    }

    this.searchTimer = setTimeout(() => {
      this.productPage.set(1);
      this.loadProducts();
      this.loadSearchResults(term);
    }, 250);
  }

  private loadSearchResults(term: string): void {
    this.searchLoading.set(true);
    this.http
      .get<ProductPage>(`${environment.apiUrl}/api/v1/products`, {
        params: { page: 1, pageSize: 8, search: term },
      })
      .subscribe({
        next: (result) => {
          this.searchResults.set(result.items);
          this.searchLoading.set(false);
        },
        error: () => {
          this.searchResults.set([]);
          this.searchLoading.set(false);
        },
      });
  }

  selectSearchResult(product: ProductRow): void {
    this.addProduct(product);
    this.closeSearch();
  }

  closeSearch(): void {
    this.searchOpen.set(false);
    this.search.set('');
    this.searchResults.set([]);
    this.productPage.set(1);
    this.loadProducts();
  }

  // ── Thêm nhanh hàng hóa từ màn hình bán hàng ─────────────────────────────

  openCreateProduct(): void {
    this.productDraft.set(emptyProductDraft());
    this.productError.set('');
    this.searchOpen.set(false);
    this.productDialogOpen.set(true);
  }

  patchProductDraft(field: keyof ProductDraft, value: unknown): void {
    this.productDraft.update((draft) => ({
      ...draft,
      [field]: field === 'sku' || field === 'name' || field === 'barcode'
        ? String(value ?? '')
        : Math.max(0, this.toNumber(value)),
    }));
  }

  saveProduct(): void {
    const draft = this.productDraft();
    const sku = draft.sku.trim();
    const name = draft.name.trim();
    if (!sku || !name) {
      this.productError.set('Mã hàng và tên hàng là bắt buộc.');
      return;
    }

    this.productSaving.set(true);
    this.productError.set('');
    this.http
      .post<ProductRow>(`${environment.apiUrl}/api/v1/products`, {
        sku,
        name,
        barcode: draft.barcode.trim() || null,
        itemType: 'Hàng hóa',
        salePrice: draft.salePrice,
        costPrice: draft.costPrice,
        stock: draft.stock,
      })
      .subscribe({
        next: (created) => {
          this.productSaving.set(false);
          this.productDialogOpen.set(false);
          this.products.update((list) => [created, ...list]);
          this.addProduct(created);
          this.toast.add({
            severity: 'success',
            summary: 'Đã thêm hàng hóa',
            detail: `${created.name} đã được thêm vào hóa đơn.`,
          });
        },
        error: (error: unknown) => {
          this.productSaving.set(false);
          this.productError.set(this.messageOf(error, 'Không lưu được hàng hóa.'));
        },
      });
  }

  previousProducts(): void {
    if (this.productPage() <= 1) return;
    this.productPage.update((page) => page - 1);
    this.loadProducts();
  }

  nextProducts(): void {
    if (this.productPage() * this.pageSize >= this.productTotal()) return;
    this.productPage.update((page) => page + 1);
    this.loadProducts();
  }

  /** Quét mã vạch / nhập mã rồi Enter: khớp chính xác thì thêm thẳng vào hóa đơn. */
  submitSearch(event?: Event): void {
    event?.preventDefault();
    const term = this.search().trim().toLocaleLowerCase('vi-VN');
    if (!term) return;

    const local = [...this.searchResults(), ...this.products()].find(
      (item) =>
        item.sku.toLocaleLowerCase('vi-VN') === term ||
        item.barcode?.toLocaleLowerCase('vi-VN') === term,
    );
    if (local) {
      this.addProduct(local);
      this.clearSearch();
      return;
    }

    this.http
      .get<ProductPage>(`${environment.apiUrl}/api/v1/products`, {
        params: { page: 1, pageSize: 20, search: term },
      })
      .subscribe({
        next: (result) => {
          const exact = result.items.find(
            (item) =>
              item.sku.toLocaleLowerCase('vi-VN') === term ||
              item.barcode?.toLocaleLowerCase('vi-VN') === term,
          );
          const target = exact ?? (result.items.length === 1 ? result.items[0] : null);
          if (target) {
            this.addProduct(target);
            this.clearSearch();
          } else {
            this.onSearchChanged(this.search());
          }
        },
        error: () => this.toast.add({ severity: 'error', summary: 'Không tìm được hàng hóa' }),
      });
  }

  private clearSearch(): void {
    this.search.set('');
    this.searchOpen.set(false);
    this.searchResults.set([]);
    this.productPage.set(1);
    this.loadProducts();
  }

  // ── Giỏ hàng ────────────────────────────────────────────────────────────

  addProduct(product: ProductRow): void {
    const existing = this.cart().find((line) => line.product.id === product.id);
    if (existing) {
      this.setQuantity(product.id, existing.quantity + 1);
    } else {
      const line: CartLine = {
        product,
        quantity: 1,
        unitPrice: product.salePrice ?? 0,
        discount: 0,
      };
      this.patchTicket({ cart: [...this.cart(), line] });
    }
    this.receivedOverride.set(null);
  }

  setQuantity(productId: string, quantity: number): void {
    const next = Math.max(1, Math.min(1_000_000, Math.round(quantity || 1)));
    this.patchTicket({
      cart: this.cart().map((line) =>
        line.product.id === productId ? { ...line, quantity: next } : line,
      ),
    });
    this.receivedOverride.set(null);
  }

  changeQuantity(productId: string, delta: number): void {
    const line = this.cart().find((item) => item.product.id === productId);
    if (!line) return;
    const next = line.quantity + delta;
    if (next <= 0) this.removeLine(productId);
    else this.setQuantity(productId, next);
  }

  setUnitPrice(productId: string, price: number): void {
    this.patchTicket({
      cart: this.cart().map((line) =>
        line.product.id === productId
          ? { ...line, unitPrice: Math.max(0, this.toNumber(price)) }
          : line,
      ),
    });
  }

  setLineDiscount(productId: string, discount: number): void {
    this.patchTicket({
      cart: this.cart().map((line) =>
        line.product.id === productId
          ? {
              ...line,
              discount: Math.min(
                Math.max(0, this.toNumber(discount)),
                line.unitPrice * line.quantity,
              ),
            }
          : line,
      ),
    });
  }

  removeLine(productId: string): void {
    this.patchTicket({ cart: this.cart().filter((line) => line.product.id !== productId) });
  }

  clearCart(): void {
    this.patchTicket({ cart: [], discount: 0, couponCode: '', note: '' });
    this.resetPaymentDraft();
  }

  setOrderDiscount(value: number): void {
    this.patchTicket({ discount: Math.max(0, this.toNumber(value)) });
    this.receivedOverride.set(null);
  }

  setCoupon(value: string): void {
    this.patchTicket({ couponCode: value.trim().toUpperCase() });
  }

  setNote(value: string): void {
    this.patchTicket({ note: value });
  }

  setDeliveryAddress(value: string): void {
    this.patchTicket({ deliveryAddress: value });
  }

  initShipping(): void {
    if (this.shippingLoaded()) return;
    this.shippingLoaded.set(true);
    this.shipping.cities().subscribe({
      next: (response) => this.shippingCities.set(response.cities),
      error: () => this.shippingError.set('Không tải được tỉnh/thành từ Goship.'),
    });
    this.shipping.pickup().subscribe({
      next: (pickup) => {
        this.pickupConfigured.set(!!(pickup.cityId && pickup.districtId && pickup.wardId));
        this.pickupCityId.set(pickup.cityId ?? '');
        this.pickupDistrictId.set(pickup.districtId ?? '');
        this.pickupWardId.set(pickup.wardId ?? '');
      },
    });
  }

  onDeliveryCityChange(cityId: string): void {
    const city = this.shippingCities().find((item) => item.id === cityId);
    this.deliveryCityId.set(cityId);
    this.deliveryCityName.set(city?.name ?? '');
    this.deliveryDistrictId.set('');
    this.deliveryDistrictName.set('');
    this.deliveryWardId.set('');
    this.deliveryWardName.set('');
    this.shippingDistricts.set([]);
    this.shippingWards.set([]);
    this.shippingRates.set([]);
    if (!cityId) return;
    this.shipping.districts(cityId).subscribe({
      next: (response) => this.shippingDistricts.set(response.districts),
      error: () => this.shippingError.set('Không tải được quận/huyện.'),
    });
  }

  onDeliveryDistrictChange(districtId: string): void {
    const district = this.shippingDistricts().find((item) => item.id === districtId);
    this.deliveryDistrictId.set(districtId);
    this.deliveryDistrictName.set(district?.name ?? '');
    this.deliveryWardId.set('');
    this.deliveryWardName.set('');
    this.shippingWards.set([]);
    if (!districtId) return;
    this.shipping.wards(districtId).subscribe({
      next: (response) => this.shippingWards.set(response.wards),
      error: () => this.shippingError.set('Không tải được phường/xã.'),
    });
  }

  onDeliveryWardChange(wardId: string): void {
    const ward = this.shippingWards().find((item) => item.id === wardId);
    this.deliveryWardId.set(wardId);
    this.deliveryWardName.set(ward?.name ?? '');
  }

  quoteShipping(): void {
    this.shippingError.set('');
    const ticket = this.activeTicket();
    if (!ticket) return;
    if (!this.pickupConfigured()) {
      this.pickupOpen.set(true);
      this.shippingError.set('Chọn điểm lấy hàng của cửa hàng trước.');
      return;
    }
    if (!this.deliveryCityId() || !this.deliveryDistrictId() || !this.deliveryWardId()) {
      this.shippingError.set('Chọn đủ Tỉnh/Quận/Phường của người nhận.');
      return;
    }
    if (!ticket.deliveryAddress.trim()) {
      this.shippingError.set('Nhập địa chỉ giao hàng.');
      return;
    }
    this.shippingLoading.set(true);
    this.shippingRates.set([]);
    this.selectedShippingRate.set(null);
    this.shippingFee.set(0);
    this.shipping.rates({
      toCityId: this.deliveryCityId(),
      toDistrictId: this.deliveryDistrictId(),
      weightGrams: Math.max(1, this.shippingWeight()),
      lengthCm: this.shippingLength(),
      widthCm: this.shippingWidth(),
      heightCm: this.shippingHeight(),
      codAmount: Math.max(0, this.total() - this.shippingFee()),
    }).subscribe({
      next: (response) => {
        this.shippingLoading.set(false);
        this.shippingRates.set(response.rates);
        if (!response.rates.length) this.shippingError.set('Không có hãng nào nhận tuyến này.');
      },
      error: (error: unknown) => {
        this.shippingLoading.set(false);
        this.shippingError.set(this.messageOf(error, 'Không lấy được bảng giá vận chuyển.'));
      },
    });
  }

  selectShippingRate(rate: ShippingRate): void {
    this.deliveryPartnerTab.set('Goship');
    this.selectedShippingRate.set(rate);
    this.shippingFee.set(rate.totalFee);
  }

  selectDeliveryPartner(tab: 'Goship' | 'Self'): void {
    this.deliveryPartnerTab.set(tab);
    if (tab === 'Self') {
      this.selectedShippingRate.set(null);
      this.shippingFee.set(Math.max(0, this.selfShippingFee()));
    } else {
      this.shippingFee.set(this.selectedShippingRate()?.totalFee ?? 0);
    }
  }

  setSelfShippingFee(value: number): void {
    this.selfShippingFee.set(Math.max(0, value));
    if (this.deliveryPartnerTab() === 'Self') this.shippingFee.set(Math.max(0, value));
  }

  openPickupConfig(): void {
    this.pickupOpen.update((open) => !open);
    if (!this.pickupCities().length) {
      this.shipping.cities().subscribe({
        next: (response) => this.pickupCities.set(response.cities),
        error: () => this.shippingError.set('Không tải được tỉnh/thành.'),
      });
    }
  }

  onPickupCityChange(cityId: string): void {
    this.pickupCityId.set(cityId);
    this.pickupDistrictId.set('');
    this.pickupWardId.set('');
    this.pickupDistricts.set([]);
    this.pickupWards.set([]);
    if (!cityId) return;
    this.shipping.districts(cityId).subscribe({
      next: (response) => this.pickupDistricts.set(response.districts),
    });
  }

  onPickupDistrictChange(districtId: string): void {
    this.pickupDistrictId.set(districtId);
    this.pickupWardId.set('');
    this.pickupWards.set([]);
    if (!districtId) return;
    this.shipping.wards(districtId).subscribe({
      next: (response) => this.pickupWards.set(response.wards),
    });
  }

  savePickup(): void {
    if (!this.pickupCityId() || !this.pickupDistrictId() || !this.pickupWardId()) {
      this.shippingError.set('Chọn đủ Tỉnh/Quận/Phường lấy hàng.');
      return;
    }
    this.pickupSaving.set(true);
    this.shipping.savePickup({
      cityId: this.pickupCityId(),
      districtId: this.pickupDistrictId(),
      wardId: this.pickupWardId(),
    }).subscribe({
      next: () => {
        this.pickupSaving.set(false);
        this.pickupConfigured.set(true);
        this.pickupOpen.set(false);
        this.toast.add({ severity: 'success', summary: 'Đã lưu điểm lấy hàng' });
      },
      error: (error: unknown) => {
        this.pickupSaving.set(false);
        this.shippingError.set(this.messageOf(error, 'Không lưu được điểm lấy hàng.'));
      },
    });
  }

  private bookShipment(order: SalesOrder, ticket: Ticket): void {
    const rate = this.selectedShippingRate();
    if (ticket.channel !== 'Delivery' || !rate) return;
    this.shipping.createShipment({
      orderId: order.id,
      rateId: rate.id,
      toName: ticket.customer?.name ?? 'Khách lẻ',
      toPhone: ticket.customer?.phone ?? '',
      toAddress: ticket.deliveryAddress,
      toCityId: this.deliveryCityId(),
      toCityName: this.deliveryCityName(),
      toDistrictId: this.deliveryDistrictId(),
      toDistrictName: this.deliveryDistrictName(),
      toWardId: this.deliveryWardId(),
      toWardName: this.deliveryWardName(),
      weightGrams: Math.max(1, this.shippingWeight()),
      lengthCm: this.shippingLength(),
      widthCm: this.shippingWidth(),
      heightCm: this.shippingHeight(),
      codAmount: Math.max(0, order.remaining),
      declaredAmount: this.insuranceEnabled() ? Math.max(0, order.total) : 0,
      senderPaysShipping: this.senderPaysShipping(),
      service: rate.service,
      expected: rate.expected,
      note: ticket.note || null,
    }).subscribe({
      next: () => this.toast.add({ severity: 'success', summary: 'Đã tạo vận đơn Goship' }),
      error: (error: unknown) => this.toast.add({
        severity: 'warn',
        summary: 'Hóa đơn đã lưu nhưng chưa tạo được vận đơn',
        detail: this.messageOf(error, 'Kiểm tra lại điểm lấy hàng Goship.'),
      }),
    });
  }

  lineTotal(line: CartLine): number {
    return Math.max(0, line.unitPrice * line.quantity - line.discount);
  }

  // ── Khách hàng ──────────────────────────────────────────────────────────

  openCustomerPicker(): void {
    this.customerPickerOpen.set(true);
    this.customerError.set('');
    if (!this.customerSearch()) this.customerSearch.set('');
    const term = this.customerSearch().trim();
    if (this.customerCache.has(term)) {
      this.customerResults.set(this.customerCache.get(term) ?? []);
      return;
    }
    this.loadCustomers();
  }

  onCustomerSearchChanged(value: string): void {
    this.customerSearch.set(value);
    const term = value.trim();
    if (!term) {
      this.customerPickerOpen.set(false);
      this.customerResults.set([]);
      return;
    }

    this.customerPickerOpen.set(true);
    if (this.customerSearchTimer) clearTimeout(this.customerSearchTimer);
    this.customerSearchTimer = setTimeout(() => this.loadCustomers(), 350);
  }

  loadCustomers(): void {
    const term = this.customerSearch().trim();
    if (this.customerCache.has(term)) {
      this.customerResults.set(this.customerCache.get(term) ?? []);
      this.customerLoading.set(false);
      return;
    }

    this.customerLoading.set(true);
    this.customerError.set('');
    const params: Record<string, string | number> = { page: 1, pageSize: 10 };
    if (term) params['search'] = term;
    const requestId = ++this.customerRequestId;

    this.http
      .get<CustomerPage>(`${environment.apiUrl}/api/v1/customers`, { params })
      .subscribe({
        next: (result) => {
          if (requestId !== this.customerRequestId) return;
          this.customerCache.set(term, result.items);
          this.customerResults.set(result.items);
          this.customerLoading.set(false);
        },
        error: () => {
          if (requestId !== this.customerRequestId) return;
          this.customerResults.set([]);
          this.customerLoading.set(false);
          this.customerError.set('Không tải được khách hàng.');
        },
      });
  }

  selectCustomer(customer: CustomerRow): void {
    this.patchTicket({ customer, customerSearch: customer.name });
    this.customerPickerOpen.set(false);
    this.usePoints.set(false);
    this.creditOverpayment.set(false);
    this.receivedOverride.set(null);
    this.openCustomerDetail(customer);
  }

  openCustomerDetail(customer: CustomerRow, page = 1): void {
    this.customerDetailOpen.set(true);
    this.customerDetailLoading.set(true);
    this.customerHistoryPage.set(page);
    this.customerDetailTab.set('info');
    this.http
      .get<CustomerHistoryResponse>(
        `${environment.apiUrl}/api/v1/customers/${customer.id}/history?page=${page}&pageSize=7`,
      )
      .subscribe({
        next: (detail) => {
          this.customerDetailLoading.set(false);
          this.customerDetail.set(detail);
        },
        error: () => {
          this.customerDetailLoading.set(false);
          this.customerDetail.set(null);
          this.toast.add({ severity: 'warn', summary: 'Không tải được lịch sử khách hàng' });
        },
      });
  }

  customerHistoryGo(page: number): void {
    const detail = this.customerDetail();
    if (!detail) return;
    this.openCustomerDetail(detail.customer, page);
  }

  customerHistoryPages(detail: CustomerHistoryResponse): number {
    return Math.max(1, Math.ceil(detail.total / detail.pageSize));
  }

  customerStatusLabel(status: string): string {
    return status === 'Completed' ? 'Hoàn thành' : status === 'Cancelled' ? 'Đã hủy' : 'Đang xử lý';
  }

  clearCustomer(): void {
    this.patchTicket({ customer: null, customerSearch: '' });
    this.usePoints.set(false);
    this.creditOverpayment.set(false);
  }

  /** Bật/tắt dùng điểm tích lũy (1 điểm = 1.000đ). */
  togglePoints(): void {
    if (!this.usePoints()) {
      const customer = this.activeTicket()?.customer;
      if (!customer || (customer.points ?? 0) <= 0) {
        this.toast.add({ severity: 'warn', summary: 'Khách hàng chưa có điểm để dùng' });
        return;
      }
    }
    this.usePoints.update((value) => !value);
    this.receivedOverride.set(null);
    this.creditOverpayment.set(false);
  }

  openCreateCustomer(): void {
    this.customerDraft.set(emptyCustomerDraft());
    this.customerDialogTab.set('general');
    this.customerPickerOpen.set(false);
    this.customerDialogOpen.set(true);
  }

  customerAvatarNotice(): void {
    this.toast.add({
      severity: 'info',
      summary: 'Chưa hỗ trợ ảnh khách hàng',
      detail: 'Phiên bản này chưa lưu ảnh đại diện khách hàng.',
    });
  }

  patchDraft(field: keyof CustomerDraft, value: unknown): void {
    this.customerDraft.update((draft) => ({ ...draft, [field]: String(value ?? '') }));
  }

  saveCustomer(): void {
    const draft = this.customerDraft();
    if (!draft.name.trim()) {
      this.customerError.set('Vui lòng nhập tên khách hàng.');
      this.customerDialogTab.set('general');
      return;
    }
    this.customerSaveLoading.set(true);
    this.customerError.set('');
    const payload = Object.fromEntries(
      Object.entries(draft).map(([key, value]) => [key, value.trim() || null]),
    );
    this.http
      .post<CustomerRow>(`${environment.apiUrl}/api/v1/customers`, payload)
      .subscribe({
        next: (created) => {
          this.customerSaveLoading.set(false);
          this.customerDialogOpen.set(false);
          this.customerCache.clear();
          this.selectCustomer(created);
          this.toast.add({
            severity: 'success',
            summary: 'Đã thêm khách hàng',
            detail: `${created.name} đã được lưu.`,
          });
        },
        error: (error: unknown) => {
          this.customerSaveLoading.set(false);
          this.customerError.set(this.messageOf(error, 'Không lưu được khách hàng.'));
        },
      });
  }

  // ── Thanh toán ──────────────────────────────────────────────────────────

  setPaymentMethod(method: PaymentMethod): void {
    this.paymentMethod.set(method);
    this.splitPayments.set([]);
    this.receivedOverride.set(null);
  }

  setReceived(value: unknown): void {
    this.receivedOverride.set(Math.max(0, this.toNumber(value)));
    this.splitPayments.set([]);
  }

  quickAmounts(): number[] {
    const total = this.total();
    const suggestions = new Set<number>([total, Math.ceil(total / 10_000) * 10_000]);
    for (const step of [10_000, 50_000, 100_000, 500_000]) {
      if (total > 0) suggestions.add(Math.ceil(total / step) * step + step);
    }
    return [...suggestions]
      .filter((value) => value >= total)
      .sort((a, b) => a - b)
      .slice(0, 4);
  }

  setQuickAmount(value: number): void {
    this.splitPayments.set([]);
    this.receivedOverride.set(value);
  }

  togglePaymentMenu(): void {
    this.paymentMenuOpen.update((open) => !open);
  }

  closePaymentMenu(): void {
    this.paymentMenuOpen.set(false);
  }

  openSplitFromMenu(): void {
    this.paymentMenuOpen.set(false);
    this.openPaymentDialog();
  }

  clearSplitPayments(): void {
    this.paymentMenuOpen.set(false);
    this.splitPayments.set([]);
    this.resetPaymentDraft();
  }

  openPaymentDialog(): void {
    if (!this.cart().length) return;
    this.paymentMenuOpen.set(false);
    this.stopQr(true);
    this.lastQrReceived.set(null);
    this.draftLines.set(this.splitPayments().map((line) => ({ ...line })));
    this.draftMethod.set(this.paymentMethod());
    const allocated = this.splitPayments().reduce((sum, line) => sum + line.amount, 0);
    this.draftAmount.set(Math.max(0, this.total() - allocated));
    this.paymentDialogOpen.set(true);
  }

  closePaymentDialog(): void {
    this.stopQr(true);
    this.paymentDialogOpen.set(false);
  }

  addDraftLine(): void {
    const amount = Math.round(this.draftAmount());
    if (amount <= 0) {
      this.toast.add({ severity: 'warn', summary: 'Số tiền phải lớn hơn 0' });
      return;
    }
    const method = this.draftMethod();
    const current = this.draftLines();
    const merged = current.map((line) => ({ ...line }));
    const index = merged.findIndex((line) => line.method === method && !line.reference);
    if (index >= 0) merged[index].amount += amount;
    else merged.push({ method, amount });
    this.draftLines.set(merged);
    this.draftAmount.set(Math.max(0, this.total() - this.draftAllocated()));
    this.syncQr();
  }

  removeDraftLine(index: number): void {
    this.draftLines.set(this.draftLines().filter((_, i) => i !== index));
    this.draftAmount.set(Math.max(0, this.total() - this.draftAllocated()));
    this.syncQr();
  }

  applyDraftLines(): void {
    // Nếu người dùng còn số tiền chưa bấm "Thêm" thì ghi nhận luôn khoản đó.
    if (this.draftAmount() > 0) this.addDraftLine();

    const lines = this.draftLines();
    this.stopQr(true);
    if (!lines.length) {
      this.splitPayments.set([]);
      this.paymentDialogOpen.set(false);
      return;
    }

    this.splitPayments.set(lines);
    this.paymentMethod.set(lines[0].method);
    this.receivedOverride.set(null);
    this.paymentDialogOpen.set(false);
  }

  resetPaymentDraft(): void {
    this.stopQr(true);
    this.stopTransferQr();
    this.splitPayments.set([]);
    this.receivedOverride.set(null);
    this.paymentMethod.set('Cash');
    this.usePoints.set(false);
    this.creditOverpayment.set(false);
  }

  draftTotalPaid(): number {
    return this.draftLines().reduce((sum, line) => sum + line.amount, 0) + this.draftAmount();
  }

  // ── QR động cho phần còn lại ────────────────────────────────────────────

  toggleQr(): void {
    if (this.qrVisible()) {
      this.stopQr(true);
      return;
    }
    const amount = this.draftRemaining();
    if (amount <= 0) return;
    this.qrVisible.set(true);
    this.startQr(amount);
  }

  private startQr(amount: number): void {
    this.stopQrWatch();
    this.qrActiveAmount = amount;
    this.qrSession.set(null);
    this.qrError.set('');
    this.qrLoading.set(true);

    this.sales.createPosQr(amount).subscribe({
      next: (session) => {
        if (this.qrActiveAmount !== amount) return;
        this.qrLoading.set(false);
        this.qrSession.set(session);
        if (session.status === 'Paid') {
          this.applyQrPayment(session);
          return;
        }
        this.startQrPolling(session.id);
      },
      error: (error: unknown) => {
        if (this.qrActiveAmount !== amount) return;
        this.qrLoading.set(false);
        this.qrError.set(this.messageOf(error, 'Không tạo được mã QR.'));
      },
    });
  }

  private startQrPolling(sessionId: string): void {
    this.stopQrWatch();
    this.qrPollTimer = setInterval(() => {
      this.sales.getPosQr(sessionId).subscribe({
        next: (session) => {
          if (session.status === 'Paid') {
            this.qrSession.set(session);
            this.applyQrPayment(session);
          } else {
            this.qrSession.set(session);
          }
        },
        error: () => this.stopQrWatch(),
      });
    }, 2500);
  }

  /** Webhook báo tiền vào: tự thêm khoản chuyển khoản, không cần bấm thêm. */
  private applyQrPayment(session: PosQrSession): void {
    if (this.handledQrSessions.has(session.id)) return;
    this.handledQrSessions.add(session.id);

    const received = Math.min(
      session.transferredAmount ?? session.amount,
      this.draftRemaining(),
    );
    if (received <= 0) {
      this.stopQr(true);
      return;
    }

    this.draftLines.update((lines) => [
      ...lines,
      { method: 'Transfer', amount: received, reference: session.reference },
    ]);
    this.draftAmount.set(Math.max(0, this.total() - this.draftAllocated()));
    this.lastQrReceived.set(received);

    if (this.draftRemaining() <= 0) {
      this.stopQr(true);
    } else {
      this.startQr(this.draftRemaining());
    }
  }

  /** Giữ QR khớp số còn thiếu khi thêm/bớt khoản thanh toán. */
  private syncQr(): void {
    if (!this.qrVisible()) return;
    const amount = this.draftRemaining();
    if (amount <= 0) {
      this.stopQr(true);
      return;
    }
    if (amount !== this.qrActiveAmount) this.startQr(amount);
  }

  private stopQr(clearVisible: boolean): void {
    this.stopQrWatch();
    this.qrActiveAmount = 0;
    this.qrSession.set(null);
    this.qrLoading.set(false);
    this.qrError.set('');
    if (clearVisible) this.qrVisible.set(false);
  }

  private stopQrWatch(): void {
    if (this.qrPollTimer) clearInterval(this.qrPollTimer);
    this.qrPollTimer = null;
  }

  // ── QR chuyển khoản cho thanh toán một phương thức ──────────────────────

  private scheduleTransferQr(amount: number): void {
    if (amount <= 0) {
      this.stopTransferQr();
      return;
    }
    // Đang có QR đúng số tiền và đã nhận tiền thì giữ nguyên, không tạo phiên mới.
    if (amount === this.transferQrAmount && this.transferQr()?.status === 'Paid') return;
    if (amount === this.transferQrAmount && this.transferQrLoading()) return;

    if (this.transferQrTimer) clearTimeout(this.transferQrTimer);
    this.transferQrTimer = setTimeout(() => this.startTransferQr(amount), 250);
  }

  private startTransferQr(amount: number): void {
    this.stopTransferQrTimers();    this.transferQrAmount = amount;
    this.transferQr.set(null);
    this.transferQrError.set('');
    this.transferQrLoading.set(true);

    this.sales.createPosQr(amount).subscribe({
      next: (session) => {
        if (this.transferQrAmount !== amount) return;
        this.transferQrLoading.set(false);
        this.transferQr.set(session);
        if (session.status !== 'Paid') this.startTransferPoll(session.id);
      },
      error: (error: unknown) => {
        if (this.transferQrAmount !== amount) return;
        this.transferQrLoading.set(false);
        this.transferQrError.set(this.messageOf(error, 'Không tạo được mã QR.'));
      },
    });
  }

  private startTransferPoll(sessionId: string): void {
    this.stopTransferQrTimers();
    this.transferQrPoll = setInterval(() => {
      this.sales.getPosQr(sessionId).subscribe({
        next: (session) => {
          if (session.status === 'Paid') this.stopTransferQrTimers();
          this.transferQr.set(session);
        },
        error: () => this.stopTransferQrTimers(),
      });
    }, 3000);
  }

  private stopTransferQrTimers(): void {
    if (this.transferQrTimer) clearTimeout(this.transferQrTimer);
    if (this.transferQrPoll) clearInterval(this.transferQrPoll);
    this.transferQrTimer = null;
    this.transferQrPoll = null;
  }

  private stopTransferQr(): void {
    this.stopTransferQrTimers();
    this.transferQrAmount = 0;
    this.transferQr.set(null);
    this.transferQrLoading.set(false);
    this.transferQrError.set('');
  }

  confirmTransferQr(session: PosQrSession): void {
    this.sales.confirmPosQr(session.id).subscribe({
      next: (confirmed) => {
        this.stopTransferQrTimers();
        this.transferQr.set(confirmed);
        this.toast.add({ severity: 'success', summary: 'Đã xác nhận chuyển khoản' });
      },
      error: (error: unknown) => {
        this.toast.add({
          severity: 'error',
          summary: 'Không xác nhận được chuyển khoản',
          detail: this.messageOf(error, 'Thử lại.'),
        });
      },
    });
  }

  // ── Tạo đơn ─────────────────────────────────────────────────────────────

  /** Nút THANH TOÁN ngoài footer: lần 1 mở sidebar, lần 2 mới chốt đơn. */
  primaryCheckout(): void {
    if (!this.cart().length || this.busy()) return;
    if (!this.checkoutPanelOpen()) {
      this.checkoutPanelOpen.set(true);
      return;
    }
    this.checkout();
  }

  openCheckout(): void {
    if (!this.cart().length) return;
    this.checkoutPanelOpen.set(true);
  }

  checkout(): void {
    const ticket = this.activeTicket();
    if (!ticket || !ticket.cart.length || this.busy()) return;

    const total = this.total();
    if (total <= 0 && !this.splitPayments().length) {
      this.toast.add({ severity: 'warn', summary: 'Hóa đơn chưa có giá trị' });
      return;
    }
    if (ticket.channel === 'Delivery') {
      if (!ticket.customer) {
        this.toast.add({ severity: 'warn', summary: 'Chọn khách hàng để giao hàng' });
        return;
      }
      if (this.deliveryPartnerTab() === 'Goship' && !this.selectedShippingRate()) {
        this.toast.add({ severity: 'warn', summary: 'Chọn hãng vận chuyển Goship trước khi thanh toán' });
        return;
      }
    }

    let payments: CreateOrderPayment[];
    if (this.splitPayments().length) {
      payments = this.splitPayments().map((line) => ({
        method: line.method,
        amount: line.method === 'Cash' ? line.amount : Math.min(line.amount, total),
        reference: line.reference ?? null,
      }));
    } else {
      const method = this.paymentMethod();
      const amount =
        method === 'Cash' ? Math.max(this.receivedOverride() ?? total, 0) : Math.min(this.paid(), total);
      if (amount <= 0) {
        this.toast.add({ severity: 'warn', summary: 'Số tiền thanh toán phải lớn hơn 0' });
        return;
      }
      const transfer = this.transferQr();
      payments = [{
        method,
        amount,
        reference: method === 'Transfer' && transfer ? transfer.reference : null,
      }];
    }

    this.busy.set(true);
    this.sales
      .createOrder({
        customerId: ticket.customer?.id ?? null,
        customerName: ticket.customer?.name ?? null,
        customerPhone: ticket.customer?.phone ?? null,
        note: ticket.note || null,
        couponCode: ticket.couponCode || null,
        discount: this.discountAmount(),
        channel: ticket.channel,
        deliveryAddress: ticket.deliveryAddress || null,
        shippingFee: ticket.channel === 'Delivery' ? this.shippingFee() : 0,
        recipientName: ticket.customer?.name ?? null,
        recipientPhone: ticket.customer?.phone ?? null,
        deliveryCityId: ticket.channel === 'Delivery' ? this.deliveryCityId() : null,
        deliveryDistrictId: ticket.channel === 'Delivery' ? this.deliveryDistrictId() : null,
        deliveryWardId: ticket.channel === 'Delivery' ? this.deliveryWardId() : null,
        deliveryCityName: ticket.channel === 'Delivery' ? this.deliveryCityName() : null,
        deliveryDistrictName: ticket.channel === 'Delivery' ? this.deliveryDistrictName() : null,
        deliveryWardName: ticket.channel === 'Delivery' ? this.deliveryWardName() : null,
        lines: ticket.cart.map((line) => ({
          productId: line.product.id,
          quantity: line.quantity,
          unitPrice: line.unitPrice,
          discount: line.discount,
        })),
        payments,
        pointsToRedeem: this.usePoints() ? this.pointsRedeemed() : 0,
        creditOverpayment: this.creditOverpayment() && this.change() > 0,
      })
      .subscribe({
        next: (order) => {
          this.busy.set(false);
          if (ticket.channel === 'Delivery') this.bookShipment(order, ticket);
          if (order.remaining > 0) {
            this.finishTransferOrder(order);
          } else {
            this.finishOrder(order);
          }
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.toast.add({
            severity: 'error',
            summary: 'Không tạo được hóa đơn',
            detail: this.messageOf(error, 'Vui lòng thử lại.'),
          });
        },
      });
  }

  private finishOrder(order: SalesOrder): void {
    this.successOrder.set(order);
    this.lastPrintedOrder.set(order);
    this.resetTicketAfterPayment();
    this.loadProducts();
    this.toast.add({
      severity: 'success',
      summary: `Đã thanh toán ${order.code}`,
      detail:
        order.changeAmount > 0
          ? `Tiền thừa trả khách: ${this.fmt(order.changeAmount)} đ`
          : `Tổng tiền ${this.fmt(order.total)} đ`,
    });
  }

  private resetTicketAfterPayment(): void {
    const ticket = this.activeTicket();
    if (!ticket) return;
    const fresh = newTicket(this.activeIndex() + 1);
    fresh.channel = ticket.channel;
    this.tickets.set(this.tickets().map((item, i) => (i === this.activeIndex() ? fresh : item)));
    this.checkoutPanelOpen.set(false);
    this.resetPaymentDraft();
  }

  // ── Chuyển khoản / VietQR ───────────────────────────────────────────────

  private finishTransferOrder(order: SalesOrder): void {
    const ticket = this.activeTicket();
    const pending: PendingTransfer = { order, qr: null };
    this.pendingTransfers.update((list) => [...list, pending]);
    if (ticket) {
      const fresh = newTicket(this.activeIndex() + 1);
      fresh.channel = ticket.channel;
      this.tickets.set(this.tickets().map((item, i) => (i === this.activeIndex() ? fresh : item)));
    }
    this.resetPaymentDraft();
    this.openTransfer(pending);
  }

  openTransfer(pending: PendingTransfer): void {
    this.activeTransfer.set(pending);
    this.vietQrOpen.set(true);
    this.vietQrError.set('');
    this.vietQrLoading.set(true);

    // Mở lại một đơn đang chờ: có thể webhook đã xác nhận trong lúc đóng popup.
    this.sales.getOrder(pending.order.id).subscribe({
      next: (order) => {
        if (order.remaining <= 0) {
          this.completeTransfer(order);
          return;
        }
        this.loadTransferQr(pending, order);
      },
      error: () => this.loadTransferQr(pending, pending.order),
    });
  }

  private loadTransferQr(pending: PendingTransfer, order: SalesOrder): void {
    this.sales.getVietQr(order.id).subscribe({
      next: (qr) => {
        this.vietQrLoading.set(false);
        pending.order = order;
        pending.qr = qr;
        this.activeTransfer.set({ ...pending });
        this.pendingTransfers.update((list) =>
          list.map((item) => (item.order.id === pending.order.id ? { ...item, order, qr } : item)),
        );
        this.startPolling(order.id);
      },
      error: (error: unknown) => {
        this.vietQrLoading.set(false);
        this.vietQrError.set(this.messageOf(error, 'Chưa tạo được mã VietQR.'));
        this.startPolling(order.id);
      },
    });
  }

  private startPolling(orderId: string): void {
    this.stopPolling();
    this.pollTimer = setInterval(() => this.checkTransfer(orderId), 3000);
  }

  private stopPolling(): void {
    if (this.pollTimer) clearInterval(this.pollTimer);
    this.pollTimer = null;
  }

  checkTransfer(orderId: string): void {
    if (this.checkingTransfer()) return;
    this.checkingTransfer.set(true);
    this.sales.getOrder(orderId).subscribe({
      next: (order) => {
        this.checkingTransfer.set(false);
        if (order.remaining <= 0) {
          this.stopPolling();
          this.completeTransfer(order);
        } else if (order.paidAmount > 0) {
          this.sales.getVietQr(orderId).subscribe({
            next: (qr) => {
              const current = this.activeTransfer();
              if (current) this.activeTransfer.set({ ...current, order, qr });
            },
          });
        }
      },
      error: () => this.checkingTransfer.set(false),
    });
  }

  /** Xác nhận thủ công khi khách báo đã chuyển nhưng webhook chưa tới. */
  confirmTransferManually(): void {
    const transfer = this.activeTransfer();
    if (!transfer) return;
    this.checkingTransfer.set(true);
    this.sales
      .addPayment(transfer.order.id, {
        method: 'Transfer',
        amount: transfer.qr?.amount ?? transfer.order.remaining,
        reference: `Xác nhận tay ${new Date().toISOString()}`,
        confirm: true,
      })
      .subscribe({
        next: (order) => {
          this.checkingTransfer.set(false);
          this.stopPolling();
          this.completeTransfer(order);
        },
        error: (error: unknown) => {
          this.checkingTransfer.set(false);
          this.toast.add({
            severity: 'error',
            summary: 'Không xác nhận được',
            detail: this.messageOf(error, 'Vui lòng thử lại.'),
          });
        },
      });
  }

  private completeTransfer(order: SalesOrder): void {
    this.pendingTransfers.update((list) => list.filter((item) => item.order.id !== order.id));
    this.vietQrOpen.set(false);
    this.activeTransfer.set(null);
    this.successOrder.set(order);
    this.lastPrintedOrder.set(order);
    this.loadProducts();
    this.toast.add({
      severity: 'success',
      summary: `Đã nhận thanh toán ${order.code}`,
      detail: `${this.fmt(order.total)} đ`,
    });
  }

  closeVietQr(): void {
    this.stopPolling();
    this.vietQrOpen.set(false);
    this.activeTransfer.set(null);
    this.toast.add({
      severity: 'info',
      summary: 'Hóa đơn đang chờ thanh toán',
      detail: 'Mở lại ở mục "Chờ chuyển khoản" bên phải.',
    });
  }

  reopenTransfer(pending: PendingTransfer): void {
    this.openTransfer(pending);
  }

  simulateTransfer(): void {
    const transfer = this.activeTransfer();
    if (!transfer) return;
    this.sales
      .simulateTransfer(transfer.order.code, transfer.qr?.amount ?? transfer.order.remaining)
      .subscribe({
        next: () => this.checkTransfer(transfer.order.id),
        error: (error: unknown) =>
          this.toast.add({
            severity: 'error',
            summary: 'Không giả lập được',
            detail: this.messageOf(error, 'Chỉ dùng khi phát triển.'),
          }),
      });
  }

  get isDev(): boolean {
    return !environment.production;
  }

  // ── In ──────────────────────────────────────────────────────────────────

  closeSuccess(): void {
    this.successOrder.set(null);
  }

  printReceipt(order?: SalesOrder): void {
    const target = order ?? this.lastPrintedOrder();
    if (!target) return;

    // Mở cửa sổ in ngay trong cú click (tránh popup blocker), rồi mới gọi API
    // lấy QR hóa đơn và ghi nội dung vào.
    const win = window.open('', '_blank', 'width=680,height=820');
    if (!win) {
      this.toast.add({
        severity: 'warn',
        summary: 'Trình duyệt chặn cửa sổ in',
        detail: 'Cho phép popup rồi thử lại.',
      });
      return;
    }

    this.sales.getInvoiceQr(target.id).subscribe({
      next: (qr) => this.writeInvoice(win, target, qr.imageUrl),
      error: () => this.writeInvoice(win, target, null),
    });
  }

  private writeInvoice(win: Window, order: SalesOrder, qrUrl: string | null): void {
    const store = this.store();
    const storeName = this.escape(store?.name ?? 'Tryum');
    const storeAddress = this.escape(store?.address ?? '—');
    const storePhone = this.escape(store?.phone ?? '—');
    const customerName = this.escape(order.customerName ?? 'Khách lẻ');
    const customerPhone = this.escape(order.customerPhone ?? '—');
    const issuedAt = new Date(order.createdAt).toLocaleString('vi-VN');

    const rows = order.lines
      .map(
        (line) => `<tr>
          <td>${this.escape(line.productName)}<br><small>${this.escape(line.sku)}</small></td>
          <td class="num">${this.fmt(line.unitPrice)}</td>
          <td class="center">${line.quantity}</td>
          <td class="num">${this.fmt(line.lineTotal)}</td>
        </tr>`,
      )
      .join('');

    const paymentLines = order.payments
      .filter((payment) => payment.status === 'Confirmed')
      .map(
        (payment) =>
          `<div class="row"><span>${this.methodLabel(payment.method)}</span><b>${this.fmt(payment.amount)} đ</b></div>`,
      )
      .join('');

    const html = `<!doctype html><html lang="vi"><head><meta charset="utf-8"><title>Hóa đơn ${order.code}</title>
      <style>
        @page { size: A5 portrait; margin: 10mm; }
        * { box-sizing: border-box; }
        body { margin: 0; font-family: "Inter", "Segoe UI", Arial, sans-serif; font-size: 11.5px; color: #111; }
        .head { display: flex; align-items: center; gap: 12px; border-bottom: 1px solid #dfe5ef; padding-bottom: 9px; }
        .logo { display: flex; align-items: center; justify-content: center; width: 46px; height: 46px; border-radius: 50%; background: #2b6cf6; color: #fff; font-size: 20px; font-weight: 800; }
        .store-name { margin: 0 0 2px; font-size: 17px; font-weight: 800; letter-spacing: .02em; }
        .store-meta { margin: 0; color: #555; font-size: 10.5px; line-height: 1.45; }
        .title { margin: 14px 0 3px; text-align: center; font-size: 15px; font-weight: 800; letter-spacing: .06em; }
        .sub { margin: 0 0 10px; text-align: center; color: #555; font-size: 10.5px; }
        .customer { margin: 0 0 8px; border-top: 1px dashed #c9d2e2; border-bottom: 1px dashed #c9d2e2; padding: 6px 0; font-size: 11px; line-height: 1.6; }
        table { width: 100%; border-collapse: collapse; }
        th { border-bottom: 1px solid #c9d2e2; padding: 4px 0; color: #667; font-size: 10px; text-align: left; text-transform: uppercase; }
        td { padding: 4px 0; border-bottom: 1px dotted #e6ebf3; vertical-align: top; }
        td small { color: #888; font-size: 9.5px; }
        .num { text-align: right; white-space: nowrap; }
        .center { text-align: center; }
        .totals { width: 63%; margin: 8px 0 0 auto; font-size: 11px; }
        .totals .row { display: flex; justify-content: space-between; gap: 10px; padding: 2px 0; }
        .totals .grand { margin-top: 3px; border-top: 1px solid #c9d2e2; padding-top: 4px; font-size: 13px; font-weight: 800; }
        .words { margin: 7px 0 0; text-align: center; font-size: 10.5px; font-style: italic; }
        .payments { width: 63%; margin: 4px 0 0 auto; font-size: 10.5px; }
        .payments .row { display: flex; justify-content: space-between; gap: 10px; padding: 1px 0; }
        .qr { margin-top: 10px; text-align: center; }
        .qr img { width: 34mm; height: 34mm; object-fit: contain; }
        .qr p { margin: 2px 0 0; color: #555; font-size: 10px; }
        .thanks { margin: 10px 0 0; text-align: center; font-size: 11px; font-weight: 600; }
        .note { margin: 2px 0 0; text-align: center; color: #777; font-size: 9.5px; }
      </style></head><body>
      <div class="head">
        <div class="logo">T</div>
        <div>
          <p class="store-name">${storeName}</p>
          <p class="store-meta">Địa chỉ: ${storeAddress}</p>
          <p class="store-meta">Điện thoại: ${storePhone}</p>
        </div>
      </div>
      <div class="title">HÓA ĐƠN BÁN HÀNG</div>
      <p class="sub">Số HĐ: <b>${order.code}</b> · ${issuedAt}</p>
      <div class="customer">
        Khách hàng: <b>${customerName}</b><br>
        SĐT: ${customerPhone}<br>
        Thu ngân: ${this.escape(order.soldByName ?? '—')}
        ${order.note ? `<br>Ghi chú: ${this.escape(order.note)}` : ''}
      </div>
      <table>
        <thead><tr><th style="width:44%">Tên hàng</th><th class="num">Đơn giá</th><th class="center">SL</th><th class="num">Thành tiền</th></tr></thead>
        <tbody>${rows}</tbody>
      </table>
      <div class="totals">
        <div class="row"><span>Tổng tiền hàng</span><b>${this.fmt(order.subtotal)} đ</b></div>
        ${order.discount > 0 ? `<div class="row"><span>Chiết khấu</span><b>-${this.fmt(order.discount)} đ</b></div>` : ''}
        ${order.pointsDiscount > 0 ? `<div class="row"><span>Điểm (${order.pointsRedeemed})</span><b>-${this.fmt(order.pointsDiscount)} đ</b></div>` : ''}
        <div class="row grand"><span>Tổng thanh toán</span><b>${this.fmt(order.total)} đ</b></div>
      </div>
      <p class="words">(${readMoneyInWords(order.total)})</p>
      <div class="payments">${paymentLines}</div>
      ${qrUrl ? `<div class="qr"><img src="${this.escape(qrUrl)}" alt="Mã QR thanh toán"><p>Quét mã thanh toán</p></div>` : ''}
      <p class="thanks">Cảm ơn và hẹn gặp lại!</p>
      <p class="note">Hóa đơn được tạo bởi Tryum</p>
      </body></html>`;

    win.document.write(html);
    win.document.close();
    win.focus();
    // Chờ ảnh QR (img.vietqr.io) tải xong trước khi mở hộp thoại in.
    setTimeout(() => win.print(), 700);
  }

  printQr(): void {
    const transfer = this.activeTransfer();
    const qr = transfer?.qr;
    if (!transfer || !qr) return;
    const html = `<!doctype html><html lang="vi"><head><meta charset="utf-8"><title>QR ${qr.orderCode}</title>
      <style>body{font-family:Arial,sans-serif;text-align:center;width:80mm;margin:0 auto;padding:4mm}
      img{width:62mm;height:auto} h1{font-size:15px;margin:4px 0} p{margin:3px 0;font-size:12px}
      .code{font-weight:700;font-size:13px}</style></head><body>
      <h1>Quét mã để thanh toán</h1>
      <p class="code">${qr.orderCode}</p>
      <img src="${this.escape(qr.imageUrl)}" alt="VietQR ${qr.orderCode}">
      <p><b>${this.fmt(qr.amount)} đ</b></p>
      <p>${this.escape(qr.bankName ?? '')} · ${this.escape(qr.accountNumber ?? '')}</p>
      <p>${this.escape(qr.accountName ?? '')}</p>
      <p>Nội dung: ${qr.orderCode}</p>
      </body></html>`;
    this.openPrintWindow(html);
  }

  private openPrintWindow(html: string): void {
    const win = window.open('', '_blank', 'width=420,height=680');
    if (!win) {
      this.toast.add({ severity: 'warn', summary: 'Trình duyệt chặn cửa sổ in', detail: 'Cho phép popup rồi thử lại.' });
      return;
    }
    win.document.write(html);
    win.document.close();
    win.focus();
    setTimeout(() => win.print(), 250);
  }

  private escape(value: string): string {
    return value
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }

  methodLabel(method: PaymentMethod): string {
    return PAYMENT_METHODS.find((item) => item.value === method)?.label ?? method;
  }

  // ── Phím tắt ────────────────────────────────────────────────────────────

  @HostListener('window:keydown', ['$event'])
  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'F2') {
      event.preventDefault();
      this.searchInput()?.nativeElement.focus();
    } else if (event.key === 'F4') {
      event.preventDefault();
      this.openCustomerPicker();
    } else if (event.key === 'F9') {
      event.preventDefault();
      this.primaryCheckout();
    } else if (event.key === 'Escape') {
      this.customerPickerOpen.set(false);
      this.customerDialogOpen.set(false);
      this.paymentDialogOpen.set(false);
      this.checkoutPanelOpen.set(false);
      this.successOrder.set(null);
    }
  }
}
