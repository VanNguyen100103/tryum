import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export type OrderStatus = 'Pending' | 'PartiallyPaid' | 'Completed' | 'Cancelled';
export type SaleChannel = 'Quick' | 'Regular' | 'Delivery';
export type PaymentMethod = 'Cash' | 'Transfer' | 'Card' | 'Wallet' | 'Voucher';
export type PaymentStatus = 'Pending' | 'Confirmed' | 'Cancelled';
export type PosQrStatus = 'Pending' | 'Paid' | 'Expired' | 'Used';

/** Phiên QR tại quầy, tạo trước khi chốt hóa đơn (thanh toán kết hợp). */
export interface PosQrSession {
  id: string;
  reference: string;
  amount: number;
  qrUrl: string;
  status: PosQrStatus;
  transferredAmount: number | null;
  paidAt: string | null;
  expiresInSeconds: number;
}

export interface OrderLine {
  id: string;
  productId: string;
  sku: string;
  productName: string;
  unitName: string | null;
  unitPrice: number;
  quantity: number;
  discount: number;
  lineTotal: number;
}

export interface OrderPayment {
  id: string;
  method: PaymentMethod;
  status: PaymentStatus;
  amount: number;
  provider: string | null;
  reference: string | null;
  paidAt: string | null;
}

export interface SalesOrder {
  id: string;
  code: string;
  status: OrderStatus;
  channel: SaleChannel;
  customerId: string | null;
  customerName: string | null;
  customerPhone: string | null;
  soldByName: string | null;
  subtotal: number;
  discount: number;
  total: number;
  paidAmount: number;
  remaining: number;
  changeAmount: number;
  pointsRedeemed: number;
  pointsDiscount: number;
  pointsEarned: number;
  note: string | null;
  couponCode: string | null;
  deliveryAddress: string | null;
  shippingFee?: number;
  recipientName?: string | null;
  recipientPhone?: string | null;
  deliveryCityName?: string | null;
  deliveryDistrictName?: string | null;
  deliveryWardName?: string | null;
  shippingCarrier?: string | null;
  trackingNumber?: string | null;
  createdAt: string;
  lines: OrderLine[];
  payments: OrderPayment[];
}

export interface OrderSummary {
  id: string;
  code: string;
  status: OrderStatus;
  channel: SaleChannel;
  customerName: string | null;
  total: number;
  paidAmount: number;
  remaining: number;
  itemCount: number;
  quantity: number;
  paymentMethods: string;
  createdAt: string;
}

export interface CreateOrderLine {
  productId: string;
  quantity: number;
  unitPrice?: number;
  discount?: number;
}

export interface CreateOrderPayment {
  method: PaymentMethod;
  amount: number;
  reference?: string | null;
}

export interface CreateOrderRequest {
  customerId?: string | null;
  customerName?: string | null;
  customerPhone?: string | null;
  note?: string | null;
  couponCode?: string | null;
  discount?: number;
  channel?: SaleChannel;
  deliveryAddress?: string | null;
  shippingFee?: number;
  recipientName?: string | null;
  recipientPhone?: string | null;
  deliveryCityId?: string | null;
  deliveryDistrictId?: string | null;
  deliveryWardId?: string | null;
  deliveryCityName?: string | null;
  deliveryDistrictName?: string | null;
  deliveryWardName?: string | null;
  lines: CreateOrderLine[];
  payments: CreateOrderPayment[];
  pointsToRedeem?: number;
  creditOverpayment?: boolean;
}

export interface VietQrResponse {
  imageUrl: string;
  amount: number;
  orderCode: string;
  bankName: string | null;
  accountNumber: string | null;
  accountName: string | null;
  template: string;
  expiresInSeconds: number;
}

export interface InvoiceQrResponse {
  imageUrl: string | null;
  amount: number;
  orderCode: string;
}

export interface StoreInfo {
  name: string;
  storeCode: string | null;
  phone: string | null;
  address: string | null;
}

