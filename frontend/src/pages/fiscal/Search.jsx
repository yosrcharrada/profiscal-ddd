import { useEffect, useMemo, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import COUNTRIES from "../../data/countries";
import { useLanguage } from "../../context/LanguageContext";
import fiscalService, { openDocumentPdf } from "../../services/fiscalService";
import { useToast } from "../../components/common/Toast";
import SourcePanel, {
  normalizeSource,
} from "../../components/fiscal/SourcePanel";

const EASE = "ease-[cubic-bezier(.16,1,.3,1)]";

// Mirrors the backend's highlight stop-word list (ElasticsearchSearchAgent.FrenchStop) —
// the ES highlight_query already excludes these as SEARCH terms, but the highlighter can
// still sweep a filler word into the SAME <em> span as a real match when they sit next to
// each other in the fragment (e.g. "de la retenue" -> one continuous highlighted run). This
// trims stop-words off the EDGES of each rendered <mark> span client-side, so only the actual
// matched term(s) stay highlighted.
const FR_STOP = new Set([
  "le", "la", "les", "l", "de", "du", "des", "d", "un", "une", "au", "aux",
  "en", "et", "est", "à", "a", "par", "pour", "sur", "dans", "avec", "que",
  "qui", "qu", "se", "sa", "son", "ses", "ce", "cette", "ces", "il", "ils",
  "elle", "elles", "je", "tu", "nous", "vous", "on", "y", "ne", "pas", "plus",
  "ou", "si", "car", "mais", "donc", "ni", "dont", "où", "lors", "dès", "tout",
  "tous", "toute", "toutes", "leur", "leurs", "même", "entre", "sous", "sans",
  "avant", "après", "pendant", "depuis", "sont", "sera", "été", "ainsi", "soit",
  "tel", "tels", "telle", "selon", "afin", "notamment", "également", "lorsque",
]);
const isStopWord = (tok) =>
  FR_STOP.has(tok.toLowerCase().replace(/^[^\wà-ÿ]+|[^\wà-ÿ]+$/gi, ""));

const trimMarkEdges = (html) =>
  html.replace(/<mark class="([^"]*)">([\s\S]*?)<\/mark>/g, (full, cls, inner) => {
    const words = inner.split(/( +)/); // keeps spaces as their own array items for exact rejoin
    let lo = 0;
    let hi = words.length - 1;
    while (lo <= hi && (/^\s+$/.test(words[lo]) || isStopWord(words[lo]))) lo++;
    while (hi >= lo && (/^\s+$/.test(words[hi]) || isStopWord(words[hi]))) hi--;
    const core = words.slice(lo, hi + 1).join("");
    if (!core.trim()) return full; // never destroy a match that's entirely stop-words
    return `${words.slice(0, lo).join("")}<mark class="${cls}">${core}</mark>${words.slice(hi + 1).join("")}`;
  });

const DOC_TYPES = [
  { key: "all", tKey: "search.all", dot: "bg-gradient-to-r from-brand to-[#FFB800]" },
  { key: "Code", tKey: "search.codes", dot: "bg-dark" },
  { key: "Convention", tKey: "search.conventions", dot: "bg-brand" },
  { key: "LoiFinances", tKey: "search.loisFinances", dot: "bg-[#8B5CF6]" },
  { key: "Doctrine", tKey: "search.doctrine", dot: "bg-[#3B82F6]" },
  { key: "Commentaire", tKey: "search.commentaires", dot: "bg-[#F97316]" },
];

const TYPE_BADGE = {
  Code: {
    bg: "bg-dark/10", text: "text-dark", border: "border-dark/20", accent: "bg-dark",
    icon: "M12 6.042A8.967 8.967 0 006 3.75c-1.052 0-2.062.18-3 .512v14.25A8.987 8.987 0 016 18c2.305 0 4.408.867 6 2.292m0-14.25a8.966 8.966 0 016-2.292c1.052 0 2.062.18 3 .512v14.25A8.987 8.987 0 0018 18a8.967 8.967 0 00-6 2.292m0-14.25v14.25",
  },
  Convention: {
    bg: "bg-brand/20", text: "text-dark", border: "border-brand/40", accent: "bg-brand",
    icon: "M12 21a9 9 0 100-18 9 9 0 000 18zm0-18v18m-9-9h18M12 3a15.3 15.3 0 013 9 15.3 15.3 0 01-3 9 15.3 15.3 0 01-3-9 15.3 15.3 0 013-9z",
  },
  LoiFinances: {
    bg: "bg-[#EDE9FE]", text: "text-[#5B21B6]", border: "border-[#C4B5FD]", accent: "bg-[#8B5CF6]",
    icon: "M12 6v12m-3-2.818l.879.659c1.171.879 3.07.879 4.242 0 1.172-.879 1.172-2.303 0-3.182C13.536 12.219 12.768 12 12 12c-.725 0-1.45-.22-2.003-.659-1.106-.879-1.106-2.303 0-3.182s2.9-.879 4.006 0l.415.33M21 12a9 9 0 11-18 0 9 9 0 0118 0z",
  },
  Doctrine: {
    bg: "bg-[#DBEAFE]", text: "text-[#1D4ED8]", border: "border-[#93C5FD]", accent: "bg-[#3B82F6]",
    icon: "M12 14l9-5-9-5-9 5 9 5zm0 0l6.16-3.422a12.083 12.083 0 01.665 6.479A11.952 11.952 0 0012 20.055a11.952 11.952 0 00-6.824-2.998 12.078 12.078 0 01.665-6.479L12 14z",
  },
  Commentaire: {
    bg: "bg-[#FFEDD5]", text: "text-[#C2410C]", border: "border-[#FDBA74]", accent: "bg-[#F97316]",
    icon: "M8.625 12a.375.375 0 11-.75 0 .375.375 0 01.75 0zm0 0H8.25m4.125 0a.375.375 0 11-.75 0 .375.375 0 01.75 0zm0 0H12m4.125 0a.375.375 0 11-.75 0 .375.375 0 01.75 0zm0 0h-.375M21 12c0 4.556-4.03 8.25-9 8.25a9.764 9.764 0 01-2.555-.337A5.972 5.972 0 015.41 20.97a5.969 5.969 0 01-.474-.065 4.48 4.48 0 00.978-2.025c.09-.457-.133-.901-.467-1.226C3.93 16.178 3 14.189 3 12c0-4.556 4.03-8.25 9-8.25s9 3.694 9 8.25z",
  },
};

