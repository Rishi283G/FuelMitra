import { supabase } from './supabase';

export interface StationFeatureSettings {
  cashDenominationsEnabled: boolean;
  cash1Enabled?: boolean;
  cash2Enabled?: boolean;
  cash1DenominationsEnabled?: boolean;
  cash2DenominationsEnabled?: boolean;
  debtorRepaymentEnabled?: boolean;
  collectionReductionEnabled?: boolean;
  otpMachineTrackingEnabled: boolean;
  otpMachineTracking: boolean;
  multiTidTransactionsEnabled: boolean;
  cash3Enabled: boolean;
  splitMorningNightEnabled: boolean;
  personalLedgerEnabled: boolean;
  personalLedgerName: string;
  phonePeTidEnabled?: boolean;
  phonePeBatchEnabled?: boolean;
}

export interface DynamicCollectionType {
  code: string;
  displayName: string;
  category: string;
  hasTidBatch: boolean;
  hasTid?: boolean;
  hasBatch?: boolean;
  hasOtp?: boolean | null;
  displayOrder: number;
  isActive: boolean;
  enableMorning: boolean;
  enableDay: boolean;
  enableNight: boolean;
}

export interface StationConfig {
  stationId: string;
  stationName?: string;
  features: StationFeatureSettings;
  collectionTypes: DynamicCollectionType[];
  isDynamic: boolean;
  fuelRates?: Record<string, number>;
  tankRates?: Record<string, number>;
  tankFuelTypes?: Record<string, string>;
  fuelTypeRates?: Record<string, number>;
  hsdRate?: number;
  msIRate?: number;
  msIIRate?: number;
  speedRate?: number;
  cngRate?: number;
  cacheSchemaVersion?: number;
  speedVerified?: boolean;
}

export interface OtpTransactionItem {
  id: string;
  collectionTypeCode: string;
  amount: number;
  otp: string;
  tid: string;
  batch: string;
  slot: string;
}

export interface ManualTidTransactionItem {
  id: string;
  collectionTypeCode: string;
  amount: number;
  tid: string;
  batch: string;
  slot: string;
}

export function isOtpEligibleType(ct: DynamicCollectionType): boolean {
  return ct.hasOtp !== undefined && ct.hasOtp !== null ? Boolean(ct.hasOtp) : Boolean(ct.hasTidBatch);
}

export function isTidEligibleType(ct: DynamicCollectionType): boolean {
  if (ct.hasTid !== undefined) return Boolean(ct.hasTid);
  return Boolean(ct.hasTidBatch);
}

export function isManualOnlyType(ct: DynamicCollectionType): boolean {
  return !isTidEligibleType(ct) && !isOtpEligibleType(ct);
}

export function parseOptionalExplicitBoolean(val: any): boolean | undefined {
  if (val === undefined || val === null) return undefined;
  if (typeof val === 'boolean') return val;
  if (typeof val === 'number') {
    if (val === 0) return false;
    if (val === 1) return true;
    return val !== 0;
  }
  if (typeof val === 'string') {
    const s = val.trim().toLowerCase();
    if (!s) return undefined;
    if (s === 'false' || s === '0' || s === 'no' || s === 'disabled') return false;
    if (s === 'true' || s === '1' || s === 'yes' || s === 'enabled') return true;
  }
  return undefined;
}

export function resolvePhonePeVisibility(
  features?: Partial<StationFeatureSettings> | null,
  phonePeConfig?: Partial<DynamicCollectionType> | null
): { isPhonePeTidVisible: boolean; isPhonePeBatchVisible: boolean; phonePeGridColumns: string } {
  const isPhonePeTidVisible = (() => {
    if (features?.phonePeTidEnabled !== undefined && features.phonePeTidEnabled !== null) {
      return Boolean(features.phonePeTidEnabled);
    }
    if (phonePeConfig?.hasTid !== undefined && phonePeConfig.hasTid !== null) {
      return Boolean(phonePeConfig.hasTid);
    }
    if (phonePeConfig?.hasTidBatch !== undefined && phonePeConfig.hasTidBatch !== null) {
      return Boolean(phonePeConfig.hasTidBatch);
    }
    return false;
  })();

  const isPhonePeBatchVisible = (() => {
    if (features?.phonePeBatchEnabled !== undefined && features.phonePeBatchEnabled !== null) {
      return Boolean(features.phonePeBatchEnabled);
    }
    if (phonePeConfig?.hasBatch !== undefined && phonePeConfig.hasBatch !== null) {
      return Boolean(phonePeConfig.hasBatch);
    }
    if (phonePeConfig?.hasTidBatch !== undefined && phonePeConfig.hasTidBatch !== null) {
      return Boolean(phonePeConfig.hasTidBatch);
    }
    return false;
  })();

  const phonePeGridColumns =
    isPhonePeTidVisible && isPhonePeBatchVisible
      ? '1.2fr 1fr 1fr'
      : (isPhonePeTidVisible || isPhonePeBatchVisible ? '1.2fr 1fr' : '1fr');

  return { isPhonePeTidVisible, isPhonePeBatchVisible, phonePeGridColumns };
}

