import { useState, useCallback } from 'react';
import { supabase } from '../lib/supabase';
import { db, type DraftSubmission } from '../lib/db';
import { v4 as uuidv4 } from 'uuid';

export function useSubmissionService() {
  const [syncing, setSyncing] = useState(false);

  const saveDraft = useCallback(async (
    draft: Omit<DraftSubmission, 'id' | 'draftId' | 'createdAt' | 'status'> & { status?: DraftSubmission['status'] }
  ) => {
    const record: DraftSubmission = {
      ...draft,
      draftId: (draft as any).draftId || uuidv4(),
      createdAt: (draft as any).createdAt || new Date().toISOString(),
      status: draft.status || 'draft',
    };
    const id = await db.drafts.add(record);
    record.id = id;
    return record;
  }, []);

  const submitToSupabase = useCallback(async (
    draft: DraftSubmission,
    dsmUserId: string,
    stationId: string
  ): Promise<string | null> => {
    setSyncing(true);
    try {
      // 1. Check if there is already an existing Pending submission for this shift & pump & user (deduplication)
      let existingId: string | null = null;
      try {
        const { data: existingSubmissions } = await supabase
          .from('DsmSubmissions')
          .select('Id')
          .eq('StationId', stationId)
          .eq('DsmUserId', dsmUserId)
          .eq('PumpId', draft.pumpId)
          .eq('ShiftDate', draft.shiftDate)
          .eq('ShiftType', draft.shiftType)
          .eq('Status', 'Pending')
          .limit(1);

        if (existingSubmissions && existingSubmissions.length > 0) {
          existingId = existingSubmissions[0].Id;
        }
      } catch (checkErr) {
        console.warn('Could not check existing pending submissions:', checkErr);
      }

      const submissionId = existingId || uuidv4();
      const isUpdate = existingId !== null;

      const metadataPayload = {
        cardSwipeDetails: draft.cardSwipeDetails || [],
        settlements: draft.settlements || [],
        debtorEntries: draft.debtorEntries || [],
        personalDebtors: draft.personalDebtors || [],
        khandhareEntries: draft.khandhareEntries || [],
        qrPayments: draft.qrPayments || [],
        expenseEntries: draft.expenseEntries || [],
        cashDenominations: (draft as any).cashDenominations || null,
        cash1Denominations: (draft as any).cash1Denominations || null,
        testingEntries: (draft as any).testingEntries || [],
        connectedPumpId: (draft as any).connectedPumpId || null,
        connectedPumpIds: (draft as any).connectedPumpIds || ((draft as any).connectedPumpId ? [(draft as any).connectedPumpId] : []),
        oilDefSales: draft.oilDefSales || []
      };

      if (isUpdate) {
        const { error: updateSubError } = await supabase
          .from('DsmSubmissions')
          .update({
            Notes: draft.notes || null,
            AttachmentUrl: draft.attachmentUrl || null,
            SubmittedAt: new Date().toISOString(),
            Metadata: metadataPayload
          })
          .eq('Id', existingId);

        if (updateSubError) {
          console.error('DsmSubmissions update error:', updateSubError);
          return `Failed to update submission: ${updateSubError.message} (${updateSubError.code})`;
        }

        // Clean up previous readings/collections for this submission
        await supabase.from('DsmSubmissionReadings').delete().eq('SubmissionId', existingId);
        await supabase.from('DsmSubmissionCollections').delete().eq('SubmissionId', existingId);
      } else {
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
          Metadata: metadataPayload
        });

        if (subError) {
          console.error('DsmSubmissions insert error:', subError);
          return `Failed to create submission: ${subError.message} (${subError.code})`;
        }
      }

      // 2. Insert nozzle readings
      if (draft.nozzleReadings.length > 0) {
        const readingsPayload = draft.nozzleReadings.map(r => ({
          SubmissionId: submissionId,
          PumpId: r.pumpId || draft.pumpId,
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
          if (!isUpdate) {
            // Rollback newly created submission
            await supabase.from('DsmSubmissions').delete().eq('Id', submissionId);
          }
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
        Short: 0,
        Excess: 0,
        PetroCard: draft.petroCard || 0,
        CashDeposit: draft.cashDeposit || 0,
        Others: draft.others || 0,
      });

      if (collError) {
        console.error('DsmSubmissionCollections insert error:', collError);
        if (!isUpdate) {
          await supabase.from('DsmSubmissions').delete().eq('Id', submissionId);
        }
        return `Failed to save collections: ${collError.message} (${collError.code})`;
      }

      // 4. Mark draft as submitted in IndexedDB
      if (draft.id != null) {
        await db.drafts.update(draft.id, { status: 'submitted' });
      }
      if (draft.draftId) {
        await db.drafts.where('draftId').equals(draft.draftId).modify({ status: 'submitted' });
      }

      // 5. Cache the submission in IndexedDB for history view (upsert)
      try {
        const existingCached = await db.submissions.where('remoteId').equals(submissionId).first();
        if (existingCached?.id) {
          await db.submissions.update(existingCached.id, {
            pumpId: draft.pumpId,
            shiftDate: draft.shiftDate,
            shiftType: draft.shiftType,
            status: 'Pending',
            submittedAt: new Date().toISOString(),
            cachedAt: new Date().toISOString(),
          });
        } else {
          await db.submissions.add({
            remoteId: submissionId,
            pumpId: draft.pumpId,
            shiftDate: draft.shiftDate,
            shiftType: draft.shiftType,
            status: 'Pending',
            submittedAt: new Date().toISOString(),
            cachedAt: new Date().toISOString(),
          });
        }
      } catch (cacheErr) {
        console.warn('Could not cache submission in IndexedDB:', cacheErr);
      }

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
    if (queued.length === 0) return;

    // Temporarily mark as in-flight draft to prevent parallel triggers
    for (const draft of queued) {
      if (draft.id) {
        await db.drafts.update(draft.id, { status: 'draft' });
      }
    }

    for (const draft of queued) {
      const err = await submitToSupabase(draft, dsmUserId, stationId);
      if (err) {
        if (draft.id) {
          await db.drafts.update(draft.id, { status: 'failed', errorMessage: err });
        }
      }
    }
  }, [submitToSupabase]);

  const fetchSubmissionHistory = useCallback(async (dsmUserId: string, authUserId?: string) => {
    let query = supabase
      .from('DsmSubmissions')
      .select('Id, PumpId, ShiftDate, ShiftType, Status, SubmittedAt, RejectionReason, ApprovedAt, Metadata');

    if (authUserId && authUserId !== dsmUserId) {
      query = query.or(`DsmUserId.eq.${dsmUserId},DsmUserId.eq.${authUserId}`);
    } else {
      query = query.eq('DsmUserId', dsmUserId);
    }

    const { data, error } = await query
      .order('SubmittedAt', { ascending: false })
      .limit(100);

    if (error) throw error;
    return data ?? [];
  }, []);

  const fetchSubmissionDetails = useCallback(async (submissionId: string) => {
    const { data: sub, error: subErr } = await supabase
      .from('DsmSubmissions')
      .select('Id, PumpId, ShiftDate, ShiftType, Status, SubmittedAt, Notes, Metadata')
      .eq('Id', submissionId)
      .single();

    if (subErr || !sub) throw subErr || new Error('Submission not found');

    const { data: coll } = await supabase
      .from('DsmSubmissionCollections')
      .select('*')
      .eq('SubmissionId', submissionId)
      .maybeSingle();

    const { data: readings } = await supabase
      .from('DsmSubmissionReadings')
      .select('*')
      .eq('SubmissionId', submissionId);

    return {
      submission: sub,
      collection: coll || null,
      readings: readings || []
    };
  }, []);
  
  const syncProductListAndStock = useCallback(async () => {
    if (!navigator.onLine) return;
    try {
      // 1. Fetch active products
      const { data: products, error: pErr } = await supabase
        .from('ProductMasters')
        .select('Id, ProductName, Category, Unit, DefaultSaleRate')
        .eq('IsActive', true);
      
      if (pErr) throw pErr;

      // 2. Fetch latest stock balances (remaining stock from OilDefDailyLogs)
      const { data: logs, error: lErr } = await supabase
        .from('OilDefDailyLogs')
        .select('ProductId, RemainingStock, LogDate')
        .order('LogDate', { ascending: false });

      if (lErr) throw lErr;

      const latestStocks: Record<number, number> = {};
      if (logs) {
        for (const log of logs) {
          const prodId = log.ProductId;
          if (latestStocks[prodId] === undefined) {
            latestStocks[prodId] = log.RemainingStock || 0;
          }
        }
      }

      await db.transaction('rw', db.products, db.stockBalances, async () => {
        await db.products.clear();
        await db.stockBalances.clear();

        if (products) {
          for (const p of products) {
            await db.products.put({
              id: p.Id,
              productName: p.ProductName,
              category: p.Category,
              unit: p.Unit,
              defaultSaleRate: p.DefaultSaleRate
            });

            await db.stockBalances.put({
              productId: p.Id,
              remainingStock: latestStocks[p.Id] || 0
            });
          }
        }
      });
    } catch (err) {
      console.error('Failed to sync product list and stock:', err);
    }
  }, []);

  return { syncing, saveDraft, submitToSupabase, retryQueuedDrafts, fetchSubmissionHistory, fetchSubmissionDetails, syncProductListAndStock };
}
