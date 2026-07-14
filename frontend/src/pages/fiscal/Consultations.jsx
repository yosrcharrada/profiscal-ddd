import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useLanguage } from "../../context/LanguageContext";
import fiscalService from "../../services/fiscalService";
import { useToast } from "../../components/common/Toast";
import ConsultationsRail from "../../components/fiscal/ConsultationsRail";

const EASE = "ease-[cubic-bezier(.16,1,.3,1)]";

const PIPELINE_KEYS = [
  {
    label: "cons.pipeline1",
    detail: "cons.pipeline1d",
    icon: "search",
    color: "from-indigo-500 to-indigo-600",
  },
  {
    label: "cons.pipeline2",
    detail: "cons.pipeline2d",
    icon: "doc",
    color: "from-violet-500 to-purple-600",
  },
  {
    label: "cons.pipeline3",
    detail: "cons.pipeline3d",
    icon: "brain",
    color: "from-emerald-500 to-teal-600",
  },
  {
    label: "cons.pipeline4",
    detail: "cons.pipeline4d",
    icon: "scale",
    color: "from-amber-500 to-orange-500",
  },
  {
    label: "cons.pipeline5",
    detail: "cons.pipeline5d",
    icon: "check",
    color: "from-blue-500 to-cyan-600",
  },
  {
    label: "cons.pipeline6",
    detail: "cons.pipeline6d",
    icon: "sparkle",
    color: "from-brand to-[#F59E0B]",
  },
];

const STEP_ICONS = {
  search: (
    <path
      strokeLinecap="round"
      strokeLinejoin="round"
      d="M21 21l-5.197-5.197m0 0A7.5 7.5 0 105.196 5.196a7.5 7.5 0 0010.607 10.607z"
    />
  ),
  doc: (
    <path
      strokeLinecap="round"
      strokeLinejoin="round"
      d="M19.5 14.25v-2.625a3.375 3.375 0 00-3.375-3.375h-1.5A1.125 1.125 0 0113.5 7.125v-1.5a3.375 3.375 0 00-3.375-3.375H8.25m2.25 0H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 00-9-9z"
    />
  ),
  brain: (
    <path
      strokeLinecap="round"
      strokeLinejoin="round"
      d="M9.813 15.904L9 18.75l-.813-2.846a4.5 4.5 0 00-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 003.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 003.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 00-3.09 3.09z"
    />
  ),
  scale: (
    <path
      strokeLinecap="round"
      strokeLinejoin="round"
      d="M12 3v17.25m0 0c-1.472 0-2.882.265-4.185.75M12 20.25c1.472 0 2.882.265 4.185.75M18.75 4.97A48.416 48.416 0 0012 4.5c-2.291 0-4.545.16-6.75.47m13.5 0c1.01.143 2.01.317 3 .52m-3-.52l2.62 10.726c.122.499-.106 1.028-.589 1.202a5.988 5.988 0 01-2.031.352 5.988 5.988 0 01-2.031-.352c-.483-.174-.711-.703-.59-1.202L18.75 4.971zm-16.5.52c.99-.203 1.99-.377 3-.52m0 0l2.62 10.726c.122.499-.106 1.028-.589 1.202a5.989 5.989 0 01-2.031.352 5.989 5.989 0 01-2.031-.352c-.483-.174-.711-.703-.59-1.202L5.25 4.971z"
    />
  ),
  check: (
    <path
      strokeLinecap="round"
      strokeLinejoin="round"
      d="M9 12.75L11.25 15 15 9.75M21 12c0 1.268-.63 2.39-1.593 3.068a3.745 3.745 0 01-1.043 3.296 3.745 3.745 0 01-3.296 1.043A3.745 3.745 0 0112 21c-1.268 0-2.39-.63-3.068-1.593a3.746 3.746 0 01-3.296-1.043 3.745 3.745 0 01-1.043-3.296A3.745 3.745 0 013 12c0-1.268.63-2.39 1.593-3.068a3.745 3.745 0 011.043-3.296 3.746 3.746 0 013.296-1.043A3.746 3.746 0 0112 3c1.268 0 2.39.63 3.068 1.593a3.746 3.746 0 013.296 1.043 3.746 3.746 0 011.043 3.296A3.745 3.745 0 0121 12z"
    />
  ),
  sparkle: (
    <path
      strokeLinecap="round"
      strokeLinejoin="round"
      d="M9.813 15.904L9 18.75l-.813-2.846a4.5 4.5 0 00-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 003.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 003.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 00-3.09 3.09zM18.259 8.715L18 9.75l-.259-1.035a3.375 3.375 0 00-2.455-2.456L14.25 6l1.036-.259a3.375 3.375 0 002.455-2.456L18 2.25l.259 1.035a3.375 3.375 0 002.456 2.456L21.75 6l-1.035.259a3.375 3.375 0 00-2.456 2.456z"
    />
  ),
};

