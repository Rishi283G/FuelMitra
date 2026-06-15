-- PostgreSQL Schema for Supabase Sync — GUID-based Identity
-- Primary key: SyncGuid (UUID) per record. station_id and local_id retained for filtering/diagnostics.
-- Foreign keys reference parent SyncGuid instead of composite (station_id, local_id).
-- Generated to match FuelPro EF Core models and GUID-based SyncEngine.

-- 1. Create timestamp trigger helper
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = now();
    RETURN NEW;
END;
$$ language 'plpgsql';

-- 2. Define Tables

-- Users
CREATE TABLE IF NOT EXISTS "Users" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "UserId" INTEGER NOT NULL,
    "Username" VARCHAR(100) NOT NULL,
    "PinHash" TEXT NOT NULL,
    "Role" VARCHAR(20) NOT NULL DEFAULT 'Operator',
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "MustChangePin" BOOLEAN NOT NULL DEFAULT FALSE,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid")
);

-- Shifts
CREATE TABLE IF NOT EXISTS "Shifts" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "ShiftId" INTEGER NOT NULL,
    "ShiftDate" TIMESTAMP NOT NULL,
    "ShiftType" VARCHAR(1) NOT NULL DEFAULT 'A',
    "IsLocked" BOOLEAN NOT NULL DEFAULT FALSE,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid")
);

-- DsmEntries
CREATE TABLE IF NOT EXISTS "DsmEntries" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "DsmEntryId" INTEGER NOT NULL,
    "ShiftId" UUID NOT NULL, -- FK → Shifts.SyncGuid (was INTEGER)
    "DsmName" VARCHAR(200) NOT NULL,
    "PumpId" INTEGER NOT NULL,
    "ConnectedPumpId" INTEGER,
    "ReconciledToPumpId" INTEGER,
    "GrossSales" NUMERIC NOT NULL DEFAULT 0.0,
    "TotalInDirect" NUMERIC NOT NULL DEFAULT 0.0,
    "TotalCreditors" NUMERIC NOT NULL DEFAULT 0.0,
    "TotalCollection" NUMERIC NOT NULL DEFAULT 0.0,
    "Mismatch" NUMERIC NOT NULL DEFAULT 0.0,
    "IsReconciled" BOOLEAN NOT NULL DEFAULT FALSE,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid"),
    CONSTRAINT fk_dsm_entries_shift FOREIGN KEY ("ShiftId") REFERENCES "Shifts" ("SyncGuid") ON DELETE CASCADE
);

-- NozzleReadings
CREATE TABLE IF NOT EXISTS "NozzleReadings" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "NozzleReadingId" INTEGER NOT NULL,
    "DsmEntryId" UUID NOT NULL, -- FK → DsmEntries.SyncGuid (was INTEGER)
    "NozzleNumber" INTEGER NOT NULL,
    "FuelType" VARCHAR(10) NOT NULL,
    "OpeningReading" DOUBLE PRECISION NOT NULL,
    "ClosingReading" DOUBLE PRECISION NOT NULL,
    "SaleLitres" DOUBLE PRECISION NOT NULL,
    "Rate" DOUBLE PRECISION NOT NULL,
    "Amount" DOUBLE PRECISION NOT NULL,
    "IsManualOpeningOverride" BOOLEAN NOT NULL DEFAULT FALSE,
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid"),
    CONSTRAINT fk_nozzle_readings_dsm_entry FOREIGN KEY ("DsmEntryId") REFERENCES "DsmEntries" ("SyncGuid") ON DELETE CASCADE
);

-- PaymentCollections
CREATE TABLE IF NOT EXISTS "PaymentCollections" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "PaymentId" INTEGER NOT NULL,
    "DsmEntryId" UUID NOT NULL, -- FK → DsmEntries.SyncGuid
    "CashDeposit" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "PhonePeMorning" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "PhonePeNight" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "PhonePeCardMorning" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "PhonePeCardNight" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "CreditCardMorning" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "CreditCardNight" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "PetroCard" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "Others" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid"),
    CONSTRAINT fk_payment_collections_dsm_entry FOREIGN KEY ("DsmEntryId") REFERENCES "DsmEntries" ("SyncGuid") ON DELETE CASCADE
);

