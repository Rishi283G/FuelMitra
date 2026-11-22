import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { resetStationData } from './stationReset';
import { db } from './db';
import { supabase } from './supabase';

// Mock the db dependency
vi.mock('./db', () => {
  const mockTable = () => ({
    clear: vi.fn().mockResolvedValue(undefined),
    put: vi.fn().mockResolvedValue(undefined),
    toArray: vi.fn().mockResolvedValue([]),
  });
  return {
    db: {
      drafts: mockTable(),
      submissions: mockTable(),
      products: mockTable(),
      stockBalances: mockTable(),
      transaction: vi.fn().mockImplementation(async (_mode, _t1, _t2, callback) => {
        await callback();
      }),
    },
  };
});

// Mock the supabase dependency
vi.mock('./supabase', () => {
  const mockFrom = vi.fn().mockImplementation((table) => {
    if (table === 'ProductMasters') {
      const builder: any = {
        select: vi.fn().mockReturnThis(),
        eq: vi.fn().mockImplementation(() => builder),
        order: vi.fn().mockImplementation(() => builder),
        then: (resolve: any) =>
          resolve({
            data: [
              { local_id: 1, Id: 1, ProductName: 'Petrol MS', Category: 'Oil', Unit: 'Litres', DefaultSaleRate: 103.8 },
              { local_id: 2, Id: 2, ProductName: 'Diesel HSD', Category: 'Oil', Unit: 'Litres', DefaultSaleRate: 90.3 }
            ],
            error: null
          })
      };
      return builder;
    }
    if (table === 'OilDefDailyLogs') {
      const builder: any = {
        select: vi.fn().mockReturnThis(),
        eq: vi.fn().mockImplementation(() => builder),
        order: vi.fn().mockImplementation(() => builder),
        then: (resolve: any) =>
          resolve({
            data: [
              { ProductId: 1, RemainingStock: 5000, LogDate: '2026-07-12' },
              { ProductId: 2, RemainingStock: 8000, LogDate: '2026-07-12' }
            ],
            error: null
          })
      };
      return builder;
    }
    const defaultBuilder: any = {
      select: vi.fn().mockReturnThis(),
      eq: vi.fn().mockImplementation(() => defaultBuilder),
      order: vi.fn().mockImplementation(() => defaultBuilder),
      then: (resolve: any) => resolve({ data: [], error: null })
    };
    return defaultBuilder;
  });

  return {
    supabase: {
      from: mockFrom
    }
  };
});

describe('Station Identity Isolation Audit Tests', () => {
  let localStorageMock: Record<string, string> = {};
  let cachesMock: { keys: any; delete: any } = {
    keys: vi.fn(),
    delete: vi.fn()
  };

  beforeEach(() => {
    localStorageMock = {};

    // Mock global localStorage
    vi.stubGlobal('localStorage', {
      getItem: vi.fn((key) => localStorageMock[key] || null),
      setItem: vi.fn((key, val) => { localStorageMock[key] = String(val); }),
      removeItem: vi.fn((key) => { delete localStorageMock[key]; }),
      clear: vi.fn(() => { localStorageMock = {}; }),
    });

    // Mock global sessionStorage
    vi.stubGlobal('sessionStorage', {
      clear: vi.fn(),
    });

    // Mock global caches
    cachesMock = {
      keys: vi.fn().mockResolvedValue(['cache-v1', 'cache-v2']),
      delete: vi.fn().mockResolvedValue(true)
    };
    vi.stubGlobal('caches', cachesMock);
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  it('Scenario 1: First login to a new station (initial sync)', async () => {
    // GIVEN: Fresh client install, current_station_id is empty
    expect(localStorage.getItem('current_station_id')).toBeNull();

    // WHEN: Logging in and triggering reset/sync for Station A
    await resetStationData('STATION_A');

    // THEN: Offline drafts and submissions are preserved (not cleared)
    expect(db.drafts.clear).not.toHaveBeenCalled();
    expect(db.submissions.clear).not.toHaveBeenCalled();
    expect(db.products.clear).toHaveBeenCalled();
    expect(db.stockBalances.clear).toHaveBeenCalled();

    // AND: New station products and stock levels are synced
    expect(supabase.from).toHaveBeenCalledWith('ProductMasters');
    expect(supabase.from).toHaveBeenCalledWith('OilDefDailyLogs');
    expect(db.products.put).toHaveBeenCalledWith(expect.objectContaining({ id: 1, productName: 'Petrol MS' }));
    expect(db.stockBalances.put).toHaveBeenCalledWith(expect.objectContaining({ productId: 1, remainingStock: 5000 }));

    // AND: Station ID is saved locally
    expect(localStorage.setItem).toHaveBeenCalledWith('current_station_id', 'STATION_A');
    expect(localStorage.getItem('current_station_id')).toBe('STATION_A');
  });

  it('Scenario 2: Logging into a different station after cached data exists', async () => {
    // GIVEN: App is already initialized for STATION_A, and has device ID and creditors cache
    localStorageMock['current_station_id'] = 'STATION_A';
    localStorageMock['dsm_device_id'] = 'DEVICE_UUID';
    localStorageMock['cached_creditors'] = JSON.stringify([{ id: 1, name: 'Alice' }]);

    // AND: SW caches have a static shell cache and a dynamic data cache
    cachesMock.keys = vi.fn().mockResolvedValue(['pyrosync-dsm-v1.4', 'dynamic-station-data']);

    // WHEN: Switching to STATION_B
    await resetStationData('STATION_B');

    // THEN: IndexedDB drafts and submissions are preserved to prevent offline work loss
    expect(db.drafts.clear).not.toHaveBeenCalled();
    expect(db.submissions.clear).not.toHaveBeenCalled();
    expect(db.products.clear).toHaveBeenCalled();
    expect(db.stockBalances.clear).toHaveBeenCalled();

    // AND: Device ID is preserved, but station-specific cached creditors are deleted
    expect(localStorageMock['dsm_device_id']).toBe('DEVICE_UUID');
    expect(localStorageMock['cached_creditors']).toBeUndefined();

    // AND: Static app shell cache is preserved while non-shell cache is deleted
    expect(sessionStorage.clear).toHaveBeenCalled();
    expect(cachesMock.keys).toHaveBeenCalled();
    expect(cachesMock.delete).not.toHaveBeenCalledWith('pyrosync-dsm-v1.4');
    expect(cachesMock.delete).toHaveBeenCalledWith('dynamic-station-data');

    // AND: Sync imports the new station data and persists STATION_B
    expect(localStorageMock['current_station_id']).toBe('STATION_B');
    expect(db.products.put).toHaveBeenCalled();
    expect(db.stockBalances.put).toHaveBeenCalled();
  });
});
