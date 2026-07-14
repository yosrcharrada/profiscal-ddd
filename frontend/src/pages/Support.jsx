import { useCallback, useEffect, useState } from 'react';
import { reclamationService } from '../services/authService';
import { useLanguage } from '../context/LanguageContext';

const fmtDate = (d) => (d ? new Date(d).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' }) : '—');

const CATEGORIES = ['Bug', 'Data', 'Feature', 'Access', 'Other'];

const CATEGORY_TONES = {
  Bug: 'bg-red-50 text-red-600 border-red-200',
  Data: 'bg-blue-50 text-blue-700 border-blue-200',
  Feature: 'bg-purple-50 text-purple-700 border-purple-200',
  Access: 'bg-orange-50 text-orange-700 border-orange-200',
  Other: 'bg-light text-body border-border',
};

export default function Support() {
  const { t } = useLanguage();
  const [form, setForm] = useState({ subject: '', description: '', category: 'Bug' });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [sent, setSent] = useState(false);
  const [mine, setMine] = useState(null);

  const load = useCallback(async () => {
    try {
      const { data: res } = await reclamationService.mine();
      setMine(res.data);
    } catch { setMine([]); }
  }, []);

  useEffect(() => { load(); }, [load]);

  const submit = async (e) => {
    e.preventDefault();
    if (!form.subject.trim() || !form.description.trim()) { setError(t('support.required')); return; }
    setBusy(true); setError('');
    try {
      await reclamationService.create(form);
      setForm({ subject: '', description: '', category: 'Bug' });
      setSent(true);
      setTimeout(() => setSent(false), 3200);
      await load();
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
                    className={`px-3 py-1.5 rounded-lg border text-[12px] font-semibold transition-all ${form.category === c ? 'bg-dark text-white border-dark' : 'bg-white text-body border-border/60 hover:border-dark/30'}`}
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

        {/* My reclamations */}
        <div className="bg-white rounded-xl border border-border/60 overflow-hidden">
          <div className="px-4 py-3 border-b border-border/40">
            <p className="text-[11px] font-semibold text-muted uppercase tracking-[0.1em]">{t('support.myReports')}</p>
          </div>
          {!mine && <p className="p-4 text-[12px] text-muted">{t('admin.users.loading')}</p>}
          {mine?.length === 0 && (
            <div className="p-8 text-center">
              <p className="text-[13px] font-semibold text-dark">{t('support.emptyTitle')}</p>
              <p className="mt-1 text-[12px] text-muted">{t('support.emptyHint')}</p>
            </div>
          )}
          {mine?.map((r) => (
            <div key={r.id} className="px-4 py-3 border-b border-border/30 last:border-0">
              <div className="flex flex-wrap items-center gap-2">
                <span className={`w-2 h-2 rounded-full shrink-0 ${r.status === 'Pending' ? 'bg-orange-400' : 'bg-green-500'}`} />
                <p className="text-[13px] font-semibold text-dark truncate flex-1">{r.subject}</p>
                <span className={`text-[10px] font-semibold border rounded-md px-2 py-0.5 ${CATEGORY_TONES[r.category] || CATEGORY_TONES.Other}`}>{t(`support.cat.${r.category}`) || r.category}</span>
                {r.status === 'Pending'
                  ? <span className="text-[10px] font-semibold text-orange-700 bg-orange-50 border border-orange-200 rounded-md px-2 py-0.5">{t('support.pending')}</span>
                  : <span className="text-[10px] font-semibold text-green-700 bg-green-50 border border-green-200 rounded-md px-2 py-0.5">✓ {t('support.resolved')}</span>}
              </div>
              <p className="mt-1 text-[11px] text-muted">{fmtDate(r.createdAt)}</p>
              {r.adminNote && (
                <div className="mt-2 bg-light/60 border border-border/40 rounded-lg p-2.5">
                  <p className="text-[10px] font-semibold text-muted uppercase tracking-[0.1em]">{t('support.teamReply')}</p>
                  <p className="mt-0.5 text-[12px] text-body">{r.adminNote}</p>
                </div>
              )}
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