-- DebitEntries
CREATE TABLE IF NOT EXISTS "DebitEntries" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "DebitId" INTEGER NOT NULL,
    "DsmEntryId" UUID NOT NULL, -- FK → DsmEntries.SyncGuid
    "DebtorName" VARCHAR(200) NOT NULL,
    "Amount" DOUBLE PRECISION NOT NULL,
    "ChequeNo" VARCHAR(100),
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid"),
    CONSTRAINT fk_debit_entries_dsm_entry FOREIGN KEY ("DsmEntryId") REFERENCES "DsmEntries" ("SyncGuid") ON DELETE CASCADE
);

-- TestingEntries
CREATE TABLE IF NOT EXISTS "TestingEntries" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "TestingId" INTEGER NOT NULL,
    "DsmEntryId" UUID NOT NULL, -- FK → DsmEntries.SyncGuid
    "FuelType" VARCHAR(10) NOT NULL,
    "Litres" DOUBLE PRECISION NOT NULL,
    "Rate" DOUBLE PRECISION NOT NULL,
    "Amount" DOUBLE PRECISION NOT NULL,
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid"),
    CONSTRAINT fk_testing_entries_dsm_entry FOREIGN KEY ("DsmEntryId") REFERENCES "DsmEntries" ("SyncGuid") ON DELETE CASCADE
);

-- Expenses
CREATE TABLE IF NOT EXISTS "Expenses" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "ExpenseId" INTEGER NOT NULL,
    "DsmEntryId" UUID, -- FK → DsmEntries.SyncGuid (nullable)
    "ShiftId" UUID, -- FK → Shifts.SyncGuid (nullable)
    "Amount" DOUBLE PRECISION NOT NULL,
    "Description" VARCHAR(500) NOT NULL,
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid"),
    CONSTRAINT fk_expenses_dsm_entry FOREIGN KEY ("DsmEntryId") REFERENCES "DsmEntries" ("SyncGuid") ON DELETE SET NULL,
    CONSTRAINT fk_expenses_shift FOREIGN KEY ("ShiftId") REFERENCES "Shifts" ("SyncGuid") ON DELETE SET NULL
);

-- CashDenominations
CREATE TABLE IF NOT EXISTS "CashDenominations" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "CashDenomId" INTEGER NOT NULL,
    "DsmEntryId" UUID NOT NULL, -- FK → DsmEntries.SyncGuid
    "CashType" VARCHAR(10) NOT NULL,
    "Denom10" INTEGER NOT NULL DEFAULT 0,
    "Denom20" INTEGER NOT NULL DEFAULT 0,
    "Denom50" INTEGER NOT NULL DEFAULT 0,
    "Denom100" INTEGER NOT NULL DEFAULT 0,
    "Denom200" INTEGER NOT NULL DEFAULT 0,
    "Denom500" INTEGER NOT NULL DEFAULT 0,
    "Coins" INTEGER NOT NULL DEFAULT 0,
    "TotalAmount" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid"),
    CONSTRAINT fk_cash_denominations_dsm_entry FOREIGN KEY ("DsmEntryId") REFERENCES "DsmEntries" ("SyncGuid") ON DELETE CASCADE
);

-- Settings
CREATE TABLE IF NOT EXISTS "Settings" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "SettingId" INTEGER NOT NULL,
    "HsdRate" DOUBLE PRECISION NOT NULL DEFAULT 90.35,
    "MsIRate" DOUBLE PRECISION NOT NULL DEFAULT 103.81,
    "MsIIRate" DOUBLE PRECISION NOT NULL DEFAULT 103.81,
    "PumpStationName" VARCHAR(300) NOT NULL DEFAULT 'VKD Petroleum',
    "LastUpdated" TIMESTAMP NOT NULL,
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid")
);

-- ShiftOtherCash
CREATE TABLE IF NOT EXISTS "ShiftOtherCash" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "ShiftOtherCashId" INTEGER NOT NULL,
    "ShiftId" UUID, -- FK → Shifts.SyncGuid (nullable)
    "ShiftDate" TIMESTAMP NOT NULL,
    "ShiftNumber" VARCHAR(1) NOT NULL,
    "Description" VARCHAR(200) NOT NULL,
    "Amount" DOUBLE PRECISION NOT NULL,
    "IsEditable" BOOLEAN NOT NULL DEFAULT TRUE,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid"),
    CONSTRAINT fk_shift_other_cash_shift FOREIGN KEY ("ShiftId") REFERENCES "Shifts" ("SyncGuid") ON DELETE SET NULL
);

