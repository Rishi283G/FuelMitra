import React, { createContext, useContext, useEffect, useState } from 'react';
import { supabase, type DsmUserProfile } from '../lib/supabase';
import type { User } from '@supabase/supabase-js';
import { resetStationData } from '../lib/stationReset';

export function getScopedAuthEmail(inputEmailOrUsername: string, stationId: string): string {
  if (!inputEmailOrUsername) return '';

  const trimmedInput = inputEmailOrUsername.trim().toLowerCase();
  const cleanStation = (stationId || '').trim().toLowerCase().replace(/\s+/g, '_');

  if (cleanStation && (trimmedInput.startsWith(`${cleanStation}.`) || trimmedInput.startsWith(`${cleanStation}_`))) {
    return trimmedInput;
  }

  if (cleanStation) {
    if (trimmedInput.includes('@')) {
      const [user, domain] = trimmedInput.split('@');
      return `${cleanStation}.${user}@${domain}`;
    } else {
      return `${cleanStation}.${trimmedInput}@fuelpro.local`;
    }
  }

  if (trimmedInput.includes('@')) {
    return trimmedInput;
  }
  return `${trimmedInput}@fuelpro.local`;
}

interface AuthContextType {
  user: User | null;
  profile: DsmUserProfile | null;
  loading: boolean;
  isResetting: boolean;
  login: (email: string, password: string, stationId?: string) => Promise<string | null>;
  logout: () => Promise<void>;
  refreshProfile: () => Promise<void>;
}

const AuthContext = createContext<AuthContextType | null>(null);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [profile, setProfile] = useState<DsmUserProfile | null>(null);
  const [loading, setLoading] = useState(true);
  const [isResetting, setIsResetting] = useState(false);

  async function fetchProfile(authUserId: string): Promise<DsmUserProfile | null> {
    try {
      const { data, error } = await supabase
        .from('DsmUsers')
        .select('SyncGuid, EmployeeCode, FullName, MobileNumber, station_id, DsmPumpAssignments(PumpId, ConnectedPumpId, ShiftType, AssignedDate, IsActive)')
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
        ConnectedPump: activeAssignment ? activeAssignment.ConnectedPumpId : null,
        AssignedShift: activeAssignment ? activeAssignment.ShiftType : null,
        AssignedDate: activeAssignment ? activeAssignment.AssignedDate : null,
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

  useEffect(() => {
    if (!profile || !profile.StationId) return;

    const cachedStationId = localStorage.getItem('current_station_id');
    if (cachedStationId !== profile.StationId) {
      (async () => {
        setIsResetting(true);
        try {
          await resetStationData(profile.StationId);
        } catch (err) {
          console.error('Failed to reset station data:', err);
        } finally {
          setIsResetting(false);
        }
      })();
    }
  }, [profile]);

  useEffect(() => {
    if (!profile || !profile.id) return;
    const dsmUserId = profile.id;
    const stationId = profile.StationId;

    async function registerDevice() {
      let deviceId = localStorage.getItem('dsm_device_id');
      if (!deviceId) {
        deviceId = typeof crypto.randomUUID === 'function'
          ? crypto.randomUUID()
          : Math.random().toString(36).substring(2) + Date.now().toString(36);
        localStorage.setItem('dsm_device_id', deviceId);
      }

      const userAgent = navigator.userAgent;
      let deviceName = 'PWA Client';
      if (userAgent.indexOf('Mobi') > -1) {
        deviceName = 'Mobile Device';
        if (userAgent.indexOf('iPhone') > -1) deviceName = 'iPhone';
        else if (userAgent.indexOf('Android') > -1) deviceName = 'Android Phone';
      } else {
        deviceName = 'Desktop Browser';
        if (userAgent.indexOf('Macintosh') > -1) deviceName = 'Mac';
        else if (userAgent.indexOf('Windows') > -1) deviceName = 'Windows PC';
      }

      try {
        // Query if device exists in Supabase
        const { data } = await supabase
          .from('DsmDevices')
          .select('SyncGuid')
          .eq('DeviceId', deviceId)
          .eq('DsmUserId', dsmUserId)
          .maybeSingle();

        const now = new Date().toISOString();
        if (data) {
          // Update
          await supabase
            .from('DsmDevices')
            .update({
              DeviceName: deviceName,
              LastSeen: now,
              LastLogin: now
            })
            .eq('DeviceId', deviceId)
            .eq('DsmUserId', dsmUserId);
        } else {
          // Insert
          await supabase
            .from('DsmDevices')
            .insert({
              DsmUserId: dsmUserId,
              DeviceId: deviceId,
              DeviceName: deviceName,
              LastLogin: now,
              LastSeen: now,
              station_id: stationId,
              IsActive: true
            });
        }
      } catch (err) {
        console.error('Failed to register device:', err);
      }
    }

    registerDevice();
  }, [profile]);

  async function login(email: string, password: string, stationId?: string): Promise<string | null> {
    const effectiveStationId = stationId || localStorage.getItem('current_station_id') || localStorage.getItem('last_station_id') || '';
    const rawEmail = email.trim();
    const scopedEmail = getScopedAuthEmail(rawEmail, effectiveStationId);

    let authRes = await supabase.auth.signInWithPassword({ email: scopedEmail, password });

    // Fallback: If scoped email login failed, try the exact raw email if it contains '@'
    if (authRes.error && rawEmail.includes('@') && scopedEmail !== rawEmail.toLowerCase()) {
      const fallbackRes = await supabase.auth.signInWithPassword({ email: rawEmail, password });
      if (!fallbackRes.error) {
        authRes = fallbackRes;
      }
    }

    if (authRes.error) return authRes.error.message;

    if (effectiveStationId) {
      localStorage.setItem('current_station_id', effectiveStationId);
      localStorage.setItem('last_station_id', effectiveStationId);
    }
    return null;
  }

  async function logout(): Promise<void> {
    await supabase.auth.signOut();
  }

  async function refreshProfile(): Promise<void> {
    if (user) {
      const p = await fetchProfile(user.id);
      setProfile(p);
    }
  }

  return (
    <AuthContext.Provider value={{ user, profile, loading, isResetting, login, logout, refreshProfile }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
