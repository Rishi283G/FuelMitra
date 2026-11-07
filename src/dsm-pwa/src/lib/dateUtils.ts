/**
 * Date utilities for PyroSync DSM PWA.
 * Ensures consistent handling of operational dates in the Asia/Kolkata (IST, UTC+05:30) timezone.
 */

export const OPERATIONAL_TIMEZONE = 'Asia/Kolkata';

/**
 * Checks if a string contains an explicit timezone designator ('Z' or [+-]HH:MM).
 */
function hasTimezoneDesignator(str: string): boolean {
  return /Z|[+-]\d{2}(?::?\d{2})?$/i.test(str.trim());
}

/**
 * Parses an assignment date string into a YYYY-MM-DD operational date string.
 *
 * Rules:
 * 1. Date-only strings (e.g., '2026-11-05') are treated as calendar dates directly
 *    without timezone conversions to prevent UTC day-shifting.
 * 2. Datetime strings without timezone offsets (e.g., '2026-11-05T18:32:48.816824'
 *    or '2026-11-05 18:32:48') represent local station operational time (IST)
 *    recorded by the desktop software. The YYYY-MM-DD date component is extracted directly.
 * 3. Datetime strings with timezone offsets (e.g. UTC 'Z' or '+00:00' or '+05:30')
 *    are converted explicitly to the Asia/Kolkata operational calendar date.
 * 4. Invalid, empty, or malformed inputs return null.
 */
export function parseAssignmentDateToYMD(rawDate: string | null | undefined): string | null {
  if (!rawDate || typeof rawDate !== 'string') return null;
  const trimmed = rawDate.trim();
  if (!trimmed) return null;

  // Case 1: Pure Date-only YYYY-MM-DD
  const dateOnlyMatch = /^(\d{4})-(\d{2})-(\d{2})$/.exec(trimmed);
  if (dateOnlyMatch) {
    const [, y, m, d] = dateOnlyMatch;
    const year = parseInt(y, 10);
    const month = parseInt(m, 10);
    const day = parseInt(d, 10);
    if (year < 1970 || year > 2200 || month < 1 || month > 12 || day < 1 || day > 31) return null;
    return `${y}-${m}-${d}`;
  }

  // Case 2: Datetime without timezone offset (local station time from desktop)
  const localMatch = /^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2})(?::(\d{2})(?:\.\d+)?)?$/.exec(trimmed);
  if (localMatch) {
    const [, y, m, d] = localMatch;
    const year = parseInt(y, 10);
    const month = parseInt(m, 10);
    const day = parseInt(d, 10);
    if (year < 1970 || year > 2200 || month < 1 || month > 12 || day < 1 || day > 31) return null;
    return `${y}-${m}-${d}`;
  }

  // Case 3: Datetime with timezone offset (e.g. UTC 'Z' or explicit offset)
  if (hasTimezoneDesignator(trimmed)) {
    const d = new Date(trimmed);
    if (isNaN(d.getTime())) return null;
    const formatter = new Intl.DateTimeFormat('en-CA', {
      timeZone: OPERATIONAL_TIMEZONE,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    });
    return formatter.format(d);
  }

  // Fallback for other standard Date-parseable strings
  const fallback = new Date(trimmed);
  if (isNaN(fallback.getTime())) return null;
  const formatter = new Intl.DateTimeFormat('en-CA', {
    timeZone: OPERATIONAL_TIMEZONE,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  });
  return formatter.format(fallback);
}

/**
 * Returns today's operational calendar date in Asia/Kolkata as YYYY-MM-DD.
 */
export function getTodayYMD(): string {
  const formatter = new Intl.DateTimeFormat('en-CA', {
    timeZone: OPERATIONAL_TIMEZONE,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  });
  return formatter.format(new Date());
}

/**
 * Formats a YYYY-MM-DD string into localized human-readable date.
 * Avoids UTC boundary shifts by constructing date at noon (12:00:00).
 */
export function formatYMDForDisplay(
  ymd: string | null | undefined,
  options: Intl.DateTimeFormatOptions = { day: 'numeric', month: 'long', year: 'numeric' },
  locale: string = 'en-IN'
): string {
  if (!ymd || typeof ymd !== 'string') return '';
  const parsedYmd = parseAssignmentDateToYMD(ymd);
  if (!parsedYmd) return '';

  const [y, m, d] = parsedYmd.split('-').map(Number);
  // Using noon avoids any DST or timezone rollover boundary anomalies
  const dateObj = new Date(y, m - 1, d, 12, 0, 0);
  return dateObj.toLocaleDateString(locale, options);
}