-- ShiftFuelRates
CREATE TABLE IF NOT EXISTS "ShiftFuelRates" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "ShiftFuelRateId" INTEGER NOT NULL,
    "ShiftId" UUID, -- FK → Shifts.SyncGuid (nullable)
    "ShiftDate" TIMESTAMP NOT NULL,
    "ShiftNumber" VARCHAR(1) NOT NULL,
    "FuelType" VARCHAR(10) NOT NULL,
    "OverrideRate" DOUBLE PRECISION NOT NULL,
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid"),
    CONSTRAINT fk_shift_fuel_rates_shift FOREIGN KEY ("ShiftId") REFERENCES "Shifts" ("SyncGuid") ON DELETE SET NULL
);

-- DsmProfiles
CREATE TABLE IF NOT EXISTS "DsmProfiles" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "DsmProfileId" INTEGER NOT NULL,
    "DsmName" VARCHAR(100) NOT NULL,
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid")
);

-- Creditors
CREATE TABLE IF NOT EXISTS "Creditors" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "CreditorId" INTEGER NOT NULL,
    "Name" VARCHAR(200) NOT NULL,
    "Phone" VARCHAR(20),
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid")
);

-- CreditorRepayments
CREATE TABLE IF NOT EXISTS "CreditorRepayments" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "CreditorRepaymentId" INTEGER NOT NULL,
    "RepaymentDate" TIMESTAMP NOT NULL,
    "CreditorName" VARCHAR(200) NOT NULL,
    "PaymentMode" VARCHAR(50) NOT NULL,
    "ChequeNo" VARCHAR(100),
    "Amount" DOUBLE PRECISION NOT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid")
);

-- AgsShiftImports
CREATE TABLE IF NOT EXISTS "AgsShiftImports" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "AgsShiftImportId" INTEGER NOT NULL,
    "ImportDate" TIMESTAMP NOT NULL,
    "ShiftType" VARCHAR(1) NOT NULL,
    "TotalMsILitres" DOUBLE PRECISION NOT NULL,
    "TotalMsIILitres" DOUBLE PRECISION NOT NULL,
    "TotalHsdLitres" DOUBLE PRECISION NOT NULL,
    "MsIOpeningStock" DOUBLE PRECISION NOT NULL,
    "MsIClosingStock" DOUBLE PRECISION NOT NULL,
    "MsIIOpeningStock" DOUBLE PRECISION NOT NULL,
    "MsIIClosingStock" DOUBLE PRECISION NOT NULL,
    "HsdOpeningStock" DOUBLE PRECISION NOT NULL,
    "HsdClosingStock" DOUBLE PRECISION NOT NULL,
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "ImportedAt" TIMESTAMP NOT NULL,
    "ImportedBy" VARCHAR(100) NOT NULL,
    "PdfFileName" VARCHAR(255) NOT NULL,
    "PdfPeriodFrom" VARCHAR(50),
    "PdfPeriodTo" VARCHAR(50),
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid")
);

-- AgsNozzleReadings
CREATE TABLE IF NOT EXISTS "AgsNozzleReadings" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "AgsNozzleReadingId" INTEGER NOT NULL,
    "AgsShiftImportId" UUID NOT NULL, -- FK → AgsShiftImports.SyncGuid (was INTEGER)
    "NozzleNumber" INTEGER NOT NULL,
    "FuelType" VARCHAR(10) NOT NULL,
    "OpeningReading" DOUBLE PRECISION NOT NULL,
    "ClosingReading" DOUBLE PRECISION NOT NULL,
    "SaleLitres" DOUBLE PRECISION NOT NULL,
    "TestingDeduction" DOUBLE PRECISION NOT NULL,
    "NetSaleLitres" DOUBLE PRECISION NOT NULL,
    "PumpNumber" INTEGER NOT NULL,
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid"),
    CONSTRAINT fk_ags_nozzle_readings_import FOREIGN KEY ("AgsShiftImportId") REFERENCES "AgsShiftImports" ("SyncGuid") ON DELETE CASCADE
);

-- AgsTankStocks
CREATE TABLE IF NOT EXISTS "AgsTankStocks" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "AgsTankStockId" INTEGER NOT NULL,
    "AgsShiftImportId" UUID NOT NULL, -- FK → AgsShiftImports.SyncGuid (was INTEGER)
    "TankNumber" INTEGER NOT NULL,
    "FuelType" VARCHAR(10) NOT NULL,
    "OpeningDipMM" DOUBLE PRECISION NOT NULL,
    "OpeningStockLitres" DOUBLE PRECISION NOT NULL,
    "ClosingDipMM" DOUBLE PRECISION NOT NULL,
    "ClosingStockLitres" DOUBLE PRECISION NOT NULL,
    "ReceiptLitres" DOUBLE PRECISION NOT NULL,
    "FuelDispensedLitres" DOUBLE PRECISION NOT NULL,
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid"),
    CONSTRAINT fk_ags_tank_stocks_import FOREIGN KEY ("AgsShiftImportId") REFERENCES "AgsShiftImports" ("SyncGuid") ON DELETE CASCADE
);

