-- ═══════════════════════════════════════════════════════════════════════
-- Supabase Schema Update: Add Salary and Joining Date Columns to DsmProfiles
-- ═══════════════════════════════════════════════════════════════════════
-- Run this script in the Supabase Dashboard SQL Editor.
-- ═══════════════════════════════════════════════════════════════════════

ALTER TABLE "DsmProfiles" ADD COLUMN IF NOT EXISTS "SalaryType" VARCHAR(50) NOT NULL DEFAULT 'FixedMonthly';
ALTER TABLE "DsmProfiles" ADD COLUMN IF NOT EXISTS "BaseSalary" DOUBLE PRECISION NOT NULL DEFAULT 12000.0;
ALTER TABLE "DsmProfiles" ADD COLUMN IF NOT EXISTS "JoiningDate" TIMESTAMP NULL;
