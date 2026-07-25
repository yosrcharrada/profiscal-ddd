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
 *
 * The configuration panel exposes the pipeline knobs an administrator plausibly needs on a
 * legal corpus (strategy, embedding backend, q, sizing). Bounds mirror `_validate_user_config`
 * in the chunker's main.py — it clamps out-of-range values silently, so matching them here is
 * what makes the UI honest about what will actually be applied. Only fields the admin changes
 * are sent; everything else stays the service's default, keeping it the owner of its contract.
 */

/** Mirrors VALID_STRATEGIES in pipeline/s2_chunkers.py. */
const STRATEGIES = [
  'auto', 'recursive', 'sliding_window', 'structure', 'semantic_boundaries',
  'sentence_clustering', 'paragraph_pack', 'legal_articles', 'hybrid_legal_semantic',
];

/** Mirrors the embedding_backend whitelist in main.py (`auto` → sent as null). */
const BACKENDS = ['auto', 'openai', 'openai-large', 'multilingual', 'english', 'tfidf'];

/** S3 entropy function. `qlog` is the q-logarithm entropy; `tsallis` the Hill number. */
const ENTROPY_MODES = ['qlog', 'tsallis'];

/** S4 similarity kernel. `qcosine` is the Fitouhi–Bouzeffour q-cosine (base = q²). */
const SIMILARITY_MODES = ['qcosine', 'cosine'];

/**
 * Defaults + validated ranges, kept in step with DEFAULT_CONFIG / _validate_user_config.
 *
 * The two q values are SEPARATE genes the S7 GA tunes independently — `q_entropy_param`
 * drives the S3 entropy, `q_s4_kernel` the S4 q-cosine. They are shown here as the run's
 * starting point, not as fixed settings: the GA searches both over [-1, 1].
 */
const CFG_DEFAULTS = {
  chunking_strategy: 'auto',
  embedding_backend: 'multilingual',
  entropy_mode: 'qlog',
  s4_similarity: 'qcosine',
  q_entropy_param: 1.0,
  q_s4_kernel: 1.0,
  K: 4,
  min_chunk_tokens: 20,
  max_chunk_tokens: 320,
  tau_sem: 0.75,
  window: 1,
  overlap: 0,
  judge_answerability: false,
};

const NUM_FIELDS = [
  { key: 'q_entropy_param',  label: 'q',         min: -1,  max: 1,    step: 0.1,  hint: 'qHint' },
  { key: 'q_s4_kernel',      label: 'qS4',       min: -1,  max: 1,    step: 0.1,  hint: 'qS4Hint' },
  { key: 'K',                label: 'K',         min: 2,   max: 8,    step: 1 },
  { key: 'min_chunk_tokens', label: 'minTokens', min: 4,   max: 200,  step: 1 },
  { key: 'max_chunk_tokens', label: 'maxTokens', min: 64,  max: 2000, step: 1 },
  { key: 'tau_sem',          label: 'tau',       min: 0.2, max: 0.99, step: 0.01 },
  { key: 'window',           label: 'window',    min: 0,   max: 5,    step: 1 },
  { key: 'overlap',          label: 'overlap',   min: 0,   max: 4,    step: 1 },
];

/**
 * Send only what the admin actually changed. `embedding_backend: 'auto'` maps to null, which is
 * how the service spells "pick OpenAI if a key exists, else the local model".
 */