/// Multi-select country picker. The list is the whole world (~195), so it gets a filter box and
/// a scroll area rather than the flat button list the old 7-country version used — and selection
/// is a set, since a consultation can concern several countries at once.
/// Matching is accent-insensitive both ways: the corpus writes "Emirats" as often as "Émirats",
/// and a user typing "egypte" should still find "Égypte".
const fold = (s) =>
  (s || "")
    .normalize("NFD")
    // Strip the combining marks NFD just split off (U+0300..U+036F).
    .replace(/[̀-ͯ]/g, "")
    .toLowerCase();

function CountryPicker({ selected, onToggle, onClear }) {
  const [filter, setFilter] = useState("");
  const shown = useMemo(() => {
    const f = fold(filter.trim());
    if (!f) return COUNTRIES;
    return COUNTRIES.filter((c) => fold(c).includes(f));
  }, [filter]);

  return (
    <div className="space-y-2">
      <div className="flex items-center gap-1.5">
        <input
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
          placeholder="Filtrer les pays…"
          className="flex-1 min-w-0 bg-white border border-border rounded-lg px-2.5 py-1 text-[12.5px] text-dark focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent transition-all"
        />
        {selected.length > 0 && (
          <button
            onClick={onClear}
            className="shrink-0 text-[11px] font-bold text-muted hover:text-dark px-1.5 py-1 rounded-md hover:bg-light transition-colors"
            title="Tout désélectionner"
          >
            ✕ {selected.length}
          </button>
        )}
      </div>

      <div className="max-h-56 overflow-y-auto space-y-px pr-0.5">
        {shown.length === 0 && (
          <p className="text-[12px] text-muted px-2.5 py-2">Aucun pays trouvé.</p>
        )}
        {shown.map((c) => {
          const active = selected.includes(c);
          return (
            <button
              key={c}
              onClick={() => onToggle(c)}
              role="checkbox"
              aria-checked={active}
              className={`w-full flex items-center gap-2 text-left rounded-lg px-2.5 py-1.5 text-[13px] transition-all ${
                active
                  ? "bg-brand/15 text-dark font-semibold"
                  : "text-body hover:text-dark hover:bg-light/60"
              }`}
            >
              <span
                className={`w-3.5 h-3.5 rounded border shrink-0 flex items-center justify-center ${
                  active ? "bg-brand border-brand" : "border-border bg-white"
                }`}
              >
                {active && (
                  <svg className="w-2.5 h-2.5 text-dark" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={3.5}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M4.5 12.75l6 6 9-13.5" />
                  </svg>
                )}
              </span>
              {c}
            </button>
          );
        })}
      </div>
    </div>
  );
}
const STANDARD_KEYWORDS = [
  "TVA",
  "IS",
  "IRPP",
  "Retenue à la source",
  "Prix de transfert",
  "Établissement stable",
  "Dividendes",
  "Redevances",
  "Plus-value",
];

// Corpus year bounds. 1888 is the earliest year Tunisian legal texts in the corpus carry;
// the upper bound is "today", since a text cannot be dated in the future — a fixed literal
// would silently expire (the previous 2000..2030 both excluded older texts and allowed
// years that do not exist yet).
const YEARS = { min: 1888, max: new Date().getFullYear() };

/// Year field. Keeps a local draft while typing so a half-typed year ("1", "19") is never
/// clamped mid-keystroke nor fired at the backend — the value is committed, and only then
/// clamped into range, on blur/Enter. A blank or 0 entry reverts to the last good value
/// rather than collapsing the bound (the input's own min/max are browser hints only: they
/// constrain the spinner, but typed or pasted text bypasses them entirely).
function YearInput({ value, onCommit, label }) {
  const [draft, setDraft] = useState(String(value));
  useEffect(() => setDraft(String(value)), [value]);

  const commit = () => {
    const n = Math.trunc(Number(draft));
    if (!Number.isFinite(n) || n === 0) return setDraft(String(value));
    onCommit(Math.min(YEARS.max, Math.max(YEARS.min, n)));
  };

  return (
    <label className="flex-1 min-w-0">
      <span className="block text-[11px] font-medium text-muted mb-1">{label}</span>
      <input
        type="number"
        inputMode="numeric"
        min={YEARS.min}
        max={YEARS.max}
        value={draft}
        onChange={(e) => setDraft(e.target.value)}
        onBlur={commit}
        onKeyDown={(e) => {
          if (e.key === "Enter") e.currentTarget.blur();
        }}
        className="w-full bg-white border border-border rounded-lg px-2.5 py-1 text-[13px] text-dark focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent transition-all"
      />
    </label>
  );
}
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

const highlightHtml = (h) =>
  trimMarkEdges(
    (h.highlight || h.content || "")
      .replace(/</g, "&lt;")
      .replace(/&lt;em>/g, '<mark class="bg-brand/40 rounded-sm px-0.5 font-semibold text-dark">')
      .replace(/&lt;\/em>/g, "</mark>"),
  );

