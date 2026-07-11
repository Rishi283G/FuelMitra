import { createClient } from '@supabase/supabase-js';

const SUPABASE_URL = import.meta.env.VITE_SUPABASE_URL as string;
const SUPABASE_ANON_KEY = import.meta.env.VITE_SUPABASE_ANON_KEY as string;

if (!SUPABASE_URL || !SUPABASE_ANON_KEY) {
  if (typeof document !== 'undefined') {
    const showOverlay = () => {
      const rootEl = document.getElementById('root');
      if (rootEl) {
        rootEl.innerHTML = `
          <div style="
            display: flex;
            flex-direction: column;
            align-items: center;
            justify-content: center;
            height: 100vh;
            background-color: #0d0e12;
            color: #fff;
            font-family: system-ui, -apple-system, sans-serif;
            padding: 20px;
            text-align: center;
          ">
            <div style="
              background-color: #1e1e24;
              border: 1px solid #ff5722;
              border-radius: 12px;
              padding: 30px;
              max-width: 450px;
              box-shadow: 0 8px 30px rgba(0, 0, 0, 0.5);
            ">
              <div style="font-size: 40px; margin-bottom: 16px;">⚠️</div>
              <h2 style="color: #ff5722; margin-top: 0; font-size: 20px;">Configuration Error</h2>
              <p style="color: #a0aec0; line-height: 1.6; font-size: 14px; margin: 10px 0 0 0;">
                The DSM Portal environment variables are not configured.<br/><br/>
                Please define <strong>VITE_SUPABASE_URL</strong> and <strong>VITE_SUPABASE_ANON_KEY</strong> in your hosting provider (e.g. Vercel) or .env file.
              </p>
            </div>
          </div>
        `;
      }
    };
    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', showOverlay);
    } else {
      setTimeout(showOverlay, 0);
    }
  }
  throw new Error('Supabase configuration is missing. Configure VITE_SUPABASE_URL and VITE_SUPABASE_ANON_KEY.');
}

export const supabase = createClient(SUPABASE_URL, SUPABASE_ANON_KEY);

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
