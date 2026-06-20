import { useEffect, useMemo, useRef, useState } from "react";
import { useAuth } from "../../context/AuthContext";
import fiscalService from "../../services/fiscalService";
import Markdown from "../../components/common/Markdown";
import SourceChips from "../../components/fiscal/SourceChips";
import SourcePanel, {
  normalizeSource,
} from "../../components/fiscal/SourcePanel";
import useChatHistory from "../../hooks/useChatHistory";

const SUGGESTIONS = [
  {
    t: "Retenue à la source",
    q: "Quel est le taux de retenue à la source sur les honoraires versés à un non-résident ?",
  },
  {
    t: "TVA à l’export",
    q: "La TVA est-elle due sur les prestations de services rendues à une société étrangère ?",
  },
  {
    t: "Prix de transfert",
    q: "Comment fonctionne le régime des prix de transfert en Tunisie ?",
  },
  {
    t: "Convention fiscale",
    q: "Comment la convention Tunisie-France traite-t-elle les dividendes ?",
  },
];

const EASE = "ease-[cubic-bezier(.16,1,.3,1)]";

/* ───────────────────────── composer ───────────────────────── */

function Composer({ value, onChange, onSend, busy, autoFocus, large }) {
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
      className={`bg-white border border-border rounded-2xl shadow-lg shadow-dark/[0.05] focus-within:ring-2 focus-within:ring-brand focus-within:border-transparent transition-all ${large ? "p-2" : "p-1.5"}`}
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
          placeholder="Posez votre question fiscale…"
          className="flex-1 min-w-0 bg-transparent text-[15px] text-dark placeholder-muted focus:outline-none resize-none px-3.5 py-2.5 leading-relaxed"
        />
        <button
          type="submit"
          disabled={busy || !value.trim()}
          className="w-10 h-10 bg-dark text-brand rounded-xl flex items-center justify-center hover:bg-black active:scale-95 disabled:opacity-30 transition-all shrink-0 mb-0.5 mr-0.5"
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
      className={`shrink-0 w-7 h-7 rounded-lg bg-dark flex items-center justify-center mt-1 ${pulsing ? "animate-pulse" : ""}`}
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

function timeLabel(ts) {
  const mins = Math.floor((Date.now() - ts) / 60000);
  if (mins < 1) return "à l’instant";
  if (mins < 60) return `il y a ${mins} min`;
  const h = Math.floor(mins / 60);
  if (h < 24) return `il y a ${h} h`;
  return new Date(ts).toLocaleDateString("fr-FR", {
    day: "numeric",
    month: "short",
  });
}

function groupLabel(ts) {
  const startOfDay = (d) =>
    new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
  const days = Math.floor(
    (startOfDay(new Date()) - startOfDay(new Date(ts))) / 86400000,
  );
  if (days <= 0) return "Aujourd’hui";
  if (days === 1) return "Hier";
  if (days < 7) return "7 derniers jours";
  if (days < 30) return "30 derniers jours";
  return "Plus ancien";
}

function ConversationItem({ conv, active, onSelect, onDelete }) {
  const [confirming, setConfirming] = useState(false);
  useEffect(() => {
    if (!confirming) return;
    const t = setTimeout(() => setConfirming(false), 2500);
    return () => clearTimeout(t);
  }, [confirming]);
  return (
    <div
      role="button"
      tabIndex={0}
      onClick={() => onSelect(conv.id)}
      onKeyDown={(e) => {
        if (e.key === "Enter") onSelect(conv.id);
      }}
      className={`group relative w-full text-left rounded-xl px-3 py-2.5 cursor-pointer transition-colors ${
        active ? "bg-dark text-white shadow-sm" : "hover:bg-light"
      }`}
    >
      <p
        className={`text-[13px] font-semibold leading-snug truncate pr-7 ${active ? "text-white" : "text-dark"}`}
      >
        {conv.title}
      </p>
      <p
        className={`text-[11px] mt-0.5 ${active ? "text-white/50" : "text-muted"}`}
      >
        {timeLabel(conv.updatedAt)} · {Math.ceil(conv.messages.length / 2)}{" "}
        échange{conv.messages.length > 2 ? "s" : ""}
      </p>
      <button
        onClick={(e) => {
          e.stopPropagation();
          if (confirming) onDelete(conv.id);
          else setConfirming(true);
        }}
        className={`absolute right-2 top-1/2 -translate-y-1/2 w-7 h-7 rounded-lg flex items-center justify-center transition-all ${
          confirming
            ? "opacity-100 bg-red-500 text-white"
            : `opacity-0 group-hover:opacity-100 ${active ? "text-white/60 hover:text-white hover:bg-white/10" : "text-muted hover:text-red-500 hover:bg-red-50"}`
        }`}
        aria-label={
          confirming ? "Confirmer la suppression" : "Supprimer la conversation"
        }
        title={confirming ? "Cliquez pour confirmer" : "Supprimer"}
      >
        {confirming ? (
          <svg
            className="w-3.5 h-3.5"
            fill="none"
            viewBox="0 0 24 24"
            stroke="currentColor"
            strokeWidth={2.5}
          >
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              d="M4.5 12.75l6 6 9-13.5"
            />
          </svg>
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
              d="M14.74 9l-.346 9m-4.788 0L9.26 9m9.968-3.21c.342.052.682.107 1.022.166m-1.022-.165L18.16 19.673a2.25 2.25 0 01-2.244 2.077H8.084a2.25 2.25 0 01-2.244-2.077L4.772 5.79m14.456 0a48.108 48.108 0 00-3.478-.397m-12 .562c.34-.059.68-.114 1.022-.165m0 0a48.11 48.11 0 013.478-.397m7.5 0v-.916c0-1.18-.91-2.164-2.09-2.201a51.964 51.964 0 00-3.32 0c-1.18.037-2.09 1.022-2.09 2.201v.916m7.5 0a48.667 48.667 0 00-7.5 0"
            />
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
}) {
  const groups = useMemo(() => {
    const order = [];
    const map = {};
    conversations.forEach((c) => {
      const g = groupLabel(c.updatedAt);
      if (!map[g]) {
        map[g] = [];
        order.push(g);
      }
      map[g].push(c);
    });
    return order.map((g) => ({ label: g, items: map[g] }));
  }, [conversations]);

  return (
    <div className="h-full flex flex-col bg-white">
      <div className="p-3 shrink-0 flex items-center gap-2">
        <button
          onClick={onNew}
          className="flex-1 flex items-center justify-center gap-2 bg-dark text-white rounded-xl px-3.5 py-2.5 text-[13px] font-bold hover:bg-black active:scale-[0.98] transition-all shadow-sm"
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
          Nouvelle conversation
        </button>
        {onClose && (
          <button
            onClick={onClose}
            className="md:hidden shrink-0 w-10 h-10 rounded-xl border border-border text-muted hover:text-dark flex items-center justify-center transition-colors"
            aria-label="Fermer l’historique"
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

      <div className="flex-1 overflow-y-auto px-3 pb-4 space-y-5">
        {groups.length === 0 && (
          <div className="pt-10 text-center px-4 animate-fade-in">
            <div className="w-10 h-10 mx-auto rounded-xl bg-light flex items-center justify-center mb-3">
              <svg
                className="w-5 h-5 text-muted"
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                strokeWidth={1.5}
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  d="M8.625 12a.375.375 0 11-.75 0 .375.375 0 01.75 0zm0 0H8.25m4.125 0a.375.375 0 11-.75 0 .375.375 0 01.75 0zm0 0H12m4.125 0a.375.375 0 11-.75 0 .375.375 0 01.75 0zm0 0h-.375M21 12c0 4.556-4.03 8.25-9 8.25a9.764 9.764 0 01-2.555-.337A5.972 5.972 0 015.41 20.97a5.969 5.969 0 01-.474-.065 4.48 4.48 0 00.978-2.025c.09-.457-.133-.901-.467-1.226C3.93 16.178 3 14.189 3 12c0-4.556 4.03-8.25 9-8.25s9 3.694 9 8.25z"
                />
              </svg>
            </div>
            <p className="text-[13px] font-semibold text-dark">
              Aucune conversation
            </p>
            <p className="text-[12px] text-muted mt-1 leading-relaxed">
              Vos échanges avec l’assistant apparaîtront ici.
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
                <ConversationItem
                  key={c.id}
                  conv={c}
                  active={c.id === activeId}
                  onSelect={onSelect}
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

/* ───────────────────────── chat page ───────────────────────── */

/* Claude-style assistant workspace, three vertical panes:
   history | thread | document. The source panel slides in as a third column
   on desktop (overlay on mobile) when a citation is clicked. */
export default function Chat() {
  const { user } = useAuth();
  const { conversations, create, update, remove } = useChatHistory();
  const [activeId, setActiveId] = useState(null);
  const [input, setInput] = useState("");
  const [busy, setBusy] = useState(false);
  const [viewing, setViewing] = useState(null);
  const [viewList, setViewList] = useState([]);
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

  // jump on conversation switch, glide on new messages
  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: "auto" });
  }, [activeId]);
  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages.length, busy]);

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
    } else {
      update(id, next);
    }
    setBusy(true);
    try {
      const history = prior.map((m) => m.content);
      const { data } = await fiscalService.chat(q, history);
      update(id, [
        ...next,
        {
          role: "assistant",
          content: data.data.answer,
          sources: data.data.sources,
        },
      ]);
    } catch (err) {
      update(id, [
        ...next,
        {
          role: "assistant",
          content:
            err.response?.data?.message ||
            "Désolé — je n’ai pas pu répondre. Vérifiez que Neo4j et le LLM sont connectés.",
          error: true,
        },
      ]);
    } finally {
      setBusy(false);
    }
  };

  const selectConversation = (id) => {
    setActiveId(id);
    setViewing(null);
    setMobileHist(false);
  };

  const newConversation = () => {
    setActiveId(null);
    setViewing(null);
    setInput("");
    setMobileHist(false);
  };

  const deleteConversation = (id) => {
    remove(id);
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
      {/* ① history — desktop column, collapsible */}
      <aside
        className={`hidden md:block shrink-0 overflow-hidden transition-[width] duration-300 ${EASE} ${
          histOpen ? "w-[270px]  border-r border-border" : "w-0"
        }`}
      >
        <div className="w-[270px] h-full">
          <HistoryPanel
            conversations={conversations}
            activeId={activeId}
            onSelect={selectConversation}
            onNew={newConversation}
            onDelete={deleteConversation}
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
            />
          </div>
        </div>
      )}

      {/* ② thread */}
      <section className="flex-1 min-w-0 flex flex-col">
        {/* slim toolbar */}
        <div className="h-16 shrink-0 flex items-center gap-2 px-3">
          <button
            onClick={() => setHistOpen((o) => !o)}
            className="hidden md:flex w-8 h-8 rounded-lg text-muted hover:text-dark hover:bg-light items-center justify-center transition-colors"
            aria-label={
              histOpen ? "Masquer l’historique" : "Afficher l’historique"
            }
            title={histOpen ? "Masquer l’historique" : "Afficher l’historique"}
          >
            <svg
              className="w-[17px] h-[17px]"
              fill="none"
              viewBox="0 0 24 24"
              stroke="currentColor"
              strokeWidth={1.8}
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                d="M3.75 5.25a1.5 1.5 0 011.5-1.5h13.5a1.5 1.5 0 011.5 1.5v13.5a1.5 1.5 0 01-1.5 1.5H5.25a1.5 1.5 0 01-1.5-1.5V5.25z"
              />
              <path strokeLinecap="round" d="M9.75 3.75v16.5" />
            </svg>
          </button>
          <button
            onClick={() => setMobileHist(true)}
            className="md:hidden flex w-8 h-8 rounded-lg text-muted hover:text-dark hover:bg-light items-center justify-center transition-colors"
            aria-label="Historique des conversations"
          >
            <svg
              className="w-[17px] h-[17px]"
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
          </button>
        </div>

        {empty ? (
          /* hero state — greeting + centered composer, Claude-style */
          <div className="flex-1 overflow-y-auto">
            <div className="min-h-full flex flex-col items-center justify-center px-4 sm:px-6 py-10 animate-fade-up">
              <div className="w-14 h-16 rounded-2xl bg-dark flex items-center justify-center mb-6 relative">
                <span className="absolute -top-1 -right-1 w-3.5 h-3.5 rounded-full bg-brand border-2 border-sand" />
                <svg
                  className="w-7 h-7 text-brand"
                  fill="none"
                  viewBox="0 0 24 24"
                  stroke="currentColor"
                  strokeWidth={1.8}
                >
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    d="M9.813 15.904L9 18.75l-.813-2.846a4.5 4.5 0 00-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 003.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 003.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 00-3.09 3.09zM18.259 8.715L18 9.75l-.259-1.035a3.375 3.375 0 00-2.455-2.456L14.25 6l1.036-.259a3.375 3.375 0 002.455-2.456L18 2.25l.259 1.035a3.375 3.375 0 002.456 2.456L21.75 6l-1.035.259a3.375 3.375 0 00-2.456 2.456z"
                  />
                </svg>
              </div>
              <h1 className="font-display text-4xl text-dark text-center">
                {user?.firstName ? `${user.firstName}, ` : ""}une question
                fiscale&nbsp;?
              </h1>
              <p className="text-muted mt-3 mb-8 text-center max-w-md">
                Chaque réponse est fondée sur le corpus juridique tunisien — et
                cite ses sources, consultables en un clic.
              </p>
              <div className="w-full max-w-xl">
                <Composer
                  value={input}
                  onChange={setInput}
                  onSend={send}
                  busy={busy}
                  autoFocus
                  large
                />
                <div className="grid sm:grid-cols-2 gap-2 mt-4">
                  {SUGGESTIONS.map((s, i) => (
                    <button
                      key={s.t}
                      onClick={() => send(s.q)}
                      style={{ animationDelay: `${i * 60}ms` }}
                      className="text-left bg-white hover:bg-brand/10 border border-border hover:border-brand/60 rounded-xl px-4 py-3 transition-all hover:-translate-y-0.5 group animate-pop opacity-0"
                    >
                      <span className="block text-[13px] font-bold text-dark">
                        {s.t}
                      </span>
                      <span className="block text-[12px] text-muted mt-0.5 line-clamp-1 group-hover:text-body">
                        {s.q}
                      </span>
                    </button>
                  ))}
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
                    <div
                      key={i}
                      className="flex justify-end animate-pop opacity-0"
                    >
                      <div className="max-w-[80%] bg-dark text-white rounded-2xl rounded-br-md px-4 py-3 text-[14.5px] leading-relaxed whitespace-pre-wrap">
                        {m.content}
                      </div>
                    </div>
                  ) : (
                    <div key={i} className="flex gap-3.5 animate-pop opacity-0">
                      <AssistantAvatar />
                      <div
                        className={`min-w-0 flex-1 text-[14.5px] ${m.error ? "text-red-600" : "text-dark"}`}
                      >
                        <Markdown
                          text={m.content}
                          onCitation={(n) => openSource(m.sources)(n - 1)}
                        />
                        <SourceChips
                          sources={m.sources || []}
                          onOpen={openSource(m.sources)}
                        />
                      </div>
                    </div>
                  ),
                )}
                {busy && (
                  <div className="flex gap-3.5 animate-fade-in">
                    <AssistantAvatar pulsing />
                    <div className="flex items-center gap-2 pt-2.5">
                      <span className="text-[13px] text-muted font-medium">
                        Analyse du corpus
                      </span>
                      {[0, 150, 300].map((d) => (
                        <span
                          key={d}
                          className="w-1.5 h-1.5 bg-muted rounded-full animate-bounce"
                          style={{ animationDelay: `${d}ms` }}
                        />
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
                <Composer
                  value={input}
                  onChange={setInput}
                  onSend={send}
                  busy={busy}
                />
                <p className="text-center text-[11px] text-muted mt-2.5">
                  Réponses générées par IA à partir du corpus indexé — vérifiez
                  les sources citées.
                </p>
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
            />
          </div>
        </div>
      )}
    </div>
  );
}
