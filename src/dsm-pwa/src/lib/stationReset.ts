import { db } from './db';
import { supabase } from './supabase';

export async function resetStationData(newStationId: string): Promise<void> {
  // 1. Clear IndexedDB (Dexie)
  await db.drafts.clear();
  await db.submissions.clear();
  await db.products.clear();
  await db.stockBalances.clear();

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

  // 5. Perform initial synchronization for products and stock
  const { data: products } = await supabase
    .from('ProductMasters')
    .select('Id, ProductName, Category, Unit, DefaultSaleRate')
    .eq('IsActive', true);

  const { data: logs } = await supabase
    .from('OilDefDailyLogs')
    .select('ProductId, RemainingStock, LogDate')
    .order('LogDate', { ascending: false });

  const latestStocks: Record<number, number> = {};
  if (logs) {
    for (const log of logs) {
      const prodId = log.ProductId;
      if (latestStocks[prodId] === undefined) {
        latestStocks[prodId] = log.RemainingStock || 0;
      }
    }
  }

  await db.transaction('rw', db.products, db.stockBalances, async () => {
    await db.products.clear();
    await db.stockBalances.clear();

    if (products) {
      for (const p of products) {
        await db.products.put({
          id: p.Id,
          productName: p.ProductName,
          category: p.Category,
          unit: p.Unit,
          defaultSaleRate: p.DefaultSaleRate
        });

        await db.stockBalances.put({
          productId: p.Id,
          remainingStock: latestStocks[p.Id] || 0
        });
      }
    }
  });

  // 6. Persist new Station ID
  localStorage.setItem('current_station_id', newStationId);
}
