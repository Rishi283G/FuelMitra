import { useState } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { Flame, Eye, EyeOff, AlertCircle } from 'lucide-react';

export default function LoginScreen() {
  const { login } = useAuth();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [showPw, setShowPw] = useState(false);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError('');
    setLoading(true);
    const err = await login(email.trim(), password);
    setLoading(false);
    if (err) setError(err);
  }

  return (
    <div className="login-container">
      {/* Background blobs */}
      <div className="blob blob-1" />
      <div className="blob blob-2" />

      <div className="login-card">
        {/* Logo */}
        <div className="login-logo">
          <div className="logo-icon">
            <Flame size={28} strokeWidth={2.5} />
          </div>
          <div className="logo-text">
            <span className="logo-brand">PyroSync</span>
            <span className="logo-sub">DSM Portal</span>
          </div>
        </div>

        <h1 className="login-title">Welcome back</h1>
        <p className="login-subtitle">Sign in to submit your shift data</p>

        {error && (
          <div className="alert-error" role="alert">
            <AlertCircle size={16} />
            <span>{error}</span>
          </div>
        )}

        <form onSubmit={handleSubmit} className="login-form" id="login-form">
          <div className="field-group">
            <label htmlFor="login-email" className="field-label">Email address</label>
            <input
              id="login-email"
              type="email"
              className="field-input"
              value={email}
              onChange={e => setEmail(e.target.value)}
              placeholder="you@station.com"
              required
              autoComplete="username"
            />
          </div>

          <div className="field-group">
            <label htmlFor="login-password" className="field-label">Password</label>
            <div className="pw-wrapper">
              <input
                id="login-password"
                type={showPw ? 'text' : 'password'}
                className="field-input"
                value={password}
                onChange={e => setPassword(e.target.value)}
                placeholder="••••••••"
                required
                autoComplete="current-password"
              />
              <button
                type="button"
                className="pw-toggle"
                onClick={() => setShowPw(v => !v)}
                aria-label={showPw ? 'Hide password' : 'Show password'}
              >
                {showPw ? <EyeOff size={18} /> : <Eye size={18} />}
              </button>
            </div>
          </div>

          <button
            id="login-submit"
            type="submit"
            className={`btn-primary ${loading ? 'btn-loading' : ''}`}
            disabled={loading}
          >
            {loading ? (
              <span className="spinner" />
            ) : (
              'Sign in'
            )}
          </button>
        </form>

        <p className="login-footer">
          Account issues? Contact your Station Manager.
        </p>
      </div>
    </div>
  );
}
