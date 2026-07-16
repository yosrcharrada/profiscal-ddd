import { useCallback, useEffect, useState } from "react";
import { useParams, useSearchParams } from "react-router-dom";
import { useAuth } from "../../context/AuthContext";
import { useLanguage } from "../../context/LanguageContext";
import fiscalService from "../../services/fiscalService";
import { taskService } from "../../services/authService";

/* Read-only consultation document — a clean Google-Docs-like render (no editor).
   Opened in its own browser tab from the manager Tasks table. When a ?task= id is
   provided, the manager can validate the submitted work here and, once validated,
   export the final .docx for the client. */

const SECTIONS = [
  { field: "contexteFaits", label: "Contexte & faits", num: "1", tone: "text-indigo-600 bg-indigo-100" },
  { field: "etendue", label: "Étendue des travaux", num: "2", tone: "text-violet-600 bg-violet-100" },
  { field: "sommairExecutif", label: "Sommaire exécutif", num: "3", tone: "text-emerald-600 bg-emerald-100" },
  { field: "analyses", label: "Analyses", num: "4", tone: "text-amber-600 bg-amber-100" },
  { field: "documents", label: "Documents & références", num: "5", tone: "text-blue-600 bg-blue-100" },
];

const STATUS_TONES = {
  Pending: "bg-light text-body border-border",
  InProgress: "bg-blue-50 text-blue-700 border-blue-200",
  Submitted: "bg-brand/20 text-dark border-brand/50",
  Approved: "bg-green-50 text-green-700 border-green-200",
};

