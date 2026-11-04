import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpClient, HttpEventType } from '@angular/common/http';
import { ChangeDetectorRef, Component, inject, OnDestroy, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { environment } from '../../../environments/environment';

interface ImportJobStatus {
  id: string;
  status: 'Reading' | 'Preparing' | 'Importing' | 'Completed' | 'Failed';
  total: number;
  processed: number;
  created: number;
  updated: number;
  imagesQueued: number;
  imageStorageConfigured: boolean;
  error: string | null;
  startedAt: string;
  finishedAt: string | null;
}

interface ProductImageSyncStatus {
  pending: number;
  stored: number;
  failed: number;
  total: number;
}

@Component({
  selector: 'app-product-list',
  imports: [DatePipe, DecimalPipe, FormsModule],
  templateUrl: './product-list.component.html',
  styleUrl: './product-list.component.scss',
})
export class ProductListComponent implements OnInit, OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly cdr = inject(ChangeDetectorRef);
  importOpen = false;
  fileName = '';
  fileError = '';
  fileSummary = '';
  dragActive = false;
  importReady = false;
  importing = false;
  importProgress: number | null = 0;
  importStage = '';
  importResult = '';
  importPhase: 'upload' | 'products' | 'images' | 'done' = 'upload';
  productTotal = 0;
  productProcessed = 0;
  imagePending = 0;
  imageStored = 0;
  imageFailed = 0;
  imageTotal = 0;
  private importJobId = '';
  private importStartedAt = '';
  private jobTimer: ReturnType<typeof setInterval> | null = null;
  private imageTimer: ReturnType<typeof setInterval> | null = null;
  private selectedFile: File | null = null;
  search = '';
  private searchTimer: ReturnType<typeof setTimeout> | null = null;
  productDialogOpen = false;
  productDialogMode: 'create' | 'edit' = 'create';
  productDialogTab: 'info' | 'description' = 'info';
  editingProductId: string | null = null;
  productForm: ProductForm = emptyProductForm();
  productSaving = false;
  productModalError = '';
  productNotice = '';
  imageFiles: File[] = [];
  imagePreviews: string[] = [];
  private existingImageUrl: string | null = null;
  private previewObjectUrls: string[] = [];
  get imagePreview(): string | null {
    return this.imagePreviews[0] ?? this.existingImageUrl;
  }
  expandedProductId: string | null = null;
  productDetail: ProductDetail | null = null;
  detailUnits: ProductUnit[] = [];
  detailAttributes: ProductAttribute[] = [];
  detailVariants: ProductVariant[] = [];
  attributeDrafts: string[] = [];
  detailLoading = false;
  detailTab: 'info' | 'description' = 'info';
  products: ProductRow[] = [];
  totalProducts = 0;
  currentPage = 1;
  readonly pageSize = 50;
  productsLoading = false;
  productsError = '';
  readonly imageSources = new Map<string, string>();
  get pageStart(): number { return this.totalProducts === 0 ? 0 : (this.currentPage - 1) * this.pageSize + 1; }
  get pageEnd(): number { return Math.min(this.currentPage * this.pageSize, this.totalProducts); }
  get productPercent(): number {
    return this.productTotal > 0 ? Math.min(100, Math.round(this.productProcessed / this.productTotal * 100)) : 0;
  }
  get imagePercent(): number {
    return this.imageTotal > 0 ? Math.min(100, Math.round((this.imageStored + this.imageFailed) / this.imageTotal * 100)) : 0;
  }

  ngOnInit(): void { this.loadProducts(); }

  ngOnDestroy(): void {
    this.stopImportTimers();
    if (this.searchTimer) clearTimeout(this.searchTimer);
    this.clearImageSources();
  }

  loadProducts(): void {
    this.productsLoading = true;
    this.productsError = '';
    this.clearImageSources();
    this.http.get<ProductPage>(`${environment.apiUrl}/api/v1/products`, {
      params: { page: this.currentPage, pageSize: this.pageSize, ...(this.search.trim() ? { search: this.search.trim() } : {}) },
    }).subscribe({
      next: (result) => {
        this.products = result.items;
        this.totalProducts = result.total;
        this.productsLoading = false;
        this.cdr.markForCheck();
        for (const product of result.items) {
          if (!product.imageId || product.imageId === '00000000-0000-0000-0000-000000000000') continue;
          this.http.get(`${environment.apiUrl}/api/v1/products/${product.id}/images/${product.imageId}`, { responseType: 'blob' })
            .subscribe({ next: (blob) => {
              this.imageSources.set(product.id, URL.createObjectURL(blob));
              this.cdr.markForCheck();
            } });
        }
      },
      error: () => {
        this.products = [];
        this.totalProducts = 0;
        this.productsLoading = false;
        this.productsError = 'Chưa kết nối được API sản phẩm. Dữ liệu chưa được tải.';
        this.cdr.markForCheck();
      },
    });
  }

  previousPage(): void { if (this.currentPage > 1) { this.currentPage -= 1; this.loadProducts(); } }
  nextPage(): void { if (this.currentPage * this.pageSize < this.totalProducts) { this.currentPage += 1; this.loadProducts(); } }

  onSearch(value: string): void {
    this.search = value;
    if (this.searchTimer) clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => {
      this.currentPage = 1;
      this.loadProducts();
    }, 300);
  }

  clearSearch(): void {
    this.search = '';
    if (this.searchTimer) clearTimeout(this.searchTimer);
    this.currentPage = 1;
    this.loadProducts();
  }

  openCreateProduct(): void {
    this.productDialogMode = 'create';
    this.editingProductId = null;
    this.productForm = emptyProductForm();
    this.attributeDrafts = [];
    this.productModalError = '';
    this.resetImageState();
    this.productDialogTab = 'info';
    this.productDialogOpen = true;
  }

  openEditProduct(product: ProductRow): void {
    this.productDialogMode = 'edit';
    this.editingProductId = product.id;
    this.productModalError = '';
    this.productDialogTab = 'info';
    this.productDialogOpen = true;
    this.productForm = formFromProduct(product);
    this.resetImageState();
    if (product.imageId && product.imageId !== '00000000-0000-0000-0000-000000000000') {
      this.loadProductImage(product.imageId);
    }
    this.loadProductDetail(product.id);
  }

  openCopyProduct(): void {
    if (!this.productDetail) return;
    this.productDialogMode = 'create';
    this.editingProductId = null;
    this.productForm = {
      ...formFromProduct(this.productDetail),
      sku: '',
      name: `${this.productDetail.name} (bản sao)`,
    };
    this.attributeDrafts = this.productForm.attributes.map(() => '');
    this.resetImageState();
    this.productModalError = '';
    this.productDialogTab = 'info';
    this.productDialogOpen = true;
  }

  closeProductDialog(): void {
    if (this.productSaving) return;
    this.productDialogOpen = false;
    this.productModalError = '';
    this.resetImageState();
  }

  patchProduct(field: keyof ProductForm, value: unknown): void {
    const numericFields: (keyof ProductForm)[] = ['salePrice', 'costPrice', 'stock', 'minStock', 'maxStock', 'weight'];
    this.productForm = {
      ...this.productForm,
      [field]: numericFields.includes(field)
        ? (value === '' || value === null || value === undefined ? null : Number(value))
        : field === 'isDirectSale'
          ? Boolean(value)
          : String(value ?? ''),
    };
  }

  addUnit(): void {
    this.productForm = {
      ...this.productForm,
      units: [...this.productForm.units, { name: '', conversion: null, salePrice: null, isDirectSale: true }],
    };
  }

  removeUnit(index: number): void {
    if (index === 0) return;
    this.productForm = { ...this.productForm, units: this.productForm.units.filter((_, i) => i !== index) };
  }

  patchUnit(index: number, field: keyof ProductUnit, value: unknown): void {
    const units = this.productForm.units.map((unit, i) => {
      if (i !== index) return unit;
      if (field === 'name') return { ...unit, name: String(value ?? '') };
      if (field === 'isDirectSale') return { ...unit, isDirectSale: Boolean(value) };
      return { ...unit, [field]: value === '' || value === null ? null : Number(value) };
    });
    this.productForm = { ...this.productForm, units };
  }

  addAttribute(): void {
    this.productForm = {
      ...this.productForm,
      attributes: [...this.productForm.attributes, { name: '', values: [] }],
    };
    this.attributeDrafts = [...this.attributeDrafts, ''];
  }

  removeAttribute(index: number): void {
    this.productForm = {
      ...this.productForm,
      attributes: this.productForm.attributes.filter((_, i) => i !== index),
    };
    this.attributeDrafts = this.attributeDrafts.filter((_, i) => i !== index);
  }

  setAttributeDraft(index: number, value: string): void {
    this.attributeDrafts = this.attributeDrafts.map((draft, i) => (i === index ? value : draft));
  }

  patchAttributeName(index: number, value: string): void {
    const attributes = this.productForm.attributes.map((attribute, i) =>
      i === index ? { ...attribute, name: String(value ?? '') } : attribute,
    );
    this.productForm = { ...this.productForm, attributes };
  }

  addAttributeValue(index: number): void {
    const value = (this.attributeDrafts[index] ?? '').trim();
    if (!value) return;
    const attributes = this.productForm.attributes.map((attribute, i) => {
      if (i !== index || attribute.values.includes(value)) return attribute;
      return { ...attribute, values: [...attribute.values, value] };
    });
    this.productForm = { ...this.productForm, attributes };
    this.attributeDrafts = this.attributeDrafts.map((draft, i) => (i === index ? '' : draft));
  }

  removeAttributeValue(index: number, value: string): void {
    const attributes = this.productForm.attributes.map((attribute, i) =>
      i === index ? { ...attribute, values: attribute.values.filter((item) => item !== value) } : attribute,
    );
    this.productForm = { ...this.productForm, attributes };
  }

  generateVariants(): void {
    const attributes = this.productForm.attributes.filter((attribute) => attribute.name.trim() && attribute.values.length);
    if (!attributes.length) {
      this.productForm = { ...this.productForm, variants: [] };
      return;
    }

    let combinations: string[][] = [[]];
    for (const attribute of attributes) {
      combinations = combinations.flatMap((combination) =>
        attribute.values.map((value) => [...combination, value]),
      );
    }

    const baseUnit = this.productForm.units[0]?.name?.trim() || 'Cái';
    const variants: ProductVariant[] = combinations.slice(0, 200).map((values) => ({
      name: values.join(' - '),
      unit: baseUnit,
      conversion: 1,
      sku: '',
      barcode: '',
      costPrice: this.productForm.costPrice,
      salePrice: this.productForm.salePrice,
      stock: this.productForm.stock,
    }));
    this.productForm = { ...this.productForm, variants };
  }

  patchVariant(index: number, field: keyof ProductVariant, value: unknown): void {
    const variants = this.productForm.variants.map((variant, i) => {
      if (i !== index) return variant;
      if (field === 'name' || field === 'unit' || field === 'sku' || field === 'barcode') {
        return { ...variant, [field]: String(value ?? '') };
      }
      return { ...variant, [field]: value === '' || value === null ? null : Number(value) };
    });
    this.productForm = { ...this.productForm, variants };
  }

  removeVariant(index: number): void {
    this.productForm = { ...this.productForm, variants: this.productForm.variants.filter((_, i) => i !== index) };
  }

  onProductImageSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = '';
    if (!files.length) return;

    for (const file of files) {
      const extension = file.name.split('.').pop()?.toLowerCase() ?? '';
      if (!['jpg', 'jpeg', 'png', 'webp', 'gif'].includes(extension) && !file.type.startsWith('image/')) {
        this.productModalError = 'Chỉ nhận ảnh JPG, PNG, WEBP hoặc GIF.';
        continue;
      }
      if (file.size > 5 * 1024 * 1024) {
        this.productModalError = 'Ảnh vượt quá 5 MB.';
        continue;
      }
      const previewUrl = URL.createObjectURL(file);
      this.imageFiles = [...this.imageFiles, file];
      this.imagePreviews = [...this.imagePreviews, previewUrl];
      this.previewObjectUrls = [...this.previewObjectUrls, previewUrl];
    }
  }

  clearSelectedImage(index?: number): void {
    if (index === undefined) {
      for (const url of this.previewObjectUrls) URL.revokeObjectURL(url);
      this.imageFiles = [];
      this.imagePreviews = [];
      this.previewObjectUrls = [];
      return;
    }

    const preview = this.imagePreviews[index];
    if (preview) URL.revokeObjectURL(preview);
    this.imagePreviews = this.imagePreviews.filter((_, i) => i !== index);
    this.imageFiles = this.imageFiles.filter((_, i) => i !== index);
    this.previewObjectUrls = this.previewObjectUrls.filter((url) => url !== preview);
  }

  private resetImageState(): void {
    for (const url of this.previewObjectUrls) URL.revokeObjectURL(url);
    if (this.existingImageUrl) URL.revokeObjectURL(this.existingImageUrl);
    this.imageFiles = [];
    this.imagePreviews = [];
    this.previewObjectUrls = [];
    this.existingImageUrl = null;
  }

  private loadProductImage(imageId: string): void {
    this.http.get(`${environment.apiUrl}/api/v1/products/${this.editingProductId}/images/${imageId}`, { responseType: 'blob' })
      .subscribe({
        next: (blob) => {
          this.existingImageUrl = URL.createObjectURL(blob);
          this.cdr.markForCheck();
        },
      });
  }

  private uploadProductImages(productId: string): void {
    const files = [...this.imageFiles];
    if (!files.length) {
      this.finishProductSave(this.lastSavedProduct!, this.pendingCreateAnother);
      return;
    }

    const uploadAt = (index: number): void => {
      if (index >= files.length) {
        this.finishProductSave(this.lastSavedProduct!, this.pendingCreateAnother);
        return;
      }

      const form = new FormData();
      form.append('file', files[index], files[index].name);
      const replace = index === 0 && !!this.existingImageUrl;
      this.http.post(`${environment.apiUrl}/api/v1/products/${productId}/images?replace=${replace}`, form).subscribe({
        next: () => uploadAt(index + 1),
        error: () => {
          this.productNotice = `Đã lưu sản phẩm nhưng chưa tải được ảnh: ${this.lastSavedProduct?.name ?? ''}.`;
          this.finishProductSave(this.lastSavedProduct!, this.pendingCreateAnother);
        },
      });
    };
    uploadAt(0);
  }

  private lastSavedProduct: ProductDetail | null = null;
  private pendingCreateAnother = false;

  private finishProductSave(saved: ProductDetail, createAnother: boolean): void {
    this.productSaving = false;
    this.productNotice = this.productDialogMode === 'edit'
      ? `Đã cập nhật ${saved.name}.`
      : `Đã tạo ${saved.name} (${saved.sku}).`;
    this.currentPage = 1;
    this.loadProducts();

    if (createAnother) {
      this.productForm = emptyProductForm();
      this.attributeDrafts = [];
      this.productDialogTab = 'info';
      this.editingProductId = null;
      this.productDialogMode = 'create';
      this.resetImageState();
      return;
    }
    this.productDialogOpen = false;
    this.resetImageState();
    if (this.expandedProductId === saved.id) this.loadProductDetail(saved.id);
  }

  saveProduct(createAnother = false): void {
    if (this.productSaving) return;
    const form = this.productForm;
    if (!form.name.trim()) {
      this.productModalError = 'Tên hàng là bắt buộc.';
      this.productDialogTab = 'info';
      return;
    }

    this.productSaving = true;
    this.productModalError = '';
    const body = {
      sku: this.productDialogMode === 'edit' ? form.sku.trim() : (form.sku.trim() || null),
      barcode: form.barcode.trim() || null,
      name: form.name.trim(),
      itemType: 'Hàng hóa',
      categoryPath: form.categoryPath.trim() || null,
      brand: form.brand.trim() || null,
      salePrice: form.salePrice,
      costPrice: form.costPrice,
      stock: form.stock,
      minStock: form.minStock,
      maxStock: form.maxStock,
      position: form.position.trim() || null,
      weight: form.weight,
      description: form.description.trim() || null,
      isDirectSale: form.isDirectSale,
      units: form.units
        .filter((unit) => unit.name.trim())
        .map((unit) => ({
          name: unit.name.trim(),
          conversion: unit.conversion,
          salePrice: unit.salePrice,
          isDirectSale: unit.isDirectSale,
        })),
      attributes: form.attributes
        .filter((attribute) => attribute.name.trim() && attribute.values.length)
        .map((attribute) => ({ name: attribute.name.trim(), values: attribute.values })),
      variants: form.variants.map((variant) => ({
        name: variant.name,
        unit: variant.unit,
        conversion: variant.conversion,
        sku: variant.sku.trim() || null,
        barcode: variant.barcode.trim() || null,
        costPrice: variant.costPrice,
        salePrice: variant.salePrice,
        stock: variant.stock,
      })),
    };

    const request = this.productDialogMode === 'edit' && this.editingProductId
      ? this.http.put<ProductDetail>(`${environment.apiUrl}/api/v1/products/${this.editingProductId}`, body)
      : this.http.post<ProductDetail>(`${environment.apiUrl}/api/v1/products`, body);

    request.subscribe({
      next: (saved) => {
        this.lastSavedProduct = saved;
        this.pendingCreateAnother = createAnother;
        if (this.imageFiles.length) {
          this.uploadProductImages(saved.id);
          return;
        }
        this.finishProductSave(saved, createAnother);
      },
      error: (error: { error?: { message?: string } }) => {
        this.productSaving = false;
        this.productModalError = error.error?.message || 'Không lưu được hàng hóa.';
      },
    });
  }

  toggleDetail(product: ProductRow): void {
    if (this.expandedProductId === product.id) {
      this.expandedProductId = null;
      this.productDetail = null;
      return;
    }
    this.expandedProductId = product.id;
    this.productDetail = null;
    this.detailTab = 'info';
    this.loadProductDetail(product.id);
  }

  loadProductDetail(productId: string): void {
    this.detailLoading = true;
    this.http.get<ProductDetail>(`${environment.apiUrl}/api/v1/products/${productId}`).subscribe({
      next: (detail) => {
        this.detailLoading = false;
        if (this.expandedProductId !== productId && this.editingProductId !== productId) return;
        this.productDetail = detail;
        const parsed = formFromProduct(detail);
        this.detailUnits = parsed.units;
        this.detailAttributes = parsed.attributes;
        this.detailVariants = parsed.variants;
        if (this.productDialogMode === 'edit' && this.editingProductId === productId) {
          this.productForm = parsed;
          this.attributeDrafts = parsed.attributes.map(() => '');
        }
        this.cdr.markForCheck();
      },
      error: () => {
        this.detailLoading = false;
        this.cdr.markForCheck();
      },
    });
  }

  editFromDetail(): void {
    if (!this.productDetail) return;
    this.openEditProduct(this.productDetail);
  }

  deleteFromDetail(): void {
    if (!this.productDetail) return;
    const product = this.productDetail;
    if (!window.confirm(`Xóa hàng hóa "${product.name}"?`)) return;
    this.http.delete(`${environment.apiUrl}/api/v1/products/${product.id}`).subscribe({
      next: () => {
        this.productNotice = `Đã xóa ${product.name}.`;
        this.expandedProductId = null;
        this.productDetail = null;
        this.loadProducts();
      },
      error: (error: { error?: { message?: string } }) => {
        this.productNotice = error.error?.message || 'Không xóa được hàng hóa.';
      },
    });
  }

  private clearImageSources(): void {
    for (const url of this.imageSources.values()) URL.revokeObjectURL(url);
    this.imageSources.clear();
  }

  openImport(): void {
    this.importOpen = true;
  }

  closeImport(): void {
    if (this.importing) return;
    this.importOpen = false;
    this.stopImportTimers();
  }

  clearFile(): void {
    if (this.importing) return;
    this.stopImportTimers();
    this.fileName = '';
    this.fileError = '';
    this.fileSummary = '';
    this.importReady = false;
    this.importResult = '';
    this.importPhase = 'upload';
    this.productTotal = 0;
    this.productProcessed = 0;
    this.imagePending = 0;
    this.imageStored = 0;
    this.imageFailed = 0;
    this.imageTotal = 0;
    this.selectedFile = null;
  }

  downloadTemplate(): void {
    const headers = [
      'Loại hàng', 'Nhóm hàng (3 cấp)', 'Mã hàng', 'Mã vạch', 'Tên hàng', 'Thương hiệu',
      'Giá bán', 'Giá vốn', 'Tồn kho', 'Tồn nhỏ nhất', 'Tồn lớn nhất', 'ĐVT', 'Mã ĐVT Cơ bản', 'Quy đổi',
    ];
    const workbook = this.createXlsx(headers);
    const workbookBuffer = workbook.buffer.slice(workbook.byteOffset, workbook.byteOffset + workbook.byteLength) as ArrayBuffer;
    const blob = new Blob([workbookBuffer], {
      type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = 'MauFileSanPham.xlsx';
    anchor.click();
    URL.revokeObjectURL(url);
  }

  async onFileSelected(event: Event): Promise<void> {
    if (this.importing) return;
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (file) await this.handleFile(file);
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    if (!this.importing) this.dragActive = true;
  }

  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    this.dragActive = false;
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragActive = false;
    if (this.importing) return;
    const file = event.dataTransfer?.files?.[0];
    if (file) void this.handleFile(file);
  }

  private async handleFile(file: File): Promise<void> {
    if (this.importing) return;

    this.clearFile();
    this.fileName = file.name;

    const extension = file.name.split('.').pop()?.toLowerCase();
    if (extension !== 'xlsx') {
      this.fileError = 'API import hiện nhận file Excel .xlsx.';
      return;
    }
    if (file.size > 10 * 1024 * 1024) {
      this.fileError = 'File vượt quá giới hạn 10 MB.';
      return;
    }

    // Cho bấm "Thực hiện" ngay, không bắt chờ đọc/parse file lớn ở trình duyệt.
    // Server vẫn kiểm tra lại toàn bộ file trước khi ghi.
    this.selectedFile = file;
    this.importReady = true;
    this.fileSummary = 'Đang kiểm tra file… có thể bấm Thực hiện ngay.';

    try {
      const rows = await this.readXlsx(await file.arrayBuffer());
      const headers = (rows.shift() ?? []).map((value) => this.normalizeHeader(value));
      const hasCode = headers.some((header) => ['ma hang', 'ma hang hoa'].includes(header));
      const hasName = headers.some((header) => ['ten hang', 'ten hang hoa'].includes(header));

      if (!hasCode || !hasName) {
        if (this.importing) return;
        this.fileError = 'Thiếu cột bắt buộc: Mã hàng và Tên hàng.';
        this.importReady = false;
        this.selectedFile = null;
        return;
      }

      const dataRows = rows.filter((row) => row.some((cell) => cell.trim().length > 0));
      this.fileSummary = `Đã đọc ${dataRows.length} dòng; đủ cột Mã hàng và Tên hàng.`;
    } catch {
      if (this.importing) return;
      this.fileError = 'Không đọc được file. Hãy dùng file .xlsx hợp lệ theo mẫu.';
      this.importReady = false;
      this.selectedFile = null;
    }
  }

  importFile(): void {
    if (!this.selectedFile || !this.importReady || this.importing) return;
    this.importing = true;
    this.importPhase = 'upload';
    this.importProgress = 0;
    this.importStage = 'Đang tải file lên máy chủ…';
    this.importResult = '';
    this.fileError = '';
    this.productTotal = 0;
    this.productProcessed = 0;
    this.imagePending = 0;
    this.imageStored = 0;
    this.imageFailed = 0;
    this.imageTotal = 0;

    const form = new FormData();
    form.append('file', this.selectedFile, this.selectedFile.name);
    this.http.post<{ jobId: string; startedAt: string; imageStorageConfigured: boolean }>(
      `${environment.apiUrl}/api/v1/products/import`, form, { observe: 'events', reportProgress: true },
    ).subscribe({
      next: (event) => {
        if (event.type === HttpEventType.UploadProgress) {
          if (event.total) {
            this.importProgress = Math.min(100, Math.round(event.loaded / event.total * 100));
            this.importStage = this.importProgress >= 100
              ? 'Đã tải file lên. Máy chủ đang đọc dữ liệu…'
              : `Đang tải file lên máy chủ… ${this.importProgress}%`;
          } else {
            this.importProgress = null;
            this.importStage = 'Đang tải file lên máy chủ…';
          }
          this.cdr.markForCheck();
          return;
        }

        if (event.type !== HttpEventType.Response || !event.body) return;
        this.importProgress = 100;
        this.importPhase = 'products';
        this.importJobId = event.body.jobId;
        this.importStartedAt = event.body.startedAt;
        this.importStage = 'Máy chủ đang đọc file…';
        this.startJobPolling(event.body.jobId);
        this.cdr.markForCheck();
      },
      error: (error: { error?: { message?: string } }) => {
        this.failImport(error.error?.message || 'Không kết nối được API production. Dữ liệu chưa được nhập.');
      },
    });
  }

  private startJobPolling(jobId: string): void {
    this.stopJobPolling();
    this.pollJob(jobId);
    this.jobTimer = setInterval(() => this.pollJob(jobId), 1000);
  }

  private pollJob(jobId: string): void {
    this.http.get<ImportJobStatus>(`${environment.apiUrl}/api/v1/products/import/${jobId}`).subscribe({
      next: (job) => {
        this.productTotal = job.total || this.productTotal;
        this.productProcessed = job.processed;
        this.importStartedAt = job.startedAt || this.importStartedAt;

        if (job.status === 'Failed') {
          this.failImport(job.error || 'Import sản phẩm thất bại.');
          return;
        }

        if (job.status === 'Completed') {
          this.productTotal = job.total;
          this.productProcessed = job.total;
          this.stopJobPolling();
          this.startImagePolling(job);
          return;
        }

        this.importStage = job.status === 'Reading'
          ? 'Máy chủ đang đọc file…'
          : job.status === 'Preparing'
            ? 'Đang kiểm tra dữ liệu…'
            : `Đang lưu sản phẩm… ${job.processed.toLocaleString('vi-VN')} / ${job.total.toLocaleString('vi-VN')}`;
        this.cdr.markForCheck();
      },
      error: () => {
        this.importStage = 'Mất kết nối tạm thời, đang thử lại…';
        this.cdr.markForCheck();
      },
    });
  }

  private startImagePolling(job: ImportJobStatus): void {
    if (!job.imagesQueued || !job.imageStorageConfigured) {
      this.finishImport(job, null);
      return;
    }

    this.importPhase = 'images';
    this.importStage = 'Đang đồng bộ hình ảnh lên R2…';
    this.stopImagePolling();
    this.pollImages(job);
    this.imageTimer = setInterval(() => this.pollImages(job), 2000);
  }

  private pollImages(job: ImportJobStatus): void {
    this.http.get<ProductImageSyncStatus>(`${environment.apiUrl}/api/v1/products/images/sync-status`, {
      params: this.importStartedAt ? { since: this.importStartedAt } : {},
      observe: 'body',
      responseType: 'json',
    }).subscribe({
      next: (status) => {
        this.imagePending = status.pending;
        this.imageStored = status.stored;
        this.imageFailed = status.failed;
        this.imageTotal = status.total;
        if (status.pending === 0) {
          this.stopImagePolling();
          this.finishImport(job, status);
        }
        this.cdr.markForCheck();
      },
      error: () => {
        this.importStage = 'Mất kết nối tạm thời khi đồng bộ ảnh, đang thử lại…';
        this.cdr.markForCheck();
      },
    });
  }

  private finishImport(job: ImportJobStatus, images: ProductImageSyncStatus | null): void {
    this.importing = false;
    this.importPhase = 'done';
    this.importStage = '';
    this.importProgress = 100;
    this.stopImportTimers();

    let result = `Đã nhập ${job.created.toLocaleString('vi-VN')} mới, cập nhật ${job.updated.toLocaleString('vi-VN')}.`;
    if (job.imagesQueued > 0 && !job.imageStorageConfigured) {
      result += ` ${job.imagesQueued.toLocaleString('vi-VN')} ảnh đang chờ cấu hình R2.`;
    } else if (images) {
      result += ` Ảnh: ${images.stored.toLocaleString('vi-VN')} đã lưu`;
      if (images.failed > 0) result += `, ${images.failed.toLocaleString('vi-VN')} lỗi`;
      result += '.';
    }
    this.importResult = result;
    this.importReady = false;
    this.selectedFile = null;
    this.currentPage = 1;
    this.loadProducts();
    this.cdr.markForCheck();
  }

  private failImport(message: string): void {
    this.fileError = message;
    this.importing = false;
    this.importPhase = 'done';
    this.importStage = '';
    this.stopImportTimers();
    this.cdr.markForCheck();
  }

  private stopJobPolling(): void {
    if (this.jobTimer) {
      clearInterval(this.jobTimer);
      this.jobTimer = null;
    }
  }

  private stopImagePolling(): void {
    if (this.imageTimer) {
      clearInterval(this.imageTimer);
      this.imageTimer = null;
    }
  }

  private stopImportTimers(): void {
    this.stopJobPolling();
    this.stopImagePolling();
  }

  private normalizeHeader(value: string): string {
    return value.trim().toLocaleLowerCase('vi-VN').normalize('NFD').replace(/[\u0300-\u036f]/g, '');
  }

  private async readXlsx(buffer: ArrayBuffer): Promise<string[][]> {
    const bytes = new Uint8Array(buffer);
    const view = new DataView(buffer);
    const decoder = new TextDecoder();
    let eocd = -1;
    for (let offset = bytes.length - 22; offset >= Math.max(0, bytes.length - 65_557); offset -= 1) {
      if (view.getUint32(offset, true) === 0x06054b50) { eocd = offset; break; }
    }
    if (eocd < 0) throw new Error('Invalid XLSX zip');

    const entries = new Map<string, { method: number; size: number; offset: number }>();
    const entryCount = view.getUint16(eocd + 10, true);
    let cursor = view.getUint32(eocd + 16, true);
    for (let index = 0; index < entryCount; index += 1) {
      if (view.getUint32(cursor, true) !== 0x02014b50) throw new Error('Invalid XLSX directory');
      const method = view.getUint16(cursor + 10, true);
      const size = view.getUint32(cursor + 20, true);
      const nameLength = view.getUint16(cursor + 28, true);
      const extraLength = view.getUint16(cursor + 30, true);
      const commentLength = view.getUint16(cursor + 32, true);
      const localOffset = view.getUint32(cursor + 42, true);
      const name = decoder.decode(bytes.subarray(cursor + 46, cursor + 46 + nameLength));
      entries.set(name, { method, size, offset: localOffset });
      cursor += 46 + nameLength + extraLength + commentLength;
    }

    const readEntry = async (name: string): Promise<string> => {
      const entry = entries.get(name);
      if (!entry) throw new Error(`Missing ${name}`);
      const nameLength = view.getUint16(entry.offset + 26, true);
      const extraLength = view.getUint16(entry.offset + 28, true);
      const start = entry.offset + 30 + nameLength + extraLength;
      const compressed = bytes.subarray(start, start + entry.size);
      if (entry.method === 0) return decoder.decode(compressed);
      if (entry.method !== 8 || typeof DecompressionStream === 'undefined') throw new Error('Unsupported XLSX compression');
      const stream = new Blob([compressed]).stream().pipeThrough(new DecompressionStream('deflate-raw'));
      return decoder.decode(await new Response(stream).arrayBuffer());
    };

    const workbookXml = new DOMParser().parseFromString(await readEntry('xl/workbook.xml'), 'application/xml');
    const relationshipsXml = new DOMParser().parseFromString(await readEntry('xl/_rels/workbook.xml.rels'), 'application/xml');
    const sheetNode = workbookXml.getElementsByTagName('sheet')[0];
    const relationId = sheetNode?.getAttribute('r:id');
    const relation = Array.from(relationshipsXml.getElementsByTagName('Relationship')).find((item) => item.getAttribute('Id') === relationId);
    const target = relation?.getAttribute('Target');
    if (!target) throw new Error('Missing worksheet reference');
    const pathParts: string[] = [];
    for (const part of (target.startsWith('/') ? target.slice(1) : `xl/${target}`).replace(/\\/g, '/').split('/')) {
      if (part === '..') pathParts.pop();
      else if (part && part !== '.') pathParts.push(part);
    }
    const [sheetXml, sharedXml] = await Promise.all([
      readEntry(pathParts.join('/')),
      entries.has('xl/sharedStrings.xml') ? readEntry('xl/sharedStrings.xml') : Promise.resolve(''),
    ]);
    const sharedDoc = sharedXml ? new DOMParser().parseFromString(sharedXml, 'application/xml') : null;
    const shared = sharedDoc ? Array.from(sharedDoc.getElementsByTagName('si')).map((item) =>
      Array.from(item.getElementsByTagName('t')).map((text) => text.textContent ?? '').join('')) : [];
    const sheet = new DOMParser().parseFromString(sheetXml, 'application/xml');
    return Array.from(sheet.getElementsByTagName('row')).map((row) => {
      const cells: string[] = [];
      for (const cell of Array.from(row.getElementsByTagName('c'))) {
        const reference = cell.getAttribute('r') ?? '';
        const letters = reference.match(/^[A-Z]+/)?.[0] ?? '';
        let column = 0;
        for (const letter of letters) column = column * 26 + letter.charCodeAt(0) - 64;
        const type = cell.getAttribute('t');
        const value = type === 'inlineStr'
          ? Array.from(cell.getElementsByTagName('t')).map((text) => text.textContent ?? '').join('')
          : cell.getElementsByTagName('v')[0]?.textContent ?? '';
        cells[column - 1] = type === 's' ? shared[Number(value)] ?? '' : value;
      }
      return cells.map((value) => value ?? '');
    });
  }

  private createXlsx(headers: string[]): Uint8Array {
    const escapeXml = (value: string) => value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    const columnName = (index: number) => {
      let number = index + 1;
      let result = '';
      while (number) { const remainder = (number - 1) % 26; result = String.fromCharCode(65 + remainder) + result; number = Math.floor((number - 1) / 26); }
      return result;
    };
    const cells = headers.map((header, index) => `<c r="${columnName(index)}1" s="1" t="inlineStr"><is><t>${escapeXml(header)}</t></is></c>`).join('');
    const widths = [16, 24, 18, 18, 30, 22, 14, 14, 14, 16, 16, 14, 20, 12]
      .map((width, index) => `<col min="${index + 1}" max="${index + 1}" width="${width}" customWidth="1"/>`).join('');
    const files: Record<string, string> = {
      '[Content_Types].xml': '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>',
      '_rels/.rels': '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>',
      'xl/workbook.xml': '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="ProductTemplate" sheetId="1" r:id="rId1"/></sheets></workbook>',
      'xl/_rels/workbook.xml.rels': '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>',
      'xl/styles.xml': '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><color rgb="FFFFFFFF"/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF5B9BD5"/><bgColor indexed="64"/></patternFill></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1"/></cellXfs></styleSheet>',
      'xl/worksheets/sheet1.xml': `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews><cols>${widths}</cols><sheetData><row r="1" ht="24" customHeight="1">${cells}</row></sheetData><autoFilter ref="A1:N1"/></worksheet>`,
    };

    const encoder = new TextEncoder();
    const crcTable = new Uint32Array(256);
    for (let n = 0; n < 256; n += 1) { let c = n; for (let k = 0; k < 8; k += 1) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; crcTable[n] = c >>> 0; }
    const crc32 = (data: Uint8Array) => { let crc = 0xffffffff; for (const byte of data) crc = crcTable[(crc ^ byte) & 0xff] ^ (crc >>> 8); return (crc ^ 0xffffffff) >>> 0; };
    const localParts: Uint8Array[] = [];
    const centralParts: Uint8Array[] = [];
    let localOffset = 0;
    const write16 = (view: DataView, offset: number, value: number) => view.setUint16(offset, value, true);
    const write32 = (view: DataView, offset: number, value: number) => view.setUint32(offset, value, true);

    for (const [name, content] of Object.entries(files)) {
      const nameBytes = encoder.encode(name);
      const data = encoder.encode(content);
      const crc = crc32(data);
      const local = new Uint8Array(30 + nameBytes.length + data.length);
      const lv = new DataView(local.buffer);
      write32(lv, 0, 0x04034b50); write16(lv, 4, 20); write16(lv, 6, 0x0800); write16(lv, 8, 0);
      write32(lv, 14, crc); write32(lv, 18, data.length); write32(lv, 22, data.length); write16(lv, 26, nameBytes.length);
      local.set(nameBytes, 30); local.set(data, 30 + nameBytes.length);
      localParts.push(local);

      const central = new Uint8Array(46 + nameBytes.length);
      const cv = new DataView(central.buffer);
      write32(cv, 0, 0x02014b50); write16(cv, 4, 20); write16(cv, 6, 20); write16(cv, 8, 0x0800); write16(cv, 10, 0);
      write32(cv, 16, crc); write32(cv, 20, data.length); write32(cv, 24, data.length); write16(cv, 28, nameBytes.length); write32(cv, 42, localOffset);
      central.set(nameBytes, 46); centralParts.push(central);
      localOffset += local.length;
    }

    const centralSize = centralParts.reduce((sum, part) => sum + part.length, 0);
    const end = new Uint8Array(22);
    const ev = new DataView(end.buffer);
    write32(ev, 0, 0x06054b50); write16(ev, 8, centralParts.length); write16(ev, 10, centralParts.length);
    write32(ev, 12, centralSize); write32(ev, 16, localOffset);
    const result = new Uint8Array(localOffset + centralSize + end.length);
    let offset = 0;
    for (const part of localParts) { result.set(part, offset); offset += part.length; }
    for (const part of centralParts) { result.set(part, offset); offset += part.length; }
    result.set(end, offset);
    return result;
  }
}

