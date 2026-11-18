import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import {
  buildShiftCollectionsKey,
  getShiftCollectionsDraft,
  saveShiftCollectionsDraft,
  clearShiftCollectionsDraftIfMatching,
  clearShiftCollectionsDraft,
  setIDBFactoryForTesting,
  type ShiftCollectionsDraft,
  type DebtorEntry,
  type OtpTransactionItem,
} from './shiftCollectionDraftStore';

/**
 * In-memory IDBFactory simulation for isolated IndexedDB testing.
 * Accurately simulates IDB transactions, object store CRUD, tx.oncomplete lifecycle,
 * and database connection closing/reopening across "application restarts".
 */
class MockIDBBackingStore {
  databases = new Map<string, Map<string, Map<any, any>>>();

  getStore(dbName: string, storeName: string): Map<any, any> {
    if (!this.databases.has(dbName)) {
      this.databases.set(dbName, new Map());
    }
    const db = this.databases.get(dbName)!;
    if (!db.has(storeName)) {
      db.set(storeName, new Map());
    }
    return db.get(storeName)!;
  }

  clear() {
    this.databases.clear();
  }
}

function createMockIDBFactory(sharedBacking: MockIDBBackingStore): IDBFactory {
  return {
    open(name: string, version?: number) {
      const openReq: any = {
        result: null,
        error: null,
        source: null,
        transaction: null,
        readyState: 'pending',
        onsuccess: null,
        onerror: null,
        onupgradeneeded: null,
        onblocked: null,
      };

      setTimeout(() => {
        const storeNames = new Set<string>();
        // Check if database already has stores in backing
        if (sharedBacking.databases.has(name)) {
          for (const sName of sharedBacking.databases.get(name)!.keys()) {
            storeNames.add(sName);
          }
        }

        const mockDb: any = {
          name,
          version: version || 1,
          objectStoreNames: {
            contains: (sName: string) => storeNames.has(sName),
          },
          close: vi.fn(),
          createObjectStore(sName: string, _options?: any) {
            storeNames.add(sName);
            sharedBacking.getStore(name, sName);
            return {};
          },
          transaction(sName: string, mode: 'readonly' | 'readwrite') {
            const rawStore = sharedBacking.getStore(name, sName);
            const tx: any = {
              mode,
              error: null,
              oncomplete: null,
              onerror: null,
              onabort: null,
              objectStore(_name: string) {
                return {
                  get(key: any) {
                    const req: any = { result: undefined, error: null, onsuccess: null, onerror: null };
                    setTimeout(() => {
                      req.result = rawStore.has(key) ? JSON.parse(JSON.stringify(rawStore.get(key))) : undefined;
                      if (req.onsuccess) req.onsuccess({ target: req });
                    }, 0);
                    return req;
                  },
                  put(item: any) {
                    const req: any = { result: item.key, error: null, onsuccess: null, onerror: null };
                    setTimeout(() => {
                      rawStore.set(item.key, JSON.parse(JSON.stringify(item)));
                      if (req.onsuccess) req.onsuccess({ target: req });
                    }, 0);
                    return req;
                  },
                  delete(key: any) {
                    const req: any = { result: undefined, error: null, onsuccess: null, onerror: null };
                    setTimeout(() => {
                      rawStore.delete(key);
                      if (req.onsuccess) req.onsuccess({ target: req });
                    }, 0);
                    return req;
                  },
                };
              },
            };

            // Trigger tx.oncomplete asynchronously after all operations queued in this tick
            setTimeout(() => {
              if (tx.oncomplete) {
                tx.oncomplete({ target: tx });
              }
            }, 10);

            return tx;
          },
        };

        openReq.result = mockDb;

        // Trigger onupgradeneeded if store not yet registered
        if (openReq.onupgradeneeded) {
          openReq.onupgradeneeded({ target: openReq });
        }
        if (openReq.onsuccess) {
          openReq.onsuccess({ target: openReq });
        }
      }, 0);

      return openReq as IDBOpenDBRequest;
    },
    cmp: vi.fn(),
    databases: vi.fn(),
    deleteDatabase: vi.fn(),
  } as unknown as IDBFactory;
}

