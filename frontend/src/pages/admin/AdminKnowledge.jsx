import { useCallback, useEffect, useRef, useState } from 'react';
import { useLanguage } from '../../context/LanguageContext';
import AdminLayout from '../../components/admin/AdminLayout';
import chunkerService, { runChunking } from '../../services/chunkerService';

/**
 * Base de connaissances — document chunking.
 *
 * Native admin surface for the qEntropy pipeline: an administrator uploads a legal document,
 * the 8-stage chunker runs (profiler → multi-strategy chunkers → Tsallis q-entropy boundary
 * refinement → merge → graph → embeddings → GA tuning → Table-I scoring), and the resulting
 * chunks come back for review before anything is published into the graph.
 *
 * The page talks only to OUR API (`api/chunker/*`), which proxies to the chunking service —
 * that is what keeps the operation Admin-only. If the service is not running the page shows a
 * clear offline state with the address it expected, instead of failing on upload.
 *
 * Deliberately not shown here: the chunker's research surface (strategy comparison, GA
 * fitness curves, per-metric tables). Those belong to the standalone chunker UI; this page is
 * the governance flow — chunk, inspect, keep or discard.
 */
export default function AdminKnowledge() {
  const { t } = useLanguage();

  const [service, setService]   = useState({ checked: false, alive: false, baseUrl: '' });
  const [file, setFile]         = useState(null);
  const [busy, setBusy]         = useState(false);
  const [prog, setProg]         = useState({ stage: '', progress: 0 });
  const [results, setResults]   = useState(null);
  const [error, setError]       = useState('');
  const [rejected, setRejected] = useState(() => new Set()); // chunk indexes the admin discards
  const [expanded, setExpanded] = useState(() => new Set());
  const inputRef = useRef(null);

  // Probe the service on mount so the UI can offer a useful message rather than letting the
  // first upload fail with a network error.
  useEffect(() => {
    let alive = true;
    chunkerService
      .health()
      .then(({ data }) => alive && setService({ checked: true, ...(data?.data || {}) }))
      .catch(() => alive && setService({ checked: true, alive: false, baseUrl: '' }));
    return () => { alive = false; };
  }, []);

  const pick = (f) => {
    if (!f) return;
    setFile(f);
    setResults(null);
    setError('');
    setRejected(new Set());
  };

  const start = useCallback(async () => {
    if (!file || busy) return;
    setBusy(true);
    setError('');
    setResults(null);
    setProg({ stage: 'upload', progress: 0 });
    try {
      const data = await runChunking(file, setProg);
      setResults(data);
    } catch (e) {
      // 503 = our proxy could not reach the chunker; anything else is the pipeline's own error.
      const status = e?.response?.status;
      const msg =
        status === 503
          ? e?.response?.data?.message
          : e?.response?.data?.detail || e?.response?.data?.message || e?.message;
      setError(msg || t('admin.knowledge.err.generic'));
    } finally {
      setBusy(false);
    }
  }, [file, busy, t]);

  const toggle = (setFn) => (i) =>
    setFn((prev) => {
      const next = new Set(prev);
      next.has(i) ? next.delete(i) : next.add(i);
      return next;
    });

  const chunks   = results?.chunks || [];
  const summary  = results?.summary || {};
  const accepted = chunks.length - rejected.size;

  return (
    <AdminLayout>
      <div className="space-y-4 animate-fade-up">
        <div>
          <h1 className="text-xl font-bold text-dark tracking-tight">{t('admin.knowledge.title')}</h1>
          <p className="text-[13px] text-muted mt-0.5">{t('admin.knowledge.subtitle')}</p>
        </div>

        {/* Service offline — actionable, with the address the API tried. */}
        {service.checked && !service.alive && (
          <div className="flex items-start gap-2.5 p-3.5 bg-cream border border-border/60 rounded-xl">
            <svg className="w-4 h-4 mt-0.5 shrink-0 text-dark" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m9-.75a9 9 0 11-18 0 9 9 0 0118 0zm-9 3.75h.008v.008H12v-.008z" />
            </svg>
            <p className="text-[12px] text-body leading-relaxed">
              <span className="font-bold text-dark">{t('admin.knowledge.offlineTitle')}</span>{' '}
              {t('admin.knowledge.offlineText')}{' '}
              {service.baseUrl && (
                <code className="text-[11px] bg-white/70 border border-border/50 rounded px-1 py-0.5">{service.baseUrl}</code>
              )}
            </p>
          </div>
        )}

        {/* ── Step 1 — upload ─────────────────────────────────────────── */}
        <section className="bg-white rounded-2xl border border-border/60 p-5">
          <div className="flex items-center gap-2 mb-3">
            <span className="w-6 h-6 rounded-full bg-dark text-brand text-[11px] font-bold flex items-center justify-center">1</span>
            <h2 className="text-[14px] font-extrabold text-dark">{t('admin.knowledge.step.upload')}</h2>
          </div>

          <div
            onClick={() => !busy && inputRef.current?.click()}
            onDragOver={(e) => e.preventDefault()}
            onDrop={(e) => { e.preventDefault(); if (!busy) pick(e.dataTransfer.files?.[0]); }}
            className={`border-2 border-dashed rounded-xl p-6 text-center transition-colors ${
              busy ? 'border-border/50 opacity-60' : 'border-border hover:border-brand cursor-pointer'
            }`}
          >
            <input
              ref={inputRef}
              type="file"
              accept=".pdf,.txt,.md,.docx,.html"
              className="hidden"
              onChange={(e) => pick(e.target.files?.[0])}
            />
            <svg className="w-7 h-7 mx-auto text-muted" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M3 16.5v2.25A2.25 2.25 0 005.25 21h13.5A2.25 2.25 0 0021 18.75V16.5m-13.5-9L12 3m0 0l4.5 4.5M12 3v13.5" />
            </svg>
            <p className="mt-2 text-[13px] font-bold text-dark">
              {file ? file.name : t('admin.knowledge.dropzone')}
            </p>
            <p className="text-[11px] text-muted mt-0.5">
              {file
                ? `${(file.size / 1024 / 1024).toFixed(2)} MB`
                : t('admin.knowledge.dropzoneHint')}
            </p>
          </div>

          <div className="mt-3 flex items-center gap-2">
            <button
              onClick={start}
              disabled={!file || busy || (service.checked && !service.alive)}
              className="text-[12px] font-bold rounded-lg px-4 py-2 bg-dark text-brand hover:brightness-110 disabled:opacity-40 disabled:cursor-not-allowed transition"
            >
              {busy ? t('admin.knowledge.running') : t('admin.knowledge.startBtn')}
            </button>
            {file && !busy && (
              <button
                onClick={() => { setFile(null); setResults(null); setError(''); }}
                className="text-[12px] font-bold rounded-lg px-3 py-2 border border-border text-body hover:text-dark hover:bg-light transition"
              >
                {t('common.cancel')}
              </button>
            )}
          </div>

          {busy && (
            <div className="mt-4">
              <div className="flex items-center justify-between text-[11px] text-muted mb-1">
                <span className="font-bold text-dark">{prog.stage || '…'}</span>
                <span>{Math.round(prog.progress || 0)}%</span>
              </div>
              <div className="h-1.5 bg-light rounded-full overflow-hidden">
                <div
                  className="h-full bg-brand transition-all duration-500"
                  style={{ width: `${Math.max(4, Math.min(100, prog.progress || 0))}%` }}
                />
              </div>
              <p className="mt-2 text-[11px] text-muted">{t('admin.knowledge.runningHint')}</p>
            </div>
          )}

          {error && (
            <p className="mt-3 text-[12px] text-red-600 bg-red-50 border border-red-200 rounded-lg px-3 py-2">
              {error}
            </p>
          )}
        </section>

        {/* ── Step 2 — review ─────────────────────────────────────────── */}
        {results && (
          <section className="bg-white rounded-2xl border border-border/60 p-5">
            <div className="flex items-center gap-2 mb-4">
              <span className="w-6 h-6 rounded-full bg-dark text-brand text-[11px] font-bold flex items-center justify-center">2</span>
              <h2 className="text-[14px] font-extrabold text-dark">{t('admin.knowledge.step.review')}</h2>
            </div>

            <div className="grid grid-cols-2 sm:grid-cols-4 gap-2 mb-4">
              <Stat label={t('admin.knowledge.stat.chunks')}   value={summary.chunk_count ?? chunks.length} />
              <Stat label={t('admin.knowledge.stat.strategy')} value={summary.winning_strategy || '—'} />
              <Stat label={t('admin.knowledge.stat.tokens')}   value={summary.token_count ?? '—'} />
              <Stat label={t('admin.knowledge.stat.kept')}     value={`${accepted}/${chunks.length}`} />
            </div>

            <div className="space-y-1.5 max-h-[55vh] overflow-y-auto pr-1">
              {chunks.map((c, i) => {
                const idx  = c.index ?? i;
                const out  = rejected.has(idx);
                const open = expanded.has(idx);
                const text = c.text || '';
                return (
                  <div
                    key={idx}
                    className={`border rounded-xl p-3 transition-colors ${
                      out ? 'border-border/40 bg-light/40 opacity-60' : 'border-border/60 bg-white'
                    }`}
                  >
                    <div className="flex items-start gap-2">
                      <span className="text-[10px] font-bold text-muted mt-0.5 shrink-0 w-8">#{idx + 1}</span>
                      <p
                        className={`flex-1 text-[12.5px] text-body leading-relaxed whitespace-pre-wrap ${open ? '' : 'line-clamp-3'}`}
                      >
                        {text}
                      </p>
                      <button
                        onClick={() => toggle(setRejected)(idx)}
                        title={out ? t('admin.knowledge.restore') : t('admin.knowledge.discard')}
                        className={`shrink-0 text-[10px] font-bold rounded-lg px-2 py-1 border transition ${
                          out
                            ? 'border-border text-body hover:text-dark'
                            : 'border-red-200 text-red-600 hover:bg-red-50'
                        }`}
                      >
                        {out ? t('admin.knowledge.restore') : t('admin.knowledge.discard')}
                      </button>
                    </div>
                    <div className="mt-1.5 pl-10 flex items-center gap-3 text-[10px] text-muted">
                      <span>{c.token_count ?? 0} tokens</span>
                      {c.boundary_type && <span>· {c.boundary_type}</span>}
                      {text.length > 180 && (
                        <button
                          onClick={() => toggle(setExpanded)(idx)}
                          className="font-bold text-dark hover:underline"
                        >
                          {open ? t('admin.knowledge.less') : t('admin.knowledge.more')}
                        </button>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>

            {/* Publishing into the Neo4j graph is the next step of the governance flow and is
                intentionally not wired yet — the review gate exists first so nothing reaches
                the corpus unreviewed. */}
            <div className="mt-4 flex items-center gap-2.5 p-3 bg-cream border border-border/50 rounded-xl">
              <svg className="w-4 h-4 shrink-0 text-dark" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M16.5 10.5V6.75a4.5 4.5 0 10-9 0v3.75m-.75 11.25h10.5a2.25 2.25 0 002.25-2.25v-6.75a2.25 2.25 0 00-2.25-2.25H6.75a2.25 2.25 0 00-2.25 2.25v6.75a2.25 2.25 0 002.25 2.25z" />
              </svg>
              <p className="text-[12px] text-body leading-relaxed">
                <span className="font-bold text-dark">{t('admin.knowledge.gateTitle')}</span> — {t('admin.knowledge.gateText')}
              </p>
            </div>
          </section>
        )}
      </div>
    </AdminLayout>
  );
}

function Stat({ label, value }) {
  return (
    <div className="bg-cream rounded-xl border border-border/50 px-3 py-2">
      <p className="text-[10px] font-bold text-muted uppercase tracking-wider">{label}</p>
      <p className="text-[14px] font-extrabold text-dark mt-0.5 truncate">{value}</p>
    </div>
  );
}
