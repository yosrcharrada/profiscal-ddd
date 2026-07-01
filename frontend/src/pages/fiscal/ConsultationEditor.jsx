import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useAuth } from "../../context/AuthContext";
import { useLanguage } from "../../context/LanguageContext";
import fiscalService from "../../services/fiscalService";
import { useToast } from "../../components/common/Toast";
import SourcePanel, {
  normalizeSource,
} from "../../components/fiscal/SourcePanel";
import ConsultationsRail from "../../components/fiscal/ConsultationsRail";

const EASE = "ease-[cubic-bezier(.16,1,.3,1)]";

const SECTIONS = [
  {
    key: "contextefaits",
    field: "contexteFaits",
    label: "Contexte & faits",
    num: "1",
  },
  { key: "etendue", field: "etendue", label: "Étendue des travaux", num: "2" },
  {
    key: "sommaire",
    field: "sommairExecutif",
    label: "Sommaire exécutif",
    num: "3",
  },
  { key: "analyses", field: "analyses", label: "Analyses", num: "4" },
  {
    key: "documents",
    field: "documents",
    label: "Documents & références",
    num: "5",
  },
];

const QUICK_ACTIONS = [
  {
    label: "Reformuler",
    prompt: "Reformule ce passage de façon plus claire et professionnelle.",
    color: "bg-indigo-500 hover:bg-indigo-600 text-white",
  },
  {
    label: "Raccourcir",
    prompt: "Raccourcis ce passage en gardant l'essentiel.",
    color: "bg-emerald-500 hover:bg-emerald-600 text-white",
  },
  {
    label: "Développer",
    prompt: "Développe ce passage avec plus de précision juridique.",
    color: "bg-violet-500 hover:bg-violet-600 text-white",
  },
  {
    label: "Sourcer",
    prompt:
      "Appuie ce passage par une citation de source juridique pertinente.",
    color: "bg-amber-500 hover:bg-amber-600 text-white",
  },
];

function DocSection({ value, onChange, onMouseUp, flash, innerRef }) {
  const ref = useRef();
  useEffect(() => {
    if (!ref.current) return;
    ref.current.style.height = "auto";
    ref.current.style.height = ref.current.scrollHeight + "px";
  }, [value]);
  return (
    <textarea
      ref={(el) => {
        ref.current = el;
        if (innerRef) innerRef.current = el;
      }}
      value={value}
      onChange={onChange}
      onMouseUp={onMouseUp}
      rows={1}
      spellCheck={false}
      className={`w-full bg-transparent text-[14px] text-dark leading-[1.9] resize-none overflow-hidden focus:outline-none rounded-lg px-2 -mx-2 py-1 transition-colors duration-700 ${flash ? "bg-brand/15" : "hover:bg-light/50 focus:bg-light/40"}`}
      placeholder="Section vide — rédigez ou demandez à l'IA…"
    />
  );
}

/* ───────────────────── AI co-writer panel ───────────────────── */

