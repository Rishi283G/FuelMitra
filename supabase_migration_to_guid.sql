-- ═══════════════════════════════════════════════════════════════════════
-- Supabase Migration: Composite PK (station_id, local_id) → SyncGuid UUID PK
-- ═══════════════════════════════════════════════════════════════════════
-- 
-- PURPOSE: Migrates an existing FuelPro Supabase database from the old
-- composite primary key scheme to UUID-based SyncGuid as primary key.
--
-- IMPORTANT: Run this in a transaction. Back up your database first.
-- This script is idempotent — safe to re-run if partially applied.
--
-- STEPS:
--   1. Add SyncGuid and machine_id columns to all tables
--   2. Populate SyncGuid with generated UUIDs for existing rows
--   3. Create temporary FK mapping columns with parent SyncGuids
--   4. Drop old constraints, PKs, FKs, and indexes
--   5. Set new primary keys on SyncGuid
--   6. Alter FK columns from INTEGER to UUID and populate them
--   7. Create new FK constraints and indexes
-- ═══════════════════════════════════════════════════════════════════════

BEGIN;

-- ─── STEP 1: Add SyncGuid and machine_id columns ───────────────────

-- Parent tables (no FK dependencies)
ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "SyncGuid" UUID DEFAULT gen_random_uuid();
ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "machine_id" TEXT;

ALTER TABLE "Shifts" ADD COLUMN IF NOT EXISTS "SyncGuid" UUID DEFAULT gen_random_uuid();
ALTER TABLE "Shifts" ADD COLUMN IF NOT EXISTS "machine_id" TEXT;

ALTER TABLE "Settings" ADD COLUMN IF NOT EXISTS "SyncGuid" UUID DEFAULT gen_random_uuid();
ALTER TABLE "Settings" ADD COLUMN IF NOT EXISTS "machine_id" TEXT;

ALTER TABLE "DsmProfiles" ADD COLUMN IF NOT EXISTS "SyncGuid" UUID DEFAULT gen_random_uuid();
ALTER TABLE "DsmProfiles" ADD COLUMN IF NOT EXISTS "machine_id" TEXT;

ALTER TABLE "Creditors" ADD COLUMN IF NOT EXISTS "SyncGuid" UUID DEFAULT gen_random_uuid();
ALTER TABLE "Creditors" ADD COLUMN IF NOT EXISTS "machine_id" TEXT;

ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "SyncGuid" UUID DEFAULT gen_random_uuid();
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "machine_id" TEXT;

ALTER TABLE "AgsShiftImports" ADD COLUMN IF NOT EXISTS "SyncGuid" UUID DEFAULT gen_random_uuid();
ALTER TABLE "AgsShiftImports" ADD COLUMN IF NOT EXISTS "machine_id" TEXT;

ALTER TABLE "AgsDailySummaries" ADD COLUMN IF NOT EXISTS "SyncGuid" UUID DEFAULT gen_random_uuid();
ALTER TABLE "AgsDailySummaries" ADD COLUMN IF NOT EXISTS "machine_id" TEXT;

-- Child tables
ALTER TABLE "DsmEntries" ADD COLUMN IF NOT EXISTS "SyncGuid" UUID DEFAULT gen_random_uuid();
ALTER TABLE "DsmEntries" ADD COLUMN IF NOT EXISTS "machine_id" TEXT;

ALTER TABLE "NozzleReadings" ADD COLUMN IF NOT EXISTS "SyncGuid" UUID DEFAULT gen_random_uuid();
ALTER TABLE "NozzleReadings" ADD COLUMN IF NOT EXISTS "machine_id" TEXT;

ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "SyncGuid" UUID DEFAULT gen_random_uuid();
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "machine_id" TEXT;

ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "SyncGuid" UUID DEFAULT gen_random_uuid();
ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "machine_id" TEXT;

ALTER TABLE "TestingEntries" ADD COLUMN IF NOT EXISTS "SyncGuid" UUID DEFAULT gen_random_uuid();
ALTER TABLE "TestingEntries" ADD COLUMN IF NOT EXISTS "machine_id" TEXT;

ALTER TABLE "Expenses" ADD COLUMN IF NOT EXISTS "SyncGuid" UUID DEFAULT gen_random_uuid();
ALTER TABLE "Expenses" ADD COLUMN IF NOT EXISTS "machine_id" TEXT;

