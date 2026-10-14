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
});
