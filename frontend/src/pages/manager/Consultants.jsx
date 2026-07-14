import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { taskService } from '../../services/authService';
import { useLanguage } from '../../context/LanguageContext';

const fmtDate = (d) => (d ? new Date(d).toLocaleDateString(undefined, { dateStyle: 'medium' }) : '—');

function StatChip({ value, label, tone }) {
  return (
    <div className={`flex-1 rounded-lg border px-2 py-1.5 text-center ${tone}`}>
      <p className="text-[15px] font-extrabold leading-none">{value}</p>
      <p className="text-[9px] font-semibold uppercase tracking-[0.08em] mt-1 opacity-80">{label}</p>
    </div>
  );
}

export default function Consultants() {
  const { t } = useLanguage();
  const [team, setTeam] = useState(null);
  const [error, setError] = useState('');

  useEffect(() => {
    taskService.consultants()
      .then(({ data: res }) => setTeam(res.data))
      .catch((err) => setError(err.response?.data?.message || 'Failed to load team.'));
  }, []);

  const totals = team?.reduce(
    (acc, c) => ({
      submitted: acc.submitted + c.tasks.submitted,
      open: acc.open + c.tasks.pending + c.tasks.inProgress,
    }),
    { submitted: 0, open: 0 },
  );

  return (
    <div className="space-y-4 animate-fade-up">
      <div className="flex flex-col sm:flex-row sm:items-end sm:justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold text-dark tracking-tight">{t('manager.team.title')}</h1>
          <p className="text-[13px] text-muted mt-0.5">
            {t('manager.team.subtitle')}
            {team && totals.submitted > 0 && (
              <span className="ml-1.5 font-bold text-dark">· {totals.submitted} {t('manager.team.toReview')}</span>
            )}
          </p>
        </div>
      </div>

      {error && <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-red-600 text-[13px]">{error}</div>}

      {!team && (
        <div className="grid sm:grid-cols-2 xl:grid-cols-3 gap-3 animate-pulse">
          {[...Array(3)].map((_, i) => <div key={i} className="h-44 bg-light rounded-xl" />)}
        </div>
      )}

      {team?.length === 0 && (
        <div className="p-12 text-center bg-white rounded-xl border border-border/60">
          <div className="w-12 h-12 mx-auto rounded-full bg-brand/15 flex items-center justify-center">
            <svg className="w-6 h-6 text-dark" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}><path strokeLinecap="round" strokeLinejoin="round" d="M18 7.5v3m0 0v3m0-3h3m-3 0h-3m-2.25-4.125a3.375 3.375 0 11-6.75 0 3.375 3.375 0 016.75 0zM3 19.235v-.11a6.375 6.375 0 0112.75 0v.109A12.318 12.318 0 019.374 21c-2.331 0-4.512-.645-6.374-1.766z" /></svg>
          </div>
          <p className="mt-3 text-[14px] font-bold text-dark">{t('manager.team.emptyTitle')}</p>
          <p className="mt-1 text-[12px] text-muted max-w-sm mx-auto">{t('manager.team.emptyHint')}</p>
        </div>
      )}

      <div className="grid sm:grid-cols-2 xl:grid-cols-3 gap-3">
        {team?.map((c) => (
          <Link
            key={c.id}
            to={`/manager/consultants/${c.id}`}
            className="group bg-cream rounded-xl border border-border/60 p-4 hover:border-dark/25 hover:shadow-lg hover:-translate-y-0.5 transition-all"
          >
            <div className="flex items-center gap-3">
              <span className="w-11 h-11 rounded-xl bg-brand/25 text-dark flex items-center justify-center text-[13px] font-extrabold shrink-0 group-hover:bg-brand/50 transition-colors">
                {`${c.firstName?.[0] ?? ''}${c.lastName?.[0] ?? ''}`.toUpperCase()}
              </span>
              <div className="min-w-0 flex-1">
                <p className="text-[14px] font-bold text-dark truncate">{c.firstName} {c.lastName}</p>
                <p className="text-[11px] text-muted truncate">{c.email}</p>
              </div>
              {c.tasks.submitted > 0 && (
                <span className="shrink-0 text-[9px] font-bold text-dark bg-brand rounded-full px-2 py-1 uppercase tracking-wide animate-pulse">
                  {c.tasks.submitted} {t('manager.team.submittedBadge')}
                </span>
              )}
            </div>

            <div className="mt-3.5 flex gap-1.5">
              <StatChip value={c.tasks.pending} label={t('tasks.status.Pending')} tone="bg-light/70 border-border/50 text-body" />
              <StatChip value={c.tasks.inProgress} label={t('tasks.status.InProgress')} tone="bg-blue-50 border-blue-200 text-blue-700" />
              <StatChip value={c.tasks.submitted} label={t('tasks.status.Submitted')} tone="bg-brand/15 border-brand/40 text-dark" />
              <StatChip value={c.tasks.approved} label={t('tasks.status.Approved')} tone="bg-green-50 border-green-200 text-green-700" />
            </div>

            <div className="mt-3 pt-3 border-t border-border/40 flex items-center justify-between text-[11px] text-muted">
              <span>{c.consultations} {t('manager.team.consultations')}</span>
              <span>{t('manager.team.lastLogin')} {fmtDate(c.lastLoginAt)}</span>
            </div>
          </Link>
        ))}
      </div>
    </div>
  );
}
