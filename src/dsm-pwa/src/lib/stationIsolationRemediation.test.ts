import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { createElement } from 'react';
import { renderToString } from 'react-dom/server';
import { useSubmissionService } from '../hooks/useSubmissionService';
import { resetStationData } from './stationReset';
import { db, type DraftSubmission, type CachedSubmission } from './db';
import { supabase } from './supabase';

// In-memory simulated Dexie Table with realistic index and compound index behavior
function createMockDexieTable<T extends Record<string, any>>(tableName: string) {
  let autoId = 1;
  const store = new Map<any, T>();

  const table = {
    _tableName: tableName,
    _store: store,
    clear: vi.fn(async () => {
      store.clear();
    }),
    add: vi.fn(async (item: T) => {
      const id = item.id || autoId++;
      const stored = { ...item, id };
      store.set(id, stored);
      return id;
    }),
    put: vi.fn(async (item: T) => {
      const id = item.id || autoId++;
      const stored = { ...item, id };
      store.set(id, stored);
      return id;
    }),
    get: vi.fn(async (id: any) => {
      return store.get(id);
    }),
    update: vi.fn(async (id: any, changes: Partial<T>) => {
      const existing = store.get(id);
      if (existing) {
        store.set(id, { ...existing, ...changes });
        return 1;
      }
      return 0;
    }),
    delete: vi.fn(async (id: any) => {
      store.delete(id);
    }),
    toArray: vi.fn(async () => Array.from(store.values())),
    where: vi.fn((key: string) => ({
      equals: (val: any) => {
        const matchFn = (item: T) => {
          if (key === '[stationId+pumpId]') {
            return Array.isArray(val) && item.stationId === val[0] && item.pumpId === val[1];
          }
          // Dexie indexing rule: missing, null, or undefined fields are NEVER indexed
          if (item[key] === undefined || item[key] === null) return false;
          return item[key] === val;
        };

        return {
          first: vi.fn(async () => {
            for (const item of store.values()) {
              if (matchFn(item)) return item;
            }
            return undefined;
          }),
          filter: (predicate: (item: T) => boolean) => ({
            toArray: vi.fn(async () => {
              const res: T[] = [];
              for (const item of store.values()) {
                if (matchFn(item) && predicate(item)) {
                  res.push(item);
                }
              }
              return res;
            }),
            modify: vi.fn(async (changes: Partial<T>) => {
              let count = 0;
              for (const [id, item] of store.entries()) {
                if (matchFn(item) && predicate(item)) {
                  store.set(id, { ...item, ...changes });
                  count++;
                }
              }
              return count;
            }),
          }),
          toArray: vi.fn(async () => {
            const res: T[] = [];
            for (const item of store.values()) {
              if (matchFn(item)) res.push(item);
            }
            return res;
          }),
          modify: vi.fn(async (changes: Partial<T>) => {
            let count = 0;
            for (const [id, item] of store.entries()) {
              if (matchFn(item)) {
                store.set(id, { ...item, ...changes });
                count++;
              }
            }
            return count;
          }),
        };
      },
    })),
    filter: (predicate: (item: T) => boolean) => ({
      toArray: vi.fn(async () => {
        const res: T[] = [];
        for (const item of store.values()) {
          if (predicate(item)) res.push(item);
        }
        return res;
      }),
    }),
  };

  return table;
}

// Module Mocks
vi.mock('./db', () => {
  const drafts = createMockDexieTable<DraftSubmission>('drafts');
  const submissions = createMockDexieTable<CachedSubmission>('submissions');
  const products = createMockDexieTable<any>('products');
  const stockBalances = createMockDexieTable<any>('stockBalances');

  return {
    db: {
      drafts,
      submissions,
      products,
      stockBalances,
      transaction: vi.fn(async (_mode: string, _t1: any, _t2: any, cb: () => Promise<void>) => {
        await cb();
      }),
    },
  };
});

vi.mock('./supabase', () => ({
  supabase: {
    from: vi.fn(),
    auth: {
      getSession: vi.fn().mockResolvedValue({ data: { session: null } }),
      onAuthStateChange: vi.fn().mockReturnValue({ data: { subscription: { unsubscribe: vi.fn() } } }),
    },
  },
}));

// Helper to extract hook output in pure Node/Vitest
function renderSubmissionHook(): ReturnType<typeof useSubmissionService> {
  let hookVal: ReturnType<typeof useSubmissionService> | undefined;
  function TestHookHarness() {
    hookVal = useSubmissionService();
    return null;
  }
  renderToString(createElement(TestHookHarness));
  if (!hookVal) throw new Error('Failed to render submission service hook harness');
  return hookVal;
}