ALTER TABLE "CashDenominations" ADD COLUMN IF NOT EXISTS "SyncGuid" UUID DEFAULT gen_random_uuid();
ALTER TABLE "CashDenominations" ADD COLUMN IF NOT EXISTS "machine_id" TEXT;

ALTER TABLE "ShiftOtherCash" ADD COLUMN IF NOT EXISTS "SyncGuid" UUID DEFAULT gen_random_uuid();
ALTER TABLE "ShiftOtherCash" ADD COLUMN IF NOT EXISTS "machine_id" TEXT;

ALTER TABLE "ShiftFuelRates" ADD COLUMN IF NOT EXISTS "SyncGuid" UUID DEFAULT gen_random_uuid();
ALTER TABLE "ShiftFuelRates" ADD COLUMN IF NOT EXISTS "machine_id" TEXT;

ALTER TABLE "AgsNozzleReadings" ADD COLUMN IF NOT EXISTS "SyncGuid" UUID DEFAULT gen_random_uuid();
ALTER TABLE "AgsNozzleReadings" ADD COLUMN IF NOT EXISTS "machine_id" TEXT;

ALTER TABLE "AgsTankStocks" ADD COLUMN IF NOT EXISTS "SyncGuid" UUID DEFAULT gen_random_uuid();
ALTER TABLE "AgsTankStocks" ADD COLUMN IF NOT EXISTS "machine_id" TEXT;


-- ─── STEP 2: Populate SyncGuid for any existing rows that have NULL ──

UPDATE "Users" SET "SyncGuid" = gen_random_uuid() WHERE "SyncGuid" IS NULL;
UPDATE "Shifts" SET "SyncGuid" = gen_random_uuid() WHERE "SyncGuid" IS NULL;
UPDATE "Settings" SET "SyncGuid" = gen_random_uuid() WHERE "SyncGuid" IS NULL;
UPDATE "DsmProfiles" SET "SyncGuid" = gen_random_uuid() WHERE "SyncGuid" IS NULL;
UPDATE "Creditors" SET "SyncGuid" = gen_random_uuid() WHERE "SyncGuid" IS NULL;
UPDATE "CreditorRepayments" SET "SyncGuid" = gen_random_uuid() WHERE "SyncGuid" IS NULL;
UPDATE "AgsShiftImports" SET "SyncGuid" = gen_random_uuid() WHERE "SyncGuid" IS NULL;
UPDATE "AgsDailySummaries" SET "SyncGuid" = gen_random_uuid() WHERE "SyncGuid" IS NULL;
UPDATE "DsmEntries" SET "SyncGuid" = gen_random_uuid() WHERE "SyncGuid" IS NULL;
UPDATE "NozzleReadings" SET "SyncGuid" = gen_random_uuid() WHERE "SyncGuid" IS NULL;
UPDATE "PaymentCollections" SET "SyncGuid" = gen_random_uuid() WHERE "SyncGuid" IS NULL;
UPDATE "DebitEntries" SET "SyncGuid" = gen_random_uuid() WHERE "SyncGuid" IS NULL;
UPDATE "TestingEntries" SET "SyncGuid" = gen_random_uuid() WHERE "SyncGuid" IS NULL;
UPDATE "Expenses" SET "SyncGuid" = gen_random_uuid() WHERE "SyncGuid" IS NULL;
UPDATE "CashDenominations" SET "SyncGuid" = gen_random_uuid() WHERE "SyncGuid" IS NULL;
UPDATE "ShiftOtherCash" SET "SyncGuid" = gen_random_uuid() WHERE "SyncGuid" IS NULL;
UPDATE "ShiftFuelRates" SET "SyncGuid" = gen_random_uuid() WHERE "SyncGuid" IS NULL;
UPDATE "AgsNozzleReadings" SET "SyncGuid" = gen_random_uuid() WHERE "SyncGuid" IS NULL;
UPDATE "AgsTankStocks" SET "SyncGuid" = gen_random_uuid() WHERE "SyncGuid" IS NULL;

