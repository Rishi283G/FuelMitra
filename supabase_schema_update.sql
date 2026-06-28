-- Supabase Database Schema Sync/Upgrade Script
-- Run this script in your Supabase SQL Editor to align the remote Postgres database with the latest local SQLite schema.
-- This script is safe to run multiple times (idempotent).

BEGIN;

-- 1. Ensure all Core Tables exist before running migrations

CREATE TABLE IF NOT EXISTS "Shifts" (
    "ShiftId" serial PRIMARY KEY,
    "ShiftDate" date NOT NULL,
    "ShiftType" text NOT NULL,
    "IsLocked" boolean NOT NULL DEFAULT FALSE,
    "SyncGuid" text NULL
);

CREATE TABLE IF NOT EXISTS "DsmProfiles" (
    "DsmProfileId" serial PRIMARY KEY,
    "DsmName" text NOT NULL,
    "SalaryType" text NOT NULL DEFAULT 'FixedMonthly',
    "BaseSalary" float8 NOT NULL DEFAULT 12000.0,
    "JoiningDate" text NULL,
    "SyncGuid" text NULL
);

CREATE TABLE IF NOT EXISTS "DsmSalaryAdjustments" (
    "Id" serial PRIMARY KEY,
    "DsmProfileId" integer NOT NULL,
    "AdjustmentDate" timestamp with time zone NOT NULL,
    "SyncGuid" text NULL
);

CREATE TABLE IF NOT EXISTS "DsmEntries" (
    "DsmEntryId" serial PRIMARY KEY,
    "ShiftId" integer NOT NULL,
    "DsmName" text NOT NULL,
    "PumpId" integer NOT NULL,
    "GrossSales" numeric NOT NULL DEFAULT 0.0,
    "TotalInDirect" numeric NOT NULL DEFAULT 0.0,
    "TotalCreditors" numeric NOT NULL DEFAULT 0.0,
    "TotalCollection" numeric NOT NULL DEFAULT 0.0,
    "Mismatch" numeric NOT NULL DEFAULT 0.0,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "SyncGuid" text NULL
);

CREATE TABLE IF NOT EXISTS "PaymentCollections" (
    "PaymentId" serial PRIMARY KEY,
    "DsmEntryId" integer NOT NULL,
    "Others" float8 NOT NULL DEFAULT 0.0,
    "SyncGuid" text NULL
);

CREATE TABLE IF NOT EXISTS "DebitEntries" (
    "DebitId" serial PRIMARY KEY,
    "DsmEntryId" integer NOT NULL,
    "DebtorName" text NOT NULL,
    "Amount" float8 NOT NULL,
    "SyncGuid" text NULL
);

CREATE TABLE IF NOT EXISTS "CreditorRepayments" (
    "RepaymentId" serial PRIMARY KEY,
    "CreditorId" integer NOT NULL,
    "RepaymentDate" timestamp with time zone NOT NULL,
    "Amount" float8 NOT NULL,
    "PaymentMode" text NOT NULL,
    "ChequeNo" text NULL,
    "SyncGuid" text NULL
);

CREATE TABLE IF NOT EXISTS "SyncChangeLogs" (
    "Id" serial PRIMARY KEY,
    "TableName" text NOT NULL,
    "RecordId" integer NOT NULL,
    "Operation" text NOT NULL,
    "Timestamp" timestamp with time zone NOT NULL,
    "IsSynced" boolean NOT NULL DEFAULT FALSE,
    "SyncGuid" text NULL
);

CREATE TABLE IF NOT EXISTS "Settings" (
    "SettingId" serial PRIMARY KEY,
    "HsdRate" float8 NOT NULL DEFAULT 90.35,
    "MsIRate" float8 NOT NULL DEFAULT 103.81,
    "MsIIRate" float8 NOT NULL DEFAULT 103.81,
    "PumpStationName" text NOT NULL DEFAULT 'Mitali Service Station',
    "LastUpdated" timestamp with time zone NOT NULL,
    "SyncGuid" text NULL
);

CREATE TABLE IF NOT EXISTS "DsmSubmissions" (
    "Id" text PRIMARY KEY,
    "DsmUserId" text NOT NULL,
    "StationId" text NOT NULL,
    "PumpId" integer NOT NULL,
    "ShiftDate" text NOT NULL,
    "ShiftType" text NOT NULL,
    "Status" text NOT NULL DEFAULT 'Pending',
    "Notes" text NULL,
    "AttachmentUrl" text NULL,
    "SubmittedAt" text NOT NULL
);


-- 2. Upgrades to Existing Tables (Adding new columns if they do not exist)

-- Table: Settings
ALTER TABLE "Settings" ADD COLUMN IF NOT EXISTS "CngRate" float8 NOT NULL DEFAULT 85.0;

-- Table: DsmSubmissions
ALTER TABLE "DsmSubmissions" ADD COLUMN IF NOT EXISTS "Metadata" jsonb NULL;

