import { useEffect, useMemo, useRef, useState } from "react";

/* Previous-consultations rail — the chat-history analog for the consultation
   workspace. Shared by the create page and the document editor.
   Collapse/expand is owned by the parent (width-animated aside).
   Double-click a name to rename it inline; hover shows a delete button. */

function groupLabel(d) {
  if (!d) return "Plus ancien";
  const startOfDay = (x) =>
    new Date(x.getFullYear(), x.getMonth(), x.getDate()).getTime();
  const days = Math.floor(
    (startOfDay(new Date()) - startOfDay(new Date(d))) / 86400000,
  );
  if (days <= 0) return "Aujourd’hui";
  if (days === 1) return "Hier";
  if (days < 7) return "7 derniers jours";
  if (days < 30) return "30 derniers jours";
  return "Plus ancien";
}

function RailItem({ c, active, onSelect, onRename, onDelete }) {
  const [editing, setEditing] = useState(false);
  const [name, setName] = useState(c.clientName || "");
  const [confirming, setConfirming] = useState(false);
  const inputRef = useRef();

  useEffect(() => {
    if (editing) {
      inputRef.current?.focus();
      inputRef.current?.select();
    }
  }, [editing]);

  useEffect(() => {
    if (!confirming) return;
    const timer = setTimeout(() => setConfirming(false), 2500);
    return () => clearTimeout(timer);
  }, [confirming]);

  const commit = () => {
    setEditing(false);
    const next = name.trim();
    if (next && next !== c.clientName) onRename?.(c.id, next);
    else setName(c.clientName || "");
  };

  return (
    <div
      role="button"
      tabIndex={0}
      onClick={() => !editing && onSelect(c.id)}
      onKeyDown={(e) => {
        if (e.key === "Enter" && !editing) onSelect(c.id);
      }}
      className={`group relative w-full text-left rounded-xl px-3 py-2.5 cursor-pointer transition-colors ${active ? "bg-white shadow-sm" : "hover:bg-light"}`}
    >
      <span className="flex items-center gap-2 min-w-0 pr-6">
        {editing ? (
          <input
            ref={inputRef}
            value={name}
            onChange={(e) => setName(e.target.value)}
            onBlur={commit}
            onClick={(e) => e.stopPropagation()}
            onKeyDown={(e) => {
              e.stopPropagation();
              if (e.key === "Enter") commit();
              if (e.key === "Escape") {
                setName(c.clientName || "");
                setEditing(false);
              }
            }}
            className="flex-1 min-w-0 bg-white border border-brand rounded-md px-1.5 py-0.5 text-[13px] font-semibold text-dark focus:outline-none focus:ring-2 focus:ring-brand/50"
          />
        ) : (
          <span
            onDoubleClick={(e) => {
              e.stopPropagation();
              if (onRename) {
                setName(c.clientName || "");
                setEditing(true);
              }
            }}
            title={onRename ? "Double-clic pour renommer" : undefined}
            className="text-[13px] font-semibold truncate text-dark"
          >
            {c.clientName}
          </span>
        )}
        <span
          className={`shrink-0 text-[9px] font-bold rounded-full px-1.5 py-0.5 ${active ? "bg-brand text-dark" : "text-dark"}`}
        >
          {c.reference}
        </span>
      </span>
      <span
        className={`block text-[11.5px] truncate mt-0.5 pr-6 ${active ? "text-gray-500" : "text-muted"}`}
      >
        {c.fiscalQuestion}
      </span>

      {/* hover delete — first click arms, second confirms */}
      {onDelete && !editing && (
        <button
          onClick={(e) => {
            e.stopPropagation();
            if (confirming) onDelete(c.id);
            else setConfirming(true);
          }}
          className={`absolute right-2 top-1/2 -translate-y-1/2 w-7 h-7 rounded-lg flex items-center justify-center transition-all ${
            confirming
              ? "opacity-100 bg-red-500 text-white"
              : "opacity-0 group-hover:opacity-100 text-muted hover:text-red-500 hover:bg-red-50"
          }`}
          aria-label={confirming ? "Confirmer la suppression" : "Supprimer la consultation"}
          title={confirming ? "Cliquez pour confirmer" : "Supprimer"}
        >
          {confirming ? (
            <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2.5}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M4.5 12.75l6 6 9-13.5" />
            </svg>
          ) : (
            <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M14.74 9l-.346 9m-4.788 0L9.26 9m9.968-3.21c.342.052.682.107 1.022.166m-1.022-.165L18.16 19.673a2.25 2.25 0 01-2.244 2.077H8.084a2.25 2.25 0 01-2.244-2.077L4.772 5.79m14.456 0a48.108 48.108 0 00-3.478-.397m-12 .562c.34-.059.68-.114 1.022-.165m0 0a48.11 48.11 0 013.478-.397m7.5 0v-.916c0-1.18-.91-2.164-2.09-2.201a51.964 51.964 0 00-3.32 0c-1.18.037-2.09 1.022-2.09 2.201v.916m7.5 0a48.667 48.667 0 00-7.5 0" />
            </svg>
          )}
        </button>
      )}
    </div>
  );
}