-- Make SyncGuid NOT NULL now that all rows have a value
ALTER TABLE "Users" ALTER COLUMN "SyncGuid" SET NOT NULL;
ALTER TABLE "Shifts" ALTER COLUMN "SyncGuid" SET NOT NULL;
ALTER TABLE "Settings" ALTER COLUMN "SyncGuid" SET NOT NULL;
ALTER TABLE "DsmProfiles" ALTER COLUMN "SyncGuid" SET NOT NULL;
ALTER TABLE "Creditors" ALTER COLUMN "SyncGuid" SET NOT NULL;
ALTER TABLE "CreditorRepayments" ALTER COLUMN "SyncGuid" SET NOT NULL;
ALTER TABLE "AgsShiftImports" ALTER COLUMN "SyncGuid" SET NOT NULL;
ALTER TABLE "AgsDailySummaries" ALTER COLUMN "SyncGuid" SET NOT NULL;
ALTER TABLE "DsmEntries" ALTER COLUMN "SyncGuid" SET NOT NULL;
ALTER TABLE "NozzleReadings" ALTER COLUMN "SyncGuid" SET NOT NULL;
ALTER TABLE "PaymentCollections" ALTER COLUMN "SyncGuid" SET NOT NULL;
ALTER TABLE "DebitEntries" ALTER COLUMN "SyncGuid" SET NOT NULL;
ALTER TABLE "TestingEntries" ALTER COLUMN "SyncGuid" SET NOT NULL;
ALTER TABLE "Expenses" ALTER COLUMN "SyncGuid" SET NOT NULL;
ALTER TABLE "CashDenominations" ALTER COLUMN "SyncGuid" SET NOT NULL;
ALTER TABLE "ShiftOtherCash" ALTER COLUMN "SyncGuid" SET NOT NULL;
ALTER TABLE "ShiftFuelRates" ALTER COLUMN "SyncGuid" SET NOT NULL;
ALTER TABLE "AgsNozzleReadings" ALTER COLUMN "SyncGuid" SET NOT NULL;
ALTER TABLE "AgsTankStocks" ALTER COLUMN "SyncGuid" SET NOT NULL;


-- ─── STEP 3: Drop ALL existing foreign key constraints ──────────────
-- (Must be done before altering FK column types from INTEGER to UUID)

ALTER TABLE "DsmEntries" DROP CONSTRAINT IF EXISTS fk_dsm_entries_shift;
ALTER TABLE "NozzleReadings" DROP CONSTRAINT IF EXISTS fk_nozzle_readings_dsm_entry;
ALTER TABLE "PaymentCollections" DROP CONSTRAINT IF EXISTS fk_payment_collections_dsm_entry;
ALTER TABLE "DebitEntries" DROP CONSTRAINT IF EXISTS fk_debit_entries_dsm_entry;
ALTER TABLE "TestingEntries" DROP CONSTRAINT IF EXISTS fk_testing_entries_dsm_entry;
ALTER TABLE "Expenses" DROP CONSTRAINT IF EXISTS fk_expenses_dsm_entry;
ALTER TABLE "Expenses" DROP CONSTRAINT IF EXISTS fk_expenses_shift;
ALTER TABLE "CashDenominations" DROP CONSTRAINT IF EXISTS fk_cash_denominations_dsm_entry;
ALTER TABLE "ShiftOtherCash" DROP CONSTRAINT IF EXISTS fk_shift_other_cash_shift;
ALTER TABLE "ShiftFuelRates" DROP CONSTRAINT IF EXISTS fk_shift_fuel_rates_shift;
ALTER TABLE "AgsNozzleReadings" DROP CONSTRAINT IF EXISTS fk_ags_nozzle_readings_import;
ALTER TABLE "AgsTankStocks" DROP CONSTRAINT IF EXISTS fk_ags_tank_stocks_import;


-- ─── STEP 4: Drop old composite primary keys ───────────────────────

