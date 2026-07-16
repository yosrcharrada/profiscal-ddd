import { useCallback, useEffect, useMemo, useState } from 'react';
import { taskService } from '../../services/authService';
import { useLanguage } from '../../context/LanguageContext';

/* Manager "Tasks" board — every task I assigned, in one filterable table.
   Clicking a task that has a deliverable opens the read-only document render
   in a NEW browser tab (/view/consultations/:id?task=:taskId) where the manager
   validates the work and exports the client-ready .docx. */

const fmtDate = (d) => (d ? new Date(d).toLocaleDateString(undefined, { dateStyle: 'medium' }) : '—');

const STATUS_TONES = {
  Pending: 'bg-light text-body border-border',
  InProgress: 'bg-blue-50 text-blue-700 border-blue-200',
  Submitted: 'bg-brand/20 text-dark border-brand/50',
  Approved: 'bg-green-50 text-green-700 border-green-200',
};

const PRIORITY_TONES = {
  Low: 'text-muted',
  Medium: 'text-blue-600',
  High: 'text-orange-600',
  Urgent: 'text-red-600',
};

const FILTERS = ['', 'Pending', 'InProgress', 'Submitted', 'Approved'];

const FILTER_ACTIVE_TONES = {
  '': 'bg-dark text-brand border-dark',
  Pending: 'bg-orange-400 text-white border-orange-400',
  InProgress: 'bg-blue-500 text-white border-blue-500',
  Submitted: 'bg-brand text-black border-brand',
  Approved: 'bg-green-600 text-white border-green-600',
};

