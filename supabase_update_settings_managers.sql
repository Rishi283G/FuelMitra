-- Supabase Schema Update: Add Manager columns to Settings, CompletedDate to DsmPumpAssignments, and KhandharePetroleumEntries table with VehicleNumber
-- Run this script in your Supabase SQL Editor.

BEGIN;

-- 1. Add manager columns to Settings table
ALTER TABLE "Settings" ADD COLUMN IF NOT EXISTS "Shift1Manager" text NULL;
ALTER TABLE "Settings" ADD COLUMN IF NOT EXISTS "Shift2Manager" text NULL;
ALTER TABLE "Settings" ADD COLUMN IF NOT EXISTS "Shift3Manager" text NULL;

-- 2. Add CompletedDate to DsmPumpAssignments table
ALTER TABLE "DsmPumpAssignments" ADD COLUMN IF NOT EXISTS "CompletedDate" text NULL;

-- 3. Create KhandharePetroleumEntries table if it does not exist
CREATE TABLE IF NOT EXISTS "KhandharePetroleumEntries" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "Id" INTEGER NULL,
    "DsmEntryId" UUID NULL,
    "DsmName" VARCHAR(200) NOT NULL DEFAULT '',
    "Name" VARCHAR(200) NOT NULL DEFAULT '',
    "SlipNumber" VARCHAR(50) NOT NULL DEFAULT '',
    "VehicleNumber" VARCHAR(50) NULL,
    "Amount" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "Date" TIMESTAMP NOT NULL DEFAULT NOW(),
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid")
);

-- 4. Add VehicleNumber column if table already existed without it
ALTER TABLE "KhandharePetroleumEntries" ADD COLUMN IF NOT EXISTS "VehicleNumber" text NULL;

-- 5. Enable RLS and set policies for KhandharePetroleumEntries
ALTER TABLE "KhandharePetroleumEntries" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "Allow all access to KhandharePetroleumEntries" ON "KhandharePetroleumEntries";
CREATE POLICY "Allow all access to KhandharePetroleumEntries" ON "KhandharePetroleumEntries" FOR ALL USING (true) WITH CHECK (true);
GRANT ALL ON TABLE "KhandharePetroleumEntries" TO anon, authenticated, service_role;

-- 6. Ensure CreditorRepayments table has all columns and SyncGuid constraint for upsert sync
CREATE TABLE IF NOT EXISTS "CreditorRepayments" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL DEFAULT '',
    "local_id" INTEGER NOT NULL DEFAULT 0,
    "machine_id" TEXT NULL,
    "CreditorRepaymentId" INTEGER NULL,
    "RepaymentDate" TIMESTAMP NOT NULL DEFAULT NOW(),
    "CreditorName" VARCHAR(200) NOT NULL DEFAULT '',
    "PaymentMode" VARCHAR(50) NOT NULL DEFAULT 'Cash',
    "ChequeNo" VARCHAR(100) NULL,
    "Amount" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "CardTid" VARCHAR(100) NULL,
    "CardBatch" VARCHAR(100) NULL,
    "Denom500" INTEGER NOT NULL DEFAULT 0,
    "Denom200" INTEGER NOT NULL DEFAULT 0,
    "Denom100" INTEGER NOT NULL DEFAULT 0,
    "Denom50" INTEGER NOT NULL DEFAULT 0,
    "Denom20" INTEGER NOT NULL DEFAULT 0,
    "Denom10" INTEGER NOT NULL DEFAULT 0,
    "Coins" INTEGER NOT NULL DEFAULT 0,
    "ShiftNumber" VARCHAR(50) NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid")
);

ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "SyncGuid" UUID DEFAULT gen_random_uuid();
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "station_id" TEXT NULL;
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "local_id" INTEGER NULL;
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "machine_id" TEXT NULL;
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "CardTid" VARCHAR(100) NULL;
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "CardBatch" VARCHAR(100) NULL;
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "Denom500" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "Denom200" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "Denom100" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "Denom50" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "Denom20" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "Denom10" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "Coins" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "ShiftNumber" VARCHAR(50) NULL;
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "CreatedAt" TIMESTAMP NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'CreditorRepayments_SyncGuid_key' OR conname = 'CreditorRepayments_pkey'
    ) THEN
        ALTER TABLE "CreditorRepayments" ADD CONSTRAINT "CreditorRepayments_SyncGuid_key" UNIQUE ("SyncGuid");
    END IF;
END $$;

ALTER TABLE "CreditorRepayments" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "Allow all access to CreditorRepayments" ON "CreditorRepayments";
CREATE POLICY "Allow all access to CreditorRepayments" ON "CreditorRepayments" FOR ALL USING (true) WITH CHECK (true);
GRANT ALL ON TABLE "CreditorRepayments" TO anon, authenticated, service_role;

COMMIT;