-- AgsDailySummaries
CREATE TABLE IF NOT EXISTS "AgsDailySummaries" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "AgsDailySummaryId" INTEGER NOT NULL,
    "SummaryDate" TIMESTAMP NOT NULL,
    "NozzleDaySalesJson" TEXT NOT NULL DEFAULT '{}',
    "ShiftBreakdownJson" TEXT NOT NULL DEFAULT '{}',
    "DayTotalMsILitres" DOUBLE PRECISION NOT NULL,
    "DayTotalMsIILitres" DOUBLE PRECISION NOT NULL,
    "DayTotalHsdLitres" DOUBLE PRECISION NOT NULL,
    "MsIDayOpeningStock" DOUBLE PRECISION NOT NULL,
    "MsIDayClosingStock" DOUBLE PRECISION NOT NULL,
    "MsIIDayOpeningStock" DOUBLE PRECISION NOT NULL,
    "MsIIDayClosingStock" DOUBLE PRECISION NOT NULL,
    "HsdDayOpeningStock" DOUBLE PRECISION NOT NULL,
    "HsdDayClosingStock" DOUBLE PRECISION NOT NULL,
    "ShiftAImported" BOOLEAN NOT NULL,
    "ShiftBImported" BOOLEAN NOT NULL,
    "ShiftCImported" BOOLEAN NOT NULL,
    "LastUpdatedAt" TIMESTAMP NOT NULL,
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid")
);

-- 3. Setup Triggers for Automatic updated_at Update

CREATE OR REPLACE PROCEDURE create_update_triggers() AS $$
DECLARE
    t TEXT;
BEGIN
    FOR t IN 
        SELECT table_name 
        FROM information_schema.tables 
        WHERE table_schema = 'public' 
          AND table_name IN (
            'Users', 'Shifts', 'DsmEntries', 'NozzleReadings', 'PaymentCollections',
            'DebitEntries', 'TestingEntries', 'Expenses', 'CashDenominations', 'Settings',
            'ShiftOtherCash', 'ShiftFuelRates', 'DsmProfiles', 'Creditors', 'CreditorRepayments',
            'AgsShiftImports', 'AgsNozzleReadings', 'AgsTankStocks', 'AgsDailySummaries'
          )
    LOOP
        EXECUTE format('DROP TRIGGER IF EXISTS tr_update_timestamp ON %I', t);
        EXECUTE format('CREATE TRIGGER tr_update_timestamp BEFORE UPDATE ON %I FOR EACH ROW EXECUTE FUNCTION update_updated_at_column()', t);
    END LOOP;
END;
$$ LANGUAGE plpgsql;

CALL create_update_triggers();

-- 4. Enable Row Level Security (RLS) on All Synced Tables

ALTER TABLE "Users" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "Shifts" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "DsmEntries" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "NozzleReadings" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "PaymentCollections" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "DebitEntries" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "TestingEntries" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "Expenses" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "CashDenominations" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "Settings" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "ShiftOtherCash" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "ShiftFuelRates" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "DsmProfiles" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "Creditors" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "CreditorRepayments" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "AgsShiftImports" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "AgsNozzleReadings" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "AgsTankStocks" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "AgsDailySummaries" ENABLE ROW LEVEL SECURITY;

-- 5. Define Row Level Security Policies (Filter by station_id)
-- Replace the true condition with actual checks against JWT custom claims if using authenticated roles.
-- Example of basic permissive station-level policies:
-- Drop existing policies first (PostgreSQL has no CREATE POLICY IF NOT EXISTS)

DROP POLICY IF EXISTS select_by_station ON "Users";
DROP POLICY IF EXISTS all_by_station ON "Users";
CREATE POLICY select_by_station ON "Users" FOR SELECT USING (true);
CREATE POLICY all_by_station ON "Users" FOR ALL USING (true) WITH CHECK (true);

