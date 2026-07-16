import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { taskService } from "../../services/authService";
import { useLanguage } from "../../context/LanguageContext";

const fmtDate = (d) =>
  d ? new Date(d).toLocaleDateString(undefined, { dateStyle: "medium" }) : "—";

function StatChip({ value, label, tone }) {
  return (
    <div className={`flex-1 rounded-lg border px-2 py-1.5 text-center ${tone}`}>
      <p className="text-[15px] font-extrabold leading-none">{value}</p>
      <p className="text-[9px] font-semibold uppercase tracking-[0.08em] mt-1 opacity-80">
        {label}
      </p>
    </div>
  );
}

const VIEW_KEY = "mgr.consultants.view";

function ViewToggle({ view, setView, t }) {
  const btn = (v, icon, label) => (
    <button
      onClick={() => setView(v)}
      title={label}
      aria-label={label}
      className={`flex items-center gap-1.5 px-3 py-2 text-[12px] font-semibold transition-colors ${view === v ? "bg-brand text-black" : "text-body hover:text-dark hover:bg-brand/5"}`}
    >
      <svg
        className="w-4 h-4"
        fill="none"
        viewBox="0 0 24 24"
        stroke="currentColor"
        strokeWidth={1.8}
      >
        {icon}
      </svg>
      <span className="hidden sm:inline">{label}</span>
    </button>
  );
  return (
    <div className="flex rounded-lg border border-border/60 bg-white overflow-hidden shrink-0">
      {btn(
        "grid",
        <path
          strokeLinecap="round"
          strokeLinejoin="round"
          d="M3.75 6A2.25 2.25 0 016 3.75h2.25A2.25 2.25 0 0110.5 6v2.25a2.25 2.25 0 01-2.25 2.25H6a2.25 2.25 0 01-2.25-2.25V6zM3.75 15.75A2.25 2.25 0 016 13.5h2.25a2.25 2.25 0 012.25 2.25V18a2.25 2.25 0 01-2.25 2.25H6A2.25 2.25 0 013.75 18v-2.25zM13.5 6a2.25 2.25 0 012.25-2.25H18A2.25 2.25 0 0120.25 6v2.25A2.25 2.25 0 0118 10.5h-2.25a2.25 2.25 0 01-2.25-2.25V6zM13.5 15.75a2.25 2.25 0 012.25-2.25H18a2.25 2.25 0 012.25 2.25V18A2.25 2.25 0 0118 20.25h-2.25A2.25 2.25 0 0113.5 18v-2.25z"
        />,
      )}
      {btn(
        "list",
        <path
          strokeLinecap="round"
          strokeLinejoin="round"
          d="M3.75 6.75h16.5M3.75 12h16.5m-16.5 5.25h16.5"
        />,
      )}
    </div>
  );
}

