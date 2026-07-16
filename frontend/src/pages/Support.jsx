import { useCallback, useEffect, useState } from 'react';
import { reclamationService } from '../services/authService';
import { useLanguage } from '../context/LanguageContext';

/* Support space — same Chrome-tab shell as the manager → consultants space:
   one fixed "Support" tab (report form + my reclamations) plus a closeable
   onglet per reclamation the user opens. */

const fmtDate = (d) => (d ? new Date(d).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' }) : '—');

const CATEGORIES = ['Bug', 'Data', 'Feature', 'Access', 'Other'];

const CATEGORY_TONES = {
  Bug: 'bg-red-50 text-red-600 border-red-200',
  Data: 'bg-blue-50 text-blue-700 border-blue-200',
  Feature: 'bg-purple-50 text-purple-700 border-purple-200',
  Access: 'bg-orange-50 text-orange-700 border-orange-200',
  Other: 'bg-light text-body border-border',
};

const TAB_BASE =
  'relative flex items-center gap-1.5 text-[12.5px] font-semibold px-4 py-2 rounded-t-lg whitespace-nowrap transition-all duration-200';
const TAB_ACTIVE = 'bg-white text-dark border border-border border-b-white -mb-px z-10';
const TAB_IDLE = 'text-muted hover:text-dark hover:bg-white/50 border border-transparent';

const STORAGE_KEY = 'support.tabs';

const readStore = () => {
  try {
    return JSON.parse(sessionStorage.getItem(STORAGE_KEY) || '[]');
  } catch {
    return [];
  }
};

const SUPPORT_ICON =
  'M9.879 7.519c1.171-1.025 3.071-1.025 4.242 0 1.172 1.025 1.172 2.687 0 3.712-.203.179-.43.326-.67.442-.745.361-1.45.999-1.45 1.827v.75M21 12a9 9 0 11-18 0 9 9 0 0118 0zm-9 5.25h.008v.008H12v-.008z';
const TICKET_ICON =
  'M16.5 6v.75m0 3v.75m0 3v.75m0 3V18m-12-.75h.008v.008H4.5v-.008zM19.5 6h-15A2.25 2.25 0 002.25 8.25v2.25a2.25 2.25 0 010 3v2.25A2.25 2.25 0 004.5 18h15a2.25 2.25 0 002.25-2.25V13.5a2.25 2.25 0 010-3V8.25A2.25 2.25 0 0019.5 6z';

/* ───────────── one reclamation, full-page detail (its own tab) ───────────── */
function ReclamationDetail({ recl, t }) {
  return (
    <div className="max-w-2xl mx-auto space-y-4 animate-fade-up">
      <div className="bg-cream rounded-xl border border-border/60 overflow-hidden">
        <div className={`h-[3px] ${recl.status === 'Pending' ? 'bg-orange-400' : 'bg-green-500'}`} />
        <div className="p-5">
          <div className="flex flex-wrap items-center gap-2 mb-3">
            <span className={`text-[10px] font-semibold border rounded-md px-2 py-0.5 ${CATEGORY_TONES[recl.category] || CATEGORY_TONES.Other}`}>
              {t(`support.cat.${recl.category}`) || recl.category}
            </span>
            {recl.status === 'Pending' ? (
              <span className="text-[10px] font-semibold text-orange-700 bg-orange-50 border border-orange-200 rounded-md px-2 py-0.5">
                {t('support.pending')}
              </span>
            ) : (
              <span className="text-[10px] font-semibold text-green-700 bg-green-50 border border-green-200 rounded-md px-2 py-0.5">
                ✓ {t('support.resolved')}
              </span>
            )}
            <span className="text-[11px] text-muted ml-auto">{fmtDate(recl.createdAt)}</span>
          </div>
          <h1 className="text-lg font-bold text-dark tracking-tight mb-3">{recl.subject}</h1>
          <p className="text-[13px] text-body whitespace-pre-wrap leading-relaxed bg-white rounded-lg border border-border/40 p-4">
            {recl.description}
          </p>
        </div>
      </div>

      {recl.adminNote ? (
        <div className="bg-brand/10 border border-brand/40 rounded-xl p-4">
          <p className="text-[10px] font-semibold text-muted uppercase tracking-[0.1em] mb-1.5 flex items-center gap-1.5">
            <svg className="w-3.5 h-3.5 text-dark" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M7.5 8.25h9m-9 3H12m-9.75 1.51c0 1.6 1.123 2.994 2.707 3.227 1.087.16 2.185.283 3.293.369V21l4.076-4.076a1.526 1.526 0 011.037-.443 48.282 48.282 0 005.68-.494c1.584-.233 2.707-1.626 2.707-3.228V6.741c0-1.602-1.123-2.995-2.707-3.228A48.394 48.394 0 0012 3c-2.392 0-4.744.175-7.043.513C3.373 3.746 2.25 5.14 2.25 6.741v6.018z" />
            </svg>
            {t('support.teamReply')}
          </p>
          <p className="text-[13px] text-dark leading-relaxed">{recl.adminNote}</p>
        </div>
      ) : (
        <div className="bg-white border border-border/60 rounded-xl p-4 flex items-center gap-2.5 text-[12.5px] text-muted">
          <span className="w-2 h-2 rounded-full bg-orange-400 animate-pulse shrink-0" />
          {t('support.awaitingReply')}
        </div>
      )}
    </div>
  );
}

/* ───────────── fixed tab: report form + list of my reclamations ───────────── */
function SupportHome({ mine, onOpen, onSent, t }) {
  const [form, setForm] = useState({ subject: '', description: '', category: 'Bug' });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [sent, setSent] = useState(false);

  const submit = async (e) => {
    e.preventDefault();
    if (!form.subject.trim() || !form.description.trim()) { setError(t('support.required')); return; }
    setBusy(true); setError('');
    try {
      await reclamationService.create(form);
      setForm({ subject: '', description: '', category: 'Bug' });
      setSent(true);
      setTimeout(() => setSent(false), 3200);
      await onSent();
    } catch (err) {
      setError(err.response?.data?.message || t('support.failed'));
    } finally {
      setBusy(false);
    }
  };

  const inputCls = 'w-full px-3 py-2.5 rounded-lg border border-border/60 bg-white text-[13px] font-medium text-dark placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent transition-all';

  return (
    <div className="space-y-4 animate-fade-up">
      <div>
        <h1 className="text-xl font-bold text-dark tracking-tight">{t('support.title')}</h1>
        <p className="text-[13px] text-muted mt-0.5">{t('support.subtitle')}</p>
      </div>

      <div className="grid lg:grid-cols-[1fr,1.2fr] gap-3 items-start">
        {/* Report form */}
        <div className="bg-cream rounded-xl border border-border/60 overflow-hidden">
          <div className="h-[3px] bg-brand" />
          <form onSubmit={submit} className="p-4 sm:p-5 space-y-3">
            <h2 className="text-[13px] font-bold text-dark uppercase tracking-[0.08em]">{t('support.reportTitle')}</h2>

            {error && <div className="p-2.5 bg-red-50 border border-red-200 rounded-lg text-red-600 text-[12px]">{error}</div>}
            {sent && (
              <div className="p-2.5 bg-green-50 border border-green-200 rounded-lg text-green-700 text-[12px] animate-fade-in">
                ✓ {t('support.sent')}
              </div>
            )}

            <div>
              <label className="block text-[11px] font-semibold text-muted uppercase tracking-[0.08em] mb-1.5">{t('support.category')}</label>
              <div className="flex flex-wrap gap-1.5">
                {CATEGORIES.map((c) => (
                  <button
                    key={c}
                    type="button"
                    onClick={() => setForm((f) => ({ ...f, category: c }))}
                    className={`px-3 py-1.5 rounded-lg border text-[12px] font-semibold transition-all ${form.category === c ? 'bg-brand text-black border-brand' : 'bg-white text-body border-border/60 hover:border-brand/50 hover:bg-brand/5'}`}
                  >
                    {t(`support.cat.${c}`)}
                  </button>
                ))}
              </div>
            </div>

            <div>
              <label className="block text-[11px] font-semibold text-muted uppercase tracking-[0.08em] mb-1.5">{t('support.subject')}</label>
              <input value={form.subject} onChange={(e) => setForm((f) => ({ ...f, subject: e.target.value }))} placeholder={t('support.subjectPh')} className={inputCls} />
            </div>

            <div>
              <label className="block text-[11px] font-semibold text-muted uppercase tracking-[0.08em] mb-1.5">{t('support.description')}</label>
              <textarea
                value={form.description}
                onChange={(e) => setForm((f) => ({ ...f, description: e.target.value }))}
                rows={5}
                placeholder={t('support.descriptionPh')}
                className={`${inputCls} resize-none`}
              />
            </div>

            <button type="submit" disabled={busy} className="w-full py-2.5 bg-brand text-dark text-[13px] font-bold rounded-lg hover:shadow-lg hover:shadow-brand/40 disabled:opacity-50 transition-all flex items-center justify-center gap-2">
              {busy && <span className="w-3.5 h-3.5 border-2 border-dark border-t-transparent rounded-full animate-spin" />}
              {busy ? t('support.sending') : t('support.send')}
            </button>
          </form>
        </div>

        {/* My reclamations — click one to open its tab */}
        <div className="bg-white rounded-xl border border-border/60 overflow-hidden">
          <div className="px-4 py-3 border-b border-border/40 flex items-center justify-between">
            <p className="text-[11px] font-semibold text-muted uppercase tracking-[0.1em]">{t('support.myReports')}</p>
            {mine?.length > 0 && (
              <span className="text-[10px] font-bold text-muted bg-light rounded-full px-2 py-0.5">{mine.length}</span>
            )}
          </div>
          {!mine && <p className="p-4 text-[12px] text-muted">{t('admin.users.loading')}</p>}
          {mine?.length === 0 && (
            <div className="p-8 text-center">
              <p className="text-[13px] font-semibold text-dark">{t('support.emptyTitle')}</p>
              <p className="mt-1 text-[12px] text-muted">{t('support.emptyHint')}</p>
            </div>
          )}
          {mine?.map((r) => (
            <button
              key={r.id}
              onClick={() => onOpen(r)}
              className="group w-full text-left px-4 py-3 border-b border-border/30 last:border-0 hover:bg-brand/5 transition-colors"
            >
              <div className="flex flex-wrap items-center gap-2">
                <span className={`w-2 h-2 rounded-full shrink-0 ${r.status === 'Pending' ? 'bg-orange-400' : 'bg-green-500'}`} />
                <p className="text-[13px] font-semibold text-dark truncate flex-1 group-hover:underline">{r.subject}</p>
                <span className={`text-[10px] font-semibold border rounded-md px-2 py-0.5 ${CATEGORY_TONES[r.category] || CATEGORY_TONES.Other}`}>{t(`support.cat.${r.category}`) || r.category}</span>
                {r.status === 'Pending'
                  ? <span className="text-[10px] font-semibold text-orange-700 bg-orange-50 border border-orange-200 rounded-md px-2 py-0.5">{t('support.pending')}</span>
                  : <span className="text-[10px] font-semibold text-green-700 bg-green-50 border border-green-200 rounded-md px-2 py-0.5">✓ {t('support.resolved')}</span>}
                <svg className="w-3.5 h-3.5 text-border group-hover:text-brand group-hover:translate-x-0.5 transition-all shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M8.25 4.5l7.5 7.5-7.5 7.5" />
                </svg>
              </div>
              <p className="mt-1 text-[11px] text-muted">{fmtDate(r.createdAt)}</p>
            </button>
          ))}
        </div>
      </div>
    </div>
  );
}

/* ───────────── shell ───────────── */
export default function Support() {
  const { t } = useLanguage();
  const [mine, setMine] = useState(null);
  const [tabs, setTabs] = useState(readStore); // [{ id, subject }]
  const [activeId, setActiveId] = useState(null); // null = fixed Support tab

  const load = useCallback(async () => {
    try {
      const { data: res } = await reclamationService.mine();
      setMine(res.data);
    } catch { setMine([]); }
  }, []);

  useEffect(() => { load(); }, [load]);

  useEffect(() => {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(tabs));
  }, [tabs]);

  const openTab = (recl) => {
    setTabs((prev) => (prev.some((x) => x.id === recl.id) ? prev : [...prev, { id: recl.id, subject: recl.subject }]));
    setActiveId(recl.id);
  };

  const closeTab = (id) => {
    setTabs((prev) => {
      const next = prev.filter((x) => x.id !== id);
      if (activeId === id) {
        const fallback = next[next.length - 1];
        setActiveId(fallback ? fallback.id : null);
      }
      return next;
    });
  };

  const activeRecl = activeId ? (mine || []).find((r) => r.id === activeId) : null;
  const homeActive = !activeId || !activeRecl;

  return (
    <div className="h-full flex flex-col bg-sand">
      {/* Chrome-style tab bar */}
      <div className="shrink-0 flex items-end bg-sand px-2 pt-1.5 border-b border-border overflow-x-auto scrollbar-hide">
        {/* Fixed "Support" tab */}
        <button
          onClick={() => setActiveId(null)}
          className={`${TAB_BASE} ${homeActive ? TAB_ACTIVE : TAB_IDLE}`}
        >
          <svg className={`w-3.5 h-3.5 shrink-0 ${homeActive ? 'text-brand' : ''}`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d={SUPPORT_ICON} />
          </svg>
          {t('support.tabHome')}
          {homeActive && <span className="absolute bottom-0 left-2 right-2 h-[2px] rounded-full bg-brand" />}
        </button>

        {/* One closeable onglet per opened reclamation */}
        {tabs.map((tab) => {
          const active = activeId === tab.id && Boolean(activeRecl);
          return (
            <button
              key={tab.id}
              onClick={() => setActiveId(tab.id)}
              className={`${TAB_BASE} ${active ? TAB_ACTIVE : TAB_IDLE} max-w-[220px]`}
            >
              <svg className={`w-3.5 h-3.5 shrink-0 ${active ? 'text-brand' : ''}`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d={TICKET_ICON} />
              </svg>
              <span className="truncate">{tab.subject}</span>
              <span
                role="button"
                tabIndex={0}
                onClick={(e) => { e.stopPropagation(); closeTab(tab.id); }}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') { e.stopPropagation(); closeTab(tab.id); }
                }}
                className="ml-0.5 w-4 h-4 rounded-md flex items-center justify-center text-muted hover:text-dark hover:bg-dark/10 transition-colors shrink-0"
                aria-label={t('chat.closeTab')}
              >
                <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2.5}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                </svg>
              </span>
              {active && <span className="absolute bottom-0 left-2 right-2 h-[2px] rounded-full bg-brand" />}
            </button>
          );
        })}
      </div>

      {/* White content area */}
      <div className="flex-1 min-h-0 overflow-y-auto bg-white">
        <div className="w-full max-w-[1100px] mx-auto px-4 sm:px-6 py-8">
          {homeActive ? (
            <SupportHome mine={mine} onOpen={openTab} onSent={load} t={t} />
          ) : (
            <ReclamationDetail recl={activeRecl} t={t} />
          )}
        </div>
      </div>
    </div>
  );
}