export function getDefaultCollectionTypes(): DynamicCollectionType[] {
  return [
    {
      code: 'PHONEPE',
      displayName: 'PhonePe',
      category: 'Online',
      hasTidBatch: false,
      hasTid: false,
      hasBatch: false,
      hasOtp: false,
      displayOrder: 1,
      isActive: true,
      enableMorning: true,
      enableDay: true,
      enableNight: true,
    },
    {
      code: 'CREDIT_CARD',
      displayName: 'Credit Card',
      category: 'Card',
      hasTidBatch: true,
      hasOtp: null,
      displayOrder: 2,
      isActive: true,
      enableMorning: true,
      enableDay: true,
      enableNight: true,
    },
    {
      code: 'PETROCARD',
      displayName: 'Petro Card',
      category: 'Card',
      hasTidBatch: true,
      hasOtp: null,
      displayOrder: 3,
      isActive: true,
      enableMorning: true,
      enableDay: true,
      enableNight: true,
    },
    {
      code: 'SBI_REDEEM',
      displayName: 'SBI Redeem',
      category: 'Card',
      hasTidBatch: false,
      hasOtp: null,
      displayOrder: 4,
      isActive: true,
      enableMorning: true,
      enableDay: true,
      enableNight: true,
    },
    {
      code: 'QR',
      displayName: 'QR',
      category: 'Online',
      hasTidBatch: false,
      hasOtp: null,
      displayOrder: 5,
      isActive: true,
      enableMorning: true,
      enableDay: true,
      enableNight: true,
    },
    {
      code: 'PINELABS_CARD',
      displayName: 'Pine Labs Card',
      category: 'Card',
      hasTidBatch: true,
      hasOtp: null,
      displayOrder: 6,
      isActive: true,
      enableMorning: true,
      enableDay: true,
      enableNight: true,
    },
    {
      code: 'PAYTM',
      displayName: 'Paytm',
      category: 'Online',
      hasTidBatch: false,
      hasOtp: null,
      displayOrder: 7,
      isActive: true,
      enableMorning: true,
      enableDay: true,
      enableNight: true,
    },
    {
      code: 'MOBIKWIK',
      displayName: 'Mobikwik',
      category: 'Online',
      hasTidBatch: false,
      hasOtp: null,
      displayOrder: 8,
      isActive: true,
      enableMorning: true,
      enableDay: true,
      enableNight: true,
    },
    {
      code: 'OTHERS',
      displayName: 'Others',
      category: 'Other',
      hasTidBatch: false,
      hasOtp: null,
      displayOrder: 9,
      isActive: true,
      enableMorning: true,
      enableDay: true,
      enableNight: true,
    },
  ];
}

export function getDefaultStationConfig(stationId: string): StationConfig {
  return {
    stationId: stationId || '',
    features: {
      cashDenominationsEnabled: false,
      cash1Enabled: true,
      cash2Enabled: true,
      cash1DenominationsEnabled: false,
      cash2DenominationsEnabled: true,
      debtorRepaymentEnabled: true,
      collectionReductionEnabled: true,
      otpMachineTrackingEnabled: false,
      otpMachineTracking: false,
      multiTidTransactionsEnabled: true,
      cash3Enabled: false,
      splitMorningNightEnabled: false,
      personalLedgerEnabled: true,
      personalLedgerName: 'Personal Ledger',
      phonePeTidEnabled: undefined,
      phonePeBatchEnabled: undefined,
    },
    collectionTypes: getDefaultCollectionTypes(),
    isDynamic: false,
    fuelRates: {},
    tankRates: {},
    tankFuelTypes: {},
    fuelTypeRates: {},
    hsdRate: 90.35,
    msIRate: 103.81,
    msIIRate: 0,
    speedRate: 0,
    cngRate: 85.0,
    cacheSchemaVersion: 2,
    speedVerified: false,
  };
}

export function normalizeFuelProductFamily(value: string): string {
  const u = (value || '').trim().toUpperCase();
  if (!u) return '';
  if (u.includes('SPEED')) return 'SPEED';
  if (u.includes('XP95') || u.includes('XP-95')) return 'XP95';
  if (u.includes('POWER')) return 'POWER';
  if (u.includes('MS-II') || u.includes('MS2') || u.includes('MS_II') || u.includes('20KL II')) return 'MS-II';
  if (u.includes('HSD') || u.includes('DIESEL')) return 'HSD';
  if (u.includes('CNG')) return 'CNG';
  if (u.includes('MS-I') || u.includes('MS1') || u === 'MS' || u.includes('MS ') || u.includes('MS-') || u.includes('PETROL')) return 'MS-I';
  return u;
}

