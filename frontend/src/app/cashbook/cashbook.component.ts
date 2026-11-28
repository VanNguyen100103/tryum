import { DecimalPipe, DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  CashbookService,
  CashflowMethod,
  CashflowType,
  CashTransactionItem,
} from '../core/services/cashbook.service';
import { ToastService } from '../core/services/toast.service';

type FilterType = '' | CashflowType;

@Component({
  selector: 'app-cashbook',
  imports: [FormsModule, DecimalPipe, DatePipe],
  templateUrl: './cashbook.component.html',
  styleUrl: './cashbook.component.scss',
})
export class CashbookComponent implements OnInit {
  private readonly cashbook = inject(CashbookService);
  private readonly toast = inject(ToastService);

  readonly items = signal<CashTransactionItem[]>([]);
  readonly total = signal(0);
  readonly income = signal(0);
  readonly expense = signal(0);
  readonly balance = signal(0);
  readonly loading = signal(true);
  readonly error = signal('');

  readonly page = signal(1);
  readonly pageSize = 50;
  readonly search = signal('');
  readonly filterType = signal<FilterType>('');

  // ── Form tạo phiếu thu/chi ────────────────────────────────────────────────
  readonly dialogOpen = signal(false);
  readonly dialogMode = signal<CashflowType>('Income');
  readonly saving = signal(false);
  readonly formError = signal('');
  readonly form = {
    amount: '',
    method: 'Cash' as CashflowMethod,
    category: '',
    partnerName: '',
    referenceCode: '',
    note: '',
  };

  readonly categories = {
    income: ['Bán hàng', 'Khách trả nợ', 'Thu khác'],
    expense: ['Nhập hàng', 'Lương', 'Điện nước', 'Mặt bằng', 'Chi phí khác'],
  } as const;

  get pageStart(): number {
    return this.total() === 0 ? 0 : (this.page() - 1) * this.pageSize + 1;
  }
  get pageEnd(): number {
    return Math.min(this.page() * this.pageSize, this.total());
  }

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set('');
    this.cashbook
      .list({
        page: this.page(),
        pageSize: this.pageSize,
        type: this.filterType() || undefined,
        search: this.search().trim() || undefined,
      })
      .subscribe({
        next: (result) => {
          this.items.set(result.items);
          this.total.set(result.total);
          this.income.set(result.summary.income);
          this.expense.set(result.summary.expense);
          this.balance.set(result.summary.balance);
          this.loading.set(false);
        },
        error: () => {
          this.items.set([]);
          this.total.set(0);
          this.loading.set(false);
          this.error.set('Chưa kết nối được API sổ quỹ. Dữ liệu chưa được tải.');
        },
      });
  }

  onSearch(value: string): void {
    this.search.set(value);
    this.page.set(1);
    this.load();
  }

  onFilterType(value: FilterType): void {
    this.filterType.set(value);
    this.page.set(1);
    this.load();
  }

  previousPage(): void {
    if (this.page() > 1) {
      this.page.update((page) => page - 1);
      this.load();
    }
  }
  nextPage(): void {
    if (this.page() * this.pageSize < this.total()) {
      this.page.update((page) => page + 1);
      this.load();
    }
  }

  openDialog(mode: 'income' | 'expense'): void {
    this.dialogMode.set(mode === 'income' ? 'Income' : 'Expense');
    this.formError.set('');
    this.form.amount = '';
    this.form.method = 'Cash';
    this.form.category = '';
    this.form.partnerName = '';
    this.form.referenceCode = '';
    this.form.note = '';
    this.dialogOpen.set(true);
  }

  closeDialog(): void {
    if (this.saving()) return;
    this.dialogOpen.set(false);
  }

  selectCategory(category: string): void {
    this.form.category = category;
  }

  methodLabel(method: CashflowMethod): string {
    switch (method) {
      case 'Cash':
        return 'Tiền mặt';
      case 'Transfer':
        return 'Chuyển khoản';
      case 'Card':
        return 'Thẻ';
      case 'Wallet':
        return 'Ví';
      default:
        return 'Khác';
    }
  }

  save(): void {
    if (this.saving()) return;
    const amount = Number(this.form.amount);
    if (!Number.isFinite(amount) || amount <= 0) {
      this.formError.set('Số tiền phải lớn hơn 0.');
      return;
    }
    if (!this.form.category.trim()) {
      this.formError.set('Vui lòng chọn nhóm thu/chi.');
      return;
    }

    this.saving.set(true);
    this.formError.set('');
    const type = this.dialogMode();
    this.cashbook
      .create({
        type,
        method: this.form.method,
        amount,
        category: this.form.category.trim(),
        partnerName: this.form.partnerName.trim() || null,
        referenceCode: this.form.referenceCode.trim() || null,
        note: this.form.note.trim() || null,
      })
      .subscribe({
        next: (created) => {
          this.saving.set(false);
          this.dialogOpen.set(false);
          this.toast.add({
            severity: 'success',
            summary: type === 'Income' ? 'Đã ghi nhận khoản thu' : 'Đã ghi nhận khoản chi',
            detail: `${created.code} · ${created.amount.toLocaleString('vi-VN')} đ`,
          });
          this.page.set(1);
          this.load();
        },
        error: (err: { error?: { message?: string } }) => {
          this.saving.set(false);
          this.formError.set(err.error?.message || 'Không lưu được phiếu thu/chi.');
        },
      });
  }
}
