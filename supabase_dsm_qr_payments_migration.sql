-- ==============================================================================
-- Supabase Database Migration Script — Cross-DSM QR Code Payments Option
-- Station: Kandhare Petroleum (PyroSync Automation)
-- ==============================================================================

-- 1. Ensure the Metadata JSON column exists on DsmSubmissions
-- (The PWA stores qrPayments: [{ targetDsmName, amount, tid, batch, slot }] inside Metadata JSON)
ALTER TABLE IF EXISTS "DsmSubmissions" ADD COLUMN IF NOT EXISTS "Metadata" JSONB NULL;

-- 2. Optional: Dedicated DsmQrPayments Table for Direct SQL Querying / Analytics
CREATE TABLE IF NOT EXISTS "DsmQrPayments" (
    "Id" BIGSERIAL PRIMARY KEY,
    "SubmissionId" UUID REFERENCES "DsmSubmissions"("Id") ON DELETE CASCADE,
    "StationId" TEXT NOT NULL,
    "DsmName" VARCHAR(200) NOT NULL,
    "TargetDsmName" VARCHAR(200) NOT NULL,
    "Amount" DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    "Tid" VARCHAR(100) NULL,
    "Batch" VARCHAR(100) NULL,
    "Slot" VARCHAR(50) NULL, -- 'Morning', 'Day', 'Night'
    "Date" DATE NOT NULL,
    "CreatedAt" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL
);

-- 3. Create Performance Indexes
CREATE INDEX IF NOT EXISTS "IX_DsmQrPayments_StationId_Date" ON "DsmQrPayments" ("StationId", "Date");
CREATE INDEX IF NOT EXISTS "IX_DsmQrPayments_TargetDsmName" ON "DsmQrPayments" ("TargetDsmName");
CREATE INDEX IF NOT EXISTS "IX_DsmQrPayments_SubmissionId" ON "DsmQrPayments" ("SubmissionId");

-- 4. Enable Row Level Security (RLS)
ALTER TABLE IF EXISTS "DsmQrPayments" ENABLE ROW LEVEL SECURITY;

-- 5. Policies for authenticated / anon DSM PWA & Desktop App
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_policies WHERE tablename = 'DsmQrPayments' AND policyname = 'Enable all access for authenticated and anon users'
    ) THEN
        CREATE POLICY "Enable all access for authenticated and anon users"
        ON "DsmQrPayments"
        FOR ALL
        USING (true)
        WITH CHECK (true);
    END IF;
END $$;

COMMENT ON TABLE "DsmQrPayments" IS 'Stores cross-DSM QR payment records where a customer pays on another DSM QR code';
