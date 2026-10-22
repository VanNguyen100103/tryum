import { HttpClient } from '@angular/common/http';
import { ChangeDetectorRef, Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { environment } from '../../environments/environment';
import { ToastService } from '../core/services/toast.service';

type PosMode = 'quick' | 'regular' | 'delivery';
interface ProductRow {
  id: string; sku: string; barcode: string | null; name: string; salePrice: number | null;
  costPrice: number | null; stock: number | null; imageId: string | null;
}
interface ProductPage { items: ProductRow[]; total: number; page: number; pageSize: number; }
interface CartLine { product: ProductRow; quantity: number; }
interface CustomerRow {
  id: string; code: string; name: string; phone: string | null; email: string | null;
  address: string | null; region: string | null; ward: string | null; groupName: string | null;
  birthDate: string | null; gender: string | null; facebook: string | null; assignedTo: string | null;
  note: string | null; companyName: string | null; taxCode: string | null; invoiceAddress: string | null;
}
interface CustomerPage { items: CustomerRow[]; total: number; }
interface CustomerDraft {
  name: string; phone: string; email: string; address: string; region: string; ward: string;
  groupName: string; birthDate: string; gender: string; facebook: string; assignedTo: string;
  note: string; companyName: string; taxCode: string; invoiceAddress: string;
}

const emptyCustomerDraft = (): CustomerDraft => ({
  name: '', phone: '', email: '', address: '', region: '', ward: '', groupName: '', birthDate: '',
  gender: '', facebook: '', assignedTo: '', note: '', companyName: '', taxCode: '', invoiceAddress: '',
});

@Component({
  selector: 'app-pos',
  imports: [FormsModule],
  templateUrl: './pos.component.html',
  styleUrl: './pos.component.scss',
})
export class PosComponent implements OnInit, OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly toast = inject(ToastService);
  private searchTimer: ReturnType<typeof setTimeout> | null = null;
  readonly pageSize = 200;
  products: ProductRow[] = [];
  totalProducts = 0;
  productPage = 1;
  loadingProducts = false;
  productsError = '';
  search = '';
  cart: CartLine[] = [];
  mode: PosMode = 'regular';
  invoiceCount = 1;
  customer = '';
  selectedCustomer: CustomerRow | null = null;
  customerSearch = '';
  customerResults: CustomerRow[] = [];
  customerPickerOpen = false;
  customerLoading = false;
  customerSaveLoading = false;
  customerError = '';
  customerDialogOpen = false;
  customerDialogTab: 'general' | 'invoice' = 'general';
  customerDraft = emptyCustomerDraft();
  private customerSearchTimer: ReturnType<typeof setTimeout> | null = null;
  note = '';
  deliveryAddress = '';
  readonly imageSources = new Map<string, string>();

  get filteredProducts(): ProductRow[] {
    return this.products;
  }
  get subtotal(): number { return this.cart.reduce((total, line) => total + (line.product.salePrice ?? 0) * line.quantity, 0); }
  get totalQuantity(): number { return this.cart.reduce((total, line) => total + line.quantity, 0); }
  get firstPage(): number { return this.totalProducts === 0 ? 0 : (this.productPage - 1) * this.pageSize + 1; }
  get lastPage(): number { return Math.min(this.productPage * this.pageSize, this.totalProducts); }

  ngOnInit(): void { this.loadProducts(); }
  ngOnDestroy(): void {
    if (this.searchTimer) clearTimeout(this.searchTimer);
    if (this.customerSearchTimer) clearTimeout(this.customerSearchTimer);
    for (const url of this.imageSources.values()) URL.revokeObjectURL(url);
    this.imageSources.clear();
  }

  loadProducts(): void {
    this.loadingProducts = true;
    this.productsError = '';
    for (const url of this.imageSources.values()) URL.revokeObjectURL(url);
    this.imageSources.clear();
    const term = this.search.trim();
    const params: Record<string, string | number> = { page: this.productPage, pageSize: this.pageSize };
    if (term) params['search'] = term;
    this.http.get<ProductPage>(`${environment.apiUrl}/api/v1/products`, { params }).subscribe({
      next: (result) => {
        this.products = result.items;
        this.totalProducts = result.total;
        this.loadingProducts = false;
        this.cdr.markForCheck();
        for (const product of result.items) {
          if (!product.imageId || product.imageId === '00000000-0000-0000-0000-000000000000') continue;
          this.http.get(`${environment.apiUrl}/api/v1/products/${product.id}/images/${product.imageId}`, { responseType: 'blob' }).subscribe({
            next: (blob) => { this.imageSources.set(product.id, URL.createObjectURL(blob)); this.cdr.markForCheck(); },
          });
        }
      },
      error: () => {
        this.products = [];
        this.loadingProducts = false;
        this.productsError = 'Không tải được sản phẩm. Hãy kiểm tra kết nối và đăng nhập cửa hàng.';
        this.cdr.markForCheck();
      },
    });
  }

  previousProducts(): void { if (this.productPage > 1) { this.productPage -= 1; this.loadProducts(); } }
  nextProducts(): void { if (this.productPage * this.pageSize < this.totalProducts) { this.productPage += 1; this.loadProducts(); } }
  onSearchChanged(value: string): void {
    this.search = value;
    if (this.searchTimer) clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => { this.productPage = 1; this.loadProducts(); }, 250);
  }

  openCustomerPicker(): void {
    this.customerPickerOpen = true;
    this.customerSearch = '';
    this.customerError = '';
    this.loadCustomers();
  }
  onCustomerSearchChanged(value: string): void {
    this.customerSearch = value;
    this.selectedCustomer = null;
    if (this.customerSearchTimer) clearTimeout(this.customerSearchTimer);
    this.customerSearchTimer = setTimeout(() => this.loadCustomers(), 250);
  }
  loadCustomers(): void {
    this.customerLoading = true;
    this.customerError = '';
    const params: Record<string, string | number> = { page: 1, pageSize: 30 };
    if (this.customerSearch.trim()) params['search'] = this.customerSearch.trim();
    this.http.get<CustomerPage>(`${environment.apiUrl}/api/v1/customers`, { params }).subscribe({
      next: (result) => { this.customerResults = result.items; this.customerLoading = false; this.cdr.markForCheck(); },
      error: () => { this.customerResults = []; this.customerLoading = false; this.customerError = 'Không tải được khách hàng. Hãy thử lại.'; this.cdr.markForCheck(); },
    });
  }
  selectCustomer(customer: CustomerRow): void {
    this.selectedCustomer = customer;
    this.customerSearch = customer.name;
    this.customerPickerOpen = false;
  }
  clearCustomer(): void { this.selectedCustomer = null; this.customerSearch = ''; }
  openCreateCustomer(): void {
    this.customerDraft = emptyCustomerDraft();
    this.customerError = '';
    this.customerDialogTab = 'general';
    this.customerPickerOpen = false;
    this.customerDialogOpen = true;
  }
  saveCustomer(): void {
    if (!this.customerDraft.name.trim()) { this.customerError = 'Vui lòng nhập tên khách hàng.'; this.customerDialogTab = 'general'; return; }
    this.customerSaveLoading = true;
    this.customerError = '';
    const payload = Object.fromEntries(Object.entries(this.customerDraft).map(([key, value]) => [key, value.trim() || null]));
    this.http.post<CustomerRow>(`${environment.apiUrl}/api/v1/customers`, payload).subscribe({
      next: (created) => {
        this.customerSaveLoading = false;
        this.customerDialogOpen = false;
        this.selectCustomer(created);
        this.toast.add({ severity: 'success', summary: 'Đã thêm khách hàng', detail: `${created.name} đã được lưu vào cửa hàng.` });
        this.cdr.markForCheck();
      },
      error: (error: { error?: { message?: string } }) => {
        this.customerSaveLoading = false;
        this.customerError = error.error?.message || 'Không lưu được khách hàng. Hãy thử lại.';
        this.cdr.markForCheck();
      },
    });
  }

  addProduct(product: ProductRow): void {
    const line = this.cart.find((item) => item.product.id === product.id);
    if (line) line.quantity += 1;
    else this.cart = [...this.cart, { product, quantity: 1 }];
    this.cdr.markForCheck();
  }

  searchFromBarcode(event: Event): void {
    event.preventDefault();
    const query = this.search.trim().toLocaleLowerCase('vi-VN');
    if (!query) return;
    const product = this.products.find((item) => item.sku.toLocaleLowerCase('vi-VN') === query || item.barcode?.toLocaleLowerCase('vi-VN') === query);
    if (product) { this.addProduct(product); this.onSearchChanged(''); }
    else if (this.products.length === 1) {
      const onlyProduct = this.products[0];
      if (onlyProduct) { this.addProduct(onlyProduct); this.onSearchChanged(''); }
    }
  }

  changeQuantity(productId: string, delta: number): void {
    this.cart = this.cart.map((line) => line.product.id === productId ? { ...line, quantity: line.quantity + delta } : line).filter((line) => line.quantity > 0);
    this.cdr.markForCheck();
  }
  removeLine(productId: string): void {
    this.cart = this.cart.filter((line) => line.product.id !== productId);
    this.cdr.markForCheck();
  }
  clearCart(): void { this.cart = []; this.cdr.markForCheck(); }
  newInvoice(): void { this.invoiceCount += 1; this.cart = []; this.customer = ''; this.selectedCustomer = null; this.customerSearch = ''; this.note = ''; this.deliveryAddress = ''; }
  checkout(): void {
    this.toast.add({ severity: 'info', summary: 'Chưa kết nối thanh toán', detail: 'Giỏ hàng đang hiển thị sản phẩm thật; API tạo đơn hàng và thanh toán chưa được cấu hình.' });
  }
}