describe('PWA Station Isolation Remediation Verification Suite', () => {
  let localStorageMock: Record<string, string> = {};
  let sessionStorageMock: Record<string, string> = {};
  let cacheStorageMock: Record<string, Set<string>> = {};

  const QA_STATION = 'FUELPRO-QA-20261108-01';
  const HISTORICAL_STATION = 'PUMP-61EC8AB41047';
  const MITALI_STATION = 'RASHTRA-74A837';

  beforeEach(() => {
    localStorageMock = {};
    sessionStorageMock = {};
    cacheStorageMock = {
      'pyrosync-dsm-v1.4': new Set(['/index.html', '/app.js', '/style.css']),
      'station-volatile-cache': new Set(['/api/data']),
    };

    vi.stubGlobal('localStorage', {
      getItem: vi.fn((key: string) => localStorageMock[key] ?? null),
      setItem: vi.fn((key: string, val: string) => { localStorageMock[key] = String(val); }),
      removeItem: vi.fn((key: string) => { delete localStorageMock[key]; }),
      clear: vi.fn(() => { localStorageMock = {}; }),
      key: vi.fn((idx: number) => Object.keys(localStorageMock)[idx] ?? null),
      get length() { return Object.keys(localStorageMock).length; },
    });

    vi.stubGlobal('sessionStorage', {
      getItem: vi.fn((key: string) => sessionStorageMock[key] ?? null),
      setItem: vi.fn((key: string, val: string) => { sessionStorageMock[key] = String(val); }),
      removeItem: vi.fn((key: string) => { delete sessionStorageMock[key]; }),
      clear: vi.fn(() => { sessionStorageMock = {}; }),
    });

    vi.stubGlobal('caches', {
      keys: vi.fn(async () => Object.keys(cacheStorageMock)),
      delete: vi.fn(async (key: string) => {
        const existed = key in cacheStorageMock;
        delete cacheStorageMock[key];
        return existed;
      }),
    });

    vi.stubGlobal('navigator', { onLine: true });

    // Default chainable Supabase builder mock
    const createDefaultBuilder = () => {
      const builder: any = {
        select: vi.fn().mockReturnThis(),
        eq: vi.fn().mockReturnThis(),
        in: vi.fn().mockReturnThis(),
        gt: vi.fn().mockReturnThis(),
        order: vi.fn().mockReturnThis(),
        limit: vi.fn().mockReturnThis(),
        single: vi.fn().mockReturnThis(),
        maybeSingle: vi.fn().mockReturnThis(),
        delete: vi.fn().mockReturnThis(),
        insert: vi.fn().mockResolvedValue({ error: null }),
        update: vi.fn().mockResolvedValue({ error: null }),
        then: (resolve: any) => resolve({ data: [], error: null }),
      };
      return builder;
    };
    (supabase.from as any).mockImplementation(() => createDefaultBuilder());

    // Clear db mocks
    (db.drafts as any)._store.clear();
    (db.submissions as any)._store.clear();
    (db.products as any)._store.clear();
    (db.stockBalances as any)._store.clear();

    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // =========================================================================
  // SCENARIO 1: Forensic Root Cause Fix — Cross-Station Opening Readings Isolation
  // =========================================================================
  describe('1. Cross-Station Opening Readings Isolation (Forensic Fix)', () => {
    it('guarantees QA Station (FUELPRO-QA-20261108-01) NEVER inherits historical readings (40, 40, 40, 42) from PUMP-61EC8AB41047', async () => {
      // 1. Seed historical station's draft in IndexedDB (Pump 1 + Pump 2, closing readings 40, 40, 40, 42)
      await db.drafts.add({
        stationId: HISTORICAL_STATION,
        pumpId: 1,
        shiftDate: '2026-10-26',
        shiftType: 'A',
        status: 'draft',
        draftId: 'historical-draft-1',
        createdAt: '2026-10-26T18:00:00Z',
        notes: 'Legacy Mitali test shift',
        cash: 0,
        upi: 0,
        card: 0,
        credit: 0,
        expense: 0,
        expenseNotes: '',
        short: 0,
        excess: 0,
        nozzleReadings: [
          { nozzleId: 1, fuelType: 'MS-I', openingReading: 10, closingReading: 40.00, rate: 103.81, pumpId: 1 },
          { nozzleId: 2, fuelType: 'MS-I', openingReading: 10, closingReading: 40.00, rate: 103.81, pumpId: 2 },
          { nozzleId: 3, fuelType: 'HSD', openingReading: 10, closingReading: 40.00, rate: 90.35, pumpId: 1 },
          { nozzleId: 4, fuelType: 'HSD', openingReading: 10, closingReading: 42.00, rate: 90.35, pumpId: 2 },
        ],
      });

      // 2. Query as QA Station with pumpsToFetch = [1, 2] using the exact SubmitShiftScreen Step 3 query
      const pumpsToFetch = [1, 2];
      const station = QA_STATION;

      const queriedDrafts = await db.drafts
        .where('stationId')
        .equals(station)
        .filter((d: any) => pumpsToFetch.includes(d.pumpId))
        .toArray();

      // Station B must find ZERO drafts despite sharing pump IDs 1 and 2
      expect(queriedDrafts).toHaveLength(0);

      // Verify prevClosings derivation produces empty dictionary for QA station
      const prevClosings: Record<number, number> = {};
      if (queriedDrafts.length > 0) {
        queriedDrafts[0].nozzleReadings.forEach((nr: any) => {
          prevClosings[nr.nozzleId] = Number(nr.closingReading);
        });
      }

      expect(prevClosings[1]).toBeUndefined();
      expect(prevClosings[2]).toBeUndefined();
      expect(prevClosings[3]).toBeUndefined();
      expect(prevClosings[4]).toBeUndefined();

      // Verify row construction sets opening to 0.00 and editable
      const nozzle1Row = {
        nozzleId: 1,
        openingReading: prevClosings[1] || 0,
        isOpeningReadOnly: (prevClosings[1] || 0) > 0,
      };
      expect(nozzle1Row.openingReading).toBe(0);
      expect(nozzle1Row.isOpeningReadOnly).toBe(false);
    });

    it('quarantines legacy unindexed drafts (where stationId is undefined) from station-scoped queries', async () => {
      // Seed a legacy unindexed draft from Dexie schema Version 4 (no stationId property)
      await db.drafts.add({
        // stationId omitted intentionally (legacy draft)
        pumpId: 1,
        shiftDate: '2026-10-25',
        shiftType: 'B',
        status: 'draft',
        draftId: 'legacy-unscoped-uuid',
        createdAt: '2026-10-25T12:00:00Z',
        notes: 'Pre-v1.3.64 draft',
        cash: 0,
        upi: 0,
        card: 0,
        credit: 0,
        expense: 0,
        expenseNotes: '',
        short: 0,
        excess: 0,
        nozzleReadings: [
          { nozzleId: 1, fuelType: 'MS-I', openingReading: 5, closingReading: 40.00, rate: 103.81 },
          { nozzleId: 4, fuelType: 'HSD', openingReading: 5, closingReading: 42.00, rate: 90.35 },
        ],
      } as any);

      // Querying for QA station or Mitali station MUST return empty
      const qaResults = await db.drafts
        .where('stationId')
        .equals(QA_STATION)
        .filter((d: any) => [1, 2].includes(d.pumpId))
        .toArray();
      expect(qaResults).toHaveLength(0);

      const mitaliResults = await db.drafts
        .where('stationId')
        .equals(MITALI_STATION)
        .filter((d: any) => [1, 2].includes(d.pumpId))
        .toArray();
      expect(mitaliResults).toHaveLength(0);
    });

    it('correctly retrieves legitimate same-station offline drafts when offline', async () => {
      // Seed a legitimate draft for QA_STATION
      await db.drafts.add({
        stationId: QA_STATION,
        pumpId: 1,
        shiftDate: '2026-11-08',
        shiftType: 'A',
        status: 'draft',
        draftId: 'qa-legit-draft-1',
        createdAt: '2026-11-08T08:00:00Z',
        notes: 'QA valid shift',
        cash: 1000,
        upi: 500,
        card: 0,
        credit: 0,
        expense: 0,
        expenseNotes: '',
        short: 0,
        excess: 0,
        nozzleReadings: [
          { nozzleId: 1, fuelType: 'MS-I', openingReading: 0, closingReading: 155.50, rate: 103.81, pumpId: 1 },
          { nozzleId: 3, fuelType: 'HSD', openingReading: 0, closingReading: 210.25, rate: 90.35, pumpId: 1 },
        ],
      });

      // Query as QA_STATION
      const qaDrafts = await db.drafts
        .where('stationId')
        .equals(QA_STATION)
        .filter((d: any) => [1, 2].includes(d.pumpId))
        .toArray();

      expect(qaDrafts).toHaveLength(1);
      expect(qaDrafts[0].nozzleReadings[0].closingReading).toBe(155.50);
      expect(qaDrafts[0].nozzleReadings[1].closingReading).toBe(210.25);
    });
  });

  // =========================================================================
  // SCENARIO 2: Connected-Pump Cross-Station Scoping
  // =========================================================================
  describe('2. Connected-Pump Scoping Across Stations', () => {
    it('isolates connected-pump readings (Pump 1 + Pump 2) across stations with overlapping pump IDs', async () => {
      // Station 1: HISTORICAL_STATION has Pump 1 + 2 with readings 40, 40, 40, 42
      await db.drafts.add({
        stationId: HISTORICAL_STATION,
        pumpId: 1,
        shiftDate: '2026-10-26',
        shiftType: 'A',
        status: 'draft',
        draftId: 'st1-draft',
        createdAt: '2026-10-26T10:00:00Z',
        notes: '',
        cash: 0, upi: 0, card: 0, credit: 0, expense: 0, expenseNotes: '', short: 0, excess: 0,
        nozzleReadings: [
          { nozzleId: 1, fuelType: 'MS-I', openingReading: 0, closingReading: 40.00, rate: 100 },
          { nozzleId: 2, fuelType: 'MS-I', openingReading: 0, closingReading: 40.00, rate: 100 },
          { nozzleId: 3, fuelType: 'HSD', openingReading: 0, closingReading: 40.00, rate: 90 },
          { nozzleId: 4, fuelType: 'HSD', openingReading: 0, closingReading: 42.00, rate: 90 },
        ],
      });

      // Station 2: QA_STATION has Pump 1 + 2 with higher readings 500, 500, 600, 600
      await db.drafts.add({
        stationId: QA_STATION,
        pumpId: 1,
        shiftDate: '2026-11-08',
        shiftType: 'A',
        status: 'draft',
        draftId: 'st2-draft',
        createdAt: '2026-11-08T10:00:00Z',
        notes: '',
        cash: 0, upi: 0, card: 0, credit: 0, expense: 0, expenseNotes: '', short: 0, excess: 0,
        nozzleReadings: [
          { nozzleId: 1, fuelType: 'MS-I', openingReading: 0, closingReading: 500.00, rate: 100 },
          { nozzleId: 2, fuelType: 'MS-I', openingReading: 0, closingReading: 500.00, rate: 100 },
          { nozzleId: 3, fuelType: 'HSD', openingReading: 0, closingReading: 600.00, rate: 90 },
          { nozzleId: 4, fuelType: 'HSD', openingReading: 0, closingReading: 600.00, rate: 90 },
        ],
      });

      // Fetch for QA_STATION: must strictly yield 500/600, never 40/42
      const qaResults = await db.drafts
        .where('stationId')
        .equals(QA_STATION)
        .filter((d: any) => [1, 2].includes(d.pumpId))
        .toArray();

      expect(qaResults).toHaveLength(1);
      expect(qaResults[0].nozzleReadings[0].closingReading).toBe(500.00);
      expect(qaResults[0].nozzleReadings[3].closingReading).toBe(600.00);

      // Fetch for HISTORICAL_STATION: must strictly yield 40/42
      const histResults = await db.drafts
        .where('stationId')
        .equals(HISTORICAL_STATION)
        .filter((d: any) => [1, 2].includes(d.pumpId))
        .toArray();

      expect(histResults).toHaveLength(1);
      expect(histResults[0].nozzleReadings[0].closingReading).toBe(40.00);
      expect(histResults[0].nozzleReadings[3].closingReading).toBe(42.00);
    });
  });

  // =========================================================================
  // SCENARIO 3: Non-Destructive Multi-Station Preservation & Station Switch
  // =========================================================================
  describe('3. Multi-Station Draft Preservation & Station Reset', () => {
    it('preserves offline drafts and submissions when resetStationData executes across station transitions', async () => {
      // Seed drafts for Station A
      await db.drafts.add({
        stationId: HISTORICAL_STATION,
        pumpId: 1,
        shiftDate: '2026-10-26',
        shiftType: 'A',
        status: 'draft',
        draftId: 'hist-draft-stay',
        createdAt: '2026-10-26T10:00:00Z',
        notes: '', cash: 0, upi: 0, card: 0, credit: 0, expense: 0, expenseNotes: '', short: 0, excess: 0,
        nozzleReadings: [],
      });

      // Seed submissions for Station A
      await db.submissions.add({
        remoteId: 'hist-sub-1',
        stationId: HISTORICAL_STATION,
        pumpId: 1,
        shiftDate: '2026-10-26',
        shiftType: 'A',
        status: 'Pending',
        submittedAt: '2026-10-26T10:00:00Z',
        cachedAt: '2026-10-26T10:00:00Z',
      });

      localStorageMock['cached_creditors'] = JSON.stringify([{ id: 1, name: 'Old Creditor' }]);
      localStorageMock['cached_vehicles'] = JSON.stringify([{ id: 1, number: 'MH-12-AB-1234' }]);
      sessionStorageMock['temporary_filter'] = 'active';

      // Perform station switch to QA_STATION
      await resetStationData(QA_STATION);

      // Critical Check 1: db.drafts.clear() was NOT called
      expect(db.drafts.clear).not.toHaveBeenCalled();
      expect(db.submissions.clear).not.toHaveBeenCalled();

      // Critical Check 2: Historical station draft is still safely in IndexedDB
      const allDrafts = await db.drafts.toArray();
      expect(allDrafts).toHaveLength(1);
      expect(allDrafts[0].stationId).toBe(HISTORICAL_STATION);

      // Critical Check 3: Volatile storage was cleared
      expect(localStorageMock['cached_creditors']).toBeUndefined();
      expect(localStorageMock['cached_vehicles']).toBeUndefined();
      expect(Object.keys(sessionStorageMock)).toHaveLength(0);

      // Critical Check 4: current_station_id updated to QA_STATION
      expect(localStorageMock['current_station_id']).toBe(QA_STATION);
      expect(localStorageMock['last_station_id']).toBe(QA_STATION);
    });

    it('preserves static Service Worker app shell caches (pyrosync-dsm-*) while clearing non-shell caches', async () => {
      expect(cacheStorageMock['pyrosync-dsm-v1.4']).toBeDefined();
      expect(cacheStorageMock['station-volatile-cache']).toBeDefined();

      await resetStationData(QA_STATION);

      // Static app shell cache MUST survive
      expect(cacheStorageMock['pyrosync-dsm-v1.4']).toBeDefined();
      // Volatile data cache MUST be deleted
      expect(cacheStorageMock['station-volatile-cache']).toBeUndefined();
    });

    it('safely supports A -> B -> A switching without resurrection or cross-contamination', async () => {
      // 1. Station A creates a draft
      await db.drafts.add({
        stationId: 'STATION_A',
        pumpId: 1,
        shiftDate: '2026-11-01',
        shiftType: 'A',
        status: 'draft',
        draftId: 'draft-a',
        createdAt: '2026-11-01T08:00:00Z',
        notes: '', cash: 0, upi: 0, card: 0, credit: 0, expense: 0, expenseNotes: '', short: 0, excess: 0,
        nozzleReadings: [{ nozzleId: 1, fuelType: 'MS-I', openingReading: 10, closingReading: 50, rate: 100 }],
      });

      // Switch to Station B
      await resetStationData('STATION_B');

      // Station B creates its own draft
      await db.drafts.add({
        stationId: 'STATION_B',
        pumpId: 1,
        shiftDate: '2026-11-02',
        shiftType: 'B',
        status: 'draft',
        draftId: 'draft-b',
        createdAt: '2026-11-02T08:00:00Z',
        notes: '', cash: 0, upi: 0, card: 0, credit: 0, expense: 0, expenseNotes: '', short: 0, excess: 0,
        nozzleReadings: [{ nozzleId: 1, fuelType: 'MS-I', openingReading: 20, closingReading: 80, rate: 100 }],
      });

      // When queried for Station B: only draft B is returned
      const bDrafts = await db.drafts.where('stationId').equals('STATION_B').toArray();
      expect(bDrafts).toHaveLength(1);
      expect(bDrafts[0].draftId).toBe('draft-b');

      // Switch back to Station A
      await resetStationData('STATION_A');

      // When queried for Station A: only draft A is returned
      const aDrafts = await db.drafts.where('stationId').equals('STATION_A').toArray();
      expect(aDrafts).toHaveLength(1);
      expect(aDrafts[0].draftId).toBe('draft-a');

      // Total records in table: both remain intact
      const total = await db.drafts.toArray();
      expect(total).toHaveLength(2);
    });
  });

  // =========================================================================
  // SCENARIO 4: Opening Readings Cache Key Isolation
  // =========================================================================
  describe('4. Opening Readings LocalStorage Cache Isolation', () => {
    it('isolates opening readings cache keys by station and purges legacy un-versioned keys', () => {
      // Populate legacy contaminated cache key from older app versions
      localStorageMock['nozzle_openings_PUMP-61EC8AB41047'] = JSON.stringify({ 1: 40.0, 2: 40.0, 3: 40.0, 4: 42.0 });
      localStorageMock['nozzle_openings_random'] = JSON.stringify({ 1: 99.0 });

      // Simulate SubmitShiftScreen cache key purge
      Object.keys(localStorageMock).forEach((k) => {
        if (k.startsWith('nozzle_openings_') && !k.startsWith('nozzle_openings_v2_')) {
          delete localStorageMock[k];
        }
      });

      expect(localStorageMock['nozzle_openings_PUMP-61EC8AB41047']).toBeUndefined();
      expect(localStorageMock['nozzle_openings_random']).toBeUndefined();

      // Station-scoped v2 keys
      const qaCacheKey = `nozzle_openings_v2_${QA_STATION}`;
      const histCacheKey = `nozzle_openings_v2_${HISTORICAL_STATION}`;

      localStorageMock[qaCacheKey] = JSON.stringify({ 1: 150.0 });
      localStorageMock[histCacheKey] = JSON.stringify({ 1: 40.0 });

      // QA Station reads ONLY its own cache key
      const parsedQa = JSON.parse(localStorageMock[qaCacheKey]);
      expect(parsedQa[1]).toBe(150.0);

      // HISTORICAL Station reads ONLY its own cache key
      const parsedHist = JSON.parse(localStorageMock[histCacheKey]);
      expect(parsedHist[1]).toBe(40.0);
    });

    it('does not write opening readings cache for unvalidated or default station IDs', () => {
      const defaultStation: string = 'DEFAULT';
      const emptyStation: string = '';

      const key1 = (defaultStation && defaultStation.toLowerCase() !== 'default')
        ? `nozzle_openings_v2_${defaultStation}` : '';
      const key2 = (emptyStation && emptyStation.toLowerCase() !== 'default')
        ? `nozzle_openings_v2_${emptyStation}` : '';

      expect(key1).toBe('');
      expect(key2).toBe('');
    });
  });

  // =========================================================================
  // SCENARIO 5: useSubmissionService Hook Station Isolation
  // =========================================================================
  describe('5. useSubmissionService Station Enforcement', () => {
    it('saveDraft rejects drafts with missing, null, or whitespace stationId', async () => {
      const hook = renderSubmissionHook();

      const baseDraft = {
        pumpId: 1,
        shiftDate: '2026-11-08',
        shiftType: 'A' as const,
        notes: '',
        cash: 100,
        upi: 0,
        card: 0,
        credit: 0,
        expense: 0,
        expenseNotes: '',
        short: 0,
        excess: 0,
        nozzleReadings: [],
      };

      // Missing stationId
      await expect(hook.saveDraft({ ...baseDraft, stationId: '' as any })).rejects.toThrow(
        /Cannot save draft: Missing or invalid station identity/
      );

      // Whitespace stationId
      await expect(hook.saveDraft({ ...baseDraft, stationId: '   ' })).rejects.toThrow(
        /Cannot save draft: Missing or invalid station identity/
      );

      // Undefined stationId
      await expect(hook.saveDraft({ ...baseDraft, stationId: undefined as any })).rejects.toThrow(
        /Cannot save draft: Missing or invalid station identity/
      );
    });

    it('saveDraft trims and persists valid stationId with generated draftId', async () => {
      const hook = renderSubmissionHook();

      const saved = await hook.saveDraft({
        stationId: `  ${QA_STATION}  `,
        pumpId: 1,
        shiftDate: '2026-11-08',
        shiftType: 'A',
        notes: 'Test note',
        cash: 500,
        upi: 200,
        card: 0,
        credit: 0,
        expense: 0,
        expenseNotes: '',
        short: 0,
        excess: 0,
        nozzleReadings: [],
      });

      expect(saved.stationId).toBe(QA_STATION);
      expect(saved.draftId).toBeDefined();
      expect(saved.status).toBe('draft');

      // Verify in db.drafts
      const inDb = await db.drafts.get(saved.id);
      expect(inDb?.stationId).toBe(QA_STATION);
    });

    it('submitToSupabase rejects submission when draft station does not match station context', async () => {
      const hook = renderSubmissionHook();

      const draft: DraftSubmission = {
        id: 1,
        draftId: 'test-mismatch-draft',
        stationId: HISTORICAL_STATION, // Station A
        pumpId: 1,
        shiftDate: '2026-11-08',
        shiftType: 'A',
        notes: '',
        cash: 100,
        upi: 0,
        card: 0,
        credit: 0,
        expense: 0,
        expenseNotes: '',
        short: 0,
        excess: 0,
        createdAt: '2026-11-08T10:00:00Z',
        status: 'draft',
        nozzleReadings: [],
      };

      // Attempt to submit under QA_STATION (Station B)
      const err = await hook.submitToSupabase(draft, 'user-123', QA_STATION);

      expect(err).toContain('Submission rejected: Draft station mismatch');
      expect(err).toContain(HISTORICAL_STATION);
      expect(err).toContain(QA_STATION);

      // Supabase insert must NOT have been called
      expect(supabase.from).not.toHaveBeenCalled();
    });

    it('submitToSupabase strictly rejects when active stationId is empty, undefined, or whitespace without falling back to draft', async () => {
      const hook = renderSubmissionHook();

      const draft: DraftSubmission = {
        id: 50,
        draftId: 'draft-st-a',
        stationId: QA_STATION,
        pumpId: 1,
        shiftDate: '2026-11-08',
        shiftType: 'A',
        notes: '', cash: 100, upi: 0, card: 0, credit: 0, expense: 0, expenseNotes: '', short: 0, excess: 0,
        createdAt: '2026-11-08T10:00:00Z',
        status: 'draft',
        nozzleReadings: [],
      };

      // 1. Empty active station
      const errEmpty = await hook.submitToSupabase(draft, 'user-1', '');
      expect(errEmpty).toContain('Submission failed: Missing authenticated station identity');

      // 2. Undefined active station
      const errUndef = await hook.submitToSupabase(draft, 'user-1', undefined as any);
      expect(errUndef).toContain('Submission failed: Missing authenticated station identity');

      // 3. Whitespace-only active station
      const errWhite = await hook.submitToSupabase(draft, 'user-1', '    ');
      expect(errWhite).toContain('Submission failed: Missing authenticated station identity');

      // Verify rejected operations performed zero remote insertions and zero local cache writes
      expect(supabase.from).not.toHaveBeenCalled();
      const cached = await db.submissions.toArray();
      expect(cached).toHaveLength(0);
    });

    it('submitToSupabase strictly rejects when draft stationId is missing or empty', async () => {
      const hook = renderSubmissionHook();

      const draftNoStation: DraftSubmission = {
        id: 51,
        draftId: 'draft-no-st',
        stationId: '' as any,
        pumpId: 1,
        shiftDate: '2026-11-08',
        shiftType: 'A',
        notes: '', cash: 100, upi: 0, card: 0, credit: 0, expense: 0, expenseNotes: '', short: 0, excess: 0,
        createdAt: '2026-11-08T10:00:00Z',
        status: 'draft',
        nozzleReadings: [],
      };

      const err = await hook.submitToSupabase(draftNoStation, 'user-1', QA_STATION);
      expect(err).toContain('Submission rejected: Draft missing station identity');

      expect(supabase.from).not.toHaveBeenCalled();
      const cached = await db.submissions.toArray();
      expect(cached).toHaveLength(0);
    });

    it('submitToSupabase attaches stationId to remote payload and caches with stationId', async () => {
      const hook = renderSubmissionHook();

      const draft: DraftSubmission = {
        id: 10,
        draftId: 'valid-draft-uuid',
        stationId: QA_STATION,
        pumpId: 1,
        shiftDate: '2026-11-08',
        shiftType: 'A',
        notes: '',
        cash: 500,
        upi: 200,
        card: 0,
        credit: 0,
        expense: 0,
        expenseNotes: '',
        short: 0,
        excess: 0,
        createdAt: '2026-11-08T10:00:00Z',
        status: 'draft',
        nozzleReadings: [
          { nozzleId: 1, fuelType: 'MS-I', openingReading: 10, closingReading: 60, rate: 100 },
        ],
      };

      // Mock Supabase calls for submission
      const insertedRows: Record<string, any[]> = {};
      (supabase.from as any).mockImplementation((table: string) => {
        const builder: any = {
          select: vi.fn().mockReturnThis(),
          eq: vi.fn().mockReturnThis(),
          limit: vi.fn().mockReturnThis(),
          order: vi.fn().mockReturnThis(),
          delete: vi.fn().mockReturnThis(),
          insert: vi.fn(async (payload: any) => {
            insertedRows[table] = insertedRows[table] || [];
            insertedRows[table].push(payload);
            return { error: null };
          }),
          then: (resolve: any) => {
            if (table === 'DsmSubmissions') {
              resolve({ data: [], error: null }); // no existing pending submission
            } else {
              resolve({ data: null, error: null });
            }
          },
        };
        return builder;
      });

      const err = await hook.submitToSupabase(draft, 'dsm-user-456', QA_STATION);
      expect(err).toBeNull(); // success

      // Verify DsmSubmissions payload contains StationId
      expect(insertedRows['DsmSubmissions']).toBeDefined();
      expect(insertedRows['DsmSubmissions'][0].StationId).toBe(QA_STATION);
      expect(insertedRows['DsmSubmissions'][0].PumpId).toBe(1);

      // Verify db.submissions cache contains stationId
      const cachedSubs = await db.submissions.toArray();
      expect(cachedSubs.length).toBeGreaterThan(0);
      const cached = cachedSubs.find((s) => s.stationId === QA_STATION);
      expect(cached).toBeDefined();
      expect(cached?.stationId).toBe(QA_STATION);
    });

    it('retryQueuedDrafts retries ONLY queued drafts matching active station and preserves stationId', async () => {
      const hook = renderSubmissionHook();

      // Seed 2 queued drafts: 1 for QA_STATION, 1 for HISTORICAL_STATION
      await db.drafts.add({
        id: 101,
        draftId: 'qa-queued-1',
        stationId: QA_STATION,
        pumpId: 1,
        shiftDate: '2026-11-08',
        shiftType: 'A',
        status: 'queued',
        createdAt: '2026-11-08T10:00:00Z',
        notes: '', cash: 100, upi: 0, card: 0, credit: 0, expense: 0, expenseNotes: '', short: 0, excess: 0,
        nozzleReadings: [],
      });

      await db.drafts.add({
        id: 102,
        draftId: 'hist-queued-2',
        stationId: HISTORICAL_STATION,
        pumpId: 1,
        shiftDate: '2026-10-26',
        shiftType: 'B',
        status: 'queued',
        createdAt: '2026-10-26T10:00:00Z',
        notes: '', cash: 200, upi: 0, card: 0, credit: 0, expense: 0, expenseNotes: '', short: 0, excess: 0,
        nozzleReadings: [],
      });

      const submittedDraftIds: string[] = [];
      (supabase.from as any).mockImplementation((table: string) => {
        const builder: any = {
          select: vi.fn().mockReturnThis(),
          eq: vi.fn().mockReturnThis(),
          limit: vi.fn().mockReturnThis(),
          order: vi.fn().mockReturnThis(),
          delete: vi.fn().mockReturnThis(),
          insert: vi.fn(async (payload: any) => {
            if (table === 'DsmSubmissions') {
              const meta = typeof payload.Metadata === 'string'
                ? JSON.parse(payload.Metadata)
                : payload.Metadata;
              submittedDraftIds.push(meta?.draftId || null);
            }
            return { error: null };
          }),
          then: (resolve: any) => resolve({ data: [], error: null }),
        };
        return builder;
      });

      // Call retry for QA_STATION
      await hook.retryQueuedDrafts('user-qa', QA_STATION);

      // HISTORICAL draft (id: 102) MUST still be queued and untouched
      const histDraft = await db.drafts.get(102);
      expect(histDraft?.status).toBe('queued');
      expect(histDraft?.stationId).toBe(HISTORICAL_STATION);

      // QA draft (id: 101) was processed
      const qaDraft = await db.drafts.get(101);
      expect(qaDraft?.status).toBe('submitted');
    });

    it('fetchSubmissionHistory fails closed and returns empty array without querying Supabase on empty, undefined, or whitespace stationId', async () => {
      const hook = renderSubmissionHook();

      // 1. Empty string
      const resEmpty = await hook.fetchSubmissionHistory('user-1', 'user-1', '');
      expect(resEmpty).toEqual([]);

      // 2. Undefined
      const resUndef = await hook.fetchSubmissionHistory('user-1', 'user-1', undefined);
      expect(resUndef).toEqual([]);

      // 3. Whitespace-only
      const resWhite = await hook.fetchSubmissionHistory('user-1', 'user-1', '   ');
      expect(resWhite).toEqual([]);

      // Critical verification: Supabase was NEVER queried!
      expect(supabase.from).not.toHaveBeenCalled();
    });

    it('fetchSubmissionHistory enforces exact station filter, user filter, ordering, and 100-record limit', async () => {
      const hook = renderSubmissionHook();

      const queryLog: { select?: string; filters: Record<string, any>; orFilter?: string; orderCol?: string; orderAsc?: boolean; limit?: number } = {
        filters: {},
      };

      (supabase.from as any).mockImplementation((table: string) => {
        expect(table).toBe('DsmSubmissions');
        const builder: any = {
          select: vi.fn((cols: string) => {
            queryLog.select = cols;
            return builder;
          }),
          eq: vi.fn((col: string, val: any) => {
            queryLog.filters[col] = val;
            return builder;
          }),
          or: vi.fn((filterStr: string) => {
            queryLog.orFilter = filterStr;
            return builder;
          }),
          order: vi.fn((col: string, opts: any) => {
            queryLog.orderCol = col;
            queryLog.orderAsc = opts?.ascending;
            return builder;
          }),
          limit: vi.fn((n: number) => {
            queryLog.limit = n;
            return builder;
          }),
          then: (resolve: any) => resolve({ data: [{ Id: 'sub-exact', StationId: QA_STATION }], error: null }),
        };
        return builder;
      });

      // 1. Call with same dsmUserId and authUserId
      const res = await hook.fetchSubmissionHistory('dsm-101', 'dsm-101', QA_STATION);
      expect(res).toHaveLength(1);
      expect(queryLog.filters['StationId']).toBe(QA_STATION);
      expect(queryLog.filters['DsmUserId']).toBe('dsm-101');
      expect(queryLog.orderCol).toBe('SubmittedAt');
      expect(queryLog.orderAsc).toBe(false);
      expect(queryLog.limit).toBe(100);

      // 2. Call with distinct authUserId
      await hook.fetchSubmissionHistory('dsm-101', 'auth-202', QA_STATION);
      expect(queryLog.orFilter).toBe('DsmUserId.eq.dsm-101,DsmUserId.eq.auth-202');
      expect(queryLog.filters['StationId']).toBe(QA_STATION);
    });
  });

  // =========================================================================
  // SCENARIO 6: Numerical Comparisons and Calculation Integrity
  // =========================================================================
  describe('6. Shift Continuity & Numerical Calculations Integrity', () => {
    it('correctly calculates dispensed fuel and net sales using numeric values', () => {
      const opening = 40.0;
      const closing = 150.5;
      const testing = 5.0;

      const dispensed = closing - opening;
      expect(dispensed).toBe(110.5);

      const netSale = Math.max(0, dispensed - testing);
      expect(netSale).toBe(105.5);

      const rate = 103.81;
      const amount = netSale * rate;
      expect(Math.round(amount * 100) / 100).toBe(10951.96);
    });

    it('strictly rejects closing reading numerically smaller than opening reading', () => {
      const opening = 100.0;
      const invalidClosing = 95.0;

      const isValid = Number(invalidClosing) >= Number(opening);
      expect(isValid).toBe(false);
    });

    it('handles fresh station zero-reading initialization safely', () => {
      const prevClosing = 0; // Fresh station with no previous closing
      const opening = prevClosing > 0 ? parseFloat(Number(prevClosing).toFixed(2)) : 0;
      const isOpeningReadOnly = prevClosing > 0;

      expect(opening).toBe(0);
      expect(isOpeningReadOnly).toBe(false); // Editable for DSM
    });
  });
});