function FilterSection({ title, defaultOpen = true, count, children }) {
  const [open, setOpen] = useState(defaultOpen);
  return (
    <div className="border-b border-border/50 last:border-0">
      <button
        onClick={() => setOpen((o) => !o)}
        className="w-full flex items-center justify-between px-4 py-2.5 hover:bg-light/50 transition-colors"
      >
        <span className="text-[13px] font-bold text-dark">{title}</span>
        <div className="flex items-center gap-2">
          {count != null && (
            <span className="text-[10px] font-bold bg-brand/20 text-dark rounded-full px-1.5 py-0.5">
              {count}
            </span>
          )}
          <svg
            className={`w-4 h-4 text-muted transition-transform duration-300 ${open ? "rotate-0" : "-rotate-90"}`}
            fill="none"
            viewBox="0 0 24 24"
            stroke="currentColor"
            strokeWidth={2}
          >
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              d="M19.5 8.25l-7.5 7.5-7.5-7.5"
            />
          </svg>
        </div>
      </button>
      <div
        className={`overflow-hidden transition-all duration-300 ${EASE} ${open ? "max-h-[600px] opacity-100" : "max-h-0 opacity-0"}`}
      >
        <div className="px-4 py-3">{children}</div>
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
  buckets,
  clientSearch,
  onClientSearch,
  dateFrom,
  dateTo,
  onDates,
  countries,
  onToggleCountry,
  onClearCountries,
  keywords,
  onToggleKeyword,
  recent,
  onRecent,
  onReset,
  filtersActive,
  onClose,
  t,
}) {
  return (
    <div className="h-full flex flex-col bg-white">
      <div className="h-11 shrink-0 flex items-center justify-between px-4 border-b border-border/70">
        <div className="flex items-center gap-2">
          <p className="text-[13px] font-bold text-dark">
            {t("search.filters")}
          </p>
          {filtersActive && (
            <span className="w-4 h-4 rounded-full bg-brand text-dark text-[9px] font-extrabold flex items-center justify-center">
              !
            </span>
          )}
        </div>
        <div className="flex items-center gap-1">
          {filtersActive && (
            <button
              onClick={onReset}
              className="text-[11px] font-semibold text-muted hover:text-dark transition-colors"
            >
              {t("search.clearAll")}
            </button>
          )}
          {onClose && (
            <button
              onClick={onClose}
              className="md:hidden w-7 h-7 rounded-lg text-muted hover:text-dark flex items-center justify-center transition-colors"
            >
              <svg
                className="w-4 h-4"
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                strokeWidth={2}
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  d="M6 18L18 6M6 6l12 12"
                />
              </svg>
            </button>
          )}
        </div>
      </div>

      <div className="flex-1 overflow-y-auto bg-white">
        {mode === "law" ? (
          <>
            <FilterSection
              title={t("search.textType")}
              count={docType !== "all" ? 1 : undefined}
            >
              <div className="space-y-px">
                {DOC_TYPES.map((d) => {
                  const active = docType === d.key;
                  const cnt = buckets?.find((b) => b.key === d.key)?.count;
                  return (
                    <button
                      key={d.key}
                      onClick={() => onDocType(d.key)}
                      className={`w-full flex items-center gap-2.5 text-left rounded-lg px-2.5 py-2 text-[13px] transition-all duration-200 ${
                        active
                          ? "bg-brand/15 text-dark font-semibold"
                          : "text-body hover:text-dark hover:bg-light/60 font-medium"
                      }`}
                    >
                      <span
                        className={`w-2.5 h-2.5 rounded-full shrink-0 ${d.dot}`}
                      />
                      <span className="flex-1 truncate">{t(d.tKey)}</span>
                      {cnt != null && (
                        <span
                          className={`text-[10px] font-semibold rounded-full px-1.5 ${active ? "text-dark" : "text-muted"}`}
                        >
                          {cnt}
                        </span>
                      )}
                    </button>
                  );
                })}
              </div>
            </FilterSection>

            <FilterSection
              title={t("search.period")}
              defaultOpen={yearMin !== YEARS.min || yearMax !== YEARS.max}
            >
              <div className="flex items-center gap-2">
                {/* Committing one bound drags the other with it when they would cross,
                    so the range can never read "de 2020 à 1995". */}
                <YearInput
                  label={t("search.from")}
                  value={yearMin}
                  onCommit={(y) => onYears(y, Math.max(y, yearMax))}
                />
                <YearInput
                  label={t("search.to")}
                  value={yearMax}
                  onCommit={(y) => onYears(Math.min(y, yearMin), y)}
                />
              </div>
            </FilterSection>
          </>
        ) : (
          <>
            <FilterSection title={t("search.client")}>
              <input
                value={clientSearch}
                onChange={(e) => onClientSearch(e.target.value)}
                placeholder={t("search.searchClient")}
                className="w-full bg-white border border-border rounded-lg px-2.5 py-1 text-[13px] text-dark placeholder-muted focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent transition-all"
              />
            </FilterSection>

            <FilterSection title={t("search.creationDate")} defaultOpen={false}>
              <div className="flex items-center gap-2">
                {[
                  {
                    v: dateFrom,
                    set: (v) => onDates(v, dateTo),
                    label: t("search.fromDate"),
                  },
                  {
                    v: dateTo,
                    set: (v) => onDates(dateFrom, v),
                    label: t("search.toDate"),
                  },
                ].map((f) => (
                  <label key={f.label} className="flex-1 min-w-0">
                    <span className="block text-[11px] font-medium text-muted mb-1">
                      {f.label}
                    </span>
                    <input
                      type="date"
                      value={f.v}
                      onChange={(e) => f.set(e.target.value)}
                      className="w-full bg-white border border-border rounded-lg px-2 py-1 text-[12px] text-dark focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent transition-all"
                    />
                  </label>
                ))}
              </div>
            </FilterSection>

            <FilterSection
              title={t("search.country")}
              defaultOpen={countries.length > 0}
            >
              <CountryPicker
                selected={countries}
                onToggle={onToggleCountry}
                onClear={onClearCountries}
              />
            </FilterSection>

            <FilterSection title={t("search.keywords")}>
              <div className="flex flex-wrap gap-1.5">
                {STANDARD_KEYWORDS.map((kw) => {
                  const active = keywords.includes(kw);
                  return (
                    <button
                      key={kw}
                      onClick={() => onToggleKeyword(kw)}
                      className={`text-[11px] font-semibold rounded-full px-2.5 py-1 border transition-all duration-200 ${
                        active
                          ? "bg-brand/20 border-brand/50 text-dark"
                          : "bg-white border-border text-muted hover:text-dark hover:border-dark/30"
                      }`}
                    >
                      {kw}
                    </button>
                  );
                })}
              </div>
            </FilterSection>
          </>
        )}

        {recent.length > 0 && (
          <FilterSection
            title={t("search.recent")}
            count={recent.length}
            defaultOpen={false}
          >
            <div className="space-y-px">
              {recent.map((r, i) => (
                <button
                  key={`${r.q}-${i}`}
                  onClick={() => onRecent(r)}
                  className="w-full flex items-center gap-2 text-left rounded-lg px-2.5 py-2 text-[13px] text-body hover:text-dark hover:bg-light/60 transition-all group"
                >
                  <svg
                    className="w-3.5 h-3.5 text-muted shrink-0"
                    fill="none"
                    viewBox="0 0 24 24"
                    stroke="currentColor"
                    strokeWidth={1.8}
                  >
                    <path
                      strokeLinecap="round"
                      strokeLinejoin="round"
                      d="M12 6v6h4.5m4.5 0a9 9 0 11-18 0 9 9 0 0118 0z"
                    />
                  </svg>
                  <span className="flex-1 truncate">{r.q}</span>
                </button>
              ))}
            </div>
          </FilterSection>
        )}
      </div>
    </div>
  );
}

