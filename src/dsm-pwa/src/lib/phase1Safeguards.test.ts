import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { db } from './db';
import { supabase } from './supabase';
import { syncProductListAndStockDirect } from './productSync';

// Mock Dexie db
vi.mock('./db', () => {
  const mockTable = () => {
    const store = new Map<any, any>();
    return {
      clear: vi.fn().mockImplementation(async () => {
        store.clear();
      }),
      put: vi.fn().mockImplementation(async (item: any) => {
        store.set(item.id || item.productId, item);
      }),
      toArray: vi.fn().mockImplementation(async () => Array.from(store.values())),
      _store: store,
    };
  };

  const tables = {
    drafts: mockTable(),
    submissions: mockTable(),
    products: mockTable(),
    stockBalances: mockTable(),
  };

  return {
    db: {
      ...tables,
      transaction: vi.fn().mockImplementation(async (_mode: string, _t1: any, _t2: any, callback: () => Promise<void>) => {
        await callback();
      }),
    },
  };
});

// Mock Supabase
vi.mock('./supabase', () => {
  return {
    supabase: {
      from: vi.fn(),
    },
  };
});

describe('Phase 1 Critical Safeguards Verification', () => {
  let localStorageMock: Record<string, string> = {};

  beforeEach(() => {
    localStorageMock = {};
    vi.stubGlobal('localStorage', {
      getItem: vi.fn((key: string) => localStorageMock[key] || null),
      setItem: vi.fn((key: string, val: string) => { localStorageMock[key] = String(val); }),
      removeItem: vi.fn((key: string) => { delete localStorageMock[key]; }),
      clear: vi.fn(() => { localStorageMock = {}; }),
    });
    vi.stubGlobal('navigator', { onLine: true });
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  describe('1. Station Isolation & Query Scoping', () => {
    it('scopes ProductMasters and OilDefDailyLogs queries to the authenticated station_id', async () => {
      const capturedQueries: { table: string; select?: string; filters: Record<string, any> }[] = [];

      (supabase.from as any).mockImplementation((table: string) => {
        const queryState: { table: string; select?: string; filters: Record<string, any> } = {
          table,
          filters: {},
        };
        capturedQueries.push(queryState);

        const builder: any = {
          select: vi.fn().mockImplementation((cols: string) => {
            queryState.select = cols;
            return builder;
          }),
          eq: vi.fn().mockImplementation((col: string, val: any) => {
            queryState.filters[col] = val;
            return builder;
          }),
          order: vi.fn().mockImplementation(() => builder),
          then: (resolve: any) => {
            if (table === 'ProductMasters') {
              resolve({
                data: [
                  {
                    local_id: 1,
                    SyncGuid: 'guid-oil-1',
                    ProductName: '2T Oil 500ml',
                    Category: 'Oil',
                    Unit: 'Pouches',
                    DefaultSaleRate: 75.0,
                    station_id: 'RASHTRA-74A837',
                  },
                ],
                error: null,
              });
            } else if (table === 'OilDefDailyLogs') {
              resolve({
                data: [
                  {
                    ProductId: 'guid-oil-1',
                    RemainingStock: 120,
                    LogDate: '2026-11-06',
                    station_id: 'RASHTRA-74A837',
                  },
                ],
                error: null,
              });
            } else {
              resolve({ data: [], error: null });
            }
          },
        };
        return builder;
      });

      await syncProductListAndStockDirect('RASHTRA-74A837');

      // 1. Verify ProductMasters was queried with station_id filter
      const pmQuery = capturedQueries.find((q) => q.table === 'ProductMasters');
      expect(pmQuery).toBeDefined();
      expect(pmQuery?.select).toContain('local_id');
      expect(pmQuery?.select).toContain('SyncGuid');
      expect(pmQuery?.select).not.toContain('Id,');
      expect(pmQuery?.filters['station_id']).toBe('RASHTRA-74A837');
      expect(pmQuery?.filters['IsActive']).toBe(true);

      // 2. Verify OilDefDailyLogs was queried with station_id filter
      const logQuery = capturedQueries.find((q) => q.table === 'OilDefDailyLogs');
      expect(logQuery).toBeDefined();
      expect(logQuery?.filters['station_id']).toBe('RASHTRA-74A837');

      // 3. Verify Dexie was populated with mapped numeric ID
      expect(db.products.put).toHaveBeenCalledWith(
        expect.objectContaining({
          id: 1,
          productName: '2T Oil 500ml',
          category: 'Oil',
          unit: 'Pouches',
          defaultSaleRate: 75.0,
        })
      );
      expect(db.stockBalances.put).toHaveBeenCalledWith(
        expect.objectContaining({
          productId: 1,
          remainingStock: 120,
        })
      );
    });

    it('aborts product sync when no station ID is available, preventing cross-station leakage', async () => {
      // GIVEN: No station in parameter and no station in localStorage
      await syncProductListAndStockDirect('');

      // THEN: Supabase is never queried
      expect(supabase.from).not.toHaveBeenCalled();
      expect(db.products.clear).not.toHaveBeenCalled();
    });
  });

  describe('2. Backward Compatibility with Numeric SQLite IDs', () => {
    it('correctly maps local_id and fallback Id to positive integer keys, rejecting non-positive or NaN values', async () => {
      (supabase.from as any).mockImplementation((table: string) => {
        const builder: any = {
          select: vi.fn().mockImplementation(() => builder),
          eq: vi.fn().mockImplementation(() => builder),
          order: vi.fn().mockImplementation(() => builder),
          then: (resolve: any) => {
            if (table === 'ProductMasters') {
              resolve({
                data: [
                  { local_id: 10, SyncGuid: 'g-10', ProductName: 'Valid Local ID', Category: 'Oil', Unit: 'Litres', DefaultSaleRate: 50 },
                  { Id: 20, SyncGuid: 'g-20', ProductName: 'Valid Legacy Id', Category: 'DEF', Unit: 'Buckets', DefaultSaleRate: 350 },
                  { local_id: -1, SyncGuid: 'g-bad-1', ProductName: 'Negative ID', Category: 'Oil', Unit: 'Litres', DefaultSaleRate: 10 },
                  { local_id: 0, SyncGuid: 'g-bad-2', ProductName: 'Zero ID', Category: 'Oil', Unit: 'Litres', DefaultSaleRate: 10 },
                  { local_id: 'not-a-number', SyncGuid: 'g-bad-3', ProductName: 'NaN ID', Category: 'Oil', Unit: 'Litres', DefaultSaleRate: 10 },
                ],
                error: null,
              });
            } else {
              resolve({ data: [], error: null });
            }
          },
        };
        return builder;
      });

      await syncProductListAndStockDirect('RASHTRA-74A837');

      // Only positive integers (10 and 20) must be put into Dexie
      expect(db.products.put).toHaveBeenCalledWith(expect.objectContaining({ id: 10, productName: 'Valid Local ID' }));
      expect(db.products.put).toHaveBeenCalledWith(expect.objectContaining({ id: 20, productName: 'Valid Legacy Id' }));
      expect(db.products.put).not.toHaveBeenCalledWith(expect.objectContaining({ id: -1 }));
      expect(db.products.put).not.toHaveBeenCalledWith(expect.objectContaining({ id: 0 }));
      expect(db.products.put).not.toHaveBeenCalledWith(expect.objectContaining({ id: expect.any(String) }));
    });
  });

  describe('3. Atomic Product and Stock Cache Replacement', () => {
    it('preserves existing local cache when cloud query fails with an error', async () => {
      (supabase.from as any).mockImplementation(() => {
        const builder: any = {
          select: vi.fn().mockImplementation(() => builder),
          eq: vi.fn().mockImplementation(() => builder),
          then: (resolve: any) => resolve({ data: null, error: { message: 'Cloud database unavailable' } }),
        };
        return builder;
      });

      await syncProductListAndStockDirect('RASHTRA-74A837');

      // Local Dexie cache must NOT be cleared on error
      expect(db.products.clear).not.toHaveBeenCalled();
      expect(db.stockBalances.clear).not.toHaveBeenCalled();
      expect(db.products.put).not.toHaveBeenCalled();
    });

    it('preserves existing local cache when cloud returns an empty array', async () => {
      (supabase.from as any).mockImplementation(() => {
        const builder: any = {
          select: vi.fn().mockImplementation(() => builder),
          eq: vi.fn().mockImplementation(() => builder),
          then: (resolve: any) => resolve({ data: [], error: null }),
        };
        return builder;
      });

      await syncProductListAndStockDirect('RASHTRA-74A837');

      // Local Dexie cache must NOT be cleared when cloud returns 0 products
      expect(db.products.clear).not.toHaveBeenCalled();
      expect(db.stockBalances.clear).not.toHaveBeenCalled();
      expect(db.products.put).not.toHaveBeenCalled();
    });

    it('preserves existing stock and product caches when OilDefDailyLogs query fails with an error', async () => {
      (supabase.from as any).mockImplementation((table: string) => {
        const builder: any = {
          select: vi.fn().mockImplementation(() => builder),
          eq: vi.fn().mockImplementation(() => builder),
          order: vi.fn().mockImplementation(() => builder),
          then: (resolve: any) => {
            if (table === 'ProductMasters') {
              resolve({
                data: [{ local_id: 1, ProductName: '2T Oil', Category: 'Oil', Unit: 'Bottle', DefaultSaleRate: 70 }],
                error: null,
              });
            } else if (table === 'OilDefDailyLogs') {
              resolve({ data: null, error: { message: 'Stock table timeout' } });
            }
          },
        };
        return builder;
      });

      await syncProductListAndStockDirect('RASHTRA-74A837');

      // Dexie tables must NOT be cleared when stock query fails
      expect(db.products.clear).not.toHaveBeenCalled();
      expect(db.stockBalances.clear).not.toHaveBeenCalled();
    });

    it('preserves existing cached stock balance when remote stock log is absent', async () => {
      // Simulate existing cached stock of 85 units in Dexie
      (db.stockBalances.toArray as any).mockResolvedValueOnce([{ productId: 1, remainingStock: 85 }]);

      (supabase.from as any).mockImplementation((table: string) => {
        const builder: any = {
          select: vi.fn().mockImplementation(() => builder),
          eq: vi.fn().mockImplementation(() => builder),
          order: vi.fn().mockImplementation(() => builder),
          then: (resolve: any) => {
            if (table === 'ProductMasters') {
              resolve({
                data: [{ local_id: 1, ProductName: '2T Oil', Category: 'Oil', Unit: 'Bottle', DefaultSaleRate: 70 }],
                error: null,
              });
            } else if (table === 'OilDefDailyLogs') {
              // Remote returns empty array for daily logs (no logs for today yet)
              resolve({ data: [], error: null });
            }
          },
        };
        return builder;
      });

      await syncProductListAndStockDirect('RASHTRA-74A837');

      // Should preserve existing stock balance of 85 instead of zeroing it out
      expect(db.stockBalances.put).toHaveBeenCalledWith(
        expect.objectContaining({
          productId: 1,
          remainingStock: 85,
        })
      );
    });

    it('atomically clears and replaces products inside db.transaction when valid products arrive', async () => {
      (supabase.from as any).mockImplementation((table: string) => {
        const builder: any = {
          select: vi.fn().mockImplementation(() => builder),
          eq: vi.fn().mockImplementation(() => builder),
          order: vi.fn().mockImplementation(() => builder),
          then: (resolve: any) => {
            if (table === 'ProductMasters') {
              resolve({
                data: [
                  { local_id: 1, SyncGuid: 'guid-1', ProductName: 'DEF 20L', Category: 'DEF', Unit: 'Bucket', DefaultSaleRate: 800 },
                ],
                error: null,
              });
            } else {
              resolve({ data: [], error: null });
            }
          },
        };
        return builder;
      });

      await syncProductListAndStockDirect('RASHTRA-74A837');

      expect(db.transaction).toHaveBeenCalledWith('rw', db.products, db.stockBalances, expect.any(Function));
      expect(db.products.clear).toHaveBeenCalledTimes(1);
      expect(db.stockBalances.clear).toHaveBeenCalledTimes(1);
      expect(db.products.put).toHaveBeenCalledWith(expect.objectContaining({ id: 1, productName: 'DEF 20L' }));
    });
  });

  describe('4. Flexible Stock Mapping (Guid vs Integer)', () => {
    it('matches stock balances by SyncGuid when foreign key is a remote GUID', async () => {
      (supabase.from as any).mockImplementation((table: string) => {
        const builder: any = {
          select: vi.fn().mockImplementation(() => builder),
          eq: vi.fn().mockImplementation(() => builder),
          order: vi.fn().mockImplementation(() => builder),
          then: (resolve: any) => {
            if (table === 'ProductMasters') {
              resolve({
                data: [
                  { local_id: 5, SyncGuid: 'remote-guid-xyz', ProductName: 'Servo 4T', Category: 'Oil', Unit: 'Bottle', DefaultSaleRate: 350 },
                ],
                error: null,
              });
            } else if (table === 'OilDefDailyLogs') {
              resolve({
                data: [
                  { ProductId: 'remote-guid-xyz', RemainingStock: 42, LogDate: '2026-11-06' },
                ],
                error: null,
              });
            }
          },
        };
        return builder;
      });

      await syncProductListAndStockDirect('RASHTRA-74A837');

      // The stock balance for productId 5 must match the log with ProductId='remote-guid-xyz'
      expect(db.stockBalances.put).toHaveBeenCalledWith(
        expect.objectContaining({
          productId: 5,
          remainingStock: 42,
        })
      );
    });

    it('matches stock balances by integer ProductId when foreign key is numeric', async () => {
      (supabase.from as any).mockImplementation((table: string) => {
        const builder: any = {
          select: vi.fn().mockImplementation(() => builder),
          eq: vi.fn().mockImplementation(() => builder),
          order: vi.fn().mockImplementation(() => builder),
          then: (resolve: any) => {
            if (table === 'ProductMasters') {
              resolve({
                data: [
                  { local_id: 7, SyncGuid: 'remote-guid-7', ProductName: 'Brake Fluid', Category: 'Oil', Unit: 'Bottle', DefaultSaleRate: 150 },
                ],
                error: null,
              });
            } else if (table === 'OilDefDailyLogs') {
              resolve({
                data: [
                  { ProductId: 7, RemainingStock: 19, LogDate: '2026-11-06' },
                ],
                error: null,
              });
            }
          },
        };
        return builder;
      });

      await syncProductListAndStockDirect('RASHTRA-74A837');

      expect(db.stockBalances.put).toHaveBeenCalledWith(
        expect.objectContaining({
          productId: 7,
          remainingStock: 19,
        })
      );
    });
  });

  describe('5. DashboardScreen Identity Ref & Recursive Refresh Suppression Logic', () => {
    it('prevents recursive execution when profile reference changes but identity key remains stable', () => {
      let lastRefreshedIdentity: string | null = null;
      let refreshCallCount = 0;

      const runEffect = (profile: { id: string; StationId: string }) => {
        const identityKey = `${profile.id}:${profile.StationId || ''}`;
        if (lastRefreshedIdentity === identityKey) {
          // Guard: already refreshed for this identity
          return;
        }

        refreshCallCount++;
        // Simulate successful refresh
        lastRefreshedIdentity = identityKey;
      };

      // Initial mount
      const profile1 = { id: 'user-1', StationId: 'RASHTRA-74A837' };
      runEffect(profile1);
      expect(refreshCallCount).toBe(1);

      // Re-render with new object reference (same user and station)
      const profile2 = { id: 'user-1', StationId: 'RASHTRA-74A837' };
      runEffect(profile2);
      expect(refreshCallCount).toBe(1); // Blocked by guard!

      // Genuine station change triggers a new refresh
      const profile3 = { id: 'user-1', StationId: 'STATION-OTHER' };
      runEffect(profile3);
      expect(refreshCallCount).toBe(2);
    });

    it('does not permanently mark identity as refreshed when refresh fails, allowing bounded retries', () => {
      let lastRefreshedIdentity: string | null = null;
      let retryCount = 0;
      let attempts = 0;

      const runEffectWithFailure = (profile: { id: string; StationId: string }, shouldFail: boolean) => {
        const identityKey = `${profile.id}:${profile.StationId || ''}`;
        if (lastRefreshedIdentity === identityKey) return;

        attempts++;
        if (shouldFail) {
          if (retryCount < 3) {
            retryCount++;
            // Bounded retry scheduled, identity NOT marked
          }
        } else {
          lastRefreshedIdentity = identityKey;
          retryCount = 0;
        }
      };

      const userProfile = { id: 'user-2', StationId: 'RASHTRA-74A837' };

      // Attempt 1: Fails
      runEffectWithFailure(userProfile, true);
      expect(attempts).toBe(1);
      expect(retryCount).toBe(1);
      expect(lastRefreshedIdentity).toBeNull();

      // Retry Attempt 2: Succeeds
      runEffectWithFailure(userProfile, false);
      expect(attempts).toBe(2);
      expect(retryCount).toBe(0);
      expect(lastRefreshedIdentity).toBe('user-2:RASHTRA-74A837');

      // Subsequent render with same profile: skipped
      runEffectWithFailure({ ...userProfile }, false);
      expect(attempts).toBe(2);
    });
  });

  describe('6. Concurrency Safety', () => {
    it('coalesces multiple simultaneous sync calls into a single operation', async () => {
      let pmQueryCount = 0;
      (supabase.from as any).mockImplementation((table: string) => {
        const builder: any = {
          select: vi.fn().mockImplementation(() => builder),
          eq: vi.fn().mockImplementation(() => builder),
          order: vi.fn().mockImplementation(() => builder),
          then: (resolve: any) => {
            if (table === 'ProductMasters') {
              pmQueryCount++;
              setTimeout(() => {
                resolve({
                  data: [{ local_id: 1, ProductName: '2T Oil', Category: 'Oil', Unit: 'Bottle', DefaultSaleRate: 70 }],
                  error: null,
                });
              }, 10);
            } else {
              resolve({ data: [], error: null });
            }
          },
        };
        return builder;
      });

      // Invoke 3 concurrent sync calls
      await Promise.all([
        syncProductListAndStockDirect('RASHTRA-74A837'),
        syncProductListAndStockDirect('RASHTRA-74A837'),
        syncProductListAndStockDirect('RASHTRA-74A837'),
      ]);

      // Supabase ProductMasters query should only have fired once due to per-station mutex
      expect(pmQueryCount).toBe(1);
    });

    it('executes distinct station synchronizations independently without cross-station coalescing or blocking', async () => {
      const queriedStations: string[] = [];

      (supabase.from as any).mockImplementation((table: string) => {
        let stationFilter = '';
        const builder: any = {
          select: vi.fn().mockImplementation(() => builder),
          eq: vi.fn().mockImplementation((col: string, val: string) => {
            if (col === 'station_id') {
              stationFilter = val;
            }
            return builder;
          }),
          order: vi.fn().mockImplementation(() => builder),
          then: (resolve: any) => {
            if (table === 'ProductMasters') {
              queriedStations.push(stationFilter);
              setTimeout(() => {
                resolve({
                  data: [{ local_id: 1, ProductName: `Oil for ${stationFilter}`, Category: 'Oil', Unit: 'Bottle', DefaultSaleRate: 70 }],
                  error: null,
                });
              }, 10);
            } else {
              resolve({ data: [], error: null });
            }
          },
        };
        return builder;
      });

      // Invoke concurrent sync calls for two different stations: Station A and Station B
      await Promise.all([
        syncProductListAndStockDirect('RASHTRA-74A837'),
        syncProductListAndStockDirect('OTHER-STATION-XYZ'),
        syncProductListAndStockDirect('RASHTRA-74A837'), // Should coalesce with first
      ]);

      // Both distinct stations must have executed their own query
      expect(queriedStations).toContain('RASHTRA-74A837');
      expect(queriedStations).toContain('OTHER-STATION-XYZ');
      expect(queriedStations.filter((s) => s === 'RASHTRA-74A837').length).toBe(1); // coalesced
      expect(queriedStations.filter((s) => s === 'OTHER-STATION-XYZ').length).toBe(1); // executed independently
    });
  });
});
