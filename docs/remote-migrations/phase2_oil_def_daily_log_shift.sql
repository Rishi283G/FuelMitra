-- MANUAL PRODUCTION MIGRATION — DO NOT RUN FROM THE APPLICATION OR DURING LOCAL TESTS.
-- Project: fuelpro-production
-- Apply only after separate approval and before clients with the ShiftId sync mapping are enabled.
-- Existing historical OilDefDailyLogs rows are intentionally left NULL; do not backfill them.

ALTER TABLE public."OilDefDailyLogs"
  ADD COLUMN "ShiftId" UUID NULL;

ALTER TABLE public."OilDefDailyLogs"
  ADD CONSTRAINT fk_oil_def_daily_logs_shift
  FOREIGN KEY ("ShiftId")
  REFERENCES public."Shifts" ("SyncGuid")
  ON DELETE SET NULL;

CREATE INDEX idx_oil_def_daily_logs_shift
  ON public."OilDefDailyLogs" ("ShiftId");
