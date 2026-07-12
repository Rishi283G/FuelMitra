import { useState } from 'react';
import { AuthProvider, useAuth } from './contexts/AuthContext';
import LoginScreen from './screens/LoginScreen';
import DashboardScreen from './screens/DashboardScreen';
import SubmitShiftScreen from './screens/SubmitShiftScreen';
import HistoryScreen from './screens/HistoryScreen';
import { Loader2 } from 'lucide-react';

type Screen = 'dashboard' | 'submit' | 'history';

function AppContent() {
  const { user, loading, isResetting } = useAuth();
  const [screen, setScreen] = useState<Screen>('dashboard');

  if (loading || isResetting) {
    return (
      <div className="splash">
        <Loader2 size={40} className="spin" />
        <p>{isResetting ? "Switching station. Resetting local database..." : "Loading PyroSync DSM..."}</p>
      </div>
    );
  }

  if (!user) {
    return <LoginScreen />;
  }

  if (screen === 'submit') {
    return <SubmitShiftScreen onBack={() => setScreen('dashboard')} />;
  }

  if (screen === 'history') {
    return <HistoryScreen onBack={() => setScreen('dashboard')} />;
  }

  return (
    <DashboardScreen
      onNavigate={(s) => setScreen(s)}
    />
  );
}

export default function App() {
  return (
    <AuthProvider>
      <AppContent />
    </AuthProvider>
  );
}