DROP POLICY IF EXISTS all_by_station ON "Shifts";
CREATE POLICY all_by_station ON "Shifts" FOR ALL USING (true) WITH CHECK (true);
DROP POLICY IF EXISTS all_by_station ON "DsmEntries";
CREATE POLICY all_by_station ON "DsmEntries" FOR ALL USING (true) WITH CHECK (true);
DROP POLICY IF EXISTS all_by_station ON "NozzleReadings";
CREATE POLICY all_by_station ON "NozzleReadings" FOR ALL USING (true) WITH CHECK (true);
DROP POLICY IF EXISTS all_by_station ON "PaymentCollections";
CREATE POLICY all_by_station ON "PaymentCollections" FOR ALL USING (true) WITH CHECK (true);
DROP POLICY IF EXISTS all_by_station ON "DebitEntries";
CREATE POLICY all_by_station ON "DebitEntries" FOR ALL USING (true) WITH CHECK (true);
DROP POLICY IF EXISTS all_by_station ON "TestingEntries";
CREATE POLICY all_by_station ON "TestingEntries" FOR ALL USING (true) WITH CHECK (true);
DROP POLICY IF EXISTS all_by_station ON "Expenses";
CREATE POLICY all_by_station ON "Expenses" FOR ALL USING (true) WITH CHECK (true);
DROP POLICY IF EXISTS all_by_station ON "CashDenominations";
CREATE POLICY all_by_station ON "CashDenominations" FOR ALL USING (true) WITH CHECK (true);
DROP POLICY IF EXISTS all_by_station ON "Settings";
CREATE POLICY all_by_station ON "Settings" FOR ALL USING (true) WITH CHECK (true);
DROP POLICY IF EXISTS all_by_station ON "ShiftOtherCash";
CREATE POLICY all_by_station ON "ShiftOtherCash" FOR ALL USING (true) WITH CHECK (true);
DROP POLICY IF EXISTS all_by_station ON "ShiftFuelRates";
CREATE POLICY all_by_station ON "ShiftFuelRates" FOR ALL USING (true) WITH CHECK (true);
DROP POLICY IF EXISTS all_by_station ON "DsmProfiles";
CREATE POLICY all_by_station ON "DsmProfiles" FOR ALL USING (true) WITH CHECK (true);
DROP POLICY IF EXISTS all_by_station ON "Creditors";
CREATE POLICY all_by_station ON "Creditors" FOR ALL USING (true) WITH CHECK (true);
DROP POLICY IF EXISTS all_by_station ON "CreditorRepayments";
CREATE POLICY all_by_station ON "CreditorRepayments" FOR ALL USING (true) WITH CHECK (true);
DROP POLICY IF EXISTS all_by_station ON "AgsShiftImports";
CREATE POLICY all_by_station ON "AgsShiftImports" FOR ALL USING (true) WITH CHECK (true);
DROP POLICY IF EXISTS all_by_station ON "AgsNozzleReadings";
CREATE POLICY all_by_station ON "AgsNozzleReadings" FOR ALL USING (true) WITH CHECK (true);
DROP POLICY IF EXISTS all_by_station ON "AgsTankStocks";
CREATE POLICY all_by_station ON "AgsTankStocks" FOR ALL USING (true) WITH CHECK (true);
DROP POLICY IF EXISTS all_by_station ON "AgsDailySummaries";
CREATE POLICY all_by_station ON "AgsDailySummaries" FOR ALL USING (true) WITH CHECK (true);

-- 6. Recommended Optimization Indexes

