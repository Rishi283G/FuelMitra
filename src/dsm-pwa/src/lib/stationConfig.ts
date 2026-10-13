import { supabase } from './supabase';

export interface StationFeatureSettings {
  cashDenominationsEnabled: boolean;
  otpMachineTrackingEnabled: boolean;
  splitMorningNightEnabled: boolean;
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
    },
    collectionTypes: getDefaultCollectionTypes(),
    isDynamic: false,
  };
}

export async function fetchStationConfig(stationId: string): Promise<StationConfig> {
  const cleanId = (stationId || '').trim();
  if (!cleanId) {
    return getDefaultStationConfig(cleanId);
  }

  // Check localStorage cache for offline/instant load
  const cacheKey = `station_config_${cleanId.toLowerCase()}`;
  try {
    const cached = localStorage.getItem(cacheKey);
    if (cached) {
      const parsed = JSON.parse(cached);
      if (parsed && parsed.stationId === cleanId) {
        // Return cached and refresh in background
        refreshStationConfigInBackground(cleanId, cacheKey);
        return parsed;
      }
    }
  } catch {}

  return await fetchAndCacheStationConfig(cleanId, cacheKey);
}

async function refreshStationConfigInBackground(cleanId: string, cacheKey: string) {
  try {
    await fetchAndCacheStationConfig(cleanId, cacheKey);
  } catch {}
}

async function fetchAndCacheStationConfig(cleanId: string, cacheKey: string): Promise<StationConfig> {
  try {
    let { data, error } = await supabase
      .from('Settings')
      .select('CollectionTypesJson, AppFeatureSettingsJson, PumpMappingsJson')
      .eq('station_id', cleanId)
      .limit(1)
      .maybeSingle();

    if (!data && !error) {
      const res = await supabase
        .from('Settings')
        .select('CollectionTypesJson, AppFeatureSettingsJson, PumpMappingsJson')
        .eq('StationId', cleanId)
        .limit(1)
        .maybeSingle();
      data = res.data;
      error = res.error;
    }

    if (error || !data) {
      console.warn('Could not fetch station settings from Supabase, using defaults:', error);
      return getDefaultStationConfig(cleanId);
    }

    let collectionTypes: DynamicCollectionType[] = [];
    if (data.CollectionTypesJson) {
      try {
        const parsed = JSON.parse(data.CollectionTypesJson);
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

    if (data.AppFeatureSettingsJson) {
      try {
        const feats = JSON.parse(data.AppFeatureSettingsJson);
        if (Array.isArray(feats)) {
          for (const f of feats) {
            const key = f.FeatureKey || f.featureKey;
            const enabled = f.IsEnabled ?? f.isEnabled;
            if (key === 'Operations_CashDenominations') cashDenominationsEnabled = Boolean(enabled);
            if (key === 'Operations_OtpMachineTracking') otpMachineTrackingEnabled = Boolean(enabled);
            if (key === 'Collection_UseMorningNight') splitMorningNightEnabled = Boolean(enabled);
          }
        }
      } catch (err) {
        console.error('Failed to parse AppFeatureSettingsJson:', err);
      }
    }

    const config: StationConfig = {
      stationId: cleanId,
      features: {
        cashDenominationsEnabled,
        otpMachineTrackingEnabled,
        splitMorningNightEnabled,
      },
      collectionTypes: collectionTypes.length > 0 ? collectionTypes : getDefaultCollectionTypes(),
      isDynamic: collectionTypes.length > 0,
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
