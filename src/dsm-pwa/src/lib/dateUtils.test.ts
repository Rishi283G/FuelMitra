import { describe, it, expect } from 'vitest';
import {
  parseAssignmentDateToYMD,
  getTodayYMD,
  formatYMDForDisplay,
  parseAssignmentTimestamp,
  selectLatestActiveAssignment,
  type CandidateAssignment,
} from './dateUtils';

describe('dateUtils — PyroSync DSM PWA Date Hardening', () => {
  describe('1. Date-Only Strings (No UTC Midnight Day-Shifting)', () => {
    it('preserves exact date-only string YYYY-MM-DD without timezone shift', () => {
      expect(parseAssignmentDateToYMD('2026-11-05')).toBe('2026-11-05');
      expect(parseAssignmentDateToYMD('2026-11-07')).toBe('2026-11-07');
      expect(parseAssignmentDateToYMD('2026-01-01')).toBe('2026-01-01');
      expect(parseAssignmentDateToYMD('2026-12-31')).toBe('2026-12-31');
    });

    it('formats date-only strings consistently into localized display format', () => {
      expect(formatYMDForDisplay('2026-11-05')).toBe('5 November 2026');
      expect(formatYMDForDisplay('2026-11-07')).toBe('7 November 2026');
      expect(formatYMDForDisplay('2026-01-01')).toBe('1 January 2026');
    });
  });

  describe('2. Desktop Local Datetime Strings (Station Time / IST)', () => {
    it('extracts date from local station datetime strings without timezone offset', () => {
      // Desktop SQLite serializes DateTime without timezone (already IST)
      expect(parseAssignmentDateToYMD('2026-11-05T18:32:48.816824')).toBe('2026-11-05');
      expect(parseAssignmentDateToYMD('2026-11-07T20:15:20.070439')).toBe('2026-11-07');
      expect(parseAssignmentDateToYMD('2026-11-07 06:50:50')).toBe('2026-11-07');
    });

    it('formats local station datetimes for display without shifting days', () => {
      expect(formatYMDForDisplay('2026-11-05T18:32:48.816824')).toBe('5 November 2026');
      expect(formatYMDForDisplay('2026-11-07T20:15:20.070439')).toBe('7 November 2026');
    });

    it('parses timestamps for local and UTC datetimes accurately in IST', () => {
      const tsLocal = parseAssignmentTimestamp('2026-11-07T20:15:20.070439');
      const tsTz = parseAssignmentTimestamp('2026-11-07T20:15:20.070439+05:30');
      expect(tsLocal).not.toBeNull();
      expect(tsLocal).toBe(tsTz);

      const tsDateOnly = parseAssignmentTimestamp('2026-11-07');
      expect(tsDateOnly).toBe(new Date('2026-11-07T00:00:00+05:30').getTime());

      expect(parseAssignmentTimestamp('invalid-date')).toBeNull();
      expect(parseAssignmentTimestamp('')).toBeNull();
    });
  });

  describe('3. UTC Timestamps & IST Timezone Handling', () => {
    it('accurately resolves UTC timestamps to Asia/Kolkata calendar dates', () => {
      // 18:30 UTC on Nov 6 is 00:00 IST on Nov 7
      expect(parseAssignmentDateToYMD('2026-11-06T18:30:00.000Z')).toBe('2026-11-07');
      // 18:29 UTC on Nov 6 is 23:59 IST on Nov 6
      expect(parseAssignmentDateToYMD('2026-11-06T18:29:59.000Z')).toBe('2026-11-06');
      // UTC timestamp with +00:00 offset
      expect(parseAssignmentDateToYMD('2026-11-05T13:02:51.13037+00:00')).toBe('2026-11-05');
    });

    it('handles midnight and year-end boundaries in IST', () => {
      // Year transition: 31 Dec 2026 18:30 UTC -> 1 Jan 2027 IST
      expect(parseAssignmentDateToYMD('2026-12-31T18:30:00.000Z')).toBe('2027-01-01');
      // Month transition: 31 Oct 2026 18:30 UTC -> 1 Nov 2026 IST
      expect(parseAssignmentDateToYMD('2026-10-31T18:30:00.000Z')).toBe('2026-11-01');
    });
  });

  describe('4. Malformed, Missing, and Invalid Dates', () => {
    it('returns null for invalid strings rather than silently substituting today', () => {
      expect(parseAssignmentDateToYMD('')).toBeNull();
      expect(parseAssignmentDateToYMD('   ')).toBeNull();
      expect(parseAssignmentDateToYMD(null)).toBeNull();
      expect(parseAssignmentDateToYMD(undefined)).toBeNull();
      expect(parseAssignmentDateToYMD('not-a-date')).toBeNull();
      expect(parseAssignmentDateToYMD('2026-13-45')).toBeNull();
    });

    it('returns empty string for display formatting when input is invalid or null', () => {
      expect(formatYMDForDisplay('')).toBe('');
      expect(formatYMDForDisplay(null)).toBe('');
      expect(formatYMDForDisplay(undefined)).toBe('');
      expect(formatYMDForDisplay('garbage')).toBe('');
    });

    it('returns today YMD string in IST', () => {
      const today = getTodayYMD();
      expect(today).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    });
  });

  describe('5. selectLatestActiveAssignment — Recurrence Prevention & Scoping', () => {
    it('excludes inactive records strictly', () => {
      const assignments: CandidateAssignment[] = [
        {
          SyncGuid: 'inactive-1',
          AssignedDate: '2026-11-07T20:00:00',
          IsActive: false,
          PumpId: 1,
        },
        {
          SyncGuid: 'inactive-2',
          AssignedDate: '2026-11-06T18:00:00',
          IsActive: false,
          PumpId: 2,
        },
      ];
      expect(selectLatestActiveAssignment(assignments)).toBeNull();
    });

    it('selects latest active assignment when multiple active assignments exist', () => {
      const assignments: CandidateAssignment[] = [
        {
          SyncGuid: 'stale-nov-5',
          AssignedDate: '2026-11-05T18:32:48.816824',
          IsActive: true,
          PumpId: 1,
          local_id: 173,
        },
        {
          SyncGuid: 'fresh-nov-7',
          AssignedDate: '2026-11-07T20:15:20.070439',
          IsActive: true,
          PumpId: 1,
          local_id: 225,
        },
      ];
      const selected = selectLatestActiveAssignment(assignments);
      expect(selected).not.toBeNull();
      expect(selected?.SyncGuid).toBe('fresh-nov-7');
      expect(selected?.AssignedDate).toBe('2026-11-07T20:15:20.070439');
    });

    it('handles Rani and Nirjala recovery scenario: stale records deactivated', () => {
      // In Rani's scenario:
      // The Nov 5 duplicate is now IsActive = false.
      // Her Nov 6 assignment is also IsActive = false.
      // No active assignment exists until manager assigns her today.
      const raniAssignments: CandidateAssignment[] = [
        {
          SyncGuid: '4abf2936-fdd8-4180-82c1-42578c6e4319',
          AssignedDate: '2026-11-05T18:32:48.816824',
          IsActive: false, // Deactivated in Phase 1
          PumpId: 1,
          local_id: 173,
        },
        {
          SyncGuid: 'e1fc10f4-365f-4775-bbff-467b1e1e877e',
          AssignedDate: '2026-11-05T18:32:48.816824',
          IsActive: false,
          PumpId: 1,
          local_id: 173,
        },
        {
          SyncGuid: '8c1d4571-1dec-4cf9-9698-2d89422980b9',
          AssignedDate: '2026-11-06T19:49:11.943452',
          IsActive: false,
          PumpId: 1,
          local_id: 212,
        },
      ];
      // Before manager re-assigns: correctly yields null (no active assignment, prevents displaying Nov 5!)
      expect(selectLatestActiveAssignment(raniAssignments)).toBeNull();

      // When manager re-assigns on desktop for today:
      const withNewAssignment = [
        ...raniAssignments,
        {
          SyncGuid: 'new-active-assignment-nov-7',
          AssignedDate: '2026-11-07T21:00:00',
          IsActive: true,
          PumpId: 1,
          local_id: 230,
        },
      ];
      const selected = selectLatestActiveAssignment(withNewAssignment);
      expect(selected).not.toBeNull();
      expect(selected?.SyncGuid).toBe('new-active-assignment-nov-7');
      expect(selected?.AssignedDate).toBe('2026-11-07T21:00:00');
    });

    it('never allows malformed dates to override valid active assignments', () => {
      const assignments: CandidateAssignment[] = [
        {
          SyncGuid: 'bad-date',
          AssignedDate: 'invalid-string',
          IsActive: true,
          PumpId: 2,
        },
        {
          SyncGuid: 'valid-date',
          AssignedDate: '2026-11-07T10:00:00',
          IsActive: true,
          PumpId: 1,
        },
      ];
      const selected = selectLatestActiveAssignment(assignments);
      expect(selected?.SyncGuid).toBe('valid-date');
    });

    it('enforces station isolation when expectedStationId is provided', () => {
      const assignments: CandidateAssignment[] = [
        {
          SyncGuid: 'other-station-guid',
          station_id: 'KANDHARE-PETROLEUM',
          AssignedDate: '2026-11-07T22:00:00',
          IsActive: true,
          PumpId: 1,
        },
        {
          SyncGuid: 'target-station-guid',
          station_id: 'RASHTRA-74A837',
          AssignedDate: '2026-11-07T20:00:00',
          IsActive: true,
          PumpId: 1,
        },
      ];
      const selected = selectLatestActiveAssignment(assignments, 'RASHTRA-74A837');
      expect(selected?.SyncGuid).toBe('target-station-guid');
      expect(selected?.station_id).toBe('RASHTRA-74A837');
    });
  });
});
