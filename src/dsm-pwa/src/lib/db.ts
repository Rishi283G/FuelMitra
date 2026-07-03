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
  short: number;
  excess: number;
  createdAt: string;
  status: 'draft' | 'queued' | 'submitted' | 'failed';
  errorMessage?: string;
  cardSwipeDetails?: { mode: string; amount: number; tid: string; batch: string; }[];
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

  constructor() {
    super('DsmPwaDB');

    this.version(1).stores({
      drafts: '++id, draftId, status, shiftDate',
      submissions: '++id, remoteId, status, shiftDate',
    });
  }
}

export const db = new DsmDatabase();
