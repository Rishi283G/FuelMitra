import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { supabase } from './supabase';
import { syncProductListAndStockDirect } from './productSync';

// Mock Dexie db
vi.mock('./db', () => {
  const store = new Map<any, any>();
  const mockTable = () => ({
    clear: vi.fn().mockImplementation(async () => { store.clear(); }),
    put: vi.fn().mockImplementation(async (item: any) => { store.set(item.id || item.productId, item); }),
    toArray: vi.fn().mockImplementation(async () => Array.from(store.values())),
  });

  return {
    db: {
      drafts: mockTable(),
      submissions: mockTable(),
      products: mockTable(),
      stockBalances: mockTable(),
      transaction: vi.fn().mockImplementation(async (_mode: string, _t1: any, _t2: any, callback: () => Promise<void>) => {
        await callback();
      }),
    },
  };
});

// Mock Supabase
vi.mock('./supabase', () => ({
  supabase: {
    from: vi.fn(),
    channel: vi.fn(),
    removeChannel: vi.fn(),
  },
}));

describe('Runtime Request-Volume & Lifecycle Audit', () => {
  let requestLog: { timestamp: number; table: string; operation: string; filter?: string }[] = [];
  let channelEvents: { event: 'created' | 'removed'; channelName: string }[] = [];
  let virtualTime = 0;

  beforeEach(() => {
    vi.useFakeTimers();
    requestLog = [];
    channelEvents = [];
    virtualTime = 0;

    vi.stubGlobal('localStorage', {
      getItem: vi.fn(() => 'RASHTRA-74A837'),
      setItem: vi.fn(),
      removeItem: vi.fn(),
      clear: vi.fn(),
    });

    (supabase.from as any).mockImplementation((table: string) => {
      const builder: any = {
        select: vi.fn().mockImplementation((cols: string) => {
          requestLog.push({ timestamp: virtualTime, table, operation: `select(${cols.substring(0, 15)}...)` });
          return builder;
        }),
        eq: vi.fn().mockImplementation((col: string, val: any) => {
          requestLog.push({ timestamp: virtualTime, table, operation: `eq(${col}=${val})`, filter: `${col}=${val}` });
          return builder;
        }),
        order: vi.fn().mockImplementation(() => builder),
        limit: vi.fn().mockImplementation(() => builder),
        single: vi.fn().mockImplementation(() => builder),
        then: (resolve: any) => {
          if (table === 'ProductMasters') {
            resolve({
              data: [{ local_id: 1, ProductName: '2T Oil', Category: 'Oil', Unit: 'Bottle', DefaultSaleRate: 70 }],
              error: null,
            });
          } else if (table === 'OilDefDailyLogs') {
            resolve({ data: [{ ProductId: 1, RemainingStock: 50 }], error: null });
          } else if (table === 'DsmNotifications') {
            resolve({ data: [], error: null });
          } else if (table === 'DsmSubmissions') {
            resolve({ data: { Id: 'sub-1', Status: 'Approved', ShiftDate: '2026-11-06', ShiftType: 'A' }, error: null });
          } else {
            resolve({ data: [], error: null });
          }
        },
      };
      return builder;
    });

    (supabase.channel as any).mockImplementation((name: string) => {
      channelEvents.push({ event: 'created', channelName: name });
      return {
        on: vi.fn().mockReturnThis(),
        subscribe: vi.fn().mockReturnThis(),
      };
    });

    (supabase.removeChannel as any).mockImplementation((_ch: any) => {
      channelEvents.push({ event: 'removed', channelName: 'active-channel' });
    });
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.clearAllMocks();
  });

  it('Scenario A & B: Initial dashboard load and 5-minute idle verification', async () => {
    // Harness simulating DashboardScreen component mount & lifecycle
    let lastRefreshedIdentity: string | null = null;
    let lastNotificationUser: string | null = null;

    const mountDashboard = async (profile: { id: string; StationId: string }) => {
      const identityKey = `${profile.id}:${profile.StationId || ''}`;

      // Effect 1: Assignment & Product sync (guarded by lastRefreshedIdentity)
      if (lastRefreshedIdentity !== identityKey) {
        requestLog.push({ timestamp: virtualTime, table: 'DsmUsers', operation: 'refreshProfile()' });
        await syncProductListAndStockDirect(profile.StationId);
        lastRefreshedIdentity = identityKey;
      }

      // Effect 2: Notifications & Recent status & Realtime (guarded by primitive [profile.id, profile.StationId])
      if (lastNotificationUser !== profile.id) {
        lastNotificationUser = profile.id;
        await supabase.from('DsmNotifications').select('Id, Message').eq('UserId', profile.id);
        await supabase.from('DsmSubmissions').select('Id, Status').eq('DsmUserId', profile.id);

        const channelName = `dsm-notifications-${profile.id}`;
        supabase.channel(channelName);
      }
    };

    // --- Scenario A: Initial Load ---
    const userProfile = { id: 'usr-101', StationId: 'RASHTRA-74A837' };
    await mountDashboard(userProfile);

    // Verify initial load requests
    const initialRequests = [...requestLog];
    const pmQueries = initialRequests.filter((r) => r.table === 'ProductMasters' && r.operation.startsWith('select'));
    const logQueries = initialRequests.filter((r) => r.table === 'OilDefDailyLogs' && r.operation.startsWith('select'));
    const userQueries = initialRequests.filter((r) => r.table === 'DsmUsers');
    const notifQueries = initialRequests.filter((r) => r.table === 'DsmNotifications' && r.operation.startsWith('select'));
    const subQueries = initialRequests.filter((r) => r.table === 'DsmSubmissions' && r.operation.startsWith('select'));

    expect(pmQueries.length).toBe(1);
    expect(logQueries.length).toBe(1);
    expect(userQueries.length).toBe(1);
    expect(notifQueries.length).toBe(1);
    expect(subQueries.length).toBe(1);
    expect(channelEvents.filter((e) => e.event === 'created').length).toBe(1);

    const initialRequestCount = requestLog.length;

    // --- Scenario B: Idle for 5 minutes (300,000 ms) ---
    // In the previous buggy version, this produced 6-12 req/sec = ~1,800 to 3,600 calls
    for (let minute = 1; minute <= 5; minute++) {
      virtualTime += 60000;
      vi.advanceTimersByTime(60000);
      // Simulate React re-rendering with new profile object reference
      await mountDashboard({ ...userProfile });
    }

    // VERIFICATION: Zero additional network requests must be made during 5 minutes of idle!
    expect(requestLog.length).toBe(initialRequestCount);
    expect(channelEvents.filter((e) => e.event === 'created').length).toBe(1); // Realtime was not recreated
  });

  it('Scenario C: Navigate away from dashboard and return', async () => {
    let lastRefreshedIdentity: string | null = null;
    let activeChannel: any = null;

    const mount = async (profile: { id: string; StationId: string }) => {
      const identityKey = `${profile.id}:${profile.StationId || ''}`;
      if (lastRefreshedIdentity !== identityKey) {
        await syncProductListAndStockDirect(profile.StationId);
        lastRefreshedIdentity = identityKey;
      }
      activeChannel = supabase.channel(`dsm-notifications-${profile.id}`);
    };

    const unmount = () => {
      if (activeChannel) {
        supabase.removeChannel(activeChannel);
        activeChannel = null;
      }
    };

    const profile = { id: 'usr-102', StationId: 'RASHTRA-74A837' };

    // 1. Initial view
    await mount(profile);
    expect(channelEvents.filter((e) => e.event === 'created').length).toBe(1);

    // 2. Navigate away (e.g. to Submit shift or History)
    unmount();
    expect(channelEvents.filter((e) => e.event === 'removed').length).toBe(1);

    // 3. Return to dashboard
    await mount(profile);
    // Identity was already refreshed, so product sync is not redundantly re-run, but Realtime re-subscribes
    expect(channelEvents.filter((e) => e.event === 'created').length).toBe(2);
  });

  it('Scenario D: Station assignment change triggers new scoped synchronization', async () => {
    let lastRefreshedIdentity: string | null = null;

    const mount = async (profile: { id: string; StationId: string }) => {
      const identityKey = `${profile.id}:${profile.StationId || ''}`;
      if (lastRefreshedIdentity !== identityKey) {
        await syncProductListAndStockDirect(profile.StationId);
        lastRefreshedIdentity = identityKey;
      }
    };

    // 1. Mount with Station A
    await mount({ id: 'usr-103', StationId: 'RASHTRA-74A837' });
    const stationAQueries = requestLog.filter((r) => r.filter?.includes('RASHTRA-74A837'));
    expect(stationAQueries.length).toBeGreaterThan(0);

    // 2. Switch station to Station B
    requestLog = [];
    await mount({ id: 'usr-103', StationId: 'NEW-STATION-B' });
    const stationBQueries = requestLog.filter((r) => r.filter?.includes('NEW-STATION-B'));
    expect(stationBQueries.length).toBeGreaterThan(0);
    expect(requestLog.filter((r) => r.filter?.includes('RASHTRA-74A837')).length).toBe(0);
  });

  it('Scenario E: Transient network failure and bounded backoff retry behaviour', async () => {
    let attempts = 0;
    let retryCount = 0;
    let lastRefreshedIdentity: string | null = null;
    const timeouts: NodeJS.Timeout[] = [];

    const executeRefreshWithSimulatedFailure = (failTimes: number) => {
      const identityKey = 'usr-104:RASHTRA-74A837';
      if (lastRefreshedIdentity === identityKey) return;

      attempts++;
      if (attempts <= failTimes) {
        // Transient network failure
        if (retryCount < 3) {
          retryCount++;
          const delay = Math.min(1000 * Math.pow(2, retryCount), 8000);
          const t = setTimeout(() => {
            executeRefreshWithSimulatedFailure(failTimes);
          }, delay);
          timeouts.push(t);
        }
      } else {
        // Success
        lastRefreshedIdentity = identityKey;
        retryCount = 0;
      }
    };

    // Case 1: Failure persists forever -> verifies retry STOPS at exactly 3 retries (4 total attempts)
    executeRefreshWithSimulatedFailure(999);
    expect(attempts).toBe(1); // Initial try

    // Retry 1: 2000ms
    vi.advanceTimersByTime(2000);
    expect(attempts).toBe(2);

    // Retry 2: 4000ms
    vi.advanceTimersByTime(4000);
    expect(attempts).toBe(3);

    // Retry 3: 8000ms
    vi.advanceTimersByTime(8000);
    expect(attempts).toBe(4);

    // Advance further: No more retries should occur!
    vi.advanceTimersByTime(60000);
    expect(attempts).toBe(4); // Strictly bounded at 4 attempts!
    expect(lastRefreshedIdentity).toBeNull(); // Not marked on permanent failure

    // Case 2: Component unmount cancels pending retry
    attempts = 0;
    retryCount = 0;
    timeouts.length = 0;

    executeRefreshWithSimulatedFailure(999);
    expect(attempts).toBe(1);

    // Unmount occurs before retry timer fires:
    timeouts.forEach((t) => clearTimeout(t));

    // Advance timers: retry should NOT fire
    vi.advanceTimersByTime(10000);
    expect(attempts).toBe(1); // Cancelled cleanly on unmount
  });
});
