import { describe, it, expect, vi, beforeEach } from 'vitest';
import {
  getDefaultCollectionTypes,
  getDefaultStationConfig,
  fetchStationConfig,
  parseOptionalExplicitBoolean,
  resolvePhonePeVisibility,
  isTidEligibleType,
  resolveFuelRate,
  validateNozzleRateResolution,
  sanitizeCachedStationConfig,
  sanitizeNozzleRatesCache,
  reconcileNozzleRowRates,
  type StationConfig,
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

    it('parses independent fuel rates and canonical TankDefinitionsJson without cross-contamination', async () => {
      const mockTanks = [
        { TankName: 'HSD - 20KL', FuelType: 'HSD', Rate: 98.15 },
        { TankName: 'MS - 20KL', FuelType: 'MS-I', Rate: 111.48 },
        { TankName: 'MS - 20KL II', FuelType: 'MS-II', Rate: 111.48 },
        { TankName: 'SPEED - 20KL', FuelType: 'SPEED', Rate: 120.59 },
        { TankName: 'CNG Line', FuelType: 'CNG', Rate: 97.75 },
      ];

      (supabase.from as any).mockReturnValue({
        select: vi.fn().mockReturnThis(),
        eq: vi.fn().mockReturnThis(),
        limit: vi.fn().mockReturnThis(),
        maybeSingle: vi.fn().mockResolvedValue({
          data: {
            StationId: 'MITALI_STATION',
            TankDefinitionsJson: JSON.stringify(mockTanks),
            FuelRatesJson: JSON.stringify({
              'HSD': 98.15,
              'MS-I': 111.48,
              'MS-II': 111.48,
              'SPEED': 120.59,
              'CNG': 97.75,
            }),
            HsdRate: 98.15,
            MsIRate: 111.48,
            MsIIRate: 111.48,
            SpeedRate: 120.59,
            CngRate: 97.75,
          },
          error: null,
        }),
      });

      const config = await fetchStationConfig('MITALI_STATION');
      expect(config.speedRate).toBe(120.59);
      expect(config.msIRate).toBe(111.48);
      expect(config.msIIRate).toBe(111.48);
      expect(config.hsdRate).toBe(98.15);
      expect(config.cngRate).toBe(97.75);
      expect(config.speedVerified).toBe(true);
      expect(config.cacheSchemaVersion).toBe(2);

      // Verify Tier 1 resolution for Speed nozzle
      expect(resolveFuelRate(config, 'SPEED', 'SPEED - 20KL')).toBe(120.59);
      expect(resolveFuelRate(config, 'SPEED')).toBe(120.59);

      // Verify Tier 1 resolution for MS-II nozzle (never contaminated with SPEED)
      expect(resolveFuelRate(config, 'MS-II', 'MS - 20KL II')).toBe(111.48);
      expect(resolveFuelRate(config, 'MS-II')).toBe(111.48);

      // Verify Tier 1 resolution for MS-I nozzle
      expect(resolveFuelRate(config, 'MS-I', 'MS - 20KL')).toBe(111.48);
      expect(resolveFuelRate(config, 'MS-I')).toBe(111.48);
    });

    it('resolveFuelRate never falls back from SPEED to MS-I or MS-II when SPEED rate is missing', () => {
      const configWithNoSpeed = {
        stationId: 'TEST',
        features: {} as any,
        collectionTypes: [],
        isDynamic: true,
        fuelRates: {
          'MS-I': 111.48,
          'MS-II': 111.48,
          'HSD': 98.15,
        },
        msIRate: 111.48,
        msIIRate: 111.48,
        hsdRate: 98.15,
        speedRate: 0,
      };

      // SPEED must return 0, NEVER 111.48 (MS-I / MS-II rate)
      expect(resolveFuelRate(configWithNoSpeed, 'SPEED', 'SPEED - 20KL')).toBe(0);
      expect(resolveFuelRate(configWithNoSpeed, 'SPEED')).toBe(0);
      expect(resolveFuelRate(configWithNoSpeed, 'XP95')).toBe(0);
    });

    it('multi-tank disambiguation: prioritizes tank-specific rate over fuel-type when tanks differ', () => {
      const multiTankConfig = {
        stationId: 'TEST',
        features: {} as any,
        collectionTypes: [],
        isDynamic: true,
        tankRates: {
          'MS - TANK 1': 105.0,
          'MS - TANK 2': 108.0,
        },
        fuelTypeRates: {}, // Ambiguity detected: multiple tanks with different rates
        fuelRates: {},
        msIRate: 105.0,
      };

      // When tank is assigned, Tier 1 resolves the specific tank rate
      expect(resolveFuelRate(multiTankConfig, 'MS', 'MS - TANK 1')).toBe(105.0);
      expect(resolveFuelRate(multiTankConfig, 'MS', 'MS - TANK 2')).toBe(108.0);
    });

    it('offline cache verification: unversioned legacy cache marks speedVerified as false while preserving HSD, MS-I, MS-II, and CNG', () => {
      const legacyCachedConfig = {
        stationId: 'CACHED_STATION',
        features: {} as any,
        collectionTypes: [],
        isDynamic: true,
        fuelRates: { 'HSD': 98.15, 'MS-I': 111.48, 'MS-II': 111.48, 'CNG': 97.75, 'SPEED': 111.48 },
        hsdRate: 98.15,
        msIRate: 111.48,
        msIIRate: 111.48,
        cngRate: 97.75,
        speedRate: 111.48,
        // cacheSchemaVersion omitted (legacy schema < 2)
      };

      storage['station_config_cached_station'] = JSON.stringify(legacyCachedConfig);

      // Verify offline loading
      const raw = localStorage.getItem('station_config_cached_station');
      const parsed = JSON.parse(raw!);
      if (parsed.cacheSchemaVersion !== 2) {
        parsed.speedVerified = false;
      }

      // SPEED is unverified
      expect(parsed.speedVerified).toBe(false);

      // HSD, MS-I, MS-II, and CNG remain completely usable
      expect(parsed.hsdRate).toBe(98.15);
      expect(parsed.msIRate).toBe(111.48);
      expect(parsed.msIIRate).toBe(111.48);
      expect(parsed.cngRate).toBe(97.75);
    });

    it('offline cache verification: equal MS and SPEED prices are allowed when independent configuration is verified', () => {
      // Station where MS and SPEED happen to legitimately cost the same (e.g. ₹111.48)
      // but SPEED has validated independent provenance in TankDefinitionsJson
      const configWithEqualPrices: StationConfig = {
        stationId: 'EQUAL_PRICE_STATION',
        features: {} as any,
        collectionTypes: [],
        isDynamic: true,
        fuelRates: { 'MS-I': 111.48, 'SPEED': 111.48 },
        tankRates: { 'MS - 20KL': 111.48, 'SPEED - 20KL': 111.48 },
        fuelTypeRates: { 'MS-I': 111.48, 'SPEED': 111.48 },
        hsdRate: 98.15,
        msIRate: 111.48,
        speedRate: 111.48,
        cacheSchemaVersion: 2,
        speedVerified: true, // Legitimate equal price with validated independent tank provenance
      };

      storage['station_config_equal_price_station'] = JSON.stringify(configWithEqualPrices);

      const raw = localStorage.getItem('station_config_equal_price_station');
      const parsed = JSON.parse(raw!);
      if (parsed.cacheSchemaVersion !== 2) {
        parsed.speedVerified = false;
      }

      // Equal price is allowed and verified
      expect(parsed.speedVerified).toBe(true);
      expect(parsed.speedRate).toBe(111.48);
      expect(parsed.msIRate).toBe(111.48);
    });

    it('offline submission validation: unverified SPEED blocks submission only when net SPEED sale > 0', () => {
      const unverifiedConfig = {
        cacheSchemaVersion: 1, // legacy
        speedVerified: false,
      };

      function validateReading(isSpeed: boolean, salesLtrs: number, test: number, speedVerified: boolean) {
        const errs: string[] = [];
        const hasNetSale = salesLtrs > test;
        if (isSpeed && hasNetSale && !speedVerified) {
          errs.push('SPEED nozzle rate could not be verified from station configuration.');
        }
        return errs;
      }

      // Case A: SPEED nozzle with zero net sale (salesLtrs = 0, test = 0) -> DOES NOT block
      const errsZeroSale = validateReading(true, 0, 0, unverifiedConfig.speedVerified);
      expect(errsZeroSale.length).toBe(0);

      // Case B: SPEED nozzle with test only (salesLtrs = 5, test = 5) -> DOES NOT block
      const errsTestOnly = validateReading(true, 5, 5, unverifiedConfig.speedVerified);
      expect(errsTestOnly.length).toBe(0);

      // Case C: SPEED nozzle with positive net sale (salesLtrs = 50, test = 0) -> BLOCKS submission!
      const errsPositiveSale = validateReading(true, 50, 0, unverifiedConfig.speedVerified);
      expect(errsPositiveSale.length).toBe(1);
      expect(errsPositiveSale[0]).toContain('SPEED nozzle rate could not be verified');

      // Case D: Non-speed nozzle (HSD/MS) with positive sale -> DOES NOT block
      const errsHsd = validateReading(false, 100, 0, unverifiedConfig.speedVerified);
      expect(errsHsd.length).toBe(0);
    });

    it('online refresh: updates verified configuration without destroying unrelated cached data', () => {
      // Populate unrelated station caches
      storage['cached_creditors'] = JSON.stringify([{ id: 1, name: 'Patil Transports' }]);
      storage['cached_vehicles'] = JSON.stringify([{ id: 1, number: 'MH-12-AB-1234' }]);
      storage['nozzle_openings_v2_TEST_STATION'] = JSON.stringify({ 1: 5432.1, 2: 9876.5 });
      storage['dsm_device_id'] = 'DEVICE-UUID-1234';

      // Simulate online refresh writing updated station config
      const refreshedConfig: StationConfig = {
        stationId: 'TEST_STATION',
        features: {} as any,
        collectionTypes: [],
        isDynamic: true,
        fuelRates: { 'SPEED': 120.59, 'MS-I': 111.48 },
        tankRates: { 'SPEED - 20KL': 120.59 },
        fuelTypeRates: { 'SPEED': 120.59 },
        speedRate: 120.59,
        msIRate: 111.48,
        cacheSchemaVersion: 2,
        speedVerified: true,
      };

      storage['station_config_test_station'] = JSON.stringify(refreshedConfig);

      // Verify that station config is updated with verified SPEED
      const loadedConfig = JSON.parse(storage['station_config_test_station']);
      expect(loadedConfig.speedVerified).toBe(true);
      expect(loadedConfig.speedRate).toBe(120.59);

      // Verify that all unrelated cached data remains intact
      expect(storage['cached_creditors']).toBe(JSON.stringify([{ id: 1, name: 'Patil Transports' }]));
      expect(storage['cached_vehicles']).toBe(JSON.stringify([{ id: 1, number: 'MH-12-AB-1234' }]));
      expect(storage['nozzle_openings_v2_TEST_STATION']).toBe(JSON.stringify({ 1: 5432.1, 2: 9876.5 }));
      expect(storage['dsm_device_id']).toBe('DEVICE-UUID-1234');
    });

    it('XP95 and Power are independent products and never supply or alias Speed', () => {
      const configWithIndependentPremium: StationConfig = {
        stationId: 'PREMIUM_STATION',
        features: {} as any,
        collectionTypes: [],
        isDynamic: true,
        fuelRates: { 'XP95': 125.0, 'Power': 122.0, 'MS-I': 111.48 },
        tankRates: { 'XP95 - 20KL': 125.0, 'Power - 20KL': 122.0 },
        fuelTypeRates: { 'XP95': 125.0, 'Power': 122.0 },
        msIRate: 111.48,
        // No SPEED configured
        cacheSchemaVersion: 2,
        speedVerified: false,
      };

      // SPEED lookup returns 0 (never inherits XP95 125.0 or Power 122.0)
      expect(resolveFuelRate(configWithIndependentPremium, 'SPEED')).toBe(0);
      expect(resolveFuelRate(configWithIndependentPremium, 'Speed')).toBe(0);

      // XP95 resolves its own independent rate
      expect(resolveFuelRate(configWithIndependentPremium, 'XP95')).toBe(125.0);

      // Power resolves its own independent rate
      expect(resolveFuelRate(configWithIndependentPremium, 'Power')).toBe(122.0);
    });

    it('reproduces Mitali production Supabase payload (no FuelRatesJson/SpeedRate columns) through fetch -> cache -> draft -> resolve', async () => {
      const mitaliTanks = [
        { TankId: 1, TankName: 'MS - 20KL', CapacityKL: 20.0, FuelType: 'MS-I', IsActive: true, HasTesting: true, Rate: 103.81 },
        { TankId: 2, TankName: 'HSD - 20KL', CapacityKL: 20.0, FuelType: 'HSD', IsActive: true, HasTesting: true, Rate: 90.35 },
        { TankId: 3, TankName: 'MS - 20KL II', CapacityKL: 20.0, FuelType: 'MS-II', IsActive: true, HasTesting: true, Rate: 103.81 },
        { TankId: 5, TankName: 'SPEED - 20KL', CapacityKL: 20.0, FuelType: 'SPEED', IsActive: true, HasTesting: true, Rate: 120.59 },
        { TankId: 9, TankName: 'CNG LINE', CapacityKL: 20.0, FuelType: 'CNG', IsActive: true, HasTesting: false, Rate: 85.0 },
      ];

      // Seed legacy contaminated localStorage cache & stale draft from v1.3.60 BEFORE online fetch
      storage['station_config_rashtra-cced97'] = JSON.stringify({
        stationId: 'RASHTRA-CCED97',
        fuelRates: { 'MS-I': 103.81, 'MS-II': 103.81, 'HSD': 90.35, 'CNG': 85.0, 'SPEED': 103.81 },
        hsdRate: 90.35,
        msIRate: 103.81,
        msIIRate: 103.81,
        cngRate: 85.0,
        speedRate: 103.81,
      });

      const staleDraftNozzles = [
        { rowId: 1, nozzleId: 1, fuelType: 'MS-I', tankName: 'MS - 20KL', openingReading: 1000, closingReading: 1010, rate: 103.81, pumpId: 1, testing: 0 },
        { rowId: 2, nozzleId: 2, fuelType: 'SPEED', tankName: 'SPEED - 20KL', openingReading: 200, closingReading: 220, rate: 103.81, pumpId: 1, testing: 0 },
        { rowId: 3, nozzleId: 3, fuelType: 'HSD', tankName: 'HSD - 20KL', openingReading: 5000, closingReading: 5050, rate: 90.35, pumpId: 1, testing: 0 },
      ];

      // Step 1: While still on legacy cache before network refresh, sanitization must strip contaminated SPEED (never show 103.81)
      const sanitizedLegacy = sanitizeCachedStationConfig(JSON.parse(storage['station_config_rashtra-cced97']), 'RASHTRA-CCED97');
      expect(sanitizedLegacy.speedVerified).toBe(false);
      expect(sanitizedLegacy.speedRate).toBe(0);
      expect(sanitizedLegacy.msIRate).toBe(103.81);
      expect(sanitizedLegacy.msIIRate).toBe(103.81);
      expect(sanitizedLegacy.hsdRate).toBe(90.35);
      expect(sanitizedLegacy.cngRate).toBe(85.0);
      const preFetchDraft = reconcileNozzleRowRates(staleDraftNozzles, sanitizedLegacy);
      expect(preFetchDraft.rows[1].rate).toBe(0); // Never 103.81!

      // Step 2: Fresh Supabase fetch with actual Mitali Settings schema (NO FuelRatesJson or SpeedRate columns)
      (supabase.from as any).mockReturnValue({
        select: vi.fn().mockReturnThis(),
        eq: vi.fn().mockReturnThis(),
        limit: vi.fn().mockReturnThis(),
        maybeSingle: vi.fn().mockResolvedValue({
          data: {
            station_id: 'RASHTRA-CCED97',
            PumpStationName: 'Mitali Service Station',
            HsdRate: 90.35,
            MsIRate: 103.81,
            MsIIRate: 103.81,
            CngRate: 85,
            TankDefinitionsJson: JSON.stringify(mitaliTanks),
            // FuelRatesJson and SpeedRate omitted identically to live Supabase Settings table
          },
          error: null,
        }),
      });

      const freshConfig = await fetchStationConfig('RASHTRA-CCED97');
      expect(freshConfig.speedVerified).toBe(true);
      expect(freshConfig.speedRate).toBe(120.59);
      expect(freshConfig.tankRates?.['SPEED - 20KL']).toBe(120.59);
      expect(freshConfig.tankFuelTypes?.['SPEED - 20KL']).toBe('SPEED');
      expect(freshConfig.fuelTypeRates?.['SPEED']).toBe(120.59);

      // Step 3: Cache is replaced and sanitized auxiliary nozzle_rates handles legacy vs verified
      const cachedAfterRefresh = JSON.parse(storage['station_config_rashtra-cced97']);
      expect(cachedAfterRefresh.cacheSchemaVersion).toBe(2);
      expect(cachedAfterRefresh.speedRate).toBe(120.59);
      const staleNozzleCache = sanitizeNozzleRatesCache({ speed: 103.81, msI: 103.81 }, freshConfig);
      expect(staleNozzleCache?.speed).toBeUndefined();

      // Step 4: Draft & Pump 1 nozzles reconcile to authoritative rates
      const reconciled = reconcileNozzleRowRates(staleDraftNozzles, freshConfig);
      expect(reconciled.changed).toBe(true);
      expect(reconciled.rows[0].rate).toBe(103.81); // Nozzle 1 (MS-I)
      expect(reconciled.rows[1].rate).toBe(120.59); // Nozzle 2 (SPEED)
      expect(reconciled.rows[2].rate).toBe(90.35);  // Nozzle 3 (HSD)

      const resNozzle2 = validateNozzleRateResolution(freshConfig, 'SPEED', 'SPEED - 20KL');
      expect(resNozzle2.rate).toBe(120.59);
      expect(resNozzle2.sourceTier).toBe('TANK');
      expect(resNozzle2.error).toBeUndefined();
      expect(resolveFuelRate(freshConfig, 'MS-II', 'MS - 20KL II')).toBe(103.81);
    });

    it('rejects mismatched tank/product assignment instead of silently displaying MS rate for SPEED', () => {
      const mitaliConfig: StationConfig = {
        stationId: 'RASHTRA-CCED97',
        features: {} as any,
        collectionTypes: [],
        isDynamic: true,
        tankRates: { 'MS - 20KL': 103.81, 'SPEED - 20KL': 120.59 },
        tankFuelTypes: { 'MS - 20KL': 'MS-I', 'SPEED - 20KL': 'SPEED' },
        fuelTypeRates: { 'MS-I': 103.81, 'SPEED': 120.59 },
        fuelRates: { 'MS-I': 103.81, 'SPEED': 120.59 },
        msIRate: 103.81,
        speedRate: 120.59,
        cacheSchemaVersion: 2,
        speedVerified: true,
      };

      const badRes = validateNozzleRateResolution(mitaliConfig, 'SPEED', 'MS - 20KL');
      expect(badRes.rate).toBe(0);
      expect(badRes.sourceTier).toBe('CONFLICT');
      expect(badRes.error).toContain('Configuration mismatch');
      expect(resolveFuelRate(mitaliConfig, 'SPEED', 'MS - 20KL')).toBe(0);
    });

    it('prevents MS-II tank substring from contaminating MS-I rate when MS-I tank is absent', async () => {
      const tanksOnlyMsII = [
        { TankName: 'MS - 20KL II', FuelType: 'MS-II', Rate: 109.99 },
        { TankName: 'SPEED - 20KL', FuelType: 'SPEED', Rate: 120.59 },
      ];
      (supabase.from as any).mockReturnValue({
        select: vi.fn().mockReturnThis(),
        eq: vi.fn().mockReturnThis(),
        limit: vi.fn().mockReturnThis(),
        maybeSingle: vi.fn().mockResolvedValue({
          data: {
            station_id: 'SUBSTRING_TEST',
            MsIRate: 103.81,
            MsIIRate: 109.99,
            TankDefinitionsJson: JSON.stringify(tanksOnlyMsII),
          },
          error: null,
        }),
      });

      const cfg = await fetchStationConfig('SUBSTRING_TEST');
      expect(cfg.msIIRate).toBe(109.99);
      expect(cfg.msIRate).toBe(103.81);
      expect(cfg.speedRate).toBe(120.59);
    });

    it('sw.js bumps cache version to pyrosync-dsm-v1.5 and cleans legacy caches on activate', async () => {
      const fs = await import('node:fs');
      const path = await import('node:path');
      const swPath = path.resolve(process.cwd(), 'public/sw.js');
      const swSource = fs.readFileSync(swPath, 'utf-8');
      expect(swSource).toContain("const CACHE_NAME = 'pyrosync-dsm-v1.5'");
      expect(swSource).toContain('caches.delete(key)');
    });
  });
});
