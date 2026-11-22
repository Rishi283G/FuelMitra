import { describe, it, expect, vi, beforeEach } from 'vitest';
import {
  getDefaultCollectionTypes,
  getDefaultStationConfig,
  fetchStationConfig,
  parseOptionalExplicitBoolean,
  resolvePhonePeVisibility,
  isTidEligibleType,
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
    expect(defaults).toHaveLength(9);
    expect(defaults.map((d) => d.code)).toEqual([
      'PHONEPE', 'CREDIT_CARD', 'PETROCARD', 'SBI_REDEEM', 'QR', 'PINELABS_CARD', 'PAYTM', 'MOBIKWIK', 'OTHERS'
    ]);
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
    expect(legacyConfig.collectionTypes).toHaveLength(9);
  });

  describe('parseOptionalExplicitBoolean', () => {
    it('accurately parses false variants without treating them as missing', () => {
      expect(parseOptionalExplicitBoolean(false)).toBe(false);
      expect(parseOptionalExplicitBoolean(0)).toBe(false);
      expect(parseOptionalExplicitBoolean('false')).toBe(false);
      expect(parseOptionalExplicitBoolean('0')).toBe(false);
      expect(parseOptionalExplicitBoolean('no')).toBe(false);
      expect(parseOptionalExplicitBoolean('disabled')).toBe(false);
      expect(parseOptionalExplicitBoolean('  DISABLED  ')).toBe(false);
    });

    it('accurately parses true variants', () => {
      expect(parseOptionalExplicitBoolean(true)).toBe(true);
      expect(parseOptionalExplicitBoolean(1)).toBe(true);
      expect(parseOptionalExplicitBoolean('true')).toBe(true);
      expect(parseOptionalExplicitBoolean('1')).toBe(true);
      expect(parseOptionalExplicitBoolean('yes')).toBe(true);
      expect(parseOptionalExplicitBoolean('enabled')).toBe(true);
      expect(parseOptionalExplicitBoolean('  ENABLED  ')).toBe(true);
    });

    it('accurately returns undefined for null, undefined, or empty values', () => {
      expect(parseOptionalExplicitBoolean(undefined)).toBeUndefined();
      expect(parseOptionalExplicitBoolean(null)).toBeUndefined();
      expect(parseOptionalExplicitBoolean('')).toBeUndefined();
      expect(parseOptionalExplicitBoolean('   ')).toBeUndefined();
      expect(parseOptionalExplicitBoolean('random_string')).toBeUndefined();
    });
  });

  describe('PhonePe TID & Batch configuration normalization and matrix', () => {
    it('defaults PhonePe hasTidBatch, hasTid and hasBatch to false, while preserving Pine Labs / Credit Card as true', () => {
      const defaults = getDefaultCollectionTypes();
      const phonePe = defaults.find((c) => c.code === 'PHONEPE')!;
      expect(phonePe).toBeDefined();
      expect(phonePe.hasTidBatch).toBe(false);
      expect(phonePe.hasTid).toBe(false);
      expect(phonePe.hasBatch).toBe(false);

      const creditCard = defaults.find((c) => c.code === 'CREDIT_CARD')!;
      expect(creditCard).toBeDefined();
      expect(creditCard.hasTidBatch).toBe(true);

      const pineLabs = defaults.find((c) => c.code === 'PINELABS_CARD')!;
      expect(pineLabs).toBeDefined();
      expect(pineLabs.hasTidBatch).toBe(true);
    });

    it('Case 1: Both TID and Batch disabled for PhonePe', async () => {
      const mockTypes = [
        { Code: 'PHONEPE', DisplayName: 'PhonePe', HasTid: false, HasBatch: false, HasTidBatch: false },
      ];
      (supabase.from as any).mockReturnValue({
        select: vi.fn().mockReturnThis(),
        eq: vi.fn().mockReturnThis(),
        limit: vi.fn().mockReturnThis(),
        maybeSingle: vi.fn().mockResolvedValue({
          data: { CollectionTypesJson: JSON.stringify(mockTypes) },
          error: null,
        }),
      });

      const config = await fetchStationConfig('TEST_STATION_PP1');
      const pp = config.collectionTypes.find((c) => c.code === 'PHONEPE')!;
      expect(pp.hasTid).toBe(false);
      expect(pp.hasBatch).toBe(false);
      expect(pp.hasTidBatch).toBe(false);
    });

    it('Case 2: TID enabled, Batch disabled for PhonePe', async () => {
      const mockTypes = [
        { Code: 'PHONEPE', DisplayName: 'PhonePe', HasTid: true, HasBatch: false, HasTidBatch: true },
      ];
      (supabase.from as any).mockReturnValue({
        select: vi.fn().mockReturnThis(),
        eq: vi.fn().mockReturnThis(),
        limit: vi.fn().mockReturnThis(),
        maybeSingle: vi.fn().mockResolvedValue({
          data: { CollectionTypesJson: JSON.stringify(mockTypes) },
          error: null,
        }),
      });

      const config = await fetchStationConfig('TEST_STATION_PP2');
      const pp = config.collectionTypes.find((c) => c.code === 'PHONEPE')!;
      expect(pp.hasTid).toBe(true);
      expect(pp.hasBatch).toBe(false);
      expect(isTidEligibleType(pp)).toBe(true);
    });

    it('Case 3: TID disabled, Batch enabled for PhonePe', async () => {
      const mockTypes = [
        { Code: 'PHONEPE', DisplayName: 'PhonePe', HasTid: false, HasBatch: true, HasTidBatch: true },
      ];
      (supabase.from as any).mockReturnValue({
        select: vi.fn().mockReturnThis(),
        eq: vi.fn().mockReturnThis(),
        limit: vi.fn().mockReturnThis(),
        maybeSingle: vi.fn().mockResolvedValue({
          data: { CollectionTypesJson: JSON.stringify(mockTypes) },
          error: null,
        }),
      });

      const config = await fetchStationConfig('TEST_STATION_PP3');
      const pp = config.collectionTypes.find((c) => c.code === 'PHONEPE')!;
      expect(pp.hasTid).toBe(false);
      expect(pp.hasBatch).toBe(true);
      expect(isTidEligibleType(pp)).toBe(false);
    });

    it('Case 4: Both TID and Batch enabled for PhonePe', async () => {
      const mockTypes = [
        { Code: 'PHONEPE', DisplayName: 'PhonePe', HasTid: true, HasBatch: true, HasTidBatch: true },
      ];
      (supabase.from as any).mockReturnValue({
        select: vi.fn().mockReturnThis(),
        eq: vi.fn().mockReturnThis(),
        limit: vi.fn().mockReturnThis(),
        maybeSingle: vi.fn().mockResolvedValue({
          data: { CollectionTypesJson: JSON.stringify(mockTypes) },
          error: null,
        }),
      });

      const config = await fetchStationConfig('TEST_STATION_PP4');
      const pp = config.collectionTypes.find((c) => c.code === 'PHONEPE')!;
      expect(pp.hasTid).toBe(true);
      expect(pp.hasBatch).toBe(true);
      expect(pp.hasTidBatch).toBe(true);
      expect(isTidEligibleType(pp)).toBe(true);
    });

    it('Precedence: normalizes feature toggles from AppFeatureSettingsJson with alias support', async () => {
      const mockFeatures = [
        { FeatureKey: 'Operations_PhonePeTid', IsEnabled: false },
        { FeatureKey: 'Operations_PhonePeBatch', IsEnabled: true },
      ];
      (supabase.from as any).mockReturnValue({
        select: vi.fn().mockReturnThis(),
        eq: vi.fn().mockReturnThis(),
        limit: vi.fn().mockReturnThis(),
        maybeSingle: vi.fn().mockResolvedValue({
          data: {
            CollectionTypesJson: JSON.stringify([{ Code: 'PHONEPE', DisplayName: 'PhonePe', HasTidBatch: true }]),
            AppFeatureSettingsJson: JSON.stringify(mockFeatures),
          },
          error: null,
        }),
      });

      const config = await fetchStationConfig('TEST_STATION_PP_FEATS');
      expect(config.features.phonePeTidEnabled).toBe(false);
      expect(config.features.phonePeBatchEnabled).toBe(true);
    });

    it('Missing PhonePe configuration in CollectionTypesJson defaults hasTidBatch to false', async () => {
      const mockTypes = [
        { Code: 'CREDIT_CARD', DisplayName: 'Credit Card', HasTidBatch: true },
        { Code: 'PHONEPE', DisplayName: 'PhonePe' }, // HasTidBatch omitted
      ];
      (supabase.from as any).mockReturnValue({
        select: vi.fn().mockReturnThis(),
        eq: vi.fn().mockReturnThis(),
        limit: vi.fn().mockReturnThis(),
        maybeSingle: vi.fn().mockResolvedValue({
          data: { CollectionTypesJson: JSON.stringify(mockTypes) },
          error: null,
        }),
      });

      const config = await fetchStationConfig('TEST_STATION_PP_MISSING');
      const pp = config.collectionTypes.find((c) => c.code === 'PHONEPE')!;
      expect(pp.hasTidBatch).toBe(false);
      expect(pp.hasTid).toBeUndefined();
      expect(pp.hasBatch).toBeUndefined();

      const cc = config.collectionTypes.find((c) => c.code === 'CREDIT_CARD')!;
      expect(cc.hasTidBatch).toBe(true);
    });

    it('resolvePhonePeVisibility: defaults to both hidden when config is missing or unconfigured', () => {
      const res = resolvePhonePeVisibility(null, null);
      expect(res.isPhonePeTidVisible).toBe(false);
      expect(res.isPhonePeBatchVisible).toBe(false);
      expect(res.phonePeGridColumns).toBe('1fr');
    });

    it('resolvePhonePeVisibility: explicit feature-toggle false overrides collection-type true', () => {
      const res = resolvePhonePeVisibility(
        { phonePeTidEnabled: false, phonePeBatchEnabled: false },
        { hasTid: true, hasBatch: true, hasTidBatch: true }
      );
      expect(res.isPhonePeTidVisible).toBe(false);
      expect(res.isPhonePeBatchVisible).toBe(false);
      expect(res.phonePeGridColumns).toBe('1fr');
    });

    it('resolvePhonePeVisibility: explicit feature-toggle true overrides collection-type false', () => {
      const res = resolvePhonePeVisibility(
        { phonePeTidEnabled: true, phonePeBatchEnabled: false },
        { hasTid: false, hasBatch: true, hasTidBatch: false }
      );
      expect(res.isPhonePeTidVisible).toBe(true);
      expect(res.isPhonePeBatchVisible).toBe(false);
      expect(res.phonePeGridColumns).toBe('1.2fr 1fr');
    });

    it('resolvePhonePeVisibility: explicit collection-type hasTid: false overrides hasTidBatch: true', () => {
      const res = resolvePhonePeVisibility(
        {},
        { hasTid: false, hasBatch: true, hasTidBatch: true }
      );
      expect(res.isPhonePeTidVisible).toBe(false);
      expect(res.isPhonePeBatchVisible).toBe(true);
      expect(res.phonePeGridColumns).toBe('1.2fr 1fr');
    });

    it('resolvePhonePeVisibility: accurately produces the 4-quadrant layout grid', () => {
      // 1. Both disabled -> 1fr
      expect(resolvePhonePeVisibility({}, { hasTidBatch: false }).phonePeGridColumns).toBe('1fr');
      // 2. Only TID enabled -> 1.2fr 1fr
      expect(resolvePhonePeVisibility({}, { hasTid: true, hasBatch: false, hasTidBatch: false }).phonePeGridColumns).toBe('1.2fr 1fr');
      // 3. Only Batch enabled -> 1.2fr 1fr
      expect(resolvePhonePeVisibility({}, { hasTid: false, hasBatch: true, hasTidBatch: false }).phonePeGridColumns).toBe('1.2fr 1fr');
      // 4. Both enabled -> 1.2fr 1fr 1fr
      expect(resolvePhonePeVisibility({}, { hasTid: true, hasBatch: true, hasTidBatch: true }).phonePeGridColumns).toBe('1.2fr 1fr 1fr');
    });
  });
});
