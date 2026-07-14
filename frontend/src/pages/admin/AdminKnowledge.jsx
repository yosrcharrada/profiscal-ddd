import { useLanguage } from '../../context/LanguageContext';
import AdminLayout from '../../components/admin/AdminLayout';

/**
 * Placeholder for the knowledge-graph governance module:
 * chunking of new legal documents + the approval gate that keeps
 * unreviewed data out of the graph. Functional version to come.
 */
export default function AdminKnowledge() {
  const { t } = useLanguage();

  const steps = [
    { key: 'upload', icon: <path strokeLinecap="round" strokeLinejoin="round" d="M3 16.5v2.25A2.25 2.25 0 005.25 21h13.5A2.25 2.25 0 0021 18.75V16.5m-13.5-9L12 3m0 0l4.5 4.5M12 3v13.5" /> },
    { key: 'chunking', icon: <path strokeLinecap="round" strokeLinejoin="round" d="M7.5 21L3 16.5m0 0L7.5 12M3 16.5h13.5m0-13.5L21 7.5m0 0L16.5 12M21 7.5H7.5" /> },
    { key: 'review', icon: <path strokeLinecap="round" strokeLinejoin="round" d="M9 12.75L11.25 15 15 9.75m-3-7.036A11.959 11.959 0 013.598 6 11.99 11.99 0 003 9.749c0 5.592 3.824 10.29 9 11.623 5.176-1.332 9-6.03 9-11.622 0-1.31-.21-2.571-.598-3.751h-.152c-3.196 0-6.1-1.248-8.25-3.285z" /> },
    { key: 'publish', icon: <path strokeLinecap="round" strokeLinejoin="round" d="M9.813 15.904L9 18.75l-.813-2.846a4.5 4.5 0 00-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 003.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 003.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 00-3.09 3.09z" /> },
  ];

  return (
    <AdminLayout>
      <div className="space-y-4 animate-fade-up">
      <div>
        <h1 className="text-xl font-bold text-dark tracking-tight">{t('admin.knowledge.title')}</h1>
        <p className="text-[13px] text-muted mt-0.5">{t('admin.knowledge.subtitle')}</p>
      </div>

      <div className="relative bg-cream rounded-2xl border border-border/60 overflow-hidden">
        <div className="absolute top-0 left-0 w-full h-[3px] bg-brand" />
        <div className="absolute -top-16 -right-16 w-64 h-64 bg-brand/10 rounded-full blur-3xl" />

        <div className="relative p-8 sm:p-12 text-center">
          <span className="inline-flex items-center gap-1.5 text-[10px] font-bold text-dark bg-brand rounded-full px-3 py-1 uppercase tracking-[0.14em]">
            {t('admin.knowledge.comingSoon')}
          </span>
          <h2 className="mt-4 text-2xl font-extrabold text-dark tracking-tight">{t('admin.knowledge.heroTitle')}</h2>
          <p className="mt-2 max-w-xl mx-auto text-[13px] text-body leading-relaxed">{t('admin.knowledge.heroText')}</p>

          <div className="mt-8 grid sm:grid-cols-4 gap-3 max-w-3xl mx-auto">
            {steps.map((s, i) => (
              <div key={s.key} className="relative bg-white/70 dark:bg-light/50 rounded-xl border border-border/50 p-4 text-left">
                <span className="absolute -top-2 -left-2 w-6 h-6 rounded-full bg-dark text-brand text-[10px] font-bold flex items-center justify-center">{i + 1}</span>
                <svg className="w-5 h-5 text-dark" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.6}>{s.icon}</svg>
                <p className="mt-2 text-[12px] font-bold text-dark">{t(`admin.knowledge.step.${s.key}`)}</p>
                <p className="mt-0.5 text-[11px] text-muted leading-relaxed">{t(`admin.knowledge.step.${s.key}Desc`)}</p>
              </div>
            ))}
          </div>

          <div className="mt-8 inline-flex items-start gap-2.5 p-3.5 bg-white/70 dark:bg-light/50 border border-border/50 rounded-xl text-left max-w-lg">
            <svg className="w-4 h-4 mt-0.5 shrink-0 text-dark" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}><path strokeLinecap="round" strokeLinejoin="round" d="M16.5 10.5V6.75a4.5 4.5 0 10-9 0v3.75m-.75 11.25h10.5a2.25 2.25 0 002.25-2.25v-6.75a2.25 2.25 0 00-2.25-2.25H6.75a2.25 2.25 0 00-2.25 2.25v6.75a2.25 2.25 0 002.25 2.25z" /></svg>
            <p className="text-[12px] text-body leading-relaxed">
              <span className="font-bold text-dark">{t('admin.knowledge.gateTitle')}</span> — {t('admin.knowledge.gateText')}
            </p>
          </div>
        </div>
      </div>
      </div>
    </AdminLayout>
  );
}