describe('ShiftCollectionDraftStore — Browser-Local IndexedDB Persistence', () => {
  let backingStore: MockIDBBackingStore;
  let fetchSpy: any;

  beforeEach(() => {
    backingStore = new MockIDBBackingStore();
    setIDBFactoryForTesting(createMockIDBFactory(backingStore));

    // Safeguard 11: Instrument network calls to assert zero HTTP traffic
    fetchSpy = vi.fn();
    (globalThis as any).fetch = fetchSpy;
  });

  afterEach(() => {
    setIDBFactoryForTesting(null);
    vi.restoreAllMocks();
  });

  describe('1. Canonical Key Generation & Identity Scoping', () => {
    it('generates normalized lowercase canonical keys for complete identity', () => {
      const key = buildShiftCollectionsKey('Mitali_Station', 'dsm-guid-1234', '2026-11-18', 'a', 1);
      expect(key).toBe('dsm-coll:mitali_station:dsm-guid-1234:2026-11-18:A:1');
    });

    it('rejects incomplete or unresolved station identity', () => {
      expect(buildShiftCollectionsKey('', 'dsm-guid-1234', '2026-11-18', 'A', 1)).toBeNull();
      expect(buildShiftCollectionsKey('   ', 'dsm-guid-1234', '2026-11-18', 'A', 1)).toBeNull();
      expect(buildShiftCollectionsKey(null, 'dsm-guid-1234', '2026-11-18', 'A', 1)).toBeNull();
    });

    it('rejects missing or unresolved DSM identifier', () => {
      expect(buildShiftCollectionsKey('Mitali', '', '2026-11-18', 'A', 1)).toBeNull();
      expect(buildShiftCollectionsKey('Mitali', null, '2026-11-18', 'A', 1)).toBeNull();
    });

    it('rejects invalid or non-positive pumpId', () => {
      expect(buildShiftCollectionsKey('Mitali', 'dsm-1', '2026-11-18', 'A', 0)).toBeNull();
      expect(buildShiftCollectionsKey('Mitali', 'dsm-1', '2026-11-18', 'A', -1)).toBeNull();
      expect(buildShiftCollectionsKey('Mitali', 'dsm-1', '2026-11-18', 'A', 'abc')).toBeNull();
      expect(buildShiftCollectionsKey('Mitali', 'dsm-1', '2026-11-18', 'A', null)).toBeNull();
    });

    it('guarantees complete isolation between different DSMs and different shifts', () => {
      const dsm1Key = buildShiftCollectionsKey('Station1', 'DSM_A', '2026-11-18', 'A', 1);
      const dsm2Key = buildShiftCollectionsKey('Station1', 'DSM_B', '2026-11-18', 'A', 1);
      const shiftBKey = buildShiftCollectionsKey('Station1', 'DSM_A', '2026-11-18', 'B', 1);
      const date2Key = buildShiftCollectionsKey('Station1', 'DSM_A', '2026-11-19', 'A', 1);
      const pump2Key = buildShiftCollectionsKey('Station1', 'DSM_A', '2026-11-18', 'A', 2);

      expect(dsm1Key).not.toBe(dsm2Key);
      expect(dsm1Key).not.toBe(shiftBKey);
      expect(dsm1Key).not.toBe(date2Key);
      expect(dsm1Key).not.toBe(pump2Key);
    });
  });

  describe('2. Debtor and OTP Collection Persistence & Full Metadata Preservation', () => {
    it('persists debtor entries with complete metadata fields without stripping', async () => {
      const key = buildShiftCollectionsKey('Mitali', 'dsm-1', '2026-11-18', 'A', 1)!;
      const debtors: DebtorEntry[] = [
        {
          debtorName: 'Patil Transports',
          amount: 2500,
          vehicleNumber: 'MH-12-AB-1234',
          slipNumber: 'SLIP-9901',
          time: '10:15',
          customNote: 'Diesel fuel on credit',
        },
      ];

      const draft: ShiftCollectionsDraft = {
        key,
        stationId: 'Mitali',
        dsmId: 'dsm-1',
        shiftDate: '2026-11-18',
        shiftType: 'A',
        pumpId: 1,
        debtorEntries: debtors,
        machineTransactions: [],
        version: 1,
        updatedAt: new Date().toISOString(),
      };

      await saveShiftCollectionsDraft(draft);

      const restored = await getShiftCollectionsDraft(key);
      expect(restored).not.toBeNull();
      expect(restored?.debtorEntries).toHaveLength(1);
      expect(restored?.debtorEntries[0]).toEqual({
        debtorName: 'Patil Transports',
        amount: 2500,
        vehicleNumber: 'MH-12-AB-1234',
        slipNumber: 'SLIP-9901',
        time: '10:15',
        customNote: 'Diesel fuel on credit',
      });
    });

    it('persists OTP machine transactions with complete metadata fields', async () => {
      const key = buildShiftCollectionsKey('Mitali', 'dsm-1', '2026-11-18', 'A', 1)!;
      const otps: OtpTransactionItem[] = [
        {
          id: 'PHONEPE-Morning-1001',
          collectionTypeCode: 'PHONEPE',
          amount: 1200,
          otp: 'OTP-8821',
          tid: 'TID-1234',
          batch: 'B-01',
          slot: 'Morning',
        },
      ];

      const draft: ShiftCollectionsDraft = {
        key,
        stationId: 'Mitali',
        dsmId: 'dsm-1',
        shiftDate: '2026-11-18',
        shiftType: 'A',
        pumpId: 1,
        debtorEntries: [],
        machineTransactions: otps,
        version: 1,
        updatedAt: new Date().toISOString(),
      };

      await saveShiftCollectionsDraft(draft);

      const restored = await getShiftCollectionsDraft(key);
      expect(restored).not.toBeNull();
      expect(restored?.machineTransactions).toHaveLength(1);
      expect(restored?.machineTransactions[0]).toEqual({
        id: 'PHONEPE-Morning-1001',
        collectionTypeCode: 'PHONEPE',
        amount: 1200,
        otp: 'OTP-8821',
        tid: 'TID-1234',
        batch: 'B-01',
        slot: 'Morning',
      });
    });

    it('restores both debtor entries and OTP transactions together', async () => {
      const key = buildShiftCollectionsKey('Mitali', 'dsm-1', '2026-11-18', 'A', 1)!;
      const debtors: DebtorEntry[] = [
        { debtorName: 'Sharma Traders', amount: 500, time: '09:00' },
      ];
      const otps: OtpTransactionItem[] = [
        { id: 'tx-1', collectionTypeCode: 'PETROCARD', amount: 800, otp: 'SLIP-55', tid: 'TID-55', batch: 'B-05', slot: 'General' },
      ];

      await saveShiftCollectionsDraft({
        key,
        stationId: 'Mitali',
        dsmId: 'dsm-1',
        shiftDate: '2026-11-18',
        shiftType: 'A',
        pumpId: 1,
        debtorEntries: debtors,
        machineTransactions: otps,
        version: 1,
        updatedAt: new Date().toISOString(),
      });

      const restored = await getShiftCollectionsDraft(key);
      expect(restored?.debtorEntries).toHaveLength(1);
      expect(restored?.debtorEntries[0].debtorName).toBe('Sharma Traders');
      expect(restored?.machineTransactions).toHaveLength(1);
      expect(restored?.machineTransactions[0].collectionTypeCode).toBe('PETROCARD');
    });

    it('persists additions, modifications, and deletions correctly', async () => {
      const key = buildShiftCollectionsKey('Mitali', 'dsm-1', '2026-11-18', 'A', 1)!;

      // 1. Initial addition
      await saveShiftCollectionsDraft({
        key,
        stationId: 'Mitali',
        dsmId: 'dsm-1',
        shiftDate: '2026-11-18',
        shiftType: 'A',
        pumpId: 1,
        debtorEntries: [{ debtorName: 'Entry 1', amount: 100, time: '10:00' }],
        machineTransactions: [],
        version: 1,
        updatedAt: new Date().toISOString(),
      });

      // 2. Add another entry (version 2)
      await saveShiftCollectionsDraft({
        key,
        stationId: 'Mitali',
        dsmId: 'dsm-1',
        shiftDate: '2026-11-18',
        shiftType: 'A',
        pumpId: 1,
        debtorEntries: [
          { debtorName: 'Entry 1', amount: 100, time: '10:00' },
          { debtorName: 'Entry 2', amount: 200, time: '10:30' },
        ],
        machineTransactions: [],
        version: 2,
        updatedAt: new Date().toISOString(),
      });

      let res = await getShiftCollectionsDraft(key);
      expect(res?.debtorEntries).toHaveLength(2);

      // 3. Delete Entry 1 (version 3)
      await saveShiftCollectionsDraft({
        key,
        stationId: 'Mitali',
        dsmId: 'dsm-1',
        shiftDate: '2026-11-18',
        shiftType: 'A',
        pumpId: 1,
        debtorEntries: [{ debtorName: 'Entry 2', amount: 200, time: '10:30' }],
        machineTransactions: [],
        version: 3,
        updatedAt: new Date().toISOString(),
      });

      res = await getShiftCollectionsDraft(key);
      expect(res?.debtorEntries).toHaveLength(1);
      expect(res?.debtorEntries[0].debtorName).toBe('Entry 2');
    });
  });

  describe('3. Cold Restart / Application Recreation Simulation', () => {
    it('survives database connection closure and restores data upon fresh connection opening', async () => {
      const key = buildShiftCollectionsKey('Mitali', 'dsm-cold', '2026-11-18', 'B', 2)!;

      // Session 1: Save data using factory 1
      setIDBFactoryForTesting(createMockIDBFactory(backingStore));
      await saveShiftCollectionsDraft({
        key,
        stationId: 'Mitali',
        dsmId: 'dsm-cold',
        shiftDate: '2026-11-18',
        shiftType: 'B',
        pumpId: 2,
        debtorEntries: [{ debtorName: 'Cold Restart Debtor', amount: 3300, time: '14:00' }],
        machineTransactions: [{ id: 'tx-cold', collectionTypeCode: 'PHONEPE', amount: 450, otp: 'SLIP-99', tid: 'TID-COLD', batch: 'B-COLD', slot: 'Day' }],
        version: 1,
        updatedAt: new Date().toISOString(),
      });

      // Session 2: Fresh factory pointing to same backing store (simulates reopening PWA)
      setIDBFactoryForTesting(createMockIDBFactory(backingStore));

      const restored = await getShiftCollectionsDraft(key);
      expect(restored).not.toBeNull();
      expect(restored?.debtorEntries[0].debtorName).toBe('Cold Restart Debtor');
      expect(restored?.debtorEntries[0].amount).toBe(3300);
      expect(restored?.machineTransactions[0].otp).toBe('SLIP-99');
    });
  });

  describe('4. Concurrency & Write Versioning Protection', () => {
    it('prevents an older snapshot from overwriting a newer committed version', async () => {
      const key = buildShiftCollectionsKey('Mitali', 'dsm-concurrent', '2026-11-18', 'A', 1)!;

      // Save version 2
      await saveShiftCollectionsDraft({
        key,
        stationId: 'Mitali',
        dsmId: 'dsm-concurrent',
        shiftDate: '2026-11-18',
        shiftType: 'A',
        pumpId: 1,
        debtorEntries: [{ debtorName: 'Newer Version 2', amount: 200, time: '11:00' }],
        machineTransactions: [],
        version: 2,
        updatedAt: new Date().toISOString(),
      });

      // Attempt to save version 1 (stale snapshot)
      await saveShiftCollectionsDraft({
        key,
        stationId: 'Mitali',
        dsmId: 'dsm-concurrent',
        shiftDate: '2026-11-18',
        shiftType: 'A',
        pumpId: 1,
        debtorEntries: [{ debtorName: 'Stale Version 1', amount: 100, time: '10:00' }],
        machineTransactions: [],
        version: 1,
        updatedAt: new Date().toISOString(),
      });

      // Stored data must remain Version 2!
      const current = await getShiftCollectionsDraft(key);
      expect(current?.version).toBe(2);
      expect(current?.debtorEntries[0].debtorName).toBe('Newer Version 2');
    });
  });

  describe('5. Post-Submission Cleanup & In-Flight Modification Preservation', () => {
    it('clears the exact submitted draft when version matches', async () => {
      const key = buildShiftCollectionsKey('Mitali', 'dsm-submit', '2026-11-18', 'A', 1)!;

      await saveShiftCollectionsDraft({
        key,
        stationId: 'Mitali',
        dsmId: 'dsm-submit',
        shiftDate: '2026-11-18',
        shiftType: 'A',
        pumpId: 1,
        debtorEntries: [{ debtorName: 'Submitted Debtor', amount: 500, time: '12:00' }],
        machineTransactions: [],
        version: 1,
        updatedAt: new Date().toISOString(),
      });

      const cleared = await clearShiftCollectionsDraftIfMatching(key, 1);
      expect(cleared).toBe(true);

      const remaining = await getShiftCollectionsDraft(key);
      expect(remaining).toBeNull();
    });

    it('preserves newer draft data if user made changes while submission was in-flight', async () => {
      const key = buildShiftCollectionsKey('Mitali', 'dsm-submit', '2026-11-18', 'A', 1)!;

      // Submission started at version 1, but user added another entry before submission finished (version 2)
      await saveShiftCollectionsDraft({
        key,
        stationId: 'Mitali',
        dsmId: 'dsm-submit',
        shiftDate: '2026-11-18',
        shiftType: 'A',
        pumpId: 1,
        debtorEntries: [
          { debtorName: 'Submitted Debtor', amount: 500, time: '12:00' },
          { debtorName: 'New Debtor Added In Flight', amount: 900, time: '12:05' },
        ],
        machineTransactions: [],
        version: 2,
        updatedAt: new Date().toISOString(),
      });

      // Submission of version 1 completes and attempts to clear
      const cleared = await clearShiftCollectionsDraftIfMatching(key, 1);
      expect(cleared).toBe(false); // Indicates newer draft preserved!

      const current = await getShiftCollectionsDraft(key);
      expect(current).not.toBeNull();
      expect(current?.version).toBe(2);
      expect(current?.debtorEntries).toHaveLength(2);
    });
  });

  describe('6. Zero Network Requests Verification', () => {
    it('guarantees zero HTTP/fetch requests are executed during get, save, and clear operations', async () => {
      const key = buildShiftCollectionsKey('Mitali', 'dsm-net', '2026-11-18', 'A', 1)!;

      await saveShiftCollectionsDraft({
        key,
        stationId: 'Mitali',
        dsmId: 'dsm-net',
        shiftDate: '2026-11-18',
        shiftType: 'A',
        pumpId: 1,
        debtorEntries: [{ debtorName: 'Offline Test', amount: 100, time: '10:00' }],
        machineTransactions: [],
        version: 1,
        updatedAt: new Date().toISOString(),
      });

      await getShiftCollectionsDraft(key);
      await clearShiftCollectionsDraftIfMatching(key, 1);

      // Verify zero network calls were made
      expect(fetchSpy).not.toHaveBeenCalled();
    });
  });

  describe('7. Manual Draft Reset (clearShiftCollectionsDraft)', () => {
    it('unconditionally removes the draft from IndexedDB for explicit user reset', async () => {
      const key = buildShiftCollectionsKey('Mitali', 'dsm-reset', '2026-11-18', 'A', 1)!;

      await saveShiftCollectionsDraft({
        key,
        stationId: 'Mitali',
        dsmId: 'dsm-reset',
        shiftDate: '2026-11-18',
        shiftType: 'A',
        pumpId: 1,
        debtorEntries: [{ debtorName: 'Debtor To Reset', amount: 300, time: '14:00' }],
        machineTransactions: [],
        version: 5,
        updatedAt: new Date().toISOString(),
      });

      const before = await getShiftCollectionsDraft(key);
      expect(before).not.toBeNull();
      expect(before?.debtorEntries).toHaveLength(1);

      // Perform user-initiated draft clear
      await clearShiftCollectionsDraft(key);

      const after = await getShiftCollectionsDraft(key);
      expect(after).toBeNull();
    });

    it('handles clearing a non-existent draft gracefully without error', async () => {
      const nonExistentKey = buildShiftCollectionsKey('Mitali', 'dsm-none', '2026-11-18', 'A', 1)!;
      await expect(clearShiftCollectionsDraft(nonExistentKey)).resolves.not.toThrow();
    });

    it('handles empty or null key safely without error', async () => {
      await expect(clearShiftCollectionsDraft('')).resolves.not.toThrow();
    });

    it('Scenario B: verifies concurrent autosave and clear sequence, reload, and non-resurrection', async () => {
      const activeKey = buildShiftCollectionsKey('Mitali', 'dsm-race', '2026-11-18', 'A', 1)!;
      const unrelatedKey = buildShiftCollectionsKey('Mitali', 'dsm-other', '2026-11-18', 'B', 2)!;

      // Unrelated shift draft already exists
      await saveShiftCollectionsDraft({
        key: unrelatedKey,
        stationId: 'Mitali',
        dsmId: 'dsm-other',
        shiftDate: '2026-11-18',
        shiftType: 'B',
        pumpId: 2,
        debtorEntries: [{ debtorName: 'Unrelated Shift Debtor', amount: 800, time: '15:00' }],
        machineTransactions: [],
        version: 1,
        updatedAt: new Date().toISOString(),
      });

      // 1. Start an autosave with active debtor entry
      const autosave1 = saveShiftCollectionsDraft({
        key: activeKey,
        stationId: 'Mitali',
        dsmId: 'dsm-race',
        shiftDate: '2026-11-18',
        shiftType: 'A',
        pumpId: 1,
        debtorEntries: [{ debtorName: 'Active Debtor 1', amount: 500, time: '09:00' }],
        machineTransactions: [],
        version: 1,
        updatedAt: new Date().toISOString(),
      });

      // 2. Trigger Clear Collections Draft immediately (serialized via FIFO write queue)
      const clearOp = clearShiftCollectionsDraft(activeKey);

      // 3. Attempt another autosave during the clearing operation
      // As in SubmitShiftScreen, if clearing is active or collections are emptied, the save payload is empty or skipped
      // But even if a queued empty save were submitted:
      await Promise.all([autosave1, clearOp]);

      // Verify active key is completely gone
      const activeDraftAfterClear = await getShiftCollectionsDraft(activeKey);
      expect(activeDraftAfterClear).toBeNull();

      // 4. Reload the application (simulate cold restart with new connection)
      setIDBFactoryForTesting(createMockIDBFactory(backingStore));

      // 5. Verify cleared entries do not return
      const reloadedActive = await getShiftCollectionsDraft(activeKey);
      expect(reloadedActive).toBeNull();

      // 6. Verify unrelated draft data remains intact
      const reloadedUnrelated = await getShiftCollectionsDraft(unrelatedKey);
      expect(reloadedUnrelated).not.toBeNull();
      expect(reloadedUnrelated?.debtorEntries[0].debtorName).toBe('Unrelated Shift Debtor');
      expect(reloadedUnrelated?.debtorEntries[0].amount).toBe(800);
    });
  });
});
