-- ═══════════════════════════════════════════════════════════════════════
-- Supabase Schema Fix: PK Sequences & Unique SyncGuid Constraints
-- ═══════════════════════════════════════════════════════════════════════
-- 
-- PURPOSE: Fixes the Supabase UPSERT errors for PumpExpenses, DsmPersonalDebtors,
-- and other tables by ensuring that the "Id" column has an auto-incrementing 
-- sequence default (or is nullable where appropriate) and that "SyncGuid" 
-- has a UNIQUE constraint/index to resolve upsert conflicts.
--
-- RUN THIS IN YOUR SUPABASE SQL EDITOR.
-- ═══════════════════════════════════════════════════════════════════════

BEGIN;

-- 1. FIX "PumpExpenses" TABLE
CREATE SEQUENCE IF NOT EXISTS "PumpExpenses_Id_seq";
ALTER TABLE "PumpExpenses" ALTER COLUMN "Id" SET DEFAULT nextval('"PumpExpenses_Id_seq"');
ALTER SEQUENCE "PumpExpenses_Id_seq" OWNED BY "PumpExpenses"."Id";
ALTER TABLE "PumpExpenses" DROP CONSTRAINT IF EXISTS "PumpExpenses_SyncGuid_key";
ALTER TABLE "PumpExpenses" ADD CONSTRAINT "PumpExpenses_SyncGuid_key" UNIQUE ("SyncGuid");

-- 2. FIX "DsmSalaryAdjustments" TABLE
CREATE SEQUENCE IF NOT EXISTS "DsmSalaryAdjustments_Id_seq";
ALTER TABLE "DsmSalaryAdjustments" ALTER COLUMN "Id" SET DEFAULT nextval('"DsmSalaryAdjustments_Id_seq"');
ALTER SEQUENCE "DsmSalaryAdjustments_Id_seq" OWNED BY "DsmSalaryAdjustments"."Id";
ALTER TABLE "DsmSalaryAdjustments" DROP CONSTRAINT IF EXISTS "DsmSalaryAdjustments_SyncGuid_key";
ALTER TABLE "DsmSalaryAdjustments" ADD CONSTRAINT "DsmSalaryAdjustments_SyncGuid_key" UNIQUE ("SyncGuid");

-- 3. FIX "PumpMappings" TABLE
CREATE SEQUENCE IF NOT EXISTS "PumpMappings_Id_seq";
ALTER TABLE "PumpMappings" ALTER COLUMN "Id" SET DEFAULT nextval('"PumpMappings_Id_seq"');
ALTER SEQUENCE "PumpMappings_Id_seq" OWNED BY "PumpMappings"."Id";
ALTER TABLE "PumpMappings" DROP CONSTRAINT IF EXISTS "PumpMappings_SyncGuid_key";
ALTER TABLE "PumpMappings" ADD CONSTRAINT "PumpMappings_SyncGuid_key" UNIQUE ("SyncGuid");

-- 4. FIX "ExpenseCategories" TABLE
CREATE SEQUENCE IF NOT EXISTS "ExpenseCategories_Id_seq";
ALTER TABLE "ExpenseCategories" ALTER COLUMN "Id" SET DEFAULT nextval('"ExpenseCategories_Id_seq"');
ALTER SEQUENCE "ExpenseCategories_Id_seq" OWNED BY "ExpenseCategories"."Id";
ALTER TABLE "ExpenseCategories" DROP CONSTRAINT IF EXISTS "ExpenseCategories_SyncGuid_key";
ALTER TABLE "ExpenseCategories" ADD CONSTRAINT "ExpenseCategories_SyncGuid_key" UNIQUE ("SyncGuid");

-- 5. FIX "PumpExpenseCategoryItems" TABLE
CREATE SEQUENCE IF NOT EXISTS "PumpExpenseCategoryItems_Id_seq";
ALTER TABLE "PumpExpenseCategoryItems" ALTER COLUMN "Id" SET DEFAULT nextval('"PumpExpenseCategoryItems_Id_seq"');
ALTER SEQUENCE "PumpExpenseCategoryItems_Id_seq" OWNED BY "PumpExpenseCategoryItems"."Id";
ALTER TABLE "PumpExpenseCategoryItems" DROP CONSTRAINT IF EXISTS "PumpExpenseCategoryItems_SyncGuid_key";
ALTER TABLE "PumpExpenseCategoryItems" ADD CONSTRAINT "PumpExpenseCategoryItems_SyncGuid_key" UNIQUE ("SyncGuid");

-- 6. FIX "DsmPersonalDebtors" TABLE (Make Id nullable since SyncGuid/local_id is used instead)
ALTER TABLE "DsmPersonalDebtors" ALTER COLUMN "Id" DROP NOT NULL;

-- 7. FIX "DsmPersonalDebtorRepayments" TABLE (Make Id nullable since SyncGuid/local_id is used instead)
ALTER TABLE "DsmPersonalDebtorRepayments" ALTER COLUMN "Id" DROP NOT NULL;

COMMIT;
