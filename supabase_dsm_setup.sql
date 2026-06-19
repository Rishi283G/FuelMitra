-- ============================================================
-- PyroSync Max — Supabase DSM Tables Setup Script
-- Run this in: Supabase Dashboard → SQL Editor
-- ============================================================

-- ── 1. DsmSubmissions ─────────────────────────────────────────
CREATE TABLE IF NOT EXISTS public."DsmSubmissions" (
  "Id"            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "DsmUserId"     text NOT NULL,           -- SyncGuid of DsmUser
  "StationId"     text NOT NULL,
  "PumpId"        integer NOT NULL,
  "ShiftDate"     date NOT NULL,
  "ShiftType"     text NOT NULL CHECK ("ShiftType" IN ('A','B','C')),
  "Status"        text NOT NULL DEFAULT 'Pending' CHECK ("Status" IN ('Pending','Approved','Rejected','Expired')),
  "Notes"         text,
  "AttachmentUrl" text,
  "SubmittedAt"   timestamptz NOT NULL DEFAULT now(),
  "ApprovedAt"    timestamptz,
  "ApprovedBy"    text,
  "ApprovalLockId" text,
  "RejectionReason" text
);

-- ── 2. DsmSubmissionReadings ──────────────────────────────────
CREATE TABLE IF NOT EXISTS public."DsmSubmissionReadings" (
  "Id"              bigserial PRIMARY KEY,
  "SubmissionId"    uuid NOT NULL REFERENCES public."DsmSubmissions"("Id") ON DELETE CASCADE,
  "PumpId"          integer NOT NULL,
  "NozzleId"        integer NOT NULL,
  "FuelType"        text,
  "OpeningReading"  numeric(12,3) NOT NULL DEFAULT 0,
  "ClosingReading"  numeric(12,3) NOT NULL DEFAULT 0,
  "Rate"            numeric(10,4) NOT NULL DEFAULT 0
);

-- ── 3. DsmSubmissionCollections ──────────────────────────────
CREATE TABLE IF NOT EXISTS public."DsmSubmissionCollections" (
  "Id"            bigserial PRIMARY KEY,
  "SubmissionId"  uuid NOT NULL REFERENCES public."DsmSubmissions"("Id") ON DELETE CASCADE,
  "Cash"          numeric(14,2) NOT NULL DEFAULT 0,
  "UPI"           numeric(14,2) NOT NULL DEFAULT 0,
  "Card"          numeric(14,2) NOT NULL DEFAULT 0,
  "Credit"        numeric(14,2) NOT NULL DEFAULT 0,
  "Expense"       numeric(14,2) NOT NULL DEFAULT 0,
  "ExpenseNotes"  text,
  "Short"         numeric(14,2) NOT NULL DEFAULT 0,
  "Excess"        numeric(14,2) NOT NULL DEFAULT 0
);

-- ── 4. DsmNotifications ──────────────────────────────────────
CREATE TABLE IF NOT EXISTS public."DsmNotifications" (
  "Id"        uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "StationId" text NOT NULL,
  "UserId"    text NOT NULL,  -- SyncGuid of DsmUser
  "Role"      text NOT NULL DEFAULT 'dsm',
  "Message"   text NOT NULL,
  "IsRead"    boolean NOT NULL DEFAULT false,
  "CreatedAt" timestamptz NOT NULL DEFAULT now()
);

-- ── 5. PumpNozzleConfig ──────────────────────────────────────
-- Manager configures nozzles per pump; DSM reads this from PWA
CREATE TABLE IF NOT EXISTS public."PumpNozzleConfig" (
  "Id"        bigserial PRIMARY KEY,
  "StationId" text NOT NULL,
  "PumpId"    integer NOT NULL,
  "NozzleId"  integer NOT NULL,
  "FuelType"  text NOT NULL DEFAULT 'MS-I',  -- 'MS-I', 'MS-II', 'HSD'
  "IsActive"  boolean NOT NULL DEFAULT true,
  "SortOrder" integer NOT NULL DEFAULT 0,
  UNIQUE ("StationId", "PumpId", "NozzleId")
);

-- ── Indexes ───────────────────────────────────────────────────
CREATE INDEX IF NOT EXISTS idx_dsmsub_station    ON public."DsmSubmissions"("StationId");
CREATE INDEX IF NOT EXISTS idx_dsmsub_status     ON public."DsmSubmissions"("Status");
CREATE INDEX IF NOT EXISTS idx_dsmsub_user       ON public."DsmSubmissions"("DsmUserId");
CREATE INDEX IF NOT EXISTS idx_dsmnotif_user     ON public."DsmNotifications"("UserId");
CREATE INDEX IF NOT EXISTS idx_pumpcfg_station   ON public."PumpNozzleConfig"("StationId");

-- ── Enable RLS ────────────────────────────────────────────────
ALTER TABLE public."DsmSubmissions"          ENABLE ROW LEVEL SECURITY;
ALTER TABLE public."DsmSubmissionReadings"   ENABLE ROW LEVEL SECURITY;
ALTER TABLE public."DsmSubmissionCollections" ENABLE ROW LEVEL SECURITY;
ALTER TABLE public."DsmNotifications"        ENABLE ROW LEVEL SECURITY;
ALTER TABLE public."PumpNozzleConfig"        ENABLE ROW LEVEL SECURITY;

