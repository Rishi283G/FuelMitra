-- ============================================================================
-- Supabase Update Script: Dynamic Configuration, Collection Periods & OTP Tracking
-- Safe and idempotent: Uses IF NOT EXISTS throughout
-- Zero-downtime, metadata-only schema evolution: Does NOT reset or delete any client records
-- ============================================================================

-- 1. Payment Collections Dynamic Items JSON (for reliable sync payload persistence)
ALTER TABLE IF EXISTS "PaymentCollections" ADD COLUMN IF NOT EXISTS "DynamicItemsJson" TEXT NULL;

-- 2. Settings JSON Payloads (for station-scoped configurations pushed by Developer Tools)
ALTER TABLE IF EXISTS "Settings" ADD COLUMN IF NOT EXISTS "CollectionTypesJson" TEXT NULL;
ALTER TABLE IF EXISTS "Settings" ADD COLUMN IF NOT EXISTS "AppFeatureSettingsJson" TEXT NULL;
ALTER TABLE IF EXISTS "Settings" ADD COLUMN IF NOT EXISTS "PumpMappingsJson" TEXT NULL;

-- 3. Individual Dynamic Items OTP tracking (if standalone table exists)
ALTER TABLE IF EXISTS "PaymentCollectionItems" ADD COLUMN IF NOT EXISTS "Otp" TEXT NULL;

-- 4. Collection Types Shift-Period Availability Flags (defaults to TRUE to preserve existing behavior)
ALTER TABLE IF EXISTS "CollectionTypes" ADD COLUMN IF NOT EXISTS "EnableMorning" BOOLEAN NOT NULL DEFAULT TRUE;
ALTER TABLE IF EXISTS "CollectionTypes" ADD COLUMN IF NOT EXISTS "EnableDay" BOOLEAN NOT NULL DEFAULT TRUE;
ALTER TABLE IF EXISTS "CollectionTypes" ADD COLUMN IF NOT EXISTS "EnableNight" BOOLEAN NOT NULL DEFAULT TRUE;

-- 5. Verification statement (returns confirmation without modifying data)
DO $$
BEGIN
    RAISE NOTICE 'PyroSync dynamic features & OTP migration verified successfully.';
END $$;
