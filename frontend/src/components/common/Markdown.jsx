/* Lightweight markdown renderer for AI answers — no external deps.
   Supports headings, bold/italic/inline code, bullet & numbered lists, tables,
   and clickable source citations: "[Source 3]", "(Source 3)" or "Source 3"
   call onCitation(idx) with the 1-based source number. */

const CITE_RE = /\[sources?\s*(\d+)\]|\(sources?\s*(\d+)\)|\bsources?\s+(\d+)\b/i;
const INLINE_RE = /(\*\*[^*]+\*\*|\*[^*]+\*|`[^`]+`|\[sources?\s*\d+\]|\(sources?\s*\d+\)|\bsources?\s+\d+\b)/gi;

function Inline({ text, onCitation }) {
  const parts = String(text).split(INLINE_RE).filter(Boolean);
  return parts.map((p, i) => {
    if (p.startsWith('**') && p.endsWith('**')) return <strong key={i} className="font-bold text-dark">{p.slice(2, -2)}</strong>;
    if (p.startsWith('*') && p.endsWith('*') && p.length > 2) return <em key={i}>{p.slice(1, -1)}</em>;
    if (p.startsWith('`') && p.endsWith('`')) return <code key={i} className="text-[0.9em] bg-light border border-border rounded px-1 py-0.5">{p.slice(1, -1)}</code>;
    const m = p.match(CITE_RE);
    if (m && onCitation) {
      const n = parseInt(m[1] || m[2] || m[3], 10);
      return (
        <button key={i} type="button" onClick={() => onCitation(n)}
          className="inline-flex items-center gap-1 align-baseline text-[0.82em] font-bold text-dark bg-brand/40 hover:bg-brand rounded-md px-1.5 py-px mx-0.5 transition-colors cursor-pointer"
          title={`Voir la source ${n}`}>
          {m[1] || m[2] ? `Source ${n}` : p}
        </button>
      );
    }
    return <span key={i}>{p}</span>;
  });
}

function Table({ rows, onCitation }) {
  if (rows.length === 0) return null;
  const cells = (line) => line.replace(/^\||\|$/g, '').split('|').map((c) => c.trim());
  const header = cells(rows[0]);
  const body = rows.slice(1).filter((r) => !/^\|?[\s:|-]+\|?$/.test(r)).map(cells);
  return (
    <div className="my-3 border border-border rounded-xl overflow-x-auto">
      <table className="w-full text-[0.92em]">
        <thead>
          <tr className="bg-dark text-white">
            {header.map((h, i) => <th key={i} className="text-left px-3 py-2 font-semibold whitespace-nowrap"><Inline text={h} onCitation={onCitation} /></th>)}
          </tr>
        </thead>
        <tbody>
          {body.map((r, i) => (
            <tr key={i} className={i % 2 ? 'bg-light/60' : 'bg-white'}>
              {r.map((c, j) => <td key={j} className="px-3 py-2 align-top text-body">{j === 0 ? <span className="font-medium text-dark"><Inline text={c} onCitation={onCitation} /></span> : <Inline text={c} onCitation={onCitation} />}</td>)}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export default function Markdown({ text, onCitation, className = '' }) {
  if (!text) return null;
  const lines = String(text).split('\n');
  const blocks = [];
  let i = 0;
  while (i < lines.length) {
    const line = lines[i];
    if (!line.trim()) { i++; continue; }
    if (line.trim().startsWith('|')) {
      const rows = [];
      while (i < lines.length && lines[i].trim().startsWith('|')) { rows.push(lines[i].trim()); i++; }
      blocks.push({ type: 'table', rows });
      continue;
    }
    if (/^[-*•]\s+/.test(line.trim()) || /^\d+[.)]\s+/.test(line.trim())) {
      const ordered = /^\d+[.)]\s+/.test(line.trim());
      const items = [];
      while (i < lines.length && (/^[-*•]\s+/.test(lines[i].trim()) || /^\d+[.)]\s+/.test(lines[i].trim()))) {
        items.push(lines[i].trim().replace(/^[-*•]\s+/, '').replace(/^\d+[.)]\s+/, ''));
        i++;
      }
      blocks.push({ type: ordered ? 'ol' : 'ul', items });
      continue;
    }
    if (/^#{1,4}\s+/.test(line.trim())) {
      const level = line.trim().match(/^(#{1,4})/)[1].length;
      blocks.push({ type: 'h', level, text: line.trim().replace(/^#{1,4}\s+/, '') });
      i++;
      continue;
    }
    if (/^---+$/.test(line.trim())) { blocks.push({ type: 'hr' }); i++; continue; }
    blocks.push({ type: 'p', text: line.trim() });
    i++;
  }

  return (
    <div className={`space-y-2.5 ${className}`}>
      {blocks.map((b, k) => {
        if (b.type === 'table') return <Table key={k} rows={b.rows} onCitation={onCitation} />;
        if (b.type === 'ul') return (
          <ul key={k} className="space-y-1.5 pl-1">
            {b.items.map((it, j) => (
              <li key={j} className="flex gap-2.5 leading-relaxed">
                <span className="mt-[0.62em] w-1.5 h-1.5 rounded-full bg-brand border border-dark/20 shrink-0" />
                <span className="flex-1"><Inline text={it} onCitation={onCitation} /></span>
              </li>
            ))}
          </ul>
        );
        if (b.type === 'ol') return (
          <ol key={k} className="space-y-1.5 pl-1">
            {b.items.map((it, j) => (
              <li key={j} className="flex gap-2.5 leading-relaxed">
                <span className="mt-px text-[0.85em] font-bold text-dark bg-light border border-border rounded-md w-5 h-5 flex items-center justify-center shrink-0">{j + 1}</span>
                <span className="flex-1"><Inline text={it} onCitation={onCitation} /></span>
              </li>
            ))}
          </ol>
        );
        if (b.type === 'h') {
          const cls = b.level <= 2 ? 'text-[1.1em] font-extrabold mt-4' : 'text-[1em] font-bold mt-3';
          return <p key={k} className={`${cls} text-dark`}><Inline text={b.text} onCitation={onCitation} /></p>;
        }
        if (b.type === 'hr') return <hr key={k} className="border-border my-3" />;
        return <p key={k} className="leading-relaxed"><Inline text={b.text} onCitation={onCitation} /></p>;
      })}
    </div>
  );
}
