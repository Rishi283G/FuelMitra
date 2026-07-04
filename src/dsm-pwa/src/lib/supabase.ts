import { createClient } from '@supabase/supabase-js';

const SUPABASE_URL = import.meta.env.VITE_SUPABASE_URL as string;
const SUPABASE_ANON_KEY = import.meta.env.VITE_SUPABASE_ANON_KEY as string;

if (!SUPABASE_URL || !SUPABASE_ANON_KEY) {
  console.warn('Supabase credentials not configured. Set VITE_SUPABASE_URL and VITE_SUPABASE_ANON_KEY in .env.local');
}

export const supabase = createClient(
  SUPABASE_URL || 'https://placeholder.supabase.co',
  SUPABASE_ANON_KEY || 'placeholder'
);

// ─── Types ─────────────────────────────────────────────────────────────────
export interface DsmUserProfile {
  id: string; // SyncGuid of the DsmUser (primary key in DsmUsers, used for references)
  AuthUserId: string; // Supabase Auth UUID
  EmployeeCode: string;
  FullName: string;
  MobileNumber: string;
  AssignedPump: number | null;
  ConnectedPump: number | null;
  AssignedShift: 'A' | 'B' | 'C' | null;
  AssignedDate: string | null;
  StationId: string;
}

export interface SubmissionPayload {
  DsmUserId: string;
  StationId: string;
  PumpId: number;
  ShiftDate: string;  // ISO date string
  ShiftType: 'A' | 'B' | 'C';
  Notes?: string;
  AttachmentUrl?: string;
}

export interface NozzleReadingPayload {
  SubmissionId: string;
  PumpId: number;
  NozzleId: number;
  OpeningReading: number;
  ClosingReading: number;
  Rate: number;
}

export interface CollectionPayload {
  SubmissionId: string;
  Cash: number;
  UPI: number;
  Card: number;
  Credit: number;
  Expense: number;
  ExpenseNotes?: string;
  Short: number;
  Excess: number;
}

export interface SubmissionRecord {
  Id: string;
  DsmUserId: string;
  StationId: string;
  PumpId: number;
  ShiftDate: string;
  ShiftType: string;
  Status: 'Pending' | 'Approved' | 'Rejected' | 'Expired';
  SubmittedAt: string;
  Notes?: string;
  AttachmentUrl?: string;
  RejectionReason?: string;
  ApprovedAt?: string;
}

export interface DsmNotification {
  Id: string;
  Message: string;
  IsRead: boolean;
  CreatedAt: string;
}