ALTER TABLE "Users" DROP CONSTRAINT IF EXISTS "Users_pkey";
ALTER TABLE "Shifts" DROP CONSTRAINT IF EXISTS "Shifts_pkey";
ALTER TABLE "DsmEntries" DROP CONSTRAINT IF EXISTS "DsmEntries_pkey";
ALTER TABLE "NozzleReadings" DROP CONSTRAINT IF EXISTS "NozzleReadings_pkey";
ALTER TABLE "PaymentCollections" DROP CONSTRAINT IF EXISTS "PaymentCollections_pkey";
ALTER TABLE "DebitEntries" DROP CONSTRAINT IF EXISTS "DebitEntries_pkey";
ALTER TABLE "TestingEntries" DROP CONSTRAINT IF EXISTS "TestingEntries_pkey";
ALTER TABLE "Expenses" DROP CONSTRAINT IF EXISTS "Expenses_pkey";
ALTER TABLE "CashDenominations" DROP CONSTRAINT IF EXISTS "CashDenominations_pkey";
ALTER TABLE "Settings" DROP CONSTRAINT IF EXISTS "Settings_pkey";
ALTER TABLE "ShiftOtherCash" DROP CONSTRAINT IF EXISTS "ShiftOtherCash_pkey";
ALTER TABLE "ShiftFuelRates" DROP CONSTRAINT IF EXISTS "ShiftFuelRates_pkey";
ALTER TABLE "DsmProfiles" DROP CONSTRAINT IF EXISTS "DsmProfiles_pkey";
ALTER TABLE "Creditors" DROP CONSTRAINT IF EXISTS "Creditors_pkey";
ALTER TABLE "CreditorRepayments" DROP CONSTRAINT IF EXISTS "CreditorRepayments_pkey";
ALTER TABLE "AgsShiftImports" DROP CONSTRAINT IF EXISTS "AgsShiftImports_pkey";
ALTER TABLE "AgsNozzleReadings" DROP CONSTRAINT IF EXISTS "AgsNozzleReadings_pkey";
ALTER TABLE "AgsTankStocks" DROP CONSTRAINT IF EXISTS "AgsTankStocks_pkey";
ALTER TABLE "AgsDailySummaries" DROP CONSTRAINT IF EXISTS "AgsDailySummaries_pkey";


-- ─── STEP 5: Create new SyncGuid primary keys ──────────────────────

ALTER TABLE "Users" ADD PRIMARY KEY ("SyncGuid");
ALTER TABLE "Shifts" ADD PRIMARY KEY ("SyncGuid");
ALTER TABLE "DsmEntries" ADD PRIMARY KEY ("SyncGuid");
ALTER TABLE "NozzleReadings" ADD PRIMARY KEY ("SyncGuid");
ALTER TABLE "PaymentCollections" ADD PRIMARY KEY ("SyncGuid");
ALTER TABLE "DebitEntries" ADD PRIMARY KEY ("SyncGuid");
ALTER TABLE "TestingEntries" ADD PRIMARY KEY ("SyncGuid");
ALTER TABLE "Expenses" ADD PRIMARY KEY ("SyncGuid");
ALTER TABLE "CashDenominations" ADD PRIMARY KEY ("SyncGuid");
ALTER TABLE "Settings" ADD PRIMARY KEY ("SyncGuid");
ALTER TABLE "ShiftOtherCash" ADD PRIMARY KEY ("SyncGuid");
ALTER TABLE "ShiftFuelRates" ADD PRIMARY KEY ("SyncGuid");
ALTER TABLE "DsmProfiles" ADD PRIMARY KEY ("SyncGuid");
ALTER TABLE "Creditors" ADD PRIMARY KEY ("SyncGuid");
ALTER TABLE "CreditorRepayments" ADD PRIMARY KEY ("SyncGuid");
ALTER TABLE "AgsShiftImports" ADD PRIMARY KEY ("SyncGuid");
ALTER TABLE "AgsNozzleReadings" ADD PRIMARY KEY ("SyncGuid");
ALTER TABLE "AgsTankStocks" ADD PRIMARY KEY ("SyncGuid");
ALTER TABLE "AgsDailySummaries" ADD PRIMARY KEY ("SyncGuid");


-- ─── STEP 6: Convert FK columns from INTEGER to UUID ────────────────
-- For each child table:
--   a) Add a temp UUID FK column
--   b) Populate it by joining to the parent table on (station_id, old_integer_FK)
--   c) Drop the old integer FK column
--   d) Rename the temp column to the original name

-- DsmEntries.ShiftId → Shifts.SyncGuid
ALTER TABLE "DsmEntries" ADD COLUMN "ShiftId_new" UUID;
UPDATE "DsmEntries" d SET "ShiftId_new" = s."SyncGuid"
  FROM "Shifts" s WHERE d."station_id" = s."station_id" AND d."ShiftId" = s."local_id";
ALTER TABLE "DsmEntries" DROP COLUMN "ShiftId";
ALTER TABLE "DsmEntries" RENAME COLUMN "ShiftId_new" TO "ShiftId";
ALTER TABLE "DsmEntries" ALTER COLUMN "ShiftId" SET NOT NULL;

