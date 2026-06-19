-- ═══════════════════════════════════════════════════════════════════════
-- Supabase Schema Update: PyroSync Pro Max Phase 1
-- ═══════════════════════════════════════════════════════════════════════

-- 1. DsmUsers (No plaintext password storage)
CREATE TABLE IF NOT EXISTS "DsmUsers" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "EmployeeCode" VARCHAR(100) NOT NULL,
    "FullName" VARCHAR(200) NOT NULL,
    "Email" VARCHAR(255) NOT NULL DEFAULT '',
    "MobileNumber" VARCHAR(20) NOT NULL,
    "AuthUserId" VARCHAR(255) NOT NULL, -- Returned from Supabase Auth Admin API
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "CreatedAt" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid")
);

-- 2. DsmPumpAssignments (Extended for Shift-Based Rotation)
CREATE TABLE IF NOT EXISTS "DsmPumpAssignments" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "DsmUserId" UUID NOT NULL REFERENCES "DsmUsers" ("SyncGuid") ON DELETE CASCADE,
    "PumpId" INTEGER NOT NULL,
    "ShiftType" VARCHAR(10) NOT NULL, -- 'A', 'B', 'C'
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "AssignedDate" TIMESTAMP NOT NULL DEFAULT NOW(),
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid")
);

-- 3. DsmDevices
CREATE TABLE IF NOT EXISTS "DsmDevices" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "DsmUserId" UUID NOT NULL REFERENCES "DsmUsers" ("SyncGuid") ON DELETE CASCADE,
    "DeviceId" VARCHAR(200) NOT NULL,
    "DeviceName" VARCHAR(200) NOT NULL,
    "LastLogin" TIMESTAMP NOT NULL DEFAULT NOW(),
    "LastSeen" TIMESTAMP NOT NULL DEFAULT NOW(),
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid")
);

-- 4. DsmSubmissions (Approval Queue - Supabase exclusive during Phase 1)
CREATE TABLE IF NOT EXISTS "DsmSubmissions" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "StationId" TEXT NOT NULL,
    "DsmUserId" UUID NOT NULL REFERENCES "DsmUsers" ("SyncGuid") ON DELETE CASCADE,
    "ShiftDate" DATE NOT NULL,
    "ShiftType" VARCHAR(10) NOT NULL,
    "PumpId" INTEGER NOT NULL,
    "Status" VARCHAR(50) NOT NULL DEFAULT 'Pending', -- 'Pending', 'Approved', 'Rejected', 'Expired'
    "RejectionReason" TEXT,
    "SubmittedAt" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "ApprovedAt" TIMESTAMP WITH TIME ZONE,
    "ApprovedBy" TEXT,
    "ApprovalLockId" VARCHAR(100), -- Prevents concurrent approvals
    "OriginalDataJson" TEXT, -- Stores original values submitted by DSM for audit
    "AttachmentUrl" TEXT, -- Nullable attachment path
    "Notes" TEXT,
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    CONSTRAINT uq_station_pump_shift UNIQUE ("StationId", "PumpId", "ShiftDate", "ShiftType")
);

-- 5. DsmSubmissionReadings (Supabase exclusive)
CREATE TABLE IF NOT EXISTS "DsmSubmissionReadings" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "SubmissionId" UUID NOT NULL REFERENCES "DsmSubmissions" ("Id") ON DELETE CASCADE,
    "PumpId" INTEGER NOT NULL,
    "NozzleId" INTEGER NOT NULL,
    "FuelType" TEXT,
    "OpeningReading" DOUBLE PRECISION NOT NULL,
    "ClosingReading" DOUBLE PRECISION NOT NULL,
    "Rate" DOUBLE PRECISION NOT NULL
);

-- 6. DsmSubmissionCollections (Supabase exclusive)
CREATE TABLE IF NOT EXISTS "DsmSubmissionCollections" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "SubmissionId" UUID NOT NULL REFERENCES "DsmSubmissions" ("Id") ON DELETE CASCADE,
    "Cash" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "UPI" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "Card" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "Credit" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "Expense" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "ExpenseNotes" TEXT,
    "Short" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "Excess" DOUBLE PRECISION NOT NULL DEFAULT 0.0
);

-- 7. DsmApprovalAudits
CREATE TABLE IF NOT EXISTS "DsmApprovalAudits" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "SubmissionId" UUID NOT NULL,
    "OriginalDataJson" TEXT NOT NULL,
    "ApprovedDataJson" TEXT NOT NULL,
    "ApprovedBy" TEXT NOT NULL,
    "ApprovedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "Remarks" TEXT,
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid")
);

-- 8. DsmNotifications (Supabase exclusive)
CREATE TABLE IF NOT EXISTS "DsmNotifications" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "StationId" TEXT NOT NULL,
    "UserId" UUID REFERENCES "DsmUsers" ("SyncGuid") ON DELETE CASCADE,
    "Role" VARCHAR(50) NOT NULL, -- 'Manager', 'DSM'
    "Message" TEXT NOT NULL,
    "IsRead" BOOLEAN NOT NULL DEFAULT FALSE,
    "CreatedAt" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL
);