interface ProductRow {
  id: string;
  sku: string;
  barcode: string | null;
  name: string;
  itemType: string;
  categoryPath: string | null;
  brand: string | null;
  salePrice: number | null;
  costPrice: number | null;
  stock: number | null;
  createdAt: string;
  imageId: string | null;
  imagesPending: number;
  imagesFailed: number;
}

interface ProductDetail extends ProductRow {
  minStock: number | null;
  maxStock: number | null;
  position: string | null;
  weight: number | null;
  description: string | null;
  isDirectSale: boolean;
  unitsJson: string;
  attributesJson: string;
  variantsJson: string;
}

interface ProductUnit {
  name: string;
  conversion: number | null;
  salePrice: number | null;
  isDirectSale: boolean;
}

interface ProductAttribute {
  name: string;
  values: string[];
}

interface ProductVariant {
  name: string;
  unit: string;
  conversion: number | null;
  sku: string;
  barcode: string;
  costPrice: number | null;
  salePrice: number | null;
  stock: number | null;
}

interface ProductForm {
  sku: string;
  barcode: string;
  name: string;
  categoryPath: string;
  brand: string;
  salePrice: number | null;
  costPrice: number | null;
  stock: number | null;
  minStock: number | null;
  maxStock: number | null;
  position: string;
  weight: number | null;
  description: string;
  isDirectSale: boolean;
  units: ProductUnit[];
  attributes: ProductAttribute[];
  variants: ProductVariant[];
}