-- NozzleReadings.DsmEntryId → DsmEntries.SyncGuid
ALTER TABLE "NozzleReadings" ADD COLUMN "DsmEntryId_new" UUID;
UPDATE "NozzleReadings" n SET "DsmEntryId_new" = d."SyncGuid"
  FROM "DsmEntries" d WHERE n."station_id" = d."station_id" AND n."DsmEntryId" = d."local_id";
ALTER TABLE "NozzleReadings" DROP COLUMN "DsmEntryId";
ALTER TABLE "NozzleReadings" RENAME COLUMN "DsmEntryId_new" TO "DsmEntryId";
ALTER TABLE "NozzleReadings" ALTER COLUMN "DsmEntryId" SET NOT NULL;

-- PaymentCollections.DsmEntryId → DsmEntries.SyncGuid
ALTER TABLE "PaymentCollections" ADD COLUMN "DsmEntryId_new" UUID;
UPDATE "PaymentCollections" p SET "DsmEntryId_new" = d."SyncGuid"
  FROM "DsmEntries" d WHERE p."station_id" = d."station_id" AND p."DsmEntryId" = d."local_id";
ALTER TABLE "PaymentCollections" DROP COLUMN "DsmEntryId";
ALTER TABLE "PaymentCollections" RENAME COLUMN "DsmEntryId_new" TO "DsmEntryId";
ALTER TABLE "PaymentCollections" ALTER COLUMN "DsmEntryId" SET NOT NULL;

-- DebitEntries.DsmEntryId → DsmEntries.SyncGuid
ALTER TABLE "DebitEntries" ADD COLUMN "DsmEntryId_new" UUID;
UPDATE "DebitEntries" de SET "DsmEntryId_new" = d."SyncGuid"
  FROM "DsmEntries" d WHERE de."station_id" = d."station_id" AND de."DsmEntryId" = d."local_id";
ALTER TABLE "DebitEntries" DROP COLUMN "DsmEntryId";
ALTER TABLE "DebitEntries" RENAME COLUMN "DsmEntryId_new" TO "DsmEntryId";
ALTER TABLE "DebitEntries" ALTER COLUMN "DsmEntryId" SET NOT NULL;

-- TestingEntries.DsmEntryId → DsmEntries.SyncGuid
ALTER TABLE "TestingEntries" ADD COLUMN "DsmEntryId_new" UUID;
UPDATE "TestingEntries" te SET "DsmEntryId_new" = d."SyncGuid"
  FROM "DsmEntries" d WHERE te."station_id" = d."station_id" AND te."DsmEntryId" = d."local_id";
ALTER TABLE "TestingEntries" DROP COLUMN "DsmEntryId";
ALTER TABLE "TestingEntries" RENAME COLUMN "DsmEntryId_new" TO "DsmEntryId";
ALTER TABLE "TestingEntries" ALTER COLUMN "DsmEntryId" SET NOT NULL;

-- Expenses.DsmEntryId → DsmEntries.SyncGuid (nullable)
ALTER TABLE "Expenses" ADD COLUMN "DsmEntryId_new" UUID;
UPDATE "Expenses" e SET "DsmEntryId_new" = d."SyncGuid"
  FROM "DsmEntries" d WHERE e."station_id" = d."station_id" AND e."DsmEntryId" = d."local_id";
ALTER TABLE "Expenses" DROP COLUMN "DsmEntryId";
ALTER TABLE "Expenses" RENAME COLUMN "DsmEntryId_new" TO "DsmEntryId";

-- Expenses.ShiftId → Shifts.SyncGuid (nullable)
ALTER TABLE "Expenses" ADD COLUMN "ShiftId_new" UUID;
UPDATE "Expenses" e SET "ShiftId_new" = s."SyncGuid"
  FROM "Shifts" s WHERE e."station_id" = s."station_id" AND e."ShiftId" = s."local_id";
ALTER TABLE "Expenses" DROP COLUMN "ShiftId";
ALTER TABLE "Expenses" RENAME COLUMN "ShiftId_new" TO "ShiftId";

-- CashDenominations.DsmEntryId → DsmEntries.SyncGuid
ALTER TABLE "CashDenominations" ADD COLUMN "DsmEntryId_new" UUID;
UPDATE "CashDenominations" c SET "DsmEntryId_new" = d."SyncGuid"
  FROM "DsmEntries" d WHERE c."station_id" = d."station_id" AND c."DsmEntryId" = d."local_id";
