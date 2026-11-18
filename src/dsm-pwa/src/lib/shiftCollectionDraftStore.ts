/**
 * shiftCollectionDraftStore.ts
 * 
 * Isolated, browser-local IndexedDB persistence for DSM shift collections
 * (debtor entries and machine OTP transactions).
 * 
 * Strict Guarantees:
 * 1. Zero network calls or server load.
 * 2. Scoped strictly to (stationId, authenticated dsmId, shiftDate, shiftType, pumpId).
 * 3. Preserves complete debtor and OTP objects without stripping metadata.
 * 4. Serialized FIFO writes with monotonic versioning to prevent out-of-order overwrites.
 * 5. Commits confirmed via transaction.oncomplete.
 * 6. Submission cleanup clears ONLY the exact submitted draft (matching version),
 *    preserving newer modifications made while submission was in-flight.
 */

import type { OtpTransactionItem } from './stationConfig';

export interface DebtorEntry {
  debtorName: string;
  amount: number;
  vehicleNumber?: string;
  slipNumber?: string;
  time: string;
  [key: string]: any;
}

export type { OtpTransactionItem };

export interface ShiftCollectionsDraft {
  key: string;
  stationId: string;
  dsmId: string;
  shiftDate: string;
  shiftType: string;
  pumpId: number;
  debtorEntries: DebtorEntry[];
  machineTransactions: OtpTransactionItem[];
  version: number;
  updatedAt: string;
}

const DB_NAME = 'PyroSyncCollectionsDraftDB';
const DB_VERSION = 1;
const STORE_NAME = 'collectionsDrafts';

let customIDBFactory: IDBFactory | null = null;

/**
 * Allows injecting custom IDBFactory in testing environments.
 */
export function setIDBFactoryForTesting(factory: IDBFactory | null): void {
  customIDBFactory = factory;
}

function getIDBFactory(): IDBFactory {
  if (customIDBFactory) return customIDBFactory;
  if (typeof indexedDB !== 'undefined') return indexedDB;
  if (typeof window !== 'undefined' && window.indexedDB) return window.indexedDB;
  throw new Error('IndexedDB is not supported or available in this environment.');
}

/**
 * Opens a connection to the isolated collections draft database.
 */
export function openCollectionsDB(): Promise<IDBDatabase> {
  const factory = getIDBFactory();
  return new Promise((resolve, reject) => {
    const request = factory.open(DB_NAME, DB_VERSION);

    request.onupgradeneeded = (_event) => {
      const db = request.result;
      if (!db.objectStoreNames.contains(STORE_NAME)) {
        db.createObjectStore(STORE_NAME, { keyPath: 'key' });
      }
    };

    request.onsuccess = () => {
      resolve(request.result);
    };

    request.onerror = () => {
      reject(request.error || new Error('Failed to open IndexedDB collections draft database.'));
    };

    request.onblocked = () => {
      console.warn('[CollectionsDraftStore] Database open request is blocked.');
    };
  });
}

/**
 * Builds the canonical storage key.
 * Returns null if any identity attribute is missing or unresolved.
 * 
 * Requires:
 * - Valid stationId
 * - Stable authenticated dsmId (SyncGuid or AuthUserId, NEVER FullName alone)
 * - Valid shiftDate (YYYY-MM-DD)
 * - Valid shiftType ('A' | 'B' | 'C')
 * - Positive pumpId (> 0)
 */
export function buildShiftCollectionsKey(
  stationId?: string | null,
  dsmId?: string | null,
  shiftDate?: string | null,
  shiftType?: string | null,
  pumpId?: number | string | null
): string | null {
  if (!stationId || !stationId.trim()) return null;
  if (!dsmId || !dsmId.trim()) return null;
  if (!shiftDate || !shiftDate.trim()) return null;
  if (!shiftType || !shiftType.trim()) return null;

  const numPump = typeof pumpId === 'number' ? pumpId : Number(pumpId);
  if (isNaN(numPump) || numPump <= 0) return null;

  return [
    'dsm-coll',
    stationId.trim().toLowerCase(),
    dsmId.trim().toLowerCase(),
    shiftDate.trim(),
    shiftType.trim().toUpperCase(),
    String(numPump)
  ].join(':');
}

/**
 * Retrieves the persisted shift collections draft for a given canonical key.
 */
