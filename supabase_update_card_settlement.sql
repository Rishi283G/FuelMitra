-- Migration script for Card Settlement feature
-- Run this in your Supabase Dashboard → SQL Editor to update the remote database schema.

-- 1. Alter "Shifts" table (required to support Shift entity synchronization)
ALTER TABLE "Shifts" 
  ADD COLUMN IF NOT EXISTS "CardSettlementPosTotal" NUMERIC DEFAULT 0;

-- 2. Alter "PaymentCollections" table
ALTER TABLE "PaymentCollections" 
  ADD COLUMN IF NOT EXISTS "CardTid" TEXT,
  ADD COLUMN IF NOT EXISTS "CardBatch" TEXT,
  ADD COLUMN IF NOT EXISTS "PhonePeTid" TEXT,
  ADD COLUMN IF NOT EXISTS "PhonePeBatch" TEXT,
  ADD COLUMN IF NOT EXISTS "PetroCardTid" TEXT,
  ADD COLUMN IF NOT EXISTS "PetroCardBatch" TEXT;

-- 3. Refresh PostgREST schema cache to ensure PostgREST picks up the new columns immediately.
NOTIFY pgrst, 'reload schema';