-- ── RLS Policies ──────────────────────────────────────────────
-- Drop old policies first (in case you are re-running)
DROP POLICY IF EXISTS dsm_submissions_select  ON public."DsmSubmissions";
DROP POLICY IF EXISTS dsm_submissions_insert  ON public."DsmSubmissions";
DROP POLICY IF EXISTS dsm_readings_select     ON public."DsmSubmissionReadings";
DROP POLICY IF EXISTS dsm_readings_insert     ON public."DsmSubmissionReadings";
DROP POLICY IF EXISTS dsm_collections_select  ON public."DsmSubmissionCollections";
DROP POLICY IF EXISTS dsm_collections_insert  ON public."DsmSubmissionCollections";
DROP POLICY IF EXISTS dsm_notif_select        ON public."DsmNotifications";
DROP POLICY IF EXISTS dsm_notif_update        ON public."DsmNotifications";
DROP POLICY IF EXISTS pump_cfg_select         ON public."PumpNozzleConfig";

-- DsmSubmissions: Authenticated user can insert; can select own rows
-- The DsmUserId in DsmSubmissions stores the SyncGuid of the DsmUser.
-- To allow manager (service_role) full access and DSM to see/insert their own:
CREATE POLICY dsm_submissions_insert ON public."DsmSubmissions"
  FOR INSERT TO authenticated
  WITH CHECK (true);  -- Any logged-in user can insert

CREATE POLICY dsm_submissions_select ON public."DsmSubmissions"
  FOR SELECT TO authenticated
  USING (true);  -- Any logged-in user can read (manager + DSM)

CREATE POLICY dsm_readings_insert ON public."DsmSubmissionReadings"
  FOR INSERT TO authenticated
  WITH CHECK (true);

CREATE POLICY dsm_readings_select ON public."DsmSubmissionReadings"
  FOR SELECT TO authenticated
  USING (true);

CREATE POLICY dsm_collections_insert ON public."DsmSubmissionCollections"
  FOR INSERT TO authenticated
  WITH CHECK (true);

CREATE POLICY dsm_collections_select ON public."DsmSubmissionCollections"
  FOR SELECT TO authenticated
  USING (true);

CREATE POLICY dsm_notif_select ON public."DsmNotifications"
  FOR SELECT TO authenticated
  USING (true);

CREATE POLICY dsm_notif_update ON public."DsmNotifications"
  FOR UPDATE TO authenticated
  USING (true)
  WITH CHECK (true);

CREATE POLICY dsm_notif_insert ON public."DsmNotifications"
  FOR INSERT TO authenticated
  WITH CHECK (true);

CREATE POLICY pump_cfg_select ON public."PumpNozzleConfig"
  FOR SELECT TO authenticated
  USING (true);

CREATE POLICY pump_cfg_all ON public."PumpNozzleConfig"
  FOR ALL TO service_role
  USING (true)
  WITH CHECK (true);

-- ── Realtime ─────────────────────────────────────────────────
-- Enable realtime for notifications
ALTER PUBLICATION supabase_realtime ADD TABLE public."DsmNotifications";

-- ── Initial Nozzle Config seed data ──────────────────────────
-- Adjust StationId to match your actual Station ID from Settings
-- This mirrors the PUMP_NOZZLE_CONFIG in the DSM PWA.
-- Replace 'YOUR_STATION_ID' with the actual station_id from your FuelPro settings.
/*
INSERT INTO public."PumpNozzleConfig" ("StationId","PumpId","NozzleId","FuelType","SortOrder") VALUES
  ('YOUR_STATION_ID', 1,  1,  'MS-I',  1),
  ('YOUR_STATION_ID', 1,  2,  'MS-II', 2),
  ('YOUR_STATION_ID', 2,  3,  'MS-I',  1),
  ('YOUR_STATION_ID', 2,  4,  'MS-II', 2),
  ('YOUR_STATION_ID', 3,  5,  'MS-I',  1),
  ('YOUR_STATION_ID', 3,  6,  'MS-II', 2),
  ('YOUR_STATION_ID', 3,  7,  'HSD',   3),
  ('YOUR_STATION_ID', 4,  8,  'MS-I',  1),
  ('YOUR_STATION_ID', 4,  9,  'MS-II', 2),
  ('YOUR_STATION_ID', 4,  10, 'HSD',   3),
  ('YOUR_STATION_ID', 5,  11, 'MS-I',  1),
  ('YOUR_STATION_ID', 5,  12, 'MS-II', 2),
  ('YOUR_STATION_ID', 5,  13, 'HSD',   3),
  ('YOUR_STATION_ID', 6,  14, 'MS-I',  1),
  ('YOUR_STATION_ID', 6,  15, 'MS-II', 2),
  ('YOUR_STATION_ID', 6,  16, 'HSD',   3),
  ('YOUR_STATION_ID', 7,  17, 'HSD',   1),
  ('YOUR_STATION_ID', 7,  18, 'HSD',   2),
  ('YOUR_STATION_ID', 8,  19, 'HSD',   1),
  ('YOUR_STATION_ID', 8,  20, 'HSD',   2)
ON CONFLICT ("StationId","PumpId","NozzleId") DO NOTHING;
*/
