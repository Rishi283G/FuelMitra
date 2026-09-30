-- ==============================================================================
-- PyroSync Dynamic: Opening Balances Migration for Supabase
-- Target Tables: "OpeningBalances", "DsmPersonalDebtors"
-- ==============================================================================

BEGIN;

-- 1. Create OpeningBalances Table
CREATE TABLE IF NOT EXISTS "OpeningBalances" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NULL,
    "machine_id" TEXT NULL,
    "OpeningBalanceId" INTEGER NULL,
    "EntityType" VARCHAR(50) NOT NULL,
    "EntityIdentifier" VARCHAR(200) NOT NULL,
    "CreditorId" UUID NULL,
    "OpeningDate" TIMESTAMP NOT NULL,
    "Amount" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "Notes" TEXT NULL,
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "CreatedBy" VARCHAR(100) NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid")
);

-- 2. Station-Scoped Partial Unique Constraint
-- Enforces that an entity (Debtor or DSM Loss) has at most ONE active opening balance per station.
CREATE UNIQUE INDEX IF NOT EXISTS "idx_opening_balances_active_unique"
    ON "OpeningBalances" ("station_id", "EntityType", "EntityIdentifier")
    WHERE "IsActive" = TRUE;

-- 3. Lookup & Performance Indexes
CREATE INDEX IF NOT EXISTS "idx_opening_balances_station_id"
    ON "OpeningBalances" ("station_id");

CREATE INDEX IF NOT EXISTS "idx_opening_balances_creditor_id"
    ON "OpeningBalances" ("CreditorId");

CREATE INDEX IF NOT EXISTS "idx_opening_balances_updated_at"
    ON "OpeningBalances" ("updated_at");

-- 4. Row Level Security & Permissions
ALTER TABLE "OpeningBalances" ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Allow all access to OpeningBalances" ON "OpeningBalances";
CREATE POLICY "Allow all access to OpeningBalances" 
    ON "OpeningBalances" 
    FOR ALL 
    USING (true) 
    WITH CHECK (true);

GRANT ALL ON TABLE "OpeningBalances" TO anon, authenticated, service_role;

-- 5. Add EntryType to DsmPersonalDebtors (Idempotent)
-- Existing records default safely to 'Operational'
ALTER TABLE "DsmPersonalDebtors" 
    ADD COLUMN IF NOT EXISTS "EntryType" VARCHAR(50) NOT NULL DEFAULT 'Operational';

CREATE INDEX IF NOT EXISTS "idx_dsm_personal_debtors_entry_type"
    ON "DsmPersonalDebtors" ("EntryType");

COMMIT;