export async function getShiftCollectionsDraft(
  key: string
): Promise<ShiftCollectionsDraft | null> {
  if (!key) return null;
  const db = await openCollectionsDB();

  return new Promise((resolve, reject) => {
    try {
      const tx = db.transaction(STORE_NAME, 'readonly');
      const store = tx.objectStore(STORE_NAME);
      const req = store.get(key);

      req.onsuccess = () => {
        resolve((req.result as ShiftCollectionsDraft) || null);
      };

      req.onerror = () => {
        reject(req.error || new Error(`Failed to read draft for key ${key}`));
      };

      tx.onabort = () => {
        reject(tx.error || new Error('Read transaction aborted.'));
      };
    } catch (err) {
      reject(err);
    }
  });
}

// Write queue for FIFO serialization of asynchronous writes
let writeQueue: Promise<any> = Promise.resolve();

function enqueueWrite<T>(op: () => Promise<T>): Promise<T> {
  const result = writeQueue.then(op, op);
  writeQueue = result;
  return result;
}

/**
 * Saves the shift collections draft to IndexedDB.
 * Serializes writes and guarantees transaction commit via tx.oncomplete.
 * Prevents an older draft snapshot from overwriting a newer draft version.
 */
export function saveShiftCollectionsDraft(
  draft: ShiftCollectionsDraft
): Promise<void> {
  return enqueueWrite(async () => {
    if (!draft || !draft.key) {
      throw new Error('Cannot save draft without valid key.');
    }

    const db = await openCollectionsDB();

    return new Promise((resolve, reject) => {
      try {
        const tx = db.transaction(STORE_NAME, 'readwrite');
        const store = tx.objectStore(STORE_NAME);

        // Version check before overwrite
        const getReq = store.get(draft.key);

        getReq.onsuccess = () => {
          const existing = getReq.result as ShiftCollectionsDraft | undefined;
          if (existing && typeof existing.version === 'number' && existing.version > draft.version) {
            // A newer draft is already committed. Skip older snapshot.
            return;
          }

          store.put(draft);
        };

        getReq.onerror = () => {
          reject(getReq.error || new Error(`Failed checking existing draft for key ${draft.key}`));
        };

        tx.oncomplete = () => {
          // Transaction has committed to persistent disk
          resolve();
        };

        tx.onerror = () => {
          reject(tx.error || new Error(`Write transaction failed for key ${draft.key}`));
        };

        tx.onabort = () => {
          reject(tx.error || new Error(`Write transaction aborted for key ${draft.key}`));
        };
      } catch (err) {
        reject(err);
      }
    });
  });
}

/**
 * Clears the shift collections draft only if its version does NOT exceed maxSubmittedVersion.
 * If new changes were made while submission was in-flight (version > maxSubmittedVersion),
 * the newer draft is preserved.
 * 
 * Returns true if the draft was cleared, or false if newer data was preserved.
 */
export function clearShiftCollectionsDraftIfMatching(
  key: string,
  maxSubmittedVersion: number
): Promise<boolean> {
  return enqueueWrite(async () => {
    if (!key) return false;
    const db = await openCollectionsDB();

    return new Promise((resolve, reject) => {
      try {
        const tx = db.transaction(STORE_NAME, 'readwrite');
        const store = tx.objectStore(STORE_NAME);

        const getReq = store.get(key);

        let wasCleared = false;
        getReq.onsuccess = () => {
          const existing = getReq.result as ShiftCollectionsDraft | undefined;
          if (!existing) {
            // Nothing to clear
            return;
          }

          if (typeof existing.version === 'number' && existing.version > maxSubmittedVersion) {
            // Newer changes exist. Preserve them!
            return;
          }

          store.delete(key);
          wasCleared = true;
        };

        getReq.onerror = () => {
          reject(getReq.error || new Error(`Failed reading draft for clear on key ${key}`));
        };

        tx.oncomplete = () => {
          resolve(wasCleared);
        };

        tx.onerror = () => {
          reject(tx.error || new Error(`Clear transaction failed for key ${key}`));
        };

        tx.onabort = () => {
          reject(tx.error || new Error(`Clear transaction aborted for key ${key}`));
        };
      } catch (err) {
        reject(err);
      }
    });
  });
}