function emptyProductForm(): ProductForm {
  return {
    sku: '',
    barcode: '',
    name: '',
    categoryPath: '',
    brand: '',
    salePrice: null,
    costPrice: null,
    stock: null,
    minStock: null,
    maxStock: null,
    position: '',
    weight: null,
    description: '',
    isDirectSale: true,
    units: [{ name: 'Cái', conversion: null, salePrice: null, isDirectSale: true }],
    attributes: [],
    variants: [],
  };
}

function parseJsonArray<T>(value: string | null | undefined): T[] {
  if (!value) return [];
  try {
    const parsed = JSON.parse(value);
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

function formFromProduct(product: ProductRow | ProductDetail): ProductForm {
  const detail = product as Partial<ProductDetail>;
  const numberOrNull = (value: unknown): number | null =>
    value === null || value === undefined || value === '' ? null : Number(value);
  const units = parseJsonArray<Record<string, unknown>>(detail.unitsJson).map((unit) => ({
    name: String(unit['name'] ?? unit['Name'] ?? ''),
    conversion: numberOrNull(unit['conversion'] ?? unit['Conversion']),
    salePrice: numberOrNull(unit['salePrice'] ?? unit['SalePrice']),
    isDirectSale: Boolean(unit['isDirectSale'] ?? unit['IsDirectSale'] ?? true),
  }));
  return {
    sku: product.sku ?? '',
    barcode: product.barcode ?? '',
    name: product.name ?? '',
    categoryPath: product.categoryPath ?? '',
    brand: product.brand ?? '',
    salePrice: product.salePrice,
    costPrice: product.costPrice,
    stock: product.stock,
    minStock: detail.minStock ?? null,
    maxStock: detail.maxStock ?? null,
    position: detail.position ?? '',
    weight: detail.weight ?? null,
    description: detail.description ?? '',
    isDirectSale: detail.isDirectSale ?? true,
    units: units.length ? units : [{ name: 'Cái', conversion: null, salePrice: product.salePrice, isDirectSale: true }],
    attributes: parseJsonArray<Record<string, unknown>>(detail.attributesJson).map((attribute) => ({
      name: String(attribute['name'] ?? attribute['Name'] ?? ''),
      values: ((attribute['values'] ?? attribute['Values'] ?? []) as string[]).map((value) => String(value)),
    })),
    variants: parseJsonArray<Record<string, unknown>>(detail.variantsJson).map((variant) => ({
      name: String(variant['name'] ?? variant['Name'] ?? ''),
      unit: String(variant['unit'] ?? variant['Unit'] ?? ''),
      conversion: numberOrNull(variant['conversion'] ?? variant['Conversion']),
      sku: String(variant['sku'] ?? variant['Sku'] ?? ''),
      barcode: String(variant['barcode'] ?? variant['Barcode'] ?? ''),
      costPrice: numberOrNull(variant['costPrice'] ?? variant['CostPrice']),
      salePrice: numberOrNull(variant['salePrice'] ?? variant['SalePrice']),
      stock: numberOrNull(variant['stock'] ?? variant['Stock']),
    })),
  };
}

interface ProductPage { items: ProductRow[]; total: number; page: number; pageSize: number; }
