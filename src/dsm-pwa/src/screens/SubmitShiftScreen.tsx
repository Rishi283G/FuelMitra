import { useState, useEffect } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { useSubmissionService } from '../hooks/useSubmissionService';
import type { DraftNozzleReading } from '../lib/db';
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
  const [shiftDate, setShiftDate] = useState(todayISO());
  const [notes, setNotes] = useState('');
  const [nozzleRows, setNozzleRows] = useState<NozzleRow[]>([]);
  const [cash, setCash] = useState(0);
  const [upi, setUpi] = useState(0);
  const [card, setCard] = useState(0);
  const [credit, setCredit] = useState(0);
  const [expense, setExpense] = useState(0);
  const [expenseNotes, setExpenseNotes] = useState('');
  const [short, setShort] = useState(0);
  const [excess, setExcess] = useState(0);

  // Expanded fields for Phase 3
  const [cardSwipeDetails, setCardSwipeDetails] = useState<{ mode: string; amount: number; tid: string; batch: string; }[]>([]);
  const [debtorEntries, setDebtorEntries] = useState<{ debtorName: string; amount: number; vehicleNumber?: string; time: string; }[]>([]);
  const [personalDebtors, setPersonalDebtors] = useState<{ amount: number; fuelProduct?: string; remarks?: string; paymentMethod: string; tid?: string; batch?: string; denom500?: number; denom200?: number; denom100?: number; denom50?: number; denom20?: number; denom10?: number; coins?: number; }[]>([]);

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

      // Try to fetch nozzle config from Supabase PumpNozzleConfig table
      const { data: nozzleConfig, error: nozzleErr } = await supabase
        .from('PumpNozzleConfig')
        .select('NozzleId, FuelType, SortOrder')
        .eq('StationId', profile.StationId)
        .eq('PumpId', pumpId)
        .eq('IsActive', true)
        .order('SortOrder', { ascending: true });

      let configRows: { nozzleId: number; fuelType: string }[] = [];

      if (!nozzleErr && nozzleConfig && nozzleConfig.length > 0) {
        // Use Supabase config
        configRows = nozzleConfig.map((r: { NozzleId: number; FuelType: string }) => ({
          nozzleId: r.NozzleId,
          fuelType: r.FuelType,
        }));
      } else {
        // Fallback: hardcoded config for this station (matches physical pump-nozzle wiring)
        const FALLBACK_CONFIG: Record<number, { nozzleId: number; fuelType: string }[]> = {
          1: [{ nozzleId: 1, fuelType: 'MS-II' }, { nozzleId: 2, fuelType: 'HSD' }],
          2: [{ nozzleId: 3, fuelType: 'MS-II' }, { nozzleId: 4, fuelType: 'HSD' }],
          3: [{ nozzleId: 5, fuelType: 'MS-I' }, { nozzleId: 6, fuelType: 'HSD' }],
          4: [{ nozzleId: 7, fuelType: 'MS-I' }, { nozzleId: 8, fuelType: 'HSD' }],
          5: [{ nozzleId: 9, fuelType: 'MS-II' }, { nozzleId: 10, fuelType: 'HSD' }],
          6: [{ nozzleId: 11, fuelType: 'MS-II' }, { nozzleId: 12, fuelType: 'HSD' }],
        };
        configRows = FALLBACK_CONFIG[pumpId] || [];
      }

      if (configRows.length === 0) {
        setNozzleError(`No nozzle configuration found for Pump ${pumpId}. Please contact your manager.`);
        setNozzleRows([]);
        return;
      }

      const rows: NozzleRow[] = configRows.map((n, index) => {
        let rate = msIRate;
        if (n.fuelType === 'HSD') rate = hsdRate;
        else if (n.fuelType === 'MS-II') rate = msIIRate;

        return {
          rowId: index + 1,
          nozzleId: n.nozzleId,
          fuelType: n.fuelType,
          openingReading: 0,
          closingReading: 0,
          rate,
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
  const totalCollections = cash + upi + card + credit;
  const mismatch = totalCollections + expense - grossSales;

  // ── Validation ───────────────────────────────────────────────
  function validateReadings(): string[] {
    const errs: string[] = [];
    if (nozzleRows.length === 0) {
      errs.push('No nozzle readings loaded. Please reload the page or contact your manager.');
      return errs;
    }
    nozzleRows.forEach((r) => {
      if (r.closingReading < r.openingReading)
        errs.push(`Nozzle ${r.nozzleId} (${r.fuelType}): Closing (${r.closingReading}) < Opening (${r.openingReading})`);
      if (r.openingReading < 0 || r.closingReading < 0)
        errs.push(`Nozzle ${r.nozzleId}: Negative readings are not allowed`);
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
    setStep('review');
  }

  // ── Submit ───────────────────────────────────────────────────
  async function handleSubmit() {
    if (!profile) return;
    setError('');

    const draftData = {
      pumpId,
      shiftDate,
      shiftType: shiftType as 'A' | 'B' | 'C',
      notes,
      nozzleReadings: nozzleRows.map(({ rowId: _r, ...rest }) => rest),
      cash, upi, card, credit, expense, expenseNotes, short, excess,
      cardSwipeDetails,
      debtorEntries,
      personalDebtors,
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
              <label className="field-label" htmlFor="shift-date">Shift Date</label>
              <input
                id="shift-date"
                type="date"
                className="field-input"
                value={shiftDate}
                max={todayISO()}
                onChange={e => setShiftDate(e.target.value)}
              />
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
                <div className="field-row-2">
                  <div className="field-group">
                    <label className="field-label">Opening Reading (L)</label>
                    <input
                      type="number"
                      className="field-input"
                      value={row.openingReading || ''}
                      step="0.01"
                      min="0"
                      onChange={e => updateNozzle(row.rowId, 'openingReading', e.target.value)}
                    />
                  </div>
                  <div className="field-group">
                    <label className="field-label">Closing Reading (L)</label>
                    <input
                      type="number"
                      className={`field-input ${row.closingReading < row.openingReading && row.closingReading > 0 ? 'input-error' : ''}`}
                      value={row.closingReading || ''}
                      step="0.01"
                      min="0"
                      onChange={e => updateNozzle(row.rowId, 'closingReading', e.target.value)}
                    />
                  </div>
                </div>
                <div className="nozzle-sale-summary">
                  <span>Sale: {Math.max(0, row.closingReading - row.openingReading).toFixed(2)} L</span>
                  <span>= ₹{(Math.max(0, row.closingReading - row.openingReading) * row.rate).toLocaleString('en-IN', { minimumFractionDigits: 2 })}</span>
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

            <div className="collection-grid">
              {[
                { id: 'cash', label: 'Cash (₹)', value: cash, setter: setCash },
                { id: 'upi', label: 'UPI / PhonePe (₹)', value: upi, setter: setUpi },
                { id: 'card', label: 'Card (₹)', value: card, setter: setCard },
                { id: 'credit', label: 'Credit / Debit (₹)', value: credit, setter: setCredit },
              ].map(({ id, label, value, setter }) => (
                <div key={id} className="field-group">
                  <label className="field-label" htmlFor={`col-${id}`}>{label}</label>
                  <input
                    id={`col-${id}`}
                    type="number"
                    className="field-input"
                    value={value}
                    step="0.01"
                    min="0"
                    onChange={e => setter(Number(e.target.value))}
                  />
                </div>
              ))}
            </div>

            <h2 className="section-heading" style={{ marginTop: '1.5rem' }}>Adjustments</h2>
            <div className="collection-grid">
              <div className="field-group">
                <label className="field-label" htmlFor="col-expense">Expense (₹)</label>
                <input id="col-expense" type="number" className="field-input" value={expense} step="0.01" min="0"
                  onChange={e => setExpense(Number(e.target.value))} />
              </div>
              <div className="field-group">
                <label className="field-label" htmlFor="col-short">Short (₹)</label>
                <input id="col-short" type="number" className="field-input" value={short} step="0.01" min="0"
                  onChange={e => setShort(Number(e.target.value))} />
              </div>
              <div className="field-group">
                <label className="field-label" htmlFor="col-excess">Excess (₹)</label>
                <input id="col-excess" type="number" className="field-input" value={excess} step="0.01" min="0"
                  onChange={e => setExcess(Number(e.target.value))} />
              </div>
            </div>

            <div className="field-group">
              <label className="field-label" htmlFor="expense-notes">Expense Notes</label>
              <input id="expense-notes" type="text" className="field-input" value={expenseNotes}
                placeholder="What was the expense for?" onChange={e => setExpenseNotes(e.target.value)} />
            </div>

            {/* Card Swipe Details Section */}
            <h2 className="section-heading" style={{ marginTop: '1.5rem' }}>Card Swipe Details</h2>
            <div className="card-swipe-form" style={{ background: '#1e293b', padding: '12px', borderRadius: '8px', marginBottom: '12px' }}>
              <div className="field-row-2">
                <div className="field-group">
                  <label className="field-label">Card Mode</label>
                  <select id="swipe-mode" className="field-input" defaultValue="PhonePe Card">
                    <option value="PhonePe Card">PhonePe Card</option>
                    <option value="PineLabs Card">PineLabs Card</option>
                    <option value="PetroCard">PetroCard</option>
                  </select>
                </div>
                <div className="field-group">
                  <label className="field-label">Amount (₹)</label>
                  <input id="swipe-amount" type="number" step="0.01" className="field-input" placeholder="0.00" />
                </div>
              </div>
              <div className="field-row-2" style={{ marginTop: '8px' }}>
                <div className="field-group">
                  <label className="field-label">TID</label>
                  <input id="swipe-tid" type="text" className="field-input" placeholder="TID" />
                </div>
                <div className="field-group">
                  <label className="field-label">Batch No.</label>
                  <input id="swipe-batch" type="text" className="field-input" placeholder="Batch" />
                </div>
              </div>
              <button
                type="button"
                className="btn-outline"
                style={{ marginTop: '12px', width: '100%', padding: '8px' }}
                onClick={() => {
                  const mode = (document.getElementById('swipe-mode') as HTMLSelectElement).value;
                  const amountVal = (document.getElementById('swipe-amount') as HTMLInputElement).value;
                  const tid = (document.getElementById('swipe-tid') as HTMLInputElement).value;
                  const batch = (document.getElementById('swipe-batch') as HTMLInputElement).value;
                  if (!amountVal || Number(amountVal) <= 0) return;
                  setCardSwipeDetails(prev => [...prev, { mode, amount: Number(amountVal), tid, batch }]);
                  setCard(prev => prev + Number(amountVal));
                  (document.getElementById('swipe-amount') as HTMLInputElement).value = '';
                  (document.getElementById('swipe-tid') as HTMLInputElement).value = '';
                  (document.getElementById('swipe-batch') as HTMLInputElement).value = '';
                }}
              >
                + Add Card Swipe
              </button>
            </div>

            {cardSwipeDetails.length > 0 && (
              <div className="card-swipe-list" style={{ marginBottom: '16px' }}>
                {cardSwipeDetails.map((item, idx) => (
                  <div key={idx} style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', background: '#0f172a', padding: '8px 12px', borderRadius: '4px', marginBottom: '4px', fontSize: '0.85rem' }}>
                    <div>
                      <strong>{item.mode}</strong>: ₹{item.amount.toFixed(2)} <br />
                      <span style={{ color: '#94a3b8' }}>TID: {item.tid} | Batch: {item.batch}</span>
                    </div>
                    <button
                      type="button"
                      style={{ color: '#ef4444', background: 'none', border: 'none', cursor: 'pointer', fontWeight: 'bold' }}
                      onClick={() => {
                        setCardSwipeDetails(prev => prev.filter((_, i) => i !== idx));
                        setCard(prev => Math.max(0, prev - item.amount));
                      }}
                    >
                      Delete
                    </button>
                  </div>
                ))}
              </div>
            )}

            {/* Debtor Entries Section */}
            <h2 className="section-heading" style={{ marginTop: '1.5rem' }}>Debtor Entries Log</h2>
            <div className="debtor-entry-form" style={{ background: '#1e293b', padding: '12px', borderRadius: '8px', marginBottom: '12px' }}>
              <div className="field-row-2">
                <div className="field-group">
                  <label className="field-label">Debtor Name</label>
                  <input id="debtor-name" type="text" className="field-input" placeholder="Name" />
                </div>
                <div className="field-group">
                  <label className="field-label">Amount (₹)</label>
                  <input id="debtor-amount" type="number" step="0.01" className="field-input" placeholder="0.00" />
                </div>
              </div>
              <div className="field-row-2" style={{ marginTop: '8px' }}>
                <div className="field-group">
                  <label className="field-label">Vehicle No.</label>
                  <input id="debtor-vehicle" type="text" className="field-input" placeholder="Vehicle No. (Optional)" />
                </div>
                <div className="field-group">
                  <label className="field-label">Time</label>
                  <input id="debtor-time" type="text" className="field-input" defaultValue={new Date().toLocaleTimeString('en-US', { hour12: false, hour: '2-digit', minute: '2-digit' })} />
                </div>
              </div>
              <button
                type="button"
                className="btn-outline"
                style={{ marginTop: '12px', width: '100%', padding: '8px' }}
                onClick={() => {
                  const debtorName = (document.getElementById('debtor-name') as HTMLInputElement).value;
                  const amountVal = (document.getElementById('debtor-amount') as HTMLInputElement).value;
                  const vehicleNumber = (document.getElementById('debtor-vehicle') as HTMLInputElement).value;
                  const time = (document.getElementById('debtor-time') as HTMLInputElement).value;
                  if (!debtorName || !amountVal || Number(amountVal) <= 0) return;
                  setDebtorEntries(prev => [...prev, { debtorName, amount: Number(amountVal), vehicleNumber, time }]);
                  setCredit(prev => prev + Number(amountVal));
                  (document.getElementById('debtor-name') as HTMLInputElement).value = '';
                  (document.getElementById('debtor-amount') as HTMLInputElement).value = '';
                  (document.getElementById('debtor-vehicle') as HTMLInputElement).value = '';
                }}
              >
                + Add Debtor Entry
              </button>
            </div>

            {debtorEntries.length > 0 && (
              <div className="debtor-entry-list" style={{ marginBottom: '16px' }}>
                {debtorEntries.map((item, idx) => (
                  <div key={idx} style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', background: '#0f172a', padding: '8px 12px', borderRadius: '4px', marginBottom: '4px', fontSize: '0.85rem' }}>
                    <div>
                      <strong>{item.debtorName}</strong>: ₹{item.amount.toFixed(2)} <br />
                      <span style={{ color: '#94a3b8' }}>Veh: {item.vehicleNumber || 'N/A'} | Time: {item.time}</span>
                    </div>
                    <button
                      type="button"
                      style={{ color: '#ef4444', background: 'none', border: 'none', cursor: 'pointer', fontWeight: 'bold' }}
                      onClick={() => {
                        setDebtorEntries(prev => prev.filter((_, i) => i !== idx));
                        setCredit(prev => Math.max(0, prev - item.amount));
                      }}
                    >
                      Delete
                    </button>
                  </div>
                ))}
              </div>
            )}

            {/* DSM Personal Debtors Section */}
            <h2 className="section-heading" style={{ marginTop: '1.5rem' }}>DSM Personal Debtors</h2>
            <div className="personal-debtor-form" style={{ background: '#1e293b', padding: '12px', borderRadius: '8px', marginBottom: '12px' }}>
              <div className="field-row-2">
                <div className="field-group">
                  <label className="field-label">Fuel Product</label>
                  <select id="pdebt-product" className="field-input" defaultValue="MS-II">
                    <option value="MS-I">MS-I</option>
                    <option value="MS-II">MS-II</option>
                    <option value="HSD">HSD</option>
                  </select>
                </div>
                <div className="field-group">
                  <label className="field-label">Amount (₹)</label>
                  <input id="pdebt-amount" type="number" step="0.01" className="field-input" placeholder="0.00" />
                </div>
              </div>
              <div className="field-row-2" style={{ marginTop: '8px' }}>
                <div className="field-group">
                  <label className="field-label">Remarks</label>
                  <input id="pdebt-remarks" type="text" className="field-input" placeholder="Remarks" />
                </div>
                <div className="field-group">
                  <label className="field-label">Payment Mode</label>
                  <select
                    id="pdebt-mode"
                    className="field-input"
                    defaultValue="Cash"
                    onChange={(e) => {
                      const mode = e.target.value;
                      const cardDiv = document.getElementById('pdebt-card-fields');
                      const cashDiv = document.getElementById('pdebt-cash-fields');
                      if (cardDiv) cardDiv.style.display = (mode !== 'Cash') ? 'flex' : 'none';
                      if (cashDiv) cashDiv.style.display = (mode === 'Cash') ? 'block' : 'none';
                    }}
                  >
                    <option value="Cash">Cash</option>
                    <option value="PhonePe">PhonePe</option>
                    <option value="PetroCard">PetroCard</option>
                    <option value="Others">Others</option>
                  </select>
                </div>
              </div>

              {/* Conditional Card Fields */}
              <div id="pdebt-card-fields" className="field-row-2" style={{ marginTop: '8px', display: 'none' }}>
                <div className="field-group">
                  <label className="field-label">TID</label>
                  <input id="pdebt-tid" type="text" className="field-input" placeholder="TID" />
                </div>
                <div className="field-group">
                  <label className="field-label">Batch No.</label>
                  <input id="pdebt-batch" type="text" className="field-input" placeholder="Batch" />
                </div>
              </div>

              {/* Conditional Cash Fields */}
              <div id="pdebt-cash-fields" style={{ marginTop: '8px', display: 'block' }}>
                <label className="field-label">Cash Denominations</label>
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: '6px' }}>
                  <input id="pdebt-d500" type="number" className="field-input" placeholder="500x" style={{ padding: '4px' }} />
                  <input id="pdebt-d200" type="number" className="field-input" placeholder="200x" style={{ padding: '4px' }} />
                  <input id="pdebt-d100" type="number" className="field-input" placeholder="100x" style={{ padding: '4px' }} />
                  <input id="pdebt-d50" type="number" className="field-input" placeholder="50x" style={{ padding: '4px' }} />
                  <input id="pdebt-d20" type="number" className="field-input" placeholder="20x" style={{ padding: '4px' }} />
                  <input id="pdebt-d10" type="number" className="field-input" placeholder="10x" style={{ padding: '4px' }} />
                  <input id="pdebt-coins" type="number" className="field-input" placeholder="Coins" style={{ padding: '4px', gridColumn: 'span 2' }} />
                </div>
              </div>

              <button
                type="button"
                className="btn-outline"
                style={{ marginTop: '12px', width: '100%', padding: '8px' }}
                onClick={() => {
                  const fuelProduct = (document.getElementById('pdebt-product') as HTMLSelectElement).value;
                  const amountVal = (document.getElementById('pdebt-amount') as HTMLInputElement).value;
                  const remarks = (document.getElementById('pdebt-remarks') as HTMLInputElement).value;
                  const paymentMethod = (document.getElementById('pdebt-mode') as HTMLSelectElement).value;
                  
                  if (!amountVal || Number(amountVal) <= 0) return;
                  
                  let entry: any = {
                    amount: Number(amountVal),
                    fuelProduct,
                    remarks,
                    paymentMethod
                  };

                  if (paymentMethod === 'Cash') {
                    entry.denom500 = Number((document.getElementById('pdebt-d500') as HTMLInputElement).value) || 0;
                    entry.denom200 = Number((document.getElementById('pdebt-d200') as HTMLInputElement).value) || 0;
                    entry.denom100 = Number((document.getElementById('pdebt-d100') as HTMLInputElement).value) || 0;
                    entry.denom50 = Number((document.getElementById('pdebt-d50') as HTMLInputElement).value) || 0;
                    entry.denom20 = Number((document.getElementById('pdebt-d20') as HTMLInputElement).value) || 0;
                    entry.denom10 = Number((document.getElementById('pdebt-d10') as HTMLInputElement).value) || 0;
                    entry.coins = Number((document.getElementById('pdebt-coins') as HTMLInputElement).value) || 0;
                  } else {
                    entry.tid = (document.getElementById('pdebt-tid') as HTMLInputElement).value;
                    entry.batch = (document.getElementById('pdebt-batch') as HTMLInputElement).value;
                  }

                  setPersonalDebtors(prev => [...prev, entry]);
                  
                  // Clear form
                  (document.getElementById('pdebt-amount') as HTMLInputElement).value = '';
                  (document.getElementById('pdebt-remarks') as HTMLInputElement).value = '';
                  if (paymentMethod === 'Cash') {
                    (document.getElementById('pdebt-d500') as HTMLInputElement).value = '';
                    (document.getElementById('pdebt-d200') as HTMLInputElement).value = '';
                    (document.getElementById('pdebt-d100') as HTMLInputElement).value = '';
                    (document.getElementById('pdebt-d50') as HTMLInputElement).value = '';
                    (document.getElementById('pdebt-d20') as HTMLInputElement).value = '';
                    (document.getElementById('pdebt-d10') as HTMLInputElement).value = '';
                    (document.getElementById('pdebt-coins') as HTMLInputElement).value = '';
                  } else {
                    (document.getElementById('pdebt-tid') as HTMLInputElement).value = '';
                    (document.getElementById('pdebt-batch') as HTMLInputElement).value = '';
                  }
                }}
              >
                + Add Personal Debtor
              </button>
            </div>

            {personalDebtors.length > 0 && (
              <div className="personal-debtor-list" style={{ marginBottom: '16px' }}>
                {personalDebtors.map((item, idx) => (
                  <div key={idx} style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', background: '#0f172a', padding: '8px 12px', borderRadius: '4px', marginBottom: '4px', fontSize: '0.85rem' }}>
                    <div>
                      <strong>Personal Debtor #{idx}</strong>: ₹{item.amount.toFixed(2)} ({item.fuelProduct}) <br />
                      <span style={{ color: '#94a3b8' }}>Mode: {item.paymentMethod} | Remarks: {item.remarks || 'None'}</span>
                    </div>
                    <button
                      type="button"
                      style={{ color: '#ef4444', background: 'none', border: 'none', cursor: 'pointer', fontWeight: 'bold' }}
                      onClick={() => {
                        setPersonalDebtors(prev => prev.filter((_, i) => i !== idx));
                      }}
                    >
                      Delete
                    </button>
                  </div>
                ))}
              </div>
            )}

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
                <div key={r.rowId} className="review-row">
                  <span>Nozzle {r.nozzleId} ({r.fuelType})</span>
                  <strong>
                    {Math.max(0, r.closingReading - r.openingReading).toFixed(2)}L
                    {' '}= ₹{(Math.max(0, r.closingReading - r.openingReading) * r.rate).toLocaleString('en-IN', { minimumFractionDigits: 2 })}
                  </strong>
                </div>
              ))}
              <div className="review-row review-total">
                <span>Gross Sales</span><strong>₹{grossSales.toLocaleString('en-IN', { minimumFractionDigits: 2 })}</strong>
              </div>
            </div>

            <div className="review-block">
              <p className="review-block-title">Collections</p>
              {cash > 0 && <div className="review-row"><span>Cash</span><strong>₹{cash.toLocaleString('en-IN')}</strong></div>}
              {upi > 0 && <div className="review-row"><span>UPI</span><strong>₹{upi.toLocaleString('en-IN')}</strong></div>}
              {card > 0 && <div className="review-row"><span>Card</span><strong>₹{card.toLocaleString('en-IN')}</strong></div>}
              {credit > 0 && <div className="review-row"><span>Credit</span><strong>₹{credit.toLocaleString('en-IN')}</strong></div>}
              {expense > 0 && <div className="review-row"><span>Expense</span><strong>₹{expense.toLocaleString('en-IN')}</strong></div>}
              <div className={`review-row review-total ${Math.abs(mismatch) > 500 ? 'review-warn' : ''}`}>
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

            {personalDebtors.length > 0 && (
              <div className="review-block">
                <p className="review-block-title">DSM Personal Debtors</p>
                {personalDebtors.map((item, idx) => (
                  <div key={idx} className="review-row">
                    <span>Personal Debtor #{idx} ({item.fuelProduct}, {item.paymentMethod})</span>
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