export default function ConsultationsRail({
  items,
  activeId,
  onSelect,
  onNew,
  onRename,
  onDelete,
  newActive = false,
  onClose,
}) {
  const [filter, setFilter] = useState("");

  const groups = useMemo(() => {
    const q = filter.trim().toLowerCase();
    const list = (items || []).filter(
      (c) =>
        !q ||
        c.clientName?.toLowerCase().includes(q) ||
        c.reference?.toLowerCase().includes(q) ||
        c.fiscalQuestion?.toLowerCase().includes(q),
    );
    const order = [];
    const map = {};
    list.forEach((c) => {
      const g = groupLabel(c.createdAt);
      if (!map[g]) {
        map[g] = [];
        order.push(g);
      }
      map[g].push(c);
    });
    return order.map((g) => ({ label: g, items: map[g] }));
  }, [items, filter]);

  return (
    <div className="h-full flex flex-col bg-white">
      <div className="p-3 pb-2 shrink-0 flex items-center gap-2">
        <button
          onClick={onNew}
          className={` w-full flex items-center gap-2.5 rounded-xl px-3.5 py-2.5 text-[13px] font-semibold text-dark bg-gradient-to-r from-brand/10 to-violet-400/10 border border-brand/30 hover:border-brand/60 hover:from-brand/20 hover:to-violet-400/15 active:scale-[0.98] transition-all`}
        >
          <span className="w-4 h-4 rounded-md bg-brand text-dark flex items-center justify-center">
            <svg
              className="w-3 h-3"
              fill="none"
              viewBox="0 0 24 24"
              stroke="currentColor"
              strokeWidth={2.5}
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                d="M12 4.5v15m7.5-7.5h-15"
              />
            </svg>
          </span>
          Nouvelle consultation
        </button>
        {onClose && (
          <button
            onClick={onClose}
            className="md:hidden shrink-0 w-10 h-10 rounded-xl border border-border text-muted hover:text-dark flex items-center justify-center transition-colors"
            aria-label="Fermer la liste"
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

      {/* quick filter */}
      <div className="px-3 pb-3 shrink-0">
        <div className="flex items-center gap-2 bg-light/70 border border-transparent focus-within:border-border focus-within:bg-white rounded-xl px-2.5 py-1.5 transition-all">
          <svg
            className="w-3.5 h-3.5 text-muted shrink-0"
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
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            placeholder="Filtrer…"
            className="flex-1 min-w-0 bg-transparent text-[12.5px] text-dark placeholder-muted focus:outline-none"
          />
        </div>
      </div>

      <div className="flex-1 overflow-y-auto px-3 pb-4 space-y-5">
        {!items && (
          <div className="space-y-2 pt-1">
            {[0, 1, 2, 3].map((i) => (
              <div key={i} className="rounded-xl bg-light animate-pulse h-12" />
            ))}
          </div>
        )}
        {items && items.length === 0 && (
          <div className="pt-8 text-center px-3 animate-fade-in">
            <p className="text-[13px] font-semibold text-dark">
              Aucune consultation
            </p>
            <p className="text-[12px] text-muted mt-1 leading-relaxed">
              Vos mémos générés apparaîtront ici.
            </p>
          </div>
        )}
        {groups.map((g) => (
          <div key={g.label}>
            <p className="px-3 mb-1.5 text-[10px] font-bold text-muted uppercase tracking-[0.18em]">
              {g.label}
            </p>
            <div className="space-y-0.5">
              {g.items.map((c) => (
                <RailItem
                  key={c.id}
                  c={c}
                  active={c.id === activeId}
                  onSelect={onSelect}
                  onRename={onRename}
                  onDelete={onDelete}
                />
              ))}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
