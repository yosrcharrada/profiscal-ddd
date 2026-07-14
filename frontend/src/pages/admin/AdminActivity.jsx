import { useCallback, useEffect, useState } from 'react';
import { adminService } from '../../services/authService';
import { useLanguage } from '../../context/LanguageContext';
import AdminLayout from '../../components/admin/AdminLayout';

const fmtTime = (d) => (d ? new Date(d).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' }) : '—');

const EVENT_TONES = {
  LoginSucceeded: 'bg-green-50 text-green-700 border-green-200',
  LoginFailed: 'bg-red-50 text-red-600 border-red-200',
  AccountLockedOut: 'bg-red-50 text-red-600 border-red-200',
  TokenReuseDetected: 'bg-red-50 text-red-600 border-red-200',
  AccountLocked: 'bg-red-50 text-red-600 border-red-200',
  AccountUnlocked: 'bg-green-50 text-green-700 border-green-200',
  Register: 'bg-brand/20 text-dark border-brand/40',
  UserProvisioned: 'bg-brand/20 text-dark border-brand/40',
  RoleChanged: 'bg-brand/20 text-dark border-brand/40',
  ManagerAssigned: 'bg-blue-50 text-blue-700 border-blue-200',
  TaskAssigned: 'bg-blue-50 text-blue-700 border-blue-200',
  TaskSubmitted: 'bg-purple-50 text-purple-700 border-purple-200',
  ReclamationOpened: 'bg-orange-50 text-orange-700 border-orange-200',
  ReclamationResolved: 'bg-green-50 text-green-700 border-green-200',
  PasswordChanged: 'bg-blue-50 text-blue-700 border-blue-200',
};

const GROUPS = {
  auth: ['LoginSucceeded', 'LoginFailed', 'Logout', 'LogoutEverywhere', 'TokenRefreshed', 'TokenReuseDetected', 'AccountLockedOut', 'SessionRevoked', 'PasswordChanged'],
  accounts: ['Register', 'UserProvisioned', 'RoleChanged', 'ManagerAssigned', 'AccountLocked', 'AccountUnlocked'],
  work: ['TaskAssigned', 'TaskSubmitted', 'ReclamationOpened', 'ReclamationResolved'],
};

export default function AdminActivity() {
  const { t } = useLanguage();
  const [rows, setRows] = useState(null);
  const [error, setError] = useState('');
  const [group, setGroup] = useState('');
  const [take, setTake] = useState(60);

  const load = useCallback(async () => {
    try {
      const { data: res } = await adminService.globalActivity(take);
      setRows(res.data);
      setError('');
    } catch (err) {
      setError(err.response?.data?.message || 'Failed to load activity.');
    }
  }, [take]);

  useEffect(() => { load(); }, [load]);

  const filtered = rows?.filter((r) => !group || GROUPS[group]?.includes(r.event));

  return (
    <AdminLayout>
      <div className="space-y-4 animate-fade-up">
      <div className="flex flex-col sm:flex-row sm:items-end sm:justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold text-dark tracking-tight">{t('admin.activity.title')}</h1>
          <p className="text-[13px] text-muted mt-0.5">{t('admin.activity.subtitle')}</p>
        </div>
        <div className="flex gap-2">
          <div className="flex rounded-lg border border-border/60 bg-white overflow-hidden">
            {[['', t('admin.activity.all')], ['auth', t('admin.activity.auth')], ['accounts', t('admin.activity.accounts')], ['work', t('admin.activity.work')]].map(([g, label]) => (
              <button
                key={g}
                onClick={() => setGroup(g)}
                className={`px-3 py-2 text-[12px] font-semibold transition-colors ${group === g ? 'bg-dark text-white' : 'text-body hover:text-dark'}`}
              >{label}</button>
            ))}
          </div>
          <button onClick={load} className="px-3 py-2 border border-border/60 bg-white rounded-lg text-[12px] font-semibold text-body hover:text-dark hover:border-dark/30 transition-colors">
            ↻ {t('admin.activity.refresh')}
          </button>
        </div>
      </div>

      {error && <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-red-600 text-[13px]">{error}</div>}

      <div className="bg-white rounded-xl border border-border/60 shadow-card overflow-hidden">
        {!rows && <div className="p-8 text-center text-muted text-[13px]">{t('admin.users.loading')}</div>}
        {filtered?.length === 0 && <div className="p-8 text-center text-muted text-[13px]">{t('admin.activity.empty')}</div>}
        {filtered?.map((a, i) => (
          <div key={i} className="px-4 py-2.5 flex flex-wrap items-center gap-2 border-b border-border/30 last:border-0 hover:bg-light/40 transition-colors">
            <span className={`text-[11px] font-semibold border rounded-md px-1.5 py-0.5 ${EVENT_TONES[a.event] || 'bg-white text-body border-border'}`}>{a.event}</span>
            <span className="text-[12px] font-semibold text-dark">{a.userName || a.email}</span>
            {a.userName && <span className="text-[11px] text-muted">{a.email}</span>}
            {a.detail && <span className="text-[11px] text-body truncate max-w-xs">{a.detail}</span>}
            <span className="text-[11px] text-muted ml-auto shrink-0">{a.ipAddress ?? ''} · {fmtTime(a.createdAt)}</span>
          </div>
        ))}
      </div>

      {rows && rows.length >= take && (
        <div className="text-center">
          <button onClick={() => setTake((n) => n + 60)} className="px-4 py-2 text-[12px] font-semibold border border-border/60 rounded-lg bg-white text-body hover:text-dark hover:border-dark/30 transition-colors">
            {t('admin.activity.loadMore')}
          </button>
        </div>
      )}
      </div>
    </AdminLayout>
  );
}
