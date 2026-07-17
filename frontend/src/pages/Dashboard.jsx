import { useEffect, useState } from "react";
import { Link, Navigate, useNavigate } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import { useLanguage } from "../context/LanguageContext";
import useSystemHealth from "../hooks/useSystemHealth";
import fiscalService from "../services/fiscalService";
import { jortService } from "../services/authService";
import HeroBanner from "../components/common/HeroBanner";
import { openLiveJort } from "./news/News";

const fmtDate = (d, lang) =>
  d
    ? new Date(d).toLocaleDateString(lang === "en" ? "en-US" : "fr-FR", {
        day: "numeric",
        month: "short",
      })
    : "—";

const CHART_COLORS = ["#FFE600", "#6366F1", "#8B5CF6", "#10B981"];
const STAT_THEMES = [
  {
    gradient: "from-amber-400/20 to-yellow-500/10",
    border: "border-amber-300/40",
    iconBg: "bg-amber-400/20",
    iconColor: "text-amber-600",
    darkGradient: "dark:from-amber-400/10 dark:to-yellow-900/10",
    darkBorder: "dark:border-amber-500/20",
    darkIcon: "dark:text-amber-400",
  },
  {
    gradient: "from-blue-400/20 to-indigo-500/10",
    border: "border-blue-300/40",
    iconBg: "bg-blue-400/20",
    iconColor: "text-blue-600",
    darkGradient: "dark:from-blue-400/10 dark:to-indigo-900/10",
    darkBorder: "dark:border-blue-500/20",
    darkIcon: "dark:text-blue-400",
  },
  {
    gradient: "from-violet-400/20 to-purple-500/10",
    border: "border-violet-300/40",
    iconBg: "bg-violet-400/20",
    iconColor: "text-violet-600",
    darkGradient: "dark:from-violet-400/10 dark:to-purple-900/10",
    darkBorder: "dark:border-violet-500/20",
    darkIcon: "dark:text-violet-400",
  },
  {
    gradient: "from-emerald-400/20 to-teal-500/10",
    border: "border-emerald-300/40",
    iconBg: "bg-emerald-400/20",
    iconColor: "text-emerald-600",
    darkGradient: "dark:from-emerald-400/10 dark:to-teal-900/10",
    darkBorder: "dark:border-emerald-500/20",
    darkIcon: "dark:text-emerald-400",
  },
];

const BAR_GRADIENTS = [
  ["#FFE600", "#F59E0B"],
  ["#8B5CF6", "#6366F1"],
  ["#3B82F6", "#0EA5E9"],
  ["#10B981", "#14B8A6"],
  ["#F97316", "#EF4444"],
  ["#EC4899", "#D946EF"],
  ["#FFE600", "#F59E0B"],
];

