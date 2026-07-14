import { useCallback, useEffect, useState } from 'react';
import { reclamationService } from '../../services/authService';
import { useLanguage } from '../../context/LanguageContext';
import AdminLayout from '../../components/admin/AdminLayout';

const fmtDate = (d) => (d ? new Date(d).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' }) : '—');

const CATEGORY_TONES = {
  Bug: 'bg-red-50 text-red-600 border-red-200',
  Data: 'bg-blue-50 text-blue-700 border-blue-200',
  Feature: 'bg-purple-50 text-purple-700 border-purple-200',
  Access: 'bg-orange-50 text-orange-700 border-orange-200',
  Other: 'bg-light text-body border-border',
};

/* Inline resolve block — expands within the card, no popup. */
function ResolvePanel({ item, onResolved, t }) {
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  const submit = async () => {
    setBusy(true); setError('');
    try {
      await reclamationService.resolve(item.id, note.trim() || null);
      await onResolved();
    } catch (err) {
      setError(err.response?.data?.message || 'Failed.');
      setBusy(false);
    }
  };

  return (
    <div className="mt-2.5 pt-2.5 border-t border-border/40">
      <p className="text-[10px] font-semibold text-muted uppercase tracking-[0.1em] mb-1.5">{t('admin.recl.resolveTitle')}</p>
      {error && <div className="mb-2 p-2 bg-red-50 border border-red-200 rounded-lg text-red-600 text-[12px]">{error}</div>}
      <div className="flex flex-col sm:flex-row gap-2">
        <textarea
          value={note}
          onChange={(e) => setNote(e.target.value)}
          rows={2}
          placeholder={t('admin.recl.notePlaceholder')}
          className="flex-1 px-3 py-2 rounded-lg border border-border/60 bg-white text-[13px] text-dark placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-green-500 resize-none"
        />
        <button
          onClick={submit}
          disabled={busy}
          className="shrink-0 sm:self-stretch px-4 py-2 bg-green-600 text-white text-[13px] font-bold rounded-lg hover:bg-green-700 disabled:opacity-50 transition-colors flex items-center justify-center gap-1.5"
        >
          {busy ? '…' : <>✓ {t('admin.recl.markResolved')}</>}
        </button>
      </div>
    </div>
  );
}

export default function AdminReclamations() {
  const { t } = useLanguage();
  const [items, setItems] = useState(null);
  const [filter, setFilter] = useState('');       // '', 'Pending', 'Resolved'
  const [error, setError] = useState('');
  const [busyId, setBusyId] = useState(null);
  const [expanded, setExpanded] = useState(null);
  const [pendingCount, setPendingCount] = useState(0);

  const load = useCallback(async () => {
    try {
      const { data: res } = await reclamationService.all(filter);
      setItems(res.data);
      setError('');
    } catch (err) {
      setError(err.response?.data?.message || 'Failed to load reclamations.');
    }
  }, [filter]);

  // Keep the tab badge accurate regardless of the active filter.
  const loadPendingCount = useCallback(async () => {
    try {
      const { data: res } = await reclamationService.all('Pending');
      setPendingCount(res.data.length);
    } catch { /* ignore */ }
  }, []);

  useEffect(() => { load(); }, [load]);
  useEffect(() => { loadPendingCount(); }, [loadPendingCount, items]);

  const reopen = async (id) => {
    setBusyId(id);
    try { await reclamationService.reopen(id); await load(); }
    catch (err) { setError(err.response?.data?.message || 'Failed.'); }
    finally { setBusyId(null); }
  };

  const afterResolve = async () => { setExpanded(null); await load(); };

  return (
    <AdminLayout badges={{ '/admin/reclamations': pendingCount }}>
      <div className="space-y-4 animate-fade-up">
      <div className="flex flex-col sm:flex-row sm:items-end sm:justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold text-dark tracking-tight">{t('admin.recl.title')}</h1>
          <p className="text-[13px] text-muted mt-0.5">
            {t('admin.recl.subtitle')}{pendingCount > 0 && <span className="ml-1.5 font-bold text-dark">· {pendingCount} {t('admin.recl.pendingCount')}</span>}
          </p>
        </div>
        <div className="flex rounded-lg border border-border/60 bg-white overflow-hidden">
          {['', 'Pending', 'Resolved'].map((f) => (
            <button
              key={f}
              onClick={() => setFilter(f)}
              className={`px-3.5 py-2 text-[12px] font-semibold transition-colors ${filter === f ? (f === 'Resolved' ? 'bg-green-600 text-white' : f === 'Pending' ? 'bg-orange-500 text-white' : 'bg-dark text-white') : 'text-body hover:text-dark'}`}
            >
              {f === '' ? t('admin.recl.all') : f === 'Pending' ? t('admin.recl.pending') : t('admin.recl.resolved')}
            </button>
          ))}
        </div>
      </div>

      {error && <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-red-600 text-[13px]">{error}</div>}

      {!items && <div className="p-8 text-center text-muted text-[13px] bg-white rounded-xl border border-border/60">{t('admin.users.loading')}</div>}
      {items?.length === 0 && (
        <div className="p-10 text-center bg-white rounded-xl border border-border/60">
          <div className="w-12 h-12 mx-auto rounded-full bg-green-50 border border-green-200 flex items-center justify-center">
            <svg className="w-6 h-6 text-green-600" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path strokeLinecap="round" strokeLinejoin="round" d="M4.5 12.75l6 6 9-13.5" /></svg>
          </div>
          <p className="mt-3 text-[13px] font-semibold text-dark">{t('admin.recl.emptyTitle')}</p>
          <p className="text-[12px] text-muted">{t('admin.recl.emptyHint')}</p>
        </div>
      )}

      <div className="space-y-2.5">
        {items?.map((r) => {
          const isOpen = expanded === r.id;
          return (
            <div key={r.id} className={`bg-white rounded-xl border overflow-hidden transition-all ${r.status === 'Pending' ? 'border-orange-200' : 'border-border/40'}`}>
              <div className="px-4 py-3 flex flex-wrap items-center gap-2.5">
                <button
                  onClick={() => setExpanded(isOpen ? null : r.id)}
                  className="flex items-center gap-2.5 min-w-0 flex-1 text-left"
                >
                  <svg className={`w-4 h-4 shrink-0 text-muted transition-transform ${isOpen ? 'rotate-90' : ''}`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path strokeLinecap="round" strokeLinejoin="round" d="M8.25 4.5l7.5 7.5-7.5 7.5" /></svg>
                  <span className={`w-2 h-2 rounded-full shrink-0 ${r.status === 'Pending' ? 'bg-orange-400 animate-pulse' : 'bg-green-500'}`} />
                  <div className="min-w-0">
                    <p className="text-[13px] font-semibold text-dark truncate">{r.subject}</p>
                    <p className="text-[11px] text-muted truncate">
                      {r.createdBy?.name} · {r.createdBy?.email} · {fmtDate(r.createdAt)}
                    </p>
                  </div>
                </button>
                <span className={`text-[10px] font-semibold border rounded-md px-2 py-0.5 ${CATEGORY_TONES[r.category] || CATEGORY_TONES.Other}`}>{t(`support.cat.${r.category}`) || r.category}</span>
                {r.status === 'Pending' ? (
                  <span className="text-[10px] font-semibold text-orange-700 bg-orange-50 border border-orange-200 rounded-md px-2 py-0.5">{t('admin.recl.pending')}</span>
                ) : (
                  <span className="text-[10px] font-semibold text-green-700 bg-green-50 border border-green-200 rounded-md px-2 py-0.5">✓ {t('admin.recl.resolved')}</span>
                )}
                {r.status === 'Pending' ? (
                  <button
                    onClick={() => setExpanded(isOpen ? null : r.id)}
                    className="text-[11px] font-bold text-dark bg-brand rounded-md px-3 py-1.5 hover:shadow-md hover:shadow-brand/40 transition-all"
                  >{t('admin.recl.resolve')}</button>
                ) : (
                  <button
                    disabled={busyId === r.id}
                    onClick={() => reopen(r.id)}
                    className="text-[11px] font-medium text-body border border-border/60 rounded-md px-3 py-1.5 hover:border-dark/30 hover:text-dark disabled:opacity-50 transition-colors"
                  >{t('admin.recl.reopen')}</button>
                )}
              </div>
              {isOpen && (
                <div className="px-4 pb-3.5 animate-scale-in origin-top">
                  <div className="bg-light/50 rounded-lg border border-border/40 p-3.5">
                    <p className="text-[12px] text-body whitespace-pre-wrap leading-relaxed">{r.description}</p>
                    {r.adminNote && (
                      <div className="mt-2.5 pt-2.5 border-t border-border/40">
                        <p className="text-[10px] font-semibold text-muted uppercase tracking-[0.1em]">{t('admin.recl.adminNote')}</p>
                        <p className="mt-1 text-[12px] text-body">{r.adminNote}</p>
                      </div>
                    )}
                    {r.resolvedAt && <p className="mt-2 text-[11px] text-muted">{t('admin.recl.resolvedOn')} {fmtDate(r.resolvedAt)}</p>}
                    {r.status === 'Pending' && <ResolvePanel item={r} onResolved={afterResolve} t={t} />}
                  </div>
                </div>
              )}
            </div>
          );
        })}
      </div>
      </div>
    </AdminLayout>
  );
}