function AssistantPanel({
  thread,
  busy,
  target,
  setTarget,
  prompt,
  setPrompt,
  onSend,
  onClose,
  t,
}) {
  const endRef = useRef();
  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: "smooth", block: "nearest" });
  }, [thread, busy]);
  const targetSection = SECTIONS.find((s) => s.key === target);

  return (
    <div className="h-full flex flex-col bg-white">
      {/* header */}
      <div className="px-4 py-3 border-b border-border bg-gradient-to-r from-dark to-[#3a3a4a] relative shrink-0">
        <div className="absolute bottom-0 left-0 w-full h-[2px] bg-gradient-to-r from-brand to-violet-400" />
        <div className="flex items-center justify-between gap-2">
          <div className="flex items-center gap-2.5 min-w-0">
            <span className="w-8 h-8 rounded-xl bg-gradient-to-br from-brand to-[#F59E0B] flex items-center justify-center shrink-0 shadow-md">
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
                  d="M9.813 15.904L9 18.75l-.813-2.846a4.5 4.5 0 00-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 003.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 003.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 00-3.09 3.09z"
                />
              </svg>
            </span>
            <div>
              <p className="text-sm font-bold text-white">
                {t("editor.aiCoWriter")}
              </p>
              <p className="text-[10px] text-white/50">
                {t("editor.aiCoWriterHint")}
              </p>
            </div>
          </div>
          <button
            onClick={onClose}
            className="w-7 h-7 rounded-lg text-white/60 hover:text-white hover:bg-white/10 flex items-center justify-center transition-colors shrink-0"
            aria-label={t("editor.hidePanel")}
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
      </div>

      {/* target selector */}
      <div className="px-3 py-2.5 border-b border-border bg-sand/60 flex items-center gap-2 overflow-x-auto shrink-0">
        <span className="text-[10px] font-bold text-muted uppercase tracking-wider shrink-0">
          {t("editor.target")}
        </span>
        <div className="flex gap-1">
          {SECTIONS.map((s) => (
            <button
              key={s.key}
              onClick={() => setTarget(s.key)}
              title={s.label}
              className={`shrink-0 text-[11px] font-bold rounded-lg px-2 py-1 transition-colors ${target === s.key ? "bg-gradient-to-r from-dark to-[#3a3a4a] text-brand" : "bg-white border border-border text-body hover:text-dark"}`}
            >
              {s.num}. {s.label.split(" ")[0]}
            </button>
          ))}
        </div>
      </div>

      {/* thread */}
      <div className="flex-1 overflow-y-auto p-4 space-y-3">
        {thread.length === 0 && (
          <div className="text-[13px] text-muted leading-relaxed animate-fade-in">
            <p className="font-semibold text-dark mb-2">
              {t("editor.modifyNaturalLanguage")}
            </p>
            <p className="mb-3 text-[12px]">{t("editor.selectPassage")}</p>
            <div className="space-y-2">
              {[
                {
                  text: t("editor.example1"),
                  color: "border-l-indigo-400 bg-indigo-50/50",
                },
                {
                  text: t("editor.example2"),
                  color: "border-l-emerald-400 bg-emerald-50/50",
                },
                {
                  text: t("editor.example3"),
                  color: "border-l-violet-400 bg-violet-50/50",
                },
              ].map((ex) => (
                <button
                  key={ex.text}
                  onClick={() => {
                    setPrompt(ex.text);
                  }}
                  className={`w-full text-left border-l-[3px] rounded-lg px-3 py-2.5 text-[12px] text-body hover:text-dark transition-colors cursor-pointer ${ex.color}`}
                >
                  « {ex.text} »
                </button>
              ))}
            </div>
          </div>
        )}
        {thread.map((m, i) => (
          <div
            key={i}
            className={`flex animate-pop opacity-0 ${m.role === "user" ? "justify-end" : "justify-start"}`}
          >
            <div
              className={`max-w-[90%] px-3.5 py-2.5 text-[13px] leading-relaxed ${m.role === "user" ? "bg-gradient-to-br from-dark to-[#3a3a4a] text-white rounded-2xl rounded-br-md shadow-md" : `bg-light border border-border rounded-2xl rounded-bl-md ${m.error ? "border-red-200 text-red-600" : "text-dark"}`}`}
            >
              {m.role === "user" && (
                <span className="block text-[10px] font-bold text-brand mb-1 uppercase tracking-wide">
                  {m.section}
                </span>
              )}
              {m.quoted && (
                <span className="block text-[11.5px] italic text-white/60 border-l-2 border-brand pl-2 mb-1.5">
                  « {m.quoted} »
                </span>
              )}
              <span className="whitespace-pre-wrap">{m.content}</span>
            </div>
          </div>
        ))}
        {busy && (
          <div className="flex justify-start animate-fade-in">
            <div className="bg-light border border-border rounded-2xl rounded-bl-md px-4 py-3 flex items-center gap-2">
              <div className="w-5 h-5 rounded-full bg-gradient-to-r from-brand to-violet-400 flex items-center justify-center">
                <span className="w-2.5 h-2.5 border-2 border-dark border-t-transparent rounded-full animate-spin" />
              </div>
              <span className="text-[12px] text-muted font-medium">
                {t("editor.rewriting")}
              </span>
            </div>
          </div>
        )}
        <div ref={endRef} />
      </div>

      {/* composer */}
      <form
        onSubmit={onSend}
        className="border-t border-border p-3 shrink-0 bg-white"
      >
        <div className="flex items-end gap-2">
          <textarea
            value={prompt}
            onChange={(e) => setPrompt(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter" && !e.shiftKey) {
                e.preventDefault();
                onSend();
              }
            }}
            rows={2}
            placeholder={`${t("editor.modify")} « ${targetSection.label} »…`}
            className="flex-1 min-w-0 bg-light border border-border rounded-xl px-3.5 py-2.5 text-[13px] text-dark placeholder-muted focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent resize-none"
          />
          <button
            type="submit"
            disabled={busy || !prompt.trim()}
            className="w-10 h-10 bg-gradient-to-br from-brand to-[#F59E0B] rounded-xl flex items-center justify-center hover:shadow-lg hover:shadow-brand/30 active:scale-95 disabled:opacity-40 transition-all shrink-0"
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
                d="M4.5 10.5L12 3m0 0l7.5 7.5M12 3v18"
              />
            </svg>
          </button>
        </div>
      </form>
    </div>
  );
}

