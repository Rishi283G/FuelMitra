-- Run this in your Supabase SQL Editor to fix the unique constraint issue on SyncChangeLogs
-- This is required to allow C# SyncEngine to push DELETE log entries to Supabase.

ALTER TABLE "SyncChangeLogs" ADD CONSTRAINT "SyncChangeLogs_SyncGuid_key" UNIQUE ("SyncGuid");
