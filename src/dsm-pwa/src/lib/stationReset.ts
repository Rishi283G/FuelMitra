import { syncProductListAndStockDirect } from './productSync';

/**
 * Resets station-specific volatile caches and synchronizes authoritative data for the new station.
 * 
 * Critical Safeguards:
 * 1. Multi-station offline drafts & submissions in IndexedDB are strictly PRESERVED to prevent data loss.
 *    Each draft is partitioned by stationId; cross-station contamination is prevented by indexed station queries.
 * 2. Static application shell Service Worker caches (e.g. 'pyrosync-dsm-*') are strictly PRESERVED
 *    to protect offline shell and asset functionality.
 * 3. Volatile caches (creditors, vehicles), sessionStorage, and station-scoped settings are cleared.
 * 4. Products and stock balances are refreshed atomically in a transaction scoped to newStationId.
 * 5. Active station ID in localStorage is updated to the validated newStationId.
 */
export async function resetStationData(newStationId: string): Promise<void> {
  const cleanNewStation = (newStationId || '').trim();
  if (!cleanNewStation) return;

  // 1. Clear station-specific volatile localStorage (do NOT clear Supabase auth tokens or device ID)
  localStorage.removeItem('cached_creditors');
  localStorage.removeItem('cached_vehicles');

  // 2. Clear sessionStorage
  sessionStorage.clear();

  // 3. Clear ONLY station-specific data caches in CacheStorage, preserving static application shell caches
  if (typeof caches !== 'undefined' && 'keys' in caches) {
    try {
      const keys = await caches.keys();
      for (const key of keys) {
        // Protect static app shell caches (e.g. pyrosync-dsm-v*)
        if (!key.startsWith('pyrosync-dsm-')) {
          await caches.delete(key);
        }
      }
    } catch (cacheErr) {
      console.warn('[resetStationData] Could not inspect CacheStorage:', cacheErr);
    }
  }

  // 4. Perform atomic synchronization for products and stock scoped to cleanNewStation
  await syncProductListAndStockDirect(cleanNewStation);

  // 5. Persist validated new Station ID
  localStorage.setItem('current_station_id', cleanNewStation);
  localStorage.setItem('last_station_id', cleanNewStation);
}
