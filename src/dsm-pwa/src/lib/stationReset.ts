import { db } from './db';
import { syncProductListAndStockDirect } from './productSync';

export async function resetStationData(newStationId: string): Promise<void> {
  // 1. Clear IndexedDB submission & draft records (station-specific)
  // Local products and stock caches are preserved until validated new data is ready for atomic replacement
  await db.drafts.clear();
  await db.submissions.clear();

  // 2. Clear station-specific localStorage (do NOT clear Supabase auth tokens or device ID)
  localStorage.removeItem('cached_creditors');
  localStorage.removeItem('cached_vehicles');

  // 3. Clear sessionStorage
  sessionStorage.clear();

  // 4. Clear Service Worker caches
  if (typeof caches !== 'undefined' && 'keys' in caches) {
    const keys = await caches.keys();
    for (const key of keys) {
      await caches.delete(key);
    }
  }

  // 5. Perform initial synchronization for products and stock scoped to newStationId
  await syncProductListAndStockDirect(newStationId);

  // 6. Persist new Station ID
  localStorage.setItem('current_station_id', newStationId);
}