-- Table: DsmProfiles
ALTER TABLE "DsmProfiles" ADD COLUMN IF NOT EXISTS "PendingAdvance" float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "DsmProfiles" ADD COLUMN IF NOT EXISTS "MonthlyAdvanceDeduction" float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "DsmProfiles" ADD COLUMN IF NOT EXISTS "MobileNumber" text NULL;
ALTER TABLE "DsmProfiles" ADD COLUMN IF NOT EXISTS "SalaryType" text NOT NULL DEFAULT 'FixedMonthly';
ALTER TABLE "DsmProfiles" ADD COLUMN IF NOT EXISTS "BaseSalary" float8 NOT NULL DEFAULT 12000.0;
ALTER TABLE "DsmProfiles" ADD COLUMN IF NOT EXISTS "JoiningDate" text NULL;

-- Table: DsmSalaryAdjustments
ALTER TABLE "DsmSalaryAdjustments" ADD COLUMN IF NOT EXISTS "PendingAdvanceDeduction" float8 NOT NULL DEFAULT 0.0;

-- Table: DsmEntries
ALTER TABLE "DsmEntries" ADD COLUMN IF NOT EXISTS "ConnectedPumpId" integer NULL;
ALTER TABLE "DsmEntries" ADD COLUMN IF NOT EXISTS "ReconciledToPumpId" integer NULL;
ALTER TABLE "DsmEntries" ADD COLUMN IF NOT EXISTS "StartTime" text NULL;
ALTER TABLE "DsmEntries" ADD COLUMN IF NOT EXISTS "EndTime" text NULL;
ALTER TABLE "DsmEntries" ADD COLUMN IF NOT EXISTS "IsReconciled" boolean NOT NULL DEFAULT FALSE;

-- Table: PaymentCollections
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "CashDeposit" float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeMorning" float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeNight" float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeCardMorning" float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeCardNight" float8 NOT NULL DEFAULT 0.0;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "CardTid" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "CardBatch" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeTid" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PhonePeBatch" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PetroCardTid" text NULL;
ALTER TABLE "PaymentCollections" ADD COLUMN IF NOT EXISTS "PetroCardBatch" text NULL;

-- Table: DebitEntries

ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "Fuel" text NULL;
ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "EntryTime" text NULL;
ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "PaymentMethod" text NOT NULL DEFAULT 'Credit';
ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "CardTid" text NULL;
ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "CardBatch" text NULL;
ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "Denom500" integer NOT NULL DEFAULT 0;
ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "Denom200" integer NOT NULL DEFAULT 0;
ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "Denom100" integer NOT NULL DEFAULT 0;
ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "Denom50" integer NOT NULL DEFAULT 0;
ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "Denom20" integer NOT NULL DEFAULT 0;
ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "Denom10" integer NOT NULL DEFAULT 0;
ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "Coins" integer NOT NULL DEFAULT 0;
ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "CreatedAt" timestamp with time zone NULL;
ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "UpdatedAt" timestamp with time zone NULL;
ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "Remarks" text NULL;
ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "ChequeNo" text NULL;
ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "VehicleNumber" text NULL;
ALTER TABLE "DebitEntries" ADD COLUMN IF NOT EXISTS "SlipNumber" text NULL;

-- Table: CreditorRepayments
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "CardTid" text NULL;
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "CardBatch" text NULL;
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "Denom500" integer NOT NULL DEFAULT 0;
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "Denom200" integer NOT NULL DEFAULT 0;
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "Denom100" integer NOT NULL DEFAULT 0;
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "Denom50" integer NOT NULL DEFAULT 0;
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "Denom20" integer NOT NULL DEFAULT 0;
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "Denom10" integer NOT NULL DEFAULT 0;
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "Coins" integer NOT NULL DEFAULT 0;
ALTER TABLE "CreditorRepayments" ADD COLUMN IF NOT EXISTS "CreatedAt" timestamp with time zone NULL;

-- Table: Shifts
ALTER TABLE "Shifts" ADD COLUMN IF NOT EXISTS "CardSettlementPosTotal" float8 NOT NULL DEFAULT 0.0;

-- Table: SyncChangeLogs
ALTER TABLE "SyncChangeLogs" ADD COLUMN IF NOT EXISTS "StationId" text NULL;
ALTER TABLE "SyncChangeLogs" ADD COLUMN IF NOT EXISTS "MachineId" text NULL;
ALTER TABLE "SyncChangeLogs" ADD COLUMN IF NOT EXISTS "SyncGuid" text NULL;
ALTER TABLE "SyncChangeLogs" ADD COLUMN IF NOT EXISTS "RecordGuid" text NULL;


-- 3. New Table Creations (Creating tables if they do not exist)

CREATE TABLE IF NOT EXISTS "DebtorVehicles" (
    "Id" serial PRIMARY KEY,
    "CreditorId" integer NOT NULL,
    "VehicleNumber" text NOT NULL,
    "IsActive" boolean NOT NULL DEFAULT TRUE,
    "SyncGuid" text NULL
);

CREATE TABLE IF NOT EXISTS "PumpMappings" (
    "Id" serial PRIMARY KEY,
    "PumpId" integer NOT NULL,
    "NozzleNumber" integer NOT NULL,
    "FuelType" text NOT NULL,
    "IsActive" boolean NOT NULL DEFAULT TRUE,
    "SyncGuid" text NULL
);

