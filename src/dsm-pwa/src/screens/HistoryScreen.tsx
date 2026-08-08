import { useState, useEffect } from 'react';
import type { ReactNode } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { useSubmissionService } from '../hooks/useSubmissionService';
import { ArrowLeft, CheckCircle2, XCircle, Clock, RefreshCw, Loader2 } from 'lucide-react';

interface HistoryProps {
  onBack: () => void;
}

interface HistoryEntry {
  Id: string;
  PumpId: number;
  ShiftDate: string;
  ShiftType: string;
  Status: 'Pending' | 'Approved' | 'Rejected' | 'Expired';
  SubmittedAt: string;
  RejectionReason?: string;
  ApprovedAt?: string;
  Metadata?: {
    connectedPumpId?: number | null;
  } | null;
}


export default function HistoryScreen({ onBack }: HistoryProps) {
  const { profile } = useAuth();
  const { fetchSubmissionHistory, fetchSubmissionDetails } = useSubmissionService();
  const [entries, setEntries] = useState<HistoryEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [filterStatus, setFilterStatus] = useState<'All' | 'Pending' | 'Approved' | 'Rejected'>('All');
  
  // Expansion state
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [details, setDetails] = useState<any>(null);
  const [detailsLoading, setDetailsLoading] = useState(false);

  const toggleExpand = async (id: string) => {
    if (expandedId === id) {
      setExpandedId(null);
      setDetails(null);
      return;
    }
    setExpandedId(id);
    setDetails(null);
    setDetailsLoading(true);
    try {
      const data = await fetchSubmissionDetails(id);
      setDetails(data);
    } catch (e) {
      console.error('Failed to load submission details:', e);
    } finally {
      setDetailsLoading(false);
    }
  };

  async function loadHistory() {
    if (!profile) return;
    setLoading(true);
    setError('');
    try {
      const data = await fetchSubmissionHistory(profile.id, profile.AuthUserId);
      setEntries(data as HistoryEntry[]);
    } catch (err: unknown) {
      if (err instanceof Error) setError(err.message);
      else setError('Failed to load history');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    loadHistory();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [profile]);

  const filtered = filterStatus === 'All' ? entries : entries.filter(e => e.Status === filterStatus);

  function statusBadge(status: string) {
    const map: Record<string, { icon: ReactNode; cls: string }> = {
      Approved: { icon: <CheckCircle2 size={14} />, cls: 'badge-approved' },
      Rejected: { icon: <XCircle size={14} />, cls: 'badge-rejected' },
      Pending: { icon: <Clock size={14} />, cls: 'badge-pending' },
      Expired: { icon: <XCircle size={14} />, cls: 'badge-expired' },
    };
    const s = map[status] ?? map['Pending'];
    return (
      <span className={`status-badge ${s.cls}`}>
        {s.icon} {status}
      </span>
    );
  }

  return (
    <div className="screen history-screen">
      <header className="app-header">
        <button className="icon-btn" onClick={onBack} aria-label="Back">
          <ArrowLeft size={22} />
        </button>
        <span className="header-title">Submission History</span>
        <button className="icon-btn" onClick={loadHistory} aria-label="Refresh" disabled={loading}>
          {loading ? <Loader2 size={20} className="spin" /> : <RefreshCw size={20} />}
        </button>
      </header>

      {/* Filter Chips */}
      <div className="filter-chips">
        {(['All', 'Pending', 'Approved', 'Rejected'] as const).map(f => (
          <button
            key={f}
            id={`filter-${f.toLowerCase()}`}
            className={`filter-chip ${filterStatus === f ? 'active' : ''}`}
            onClick={() => setFilterStatus(f)}
          >
            {f}
          </button>
        ))}
      </div>

      <main className="history-main">
        {loading && (
          <div className="empty-state">
            <Loader2 size={32} className="spin" />
            <p>Loading history...</p>
          </div>
        )}

        {!loading && error && (
          <div className="alert-error" role="alert">
            <XCircle size={16} />
            <span>{error}</span>
          </div>
        )}

        {!loading && !error && filtered.length === 0 && (
          <div className="empty-state">
            <Clock size={40} className="empty-icon" />
            <p>No submissions found</p>
            {filterStatus !== 'All' && (
              <button className="btn-text-sm" onClick={() => setFilterStatus('All')}>
                Show all entries
              </button>
            )}
          </div>
        )}

        {!loading && filtered.map(entry => (
          <div
            key={entry.Id}
            id={`history-entry-${entry.Id}`}
            className={`history-card history-card--${entry.Status.toLowerCase()}`}
            onClick={() => toggleExpand(entry.Id)}
            style={{ cursor: 'pointer' }}
          >
            <div className="history-card-top">
              <div>
                <p className="history-card-title">
                  Pump {entry.PumpId}{entry.Metadata?.connectedPumpId ? ` + Pump ${entry.Metadata.connectedPumpId} (Connected)` : ''} · Shift {entry.ShiftType}
                </p>

                <p className="history-card-date">
                  {new Date(entry.ShiftDate).toLocaleDateString('en-IN', {
                    day: 'numeric', month: 'long', year: 'numeric'
                  })}
                </p>
              </div>
              {statusBadge(entry.Status)}
            </div>

            <div className="history-card-meta">
              <span>Submitted: {new Date(entry.SubmittedAt).toLocaleDateString('en-IN', {
                day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit'
              })}</span>
              {entry.ApprovedAt && (
                <span>Approved: {new Date(entry.ApprovedAt).toLocaleDateString('en-IN', {
                  day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit'
                })}</span>
              )}
            </div>

            {entry.Status === 'Rejected' && entry.RejectionReason && (
              <div className="rejection-reason">
                <XCircle size={14} />
                <span>{entry.RejectionReason}</span>
              </div>
            )}

            {expandedId === entry.Id && (
              <div className="history-card-details" onClick={e => e.stopPropagation()} style={{ marginTop: '12px', paddingTop: '12px', borderTop: '1px solid #334155' }}>
                {detailsLoading ? (
                  <div style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '12px', color: '#94a3b8' }}>
                    <Loader2 size={16} className="spin" />
                    <span>Loading submission details...</span>
                  </div>
                ) : details ? (
                  <div>
                    {/* Nozzle Readings */}
                    <div style={{ marginBottom: '12px' }}>
                      <p style={{ fontWeight: 'bold', fontSize: '0.85rem', color: '#f8fafc', marginBottom: '6px' }}>Nozzle Readings</p>
                      {details.readings.map((r: any, idx: number) => {
                        const testingLtr = details.submission.Metadata?.testingEntries?.find((t: any) => t.nozzleId === r.NozzleId)?.amount || 0;
                        return (
                          <div key={idx} style={{ display: 'flex', flexDirection: 'column', fontSize: '0.8rem', color: '#94a3b8', marginBottom: '4px', background: '#0f172a', padding: '6px', borderRadius: '4px' }}>
                            <div style={{ display: 'flex', justifyContent: 'space-between', color: '#e2e8f0', fontWeight: '500' }}>
                              <span>Nozzle {r.NozzleId} ({r.FuelType || 'Fuel'})</span>
                              <span>{(r.ClosingReading - r.OpeningReading).toFixed(2)} L (₹{((r.ClosingReading - r.OpeningReading) * r.Rate).toFixed(2)})</span>
                            </div>
                            <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.75rem', marginTop: '2px' }}>
                              <span>Op: {r.OpeningReading} | Cl: {r.ClosingReading}</span>
                              {testingLtr > 0 && <span style={{ color: '#fb923c' }}>Testing: {testingLtr} L</span>}
                            </div>
                          </div>
                        );
                      })}
                    </div>

                    {/* Collections */}
                    <div style={{ marginBottom: '12px' }}>
                      <p style={{ fontWeight: 'bold', fontSize: '0.85rem', color: '#f8fafc', marginBottom: '6px' }}>Collections &amp; Adjustments</p>
                      <div style={{ display: 'flex', flexDirection: 'column', gap: '4px', fontSize: '0.8rem', color: '#94a3b8' }}>
                        {details.collection?.Cash > 0 && (
                          <div style={{ background: '#0f172a', padding: '6px', borderRadius: '4px' }}>
                            <div style={{ display: 'flex', justifyContent: 'space-between', color: '#e2e8f0' }}>
                              <span>Cash Total:</span>
                              <strong>₹{details.collection.Cash.toLocaleString('en-IN')}</strong>
                            </div>
                            {details.submission.Metadata?.cashDenominations && (
                              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', fontSize: '0.75rem', color: '#64748b', marginTop: '4px', paddingLeft: '6px', borderLeft: '2px solid #334155' }}>
                                {Object.entries(details.submission.Metadata.cashDenominations as Record<string, number>).map(([k, v]) => {
                                  if (!v || v === 0) return null;
                                  return (
                                    <div key={k}>
                                      {k.replace('denom', '₹')} x {String(v)}
                                    </div>
                                  );
                                })}
                              </div>
                            )}
                          </div>
                        )}
                        {details.collection?.UPI > 0 && (
                          <div style={{ display: 'flex', justifyContent: 'space-between', background: '#0f172a', padding: '6px', borderRadius: '4px' }}>
                            <span>UPI (PhonePe):</span>
                            <span style={{ color: '#e2e8f0' }}>₹{details.collection.UPI.toLocaleString('en-IN')}</span>
                          </div>
                        )}
                        {details.collection?.Card > 0 && (
                          <div style={{ display: 'flex', justifyContent: 'space-between', background: '#0f172a', padding: '6px', borderRadius: '4px' }}>
                            <span>Card/Swipe:</span>
                            <span style={{ color: '#e2e8f0' }}>₹{details.collection.Card.toLocaleString('en-IN')}</span>
                          </div>
                        )}
                        {details.collection?.Credit > 0 && (
                          <div style={{ display: 'flex', justifyContent: 'space-between', background: '#0f172a', padding: '6px', borderRadius: '4px' }}>
                            <span>Debtors (Credit):</span>
                            <span style={{ color: '#e2e8f0' }}>₹{details.collection.Credit.toLocaleString('en-IN')}</span>
                          </div>
                        )}
                        {details.collection?.Expense > 0 && (
                          <div style={{ background: '#0f172a', padding: '6px', borderRadius: '4px' }}>
                            <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                              <span>Expense:</span>
                              <span style={{ color: '#f87171' }}>₹{details.collection.Expense.toLocaleString('en-IN')}</span>
                            </div>
                            {details.collection.ExpenseNotes && (
                              <div style={{ fontSize: '0.75rem', color: '#64748b', marginTop: '2px' }}>
                                Note: {details.collection.ExpenseNotes}
                              </div>
                            )}
                          </div>
                        )}
                      </div>
                    </div>

                    {/* Debtors List */}
                    {details.submission.Metadata?.debtorEntries?.length > 0 && (
                      <div style={{ marginBottom: '12px' }}>
                        <p style={{ fontWeight: 'bold', fontSize: '0.85rem', color: '#f8fafc', marginBottom: '6px' }}>Debtor Logs</p>
                        {details.submission.Metadata.debtorEntries.map((d: any, idx: number) => (
                          <div key={idx} style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.8rem', color: '#94a3b8', background: '#0f172a', padding: '6px', borderRadius: '4px', marginBottom: '4px' }}>
                            <span>{d.debtorName} {d.vehicleNumber ? `(${d.vehicleNumber})` : ''}</span>
                            <span style={{ color: '#e2e8f0' }}>₹{d.amount.toFixed(2)}</span>
                          </div>
                        ))}
                      </div>
                    )}

                    {/* General Notes */}
                    {details.submission.Notes && (
                      <div style={{ background: '#0f172a', padding: '8px', borderRadius: '4px', fontSize: '0.8rem', color: '#94a3b8' }}>
                        <strong style={{ color: '#e2e8f0' }}>Notes:</strong> {details.submission.Notes}
                      </div>
                    )}
                  </div>
                ) : (
                  <div style={{ color: '#ef4444', fontSize: '0.8rem' }}>Failed to load details.</div>
                )}
              </div>
            )}
          </div>
        ))}
      </main>
    </div>
  );
}
