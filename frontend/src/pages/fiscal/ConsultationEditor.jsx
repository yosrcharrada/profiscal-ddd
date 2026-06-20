import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import fiscalService from "../../services/fiscalService";
import { useToast } from "../../components/common/Toast";
import SourcePanel, {
  normalizeSource,
} from "../../components/fiscal/SourcePanel";
import ConsultationsRail from "../../components/fiscal/ConsultationsRail";

const EASE = "ease-[cubic-bezier(.16,1,.3,1)]";

/* The five editable sections, mapped to ConsultationOutput fields + refine keys. */
const SECTIONS = [
  { key: "contextefaits", field: "contexteFaits", label: "Contexte & faits" },
  { key: "etendue", field: "etendue", label: "Étendue des travaux" },
  { key: "sommaire", field: "sommairExecutif", label: "Sommaire exécutif" },
  { key: "analyses", field: "analyses", label: "Analyses" },
  { key: "documents", field: "documents", label: "Documents & références" },
];

const QUICK_ACTIONS = [
  {
    label: "Reformuler",
    prompt: "Reformule ce passage de façon plus claire et professionnelle.",
  },
  {
    label: "Raccourcir",
    prompt: "Raccourcis ce passage en gardant l’essentiel.",
  },
  {
    label: "Développer",
    prompt: "Développe ce passage avec plus de précision juridique.",
  },
  {
    label: "Sourcer",
    prompt:
      "Appuie ce passage par une citation de source juridique pertinente.",
  },
];

