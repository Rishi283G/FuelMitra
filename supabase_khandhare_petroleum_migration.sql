-- Supabase Schema Migration Script for Khandhare Petroleum Entries, Fuel Tankers, Tank Daily Stocks, and Salary Tables
-- Run this script in the Supabase SQL Editor for your project (https://app.supabase.com -> SQL Editor).

BEGIN;

-- 1. KhandharePetroleumEntries Table
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
    "Amount" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "Date" TIMESTAMP NOT NULL DEFAULT NOW(),
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid")
);

ALTER TABLE "KhandharePetroleumEntries" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "Allow all access to KhandharePetroleumEntries" ON "KhandharePetroleumEntries";
CREATE POLICY "Allow all access to KhandharePetroleumEntries" ON "KhandharePetroleumEntries" FOR ALL USING (true) WITH CHECK (true);
GRANT ALL ON TABLE "KhandharePetroleumEntries" TO anon, authenticated, service_role;


-- 2. FuelTankers Table
CREATE TABLE IF NOT EXISTS "FuelTankers" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "FuelTankerId" INTEGER NULL,
    "TankerDate" TIMESTAMP NOT NULL DEFAULT NOW(),
    "TankerNumber" VARCHAR(50) NULL,
    "InvoiceNumber" VARCHAR(100) NOT NULL DEFAULT '',
    "FuelType" VARCHAR(10) NOT NULL DEFAULT 'HSD',
    "Quantity" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "PurchaseRate" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "TotalAmount" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "Density" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "Remarks" TEXT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid")
);

ALTER TABLE "FuelTankers" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "Allow all access to FuelTankers" ON "FuelTankers";
CREATE POLICY "Allow all access to FuelTankers" ON "FuelTankers" FOR ALL USING (true) WITH CHECK (true);
GRANT ALL ON TABLE "FuelTankers" TO anon, authenticated, service_role;


-- 3. TankDailyStocks Table
CREATE TABLE IF NOT EXISTS "TankDailyStocks" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "Id" INTEGER NULL,
    "Date" TIMESTAMP NOT NULL DEFAULT NOW(),
    "FuelType" VARCHAR(10) NOT NULL DEFAULT 'HSD',
    "OpeningStock" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "DaySaleLitres" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "TestingLitres" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "PurchasedLitres" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "ClosingStock" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "DipMm" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "ManualStock" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "LastUpdated" TIMESTAMP NOT NULL DEFAULT NOW(),
    "ShiftId" UUID NULL,
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid")
);

ALTER TABLE "TankDailyStocks" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "Allow all access to TankDailyStocks" ON "TankDailyStocks";
CREATE POLICY "Allow all access to TankDailyStocks" ON "TankDailyStocks" FOR ALL USING (true) WITH CHECK (true);
GRANT ALL ON TABLE "TankDailyStocks" TO anon, authenticated, service_role;


-- 4. DsmSalaryHistories Table
CREATE TABLE IF NOT EXISTS "DsmSalaryHistories" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "DsmSalaryHistoryId" INTEGER NULL,
    "DsmProfileId" UUID NULL,
    "OldBaseSalary" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "NewBaseSalary" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "OldSalaryType" VARCHAR(50) NOT NULL DEFAULT '',
    "NewSalaryType" VARCHAR(50) NOT NULL DEFAULT '',
    "ChangeDate" TIMESTAMP NOT NULL DEFAULT NOW(),
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid")
);

ALTER TABLE "DsmSalaryHistories" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "Allow all access to DsmSalaryHistories" ON "DsmSalaryHistories";
CREATE POLICY "Allow all access to DsmSalaryHistories" ON "DsmSalaryHistories" FOR ALL USING (true) WITH CHECK (true);
GRANT ALL ON TABLE "DsmSalaryHistories" TO anon, authenticated, service_role;


-- 5. DsmSalaryPayments Table
CREATE TABLE IF NOT EXISTS "DsmSalaryPayments" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "DsmSalaryPaymentId" INTEGER NULL,
    "DsmProfileId" UUID NULL,
    "Year" INTEGER NOT NULL,
    "Month" INTEGER NOT NULL,
    "NetSalary" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "PaidAmount" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "PaymentDate" TIMESTAMP NOT NULL DEFAULT NOW(),
    "PaymentMode" VARCHAR(50) NOT NULL DEFAULT 'Cash',
    "Remarks" TEXT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid")
);

ALTER TABLE "DsmSalaryPayments" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "Allow all access to DsmSalaryPayments" ON "DsmSalaryPayments";
CREATE POLICY "Allow all access to DsmSalaryPayments" ON "DsmSalaryPayments" FOR ALL USING (true) WITH CHECK (true);
GRANT ALL ON TABLE "DsmSalaryPayments" TO anon, authenticated, service_role;


-- 6. Reload PostgREST Schema Cache
NOTIFY pgrst, 'reload schema';

COMMIT;
