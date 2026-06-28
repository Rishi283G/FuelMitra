import { useState, useCallback } from 'react';
import { supabase } from '../lib/supabase';
import { db, type DraftSubmission } from '../lib/db';
import { v4 as uuidv4 } from 'uuid';

export function useSubmissionService() {
  const [syncing, setSyncing] = useState(false);

  const saveDraft = useCallback(async (
    draft: Omit<DraftSubmission, 'id' | 'draftId' | 'createdAt' | 'status'>
  ) => {
    const record: DraftSubmission = {
      ...draft,
      draftId: uuidv4(),
      createdAt: new Date().toISOString(),
      status: 'draft',
    };
    await db.drafts.add(record);
    return record;
  }, []);

  const submitToSupabase = useCallback(async (
    draft: DraftSubmission,
    dsmUserId: string,
    stationId: string
  ): Promise<string | null> => {
    setSyncing(true);
    try {
      // 1. Create the submission record
      const submissionId = uuidv4();

      const { error: subError } = await supabase.from('DsmSubmissions').insert({
        Id: submissionId,
        DsmUserId: dsmUserId,
        StationId: stationId,
        PumpId: draft.pumpId,
        ShiftDate: draft.shiftDate,
        ShiftType: draft.shiftType,
        Status: 'Pending',
        Notes: draft.notes || null,
        AttachmentUrl: draft.attachmentUrl || null,
        SubmittedAt: new Date().toISOString(),
        Metadata: {
          cardSwipeDetails: draft.cardSwipeDetails || [],
          debtorEntries: draft.debtorEntries || [],
          personalDebtors: draft.personalDebtors || []
        }
      });

      if (subError) {
        console.error('DsmSubmissions insert error:', subError);
        return `Failed to create submission: ${subError.message} (${subError.code})`;
      }

      // 2. Insert nozzle readings
      if (draft.nozzleReadings.length > 0) {
        const readingsPayload = draft.nozzleReadings.map(r => ({
          SubmissionId: submissionId,
          PumpId: draft.pumpId,
          NozzleId: r.nozzleId,
          FuelType: r.fuelType,
          OpeningReading: r.openingReading,
          ClosingReading: r.closingReading,
          Rate: r.rate,
        }));

        let { error: readingsError } = await supabase
          .from('DsmSubmissionReadings')
          .insert(readingsPayload);

        // Phase-1 Supabase schema may lack FuelType; retry without it
        if (readingsError?.message?.includes("'FuelType'")) {
          const legacyPayload = readingsPayload.map(({ FuelType: _ft, ...rest }) => rest);
          ({ error: readingsError } = await supabase
            .from('DsmSubmissionReadings')
            .insert(legacyPayload));
        }

        if (readingsError) {
          console.error('DsmSubmissionReadings insert error:', readingsError);
          // Rollback submission
          await supabase.from('DsmSubmissions').delete().eq('Id', submissionId);
          return `Failed to save nozzle readings: ${readingsError.message} (${readingsError.code})`;
        }
      }

      // 3. Insert collection
      const { error: collError } = await supabase.from('DsmSubmissionCollections').insert({
        SubmissionId: submissionId,
        Cash: draft.cash,
        UPI: draft.upi,
        Card: draft.card,
        Credit: draft.credit,
        Expense: draft.expense,
        ExpenseNotes: draft.expenseNotes || null,
        Short: draft.short,
        Excess: draft.excess,
      });

      if (collError) {
        console.error('DsmSubmissionCollections insert error:', collError);
        await supabase.from('DsmSubmissions').delete().eq('Id', submissionId);
        return `Failed to save collections: ${collError.message} (${collError.code})`;
      }

      // 4. Mark draft as submitted in IndexedDB
      if (draft.id != null) {
        await db.drafts.update(draft.id, { status: 'submitted' });
      }

      // 5. Cache the submission in IndexedDB for history view
      await db.submissions.add({
        remoteId: submissionId,
        pumpId: draft.pumpId,
        shiftDate: draft.shiftDate,
        shiftType: draft.shiftType,
        status: 'Pending',
        submittedAt: new Date().toISOString(),
        cachedAt: new Date().toISOString(),
      });

      return null; // success
    } catch (err: unknown) {
      console.error('submitToSupabase exception:', err);
      if (err instanceof Error) return err.message;
      return 'Unknown error during submission';
    } finally {
      setSyncing(false);
    }
  }, []);

  const retryQueuedDrafts = useCallback(async (dsmUserId: string, stationId: string) => {
    const queued = await db.drafts.where('status').equals('queued').toArray();
    for (const draft of queued) {
      const err = await submitToSupabase(draft, dsmUserId, stationId);
      if (err) {
        await db.drafts.update(draft.id!, { status: 'failed', errorMessage: err });
      }
    }
  }, [submitToSupabase]);

  const fetchSubmissionHistory = useCallback(async (dsmUserId: string) => {
    const { data, error } = await supabase
      .from('DsmSubmissions')
      .select('Id, PumpId, ShiftDate, ShiftType, Status, SubmittedAt, RejectionReason, ApprovedAt')
      .eq('DsmUserId', dsmUserId)
      .order('SubmittedAt', { ascending: false })
      .limit(30);

    if (error) throw error;
    return data ?? [];
  }, []);

  return { syncing, saveDraft, submitToSupabase, retryQueuedDrafts, fetchSubmissionHistory };
}
