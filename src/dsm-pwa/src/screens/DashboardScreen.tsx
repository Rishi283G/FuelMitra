import { useState, useEffect } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { supabase } from '../lib/supabase';
import type { DsmNotification } from '../lib/supabase';
import {
  Flame, LogOut, PlusCircle, History, Bell, BellDot,
  CheckCircle2, XCircle, Clock, ChevronRight, WifiOff,
  RotateCw, X, Loader2
} from 'lucide-react';
import { useSubmissionService } from '../hooks/useSubmissionService';

interface DashboardProps {
  onNavigate: (screen: 'submit' | 'history') => void;
}

export default function DashboardScreen({ onNavigate }: DashboardProps) {
  const { profile, logout, refreshProfile } = useAuth();
  const { fetchSubmissionDetails } = useSubmissionService();
  const [notifications, setNotifications] = useState<DsmNotification[]>([]);
  const [showNotifs, setShowNotifs] = useState(false);
  const [recentStatus, setRecentStatus] = useState<{ id: string; status: string; shiftDate: string; shiftType: string } | null>(null);
  const [online, setOnline] = useState(navigator.onLine);

  const [isRefreshing, setIsRefreshing] = useState(false);
  const [showLastDetails, setShowLastDetails] = useState(false);
  const [lastDetails, setLastDetails] = useState<any>(null);
  const [lastDetailsLoading, setLastDetailsLoading] = useState(false);

  const handleRefresh = async () => {
    try {
      setIsRefreshing(true);
      await refreshProfile();
    } catch (e) {
      console.error(e);
    } finally {
      setIsRefreshing(false);
    }
  };

  const handleViewLastDetails = async () => {
    if (!recentStatus?.id) return;
    setShowLastDetails(true);
    setLastDetailsLoading(true);
    try {
      const data = await fetchSubmissionDetails(recentStatus.id);
      setLastDetails(data);
    } catch (e) {
      console.error('Failed to load last submission details:', e);
    } finally {
      setLastDetailsLoading(false);
    }
  };

  useEffect(() => {
    const handleOnline = () => setOnline(true);
    const handleOffline = () => setOnline(false);
    window.addEventListener('online', handleOnline);
    window.addEventListener('offline', handleOffline);
    return () => {
      window.removeEventListener('online', handleOnline);
      window.removeEventListener('offline', handleOffline);
    };
  }, []);

  useEffect(() => {
    if (!profile) return;

    // Fetch unread notifications
    supabase
      .from('DsmNotifications')
      .select('Id, Message, IsRead, CreatedAt')
      .eq('UserId', profile.id)
      .eq('IsRead', false)
      .order('CreatedAt', { ascending: false })
      .limit(10)
      .then(({ data }) => {
        if (data) setNotifications(data as DsmNotification[]);
      });

    // Fetch latest submission status
    supabase
      .from('DsmSubmissions')
      .select('Id, Status, ShiftDate, ShiftType')
      .eq('DsmUserId', profile.id)
      .order('SubmittedAt', { ascending: false })
      .limit(1)
      .single()
      .then(({ data }) => {
        if (data) {
          setRecentStatus({
            id: data.Id,
            status: data.Status,
            shiftDate: data.ShiftDate,
            shiftType: data.ShiftType
          });
        }
      });

    // Real-time subscription for notifications
    const channel = supabase
      .channel('dsm-notifications')
      .on('postgres_changes', {
        event: 'INSERT',
        schema: 'public',
        table: 'DsmNotifications',
        filter: `UserId=eq.${profile.id}`,
      }, payload => {
        setNotifications(prev => [payload.new as DsmNotification, ...prev]);
      })
      .subscribe();

    return () => { supabase.removeChannel(channel); };
  }, [profile]);

  async function markAllRead() {
    if (!profile) return;
    await supabase.from('DsmNotifications').update({ IsRead: true }).eq('UserId', profile.id);
    setNotifications([]);
    setShowNotifs(false);
  }

  const unreadCount = notifications.length;
  const today = new Date().toLocaleDateString('en-IN', { weekday: 'long', day: 'numeric', month: 'long' });
  const hour = new Date().getHours();
  const greeting = hour < 12 ? 'Good morning' : hour < 17 ? 'Good afternoon' : 'Good evening';

  function statusIcon(status: string) {
    if (status === 'Approved') return <CheckCircle2 size={16} className="status-icon approved" />;
    if (status === 'Rejected') return <XCircle size={16} className="status-icon rejected" />;
    return <Clock size={16} className="status-icon pending" />;
  }

  return (
    <div className="screen dashboard-screen">
      {/* Header */}
      <header className="app-header">
        <div className="header-brand">
          <Flame size={22} className="brand-icon" />
          <span className="brand-name">PyroSync</span>
        </div>
        <div className="header-actions">
          {!online && (
            <div className="offline-badge" title="You are offline">
              <WifiOff size={16} />
            </div>
          )}
          <button
            id="refresh-btn"
            className="icon-btn"
            onClick={handleRefresh}
            aria-label="Refresh assignment"
            disabled={isRefreshing}
          >
            <RotateCw size={20} className={isRefreshing ? 'spin' : ''} />
          </button>
          <button
            id="notif-btn"
            className="icon-btn"
            onClick={() => setShowNotifs(v => !v)}
            aria-label="Notifications"
          >
            {unreadCount > 0 ? <BellDot size={22} className="notif-active" /> : <Bell size={22} />}
            {unreadCount > 0 && <span className="notif-badge">{unreadCount}</span>}
          </button>
          <button id="logout-btn" className="icon-btn" onClick={logout} aria-label="Sign out">
            <LogOut size={20} />
          </button>
        </div>
      </header>

      {/* Notification Drawer */}
      {showNotifs && (
        <div className="notif-drawer">
          <div className="notif-drawer-header">
            <span>Notifications</span>
            {unreadCount > 0 && (
              <button className="btn-text-sm" onClick={markAllRead}>Mark all read</button>
            )}
          </div>
          {notifications.length === 0 ? (
            <div className="notif-empty">No new notifications</div>
          ) : (
            notifications.map(n => (
              <div key={n.Id} className="notif-item">
                <p className="notif-msg">{n.Message}</p>
                <span className="notif-time">
                  {new Date(n.CreatedAt).toLocaleDateString('en-IN', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' })}
                </span>
              </div>
            ))
          )}
        </div>
      )}

      <main className="dashboard-main">
        {/* Greeting Card */}
        <div className="greeting-card">
          <div className="greeting-wave">👋</div>
          <div>
            <h1 className="greeting-title">{greeting}, {profile?.FullName?.split(' ')[0] ?? 'DSM'}!</h1>
            <p className="greeting-date">{today}</p>
            <p className="greeting-station">
              Station ID: {profile?.StationId}
              {profile?.AssignedPump ? ` · Pump ${profile.AssignedPump} (Shift ${profile.AssignedShift})` : ' · No Active Assignment'}
            </p>
          </div>
        </div>

        {/* Latest Status Card */}
        {recentStatus && (
          <div className="status-card" onClick={handleViewLastDetails} style={{ cursor: 'pointer' }}>
            <div className="status-card-row">
              {statusIcon(recentStatus.status)}
              <div style={{ flex: 1 }}>
                <p className="status-label">Last submission</p>
                <p className="status-value">
                  {new Date(recentStatus.shiftDate).toLocaleDateString('en-IN', { day: 'numeric', month: 'short' })}
                  {' '}· Shift {recentStatus.shiftType}
                  {' '}· <span className={`status-text status-${recentStatus.status.toLowerCase()}`}>{recentStatus.status}</span>
                </p>
              </div>
              <ChevronRight size={16} style={{ color: 'var(--text-muted)' }} />
            </div>
          </div>
        )}

        {/* Main Actions */}
        <div className="action-grid">
          <button
            id="new-submission-btn"
            className="action-card action-card--primary"
            onClick={() => {
              if (!profile?.AssignedPump) {
                alert('No active pump assignment. Please ask your manager to assign you a pump first.');
                return;
              }
              onNavigate('submit');
            }}
            disabled={!profile?.AssignedPump}
            style={!profile?.AssignedPump ? { opacity: 0.6, cursor: 'not-allowed' } : {}}
          >
            <div className="action-card-icon">
              <PlusCircle size={32} />
            </div>
            <div>
              <p className="action-card-title">New Shift Entry</p>
              <p className="action-card-desc">
                {profile?.AssignedPump 
                  ? `Submit meter readings & collections for Pump ${profile.AssignedPump}` 
                  : 'Requires active pump assignment'}
              </p>
            </div>
            <ChevronRight size={20} className="action-card-arrow" />
          </button>

          <button
            id="history-btn"
            className="action-card action-card--secondary"
            onClick={() => onNavigate('history')}
          >
            <div className="action-card-icon">
              <History size={32} />
            </div>
            <div>
              <p className="action-card-title">Submission History</p>
              <p className="action-card-desc">View approvals, rejections &amp; past entries</p>
            </div>
            <ChevronRight size={20} className="action-card-arrow" />
          </button>
        </div>

        {/* Pump Assignments */}
        <div className="pumps-section">
          <h2 className="section-title">Your Assignment</h2>
          {profile?.AssignedPump ? (
            <div className="pump-chips">
              <div className="pump-chip">
                <Flame size={14} />
                Pump {profile.AssignedPump} (Shift {profile.AssignedShift})
              </div>
            </div>
          ) : (
            <p style={{ fontSize: '0.875rem', color: '#e53e3e', fontWeight: 'bold' }}>
              ⚠️ No active assignment. Ask your manager to assign you a pump &amp; shift from the admin console.
            </p>
          )}
        </div>
      </main>

      {/* Previous Sheet Details Modal */}
      {showLastDetails && (
        <div className="modal-overlay" onClick={() => setShowLastDetails(false)}>
          <div className="modal-content" onClick={e => e.stopPropagation()}>
            <div className="modal-header">
              <span className="modal-title">Previous Sheet Details</span>
              <button className="icon-btn" onClick={() => setShowLastDetails(false)} style={{ width: '32px', height: '32px' }}>
                <X size={18} />
              </button>
            </div>
            
            {lastDetailsLoading ? (
              <div style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '24px 0', justifyContent: 'center', color: '#94a3b8' }}>
                <Loader2 size={24} className="spin" />
                <span>Loading details...</span>
              </div>
            ) : lastDetails ? (
              <div>
                {/* Nozzle Readings */}
                <div style={{ marginBottom: '16px' }}>
                  <p style={{ fontWeight: 'bold', fontSize: '0.85rem', color: '#f8fafc', marginBottom: '8px' }}>Nozzle Readings</p>
                  {lastDetails.readings.map((r: any, idx: number) => {
                    const testingLtr = lastDetails.submission.Metadata?.testingEntries?.find((t: any) => t.nozzleId === r.NozzleId)?.amount || 0;
                    return (
                      <div key={idx} style={{ display: 'flex', flexDirection: 'column', fontSize: '0.8rem', color: '#94a3b8', marginBottom: '6px', background: '#0f172a', padding: '8px', borderRadius: '6px' }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', color: '#e2e8f0', fontWeight: '500' }}>
                          <span>Nozzle {r.NozzleId} ({r.FuelType || 'Fuel'})</span>
                          <span>{(r.ClosingReading - r.OpeningReading).toFixed(2)} L (₹{((r.ClosingReading - r.OpeningReading) * r.Rate).toFixed(2)})</span>
                        </div>
                        <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.75rem', marginTop: '4px' }}>
                          <span>Op: {r.OpeningReading} | Cl: {r.ClosingReading}</span>
                          {testingLtr > 0 && <span style={{ color: '#fb923c' }}>Testing: {testingLtr} L</span>}
                        </div>
                      </div>
                    );
                  })}
                </div>

                {/* Collections */}
                <div style={{ marginBottom: '16px' }}>
                  <p style={{ fontWeight: 'bold', fontSize: '0.85rem', color: '#f8fafc', marginBottom: '8px' }}>Collections &amp; Adjustments</p>
                  <div style={{ display: 'flex', flexDirection: 'column', gap: '6px', fontSize: '0.8rem', color: '#94a3b8' }}>
                    {lastDetails.collection?.Cash > 0 && (
                      <div style={{ background: '#0f172a', padding: '8px', borderRadius: '6px' }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', color: '#e2e8f0' }}>
                          <span>Cash Total:</span>
                          <strong>₹{lastDetails.collection.Cash.toLocaleString('en-IN')}</strong>
                        </div>
                        {lastDetails.submission.Metadata?.cashDenominations && (
                          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', fontSize: '0.75rem', color: '#64748b', marginTop: '4px', paddingLeft: '6px', borderLeft: '2px solid #334155' }}>
                            {Object.entries(lastDetails.submission.Metadata.cashDenominations as Record<string, number>).map(([k, v]) => {
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
                    {lastDetails.collection?.UPI > 0 && (
                      <div style={{ display: 'flex', justifyContent: 'space-between', background: '#0f172a', padding: '8px', borderRadius: '6px' }}>
                        <span>UPI (PhonePe):</span>
                        <span style={{ color: '#e2e8f0' }}>₹{lastDetails.collection.UPI.toLocaleString('en-IN')}</span>
                      </div>
                    )}
                    {lastDetails.collection?.Card > 0 && (
                      <div style={{ display: 'flex', justifyContent: 'space-between', background: '#0f172a', padding: '8px', borderRadius: '6px' }}>
                        <span>Card/Swipe:</span>
                        <span style={{ color: '#e2e8f0' }}>₹{lastDetails.collection.Card.toLocaleString('en-IN')}</span>
                      </div>
                    )}
                    {lastDetails.collection?.Credit > 0 && (
                      <div style={{ display: 'flex', justifyContent: 'space-between', background: '#0f172a', padding: '8px', borderRadius: '6px' }}>
                        <span>Debtors (Credit):</span>
                        <span style={{ color: '#e2e8f0' }}>₹{lastDetails.collection.Credit.toLocaleString('en-IN')}</span>
                      </div>
                    )}
                    {lastDetails.collection?.Expense > 0 && (
                      <div style={{ background: '#0f172a', padding: '8px', borderRadius: '6px' }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                          <span>Expense:</span>
                          <span style={{ color: '#f87171' }}>₹{lastDetails.collection.Expense.toLocaleString('en-IN')}</span>
                        </div>
                        {lastDetails.collection.ExpenseNotes && (
                          <div style={{ fontSize: '0.75rem', color: '#64748b', marginTop: '4px' }}>
                            Note: {lastDetails.collection.ExpenseNotes}
                          </div>
                        )}
                      </div>
                    )}
                  </div>
                </div>

                {/* Debtors List */}
                {lastDetails.submission.Metadata?.debtorEntries?.length > 0 && (
                  <div style={{ marginBottom: '16px' }}>
                    <p style={{ fontWeight: 'bold', fontSize: '0.85rem', color: '#f8fafc', marginBottom: '8px' }}>Debtor Logs</p>
                    {lastDetails.submission.Metadata.debtorEntries.map((d: any, idx: number) => (
                      <div key={idx} style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.8rem', color: '#94a3b8', background: '#0f172a', padding: '8px', borderRadius: '6px', marginBottom: '4px' }}>
                        <span>{d.debtorName} {d.vehicleNumber ? `(${d.vehicleNumber})` : ''}</span>
                        <span style={{ color: '#e2e8f0' }}>₹{d.amount.toFixed(2)}</span>
                      </div>
                    ))}
                  </div>
                )}

                {/* General Notes */}
                {lastDetails.submission.Notes && (
                  <div style={{ background: '#0f172a', padding: '8px', borderRadius: '6px', fontSize: '0.8rem', color: '#94a3b8' }}>
                    <strong style={{ color: '#e2e8f0' }}>Notes:</strong> {lastDetails.submission.Notes}
                  </div>
                )}
              </div>
            ) : (
              <div style={{ color: '#ef4444', fontSize: '0.8rem', textAlign: 'center', padding: '16px 0' }}>Failed to load details.</div>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
