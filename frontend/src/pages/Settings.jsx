import { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { useLanguage } from '../context/LanguageContext';
import authService from '../services/authService';

const fmtDate = (d, lang) => (d ? new Date(d).toLocaleString(lang === 'en' ? 'en-US' : 'fr-FR', { dateStyle: 'medium', timeStyle: 'short' }) : '—');

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
  const { t, lang } = useLanguage();
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
    <div className="max-w-2xl mx-auto space-y-4 animate-fade-up">
      <h1 className="text-xl font-bold text-dark tracking-tight">{t('settings.title')}</h1>

      <div className="bg-white rounded-xl border border-border/60 shadow-card p-5">
        <h2 className="text-[10px] font-semibold text-muted uppercase tracking-[0.12em] mb-4">{t('settings.profile')}</h2>
        <div className="flex items-center gap-4">
          <div className="w-14 h-14 rounded-xl bg-dark text-brand text-lg font-bold flex items-center justify-center">{I}</div>
          <div>
            <p className="text-base font-bold text-dark">{user?.firstName} {user?.lastName}</p>
            <p className="text-[13px] text-muted">{user?.email}</p>
            <div className="mt-1.5 flex items-center gap-2">
              <span className="inline-block text-[10px] bg-brand/15 text-dark px-2.5 py-0.5 rounded-md font-semibold border border-brand/20">{user?.roles?.[0] ?? 'User'}</span>
              {user?.lastLoginAt && <span className="text-[11px] text-muted">{t('settings.lastLogin')} {fmtDate(user.lastLoginAt, lang)}</span>}
            </div>
          </div>
        </div>
      </div>

      <div className="bg-white rounded-xl border border-border/60 shadow-card p-5">
        <h2 className="text-[10px] font-semibold text-muted uppercase tracking-[0.12em] mb-4">{t('settings.security')}</h2>
        <Link to="/settings/password" className="flex items-center justify-between p-3 rounded-lg border border-border/50 hover:border-brand/30 hover:shadow-card-hover transition-all group">
          <div className="flex items-center gap-3">
            <div className="w-9 h-9 rounded-lg bg-light text-muted flex items-center justify-center group-hover:bg-brand/15 group-hover:text-dark transition-colors">
              <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}><path strokeLinecap="round" strokeLinejoin="round" d="M16.5 10.5V6.75a4.5 4.5 0 10-9 0v3.75m-.75 11.25h10.5a2.25 2.25 0 002.25-2.25v-6.75a2.25 2.25 0 00-2.25-2.25H6.75a2.25 2.25 0 00-2.25 2.25v6.75a2.25 2.25 0 002.25 2.25z"/></svg>
            </div>
            <div>
              <p className="text-[13px] font-semibold text-dark">{t('settings.changePassword')}</p>
              <p className="text-[11px] text-muted">{t('settings.secureAccount')}</p>
            </div>
          </div>
          <svg className="w-3.5 h-3.5 text-border group-hover:text-muted transition-colors" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path strokeLinecap="round" strokeLinejoin="round" d="M8.25 4.5l7.5 7.5-7.5 7.5"/></svg>
        </Link>
      </div>

      <div className="bg-white rounded-xl border border-border/60 shadow-card p-5">
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-[10px] font-semibold text-muted uppercase tracking-[0.12em]">{t('settings.activeSessions')}</h2>
          <button onClick={signOutEverywhere} disabled={busy} className="text-[11px] font-semibold text-red-600 border border-red-200 bg-red-50 rounded-md px-2.5 py-1 hover:bg-red-100 disabled:opacity-50 transition-colors">{t('settings.logoutAll')}</button>
        </div>
        {!sessions && <p className="text-[13px] text-muted">{t('settings.loading')}</p>}
        {sessions?.length === 0 && <p className="text-[13px] text-muted">{t('settings.noSessions')}</p>}
        <div className="space-y-2">
          {sessions?.map((s) => (
            <div key={s.id} className="flex items-center justify-between p-3 rounded-lg border border-border/50">
              <div className="flex items-center gap-3 min-w-0">
                <div className="w-9 h-9 rounded-lg bg-light text-muted flex items-center justify-center shrink-0">
                  <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}><path strokeLinecap="round" strokeLinejoin="round" d="M9 17.25v1.007a3 3 0 01-.879 2.122L7.5 21h9l-.621-.621A3 3 0 0115 18.257V17.25m6-12V15a2.25 2.25 0 01-2.25 2.25H5.25A2.25 2.25 0 013 15V5.25m18 0A2.25 2.25 0 0018.75 3H5.25A2.25 2.25 0 003 5.25m18 0V12a2.25 2.25 0 01-2.25 2.25H5.25A2.25 2.25 0 013 12V5.25"/></svg>
                </div>
                <div className="min-w-0">
                  <p className="text-[13px] font-medium text-dark flex items-center gap-1.5">
                    {deviceLabel(s.userAgent)}
                    {s.isCurrent && <span className="text-[9px] font-semibold bg-brand/25 text-dark rounded px-1.5 py-0.5">{t('settings.thisDevice')}</span>}
                  </p>
                  <p className="text-[11px] text-muted truncate">{s.createdByIp ?? t('settings.unknownIP')} · {fmtDate(s.createdAt, lang)}</p>
                </div>
              </div>
              {!s.isCurrent && (
                <button onClick={() => revoke(s.id)} disabled={busy} className="text-[11px] font-medium text-body border border-border/60 rounded-md px-2.5 py-1 hover:border-red-300 hover:text-red-600 disabled:opacity-50 transition-colors shrink-0">{t('settings.revoke')}</button>
              )}
            </div>
          ))}
        </div>
      </div>

      <div className="bg-white rounded-xl border border-border/60 shadow-card p-5">
        <h2 className="text-[10px] font-semibold text-muted uppercase tracking-[0.12em] mb-4">{t('settings.account')}</h2>
        <button onClick={async()=>{await logout();navigate('/login');}} className="flex items-center gap-2.5 px-3 py-2.5 rounded-lg border border-red-100 text-red-500 hover:bg-red-50 transition-colors text-[13px] font-medium w-full">
          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}><path strokeLinecap="round" strokeLinejoin="round" d="M15.75 9V5.25A2.25 2.25 0 0013.5 3h-6a2.25 2.25 0 00-2.25 2.25v13.5A2.25 2.25 0 007.5 21h6a2.25 2.25 0 002.25-2.25V15M12 9l-3 3m0 0l3 3m-3-3h12.75"/></svg>
          {t('settings.logout')}
        </button>
      </div>
    </div>
  );
}
