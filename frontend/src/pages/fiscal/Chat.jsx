import { useEffect, useMemo, useRef, useState } from "react";
import { useAuth } from "../../context/AuthContext";
import { useLanguage } from "../../context/LanguageContext";
import fiscalService from "../../services/fiscalService";
import Markdown from "../../components/common/Markdown";
import SourceChips from "../../components/fiscal/SourceChips";
import SourcePanel, {
  normalizeSource,
} from "../../components/fiscal/SourcePanel";
import useChatHistory from "../../hooks/useChatHistory";

const EASE = "ease-[cubic-bezier(.16,1,.3,1)]";

/* ───────────────────────── composer ───────────────────────── */

function Composer({ value, onChange, onSend, busy, autoFocus, large, placeholder }) {
  const ref = useRef();
  useEffect(() => {
    if (autoFocus) ref.current?.focus();
  }, [autoFocus]);
  useEffect(() => {
    if (!ref.current) return;
    ref.current.style.height = "auto";
    ref.current.style.height = Math.min(ref.current.scrollHeight, 180) + "px";
  }, [value]);
  return (
    <form
      onSubmit={(e) => {
        e.preventDefault();
        onSend();
      }}
      className={`bg-white dark:bg-cream border border-border rounded-2xl shadow-lg shadow-dark/[0.05] focus-within:ring-2 focus-within:ring-brand/70 focus-within:border-brand/30 focus-within:shadow-brand/10 transition-all ${large ? "p-2" : "p-1.5"}`}
    >
      <div className="flex items-end gap-2">
        <textarea
          ref={ref}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          rows={large ? 2 : 1}
          onKeyDown={(e) => {
            if (e.key === "Enter" && !e.shiftKey) {
              e.preventDefault();
              onSend();
            }
          }}
          placeholder={placeholder}
          className="flex-1 min-w-0 bg-transparent text-[15px] text-dark placeholder-muted focus:outline-none resize-none px-3.5 py-2.5 leading-relaxed"
        />
        <button
          type="submit"
          disabled={busy || !value.trim()}
          className="w-10 h-10 bg-gradient-to-br from-dark to-[#3a3a4a] text-brand rounded-xl flex items-center justify-center hover:from-black hover:to-dark active:scale-95 disabled:opacity-30 transition-all shrink-0 mb-0.5 mr-0.5 shadow-md shadow-dark/15"
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
              d="M4.5 10.5L12 3m0 0l7.5 7.5M12 3v18"
            />
          </svg>
        </button>
      </div>
    </form>
  );
}

function AssistantAvatar({ pulsing }) {
  return (
    <span
      className={`shrink-0 w-7 h-7 rounded-lg bg-gradient-to-br from-dark to-[#3a3a4a] flex items-center justify-center mt-1 shadow-sm ${pulsing ? "animate-pulse" : ""}`}
    >
      <svg
        className="w-3.5 h-3.5 text-brand"
        fill="none"
        viewBox="0 0 24 24"
        stroke="currentColor"
        strokeWidth={2.2}
      >
        <path
          strokeLinecap="round"
          strokeLinejoin="round"
          d="M9.813 15.904L9 18.75l-.813-2.846a4.5 4.5 0 00-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 003.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 003.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 00-3.09 3.09z"
        />
      </svg>
    </span>
  );
}

/* ───────────────────── conversation history ───────────────────── */

function timeLabel(ts, t) {
  const mins = Math.floor((Date.now() - ts) / 60000);
  if (mins < 1) return t("chat.justNow");
  if (mins < 60) return t("chat.minutesAgo", { n: mins });
  const h = Math.floor(mins / 60);
  if (h < 24) return t("chat.hoursAgo", { n: h });
  return new Date(ts).toLocaleDateString(undefined, {
    day: "numeric",
    month: "short",
  });
}

function groupLabel(ts, t) {
  const startOfDay = (d) =>
    new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
  const days = Math.floor(
    (startOfDay(new Date()) - startOfDay(new Date(ts))) / 86400000,
  );
  if (days <= 0) return t("chat.today");
  if (days === 1) return t("chat.yesterday");
  if (days < 7) return t("chat.last7days");
  if (days < 30) return t("chat.last30days");
  return t("chat.older");
}

