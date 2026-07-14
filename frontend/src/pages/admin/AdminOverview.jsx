import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { adminService } from '../../services/authService';
import { useLanguage } from '../../context/LanguageContext';
import AdminLayout from '../../components/admin/AdminLayout';

const fmtDate = (d) => (d ? new Date(d).toLocaleDateString(undefined, { dateStyle: 'medium' }) : '—');
const fmtTime = (d) => (d ? new Date(d).toLocaleString(undefined, { dateStyle: 'short', timeStyle: 'short' }) : '—');

const EVENT_TONES = {
  LoginSucceeded: 'bg-green-50 text-green-700 border-green-200 dark:bg-green-500/10 dark:text-green-400 dark:border-green-500/30',
  LoginFailed: 'bg-red-50 text-red-600 border-red-200 dark:bg-red-500/10 dark:text-red-400 dark:border-red-500/30',
  AccountLockedOut: 'bg-red-50 text-red-600 border-red-200 dark:bg-red-500/10 dark:text-red-400 dark:border-red-500/30',
  TokenReuseDetected: 'bg-red-50 text-red-600 border-red-200 dark:bg-red-500/10 dark:text-red-400 dark:border-red-500/30',
  UserProvisioned: 'bg-brand/20 text-dark border-brand/40',
  RoleChanged: 'bg-brand/20 text-dark border-brand/40',
  ManagerAssigned: 'bg-blue-50 text-blue-700 border-blue-200 dark:bg-blue-500/10 dark:text-blue-400 dark:border-blue-500/30',
  TaskAssigned: 'bg-blue-50 text-blue-700 border-blue-200 dark:bg-blue-500/10 dark:text-blue-400 dark:border-blue-500/30',
  TaskSubmitted: 'bg-purple-50 text-purple-700 border-purple-200 dark:bg-purple-500/10 dark:text-purple-400 dark:border-purple-500/30',
  ReclamationOpened: 'bg-orange-50 text-orange-700 border-orange-200 dark:bg-orange-500/10 dark:text-orange-400 dark:border-orange-500/30',
  ReclamationResolved: 'bg-green-50 text-green-700 border-green-200 dark:bg-green-500/10 dark:text-green-400 dark:border-green-500/30',
};

function Kpi({ label, value, hint, icon, to, tone = 'bg-brand/20 text-dark', bar = 'bg-brand' }) {
  const body = (
    <div className="group relative bg-cream rounded-xl border border-border/60 p-4 h-full hover:border-dark/25 hover:shadow-md hover:-translate-y-0.5 transition-all overflow-hidden">
      <span className={`absolute top-0 left-0 w-full h-[3px] ${bar} opacity-80`} />
      <div className="flex items-start justify-between">
        <p className="text-[11px] font-semibold text-muted uppercase tracking-[0.1em]">{label}</p>
        <span className={`w-8 h-8 rounded-lg flex items-center justify-center transition-transform group-hover:scale-110 ${tone}`}>
          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>{icon}</svg>
        </span>
      </div>
      <p className="mt-1 text-2xl font-extrabold text-dark tracking-tight">{value}</p>
      {hint && <p className="mt-0.5 text-[11px] text-muted">{hint}</p>}
    </div>
  );
  return to ? <Link to={to} className="block h-full">{body}</Link> : body;
}

function RoleBar({ overview, t }) {
  const total = Math.max(1, overview.admins + overview.managers + overview.consultants);
  const seg = [
    { label: 'Admin', count: overview.admins, cls: 'bg-dark dark:bg-[#8a8a9d]' },
    { label: 'Manager', count: overview.managers, cls: 'bg-brand' },
    { label: 'Consultant', count: overview.consultants, cls: 'bg-brand/40' },
  ];
  return (
    <div className="bg-cream rounded-xl border border-border/60 p-4">
      <p className="text-[11px] font-semibold text-muted uppercase tracking-[0.1em] mb-3">{t('admin.overview.roleDistribution')}</p>
      <div className="h-2.5 rounded-full overflow-hidden flex bg-light">
        {seg.map((s) => s.count > 0 && (
          <div key={s.label} className={`${s.cls} transition-all`} style={{ width: `${(s.count / total) * 100}%` }} />
        ))}
      </div>
      <div className="mt-3 flex flex-wrap gap-x-5 gap-y-1.5">
        {seg.map((s) => (
          <span key={s.label} className="inline-flex items-center gap-1.5 text-[12px] text-body">
            <span className={`w-2 h-2 rounded-full ${s.cls}`} />
            <span className="font-semibold text-dark">{s.count}</span> {s.label}{s.count > 1 ? 's' : ''}
          </span>
        ))}
        {overview.lockedUsers > 0 && (
          <span className="inline-flex items-center gap-1.5 text-[12px] text-red-600">
            <span className="w-2 h-2 rounded-full bg-red-400" />
            <span className="font-semibold">{overview.lockedUsers}</span> {t('admin.overview.locked')}
          </span>
        )}
      </div>
    </div>
  );
}

