import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import fiscalService from "../../services/fiscalService";
import SourcePanel, {
  normalizeSource,
} from "../../components/fiscal/SourcePanel";

const EASE = "ease-[cubic-bezier(.16,1,.3,1)]";

/* Soft pastel accents per document type — yellow stays the primary brand
   accent, the rest sit quietly next to white.
   The ES corpus (elasticsearch_indexer.py) only ever tags `document_type` as
   "loi" or "note_commune" (see document_processor.py's detect_type()) — a
   coarser taxonomy than the taxmind/Neo4j categories used elsewhere in the
   app. Filtering on any other value here silently returns zero hits. */
const DOC_TYPES = [
  { key: "all", label: "Tous les textes", dot: "bg-gradient-to-r from-brand to-[#FFB800]" },
  { key: "loi", label: "Lois", dot: "bg-dark" },
  { key: "note_commune", label: "Notes communes", dot: "bg-[#3B82F6]" },
];

/* Chunk granularity filter — the corpus is chunked well below the whole-document
   level (article / section / subsection / table / …); exposing this lets you
   jump straight to the kind of passage you want, same as the original engine. */
const CHUNK_TYPES = [
  { key: "all", label: "Tous les passages" },
  { key: "article", label: "Article" },
  { key: "article_part", label: "Article (extrait)" },
  { key: "preamble", label: "Préambule" },
  { key: "section", label: "Section" },
  { key: "subsection", label: "Sous-section" },
  { key: "resume", label: "Résumé" },
  { key: "text_table", label: "Tableau (texte)" },
  { key: "image_table", label: "Tableau (image)" },
  { key: "full_document", label: "Document complet" },
];

const TYPE_BADGE = {
  loi: "bg-dark text-white",
  note_commune: "bg-[#DBEAFE] text-[#1D4ED8]",
};

const CHUNK_BADGE = {
  article: "bg-[#FEF3C7] text-[#92400E]",
  article_part: "bg-[#FEF3C7] text-[#92400E]",
  preamble: "bg-light text-body",
  section: "bg-[#F3E8FF] text-[#6B21A8]",
  subsection: "bg-[#E0F2FE] text-[#075985]",
  resume: "bg-[#FCE7F3] text-[#9D174D]",
  text_table: "bg-[#F0FDF4] text-[#15803D]",
  image_table: "bg-[#F0FDF4] text-[#15803D]",
  full_document: "bg-light text-body",
};

const CHUNK_LABEL = Object.fromEntries(CHUNK_TYPES.map((c) => [c.key, c.label]));

const EXAMPLES = {
  law: [
    "retenue à la source non-résident français",
    "TVA prestations de services export",
    "amortissement matériel informatique",
    "convention Tunisie-France dividendes",
  ],
  consultations: ["nom du client", "référence du mémo", "question fiscale traitée"],
};

const YEARS = { min: 2000, max: 2030 };
const RECENT_KEY = "taxmind.search.recent";

const fmtDate = (d) =>
  d
    ? new Date(d).toLocaleDateString(undefined, {
        day: "numeric",
        month: "short",
        year: "numeric",
      })
    : "—";

const loadRecent = () => {
  try {
    const l = JSON.parse(localStorage.getItem(RECENT_KEY));
    return Array.isArray(l) ? l : [];
  } catch {
    return [];
  }
};

/* highlight <em> from the engine as brand-yellow marks, everything else escaped */
const highlightHtml = (h) =>
  (h.highlight || h.content || "")
    .replace(/</g, "&lt;")
    .replace(/&lt;em>/g, '<mark class="bg-brand/40 rounded-sm px-0.5">')
    .replace(/&lt;\/em>/g, "</mark>");

/* ───────────────────────── pieces ───────────────────────── */