-- 9. DsmAttendance (Groundwork for Phase 2)
CREATE TABLE IF NOT EXISTS "DsmAttendance" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "DsmUserId" UUID NOT NULL REFERENCES "DsmUsers" ("SyncGuid") ON DELETE CASCADE,
    "AttendanceDate" DATE NOT NULL,
    "ShiftType" VARCHAR(10) NOT NULL,
    "ClockInTime" TIMESTAMP WITH TIME ZONE NOT NULL,
    "ClockOutTime" TIMESTAMP WITH TIME ZONE,
    "Status" VARCHAR(50) NOT NULL DEFAULT 'Present', -- 'Present', 'Absent', 'HalfDay', 'Leave'
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid")
);

-- Triggers for automatic updated_at update
CREATE OR REPLACE PROCEDURE create_dsm_update_triggers() AS $$
DECLARE
    t TEXT;
BEGIN
    FOR t IN 
        SELECT table_name 
        FROM information_schema.tables 
        WHERE table_schema = 'public' 
          AND table_name IN (
            'DsmUsers', 'DsmPumpAssignments', 'DsmDevices', 'DsmSubmissions', 
            'DsmApprovalAudits', 'DsmAttendance'
          )
    LOOP
        EXECUTE format('DROP TRIGGER IF EXISTS tr_update_timestamp ON %I', t);
        EXECUTE format('CREATE TRIGGER tr_update_timestamp BEFORE UPDATE ON %I FOR EACH ROW EXECUTE FUNCTION update_updated_at_column()', t);
    END LOOP;
END;
$$ LANGUAGE plpgsql;

CALL create_dsm_update_triggers();

-- Enable Row Level Security (RLS) on all new tables
ALTER TABLE "DsmUsers" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "DsmPumpAssignments" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "DsmDevices" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "DsmSubmissions" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "DsmSubmissionReadings" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "DsmSubmissionCollections" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "DsmApprovalAudits" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "DsmNotifications" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "DsmAttendance" ENABLE ROW LEVEL SECURITY;

-- Permissive policies for Phase 1
DROP POLICY IF EXISTS all_by_station ON "DsmUsers";
CREATE POLICY all_by_station ON "DsmUsers" FOR ALL USING (true) WITH CHECK (true);

DROP POLICY IF EXISTS all_by_station ON "DsmPumpAssignments";
CREATE POLICY all_by_station ON "DsmPumpAssignments" FOR ALL USING (true) WITH CHECK (true);

DROP POLICY IF EXISTS all_by_station ON "DsmDevices";
CREATE POLICY all_by_station ON "DsmDevices" FOR ALL USING (true) WITH CHECK (true);

DROP POLICY IF EXISTS all_by_station ON "DsmSubmissions";
CREATE POLICY all_by_station ON "DsmSubmissions" FOR ALL USING (true) WITH CHECK (true);

DROP POLICY IF EXISTS all_by_station ON "DsmSubmissionReadings";
CREATE POLICY all_by_station ON "DsmSubmissionReadings" FOR ALL USING (true) WITH CHECK (true);

DROP POLICY IF EXISTS all_by_station ON "DsmSubmissionCollections";
CREATE POLICY all_by_station ON "DsmSubmissionCollections" FOR ALL USING (true) WITH CHECK (true);

DROP POLICY IF EXISTS all_by_station ON "DsmApprovalAudits";
CREATE POLICY all_by_station ON "DsmApprovalAudits" FOR ALL USING (true) WITH CHECK (true);

DROP POLICY IF EXISTS all_by_station ON "DsmNotifications";
CREATE POLICY all_by_station ON "DsmNotifications" FOR ALL USING (true) WITH CHECK (true);

DROP POLICY IF EXISTS all_by_station ON "DsmAttendance";
CREATE POLICY all_by_station ON "DsmAttendance" FOR ALL USING (true) WITH CHECK (true);

-- Performance and Sync Indexes
CREATE INDEX IF NOT EXISTS idx_dsm_users_sync ON "DsmUsers" ("station_id", "updated_at");
CREATE INDEX IF NOT EXISTS idx_dsm_assignments_sync ON "DsmPumpAssignments" ("station_id", "updated_at");
CREATE INDEX IF NOT EXISTS idx_dsm_devices_sync ON "DsmDevices" ("station_id", "updated_at");
CREATE INDEX IF NOT EXISTS idx_dsm_submissions_sync ON "DsmSubmissions" ("StationId", "updated_at");
CREATE INDEX IF NOT EXISTS idx_dsm_approval_audits_sync ON "DsmApprovalAudits" ("station_id", "updated_at");
CREATE INDEX IF NOT EXISTS idx_dsm_attendance_sync ON "DsmAttendance" ("station_id", "updated_at");

CREATE UNIQUE INDEX IF NOT EXISTS idx_uq_assignments_active ON "DsmPumpAssignments" ("station_id", "PumpId", "ShiftType") WHERE ("IsActive" = TRUE);
CREATE INDEX IF NOT EXISTS idx_dsm_submissions_pending ON "DsmSubmissions" ("StationId", "Status") WHERE ("Status" = 'Pending');
CREATE INDEX IF NOT EXISTS idx_dsm_readings_submission ON "DsmSubmissionReadings" ("SubmissionId");
CREATE INDEX IF NOT EXISTS idx_dsm_collections_submission ON "DsmSubmissionCollections" ("SubmissionId");
CREATE INDEX IF NOT EXISTS idx_dsm_devices_user ON "DsmDevices" ("DsmUserId");
CREATE INDEX IF NOT EXISTS idx_dsm_assignments_user ON "DsmPumpAssignments" ("DsmUserId");
CREATE INDEX IF NOT EXISTS idx_dsm_attendance_user ON "DsmAttendance" ("DsmUserId");