ALTER TABLE "CashDenominations" DROP COLUMN "DsmEntryId";
ALTER TABLE "CashDenominations" RENAME COLUMN "DsmEntryId_new" TO "DsmEntryId";
ALTER TABLE "CashDenominations" ALTER COLUMN "DsmEntryId" SET NOT NULL;

-- ShiftOtherCash.ShiftId → Shifts.SyncGuid (nullable)
ALTER TABLE "ShiftOtherCash" ADD COLUMN "ShiftId_new" UUID;
UPDATE "ShiftOtherCash" soc SET "ShiftId_new" = s."SyncGuid"
  FROM "Shifts" s WHERE soc."station_id" = s."station_id" AND soc."ShiftId" = s."local_id";
ALTER TABLE "ShiftOtherCash" DROP COLUMN "ShiftId";
ALTER TABLE "ShiftOtherCash" RENAME COLUMN "ShiftId_new" TO "ShiftId";

-- ShiftFuelRates.ShiftId → Shifts.SyncGuid (nullable)
ALTER TABLE "ShiftFuelRates" ADD COLUMN "ShiftId_new" UUID;
UPDATE "ShiftFuelRates" sfr SET "ShiftId_new" = s."SyncGuid"
  FROM "Shifts" s WHERE sfr."station_id" = s."station_id" AND sfr."ShiftId" = s."local_id";
ALTER TABLE "ShiftFuelRates" DROP COLUMN "ShiftId";
ALTER TABLE "ShiftFuelRates" RENAME COLUMN "ShiftId_new" TO "ShiftId";

-- AgsNozzleReadings.AgsShiftImportId → AgsShiftImports.SyncGuid
ALTER TABLE "AgsNozzleReadings" ADD COLUMN "AgsShiftImportId_new" UUID;
UPDATE "AgsNozzleReadings" anr SET "AgsShiftImportId_new" = asi."SyncGuid"
  FROM "AgsShiftImports" asi WHERE anr."station_id" = asi."station_id" AND anr."AgsShiftImportId" = asi."local_id";
ALTER TABLE "AgsNozzleReadings" DROP COLUMN "AgsShiftImportId";
ALTER TABLE "AgsNozzleReadings" RENAME COLUMN "AgsShiftImportId_new" TO "AgsShiftImportId";
ALTER TABLE "AgsNozzleReadings" ALTER COLUMN "AgsShiftImportId" SET NOT NULL;

-- AgsTankStocks.AgsShiftImportId → AgsShiftImports.SyncGuid
ALTER TABLE "AgsTankStocks" ADD COLUMN "AgsShiftImportId_new" UUID;
UPDATE "AgsTankStocks" ats SET "AgsShiftImportId_new" = asi."SyncGuid"
  FROM "AgsShiftImports" asi WHERE ats."station_id" = asi."station_id" AND ats."AgsShiftImportId" = asi."local_id";
ALTER TABLE "AgsTankStocks" DROP COLUMN "AgsShiftImportId";
ALTER TABLE "AgsTankStocks" RENAME COLUMN "AgsShiftImportId_new" TO "AgsShiftImportId";
ALTER TABLE "AgsTankStocks" ALTER COLUMN "AgsShiftImportId" SET NOT NULL;


-- ─── STEP 7: Create new foreign key constraints ────────────────────

ALTER TABLE "DsmEntries" ADD CONSTRAINT fk_dsm_entries_shift
  FOREIGN KEY ("ShiftId") REFERENCES "Shifts" ("SyncGuid") ON DELETE CASCADE;

ALTER TABLE "NozzleReadings" ADD CONSTRAINT fk_nozzle_readings_dsm_entry
  FOREIGN KEY ("DsmEntryId") REFERENCES "DsmEntries" ("SyncGuid") ON DELETE CASCADE;

ALTER TABLE "PaymentCollections" ADD CONSTRAINT fk_payment_collections_dsm_entry
  FOREIGN KEY ("DsmEntryId") REFERENCES "DsmEntries" ("SyncGuid") ON DELETE CASCADE;

ALTER TABLE "DebitEntries" ADD CONSTRAINT fk_debit_entries_dsm_entry
  FOREIGN KEY ("DsmEntryId") REFERENCES "DsmEntries" ("SyncGuid") ON DELETE CASCADE;

ALTER TABLE "TestingEntries" ADD CONSTRAINT fk_testing_entries_dsm_entry
  FOREIGN KEY ("DsmEntryId") REFERENCES "DsmEntries" ("SyncGuid") ON DELETE CASCADE;