export interface SePaySettings {
  webhookUrl: string;
  webhookSecured: boolean;
  authMode: string;
  bank: {
    configured: boolean;
    bin: string | null;
    name: string | null;
    accountNumber: string | null;
    accountName: string | null;
  };
  vietQrTemplate: string;
}

/** Gọi API bán hàng (đơn hàng, thanh toán, VietQR) và cấu hình SePay. */
@Injectable({ providedIn: 'root' })
export class SalesService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/v1`;

  createOrder(request: CreateOrderRequest): Observable<SalesOrder> {
    return this.http.post<SalesOrder>(`${this.baseUrl}/orders`, request);
  }

  getOrder(orderId: string): Observable<SalesOrder> {
    return this.http.get<SalesOrder>(`${this.baseUrl}/orders/${orderId}`);
  }

  listOrders(options: {
    page?: number;
    pageSize?: number;
    search?: string;
    status?: OrderStatus;
    from?: string;
    to?: string;
  } = {}): Observable<{ items: OrderSummary[]; total: number }> {
    let params = new HttpParams();
    if (options.page) params = params.set('page', options.page);
    if (options.pageSize) params = params.set('pageSize', options.pageSize);
    if (options.search) params = params.set('search', options.search);
    if (options.status) params = params.set('status', options.status);
    if (options.from) params = params.set('from', options.from);
    if (options.to) params = params.set('to', options.to);
    return this.http.get<{ items: OrderSummary[]; total: number }>(`${this.baseUrl}/orders`, { params });
  }

  addPayment(
    orderId: string,
    request: { method: PaymentMethod; amount: number; reference?: string | null; confirm: boolean },
  ): Observable<SalesOrder> {
    return this.http.post<SalesOrder>(`${this.baseUrl}/orders/${orderId}/payments`, request);
  }

  cancelOrder(orderId: string): Observable<SalesOrder> {
    return this.http.post<SalesOrder>(`${this.baseUrl}/orders/${orderId}/cancel`, {});
  }

  getVietQr(orderId: string): Observable<VietQrResponse> {
    return this.http.get<VietQrResponse>(`${this.baseUrl}/orders/${orderId}/vietqr`);
  }

  /** Mở phiên QR cho số còn thiếu (thanh toán kết hợp) - poll cho tới khi webhook báo Paid. */
  createPosQr(amount: number): Observable<PosQrSession> {
    return this.http.post<PosQrSession>(`${this.baseUrl}/payments/pos/qr`, { amount });
  }

  getPosQr(sessionId: string): Observable<PosQrSession> {
    return this.http.get<PosQrSession>(`${this.baseUrl}/payments/pos/qr/${sessionId}`);
  }

  confirmPosQr(sessionId: string): Observable<PosQrSession> {
    return this.http.post<PosQrSession>(`${this.baseUrl}/payments/pos/qr/${sessionId}/confirm`, {});
  }

  getStore(): Observable<StoreInfo> {
    return this.http.get<StoreInfo>(`${this.baseUrl}/store`);
  }

  /** QR in trên hóa đơn - BE trả cả khi đơn đã thanh toán (khác getVietQr). */
  getInvoiceQr(orderId: string): Observable<InvoiceQrResponse> {
    return this.http.get<InvoiceQrResponse>(`${this.baseUrl}/orders/${orderId}/invoice-qr`);
  }

  sePaySettings(): Observable<SePaySettings> {
    return this.http.get<SePaySettings>(`${this.baseUrl}/payments/sepay/settings`);
  }

  simulateTransfer(orderCode: string, amount?: number): Observable<{
    success: boolean;
    outcome: string;
    message: string;
    orderCode: string | null;
    amount: number;
    status: OrderStatus | null;
  }> {
    return this.http.post<{
      success: boolean;
      outcome: string;
      message: string;
      orderCode: string | null;
      amount: number;
      status: OrderStatus | null;
    }>(`${this.baseUrl}/payments/sepay/simulate`, { orderCode, amount });
  }
}
