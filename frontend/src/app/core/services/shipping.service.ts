import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface LocationOption {
  id: string;
  name: string;
}

export interface ShippingRate {
  id: string;
  carrierName: string | null;
  carrierShortName: string | null;
  carrierLogo: string | null;
  service: string;
  expected: string | null;
  totalFee: number;
  codFee: number;
  serviceFee: number;
  successPercent: number | null;
}

export interface PickupAddress {
  name: string;
  phone: string | null;
  street: string | null;
  cityId: string | null;
  districtId: string | null;
  wardId: string | null;
}

export interface ShippingRateRequest {
  toCityId: string;
  toDistrictId: string;
  weightGrams: number;
  lengthCm?: number;
  widthCm?: number;
  heightCm?: number;
  codAmount?: number;
  declaredAmount?: number;
}

export interface CreateShipmentRequest {
  orderId?: string | null;
  rateId: string;
  toName: string;
  toPhone: string;
  toAddress: string;
  toCityId: string;
  toCityName?: string | null;
  toDistrictId: string;
  toDistrictName?: string | null;
  toWardId: string;
  toWardName?: string | null;
  weightGrams: number;
  lengthCm?: number;
  widthCm?: number;
  heightCm?: number;
  codAmount?: number;
  declaredAmount?: number;
  senderPaysShipping?: boolean;
  service?: string | null;
  expected?: string | null;
  note?: string | null;
}

/** Gọi API vận chuyển Goship cho màn Bán giao hàng. */
@Injectable({ providedIn: 'root' })
export class ShippingService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/v1/shipping`;

  pickup(): Observable<PickupAddress> {
    return this.http.get<PickupAddress>(`${this.baseUrl}/pickup`);
  }

  savePickup(request: { cityId: string; districtId: string; wardId: string }): Observable<PickupAddress> {
    return this.http.put<PickupAddress>(`${this.baseUrl}/pickup`, request);
  }

  cities(): Observable<{ cities: LocationOption[] }> {
    return this.http.get<{ cities: LocationOption[] }>(`${this.baseUrl}/cities`);
  }

  districts(cityId: string): Observable<{ districts: LocationOption[] }> {
    return this.http.get<{ districts: LocationOption[] }>(`${this.baseUrl}/districts`, {
      params: { cityId },
    });
  }

  wards(districtId: string): Observable<{ wards: LocationOption[] }> {
    return this.http.get<{ wards: LocationOption[] }>(`${this.baseUrl}/wards`, {
      params: { districtId },
    });
  }

  rates(request: ShippingRateRequest): Observable<{ rates: ShippingRate[] }> {
    return this.http.post<{ rates: ShippingRate[] }>(`${this.baseUrl}/rates`, request);
  }

  createShipment(request: CreateShipmentRequest): Observable<{ message: string; shipment: unknown }> {
    return this.http.post<{ message: string; shipment: unknown }>(`${this.baseUrl}/shipments`, request);
  }
}