/* Borderless, auto-growing textarea — the document feels like Notion, not a form. */
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
      className={`w-full bg-transparent text-[15px] text-dark leading-[1.9] resize-none overflow-hidden focus:outline-none rounded-lg px-2 -mx-2 py-1 transition-colors duration-700 ${flash ? "bg-brand/15" : "hover:bg-light/50 focus:bg-light/40"}`}
      placeholder="Section vide — rédigez ou demandez à l’IA…"
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
}) {
  const endRef = useRef();
  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: "smooth", block: "nearest" });
  }, [thread, busy]);
  const targetSection = SECTIONS.find((s) => s.key === target);

  return (
    <div className="h-full flex flex-col bg-white">
      <div className="px-4 py-3 border-b border-border bg-dark relative shrink-0">
        <div className="absolute bottom-0 left-0 w-full h-[2px] bg-brand" />
        <div className="flex items-center justify-between gap-2">
          <div className="flex items-center gap-2.5 min-w-0">
            <span className="w-7 h-7 rounded-lg bg-brand flex items-center justify-center shrink-0">
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
                  d="M9.813 15.904L9 18.75l-.813-2.846a4.5 4.5 0 00-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 003.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 003.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 00-3.09 3.09z"
                />
              </svg>
            </span>
            <p className="text-sm font-bold text-white truncate">
              Co-rédacteur IA
            </p>
          </div>
          <button
            onClick={onClose}
            className="w-7 h-7 rounded-lg text-white/60 hover:text-white hover:bg-white/10 flex items-center justify-center transition-colors shrink-0"
            title="Masquer le panneau"
            aria-label="Masquer l’assistant"
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
          Cible
        </span>
        <div className="flex gap-1">
          {SECTIONS.map((s, i) => (
            <button
              key={s.key}
              onClick={() => setTarget(s.key)}
              title={s.label}
              className={`shrink-0 text-[11px] font-bold rounded-lg px-2 py-1 transition-colors ${target === s.key ? "bg-dark text-brand" : "bg-white border border-border text-body hover:text-dark"}`}
            >
              {i + 1}. {s.label.split(" ")[0]}
            </button>
          ))}
        </div>
      </div>

      {/* thread */}
      <div className="flex-1 overflow-y-auto p-4 space-y-3">
        {thread.length === 0 && (
          <div className="text-[13px] text-muted leading-relaxed animate-fade-in">
            <p className="font-semibold text-dark mb-1">
              Modifiez le document en langage naturel.
            </p>
            <p className="mb-3">
              Sélectionnez un passage dans le document, ou ciblez une section et
              décrivez le changement :
            </p>
            <ul className="space-y-1.5">
              <li className="bg-light border border-border rounded-lg px-3 py-2">
                « Ajoute une analyse sur la TVA pour les non-résidents. »
              </li>
              <li className="bg-light border border-border rounded-lg px-3 py-2">
                « Reformule le sommaire de façon plus concise. »
              </li>
              <li className="bg-light border border-border rounded-lg px-3 py-2">
                « Cite la convention Tunisie-France sur les dividendes. »
              </li>
            </ul>
          </div>
        )}
        {thread.map((m, i) => (
          <div
            key={i}
            className={`flex animate-pop opacity-0 ${m.role === "user" ? "justify-end" : "justify-start"}`}
          >
            <div
              className={`max-w-[90%] px-3.5 py-2.5 text-[13px] leading-relaxed ${m.role === "user" ? "bg-dark text-white rounded-2xl rounded-br-md" : `bg-light border border-border rounded-2xl rounded-bl-md ${m.error ? "border-red-200 text-red-600" : "text-dark"}`}`}
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
              <span className="text-[12px] text-muted font-medium">
                Réécriture
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
            placeholder={`Modifier « ${targetSection.label} »…`}
            className="flex-1 min-w-0 bg-light border border-border rounded-xl px-3.5 py-2.5 text-[13px] text-dark placeholder-muted focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent resize-none"
          />
          <button
            type="submit"
            disabled={busy || !prompt.trim()}
            className="w-10 h-10 bg-brand rounded-xl flex items-center justify-center hover:shadow-md hover:shadow-brand/40 active:scale-95 disabled:opacity-40 transition-all shrink-0"
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

/* ───────────────────────── editor workspace ─────────────────────────
   Multi-pane, like the chat & search: consultations rail | document |
   co-writer | source. Clicking a source opens the passage as an extra
   vertical pane on wide screens (overlay otherwise) and auto-collapses
   the rail to give the document room. */
export default function ConsultationEditor() {
  const { id } = useParams();
  const navigate = useNavigate();
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
  const [sel, setSel] = useState(null); // { x, y, sectionKey, text }
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

  /* dismiss the selection popup on outside click / Escape */
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

  /* sibling consultations for the rail */
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
    if (window.matchMedia("(max-width: 1023px)").matches)
      setMobileAssist(true);
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
      const m =
        err.response?.data?.message ||
        "La modification a échoué. Vérifiez que le LLM est connecté.";
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
      toast("Document enregistré.", "success");
    } catch {
      toast("Échec de l’enregistrement.", "error");
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
      toast("Document Word exporté.", "success");
    } catch {
      toast("Échec de l’export.", "error");
    } finally {
      setExporting(false);
    }
  };

  const rate = async (stars) => {
    setRating(stars);
    try {
      await fiscalService.rate(id, meta.reference, stars);
      toast(`Noté ${stars}★`, "success");
    } catch {
      /* non-critical */
    }
  };

  const openSource = (s) => {
    const list = (output.sources || []).map((x) => normalizeSource(x));
    setViewList(list);
    setViewing(list.find((x) => x.index === s.index) || normalizeSource(s));
    setRailOpen(false); // give the document room — Apple-style focus
  };

  const switchConsultation = (cid) => {
    if (cid === id) return;
    if (
      !saved &&
      !window.confirm(
        "Modifications non enregistrées — changer de document quand même ?",
      )
    )
      return;
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
        <div className="h-12 shrink-0 flex items-center gap-2 px-3 border-b border-border/70 bg-white/70 backdrop-blur">
          <button
            onClick={() =>
              setRailOpen((o) => {
                // rail and source pane are mutually exclusive — the document
                // keeps a comfortable width
                if (!o) setViewing(null);
                return !o;
              })
            }
            className="hidden md:flex w-8 h-8 rounded-lg text-muted hover:text-dark hover:bg-light items-center justify-center transition-colors shrink-0"
            aria-label={railOpen ? "Masquer la liste" : "Afficher la liste"}
            title={railOpen ? "Masquer la liste" : "Afficher la liste"}
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
            title="Toutes les consultations"
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

          {/* the title owns the flexible space — chip & stars yield when the
             source pane narrows the column */}
          <div className="flex-1 min-w-0 flex items-center gap-2">
            <p className="text-[13px] font-bold text-dark truncate">
              {meta?.clientName || "Ouverture…"}
            </p>
            {meta && (
              <span
                className={`text-[9px] font-bold text-dark bg-brand/30 rounded-full px-2 py-0.5 shrink-0 ${viewing ? "hidden 2xl:inline" : "hidden sm:inline"}`}
              >
                {meta.reference}
              </span>
            )}
            <span
              className={`w-1.5 h-1.5 rounded-full shrink-0 ${saved ? "bg-green-500" : "bg-amber-500 animate-pulse"}`}
              title={saved ? "Enregistré" : "Modifications non enregistrées"}
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
                title={`Noter ${n}/5`}
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
            {saved ? "Enregistré" : "Enregistrer"}
          </button>
          <button
            onClick={exportDocx}
            disabled={exporting || !ready}
            className="shrink-0 text-[12px] font-bold rounded-lg px-3 py-1.5 bg-brand text-dark hover:shadow-md hover:shadow-brand/40 disabled:opacity-50 transition-all flex items-center gap-1.5"
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
            className={`shrink-0 w-8 h-8 rounded-lg flex items-center justify-center transition-colors ${assistOpen ? "bg-dark text-brand" : "text-muted hover:text-dark hover:bg-light"}`}
            aria-label="Assistant IA"
            title="Co-rédacteur IA"
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

        {/* document scroll area */}
        <div className="flex-1 overflow-y-auto">
          {!ready ? (
            <div className="h-full flex items-center justify-center gap-3 text-muted">
              <span className="w-5 h-5 border-2 border-brand border-t-transparent rounded-full animate-spin" />
              Ouverture du document…
            </div>
          ) : (
            <div className="max-w-[860px] mx-auto px-4 sm:px-6 py-6 animate-fade-up">
              <div className="bg-white border border-border rounded-2xl shadow-sm relative overflow-hidden">
                <div className="absolute top-0 left-0 w-full h-1 bg-brand" />
                <div className="px-7 sm:px-12 py-9 sm:py-11">
                  {/* doc letterhead */}
                  <p className="text-[11px] font-bold text-muted uppercase tracking-[0.25em]">
                    EY | Taxmind — Consultation fiscale
                  </p>
                  <h2 className="font-display text-[34px] text-dark mt-3 leading-tight">
                    {meta.clientName}
                  </h2>
                  <p className="text-[13px] text-muted mt-2">
                    {meta.reference}
                    {created ? ` · ${created}` : ""}
                  </p>
                  <div className="mt-4 bg-sand/80 border border-border rounded-xl px-4 py-3">
                    <p className="text-[11px] font-bold text-muted uppercase tracking-wider mb-1">
                      Question traitée
                    </p>
                    <p className="text-[13.5px] text-dark leading-relaxed">
                      {meta.fiscalQuestion}
                    </p>
                  </div>

                  {/* sections */}
                  <div className="mt-10 space-y-9">
                    {SECTIONS.map((s, i) => (
                      <section
                        key={s.key}
                        ref={(el) => {
                          sectionRefs.current[s.key] = el;
                        }}
                      >
                        <div className="flex items-center justify-between gap-3 mb-2.5 group">
                          <h3 className="flex items-center gap-2.5 text-[13px] font-extrabold text-dark uppercase tracking-[0.14em]">
                            <span className="w-6 h-6 rounded-lg bg-brand/30 text-dark text-[11px] font-extrabold flex items-center justify-center">
                              {i + 1}
                            </span>
                            {s.label}
                          </h3>
                          <button
                            onClick={() => {
                              setTarget(s.key);
                              openAssistant();
                            }}
                            title={`Modifier « ${s.label} » avec l’IA`}
                            className={`flex items-center gap-1.5 text-[11px] font-bold rounded-lg px-2.5 py-1.5 transition-all ${target === s.key && assistOpen ? "bg-dark text-brand" : "text-muted opacity-0 group-hover:opacity-100 hover:bg-light hover:text-dark"}`}
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
                                  <tr className="bg-dark text-white">
                                    <th className="text-left px-3.5 py-2.5 font-semibold">
                                      Sujet
                                    </th>
                                    <th className="text-left px-3.5 py-2.5 font-semibold">
                                      Analyse
                                    </th>
                                    <th className="text-left px-3.5 py-2.5 font-semibold">
                                      Conclusion
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
                    ))}
                  </div>

                  {/* sources */}
                  {output.sources?.length > 0 && (
                    <div className="mt-12 pt-8 border-t border-border">
                      <h3 className="text-[13px] font-extrabold text-dark uppercase tracking-[0.14em] mb-4">
                        Sources juridiques ({output.sources.length})
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

      {/* selection popup — “ask AI about this passage” */}
      {sel && (
        <div
          ref={selPopupRef}
          className="fixed z-[70] w-[320px] bg-white border border-border rounded-2xl shadow-2xl shadow-dark/20 animate-pop overflow-hidden"
          style={{ left: sel.x, top: sel.y }}
        >
          <div className="px-3.5 pt-3 pb-2 bg-sand/70 border-b border-border">
            <p className="text-[10px] font-bold text-muted uppercase tracking-wider mb-1">
              Passage sélectionné ·{" "}
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
                  className="text-[11.5px] font-bold text-dark bg-brand/25 hover:bg-brand rounded-lg px-2.5 py-1.5 transition-colors"
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
                placeholder="Ou décrivez la modification…"
                className="flex-1 bg-light border border-border rounded-lg px-3 py-2 text-[12.5px] text-dark placeholder-muted focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent"
              />
              <button
                type="submit"
                disabled={!selPrompt.trim()}
                className="w-8 h-8 bg-dark text-brand rounded-lg flex items-center justify-center hover:bg-black disabled:opacity-30 transition-all shrink-0"
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
