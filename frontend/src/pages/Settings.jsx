import { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import authService from '../services/authService';

const fmtDate = (d) => (d ? new Date(d).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' }) : '—');

function deviceLabel(userAgent) {
  if (!userAgent) return 'Unknown device';
  const ua = userAgent.toLowerCase();
  const browser =
    ua.includes('edg') ? 'Edge' :
    ua.includes('chrome') ? 'Chrome' :
    ua.includes('safari') ? 'Safari' :
    ua.includes('firefox') ? 'Firefox' : 'Browser';
  const os =
    ua.includes('iphone') || ua.includes('ipad') ? 'iOS' :
    ua.includes('android') ? 'Android' :
    ua.includes('mac') ? 'macOS' :
    ua.includes('windows') ? 'Windows' :
    ua.includes('linux') ? 'Linux' : 'Unknown OS';
  return `${browser} · ${os}`;
}

export default function Settings() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const I = `${user?.firstName?.[0] || ''}${user?.lastName?.[0] || ''}`.toUpperCase();

  const [sessions, setSessions] = useState(null);
  const [busy, setBusy] = useState(false);

  const loadSessions = useCallback(async () => {
    try {
      const { data: res } = await authService.sessions();
      setSessions(res.data);
    } catch {
      setSessions([]);
    }
  }, []);

  useEffect(() => { loadSessions(); }, [loadSessions]);

  const revoke = async (id) => {
    setBusy(true);
    try { await authService.revokeSession(id); await loadSessions(); } finally { setBusy(false); }
  };

  const signOutEverywhere = async () => {
    setBusy(true);
    await logout(true);
    navigate('/login');
  };

  return (
    <div className="max-w-2xl mx-auto space-y-6 animate-fade-up">
      <h1 className="text-2xl font-extrabold text-dark">Settings</h1>

      <div className="bg-white rounded-2xl border border-border p-6"><h2 className="text-xs font-bold text-muted uppercase tracking-widest mb-5">Profile</h2>
        <div className="flex items-center gap-4"><div className="w-16 h-16 rounded-xl bg-dark text-brand text-xl font-bold flex items-center justify-center">{I}</div><div><p className="text-lg font-bold text-dark">{user?.firstName} {user?.lastName}</p><p className="text-sm text-muted">{user?.email}</p><div className="mt-1.5 flex items-center gap-2"><span className="inline-block text-xs bg-brand/10 text-dark px-3 py-0.5 rounded-full font-bold border border-brand/20">{user?.roles?.[0] ?? 'User'}</span>{user?.lastLoginAt && <span className="text-xs text-muted">Last login {fmtDate(user.lastLoginAt)}</span>}</div></div></div>
      </div>

      <div className="bg-white rounded-2xl border border-border p-6"><h2 className="text-xs font-bold text-muted uppercase tracking-widest mb-5">Security</h2>
        <Link to="/settings/password" className="flex items-center justify-between p-4 rounded-xl border border-border hover:border-brand/40 hover:shadow-sm transition-all group">
          <div className="flex items-center gap-3"><div className="w-10 h-10 rounded-lg bg-light text-muted flex items-center justify-center group-hover:bg-brand/10 group-hover:text-dark transition-colors"><svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}><path strokeLinecap="round" strokeLinejoin="round" d="M16.5 10.5V6.75a4.5 4.5 0 10-9 0v3.75m-.75 11.25h10.5a2.25 2.25 0 002.25-2.25v-6.75a2.25 2.25 0 00-2.25-2.25H6.75a2.25 2.25 0 00-2.25 2.25v6.75a2.25 2.25 0 002.25 2.25z"/></svg></div><div><p className="text-sm font-semibold text-dark">Change Password</p><p className="text-xs text-muted">Keep your account secure</p></div></div>
          <svg className="w-4 h-4 text-gray-300 group-hover:text-dark transition-colors" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path strokeLinecap="round" strokeLinejoin="round" d="M8.25 4.5l7.5 7.5-7.5 7.5"/></svg>
        </Link>
      </div>

      <div className="bg-white rounded-2xl border border-border p-6">
        <div className="flex items-center justify-between mb-5">
          <h2 className="text-xs font-bold text-muted uppercase tracking-widest">Active sessions</h2>
          <button onClick={signOutEverywhere} disabled={busy} className="text-xs font-semibold text-red-600 border border-red-200 bg-red-50 rounded-lg px-3 py-1.5 hover:bg-red-100 disabled:opacity-50 transition-colors">Sign out everywhere</button>
        </div>
        {!sessions && <p className="text-sm text-muted">Loading sessions…</p>}
        {sessions?.length === 0 && <p className="text-sm text-muted">No active sessions.</p>}
        <div className="space-y-3">
          {sessions?.map((s) => (
            <div key={s.id} className="flex items-center justify-between p-4 rounded-xl border border-border">
              <div className="flex items-center gap-3 min-w-0">
                <div className="w-10 h-10 rounded-lg bg-light text-muted flex items-center justify-center shrink-0">
                  <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}><path strokeLinecap="round" strokeLinejoin="round" d="M9 17.25v1.007a3 3 0 01-.879 2.122L7.5 21h9l-.621-.621A3 3 0 0115 18.257V17.25m6-12V15a2.25 2.25 0 01-2.25 2.25H5.25A2.25 2.25 0 013 15V5.25m18 0A2.25 2.25 0 0018.75 3H5.25A2.25 2.25 0 003 5.25m18 0V12a2.25 2.25 0 01-2.25 2.25H5.25A2.25 2.25 0 013 12V5.25"/></svg>
                </div>
                <div className="min-w-0">
                  <p className="text-sm font-semibold text-dark flex items-center gap-2">
                    {deviceLabel(s.userAgent)}
                    {s.isCurrent && <span className="text-[10px] font-bold bg-brand text-dark rounded-full px-2 py-0.5">This device</span>}
                  </p>
                  <p className="text-xs text-muted truncate">{s.createdByIp ?? 'Unknown IP'} · started {fmtDate(s.createdAt)}</p>
                </div>
              </div>
              {!s.isCurrent && (
                <button onClick={() => revoke(s.id)} disabled={busy} className="text-xs font-semibold text-body border border-border rounded-lg px-3 py-1.5 hover:border-red-300 hover:text-red-600 disabled:opacity-50 transition-colors shrink-0">Revoke</button>
              )}
            </div>
          ))}
        </div>
      </div>

      <div className="bg-white rounded-2xl border border-border p-6"><h2 className="text-xs font-bold text-muted uppercase tracking-widest mb-5">Account</h2>
        <button onClick={async()=>{await logout();navigate('/login');}} className="flex items-center gap-3 px-4 py-3 rounded-xl border border-red-100 text-red-500 hover:bg-red-50 transition-colors text-sm font-medium w-full"><svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}><path strokeLinecap="round" strokeLinejoin="round" d="M15.75 9V5.25A2.25 2.25 0 0013.5 3h-6a2.25 2.25 0 00-2.25 2.25v13.5A2.25 2.25 0 007.5 21h6a2.25 2.25 0 002.25-2.25V15M12 9l-3 3m0 0l3 3m-3-3h12.75"/></svg>Sign out</button>
      </div>
    </div>
  );
}
