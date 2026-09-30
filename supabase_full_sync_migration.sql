-- ==============================================================================
-- Full Supabase Schema Synchronization Migration for PyroSync
-- Run this SQL directly in the Supabase SQL Editor (Dashboard > SQL Editor > New Query)
-- It is 100% idempotent (safe to run multiple times).
-- ==============================================================================

BEGIN;

-- 1. Upgrade PaymentCollections with DynamicItemsJson and all digital mode columns
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "DynamicItemsJson" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "CreditCardMorning" float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "CreditCardDay" float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "CreditCardNight" float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeMorning" float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeDay" float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeNight" float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeCardMorning" float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeCardDay" float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeCardNight" float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PetroCardMorning" float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PetroCardDay" float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PetroCardNight" float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "CardTid" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "CardBatch" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeTid" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeBatch" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PetroCardTid" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PetroCardBatch" text NULL;

-- 2. Upgrade Settings with Dynamic JSON fields and configuration rates
ALTER TABLE "Settings" ADD COLUMN IF NOT EXISTS "TankDefinitionsJson" text NULL;
ALTER TABLE "Settings" ADD COLUMN IF NOT EXISTS "CollectionTypesJson" text NULL;
ALTER TABLE "Settings" ADD COLUMN IF NOT EXISTS "AppFeatureSettingsJson" text NULL;
ALTER TABLE "Settings" ADD COLUMN IF NOT EXISTS "PumpMappingsJson" text NULL;
ALTER TABLE "Settings" ADD COLUMN IF NOT EXISTS "CngRate" float8 NOT NULL DEFAULT 85.0;
ALTER TABLE "Settings" ADD COLUMN IF NOT EXISTS "Shift1Manager" text NULL;
ALTER TABLE "Settings" ADD COLUMN IF NOT EXISTS "Shift2Manager" text NULL;
ALTER TABLE "Settings" ADD COLUMN IF NOT EXISTS "Shift3Manager" text NULL;

-- 3. Upgrade DsmEntries for connected pumps and reconciliation
ALTER TABLE "DsmEntries" ADD COLUMN IF NOT EXISTS "ConnectedPumpId" integer NULL;
ALTER TABLE "DsmEntries" ADD COLUMN IF NOT EXISTS "ReconciledToPumpId" integer NULL;
ALTER TABLE "DsmEntries" ADD COLUMN IF NOT EXISTS "StartTime" text NULL;
ALTER TABLE "DsmEntries" ADD COLUMN IF NOT EXISTS "EndTime" text NULL;
ALTER TABLE "DsmEntries" ADD COLUMN IF NOT EXISTS "IsReconciled" boolean NOT NULL DEFAULT FALSE;

-- 4. Create Dynamic Configuration Tables if not already existing

-- TankDefinitions
CREATE TABLE IF NOT EXISTS "TankDefinitions" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "TankId" INTEGER NOT NULL,
    "TankName" VARCHAR(100) NOT NULL,
    "FuelType" VARCHAR(50) NOT NULL,
    "CapacityKL" DOUBLE PRECISION NOT NULL DEFAULT 20.0,
    "HasTesting" BOOLEAN NOT NULL DEFAULT TRUE,
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL
);

-- CollectionTypes
CREATE TABLE IF NOT EXISTS "CollectionTypes" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "Id" INTEGER NOT NULL,
    "Code" VARCHAR(100) NOT NULL,
    "DisplayName" VARCHAR(100) NOT NULL,
    "Category" VARCHAR(50) NOT NULL,
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "DisplayOrder" INTEGER NOT NULL DEFAULT 0,
    "RequiresTid" BOOLEAN NOT NULL DEFAULT FALSE,
    "RequiresBatch" BOOLEAN NOT NULL DEFAULT FALSE,
    "RequiresSlot" BOOLEAN NOT NULL DEFAULT FALSE,
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL
);