function ActivityChart({ consultations, lang }) {
  const days = 7;
  const today = new Date();
  today.setHours(23, 59, 59, 999);
  const labels = [];
  const dayLabels = [];
  const counts = [];
  for (let i = days - 1; i >= 0; i--) {
    const d = new Date(today);
    d.setDate(d.getDate() - i);
    labels.push(
      d.toLocaleDateString(lang === "en" ? "en-US" : "fr-FR", {
        weekday: "short",
      }),
    );
    dayLabels.push(
      d.toLocaleDateString(lang === "en" ? "en-US" : "fr-FR", {
        day: "numeric",
        month: "short",
      }),
    );
    counts.push(0);
  }
  (consultations || []).forEach((c) => {
    const created = new Date(c.createdAt);
    const diffDays = Math.floor((today - created) / 86400000);
    if (diffDays >= 0 && diffDays < days) counts[days - 1 - diffDays]++;
  });
  const total = counts.reduce((s, c) => s + c, 0);
  const maxC = Math.max(...counts, 3);
  const ySteps = [0, Math.ceil(maxC / 2), maxC];

  const [animated, setAnimated] = useState(false);
  useEffect(() => {
    const t = setTimeout(() => setAnimated(true), 100);
    return () => clearTimeout(t);
  }, []);

  const w = 600,
    h = 200,
    pad = 40,
    barGap = 14;
  const chartW = w - pad - 10;
  const chartH = h - 40;
  const barW = (chartW - barGap * (days - 1)) / days;

  return (
    <div className="relative">
      <div className="flex items-center justify-between mb-2">
        <div className="flex items-baseline gap-2">
          <span className="text-2xl font-extrabold text-dark">{total}</span>
          <span className="text-[12px] text-muted font-medium">
            {lang === "en"
              ? "consultations this week"
              : "consultations cette semaine"}
          </span>
        </div>
        {total > 0 && (
          <span className="text-[11px] font-bold text-emerald-500 dark:text-emerald-400 bg-emerald-50 dark:bg-emerald-500/10 rounded-full px-2 py-0.5">
            {counts[6] > 0
              ? `+${counts[6]} ${lang === "en" ? "today" : "aujourd'hui"}`
              : ""}
          </span>
        )}
      </div>
      <svg
        viewBox={`0 0 ${w} ${h}`}
        className="w-full"
        style={{ height: "auto", maxHeight: 220 }}
      >
        <defs>
          {BAR_GRADIENTS.map((g, i) => (
            <linearGradient
              key={i}
              id={`bar-grad-${i}`}
              x1="0"
              y1="0"
              x2="0"
              y2="1"
            >
              <stop offset="0%" stopColor={g[0]} />
              <stop offset="100%" stopColor={g[1]} />
            </linearGradient>
          ))}
          <linearGradient id="bar-shadow" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="#FFE600" stopOpacity="0.15" />
            <stop offset="100%" stopColor="#FFE600" stopOpacity="0" />
          </linearGradient>
        </defs>
        {ySteps.map((v) => {
          const y = 10 + chartH - (v / maxC) * chartH;
          return (
            <g key={v}>
              <line
                x1={pad}
                y1={y}
                x2={w - 10}
                y2={y}
                stroke="rgb(var(--c-border))"
                strokeWidth="0.8"
                strokeDasharray="4,4"
                opacity="0.5"
              />
              <text
                x={pad - 8}
                y={y + 3}
                textAnchor="end"
                className="fill-muted"
                style={{ fontSize: "9px", fontWeight: 600 }}
              >
                {v}
              </text>
            </g>
          );
        })}
        {counts.map((c, i) => {
          const x = pad + i * (barW + barGap);
          const barH = animated ? Math.max(4, (c / maxC) * chartH) : 4;
          const y = 10 + chartH - barH;
          return (
            <g key={i}>
              <rect
                x={x}
                y={y}
                width={barW}
                height={barH}
                rx={barW > 20 ? 8 : 5}
                fill={c > 0 ? `url(#bar-grad-${i})` : "rgb(var(--c-border))"}
                opacity={c > 0 ? 1 : 0.25}
                className="transition-all duration-700 ease-out"
                style={{ transitionDelay: `${i * 80}ms` }}
              />
              {c > 0 && (
                <text
                  x={x + barW / 2}
                  y={y - 6}
                  textAnchor="middle"
                  className="fill-dark"
                  style={{
                    fontSize: "10px",
                    fontWeight: 700,
                    opacity: animated ? 1 : 0,
                    transition: "opacity 0.5s",
                    transitionDelay: `${i * 80 + 400}ms`,
                  }}
                >
                  {c}
                </text>
              )}
              <text
                x={x + barW / 2}
                y={h - 6}
                textAnchor="middle"
                className="fill-muted"
                style={{ fontSize: "9px", fontWeight: 600 }}
              >
                {labels[i]}
              </text>
            </g>
          );
        })}
      </svg>
    </div>
  );
}

