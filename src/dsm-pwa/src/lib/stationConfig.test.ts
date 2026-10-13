import { describe, it, expect, vi, beforeEach } from 'vitest';
import {
  getDefaultCollectionTypes,
  getDefaultStationConfig,
  fetchStationConfig,
} from './stationConfig';
import { supabase } from './supabase';

const storage: Record<string, string> = {};
(globalThis as any).localStorage = {
  getItem: vi.fn((k: string) => storage[k] || null),
  setItem: vi.fn((k: string, v: string) => { storage[k] = v; }),
  removeItem: vi.fn((k: string) => { delete storage[k]; }),
  clear: vi.fn(() => { Object.keys(storage).forEach((k) => delete storage[k]); }),
  key: vi.fn(() => null),
  length: 0,
};

vi.mock('./supabase', () => {
  return {
    supabase: {
      from: vi.fn(),
    },
  };
});

describe('stationConfig', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    (globalThis as any).localStorage.clear();
  });

  it('provides authoritative default collection types for legacy non-dynamic stations', () => {
    const defaults = getDefaultCollectionTypes();
    expect(defaults).toHaveLength(3);
    expect(defaults.map((d) => d.code)).toEqual(['PHONEPE', 'CREDIT_CARD', 'PETROCARD']);
    expect(defaults.every((d) => d.isActive)).toBe(true);
    expect(defaults.every((d) => d.enableMorning && d.enableDay && d.enableNight)).toBe(true);
  });

  it('isolates default config by station ID and marks isDynamic as false', () => {
    const configA = getDefaultStationConfig('STATION_A');
    const configB = getDefaultStationConfig('STATION_B');

    expect(configA.stationId).toBe('STATION_A');
    expect(configB.stationId).toBe('STATION_B');
    expect(configA.isDynamic).toBe(false);
    expect(configA.features.cashDenominationsEnabled).toBe(false);
    expect(configA.features.otpMachineTrackingEnabled).toBe(false);
  });

  it('fetches and parses dynamic collection types and station feature toggles from Supabase', async () => {
    const mockCustomTypes = [
      {
        Code: 'SBI_REDEEM',
        DisplayName: 'SBI Point Redeem',
        Category: 'Card',
        HasTidBatch: true,
        DisplayOrder: 1,
        IsActive: true,
        EnableMorning: true,
        EnableDay: false,
        EnableNight: true,
      },
      {
        Code: 'PAYTM_QR',
        DisplayName: 'Paytm Dynamic QR',
        Category: 'Online',
        HasTidBatch: false,
        DisplayOrder: 2,
        IsActive: true,
        EnableMorning: true,
        EnableDay: true,
        EnableNight: true,
      },
    ];

    const mockFeatures = [
      { FeatureKey: 'Operations_CashDenominations', IsEnabled: true },
      { FeatureKey: 'Operations_OtpMachineTracking', IsEnabled: true },
    ];

    (supabase.from as any).mockReturnValue({
      select: vi.fn().mockReturnThis(),
      eq: vi.fn().mockReturnThis(),
      limit: vi.fn().mockReturnThis(),
      maybeSingle: vi.fn().mockResolvedValue({
        data: {
          CollectionTypesJson: JSON.stringify(mockCustomTypes),
          AppFeatureSettingsJson: JSON.stringify(mockFeatures),
        },
        error: null,
      }),
    });

    const config = await fetchStationConfig('MITALI_PUMP_01');

    expect(config.stationId).toBe('MITALI_PUMP_01');
    expect(config.isDynamic).toBe(true);
    expect(config.features.cashDenominationsEnabled).toBe(true);
    expect(config.features.otpMachineTrackingEnabled).toBe(true);
    expect(config.collectionTypes).toHaveLength(2);
    expect(config.collectionTypes[0].code).toBe('SBI_REDEEM');
    expect(config.collectionTypes[0].enableMorning).toBe(true);
    expect(config.collectionTypes[0].enableDay).toBe(false);
    expect(config.collectionTypes[0].enableNight).toBe(true);
  });

  it('falls back safely for legacy stations without leaking settings between stations', async () => {
    (supabase.from as any).mockReturnValue({
      select: vi.fn().mockReturnThis(),
      eq: vi.fn().mockReturnThis(),
      limit: vi.fn().mockReturnThis(),
      maybeSingle: vi.fn().mockResolvedValue({
        data: null,
        error: null,
      }),
    });

    const legacyConfig = await fetchStationConfig('LEGACY_STATION_KANDHARE');
    expect(legacyConfig.stationId).toBe('LEGACY_STATION_KANDHARE');
    expect(legacyConfig.isDynamic).toBe(false);
    expect(legacyConfig.features.cashDenominationsEnabled).toBe(false);
    expect(legacyConfig.features.otpMachineTrackingEnabled).toBe(false);
    expect(legacyConfig.collectionTypes).toHaveLength(3);
  });
});