export function sanitizeCachedStationConfig(raw: any, fallbackStationId = ''): StationConfig {
  if (!raw || typeof raw !== 'object') {
    return getDefaultStationConfig(fallbackStationId);
  }

  const parsed: StationConfig = {
    ...getDefaultStationConfig(raw.stationId || fallbackStationId),
    ...raw,
    fuelRates: raw.fuelRates && typeof raw.fuelRates === 'object' ? { ...raw.fuelRates } : {},
    tankRates: raw.tankRates && typeof raw.tankRates === 'object' ? { ...raw.tankRates } : {},
    tankFuelTypes: raw.tankFuelTypes && typeof raw.tankFuelTypes === 'object' ? { ...raw.tankFuelTypes } : {},
    fuelTypeRates: raw.fuelTypeRates && typeof raw.fuelTypeRates === 'object' ? { ...raw.fuelTypeRates } : {},
  };

  const hasVerifiedSpeedProvenance =
    parsed.cacheSchemaVersion === 2 &&
    parsed.speedVerified === true &&
    typeof parsed.speedRate === 'number' &&
    parsed.speedRate > 0;

  if (!hasVerifiedSpeedProvenance) {
    parsed.speedVerified = false;
    parsed.speedRate = 0;

    if (parsed.fuelRates) {
      for (const k of Object.keys(parsed.fuelRates)) {
        if (k.toUpperCase().includes('SPEED')) {
          delete parsed.fuelRates[k];
        }
      }
    }
    if (parsed.fuelTypeRates) {
      for (const k of Object.keys(parsed.fuelTypeRates)) {
        if (k.toUpperCase().includes('SPEED')) {
          delete parsed.fuelTypeRates[k];
        }
      }
    }
    if (parsed.tankRates) {
      for (const k of Object.keys(parsed.tankRates)) {
        const tankFuel = parsed.tankFuelTypes?.[k] || parsed.tankFuelTypes?.[k.toUpperCase()] || '';
        if (k.toUpperCase().includes('SPEED') || tankFuel.toUpperCase().includes('SPEED')) {
          delete parsed.tankRates[k];
        }
      }
    }
  }

  return parsed;
}

export function sanitizeNozzleRatesCache(
  raw: any,
  stationConfig?: StationConfig | null
): Record<string, any> | null {
  if (!raw || typeof raw !== 'object') return null;
  const copy = { ...raw };
  const isVerified =
    copy.cacheSchemaVersion === 2 &&
    (!stationConfig || stationConfig.speedVerified === true);

  if (!isVerified) {
    delete copy.speed;
  }
  return copy;
}

export async function fetchStationConfig(stationId: string): Promise<StationConfig> {
  const cleanId = (stationId || '').trim();
  if (!cleanId) {
    return getDefaultStationConfig(cleanId);
  }

  const cacheKey = `station_config_${cleanId.toLowerCase()}`;

  // If online, fetch fresh configuration from Supabase to ensure updated slot and type settings propagate immediately
  if (typeof navigator === 'undefined' || navigator.onLine !== false) {
    try {
      const fresh = await fetchAndCacheStationConfig(cleanId, cacheKey);
      return fresh;
    } catch (err) {
      console.warn('Network fetch for station config failed, falling back to cache:', err);
    }
  }

  // Check localStorage cache for offline/instant load
  try {
    const cached = localStorage.getItem(cacheKey);
    if (cached) {
      const parsed = JSON.parse(cached);
      if (parsed && parsed.stationId === cleanId) {
        return sanitizeCachedStationConfig(parsed, cleanId);
      }
    }
  } catch {}

  return await fetchAndCacheStationConfig(cleanId, cacheKey);
}