function DonutChart({ data }) {
  if (!data?.length) return null;
  const total = data.reduce((s, d) => s + Number(d.count), 0);
  const r = 28,
    cx = 38,
    cy = 38,
    sw = 6;
  const C = 2 * Math.PI * r;
  let acc = 0;
  return (
    <div className="flex items-center gap-3">
      <svg viewBox="0 0 76 76" className="w-[76px] h-[76px] shrink-0">
        <circle
          cx={cx}
          cy={cy}
          r={r}
          fill="none"
          stroke="rgb(var(--c-border))"
          strokeWidth={sw}
          opacity={0.3}
        />
        {data.map((d, i) => {
          const pct = Number(d.count) / total;
          const dash = C * pct;
          const off = -C * acc;
          acc += pct;
          return (
            <circle
              key={i}
              cx={cx}
              cy={cy}
              r={r}
              fill="none"
              stroke={CHART_COLORS[i % CHART_COLORS.length]}
              strokeWidth={sw}
              strokeDasharray={`${dash} ${C - dash}`}
              strokeDashoffset={off}
              transform={`rotate(-90 ${cx} ${cy})`}
              strokeLinecap="round"
              className="transition-all duration-700"
            />
          );
        })}
        <text
          x={cx}
          y={cx - 1}
          textAnchor="middle"
          dominantBaseline="middle"
          className="fill-dark"
          style={{ fontSize: "9px", fontWeight: 700 }}
        >
          {total >= 1000 ? `${(total / 1000).toFixed(1)}k` : total}
        </text>
        <text
          x={cx}
          y={cx + 7}
          textAnchor="middle"
          className="fill-muted"
          style={{ fontSize: "4px" }}
        >
          docs
        </text>
      </svg>
      <div className="space-y-1.5 min-w-0 flex-1">
        {data.map((d, i) => (
          <div key={i} className="flex items-center gap-1.5 text-[11px]">
            <span
              className="w-2 h-2 rounded-full shrink-0"
              style={{ backgroundColor: CHART_COLORS[i % CHART_COLORS.length] }}
            />
            <span className="text-body truncate">{d.label}</span>
            <span className="text-muted ml-auto tabular-nums font-semibold">
              {Number(d.count).toLocaleString()}
            </span>
          </div>
        ))}
      </div>
    </div>
  );
}

function Stat({ label, value, hint, icon, loading, themeIdx, trend }) {
  const th = STAT_THEMES[themeIdx % STAT_THEMES.length];
  return (
    <div
      className={`relative overflow-hidden rounded-xl p-4 bg-gradient-to-br ${th.gradient} ${th.darkGradient} border ${th.border} ${th.darkBorder} hover:scale-[1.02] hover:-translate-y-0.5 hover:shadow-lg transition-all duration-200 cursor-default group`}
    >
      <div className="flex items-center gap-2.5 mb-2">
        <span
          className={`w-8 h-8 rounded-lg ${th.iconBg} flex items-center justify-center shrink-0`}
        >
          <svg
            className={`w-4 h-4 ${th.iconColor} ${th.darkIcon}`}
            fill="none"
            viewBox="0 0 24 24"
            stroke="currentColor"
            strokeWidth={1.8}
          >
            {icon}
          </svg>
        </span>
        <p className="text-[11px] font-bold text-muted uppercase tracking-[0.1em]">
          {label}
        </p>
      </div>
      {loading ? (
        <div className="h-8 w-20 bg-light/60 rounded-lg animate-pulse" />
      ) : (
        <div className="flex items-baseline gap-2">
          <p className="text-2xl font-extrabold text-dark tracking-tight">
            {value}
          </p>
          {trend && (
            <span className="text-[11px] font-bold text-emerald-500 dark:text-emerald-400 bg-emerald-50 dark:bg-emerald-500/10 rounded-full px-2 py-0.5">
              {trend}
            </span>
          )}
        </div>
      )}
      <p className="text-[11px] text-muted mt-1 leading-snug">{hint}</p>
    </div>
  );
}

function QuickAction({ to, icon, label, desc, color }) {
  return (
    <Link
      to={to}
      className="flex items-center gap-2.5 rounded-xl border border-border/70 p-3 hover:border-brand/50 hover:shadow-md hover:scale-[1.01] transition-all duration-200 group bg-white dark:bg-cream"
    >
      <span
        className={`w-8 h-8 rounded-lg ${color || "bg-light/80"} group-hover:scale-110 flex items-center justify-center transition-all shrink-0`}
      >
        <svg
          className="w-4 h-4 text-dark transition-colors"
          fill="none"
          viewBox="0 0 24 24"
          stroke="currentColor"
          strokeWidth={1.8}
        >
          {icon}
        </svg>
      </span>
      <span className="min-w-0 flex-1">
        <span className="block text-[12px] font-bold text-dark">{label}</span>
        <span className="block text-[10px] text-muted truncate">{desc}</span>
      </span>
      <svg
        className="w-3 h-3 text-border group-hover:text-brand group-hover:translate-x-0.5 transition-all shrink-0"
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
    </Link>
  );
}

