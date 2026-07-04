import { useState, useEffect } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { useSubmissionService } from '../hooks/useSubmissionService';
import { db, type DraftNozzleReading } from '../lib/db';
import { supabase } from '../lib/supabase';
import {
  ArrowLeft, Send,
  AlertTriangle, CheckCircle2, Loader2, WifiOff, RefreshCw
} from 'lucide-react';

interface SubmitProps {
  onBack: () => void;
}

interface NozzleRow extends DraftNozzleReading {
  rowId: number;
  isOpeningReadOnly?: boolean;
}

function todayISO() {
  return new Date().toISOString().split('T')[0];
}

export default function SubmitShiftScreen({ onBack }: SubmitProps) {
  const { profile } = useAuth();
  const { syncing, saveDraft, submitToSupabase } = useSubmissionService();

  // Form State — pump & shift come from manager assignment, DSM cannot change them
  const pumpId = profile?.AssignedPump ?? 0;
  const shiftType = profile?.AssignedShift ?? 'A';
  const [shiftDate, setShiftDate] = useState(() => {
    if (profile?.AssignedDate) {
      try {
        const d = new Date(profile.AssignedDate);
        const year = d.getFullYear();
        const month = String(d.getMonth() + 1).padStart(2, '0');
        const day = String(d.getDate()).padStart(2, '0');
        return `${year}-${month}-${day}`;
      } catch (e) {
        console.error(e);
      }
    }
    const d = new Date();
    const year = d.getFullYear();
    const month = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  });

  useEffect(() => {
    if (profile?.AssignedDate) {
      try {
        const d = new Date(profile.AssignedDate);
        const year = d.getFullYear();
        const month = String(d.getMonth() + 1).padStart(2, '0');
        const day = String(d.getDate()).padStart(2, '0');
        setShiftDate(`${year}-${month}-${day}`);
      } catch (e) {
        console.error(e);
      }
    }
  }, [profile]);

  const [notes, setNotes] = useState('');
  const [nozzleRows, setNozzleRows] = useState<NozzleRow[]>([]);
  
  // Cash Denomination states
  const [denom500, setDenom500] = useState<number>(0);
  const [denom200, setDenom200] = useState<number>(0);
  const [denom100, setDenom100] = useState<number>(0);
  const [denom50, setDenom50] = useState<number>(0);
  const [denom20, setDenom20] = useState<number>(0);
  const [denom10, setDenom10] = useState<number>(0);
  const [coins, setCoins] = useState<number>(0);
  const cash = (denom500 * 500) + (denom200 * 200) + (denom100 * 100) + (denom50 * 50) + (denom20 * 20) + (denom10 * 10) + coins;

  // Creditors & Vehicles dropdown states
  const [creditorList, setCreditorList] = useState<{ id: string; name: string }[]>([]);
  const [vehicleList, setVehicleList] = useState<{ creditorId: string; vehicleNumber: string }[]>([]);
  const [selectedCreditorId, setSelectedCreditorId] = useState<string>('');

  // Debtor entry adding state
  const [newDebtorName, setNewDebtorName] = useState('');
  const [newDebtorAmount, setNewDebtorAmount] = useState('');
  const [newDebtorVehicle, setNewDebtorVehicle] = useState('');
  const [newDebtorSlip, setNewDebtorSlip] = useState('');
  const [customVehicle, setCustomVehicle] = useState(false);

  const [cashDeposit, setCashDeposit] = useState(0);
  const [others, setOthers] = useState(0);
  const [expense, setExpense] = useState(0);
  const [expenseNotes, setExpenseNotes] = useState('');

  // Slot-based collections fields
  const [phonePeMorning, setPhonePeMorning] = useState<number>(0);
  const [phonePeTidMorning, setPhonePeTidMorning] = useState<string>('');
  const [phonePeBatchMorning, setPhonePeBatchMorning] = useState<string>('');
  const [phonePeNight, setPhonePeNight] = useState<number>(0);
  const [phonePeTidNight, setPhonePeTidNight] = useState<string>('');
  const [phonePeBatchNight, setPhonePeBatchNight] = useState<string>('');

  const [creditCardMorning, setCreditCardMorning] = useState<number>(0);
  const [creditCardTidMorning, setCreditCardTidMorning] = useState<string>('');
  const [creditCardBatchMorning, setCreditCardBatchMorning] = useState<string>('');
  const [creditCardNight, setCreditCardNight] = useState<number>(0);
  const [creditCardTidNight, setCreditCardTidNight] = useState<string>('');
  const [creditCardBatchNight, setCreditCardBatchNight] = useState<string>('');

  const [petroCardMorning, setPetroCardMorning] = useState<number>(0);
  const [petroCardTidMorning, setPetroCardTidMorning] = useState<string>('');
  const [petroCardBatchMorning, setPetroCardBatchMorning] = useState<string>('');
  const [petroCardNight, setPetroCardNight] = useState<number>(0);
  const [petroCardTidNight, setPetroCardTidNight] = useState<string>('');
  const [petroCardBatchNight, setPetroCardBatchNight] = useState<string>('');

  const [debtorEntries, setDebtorEntries] = useState<{ debtorName: string; amount: number; vehicleNumber?: string; slipNumber?: string; time: string; }[]>([]);

  const [cardSwipeDetails, setCardSwipeDetails] = useState<{ mode: string; amount: number; tid: string; batch: string; }[]>([]);

  // Loading state for nozzle config
  const [nozzleLoading, setNozzleLoading] = useState(true);
  const [nozzleError, setNozzleError] = useState('');

  // UI State
  const [step, setStep] = useState<'readings' | 'collections' | 'review'>('readings');
  const [error, setError] = useState('');
  const [success, setSuccess] = useState(false);
  const [online, setOnline] = useState(navigator.onLine);
  const [validationErrors, setValidationErrors] = useState<string[]>([]);

  useEffect(() => {
    const on = () => setOnline(true);
    const off = () => setOnline(false);
    window.addEventListener('online', on);
    window.addEventListener('offline', off);
    return () => { window.removeEventListener('online', on); window.removeEventListener('offline', off); };
  }, []);

  // Load Creditors & Vehicles
  useEffect(() => {
    async function loadCreditors() {
      if (!profile) return;
      
      const cachedCreds = localStorage.getItem('cached_creditors');
      const cachedVehs = localStorage.getItem('cached_vehicles');
      if (cachedCreds) {
        setCreditorList(JSON.parse(cachedCreds));
      }
      if (cachedVehs) {
        setVehicleList(JSON.parse(cachedVehs));
      }

      if (!navigator.onLine) return;

      try {
        const { data: creds, error: credsErr } = await supabase
          .from('Creditors')
          .select('SyncGuid, Name')
          .eq('station_id', profile.StationId)
          .eq('IsActive', true)
          .order('Name', { ascending: true });
        
        if (!credsErr && creds) {
          const formattedCreds = creds.map(c => ({ id: c.SyncGuid, name: c.Name }));
          setCreditorList(formattedCreds);
          localStorage.setItem('cached_creditors', JSON.stringify(formattedCreds));
        }

        const { data: vehs, error: vehsErr } = await supabase
          .from('DebtorVehicles')
          .select('CreditorId, VehicleNumber')
          .eq('station_id', profile.StationId)
          .eq('IsActive', true);

        if (!vehsErr && vehs) {
          const formattedVehs = vehs.map(v => ({ creditorId: v.CreditorId, vehicleNumber: v.VehicleNumber }));
          setVehicleList(formattedVehs);
          localStorage.setItem('cached_vehicles', JSON.stringify(formattedVehs));
        }
      } catch (e) {
        console.error('Failed to load creditors/vehicles from Supabase:', e);
      }
    }
    loadCreditors();
  }, [profile]);

  // ── Load nozzle config from Supabase (set by manager) ───────
  async function loadNozzleConfig() {
    if (!profile || !pumpId) return;

    setNozzleLoading(true);
    setNozzleError('');

    try {
      // Fetch rates from Settings
      let hsdRate = 90.35;
      let msIRate = 103.81;
      let msIIRate = 103.81;

      const { data: settingsData } = await supabase
        .from('Settings')
        .select('HsdRate, MsIRate, MsIIRate')
        .eq('station_id', profile.StationId)
        .order('LastUpdated', { ascending: false })
        .limit(1);

      if (settingsData && settingsData.length > 0) {
        hsdRate = settingsData[0].HsdRate ?? hsdRate;
        msIRate = settingsData[0].MsIRate ?? msIRate;
        msIIRate = settingsData[0].MsIIRate ?? msIIRate;
      }

      // We load nozzles of both the primary and connected pump
      const pumpsToFetch = [pumpId];
      if (profile.ConnectedPump) {
        pumpsToFetch.push(profile.ConnectedPump);
      }

      // Try to fetch nozzle config from Supabase PumpNozzleConfig table
      const { data: nozzleConfig, error: nozzleErr } = await supabase
        .from('PumpNozzleConfig')
        .select('NozzleId, FuelType, SortOrder, PumpId')
        .eq('StationId', profile.StationId)
        .in('PumpId', pumpsToFetch)
        .eq('IsActive', true)
        .order('SortOrder', { ascending: true });

      let configRows: { nozzleId: number; fuelType: string; pumpId: number }[] = [];

      if (!nozzleErr && nozzleConfig && nozzleConfig.length > 0) {
        // Use Supabase config
        configRows = nozzleConfig.map((r: any) => ({
          nozzleId: r.NozzleId,
          fuelType: r.FuelType,
          pumpId: r.PumpId
        }));
      } else {
        const FALLBACK_CONFIG: Record<number, { nozzleId: number; fuelType: string; pumpId: number }[]> = {
          1: [{ nozzleId: 1, fuelType: 'MS-II', pumpId: 1 }, { nozzleId: 3, fuelType: 'HSD', pumpId: 1 }],
          2: [{ nozzleId: 2, fuelType: 'MS-II', pumpId: 2 }, { nozzleId: 4, fuelType: 'HSD', pumpId: 2 }],
          3: [{ nozzleId: 5, fuelType: 'MS-I', pumpId: 3 }, { nozzleId: 7, fuelType: 'HSD', pumpId: 3 }],
          4: [{ nozzleId: 6, fuelType: 'MS-I', pumpId: 4 }, { nozzleId: 8, fuelType: 'HSD', pumpId: 4 }],
          5: [{ nozzleId: 9, fuelType: 'MS-II', pumpId: 5 }, { nozzleId: 11, fuelType: 'HSD', pumpId: 5 }],
          6: [{ nozzleId: 10, fuelType: 'MS-II', pumpId: 6 }, { nozzleId: 12, fuelType: 'HSD', pumpId: 6 }],
        };
        
        configRows = FALLBACK_CONFIG[pumpId] || [];
        if (profile.ConnectedPump && FALLBACK_CONFIG[profile.ConnectedPump]) {
          configRows = [...configRows, ...FALLBACK_CONFIG[profile.ConnectedPump]];
        }
      }

      if (configRows.length === 0) {
        setNozzleError(`No nozzle configuration found for Pump ${pumpId}. Please contact your manager.`);
        setNozzleRows([]);
        return;
      }

      // Fetch previous closing readings for both pumps
      const prevClosings: Record<number, number> = {};
      let prevSourceDate = '';
      let prevSourceType = 'A';

      function compareShifts(dateA: string, typeA: string, dateB: string, typeB: string): number {
        const dateCompare = dateA.localeCompare(dateB);
        if (dateCompare !== 0) return dateCompare;
        const rank: Record<string, number> = { A: 1, B: 2, C: 3 };
        const rA = rank[typeA] || 0;
        const rB = rank[typeB] || 0;
        return rA - rB;
      }

      // 1. Fetch from approved DsmEntries + NozzleReadings in Supabase
      try {
        const { data: latestEntries } = await supabase
          .from('DsmEntries')
          .select('DsmEntryId, ShiftId, PumpId')
          .in('PumpId', pumpsToFetch)
          .order('DsmEntryId', { ascending: false });

        if (latestEntries && latestEntries.length > 0) {
          for (const pId of pumpsToFetch) {
            const entry = latestEntries.find(e => e.PumpId === pId);
            if (entry) {
              const { data: shiftData } = await supabase
                .from('Shifts')
                .select('ShiftDate, ShiftType')
                .eq('ShiftId', entry.ShiftId)
                .limit(1);
                
              if (shiftData && shiftData.length > 0) {
                const shiftDateStr = shiftData[0].ShiftDate ? shiftData[0].ShiftDate.split('T')[0] : '';
                const shiftTypeStr = shiftData[0].ShiftType || 'A';
                
                const { data: approvedReadings } = await supabase
                  .from('NozzleReadings')
                  .select('NozzleNumber, ClosingReading')
                  .eq('DsmEntryId', entry.DsmEntryId);
                
                if (approvedReadings && approvedReadings.length > 0) {
                  approvedReadings.forEach((r: any) => {
                    prevClosings[r.NozzleNumber] = Number(r.ClosingReading);
                  });
                  prevSourceDate = shiftDateStr;
                  prevSourceType = shiftTypeStr;
                }
              }
            }
          }
        }
      } catch (e) {
        console.error('Failed to fetch from approved NozzleReadings:', e);
      }

      // 2. Fetch from DsmSubmissions + DsmSubmissionReadings in Supabase
      try {
        const { data: lastSubmissions } = await supabase
          .from('DsmSubmissions')
          .select('Id, ShiftDate, ShiftType, PumpId')
          .in('PumpId', pumpsToFetch)
          .order('ShiftDate', { ascending: false })
          .order('SubmittedAt', { ascending: false });

        if (lastSubmissions && lastSubmissions.length > 0) {
          for (const pId of pumpsToFetch) {
            const sub = lastSubmissions.find(s => s.PumpId === pId);
            if (sub) {
              const subDate = sub.ShiftDate ? sub.ShiftDate.split('T')[0] : '';
              const subType = sub.ShiftType || 'A';

              if (!prevSourceDate || compareShifts(subDate, subType, prevSourceDate, prevSourceType) >= 0) {
                const { data: lastReadings } = await supabase
                  .from('DsmSubmissionReadings')
                  .select('NozzleId, ClosingReading')
                  .eq('SubmissionId', sub.Id);
                
                if (lastReadings && lastReadings.length > 0) {
                  lastReadings.forEach((r: any) => {
                    if (Number(r.ClosingReading) > 0) {
                      prevClosings[r.NozzleId] = Number(r.ClosingReading);
                    }
                  });
                  prevSourceDate = subDate;
                  prevSourceType = subType;
                }
              }
            }
          }
        }
      } catch (e) {
        console.error('Failed to fetch from pending submissions:', e);
      }

      // 3. Fallback/override with local drafts if they are more recent!
      try {
        const localDrafts = await db.drafts
          .where('pumpId')
          .anyOf(pumpsToFetch)
          .toArray();

        if (localDrafts && localDrafts.length > 0) {
          const sortedDrafts = localDrafts.sort((a, b) => 
            b.shiftDate.localeCompare(a.shiftDate) || b.createdAt.localeCompare(a.createdAt)
          );
          const mostRecentDraft = sortedDrafts[0];
          if (mostRecentDraft && mostRecentDraft.nozzleReadings) {
            const draftDate = mostRecentDraft.shiftDate;
            const draftType = mostRecentDraft.shiftType;

            if (!prevSourceDate || compareShifts(draftDate, draftType, prevSourceDate, prevSourceType) >= 0) {
              mostRecentDraft.nozzleReadings.forEach(nr => {
                if (nr.closingReading > 0) {
                  prevClosings[nr.nozzleId] = Number(nr.closingReading);
                }
              });
            }
          }
        }
      } catch (e) {
        console.error('Failed to fetch from IndexedDB drafts:', e);
      }

      const rows: NozzleRow[] = configRows.map((n, index) => {
        let rate = msIRate;
        if (n.fuelType === 'HSD') rate = hsdRate;
        else if (n.fuelType === 'MS-II') rate = msIIRate;

        const prevClosing = prevClosings[n.nozzleId] || 0;

        return {
          rowId: index + 1,
          nozzleId: n.nozzleId,
          fuelType: n.fuelType,
          openingReading: prevClosing,
          closingReading: 0,
          rate,
          isOpeningReadOnly: prevClosing > 0,
          pumpId: n.pumpId,
          testing: 0
        };
      });

      setNozzleRows(rows);
    } catch (err) {
      console.error('Failed to load nozzle config:', err);
      setNozzleError('Failed to load nozzle configuration. Please check your connection and try again.');
    } finally {
      setNozzleLoading(false);
    }
  }

  useEffect(() => {
    loadNozzleConfig();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pumpId, profile]);

  function updateNozzle(rowId: number, field: keyof DraftNozzleReading, value: string | number) {
    setNozzleRows(prev =>
      prev.map(r => r.rowId === rowId ? { ...r, [field]: typeof value === 'string' ? value : Number(value) } : r)
    );
  }

  // ── Computed totals ──────────────────────────────────────────
  const grossSales = nozzleRows.reduce((sum, r) => sum + Math.max(0, r.closingReading - r.openingReading) * r.rate, 0);
  const upiTotal = phonePeMorning + phonePeNight;
  const cardTotal = creditCardMorning + creditCardNight;
  const petroCardTotal = petroCardMorning + petroCardNight;
  const creditTotal = debtorEntries.reduce((sum, d) => sum + d.amount, 0);
  const totalTesting = nozzleRows.reduce((sum, r) => sum + (r.testing || 0) * r.rate, 0);
  const totalCollections = cash + upiTotal + cardTotal + petroCardTotal + cashDeposit + others + creditTotal + totalTesting;
  const mismatch = totalCollections + expense - grossSales;

  // ── Validation ───────────────────────────────────────────────
  function validateReadings(): string[] {
    const errs: string[] = [];
    if (nozzleRows.length === 0) {
      errs.push('No nozzle readings loaded. Please reload the page or contact your manager.');
      return errs;
    }
    nozzleRows.forEach((r) => {
      const salesLtrs = r.closingReading - r.openingReading;
      if (r.closingReading < r.openingReading)
        errs.push(`Nozzle ${r.nozzleId} (${r.fuelType}): Closing (${r.closingReading}) < Opening (${r.openingReading})`);
      if (r.testing && r.testing > salesLtrs)
        errs.push(`Nozzle ${r.nozzleId} (${r.fuelType}): Testing (${r.testing} Ltr) cannot exceed dispensed fuel (${salesLtrs.toFixed(2)} Ltr)`);
      if (r.openingReading < 0 || r.closingReading < 0 || (r.testing || 0) < 0)
        errs.push(`Nozzle ${r.nozzleId}: Negative values are not allowed`);
      if (r.rate <= 0)
        errs.push(`Nozzle ${r.nozzleId}: Rate must be positive`);
    });
    return errs;
  }

  function validateCollections(): string[] {
    const errs: string[] = [];
    if (totalCollections < 0) errs.push('Total collections cannot be negative');
    if (Math.abs(mismatch) > 10000) errs.push(`Mismatch of ₹${mismatch.toFixed(2)} is unusually high. Please verify readings.`);
    return errs;
  }

  // ── Step navigation ──────────────────────────────────────────
  function goToCollections() {
    const errs = validateReadings();
    if (errs.length) { setValidationErrors(errs); return; }
    setValidationErrors([]);
    setStep('collections');
  }

  function goToReview() {
    const errs = validateCollections();
    if (errs.length) { setValidationErrors(errs); return; }
    setValidationErrors([]);

    const cardSwipes = [];
    if (phonePeMorning > 0) {
      cardSwipes.push({ mode: 'PhonePe Morning', amount: phonePeMorning, tid: phonePeTidMorning, batch: phonePeBatchMorning });
    }
    if (phonePeNight > 0) {
      cardSwipes.push({ mode: 'PhonePe Night', amount: phonePeNight, tid: phonePeTidNight, batch: phonePeBatchNight });
    }
    if (creditCardMorning > 0) {
      cardSwipes.push({ mode: 'PineLabs Card Morning', amount: creditCardMorning, tid: creditCardTidMorning, batch: creditCardBatchMorning });
    }
    if (creditCardNight > 0) {
      cardSwipes.push({ mode: 'PineLabs Card Night', amount: creditCardNight, tid: creditCardTidNight, batch: creditCardBatchNight });
    }
    if (petroCardMorning > 0) {
      cardSwipes.push({ mode: 'PetroCard Morning', amount: petroCardMorning, tid: petroCardTidMorning, batch: petroCardBatchMorning });
    }
    if (petroCardNight > 0) {
      cardSwipes.push({ mode: 'PetroCard Night', amount: petroCardNight, tid: petroCardTidNight, batch: petroCardBatchNight });
    }
    setCardSwipeDetails(cardSwipes);

    setStep('review');
  }

  // ── Submit ───────────────────────────────────────────────────
  async function handleSubmit() {
    if (!profile) return;
    setError('');

    const cardSwipes = [];
    if (phonePeMorning > 0) {
      cardSwipes.push({ mode: 'PhonePe Morning', amount: phonePeMorning, tid: phonePeTidMorning, batch: phonePeBatchMorning });
    }
    if (phonePeNight > 0) {
      cardSwipes.push({ mode: 'PhonePe Night', amount: phonePeNight, tid: phonePeTidNight, batch: phonePeBatchNight });
    }
    if (creditCardMorning > 0) {
      cardSwipes.push({ mode: 'PineLabs Card Morning', amount: creditCardMorning, tid: creditCardTidMorning, batch: creditCardBatchMorning });
    }
    if (creditCardNight > 0) {
      cardSwipes.push({ mode: 'PineLabs Card Night', amount: creditCardNight, tid: creditCardTidNight, batch: creditCardBatchNight });
    }
    if (petroCardMorning > 0) {
      cardSwipes.push({ mode: 'PetroCard Morning', amount: petroCardMorning, tid: petroCardTidMorning, batch: petroCardBatchMorning });
    }
    if (petroCardNight > 0) {
      cardSwipes.push({ mode: 'PetroCard Night', amount: petroCardNight, tid: petroCardTidNight, batch: petroCardBatchNight });
    }

    const draftData = {
      pumpId,
      shiftDate,
      shiftType: shiftType as 'A' | 'B' | 'C',
      notes,
      nozzleReadings: nozzleRows.map(({ rowId: _r, isOpeningReadOnly, ...rest }) => rest),
      cash,
      upi: upiTotal,
      card: cardTotal,
      petroCard: petroCardTotal,
      cashDeposit,
      others,
      credit: creditTotal,
      expense,
      expenseNotes,
      short: 0,
      excess: 0,
      cardSwipeDetails: cardSwipes,
      debtorEntries,
      personalDebtors: [],
      cashDenominations: {
        denom500,
        denom200,
        denom100,
        denom50,
        denom20,
        denom10,
        coins
      },
      testingEntries: nozzleRows
        .filter(r => (r.testing || 0) > 0)
        .map(r => ({
          nozzleId: r.nozzleId,
          fuelType: r.fuelType,
          amount: r.testing || 0,
          pumpId: r.pumpId || pumpId
        })),
      phonePeMorning, phonePeTidMorning, phonePeBatchMorning,
      phonePeNight, phonePeTidNight, phonePeBatchNight,
      creditCardMorning, creditCardTidMorning, creditCardBatchMorning,
      creditCardNight, creditCardTidNight, creditCardBatchNight,
      petroCardMorning, petroCardTidMorning, petroCardBatchMorning,
      petroCardNight, petroCardTidNight, petroCardBatchNight,
    };

    if (!online) {
      const saved = await saveDraft({ ...draftData, status: 'queued' } as Parameters<typeof saveDraft>[0]);
      if (saved) setSuccess(true);
      return;
    }

    const savedDraft = await saveDraft(draftData as Parameters<typeof saveDraft>[0]);
    const err = await submitToSupabase(savedDraft, profile.id, profile.StationId);
    if (err) {
      setError(err);
    } else {
      setSuccess(true);
    }
  }

  if (success) {
    return (
      <div className="screen success-screen">
        <div className="success-card">
          <CheckCircle2 size={64} className="success-icon" />
          <h1 className="success-title">
            {online ? 'Submitted!' : 'Saved Offline!'}
          </h1>
          <p className="success-msg">
            {online
              ? 'Your shift entry has been submitted for manager approval.'
              : 'Your entry is saved and will be submitted when you\'re back online.'}
          </p>
          <button id="back-to-dashboard-btn" className="btn-primary" onClick={onBack}>
            Back to Dashboard
          </button>
        </div>
      </div>
    );
  }

  const steps = ['Readings', 'Collections', 'Review'];
  const currentStepIdx = step === 'readings' ? 0 : step === 'collections' ? 1 : 2;

  return (
    <div className="screen submit-screen">
      {/* Header */}
      <header className="app-header">
        <button className="icon-btn" onClick={onBack} aria-label="Back">
          <ArrowLeft size={22} />
        </button>
        <span className="header-title">New Shift Entry</span>
        {!online && (
          <div className="offline-badge">
            <WifiOff size={16} />
            <span>Offline</span>
          </div>
        )}
      </header>

      {/* Step Indicator */}
      <div className="step-indicator">
        {steps.map((s, i) => (
          <div key={s} className={`step-dot-row ${i < currentStepIdx ? 'done' : i === currentStepIdx ? 'active' : ''}`}>
            <div className="step-dot">{i < currentStepIdx ? '✓' : i + 1}</div>
            <span className="step-label">{s}</span>
            {i < steps.length - 1 && <div className="step-connector" />}
          </div>
        ))}
      </div>

      <main className="submit-main">
        {/* ── Step 1: Readings ─────────────────────────────── */}
        {step === 'readings' && (
          <div className="form-section" id="step-readings">
            <h2 className="section-heading">Shift Information</h2>

            {/* Pump & Shift — read-only, assigned by manager */}
            <div className="field-row-2">
              <div className="field-group">
                <label className="field-label">Assigned Pump</label>
                <div className="field-input" style={{ background: '#1e293b', display: 'flex', alignItems: 'center', minHeight: '42px', paddingLeft: '12px', fontWeight: 'bold', color: '#f8fafc', borderRadius: '0.375rem' }}>
                  Pump {pumpId}
                </div>
              </div>
              <div className="field-group">
                <label className="field-label">Assigned Shift</label>
                <div className="field-input" style={{ background: '#1e293b', display: 'flex', alignItems: 'center', minHeight: '42px', paddingLeft: '12px', fontWeight: 'bold', color: '#f8fafc', borderRadius: '0.375rem' }}>
                  Shift {shiftType} ({shiftType === 'A' ? 'Morning' : shiftType === 'B' ? 'Afternoon' : 'Night'})
                </div>
              </div>
            </div>

            <div className="field-group">
              <label className="field-label">Shift Date</label>
              <div className="field-input" style={{ background: '#1e293b', display: 'flex', alignItems: 'center', minHeight: '42px', paddingLeft: '12px', fontWeight: 'bold', color: '#f8fafc', borderRadius: '0.375rem' }}>
                {new Date(shiftDate + 'T00:00:00').toLocaleDateString('en-IN', { day: 'numeric', month: 'long', year: 'numeric' })}
              </div>
            </div>

            <h2 className="section-heading" style={{ marginTop: '1.5rem' }}>Nozzle Readings</h2>

            {/* Loading / Error states for nozzle config */}
            {nozzleLoading ? (
              <div style={{ display: 'flex', alignItems: 'center', gap: '10px', padding: '20px', background: '#1e293b', borderRadius: '0.5rem', color: '#94a3b8' }}>
                <Loader2 size={20} className="spin" />
                <span>Loading nozzle configuration for Pump {pumpId}...</span>
              </div>
            ) : nozzleError ? (
              <div className="validation-errors">
                <AlertTriangle size={16} />
                <span style={{ flex: 1 }}>{nozzleError}</span>
                <button
                  className="btn-outline"
                  style={{ padding: '4px 12px', fontSize: '0.75rem' }}
                  onClick={loadNozzleConfig}
                >
                  <RefreshCw size={14} /> Retry
                </button>
              </div>
            ) : null}

            {!nozzleLoading && !nozzleError && nozzleRows.map((row) => (
              <div key={row.rowId} className="nozzle-card">
                <div className="nozzle-card-header" style={{ borderBottom: '1px solid #334155', paddingBottom: '8px', marginBottom: '12px' }}>
                  <span className="nozzle-num" style={{ fontSize: '1.05rem', fontWeight: 'bold' }}>Nozzle {row.nozzleId} ({row.fuelType})</span>
                  <span style={{ fontSize: '0.85rem', color: '#94a3b8', fontWeight: '500' }}>Rate: ₹{row.rate.toFixed(2)} / L</span>
                </div>
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: '8px' }}>
                  <div className="field-group" style={{ marginBottom: '8px' }}>
                    <label className="field-label" style={{ fontSize: '0.75rem' }}>Opening (L)</label>
                    <input
                      type="number"
                      className="field-input"
                      value={row.openingReading || ''}
                      step="0.01"
                      min="0"
                      readOnly={row.isOpeningReadOnly}
                      style={row.isOpeningReadOnly ? { backgroundColor: '#1e293b', color: '#64748b', border: '1px solid #334155', cursor: 'not-allowed', padding: '6px' } : { padding: '6px' }}
                      onChange={e => updateNozzle(row.rowId, 'openingReading', e.target.value)}
                    />
                  </div>
                  <div className="field-group" style={{ marginBottom: '8px' }}>
                    <label className="field-label" style={{ fontSize: '0.75rem' }}>Closing (L)</label>
                    <input
                      type="number"
                      className={`field-input ${row.closingReading < row.openingReading && row.closingReading > 0 ? 'input-error' : ''}`}
                      value={row.closingReading || ''}
                      step="0.01"
                      min="0"
                      style={{ padding: '6px' }}
                      onChange={e => updateNozzle(row.rowId, 'closingReading', e.target.value)}
                    />
                  </div>
                  <div className="field-group" style={{ marginBottom: '8px' }}>
                    <label className="field-label" style={{ fontSize: '0.75rem' }}>Testing (L)</label>
                    <input
                      type="number"
                      className="field-input"
                      value={row.testing || ''}
                      step="0.01"
                      min="0"
                      style={{ padding: '6px' }}
                      onChange={e => updateNozzle(row.rowId, 'testing', e.target.value)}
                    />
                  </div>
                </div>
                <div className="nozzle-sale-summary" style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.85rem', color: '#e2e8f0', marginTop: '6px' }}>
                  <span>Net Sale: {Math.max(0, row.closingReading - row.openingReading - (row.testing || 0)).toFixed(2)} L (Dispensed: {Math.max(0, row.closingReading - row.openingReading).toFixed(2)} L)</span>
                  <span style={{ fontWeight: 'bold' }}>= ₹{(Math.max(0, row.closingReading - row.openingReading) * row.rate).toLocaleString('en-IN', { minimumFractionDigits: 2 })}</span>
                </div>
              </div>
            ))}

            {!nozzleLoading && !nozzleError && nozzleRows.length > 0 && (
              <div className="gross-sales-bar">
                <span>Gross Sales</span>
                <span className="gross-amount">₹{grossSales.toLocaleString('en-IN', { minimumFractionDigits: 2 })}</span>
              </div>
            )}

            {validationErrors.length > 0 && (
              <div className="validation-errors">
                <AlertTriangle size={16} />
                <ul>
                  {validationErrors.map((e, i) => <li key={i}>{e}</li>)}
                </ul>
              </div>
            )}

            <button
              id="next-collections-btn"
              className="btn-primary"
              onClick={goToCollections}
              disabled={nozzleLoading || nozzleRows.length === 0}
            >
              Continue to Collections →
            </button>
          </div>
        )}

        {/* ── Step 2: Collections ──────────────────────────── */}
        {step === 'collections' && (
          <div className="form-section" id="step-collections">
            <h2 className="section-heading">Payment Collections</h2>

            {/* Cash in Hand (Denominations) */}
            <div className="nozzle-card" style={{ marginBottom: '16px', padding: '16px' }}>
              <h3 style={{ fontSize: '1rem', fontWeight: 'bold', marginBottom: '12px', borderBottom: '1px solid #334155', paddingBottom: '6px' }}>Cash in Hand (Denominations)</h3>
              
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '10px 16px', marginBottom: '12px' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                  <span style={{ width: '45px', fontWeight: 'bold', fontSize: '0.9rem' }}>₹500 x</span>
                  <input type="number" className="field-input" style={{ padding: '6px' }} value={denom500 || ''} min="0" onChange={e => setDenom500(Math.max(0, parseInt(e.target.value) || 0))} />
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                  <span style={{ width: '45px', fontWeight: 'bold', fontSize: '0.9rem' }}>₹200 x</span>
                  <input type="number" className="field-input" style={{ padding: '6px' }} value={denom200 || ''} min="0" onChange={e => setDenom200(Math.max(0, parseInt(e.target.value) || 0))} />
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                  <span style={{ width: '45px', fontWeight: 'bold', fontSize: '0.9rem' }}>₹100 x</span>
                  <input type="number" className="field-input" style={{ padding: '6px' }} value={denom100 || ''} min="0" onChange={e => setDenom100(Math.max(0, parseInt(e.target.value) || 0))} />
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                  <span style={{ width: '45px', fontWeight: 'bold', fontSize: '0.9rem' }}>₹50 x</span>
                  <input type="number" className="field-input" style={{ padding: '6px' }} value={denom50 || ''} min="0" onChange={e => setDenom50(Math.max(0, parseInt(e.target.value) || 0))} />
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                  <span style={{ width: '45px', fontWeight: 'bold', fontSize: '0.9rem' }}>₹20 x</span>
                  <input type="number" className="field-input" style={{ padding: '6px' }} value={denom20 || ''} min="0" onChange={e => setDenom20(Math.max(0, parseInt(e.target.value) || 0))} />
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                  <span style={{ width: '45px', fontWeight: 'bold', fontSize: '0.9rem' }}>₹10 x</span>
                  <input type="number" className="field-input" style={{ padding: '6px' }} value={denom10 || ''} min="0" onChange={e => setDenom10(Math.max(0, parseInt(e.target.value) || 0))} />
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', gridColumn: 'span 2' }}>
                  <span style={{ fontWeight: 'bold', fontSize: '0.9rem', marginRight: '6px' }}>Coins/Other (₹)</span>
                  <input type="number" className="field-input" style={{ padding: '6px' }} value={coins || ''} min="0" onChange={e => setCoins(Math.max(0, parseFloat(e.target.value) || 0))} />
                </div>
              </div>

              <div style={{ display: 'flex', justifyContent: 'space-between', borderTop: '1px solid #334155', paddingTop: '10px', marginTop: '10px', fontSize: '1rem', fontWeight: 'bold', color: '#10b981' }}>
                <span>Total Cash:</span>
                <span>₹{cash.toLocaleString('en-IN', { minimumFractionDigits: 2 })}</span>
              </div>
            </div>

            {/* PhonePe UPI */}
            <div className="nozzle-card" style={{ marginBottom: '16px', padding: '16px' }}>
              <h3 style={{ fontSize: '1rem', fontWeight: 'bold', marginBottom: '12px', borderBottom: '1px solid #334155', paddingBottom: '6px' }}>PhonePe UPI</h3>
              
              <div style={{ display: 'grid', gridTemplateColumns: '1.2fr 1fr 1fr', gap: '8px', marginBottom: '12px' }}>
                <div className="field-group">
                  <label className="field-label">Morning (₹)</label>
                  <input type="number" className="field-input" value={phonePeMorning || ''} step="0.01" min="0" onChange={e => setPhonePeMorning(Number(e.target.value))} />
                </div>
                <div className="field-group">
                  <label className="field-label">TID</label>
                  <input type="text" className="field-input" placeholder="TID" value={phonePeTidMorning} onChange={e => setPhonePeTidMorning(e.target.value)} />
                </div>
                <div className="field-group">
                  <label className="field-label">Batch</label>
                  <input type="text" className="field-input" placeholder="Batch" value={phonePeBatchMorning} onChange={e => setPhonePeBatchMorning(e.target.value)} />
                </div>
              </div>

              <div style={{ display: 'grid', gridTemplateColumns: '1.2fr 1fr 1fr', gap: '8px' }}>
                <div className="field-group">
                  <label className="field-label">Night (₹)</label>
                  <input type="number" className="field-input" value={phonePeNight || ''} step="0.01" min="0" onChange={e => setPhonePeNight(Number(e.target.value))} />
                </div>
                <div className="field-group">
                  <label className="field-label">TID</label>
                  <input type="text" className="field-input" placeholder="TID" value={phonePeTidNight} onChange={e => setPhonePeTidNight(e.target.value)} />
                </div>
                <div className="field-group">
                  <label className="field-label">Batch</label>
                  <input type="text" className="field-input" placeholder="Batch" value={phonePeBatchNight} onChange={e => setPhonePeBatchNight(e.target.value)} />
                </div>
              </div>
            </div>

            {/* PineLabs Credit Card */}
            <div className="nozzle-card" style={{ marginBottom: '16px', padding: '16px' }}>
              <h3 style={{ fontSize: '1rem', fontWeight: 'bold', marginBottom: '12px', borderBottom: '1px solid #334155', paddingBottom: '6px' }}>PineLab Credit Card</h3>
              
              <div style={{ display: 'grid', gridTemplateColumns: '1.2fr 1fr 1fr', gap: '8px', marginBottom: '12px' }}>
                <div className="field-group">
                  <label className="field-label">Morning (₹)</label>
                  <input type="number" className="field-input" value={creditCardMorning || ''} step="0.01" min="0" onChange={e => setCreditCardMorning(Number(e.target.value))} />
                </div>
                <div className="field-group">
                  <label className="field-label">TID</label>
                  <input type="text" className="field-input" placeholder="TID" value={creditCardTidMorning} onChange={e => setCreditCardTidMorning(e.target.value)} />
                </div>
                <div className="field-group">
                  <label className="field-label">Batch</label>
                  <input type="text" className="field-input" placeholder="Batch" value={creditCardBatchMorning} onChange={e => setCreditCardBatchMorning(e.target.value)} />
                </div>
              </div>

              <div style={{ display: 'grid', gridTemplateColumns: '1.2fr 1fr 1fr', gap: '8px' }}>
                <div className="field-group">
                  <label className="field-label">Night (₹)</label>
                  <input type="number" className="field-input" value={creditCardNight || ''} step="0.01" min="0" onChange={e => setCreditCardNight(Number(e.target.value))} />
                </div>
                <div className="field-group">
                  <label className="field-label">TID</label>
                  <input type="text" className="field-input" placeholder="TID" value={creditCardTidNight} onChange={e => setCreditCardTidNight(e.target.value)} />
                </div>
                <div className="field-group">
                  <label className="field-label">Batch</label>
                  <input type="text" className="field-input" placeholder="Batch" value={creditCardBatchNight} onChange={e => setCreditCardBatchNight(e.target.value)} />
                </div>
              </div>
            </div>

            {/* Petro Card */}
            <div className="nozzle-card" style={{ marginBottom: '16px', padding: '16px' }}>
              <h3 style={{ fontSize: '1rem', fontWeight: 'bold', marginBottom: '12px', borderBottom: '1px solid #334155', paddingBottom: '6px' }}>Petro Card</h3>
              
              <div style={{ display: 'grid', gridTemplateColumns: '1.2fr 1fr 1fr', gap: '8px', marginBottom: '12px' }}>
                <div className="field-group">
                  <label className="field-label">Morning (₹)</label>
                  <input type="number" className="field-input" value={petroCardMorning || ''} step="0.01" min="0" onChange={e => setPetroCardMorning(Number(e.target.value))} />
                </div>
                <div className="field-group">
                  <label className="field-label">TID</label>
                  <input type="text" className="field-input" placeholder="TID" value={petroCardTidMorning} onChange={e => setPetroCardTidMorning(e.target.value)} />
                </div>
                <div className="field-group">
                  <label className="field-label">Batch</label>
                  <input type="text" className="field-input" placeholder="Batch" value={petroCardBatchMorning} onChange={e => setPetroCardBatchMorning(e.target.value)} />
                </div>
              </div>

              <div style={{ display: 'grid', gridTemplateColumns: '1.2fr 1fr 1fr', gap: '8px' }}>
                <div className="field-group">
                  <label className="field-label">Night (₹)</label>
                  <input type="number" className="field-input" value={petroCardNight || ''} step="0.01" min="0" onChange={e => setPetroCardNight(Number(e.target.value))} />
                </div>
                <div className="field-group">
                  <label className="field-label">TID</label>
                  <input type="text" className="field-input" placeholder="TID" value={petroCardTidNight} onChange={e => setPetroCardTidNight(e.target.value)} />
                </div>
                <div className="field-group">
                  <label className="field-label">Batch</label>
                  <input type="text" className="field-input" placeholder="Batch" value={petroCardBatchNight} onChange={e => setPetroCardBatchNight(e.target.value)} />
                </div>
              </div>
            </div>

            {/* Other Collections */}
            <div className="nozzle-card" style={{ marginBottom: '16px', padding: '16px' }}>
              <h3 style={{ fontSize: '1rem', fontWeight: 'bold', marginBottom: '12px', borderBottom: '1px solid #334155', paddingBottom: '6px' }}>Other Collections</h3>
              <div className="field-row-2">
                <div className="field-group">
                  <label className="field-label">Cash Deposit (Bank) (₹)</label>
                  <input
                    type="number"
                    className="field-input"
                    value={cashDeposit || ''}
                    step="0.01"
                    min="0"
                    onChange={e => setCashDeposit(Number(e.target.value))}
                  />
                </div>
                <div className="field-group">
                  <label className="field-label">Others (Not in Total) (₹)</label>
                  <input
                    type="number"
                    className="field-input"
                    value={others || ''}
                    step="0.01"
                    min="0"
                    onChange={e => setOthers(Number(e.target.value))}
                  />
                </div>
              </div>
            </div>

            {/* Adjustments (Expense only) */}
            <div className="nozzle-card" style={{ marginBottom: '16px', padding: '16px' }}>
              <h3 style={{ fontSize: '1rem', fontWeight: 'bold', marginBottom: '12px', borderBottom: '1px solid #334155', paddingBottom: '6px' }}>Adjustments</h3>
              <div className="field-group" style={{ marginBottom: '12px' }}>
                <label className="field-label">Expense (₹)</label>
                <input type="number" className="field-input" value={expense || ''} step="0.01" min="0" onChange={e => setExpense(Number(e.target.value))} />
              </div>
              <div className="field-group" style={{ marginBottom: 0 }}>
                <label className="field-label">Expense Notes</label>
                <input
                  type="text"
                  className="field-input"
                  value={expenseNotes}
                  placeholder="What was the expense for?"
                  onChange={e => setExpenseNotes(e.target.value)}
                />
              </div>
            </div>

            {/* Debtors Log */}
            <div className="nozzle-card" style={{ marginBottom: '16px', padding: '16px' }}>
              <h3 style={{ fontSize: '1rem', fontWeight: 'bold', marginBottom: '12px', borderBottom: '1px solid #334155', paddingBottom: '6px' }}>Debtors (Credit/Debit)</h3>
              
              <div className="debtor-entry-form" style={{ background: '#0f172a', padding: '12px', borderRadius: '6px', marginBottom: '12px' }}>
                <div className="field-row-2">
                  <div className="field-group">
                    <label className="field-label">Debtor Name</label>
                    <select
                      className="field-input"
                      value={selectedCreditorId}
                      onChange={e => {
                        const val = e.target.value;
                        setSelectedCreditorId(val);
                        const c = creditorList.find(x => x.id === val);
                        setNewDebtorName(c ? c.name : '');
                        setNewDebtorVehicle('');
                      }}
                    >
                      <option value="">-- Select Debtor --</option>
                      {creditorList.map(c => (
                        <option key={c.id} value={c.id}>{c.name}</option>
                      ))}
                    </select>
                  </div>
                  <div className="field-group">
                    <label className="field-label">Amount (₹)</label>
                    <input
                      type="number"
                      step="0.01"
                      className="field-input"
                      placeholder="0.00"
                      value={newDebtorAmount}
                      onChange={e => setNewDebtorAmount(e.target.value)}
                    />
                  </div>
                </div>
                
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', margin: '10px 0 6px 0' }}>
                  <input
                    type="checkbox"
                    id="custom-vehicle-check"
                    checked={customVehicle}
                    onChange={e => {
                      setCustomVehicle(e.target.checked);
                      setNewDebtorVehicle('');
                    }}
                  />
                  <label htmlFor="custom-vehicle-check" style={{ fontSize: '0.85rem', fontWeight: '500', color: '#94a3b8', cursor: 'pointer' }}>
                    Type custom vehicle number
                  </label>
                </div>

                <div className="field-row-2">
                  <div className="field-group">
                    <label className="field-label">Vehicle No.</label>
                    {customVehicle ? (
                      <input
                        type="text"
                        className="field-input"
                        placeholder="MH-12-XX-XXXX"
                        value={newDebtorVehicle}
                        onChange={e => setNewDebtorVehicle(e.target.value)}
                      />
                    ) : (
                      <select
                        className="field-input"
                        value={newDebtorVehicle}
                        onChange={e => setNewDebtorVehicle(e.target.value)}
                        disabled={!selectedCreditorId}
                      >
                        <option value="">-- Select Vehicle --</option>
                        {vehicleList
                          .filter(v => v.creditorId === selectedCreditorId)
                          .map((v, i) => (
                            <option key={i} value={v.vehicleNumber}>{v.vehicleNumber}</option>
                          ))}
                      </select>
                    )}
                  </div>
                  <div className="field-group">
                    <label className="field-label">Slip No.</label>
                    <input
                      type="text"
                      className="field-input"
                      placeholder="Slip No."
                      value={newDebtorSlip}
                      onChange={e => setNewDebtorSlip(e.target.value)}
                    />
                  </div>
                </div>
                
                <button
                  type="button"
                  className="btn-outline"
                  style={{ marginTop: '12px', width: '100%', padding: '8px' }}
                  onClick={() => {
                    if (!newDebtorName || !newDebtorAmount || Number(newDebtorAmount) <= 0) return;
                    
                    const time = new Date().toLocaleTimeString('en-US', { hour12: false, hour: '2-digit', minute: '2-digit' });
                    setDebtorEntries(prev => [...prev, {
                      debtorName: newDebtorName,
                      amount: Number(newDebtorAmount),
                      vehicleNumber: newDebtorVehicle,
                      slipNumber: newDebtorSlip,
                      time
                    }]);
                    
                    // Reset fields
                    setNewDebtorAmount('');
                    setNewDebtorVehicle('');
                    setNewDebtorSlip('');
                    setSelectedCreditorId('');
                    setNewDebtorName('');
                    setCustomVehicle(false);
                  }}
                >
                  + Add Debtor Entry
                </button>
              </div>

              {debtorEntries.length > 0 && (
                <div className="debtor-entry-list">
                  {debtorEntries.map((item, idx) => (
                    <div key={idx} style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', background: '#0f172a', padding: '8px 12px', borderRadius: '4px', marginBottom: '4px', fontSize: '0.85rem' }}>
                      <div>
                        <strong>{item.debtorName}</strong>: ₹{item.amount.toFixed(2)} <br />
                        <span style={{ color: '#94a3b8' }}>
                          Veh: {item.vehicleNumber || 'N/A'} | Slip: {item.slipNumber || 'N/A'} | Time: {item.time}
                        </span>
                      </div>
                      <button
                        type="button"
                        style={{ color: '#ef4444', background: 'none', border: 'none', cursor: 'pointer', fontWeight: 'bold' }}
                        onClick={() => {
                          setDebtorEntries(prev => prev.filter((_, i) => i !== idx));
                        }}
                      >
                        Delete
                      </button>
                    </div>
                  ))}
                </div>
              )}
            </div>



            {/* Mismatch Preview */}
            <div className={`mismatch-preview ${Math.abs(mismatch) > 500 ? 'mismatch-warn' : 'mismatch-ok'}`}>
              <div className="mismatch-row">
                <span>Gross Sales</span>
                <span>₹{grossSales.toLocaleString('en-IN', { minimumFractionDigits: 2 })}</span>
              </div>
              <div className="mismatch-row">
                <span>Total Collections</span>
                <span>₹{totalCollections.toLocaleString('en-IN', { minimumFractionDigits: 2 })}</span>
              </div>
              <div className="mismatch-row mismatch-total">
                <span>Mismatch</span>
                <span className={Math.abs(mismatch) > 500 ? 'text-warn' : 'text-ok'}>
                  ₹{mismatch.toFixed(2)}
                </span>
              </div>
            </div>

            {validationErrors.length > 0 && (
              <div className="validation-errors">
                <AlertTriangle size={16} />
                <ul>
                  {validationErrors.map((e, i) => <li key={i}>{e}</li>)}
                </ul>
              </div>
            )}

            <div className="btn-row">
              <button className="btn-outline" onClick={() => setStep('readings')}>← Back</button>
              <button id="next-review-btn" className="btn-primary" onClick={goToReview}>Review →</button>
            </div>
          </div>
        )}

        {/* ── Step 3: Review ───────────────────────────────── */}
        {step === 'review' && (
          <div className="form-section" id="step-review">
            <h2 className="section-heading">Review Submission</h2>

            <div className="review-block">
              <div className="review-row"><span>Pump</span><strong>Pump {pumpId}</strong></div>
              <div className="review-row"><span>Date</span><strong>{new Date(shiftDate + 'T00:00:00').toLocaleDateString('en-IN', { day: 'numeric', month: 'long', year: 'numeric' })}</strong></div>
              <div className="review-row"><span>Shift</span><strong>Shift {shiftType} ({shiftType === 'A' ? 'Morning' : shiftType === 'B' ? 'Afternoon' : 'Night'})</strong></div>
            </div>

            <div className="review-block">
              <p className="review-block-title">Nozzle Readings</p>
              {nozzleRows.map((r) => (
                <div key={r.rowId} className="review-row" style={{ flexDirection: 'column', alignItems: 'stretch', marginBottom: '8px' }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                    <span>Nozzle {r.nozzleId} ({r.fuelType})</span>
                    <strong>
                      {Math.max(0, r.closingReading - r.openingReading).toFixed(2)}L
                      {' '}= ₹{(Math.max(0, r.closingReading - r.openingReading) * r.rate).toLocaleString('en-IN', { minimumFractionDigits: 2 })}
                    </strong>
                  </div>
                  {(r.testing || 0) > 0 && (
                    <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.8rem', color: '#94a3b8', paddingLeft: '8px', marginTop: '2px' }}>
                      <span>└ Testing Quantity</span>
                      <span>-{r.testing} L (₹{((r.testing || 0) * r.rate).toFixed(2)})</span>
                    </div>
                  )}
                </div>
              ))}
              <div className="review-row review-total" style={{ borderTop: '1px solid #334155', paddingTop: '8px', marginTop: '8px' }}>
                <span>Gross Sales</span><strong>₹{grossSales.toLocaleString('en-IN', { minimumFractionDigits: 2 })}</strong>
              </div>
            </div>

            <div className="review-block">
              <p className="review-block-title">Collections &amp; Adjustments</p>
              {cash > 0 && (
                <>
                  <div className="review-row"><span>Cash (Total)</span><strong>₹{cash.toLocaleString('en-IN', { minimumFractionDigits: 2 })}</strong></div>
                  <div style={{ fontSize: '0.8rem', color: '#94a3b8', paddingLeft: '12px', marginBottom: '8px', borderLeft: '2px solid #334155' }}>
                    {denom500 > 0 && <div>500 x {denom500} = ₹{denom500 * 500}</div>}
                    {denom200 > 0 && <div>200 x {denom200} = ₹{denom200 * 200}</div>}
                    {denom100 > 0 && <div>100 x {denom100} = ₹{denom100 * 100}</div>}
                    {denom50 > 0 && <div>50 x {denom50} = ₹{denom50 * 50}</div>}
                    {denom20 > 0 && <div>20 x {denom20} = ₹{denom20 * 20}</div>}
                    {denom10 > 0 && <div>10 x {denom10} = ₹{denom10 * 10}</div>}
                    {coins > 0 && <div>Coins/Other = ₹{coins}</div>}
                  </div>
                </>
              )}
              {upiTotal > 0 && <div className="review-row"><span>UPI</span><strong>₹{upiTotal.toLocaleString('en-IN', { minimumFractionDigits: 2 })}</strong></div>}
              {cardTotal > 0 && <div className="review-row"><span>Card</span><strong>₹{cardTotal.toLocaleString('en-IN', { minimumFractionDigits: 2 })}</strong></div>}
              {creditTotal > 0 && <div className="review-row"><span>Credit (Debtors)</span><strong>₹{creditTotal.toLocaleString('en-IN', { minimumFractionDigits: 2 })}</strong></div>}
              {totalTesting > 0 && <div className="review-row"><span>Testing Credit</span><strong>₹{totalTesting.toLocaleString('en-IN', { minimumFractionDigits: 2 })}</strong></div>}
              {expense > 0 && <div className="review-row"><span>Expense</span><strong>₹{expense.toLocaleString('en-IN', { minimumFractionDigits: 2 })}</strong></div>}
              <div className={`review-row review-total ${Math.abs(mismatch) > 500 ? 'review-warn' : ''}`} style={{ borderTop: '1px solid #334155', paddingTop: '8px', marginTop: '8px' }}>
                <span>Mismatch</span><strong>₹{mismatch.toFixed(2)}</strong>
              </div>
            </div>

            {cardSwipeDetails.length > 0 && (
              <div className="review-block">
                <p className="review-block-title">Card Swipe Details</p>
                {cardSwipeDetails.map((item, idx) => (
                  <div key={idx} className="review-row">
                    <span>{item.mode} (TID: {item.tid || 'N/A'}, Batch: {item.batch || 'N/A'})</span>
                    <strong>₹{item.amount.toFixed(2)}</strong>
                  </div>
                ))}
              </div>
            )}

            {debtorEntries.length > 0 && (
              <div className="review-block">
                <p className="review-block-title">Debtor Entries Log</p>
                {debtorEntries.map((item, idx) => (
                  <div key={idx} className="review-row">
                    <span>{item.debtorName} ({item.time})</span>
                    <strong>₹{item.amount.toFixed(2)}</strong>
                  </div>
                ))}
              </div>
            )}



            <div className="field-group">
              <label className="field-label" htmlFor="submission-notes">Notes (optional)</label>
              <textarea
                id="submission-notes"
                className="field-input field-textarea"
                value={notes}
                onChange={e => setNotes(e.target.value)}
                placeholder="Any remarks for the manager..."
                rows={3}
              />
            </div>

            {error && (
              <div className="alert-error" role="alert">
                <AlertTriangle size={16} />
                <span>{error}</span>
              </div>
            )}

            {!online && (
              <div className="alert-info">
                <WifiOff size={16} />
                <span>You are offline. This entry will be saved locally and submitted when online.</span>
              </div>
            )}

            <div className="btn-row">
              <button className="btn-outline" onClick={() => setStep('collections')}>← Back</button>
              <button
                id="submit-btn"
                className={`btn-primary ${syncing ? 'btn-loading' : ''}`}
                onClick={handleSubmit}
                disabled={syncing}
              >
                {syncing ? <Loader2 size={18} className="spin" /> : <Send size={18} />}
                {syncing ? 'Submitting...' : online ? 'Submit Entry' : 'Save Offline'}
              </button>
            </div>
          </div>
        )}
      </main>
    </div>
  );
}
