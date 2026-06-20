import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import fiscalService from "../../services/fiscalService";
import Markdown from "../common/Markdown";
import SourceChips from "./SourceChips";
import SourceDrawer, { normalizeSource } from "./SourceDrawer";

const QUICK = [
  "Taux de retenue à la source sur honoraires ?",
  "TVA sur services rendus à l’étranger ?",
  "Délai de dépôt de la déclaration IS ?",
];

/* Floating quick-ask assistant — a corner bubble that expands into a mini chat,
   so users can ask the fiscal corpus from anywhere without leaving the page. */
export default function ChatBubble() {
  const [open, setOpen] = useState(false);
  const [messages, setMessages] = useState([]);
  const [input, setInput] = useState("");
  const [busy, setBusy] = useState(false);
  const [viewing, setViewing] = useState(null); // normalized source
  const [viewList, setViewList] = useState([]);
  const endRef = useRef();
  const inputRef = useRef();

  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages, busy, open]);
  useEffect(() => {
    if (open) setTimeout(() => inputRef.current?.focus(), 150);
  }, [open]);

  const send = async (text) => {
    const q = (text ?? input).trim();
    if (!q || busy) return;
    setInput("");
    const next = [...messages, { role: "user", content: q }];
    setMessages(next);
    setBusy(true);
    try {
      const history = messages.map((m) => m.content);
      const { data } = await fiscalService.chat(q, history);
      setMessages([
        ...next,
        {
          role: "assistant",
          content: data.data.answer,
          sources: data.data.sources,
        },
      ]);
    } catch (err) {
      setMessages([
        ...next,
        {
          role: "assistant",
          content:
            err.response?.data?.message ||
            "Je n’ai pas pu répondre — vérifiez que le moteur est connecté.",
          error: true,
        },
      ]);
    } finally {
      setBusy(false);
    }
  };

  const openSource = (sources) => (i) => {
    const list = (sources || []).map((s, j) => normalizeSource(s, j + 1));
    setViewList(list);
    setViewing(list[i] || list[0]);
  };

  return (
    <>
      {/* collapsed bubble */}
      {!open && (
        <button
          onClick={() => setOpen(true)}
          aria-label="Quick ask AI"
          className="fixed bottom-6 right-6 z-40 w-14 h-14 rounded-2xl bg-dark text-white shadow-xl shadow-dark/25 flex items-center justify-center hover:scale-105 active:scale-95 transition-transform group"
        >
          <span className="absolute -top-1 -right-1 w-4 h-4 rounded-full bg-brand border-2 border-white" />
          <svg
            className="w-6 h-6 group-hover:rotate-12 transition-transform"
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
        </button>
      )}

      {/* expanded panel */}
      {open && (
        <div className="fixed bottom-6 right-6 z-40 w-[min(400px,calc(100vw-2.5rem))] h-[min(580px,calc(100vh-6rem))] bg-white rounded-2xl border border-border shadow-2xl shadow-dark/20 flex flex-col overflow-hidden animate-pop origin-bottom-right">
          {/* header */}
          <div className="px-4 py-3 bg-dark flex items-center justify-between gap-3 relative">
            <div className="absolute bottom-0 left-0 w-full h-[2px] bg-brand" />
            <div className="flex items-center gap-2.5 min-w-0">
              <span className="w-8 h-8 rounded-xl bg-brand flex items-center justify-center shrink-0">
                <svg
                  className="w-4 h-4 text-dark"
                  fill="none"
                  viewBox="0 0 24 24"
                  stroke="currentColor"
                  strokeWidth={2}
                >
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    d="M9.813 15.904L9 18.75l-.813-2.846a4.5 4.5 0 00-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 003.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 003.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 00-3.09 3.09z"
                  />
                </svg>
              </span>
              <div className="min-w-0">
                <p className="text-sm font-bold text-white leading-tight">
                  Assistant fiscal
                </p>
                <p className="text-[10.5px] text-white/50 truncate">
                  Réponses sourcées sur le corpus
                </p>
              </div>
            </div>
            <div className="flex items-center gap-1 shrink-0">
              <Link
                to="/app/chat"
                onClick={() => setOpen(false)}
                title="Ouvrir le chat complet"
                className="w-8 h-8 rounded-lg text-white/60 hover:text-white hover:bg-white/10 flex items-center justify-center transition-colors"
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
                    d="M3.75 3.75v4.5m0-4.5h4.5m-4.5 0L9 9M3.75 20.25v-4.5m0 4.5h4.5m-4.5 0L9 15M20.25 3.75h-4.5m4.5 0v4.5m0-4.5L15 9m5.25 11.25h-4.5m4.5 0v-4.5m0 4.5L15 15"
                  />
                </svg>
              </Link>
              <button
                onClick={() => setOpen(false)}
                title="Réduire"
                className="w-8 h-8 rounded-lg text-white/60 hover:text-white hover:bg-white/10 flex items-center justify-center transition-colors"
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
                    d="M19.5 12h-15"
                  />
                </svg>
              </button>
            </div>
          </div>

          {/* thread */}
          <div className="flex-1 px-4 py-4 space-y-4 bg-sand/50">
            {messages.length === 0 && (
              <div className="pt-4">
                <p className="text-sm font-bold text-dark mb-1">
                  Une question rapide ?
                </p>
                <p className="text-xs text-muted mb-4">
                  Posez votre question sans quitter la page — chaque réponse
                  cite ses sources.
                </p>
                <div className="space-y-1.5">
                  {QUICK.map((s) => (
                    <button
                      key={s}
                      onClick={() => send(s)}
                      className="w-full text-left text-[12.5px] text-body bg-white hover:bg-brand/10 border border-border hover:border-brand/60 rounded-xl px-3 py-2 transition-colors"
                    >
                      {s}
                    </button>
                  ))}
                </div>
              </div>
            )}
            {messages.map((m, i) =>
              m.role === "user" ? (
                <div key={i} className="flex justify-end">
                  <div className="max-w-[85%] bg-dark text-white rounded-2xl rounded-br-md px-3.5 py-2.5 text-[13px] leading-relaxed whitespace-pre-wrap">
                    {m.content}
                  </div>
                </div>
              ) : (
                <div key={i} className="flex gap-2.5">
                  <span className="shrink-0 w-6 h-6 rounded-lg bg-brand flex items-center justify-center mt-0.5">
                    <svg
                      className="w-3 h-3 text-dark"
                      fill="none"
                      viewBox="0 0 24 24"
                      stroke="currentColor"
                      strokeWidth={2.4}
                    >
                      <path
                        strokeLinecap="round"
                        strokeLinejoin="round"
                        d="M9.813 15.904L9 18.75l-.813-2.846a4.5 4.5 0 00-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 003.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 003.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 00-3.09 3.09z"
                      />
                    </svg>
                  </span>
                  <div
                    className={`min-w-0 flex-1 text-[13px] ${m.error ? "text-red-600" : "text-dark"}`}
                  >
                    <Markdown
                      text={m.content}
                      onCitation={(n) => openSource(m.sources)(n - 1)}
                    />
                    <SourceChips
                      sources={m.sources || []}
                      onOpen={openSource(m.sources)}
                      compact
                    />
                  </div>
                </div>
              ),
            )}
            {busy && (
              <div className="flex gap-2.5">
                <span className="shrink-0 w-6 h-6 rounded-lg bg-brand flex items-center justify-center animate-pulse" />
                <div className="flex items-center gap-1.5 pt-1.5">
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

          {/* composer */}
          <form
            onSubmit={(e) => {
              e.preventDefault();
              send();
            }}
            className="border-t border-border bg-white p-3 flex items-center gap-2"
          >
            <input
              ref={inputRef}
              value={input}
              onChange={(e) => setInput(e.target.value)}
              placeholder="Question fiscale rapide…"
              className="flex-1 bg-light border border-border rounded-xl px-3.5 py-2.5 text-[13px] text-dark placeholder-muted focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent"
            />
            <button
              type="submit"
              disabled={busy || !input.trim()}
              className="w-10 h-10 bg-brand rounded-xl flex items-center justify-center hover:shadow-md hover:shadow-brand/40 disabled:opacity-40 transition-all shrink-0"
            >
              <svg
                className="w-4 h-4 text-dark"
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                strokeWidth={2.2}
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  d="M4.5 19.5l15-7.5-15-7.5v6l10.5 1.5L4.5 13.5v6z"
                />
              </svg>
            </button>
          </form>
        </div>
      )}

      {viewing && (
        <SourceDrawer
          source={viewing}
          sources={viewList}
          onNavigate={setViewing}
          onClose={() => setViewing(null)}
        />
      )}
    </>
  );
}