function JortFeed({ t, lang, isAdmin }) {
  const [items, setItems] = useState(null);
  const [refreshing, setRefreshing] = useState(false);

  const load = async () => {
    try {
      const { data: res } = await jortService.activities(8);
      setItems(res.data || []);
    } catch {
      setItems([]);
    }
  };

  useEffect(() => {
    load();
  }, []);

  const refresh = async () => {
    setRefreshing(true);
    try {
      await jortService.refresh();
      await load();
    } catch {
    } finally {
      setRefreshing(false);
    }
  };

  return (
    <div className="rounded-xl border border-border/70 overflow-hidden bg-white dark:bg-cream">
      <div className="px-4 py-3 border-b border-border/40 flex items-center justify-between bg-gradient-to-r from-red-50/50 to-transparent dark:from-red-500/5">
        <div className="flex items-center gap-2">
          <span className="w-6 h-6 rounded-lg bg-red-100 dark:bg-red-500/15 flex items-center justify-center">
            <svg
              className="w-3.5 h-3.5 text-red-500"
              fill="none"
              viewBox="0 0 24 24"
              stroke="currentColor"
              strokeWidth={2}
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                d="M12 7.5h1.5m-1.5 3h1.5m-7.5 3h7.5m-7.5 3h7.5m3-9h3.375c.621 0 1.125.504 1.125 1.125V18a2.25 2.25 0 01-2.25 2.25M16.5 7.5V18a2.25 2.25 0 002.25 2.25M16.5 7.5V4.875c0-.621-.504-1.125-1.125-1.125H4.125C3.504 3.75 3 4.254 3 4.875V18a2.25 2.25 0 002.25 2.25h13.5M6 7.5h3v3H6v-3z"
              />
            </svg>
          </span>
          <h3 className="text-[13px] font-bold text-dark">{t("jort.title")}</h3>
        </div>
        <div className="flex items-center gap-2">
          {isAdmin && (
            <button
              onClick={refresh}
              disabled={refreshing}
              title={t("jort.refresh")}
              className="text-[11px] font-semibold text-muted hover:text-dark flex items-center gap-1 transition-colors disabled:opacity-50"
            >
              <svg
                className={`w-3 h-3 ${refreshing ? "animate-spin" : ""}`}
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                strokeWidth={2.2}
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  d="M16.023 9.348h4.992V4.356M2.985 19.644v-4.992h4.992m9.348-4.992a7.5 7.5 0 00-12.548-3.364L2.985 9.348m0 0h4.992m-4.992 0V4.356M20.015 14.652a7.5 7.5 0 01-12.548 3.364l-1.792-1.848m0 0H2.985"
                />
              </svg>
              {refreshing ? t("jort.refreshing") : t("jort.refresh")}
            </button>
          )}
          <Link
            to="/app/news"
            className="text-[11px] font-semibold text-muted hover:text-dark flex items-center gap-0.5 transition-colors"
          >
            {t("jort.seeAll")}
            <svg
              className="w-2.5 h-2.5"
              fill="none"
              viewBox="0 0 24 24"
              stroke="currentColor"
              strokeWidth={2.5}
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                d="M8.25 4.5l7.5 7.5-7.5 7.5"
              />
            </svg>
          </Link>
        </div>
      </div>
      <div className="divide-y divide-border/30">
        {items === null &&
          [...Array(4)].map((_, i) => (
            <div key={i} className="flex items-start gap-2.5 px-4 py-3 animate-pulse">
              <span className="mt-1.5 w-2 h-2 rounded-full shrink-0 bg-light" />
              <div className="min-w-0 flex-1 space-y-1.5">
                <div className="h-2.5 bg-light rounded w-3/4" />
                <div className="h-2 bg-light rounded w-1/3" />
              </div>
            </div>
          ))}
        {items?.length === 0 && (
          <p className="px-4 py-8 text-center text-[12px] text-muted">
            {t("jort.empty")}
          </p>
        )}
        {items?.map((item) => (
          <button
            key={item.id}
            onClick={() => openLiveJort("latest")}
            className="w-full text-left flex items-start gap-2.5 px-4 py-3 hover:bg-light/40 transition-colors group"
          >
            <span
              className="mt-1.5 w-2 h-2 rounded-full shrink-0"
              style={{
                backgroundColor: item.isNew
                  ? "#10B981"
                  : "rgb(var(--c-border))",
              }}
            />
            <div className="min-w-0 flex-1" dir="auto">
              <p className="text-[12px] font-medium text-dark group-hover:text-dark/80 line-clamp-2">
                {item.title}
              </p>
              <div className="flex items-center gap-2 mt-1">
                <span className="text-[10px] font-bold text-muted bg-light rounded-md px-1.5 py-0.5">
                  {t(`jort.cat.${item.category}`) || item.category}
                </span>
                <span className="text-[10px] text-muted">
                  {item.date
                    ? new Date(item.date).toLocaleDateString(
                        lang === "en" ? "en-US" : "fr-FR",
                        { day: "numeric", month: "short", year: "numeric" },
                      )
                    : item.dateText}
                </span>
                {item.isNew && (
                  <span className="text-[9px] font-bold text-emerald-600 dark:text-emerald-400 bg-emerald-50 dark:bg-emerald-500/10 rounded-md px-1.5 py-0.5">
                    {t("jort.new")}
                  </span>
                )}
              </div>
            </div>
          </button>
        ))}
      </div>
    </div>
  );
}

