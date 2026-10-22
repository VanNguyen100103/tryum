import { HttpClient, HttpEventType } from '@angular/common/http';
import { ChangeDetectorRef, Component, inject, OnDestroy, OnInit } from '@angular/core';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-product-list',
  imports: [],
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
  importReady = false;
  importing = false;
  importProgress: number | null = 0;
  importStage = '';
  importResult = '';
  private selectedFile: File | null = null;
  products: ProductRow[] = [];
  totalProducts = 0;
  currentPage = 1;
  readonly pageSize = 50;
  productsLoading = false;
  productsError = '';
  readonly imageSources = new Map<string, string>();
  get pageStart(): number { return this.totalProducts === 0 ? 0 : (this.currentPage - 1) * this.pageSize + 1; }
  get pageEnd(): number { return Math.min(this.currentPage * this.pageSize, this.totalProducts); }

  ngOnInit(): void { this.loadProducts(); }

  ngOnDestroy(): void { this.clearImageSources(); }

  loadProducts(): void {
    this.productsLoading = true;
    this.productsError = '';
    this.clearImageSources();
    this.http.get<ProductPage>(`${environment.apiUrl}/api/v1/products`, {
      params: { page: this.currentPage, pageSize: this.pageSize },
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

  private clearImageSources(): void {
    for (const url of this.imageSources.values()) URL.revokeObjectURL(url);
    this.imageSources.clear();
  }

  openImport(): void {
    this.importOpen = true;
  }

  closeImport(): void {
    this.importOpen = false;
  }

  clearFile(): void {
    this.fileName = '';
    this.fileError = '';
    this.fileSummary = '';
    this.importReady = false;
    this.importResult = '';
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
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    this.clearFile();
    this.fileName = file.name;
    input.value = '';

    const extension = file.name.split('.').pop()?.toLowerCase();
    if (extension !== 'xlsx') {
      this.fileError = 'API import hiện nhận file Excel .xlsx.';
      return;
    }
    if (file.size > 10 * 1024 * 1024) {
      this.fileError = 'File vượt quá giới hạn 10 MB.';
      return;
    }

    try {
      const rows = await this.readXlsx(await file.arrayBuffer());
      const headers = (rows.shift() ?? []).map((value) => this.normalizeHeader(value));
      const hasCode = headers.some((header) => ['ma hang', 'ma hang hoa'].includes(header));
      const hasName = headers.some((header) => ['ten hang', 'ten hang hoa'].includes(header));

      if (!hasCode || !hasName) {
        this.fileError = 'Thiếu cột bắt buộc: Mã hàng và Tên hàng.';
        return;
      }

      const dataRows = rows.filter((row) => row.some((cell) => cell.trim().length > 0));
      this.fileSummary = `Đã đọc ${dataRows.length} dòng; đủ cột Mã hàng và Tên hàng.`;
      this.importReady = true;
      this.selectedFile = file;
    } catch {
      this.fileError = 'Không đọc được file. Hãy dùng file .xlsx hợp lệ theo mẫu.';
    }
  }

  importFile(): void {
    if (!this.selectedFile || !this.importReady || this.importing) return;
    this.importing = true;
    this.importProgress = 0;
    this.importStage = 'Đang tải file lên máy chủ…';
    this.importResult = '';
    const form = new FormData();
    form.append('file', this.selectedFile, this.selectedFile.name);
    this.http.post<{ created: number; updated: number; imagesQueued: number; imageStorageConfigured: boolean; message: string }>(
      `${environment.apiUrl}/api/v1/products/import`, form, { observe: 'events', reportProgress: true },
    ).subscribe({
      next: (event) => {
        if (event.type === HttpEventType.UploadProgress) {
          if (event.total) {
            this.importProgress = Math.min(100, Math.round(event.loaded / event.total * 100));
            this.importStage = this.importProgress >= 100
              ? 'Đã tải file lên. Máy chủ đang kiểm tra và lưu sản phẩm…'
              : `Đang tải file lên máy chủ… ${this.importProgress}%`;
          } else {
            this.importProgress = null;
            this.importStage = 'Đang tải file lên máy chủ…';
          }
          this.cdr.markForCheck();
          return;
        }
        if (event.type !== HttpEventType.Response || !event.body) return;
        const result = event.body;
        this.importResult = `${result.message} Thêm ${result.created}, cập nhật ${result.updated}, ảnh chờ đồng bộ ${result.imagesQueued}.`;
        this.importing = false;
        this.importProgress = 0;
        this.importStage = '';
        this.importReady = false;
        this.selectedFile = null;
        this.currentPage = 1;
        this.loadProducts();
        this.cdr.markForCheck();
      },
      error: (error: { error?: { message?: string } }) => {
        this.fileError = error.error?.message || 'Không kết nối được API production. Dữ liệu chưa được nhập.';
        this.importing = false;
        this.importProgress = 0;
        this.importStage = '';
        this.cdr.markForCheck();
      },
    });
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
  imageId: string | null;
  imagesPending: number;
  imagesFailed: number;
}

interface ProductPage { items: ProductRow[]; total: number; page: number; pageSize: number; }
