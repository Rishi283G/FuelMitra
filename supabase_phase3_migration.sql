-- Supabase Database Schema Migration — Phase 3 Features
-- Run this script in the Supabase SQL Editor to sync the Supabase database with the SQLite/EF Core model changes.

-- 1. Add columns to DsmEntries (Half-Shift Start/End times)
ALTER TABLE "DsmEntries" ADD COLUMN IF NOT EXISTS "StartTime" VARCHAR(50) NULL;
ALTER TABLE "DsmEntries" ADD COLUMN IF NOT EXISTS "EndTime" VARCHAR(50) NULL;

-- 2. Add columns to DebitEntries (Fuel, EntryTime, Payment Details, Denominations)
ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "Fuel" VARCHAR(50) NULL;
ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "EntryTime" VARCHAR(50) NULL;
ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "PaymentMethod" VARCHAR(50) NOT NULL DEFAULT 'Credit';
ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "CardTid" VARCHAR(100) NULL;
ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "CardBatch" VARCHAR(100) NULL;
ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "Denom500" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "Denom200" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "Denom100" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "Denom50" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "Denom20" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "Denom10" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "Coins" INTEGER NOT NULL DEFAULT 0;

-- 3. Add columns to CreditorRepayments (Payment details, Denominations)
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "CardTid" VARCHAR(100) NULL;
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "CardBatch" VARCHAR(100) NULL;
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "Denom500" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "Denom200" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "Denom100" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "Denom50" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "Denom20" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "Denom10" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "Coins" INTEGER NOT NULL DEFAULT 0;

-- 4. Create DsmPersonalDebtors Table
CREATE TABLE IF NOT EXISTS "DsmPersonalDebtors" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "Id" INTEGER NOT NULL,
    "DsmEntryId" UUID NULL, -- FK to DsmEntries.SyncGuid
    "DsmName" VARCHAR(200) NOT NULL,
    "Date" TIMESTAMP NOT NULL,
    "Time" VARCHAR(50) NOT NULL,
    "Amount" DOUBLE PRECISION NOT NULL,
    "FuelProduct" VARCHAR(100) NULL,
    "Remarks" TEXT NULL,
    "PaymentMethod" VARCHAR(50) NOT NULL DEFAULT 'Cash',
    "Denom500" INTEGER NOT NULL DEFAULT 0,
    "Denom200" INTEGER NOT NULL DEFAULT 0,
    "Denom100" INTEGER NOT NULL DEFAULT 0,
    "Denom50" INTEGER NOT NULL DEFAULT 0,
    "Denom20" INTEGER NOT NULL DEFAULT 0,
    "Denom10" INTEGER NOT NULL DEFAULT 0,
    "Coins" INTEGER NOT NULL DEFAULT 0,
    "CardTid" VARCHAR(100) NULL,
    "CardBatch" VARCHAR(100) NULL,
    "SequenceNumber" INTEGER NOT NULL DEFAULT 0,
    "RepaidAmount" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid"),
    CONSTRAINT fk_dsm_personal_debtors_dsm_entry FOREIGN KEY ("DsmEntryId") REFERENCES "DsmEntries" ("SyncGuid") ON DELETE SET NULL
);

-- 5. Create DsmPersonalDebtorRepayments Table
CREATE TABLE IF NOT EXISTS "DsmPersonalDebtorRepayments" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "Id" INTEGER NOT NULL,
    "DsmPersonalDebtorId" UUID NOT NULL, -- FK to DsmPersonalDebtors.SyncGuid
    "ShiftId" UUID NULL, -- FK to Shifts.SyncGuid
    "Date" TIMESTAMP NOT NULL,
    "Amount" DOUBLE PRECISION NOT NULL,
    "PaymentMethod" VARCHAR(50) NOT NULL DEFAULT 'Cash',
    "Denom500" INTEGER NOT NULL DEFAULT 0,
    "Denom200" INTEGER NOT NULL DEFAULT 0,
    "Denom100" INTEGER NOT NULL DEFAULT 0,
    "Denom50" INTEGER NOT NULL DEFAULT 0,
    "Denom20" INTEGER NOT NULL DEFAULT 0,
    "Denom10" INTEGER NOT NULL DEFAULT 0,
    "Coins" INTEGER NOT NULL DEFAULT 0,
    "CardTid" VARCHAR(100) NULL,
    "CardBatch" VARCHAR(100) NULL,
    "Source" VARCHAR(50) NOT NULL DEFAULT 'ManagerShiftTotal',
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid"),
    CONSTRAINT fk_dsm_personal_repayments_debtor FOREIGN KEY ("DsmPersonalDebtorId") REFERENCES "DsmPersonalDebtors" ("SyncGuid") ON DELETE CASCADE,
    CONSTRAINT fk_dsm_personal_repayments_shift FOREIGN KEY ("ShiftId") REFERENCES "Shifts" ("SyncGuid") ON DELETE SET NULL
);

-- 6. Enable Row Level Security (RLS) on new tables
ALTER TABLE "DsmPersonalDebtors" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "DsmPersonalDebtorRepayments" ENABLE ROW LEVEL SECURITY;

-- 7. Add Select, Insert, Update, and Delete Policies
CREATE POLICY "Allow select for all" ON "DsmPersonalDebtors" FOR SELECT USING (true);
CREATE POLICY "Allow insert for all" ON "DsmPersonalDebtors" FOR INSERT WITH CHECK (true);
CREATE POLICY "Allow update for all" ON "DsmPersonalDebtors" FOR UPDATE USING (true) WITH CHECK (true);
CREATE POLICY "Allow delete for all" ON "DsmPersonalDebtors" FOR DELETE USING (true);

CREATE POLICY "Allow select for all repayments" ON "DsmPersonalDebtorRepayments" FOR SELECT USING (true);
CREATE POLICY "Allow insert for all repayments" ON "DsmPersonalDebtorRepayments" FOR INSERT WITH CHECK (true);
CREATE POLICY "Allow update for all repayments" ON "DsmPersonalDebtorRepayments" FOR UPDATE USING (true) WITH CHECK (true);
CREATE POLICY "Allow delete for all repayments" ON "DsmPersonalDebtorRepayments" FOR DELETE USING (true);

-- 8. Add updated_at trigger for automatic timestamp tracking
CREATE OR REPLACE TRIGGER update_dsm_personal_debtors_modtime BEFORE UPDATE ON "DsmPersonalDebtors" FOR EACH ROW EXECUTE PROCEDURE update_updated_at_column();
CREATE OR REPLACE TRIGGER update_dsm_personal_debtor_repayments_modtime BEFORE UPDATE ON "DsmPersonalDebtorRepayments" FOR EACH ROW EXECUTE PROCEDURE update_updated_at_column();

-- 9. Add Metadata JSONB column to DsmSubmissions
ALTER TABLE "DsmSubmissions" ADD COLUMN IF NOT EXISTS "Metadata" JSONB NULL;
