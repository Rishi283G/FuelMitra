-- Migration: Remove unique constraint uq_station_pump_shift from DsmSubmissions
-- Run this in Supabase Dashboard -> SQL Editor to allow multiple shift entries per pump/shift by the same or different DSMs.

ALTER TABLE "DsmSubmissions" DROP CONSTRAINT IF EXISTS "uq_station_pump_shift";