export default function ManagerTasks() {
  const { t } = useLanguage();
  const [tasks, setTasks] = useState(null);
  const [error, setError] = useState('');
  const [filter, setFilter] = useState('');
  const [search, setSearch] = useState('');
  const [busyId, setBusyId] = useState(null);

  const load = useCallback(async () => {
    try {
      const { data: res } = await taskService.assigned();
      setTasks(res.data || []);
      setError('');
    } catch (err) {
      setError(err.response?.data?.message || 'Failed to load tasks.');
    }
  }, []);

  // Live board: poll so newly assigned/submitted tasks appear without a manual
  // reload, and refresh immediately when the window regains focus.
  useEffect(() => {
    load();
    const id = setInterval(load, 15000);
    window.addEventListener('focus', load);
    return () => {
      clearInterval(id);
      window.removeEventListener('focus', load);
    };
  }, [load]);

  const counts = useMemo(() => {
    const c = { '': tasks?.length || 0 };
    FILTERS.slice(1).forEach((s) => { c[s] = (tasks || []).filter((x) => x.status === s).length; });
    return c;
  }, [tasks]);

  const shown = useMemo(() => {
    const q = search.trim().toLowerCase();
    return (tasks || [])
      .filter((x) => !filter || x.status === filter)
      .filter((x) =>
        !q ||
        x.title?.toLowerCase().includes(q) ||
        x.clientName?.toLowerCase().includes(q) ||
        x.consultant?.name?.toLowerCase().includes(q));
  }, [tasks, filter, search]);

  const openDocument = (task) => {
    if (!task.consultation) return;
    // New browser tab with the read-only document render (validate + export live there).
    window.open(`/view/consultations/${task.consultation.id}?task=${task.id}`, '_blank', 'noopener');
  };

  const onAction = async (action, task) => {
    setBusyId(task.id); setError('');
    try {
      if (action === 'approve') await taskService.approve(task.id);
      else if (action === 'reopen') await taskService.reopen(task.id);
      else if (action === 'delete') await taskService.remove(task.id);
      await load();
    } catch (err) {
      setError(err.response?.data?.message || 'Action failed.');
    } finally {
      setBusyId(null);
    }
  };

  return (
    <div className="space-y-4 animate-fade-up">
      {/* header */}
      <div className="flex flex-col sm:flex-row sm:items-end sm:justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold text-dark tracking-tight">{t('manager.tasks.title')}</h1>
          <p className="text-[13px] text-muted mt-0.5">
            {t('manager.tasks.subtitle')}
            {counts.Submitted > 0 && (
              <span className="ml-1.5 font-bold text-dark">· {counts.Submitted} {t('manager.team.toReview')}</span>
            )}
          </p>
        </div>
        <div className="flex items-center gap-2 bg-white border border-border/60 rounded-lg px-2.5 py-1.5 focus-within:ring-2 focus-within:ring-brand transition-all">
          <svg className="w-3.5 h-3.5 text-muted shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M21 21l-5.197-5.197m0 0A7.5 7.5 0 105.196 5.196a7.5 7.5 0 0010.607 10.607z" />
          </svg>
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder={t('manager.tasks.searchPh')}
            className="w-40 sm:w-52 bg-transparent text-[12.5px] text-dark placeholder-muted focus:outline-none"
          />
        </div>
      </div>

      {/* status filter chips */}
      <div className="flex flex-wrap gap-1.5">
        {FILTERS.map((s) => {
          const active = filter === s;
          return (
            <button
              key={s || 'all'}
              onClick={() => setFilter(s)}
              className={`px-3 py-1.5 rounded-lg border text-[12px] font-semibold transition-all ${
                active
                  ? FILTER_ACTIVE_TONES[s]
                  : 'bg-white text-body border-border/60 hover:border-brand/60 hover:bg-brand/5'
              }`}
            >
              {s === '' ? t('manager.tasks.all') : t(`tasks.status.${s}`)}
              <span className={`ml-1.5 text-[10px] font-bold ${active ? 'opacity-80' : 'text-muted'}`}>{counts[s]}</span>
            </button>
          );
        })}
      </div>

      {error && <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-red-600 text-[13px]">{error}</div>}

      {!tasks && <div className="h-40 bg-light rounded-xl animate-pulse" />}

      {tasks && shown.length === 0 && (
        <div className="p-12 text-center bg-white rounded-xl border border-border/60">
          <div className="w-12 h-12 mx-auto rounded-full bg-brand/15 flex items-center justify-center">
            <svg className="w-6 h-6 text-dark" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M9 12.75L11.25 15 15 9.75M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
          </div>
          <p className="mt-3 text-[14px] font-bold text-dark">{t('manager.tasks.emptyTitle')}</p>
          <p className="mt-1 text-[12px] text-muted">{t('manager.tasks.emptyHint')}</p>
        </div>
      )}

      {tasks && shown.length > 0 && (
        <div className="bg-white rounded-xl border border-border/60 overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full text-[13px]">
              <thead>
                <tr className="bg-light/70 text-[10px] font-bold text-muted uppercase tracking-[0.1em]">
                  <th className="text-left px-4 py-2.5">{t('manager.tasks.colTask')}</th>
                  <th className="text-left px-3 py-2.5 hidden md:table-cell">{t('manager.tasks.colConsultant')}</th>
                  <th className="text-left px-3 py-2.5 hidden lg:table-cell">{t('manager.assign.client')}</th>
                  <th className="text-center px-3 py-2.5">{t('manager.assign.priority')}</th>
                  <th className="text-center px-3 py-2.5">{t('manager.tasks.colStatus')}</th>
                  <th className="text-right px-3 py-2.5 hidden lg:table-cell">{t('manager.assign.dueDate')}</th>
                  <th className="text-right px-4 py-2.5">{t('manager.tasks.colActions')}</th>
                </tr>
              </thead>
              <tbody>
                {shown.map((task) => {
                  const openable = Boolean(task.consultation);
                  return (
                    <tr
                      key={task.id}
                      onClick={() => openDocument(task)}
                      className={`border-t border-border/40 transition-colors ${openable ? 'cursor-pointer hover:bg-brand/5' : 'hover:bg-light/40'}`}
                      title={openable ? t('manager.tasks.openDocHint') : undefined}
                    >
                      <td className="px-4 py-3">
                        <div className="flex items-center gap-2.5 min-w-0">
                          <span className={`w-2 h-2 rounded-full shrink-0 ${task.status === 'Pending' ? 'bg-orange-400' : task.status === 'InProgress' ? 'bg-blue-500' : task.status === 'Submitted' ? 'bg-brand' : 'bg-green-500'}`} />
                          <div className="min-w-0">
                            <p className="font-bold text-dark truncate max-w-[260px]">
                              {task.title}
                              {openable && (
                                <svg className="inline w-3 h-3 ml-1.5 text-muted align-middle" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                                  <path strokeLinecap="round" strokeLinejoin="round" d="M13.5 6H5.25A2.25 2.25 0 003 8.25v10.5A2.25 2.25 0 005.25 21h10.5A2.25 2.25 0 0018 18.75V10.5m-10.5 6L21 3m0 0h-5.25M21 3v5.25" />
                                </svg>
                              )}
                            </p>
                            <p className="text-[11px] text-muted truncate max-w-[260px] md:hidden">{task.consultant?.name}</p>
                          </div>
                        </div>
                      </td>
                      <td className="px-3 py-3 hidden md:table-cell">
                        <span className="flex items-center gap-2 min-w-0">
                          <span className="w-6 h-6 rounded-md bg-brand/25 text-dark flex items-center justify-center text-[9px] font-extrabold shrink-0">
                            {(task.consultant?.name || '?').split(' ').map((x) => x[0]).slice(0, 2).join('').toUpperCase()}
                          </span>
                          <span className="text-body truncate max-w-[140px]">{task.consultant?.name}</span>
                        </span>
                      </td>
                      <td className="px-3 py-3 text-muted truncate max-w-[120px] hidden lg:table-cell">{task.clientName || '—'}</td>
                      <td className="px-3 py-3 text-center">
                        <span className={`text-[10px] font-bold whitespace-nowrap ${PRIORITY_TONES[task.priority] || ''}`}>
                          {task.priority === 'Urgent' ? '●●●' : task.priority === 'High' ? '●●' : '●'} {t(`tasks.priority.${task.priority}`)}
                        </span>
                      </td>
                      <td className="px-3 py-3 text-center">
                        <span className={`inline-block text-[10px] font-semibold border rounded-md px-2 py-0.5 whitespace-nowrap ${STATUS_TONES[task.status]}`}>
                          {t(`tasks.status.${task.status}`)}
                        </span>
                      </td>
                      <td className="px-3 py-3 text-right text-[11.5px] text-muted whitespace-nowrap hidden lg:table-cell">{fmtDate(task.dueDate)}</td>
                      <td className="px-4 py-3">
                        <div className="flex items-center justify-end gap-1.5" onClick={(e) => e.stopPropagation()}>
                          {openable && (
                            <button
                              onClick={() => openDocument(task)}
                              className="text-[11px] font-bold text-dark bg-brand/20 border border-brand/40 rounded-md px-2.5 py-1.5 hover:bg-brand/40 transition-colors whitespace-nowrap"
                            >
                              {t('manager.tasks.openDoc')}
                            </button>
                          )}
                          {task.status === 'Submitted' && (
                            <button
                              disabled={busyId === task.id}
                              onClick={() => onAction('approve', task)}
                              className="text-[11px] font-bold text-white bg-green-600 rounded-md px-2.5 py-1.5 hover:bg-green-700 disabled:opacity-50 transition-colors whitespace-nowrap"
                            >
                              ✓ {t('tasks.approve')}
                            </button>
                          )}
                          {task.status === 'Submitted' && (
                            <button
                              disabled={busyId === task.id}
                              onClick={() => onAction('reopen', task)}
                              className="text-[11px] font-medium text-body border border-border/60 rounded-md px-2.5 py-1.5 hover:border-dark/30 hover:text-dark disabled:opacity-50 transition-colors whitespace-nowrap"
                            >
                              {t('tasks.sendBack')}
                            </button>
                          )}
                          {(task.status === 'Pending' || task.status === 'InProgress') && (
                            <button
                              disabled={busyId === task.id}
                              onClick={() => onAction('delete', task)}
                              className="text-[11px] font-medium text-red-600 border border-red-200 bg-red-50 rounded-md px-2.5 py-1.5 hover:bg-red-100 disabled:opacity-50 transition-colors"
                            >
                              {t('tasks.delete')}
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}
