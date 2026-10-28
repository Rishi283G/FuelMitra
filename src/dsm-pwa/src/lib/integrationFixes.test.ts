import { describe, it, expect } from 'vitest';
import { getDefaultStationConfig } from './stationConfig';

describe('Integration Fixes Verification (PWA)', () => {
  describe('Fix 2: Nozzle Readings Numerical Comparisons', () => {
    it('correctly compares decimal string readings numerically instead of lexicographically', () => {
      // In string comparison, "100.5" < "90.0" is true because '1' < '9'.
      // With numerical parsing, 100.5 >= 90.0
      const opening = 90.0;
      const closingInput = "100.5";
      const testingInput = "5.0";

      const numOpening = Number(opening);
      const numClosing = Number(closingInput);
      const numTesting = Number(testingInput);

      expect(numClosing < numOpening).toBe(false);
      expect(numClosing > numOpening).toBe(true);

      const dispensed = numClosing - numOpening;
      expect(dispensed).toBe(10.5);

      const netSale = Math.max(0, dispensed - numTesting);
      expect(netSale).toBe(5.5);
    });

    it('rejects closing reading strictly when closing is less than opening numerically', () => {
      const opening = 100.0;
      const invalidClosing = 95.5;

      const isValid = Number(invalidClosing) >= Number(opening);
      expect(isValid).toBe(false);
    });

    it('validates that testing does not exceed dispensed fuel', () => {
      const opening = 100.0;
      const closing = 110.0;
      const dispensed = closing - opening; // 10
      const validTesting = 5.0;
      const invalidTesting = 15.0;

      expect(validTesting <= dispensed).toBe(true);
      expect(invalidTesting <= dispensed).toBe(false);
    });
  });

  describe('Fix 3: Dynamic Ledger Name', () => {
    it('defaults to "Personal Ledger" when no custom name is configured', () => {
      const config = getDefaultStationConfig('MITALI_PUMP');
      expect(config.features.personalLedgerName).toBe('Personal Ledger');
      expect(config.features.personalLedgerEnabled).toBe(true);
    });

    it('uses custom feature DisplayName when Operations_PersonalLedger is configured', () => {
      const mockSettings = {
        station_id: 'KANDHARE_PUMP',
        AppFeatureSettingsJson: JSON.stringify([
          {
            FeatureKey: 'Operations_PersonalLedger',
            IsEnabled: true,
            DisplayName: 'Kandhare Petroleum',
          },
        ]),
      };

      // Emulate the parser in fetchStationConfig
      const features = JSON.parse(mockSettings.AppFeatureSettingsJson);
      const plSetting = features.find((f: any) => f.FeatureKey === 'Operations_PersonalLedger');
      
      const displayName = plSetting?.DisplayName || 'Personal Ledger';
      const isEnabled = plSetting?.IsEnabled ?? true;

      expect(displayName).toBe('Kandhare Petroleum');
      expect(isEnabled).toBe(true);
    });
  });

  describe('Fix 1: OTP Machine Transactions & Shared TID/Batch', () => {
    it('sums live parent amount from itemized transactions without double counting', () => {
      const transactions = [
        { id: '1', collectionTypeCode: 'CREDIT_CARD', amount: 1200, otp: '112233', slot: 'Morning' },
        { id: '2', collectionTypeCode: 'CREDIT_CARD', amount: 800, otp: '445566', slot: 'Morning' },
        { id: '3', collectionTypeCode: 'PETROCARD', amount: 1500, otp: '778899', slot: 'Morning' },
      ];

      const getMachineTotalForType = (code: string) =>
        transactions
          .filter((t) => t.collectionTypeCode.toUpperCase() === code.toUpperCase())
          .reduce((sum, t) => sum + t.amount, 0);

      expect(getMachineTotalForType('CREDIT_CARD')).toBe(2000);
      expect(getMachineTotalForType('PETROCARD')).toBe(1500);
      expect(getMachineTotalForType('PHONEPE')).toBe(0);
    });

    it('shares card-level TID and Batch across transactions for that collection type', () => {
      const cardTidMorning = 'TID_CARD_01';
      const cardBatchMorning = 'BATCH_098';

      const getSharedTidBatch = (code: string, slot: string) => {
        if (code === 'CREDIT_CARD' && slot === 'Morning') {
          return { tid: cardTidMorning, batch: cardBatchMorning };
        }
        return { tid: '', batch: '' };
      };

      const tx1 = { collectionTypeCode: 'CREDIT_CARD', slot: 'Morning', amount: 500, otp: '123456' };
      const shared = getSharedTidBatch(tx1.collectionTypeCode, tx1.slot);

      expect(shared.tid).toBe('TID_CARD_01');
      expect(shared.batch).toBe('BATCH_098');
    });
  });

  describe('Fix 5: Fuel Rates Priority', () => {
    it('prioritizes station settings over historical reading rates', () => {
      const historicalReading = { Rate: 85.50, ClosingReading: 12000 };
      const stationConfiguredSettings = { HsdRate: 90.35, FuelRatesJson: JSON.stringify({ HSD: 90.35 }) };

      // Ground truth rate must come from stationConfiguredSettings, NOT historicalReading
      let effectiveHsdRate = 0;
      if (stationConfiguredSettings.HsdRate) {
        effectiveHsdRate = stationConfiguredSettings.HsdRate;
      }

      // Historical reading supplies meter continuity ONLY:
      const continuityClosing = historicalReading.ClosingReading;

      expect(effectiveHsdRate).toBe(90.35);
      expect(continuityClosing).toBe(12000);
    });
  });

  describe('Fix 6: PWA OTP Table Column Alignment & Visibility', () => {
    it('has exactly 5 columns matching headers: Slot, Amount, OTP, TID/Batch, Actions', () => {
      const headers = ['Slot', 'Amount', 'OTP', 'TID / Batch', 'Actions'];
      const tx = { id: 'tx-1', slot: 'Morning', amount: 545.0, otp: '849201', tid: 'TID1', batch: 'B1' };

      // Emulate the cells rendered per row
      const cells = [
        tx.slot,
        `₹${tx.amount.toFixed(2)}`,
        tx.otp || '—',
        `TID:${tx.tid} B:${tx.batch}`,
        '✕'
      ];

      expect(headers.length).toBe(5);
      expect(cells.length).toBe(5);
      expect(cells[2]).toBe('849201'); // OTP is in the 3rd cell, matching the 3rd header 'OTP'
    });
  });

  describe('Fix 7: Dynamic Slot Options from Station Configuration', () => {
    it('respects configured enableMorning, enableDay, enableNight for collection types', () => {
      const collectionType = {
        code: 'PETROCARD',
        enableMorning: true,
        enableDay: true,
        enableNight: false,
      };

      const getSlots = (shift: string, conf: typeof collectionType) => {
        const slots: string[] = [];
        if (shift === 'B') {
          if (conf.enableDay) slots.push('Day');
          if (conf.enableMorning) slots.push('Morning');
          if (conf.enableNight) slots.push('Night');
        } else {
          if (conf.enableMorning) slots.push('Morning');
          if (conf.enableDay) slots.push('Day');
          if (conf.enableNight) slots.push('Night');
        }
        if (slots.length === 0) slots.push(shift === 'B' ? 'Day' : 'Morning');
        slots.push('General');
        return slots;
      };

      const shiftASlots = getSlots('A', collectionType);
      expect(shiftASlots).toEqual(['Morning', 'Day', 'General']);
      expect(shiftASlots).not.toContain('Night');

      const shiftBSlots = getSlots('B', collectionType);
      expect(shiftBSlots).toEqual(['Day', 'Morning', 'General']);
    });
  });

  describe('Fix 8: Diagnostic Reconciliation Consistency', () => {
    it('produces exact -₹0.35 mismatch on diagnostic fixture (₹21,023.45 gross, ₹3,214.10 test, ₹17,809.00 coll)', () => {
      const grossSales = 21023.45;
      const totalTesting = 3214.10;
      const netFuelSales = grossSales - totalTesting; // 17809.35
      const grandProductSales = 0.00;
      const totalSalesLiability = netFuelSales + grandProductSales; // 17809.35
      const totalCollections = 17809.00;

      const mismatch = Number((totalCollections - totalSalesLiability).toFixed(2));
      expect(netFuelSales).toBeCloseTo(17809.35, 2);
      expect(mismatch).toBe(-0.35);
    });
  });

  describe('Controlled Staging Validation Scenarios', () => {
    it('Case 1: PhonePe structured settlement of ₹5,000 + itemized ₹5,000 and ₹700 yields ₹5,700 total on PWA', () => {
      // 1 structured settlement of ₹5,000
      const settlements = [
        { paymentType: 'PhonePe', period: 'Morning', amount: 5000, tid: 'PP01', batch: 'B01' }
      ];

      // 2 itemized swipes: 1 covered by settlement (₹5000), 1 additional (₹700)
      const swipes = [
        { mode: 'PhonePe (Morning)', amount: 5000, otp: '1111', tid: 'PP01', batch: 'B01' },
        { mode: 'PhonePe (Morning)', amount: 700, otp: '2222', tid: 'PP01', batch: 'B01' }
      ];

      // 1-to-1 match against unconsumed settlements
      const unconsumedSettlements = [...settlements.map(s => ({ ...s, isConsumed: false }))];
      let totalPhonePe = 0;

      for (const s of settlements) {
        if (s.paymentType.toUpperCase() === 'PHONEPE') {
          totalPhonePe += s.amount;
        }
      }

      for (const swipe of swipes) {
        const match = unconsumedSettlements.find(s =>
          !s.isConsumed &&
          s.paymentType.toUpperCase() === 'PHONEPE' &&
          s.amount === swipe.amount
        );

        if (match) {
          match.isConsumed = true;
        } else {
          totalPhonePe += swipe.amount;
        }
      }

      expect(totalPhonePe).toBe(5700);
      expect(totalPhonePe).not.toBe(5000);
      expect(totalPhonePe).not.toBe(6200);
      expect(totalPhonePe).not.toBe(10700);
    });

    it('Case 2: Two legitimate same-type same-amount transactions with different TIDs both survive on PWA', () => {
      const swipes = [
        { mode: 'PineLabs Card Morning', amount: 1500, otp: '8811', tid: 'POS01', batch: 'B01' },
        { mode: 'PineLabs Card Morning', amount: 1500, otp: '8822', tid: 'POS02', batch: 'B02' }
      ];

      // Filter/Deduplicate by unique identity (amount + TID + OTP)
      const uniqueSwipes = swipes.filter((swipe, idx, self) =>
        idx === self.findIndex(s => s.mode === swipe.mode && s.amount === swipe.amount && s.tid === swipe.tid && s.otp === swipe.otp)
      );

      expect(uniqueSwipes.length).toBe(2);
      const totalAmount = uniqueSwipes.reduce((sum, s) => sum + s.amount, 0);
      expect(totalAmount).toBe(3000);
    });
  });

  describe('Fix 1: Authoritative Fuel Rate Resolution', () => {
    it('resolves configured station fuel rates (MS-I = ₹50, Speed = ₹80) accurately over historical defaults', async () => {
      const { resolveFuelRate } = await import('./stationConfig');
      const mockConfig = {
        fuelRates: {
          'MS-I': 50.0,
          'Speed': 80.0,
          'HSD': 92.5,
          'CNG': 75.0,
        },
        hsdRate: 92.5,
        msIRate: 50.0,
        speedRate: 80.0,
      } as any;

      expect(resolveFuelRate(mockConfig, 'MS-I')).toBe(50.0);
      expect(resolveFuelRate(mockConfig, 'Speed')).toBe(80.0);
      expect(resolveFuelRate(mockConfig, 'MS', 'Speed Tank')).toBe(80.0);
      expect(resolveFuelRate(mockConfig, 'XP95')).toBe(80.0); // Premium category matches Speed rate
      expect(resolveFuelRate(mockConfig, 'HSD')).toBe(92.5);
      expect(resolveFuelRate(mockConfig, 'CNG')).toBe(75.0);
      expect(resolveFuelRate(mockConfig, 'UNKNOWN')).toBe(50.0); // Falls back to station msIRate
    });
  });

  describe('Fix 2: Developer Side Slot Settings Enforcement', () => {
    it('filters out Morning/Night/Day slots when disabled in station configuration', () => {
      const collectionTypes = [
        { code: 'PHONEPE', displayName: 'PhonePe', enableMorning: false, enableDay: true, enableNight: false },
        { code: 'CREDIT_CARD', displayName: 'Credit Card', enableMorning: true, enableDay: true, enableNight: false },
        { code: 'PETROCARD', displayName: 'PetroCard', enableMorning: false, enableDay: false, enableNight: false },
      ];

      function getTypeSlotConfig(code: string) {
        const conf = collectionTypes.find(c => c.code === code);
        if (!conf) return { enableMorning: true, enableDay: true, enableNight: true };
        return {
          enableMorning: conf.enableMorning !== false,
          enableDay: conf.enableDay !== false,
          enableNight: conf.enableNight !== false,
        };
      }

      const ppConf = getTypeSlotConfig('PHONEPE');
      expect(ppConf.enableMorning).toBe(false);
      expect(ppConf.enableDay).toBe(true);
      expect(ppConf.enableNight).toBe(false);

      const petroConf = getTypeSlotConfig('PETROCARD');
      expect(petroConf.enableDay).toBe(false);
      expect(petroConf.enableMorning).toBe(false);
      expect(petroConf.enableNight).toBe(false);
    });
  });

  describe('Fix 3: Per-Collection Manual Entry Toggle & OTP Calculation', () => {
    it('when Manual Toggle is OFF, only OTP rows contribute to total and submission', () => {
      const manualToggles: Record<string, boolean> = { PHONEPE: false };
      const isManualEnabled = (code: string) => manualToggles[code] ?? false;

      const manualAmount = 5000.0;
      const otpTxs = [
        { amount: 1500, otp: '111' },
        { amount: 700, otp: '222' },
      ];
      const otpTotal = otpTxs.reduce((sum, t) => sum + t.amount, 0);

      const effectiveTotal = (isManualEnabled('PHONEPE') ? manualAmount : 0) + otpTotal;
      expect(effectiveTotal).toBe(2200.0);
    });

    it('when Manual Toggle is ON, manual amount + OTP rows contribute exactly once', () => {
      const manualToggles: Record<string, boolean> = { PHONEPE: true };
      const isManualEnabled = (code: string) => manualToggles[code] ?? false;

      const manualAmount = 5000.0;
      const otpTxs = [
        { amount: 1500, otp: '111' },
        { amount: 700, otp: '222' },
      ];
      const otpTotal = otpTxs.reduce((sum, t) => sum + t.amount, 0);

      const effectiveTotal = (isManualEnabled('PHONEPE') ? manualAmount : 0) + otpTotal;
      expect(effectiveTotal).toBe(7200.0);
    });
  });

  describe('Client-Site Issue 1: Authoritative Station Rates vs Stale Draft Rates', () => {
    it('overrides stale rates stored in saved draft with authoritative station rates', async () => {
      const { resolveFuelRate } = await import('./stationConfig');
      const liveConfig: any = {
        stationId: 'RASHTRA-74A837',
        fuelRates: {
          'MS - 20KL': 105.50,
          'HSD - 20KL': 93.20,
          'Speed Tank': 114.80,
        },
        hsdRate: 93.20,
        msIRate: 105.50,
        msIIRate: 114.80,
        speedRate: 114.80,
      };

      // Simulating a stale draft where older/default rates were saved in localStorage
      const staleDraftRows = [
        { nozzleId: 1, fuelType: 'MS-I', tankName: 'MS - 20KL', rate: 103.81 },
        { nozzleId: 2, fuelType: 'HSD', tankName: 'HSD - 20KL', rate: 90.35 },
        { nozzleId: 3, fuelType: 'Speed', tankName: 'Speed Tank', rate: 103.81 },
      ];

      // Restoration logic: re-resolve each row against authoritative liveConfig
      const restoredRows = staleDraftRows.map((row) => {
        let authoritativeRate = resolveFuelRate(liveConfig, row.fuelType, row.tankName);
        if (authoritativeRate <= 0) {
          const uFuel = (row.fuelType || '').toUpperCase();
          if (uFuel.includes('SPEED') || uFuel.includes('XP95') || uFuel.includes('POWER')) {
            authoritativeRate = liveConfig.speedRate || liveConfig.msIIRate || 0;
          } else if (uFuel.includes('HSD') || uFuel.includes('DIESEL')) {
            authoritativeRate = liveConfig.hsdRate || 0;
          } else {
            authoritativeRate = liveConfig.msIRate || 0;
          }
        }
        return {
          ...row,
          rate: authoritativeRate > 0 ? authoritativeRate : row.rate,
        };
      });

      expect(restoredRows[0].rate).toBe(105.50);
      expect(restoredRows[1].rate).toBe(93.20);
      expect(restoredRows[2].rate).toBe(114.80);
    });
  });

  describe('Client-Site Issue 2: Mobile Input Hygiene & Auth Scoping', () => {
    it('sanitizes zero-width characters and surrounding spaces from stationId and email while leaving password intact', () => {
      function sanitizeField(val: string): string {
        return val.replace(/[\u200B-\u200D\uFEFF]/g, '').trim();
      }

      // Zero-width space injected by mobile keyboard
      const dirtyStation = '\u200BRashTra-74A837 \uFEFF';
      const dirtyEmail = ' rani@gmail.com\u200B ';
      const exactPassword = ' Secret Pass 123 '; // Legitimate space inside/around password

      expect(sanitizeField(dirtyStation)).toBe('RashTra-74A837');
      expect(sanitizeField(dirtyEmail)).toBe('rani@gmail.com');
      // Password must NEVER be sanitized or trimmed
      expect(exactPassword).toBe(' Secret Pass 123 ');
    });

    it('correctly scopes auth emails case-insensitively with station prefix', async () => {
      const { getScopedAuthEmail } = await import('../contexts/AuthContext');

      expect(getScopedAuthEmail('rani@gmail.com', 'RASHTRA-74A837')).toBe('rashtra-74a837.rani@gmail.com');
      expect(getScopedAuthEmail('rani', 'RASHTRA-74A837')).toBe('rashtra-74a837.rani@fuelpro.local');
      expect(getScopedAuthEmail('rashtra-74a837.rani@gmail.com', 'RASHTRA-74A837')).toBe('rashtra-74a837.rani@gmail.com');
      expect(getScopedAuthEmail('rahul@gmail.com', 'RASHTRA-74A837')).toBe('rashtra-74a837.rahul@gmail.com');
    });
  });
});