-- PumpMappings
CREATE TABLE IF NOT EXISTS "PumpMappings" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "PumpMappingId" INTEGER NOT NULL,
    "PumpId" INTEGER NOT NULL,
    "NozzleNumber" INTEGER NOT NULL,
    "FuelType" VARCHAR(50) NOT NULL,
    "TankName" VARCHAR(100) NOT NULL,
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL
);

-- AppFeatureSettings
CREATE TABLE IF NOT EXISTS "AppFeatureSettings" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "Id" INTEGER NOT NULL,
    "FeatureKey" VARCHAR(100) NOT NULL,
    "DisplayName" VARCHAR(100) NOT NULL,
    "Description" TEXT NULL,
    "IsEnabled" BOOLEAN NOT NULL DEFAULT TRUE,
    "Category" VARCHAR(50) NOT NULL,
    "RequiresRestart" BOOLEAN NOT NULL DEFAULT FALSE,
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL
);

-- PaymentCollectionItems
CREATE TABLE IF NOT EXISTS "PaymentCollectionItems" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "Id" INTEGER NOT NULL,
    "PaymentId" UUID NULL,
    "CollectionTypeCode" VARCHAR(100) NOT NULL,
    "Amount" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "Tid" TEXT NULL,
    "Batch" TEXT NULL,
    "Slot" VARCHAR(50) NULL,
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL
);

-- 5. Enable Row Level Security (RLS) and grant open access for authenticated & anon sync
ALTER TABLE "PaymentCollections" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "Settings" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "DsmEntries" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "TankDefinitions" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "CollectionTypes" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "PumpMappings" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "AppFeatureSettings" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "PaymentCollectionItems" ENABLE ROW LEVEL SECURITY;

DO $$
BEGIN
    DROP POLICY IF EXISTS "Allow anon all on PaymentCollections" ON "PaymentCollections";
    CREATE POLICY "Allow anon all on PaymentCollections" ON "PaymentCollections" FOR ALL TO public USING (true) WITH CHECK (true);

    DROP POLICY IF EXISTS "Allow anon all on Settings" ON "Settings";
    CREATE POLICY "Allow anon all on Settings" ON "Settings" FOR ALL TO public USING (true) WITH CHECK (true);

    DROP POLICY IF EXISTS "Allow anon all on DsmEntries" ON "DsmEntries";
    CREATE POLICY "Allow anon all on DsmEntries" ON "DsmEntries" FOR ALL TO public USING (true) WITH CHECK (true);

    DROP POLICY IF EXISTS "Allow anon all on TankDefinitions" ON "TankDefinitions";
    CREATE POLICY "Allow anon all on TankDefinitions" ON "TankDefinitions" FOR ALL TO public USING (true) WITH CHECK (true);

    DROP POLICY IF EXISTS "Allow anon all on CollectionTypes" ON "CollectionTypes";
    CREATE POLICY "Allow anon all on CollectionTypes" ON "CollectionTypes" FOR ALL TO public USING (true) WITH CHECK (true);

    DROP POLICY IF EXISTS "Allow anon all on PumpMappings" ON "PumpMappings";
    CREATE POLICY "Allow anon all on PumpMappings" ON "PumpMappings" FOR ALL TO public USING (true) WITH CHECK (true);

    DROP POLICY IF EXISTS "Allow anon all on AppFeatureSettings" ON "AppFeatureSettings";
    CREATE POLICY "Allow anon all on AppFeatureSettings" ON "AppFeatureSettings" FOR ALL TO public USING (true) WITH CHECK (true);

    DROP POLICY IF EXISTS "Allow anon all on PaymentCollectionItems" ON "PaymentCollectionItems";
    CREATE POLICY "Allow anon all on PaymentCollectionItems" ON "PaymentCollectionItems" FOR ALL TO public USING (true) WITH CHECK (true);
END $$;

-- 6. Reload PostgREST schema cache
NOTIFY pgrst, 'reload schema';

COMMIT;
