-- Ensure TID and Batch columns exist in PaymentCollections table on Supabase
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "CardTid" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "CardBatch" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeTid" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeBatch" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PetroCardTid" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PetroCardBatch" text NULL;