async function fetchAndCacheStationConfig(cleanId: string, cacheKey: string): Promise<StationConfig> {
  try {
    let data: any = null;
    let lastError: any = null;

    // 1. Try lowercase station_id with exact match
    const r1 = await supabase
      .from('Settings')
      .select('*')
      .eq('station_id', cleanId)
      .limit(1)
      .maybeSingle();

    if (r1.data) {
      data = r1.data;
    } else {
      lastError = r1.error;
      // 2. Try PascalCase StationId with exact match
      const r2 = await supabase
        .from('Settings')
        .select('*')
        .eq('StationId', cleanId)
        .limit(1)
        .maybeSingle();

      if (r2.data) {
        data = r2.data;
      } else {
        lastError = r2.error || lastError;
        // 3. Try case-insensitive lookup for station_id
        const q3 = supabase.from('Settings').select('*');
        const r3 = typeof (q3 as any).ilike === 'function'
          ? await (q3 as any).ilike('station_id', cleanId).limit(1).maybeSingle()
          : await q3.eq('station_id', cleanId).limit(1).maybeSingle();

        if (r3.data) {
          data = r3.data;
        } else {
          lastError = r3.error || lastError;
          // 4. Try case-insensitive lookup for StationId
          const q4 = supabase.from('Settings').select('*');
          const r4 = typeof (q4 as any).ilike === 'function'
            ? await (q4 as any).ilike('StationId', cleanId).limit(1).maybeSingle()
            : await q4.eq('StationId', cleanId).limit(1).maybeSingle();

          if (r4.data) {
            data = r4.data;
          } else {
            lastError = r4.error || lastError;
          }
        }
      }
    }

    if (!data) {
      console.warn('Could not fetch station settings from Supabase, checking cache:', lastError);
      try {
        const cached = localStorage.getItem(cacheKey);
        if (cached) {
          const parsed = JSON.parse(cached);
          if (parsed && parsed.stationId === cleanId) {
            return sanitizeCachedStationConfig(parsed, cleanId);
          }
        }
      } catch {}
      return getDefaultStationConfig(cleanId);
    }

    const collectionTypesRaw =
      data.CollectionTypesJson ??
      data.collection_types_json ??
      data.collectiontypesjson;

    let appFeatureSettingsRaw =
      data.AppFeatureSettingsJson ??
      data.app_feature_settings_json ??
      data.appfeaturesettingsjson ??
      data.FeatureSettingsJson ??
      data.feature_settings_json ??
      data.featuresettingsjson ??
      data.FeatureTogglesJson ??
      data.feature_toggles_json ??
      data.feature_toggles ??
      data.FeatureToggles ??
      data.features ??
      data.Features;

    const fuelRatesRaw =
      data.FuelRatesJson ??
      data.fuel_rates_json ??
      data.fuelratesjson;

    const tankDefinitionsRaw =
      data.TankDefinitionsJson ??
      data.tank_definitions_json ??
      data.tankdefinitionsjson;

    const rawHsd = data.HsdRate ?? data.hsd_rate ?? data.hsdrate;
    const rawMsI = data.MsIRate ?? data.ms_i_rate ?? data.ms_irate ?? data.msirate;
    const rawMsII = data.MsIIRate ?? data.ms_ii_rate ?? data.ms_iirate ?? data.msiirate;
    const rawCng = data.CngRate ?? data.cng_rate ?? data.cngrate;

    const stationName =
      data.StationName ??
      data.station_name ??
      data.PumpStationName ??
      data.DisplayName ??
      data.Name ??
      data.pump_station_name ??
      undefined;

    let collectionTypes: DynamicCollectionType[] = [];
    let hasAuthoritativeCollectionTypes = false;
    if (collectionTypesRaw) {
      try {
        const parsed = typeof collectionTypesRaw === 'string' ? JSON.parse(collectionTypesRaw) : collectionTypesRaw;
        if (Array.isArray(parsed) && parsed.length > 0) {
          hasAuthoritativeCollectionTypes = true;
          collectionTypes = parsed
            .map((item: any) => {
              const code = (item.Code || item.code || '').trim().toUpperCase();
              const rawTidBatch = item.HasTidBatch !== undefined ? item.HasTidBatch : item.hasTidBatch;
              const explicitTidBatch = parseOptionalExplicitBoolean(rawTidBatch);

              const rawTid = item.HasTid ?? item.hasTid ?? item.EnableTid ?? item.enableTid ?? item.TidEnabled ?? item.tidEnabled ?? item.ShowTid ?? item.showTid;
              const explicitTid = parseOptionalExplicitBoolean(rawTid);

              const rawBatch = item.HasBatch ?? item.hasBatch ?? item.EnableBatch ?? item.enableBatch ?? item.BatchEnabled ?? item.batchEnabled ?? item.ShowBatch ?? item.showBatch;
              const explicitBatch = parseOptionalExplicitBoolean(rawBatch);

              // Normalize hasTidBatch:
              // For PhonePe: default to false if omitted/null per desktop SeedData.cs
              // For other types: preserve existing default true
              const defaultTidBatch = code === 'PHONEPE' ? false : true;
              const hasTidBatch = explicitTidBatch !== undefined ? explicitTidBatch : defaultTidBatch;

              return {
                code,
                displayName: item.DisplayName || item.displayName || item.Code || '',
                category: item.Category || item.category || 'Online',
                hasTidBatch,
                hasTid: explicitTid,
                hasBatch: explicitBatch,
                hasOtp: (() => {
                  const raw = item.HasOtp !== undefined ? item.HasOtp : item.hasOtp;
                  if (raw === undefined || raw === null) {
                    if (code === 'PHONEPE') return false;
                    return null;
                  }
                  if (typeof raw === 'boolean') return raw;
                  if (typeof raw === 'number') return raw !== 0;
                  if (typeof raw === 'string') {
                    const s = raw.trim().toLowerCase();
                    if (s === 'true' || s === '1' || s === 'yes') return true;
                    if (s === 'false' || s === '0' || s === 'no') return false;
                    if (s === 'null' || s === '') return null;
                  }
                  return null;
                })(),
                displayOrder: Number(item.DisplayOrder ?? item.displayOrder ?? 0),
                isActive: item.IsActive !== false && item.isActive !== false,
                enableMorning: item.EnableMorning !== false && item.enableMorning !== false,
                enableDay: item.EnableDay !== false && item.enableDay !== false,
                enableNight: item.EnableNight !== false && item.enableNight !== false,
              };
            })
            .filter((c: DynamicCollectionType) => c.code)
            .sort((a: DynamicCollectionType, b: DynamicCollectionType) => a.displayOrder - b.displayOrder);
        }
      } catch (err) {
        console.error('Failed to parse CollectionTypesJson:', err);
      }
    }

    function parseExplicitBoolean(val: any, defaultVal = false): boolean {
      if (val === undefined || val === null) return defaultVal;
      if (typeof val === 'boolean') return val;
      if (typeof val === 'number') return val !== 0;
      if (typeof val === 'string') {
        const s = val.trim().toLowerCase();
        if (s === 'true' || s === '1' || s === 'yes' || s === 'y' || s === 'enabled') return true;
        if (s === 'false' || s === '0' || s === 'no' || s === 'n' || s === 'disabled') return false;
      }
      return defaultVal;
    }

    function normalizeFeatureKey(k: string): string {
      return (k || '').trim().toLowerCase().replace(/[-_\s]/g, '');
    }

    let cashDenominationsEnabled = false;
    let cash1Enabled = true;
    let cash2Enabled = true;
    let cash1DenominationsEnabled = false;
    let cash1DenomExplicit = false;
    let cash2DenominationsEnabled = true;
    let debtorRepaymentEnabled = true;
    let collectionReductionEnabled = true;
    let otpMachineTrackingEnabled = false;
    let multiTidTransactionsEnabled = true;
    let cash3Enabled = false;
    let splitMorningNightEnabled = false;
    let personalLedgerEnabled = true;
    let personalLedgerName = 'Personal Ledger';
    let phonePeTidEnabled: boolean | undefined = undefined;
    let phonePeBatchEnabled: boolean | undefined = undefined;

    function applyFeatureItem(itemKey: string, itemVal: any, itemDisplayName?: string) {
      const normKey = normalizeFeatureKey(itemKey);
      const isEnabled = parseExplicitBoolean(itemVal, false);
      const explicitOpt = parseOptionalExplicitBoolean(itemVal);

      if (
        normKey === 'operationsphonepetid' ||
        normKey === 'phonepetid' ||
        normKey === 'phonepetidenabled' ||
        normKey === 'operationsphonepetidenabled' ||
        normKey === 'phonepetidvisible' ||
        normKey === 'operationsphonepetidvisible'
      ) {
        if (explicitOpt !== undefined) {
          phonePeTidEnabled = explicitOpt;
        }
      } else if (
        normKey === 'operationsphonepebatch' ||
        normKey === 'phonepebatch' ||
        normKey === 'phonepebatchenabled' ||
        normKey === 'operationsphonepebatchenabled' ||
        normKey === 'phonepebatchvisible' ||
        normKey === 'operationsphonepebatchvisible'
      ) {
        if (explicitOpt !== undefined) {
          phonePeBatchEnabled = explicitOpt;
        }
      } else if (normKey === 'operationscash1' || normKey === 'cash1') {
        cash1Enabled = isEnabled;
      } else if (normKey === 'operationscash2' || normKey === 'cash2') {
        cash2Enabled = isEnabled;
      } else if (normKey === 'operationscash1denominations' || normKey === 'cash1denominations') {
        cash1DenominationsEnabled = isEnabled;
        cash1DenomExplicit = true;
      } else if (normKey === 'operationscash2denominations' || normKey === 'cash2denominations') {
        cash2DenominationsEnabled = isEnabled;
      } else if (normKey === 'operationsdebtorrepayment' || normKey === 'debtorrepayment') {
        debtorRepaymentEnabled = isEnabled;
      } else if (normKey === 'operationscollectionreduction' || normKey === 'collectionreduction') {
        collectionReductionEnabled = isEnabled;
      } else if (normKey === 'operationscashdenominations' || normKey === 'cashdenominations') {
        cashDenominationsEnabled = isEnabled;
        if (!cash1DenomExplicit) {
          cash1DenominationsEnabled = isEnabled;
        }
      } else if (
        normKey === 'operationsotpmachinetracking' ||
        normKey === 'otpmachinetracking' ||
        normKey === 'operationsotptracking' ||
        normKey === 'otptracking'
      ) {
        otpMachineTrackingEnabled = isEnabled;
      } else if (
        normKey === 'operationsmultitidtransactions' ||
        normKey === 'multitidtransactions'
      ) {
        multiTidTransactionsEnabled = isEnabled;
      } else if (
        normKey === 'operationscash3' ||
        normKey === 'cash3'
      ) {
        cash3Enabled = isEnabled;
      } else if (normKey === 'collectionusemorningnight' || normKey === 'usemorningnight') {
        splitMorningNightEnabled = isEnabled;
      } else if (normKey === 'operationspersonalledger' || normKey === 'personalledger') {
        personalLedgerEnabled = parseExplicitBoolean(itemVal, true);
        if (itemDisplayName && itemDisplayName.trim()) {
          personalLedgerName = itemDisplayName.trim();
        }
      }
    }

    if (appFeatureSettingsRaw) {
      try {
        const feats = typeof appFeatureSettingsRaw === 'string' ? JSON.parse(appFeatureSettingsRaw) : appFeatureSettingsRaw;
        if (Array.isArray(feats)) {
          for (const f of feats) {
            if (!f || typeof f !== 'object') continue;
            const key =
              f.FeatureKey ||
              f.featureKey ||
              f.feature_key ||
              f.Feature_Key ||
              f.Key ||
              f.key ||
              f.Name ||
              f.name ||
              f.Code ||
              f.code ||
              '';
            const enabled =
              f.IsEnabled ??
              f.isEnabled ??
              f.is_enabled ??
              f.Enabled ??
              f.enabled ??
              f.Value ??
              f.value ??
              f.IsActive ??
              f.isActive;
            const displayName = f.DisplayName || f.displayName || f.Name || f.name || '';
            applyFeatureItem(key, enabled, displayName);
          }
        } else if (feats && typeof feats === 'object') {
          for (const [k, v] of Object.entries(feats)) {
            if (v && typeof v === 'object' && !Array.isArray(v)) {
              const objVal = v as any;
              const enabled =
                objVal.IsEnabled ??
                objVal.isEnabled ??
                objVal.is_enabled ??
                objVal.Enabled ??
                objVal.enabled ??
                objVal.Value ??
                objVal.value;
              const displayName = objVal.DisplayName || objVal.displayName || '';
              applyFeatureItem(k, enabled, displayName);
            } else {
              applyFeatureItem(k, v);
            }
          }
        }
      } catch (err) {
        console.error('Failed to parse AppFeatureSettingsJson:', err);
      }
    }

    let fuelRates: Record<string, number> = {};
    if (fuelRatesRaw) {
      try {
        const parsedRates = typeof fuelRatesRaw === 'string' ? JSON.parse(fuelRatesRaw) : fuelRatesRaw;
        if (parsedRates && typeof parsedRates === 'object') {
          for (const [k, v] of Object.entries(parsedRates)) {
            const num = Number(v);
            if (!isNaN(num) && num > 0) {
              fuelRates[k] = num;
              fuelRates[k.trim()] = num;
            }
          }
        }
      } catch (err) {
        console.error('Failed to parse FuelRatesJson:', err);
      }
    }

    // ── Parse Canonical TankDefinitionsJson ──
    let tankRates: Record<string, number> = {};
    let tankFuelTypes: Record<string, string> = {};
    let fuelTypeCandidateMap: Record<string, Set<number>> = {};
    let tankDefinitionsParsed: any[] = [];

    if (tankDefinitionsRaw) {
      try {
        const parsedTanks = typeof tankDefinitionsRaw === 'string' ? JSON.parse(tankDefinitionsRaw) : tankDefinitionsRaw;
        if (Array.isArray(parsedTanks)) {
          tankDefinitionsParsed = parsedTanks;
          for (const tank of parsedTanks) {
            const tankName = (tank.TankName || tank.tankName || '').trim();
            const fuelType = (tank.FuelType || tank.fuelType || '').trim();
            const rate = Number(tank.Rate ?? tank.rate);
            if (tankName && fuelType) {
              tankFuelTypes[tankName] = fuelType;
              tankFuelTypes[tankName.toUpperCase()] = fuelType;
            }
            if (!isNaN(rate) && rate > 0) {
              if (tankName) {
                tankRates[tankName] = rate;
                tankRates[tankName.toUpperCase()] = rate;
              }
              if (fuelType) {
                const uFuel = fuelType.toUpperCase();
                if (!fuelTypeCandidateMap[uFuel]) {
                  fuelTypeCandidateMap[uFuel] = new Set<number>();
                }
                fuelTypeCandidateMap[uFuel].add(rate);
              }
            }
          }
        }
      } catch (err) {
        console.error('Failed to parse TankDefinitionsJson:', err);
      }
    }

    // Build ambiguity-checked fuelTypeRates:
    let fuelTypeRates: Record<string, number> = {};
    for (const [fuelKey, rateSet] of Object.entries(fuelTypeCandidateMap)) {
      if (rateSet.size === 1) {
        fuelTypeRates[fuelKey] = Array.from(rateSet)[0];
      }
    }

    function findRateInMap(map: Record<string, number>, keywords: string[], excludeKeywords: string[] = []): number | undefined {
      for (const [k, v] of Object.entries(map)) {
        const uk = k.toUpperCase();
        if (excludeKeywords.some((ex) => uk.includes(ex))) continue;
        if (keywords.some((kw) => uk === kw || uk.includes(kw))) {
          if (v > 0) return v;
        }
      }
      return undefined;
    }

    // Strict independent rate resolution with ZERO cross-contamination
    const hsdFromTanks = fuelTypeRates['HSD'] ?? findRateInMap(tankRates, ['HSD'], ['II', 'MS-II']);
    const hsdFromMap = findRateInMap(fuelRates, ['HSD', 'DIESEL'], ['II', 'MS-II']);
    const hsdRate = hsdFromTanks ?? hsdFromMap ?? (rawHsd && Number(rawHsd) > 0 ? Number(rawHsd) : 90.35);

    const msIExclude = ['II', 'MS-II', 'MS2', 'MS_II', 'SPEED', 'XP95', 'XP-95', 'POWER', 'HSD', 'CNG'];
    const msIFromTanks = fuelTypeRates['MS-I'] ?? fuelTypeRates['MS'] ?? findRateInMap(tankRates, ['MS-I', 'MS - 20KL', 'PETROL'], msIExclude);
    const msIFromMap = findRateInMap(fuelRates, ['MS-I', 'MS -', 'PETROL', 'MS1'], msIExclude) ?? (fuelRates['MS'] && fuelRates['MS'] > 0 ? fuelRates['MS'] : undefined);
    const msIRate = msIFromTanks ?? msIFromMap ?? (rawMsI && Number(rawMsI) > 0 ? Number(rawMsI) : 103.81);

    const speedFromTanks = fuelTypeRates['SPEED'] ?? findRateInMap(tankRates, ['SPEED']);
    const speedFromMap = findRateInMap(fuelRates, ['SPEED']);
    const rawSpeed = data.SpeedRate ?? data.speed_rate ?? data.speedrate;
    const speedRate = speedFromTanks ?? speedFromMap ?? (rawSpeed && Number(rawSpeed) > 0 ? Number(rawSpeed) : undefined);

    const msIIFromTanks = fuelTypeRates['MS-II'] ?? fuelTypeRates['MS2'] ?? findRateInMap(tankRates, ['MS-II', 'MS - 20KL II', 'HSD - 20KL II']);
    const msIIFromMap = findRateInMap(fuelRates, ['MS-II', 'MS2', 'MS_II', '20KL II']);
    const msIIRate = msIIFromTanks ?? msIIFromMap ?? (rawMsII && Number(rawMsII) > 0 ? Number(rawMsII) : undefined);

    const cngFromTanks = fuelTypeRates['CNG'] ?? findRateInMap(tankRates, ['CNG']);
    const cngFromMap = findRateInMap(fuelRates, ['CNG']);
    const cngRate = cngFromTanks ?? cngFromMap ?? (rawCng && Number(rawCng) > 0 ? Number(rawCng) : 85.0);

    // Speed verification status:
    const speedVerified = (speedRate !== undefined && speedRate > 0) && (
      (tankDefinitionsParsed.some(t => {
        const name = (t.TankName || t.tankName || '').toUpperCase();
        const ftype = (t.FuelType || t.fuelType || '').toUpperCase();
        return (name.includes('SPEED') || ftype.includes('SPEED')) && Number(t.Rate ?? t.rate) > 0;
      })) ||
      Boolean(speedFromMap && speedFromMap > 0) ||
      Boolean(rawSpeed && Number(rawSpeed) > 0)
    );

    // Normalize canonical keys into fuelRates map for direct lookup
    if (speedRate && !fuelRates['Speed']) fuelRates['Speed'] = speedRate;
    if (speedRate && !fuelRates['SPEED']) fuelRates['SPEED'] = speedRate;
    if (msIRate && !fuelRates['MS-I']) fuelRates['MS-I'] = msIRate;
    if (msIIRate && !fuelRates['MS-II']) fuelRates['MS-II'] = msIIRate;
    if (hsdRate && !fuelRates['HSD']) fuelRates['HSD'] = hsdRate;
    if (cngRate && !fuelRates['CNG']) fuelRates['CNG'] = cngRate;

    const config: StationConfig = {
      stationId: cleanId,
      stationName,
      features: {
        cashDenominationsEnabled,
        cash1Enabled,
        cash2Enabled,
        cash1DenominationsEnabled,
        cash2DenominationsEnabled,
        debtorRepaymentEnabled,
        collectionReductionEnabled,
        otpMachineTrackingEnabled,
        otpMachineTracking: otpMachineTrackingEnabled,
        multiTidTransactionsEnabled,
        cash3Enabled,
        splitMorningNightEnabled,
        personalLedgerEnabled,
        personalLedgerName,
        phonePeTidEnabled,
        phonePeBatchEnabled,
      },
      collectionTypes: hasAuthoritativeCollectionTypes ? collectionTypes : getDefaultCollectionTypes(),
      isDynamic: hasAuthoritativeCollectionTypes,
      fuelRates,
      tankRates,
      tankFuelTypes,
      fuelTypeRates,
      hsdRate,
      msIRate,
      msIIRate,
      speedRate,
      cngRate,
      cacheSchemaVersion: 2,
      speedVerified,
    };

    try {
      localStorage.setItem(cacheKey, JSON.stringify(config));
    } catch {}

    return config;
  } catch (err) {
    console.error('Exception in fetchAndCacheStationConfig:', err);
    try {
      const cached = localStorage.getItem(cacheKey);
      if (cached) {
        const parsed = JSON.parse(cached);
        if (parsed && parsed.stationId === cleanId) {
          return sanitizeCachedStationConfig(parsed, cleanId);
        }
      }
    } catch {}
    return getDefaultStationConfig(cleanId);
  }
}