export default function AdminOverview() {
  const { t } = useLanguage();
  const [overview, setOverview] = useState(null);
  const [error, setError] = useState('');

  useEffect(() => {
    adminService.overview()
      .then(({ data: res }) => setOverview(res.data))
      .catch((err) => setError(err.response?.data?.message || 'Failed to load overview.'));
  }, []);

  if (error) return (
    <AdminLayout>
      <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-red-600 text-[13px]">{error}</div>
    </AdminLayout>
  );
  if (!overview) return (
    <AdminLayout>
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-3 animate-pulse">
        {[...Array(4)].map((_, i) => <div key={i} className="h-28 bg-light rounded-xl" />)}
      </div>
    </AdminLayout>
  );

  const tasksInFlight = overview.tasksPending + overview.tasksInProgress + overview.tasksSubmitted;

  return (
    <AdminLayout badges={{ '/admin/reclamations': overview.pendingReclamations }}>
      <div className="space-y-4 animate-fade-up">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-end sm:justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold text-dark tracking-tight">{t('admin.overview.title')}</h1>
          <p className="text-[13px] text-muted mt-0.5">{t('admin.overview.subtitle')}</p>
        </div>
        <div className="flex gap-2">
          <Link to="/admin/users?new=1" className="px-3.5 py-2 bg-brand text-dark text-[13px] font-bold rounded-lg hover:shadow-lg hover:shadow-brand/40 transition-all inline-flex items-center gap-1.5">
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path strokeLinecap="round" strokeLinejoin="round" d="M12 4.5v15m7.5-7.5h-15" /></svg>
            {t('admin.overview.addUser')}
          </Link>
          <Link to="/admin/reclamations" className="px-3.5 py-2 bg-dark text-white text-[13px] font-semibold rounded-lg hover:bg-black transition-colors">
            {t('admin.overview.reviewReclamations')}
            {overview.pendingReclamations > 0 && (
              <span className="ml-1.5 bg-brand text-dark text-[10px] font-bold rounded-full px-1.5 py-0.5">{overview.pendingReclamations}</span>
            )}
          </Link>
        </div>
      </div>

      {/* KPI row */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
        <Kpi
          label={t('admin.overview.kpi.users')} value={overview.totalUsers}
          hint={`+${overview.newUsersThisMonth} ${t('admin.overview.kpi.thisMonth')}`} to="/admin/users"
          tone="bg-blue-50 text-blue-700" bar="bg-blue-500"
          icon={<path strokeLinecap="round" strokeLinejoin="round" d="M15 19.128a9.38 9.38 0 002.625.372 9.337 9.337 0 004.121-.952 4.125 4.125 0 00-7.533-2.493M15 19.128v-.003c0-1.113-.285-2.16-.786-3.07M15 19.128v.106A12.318 12.318 0 018.624 21c-2.331 0-4.512-.645-6.374-1.766l-.001-.109a6.375 6.375 0 0111.964-3.07M12 6.375a3.375 3.375 0 11-6.75 0 3.375 3.375 0 016.75 0zm8.25 2.25a2.625 2.625 0 11-5.25 0 2.625 2.625 0 015.25 0z" />}
        />
        <Kpi
          label={t('admin.overview.kpi.reclamations')} value={overview.pendingReclamations}
          hint={`${overview.resolvedReclamations} ${t('admin.overview.kpi.resolved')}`} to="/admin/reclamations"
          tone="bg-orange-50 text-orange-700" bar="bg-orange-500"
          icon={<path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m9-.75a9 9 0 11-18 0 9 9 0 0118 0zm-9 3.75h.008v.008H12v-.008z" />}
        />
        <Kpi
          label={t('admin.overview.kpi.consultations')} value={overview.totalConsultations}
          hint={`+${overview.consultationsThisWeek} ${t('admin.overview.kpi.thisWeek')}`} to="/app/consultations"
          tone="bg-brand/20 text-dark" bar="bg-brand"
          icon={<path strokeLinecap="round" strokeLinejoin="round" d="M19.5 14.25v-2.625a3.375 3.375 0 00-3.375-3.375h-1.5A1.125 1.125 0 0113.5 7.125v-1.5a3.375 3.375 0 00-3.375-3.375H8.25m2.25 0H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 00-9-9z" />}
        />
        <Kpi
          label={t('admin.overview.kpi.tasks')} value={tasksInFlight}
          hint={`${overview.tasksSubmitted} ${t('admin.overview.kpi.awaitingReview')}`}
          tone="bg-green-50 text-green-700" bar="bg-emerald-500"
          icon={<path strokeLinecap="round" strokeLinejoin="round" d="M9 12.75L11.25 15 15 9.75M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />}
        />
      </div>

      <RoleBar overview={overview} t={t} />

      <div className="grid lg:grid-cols-2 gap-3">
        {/* Recent registrations */}
        <div className="bg-cream rounded-xl border border-border/60 overflow-hidden">
          <div className="px-4 py-3 border-b border-border/40 flex items-center justify-between">
            <p className="text-[11px] font-semibold text-muted uppercase tracking-[0.1em]">{t('admin.overview.recentUsers')}</p>
            <Link to="/admin/users" className="text-[11px] font-semibold text-dark hover:underline">{t('admin.overview.seeAll')}</Link>
          </div>
          {overview.recentUsers.length === 0 && <p className="p-4 text-[12px] text-muted">{t('admin.overview.empty')}</p>}
          {overview.recentUsers.map((u) => {
            const role = u.roles?.[0] ?? 'Consultant';
            return (
              <div key={u.id} className="px-4 py-2.5 flex items-center gap-2.5 border-b border-border/30 last:border-0">
                <span className={`w-8 h-8 rounded-lg flex items-center justify-center text-[10px] font-bold shrink-0 ${role === 'Admin' ? 'bg-dark text-brand' : role === 'Manager' ? 'bg-brand text-dark' : 'bg-brand/25 text-dark'}`}>
                  {`${u.firstName?.[0] ?? ''}${u.lastName?.[0] ?? ''}`.toUpperCase()}
                </span>
                <div className="min-w-0 flex-1">
                  <p className="text-[13px] font-semibold text-dark truncate">{u.firstName} {u.lastName}</p>
                  <p className="text-[11px] text-muted truncate">
                    {role}{u.managerName ? ` · ${t('admin.overview.managedBy')} ${u.managerName}` : ''}
                  </p>
                </div>
                <span className="text-[11px] text-muted shrink-0">{fmtDate(u.createdAt)}</span>
              </div>
            );
          })}
        </div>

        {/* Activity feed */}
        <div className="bg-cream rounded-xl border border-border/60 overflow-hidden">
          <div className="px-4 py-3 border-b border-border/40 flex items-center justify-between">
            <p className="text-[11px] font-semibold text-muted uppercase tracking-[0.1em]">{t('admin.overview.latestActivity')}</p>
            <Link to="/admin/activity" className="text-[11px] font-semibold text-dark hover:underline">{t('admin.overview.seeAll')}</Link>
          </div>
          <div className="max-h-[340px] overflow-y-auto">
            {overview.recentActivity.length === 0 && <p className="p-4 text-[12px] text-muted">{t('admin.overview.empty')}</p>}
            {overview.recentActivity.map((a, i) => (
              <div key={i} className="px-4 py-2 flex flex-wrap items-center gap-1.5 border-b border-border/30 last:border-0 text-[11px]">
                <span className={`font-semibold border rounded-md px-1.5 py-0.5 ${EVENT_TONES[a.event] || 'bg-white text-body border-border dark:bg-light'}`}>{a.event}</span>
                <span className="text-dark font-medium truncate max-w-[160px]">{a.userName || a.email}</span>
                {a.detail && <span className="text-muted truncate max-w-[180px]">{a.detail}</span>}
                <span className="text-muted ml-auto shrink-0">{fmtTime(a.createdAt)}</span>
              </div>
            ))}
          </div>
        </div>
      </div>
      </div>
    </AdminLayout>
  );
}
