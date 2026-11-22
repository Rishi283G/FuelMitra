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
  hsdRate?: number;
  msIRate?: number;
  msIIRate?: number;
  speedRate?: number;
  cngRate?: number;
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
    hsdRate: 90.35,
    msIRate: 103.81,
    msIIRate: 0,
    speedRate: 0,
    cngRate: 85.0,
  };
}

export async function fetchStationConfig(stationId: string): Promise<StationConfig> {
  const cleanId = (stationId || '').trim();
  if (!cleanId) {
    return getDefaultStationConfig(cleanId);
  }

  const cacheKey = `station_config_${cleanId.toLowerCase()}`;

  // If online, fetch fresh configuration from Supabase to ensure updated slot and type settings propagate immediately
  if (typeof navigator === 'undefined' || navigator.onLine) {
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
        return parsed;
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
            return parsed;
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

    const rawSpeed = data.SpeedRate ?? data.speed_rate ?? data.speedrate;

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

    const hsdFromMap = findRateInMap(fuelRates, ['HSD', 'DIESEL'], ['II', 'MS-II']);
    const msIFromMap = findRateInMap(fuelRates, ['MS-I', 'MS -', 'PETROL', 'MS1']) ?? (fuelRates['MS'] && fuelRates['MS'] > 0 ? fuelRates['MS'] : undefined);
    const speedFromMap = findRateInMap(fuelRates, ['SPEED', 'XP95', 'POWER', 'TURBO', 'EXTRA']);
    const msIIFromMap = findRateInMap(fuelRates, ['MS-II', 'MS2', 'MS_II', '20KL II']) ?? speedFromMap;
    const cngFromMap = findRateInMap(fuelRates, ['CNG']);

    // Authoritative rate resolution: dynamic fuelRates map takes precedence over scalar columns, which take precedence over hardcoded defaults
    const hsdRate = hsdFromMap ?? (rawHsd && Number(rawHsd) > 0 ? Number(rawHsd) : 90.35);
    const msIRate = msIFromMap ?? (rawMsI && Number(rawMsI) > 0 ? Number(rawMsI) : 103.81);
    const msIIRate = msIIFromMap ?? (rawMsII && Number(rawMsII) > 0 ? Number(rawMsII) : (speedFromMap ?? 0));
    const speedRate = speedFromMap ?? (rawSpeed && Number(rawSpeed) > 0 ? Number(rawSpeed) : (rawMsII && Number(rawMsII) > 0 ? Number(rawMsII) : 0));
    const cngRate = cngFromMap ?? (rawCng && Number(rawCng) > 0 ? Number(rawCng) : 85.0);

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
      hsdRate,
      msIRate,
      msIIRate,
      speedRate,
      cngRate,
    };

    try {
      localStorage.setItem(cacheKey, JSON.stringify(config));
    } catch {}

    return config;
  } catch (err) {
    console.error('Exception in fetchAndCacheStationConfig:', err);
    return getDefaultStationConfig(cleanId);
  }
}

export function resolveFuelRate(
  config: StationConfig | null | undefined,
  fuelType: string,
  tankName?: string
): number {
  if (!config) return 0;
  const ratesMap = config.fuelRates || {};
  const normFuel = (fuelType || '').trim();
  const normTank = (tankName || '').trim();

  // 1. Direct match on tankName or fuelType
  if (normTank && ratesMap[normTank] !== undefined && ratesMap[normTank] > 0) return Number(ratesMap[normTank]);
  if (normFuel && ratesMap[normFuel] !== undefined && ratesMap[normFuel] > 0) return Number(ratesMap[normFuel]);

  // 2. Case-insensitive key match
  const upperTank = normTank.toUpperCase();
  const upperFuel = normFuel.toUpperCase();
  for (const [k, v] of Object.entries(ratesMap)) {
    const key = k.trim().toUpperCase();
    const val = Number(v);
    if (val <= 0) continue;
    if (upperTank && key === upperTank) return val;
    if (upperFuel && key === upperFuel) return val;
  }

  // 3. Keyword-based matching
  const combined = `${normFuel} ${normTank}`.toUpperCase();

  // Speed / Premium Petrol
  if (combined.includes('SPEED') || combined.includes('XP95') || combined.includes('POWER') || combined.includes('TURBO') || combined.includes('EXTRA')) {
    for (const [k, v] of Object.entries(ratesMap)) {
      const key = k.toUpperCase();
      const val = Number(v);
      if (val > 0 && (key.includes('SPEED') || key.includes('XP95') || key.includes('POWER') || key.includes('EXTRA'))) return val;
    }
    // Also check MS-II in ratesMap as alias
    for (const [k, v] of Object.entries(ratesMap)) {
      const key = k.toUpperCase();
      const val = Number(v);
      if (val > 0 && (key.includes('MS-II') || key.includes('MS2') || key.includes('20KL II'))) return val;
    }
    if (config.speedRate && config.speedRate > 0) return config.speedRate;
    if (config.msIIRate && config.msIIRate > 0) return config.msIIRate;
    return 0; // Crucial: never return msIRate for Speed!
  }

  // HSD / Diesel
  if (combined.includes('HSD') || combined.includes('DIESEL')) {
    for (const [k, v] of Object.entries(ratesMap)) {
      const key = k.toUpperCase();
      const val = Number(v);
      if (val > 0 && (key.includes('HSD') || key.includes('DIESEL')) && !key.includes('II')) return val;
    }
    if (config.hsdRate && config.hsdRate > 0) return config.hsdRate;
  }

  // MS-II
  if (combined.includes('MS-II') || combined.includes('MS2') || combined.includes('MS_II') || combined.includes('20KL II')) {
    for (const [k, v] of Object.entries(ratesMap)) {
      const key = k.toUpperCase();
      const val = Number(v);
      if (val > 0 && (key.includes('MS-II') || key.includes('MS2') || key.includes('20KL II') || key.includes('SPEED') || key.includes('XP95') || key.includes('POWER'))) return val;
    }
    if (config.msIIRate && config.msIIRate > 0) return config.msIIRate;
    if (config.speedRate && config.speedRate > 0) return config.speedRate;
  }

  // MS-I / Petrol
  if (combined.includes('MS-I') || combined.includes('MS') || combined.includes('PETROL')) {
    for (const [k, v] of Object.entries(ratesMap)) {
      const key = k.toUpperCase();
      const val = Number(v);
      if (val > 0 && (key.includes('MS-I') || key === 'MS' || key.includes('PETROL') || key.includes('MS -'))) return val;
    }
    if (config.msIRate && config.msIRate > 0) return config.msIRate;
  }

  // CNG
  if (combined.includes('CNG')) {
    for (const [k, v] of Object.entries(ratesMap)) {
      const key = k.toUpperCase();
      const val = Number(v);
      if (val > 0 && key.includes('CNG')) return val;
    }
    if (config.cngRate && config.cngRate > 0) return config.cngRate;
  }

  // 4. Exact fuelType fallbacks
  if (upperFuel.includes('SPEED') || upperFuel.includes('XP95') || upperFuel.includes('POWER')) return config.speedRate || config.msIIRate || 0;
  if (upperFuel.includes('HSD')) return config.hsdRate || 90.35;
  if (upperFuel.includes('CNG')) return config.cngRate || 85.0;
  if (upperFuel.includes('MS-II')) return config.msIIRate || config.speedRate || 103.81;
  if (upperFuel.includes('MS-I') || upperFuel.includes('MS') || upperFuel.includes('PETROL')) return config.msIRate || 103.81;

  return config.msIRate || 103.81;
}