export default function ConsultationView() {
  const { id } = useParams();
  const [params] = useSearchParams();
  const taskId = params.get("task");
  const { isManager } = useAuth();
  const { t } = useLanguage();

  const [meta, setMeta] = useState(null);
  const [output, setOutput] = useState(null);
  const [task, setTask] = useState(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [flash, setFlash] = useState("");

  const load = useCallback(async () => {
    try {
      const { data } = await fiscalService.get(id);
      setMeta(data.data);
      setOutput(data.data.output);
    } catch (err) {
      setError(err.response?.data?.message || t("view.loadFailed"));
    }
    if (taskId) {
      try {
        const { data: res } = await taskService.get(taskId);
        setTask(res.data);
      } catch {
        /* task chrome is optional — document still renders */
      }
    }
  }, [id, taskId, t]);

  useEffect(() => { load(); }, [load]);

  const validate = async () => {
    if (!task) return;
    setBusy(true);
    setError("");
    try {
      const { data: res } = await taskService.approve(task.id);
      setTask(res.data);
      setFlash(t("view.validated"));
      setTimeout(() => setFlash(""), 3500);
    } catch (err) {
      setError(err.response?.data?.message || t("view.validateFailed"));
    } finally {
      setBusy(false);
    }
  };

  // With a linked task: export unlocks once the manager has validated the work.
  const canExport = !task || task.status === "Approved";

  const exportDocx = async () => {
    if (!canExport || !meta || !output) return;
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
    } catch {
      setError(t("view.exportFailed"));
    } finally {
      setExporting(false);
    }
  };

  const created = meta?.createdAt
    ? new Date(meta.createdAt).toLocaleDateString(undefined, { day: "numeric", month: "long", year: "numeric" })
    : "";

  return (
    <div className="min-h-screen bg-[#ececec] dark:bg-sand flex flex-col">
      {/* top bar */}
      <header className="sticky top-0 z-20 h-14 shrink-0 flex items-center gap-3 px-4 sm:px-6 bg-white dark:bg-cream border-b border-border shadow-sm">
        <span className="flex items-center gap-2 shrink-0">
          <svg width="20" height="7" viewBox="0 0 26 8" aria-hidden="true">
            <polygon points="0,8 26,0 26,8" fill="#FFE600" />
          </svg>
          <span className="text-[12px] font-bold text-dark tracking-[0.2em] uppercase hidden sm:inline">EY | Taxmind</span>
        </span>
        <span className="h-5 w-px bg-border" />
        <div className="min-w-0 flex-1 flex items-center gap-2">
          <p className="text-[13.5px] font-bold text-dark truncate">
            {meta?.clientName || t("view.opening")}
          </p>
          {meta && (
            <span className="hidden sm:inline text-[9px] font-bold text-dark bg-brand/30 rounded-full px-2 py-0.5 shrink-0">
              {meta.reference}
            </span>
          )}
          {task && (
            <span className={`text-[10px] font-semibold border rounded-md px-2 py-0.5 shrink-0 ${STATUS_TONES[task.status] || ""}`}>
              {t(`tasks.status.${task.status}`)}
            </span>
          )}
        </div>

        {flash && (
          <span className="text-[12px] font-semibold text-green-700 bg-green-50 border border-green-200 rounded-lg px-2.5 py-1 animate-fade-in hidden md:inline">
            ✓ {flash}
          </span>
        )}

        {task && task.status === "Submitted" && isManager && (
          <button
            onClick={validate}
            disabled={busy}
            className="shrink-0 flex items-center gap-1.5 text-[12.5px] font-bold text-white bg-green-600 rounded-lg px-4 py-2 hover:bg-green-700 hover:shadow-lg hover:shadow-green-600/30 disabled:opacity-50 transition-all"
          >
            {busy ? (
              <span className="w-3.5 h-3.5 border-2 border-white border-t-transparent rounded-full animate-spin" />
            ) : (
              <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2.5}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M4.5 12.75l6 6 9-13.5" />
              </svg>
            )}
            {t("view.validate")}
          </button>
        )}

        <div className="relative shrink-0" title={canExport ? "" : t("view.exportLocked")}>
          <button
            onClick={exportDocx}
            disabled={!canExport || exporting || !output}
            className="flex items-center gap-1.5 text-[12.5px] font-bold rounded-lg px-4 py-2 bg-gradient-to-r from-brand to-[#F59E0B] text-dark hover:shadow-lg hover:shadow-brand/40 disabled:opacity-40 disabled:shadow-none transition-all"
          >
            {exporting ? (
              <span className="w-3.5 h-3.5 border-2 border-dark border-t-transparent rounded-full animate-spin" />
            ) : canExport ? (
              <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M3 16.5v2.25A2.25 2.25 0 005.25 21h13.5A2.25 2.25 0 0021 18.75V16.5M16.5 12L12 16.5m0 0L7.5 12m4.5 4.5V3" />
              </svg>
            ) : (
              <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M16.5 10.5V6.75a4.5 4.5 0 10-9 0v3.75m-.75 11.25h10.5a2.25 2.25 0 002.25-2.25v-6.75a2.25 2.25 0 00-2.25-2.25H6.75a2.25 2.25 0 00-2.25 2.25v6.75a2.25 2.25 0 002.25 2.25z" />
              </svg>
            )}
            {t("view.exportClient")}
          </button>
        </div>
      </header>

      {/* task banner — what the manager is validating */}
      {task && task.status === "Submitted" && (
        <div className="bg-brand/15 border-b border-brand/40 px-4 sm:px-6 py-2.5 flex items-center gap-2.5 text-[12.5px]">
          <svg className="w-4 h-4 text-dark shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m9-.75a9 9 0 11-18 0 9 9 0 0118 0zm-9 3.75h.008v.008H12v-.008z" />
          </svg>
          <p className="text-dark min-w-0 truncate">
            <span className="font-bold">{task.consultant?.name}</span> {t("view.submittedBanner")}
            {task.submitNote && <span className="italic text-body"> — « {task.submitNote} »</span>}
          </p>
        </div>
      )}

      {error && (
        <div className="mx-4 sm:mx-6 mt-4 p-3 bg-red-50 border border-red-200 rounded-lg text-red-600 text-[13px]">{error}</div>
      )}

      {/* the paper */}
      <main className="flex-1 overflow-y-auto">
        {!meta || !output ? (
          !error && (
            <div className="h-64 flex items-center justify-center gap-3 text-muted">
              <span className="w-5 h-5 border-2 border-brand border-t-transparent rounded-full animate-spin" />
              {t("view.opening")}
            </div>
          )
        ) : (
          <div className="max-w-[816px] mx-auto py-8 px-4 animate-fade-up">
            <div className="bg-white shadow-xl rounded-sm min-h-[1056px] relative">
              <div className="absolute top-0 left-0 w-full h-1 bg-gradient-to-r from-brand via-[#F59E0B] to-brand" />
              <div className="px-10 sm:px-16 py-12 sm:py-14">
                {/* letterhead */}
                <div className="text-center mb-6">
                  <div className="flex items-center justify-center gap-2 mb-1">
                    <svg width="22" height="8" viewBox="0 0 26 8" aria-hidden="true">
                      <polygon points="0,8 26,0 26,8" fill="#FFE600" />
                    </svg>
                    <span className="text-[11px] font-bold text-gray-900 tracking-[0.3em] uppercase">EY | Taxmind</span>
                  </div>
                  <p className="text-[10px] text-gray-500 tracking-wider">{t("editor.expertise")}</p>
                </div>

                <div className="border-t-2 border-gray-900 mb-6" />

                <h1 className="text-center font-display text-[22px] font-extrabold text-gray-900 uppercase tracking-wider mb-2">
                  {t("editor.fiscalConsultation")}
                </h1>
                <p className="text-center text-[13px] text-gray-500 italic mb-6">
                  {t("editor.consultationSubtitle")} — {meta.fiscalQuestion?.slice(0, 80)}
                  {meta.fiscalQuestion?.length > 80 ? "…" : ""}
                </p>

                {/* metadata table */}
                <div className="border border-gray-200 rounded-lg overflow-hidden mb-8">
                  <table className="w-full text-[13px]">
                    <tbody>
                      {[
                        [t("editor.reference"), meta.reference],
                        [t("editor.date"), created],
                        [t("editor.recipient"), meta.clientName],
                      ].map(([label, val]) => (
                        <tr key={label} className="border-b border-gray-200 last:border-b-0">
                          <td className="px-4 py-2.5 bg-gray-50 font-bold text-gray-900 w-[160px] border-r border-gray-200">{label}</td>
                          <td className="px-4 py-2.5 text-gray-700">{val}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>

                {/* sections — pure read-only text */}
                <div className="space-y-8">
                  {SECTIONS.map((s) => (
                    <section key={s.field}>
                      <h3 className="flex items-center gap-2.5 mb-3">
                        <span className={`w-7 h-7 rounded-lg ${s.tone} text-[12px] font-extrabold flex items-center justify-center`}>
                          {s.num}
                        </span>
                        <span className="text-[14px] font-extrabold text-gray-900 uppercase tracking-[0.08em]">{s.label}</span>
                      </h3>
                      <div className="text-[14px] text-gray-800 leading-[1.9] whitespace-pre-wrap">
                        {output[s.field] || <span className="text-gray-400 italic">—</span>}
                      </div>
                    </section>
                  ))}
                </div>

                {/* sources */}
                {output.sources?.length > 0 && (
                  <div className="mt-10 pt-6 border-t-2 border-gray-900">
                    <h3 className="flex items-center gap-2 text-[14px] font-extrabold text-gray-900 uppercase tracking-[0.08em] mb-4">
                      <span className="w-7 h-7 rounded-lg bg-rose-100 text-rose-600 text-[12px] font-extrabold flex items-center justify-center">S</span>
                      {t("editor.legalSources")} ({output.sources.length})
                    </h3>
                    <ol className="space-y-1.5">
                      {output.sources.map((s) => (
                        <li key={s.index} className="flex items-start gap-2.5 text-[12.5px] text-gray-700">
                          <span className="shrink-0 w-6 h-6 rounded-md bg-brand/40 text-gray-900 text-[10px] font-extrabold flex items-center justify-center">
                            S{s.index}
                          </span>
                          <span className="min-w-0 capitalize">
                            <span className="font-bold text-gray-900">{(s.docName || "").replace(/[-_]/g, " ")}{s.year ? ` (${s.year})` : ""}</span>
                            {(s.articleRef || s.sectionTitle) && <span className="text-gray-500"> — {s.articleRef || s.sectionTitle}</span>}
                          </span>
                        </li>
                      ))}
                    </ol>
                  </div>
                )}

                {/* footer */}
                <div className="mt-12 pt-4 border-t border-gray-200 text-center">
                  <p className="text-[10px] text-gray-400 tracking-wider">
                    {t("editor.confidential")} — EY Taxmind © {new Date().getFullYear()}
                  </p>
                </div>
              </div>
            </div>
          </div>
        )}
      </main>
    </div>
  );
}