export interface NozzleRateResolutionResult {
  rate: number;
  sourceTier: 'TANK' | 'FUEL_TYPE' | 'CATEGORY' | 'CONFLICT' | 'UNRESOLVED';
  error?: string;
}

export function validateNozzleRateResolution(
  config: StationConfig | null | undefined,
  fuelType: string,
  tankName?: string
): NozzleRateResolutionResult {
  if (!config) {
    return { rate: 0, sourceTier: 'UNRESOLVED', error: 'Station configuration is not loaded.' };
  }

  const normTank = (tankName || '').trim();
  const normFuel = (fuelType || '').trim();
  const upperTank = normTank.toUpperCase();
  const upperFuel = normFuel.toUpperCase();

  const nozzleFamily = normalizeFuelProductFamily(normFuel);
  const declaredTankFuel = normTank
    ? (config.tankFuelTypes?.[normTank] ?? config.tankFuelTypes?.[upperTank])
    : undefined;
  const tankFamily = declaredTankFuel
    ? normalizeFuelProductFamily(declaredTankFuel)
    : normalizeFuelProductFamily(normTank);

  // Tank-product compatibility guard:
  // 1. If TankDefinitionsJson declared a FuelType for this tank, nozzle FuelType must match it.
  // 2. A SPEED nozzle must never resolve against an MS-I / MS-II / HSD / CNG / XP95 / POWER tank.
  if (normTank && nozzleFamily && tankFamily && nozzleFamily !== tankFamily) {
    if (declaredTankFuel || nozzleFamily === 'SPEED') {
      return {
        rate: 0,
        sourceTier: 'CONFLICT',
        error: `Configuration mismatch: Nozzle FuelType '${normFuel}' is mapped to tank '${normTank}' (${declaredTankFuel || tankFamily}).`,
      };
    }
  }

  // Tier 1: TankName direct or case-insensitive match in tankRates or fuelRates
  if (normTank) {
    if (config.tankRates) {
      if (config.tankRates[normTank] !== undefined && config.tankRates[normTank] > 0) {
        return { rate: Number(config.tankRates[normTank]), sourceTier: 'TANK' };
      }
      if (config.tankRates[upperTank] !== undefined && config.tankRates[upperTank] > 0) {
        return { rate: Number(config.tankRates[upperTank]), sourceTier: 'TANK' };
      }
    }
    if (config.fuelRates) {
      if (config.fuelRates[normTank] !== undefined && config.fuelRates[normTank] > 0) {
        return { rate: Number(config.fuelRates[normTank]), sourceTier: 'TANK' };
      }
      for (const [k, v] of Object.entries(config.fuelRates)) {
        if (k.trim().toUpperCase() === upperTank && Number(v) > 0) {
          return { rate: Number(v), sourceTier: 'TANK' };
        }
      }
    }
  }

  // Tier 2: FuelType direct or case-insensitive match in fuelTypeRates or fuelRates
  if (normFuel) {
    if (config.fuelTypeRates) {
      if (config.fuelTypeRates[normFuel] !== undefined && config.fuelTypeRates[normFuel] > 0) {
        return { rate: Number(config.fuelTypeRates[normFuel]), sourceTier: 'FUEL_TYPE' };
      }
      if (config.fuelTypeRates[upperFuel] !== undefined && config.fuelTypeRates[upperFuel] > 0) {
        return { rate: Number(config.fuelTypeRates[upperFuel]), sourceTier: 'FUEL_TYPE' };
      }
    }
    if (config.fuelRates) {
      if (config.fuelRates[normFuel] !== undefined && config.fuelRates[normFuel] > 0) {
        return { rate: Number(config.fuelRates[normFuel]), sourceTier: 'FUEL_TYPE' };
      }
      for (const [k, v] of Object.entries(config.fuelRates)) {
        if (k.trim().toUpperCase() === upperFuel && Number(v) > 0) {
          return { rate: Number(v), sourceTier: 'FUEL_TYPE' };
        }
      }
    }
  }

  // Tier 3: Strict product category fallback based on keywords (ZERO cross-contamination!)
  const combined = `${upperFuel} ${upperTank}`.trim();

  // SPEED (BPCL Premium Petrol)
  if (combined.includes('SPEED')) {
    if (config.speedRate && config.speedRate > 0) {
      return { rate: config.speedRate, sourceTier: 'CATEGORY' };
    }
    return {
      rate: 0,
      sourceTier: 'UNRESOLVED',
      error: `SPEED rate is missing or unverified for '${normTank || normFuel}'.`,
    };
  }

  // XP95 (IOCL Premium Petrol - independent product)
  if (combined.includes('XP95') || combined.includes('XP-95')) {
    const xpRate = config.fuelRates?.['XP95'] || config.tankRates?.['XP95'];
    if (xpRate && xpRate > 0) {
      return { rate: xpRate, sourceTier: 'CATEGORY' };
    }
    return { rate: 0, sourceTier: 'UNRESOLVED', error: `XP95 rate is not configured.` };
  }

  // POWER (HPCL Premium Petrol - independent product)
  if (combined.includes('POWER')) {
    const pwrRate = config.fuelRates?.['Power'] || config.fuelRates?.['POWER'] || config.tankRates?.['Power'];
    if (pwrRate && pwrRate > 0) {
      return { rate: pwrRate, sourceTier: 'CATEGORY' };
    }
    return { rate: 0, sourceTier: 'UNRESOLVED', error: `Power rate is not configured.` };
  }

  // MS-II
  if (combined.includes('MS-II') || combined.includes('MS2') || combined.includes('MS_II') || combined.includes('20KL II')) {
    if (config.msIIRate && config.msIIRate > 0) {
      return { rate: config.msIIRate, sourceTier: 'CATEGORY' };
    }
    return { rate: 0, sourceTier: 'UNRESOLVED', error: `MS-II rate is not configured.` };
  }

  // HSD / Diesel
  if (combined.includes('HSD') || combined.includes('DIESEL')) {
    if (config.hsdRate && config.hsdRate > 0) {
      return { rate: config.hsdRate, sourceTier: 'CATEGORY' };
    }
    return { rate: 0, sourceTier: 'UNRESOLVED', error: `HSD rate is not configured.` };
  }

  // CNG
  if (combined.includes('CNG')) {
    if (config.cngRate && config.cngRate > 0) {
      return { rate: config.cngRate, sourceTier: 'CATEGORY' };
    }
    return { rate: 0, sourceTier: 'UNRESOLVED', error: `CNG rate is not configured.` };
  }

  // MS-I / Regular Petrol
  if (nozzleFamily === 'MS-I') {
    if (config.msIRate && config.msIRate > 0) {
      return { rate: config.msIRate, sourceTier: 'CATEGORY' };
    }
    return { rate: 0, sourceTier: 'UNRESOLVED', error: `MS-I rate is not configured.` };
  }

  // Tier 4: Unknown product
  return {
    rate: 0,
    sourceTier: 'UNRESOLVED',
    error: `Fuel rate could not be resolved for '${normFuel || normTank || 'UNKNOWN'}'.`,
  };
}

export function resolveFuelRate(
  config: StationConfig | null | undefined,
  fuelType: string,
  tankName?: string
): number {
  return validateNozzleRateResolution(config, fuelType, tankName).rate;
}

export function reconcileNozzleRowRates<
  T extends { fuelType: string; tankName?: string; rate: number; rateError?: string }
>(rows: T[], config: StationConfig | null | undefined): { rows: T[]; changed: boolean } {
  let changed = false;
  const updated = rows.map((row) => {
    const res = validateNozzleRateResolution(config, row.fuelType, row.tankName);
    const nextRate = res.rate > 0 ? res.rate : 0;
    const nextError = res.error;
    if (Math.abs(nextRate - (Number(row.rate) || 0)) > 0.0001 || (row.rateError || '') !== (nextError || '')) {
      changed = true;
      return {
        ...row,
        rate: nextRate,
        rateError: nextError,
      };
    }
    return row;
  });
  return { rows: updated, changed };
}
