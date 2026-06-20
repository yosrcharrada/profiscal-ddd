/* Numbered, clickable citation chips shown under an AI answer.
   Click opens the passage in the SourceDrawer (handled by the parent). */
export default function SourceChips({ sources = [], onOpen, compact = false }) {
  if (!sources.length) return null;
  return (
    <div className="mt-3 pt-3 border-t border-border/70">
      <p className="text-[10px] font-bold text-muted uppercase tracking-[0.18em] mb-2">{sources.length} source{sources.length > 1 ? 's' : ''} juridique{sources.length > 1 ? 's' : ''}</p>
      <div className={compact ? 'flex flex-wrap gap-1.5' : 'grid sm:grid-cols-2 gap-1.5'}>
        {sources.map((s, i) => (
          <button key={i} type="button" onClick={() => onOpen(i)}
            className={`group flex items-center gap-2 text-left bg-white border border-border rounded-lg hover:border-dark/30 hover:shadow-sm transition-all ${compact ? 'px-2 py-1' : 'px-2.5 py-1.5 min-w-0'}`}>
            <span className="shrink-0 w-5 h-5 rounded-md bg-brand/40 group-hover:bg-brand text-dark text-[10px] font-extrabold flex items-center justify-center transition-colors">{i + 1}</span>
            <span className="min-w-0">
              <span className={`block font-semibold text-dark truncate capitalize ${compact ? 'text-[11px] max-w-[140px]' : 'text-[12px]'}`}>
                {(s.docName || s.filename || 'Document').replace(/[-_]/g, ' ')}
              </span>
              {!compact && (s.articleRef || s.pageNum != null) && (
                <span className="block text-[10.5px] text-muted truncate">{s.articleRef || ''}{s.articleRef && s.pageNum != null ? ' · ' : ''}{s.pageNum != null ? `p.${s.pageNum}` : ''}</span>
              )}
            </span>
            <svg className="w-3.5 h-3.5 text-muted/0 group-hover:text-muted ml-auto shrink-0 transition-colors" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M13.5 6H5.25A2.25 2.25 0 003 8.25v10.5A2.25 2.25 0 005.25 21h10.5A2.25 2.25 0 0018 18.75V10.5m-10.5 6L21 3m0 0h-5.25M21 3v5.25" />
            </svg>
          </button>
        ))}
      </div>
    </div>
  );
}
