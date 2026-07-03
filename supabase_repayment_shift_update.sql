-- ============================================================
-- Supabase Schema Update: PaymentCollections & Related Tables
-- Run this in Supabase SQL Editor (Dashboard → SQL Editor)
-- All statements use ADD COLUMN IF NOT EXISTS -- safe to re-run.
-- ============================================================

-- 1. Core PhonePe Morning/Night columns
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeMorning"     float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeNight"       float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeCardMorning" float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeCardNight"   float8 NOT NULL DEFAULT 0.0;

-- 2. CreditCard Morning/Night columns
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "CreditCardMorning"  float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "CreditCardNight"    float8 NOT NULL DEFAULT 0.0;

-- 3. CashDeposit column
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "CashDeposit"        float8 NOT NULL DEFAULT 0.0;

-- 4. Legacy TID/Batch columns (backward compatibility)
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "CardTid"            text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "CardBatch"          text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeTid"         text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeBatch"       text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PetroCardTid"       text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PetroCardBatch"     text NULL;

-- 5. PhonePe Slot TIDs/Batches
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeTidMorning"   text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeBatchMorning" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeTidNight"     text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeBatchNight"   text NULL;

-- 6. CreditCard Slot TIDs/Batches
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "CreditCardTidMorning"   text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "CreditCardBatchMorning" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "CreditCardTidNight"     text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "CreditCardBatchNight"   text NULL;

-- 7. PetroCard Morning/Night amounts + TIDs/Batches
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PetroCardMorning"       float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PetroCardNight"         float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PetroCardTidMorning"    text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PetroCardBatchMorning"  text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PetroCardTidNight"      text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PetroCardBatchNight"    text NULL;

-- 8. Card Settlement POS Total on Shifts
ALTER TABLE "Shifts" ADD COLUMN IF NOT EXISTS "CardSettlementPosTotal" float8 NOT NULL DEFAULT 0.0;

-- 9. Debtor Repayment ShiftNumber (same-shift repayment tracking)
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "ShiftNumber" text NULL;

-- 10. Add new collection fields to PWA submission table
ALTER TABLE "DsmSubmissionCollections" ADD COLUMN IF NOT EXISTS "PetroCard" float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "DsmSubmissionCollections" ADD COLUMN IF NOT EXISTS "CashDeposit" float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "DsmSubmissionCollections" ADD COLUMN IF NOT EXISTS "Others" float8 NOT NULL DEFAULT 0.0;

-- ============================================================
-- Force reload Supabase schema cache so sync works immediately
-- ============================================================
NOTIFY pgrst, 'reload schema';


