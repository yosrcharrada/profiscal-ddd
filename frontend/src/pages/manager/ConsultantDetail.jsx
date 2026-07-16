import { useCallback, useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { taskService } from '../../services/authService';
import { useLanguage } from '../../context/LanguageContext';

const fmtDate = (d) => (d ? new Date(d).toLocaleDateString(undefined, { dateStyle: 'medium' }) : '—');
const fmtTime = (d) => (d ? new Date(d).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' }) : '—');

export const STATUS_TONES = {
  Pending: 'bg-light text-body border-border',
  InProgress: 'bg-blue-50 text-blue-700 border-blue-200',
  Submitted: 'bg-brand/20 text-dark border-brand/50',
  Approved: 'bg-green-50 text-green-700 border-green-200',
};

export const PRIORITY_TONES = {
  Low: 'text-muted',
  Medium: 'text-blue-600',
  High: 'text-orange-600',
  Urgent: 'text-red-600',
};

/* ───────────── Inline assign-task panel (no popup) ───────────── */
function AssignTaskPanel({ consultant, onClose, onCreated, t }) {
  const [form, setForm] = useState({ title: '', description: '', clientName: '', priority: 'Medium', dueDate: '' });
  const [emails, setEmails] = useState([]);
  const [team, setTeam] = useState([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const set = (k) => (e) => setForm((f) => ({ ...f, [k]: e.target.value }));

  // Collaborators are picked from the manager's real consultants (not free-typed) so the
  // invite always resolves to an account — otherwise the backend silently skips an unknown
  // email and the collaborator never sees the task.
  useEffect(() => {
    taskService.consultants()
      .then(({ data }) => setTeam(data.data || []))
      .catch(() => setTeam([]));
  }, []);

  const available = team.filter((c) => c.id !== consultant.id && !emails.includes(c.email));
  const nameFor = (email) => {
    const c = team.find((x) => x.email === email);
    return c ? (`${c.firstName} ${c.lastName}`.trim() || email) : email;
  };
  const addCollaborator = (email) => {
    if (email && !emails.includes(email)) setEmails((e) => [...e, email]);
  };

  const submit = async (e) => {
    e.preventDefault();
    if (!form.title.trim()) { setError(t('manager.assign.titleRequired')); return; }
    setBusy(true); setError('');
    try {
      const { data: res } = await taskService.create({
        title: form.title.trim(),
        description: form.description.trim(),
        clientName: form.clientName.trim() || null,
        consultantId: consultant.id,
        priority: form.priority,
        dueDate: form.dueDate || null,
        collaboratorEmails: emails,
      });
      const skipped = res.data?.skippedCollaborators || [];
      onCreated(skipped);
      onClose();
    } catch (err) {
      setError(err.response?.data?.message || t('manager.assign.failed'));
      setBusy(false);
    }
  };

  const inputCls = 'w-full px-3 py-2.5 rounded-lg border border-border/60 bg-white text-[13px] font-medium text-dark placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent transition-all';

  return (
    <div className="bg-cream rounded-xl border border-brand/50 overflow-hidden animate-scale-in origin-top shadow-card">
      <div className="h-[3px] bg-gradient-to-r from-brand via-orange-400 to-brand" />
      <form onSubmit={submit} className="p-4 sm:p-5">
          <div className="flex items-start justify-between">
            <div className="flex items-center gap-2.5">
              <span className="w-9 h-9 rounded-lg bg-brand/20 text-dark flex items-center justify-center">
                <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}><path strokeLinecap="round" strokeLinejoin="round" d="M12 4.5v15m7.5-7.5h-15" /></svg>
              </span>
              <div>
                <h2 className="text-[14px] font-bold text-dark">{t('manager.assign.title')}</h2>
                <p className="text-[12px] text-muted">
                  {t('manager.assign.for')} <span className="font-semibold text-dark">{consultant.firstName} {consultant.lastName}</span>
                </p>
              </div>
            </div>
            <button type="button" onClick={onClose} className="w-7 h-7 rounded-md text-muted hover:text-dark hover:bg-light flex items-center justify-center transition-colors">
              <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" /></svg>
            </button>
          </div>

          {error && <div className="mt-3 p-2.5 bg-red-50 border border-red-200 rounded-lg text-red-600 text-[12px]">{error}</div>}

          <div className="mt-4 space-y-3">
            <div className="grid sm:grid-cols-2 gap-3">
              <div>
                <label className="block text-[11px] font-semibold text-muted uppercase tracking-[0.08em] mb-1.5">{t('manager.assign.taskTitle')}</label>
                <input value={form.title} onChange={set('title')} placeholder={t('manager.assign.taskTitlePh')} className={inputCls} />
              </div>
              <div>
                <label className="block text-[11px] font-semibold text-muted uppercase tracking-[0.08em] mb-1.5">{t('manager.assign.client')}</label>
                <input value={form.clientName} onChange={set('clientName')} placeholder="—" className={inputCls} />
              </div>
            </div>
            <div>
              <label className="block text-[11px] font-semibold text-muted uppercase tracking-[0.08em] mb-1.5">{t('manager.assign.description')}</label>
              <textarea value={form.description} onChange={set('description')} rows={2} placeholder={t('manager.assign.descriptionPh')} className={`${inputCls} resize-none`} />
            </div>
            <div className="grid grid-cols-2 sm:grid-cols-3 gap-3">
              <div>
                <label className="block text-[11px] font-semibold text-muted uppercase tracking-[0.08em] mb-1.5">{t('manager.assign.priority')}</label>
                <select value={form.priority} onChange={set('priority')} className={inputCls}>
                  <option value="Low">{t('tasks.priority.Low')}</option>
                  <option value="Medium">{t('tasks.priority.Medium')}</option>
                  <option value="High">{t('tasks.priority.High')}</option>
                  <option value="Urgent">{t('tasks.priority.Urgent')}</option>
                </select>
              </div>
              <div>
                <label className="block text-[11px] font-semibold text-muted uppercase tracking-[0.08em] mb-1.5">{t('manager.assign.dueDate')}</label>
                <input type="date" value={form.dueDate} onChange={set('dueDate')} className={inputCls} />
              </div>
              <div className="col-span-2 sm:col-span-1">
                <label className="block text-[11px] font-semibold text-muted uppercase tracking-[0.08em] mb-1.5">{t('manager.assign.collaborators')}</label>
                <select
                  value=""
                  onChange={(e) => { addCollaborator(e.target.value); e.target.value = ''; }}
                  disabled={available.length === 0}
                  className={`${inputCls} disabled:opacity-60`}
                >
                  <option value="" disabled>
                    {available.length === 0 ? t('manager.assign.noCollaborators') : t('manager.assign.pickCollaborator')}
                  </option>
                  {available.map((c) => (
                    <option key={c.id} value={c.email}>
                      {`${c.firstName} ${c.lastName}`.trim()} — {c.email}
                    </option>
                  ))}
                </select>
              </div>
            </div>

            {emails.length > 0 && (
              <div className="flex flex-wrap gap-1.5">
                {emails.map((e) => (
                  <span key={e} className="inline-flex items-center gap-1 text-[11px] font-medium text-dark bg-brand/15 border border-brand/40 rounded-full pl-2.5 pr-1 py-0.5">
                    {nameFor(e)}
                    <button type="button" onClick={() => setEmails((l) => l.filter((x) => x !== e))} className="w-4 h-4 rounded-full hover:bg-brand/40 flex items-center justify-center">
                      <svg className="w-2.5 h-2.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2.5}><path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" /></svg>
                    </button>
                  </span>
                ))}
              </div>
            )}
            <p className="text-[10px] text-muted">{t('manager.assign.collaboratorsHint')}</p>
          </div>

          <div className="mt-4 flex justify-end gap-2">
            <button type="button" onClick={onClose} className="px-4 py-2.5 border border-border/60 text-[13px] font-semibold text-body rounded-lg hover:border-dark/30 hover:text-dark transition-colors">
              {t('admin.recl.cancel')}
            </button>
            <button type="submit" disabled={busy} className="px-5 py-2.5 bg-brand text-dark text-[13px] font-bold rounded-lg hover:shadow-lg hover:shadow-brand/40 disabled:opacity-50 transition-all flex items-center justify-center gap-2">
              {busy && <span className="w-3.5 h-3.5 border-2 border-dark border-t-transparent rounded-full animate-spin" />}
              {busy ? t('manager.assign.assigning') : t('manager.assign.assign')}
            </button>
          </div>
        </form>
    </div>
  );
}

/* ───────────── Task card (manager view) ───────────── */
function TaskCard({ task, onAction, busy, t }) {
  const [open, setOpen] = useState(false);
  return (
    <div className="bg-white rounded-xl border border-border/60 overflow-hidden hover:border-dark/20 transition-colors">
      <button onClick={() => setOpen(!open)} className="w-full px-4 py-3 flex flex-wrap items-center gap-2.5 text-left">
        <div className="min-w-0 flex-1">
          <p className="text-[13px] font-bold text-dark truncate">{task.title}</p>
          <p className="text-[11px] text-muted truncate">
            {task.clientName && <span>{task.clientName} · </span>}
            {t('tasks.assignedOn')} {fmtDate(task.createdAt)}
            {task.dueDate && <span> · {t('tasks.due')} {fmtDate(task.dueDate)}</span>}
          </p>
        </div>
        <span className={`text-[10px] font-bold ${PRIORITY_TONES[task.priority] || ''}`}>
          {task.priority === 'Urgent' ? '●●●' : task.priority === 'High' ? '●●' : '●'} {t(`tasks.priority.${task.priority}`)}
        </span>
        <span className={`text-[10px] font-semibold border rounded-md px-2 py-0.5 ${STATUS_TONES[task.status]}`}>
          {t(`tasks.status.${task.status}`)}
        </span>
      </button>

      {open && (
        <div className="px-4 pb-3.5 animate-scale-in origin-top space-y-3">
          {task.description && <p className="text-[12px] text-body whitespace-pre-wrap leading-relaxed bg-light/50 rounded-lg border border-border/40 p-3">{task.description}</p>}

          {task.collaborators?.length > 0 && (
            <div className="flex flex-wrap items-center gap-1.5">
              <span className="text-[10px] font-semibold text-muted uppercase tracking-[0.1em]">{t('tasks.collaborators')}:</span>
              {task.collaborators.map((c) => (
                <span key={c.id} className="text-[11px] font-medium text-dark bg-brand/15 border border-brand/40 rounded-full px-2.5 py-0.5">{c.name || c.email}</span>
              ))}
            </div>
          )}

          {/* progress timeline */}
          <div className="flex items-center gap-1.5 text-[10px] text-muted">
            <span className={task.createdAt ? 'text-dark font-semibold' : ''}>{t('tasks.timeline.assigned')} {fmtTime(task.createdAt)}</span>
            <span>→</span>
            <span className={task.startedAt ? 'text-dark font-semibold' : ''}>{task.startedAt ? `${t('tasks.timeline.started')} ${fmtTime(task.startedAt)}` : t('tasks.timeline.notStarted')}</span>
            <span>→</span>
            <span className={task.submittedAt ? 'text-dark font-semibold' : ''}>{task.submittedAt ? `${t('tasks.timeline.submitted')} ${fmtTime(task.submittedAt)}` : t('tasks.timeline.notSubmitted')}</span>
          </div>

          {task.status === 'Submitted' || task.status === 'Approved' ? (
            <div className="bg-brand/10 border border-brand/40 rounded-lg p-3">
              <p className="text-[10px] font-semibold text-muted uppercase tracking-[0.1em] mb-1.5">{t('tasks.deliverable')}</p>
              {task.consultation ? (
                <div className="flex flex-wrap items-center gap-2">
                  <div className="min-w-0 flex-1">
                    <p className="text-[12px] font-bold text-dark truncate">{task.consultation.reference} — {task.consultation.clientName}</p>
                    <p className="text-[11px] text-muted truncate">{task.consultation.fiscalQuestion}</p>
                  </div>
                  <Link
                    to={`/app/consultations/${task.consultation.id}`}
                    className="px-3 py-1.5 border border-border text-body text-[11px] font-bold rounded-md hover:border-brand/50 hover:bg-brand/5 transition-all inline-flex items-center gap-1.5"
                  >
                    <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path strokeLinecap="round" strokeLinejoin="round" d="M2.036 12.322a1.012 1.012 0 010-.639C3.423 7.51 7.36 4.5 12 4.5c4.638 0 8.573 3.007 9.963 7.178.07.207.07.431 0 .639C20.577 16.49 16.64 19.5 12 19.5c-4.638 0-8.573-3.007-9.963-7.178z" /><path strokeLinecap="round" strokeLinejoin="round" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" /></svg>
                    {t('tasks.viewExport')}
                  </Link>
                </div>
              ) : (
                <p className="text-[11px] text-muted">{t('tasks.noConsultationLinked')}</p>
              )}
              {task.submitNote && <p className="mt-2 text-[11px] text-body italic">« {task.submitNote} »</p>}
            </div>
          ) : null}

          <div className="flex gap-1.5 justify-end">
            {task.status === 'Submitted' && (
              <>
                <button disabled={busy} onClick={() => onAction('reopen', task)} className="text-[11px] font-medium text-body border border-border/60 rounded-md px-3 py-1.5 hover:border-dark/30 hover:text-dark disabled:opacity-50 transition-colors">
                  {t('tasks.sendBack')}
                </button>
                <button disabled={busy} onClick={() => onAction('approve', task)} className="text-[11px] font-bold text-white bg-green-600 rounded-md px-3 py-1.5 hover:bg-green-700 disabled:opacity-50 transition-colors">
                  ✓ {t('tasks.approve')}
                </button>
              </>
            )}
            {(task.status === 'Pending' || task.status === 'InProgress') && (
              <button disabled={busy} onClick={() => onAction('delete', task)} className="text-[11px] font-medium text-red-600 border border-red-200 bg-red-50 rounded-md px-3 py-1.5 hover:bg-red-100 disabled:opacity-50 transition-colors">
                {t('tasks.delete')}
              </button>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

/* ───────────── Page ───────────── */
export default function ConsultantDetail() {
  const { id } = useParams();
  const { t } = useLanguage();
  const [data, setData] = useState(null);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [showAssign, setShowAssign] = useState(false);
  const [busy, setBusy] = useState(false);
  const [statusFilter, setStatusFilter] = useState('');

  const load = useCallback(async () => {
    try {
      const { data: res } = await taskService.consultantDetail(id);
      setData(res.data);
      setError('');
    } catch (err) {
      setError(err.response?.data?.message || 'Failed to load consultant.');
    }
  }, [id]);

  useEffect(() => { load(); }, [load]);

  const onAction = async (action, task) => {
    setBusy(true); setError('');
    try {
      if (action === 'approve') await taskService.approve(task.id);
      else if (action === 'reopen') await taskService.reopen(task.id);
      else if (action === 'delete') await taskService.remove(task.id);
      await load();
    } catch (err) {
      setError(err.response?.data?.message || 'Action failed.');
    } finally {
      setBusy(false);
    }
  };

  if (error && !data) return <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-red-600 text-[13px]">{error}</div>;
  if (!data) return <div className="h-40 bg-light rounded-xl animate-pulse" />;

  const { consultant, tasks } = data;
  const filtered = tasks.filter((tk) => !statusFilter || tk.status === statusFilter);
  const counts = {
    Submitted: tasks.filter((x) => x.status === 'Submitted').length,
  };

  return (
    <div className="space-y-4 animate-fade-up">
      {/* Profile header */}
      <div className="bg-cream rounded-xl border border-border/60 p-4 sm:p-5 flex flex-wrap items-center gap-4">
        <span className="w-14 h-14 rounded-2xl bg-brand/25 text-dark flex items-center justify-center text-[17px] font-extrabold">
          {`${consultant.firstName?.[0] ?? ''}${consultant.lastName?.[0] ?? ''}`.toUpperCase()}
        </span>
        <div className="min-w-0 flex-1">
          <h1 className="text-lg font-bold text-dark tracking-tight">{consultant.firstName} {consultant.lastName}</h1>
          <p className="text-[12px] text-muted">{consultant.email} · {t('manager.detail.memberSince')} {fmtDate(consultant.createdAt)}</p>
        </div>
        <button
          onClick={() => setShowAssign((v) => !v)}
          className={`px-4 py-2.5 text-[13px] font-bold rounded-lg transition-all inline-flex items-center gap-1.5 ${showAssign ? 'border border-border text-body hover:border-brand/50 hover:bg-brand/5' : 'bg-brand text-dark hover:shadow-lg hover:shadow-brand/40'}`}
        >
          <svg className={`w-4 h-4 transition-transform ${showAssign ? 'rotate-45' : ''}`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path strokeLinecap="round" strokeLinejoin="round" d="M12 4.5v15m7.5-7.5h-15" /></svg>
          {showAssign ? t('admin.recl.cancel') : t('manager.detail.assignTask')}
        </button>
      </div>

      {showAssign && (
        <AssignTaskPanel
          consultant={consultant}
          onClose={() => setShowAssign(false)}
          onCreated={(skipped) => {
            load();
            setShowAssign(false);
            setNotice(skipped.length > 0 ? `${t('manager.assign.skipped')}: ${skipped.join(', ')}` : '');
          }}
          t={t}
        />
      )}

      {error && <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-red-600 text-[13px]">{error}</div>}
      {notice && <div className="p-3 bg-brand/10 border border-brand/40 rounded-lg text-dark text-[13px]">{notice}</div>}

      {/* Task list */}
      <div className="flex items-center justify-between">
        <h2 className="text-[13px] font-bold text-dark uppercase tracking-[0.08em]">
          {t('manager.detail.tasks')} <span className="text-muted font-medium">({filtered.length})</span>
          {counts.Submitted > 0 && !statusFilter && (
            <span className="ml-2 text-[10px] font-bold text-dark bg-brand rounded-full px-2 py-0.5 normal-case">{counts.Submitted} {t('manager.detail.awaitingReview')}</span>
          )}
        </h2>
        <select
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value)}
          className="px-2.5 py-1.5 rounded-lg border border-border/60 bg-white text-[12px] font-medium text-dark focus:outline-none focus:ring-2 focus:ring-brand"
        >
          <option value="">{t('manager.detail.allStatuses')}</option>
          {['Pending', 'InProgress', 'Submitted', 'Approved'].map((s) => (
            <option key={s} value={s}>{t(`tasks.status.${s}`)}</option>
          ))}
        </select>
      </div>

      {filtered.length === 0 && (
        <div className="p-8 text-center bg-white rounded-xl border border-border/60">
          <p className="text-[13px] font-semibold text-dark">{t('manager.detail.noTasks')}</p>
          <p className="mt-1 text-[12px] text-muted">{t('manager.detail.noTasksHint')}</p>
        </div>
      )}

      <div className="space-y-2.5">
        {filtered.map((task) => (
          <TaskCard key={task.id} task={task} onAction={onAction} busy={busy} t={t} />
        ))}
      </div>
    </div>
  );
}
