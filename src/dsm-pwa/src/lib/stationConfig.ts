import { supabase } from './supabase';

export interface StationFeatureSettings {
  cashDenominationsEnabled: boolean;
  otpMachineTrackingEnabled: boolean;
  splitMorningNightEnabled: boolean;
  personalLedgerEnabled: boolean;
  personalLedgerName: string;
}

export interface DynamicCollectionType {
  code: string;
  displayName: string;
  category: string;
  hasTidBatch: boolean;
  displayOrder: number;
  isActive: boolean;
  enableMorning: boolean;
  enableDay: boolean;
  enableNight: boolean;
}

export interface StationConfig {
  stationId: string;
  features: StationFeatureSettings;
  collectionTypes: DynamicCollectionType[];
  isDynamic: boolean;
  fuelRates?: Record<string, number>;
  hsdRate?: number;
  msIRate?: number;
  msIIRate?: number;
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

export function getDefaultCollectionTypes(): DynamicCollectionType[] {
  return [
    {
      code: 'PHONEPE',
      displayName: 'PhonePe',
      category: 'Online',
      hasTidBatch: true,
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
      displayOrder: 3,
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
      otpMachineTrackingEnabled: false,
      splitMorningNightEnabled: false,
      personalLedgerEnabled: true,
      personalLedgerName: 'Personal Ledger',
    },
    collectionTypes: getDefaultCollectionTypes(),
    isDynamic: false,
    fuelRates: {},
    hsdRate: 90.35,
    msIRate: 103.81,
    msIIRate: 103.81,
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
    let { data, error } = await supabase
      .from('Settings')
      .select('*')
      .eq('station_id', cleanId)
      .limit(1)
      .maybeSingle();

    if (!data && !error) {
      const res = await supabase
        .from('Settings')
        .select('*')
        .eq('StationId', cleanId)
        .limit(1)
        .maybeSingle();
      data = res.data;
      error = res.error;
    }

    if (error || !data) {
      console.warn('Could not fetch station settings from Supabase, checking cache:', error);
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

    const collectionTypesRaw = data.CollectionTypesJson ?? data.collection_types_json ?? data.collectiontypesjson;
    const appFeatureSettingsRaw = data.AppFeatureSettingsJson ?? data.app_feature_settings_json ?? data.appfeaturesettingsjson;
    const fuelRatesRaw = data.FuelRatesJson ?? data.fuel_rates_json ?? data.fuelratesjson;
    const rawHsd = data.HsdRate ?? data.hsd_rate ?? data.hsdrate;
    const rawMsI = data.MsIRate ?? data.ms_i_rate ?? data.ms_irate ?? data.msirate;
    const rawMsII = data.MsIIRate ?? data.ms_ii_rate ?? data.ms_iirate ?? data.msiirate;
    const rawCng = data.CngRate ?? data.cng_rate ?? data.cngrate;

    let collectionTypes: DynamicCollectionType[] = [];
    if (collectionTypesRaw) {
      try {
        const parsed = typeof collectionTypesRaw === 'string' ? JSON.parse(collectionTypesRaw) : collectionTypesRaw;
        if (Array.isArray(parsed) && parsed.length > 0) {
          collectionTypes = parsed
            .map((item: any) => ({
              code: (item.Code || item.code || '').trim().toUpperCase(),
              displayName: item.DisplayName || item.displayName || item.Code || '',
              category: item.Category || item.category || 'Online',
              hasTidBatch: Boolean(item.HasTidBatch ?? item.hasTidBatch),
              displayOrder: Number(item.DisplayOrder ?? item.displayOrder ?? 0),
              isActive: item.IsActive !== false && item.isActive !== false,
              enableMorning: item.EnableMorning !== false && item.enableMorning !== false,
              enableDay: item.EnableDay !== false && item.enableDay !== false,
              enableNight: item.EnableNight !== false && item.enableNight !== false,
            }))
            .filter((c: DynamicCollectionType) => c.isActive && c.code)
            .sort((a: DynamicCollectionType, b: DynamicCollectionType) => a.displayOrder - b.displayOrder);
        }
      } catch (err) {
        console.error('Failed to parse CollectionTypesJson:', err);
      }
    }

    let cashDenominationsEnabled = false;
    let otpMachineTrackingEnabled = false;
    let splitMorningNightEnabled = false;
    let personalLedgerEnabled = true;
    let personalLedgerName = 'Personal Ledger';

    if (appFeatureSettingsRaw) {
      try {
        const feats = typeof appFeatureSettingsRaw === 'string' ? JSON.parse(appFeatureSettingsRaw) : appFeatureSettingsRaw;
        if (Array.isArray(feats)) {
          for (const f of feats) {
            const key = f.FeatureKey || f.featureKey;
            const enabled = f.IsEnabled ?? f.isEnabled;
            const displayName = (f.DisplayName || f.displayName || '').trim();
            if (key === 'Operations_CashDenominations') cashDenominationsEnabled = Boolean(enabled);
            if (key === 'Operations_OtpMachineTracking') otpMachineTrackingEnabled = Boolean(enabled);
            if (key === 'Collection_UseMorningNight') splitMorningNightEnabled = Boolean(enabled);
            if (key === 'Operations_PersonalLedger') {
              personalLedgerEnabled = enabled !== false;
              if (displayName) {
                personalLedgerName = displayName;
              }
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
            if (!isNaN(num) && num > 0) fuelRates[k] = num;
          }
        }
      } catch (err) {
        console.error('Failed to parse FuelRatesJson:', err);
      }
    }

    // Authoritative rate resolution: dynamic fuelRates map takes precedence over scalar columns, which take precedence over hardcoded defaults
    const hsdRate = (fuelRates['HSD'] || fuelRates['HSD - 20KL']) ?? (rawHsd && Number(rawHsd) > 0 ? Number(rawHsd) : 90.35);
    const msIRate = (fuelRates['MS-I'] || fuelRates['MS'] || fuelRates['MS - 20KL']) ?? (rawMsI && Number(rawMsI) > 0 ? Number(rawMsI) : 103.81);
    const msIIRate = (fuelRates['MS-II'] || fuelRates['HSD - 20KL II']) ?? (rawMsII && Number(rawMsII) > 0 ? Number(rawMsII) : msIRate);
    const cngRate = (fuelRates['CNG']) ?? (rawCng && Number(rawCng) > 0 ? Number(rawCng) : 85.0);

    const config: StationConfig = {
      stationId: cleanId,
      features: {
        cashDenominationsEnabled,
        otpMachineTrackingEnabled,
        splitMorningNightEnabled,
        personalLedgerEnabled,
        personalLedgerName,
      },
      collectionTypes: collectionTypes.length > 0 ? collectionTypes : getDefaultCollectionTypes(),
      isDynamic: collectionTypes.length > 0,
      fuelRates,
      hsdRate,
      msIRate,
      msIIRate,
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
  for (const [k, v] of Object.entries(ratesMap)) {
    const key = k.trim().toUpperCase();
    const val = Number(v);
    if (val <= 0) continue;
    if (normTank && key === normTank.toUpperCase()) return val;
    if (normFuel && key === normFuel.toUpperCase()) return val;
  }

  // 3. Keyword-based matching
  const combined = `${normFuel} ${normTank}`.toUpperCase();

  if (combined.includes('SPEED') || combined.includes('XP95') || combined.includes('POWER') || combined.includes('TURBO') || combined.includes('EXTRA')) {
    for (const [k, v] of Object.entries(ratesMap)) {
      const key = k.toUpperCase();
      const val = Number(v);
      if (val > 0 && (key.includes('SPEED') || key.includes('XP95') || key.includes('POWER') || key.includes('EXTRA'))) return val;
    }
  }

  if (combined.includes('HSD') || combined.includes('DIESEL')) {
    for (const [k, v] of Object.entries(ratesMap)) {
      const key = k.toUpperCase();
      const val = Number(v);
      if (val > 0 && (key.includes('HSD') || key.includes('DIESEL')) && !key.includes('II')) return val;
    }
    if (config.hsdRate && config.hsdRate > 0) return config.hsdRate;
  }

  if (combined.includes('MS-II') || combined.includes('MS2') || combined.includes('MS_II') || combined.includes('20KL II')) {
    for (const [k, v] of Object.entries(ratesMap)) {
      const key = k.toUpperCase();
      const val = Number(v);
      if (val > 0 && (key.includes('MS-II') || key.includes('MS2') || key.includes('20KL II'))) return val;
    }
    if (config.msIIRate && config.msIIRate > 0) return config.msIIRate;
  }

  if (combined.includes('MS-I') || combined.includes('MS') || combined.includes('PETROL')) {
    for (const [k, v] of Object.entries(ratesMap)) {
      const key = k.toUpperCase();
      const val = Number(v);
      if (val > 0 && (key.includes('MS-I') || key === 'MS' || key.includes('PETROL') || key.includes('MS -'))) return val;
    }
    if (config.msIRate && config.msIRate > 0) return config.msIRate;
  }

  if (combined.includes('CNG')) {
    for (const [k, v] of Object.entries(ratesMap)) {
      const key = k.toUpperCase();
      const val = Number(v);
      if (val > 0 && key.includes('CNG')) return val;
    }
    if (config.cngRate && config.cngRate > 0) return config.cngRate;
  }

  // 4. Fallbacks to standard rates
  if (normFuel.toUpperCase().includes('HSD')) return config.hsdRate || 90.35;
  if (normFuel.toUpperCase().includes('CNG')) return config.cngRate || 85.0;
  if (normFuel.toUpperCase().includes('MS-II')) return config.msIIRate || 103.81;
  return config.msIRate || 103.81;
}