CREATE TABLE IF NOT EXISTS "AuditLogs" (
    "AuditLogId" serial PRIMARY KEY,
    "TableName" text NOT NULL,
    "RecordId" integer NOT NULL,
    "Action" text NOT NULL DEFAULT 'Update',
    "FieldName" text NULL,
    "OldValue" text NULL,
    "NewValue" text NULL,
    "ModifiedBy" text NOT NULL,
    "ModifiedAt" timestamp with time zone NOT NULL,
    "Reason" text NULL,
    "SyncGuid" text NULL
);

CREATE TABLE IF NOT EXISTS "DayLocks" (
    "DayLockId" serial PRIMARY KEY,
    "LockDate" timestamp with time zone NOT NULL,
    "LockedAt" timestamp with time zone NOT NULL,
    "LockedBy" text NOT NULL,
    "IsLocked" boolean NOT NULL DEFAULT TRUE,
    "UnlockedAt" timestamp with time zone NULL,
    "UnlockedBy" text NULL,
    "UnlockReason" text NULL,
    "SyncGuid" text NULL
);

-- PumpExpenses: matches PumpExpense.cs model exactly (individual expense columns, not category-based)
CREATE TABLE IF NOT EXISTS "PumpExpenses" (
    "Id" serial PRIMARY KEY,
    "ExpenseDate" timestamp with time zone NOT NULL,
    "Rent" float8 NOT NULL DEFAULT 0.0,
    "Salary" float8 NOT NULL DEFAULT 0.0,
    "TripSheetLoss" float8 NOT NULL DEFAULT 0.0,
    "DsmShort" float8 NOT NULL DEFAULT 0.0,
    "BankingExpenses" float8 NOT NULL DEFAULT 0.0,
    "BpclPortalExpenses" float8 NOT NULL DEFAULT 0.0,
    "FuelAndTravel" float8 NOT NULL DEFAULT 0.0,
    "OilPurchase" float8 NOT NULL DEFAULT 0.0,
    "RepairsAndMaintenance" float8 NOT NULL DEFAULT 0.0,
    "ElectricityExpenses" float8 NOT NULL DEFAULT 0.0,
    "OfficeExpenses" float8 NOT NULL DEFAULT 0.0,
    "PrintingExpense" float8 NOT NULL DEFAULT 0.0,
    "OtherDescription" text NOT NULL DEFAULT '',
    "OtherAmount" float8 NOT NULL DEFAULT 0.0,
    "Remarks" text NOT NULL DEFAULT '',
    "CreatedAt" timestamp with time zone NOT NULL,
    "SyncGuid" text NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS "DsmPersonalDebtors" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "Id" INTEGER NULL,
    "DsmEntryId" UUID NULL,
    "DsmName" VARCHAR(200) NOT NULL,
    "Date" TIMESTAMP NOT NULL,
    "Time" VARCHAR(50) NOT NULL,
    "Amount" DOUBLE PRECISION NOT NULL,
    "FuelProduct" VARCHAR(100) NULL,
    "Remarks" TEXT NULL,
    "PaymentMethod" VARCHAR(50) NOT NULL DEFAULT 'Cash',
    "Denom500" INTEGER NOT NULL DEFAULT 0,
    "Denom200" INTEGER NOT NULL DEFAULT 0,
    "Denom100" INTEGER NOT NULL DEFAULT 0,
    "Denom50" INTEGER NOT NULL DEFAULT 0,
    "Denom20" INTEGER NOT NULL DEFAULT 0,
    "Denom10" INTEGER NOT NULL DEFAULT 0,
    "Coins" INTEGER NOT NULL DEFAULT 0,
    "CardTid" VARCHAR(100) NULL,
    "CardBatch" VARCHAR(100) NULL,
    "SequenceNumber" INTEGER NOT NULL DEFAULT 0,
    "RepaidAmount" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "DeductFromSalary" BOOLEAN NOT NULL DEFAULT TRUE,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid")
);

CREATE TABLE IF NOT EXISTS "DsmPersonalDebtorRepayments" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "Id" INTEGER NULL,
    "DsmPersonalDebtorId" UUID NOT NULL,
    "ShiftId" UUID NULL,
    "Date" TIMESTAMP NOT NULL,
    "Amount" DOUBLE PRECISION NOT NULL,
    "PaymentMethod" VARCHAR(50) NOT NULL DEFAULT 'Cash',
    "Denom500" INTEGER NOT NULL DEFAULT 0,
    "Denom200" INTEGER NOT NULL DEFAULT 0,
    "Denom100" INTEGER NOT NULL DEFAULT 0,
    "Denom50" INTEGER NOT NULL DEFAULT 0,
    "Denom20" INTEGER NOT NULL DEFAULT 0,
    "Denom10" INTEGER NOT NULL DEFAULT 0,
    "Coins" INTEGER NOT NULL DEFAULT 0,
    "CardTid" VARCHAR(100) NULL,
    "CardBatch" VARCHAR(100) NULL,
    "Source" VARCHAR(50) NOT NULL DEFAULT 'ManagerShiftTotal',
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid")
);

COMMIT;
