import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export type CashflowType = 'Income' | 'Expense';
export type CashflowMethod = 'Cash' | 'Transfer' | 'Card' | 'Wallet' | 'Other';

export interface CashTransactionItem {
  id: string;
  code: string;
  type: CashflowType;
  method: CashflowMethod;
  amount: number;
  category: string | null;
  partnerName: string | null;
  referenceCode: string | null;
  note: string | null;
  transactedAt: string;
  createdByName: string | null;
}

export interface CashbookSummary {
  income: number;
  expense: number;
  balance: number;
}

export interface CashbookPage {
  items: CashTransactionItem[];
  total: number;
  page: number;
  pageSize: number;
  summary: CashbookSummary;
}

export interface CreateCashTransactionRequest {
  type: CashflowType;
  method: CashflowMethod;
  amount: number;
  category?: string | null;
  partnerName?: string | null;
  referenceCode?: string | null;
  note?: string | null;
  transactedAt?: string | null;
}

/** Gọi API sổ quỹ: danh sách phiếu thu/chi, tạo phiếu, tổng kết số dư. */
@Injectable({ providedIn: 'root' })
export class CashbookService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/v1/cashbook`;

  list(
    options: {
      page?: number;
      pageSize?: number;
      type?: CashflowType;
      method?: CashflowMethod;
      from?: string;
      to?: string;
      search?: string;
    } = {},
  ): Observable<CashbookPage> {
    let params = new HttpParams();
    if (options.page) params = params.set('page', options.page);
    if (options.pageSize) params = params.set('pageSize', options.pageSize);
    if (options.type) params = params.set('type', options.type);
    if (options.method) params = params.set('method', options.method);
    if (options.from) params = params.set('from', options.from);
    if (options.to) params = params.set('to', options.to);
    if (options.search) params = params.set('search', options.search);
    return this.http.get<CashbookPage>(this.baseUrl, { params });
  }

  create(request: CreateCashTransactionRequest): Observable<CashTransactionItem> {
    return this.http.post<CashTransactionItem>(this.baseUrl, request);
  }
}