export default function Consultants() {
  const { t } = useLanguage();
  const [team, setTeam] = useState(null);
  const [error, setError] = useState("");
  const [view, setView] = useState(
    () => localStorage.getItem(VIEW_KEY) || "grid",
  );

  useEffect(() => {
    localStorage.setItem(VIEW_KEY, view);
  }, [view]);

  useEffect(() => {
    taskService
      .consultants()
      .then(({ data: res }) => setTeam(res.data))
      .catch((err) =>
        setError(err.response?.data?.message || "Failed to load team."),
      );
  }, []);

  const totals = team?.reduce(
    (acc, c) => ({
      submitted: acc.submitted + c.tasks.submitted,
      open: acc.open + c.tasks.pending + c.tasks.inProgress,
    }),
    { submitted: 0, open: 0 },
  );

  return (
    <div className="space-y-4 animate-fade-up">
      <div className="flex flex-col sm:flex-row sm:items-end sm:justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold text-dark tracking-tight">
            {t("manager.team.title")}
          </h1>
          <p className="text-[13px] text-muted mt-0.5">
            {t("manager.team.subtitle")}
            {team && totals.submitted > 0 && (
              <span className="ml-1.5 font-bold text-dark">
                · {totals.submitted} {t("manager.team.toReview")}
              </span>
            )}
          </p>
        </div>
        <ViewToggle view={view} setView={setView} t={t} />
      </div>

      {error && (
        <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-red-600 text-[13px]">
          {error}
        </div>
      )}

      {!team && (
        <div className="grid sm:grid-cols-2 xl:grid-cols-3 gap-3 animate-pulse">
          {[...Array(3)].map((_, i) => (
            <div key={i} className="h-44 bg-light rounded-xl" />
          ))}
        </div>
      )}

      {team?.length === 0 && (
        <div className="p-12 text-center bg-white rounded-xl border border-border/60">
          <div className="w-12 h-12 mx-auto rounded-full bg-brand/15 flex items-center justify-center">
            <svg
              className="w-6 h-6 text-dark"
              fill="none"
              viewBox="0 0 24 24"
              stroke="currentColor"
              strokeWidth={1.8}
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                d="M18 7.5v3m0 0v3m0-3h3m-3 0h-3m-2.25-4.125a3.375 3.375 0 11-6.75 0 3.375 3.375 0 016.75 0zM3 19.235v-.11a6.375 6.375 0 0112.75 0v.109A12.318 12.318 0 019.374 21c-2.331 0-4.512-.645-6.374-1.766z"
              />
            </svg>
          </div>
          <p className="mt-3 text-[14px] font-bold text-dark">
            {t("manager.team.emptyTitle")}
          </p>
          <p className="mt-1 text-[12px] text-muted max-w-sm mx-auto">
            {t("manager.team.emptyHint")}
          </p>
        </div>
      )}

      {view === "grid" ? (
        <div className="grid sm:grid-cols-2 xl:grid-cols-3 gap-3">
          {team?.map((c) => (
            <Link
              key={c.id}
              to={`/manager/consultants/${c.id}`}
              state={{
                name:
                  `${c.firstName ?? ""} ${c.lastName ?? ""}`.trim() || c.email,
              }}
              className="group bg-cream rounded-xl border border-border/60 p-4 hover:border-dark/25 hover:shadow-lg hover:-translate-y-0.5 transition-all"
            >
              <div className="flex items-center gap-3">
                <span className="w-11 h-11 rounded-xl bg-brand/25 text-dark flex items-center justify-center text-[13px] font-extrabold shrink-0 group-hover:bg-brand/50 transition-colors">
                  {`${c.firstName?.[0] ?? ""}${c.lastName?.[0] ?? ""}`.toUpperCase()}
                </span>
                <div className="min-w-0 flex-1">
                  <p className="text-[14px] font-bold text-dark truncate">
                    {c.firstName} {c.lastName}
                  </p>
                  <p className="text-[11px] text-muted truncate">{c.email}</p>
                </div>
                {c.tasks.submitted > 0 && (
                  <span className="shrink-0 text-[9px] font-bold text-dark bg-brand rounded-full px-2 py-1 uppercase tracking-wide animate-pulse">
                    {c.tasks.submitted} {t("manager.team.submittedBadge")}
                  </span>
                )}
              </div>

              <div className="mt-3.5 flex gap-1.5">
                <StatChip
                  value={c.tasks.pending}
                  label={t("tasks.status.Pending")}
                  tone="bg-light/70 border-border/50 text-body"
                />
                <StatChip
                  value={c.tasks.inProgress}
                  label={t("tasks.status.InProgress")}
                  tone="bg-blue-50 border-blue-200 text-blue-700"
                />
                <StatChip
                  value={c.tasks.submitted}
                  label={t("tasks.status.Submitted")}
                  tone="bg-brand/15 border-brand/40 text-dark"
                />
                <StatChip
                  value={c.tasks.approved}
                  label={t("tasks.status.Approved")}
                  tone="bg-green-50 border-green-200 text-green-700"
                />
              </div>

              <div className="mt-3 pt-3 border-t border-border/40 flex items-center justify-between text-[11px] text-muted">
                <span>
                  {c.consultations} {t("manager.team.consultations")}
                </span>
                <span>
                  {t("manager.team.lastLogin")} {fmtDate(c.lastLoginAt)}
                </span>
              </div>
            </Link>
          ))}
        </div>
      ) : (
        /* list view — one compact row per consultant */
        team &&
        team.length > 0 && (
          <div className="bg-white rounded-xl border border-border/60 overflow-hidden">
            <div className="overflow-x-auto">
              <table className="w-full text-[13px]">
                <thead>
                  <tr className="bg-light/70 text-[10px] font-bold text-muted uppercase tracking-[0.1em]">
                    <th className="text-left px-4 py-2.5">
                      {t("manager.team.colConsultant")}
                    </th>
                    <th className="text-center px-3 py-2.5">
                      {t("tasks.status.Pending")}
                    </th>
                    <th className="text-center px-3 py-2.5">
                      {t("tasks.status.InProgress")}
                    </th>
                    <th className="text-center px-3 py-2.5">
                      {t("tasks.status.Submitted")}
                    </th>
                    <th className="text-center px-3 py-2.5">
                      {t("tasks.status.Approved")}
                    </th>
                    <th className="text-center px-3 py-2.5 hidden md:table-cell">
                      {t("manager.team.consultations")}
                    </th>
                    <th className="text-right px-4 py-2.5 hidden lg:table-cell">
                      {t("manager.team.lastLogin")}
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {team.map((c) => (
                    <tr
                      key={c.id}
                      className="border-t border-border/40 hover:bg-brand/5 transition-colors"
                    >
                      <td className="px-4 py-2.5">
                        <Link
                          to={`/manager/consultants/${c.id}`}
                          state={{
                            name:
                              `${c.firstName ?? ""} ${c.lastName ?? ""}`.trim() ||
                              c.email,
                          }}
                          className="flex items-center gap-2.5 group"
                        >
                          <span className="w-8 h-8 rounded-lg bg-brand/25 text-dark flex items-center justify-center text-[11px] font-extrabold shrink-0 group-hover:bg-brand/50 transition-colors">
                            {`${c.firstName?.[0] ?? ""}${c.lastName?.[0] ?? ""}`.toUpperCase()}
                          </span>
                          <span className="min-w-0">
                            <span className="block font-bold text-dark truncate group-hover:underline">
                              {c.firstName} {c.lastName}
                              {c.tasks.submitted > 0 && (
                                <span className="ml-1.5 text-[9px] font-bold text-dark bg-brand rounded-full px-1.5 py-0.5 uppercase align-middle">
                                  {c.tasks.submitted}{" "}
                                  {t("manager.team.submittedBadge")}
                                </span>
                              )}
                            </span>
                            <span className="block text-[11px] text-muted truncate">
                              {c.email}
                            </span>
                          </span>
                        </Link>
                      </td>
                      <td className="text-center px-3 py-2.5 font-bold text-body tabular-nums">
                        {c.tasks.pending}
                      </td>
                      <td className="text-center px-3 py-2.5 font-bold text-blue-600 tabular-nums">
                        {c.tasks.inProgress}
                      </td>
                      <td className="text-center px-3 py-2.5 tabular-nums">
                        <span className="font-bold text-dark bg-brand/20 rounded-md px-2 py-0.5">
                          {c.tasks.submitted}
                        </span>
                      </td>
                      <td className="text-center px-3 py-2.5 font-bold text-green-600 tabular-nums">
                        {c.tasks.approved}
                      </td>
                      <td className="text-center px-3 py-2.5 text-muted tabular-nums hidden md:table-cell">
                        {c.consultations}
                      </td>
                      <td className="text-right px-4 py-2.5 text-[11px] text-muted hidden lg:table-cell">
                        {fmtDate(c.lastLoginAt)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )
      )}
    </div>
  );
}