function GeneratingView({ done, clientName, t }) {
  const [step, setStep] = useState(0);
  useEffect(() => {
    if (done) {
      setStep(PIPELINE_KEYS.length);
      return;
    }
    const timers = [2500, 9000, 16000, 24000, 38000].map((ms, i) =>
      setTimeout(() => setStep(i + 1), ms),
    );
    return () => timers.forEach(clearTimeout);
  }, [done]);
  const pct = Math.min(100, Math.round((step / PIPELINE_KEYS.length) * 100));

  return (
    <div className="h-full flex items-center justify-center px-6">
      <div className="max-w-2xl w-full animate-fade-up">
        <div className="text-center mb-10">
          <div className="relative inline-flex items-center justify-center w-20 h-20 mb-6">
            <div className="absolute inset-0 rounded-2xl bg-gradient-to-br from-brand to-[#F59E0B] animate-pulse opacity-30" />
            <div className="relative w-16 h-16 rounded-2xl bg-gradient-to-br from-dark to-[#3a3a4a] flex items-center justify-center shadow-2xl">
              <svg
                className="w-8 h-8 text-brand animate-spin"
                style={{ animationDuration: "3s" }}
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                strokeWidth={1.5}
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  d="M9.813 15.904L9 18.75l-.813-2.846a4.5 4.5 0 00-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 003.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 003.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 00-3.09 3.09zM18.259 8.715L18 9.75l-.259-1.035a3.375 3.375 0 00-2.455-2.456L14.25 6l1.036-.259a3.375 3.375 0 002.455-2.456L18 2.25l.259 1.035a3.375 3.375 0 002.456 2.456L21.75 6l-1.035.259a3.375 3.375 0 00-2.456 2.456z"
                />
              </svg>
            </div>
          </div>
          <h2 className="font-display text-3xl text-dark mb-2">
            {t("cons.generating")}
            {clientName ? ` — ${clientName}` : ""}
          </h2>
          <p className="text-muted text-sm">{t("cons.generatingHint")}</p>
        </div>

        <div className="relative mb-8">
          <div className="h-2 bg-light rounded-full overflow-hidden">
            <div
              className="h-full bg-gradient-to-r from-brand via-[#F59E0B] to-emerald-400 rounded-full transition-all duration-1000 ease-out"
              style={{ width: `${Math.max(4, pct)}%` }}
            />
          </div>
          <div className="flex justify-between mt-2">
            <span className="text-[11px] font-bold text-muted">{pct}%</span>
            <span className="text-[11px] font-bold text-muted">
              {step}/{PIPELINE_KEYS.length}
            </span>
          </div>
        </div>

        <div className="grid grid-cols-2 sm:grid-cols-3 gap-3">
          {PIPELINE_KEYS.map((s, i) => {
            const state = i < step ? "done" : i === step ? "active" : "todo";
            return (
              <div
                key={s.label}
                className={`relative rounded-xl border p-4 transition-all duration-500 ${
                  state === "done"
                    ? "bg-white border-emerald-200 shadow-sm"
                    : state === "active"
                      ? "bg-white border-brand shadow-lg shadow-brand/10 scale-[1.02]"
                      : "bg-light/50 border-border opacity-50"
                }`}
                style={{ animationDelay: `${i * 100}ms` }}
              >
                <div
                  className={`w-9 h-9 rounded-xl flex items-center justify-center mb-3 transition-all ${
                    state === "done"
                      ? "bg-emerald-100 text-emerald-600"
                      : state === "active"
                        ? `bg-gradient-to-br ${s.color} text-white shadow-md`
                        : "bg-light text-muted"
                  }`}
                >
                  {state === "done" ? (
                    <svg
                      className="w-4.5 h-4.5"
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
                  ) : state === "active" ? (
                    <svg
                      className="w-4.5 h-4.5 animate-pulse"
                      fill="none"
                      viewBox="0 0 24 24"
                      stroke="currentColor"
                      strokeWidth={1.8}
                    >
                      {STEP_ICONS[s.icon]}
                    </svg>
                  ) : (
                    <svg
                      className="w-4 h-4"
                      fill="none"
                      viewBox="0 0 24 24"
                      stroke="currentColor"
                      strokeWidth={1.8}
                    >
                      {STEP_ICONS[s.icon]}
                    </svg>
                  )}
                </div>
                <p
                  className={`text-[12px] font-bold transition-colors ${state === "todo" ? "text-muted" : "text-dark"}`}
                >
                  {t(s.label)}
                </p>
                <p className="text-[10px] text-muted mt-0.5 line-clamp-2">
                  {t(s.detail)}
                </p>
                {state === "active" && (
                  <div className="absolute top-2 right-2">
                    <span className="flex h-2.5 w-2.5">
                      <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-brand opacity-75" />
                      <span className="relative inline-flex rounded-full h-2.5 w-2.5 bg-brand" />
                    </span>
                  </div>
                )}
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}

const SITUATION_TIP_KEYS = ["cons.tip1", "cons.tip2", "cons.tip3", "cons.tip4"];
const QUESTION_EXAMPLE_KEYS = [
  "cons.example1",
  "cons.example2",
  "cons.example3",
];

function FieldCheck({ ok, children }) {
  return (
    <p
      className={`text-xs mt-1.5 transition-colors ${ok ? "text-emerald-600 font-semibold" : "text-muted"}`}
    >
      {children}
    </p>
  );
}

export default function Consultations() {
  const navigate = useNavigate();
  const { t } = useLanguage();
  const { toast } = useToast();
  const year = new Date().getFullYear();
  const [items, setItems] = useState(null);
  const [form, setForm] = useState({
    clientName: "",
    reference: "",
    situation: "",
    fiscalQuestion: "",
    mode: "detaillee",
  });
  const [generating, setGenerating] = useState(false);
  const [done, setDone] = useState(false);
  const [error, setError] = useState("");
  const [railOpen, setRailOpen] = useState(
    () => localStorage.getItem("taxmind.consultations.rail") !== "0",
  );
  const [mobileRail, setMobileRail] = useState(false);
  const set = (k) => (e) => setForm((f) => ({ ...f, [k]: e.target.value }));

  useEffect(() => {
    localStorage.setItem("taxmind.consultations.rail", railOpen ? "1" : "0");
  }, [railOpen]);

  useEffect(() => {
    fiscalService
      .list("")
      .then(({ data }) => setItems(data.data))
      .catch(() => setItems([]));
  }, []);

  const valid = {
    client: form.clientName.trim().length > 0,
    situation: form.situation.trim().length >= 20,
    question: form.fiscalQuestion.trim().length >= 10,
  };
  const allValid = valid.client && valid.situation && valid.question;
  const dirty =
    form.clientName || form.reference || form.situation || form.fiscalQuestion;

  const submit = async (e) => {
    e?.preventDefault();
    if (!allValid || generating) return;
    setGenerating(true);
    setError("");
    try {
      const body = {
        ...form,
        reference:
          form.reference.trim() ||
          `CONS-${year}-${String(Date.now()).slice(-4)}`,
      };
      const { data } = await fiscalService.generate(body);
      setDone(true);
      toast(t("cons.generated"), "success");
      setTimeout(
        () => navigate(`/app/consultations/${data.data.consultationId}`),
        700,
      );
    } catch (err) {
      setGenerating(false);
      const msg = err.response?.data?.message || t("cons.generateFailed");
      setError(msg);
      toast(msg, "error", 6000);
    }
  };

  const openConsultation = (id) => {
    if (generating) {
      if (!window.confirm(t("cons.confirmLeave"))) return;
    } else if (dirty && !window.confirm(t("cons.confirmDiscard"))) {
      return;
    }
    navigate(`/app/consultations/${id}`);
  };

  const rail = (closeFn) => (
    <ConsultationsRail
      items={items}
      activeId={null}
      onSelect={openConsultation}
      onNew={() => setMobileRail(false)}
      newActive
      onClose={closeFn}
    />
  );

  return (
    <div className="h-full flex bg-sand">
      {/* ① previous consultations — collapsible rail */}
      <aside
        className={`hidden md:block shrink-0 overflow-hidden transition-[width] duration-300 ${EASE} ${railOpen ? "w-[270px] border-r border-border" : "w-0"}`}
      >
        <div className="w-[270px] h-full">{rail()}</div>
      </aside>

      {/* ① rail — mobile overlay */}
      {mobileRail && (
        <div className="md:hidden fixed inset-0 z-[70]">
          <div
            className="absolute inset-0 bg-dark/40 backdrop-blur-[2px] animate-fade-in"
            onClick={() => setMobileRail(false)}
          />
          <div className="absolute left-0 top-0 h-full w-[290px] shadow-2xl animate-slide-in-left">
            {rail(() => setMobileRail(false))}
          </div>
        </div>
      )}

      {/* ② intake / pipeline */}
      <section className="flex-1 min-w-0 flex flex-col bg-white">
        {/* toolbar */}
        <div className="h-12 shrink-0 flex items-center gap-2 px-3 border-b border-border/70 bg-white backdrop-blur">
          <button
            onClick={() => setRailOpen((o) => !o)}
            className="hidden md:flex w-8 h-8 rounded-lg text-muted hover:text-dark hover:bg-light items-center justify-center transition-colors"
            aria-label={railOpen ? t("cons.hideList") : t("cons.showList")}
            title={railOpen ? t("cons.hideList") : t("cons.showList")}
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
            onClick={() => setMobileRail(true)}
            className="md:hidden flex w-8 h-8 rounded-lg text-muted hover:text-dark hover:bg-light items-center justify-center transition-colors"
            aria-label={t("cons.previousConsultations")}
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
          <p className="flex-1 min-w-0 text-[13px] font-bold text-dark truncate">
            {t("cons.newConsultation")}
          </p>
          {items && (
            <p className="text-[12px] text-muted shrink-0">
              <span className="font-bold text-dark">{items.length}</span>{" "}
              {t("cons.memosGenerated")}
            </p>
          )}
        </div>

        {generating ? (
          <GeneratingView done={done} clientName={form.clientName} t={t} />
        ) : (
          <div className="flex-1 flex overflow-hidden">
            {/* left: form — takes full height, scrollable only if truly overflowing */}
            <div className="flex-1 min-w-0 overflow-y-auto">
              <form
                onSubmit={submit}
                className="max-w-2xl mx-auto px-4 sm:px-8 py-6 h-full flex flex-col"
              >
                {/* header */}
                <div className="animate-fade-up mb-6">
                  <div className="flex items-center gap-3 mb-2">
                    <div>
                      <h1 className="font-display text-4xl text-dark leading-tight">
                        {t("cons.describeCase")}
                      </h1>
                      <p className="text-muted text-sm">
                        {t("cons.describeHint")}
                      </p>
                    </div>
                  </div>
                </div>

                {/* compact form grid */}
                <div className="flex-1 space-y-5">
                  {/* row 1: client + ref */}
                  <div
                    className="grid sm:grid-cols-2 gap-3 animate-fade-up opacity-0"
                    style={{ animationDelay: "60ms" }}
                  >
                    <div>
                      <label className="flex items-center gap-2 text-sm font-bold text-dark mb-1.5">
                        <span className="w-5 h-5 rounded-md bg-indigo-100 text-indigo-600 text-[9px] font-extrabold flex items-center justify-center">
                          1
                        </span>
                        {t("cons.clientName")}
                      </label>
                      <input
                        autoFocus
                        value={form.clientName}
                        onChange={set("clientName")}
                        placeholder={t("cons.clientPlaceholder")}
                        className="w-full px-3.5 py-2.5 rounded-xl border border-border bg-white text-[14px] focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent transition-all"
                      />
                    </div>
                    <div>
                      <label className="flex items-center gap-2 text-sm font-bold text-dark mb-1.5">
                        <span className="w-5 h-5 rounded-md bg-indigo-100 text-indigo-600 text-[9px] font-extrabold flex items-center justify-center">
                          #
                        </span>
                        {t("cons.reference")}{" "}
                        <span className="text-muted font-normal text-xs">
                          ({t("cons.optional")})
                        </span>
                      </label>
                      <input
                        value={form.reference}
                        onChange={set("reference")}
                        placeholder={`CONS-${year}-001`}
                        className="w-full px-3.5 py-2.5 rounded-xl border border-border bg-white text-[14px] focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent transition-all"
                      />
                    </div>
                  </div>

                  {/* row 2: situation */}
                  <div
                    className="animate-fade-up opacity-0"
                    style={{ animationDelay: "120ms" }}
                  >
                    <label className="flex items-center gap-2 text-sm font-bold text-dark mb-1.5">
                      <span className="w-5 h-5 rounded-md bg-emerald-100 text-emerald-600 text-[9px] font-extrabold flex items-center justify-center">
                        2
                      </span>
                      {t("cons.factualContext")}
                    </label>
                    <textarea
                      value={form.situation}
                      onChange={set("situation")}
                      rows={2}
                      placeholder={t("cons.situationPlaceholder")}
                      className="w-full px-3.5 py-3 rounded-xl border border-border bg-white text-[14px] leading-normal focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent resize-none transition-all"
                    />
                    <FieldCheck ok={valid.situation}>
                      {form.situation.trim().length} {t("cons.characters")}{" "}
                      {valid.situation ? "✓" : `(${t("cons.min20")})`}
                    </FieldCheck>
                  </div>

                  {/* row 3: question */}
                  <div
                    className="animate-fade-up opacity-0"
                    style={{ animationDelay: "180ms" }}
                  >
                    <label className="flex items-center gap-2 text-sm font-bold text-dark mb-1.5">
                      <span className="w-5 h-5 rounded-md bg-purple-100 text-purple-600 text-[9px] font-extrabold flex items-center justify-center">
                        3
                      </span>
                      {t("cons.fiscalQuestion")}
                    </label>
                    <textarea
                      value={form.fiscalQuestion}
                      onChange={set("fiscalQuestion")}
                      rows={2}
                      placeholder={t("cons.questionPlaceholder")}
                      className="w-full px-3.5 py-3 rounded-xl border border-border bg-white text-[14px] leading-normal focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent resize-none transition-all"
                    />
                    <FieldCheck ok={valid.question}>
                      {form.fiscalQuestion.trim().length} {t("cons.characters")}{" "}
                      {valid.question ? "✓" : `(${t("cons.min10")})`}
                    </FieldCheck>
                  </div>

                  {/* row 4: mode + submit */}
                  <div
                    className="animate-fade-up opacity-0"
                    style={{ animationDelay: "210ms" }}
                  >
                    <label className="flex items-center gap-2 text-sm font-bold text-dark mb-2">
                      <span className="w-5 h-5 rounded-md bg-amber-100 text-amber-600 text-[9px] font-extrabold flex items-center justify-center">
                        4
                      </span>
                      {t("cons.consultationFormat")}
                    </label>
                    <div className="grid sm:grid-cols-2 gap-3">
                      {[
                        {
                          v: "detaillee",
                          label: t("cons.detailed"),
                          d: t("cons.detailedDesc"),
                          accent: "border-yellow-400 bg-yellow-50/5 ",
                        },
                        {
                          v: "concise",
                          label: t("cons.concise"),
                          d: t("cons.conciseDesc"),
                          accent: "border-emerald-400 bg-emerald-50/5",
                        },
                      ].map((o) => {
                        const active = form.mode === o.v;
                        return (
                          <button
                            key={o.v}
                            type="button"
                            onClick={() =>
                              setForm((f) => ({ ...f, mode: o.v }))
                            }
                            className={`text-left rounded-xl border p-3 transition-all ${
                              active
                                ? `${o.accent} `
                                : "border-border bg-white hover:border-dark/30"
                            }`}
                          >
                            <span className="flex items-center gap-2">
                              <span
                                className={`w-4 h-4 rounded-full border-2 flex items-center justify-center ${active ? "border-dark" : "border-muted"}`}
                              >
                                {active && (
                                  <span className="w-2 h-2 rounded-full bg-dark" />
                                )}
                              </span>
                              <span className="text-[13px] font-bold text-dark">
                                {o.label}
                              </span>
                            </span>
                            <span className="block text-[11px] text-muted mt-1 pl-6">
                              {o.d}
                            </span>
                          </button>
                        );
                      })}
                    </div>
                  </div>

                  {error && (
                    <div className="p-3.5 bg-red-50 border border-red-200 rounded-xl text-red-600 text-sm animate-pop opacity-0">
                      {error}
                    </div>
                  )}

                  {/* submit */}
                  <div
                    className="animate-fade-up opacity-0 pb-4"
                    style={{ animationDelay: "240ms" }}
                  >
                    <button
                      type="submit"
                      disabled={!allValid}
                      className="w-full sm:w-auto flex items-center justify-center gap-2 text-sm font-bold rounded-lg px-3 py-2 bg-brand text-dark hover:shadow-xl hover:shadow-brand/30 hover:-translate-y-0.5 active:scale-[0.98] disabled:shadow-none disabled:translate-y-0 transition-all"
                    >
                      <svg
                        className="w-4 4"
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
                      {t("cons.generate")}
                    </button>
                  </div>
                </div>
              </form>
            </div>

            {/* right: preview & tips sidebar */}
            <div className="hidden lg:block w-[300px] shrink-0 border-l border-border/70 overflow-y-auto p-5">
              <div
                className="sticky top-0 space-y-4 animate-fade-up opacity-0"
                style={{ animationDelay: "150ms" }}
              >
                {/* mini doc preview */}
                <div className="bg-white border border-border rounded-2xl overflow-hidden shadow-sm">
                  <div className="h-1.5 bg-gradient-to-r from-brand via-[#F59E0B] to-emerald-400" />
                  <div className="p-4">
                    <p className="text-[10px] font-bold text-muted uppercase tracking-[0.2em] mb-3">
                      {t("cons.memoPreview")}
                    </p>
                    <p className="font-extrabold text-dark text-[14px] truncate">
                      {form.clientName || "Client…"}
                    </p>
                    <p className="text-[11px] text-muted mb-3 truncate">
                      {form.reference || `CONS-${year}-···`}
                    </p>
                    <div className="space-y-2">
                      {[
                        {
                          s: t("cons.previewContext"),
                          color: "bg-indigo-100 text-indigo-600",
                        },
                        {
                          s: t("cons.previewScope"),
                          color: "bg-violet-100 text-violet-600",
                        },
                        {
                          s: t("cons.previewSummary"),
                          color: "bg-emerald-100 text-emerald-600",
                        },
                        {
                          s: t("cons.previewAnalysis"),
                          color: "bg-amber-100 text-amber-600",
                        },
                        {
                          s: t("cons.previewDocs"),
                          color: "bg-blue-100 text-blue-600",
                        },
                      ].map((item, i) => (
                        <div key={item.s} className="flex items-center gap-2.5">
                          <span
                            className={`w-5 h-5 rounded-md ${item.color} text-[9px] font-extrabold flex items-center justify-center shrink-0`}
                          >
                            {i + 1}
                          </span>
                          <span className="text-[12px] font-semibold text-body">
                            {item.s}
                          </span>
                        </div>
                      ))}
                    </div>
                    <p className="text-[10px] text-muted mt-4 pt-3 border-t border-border">
                      {t("cons.previewFooter")}
                    </p>
                  </div>
                </div>

                {/* tips */}
                <div className="bg-gradient-to-br from-brand/10 to-violet-400/10 border border-brand/30 rounded-2xl p-4">
                  <p className="text-[11px] font-bold text-dark uppercase tracking-[0.15em] mb-2.5 flex items-center gap-1.5">
                    <svg
                      className="w-3.5 h-3.5 text-brand"
                      fill="none"
                      viewBox="0 0 24 24"
                      stroke="currentColor"
                      strokeWidth={2}
                    >
                      <path
                        strokeLinecap="round"
                        strokeLinejoin="round"
                        d="M12 18v-5.25m0 0a6.01 6.01 0 001.5-.189m-1.5.189a6.01 6.01 0 01-1.5-.189m3.75 7.478a12.06 12.06 0 01-4.5 0m3.75 2.383a14.406 14.406 0 01-3 0M14.25 18v-.192c0-.983.658-1.823 1.508-2.316a7.5 7.5 0 10-7.517 0c.85.493 1.509 1.333 1.509 2.316V18"
                      />
                    </svg>
                    {t("cons.includeTips")}
                  </p>
                  <ul className="space-y-1.5">
                    {SITUATION_TIP_KEYS.map((key) => (
                      <li
                        key={key}
                        className="text-[12px] text-body flex gap-2"
                      >
                        <span className="text-brand font-bold mt-0.5">•</span>
                        {t(key)}
                      </li>
                    ))}
                  </ul>
                </div>
              </div>
            </div>
          </div>
        )}
      </section>
    </div>
  );
}