/* ───────────────────────── editor workspace ───────────────────────── */
export default function ConsultationEditor() {
  const { id } = useParams();
  const navigate = useNavigate();
  const { user } = useAuth();
  const { t } = useLanguage();
  const { toast } = useToast();
  const [meta, setMeta] = useState(null);
  const [output, setOutput] = useState(null);
  const [sessionId, setSessionId] = useState(null);
  const [target, setTarget] = useState("analyses");
  const [prompt, setPrompt] = useState("");
  const [thread, setThread] = useState([]);
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState(true);
  const [rating, setRating] = useState(0);
  const [flashKey, setFlashKey] = useState(null);
  const [exporting, setExporting] = useState(false);
  const [viewing, setViewing] = useState(null);
  const [viewList, setViewList] = useState([]);
  const [sel, setSel] = useState(null);
  const [selPrompt, setSelPrompt] = useState("");
  const [siblings, setSiblings] = useState(null);
  const [railOpen, setRailOpen] = useState(
    () => localStorage.getItem("taxmind.consultations.rail") !== "0",
  );
  const [assistOpen, setAssistOpen] = useState(
    () => localStorage.getItem("taxmind.editor.assistant") !== "0",
  );
  const [mobileAssist, setMobileAssist] = useState(false);
  const selPopupRef = useRef();
  const sectionRefs = useRef({});

  useEffect(() => {
    localStorage.setItem("taxmind.consultations.rail", railOpen ? "1" : "0");
  }, [railOpen]);
  useEffect(() => {
    localStorage.setItem("taxmind.editor.assistant", assistOpen ? "1" : "0");
  }, [assistOpen]);

  useEffect(() => {
    const down = (e) => {
      if (selPopupRef.current && !selPopupRef.current.contains(e.target))
        setSel(null);
    };
    const key = (e) => {
      if (e.key === "Escape") setSel(null);
    };
    if (sel) {
      document.addEventListener("mousedown", down);
      document.addEventListener("keydown", key);
    }
    return () => {
      document.removeEventListener("mousedown", down);
      document.removeEventListener("keydown", key);
    };
  }, [sel]);

  useEffect(() => {
    fiscalService
      .list("")
      .then(({ data }) => setSiblings(data.data))
      .catch(() => setSiblings([]));
  }, [id]);

  const load = useCallback(async () => {
    setMeta(null);
    setOutput(null);
    setThread([]);
    setViewing(null);
    setSaved(true);
    setTarget("analyses");
    const { data } = await fiscalService.get(id);
    const d = data.data;
    setMeta(d);
    setOutput(d.output);
    setRating(d.rating || 0);
    try {
      const { data: s } = await fiscalService.startSession(
        d.id,
        d.clientName,
        d.reference,
      );
      setSessionId(s.data.sessionId);
    } catch {
      /* refinement still attempts with a deterministic session id */
    }
  }, [id]);

  useEffect(() => {
    load();
  }, [load]);

  const openAssistant = () => {
    setAssistOpen(true);
    if (window.matchMedia("(max-width: 1023px)").matches) setMobileAssist(true);
  };

  const refine = async (message, sectionKey, quoted) => {
    const msg = (message || "").trim();
    if (!msg || busy || !output) return;
    const sec =
      SECTIONS.find((s) => s.key === sectionKey) ||
      SECTIONS.find((s) => s.key === target);
    const fullMsg = quoted
      ? `Concernant ce passage : « ${quoted} »\n\n${msg}`
      : msg;
    openAssistant();
    setThread((t) => [
      ...t,
      { role: "user", content: msg, section: sec.label, quoted },
    ]);
    setBusy(true);
    try {
      const { data } = await fiscalService.refine({
        sessionId: sessionId || `sess_${id}`,
        userMessage: fullMsg,
        targetSection: sec.key,
        currentOutput: output,
        sources: output.sources || [],
      });
      setOutput(data.data.updatedOutput);
      setSaved(false);
      const changed =
        SECTIONS.find((s) => s.key === (data.data.sectionName || sec.key)) ||
        sec;
      setTarget(changed.key);
      setFlashKey(changed.key);
      sectionRefs.current[changed.key]?.scrollIntoView({
        behavior: "smooth",
        block: "center",
      });
      setTimeout(() => setFlashKey(null), 1400);
      setThread((t) => [
        ...t,
        { role: "assistant", content: data.data.reply, section: changed.label },
      ]);
    } catch (err) {
      const m = err.response?.data?.message || t("editor.refineFailed");
      setThread((t) => [...t, { role: "assistant", content: m, error: true }]);
      toast(m, "error", 6000);
    } finally {
      setBusy(false);
    }
  };

  const sendPanel = (e) => {
    e?.preventDefault();
    const m = prompt;
    setPrompt("");
    refine(m, target);
  };

  const sendSelection = (instruction) => {
    if (!sel) return;
    const { sectionKey, text } = sel;
    setSel(null);
    setSelPrompt("");
    setTarget(sectionKey);
    refine(
      instruction,
      sectionKey,
      text.length > 220 ? text.slice(0, 220) + "…" : text,
    );
  };

  const onSectionMouseUp = (sectionKey) => (e) => {
    const el = e.target;
    const text = el.value.substring(el.selectionStart, el.selectionEnd).trim();
    if (text.length < 3) {
      setSel(null);
      return;
    }
    const x = Math.min(e.clientX, window.innerWidth - 340);
    const y = Math.min(e.clientY + 14, window.innerHeight - 220);
    setSel({ x, y, sectionKey, text });
  };

  const save = async () => {
    if (!output) return;
    try {
      await fiscalService.saveOutput(id, output);
      setSaved(true);
      toast(t("editor.saved"), "success");
    } catch {
      toast(t("editor.saveFailed"), "error");
    }
  };

  const exportDocx = async () => {
    setExporting(true);
    try {
      const { data } = await fiscalService.exportDocx({
        reference: meta.reference,
        clientName: meta.clientName,
        situation: meta.situation,
        fiscalQuestion: meta.fiscalQuestion,
        documents: [],
        output,
      });
      const url = URL.createObjectURL(data);
      const a = document.createElement("a");
      a.href = url;
      a.download = `Consultation_${meta.clientName}.docx`;
      a.click();
      URL.revokeObjectURL(url);
      toast(t("editor.exportSuccess"), "success");
    } catch {
      toast(t("editor.exportFailed"), "error");
    } finally {
      setExporting(false);
    }
  };

  const rate = async (stars) => {
    setRating(stars);
    try {
      await fiscalService.rate(id, meta.reference, stars);
      toast(`${t("editor.rated")} ${stars}★`, "success");
    } catch {
      /* non-critical */
    }
  };

  const openSource = (s) => {
    const list = (output.sources || []).map((x) => normalizeSource(x));
    setViewList(list);
    setViewing(list.find((x) => x.index === s.index) || normalizeSource(s));
    setRailOpen(false);
  };

  const switchConsultation = (cid) => {
    if (cid === id) return;
    if (!saved && !window.confirm(t("editor.unsavedWarning"))) return;
    navigate(`/app/consultations/${cid}`);
  };

  const created = meta?.createdAt
    ? new Date(meta.createdAt).toLocaleDateString(undefined, {
        day: "numeric",
        month: "long",
        year: "numeric",
      })
    : "";
  const ready = meta && output;
  const authorName = user
    ? `${user.firstName || ""} ${user.lastName || ""}`.trim()
    : "";

  const assistantPanel = (closeFn) => (
    <AssistantPanel
      thread={thread}
      busy={busy}
      target={target}
      setTarget={setTarget}
      prompt={prompt}
      setPrompt={setPrompt}
      onSend={sendPanel}
      onClose={closeFn}
      t={t}
    />
  );

  return (
    <div className="h-full flex bg-sand">
      {/* ① consultations rail — desktop, collapsible */}
      <aside
        className={`hidden md:block shrink-0 overflow-hidden transition-[width] duration-300 ${EASE} ${railOpen ? "w-[250px] border-r border-border" : "w-0"}`}
      >
        <div className="w-[250px] h-full">
          <ConsultationsRail
            items={siblings}
            activeId={id}
            onSelect={switchConsultation}
            onNew={() => navigate("/app/consultations")}
          />
        </div>
      </aside>

      {/* ② document column */}
      <section className="flex-1 min-w-0 flex flex-col">
        {/* toolbar */}
        <div className="h-12 shrink-0 flex items-center gap-2 px-3 border-b border-border/70 bg-white backdrop-blur">
          <button
            onClick={() =>
              setRailOpen((o) => {
                if (!o) setViewing(null);
                return !o;
              })
            }
            className="hidden md:flex w-8 h-8 rounded-lg text-muted hover:text-dark hover:bg-light items-center justify-center transition-colors shrink-0"
            aria-label={railOpen ? t("editor.hideList") : t("editor.showList")}
            title={railOpen ? t("editor.hideList") : t("editor.showList")}
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
          <Link
            to="/app/consultations"
            className="w-8 h-8 rounded-lg text-muted hover:text-dark hover:bg-light flex items-center justify-center transition-colors shrink-0"
            title={t("editor.allConsultations")}
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
          </Link>

          <div className="flex-1 min-w-0 flex items-center gap-2">
            <p className="text-[13px] font-bold text-dark truncate">
              {meta?.clientName || t("editor.opening")}
            </p>
            {meta && (
              <span
                className={`text-[9px] font-bold text-dark bg-gradient-to-r from-brand/30 to-violet-400/20 rounded-full px-2 py-0.5 shrink-0 ${viewing ? "hidden 2xl:inline" : "hidden sm:inline"}`}
              >
                {meta.reference}
              </span>
            )}
            <span
              className={`w-1.5 h-1.5 rounded-full shrink-0 ${saved ? "bg-emerald-500" : "bg-amber-500 animate-pulse"}`}
              title={saved ? t("editor.saved") : t("editor.unsaved")}
            />
          </div>

          {/* rating */}
          <div
            className={`items-center gap-0.5 mr-1 shrink-0 ${viewing ? "hidden 2xl:flex" : "hidden lg:flex"}`}
          >
            {[1, 2, 3, 4, 5].map((n) => (
              <button
                key={n}
                onClick={() => ready && rate(n)}
                className="p-0.5"
                title={`${t("editor.rate")} ${n}/5`}
              >
                <svg
                  className={`w-4 h-4 transition-colors ${n <= rating ? "text-brand drop-shadow-sm" : "text-border hover:text-brand/60"}`}
                  fill="currentColor"
                  viewBox="0 0 20 20"
                >
                  <path d="M9.049 2.927c.3-.921 1.603-.921 1.902 0l1.07 3.292a1 1 0 00.95.69h3.462c.969 0 1.371 1.24.588 1.81l-2.8 2.034a1 1 0 00-.364 1.118l1.07 3.292c.3.921-.755 1.688-1.54 1.118l-2.8-2.034a1 1 0 00-1.175 0l-2.8 2.034c-.784.57-1.838-.197-1.539-1.118l1.07-3.292a1 1 0 00-.364-1.118L2.98 8.72c-.783-.57-.38-1.81.588-1.81h3.461a1 1 0 00.951-.69l1.07-3.292z" />
                </svg>
              </button>
            ))}
          </div>

          <button
            onClick={save}
            disabled={saved || !ready}
            className={`shrink-0 text-[12px] font-bold rounded-lg px-3 py-1.5 border transition-all ${saved ? "border-border text-muted" : "border-dark bg-dark text-white hover:bg-black"}`}
          >
            {saved ? t("editor.saved") : t("editor.save")}
          </button>
          <button
            onClick={exportDocx}
            disabled={exporting || !ready}
            className="shrink-0 text-[12px] font-bold rounded-lg px-3 py-1.5 bg-gradient-to-r from-brand to-[#F59E0B] text-dark hover:shadow-md hover:shadow-brand/40 disabled:opacity-50 transition-all flex items-center gap-1.5"
          >
            {exporting ? (
              <span className="w-3.5 h-3.5 border-2 border-dark border-t-transparent rounded-full animate-spin" />
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
                  d="M3 16.5v2.25A2.25 2.25 0 005.25 21h13.5A2.25 2.25 0 0021 18.75V16.5M16.5 12L12 16.5m0 0L7.5 12m4.5 4.5V3"
                />
              </svg>
            )}
            <span className="hidden sm:inline">.docx</span>
          </button>
          <button
            onClick={() => {
              if (window.matchMedia("(min-width: 1024px)").matches)
                setAssistOpen((o) => !o);
              else setMobileAssist(true);
            }}
            className={`shrink-0 w-8 h-8 rounded-lg flex items-center justify-center transition-colors ${assistOpen ? "bg-gradient-to-r from-dark to-[#3a3a4a] text-brand shadow-md" : "text-muted hover:text-dark hover:bg-light"}`}
            aria-label={t("editor.aiAssistant")}
            title={t("editor.aiCoWriter")}
          >
            <svg
              className="w-[17px] h-[17px]"
              fill="none"
              viewBox="0 0 24 24"
              stroke="currentColor"
              strokeWidth={1.9}
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                d="M9.813 15.904L9 18.75l-.813-2.846a4.5 4.5 0 00-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 003.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 003.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 00-3.09 3.09z"
              />
            </svg>
          </button>
        </div>

        {/* document — Google Docs style paper */}
        <div className="flex-1 overflow-y-auto bg-[#f0f0f0] dark:bg-sand">
          {!ready ? (
            <div className="h-full flex items-center justify-center gap-3 text-muted">
              <div className="w-6 h-6 rounded-full bg-gradient-to-r from-brand to-violet-400 flex items-center justify-center">
                <span className="w-3 h-3 border-2 border-dark border-t-transparent rounded-full animate-spin" />
              </div>
              {t("editor.opening")}
            </div>
          ) : (
            <div className="max-w-[816px] mx-auto py-6 px-4 animate-fade-up">
              {/* The paper */}
              <div className="bg-white shadow-lg rounded-sm min-h-[1056px] relative">
                {/* gold top accent line */}
                <div className="absolute top-0 left-0 w-full h-1 bg-gradient-to-r from-brand via-[#F59E0B] to-brand" />

                <div className="px-12 sm:px-16 py-12 sm:py-14">
                  {/* ── letterhead ── */}
                  <div className="text-center mb-6">
                    <div className="flex items-center justify-center gap-2 mb-1">
                      <svg
                        width="22"
                        height="8"
                        viewBox="0 0 26 8"
                        aria-hidden="true"
                      >
                        <polygon points="0,8 26,0 26,8" fill="#FFE600" />
                      </svg>
                      <span className="text-[11px] font-bold text-dark tracking-[0.3em] uppercase">
                        EY | Taxmind
                      </span>
                    </div>
                    <p className="text-[10px] text-muted tracking-wider">
                      {t("editor.expertise")}
                    </p>
                  </div>

                  {/* separator */}
                  <div className="border-t-2 border-dark mb-6" />

                  {/* title */}
                  <h1 className="text-center font-display text-[22px] font-extrabold text-dark uppercase tracking-wider mb-2">
                    {t("editor.fiscalConsultation")}
                  </h1>
                  <p className="text-center text-[13px] text-muted italic mb-6">
                    {t("editor.consultationSubtitle")} —{" "}
                    {meta.fiscalQuestion?.slice(0, 80)}
                    {meta.fiscalQuestion?.length > 80 ? "…" : ""}
                  </p>

                  {/* metadata table */}
                  <div className="border border-border rounded-lg overflow-hidden mb-8">
                    <table className="w-full text-[13px]">
                      <tbody>
                        {[
                          [t("editor.reference"), meta.reference],
                          [t("editor.date"), created],
                          [t("editor.recipient"), meta.clientName],
                          [t("editor.author"), authorName || "EY Taxmind"],
                        ].map(([label, val]) => (
                          <tr
                            key={label}
                            className="border-b border-border last:border-b-0"
                          >
                            <td className="px-4 py-2.5 bg-sand/60 font-bold text-dark w-[160px] border-r border-border">
                              {label}
                            </td>
                            <td className="px-4 py-2.5 text-body">{val}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>

                  {/* ── document sections ── */}
                  <div className="space-y-8">
                    {SECTIONS.map((s) => {
                      const sectionColors = {
                        contextefaits: "text-indigo-600 bg-indigo-100",
                        etendue: "text-violet-600 bg-violet-100",
                        sommaire: "text-emerald-600 bg-emerald-100",
                        analyses: "text-amber-600 bg-amber-100",
                        documents: "text-blue-600 bg-blue-100",
                      };
                      return (
                        <section
                          key={s.key}
                          ref={(el) => {
                            sectionRefs.current[s.key] = el;
                          }}
                        >
                          <div className="flex items-center justify-between gap-3 mb-3 group">
                            <h3 className="flex items-center gap-2.5">
                              <span
                                className={`w-7 h-7 rounded-lg ${sectionColors[s.key] || "bg-light text-muted"} text-[12px] font-extrabold flex items-center justify-center`}
                              >
                                {s.num}
                              </span>
                              <span className="text-[14px] font-extrabold text-dark uppercase tracking-[0.08em]">
                                {s.label}
                              </span>
                            </h3>
                            <button
                              onClick={() => {
                                setTarget(s.key);
                                openAssistant();
                              }}
                              title={`${t("editor.modifyWith")} « ${s.label} »`}
                              className={`flex items-center gap-1.5 text-[11px] font-bold rounded-lg px-2.5 py-1.5 transition-all ${target === s.key && assistOpen ? "bg-gradient-to-r from-dark to-[#3a3a4a] text-brand shadow-sm" : "text-muted opacity-0 group-hover:opacity-100 hover:bg-light hover:text-dark"}`}
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
                                  d="M9.813 15.904L9 18.75l-.813-2.846a4.5 4.5 0 00-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 003.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 003.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 00-3.09 3.09z"
                                />
                              </svg>
                              IA
                            </button>
                          </div>
                          <DocSection
                            value={output[s.field] || ""}
                            onChange={(e) => {
                              setOutput((o) => ({
                                ...o,
                                [s.field]: e.target.value,
                              }));
                              setSaved(false);
                            }}
                            onMouseUp={onSectionMouseUp(s.key)}
                            flash={flashKey === s.key}
                          />
                          {s.key === "analyses" &&
                            output.analysisTable?.length > 0 && (
                              <div className="mt-5 border border-border rounded-xl overflow-x-auto">
                                <table className="w-full text-[12.5px]">
                                  <thead>
                                    <tr className="bg-gradient-to-r from-dark to-[#3a3a4a] text-white">
                                      <th className="text-left px-3.5 py-2.5 font-semibold">
                                        {t("editor.subject")}
                                      </th>
                                      <th className="text-left px-3.5 py-2.5 font-semibold">
                                        {t("editor.analysis")}
                                      </th>
                                      <th className="text-left px-3.5 py-2.5 font-semibold">
                                        {t("editor.conclusion")}
                                      </th>
                                    </tr>
                                  </thead>
                                  <tbody>
                                    {output.analysisTable.map((r, j) => (
                                      <tr
                                        key={j}
                                        className={
                                          j % 2 ? "bg-light/60" : "bg-white"
                                        }
                                      >
                                        <td className="px-3.5 py-2.5 align-top font-semibold text-dark">
                                          {r.sujet}
                                        </td>
                                        <td className="px-3.5 py-2.5 align-top text-body leading-relaxed">
                                          {r.analyse}
                                        </td>
                                        <td className="px-3.5 py-2.5 align-top font-bold text-dark">
                                          {r.conclusion}
                                        </td>
                                      </tr>
                                    ))}
                                  </tbody>
                                </table>
                              </div>
                            )}
                        </section>
                      );
                    })}
                  </div>

                  {/* sources */}
                  {output.sources?.length > 0 && (
                    <div className="mt-10 pt-6 border-t-2 border-dark">
                      <h3 className="flex items-center gap-2 text-[14px] font-extrabold text-dark uppercase tracking-[0.08em] mb-4">
                        <span className="w-7 h-7 rounded-lg bg-rose-100 text-rose-600 text-[12px] font-extrabold flex items-center justify-center">
                          S
                        </span>
                        {t("editor.legalSources")} ({output.sources.length})
                      </h3>
                      <div className="grid sm:grid-cols-2 gap-2">
                        {output.sources.map((s) => {
                          const open = viewing && viewing.index === s.index;
                          return (
                            <button
                              key={s.index}
                              onClick={() => openSource(s)}
                              className={`group flex items-start gap-2.5 text-left rounded-xl px-3 py-2.5 border transition-all ${open ? "bg-white border-brand ring-1 ring-brand/60 shadow-sm" : "bg-sand/60 hover:bg-white border-transparent hover:border-border hover:shadow-sm"}`}
                            >
                              <span
                                className={`shrink-0 w-6 h-6 rounded-md text-dark text-[10px] font-extrabold flex items-center justify-center transition-colors ${open ? "bg-brand" : "bg-brand/40 group-hover:bg-brand"}`}
                              >
                                S{s.index}
                              </span>
                              <span className="min-w-0">
                                <span className="block text-[12.5px] font-bold text-dark truncate capitalize">
                                  {(s.docName || "").replace(/[-_]/g, " ")}{" "}
                                  {s.year && `(${s.year})`}
                                </span>
                                <span className="block text-[11.5px] text-muted truncate">
                                  {s.articleRef || s.sectionTitle || s.text}
                                </span>
                              </span>
                            </button>
                          );
                        })}
                      </div>
                    </div>
                  )}

                  {/* footer */}
                  <div className="mt-12 pt-4 border-t border-border text-center">
                    <p className="text-[10px] text-muted tracking-wider">
                      {t("editor.confidential")} — EY Taxmind ©{" "}
                      {new Date().getFullYear()}
                    </p>
                  </div>
                </div>
              </div>
            </div>
          )}
        </div>
      </section>

      {/* ③ co-writer — desktop column, collapsible */}
      <aside
        className={`hidden lg:block shrink-0 overflow-hidden transition-[width] duration-300 ${EASE} ${assistOpen ? "w-[350px] border-l border-border" : "w-0"}`}
      >
        <div className="w-[350px] h-full">
          {assistantPanel(() => setAssistOpen(false))}
        </div>
      </aside>

      {/* ③ co-writer — mobile/tablet overlay */}
      {mobileAssist && (
        <div className="lg:hidden fixed inset-0 z-[70]">
          <div
            className="absolute inset-0 bg-dark/40 backdrop-blur-[2px] animate-fade-in"
            onClick={() => setMobileAssist(false)}
          />
          <div className="absolute right-0 top-0 h-full w-full max-w-[380px] shadow-2xl animate-slide-in-right">
            {assistantPanel(() => setMobileAssist(false))}
          </div>
        </div>
      )}

      {/* ④ source — inline pane on wide screens */}
      <aside
        className={`hidden xl:block shrink-0 overflow-hidden transition-[width] duration-300 ${EASE} ${viewing ? "w-[380px]" : "w-0"}`}
      >
        {viewing && (
          <div className="w-[380px] h-full border-l border-border">
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

      {/* ④ source — overlay below xl */}
      {viewing && (
        <div
          className="xl:hidden fixed inset-0 z-[80]"
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

      {/* selection popup */}
      {sel && (
        <div
          ref={selPopupRef}
          className="fixed z-[70] w-[320px] bg-white border border-border rounded-2xl shadow-2xl shadow-dark/20 animate-pop overflow-hidden"
          style={{ left: sel.x, top: sel.y }}
        >
          <div className="px-3.5 pt-3 pb-2 bg-gradient-to-r from-sand to-light border-b border-border">
            <p className="text-[10px] font-bold text-muted uppercase tracking-wider mb-1">
              {t("editor.selectedPassage")} ·{" "}
              {SECTIONS.find((s) => s.key === sel.sectionKey)?.label}
            </p>
            <p className="text-[12px] text-dark italic line-clamp-2">
              « {sel.text} »
            </p>
          </div>
          <div className="p-2.5">
            <div className="flex flex-wrap gap-1.5 mb-2">
              {QUICK_ACTIONS.map((a) => (
                <button
                  key={a.label}
                  onClick={() => sendSelection(a.prompt)}
                  className={`text-[11px] font-bold rounded-lg px-2.5 py-1.5 transition-all hover:shadow-md ${a.color}`}
                >
                  {a.label}
                </button>
              ))}
            </div>
            <form
              onSubmit={(e) => {
                e.preventDefault();
                if (selPrompt.trim()) sendSelection(selPrompt);
              }}
              className="flex items-center gap-1.5"
            >
              <input
                autoFocus
                value={selPrompt}
                onChange={(e) => setSelPrompt(e.target.value)}
                placeholder={t("editor.describeModification")}
                className="flex-1 bg-light border border-border rounded-lg px-3 py-2 text-[12.5px] text-dark placeholder-muted focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent"
              />
              <button
                type="submit"
                disabled={!selPrompt.trim()}
                className="w-8 h-8 bg-gradient-to-br from-dark to-[#3a3a4a] text-brand rounded-lg flex items-center justify-center hover:shadow-md disabled:opacity-30 transition-all shrink-0"
              >
                <svg
                  className="w-3.5 h-3.5"
                  fill="none"
                  viewBox="0 0 24 24"
                  stroke="currentColor"
                  strokeWidth={2.2}
                >
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    d="M4.5 10.5L12 3m0 0l7.5 7.5M12 3v18"
                  />
                </svg>
              </button>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