export default function Dashboard() {
  const { user, isAdmin } = useAuth();
  const { t, lang } = useLanguage();
  const navigate = useNavigate();
  const { data: health } = useSystemHealth({ poll: 30000 });
  const [consultations, setConsultations] = useState(null);
  const [stats, setStats] = useState(null);

  useEffect(() => {
    fiscalService
      .list("")
      .then(({ data }) => setConsultations(data.data))
      .catch(() => setConsultations([]));
    fiscalService
      .stats()
      .then(({ data }) => setStats(data.data))
      .catch(() => setStats(null));
  }, []);

  // Admins land on the full-bleed governance console instead of the consultant dashboard.
  if (isAdmin) return <Navigate to="/admin" replace />;

  const recent = (consultations || []).slice(0, 4);
  const kb =
    stats &&
    [
      { label: "Codes fiscaux", count: stats.codesCount },
      { label: "Conventions", count: stats.conventionsCount },
      { label: "Lois de finances", count: stats.loisCount },
      { label: "Doctrine & notes", count: stats.notesCount },
    ].filter((x) => x.count > 0);

  const greeting = () => {
    const h = new Date().getHours();
    return h < 12
      ? t("dashboard.greeting.morning")
      : h < 18
        ? t("dashboard.greeting.afternoon")
        : t("dashboard.greeting.evening");
  };

  const thisWeekCount = (consultations || []).filter((c) => {
    const diffDays = Math.floor(
      (Date.now() - new Date(c.createdAt)) / 86400000,
    );
    return diffDays >= 0 && diffDays < 7;
  }).length;

  const activeClients = consultations
    ? new Set(consultations.map((c) => c.clientName).filter(Boolean)).size
    : null;

  return (
    <div className="animate-fade-up space-y-4 max-w-full overflow-x-hidden">
      {/* greeting — hero box with the primary actions inside */}
      <HeroBanner
        greeting={greeting()}
        name={user?.firstName}
        subtitle={t("dashboard.subtitle")}
        actions={
          <>
            <Link
              to="/app/chat"
              className="flex items-center gap-1.5 text-[12px] font-bold text-dark bg-white/80 dark:bg-white/10 border border-dark/15 dark:border-white/15 backdrop-blur rounded-xl px-3.5 py-2.5 hover:border-brand hover:bg-brand/20 hover:-translate-y-0.5 transition-all shadow-sm"
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
                  d="M8.625 12a.375.375 0 11-.75 0 .375.375 0 01.75 0zm0 0H8.25m4.125 0a.375.375 0 11-.75 0 .375.375 0 01.75 0zm0 0H12m4.125 0a.375.375 0 11-.75 0 .375.375 0 01.75 0zm0 0h-.375M21 12c0 4.556-4.03 8.25-9 8.25a9.764 9.764 0 01-2.555-.337A5.972 5.972 0 015.41 20.97a5.969 5.969 0 01-.474-.065 4.48 4.48 0 00.978-2.025c.09-.457-.133-.901-.467-1.226C3.93 16.178 3 14.189 3 12c0-4.556 4.03-8.25 9-8.25s9 3.694 9 8.25z"
                />
              </svg>
              {t("dashboard.askQuestion")}
            </Link>
            <Link
              to="/app/consultations?new=1"
              className="flex items-center gap-1.5 bg-dark text-brand text-[12px] font-bold rounded-xl px-4 py-2.5 hover:shadow-xl hover:shadow-dark/25 hover:-translate-y-0.5 transition-all"
            >
              <svg
                className="w-3.5 h-3.5"
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                strokeWidth={2.4}
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  d="M12 4.5v15m7.5-7.5h-15"
                />
              </svg>
              {t("dashboard.newConsultation")}
            </Link>
          </>
        }
      />

      {/* KPIs */}
      <div className="grid grid-cols-2 xl:grid-cols-4 gap-3">
        <Stat
          label={t("dashboard.kpi.consultations")}
          value={consultations ? consultations.length : "—"}
          loading={!consultations}
          hint={t("dashboard.kpi.consultationsHint")}
          trend={
            consultations && thisWeekCount > 0
              ? `+${thisWeekCount} ${t("dashboard.kpi.consultationsThisWeek")}`
              : null
          }
          themeIdx={0}
          icon={
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              d="M19.5 14.25v-2.625a3.375 3.375 0 00-3.375-3.375h-1.5A1.125 1.125 0 0113.5 7.125v-1.5a3.375 3.375 0 00-3.375-3.375H8.25m2.25 0H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 00-9-9z"
            />
          }
        />
        <Stat
          label={t("dashboard.kpi.chunks")}
          value={health?.chunks ? health.chunks.toLocaleString() : "—"}
          loading={!health}
          hint={t("dashboard.kpi.chunksHint")}
          themeIdx={1}
          icon={
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              d="M12 21a9.004 9.004 0 008.716-6.747M12 21a9.004 9.004 0 01-8.716-6.747M12 21c2.485 0 4.5-4.03 4.5-9S14.485 3 12 3m0 18c-2.485 0-4.5-4.03-4.5-9S9.515 3 12 3m0 0a8.997 8.997 0 017.843 4.582M12 3a8.997 8.997 0 00-7.843 4.582m15.686 0A11.953 11.953 0 0112 10.5c-2.998 0-5.74-1.1-7.843-2.918m15.686 0A8.959 8.959 0 0121 12c0 .778-.099 1.533-.284 2.253m-18.432 0A8.959 8.959 0 013 12c0-.778.099-1.533.284-2.253"
            />
          }
        />
        <Stat
          label={t("dashboard.kpi.entities")}
          value={
            stats?.totalEntities
              ? Number(stats.totalEntities).toLocaleString()
              : "—"
          }
          loading={!stats && !health}
          hint={t("dashboard.kpi.entitiesHint")}
          themeIdx={2}
          icon={
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              d="M7.217 10.907a2.25 2.25 0 100 2.186m0-2.186c.18.324.283.696.283 1.093s-.103.77-.283 1.093m0-2.186l9.566-5.314m-9.566 7.5l9.566 5.314m0 0a2.25 2.25 0 103.935 2.186 2.25 2.25 0 00-3.935-2.186zm0-12.814a2.25 2.25 0 103.933-2.185 2.25 2.25 0 00-3.933 2.185z"
            />
          }
        />
        <Stat
          label={t("dashboard.kpi.clients")}
          value={activeClients !== null ? activeClients : "—"}
          loading={activeClients === null}
          hint={t("dashboard.kpi.clientsHint")}
          themeIdx={3}
          icon={
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              d="M15 19.128a9.38 9.38 0 002.625.372 9.337 9.337 0 004.121-.952 4.125 4.125 0 00-7.533-2.493M15 19.128v-.003c0-1.113-.285-2.16-.786-3.07M15 19.128v.106A12.318 12.318 0 018.624 21c-2.331 0-4.512-.645-6.374-1.766l-.001-.109a6.375 6.375 0 0111.964-3.07M12 6.375a3.375 3.375 0 11-6.75 0 3.375 3.375 0 016.75 0zm8.25 2.25a2.625 2.625 0 11-5.25 0 2.625 2.625 0 015.25 0z"
            />
          }
        />
      </div>

      {/* Charts + KB */}
      <div className="grid lg:grid-cols-[1fr,240px] gap-3 items-start">
        <div className="rounded-xl border border-border/70 p-5 bg-white dark:bg-cream">
          <div className="flex items-center justify-between mb-3">
            <h3 className="text-[13px] font-bold text-dark">
              {t("dashboard.activity")}
            </h3>
            <span className="text-[10px] font-semibold text-muted bg-light rounded-md px-2 py-0.5">
              {t("dashboard.last7days")}
            </span>
          </div>
          {consultations ? (
            <ActivityChart consultations={consultations} lang={lang} />
          ) : (
            <div className="h-40 bg-light/40 rounded-lg animate-pulse" />
          )}
        </div>
        <div className="rounded-xl border border-border/70 p-4 bg-white dark:bg-cream">
          <h3 className="text-[13px] font-bold text-dark mb-3">
            {t("dashboard.kb")}
          </h3>
          {kb?.length ? (
            <DonutChart data={kb} />
          ) : (
            <div className="flex items-center gap-3">
              <div className="w-[76px] h-[76px] rounded-full bg-light/40 animate-pulse shrink-0" />
              <div className="space-y-1.5 flex-1">
                {[0, 1, 2, 3].map((i) => (
                  <div
                    key={i}
                    className="h-2.5 bg-light/40 rounded animate-pulse"
                  />
                ))}
              </div>
            </div>
          )}
        </div>
      </div>

      {/* Bottom grid */}
      <div className="grid lg:grid-cols-[1fr,240px] gap-3 items-start">
        <div className="space-y-3">
          {/* Recent consultations */}
          <div className="rounded-xl border border-border/70 overflow-hidden bg-white dark:bg-cream">
            <div className="px-4 py-3 border-b border-border/40 flex items-center justify-between">
              <h2 className="text-[13px] font-bold text-dark">
                {t("dashboard.recent")}
              </h2>
              <Link
                to="/app/consultations"
                className="text-[11px] font-semibold text-muted hover:text-dark flex items-center gap-0.5 transition-colors"
              >
                {t("dashboard.seeAll")}
                <svg
                  className="w-2.5 h-2.5"
                  fill="none"
                  viewBox="0 0 24 24"
                  stroke="currentColor"
                  strokeWidth={2.2}
                >
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    d="M8.25 4.5l7.5 7.5-7.5 7.5"
                  />
                </svg>
              </Link>
            </div>
            {!consultations && (
              <div className="p-3 space-y-1.5">
                {[0, 1, 2].map((i) => (
                  <div
                    key={i}
                    className="h-10 bg-light/40 rounded-lg animate-pulse"
                  />
                ))}
              </div>
            )}
            {consultations?.length === 0 && (
              <div className="px-4 py-6 text-center">
                <p className="text-[13px] font-bold text-dark">
                  {t("dashboard.noConsultations")}
                </p>
                <p className="text-[11px] text-muted mt-0.5 mb-3">
                  {t("dashboard.noConsultationsHint")}
                </p>
                <Link
                  to="/app/consultations?new=1"
                  className="inline-block bg-brand text-black font-bold text-[11px] rounded-lg px-3.5 py-2 hover:bg-black/60 hover:text-white transition-all"
                >
                  {t("dashboard.createConsultation")}
                </Link>
              </div>
            )}
            <div className="divide-y divide-border/30">
              {recent.map((c) => (
                <button
                  key={c.id}
                  onClick={() => navigate(`/app/consultations/${c.id}`)}
                  className="w-full text-left px-4 py-3 flex items-center gap-3 hover:bg-light/30 transition-colors group"
                >
                  <span className="shrink-0 w-8 h-8 rounded-lg bg-gradient-to-br from-brand/20 to-amber-200/20 dark:from-brand/10 dark:to-amber-500/10 group-hover:from-brand/30 group-hover:to-amber-200/30 flex items-center justify-center transition-colors">
                    <svg
                      className="w-4 h-4 text-dark/70"
                      fill="none"
                      viewBox="0 0 24 24"
                      stroke="currentColor"
                      strokeWidth={1.8}
                    >
                      <path
                        strokeLinecap="round"
                        strokeLinejoin="round"
                        d="M19.5 14.25v-2.625a3.375 3.375 0 00-3.375-3.375h-1.5A1.125 1.125 0 0113.5 7.125v-1.5a3.375 3.375 0 00-3.375-3.375H8.25m6.75 12H9m5.25 3H9"
                      />
                    </svg>
                  </span>
                  <span className="min-w-0 flex-1">
                    <span className="flex items-center gap-1.5">
                      <span className="font-bold text-dark text-[12px] truncate">
                        {c.clientName}
                      </span>
                      <span className="text-[9px] font-bold text-dark/70 bg-brand/20 dark:bg-brand/15 rounded-md px-1.5 py-0.5 shrink-0">
                        {c.reference}
                      </span>
                      {c.isInternational && (
                        <span className="text-[9px] font-medium text-body bg-light border border-border/40 rounded-md px-1.5 py-0.5 shrink-0">
                          Intl
                        </span>
                      )}
                    </span>
                    <span className="block text-[11px] text-muted truncate mt-0.5">
                      {c.fiscalQuestion}
                    </span>
                  </span>
                  <span className="text-[10px] text-muted shrink-0 hidden sm:block">
                    {fmtDate(c.createdAt, lang)}
                  </span>
                </button>
              ))}
            </div>
          </div>

          <JortFeed t={t} lang={lang} isAdmin={isAdmin} />
        </div>

        {/* Quick actions */}
        <div className="space-y-2">
          <h2 className="text-[10px] font-bold text-muted uppercase tracking-[0.12em] px-1">
            {t("dashboard.quickActions")}
          </h2>
          <QuickAction
            to="/app/search"
            label={t("dashboard.qa.search")}
            desc={t("dashboard.qa.searchDesc")}
            color="bg-blue-100/80 dark:bg-blue-500/15"
            icon={
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                d="M21 21l-5.197-5.197m0 0A7.5 7.5 0 105.196 5.196a7.5 7.5 0 0010.607 10.607z"
              />
            }
          />
          <QuickAction
            to="/app/chat"
            label={t("dashboard.qa.chat")}
            desc={t("dashboard.qa.chatDesc")}
            color="bg-purple-100/80 dark:bg-purple-500/15"
            icon={
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                d="M9.813 15.904L9 18.75l-.813-2.846a4.5 4.5 0 00-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 003.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 003.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 00-3.09 3.09z"
              />
            }
          />
          <QuickAction
            to="/app/consultations?new=1"
            label={t("dashboard.qa.newConsultation")}
            desc={t("dashboard.qa.newConsultationDesc")}
            color="bg-emerald-100/80 dark:bg-emerald-500/15"
            icon={
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                d="M12 4.5v15m7.5-7.5h-15"
              />
            }
          />
          {isAdmin && (
            <QuickAction
              to="/admin/users"
              label={t("dashboard.qa.users")}
              desc={t("dashboard.qa.usersDesc")}
              color="bg-amber-100/80 dark:bg-amber-500/15"
              icon={
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  d="M15 19.128a9.38 9.38 0 002.625.372 9.337 9.337 0 004.121-.952 4.125 4.125 0 00-7.533-2.493M15 19.128v-.003c0-1.113-.285-2.16-.786-3.07M15 19.128v.106A12.318 12.318 0 018.624 21c-2.331 0-4.512-.645-6.374-1.766l-.001-.109a6.375 6.375 0 0111.964-3.07M12 6.375a3.375 3.375 0 11-6.75 0 3.375 3.375 0 016.75 0z"
                />
              }
            />
          )}
        </div>
      </div>
    </div>
  );
}
