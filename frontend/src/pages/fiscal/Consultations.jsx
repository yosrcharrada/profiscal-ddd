import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import fiscalService from "../../services/fiscalService";
import { useToast } from "../../components/common/Toast";
import ConsultationsRail from "../../components/fiscal/ConsultationsRail";

const EASE = "ease-[cubic-bezier(.16,1,.3,1)]";

const PIPELINE = [
  {
    label: "Détection des branches & de la juridiction",
    detail: "IS · IRPP · TVA · Retenue · Prix de transfert",
  },
  {
    label: "Recherche sémantique du corpus",
    detail: "Embeddings multilingues sur 67k passages",
  },
  {
    label: "Récupération des sources (Neo4j)",
    detail: "Conventions → codes → lois de finances → doctrine",
  },
  { label: "Rédaction du contexte, étendue & sommaire", detail: "Phase 1" },
  {
    label: "Analyses & tableau de synthèse",
    detail: "Phases 2 ‖ 3 — en parallèle",
  },
  {
    label: "Résolution des citations & finalisation",
    detail: "Chaque affirmation tracée à sa source",
  },
];

/* Real pipeline phases, auto-advancing on an estimate; completes when the API returns. */
function GeneratingView({ done, clientName }) {
  const [step, setStep] = useState(0);
  useEffect(() => {
    if (done) {
      setStep(PIPELINE.length);
      return;
    }
    const timers = [2500, 9000, 16000, 24000, 38000].map((ms, i) =>
      setTimeout(() => setStep(i + 1), ms),
    );
    return () => timers.forEach(clearTimeout);
  }, [done]);
  const pct = Math.min(100, Math.round((step / PIPELINE.length) * 100));

  return (
    <div className="max-w-lg mx-auto w-full animate-fade-up">
      <div className="flex items-center gap-4 mb-2">
        <span className="w-11 h-11 rounded-2xl bg-brand flex items-center justify-center shrink-0">
          <span className="w-5 h-5 border-2 border-dark border-t-transparent rounded-full animate-spin" />
        </span>
        <div>
          <h2 className="text-lg font-extrabold text-dark">
            Rédaction de la consultation{clientName ? ` — ${clientName}` : ""}
          </h2>
          <p className="text-sm text-muted">Généralement moins d’une minute.</p>
        </div>
      </div>
      <div className="h-1.5 bg-light rounded-full overflow-hidden my-6">
        <div
          className="h-full bg-brand rounded-full transition-all duration-1000"
          style={{ width: `${Math.max(6, pct)}%` }}
        />
      </div>
      <div className="space-y-1">
        {PIPELINE.map((s, i) => {
          const state = i < step ? "done" : i === step ? "active" : "todo";
          return (
            <div key={s.label} className="flex items-start gap-3 py-2">
              <span
                className={`mt-0.5 w-5 h-5 rounded-full flex items-center justify-center shrink-0 transition-all ${state === "done" ? "bg-brand text-dark" : state === "active" ? "bg-dark text-white" : "bg-light text-muted"}`}
              >
                {state === "done" ? (
                  <svg
                    className="w-3 h-3"
                    fill="none"
                    viewBox="0 0 24 24"
                    stroke="currentColor"
                    strokeWidth={3}
                  >
                    <path
                      strokeLinecap="round"
                      strokeLinejoin="round"
                      d="M4.5 12.75l6 6 9-13.5"
                    />
                  </svg>
                ) : state === "active" ? (
                  <span className="w-1.5 h-1.5 bg-white rounded-full animate-pulse" />
                ) : (
                  <span className="w-1.5 h-1.5 bg-muted/50 rounded-full" />
                )}
              </span>
              <div>
                <p
                  className={`text-sm font-semibold transition-colors ${state === "todo" ? "text-muted" : "text-dark"}`}
                >
                  {s.label}
                </p>
                <p className="text-xs text-muted">{s.detail}</p>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

const SITUATION_TIPS = [
  "Forme juridique et résidence fiscale des parties",
  "Nature et montants des opérations concernées",
  "Pays impliqués si le dossier est international",
  "Dates et exercices fiscaux concernés",
];

const QUESTION_EXAMPLES = [
  "Quel est le traitement fiscal de ces redevances versées au prestataire français ?",
  "La société bénéficie-t-elle d’un avantage fiscal au titre de cet investissement ?",
  "Quelles obligations de retenue à la source s’appliquent ?",
];

/* Soft pastel chips marking the three parts of the intake — same family as
   the search facets: blue / green / pink next to the EY yellow. */
const PART_CHIP = {
  dossier: "bg-[#DBEAFE] text-[#1D4ED8]",
  situation: "bg-[#ECFDF5] text-[#047857]",
  question: "bg-[#FCE7F3] text-[#BE185D]",
};

function FieldCheck({ ok, children }) {
  return (
    <p
      className={`text-xs mt-1.5 transition-colors ${ok ? "text-green-600 font-semibold" : "text-muted"}`}
    >
      {children}
    </p>
  );
}

/* Create-consultation workspace — split screen like the chat:
   previous consultations as a collapsible rail, one single intake form
   (no steps), the generation pipeline inline, then the document opens. */
export default function Consultations() {
  const navigate = useNavigate();
  const { toast } = useToast();
  const year = new Date().getFullYear();
  const [items, setItems] = useState(null);
  const [form, setForm] = useState({
    clientName: "",
    reference: "",
    situation: "",
    fiscalQuestion: "",
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
      toast("Consultation générée.", "success");
      setTimeout(
        () => navigate(`/app/consultations/${data.data.consultationId}`),
        700,
      );
    } catch (err) {
      setGenerating(false);
      const msg =
        err.response?.data?.message ||
        "La génération a échoué. Vérifiez le statut du moteur — Neo4j et le LLM doivent être connectés.";
      setError(msg);
      toast(msg, "error", 6000);
    }
  };

  const openConsultation = (id) => {
    if (generating) {
      if (!window.confirm("Génération en cours — quitter cette page ?")) return;
    } else if (
      dirty &&
      !window.confirm("Le brouillon en cours sera perdu — continuer ?")
    ) {
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
      <section className="flex-1 min-w-0 flex flex-col">
        {/* toolbar */}
        <div className="h-12 shrink-0 flex items-center gap-2 px-3 border-b border-border/70 bg-white/70 backdrop-blur">
          <button
            onClick={() => setRailOpen((o) => !o)}
            className="hidden md:flex w-8 h-8 rounded-lg text-muted hover:text-dark hover:bg-light items-center justify-center transition-colors"
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
          <button
            onClick={() => setMobileRail(true)}
            className="md:hidden flex w-8 h-8 rounded-lg text-muted hover:text-dark hover:bg-light items-center justify-center transition-colors"
            aria-label="Consultations précédentes"
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
            Nouvelle consultation
          </p>
          {items && (
            <p className="text-[12px] text-muted shrink-0">
              <span className="font-bold text-dark">{items.length}</span>{" "}
              mémo{items.length !== 1 ? "s" : ""} généré
              {items.length !== 1 ? "s" : ""}
            </p>
          )}
        </div>

        <div className="flex-1 overflow-y-auto">
          {generating ? (
            <div className="min-h-full flex items-center justify-center px-6 py-10">
              <GeneratingView done={done} clientName={form.clientName} />
            </div>
          ) : (
            <div className="max-w-4xl mx-auto w-full px-4 sm:px-8 py-8 grid lg:grid-cols-[1fr,290px] gap-10">
              {/* the form — every field together, one click to generate */}
              <form onSubmit={submit} className="max-w-xl min-w-0">
                <div className="animate-fade-up">
                  <h1 className="font-display text-[32px] text-dark leading-tight">
                    Décrivez le dossier.
                  </h1>
                  <p className="text-muted mt-2 mb-8">
                    L’IA rédige un mémo structuré et sourcé sur le corpus
                    juridique — en une minute environ.
                  </p>
                </div>

                {/* le dossier */}
                <div
                  className="animate-fade-up opacity-0"
                  style={{ animationDelay: "60ms" }}
                >
                  <span
                    className={`inline-block text-[10px] font-extrabold rounded-full px-2 py-0.5 mb-2.5 ${PART_CHIP.dossier}`}
                  >
                    Le dossier
                  </span>
                  <div className="grid sm:grid-cols-2 gap-3">
                    <div>
                      <label className="block text-sm font-bold text-dark mb-1.5">
                        Nom du client
                      </label>
                      <input
                        autoFocus
                        value={form.clientName}
                        onChange={set("clientName")}
                        placeholder="Société X"
                        className="w-full px-3.5 py-2.5 rounded-xl border border-border bg-white text-[14.5px] focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent transition-all"
                      />
                    </div>
                    <div>
                      <label className="block text-sm font-bold text-dark mb-1.5">
                        Référence{" "}
                        <span className="text-muted font-normal text-xs">
                          (optionnel)
                        </span>
                      </label>
                      <input
                        value={form.reference}
                        onChange={set("reference")}
                        placeholder={`CONS-${year}-001`}
                        className="w-full px-3.5 py-2.5 rounded-xl border border-border bg-white text-[14.5px] focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent transition-all"
                      />
                    </div>
                  </div>
                </div>

                {/* la situation */}
                <div
                  className="mt-7 animate-fade-up opacity-0"
                  style={{ animationDelay: "120ms" }}
                >
                  <span
                    className={`inline-block text-[10px] font-extrabold rounded-full px-2 py-0.5 mb-2.5 ${PART_CHIP.situation}`}
                  >
                    La situation
                  </span>
                  <label className="block text-sm font-bold text-dark mb-1.5">
                    Contexte factuel
                  </label>
                  <textarea
                    value={form.situation}
                    onChange={set("situation")}
                    rows={6}
                    placeholder="Qui sont les parties, quelles opérations, quels montants, quels pays…"
                    className="w-full px-3.5 py-3 rounded-xl border border-border bg-white text-[14.5px] leading-relaxed focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent resize-none transition-all"
                  />
                  <FieldCheck ok={valid.situation}>
                    {form.situation.trim().length} caractères{" "}
                    {valid.situation ? "✓" : "(min. 20)"}
                  </FieldCheck>
                </div>

                {/* la question */}
                <div
                  className="mt-7 animate-fade-up opacity-0"
                  style={{ animationDelay: "180ms" }}
                >
                  <span
                    className={`inline-block text-[10px] font-extrabold rounded-full px-2 py-0.5 mb-2.5 ${PART_CHIP.question}`}
                  >
                    La question
                  </span>
                  <label className="block text-sm font-bold text-dark mb-1.5">
                    Question fiscale à trancher
                  </label>
                  <textarea
                    value={form.fiscalQuestion}
                    onChange={set("fiscalQuestion")}
                    rows={3}
                    placeholder="Quelle est la question fiscale à traiter ?"
                    className="w-full px-3.5 py-3 rounded-xl border border-border bg-white text-[14.5px] leading-relaxed focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent resize-none transition-all"
                  />
                  <FieldCheck ok={valid.question}>
                    {form.fiscalQuestion.trim().length} caractères{" "}
                    {valid.question ? "✓" : "(min. 10)"}
                  </FieldCheck>
                  <div className="flex flex-wrap gap-1.5 mt-3">
                    {QUESTION_EXAMPLES.map((q) => (
                      <button
                        key={q}
                        type="button"
                        onClick={() =>
                          setForm((f) => ({ ...f, fiscalQuestion: q }))
                        }
                        className="text-[12px] font-semibold text-body bg-white hover:bg-brand/10 border border-border hover:border-brand/60 hover:text-dark rounded-full px-3 py-1.5 transition-all hover:-translate-y-0.5"
                      >
                        {q.length > 52 ? `${q.slice(0, 49)}…` : q}
                      </button>
                    ))}
                  </div>
                </div>

                {error && (
                  <div className="mt-6 p-3.5 bg-red-50 border border-red-200 rounded-xl text-red-600 text-sm animate-pop opacity-0">
                    {error}
                  </div>
                )}

                {/* one click — no steps */}
                <div
                  className="mt-8 animate-fade-up opacity-0"
                  style={{ animationDelay: "240ms" }}
                >
                  <button
                    type="submit"
                    disabled={!allValid}
                    className="w-full sm:w-auto flex items-center justify-center gap-2 text-sm font-bold rounded-xl px-7 py-3.5 bg-brand text-dark hover:shadow-lg hover:shadow-brand/40 active:scale-[0.98] disabled:opacity-40 disabled:shadow-none transition-all"
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
                        d="M9.813 15.904L9 18.75l-.813-2.846a4.5 4.5 0 00-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 003.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 003.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 00-3.09 3.09z"
                      />
                    </svg>
                    Générer la consultation
                  </button>
                  {!allValid && (
                    <p className="text-[11.5px] text-muted mt-2">
                      Renseignez le client, la situation et la question pour
                      lancer la génération.
                    </p>
                  )}
                </div>
              </form>

              {/* live preview & tips */}
              <div className="hidden lg:block">
                <div className="sticky top-6 space-y-4 animate-fade-up opacity-0" style={{ animationDelay: "150ms" }}>
                  <div className="bg-white border border-border rounded-2xl p-5 relative overflow-hidden">
                    <div className="absolute top-0 left-0 w-full h-1 bg-brand" />
                    <p className="text-[10px] font-bold text-muted uppercase tracking-[0.2em] mb-3">
                      Aperçu du mémo
                    </p>
                    <p className="font-extrabold text-dark text-[15px] truncate">
                      {form.clientName || "Client…"}
                    </p>
                    <p className="text-[11px] text-muted mb-3 truncate">
                      {form.reference || `CONS-${year}-···`}
                    </p>
                    <div className="space-y-2">
                      {[
                        "Contexte & faits",
                        "Étendue des travaux",
                        "Sommaire exécutif",
                        "Analyses",
                        "Documents & références",
                      ].map((s, i) => (
                        <div key={s} className="flex items-center gap-2.5">
                          <span className="w-5 h-5 rounded-md bg-light text-muted text-[9px] font-extrabold flex items-center justify-center shrink-0">
                            {i + 1}
                          </span>
                          <span className="text-[12.5px] font-semibold text-body">
                            {s}
                          </span>
                        </div>
                      ))}
                    </div>
                    <p className="text-[11px] text-muted mt-4 pt-3 border-t border-border">
                      + tableau de synthèse et sources juridiques numérotées,
                      le tout modifiable avec l’IA après génération.
                    </p>
                  </div>
                  <div className="bg-brand/15 border border-brand/40 rounded-2xl p-5">
                    <p className="text-[11px] font-bold text-dark uppercase tracking-[0.15em] mb-2.5">
                      💡 À inclure si possible
                    </p>
                    <ul className="space-y-1.5">
                      {SITUATION_TIPS.map((t) => (
                        <li
                          key={t}
                          className="text-[12.5px] text-body flex gap-2"
                        >
                          <span className="text-dark font-bold">·</span>
                          {t}
                        </li>
                      ))}
                    </ul>
                  </div>
                </div>
              </div>
            </div>
          )}
        </div>
      </section>
    </div>
  );
}
