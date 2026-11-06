import { db } from './db';
import { supabase } from './supabase';

/**
 * Per-station mutex map to prevent concurrent overlapping synchronization runs for the same station.
 * Simultaneous calls for the same station coalesce to the same shared in-flight promise.
 * Distinct stations synchronize independently and never block, share, or skip each other's synchronization.
 */
const activeSyncPromisesByStation = new Map<string, Promise<void>>();

/**
 * Authoritative synchronization of ProductMasters and OilDefDailyLogs into Dexie IndexedDB.
 * Enforces:
 * 1. Station isolation: Scoped strictly to effectiveStationId (aborts safely if absent).
 * 2. Remote schema compatibility: Queries actual Supabase columns (local_id, SyncGuid, etc.).
 * 3. Atomic cache replacement: Verifies payload validity before clearing local Dexie tables.
 * 4. Backward compatibility: Validates and maps local_id/Id to positive integer keys.
 * 5. Flexible Stock mapping: Resolves remaining stock by both SyncGuid and integer ProductId.
 * 6. Per-station concurrency safety: Coalesces concurrent calls per station without cross-station interference.
 * 7. Stock failure preservation: If stock log query fails, preserves existing local caches.
 * 8. Safe empty stock handling: If remote has no stock entry for a valid product, preserves existing cached stock.
 */
export async function syncProductListAndStockDirect(stationIdOverride?: string): Promise<void> {
  if (typeof navigator !== 'undefined' && navigator.onLine === false) return;

  const effectiveStationId = (
    stationIdOverride ||
    (typeof localStorage !== 'undefined' ? localStorage.getItem('current_station_id') : null) ||
    (typeof localStorage !== 'undefined' ? localStorage.getItem('last_station_id') : null) ||
    ''
  ).trim();

  if (!effectiveStationId) {
    console.warn('[syncProductListAndStock] Skipping product sync: No authenticated station_id available.');
    return;
  }

  const existingPromise = activeSyncPromisesByStation.get(effectiveStationId);
  if (existingPromise) {
    return existingPromise;
  }

  const syncOperation = async () => {
    try {
      // 1. Fetch active products scoped to this station
      const { data: products, error: pErr } = await supabase
        .from('ProductMasters')
        .select('local_id, SyncGuid, ProductName, Category, Unit, DefaultSaleRate, station_id')
        .eq('station_id', effectiveStationId)
        .eq('IsActive', true);

      if (pErr) {
        console.error('[syncProductListAndStock] Failed to fetch ProductMasters from cloud:', pErr);
        return;
      }

      // Safeguard: validate products array. If empty or invalid, preserve local cache!
      if (!products || !Array.isArray(products) || products.length === 0) {
        console.warn(`[syncProductListAndStock] No active products returned for station ${effectiveStationId}. Preserving existing local cache.`);
        return;
      }

      // Validate and map product entities to integer IDs
      const validProducts: Array<{
        id: number;
        productName: string;
        category: string;
        unit: string;
        defaultSaleRate: number;
        syncGuid?: string;
      }> = [];

      for (const p of products) {
        const rawId = p.local_id ?? (p as any).Id ?? (p as any).id;
        const numId = Number(rawId);
        if (!Number.isInteger(numId) || numId <= 0) {
          console.warn('[syncProductListAndStock] Skipping invalid product record with non-positive integer ID:', p);
          continue;
        }

        validProducts.push({
          id: numId,
          productName: (p.ProductName || '').trim() || `Product ${numId}`,
          category: (p.Category || 'Oil').trim(),
          unit: (p.Unit || 'Litres').trim(),
          defaultSaleRate: Number(p.DefaultSaleRate) || 0,
          syncGuid: p.SyncGuid || undefined,
        });
      }

      if (validProducts.length === 0) {
        console.warn('[syncProductListAndStock] No valid products after ID verification. Preserving local cache.');
        return;
      }

      // 2. Fetch latest stock balances (remaining stock from OilDefDailyLogs scoped to station)
      const { data: logs, error: lErr } = await supabase
        .from('OilDefDailyLogs')
        .select('ProductId, RemainingStock, LogDate, station_id')
        .eq('station_id', effectiveStationId)
        .order('LogDate', { ascending: false });

      if (lErr) {
        console.error('[syncProductListAndStock] Failed to fetch OilDefDailyLogs from cloud:', lErr);
        // Abort to prevent clearing existing stock balances on remote fetch error
        return;
      }

      // Read existing cached stock balances to avoid accidentally zeroing out existing local data when remote logs are empty
      const existingStocks = typeof db.stockBalances?.toArray === 'function'
        ? await db.stockBalances.toArray()
        : [];
      const existingStockMap = new Map<number, number>(
        existingStocks.map((s) => [s.productId, s.remainingStock])
      );

      // Map stocks by both SyncGuid and numeric ProductId
      const stocksByGuid: Record<string, number> = {};
      const stocksByLocalId: Record<number, number> = {};
      if (logs && Array.isArray(logs)) {
        for (const log of logs) {
          const rawPid = log.ProductId;
          const remaining = Number(log.RemainingStock) || 0;
          if (rawPid !== null && rawPid !== undefined) {
            const strId = String(rawPid);
            if (stocksByGuid[strId] === undefined) {
              stocksByGuid[strId] = remaining;
            }
            const numPid = Number(rawPid);
            if (Number.isInteger(numPid) && numPid > 0 && stocksByLocalId[numPid] === undefined) {
              stocksByLocalId[numPid] = remaining;
            }
          }
        }
      }

      // 3. Atomically replace products and stock balances in a single transaction
      await db.transaction('rw', db.products, db.stockBalances, async () => {
        await db.products.clear();
        await db.stockBalances.clear();

        for (const vp of validProducts) {
          await db.products.put({
            id: vp.id,
            productName: vp.productName,
            category: vp.category,
            unit: vp.unit,
            defaultSaleRate: vp.defaultSaleRate,
          });

          // Resolve stock: priority to remote syncGuid -> remote localId -> existing cached balance -> 0
          let stock: number;
          if (vp.syncGuid && stocksByGuid[vp.syncGuid] !== undefined) {
            stock = stocksByGuid[vp.syncGuid];
          } else if (stocksByLocalId[vp.id] !== undefined) {
            stock = stocksByLocalId[vp.id];
          } else if (existingStockMap.has(vp.id)) {
            stock = existingStockMap.get(vp.id)!;
          } else {
            stock = 0;
          }

          await db.stockBalances.put({
            productId: vp.id,
            remainingStock: stock,
          });
        }
      });
    } catch (err) {
      console.error('[syncProductListAndStock] Failed to sync product list and stock:', err);
    }
  };

  const syncPromise = syncOperation().finally(() => {
    activeSyncPromisesByStation.delete(effectiveStationId);
  });

  activeSyncPromisesByStation.set(effectiveStationId, syncPromise);

  return syncPromise;
}
