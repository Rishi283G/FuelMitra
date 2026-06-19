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
}

export default function HistoryScreen({ onBack }: HistoryProps) {
  const { profile } = useAuth();
  const { fetchSubmissionHistory } = useSubmissionService();
  const [entries, setEntries] = useState<HistoryEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [filterStatus, setFilterStatus] = useState<'All' | 'Pending' | 'Approved' | 'Rejected'>('All');

  async function loadHistory() {
    if (!profile) return;
    setLoading(true);
    setError('');
    try {
      const data = await fetchSubmissionHistory(profile.id);
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
          >
            <div className="history-card-top">
              <div>
                <p className="history-card-title">
                  Pump {entry.PumpId} · Shift {entry.ShiftType}
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
          </div>
        ))}
      </main>
    </div>
  );
}
