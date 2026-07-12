export type TenantId = string;
export type Timestamp = string;

export interface Money {
  amount: number;
  currency: "NPR";
}

export interface Address {
  line1: string;
  line2?: string;
  city: string;
  district: string;
  province?: string;
  postalCode?: string;
  country: "NP";
  contactName: string;
  contactPhone: string;
}

export interface PaginatedResult<T> {
  items: T[];
  cursor: string | null;
  hasMore: boolean;
  totalCount?: number;
}

export interface ApiError {
  type: string;
  title: string;
  status: number;
  detail: string;
  errors?: Record<string, string[]>;
}
