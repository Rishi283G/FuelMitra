import React, { createContext, useContext, useEffect, useState } from 'react';
import { supabase, type DsmUserProfile } from '../lib/supabase';
import type { User } from '@supabase/supabase-js';

interface AuthContextType {
  user: User | null;
  profile: DsmUserProfile | null;
  loading: boolean;
  login: (email: string, password: string) => Promise<string | null>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthContextType | null>(null);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [profile, setProfile] = useState<DsmUserProfile | null>(null);
  const [loading, setLoading] = useState(true);

  async function fetchProfile(authUserId: string): Promise<DsmUserProfile | null> {
    try {
      const { data, error } = await supabase
        .from('DsmUsers')
        .select('SyncGuid, EmployeeCode, FullName, MobileNumber, station_id, DsmPumpAssignments(PumpId, ShiftType, IsActive)')
        .eq('AuthUserId', authUserId)
        .eq('IsActive', true)
        .single();

      if (error || !data) return null;

      const activeAssignment = (data.DsmPumpAssignments as any[])?.find(
        (a: any) => a.IsActive === true
      );

      return {
        id: data.SyncGuid,
        AuthUserId: authUserId,
        EmployeeCode: data.EmployeeCode,
        FullName: data.FullName,
        MobileNumber: data.MobileNumber,
        StationId: data.station_id,
        AssignedPump: activeAssignment ? activeAssignment.PumpId : null,
        AssignedShift: activeAssignment ? activeAssignment.ShiftType : null,
      };
    } catch {
      return null;
    }
  }

  useEffect(() => {
    supabase.auth.getSession().then(async ({ data: { session } }) => {
      const u = session?.user ?? null;
      setUser(u);
      if (u) {
        const p = await fetchProfile(u.id);
        setProfile(p);
      }
      setLoading(false);
    });

    const { data: { subscription } } = supabase.auth.onAuthStateChange(async (_event, session) => {
      const u = session?.user ?? null;
      setUser(u);
      if (u) {
        const p = await fetchProfile(u.id);
        setProfile(p);
      } else {
        setProfile(null);
      }
    });

    return () => subscription.unsubscribe();
  }, []);

  async function login(email: string, password: string): Promise<string | null> {
    const { error } = await supabase.auth.signInWithPassword({ email, password });
    if (error) return error.message;
    return null;
  }

  async function logout(): Promise<void> {
    await supabase.auth.signOut();
  }

  return (
    <AuthContext.Provider value={{ user, profile, loading, login, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