function ConversationItem({ conv, active, onSelect, onDelete, t }) {
  const [confirming, setConfirming] = useState(false);
  useEffect(() => {
    if (!confirming) return;
    const timer = setTimeout(() => setConfirming(false), 2500);
    return () => clearTimeout(timer);
  }, [confirming]);
  return (
    <div
      role="button"
      tabIndex={0}
      onClick={() => onSelect(conv.id)}
      onKeyDown={(e) => {
        if (e.key === "Enter") onSelect(conv.id);
      }}
      className={`group relative w-full text-left rounded-xl px-3 py-2.5 cursor-pointer transition-all duration-200 ${
        active
          ? "bg-brand/15 border border-brand/30"
          : "hover:bg-light/80 border border-transparent"
      }`}
    >
      <div className="flex items-center gap-2.5">
        <svg
          className={`w-4 h-4 shrink-0 transition-colors ${active ? "text-dark" : "text-muted"}`}
          fill="none"
          viewBox="0 0 24 24"
          stroke="currentColor"
          strokeWidth={1.8}
        >
          <path
            strokeLinecap="round"
            strokeLinejoin="round"
            d="M8.625 12a.375.375 0 11-.75 0 .375.375 0 01.75 0zm0 0H8.25m4.125 0a.375.375 0 11-.75 0 .375.375 0 01.75 0zm0 0H12m4.125 0a.375.375 0 11-.75 0 .375.375 0 01.75 0zm0 0h-.375M21 12c0 4.556-4.03 8.25-9 8.25a9.764 9.764 0 01-2.555-.337A5.972 5.972 0 015.41 20.97a5.969 5.969 0 01-.474-.065 4.48 4.48 0 00.978-2.025c.09-.457-.133-.901-.467-1.226C3.93 16.178 3 14.189 3 12c0-4.556 4.03-8.25 9-8.25s9 3.694 9 8.25z"
          />
        </svg>
        <div className="flex-1 min-w-0">
          <p
            className={`text-[13px] font-medium leading-snug truncate ${active ? "text-dark font-semibold" : "text-dark"}`}
          >
            {conv.title}
          </p>
          <p
            className={`text-[11px] mt-0.5 ${active ? "text-body" : "text-muted"}`}
          >
            {timeLabel(conv.updatedAt, t)}
          </p>
        </div>
      </div>
      <button
        onClick={(e) => {
          e.stopPropagation();
          if (confirming) onDelete(conv.id);
          else setConfirming(true);
        }}
        className={`absolute right-2 top-1/2 -translate-y-1/2 w-7 h-7 rounded-lg flex items-center justify-center transition-all ${
          confirming
            ? "opacity-100 bg-red-500 text-white"
            : `opacity-0 group-hover:opacity-100 ${active ? "text-body hover:text-red-500 hover:bg-red-50" : "text-muted hover:text-red-500 hover:bg-red-50"}`
        }`}
        aria-label={
          confirming ? t("chat.confirmDelete") : t("chat.deleteConversation")
        }
        title={confirming ? t("chat.clickToConfirm") : t("chat.deleteConversation")}
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
    </div>
  );
}

