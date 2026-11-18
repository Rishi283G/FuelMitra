/**
 * draftCompatibility.ts
 * 
 * Safe, multi-factor proof-of-ownership verification for historical local drafts.
 * 
 * Guarantees:
 * 1. Direct stationId match when available (normalized, case-insensitive).
 * 2. Unambiguous legacy proof when stationId is omitted:
 *    - MUST match authoritative stationName
 *    - MUST match assigned pumpId
 *    - MUST contain nozzle readings belonging to configured station nozzleIds
 * 3. Quarantines any draft where ownership cannot be established unambiguously.
 * 4. Zero cross-station reading leakage.
 */

export function isUnambiguousStationDraft(
  draft: any,
  activeStationId: string,
  authoritativeStationName?: string,
  allowedPumpIds?: number[],
  stationNozzleIds?: number[]
): boolean {
  if (!draft) return false;

  const cleanActiveStation = (activeStationId || '').trim().toLowerCase();
  if (!cleanActiveStation) return false;

  // 1. Direct authoritative stationId match
  if (draft.stationId && typeof draft.stationId === 'string') {
    return draft.stationId.trim().toLowerCase() === cleanActiveStation;
  }

  // 2. Legacy draft without stationId: MUST satisfy multi-factor proof of ownership
  if (!draft.stationId) {
    // A. Must have valid stationName matching authoritative config
    const draftStationName = (draft.stationName || '').trim().toLowerCase();
    const configStationName = (authoritativeStationName || '').trim().toLowerCase();
    if (!draftStationName || !configStationName || draftStationName !== configStationName) {
      return false; // Quarantined: station name mismatch or missing
    }

    // B. Must match assigned pump from trusted configuration
    if (!Array.isArray(allowedPumpIds) || allowedPumpIds.length === 0 || !allowedPumpIds.includes(Number(draft.pumpId))) {
      return false; // Quarantined: pumpId not in trusted assigned pumps
    }

    // C. Must contain nozzle readings and ALL readings must belong to the trusted station's configured nozzle IDs
    if (!Array.isArray(stationNozzleIds) || stationNozzleIds.length === 0) {
      return false; // Quarantined: missing trusted nozzle configuration
    }
    if (!Array.isArray(draft.nozzleReadings) || draft.nozzleReadings.length === 0) {
      return false; // Quarantined: no nozzle readings
    }
    const allNozzlesBelongToStation = draft.nozzleReadings.every((nr: any) =>
      stationNozzleIds.includes(Number(nr.nozzleId))
    );
    if (!allNozzlesBelongToStation) {
      return false; // Quarantined: contains nozzle readings not belonging to this station
    }

    // Multi-factor verification passed
    return true;
  }

  return false;
}