ALTER TABLE "Expenses" ADD CONSTRAINT fk_expenses_dsm_entry
  FOREIGN KEY ("DsmEntryId") REFERENCES "DsmEntries" ("SyncGuid") ON DELETE SET NULL;
ALTER TABLE "Expenses" ADD CONSTRAINT fk_expenses_shift
  FOREIGN KEY ("ShiftId") REFERENCES "Shifts" ("SyncGuid") ON DELETE SET NULL;

ALTER TABLE "CashDenominations" ADD CONSTRAINT fk_cash_denominations_dsm_entry
  FOREIGN KEY ("DsmEntryId") REFERENCES "DsmEntries" ("SyncGuid") ON DELETE CASCADE;

ALTER TABLE "ShiftOtherCash" ADD CONSTRAINT fk_shift_other_cash_shift
  FOREIGN KEY ("ShiftId") REFERENCES "Shifts" ("SyncGuid") ON DELETE SET NULL;

ALTER TABLE "ShiftFuelRates" ADD CONSTRAINT fk_shift_fuel_rates_shift
  FOREIGN KEY ("ShiftId") REFERENCES "Shifts" ("SyncGuid") ON DELETE SET NULL;

ALTER TABLE "AgsNozzleReadings" ADD CONSTRAINT fk_ags_nozzle_readings_import
  FOREIGN KEY ("AgsShiftImportId") REFERENCES "AgsShiftImports" ("SyncGuid") ON DELETE CASCADE;

ALTER TABLE "AgsTankStocks" ADD CONSTRAINT fk_ags_tank_stocks_import
  FOREIGN KEY ("AgsShiftImportId") REFERENCES "AgsShiftImports" ("SyncGuid") ON DELETE CASCADE;


-- ─── STEP 8: Drop old composite FK indexes and create new UUID ones ──

-- Drop old composite indexes (FK lookups)
DROP INDEX IF EXISTS idx_dsm_entries_shift;
DROP INDEX IF EXISTS idx_nozzle_readings_dsm;
DROP INDEX IF EXISTS idx_payment_collections_dsm;
DROP INDEX IF EXISTS idx_debit_entries_dsm;
DROP INDEX IF EXISTS idx_testing_entries_dsm;
DROP INDEX IF EXISTS idx_expenses_dsm;
DROP INDEX IF EXISTS idx_expenses_shift;
DROP INDEX IF EXISTS idx_cash_denominations_dsm;
DROP INDEX IF EXISTS idx_shift_other_cash_shift;
DROP INDEX IF EXISTS idx_shift_fuel_rates_shift;
DROP INDEX IF EXISTS idx_ags_nozzle_readings_import;
DROP INDEX IF EXISTS idx_ags_tank_stocks_import;

-- Drop old composite unique indexes that included integer FKs
DROP INDEX IF EXISTS idx_cash_denoms_dsm_type;

-- Create new FK indexes (single UUID column)
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

-- Recreate unique index for CashDenominations with UUID FK
CREATE UNIQUE INDEX IF NOT EXISTS idx_cash_denoms_dsm_type ON "CashDenominations" ("DsmEntryId", "CashType");

-- Machine tracking index for diagnostics
CREATE INDEX IF NOT EXISTS idx_shifts_machine ON "Shifts" ("machine_id");
CREATE INDEX IF NOT EXISTS idx_dsm_entries_machine ON "DsmEntries" ("machine_id");

COMMIT;

-- ─── VERIFICATION ───────────────────────────────────────────────────
-- Run these queries after migration to verify:

-- Check all tables have SyncGuid as PK:
-- SELECT table_name, column_name FROM information_schema.table_constraints tc
-- JOIN information_schema.key_column_usage kcu ON tc.constraint_name = kcu.constraint_name
-- WHERE tc.constraint_type = 'PRIMARY KEY' AND tc.table_schema = 'public';

-- Check FK columns are UUID type:
-- SELECT table_name, column_name, data_type FROM information_schema.columns
-- WHERE column_name IN ('ShiftId', 'DsmEntryId', 'AgsShiftImportId')
-- AND table_schema = 'public' ORDER BY table_name;

-- Check for orphaned FK values (should return 0 rows):
-- SELECT * FROM "DsmEntries" WHERE "ShiftId" NOT IN (SELECT "SyncGuid" FROM "Shifts");
