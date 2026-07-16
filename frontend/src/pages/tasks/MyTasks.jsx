import { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { taskService } from '../../services/authService';
import fiscalService from '../../services/fiscalService';
import { useLanguage } from '../../context/LanguageContext';
import { CONSULTATION_PREFILL_KEY } from '../fiscal/Consultations';

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

/* ───────────── Submit modal: pick one of my consultations ───────────── */
/* Inline submit panel — expands under the task row, no popup. */
function SubmitPanel({ task, onClose, onSubmitted, t }) {
  const [consultations, setConsultations] = useState(null);
  const [selected, setSelected] = useState(task.consultationId || '');
  const [note, setNote] = useState(task.submitNote || '');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    fiscalService.list()
      .then(({ data: res }) => setConsultations(res.data || []))
      .catch(() => setConsultations([]));
  }, []);

  const submit = async () => {
    setBusy(true); setError('');
    try {
      await taskService.submit(task.id, { consultationId: selected || null, note: note.trim() || null });
      await onSubmitted();
      onClose();
    } catch (err) {
      setError(err.response?.data?.message || 'Failed to submit.');
      setBusy(false);
    }
  };

  return (
    <div className="mt-2.5 pt-3 border-t border-border/40 animate-scale-in origin-top">
      <p className="text-[11px] font-bold text-dark uppercase tracking-[0.08em] mb-2">{t('tasks.submitModal.title')}</p>
      {error && <div className="mb-2 p-2.5 bg-red-50 border border-red-200 rounded-lg text-red-600 text-[12px]">{error}</div>}

      <label className="block text-[11px] font-semibold text-muted uppercase tracking-[0.08em] mb-1.5">{t('tasks.submitModal.pickConsultation')}</label>
      {!consultations && <p className="text-[12px] text-muted">{t('admin.users.loading')}</p>}
      {consultations?.length === 0 && (
        <p className="text-[12px] text-muted bg-light/60 border border-border/40 rounded-lg p-3">
          {t('tasks.submitModal.noConsultations')}{' '}
          <Link to="/app/consultations" className="font-semibold text-dark hover:underline">{t('tasks.submitModal.createOne')}</Link>
        </p>
      )}
      {consultations?.length > 0 && (
        <div className="max-h-52 overflow-y-auto space-y-1.5 pr-1">
          {consultations.map((c) => (
            <label key={c.id} className={`flex items-center gap-2.5 p-2.5 rounded-lg border cursor-pointer transition-all ${selected === c.id ? 'border-brand bg-brand/10' : 'border-border/50 bg-white hover:border-dark/25'}`}>
              <input
                type="radio"
                name={`consultation-${task.id}`}
                checked={selected === c.id}
                onChange={() => setSelected(c.id)}
                className="accent-[#FFE600]"
              />
              <div className="min-w-0">
                <p className="text-[12px] font-bold text-dark truncate">{c.reference} — {c.clientName}</p>
                <p className="text-[11px] text-muted truncate">{c.fiscalQuestion} · {fmtDate(c.createdAt)}</p>
              </div>
            </label>
          ))}
        </div>
      )}

      <label className="block text-[11px] font-semibold text-muted uppercase tracking-[0.08em] mb-1.5 mt-3">{t('tasks.submitModal.note')}</label>
      <textarea
        value={note}
        onChange={(e) => setNote(e.target.value)}
        rows={2}
        placeholder={t('tasks.submitModal.notePh')}
        className="w-full px-3 py-2.5 rounded-lg border border-border/60 bg-white text-[13px] text-dark placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-brand resize-none"
      />

      <div className="mt-3 flex justify-end gap-2">
        <button type="button" onClick={onClose} className="px-4 py-2 border border-border/60 text-[13px] font-semibold text-body rounded-lg hover:border-dark/30 hover:text-dark transition-colors">
          {t('admin.recl.cancel')}
        </button>
        <button type="button" onClick={submit} disabled={busy} className="px-5 py-2 bg-brand text-dark text-[13px] font-bold rounded-lg hover:shadow-lg hover:shadow-brand/40 disabled:opacity-50 transition-all">
          {busy ? '…' : t('tasks.submitModal.submit')}
        </button>
      </div>
    </div>
  );
}

