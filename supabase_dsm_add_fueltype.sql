-- Add FuelType to DsmSubmissionReadings (required by DSM PWA submit)
-- Run this in Supabase Dashboard → SQL Editor if submit fails with:
--   "Could not find the 'FuelType' column of 'DsmSubmissionReadings' in the schema cache"

ALTER TABLE "DsmSubmissionReadings"
  ADD COLUMN IF NOT EXISTS "FuelType" TEXT;

-- Refresh PostgREST schema cache (Supabase usually picks this up automatically;
-- if the error persists after running, go to Settings → API → Reload schema)
NOTIFY pgrst, 'reload schema';
