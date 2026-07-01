import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import fiscalService from "../../services/fiscalService";
import { useLanguage } from "../../context/LanguageContext";
import Markdown from "../common/Markdown";
import SourceChips from "./SourceChips";
import SourceDrawer, { normalizeSource } from "./SourceDrawer";

const QUICK = [
  "Taux de retenue à la source sur honoraires ?",
  "TVA sur services rendus à l'étranger ?",
  "Délai de dépôt de la déclaration IS ?",
];

export default function ChatBubble() {
  const { t } = useLanguage();
  const [open, setOpen] = useState(false);
  const [messages, setMessages] = useState([]);
  const [input, setInput] = useState("");
  const [busy, setBusy] = useState(false);
  const [viewing, setViewing] = useState(null);
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
            "Je n'ai pas pu répondre — vérifiez que le moteur est connecté.",
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
      {!open && (
        <button
          onClick={() => setOpen(true)}
          aria-label="Quick ask AI"
          className="fixed bottom-5 right-5 z-40 w-10 h-10 rounded-full bg-brand text-dark shadow-lg shadow-brand/30 ring-4 ring-brand/15 flex items-center justify-center hover:scale-110 hover:shadow-xl hover:shadow-brand/40 active:scale-95 transition-all duration-200"
        >
          <svg
            className="w-[18px] h-[18px]"
            fill="none"
            viewBox="0 0 24 24"
            stroke="currentColor"
            strokeWidth={1.8}
          >
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              d="M20.25 8.511c.884.284 1.5 1.128 1.5 2.097v4.286c0 1.136-.847 2.1-1.98 2.193-.34.027-.68.052-1.02.072v3.091l-3-3c-1.354 0-2.694-.055-4.02-.163a2.115 2.115 0 01-.825-.242m9.345-8.334a2.126 2.126 0 00-.476-.095 48.64 48.64 0 00-8.048 0c-1.131.094-1.976 1.057-1.976 2.192v4.286c0 .837.46 1.58 1.155 1.951m9.345-8.334V6.637c0-1.621-1.152-3.026-2.76-3.235A48.455 48.455 0 0011.25 3c-2.115 0-4.198.137-6.24.402-1.608.209-2.76 1.614-2.76 3.235v6.226c0 1.621 1.152 3.026 2.76 3.235.577.075 1.157.14 1.74.194V21l4.155-4.155"
            />
          </svg>
        </button>
      )}

      {open && (
        <div className="fixed bottom-5 right-5 z-40 w-[min(380px,calc(100vw-2.5rem))] h-[min(520px,calc(100vh-5rem))] bg-white rounded-2xl border border-border/60 shadow-2xl shadow-dark/15 flex flex-col overflow-hidden animate-pop origin-bottom-right">
          <div className="px-3.5 py-2.5 bg-dark flex items-center justify-between gap-3 relative">
            <div className="absolute bottom-0 left-0 w-full h-[2px] bg-brand" />
            <div className="flex items-center gap-2 min-w-0">
              <span className="w-7 h-7 rounded-full bg-brand flex items-center justify-center shrink-0">
                <svg
                  className="w-3.5 h-3.5 text-dark"
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
                <p className="text-[13px] font-bold text-white leading-tight">
                  {t("chat.title")}
                </p>
                <p className="text-[10px] text-white/50 truncate">
                  {t("chat.subtitle")}
                </p>
              </div>
            </div>
            <div className="flex items-center gap-0.5 shrink-0">
              <Link
                to="/app/chat"
                onClick={() => setOpen(false)}
                title="Ouvrir en plein écran"
                className="w-7 h-7 rounded-lg text-white/60 hover:text-white hover:bg-white/10 flex items-center justify-center transition-colors"
              >
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
                    d="M3.75 3.75v4.5m0-4.5h4.5m-4.5 0L9 9M3.75 20.25v-4.5m0 4.5h4.5m-4.5 0L9 15M20.25 3.75h-4.5m4.5 0v4.5m0-4.5L15 9m5.25 11.25h-4.5m4.5 0v-4.5m0 4.5L15 15"
                  />
                </svg>
              </Link>
              <button
                onClick={() => setOpen(false)}
                title="Réduire"
                className="w-7 h-7 rounded-lg text-white/60 hover:text-white hover:bg-white/10 flex items-center justify-center transition-colors"
              >
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
                    d="M19.5 12h-15"
                  />
                </svg>
              </button>
            </div>
          </div>

          <div className="flex-1 overflow-y-auto px-3.5 py-3 space-y-3 bg-sand/50">
            {messages.length === 0 && (
              <div className="pt-2">
                <p className="text-[13px] font-bold text-dark mb-0.5">
                  {t("chat.intro")}
                </p>
                <p className="text-[11px] text-muted mb-3">
                  {t("chat.introHint")}
                </p>
                <div className="space-y-1.5">
                  {QUICK.map((s) => (
                    <button
                      key={s}
                      onClick={() => send(s)}
                      className="w-full text-left text-[12px] text-body bg-white hover:bg-brand/10 border border-border/60 hover:border-brand/50 rounded-xl px-3 py-2 transition-colors"
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
                  <div className="max-w-[85%] bg-dark text-white rounded-2xl rounded-br-md px-3 py-2 text-[12px] leading-relaxed whitespace-pre-wrap">
                    {m.content}
                  </div>
                </div>
              ) : (
                <div key={i} className="flex gap-2">
                  <span className="shrink-0 w-6 h-6 rounded-full bg-brand flex items-center justify-center mt-0.5">
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
                    className={`min-w-0 flex-1 text-[12px] ${m.error ? "text-red-600" : "text-dark"}`}
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
              <div className="flex gap-2">
                <span className="shrink-0 w-6 h-6 rounded-full bg-brand flex items-center justify-center animate-pulse" />
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

          <form
            onSubmit={(e) => {
              e.preventDefault();
              send();
            }}
            className="border-t border-border/40 bg-white p-2.5 flex items-center gap-2"
          >
            <input
              ref={inputRef}
              value={input}
              onChange={(e) => setInput(e.target.value)}
              placeholder={t("chat.placeholder")}
              className="flex-1 bg-light/80 border border-border/90 rounded-xl px-3 py-2 text-[12px] text-dark placeholder-muted focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent"
            />
            <button
              type="submit"
              disabled={busy || !input.trim()}
              className="w-8 h-8 bg-brand rounded-full flex items-center justify-center hover:shadow-md hover:shadow-brand/40 disabled:opacity-40 transition-all shrink-0"
            >
              <svg
                className="w-3.5 h-3.5 text-dark"
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
