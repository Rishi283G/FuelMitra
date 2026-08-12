import Dexie, { type Table } from 'dexie';

export interface DraftNozzleReading {
  nozzleId: number;
  fuelType: string;
  openingReading: number;
  closingReading: number;
  rate: number;
  pumpId?: number;
  testing?: number;
}

export type PaymentType = 'PhonePe' | 'PineLabs' | 'PetroCard';
export type BusinessPeriod = 'Morning' | 'Day' | 'Night';

export interface SettlementEntry {
  paymentType: PaymentType;
  period: BusinessPeriod;
  amount: number;
  tid: string;
  batch: string;
  businessDate: string;    // YYYY-MM-DD
  operationalShift: string; // 'A' or 'B'
}

export interface DraftSubmission {
  id?: number; // auto-increment local key
  draftId: string; // client-generated uuid
  pumpId: number;
  shiftDate: string;    // YYYY-MM-DD
  shiftType: 'A' | 'B' | 'C';
  notes: string;
  attachmentUrl?: string;
  nozzleReadings: DraftNozzleReading[];
  cash: number;
  upi: number;
  card: number;
  petroCard?: number;
  cashDeposit?: number;
  others?: number;
  credit: number;
  expense: number;
  expenseNotes: string;
  expenseEntries?: { description: string; amount: number; }[];
  short: number;
  excess: number;
  createdAt: string;
  status: 'draft' | 'queued' | 'submitted' | 'failed';
  errorMessage?: string;
  cardSwipeDetails?: { mode: string; amount: number; tid: string; batch: string; }[];
  settlements?: SettlementEntry[];
  debtorEntries?: { debtorName: string; amount: number; vehicleNumber?: string; slipNumber?: string; time: string; }[];
  phonePeMorning?: number;
  phonePeTidMorning?: string;
  phonePeBatchMorning?: string;
  phonePeNight?: number;
  phonePeTidNight?: string;
  phonePeBatchNight?: string;
  creditCardMorning?: number;
  creditCardTidMorning?: string;
  creditCardBatchMorning?: string;
  creditCardNight?: number;
  creditCardTidNight?: string;
  creditCardBatchNight?: string;
  petroCardMorning?: number;
  petroCardTidMorning?: string;
  petroCardBatchMorning?: string;
  petroCardNight?: number;
  petroCardTidNight?: string;
  petroCardBatchNight?: string;
  personalDebtors?: { amount: number; fuelProduct?: string; remarks?: string; paymentMethod: string; tid?: string; batch?: string; denom500?: number; denom200?: number; denom100?: number; denom50?: number; denom20?: number; denom10?: number; coins?: number; }[];
  khandhareEntries?: { name: string; vehicleNumber?: string; slipNumber: string; amount: number; }[];
  oilDefSales?: { productId: number; productName: string; category: string; unit: string; quantity: number; price: number; total: number; }[];
}

export interface CachedProduct {
  id: number;
  productName: string;
  category: string; // 'Oil' | 'DEF'
  unit: string;
  defaultSaleRate: number;
}

export interface CachedStock {
  productId: number;
  remainingStock: number;
}

export interface CachedSubmission {
  id?: number;
  remoteId: string;
  pumpId: number;
  shiftDate: string;
  shiftType: string;
  status: string;
  submittedAt: string;
  rejectionReason?: string;
  cachedAt: string;
}

export class DsmDatabase extends Dexie {
  drafts!: Table<DraftSubmission>;
  submissions!: Table<CachedSubmission>;
  products!: Table<CachedProduct>;
  stockBalances!: Table<CachedStock>;

  constructor() {
    super('DsmPwaDB');

    this.version(1).stores({
      drafts: '++id, draftId, status, shiftDate',
      submissions: '++id, remoteId, status, shiftDate',
    });

    // Version 2 to support settlements array in drafts/submissions locally
    this.version(2).stores({
      drafts: '++id, draftId, status, shiftDate',
      submissions: '++id, remoteId, status, shiftDate',
    });

    // Version 3 to support products and stock balances caching locally
    this.version(3).stores({
      drafts: '++id, draftId, status, shiftDate',
      submissions: '++id, remoteId, status, shiftDate',
      products: 'id, productName, category',
      stockBalances: 'productId'
    });
  }
}

export const db = new DsmDatabase();