export default function Search() {
  const navigate = useNavigate();
  const { t } = useLanguage();
  const { toast } = useToast();
  const [pdfLoadingId, setPdfLoadingId] = useState(null);
  const [mode, setMode] = useState("law");
  const [query, setQuery] = useState("");
  const [docType, setDocType] = useState("all");
  const [yearMin, setYearMin] = useState(YEARS.min);
  const [yearMax, setYearMax] = useState(YEARS.max);
  const [clientSearch, setClientSearch] = useState("");
  const [dateFrom, setDateFrom] = useState("");
  const [dateTo, setDateTo] = useState("");
  const [countries, setCountries] = useState([]);
  const [keywords, setKeywords] = useState([]);
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

  useEffect(() => {
    inputRef.current?.focus();
  }, [mode]);
  useEffect(() => {
    localStorage.setItem("taxmind.search.filters", filtersOpen ? "1" : "0");
  }, [filtersOpen]);

  const lawFiltersActive =
    docType !== "all" || yearMin !== YEARS.min || yearMax !== YEARS.max;
  const consFiltersActive =
    clientSearch !== "" ||
    dateFrom !== "" ||
    dateTo !== "" ||
    countries.length > 0 ||
    keywords.length > 0;
  const filtersActive = mode === "law" ? lawFiltersActive : consFiltersActive;

  const remember = (q, m) => {
    setRecent((prev) => {
      const next = [{ q, mode: m }, ...prev.filter((r) => r.q !== q)].slice(
        0,
        8,
      );
      try {
        localStorage.setItem(RECENT_KEY, JSON.stringify(next));
      } catch {}
      return next;
    });
  };

  const toggleKeyword = (kw) => {
    setKeywords((prev) =>
      prev.includes(kw) ? prev.filter((k) => k !== kw) : [...prev, kw],
    );
  };

  const run = async ({
    q = query,
    m = mode,
    dt = docType,
    ymin = yearMin,
    ymax = yearMax,
  } = {}) => {
    const text = q.trim();
    if (loading) return;
    // Filter-only search: allow an empty query as long as at least one filter is set, so the
    // user can just pick filters (a doc type, a period, a client…) and browse the data.
    const lawActive = dt !== "all" || ymin !== YEARS.min || ymax !== YEARS.max;
    const consActive =
      clientSearch !== "" ||
      dateFrom !== "" ||
      dateTo !== "" ||
      countries.length > 0 ||
      keywords.length > 0;
    if (!text && !(m === "law" ? lawActive : consActive)) return;
    setLoading(true);
    setError("");
    setSearched(true);
    setViewing(null);
    scrollRef.current?.scrollTo({ top: 0 });
    try {
      if (m === "law") {
        const { data } = await fiscalService.search({
          query: text,
          docType: dt,
          yearMin: ymin,
          yearMax: ymax,
          size: 30,
        });
        setLawRes(data.data);
      } else {
        // Date range is filtered server-side; client / country / keyword are applied here on
        // the returned list (the consultation record has no dedicated country column, so we
        // match the country name against its text).
        const { data } = await fiscalService.list(
          text,
          false,
          dateFrom || undefined,
          dateTo || undefined,
        );
        let list = data.data || [];
        if (clientSearch) {
          const q2 = clientSearch.toLowerCase();
          list = list.filter((c) => (c.clientName || "").toLowerCase().includes(q2));
        }
        if (countries.length) {
          // OR across the selection: keep a consultation matching ANY chosen country.
          const wanted = countries.map(fold);
          list = list.filter((c) => {
            const hay = fold(`${c.fiscalQuestion || ""} ${c.clientName || ""}`);
            return wanted.some((w) => hay.includes(w));
          });
        }
        if (keywords.length) {
          list = list.filter((c) => {
            const hay = `${c.fiscalQuestion || ""} ${c.reference || ""}`.toLowerCase();
            return keywords.some((kw) => hay.includes(kw.toLowerCase()));
          });
        }
        setConsRes(list);
      }
      if (text) remember(text, m);
    } catch (err) {
      setError(
        err.response?.data?.message ||
          t("search.searchFailed"),
      );
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
    if (searched) run({ dt });
  };

  const pickYears = (ymin, ymax) => {
    setYearMin(ymin);
    setYearMax(ymax);
    if (searched) run({ ymin, ymax });
  };

  const resetFilters = () => {
    if (mode === "law") {
      setDocType("all");
      setYearMin(YEARS.min);
      setYearMax(YEARS.max);
      if (searched && query.trim())
        run({ dt: "all", ymin: YEARS.min, ymax: YEARS.max });
      else if (searched) {
        setLawRes(null);
        setSearched(false);
      }
    } else {
      setClientSearch("");
      setDateFrom("");
      setDateTo("");
      setCountries([]);
      setKeywords([]);
    }
  };

  const pickRecent = (r) => {
    setQuery(r.q);
    setMobileFilters(false);
    if (r.mode !== mode) switchMode(r.mode);
    run({ q: r.q, m: r.mode });
  };

  const openHit = async (hits, i) => {
    const list = hits.map((x, j) => normalizeSource(x, j + 1));
    setViewList(list);
    setViewing(list[i]);
    // Google model: clicking a result opens the WHOLE document (all passages, in order).
    const h = hits[i];
    if (!h?.documentId) return;
    try {
      const { data } = await fiscalService.getDocument(h.documentId);
      const full = data?.data;
      if (!full?.text) return;
      const merge = (s) => ({
        ...s,
        text: full.text,
        docName: (full.filename || s.docName).replace(/[-_]/g, " "),
        whole: true,
      });
      setViewing((v) => (v && v.index === i + 1 ? merge(v) : v));
      setViewList((l) => l.map((s, j) => (j === i ? merge(s) : s)));
    } catch {
      /* keep the passage view if the full-document fetch fails */
    }
  };

  const openPdf = async (documentId, page) => {
    if (!documentId || pdfLoadingId) return;
    setPdfLoadingId(documentId);
    try {
      await openDocumentPdf(documentId, page);
    } catch (err) {
      toast(
        err?.message === "popup-blocked"
          ? t("search.pdfPopupBlocked")
          : t("search.pdfUnavailable"),
        "error",
      );
    } finally {
      setPdfLoadingId(null);
    }
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
      buckets={lawRes?.docTypeBuckets}
      clientSearch={clientSearch}
      onClientSearch={setClientSearch}
      dateFrom={dateFrom}
      dateTo={dateTo}
      onDates={(f, t) => {
        setDateFrom(f);
        setDateTo(t);
      }}
      countries={countries}
      onToggleCountry={(c) =>
        setCountries((prev) =>
          prev.includes(c) ? prev.filter((x) => x !== c) : [...prev, c],
        )
      }
      onClearCountries={() => setCountries([])}
      keywords={keywords}
      onToggleKeyword={toggleKeyword}
      recent={recent}
      onRecent={pickRecent}
      onReset={resetFilters}
      filtersActive={filtersActive}
      onClose={closeFn}
      t={t}
    />
  );

  return (
    <div className="h-full flex flex-col bg-sand">
      {/* Chrome-style tab bar */}
      <div className="shrink-0 flex items-end bg-sand px-2 pt-1.5 border-b border-border">
        {[
          {
            key: "law",
            label: t("search.lawTab"),
            color: "text-indigo-500",
            activeBg: "bg-indigo-500",
            icon: (
              <svg
                className="w-3.5 h-3.5"
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                strokeWidth={2}
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  d="M12 6.042A8.967 8.967 0 006 3.75c-1.052 0-2.062.18-3 .512v14.25A8.987 8.987 0 016 18c2.305 0 4.408.867 6 2.292m0-14.25a8.966 8.966 0 016-2.292c1.052 0 2.062.18 3 .512v14.25A8.987 8.987 0 0018 18a8.967 8.967 0 00-6 2.292m0-14.25v14.25"
                />
              </svg>
            ),
          },
          {
            key: "consultations",
            label: t("search.consultationsTab"),
            color: "text-emerald-500",
            activeBg: "bg-emerald-500",
            icon: (
              <svg
                className="w-3.5 h-3.5"
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                strokeWidth={2}
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  d="M19.5 14.25v-2.625a3.375 3.375 0 00-3.375-3.375h-1.5A1.125 1.125 0 0113.5 7.125v-1.5a3.375 3.375 0 00-3.375-3.375H8.25m2.25 0H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 00-9-9z"
                />
              </svg>
            ),
          },
        ].map((m) => {
          const active = mode === m.key;
          return (
            <button
              key={m.key}
              onClick={() => switchMode(m.key)}
              className={`relative flex items-center gap-1.5 text-[12.5px] font-semibold px-4 py-2 rounded-t-lg transition-all duration-200 ${
                active
                  ? "bg-white text-dark border border-border border-b-white -mb-px z-10"
                  : "text-muted hover:text-dark hover:bg-white/50 border border-transparent"
              }`}
            >
              {m.icon}
              {m.label}
              {active && (
                <span className={`absolute bottom-0 left-2 right-2 h-[2px] rounded-full ${m.activeBg}`} />
              )}
            </button>
          );
        })}
      </div>

      <div className="flex-1 min-h-0 flex">
        {/* Filters panel — desktop */}
        <aside
          className={`hidden md:block shrink-0 overflow-hidden transition-[width] duration-300 ${EASE} ${
            filtersOpen ? "w-[240px] border-r border-border" : "w-0"
          }`}
        >
          <div className="w-[240px] h-full">{filtersPanel()}</div>
        </aside>

        {/* Filters — mobile overlay */}
        {mobileFilters && (
          <div className="md:hidden fixed inset-0 z-[70]">
            <div
              className="absolute inset-0 bg-dark/40 backdrop-blur-[2px] animate-fade-in"
              onClick={() => setMobileFilters(false)}
            />
            <div className="absolute left-0 top-0 h-full w-[280px] shadow-2xl animate-slide-in-left">
              {filtersPanel(() => setMobileFilters(false))}
            </div>
          </div>
        )}

        {/* Results column */}
        <section className="flex-1 min-w-0 flex flex-col">
          {/* Slim search bar */}
          <div className="shrink-0 flex items-center gap-2 px-3 py-2 border-border/60 bg-white">
            <button
              onClick={() => {
                if (window.innerWidth < 768) setMobileFilters(true);
                else setFiltersOpen((o) => !o);
              }}
              className="relative w-8 h-8 rounded-lg text-muted hover:text-dark hover:bg-light flex items-center justify-center transition-colors shrink-0"
              aria-label="Filtres"
            >
              <svg
                className="w-4 h-4"
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                strokeWidth={1.8}
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  d="M12 3c2.755 0 5.455.232 8.083.678.533.09.917.556.917 1.096v1.044a2.25 2.25 0 01-.659 1.591l-5.432 5.432a2.25 2.25 0 00-.659 1.591v2.927a2.25 2.25 0 01-1.244 2.013L9.75 21v-6.568a2.25 2.25 0 00-.659-1.591L3.659 7.409A2.25 2.25 0 013 5.818V4.774c0-.54.384-1.006.917-1.096A48.32 48.32 0 0112 3z"
                />
              </svg>
              {filtersActive && (
                <span className="absolute top-1.5 right-1.5 w-1.5 h-1.5 rounded-full bg-brand" />
              )}
            </button>

            <form
              onSubmit={(e) => {
                e.preventDefault();
                run();
              }}
              className="flex-1 min-w-0 flex items-center"
            >
              <div className="flex-1 min-w-0 relative flex items-center">
                <svg
                  className="absolute left-3 w-4 h-4 text-muted pointer-events-none"
                  fill="none"
                  viewBox="0 0 24 24"
                  stroke="currentColor"
                  strokeWidth={2}
                >
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    d="M21 21l-5.197-5.197m0 0A7.5 7.5 0 105.196 5.196a7.5 7.5 0 0010.607 10.607z"
                  />
                </svg>
                <input
                  ref={inputRef}
                  value={query}
                  onChange={(e) => setQuery(e.target.value)}
                  placeholder={
                    mode === "law"
                      ? t("search.searchLaw")
                      : t("search.searchConsultations")
                  }
                  className="w-full bg-light/60 border border-border rounded-lg pl-9 pr-3 py-2 text-[13px] text-dark placeholder-muted focus:outline-none focus:ring-2 focus:ring-brand/60 focus:border-transparent focus:bg-white transition-all"
                />
              </div>
              <button
                type="submit"
                disabled={loading || (!query.trim() && !filtersActive)}
                className="ml-2 bg-gradient-to-r from-brand to-[#F59E0B] text-dark text-[12px] font-bold rounded-lg px-4 py-2 hover:shadow-md hover:shadow-brand/30 active:scale-95 disabled:opacity-30 transition-all shrink-0"
              >
                {loading ? (
                  <span className="w-3.5 h-3.5 border-2 border-dark/30 border-t-dark rounded-full animate-spin inline-block" />
                ) : (
                  t("search.searchBtn")
                )}
              </button>
            </form>

            <div className="hidden sm:block shrink-0 text-[11px] text-muted">
              {!loading && mode === "law" && lawRes && (
                <span className="animate-fade-in">
                  <b className="text-dark">{lawRes.total}</b> {t("search.results")} ·{" "}
                  {Math.round(lawRes.elapsedMs)}ms
                </span>
              )}
              {!loading && mode === "consultations" && consRes && (
                <span className="animate-fade-in">
                  <b className="text-dark">{consRes.length}</b>{" "}
                  {consRes.length !== 1 ? t("search.consultations") : t("search.consultation")}
                </span>
              )}
            </div>
          </div>

          {/* Results area */}
          <div ref={scrollRef} className="flex-1 overflow-y-auto bg-white">
            {!searched && !loading ? (
              <div className="flex flex-col items-center justify-center h-full px-4 animate-fade-up relative overflow-hidden">
                <div className="absolute top-10 left-10 w-32 h-32 bg-brand/10 rounded-full blur-3xl pointer-events-none" />
                <div className="absolute bottom-20 right-16 w-40 h-40 bg-indigo-400/10 rounded-full blur-3xl pointer-events-none" />
                <div className="absolute top-1/3 right-1/4 w-24 h-24 bg-emerald-400/8 rounded-full blur-2xl pointer-events-none" />
                <div className="w-14 h-14 rounded-2xl bg-gradient-to-br from-brand to-[#F59E0B] flex items-center justify-center mb-5 shadow-lg shadow-brand/20">
                  <svg className="w-7 h-7 text-dark" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M21 21l-5.197-5.197m0 0A7.5 7.5 0 105.196 5.196a7.5 7.5 0 0010.607 10.607z" />
                  </svg>
                </div>
                <h2 className="font-display text-3xl sm:text-4xl text-dark text-center mb-3">
                  {t("search.whatLooking")}
                </h2>
                <p className="text-muted text-[14px] text-center max-w-sm mb-6">
                  {mode === "law"
                    ? t("search.lawHint")
                    : t("search.consHint")}
                </p>
                <div className="flex flex-wrap justify-center gap-2">
                  {(mode === "law"
                    ? [
                        { q: "retenue à la source", color: "hover:border-indigo-400 hover:bg-indigo-50 dark:hover:bg-indigo-500/10" },
                        { q: "TVA services export", color: "hover:border-emerald-400 hover:bg-emerald-50 dark:hover:bg-emerald-500/10" },
                        { q: "convention Tunisie-France", color: "hover:border-brand hover:bg-brand/10" },
                        { q: "amortissement", color: "hover:border-purple-400 hover:bg-purple-50 dark:hover:bg-purple-500/10" },
                      ]
                    : [
                        { q: "nom du client", color: "hover:border-blue-400 hover:bg-blue-50 dark:hover:bg-blue-500/10" },
                        { q: "référence mémo", color: "hover:border-brand hover:bg-brand/10" },
                        { q: "question fiscale", color: "hover:border-violet-400 hover:bg-violet-50 dark:hover:bg-violet-500/10" },
                      ]
                  ).map((item, i) => (
                    <button
                      key={item.q}
                      onClick={() => {
                        setQuery(item.q);
                        run({ q: item.q });
                      }}
                      style={{ animationDelay: `${i * 60}ms` }}
                      className={`text-[11.5px] font-semibold text-body bg-white border border-border hover:text-dark rounded-full px-3.5 py-2 transition-all hover:-translate-y-0.5 hover:shadow-sm animate-pop opacity-0 ${item.color}`}
                    >
                      {item.q}
                    </button>
                  ))}
                </div>
              </div>
            ) : (
              <div className="max-w-3xl mx-auto w-full px-3 sm:px-5 py-3">
                {error && (
                  <div className="mb-2 p-3 bg-red-50 border border-red-200 rounded-xl text-red-600 text-[13px] flex items-center gap-2 animate-pop opacity-0">
                    <svg
                      className="w-4 h-4 shrink-0"
                      fill="none"
                      viewBox="0 0 24 24"
                      stroke="currentColor"
                      strokeWidth={2}
                    >
                      <path
                        strokeLinecap="round"
                        strokeLinejoin="round"
                        d="M12 9v3.75m9-.75a9 9 0 11-18 0 9 9 0 0118 0zm-9 3.75h.008v.008H12v-.008z"
                      />
                    </svg>
                    {error}
                  </div>
                )}

                {loading && (
                  <div className="space-y-2">
                    {[0, 1, 2, 3, 4].map((i) => (
                      <div
                        key={i}
                        className="bg-white border border-border rounded-xl p-4 animate-pulse"
                        style={{ animationDelay: `${i * 80}ms` }}
                      >
                        <div className="flex items-center gap-3">
                          <div className="w-6 h-6 bg-light rounded-md" />
                          <div className="h-3.5 bg-light rounded w-1/3" />
                          <div className="ml-auto h-4 bg-light rounded-full w-16" />
                        </div>
                        <div className="mt-3 space-y-1.5">
                          <div className="h-3 bg-light rounded w-full" />
                          <div className="h-3 bg-light rounded w-4/5" />
                        </div>
                      </div>
                    ))}
                  </div>
                )}

                {!loading && mode === "law" && lawRes && (
                  <>
                    {lawRes.total === 0 && (
                      <div className="mt-4 bg-gradient-to-br from-indigo-50/50 to-brand/5 dark:from-indigo-500/5 dark:to-brand/5 border border-dashed border-indigo-200 dark:border-indigo-500/20 rounded-xl p-8 text-center animate-fade-in">
                        <div className="w-10 h-10 mx-auto rounded-xl bg-indigo-100 dark:bg-indigo-500/15 flex items-center justify-center mb-3">
                          <svg className="w-5 h-5 text-indigo-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                            <path strokeLinecap="round" strokeLinejoin="round" d="M21 21l-5.197-5.197m0 0A7.5 7.5 0 105.196 5.196a7.5 7.5 0 0010.607 10.607z" />
                          </svg>
                        </div>
                        <p className="text-dark font-semibold text-[14px]">
                          {t("search.noLawResults")}
                        </p>
                        <p className="text-muted text-[13px] mt-1">
                          {t("search.noLawHint")}
                        </p>
                      </div>
                    )}
                    <div className="space-y-2">
                      {hits.map((h, i) => {
                        const open = viewing && viewing.index === i + 1;
                        const badge = TYPE_BADGE[h.documentType] || {
                          bg: "bg-light",
                          text: "text-body",
                          border: "border-border",
                          accent: "bg-border",
                          icon: null,
                        };
                        const pdfBusy = pdfLoadingId === h.documentId;
                        return (
                          <div
                            key={h.id || i}
                            role="button"
                            tabIndex={0}
                            onClick={() => openHit(hits, i)}
                            onKeyDown={(e) => {
                              if (e.key === "Enter" || e.key === " ") {
                                e.preventDefault();
                                openHit(hits, i);
                              }
                            }}
                            style={{
                              animationDelay: `${Math.min(i, 8) * 40}ms`,
                            }}
                            className={`relative w-full text-left bg-white border rounded-xl pl-4 pr-3 py-3.5 transition-all duration-200 group animate-pop opacity-0 cursor-pointer overflow-hidden ${
                              open
                                ? "border-brand ring-1 ring-brand/40 shadow-md"
                                : "border-border hover:border-dark/20 hover:shadow-sm"
                            }`}
                          >
                            {/* Left accent strip — instant color cue for the document family. */}
                            <span
                              className={`absolute left-0 top-0 bottom-0 w-[3px] ${badge.accent}`}
                            />
                            <div className="flex items-start gap-3">
                              <span
                                className={`shrink-0 mt-0.5 w-7 h-7 rounded-lg text-[11px] font-bold flex items-center justify-center transition-colors ${
                                  open
                                    ? "bg-brand text-dark"
                                    : "bg-light text-muted group-hover:bg-brand/30 group-hover:text-dark"
                                }`}
                              >
                                {i + 1}
                              </span>
                              <div className="flex-1 min-w-0">
                                <div className="flex items-center gap-2 flex-wrap">
                                  {h.documentType && (
                                    <span
                                      className={`inline-flex items-center gap-1 text-[9.5px] font-bold rounded-full pl-1.5 pr-2 py-0.5 border shrink-0 ${badge.bg} ${badge.text} ${badge.border}`}
                                    >
                                      {badge.icon && (
                                        <svg
                                          className="w-2.5 h-2.5"
                                          fill="none"
                                          viewBox="0 0 24 24"
                                          stroke="currentColor"
                                          strokeWidth={2.2}
                                        >
                                          <path
                                            strokeLinecap="round"
                                            strokeLinejoin="round"
                                            d={badge.icon}
                                          />
                                        </svg>
                                      )}
                                      {h.documentType}
                                    </span>
                                  )}
                                  <p className="text-[13.5px] font-bold text-dark capitalize truncate">
                                    {(h.filename || "Document").replace(
                                      /[-_]/g,
                                      " ",
                                    )}
                                    {h.articleNumber
                                      ? ` · ${h.articleNumber}`
                                      : ""}
                                  </p>
                                  {h.matchCount > 1 && (
                                    <span className="text-[9px] font-bold rounded-full px-2 py-0.5 bg-brand/15 text-dark border border-brand/30 shrink-0">
                                      {h.matchCount} passages
                                    </span>
                                  )}
                                </div>
                                {(h.sectionTitle || h.pageNumber != null) && (
                                  <p className="text-[11px] text-muted mt-1 truncate">
                                    {h.sectionTitle}
                                    {h.sectionTitle && h.pageNumber != null ? " · " : ""}
                                    {h.pageNumber != null ? `p. ${h.pageNumber}` : ""}
                                  </p>
                                )}
                                <p
                                  className="mt-2 text-[13px] text-body leading-[1.6] line-clamp-3"
                                  dangerouslySetInnerHTML={{
                                    __html: highlightHtml(h),
                                  }}
                                />
                              </div>
                              <div className="flex flex-col items-center gap-1.5 shrink-0">
                                <button
                                  type="button"
                                  onClick={(e) => {
                                    e.stopPropagation();
                                    openPdf(h.documentId, h.pageNumber);
                                  }}
                                  disabled={pdfBusy}
                                  title={t("search.viewPdf")}
                                  aria-label={t("search.viewPdf")}
                                  className="w-7 h-7 rounded-lg border border-border text-muted hover:text-dark hover:border-dark/30 hover:bg-light/70 flex items-center justify-center transition-colors disabled:opacity-40"
                                >
                                  {pdfBusy ? (
                                    <span className="w-3.5 h-3.5 border-2 border-muted/40 border-t-dark rounded-full animate-spin" />
                                  ) : (
                                    <svg
                                      className="w-3.5 h-3.5"
                                      fill="none"
                                      viewBox="0 0 24 24"
                                      stroke="currentColor"
                                      strokeWidth={1.8}
                                    >
                                      <path
                                        strokeLinecap="round"
                                        strokeLinejoin="round"
                                        d="M19.5 14.25v-2.625a3.375 3.375 0 00-3.375-3.375h-1.5A1.125 1.125 0 0113.5 7.125v-1.5a3.375 3.375 0 00-3.375-3.375H8.25m2.25 0H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 00-9-9z"
                                      />
                                    </svg>
                                  )}
                                </button>
                                <svg
                                  className={`w-4 h-4 transition-all ${open ? "text-brand" : "text-border group-hover:text-muted"}`}
                                  fill="none"
                                  viewBox="0 0 24 24"
                                  stroke="currentColor"
                                  strokeWidth={2}
                                >
                                  <path
                                    strokeLinecap="round"
                                    strokeLinejoin="round"
                                    d="M8.25 4.5l7.5 7.5-7.5 7.5"
                                  />
                                </svg>
                              </div>
                            </div>
                          </div>
                        );
                      })}
                    </div>
                  </>
                )}

                {!loading && mode === "consultations" && consRes && (
                  <>
                    {consRes.length === 0 && (
                      <div className="mt-4 bg-gradient-to-br from-emerald-50/50 to-brand/5 dark:from-emerald-500/5 dark:to-brand/5 border border-dashed border-emerald-200 dark:border-emerald-500/20 rounded-xl p-8 text-center animate-fade-in">
                        <div className="w-10 h-10 mx-auto rounded-xl bg-emerald-100 dark:bg-emerald-500/15 flex items-center justify-center mb-3">
                          <svg className="w-5 h-5 text-emerald-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                            <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 14.25v-2.625a3.375 3.375 0 00-3.375-3.375h-1.5A1.125 1.125 0 0113.5 7.125v-1.5a3.375 3.375 0 00-3.375-3.375H8.25m2.25 0H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 00-9-9z" />
                          </svg>
                        </div>
                        <p className="text-dark font-semibold text-[14px]">
                          {t("search.noConsResults")}
                        </p>
                        <p className="text-muted text-[13px] mt-1">
                          {t("search.noConsHint")}
                        </p>
                      </div>
                    )}
                    <div className="space-y-1.5">
                      {consRes.map((c, i) => (
                        <button
                          key={c.id}
                          onClick={() => navigate(`/app/consultations/${c.id}`)}
                          style={{
                            animationDelay: `${Math.min(i, 8) * 40}ms`,
                          }}
                          className="w-full text-left bg-white border border-border rounded-xl px-4 py-3 hover:border-dark/20 hover:shadow-sm transition-all duration-200 group animate-pop opacity-0"
                        >
                          <div className="flex items-start gap-3">
                            <div className="w-8 h-8 rounded-lg bg-brand/10 flex items-center justify-center shrink-0 mt-0.5">
                              <svg
                                className="w-4 h-4 text-dark/70"
                                fill="none"
                                viewBox="0 0 24 24"
                                stroke="currentColor"
                                strokeWidth={1.5}
                              >
                                <path
                                  strokeLinecap="round"
                                  strokeLinejoin="round"
                                  d="M19.5 14.25v-2.625a3.375 3.375 0 00-3.375-3.375h-1.5A1.125 1.125 0 0113.5 7.125v-1.5a3.375 3.375 0 00-3.375-3.375H8.25m2.25 0H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 00-9-9z"
                                />
                              </svg>
                            </div>
                            <div className="flex-1 min-w-0">
                              <div className="flex items-center gap-2 flex-wrap">
                                <span className="text-[10px] font-bold text-dark bg-brand/15 rounded px-1.5 py-0.5">
                                  {c.reference}
                                </span>
                                {c.isInternational && (
                                  <span className="text-[10px] font-semibold text-[#1D4ED8] bg-[#DBEAFE] rounded px-1.5 py-0.5">
                                    International
                                  </span>
                                )}
                                <span className="text-[11px] text-muted ml-auto shrink-0">
                                  {fmtDate(c.createdAt)}
                                </span>
                              </div>
                              <p className="text-[13px] font-semibold text-dark mt-1">
                                {c.clientName}
                              </p>
                              <p className="text-[12px] text-body line-clamp-1 mt-0.5">
                                {c.fiscalQuestion}
                              </p>
                            </div>
                            <svg
                              className="w-4 h-4 text-border group-hover:text-muted shrink-0 mt-1 transition-colors"
                              fill="none"
                              viewBox="0 0 24 24"
                              stroke="currentColor"
                              strokeWidth={2}
                            >
                              <path
                                strokeLinecap="round"
                                strokeLinejoin="round"
                                d="M8.25 4.5l7.5 7.5-7.5 7.5"
                              />
                            </svg>
                          </div>
                        </button>
                      ))}
                    </div>
                  </>
                )}
              </div>
            )}
          </div>
        </section>

        {/* Document panel — desktop */}
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
                onOpenPdf={
                  viewing.documentId
                    ? () => openPdf(viewing.documentId, viewing.page)
                    : undefined
                }
                pdfLoading={pdfLoadingId === viewing.documentId}
              />
            </div>
          )}
        </aside>

        {/* Document panel — mobile overlay */}
        {viewing && (
          <div
            className="lg:hidden fixed inset-0 z-[80]"
            role="dialog"
            aria-modal="true"
          >
            <div
              className="absolute inset-0 bg-dark/40 backdrop-blur-[2px] animate-fade-in"
              onClick={() => setViewing(null)}
            />
            <div className="absolute right-0 top-0 h-full w-full max-w-md shadow-2xl animate-slide-in-right">
              <SourcePanel
                source={viewing}
                sources={viewList}
                onNavigate={setViewing}
                onClose={() => setViewing(null)}
                onOpenPdf={
                  viewing.documentId
                    ? () => openPdf(viewing.documentId, viewing.page)
                    : undefined
                }
                pdfLoading={pdfLoadingId === viewing.documentId}
              />
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
