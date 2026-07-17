import { useEffect } from "react";

/* Document viewer panel — renders a legal source "in the document":
   header with metadata, the passage laid out like a page, and a footer with
   relevance + prev/next navigation between citations.

   Pure column component (h-full flex-col): embed it inline as a third screen
   pane (Chat) or wrap it in an overlay (SourceDrawer). Pressing Escape calls
   `onClose` wherever it lives. */

const TYPE_STYLES = {
  Code: "bg-dark text-white",
  Convention: "bg-brand text-dark",
  LoiFinances: "bg-[#EDE9FE] text-[#5B21B6]",
  Doctrine: "bg-[#DBEAFE] text-[#1D4ED8]",
  Commentaire: "bg-[#FFEDD5] text-[#C2410C]",
};

export function normalizeSource(s = {}, index) {
  return {
    index: index ?? s.index,
    documentId: s.documentId || s.docId || "",
    docName: (s.docName || s.filename || "Document").replace(/[-_]/g, " "),
    docType: s.docType || s.documentType || s.category || "",
    articleRef: s.articleRef || s.articleNumber || "",
    sectionTitle: s.sectionTitle || "",
    year: s.year || "",
    page: s.page ?? s.pageNum ?? s.pageNumber ?? null,
    text: s.text || s.content || "",
    score: s.score,
    isExpert: !!s.isExpert,
  };
}

export default function SourcePanel({
  source,
  onClose,
  sources = [],
  onNavigate,
  onOpenPdf,
  pdfLoading = false,
}) {
  useEffect(() => {
    const fn = (e) => {
      if (e.key === "Escape") onClose();
    };
    document.addEventListener("keydown", fn);
    return () => document.removeEventListener("keydown", fn);
  }, [onClose]);

  if (!source) return null;
  const s = source;
  const pos = sources.length > 1 ? sources.findIndex((x) => x === s) : -1;
  const typeCls =
    TYPE_STYLES[s.docType] || "bg-light text-body border border-border";
  // Every caller passes a 0..1 relevance: consultation/chat sources are similarity scores
  // already, and Search normalises Elasticsearch's raw BM25 _score against the result set's
  // maxScore before handing it over. Anything outside 0..1 is a score whose scale we do not
  // know, so we show NO badge rather than invent a number.
  //
  // This previously read `s.score <= 1 ? s.score*100 : (Math.min(s.score,30)/30)*100` — i.e.
  // it treated an unbounded BM25 score as "out of 30", so every score >= 30 rendered as 100%.
  // Real queries clear 30 easily ("retenue a la source honoraires personnes morales" scored
  // 43.5/41.3/41.2/40.3/39.3), so the panel claimed a perfect match on every result of every
  // realistic search; only single-word queries stayed under the cap and appeared to vary.
  const relevance =
    typeof s.score === "number" && s.score >= 0 && s.score <= 1
      ? Math.round(s.score * 100)
      : null;

  return (
    <div className="h-full bg-white flex flex-col">
      {/* header */}
      <div className="px-5 py-4 border-b border-border flex items-start justify-between gap-3 shrink-0">
        <div className="min-w-0">
          <div className="flex items-center gap-2 mb-2 flex-wrap">
            {s.docType && (
              <span
                className={`text-[10px] font-bold rounded-full px-2.5 py-1 uppercase tracking-wider ${typeCls}`}
              >
                {s.docType}
              </span>
            )}
            {s.isExpert && (
              <span className="text-[10px] font-bold bg-brand/30 text-dark rounded-full px-2.5 py-1 uppercase tracking-wider">
                Expert
              </span>
            )}
            {s.index != null && (
              <span className="text-[10px] font-bold text-muted">
                Source {s.index}
              </span>
            )}
          </div>
          <h2 className="text-[16px] font-extrabold text-dark leading-snug capitalize">
            {s.docName}
          </h2>
          <p className="text-[12.5px] text-muted mt-1 flex items-center gap-2 flex-wrap">
            {s.articleRef && (
              <span className="font-semibold text-body">{s.articleRef}</span>
            )}
            {s.year && <span>· {s.year}</span>}
            {s.page != null && <span>· page {s.page}</span>}
          </p>
        </div>
        <div className="flex items-center gap-2 shrink-0">
          {onOpenPdf && (
            <button
              onClick={onOpenPdf}
              disabled={pdfLoading}
              className="h-9 rounded-xl border border-border text-body hover:text-dark hover:border-dark/30 hover:bg-light/60 flex items-center gap-1.5 px-3 text-[12px] font-semibold transition-colors disabled:opacity-40"
              title={s.page != null ? `Ouvrir le PDF à la page ${s.page}` : "Ouvrir le PDF"}
            >
              {pdfLoading ? (
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
              <span className="hidden sm:inline">Voir le PDF</span>
            </button>
          )}
          <button
            onClick={onClose}
            className="shrink-0 w-9 h-9 rounded-xl border border-border text-muted hover:text-dark hover:border-dark/30 flex items-center justify-center transition-colors"
            aria-label="Fermer"
            title="Fermer (Échap)"
          >
            <svg
              className="w-[18px] h-[18px]"
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
        </div>
      </div>

      {/* document page */}
      <div className="flex-1 overflow-y-auto bg-sand px-5 py-5">
        <div className="bg-white border border-border rounded-2xl shadow-sm px-7 py-7 relative overflow-hidden animate-fade-in">
          <div className="absolute top-0 left-0 w-full h-1 bg-brand" />
          <p className="text-[11px] font-bold text-muted uppercase tracking-[0.2em] mb-1.5">
            {s.whole ? "Document complet" : "Extrait du document"}
          </p>
          {s.sectionTitle && (
            <p className="text-sm font-bold text-dark mb-4">{s.sectionTitle}</p>
          )}
          <p className="text-[14.5px] text-dark leading-[1.85] whitespace-pre-wrap">
            {s.text || "— Texte du passage indisponible —"}
          </p>
        </div>
      </div>

      {/* footer */}
      <div className="px-5 py-3.5 border-t border-border flex items-center justify-between gap-4 bg-white shrink-0">
        {relevance != null ? (
          <div className="flex items-center gap-3 min-w-0">
            <span className="text-[11px] font-bold text-muted uppercase tracking-wider shrink-0">
              Pertinence
            </span>
            <div className="w-24 h-1.5 bg-light rounded-full overflow-hidden">
              <div
                className="h-full bg-brand rounded-full transition-all duration-500"
                style={{ width: `${relevance}%` }}
              />
            </div>
            <span className="text-xs font-bold text-dark">{relevance}%</span>
          </div>
        ) : (
          <span />
        )}
        {pos >= 0 && onNavigate && (
          <div className="flex items-center gap-1.5 shrink-0">
            <button
              onClick={() =>
                onNavigate(sources[(pos - 1 + sources.length) % sources.length])
              }
              className="w-8 h-8 rounded-lg border border-border text-body hover:text-dark hover:border-dark/30 flex items-center justify-center transition-colors"
              aria-label="Source précédente"
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
                  d="M15.75 19.5L8.25 12l7.5-7.5"
                />
              </svg>
            </button>
            <span className="text-xs font-semibold text-muted px-1">
              {pos + 1} / {sources.length}
            </span>
            <button
              onClick={() => onNavigate(sources[(pos + 1) % sources.length])}
              className="w-8 h-8 rounded-lg border border-border text-body hover:text-dark hover:border-dark/30 flex items-center justify-center transition-colors"
              aria-label="Source suivante"
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
                  d="M8.25 4.5l7.5 7.5-7.5 7.5"
                />
              </svg>
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