-- Composite index for syncing performance (queries filter by station_id and updated_at)
CREATE INDEX IF NOT EXISTS idx_users_sync ON "Users" ("station_id", "updated_at");
CREATE INDEX IF NOT EXISTS idx_shifts_sync ON "Shifts" ("station_id", "updated_at");
CREATE INDEX IF NOT EXISTS idx_dsm_entries_sync ON "DsmEntries" ("station_id", "updated_at");
CREATE INDEX IF NOT EXISTS idx_nozzle_readings_sync ON "NozzleReadings" ("station_id", "updated_at");
CREATE INDEX IF NOT EXISTS idx_payment_collections_sync ON "PaymentCollections" ("station_id", "updated_at");
CREATE INDEX IF NOT EXISTS idx_debit_entries_sync ON "DebitEntries" ("station_id", "updated_at");
CREATE INDEX IF NOT EXISTS idx_testing_entries_sync ON "TestingEntries" ("station_id", "updated_at");
CREATE INDEX IF NOT EXISTS idx_expenses_sync ON "Expenses" ("station_id", "updated_at");
CREATE INDEX IF NOT EXISTS idx_cash_denominations_sync ON "CashDenominations" ("station_id", "updated_at");
CREATE INDEX IF NOT EXISTS idx_settings_sync ON "Settings" ("station_id", "updated_at");
CREATE INDEX IF NOT EXISTS idx_shift_other_cash_sync ON "ShiftOtherCash" ("station_id", "updated_at");
CREATE INDEX IF NOT EXISTS idx_shift_fuel_rates_sync ON "ShiftFuelRates" ("station_id", "updated_at");
CREATE INDEX IF NOT EXISTS idx_dsm_profiles_sync ON "DsmProfiles" ("station_id", "updated_at");
CREATE INDEX IF NOT EXISTS idx_creditors_sync ON "Creditors" ("station_id", "updated_at");
CREATE INDEX IF NOT EXISTS idx_creditor_repayments_sync ON "CreditorRepayments" ("station_id", "updated_at");
CREATE INDEX IF NOT EXISTS idx_ags_shift_imports_sync ON "AgsShiftImports" ("station_id", "updated_at");
CREATE INDEX IF NOT EXISTS idx_ags_nozzle_readings_sync ON "AgsNozzleReadings" ("station_id", "updated_at");
CREATE INDEX IF NOT EXISTS idx_ags_tank_stocks_sync ON "AgsTankStocks" ("station_id", "updated_at");
CREATE INDEX IF NOT EXISTS idx_ags_daily_summaries_sync ON "AgsDailySummaries" ("station_id", "updated_at");

-- Unique functional constraints per station (business rules)
CREATE UNIQUE INDEX IF NOT EXISTS idx_users_username ON "Users" ("station_id", "Username");
CREATE UNIQUE INDEX IF NOT EXISTS idx_shifts_date_type ON "Shifts" ("station_id", "ShiftDate", "ShiftType");
CREATE UNIQUE INDEX IF NOT EXISTS idx_creditors_name ON "Creditors" ("station_id", "Name");
CREATE UNIQUE INDEX IF NOT EXISTS idx_cash_denoms_dsm_type ON "CashDenominations" ("DsmEntryId", "CashType");
CREATE UNIQUE INDEX IF NOT EXISTS idx_shift_fuel_rates_date_num_type ON "ShiftFuelRates" ("station_id", "ShiftDate", "ShiftNumber", "FuelType");
CREATE UNIQUE INDEX IF NOT EXISTS idx_dsm_profiles_name ON "DsmProfiles" ("station_id", "DsmName");
CREATE UNIQUE INDEX IF NOT EXISTS idx_ags_daily_summaries_date ON "AgsDailySummaries" ("station_id", "SummaryDate");

-- Performance indexes for foreign key lookups (now using UUID FKs)
CREATE INDEX IF NOT EXISTS idx_dsm_entries_shift ON "DsmEntries" ("ShiftId");
CREATE INDEX IF NOT EXISTS idx_nozzle_readings_dsm ON "NozzleReadings" ("DsmEntryId");
CREATE INDEX IF NOT EXISTS idx_payment_collections_dsm ON "PaymentCollections" ("DsmEntryId");
CREATE INDEX IF NOT EXISTS idx_debit_entries_dsm ON "DebitEntries" ("DsmEntryId");
CREATE INDEX IF NOT EXISTS idx_testing_entries_dsm ON "TestingEntries" ("DsmEntryId");
CREATE INDEX IF NOT EXISTS idx_expenses_dsm ON "Expenses" ("DsmEntryId");
CREATE INDEX IF NOT EXISTS idx_expenses_shift ON "Expenses" ("ShiftId");
CREATE INDEX IF NOT EXISTS idx_cash_denominations_dsm ON "CashDenominations" ("DsmEntryId");
CREATE INDEX IF NOT EXISTS idx_shift_other_cash_shift ON "ShiftOtherCash" ("ShiftId");
CREATE INDEX IF NOT EXISTS idx_shift_fuel_rates_shift ON "ShiftFuelRates" ("ShiftId");
CREATE INDEX IF NOT EXISTS idx_ags_nozzle_readings_import ON "AgsNozzleReadings" ("AgsShiftImportId");
CREATE INDEX IF NOT EXISTS idx_ags_tank_stocks_import ON "AgsTankStocks" ("AgsShiftImportId");

-- Machine tracking index for diagnostics
CREATE INDEX IF NOT EXISTS idx_users_machine ON "Users" ("machine_id");
CREATE INDEX IF NOT EXISTS idx_shifts_machine ON "Shifts" ("machine_id");
CREATE INDEX IF NOT EXISTS idx_dsm_entries_machine ON "DsmEntries" ("machine_id");