/* ───────────── Page ───────────── */
export default function MyTasks() {
  const { t } = useLanguage();
  const navigate = useNavigate();
  const [tasks, setTasks] = useState(null);
  const [error, setError] = useState('');
  const [busyId, setBusyId] = useState(null);
  const [submitting, setSubmitting] = useState(null);
  const [expanded, setExpanded] = useState(null);
  const [tab, setTab] = useState('open'); // open | done

  const load = useCallback(async () => {
    try {
      const { data: res } = await taskService.mine();
      setTasks(res.data);
      setError('');
    } catch (err) {
      setError(err.response?.data?.message || 'Failed to load tasks.');
    }
  }, []);

  // Live queue: poll so newly assigned tasks appear without a manual reload.
  useEffect(() => {
    load();
    const id = setInterval(load, 15000);
    window.addEventListener('focus', load);
    return () => {
      clearInterval(id);
      window.removeEventListener('focus', load);
    };
  }, [load]);

  const start = async (task) => {
    setBusyId(task.id);
    try { await taskService.start(task.id); await load(); }
    catch (err) { setError(err.response?.data?.message || 'Failed.'); }
    finally { setBusyId(null); }
  };

  /* Hand the task over to the consultation intake form pre-filled — no copy/paste. */
  const createConsultationFromTask = (task) => {
    try {
      sessionStorage.setItem(CONSULTATION_PREFILL_KEY, JSON.stringify({
        taskId: task.id,
        taskTitle: task.title,
        clientName: task.clientName || '',
        situation: task.description || '',
        fiscalQuestion: task.title || '',
      }));
    } catch { /* storage full — form just opens empty */ }
    navigate('/app/consultations?new=1');
  };

  const open = tasks?.filter((x) => x.status === 'Pending' || x.status === 'InProgress') ?? [];
  const done = tasks?.filter((x) => x.status === 'Submitted' || x.status === 'Approved') ?? [];
  const shown = tab === 'open' ? open : done;

  return (
    <div className="space-y-4 animate-fade-up">
      <div className="flex flex-col sm:flex-row sm:items-end sm:justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold text-dark tracking-tight">{t('tasks.title')}</h1>
          <p className="text-[13px] text-muted mt-0.5">{t('tasks.subtitle')}</p>
        </div>
        <div className="flex rounded-lg border border-border/60 bg-white overflow-hidden">
          <button onClick={() => setTab('open')} className={`px-3.5 py-2 text-[12px] font-semibold transition-colors ${tab === 'open' ? 'bg-brand text-black' : 'text-body hover:text-dark hover:bg-brand/5'}`}>
            {t('tasks.tabOpen')} ({open.length})
          </button>
          <button onClick={() => setTab('done')} className={`px-3.5 py-2 text-[12px] font-semibold transition-colors ${tab === 'done' ? 'bg-brand text-black' : 'text-body hover:text-dark hover:bg-brand/5'}`}>
            {t('tasks.tabDone')} ({done.length})
          </button>
        </div>
      </div>

      {error && <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-red-600 text-[13px]">{error}</div>}

      {!tasks && <div className="h-32 bg-light rounded-xl animate-pulse" />}

      {tasks && shown.length === 0 && (
        <div className="p-12 text-center bg-white rounded-xl border border-border/60">
          <div className="w-12 h-12 mx-auto rounded-full bg-brand/15 flex items-center justify-center">
            <svg className="w-6 h-6 text-dark" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}><path strokeLinecap="round" strokeLinejoin="round" d="M9 12.75L11.25 15 15 9.75M21 12a9 9 0 11-18 0 9 9 0 0118 0z" /></svg>
          </div>
          <p className="mt-3 text-[14px] font-bold text-dark">{tab === 'open' ? t('tasks.emptyOpen') : t('tasks.emptyDone')}</p>
          <p className="mt-1 text-[12px] text-muted">{tab === 'open' ? t('tasks.emptyOpenHint') : t('tasks.emptyDoneHint')}</p>
        </div>
      )}

      <div className="space-y-2.5">
        {shown.map((task) => (
          <div key={task.id} className="bg-white rounded-xl border border-border/60 overflow-hidden hover:border-dark/20 transition-colors">
            <div className="px-4 py-3 flex flex-wrap items-center gap-2.5">
              <button onClick={() => setExpanded(expanded === task.id ? null : task.id)} className="flex items-center gap-3 min-w-0 flex-1 text-left">
                <span className={`w-2 h-2 rounded-full shrink-0 ${task.status === 'Pending' ? 'bg-orange-400 animate-pulse' : task.status === 'InProgress' ? 'bg-blue-500' : task.status === 'Submitted' ? 'bg-brand' : 'bg-green-500'}`} />
                <div className="min-w-0">
                  <p className="text-[13px] font-bold text-dark truncate">
                    {task.title}
                    {task.isCollaboration && (
                      <span className="ml-1.5 text-[9px] font-bold text-blue-700 bg-blue-50 border border-blue-200 rounded px-1.5 py-px uppercase align-middle">{t('tasks.collab')}</span>
                    )}
                  </p>
                  <p className="text-[11px] text-muted truncate">
                    {task.clientName && <span>{task.clientName} · </span>}
                    {t('tasks.from')} {task.manager?.name}
                    {task.dueDate && <span> · {t('tasks.due')} <span className={new Date(task.dueDate) < new Date() && task.status !== 'Approved' && task.status !== 'Submitted' ? 'text-red-600 font-semibold' : ''}>{fmtDate(task.dueDate)}</span></span>}
                  </p>
                </div>
              </button>
              <span className={`text-[10px] font-bold ${PRIORITY_TONES[task.priority] || ''}`}>
                {task.priority === 'Urgent' ? '●●●' : task.priority === 'High' ? '●●' : '●'} {t(`tasks.priority.${task.priority}`)}
              </span>
              <span className={`text-[10px] font-semibold border rounded-md px-2 py-0.5 ${STATUS_TONES[task.status]}`}>
                {t(`tasks.status.${task.status}`)}
              </span>

              {task.status === 'Pending' && (
                <button disabled={busyId === task.id} onClick={() => start(task)}
                  className="text-[11px] font-bold text-black bg-brand rounded-md px-3 py-1.5 hover:shadow-md hover:shadow-brand/40 disabled:opacity-50 transition-all">
                  {t('tasks.start')}
                </button>
              )}
              {task.status === 'InProgress' && (
                <>
                  <button
                    onClick={() => createConsultationFromTask(task)}
                    className="text-[11px] font-medium text-body border border-border/60 rounded-md px-3 py-1.5 hover:border-brand/60 hover:bg-brand/10 hover:text-dark transition-colors"
                  >{t('tasks.workOnIt')}</button>
                  <button
                    disabled={busyId === task.id}
                    onClick={() => { setSubmitting(submitting === task.id ? null : task.id); setExpanded(task.id); }}
                    className={`text-[11px] font-bold rounded-md px-3 py-1.5 disabled:opacity-50 transition-all ${submitting === task.id ? 'border border-border text-body hover:border-brand/50 hover:bg-brand/5' : 'text-black bg-brand hover:shadow-md hover:shadow-brand/40'}`}
                  >
                    {submitting === task.id ? t('admin.recl.cancel') : t('tasks.submit')}
                  </button>
                </>
              )}
              {task.status === 'Submitted' && (
                <span className="text-[11px] text-muted italic">{t('tasks.awaitingManager')}</span>
              )}
              {task.status === 'Approved' && (
                <span className="text-[11px] font-semibold text-green-700">✓ {t('tasks.approvedByManager')}</span>
              )}
            </div>

            {expanded === task.id && (
              <div className="px-4 pb-3.5 animate-scale-in origin-top space-y-2.5">
                {task.description && (
                  <p className="text-[12px] text-body whitespace-pre-wrap leading-relaxed bg-light/50 rounded-lg border border-border/40 p-3">{task.description}</p>
                )}
                {task.collaborators?.length > 0 && (
                  <div className="flex flex-wrap items-center gap-1.5">
                    <span className="text-[10px] font-semibold text-muted uppercase tracking-[0.1em]">{t('tasks.collaborators')}:</span>
                    {task.collaborators.map((c) => (
                      <span key={c.id} className="text-[11px] font-medium text-dark bg-brand/15 border border-brand/40 rounded-full px-2.5 py-0.5">{c.name || c.email}</span>
                    ))}
                  </div>
                )}
                {task.consultation && (
                  <div className="flex flex-wrap items-center gap-2 bg-brand/10 border border-brand/40 rounded-lg p-3">
                    <div className="min-w-0 flex-1">
                      <p className="text-[12px] font-bold text-dark truncate">{task.consultation.reference} — {task.consultation.clientName}</p>
                      <p className="text-[11px] text-muted truncate">{task.consultation.fiscalQuestion}</p>
                    </div>
                    <Link to={`/app/consultations/${task.consultation.id}`} className="px-3 py-1.5 border border-border text-body text-[11px] font-bold rounded-md hover:border-brand/50 hover:bg-brand/5 transition-all">
                      {t('tasks.open')}
                    </Link>
                  </div>
                )}
                {submitting === task.id && task.status === 'InProgress' && (
                  <SubmitPanel task={task} onClose={() => setSubmitting(null)} onSubmitted={load} t={t} />
                )}
              </div>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}
