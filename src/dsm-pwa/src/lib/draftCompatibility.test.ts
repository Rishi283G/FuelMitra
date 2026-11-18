import { describe, it, expect } from 'vitest';
import { isUnambiguousStationDraft } from './draftCompatibility';

describe('draftCompatibility - isUnambiguousStationDraft', () => {
  const ACTIVE_STATION = 'MITALI_STATION_01';
  const STATION_NAME = 'Mitali Service Station';
  const ASSIGNED_PUMPS = [1, 2];
  const CONFIGURED_NOZZLES = [101, 102, 103, 104];

  it('accepts drafts with direct matching stationId', () => {
    const draft = {
      stationId: 'mitali_station_01',
      pumpId: 1,
      nozzleReadings: [{ nozzleId: 101, closingReading: 5000 }],
    };
    expect(isUnambiguousStationDraft(draft, ACTIVE_STATION, STATION_NAME, ASSIGNED_PUMPS, CONFIGURED_NOZZLES)).toBe(true);
  });

  it('rejects drafts with mismatched stationId even if stationName matches', () => {
    const draft = {
      stationId: 'OTHER_STATION',
      stationName: STATION_NAME,
      pumpId: 1,
      nozzleReadings: [{ nozzleId: 101, closingReading: 5000 }],
    };
    expect(isUnambiguousStationDraft(draft, ACTIVE_STATION, STATION_NAME, ASSIGNED_PUMPS, CONFIGURED_NOZZLES)).toBe(false);
  });

  it('accepts legacy drafts without stationId when multi-factor proof matches completely', () => {
    const legacyDraft = {
      // stationId undefined
      stationName: 'Mitali Service Station',
      pumpId: 1,
      nozzleReadings: [
        { nozzleId: 101, closingReading: 5000 },
        { nozzleId: 102, closingReading: 6000 },
      ],
    };
    expect(isUnambiguousStationDraft(legacyDraft, ACTIVE_STATION, STATION_NAME, ASSIGNED_PUMPS, CONFIGURED_NOZZLES)).toBe(true);
  });

  it('quarantines legacy drafts if stationName does not match', () => {
    const legacyDraft = {
      stationName: 'Kandhare Petroleum',
      pumpId: 1,
      nozzleReadings: [{ nozzleId: 101, closingReading: 5000 }],
    };
    expect(isUnambiguousStationDraft(legacyDraft, ACTIVE_STATION, STATION_NAME, ASSIGNED_PUMPS, CONFIGURED_NOZZLES)).toBe(false);
  });

  it('quarantines legacy drafts if pumpId does not match assigned pumps', () => {
    const legacyDraft = {
      stationName: STATION_NAME,
      pumpId: 99, // Unknown / unassigned pump
      nozzleReadings: [{ nozzleId: 101, closingReading: 5000 }],
    };
    expect(isUnambiguousStationDraft(legacyDraft, ACTIVE_STATION, STATION_NAME, ASSIGNED_PUMPS, CONFIGURED_NOZZLES)).toBe(false);
  });

  it('quarantines legacy drafts if nozzle IDs do not belong to the configured station', () => {
    const legacyDraft = {
      stationName: STATION_NAME,
      pumpId: 1,
      nozzleReadings: [{ nozzleId: 999, closingReading: 5000 }], // Nozzle 999 does not belong to Mitali
    };
    expect(isUnambiguousStationDraft(legacyDraft, ACTIVE_STATION, STATION_NAME, ASSIGNED_PUMPS, CONFIGURED_NOZZLES)).toBe(false);
  });

  it('quarantines legacy drafts with partially overlapping or foreign nozzles', () => {
    const legacyDraft = {
      stationName: STATION_NAME,
      pumpId: 1,
      nozzleReadings: [
        { nozzleId: 101, closingReading: 5000 },
        { nozzleId: 999, closingReading: 6000 }, // 999 is foreign
      ],
    };
    expect(isUnambiguousStationDraft(legacyDraft, ACTIVE_STATION, STATION_NAME, ASSIGNED_PUMPS, CONFIGURED_NOZZLES)).toBe(false);
  });

  it('strictly isolates two stations with identical names using trusted nozzle configuration', () => {
    const sharedName = 'City Fuel Station';
    const stationA_Id = 'CITY_STATION_A';
    const stationA_Pumps = [1, 2];
    const stationA_Nozzles = [101, 102];

    const stationB_Id = 'CITY_STATION_B';
    const stationB_Pumps = [1, 2];
    const stationB_Nozzles = [201, 202];

    const legacyDraftFromStationB = {
      // stationId omitted
      stationName: sharedName,
      pumpId: 1,
      nozzleReadings: [
        { nozzleId: 201, closingReading: 4500 },
        { nozzleId: 202, closingReading: 5500 },
      ],
    };

    // When Station A inspects this draft, it MUST be quarantined because nozzles 201 and 202 do not belong to Station A
    expect(
      isUnambiguousStationDraft(legacyDraftFromStationB, stationA_Id, sharedName, stationA_Pumps, stationA_Nozzles)
    ).toBe(false);

    // When Station B inspects its own draft, it is accepted
    expect(
      isUnambiguousStationDraft(legacyDraftFromStationB, stationB_Id, sharedName, stationB_Pumps, stationB_Nozzles)
    ).toBe(true);
  });

  it('quarantines legacy drafts if trusted configuration is missing or empty', () => {
    const legacyDraft = {
      stationName: STATION_NAME,
      pumpId: 1,
      nozzleReadings: [{ nozzleId: 101, closingReading: 5000 }],
    };
    // Missing pump config
    expect(isUnambiguousStationDraft(legacyDraft, ACTIVE_STATION, STATION_NAME, [], CONFIGURED_NOZZLES)).toBe(false);
    // Missing nozzle config
    expect(isUnambiguousStationDraft(legacyDraft, ACTIVE_STATION, STATION_NAME, ASSIGNED_PUMPS, [])).toBe(false);
    // Missing station name config
    expect(isUnambiguousStationDraft(legacyDraft, ACTIVE_STATION, '', ASSIGNED_PUMPS, CONFIGURED_NOZZLES)).toBe(false);
  });

  it('quarantines legacy drafts with empty or invalid nozzle readings', () => {
    const legacyDraft = {
      stationName: STATION_NAME,
      pumpId: 1,
      nozzleReadings: [],
    };
    expect(isUnambiguousStationDraft(legacyDraft, ACTIVE_STATION, STATION_NAME, ASSIGNED_PUMPS, CONFIGURED_NOZZLES)).toBe(false);
  });

  it('rejects null, undefined, or empty drafts safely without throwing', () => {
    expect(isUnambiguousStationDraft(null, ACTIVE_STATION, STATION_NAME, ASSIGNED_PUMPS, CONFIGURED_NOZZLES)).toBe(false);
    expect(isUnambiguousStationDraft(undefined, ACTIVE_STATION, STATION_NAME, ASSIGNED_PUMPS, CONFIGURED_NOZZLES)).toBe(false);
    expect(isUnambiguousStationDraft({}, ACTIVE_STATION, STATION_NAME, ASSIGNED_PUMPS, CONFIGURED_NOZZLES)).toBe(false);
  });
});