/**
 * Parses raw assignment date into an epoch timestamp (ms).
 * Datetimes without timezone are assumed to be IST (+05:30).
 * Invalid dates return null.
 */
export function parseAssignmentTimestamp(rawDate: string | null | undefined): number | null {
  if (!rawDate || typeof rawDate !== 'string') return null;
  const trimmed = rawDate.trim();
  if (!trimmed) return null;

  // If date-only: treat as start of operational day in IST (00:00:00+05:30)
  if (/^\d{4}-\d{2}-\d{2}$/.test(trimmed)) {
    const d = new Date(`${trimmed}T00:00:00+05:30`);
    return isNaN(d.getTime()) ? null : d.getTime();
  }

  // If datetime without timezone: append +05:30 (IST)
  if (/^(\d{4}-\d{2}-\d{2})[T ](\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?)$/.test(trimmed)) {
    const normalized = trimmed.replace(' ', 'T');
    const d = new Date(`${normalized}+05:30`);
    return isNaN(d.getTime()) ? null : d.getTime();
  }

  // Datetime with timezone offset
  const d = new Date(trimmed);
  return isNaN(d.getTime()) ? null : d.getTime();
}

export interface CandidateAssignment {
  PumpId?: number | null;
  ConnectedPumpId?: number | null;
  ConnectedPumpIdsJson?: string | null;
  ShiftType?: string | null;
  AssignedDate?: string | null;
  IsActive?: boolean | null;
  SyncGuid?: string;
  local_id?: number | null;
  station_id?: string | null;
  [key: string]: any;
}

/**
 * Selects the single latest valid active assignment for a DSM deterministically.
 * 
 * Safeguards:
 * 1. Strict active filter: IsActive === true. Inactive/stale records are completely excluded.
 * 2. Scoped to stationId if provided.
 * 3. Deterministic ordering:
 *    - Valid AssignedDate timestamps sort descending (latest date first).
 *    - Records with valid dates ALWAYS take precedence over records with missing/malformed dates.
 *    - Tie-breakers: local_id descending, PumpId descending, SyncGuid descending.
 * 4. Returns null if no valid active assignment exists.
 */
export function selectLatestActiveAssignment<T extends CandidateAssignment>(
  assignments: T[] | null | undefined,
  expectedStationId?: string | null
): T | null {
  if (!assignments || !Array.isArray(assignments) || assignments.length === 0) {
    return null;
  }

  const activeCandidates = assignments.filter((a) => {
    if (!a || a.IsActive !== true) return false;
    if (expectedStationId && a.station_id && a.station_id !== expectedStationId) {
      return false;
    }
    return true;
  });

  if (activeCandidates.length === 0) {
    return null;
  }

  activeCandidates.sort((a, b) => {
    const timeA = parseAssignmentTimestamp(a.AssignedDate);
    const timeB = parseAssignmentTimestamp(b.AssignedDate);

    // If both have timestamps, sort latest first
    if (timeA !== null && timeB !== null) {
      if (timeB !== timeA) return timeB - timeA;
    } else if (timeB !== null) {
      return 1; // B has valid date, A does not -> B wins
    } else if (timeA !== null) {
      return -1; // A has valid date, B does not -> A wins
    }

    // Tie breaker 1: local_id descending
    const idA = typeof a.local_id === 'number' ? a.local_id : 0;
    const idB = typeof b.local_id === 'number' ? b.local_id : 0;
    if (idB !== idA) return idB - idA;

    // Tie breaker 2: PumpId descending
    const pumpA = typeof a.PumpId === 'number' ? a.PumpId : 0;
    const pumpB = typeof b.PumpId === 'number' ? b.PumpId : 0;
    if (pumpB !== pumpA) return pumpB - pumpA;

    // Tie breaker 3: SyncGuid string comparison
    const guidA = a.SyncGuid || '';
    const guidB = b.SyncGuid || '';
    return guidB.localeCompare(guidA);
  });

  return activeCandidates[0];
}