function SearchBar({ value, onChange, onSubmit, loading, inputRef, large }) {
  return (
    <form onSubmit={(e) => { e.preventDefault(); onSubmit(); }}
      className={`flex items-center gap-2 bg-white border border-border rounded-2xl shadow-lg shadow-dark/[0.05] focus-within:ring-2 focus-within:ring-brand focus-within:border-transparent transition-all ${large ? "pl-5 pr-2 py-2" : "pl-4 pr-1.5 py-1.5"}`}>
      <svg className="w-[18px] h-[18px] text-muted shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
        <path strokeLinecap="round" strokeLinejoin="round" d="M21 21l-5.197-5.197m0 0A7.5 7.5 0 105.196 5.196a7.5 7.5 0 0010.607 10.607z" />
      </svg>
      <input
        ref={inputRef}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder="Décrivez votre question en langage naturel…"
        className="flex-1 min-w-0 bg-transparent text-[15px] text-dark placeholder-muted focus:outline-none py-2"
      />
      <button
        type="submit"
        disabled={loading || !value.trim()}
        className={`bg-dark text-white text-sm font-semibold rounded-xl hover:bg-black active:scale-95 disabled:opacity-30 transition-all shrink-0 ${large ? "px-5 py-2.5" : "px-4 py-2"}`}
      >
        {loading ? (
          <span className="flex items-center gap-1.5">
            <span className="w-3.5 h-3.5 border-2 border-white/30 border-t-brand rounded-full animate-spin" />
            <span className="hidden sm:inline">Recherche</span>
          </span>
        ) : (
          "Rechercher"
        )}
      </button>
    </form>
  );
}

function ResultSkeleton({ delay = 0 }) {
  return (
    <div className="bg-white border border-border rounded-2xl p-5 animate-pulse" style={{ animationDelay: `${delay}ms` }}>
      <div className="flex items-center justify-between">
        <div className="h-3.5 bg-light rounded w-1/3" />
        <div className="h-4 bg-light rounded-full w-16" />
      </div>
      <div className="mt-4 space-y-2">
        <div className="h-3 bg-light rounded w-full" />
        <div className="h-3 bg-light rounded w-11/12" />
        <div className="h-3 bg-light rounded w-2/3" />
      </div>
    </div>
  );
}