function HistoryPanel({
  conversations,
  activeId,
  onSelect,
  onNew,
  onDelete,
  onClose,
  t,
}) {
  const groups = useMemo(() => {
    const order = [];
    const map = {};
    conversations.forEach((c) => {
      const g = groupLabel(c.updatedAt, t);
      if (!map[g]) {
        map[g] = [];
        order.push(g);
      }
      map[g].push(c);
    });
    return order.map((g) => ({ label: g, items: map[g] }));
  }, [conversations, t]);

  return (
    <div className="h-full flex flex-col bg-white">
      {/* New chat button */}
      <div className="p-3 shrink-0">
        <div className="flex items-center justify-between mb-3">
          <span className="text-[11px] font-bold text-muted uppercase tracking-[0.14em]">
            {t("chat.newChat")}
          </span>
          {onClose && (
            <button
              onClick={onClose}
              className="md:hidden w-7 h-7 rounded-lg text-muted hover:text-dark flex items-center justify-center transition-colors"
            >
              <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
              </svg>
            </button>
          )}
        </div>
        <button
          onClick={onNew}
          className="w-full flex items-center gap-2.5 rounded-xl px-3.5 py-2.5 text-[13px] font-semibold text-dark bg-gradient-to-r from-brand/10 to-violet-400/10 border border-brand/30 hover:border-brand/60 hover:from-brand/20 hover:to-violet-400/15 active:scale-[0.98] transition-all"
        >
          <svg className="w-4 h-4 text-muted" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 4.5v15m7.5-7.5h-15" />
          </svg>
          {t("chat.newChat")}
        </button>
      </div>

      {/* Conversation list */}
      <div className="flex-1 overflow-y-auto px-3 pb-4 space-y-4">
        {groups.length === 0 && (
          <div className="pt-10 text-center px-4 animate-fade-in">
            <div className="w-10 h-10 mx-auto rounded-xl bg-light flex items-center justify-center mb-3">
              <svg className="w-5 h-5 text-muted" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M8.625 12a.375.375 0 11-.75 0 .375.375 0 01.75 0zm0 0H8.25m4.125 0a.375.375 0 11-.75 0 .375.375 0 01.75 0zm0 0H12m4.125 0a.375.375 0 11-.75 0 .375.375 0 01.75 0zm0 0h-.375M21 12c0 4.556-4.03 8.25-9 8.25a9.764 9.764 0 01-2.555-.337A5.972 5.972 0 015.41 20.97a5.969 5.969 0 01-.474-.065 4.48 4.48 0 00.978-2.025c.09-.457-.133-.901-.467-1.226C3.93 16.178 3 14.189 3 12c0-4.556 4.03-8.25 9-8.25s9 3.694 9 8.25z" />
              </svg>
            </div>
            <p className="text-[13px] font-semibold text-dark">{t("chat.noConversations")}</p>
            <p className="text-[12px] text-muted mt-1 leading-relaxed">{t("chat.noConversationsHint")}</p>
          </div>
        )}
        {groups.map((g) => (
          <div key={g.label}>
            <p className="px-3 mb-1.5 text-[10px] font-bold text-muted uppercase tracking-[0.18em]">
              {g.label}
            </p>
            <div className="space-y-0.5">
              {g.items.map((c) => (
                <ConversationItem
                  key={c.id}
                  conv={c}
                  active={c.id === activeId}
                  onSelect={onSelect}
                  onDelete={onDelete}
                  t={t}
                />
              ))}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

/* ───────────────────── references side panel ───────────────────── */

function ReferencesPanel({ sources, onViewSource, onClose, t }) {
  const list = useMemo(
    () => (sources || []).map((s, i) => normalizeSource(s, i + 1)),
    [sources],
  );

  if (!list.length) return null;

  const TYPE_COLORS = {
    Code: "border-l-gray-700",
    Convention: "border-l-yellow-400",
    LoiFinances: "border-l-purple-400",
    Doctrine: "border-l-blue-400",
    Commentaire: "border-l-orange-400",
  };

  return (
    <div className="h-full flex flex-col bg-white animate-slide-in-right">
      <div className="h-12 shrink-0 flex items-center justify-between px-4 border-b border-border/70">
        <div className="flex items-center gap-2">
          <svg className="w-4 h-4 text-brand" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 6.042A8.967 8.967 0 006 3.75c-1.052 0-2.062.18-3 .512v14.25A8.987 8.987 0 016 18c2.305 0 4.408.867 6 2.292m0-14.25a8.966 8.966 0 016-2.292c1.052 0 2.062.18 3 .512v14.25A8.987 8.987 0 0018 18a8.967 8.967 0 00-6 2.292m0-14.25v14.25" />
          </svg>
          <p className="text-[13px] font-bold text-dark">{t("chat.references")}</p>
          <span className="text-[10px] font-bold bg-gradient-to-r from-brand/25 to-violet-400/25 text-dark rounded-full px-1.5 py-0.5">
            {list.length}
          </span>
        </div>
        <button
          onClick={onClose}
          className="w-7 h-7 rounded-lg text-muted hover:text-dark hover:bg-light flex items-center justify-center transition-colors"
        >
          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
          </svg>
        </button>
      </div>
      <div className="flex-1 overflow-y-auto p-3 space-y-2">
        {list.map((src, i) => (
          <button
            key={i}
            onClick={() => onViewSource(src)}
            className={`w-full text-left rounded-xl border border-border/80 hover:border-brand/40 hover:shadow-sm px-3.5 py-3 transition-all duration-200 group border-l-[3px] ${TYPE_COLORS[src.docType] || "border-l-gray-300"}`}
          >
            <div className="flex items-start gap-2">
              <span className="shrink-0 w-5 h-5 rounded-md bg-light text-[10px] font-bold text-muted flex items-center justify-center group-hover:bg-brand/20 group-hover:text-dark transition-colors mt-0.5">
                {src.index}
              </span>
              <div className="min-w-0 flex-1">
                <p className="text-[12px] font-semibold text-dark truncate capitalize">
                  {src.docName}
                </p>
                <div className="flex items-center gap-1.5 mt-0.5 flex-wrap">
                  {src.docType && (
                    <span className="text-[9px] font-bold text-muted uppercase">{src.docType}</span>
                  )}
                  {src.articleRef && (
                    <span className="text-[9px] text-muted">{src.articleRef}</span>
                  )}
                  {src.year && (
                    <span className="text-[9px] text-muted">{src.year}</span>
                  )}
                </div>
                {src.text && (
                  <p className="text-[11px] text-body mt-1.5 line-clamp-2 leading-relaxed">
                    {src.text}
                  </p>
                )}
              </div>
              <svg className="w-3.5 h-3.5 text-border group-hover:text-muted shrink-0 mt-1 transition-colors" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M8.25 4.5l7.5 7.5-7.5 7.5" />
              </svg>
            </div>
          </button>
        ))}
      </div>
    </div>
  );
}

/* ───────────────────────── tab bar ───────────────────────── */

function TabBar({ tabs, activeTabId, onSelect, onClose, onNew, t }) {
  if (tabs.length === 0) return null;
  return (
    <div className="shrink-0 flex items-end bg-sand px-2 pt-1 border-b border-border overflow-x-auto scrollbar-hide">
      {tabs.map((tab, idx) => {
        const active = tab.id === activeTabId;
        const dotColors = ["bg-indigo-400", "bg-emerald-400", "bg-purple-400", "bg-brand", "bg-blue-400", "bg-rose-400"];
        return (
          <div
            key={tab.id}
            className={`relative flex items-center gap-1.5 text-[12px] font-medium px-3 py-2 rounded-t-lg transition-all duration-200 max-w-[200px] min-w-[120px] cursor-pointer ${
              active
                ? "bg-white text-dark border border-border border-b-white -mb-px z-10"
                : "text-muted hover:text-dark hover:bg-white/50 border border-transparent"
            }`}
            onClick={() => onSelect(tab.id)}
          >
            <span className={`w-2 h-2 rounded-full shrink-0 ${dotColors[idx % dotColors.length]} ${active ? "" : "opacity-50"}`} />
            <span className="truncate flex-1">{tab.title}</span>
            <button
              onClick={(e) => {
                e.stopPropagation();
                onClose(tab.id);
              }}
              className={`shrink-0 w-5 h-5 rounded flex items-center justify-center transition-all ${
                active
                  ? "text-muted hover:text-dark hover:bg-light"
                  : "text-transparent group-hover:text-muted hover:text-dark"
              }`}
              title={t("chat.closeTab")}
            >
              <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
              </svg>
            </button>
            {active && <span className="absolute bottom-0 left-0 right-0 h-px bg-white" />}
          </div>
        );
      })}
      <button
        onClick={onNew}
        className="shrink-0 w-7 h-7 rounded-lg text-muted hover:text-dark hover:bg-light/80 flex items-center justify-center transition-colors ml-1 mb-0.5"
        title={t("chat.newChat")}
      >
        <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M12 4.5v15m7.5-7.5h-15" />
        </svg>
      </button>
    </div>
  );
}

/* ───────────────────────── chat page ───────────────────────── */

export default function Chat() {
  const { user } = useAuth();
  const { t } = useLanguage();
  const { conversations, create, update, remove } = useChatHistory();
  const [activeId, setActiveId] = useState(null);
  const [openTabs, setOpenTabs] = useState([]);
  const [input, setInput] = useState("");
  const [busy, setBusy] = useState(false);
  const [streamStatus, setStreamStatus] = useState("");
  const [viewing, setViewing] = useState(null);
  const [viewList, setViewList] = useState([]);
  const [refsOpen, setRefsOpen] = useState(false);
  const [histOpen, setHistOpen] = useState(
    () => localStorage.getItem("taxmind.chat.history") !== "0",
  );
  const [mobileHist, setMobileHist] = useState(false);
  const endRef = useRef();

  useEffect(() => {
    localStorage.setItem("taxmind.chat.history", histOpen ? "1" : "0");
  }, [histOpen]);

  const active = conversations.find((c) => c.id === activeId);
  const messages = active?.messages || [];
  const empty = messages.length === 0;

  const allSources = useMemo(() => {
    const srcs = [];
    messages.forEach((m) => {
      if (m.role === "assistant" && m.sources?.length) {
        srcs.push(...m.sources);
      }
    });
    return srcs;
  }, [messages]);

  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: "auto" });
  }, [activeId]);
  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages.length, busy]);

  const suggestions = useMemo(() => [
    { t: t("chat.sug1.title"), q: t("chat.sug1.q") },
    { t: t("chat.sug2.title"), q: t("chat.sug2.q") },
    { t: t("chat.sug3.title"), q: t("chat.sug3.q") },
    { t: t("chat.sug4.title"), q: t("chat.sug4.q") },
  ], [t]);

  const addTab = (id, title) => {
    setOpenTabs((prev) => {
      if (prev.find((tab) => tab.id === id)) return prev;
      return [...prev, { id, title: title || "Chat" }];
    });
  };

  const closeTab = (id) => {
    setOpenTabs((prev) => prev.filter((tab) => tab.id !== id));
    if (activeId === id) {
      const remaining = openTabs.filter((tab) => tab.id !== id);
      if (remaining.length > 0) {
        setActiveId(remaining[remaining.length - 1].id);
      } else {
        setActiveId(null);
      }
    }
  };

  const send = async (text) => {
    const q = (text ?? input).trim();
    if (!q || busy) return;
    setInput("");
    const prior = active?.messages || [];
    const next = [...prior, { role: "user", content: q }];
    let id = activeId;
    if (!id) {
      id = create(next);
      setActiveId(id);
      const title = q.length > 40 ? q.slice(0, 37) + "…" : q;
      addTab(id, title);
    } else {
      update(id, next);
    }
    setBusy(true);
    setStreamStatus(t("chat.analyzing"));

    const history = prior.map((m) => m.content);
    let acc = "";
    let srcs = [];
    let gotToken = false;
    const render = (extra = {}) =>
      update(id, [
        ...next,
        { role: "assistant", content: acc, sources: srcs, ...extra },
      ]);

    try {
      await fiscalService.chatStream(
        { question: q, history },
        {
          onStatus: (s) => setStreamStatus(s?.text || ""),
          onSources: (list) => {
            srcs = list || [];
            if (srcs.length > 0) setRefsOpen(true);
          },
          onToken: (tok) => {
            gotToken = true;
            acc += tok;
            setStreamStatus("");
            render({ streaming: true });
          },
          onDone: () => {
            if (gotToken) render();
          },
          onError: () => {
            if (!gotToken)
              update(id, [
                ...next,
                {
                  role: "assistant",
                  content: t("chat.errorMsg"),
                  error: true,
                },
              ]);
          },
        },
      );
    } finally {
      setBusy(false);
      setStreamStatus("");
    }
  };

  const selectConversation = (id) => {
    const conv = conversations.find((c) => c.id === id);
    if (conv) {
      addTab(id, conv.title);
    }
    setActiveId(id);
    setViewing(null);
    setMobileHist(false);
  };

  const newConversation = () => {
    setActiveId(null);
    setViewing(null);
    setRefsOpen(false);
    setInput("");
    setMobileHist(false);
  };

  const deleteConversation = (id) => {
    remove(id);
    closeTab(id);
    if (id === activeId) {
      setActiveId(null);
      setViewing(null);
    }
  };

  const openSource = (sources) => (i) => {
    const list = (sources || []).map((s, j) => normalizeSource(s, j + 1));
    setViewList(list);
    if (list[i]) setViewing(list[i]);
  };

  return (
    <div className="h-full flex bg-sand">
      {/* ① history sidebar — Claude.ai style */}
      <aside
        className={`hidden md:block shrink-0 overflow-hidden transition-[width] duration-300 ${EASE} ${
          histOpen ? "w-[260px] border-r border-border" : "w-0"
        }`}
      >
        <div className="w-[260px] h-full">
          <HistoryPanel
            conversations={conversations}
            activeId={activeId}
            onSelect={selectConversation}
            onNew={newConversation}
            onDelete={deleteConversation}
            t={t}
          />
        </div>
      </aside>

      {/* ① history — mobile overlay */}
      {mobileHist && (
        <div className="md:hidden fixed inset-0 z-[70]">
          <div
            className="absolute inset-0 bg-dark/40 backdrop-blur-[2px] animate-fade-in"
            onClick={() => setMobileHist(false)}
          />
          <div className="absolute left-0 top-0 h-full w-[290px] shadow-2xl animate-slide-in-left">
            <HistoryPanel
              conversations={conversations}
              activeId={activeId}
              onSelect={selectConversation}
              onNew={newConversation}
              onDelete={deleteConversation}
              onClose={() => setMobileHist(false)}
              t={t}
            />
          </div>
        </div>
      )}

      {/* ② main area */}
      <section className="flex-1 min-w-0 flex flex-col bg-white">
        {/* tab bar for open chats */}
        <TabBar
          tabs={openTabs}
          activeTabId={activeId}
          onSelect={(id) => {
            setActiveId(id);
            setViewing(null);
          }}
          onClose={closeTab}
          onNew={newConversation}
          t={t}
        />

        {/* slim toolbar */}
        <div className="h-12 shrink-0 flex items-center gap-2 px-3">
          <button
            onClick={() => setHistOpen((o) => !o)}
            className="hidden md:flex w-8 h-8 rounded-lg text-muted hover:text-dark hover:bg-light items-center justify-center transition-colors"
            aria-label={histOpen ? t("chat.hideHistory") : t("chat.showHistory")}
            title={histOpen ? t("chat.hideHistory") : t("chat.showHistory")}
          >
            <svg className="w-[17px] h-[17px]" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M3.75 5.25a1.5 1.5 0 011.5-1.5h13.5a1.5 1.5 0 011.5 1.5v13.5a1.5 1.5 0 01-1.5 1.5H5.25a1.5 1.5 0 01-1.5-1.5V5.25z" />
              <path strokeLinecap="round" d="M9.75 3.75v16.5" />
            </svg>
          </button>
          <button
            onClick={() => setMobileHist(true)}
            className="md:hidden flex w-8 h-8 rounded-lg text-muted hover:text-dark hover:bg-light items-center justify-center transition-colors"
            aria-label={t("chat.conversationHistory")}
          >
            <svg className="w-[17px] h-[17px]" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M12 6v6h4.5m4.5 0a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
          </button>
          <div className="flex-1" />
          {allSources.length > 0 && (
            <button
              onClick={() => setRefsOpen((o) => !o)}
              className={`flex items-center gap-1.5 text-[12px] font-semibold px-2.5 py-1.5 rounded-lg transition-all ${
                refsOpen
                  ? "bg-brand/15 text-dark border border-brand/30"
                  : "text-muted hover:text-dark hover:bg-light border border-transparent"
              }`}
            >
              <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M12 6.042A8.967 8.967 0 006 3.75c-1.052 0-2.062.18-3 .512v14.25A8.987 8.987 0 016 18c2.305 0 4.408.867 6 2.292m0-14.25a8.966 8.966 0 016-2.292c1.052 0 2.062.18 3 .512v14.25A8.987 8.987 0 0018 18a8.967 8.967 0 00-6 2.292m0-14.25v14.25" />
              </svg>
              {t("chat.sources")} ({allSources.length})
            </button>
          )}
        </div>

        {empty ? (
          /* hero state */
          <div className="flex-1 overflow-y-auto">
            <div className="min-h-full flex flex-col items-center justify-center px-4 sm:px-6 py-10 animate-fade-up relative overflow-hidden">
              <div className="absolute top-16 left-1/4 w-44 h-44 bg-brand/8 rounded-full blur-3xl pointer-events-none" />
              <div className="absolute bottom-20 right-1/4 w-52 h-52 bg-violet-400/8 rounded-full blur-3xl pointer-events-none" />
              <div className="absolute top-1/3 right-10 w-28 h-28 bg-blue-400/6 rounded-full blur-2xl pointer-events-none" />
              <div className="w-16 h-16 rounded-2xl bg-gradient-to-br from-dark to-[#3a3a4a] flex items-center justify-center mb-6 relative shadow-xl shadow-dark/20">
                <span className="absolute -top-1.5 -right-1.5 w-4 h-4 rounded-full bg-gradient-to-r from-brand to-[#F59E0B] border-2 border-white animate-pulse" />
                <svg className="w-7 h-7 text-brand" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M9.813 15.904L9 18.75l-.813-2.846a4.5 4.5 0 00-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 003.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 003.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 00-3.09 3.09zM18.259 8.715L18 9.75l-.259-1.035a3.375 3.375 0 00-2.455-2.456L14.25 6l1.036-.259a3.375 3.375 0 002.455-2.456L18 2.25l.259 1.035a3.375 3.375 0 002.456 2.456L21.75 6l-1.035.259a3.375 3.375 0 00-2.456 2.456z" />
                </svg>
              </div>
              <h1 className="font-display text-4xl text-dark text-center">
                {user?.firstName ? `${user.firstName}, ` : ""}{t("chat.greeting")}
              </h1>
              <p className="text-muted mt-3 mb-8 text-center max-w-md">
                {t("chat.greetingDesc")}
              </p>
              <div className="w-full max-w-xl">
                <Composer
                  value={input}
                  onChange={setInput}
                  onSend={send}
                  busy={busy}
                  autoFocus
                  large
                  placeholder={t("chat.inputPlaceholder")}
                />
                <div className="grid sm:grid-cols-2 gap-2 mt-4">
                  {suggestions.map((s, i) => {
                    const colors = [
                      "hover:border-indigo-400 hover:bg-indigo-50 dark:hover:bg-indigo-500/10",
                      "hover:border-emerald-400 hover:bg-emerald-50 dark:hover:bg-emerald-500/10",
                      "hover:border-purple-400 hover:bg-purple-50 dark:hover:bg-purple-500/10",
                      "hover:border-brand hover:bg-brand/10",
                    ];
                    const iconColors = ["text-indigo-400", "text-emerald-400", "text-purple-400", "text-brand"];
                    return (
                      <button
                        key={s.t}
                        onClick={() => send(s.q)}
                        style={{ animationDelay: `${i * 60}ms` }}
                        className={`text-left bg-white border border-border rounded-xl px-4 py-3 transition-all hover:-translate-y-0.5 hover:shadow-md group animate-pop opacity-0 ${colors[i]}`}
                      >
                        <div className="flex items-center gap-2 mb-1">
                          <svg className={`w-3.5 h-3.5 ${iconColors[i]}`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                            <path strokeLinecap="round" strokeLinejoin="round" d="M9.813 15.904L9 18.75l-.813-2.846a4.5 4.5 0 00-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 003.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 003.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 00-3.09 3.09z" />
                          </svg>
                          <span className="text-[13px] font-bold text-dark">{s.t}</span>
                        </div>
                        <span className="block text-[12px] text-muted line-clamp-1 group-hover:text-body pl-5.5">{s.q}</span>
                      </button>
                    );
                  })}
                </div>
              </div>
            </div>
          </div>
        ) : (
          <>
            {/* messages */}
            <div className="flex-1 overflow-y-auto">
              <div className="max-w-3xl mx-auto px-4 sm:px-6 py-6 space-y-7">
                {messages.map((m, i) =>
                  m.role === "user" ? (
                    <div key={i} className="flex justify-end animate-pop opacity-0">
                      <div className="max-w-[80%] bg-dark text-white rounded-2xl rounded-br-md px-4 py-3 text-[14.5px] leading-relaxed whitespace-pre-wrap">
                        {m.content}
                      </div>
                    </div>
                  ) : (
                    <div key={i} className="flex gap-3.5 animate-pop opacity-0">
                      <AssistantAvatar />
                      <div className={`min-w-0 flex-1 text-[14.5px] ${m.error ? "text-red-600" : "text-dark"}`}>
                        <Markdown text={m.content} onCitation={(n) => openSource(m.sources)(n - 1)} />
                        <SourceChips sources={m.sources || []} onOpen={openSource(m.sources)} />
                      </div>
                    </div>
                  ),
                )}
                {busy && streamStatus && (
                  <div className="flex gap-3.5 animate-fade-in">
                    <AssistantAvatar pulsing />
                    <div className="flex items-center gap-2 pt-2.5">
                      <span className="text-[13px] text-muted font-medium italic">{streamStatus}</span>
                      {[0, 150, 300].map((d) => (
                        <span key={d} className="w-1.5 h-1.5 bg-muted rounded-full animate-bounce" style={{ animationDelay: `${d}ms` }} />
                      ))}
                    </div>
                  </div>
                )}
                <div ref={endRef} />
              </div>
            </div>

            {/* composer */}
            <div className="shrink-0 px-4 sm:px-6 pb-5 pt-2 bg-gradient-to-t from-sand via-sand/95 to-transparent">
              <div className="max-w-3xl mx-auto">
                <Composer value={input} onChange={setInput} onSend={send} busy={busy} placeholder={t("chat.inputPlaceholder")} />
                <p className="text-center text-[11px] text-muted mt-2.5">{t("chat.disclaimer")}</p>
              </div>
            </div>
          </>
        )}
      </section>

      {/* ③ references side panel — inside the chat, not a tab */}
      <aside
        className={`hidden lg:block shrink-0 overflow-hidden transition-[width] duration-300 ${EASE} ${
          refsOpen && allSources.length > 0 && !viewing ? "w-[300px] xl:w-[340px]" : "w-0"
        }`}
      >
        {refsOpen && allSources.length > 0 && !viewing && (
          <div className="w-[300px] xl:w-[340px] h-full border-l border-border">
            <ReferencesPanel
              sources={allSources}
              onViewSource={(src) => {
                setViewList(allSources.map((s, i) => normalizeSource(s, i + 1)));
                setViewing(src);
              }}
              onClose={() => setRefsOpen(false)}
              t={t}
            />
          </div>
        )}
      </aside>

      {/* ④ document viewer — desktop inline */}
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

      {/* ④ document — mobile/tablet overlay */}
      {viewing && (
        <div className="lg:hidden fixed inset-0 z-[80]" role="dialog" aria-modal="true">
          <div className="absolute inset-0 bg-dark/40 backdrop-blur-[2px] animate-fade-in" onClick={() => setViewing(null)} />
          <div className="absolute right-0 top-0 h-full w-full max-w-md shadow-2xl animate-slide-in-right">
            <SourcePanel
              source={viewing}
              sources={viewList}
              onNavigate={setViewing}
              onClose={() => setViewing(null)}
            />
          </div>
        </div>
      )}
    </div>
  );
}