function toChunkerConfig(cfg) {
  const out = {};
  for (const [k, v] of Object.entries(cfg)) {
    if (v === CFG_DEFAULTS[k]) continue;
    out[k] = k === 'embedding_backend' && v === 'auto' ? null : v;
  }
  return out;
}
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
  const [cfg, setCfg]           = useState(CFG_DEFAULTS);
  const [cfgOpen, setCfgOpen]   = useState(false);
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
      const data = await runChunking(file, setProg, { config: toChunkerConfig(cfg) });
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
  }, [file, busy, cfg, t]);

  const setField = (key) => (value) => setCfg((p) => ({ ...p, [key]: value }));

  const toggle = (setFn) => (i) =>
    setFn((prev) => {
      const next = new Set(prev);
      next.has(i) ? next.delete(i) : next.add(i);
      return next;
    });

  const chunks   = results?.chunks || [];
  const summary  = results?.summary || {};
  const accepted = chunks.length - rejected.size;
  const dirty    = Object.keys(CFG_DEFAULTS).some((k) => cfg[k] !== CFG_DEFAULTS[k]);

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
              /* Matches what _parse_file actually handles: PDF (detected by magic bytes too,
                 so a mis-named PDF still parses) and anything text-decodable. Office/
                 OpenDocument files are zip archives the service rejects outright, so they are
                 deliberately NOT offered here — listing .docx only invited a guaranteed error. */
              accept=".pdf,text/*,.md,.markdown,.rst,.tex,.csv,.json,.xml,.yaml,.yml,.log"
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

          {/* ── Configuration ─────────────────────────────────────────── */}
          <div className="mt-4 border-t border-border/50 pt-3">
            <div className="flex items-center justify-between">
              <h3 className="text-[12px] font-extrabold text-dark">{t('admin.knowledge.cfg.title')}</h3>
              <div className="flex items-center gap-2">
                {!dirty || busy ? null : (
                  <button
                    onClick={() => setCfg(CFG_DEFAULTS)}
                    className="text-[11px] font-bold text-body hover:text-dark hover:underline"
                  >
                    {t('admin.knowledge.cfg.reset')}
                  </button>
                )}
                <button
                  onClick={() => setCfgOpen((o) => !o)}
                  className="text-[11px] font-bold rounded-lg px-2.5 py-1 border border-border text-body hover:text-dark hover:bg-light transition"
                >
                  {cfgOpen ? t('admin.knowledge.cfg.hide') : t('admin.knowledge.cfg.show')}
                </button>
              </div>
            </div>

            {cfgOpen && (
              <div className="mt-3 space-y-3">
                <p className="text-[11px] text-muted leading-relaxed">{t('admin.knowledge.cfg.hint')}</p>

                <div className="grid sm:grid-cols-2 gap-3">
                  <Field label={t('admin.knowledge.cfg.strategy')} hint={t('admin.knowledge.cfg.strategyHint')}>
                    <select
                      value={cfg.chunking_strategy}
                      disabled={busy}
                      onChange={(e) => setField('chunking_strategy')(e.target.value)}
                      className="w-full text-[12px] rounded-lg border border-border bg-white px-2 py-1.5 text-dark disabled:opacity-50"
                    >
                      {STRATEGIES.map((s) => (
                        <option key={s} value={s}>
                          {s === 'auto' ? t('admin.knowledge.cfg.strategy.auto') : s}
                        </option>
                      ))}
                    </select>
                  </Field>

                  <Field label={t('admin.knowledge.cfg.backend')} hint={t('admin.knowledge.cfg.backendHint')}>
                    <select
                      value={cfg.embedding_backend}
                      disabled={busy}
                      onChange={(e) => setField('embedding_backend')(e.target.value)}
                      className="w-full text-[12px] rounded-lg border border-border bg-white px-2 py-1.5 text-dark disabled:opacity-50"
                    >
                      {BACKENDS.map((b) => <option key={b} value={b}>{b}</option>)}
                    </select>
                  </Field>

                  <Field label={t('admin.knowledge.cfg.entropy')} hint={t('admin.knowledge.cfg.entropyHint')}>
                    <select
                      value={cfg.entropy_mode}
                      disabled={busy}
                      onChange={(e) => setField('entropy_mode')(e.target.value)}
                      className="w-full text-[12px] rounded-lg border border-border bg-white px-2 py-1.5 text-dark disabled:opacity-50"
                    >
                      {ENTROPY_MODES.map((m) => <option key={m} value={m}>{m}</option>)}
                    </select>
                  </Field>

                  <Field label={t('admin.knowledge.cfg.similarity')} hint={t('admin.knowledge.cfg.similarityHint')}>
                    <select
                      value={cfg.s4_similarity}
                      disabled={busy}
                      onChange={(e) => setField('s4_similarity')(e.target.value)}
                      className="w-full text-[12px] rounded-lg border border-border bg-white px-2 py-1.5 text-dark disabled:opacity-50"
                    >
                      {SIMILARITY_MODES.map((m) => <option key={m} value={m}>{m}</option>)}
                    </select>
                  </Field>
                </div>

                <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
                  {NUM_FIELDS.map((f) => (
                    <Field
                      key={f.key}
                      label={t(`admin.knowledge.cfg.${f.label}`)}
                      hint={f.hint ? t(`admin.knowledge.cfg.${f.hint}`) : `${f.min} – ${f.max}`}
                    >
                      <input
                        type="number"
                        value={cfg[f.key]}
                        min={f.min}
                        max={f.max}
                        step={f.step}
                        disabled={busy}
                        onChange={(e) => {
                          // Keep the raw text while typing; clamping happens on blur so the
                          // field doesn't fight the user mid-entry.
                          const v = e.target.value;
                          setField(f.key)(v === '' ? '' : Number(v));
                        }}
                        onBlur={(e) => {
                          const n = Number(e.target.value);
                          setField(f.key)(
                            Number.isFinite(n) ? Math.max(f.min, Math.min(f.max, n)) : CFG_DEFAULTS[f.key]
                          );
                        }}
                        className="w-full text-[12px] rounded-lg border border-border bg-white px-2 py-1.5 text-dark disabled:opacity-50"
                      />
                    </Field>
                  ))}
                </div>

                <label className="flex items-start gap-2 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={cfg.judge_answerability}
                    disabled={busy}
                    onChange={(e) => setField('judge_answerability')(e.target.checked)}
                    className="mt-0.5 accent-dark"
                  />
                  <span>
                    <span className="text-[12px] font-bold text-dark">{t('admin.knowledge.cfg.judge')}</span>
                    <span className="block text-[10.5px] text-muted leading-relaxed">
                      {t('admin.knowledge.cfg.judgeHint')}
                    </span>
                  </span>
                </label>
              </div>
            )}
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

function Field({ label, hint, children }) {
  return (
    <label className="block">
      <span className="block text-[10.5px] font-bold text-muted uppercase tracking-wider mb-1">{label}</span>
      {children}
      {hint && <span className="block text-[10px] text-muted mt-0.5 leading-snug">{hint}</span>}
    </label>
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