function FiltersPanel({
  mode,
  docType,
  onDocType,
  yearMin,
  yearMax,
  onYears,
  chunkType,
  onChunkType,
  number,
  onNumber,
  dateText,
  onDateText,
  onApplyText,
  consFrom,
  consTo,
  onConsFrom,
  onConsTo,
  onApplyCons,
  buckets,
  recent,
  onRecent,
  onReset,
  filtersActive,
  onClose,
}) {
  return (
    <div className="h-full flex flex-col bg-white">
      <div className="h-12 shrink-0 flex items-center justify-between px-4 border-b border-border/70">
        <p className="text-[11px] font-bold text-muted uppercase tracking-[0.18em]">Filtres</p>
        <div className="flex items-center gap-1">
          {filtersActive && (
            <button onClick={onReset}
              className="text-[11px] font-bold text-body hover:text-dark rounded-lg px-2 py-1 hover:bg-light transition-colors">
              Réinitialiser
            </button>
          )}
          {onClose && (
            <button onClick={onClose}
              className="md:hidden w-8 h-8 rounded-lg text-muted hover:text-dark flex items-center justify-center transition-colors"
              aria-label="Fermer les filtres">
              <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" /></svg>
            </button>
          )}
        </div>
      </div>

      <div className="flex-1 overflow-y-auto px-3 py-4 space-y-6">
        {mode === "law" ? (
          <>
            {/* document types */}
            <div>
              <p className="px-2 mb-2 text-[10px] font-bold text-muted uppercase tracking-[0.18em]">Type de texte</p>
              <div className="space-y-0.5">
                {DOC_TYPES.map((d) => {
                  const active = docType === d.key;
                  const count = buckets?.find((b) => b.key === d.key)?.count;
                  return (
                    <button key={d.key} onClick={() => onDocType(d.key)}
                      className={`w-full flex items-center gap-2.5 text-left rounded-xl px-2.5 py-2 text-[13px] font-semibold transition-colors ${
                        active ? "bg-dark text-white shadow-sm" : "text-body hover:text-dark hover:bg-light"
                      }`}>
                      <span className={`w-2 h-2 rounded-full shrink-0 ${d.dot} ${active ? "ring-2 ring-white/30" : ""}`} />
                      <span className="flex-1 truncate">{d.label}</span>
                      {count != null && (
                        <span className={`text-[10px] font-bold rounded-full px-1.5 py-0.5 ${active ? "bg-white/15 text-white" : "bg-light text-muted"}`}>
                          {count}
                        </span>
                      )}
                    </button>
                  );
                })}
              </div>
            </div>

            {/* period */}
            <div>
              <p className="px-2 mb-2 text-[10px] font-bold text-muted uppercase tracking-[0.18em]">Période</p>
              <div className="flex items-center gap-2 px-2">
                {[
                  { v: yearMin, set: (y) => onYears(y, yearMax), label: "De" },
                  { v: yearMax, set: (y) => onYears(yearMin, y), label: "À" },
                ].map((f) => (
                  <label key={f.label} className="flex-1 min-w-0">
                    <span className="block text-[10px] font-bold text-muted mb-1">{f.label}</span>
                    <input
                      type="number"
                      min={YEARS.min}
                      max={YEARS.max}
                      value={f.v}
                      onChange={(e) => f.set(Number(e.target.value) || YEARS.min)}
                      className="w-full bg-white border border-border rounded-xl px-3 py-2 text-[13px] font-semibold text-dark focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent transition-all"
                    />
                  </label>
                ))}
              </div>
            </div>

            {/* chunk type — the corpus's real granularity (article / section / tableau / …) */}
            <div>
              <p className="px-2 mb-2 text-[10px] font-bold text-muted uppercase tracking-[0.18em]">Type de passage</p>
              <div className="px-2">
                <select
                  value={chunkType}
                  onChange={(e) => onChunkType(e.target.value)}
                  className="w-full bg-white border border-border rounded-xl px-3 py-2 text-[13px] font-semibold text-dark focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent transition-all"
                >
                  {CHUNK_TYPES.map((c) => (
                    <option key={c.key} value={c.key}>{c.label}</option>
                  ))}
                </select>
              </div>
            </div>

            {/* JORT reference: numéro + date */}
            <div>
              <p className="px-2 mb-2 text-[10px] font-bold text-muted uppercase tracking-[0.18em]">Référence du texte</p>
              <div className="px-2 space-y-2">
                <label className="block">
                  <span className="block text-[10px] font-bold text-muted mb-1">Numéro</span>
                  <input
                    value={number}
                    onChange={(e) => onNumber(e.target.value)}
                    onKeyDown={(e) => { if (e.key === "Enter") onApplyText(); }}
                    onBlur={onApplyText}
                    placeholder="ex : 2015-36"
                    className="w-full bg-white border border-border rounded-xl px-3 py-2 text-[13px] font-semibold text-dark placeholder-muted focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent transition-all"
                  />
                </label>
                <label className="block">
                  <span className="block text-[10px] font-bold text-muted mb-1">Date du texte</span>
                  <input
                    value={dateText}
                    onChange={(e) => onDateText(e.target.value)}
                    onKeyDown={(e) => { if (e.key === "Enter") onApplyText(); }}
                    onBlur={onApplyText}
                    placeholder="ex : 2015 ou 15/09/2015"
                    className="w-full bg-white border border-border rounded-xl px-3 py-2 text-[13px] font-semibold text-dark placeholder-muted focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent transition-all"
                  />
                </label>
              </div>
            </div>
          </>
        ) : (
          <div className="px-2 animate-fade-in space-y-5">
            <div className="bg-[#ECFDF5] border border-[#A7F3D0] rounded-xl p-3.5">
              <p className="text-[12.5px] font-bold text-[#047857]">Recherche dans vos mémos</p>
              <p className="text-[12px] text-[#059669] mt-1 leading-relaxed">
                Nom de client, référence ou mots de la question fiscale — quelques lettres suffisent.
              </p>
            </div>

            {/* date range for consultation search */}
            <div>
              <p className="mb-2 text-[10px] font-bold text-muted uppercase tracking-[0.18em]">Période (date de création)</p>
              <div className="flex items-center gap-2">
                {[
                  { v: consFrom, set: onConsFrom, label: "Du" },
                  { v: consTo, set: onConsTo, label: "Au" },
                ].map((f) => (
                  <label key={f.label} className="flex-1 min-w-0">
                    <span className="block text-[10px] font-bold text-muted mb-1">{f.label}</span>
                    <input
                      type="date"
                      value={f.v}
                      onChange={(e) => f.set(e.target.value)}
                      onBlur={onApplyCons}
                      className="w-full bg-white border border-border rounded-xl px-2.5 py-2 text-[12.5px] font-semibold text-dark focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent transition-all"
                    />
                  </label>
                ))}
              </div>
            </div>
          </div>
        )}

        {/* recent searches */}
        {recent.length > 0 && (
          <div>
            <p className="px-2 mb-2 text-[10px] font-bold text-muted uppercase tracking-[0.18em]">Recherches récentes</p>
            <div className="space-y-0.5">
              {recent.map((r, i) => (
                <button key={`${r.q}-${i}`} onClick={() => onRecent(r)}
                  className="w-full flex items-center gap-2.5 text-left rounded-xl px-2.5 py-2 text-[13px] text-body hover:text-dark hover:bg-light transition-colors group">
                  <svg className="w-3.5 h-3.5 text-muted shrink-0 group-hover:text-dark transition-colors" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M12 6v6h4.5m4.5 0a9 9 0 11-18 0 9 9 0 0118 0z" />
                  </svg>
                  <span className="flex-1 truncate">{r.q}</span>
                </button>
              ))}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

/* ───────────────────────── page ───────────────────────── */

/* Semantic search workspace, three vertical panes (same shell as the chat):
   filters | results | document. Clicking a result opens the passage inline
   as a third column on desktop (overlay on mobile). */
export default function Search() {
  const navigate = useNavigate();
  const [mode, setMode] = useState("law"); // 'law' | 'consultations'
  const [query, setQuery] = useState("");
  const [docType, setDocType] = useState("all");
  const [yearMin, setYearMin] = useState(YEARS.min);
  const [yearMax, setYearMax] = useState(YEARS.max);
  const [chunkType, setChunkType] = useState("all");
  const [number, setNumber] = useState("");
  const [dateText, setDateText] = useState("");
  const [consFrom, setConsFrom] = useState(""); // consultation search: date range
  const [consTo, setConsTo] = useState("");
  const [lawRes, setLawRes] = useState(null);
  const [consRes, setConsRes] = useState(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [searched, setSearched] = useState(false);
  const [viewing, setViewing] = useState(null);
  const [viewList, setViewList] = useState([]);
  const [recent, setRecent] = useState(loadRecent);
  const [filtersOpen, setFiltersOpen] = useState(
    () => localStorage.getItem("taxmind.search.filters") !== "0",
  );
  const [mobileFilters, setMobileFilters] = useState(false);
  const inputRef = useRef();
  const scrollRef = useRef();

  useEffect(() => { inputRef.current?.focus(); }, [mode]);
  useEffect(() => {
    localStorage.setItem("taxmind.search.filters", filtersOpen ? "1" : "0");
  }, [filtersOpen]);

  const filtersActive =
    docType !== "all" || yearMin !== YEARS.min || yearMax !== YEARS.max ||
    chunkType !== "all" || number.trim() !== "" || dateText.trim() !== "";

  const remember = (q, m) => {
    setRecent((prev) => {
      const next = [{ q, mode: m }, ...prev.filter((r) => r.q !== q)].slice(0, 8);
      try { localStorage.setItem(RECENT_KEY, JSON.stringify(next)); } catch {}
      return next;
    });
  };

  /* explicit params so facet/recent clicks never race state updates */
  const run = async ({
    q = query, m = mode, dt = docType, ymin = yearMin, ymax = yearMax,
    ct = chunkType, nb = number, dtx = dateText,
  } = {}) => {
    const text = q.trim();
    if (!text || loading) return;
    setLoading(true);
    setError("");
    setSearched(true);
    setViewing(null);
    scrollRef.current?.scrollTo({ top: 0 });
    try {
      if (m === "law") {
        const { data } = await fiscalService.search({
          query: text, docType: dt, chunkType: ct, number: nb, dateText: dtx,
          yearMin: ymin, yearMax: ymax, size: 30,
        });
        setLawRes(data.data);
      } else {
        const { data } = await fiscalService.list(text, false, consFrom, consTo);
        setConsRes(data.data);
      }
      remember(text, m);
    } catch (err) {
      setError(err.response?.data?.message || "La recherche a échoué — vérifiez la connexion du moteur (statut en haut à droite).");
      if (m === "law") setLawRes(null);
      else setConsRes(null);
    } finally {
      setLoading(false);
    }
  };

  const switchMode = (m) => {
    if (m === mode) return;
    setMode(m);
    setError("");
    setSearched(false);
    setLawRes(null);
    setConsRes(null);
    setViewing(null);
  };

  const pickDocType = (dt) => {
    setDocType(dt);
    setMobileFilters(false);
    if (searched && query.trim()) run({ dt });
  };

  const pickYears = (ymin, ymax) => {
    setYearMin(ymin);
    setYearMax(ymax);
    if (searched && query.trim()) run({ ymin, ymax });
  };

  const pickChunkType = (ct) => {
    setChunkType(ct);
    setMobileFilters(false);
    if (searched && query.trim()) run({ ct });
  };

  /* number/date are free-text — apply on Enter/blur using current state */
  const applyText = () => {
    if (searched && query.trim()) run();
  };

  /* consultation date-range applies on blur */
  const applyCons = () => {
    if (mode === "consultations" && searched && query.trim()) run();
  };

  const resetFilters = () => {
    setDocType("all");
    setYearMin(YEARS.min);
    setYearMax(YEARS.max);
    setChunkType("all");
    setNumber("");
    setDateText("");
    if (searched && query.trim())
      run({ dt: "all", ymin: YEARS.min, ymax: YEARS.max, ct: "all", nb: "", dtx: "" });
  };

  const pickRecent = (r) => {
    setQuery(r.q);
    setMobileFilters(false);
    if (r.mode !== mode) switchMode(r.mode);
    run({ q: r.q, m: r.mode });
  };

  const openHit = (hits, i) => {
    const list = hits.map((x, j) => normalizeSource(x, j + 1));
    setViewList(list);
    setViewing(list[i]);
  };

  const hits = lawRes?.hits || [];
  const filtersPanel = (closeFn) => (
    <FiltersPanel
      mode={mode}
      docType={docType}
      onDocType={pickDocType}
      yearMin={yearMin}
      yearMax={yearMax}
      onYears={pickYears}
      chunkType={chunkType}
      onChunkType={pickChunkType}
      number={number}
      onNumber={setNumber}
      dateText={dateText}
      onDateText={setDateText}
      onApplyText={applyText}
      consFrom={consFrom}
      consTo={consTo}
      onConsFrom={setConsFrom}
      onConsTo={setConsTo}
      onApplyCons={applyCons}
      buckets={lawRes?.docTypeBuckets}
      recent={recent}
      onRecent={pickRecent}
      onReset={resetFilters}
      filtersActive={filtersActive}
      onClose={closeFn}
    />
  );

  return (
    <div className="h-full flex bg-sand">
      {/* ① filters — desktop column, collapsible */}
      <aside
        className={`hidden md:block shrink-0 overflow-hidden transition-[width] duration-300 ${EASE} ${
          filtersOpen ? "w-[250px] border-r border-border" : "w-0"
        }`}
      >
        <div className="w-[250px] h-full">{filtersPanel()}</div>
      </aside>

      {/* ① filters — mobile overlay */}
      {mobileFilters && (
        <div className="md:hidden fixed inset-0 z-[70]">
          <div className="absolute inset-0 bg-dark/40 backdrop-blur-[2px] animate-fade-in" onClick={() => setMobileFilters(false)} />
          <div className="absolute left-0 top-0 h-full w-[280px] shadow-2xl animate-slide-in-left">
            {filtersPanel(() => setMobileFilters(false))}
          </div>
        </div>
      )}

      {/* ② results */}
      <section className="flex-1 min-w-0 flex flex-col">
        {/* toolbar */}
        <div className="h-12 shrink-0 flex items-center gap-2 px-3 border-b border-border/70 bg-white/70 backdrop-blur">
          <button
            onClick={() => setFiltersOpen((o) => !o)}
            className="hidden md:flex relative w-8 h-8 rounded-lg text-muted hover:text-dark hover:bg-light items-center justify-center transition-colors"
            aria-label={filtersOpen ? "Masquer les filtres" : "Afficher les filtres"}
            title={filtersOpen ? "Masquer les filtres" : "Afficher les filtres"}
          >
            <svg className="w-[17px] h-[17px]" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M3.75 5.25a1.5 1.5 0 011.5-1.5h13.5a1.5 1.5 0 011.5 1.5v13.5a1.5 1.5 0 01-1.5 1.5H5.25a1.5 1.5 0 01-1.5-1.5V5.25z" />
              <path strokeLinecap="round" d="M9.75 3.75v16.5" />
            </svg>
            {filtersActive && <span className="absolute top-1 right-1 w-1.5 h-1.5 rounded-full bg-brand ring-2 ring-white" />}
          </button>
          <button
            onClick={() => setMobileFilters(true)}
            className="md:hidden relative flex w-8 h-8 rounded-lg text-muted hover:text-dark hover:bg-light items-center justify-center transition-colors"
            aria-label="Filtres"
          >
            <svg className="w-[17px] h-[17px]" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M12 3c2.755 0 5.455.232 8.083.678.533.09.917.556.917 1.096v1.044a2.25 2.25 0 01-.659 1.591l-5.432 5.432a2.25 2.25 0 00-.659 1.591v2.927a2.25 2.25 0 01-1.244 2.013L9.75 21v-6.568a2.25 2.25 0 00-.659-1.591L3.659 7.409A2.25 2.25 0 013 5.818V4.774c0-.54.384-1.006.917-1.096A48.32 48.32 0 0112 3z" />
            </svg>
            {filtersActive && <span className="absolute top-1 right-1 w-1.5 h-1.5 rounded-full bg-brand ring-2 ring-white" />}
          </button>

          {/* Apple-style segmented scope control */}
          <div className="flex bg-light rounded-xl p-1 gap-0.5">
            {[
              { key: "law", label: "Textes de loi" },
              { key: "consultations", label: "Consultations" },
            ].map((m) => (
              <button key={m.key} onClick={() => switchMode(m.key)}
                className={`text-[12px] font-bold rounded-lg px-3 py-1.5 transition-all ${
                  mode === m.key ? "bg-white text-dark shadow-sm" : "text-muted hover:text-dark"
                }`}>
                {m.label}
              </button>
            ))}
          </div>

          <div className="flex-1" />
          {!loading && mode === "law" && lawRes && (
            <p className="text-[12px] text-muted shrink-0 animate-fade-in">
              <span className="font-bold text-dark">{lawRes.total}</span> résultats · {Math.round(lawRes.elapsedMs)} ms
            </p>
          )}
          {!loading && mode === "consultations" && consRes && (
            <p className="text-[12px] text-muted shrink-0 animate-fade-in">
              <span className="font-bold text-dark">{consRes.length}</span> consultation{consRes.length !== 1 ? "s" : ""}
            </p>
          )}
        </div>

        {!searched && !loading ? (
          /* hero state — big centered search, like the chat's greeting */
          <div className="flex-1 overflow-y-auto">
            <div className="min-h-full flex flex-col items-center justify-center px-4 sm:px-6 py-10 animate-fade-up">
              <div className="w-14 h-14 rounded-2xl bg-dark flex items-center justify-center mb-6 relative">
                <span className="absolute -top-1 -right-1 w-3.5 h-3.5 rounded-full bg-brand border-2 border-sand" />
                <svg className="w-6 h-6 text-brand" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M21 21l-5.197-5.197m0 0A7.5 7.5 0 105.196 5.196a7.5 7.5 0 0010.607 10.607z" />
                </svg>
              </div>
              <h1 className="font-display text-4xl text-dark text-center">Que cherchez-vous&nbsp;?</h1>
              <p className="text-muted mt-3 mb-8 text-center max-w-md">
                {mode === "law"
                  ? "Recherche sémantique sur 67 000+ passages juridiques — décrivez votre question, même sans les mots exacts."
                  : "Retrouvez un mémo par client, référence ou question traitée — quelques lettres suffisent."}
              </p>
              <div className="w-full max-w-xl">
                <SearchBar value={query} onChange={setQuery} onSubmit={run} loading={loading} inputRef={inputRef} large />
                <div className="flex flex-wrap justify-center gap-2 mt-4">
                  {EXAMPLES[mode].map((q, i) => (
                    <button key={q} onClick={() => { setQuery(q); if (mode === "law") run({ q }); }}
                      style={{ animationDelay: `${i * 60}ms` }}
                      className="text-[12.5px] font-semibold text-body bg-white hover:bg-brand/10 border border-border hover:border-brand/60 hover:text-dark rounded-full px-3.5 py-1.5 transition-all hover:-translate-y-0.5 animate-pop opacity-0">
                      {q}
                    </button>
                  ))}
                </div>
              </div>
            </div>
          </div>
        ) : (
          <>
            {/* compact search bar pinned under the toolbar */}
            <div className="shrink-0 px-4 sm:px-6 pt-4 pb-3">
              <div className="max-w-3xl mx-auto">
                <SearchBar value={query} onChange={setQuery} onSubmit={run} loading={loading} inputRef={inputRef} />
              </div>
            </div>

            <div ref={scrollRef} className="flex-1 overflow-y-auto">
              <div className="max-w-3xl mx-auto w-full px-4 sm:px-6 pb-10">
                {error && (
                  <div className="mt-2 p-4 bg-red-50 border border-red-200 rounded-2xl text-red-600 text-sm animate-pop opacity-0">
                    {error}
                  </div>
                )}

                {loading && (
                  <div className="mt-2 space-y-3">
                    {[0, 1, 2, 3].map((i) => <ResultSkeleton key={i} delay={i * 120} />)}
                  </div>
                )}

                {/* law results */}
                {!loading && mode === "law" && lawRes && (
                  <>
                    {lawRes.total === 0 && (
                      <div className="mt-6 bg-white border border-border border-dashed rounded-2xl p-10 text-center animate-fade-in">
                        <p className="text-dark font-semibold">Aucun texte trouvé</p>
                        <p className="text-muted text-sm mt-1">Élargissez les mots-clés ou réinitialisez les filtres.</p>
                      </div>
                    )}
                    <div className="mt-2 space-y-3">
                      {hits.map((h, i) => {
                        const open = viewing && viewing.index === i + 1;
                        return (
                          <button key={h.id || i} onClick={() => openHit(hits, i)}
                            style={{ animationDelay: `${Math.min(i, 8) * 45}ms` }}
                            className={`w-full text-left bg-white border rounded-2xl p-5 transition-all group animate-pop opacity-0 ${
                              open
                                ? "border-brand ring-2 ring-brand/60 shadow-md"
                                : "border-border hover:border-dark/25 hover:shadow-md hover:-translate-y-0.5"
                            }`}>
                            <div className="flex items-start justify-between gap-3">
                              <div className="min-w-0 flex items-start gap-2.5">
                                <span className={`shrink-0 mt-0.5 w-5 h-5 rounded-md text-[10px] font-extrabold flex items-center justify-center transition-colors ${open ? "bg-brand text-dark" : "bg-brand/30 text-dark group-hover:bg-brand"}`}>
                                  {i + 1}
                                </span>
                                <div className="min-w-0">
                                  <p className="text-sm font-bold text-dark capitalize truncate">
                                    {(h.filename || "Document").replace(/[-_]/g, " ")}
                                    {h.articleNumber ? ` · ${h.articleNumber}` : ""}
                                  </p>
                                  {h.sectionTitle && <p className="text-xs text-muted mt-0.5 truncate">{h.sectionTitle}</p>}
                                </div>
                              </div>
                              <div className="flex items-center gap-2 shrink-0">
                                {h.chunkType && (
                                  <span className={`text-[10px] font-bold rounded-full px-2 py-0.5 uppercase tracking-wide ${CHUNK_BADGE[h.chunkType] || "bg-light text-body"}`}>
                                    {CHUNK_LABEL[h.chunkType] || h.chunkType}
                                  </span>
                                )}
                                {h.documentType && (
                                  <span className={`text-[10px] font-bold rounded-full px-2 py-0.5 uppercase tracking-wide ${TYPE_BADGE[h.documentType] || "bg-light text-body"}`}>
                                    {DOC_TYPES.find((d) => d.key === h.documentType)?.label || h.documentType}
                                  </span>
                                )}
                                {h.pageNumber != null && <span className="text-[10px] text-muted">p.{h.pageNumber}</span>}
                              </div>
                            </div>
                            <p
                              className="mt-3 text-[13.5px] text-body leading-relaxed line-clamp-3"
                              dangerouslySetInnerHTML={{ __html: highlightHtml(h) }}
                            />
                            <span className="mt-3 inline-flex items-center gap-1.5 text-[12px] font-semibold text-muted group-hover:text-dark transition-colors">
                              {open ? "Passage ouvert" : "Ouvrir le passage"}
                              <svg className="w-3.5 h-3.5 group-hover:translate-x-0.5 transition-transform" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2.2}>
                                <path strokeLinecap="round" strokeLinejoin="round" d="M8.25 4.5l7.5 7.5-7.5 7.5" />
                              </svg>
                            </span>
                          </button>
                        );
                      })}
                    </div>
                  </>
                )}

                {/* consultation results */}
                {!loading && mode === "consultations" && consRes && (
                  <>
                    {consRes.length === 0 && (
                      <div className="mt-6 bg-white border border-border border-dashed rounded-2xl p-10 text-center animate-fade-in">
                        <p className="text-dark font-semibold">Aucune consultation ne correspond</p>
                        <p className="text-muted text-sm mt-1">Essayez un autre nom de client ou une référence partielle.</p>
                      </div>
                    )}
                    <div className="mt-2 space-y-3">
                      {consRes.map((c, i) => (
                        <button key={c.id} onClick={() => navigate(`/app/consultations/${c.id}`)}
                          style={{ animationDelay: `${Math.min(i, 8) * 45}ms` }}
                          className="w-full text-left bg-white border border-border rounded-2xl p-5 hover:border-dark/25 hover:shadow-md hover:-translate-y-0.5 transition-all group animate-pop opacity-0">
                          <div className="flex items-center gap-2 flex-wrap">
                            <span className="text-[10px] font-bold text-dark bg-brand/30 rounded-full px-2 py-0.5">{c.reference}</span>
                            {c.isInternational && (
                              <span className="text-[10px] font-semibold text-[#1D4ED8] bg-[#DBEAFE] rounded-full px-2 py-0.5">International</span>
                            )}
                            <span className="text-xs text-muted ml-auto">{fmtDate(c.createdAt)}</span>
                          </div>
                          <p className="mt-2.5 font-bold text-dark">{c.clientName}</p>
                          <p className="text-[13.5px] text-body line-clamp-2 mt-1 leading-snug">{c.fiscalQuestion}</p>
                          <div className="mt-3 flex items-center justify-between text-xs text-muted">
                            <span>{c.sourcesCount} sources · {c.refineCount} modifications</span>
                            <span className="inline-flex items-center gap-1.5 font-semibold group-hover:text-dark transition-colors">
                              Ouvrir le document
                              <svg className="w-3.5 h-3.5 group-hover:translate-x-0.5 transition-transform" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2.2}>
                                <path strokeLinecap="round" strokeLinejoin="round" d="M8.25 4.5l7.5 7.5-7.5 7.5" />
                              </svg>
                            </span>
                          </div>
                        </button>
                      ))}
                    </div>
                  </>
                )}
              </div>
            </div>
          </>
        )}
      </section>

      {/* ③ document — desktop inline third pane */}
      <aside
        className={`hidden lg:block shrink-0 overflow-hidden transition-[width] duration-300 ${EASE} ${
          viewing ? "w-[400px] xl:w-[460px]" : "w-0"
        }`}
      >
        {viewing && (
          <div className="w-[400px] xl:w-[460px] h-full border-l border-border">
            <SourcePanel
              key={`${viewing.index}-${viewing.docName}`}
              source={viewing}
              sources={viewList}
              onNavigate={setViewing}
              onClose={() => setViewing(null)}
            />
          </div>
        )}
      </aside>

      {/* ③ document — mobile/tablet overlay */}
      {viewing && (
        <div className="lg:hidden fixed inset-0 z-[80]" role="dialog" aria-modal="true">
          <div className="absolute inset-0 bg-dark/40 backdrop-blur-[2px] animate-fade-in" onClick={() => setViewing(null)} />
          <div className="absolute right-0 top-0 h-full w-full max-w-md shadow-2xl animate-slide-in-right">
            <SourcePanel source={viewing} sources={viewList} onNavigate={setViewing} onClose={() => setViewing(null)} />
          </div>
        </div>
      )}
    </div>
  );
}
