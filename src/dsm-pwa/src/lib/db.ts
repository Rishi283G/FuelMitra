import Dexie, { type Table } from 'dexie';

export interface DraftNozzleReading {
  nozzleId: number;
  fuelType: string;
  openingReading: number;
  closingReading: number;
  rate: number;
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
  credit: number;
  expense: number;
  expenseNotes: string;
  short: number;
  excess: number;
  createdAt: string;
  status: 'draft' | 'queued' | 'submitted' | 'failed';
  errorMessage?: string;
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
