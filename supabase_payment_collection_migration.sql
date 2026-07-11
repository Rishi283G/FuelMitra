-- Idempotent PostgreSQL migration to sync Supabase PaymentCollections with local C# schema.
-- Copy and paste this script directly into the Supabase SQL Editor and execute it.

BEGIN;

-- PhonePe UPI Columns
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeMorning" float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeDay" float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeNight" float8 NOT NULL DEFAULT 0.0;

-- PhonePe Card/Swipe Columns
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeCardMorning" float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeCardDay" float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeCardNight" float8 NOT NULL DEFAULT 0.0;

-- Credit Card Columns
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "CreditCardMorning" float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "CreditCardDay" float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "CreditCardNight" float8 NOT NULL DEFAULT 0.0;

-- Petro Card Columns
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PetroCardMorning" float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PetroCardDay" float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PetroCardNight" float8 NOT NULL DEFAULT 0.0;

-- Credit Card TID/Batch Columns
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "CreditCardTidMorning" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "CreditCardBatchMorning" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "CreditCardTidDay" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "CreditCardBatchDay" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "CreditCardTidNight" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "CreditCardBatchNight" text NULL;

-- PhonePe TID/Batch Columns
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeTidMorning" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeBatchMorning" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeTidDay" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeBatchDay" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeTidNight" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeBatchNight" text NULL;

-- Petro Card TID/Batch Columns
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PetroCardTidMorning" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PetroCardBatchMorning" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PetroCardTidDay" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PetroCardBatchDay" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PetroCardTidNight" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PetroCardBatchNight" text NULL;

-- Notify PostgREST to reload its schema cache immediately to discover the new columns
NOTIFY pgrst, 'reload schema';

COMMIT;
