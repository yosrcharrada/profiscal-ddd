import { useCallback, useEffect, useState } from "react";
import { useAuth } from "../../context/AuthContext";
import { useLanguage } from "../../context/LanguageContext";
import { jortService } from "../../services/authService";

/* Journal Officiel space — Chrome-style tab shell (same look as the manager and
   support spaces): one fixed "Journal Officiel" tab holding the scraped French
   feed, plus a closeable onglet per text the user opens. Each text tab renders
   the OFFICIAL PDF, streamed through the backend session replay. */

// localStorage key shared with the sidebar "new" dot — writing the newest activity
// date here (on view) clears the dot the next time the sidebar re-checks.
export const NEWS_SEEN_KEY = "taxmind.news.lastSeen";

const CAT_STYLE = {
  Loi: { chip: "bg-red-100 text-red-700 border-red-300 dark:bg-red-500/15 dark:text-red-300 dark:border-red-500/30", bar: "bg-red-400" },
  Decret: { chip: "bg-violet-100 text-violet-700 border-violet-300 dark:bg-violet-500/15 dark:text-violet-300 dark:border-violet-500/30", bar: "bg-violet-400" },
  Arrete: { chip: "bg-blue-100 text-blue-700 border-blue-300 dark:bg-blue-500/15 dark:text-blue-300 dark:border-blue-500/30", bar: "bg-blue-400" },
  Avis: { chip: "bg-emerald-100 text-emerald-700 border-emerald-300 dark:bg-emerald-500/15 dark:text-emerald-300 dark:border-emerald-500/30", bar: "bg-emerald-400" },
  Decision: { chip: "bg-orange-100 text-orange-700 border-orange-300 dark:bg-orange-500/15 dark:text-orange-300 dark:border-orange-500/30", bar: "bg-orange-400" },
  Autre: { chip: "bg-brand/25 text-dark border-brand/50", bar: "bg-brand" },
};

const TAB_BASE =
  "relative flex items-center gap-1.5 text-[12.5px] font-semibold px-4 py-2 rounded-t-lg whitespace-nowrap transition-all duration-200";
const TAB_ACTIVE = "bg-white text-dark border border-border border-b-white -mb-px z-10";
const TAB_IDLE = "text-muted hover:text-dark hover:bg-white/50 border border-transparent";

const NEWS_ICON =
  "M12 7.5h1.5m-1.5 3h1.5m-7.5 3h7.5m-7.5 3h7.5m3-9h3.375c.621 0 1.125.504 1.125 1.125V18a2.25 2.25 0 01-2.25 2.25M16.5 7.5V18a2.25 2.25 0 002.25 2.25M16.5 7.5V4.875c0-.621-.504-1.125-1.125-1.125H4.125C3.504 3.75 3 4.254 3 4.875V18a2.25 2.25 0 002.25 2.25h13.5M6 7.5h3v3H6v-3z";
const DOC_ICON =
  "M19.5 14.25v-2.625a3.375 3.375 0 00-3.375-3.375h-1.5A1.125 1.125 0 0113.5 7.125v-1.5a3.375 3.375 0 00-3.375-3.375H8.25m2.25 0H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 00-9-9z";

const fmt = (d, fallback) =>
  fallback ||
  (d
    ? new Date(d).toLocaleDateString("fr-FR", {
        day: "numeric",
        month: "short",
        year: "numeric",
      })
    : "—");

/* Open a fresh French iort.gov.tn session link in a new browser tab. The tab is
   opened synchronously (popup-blocker safe) and pointed at the minted URL once
   ready. NOTE: no "noopener" feature string — window.open would return null and
   the tab would stay blank. */
export const openLiveJort = async (target = "latest") => {
  const win = window.open("", "_blank");
  try {
    const { data: res } = await jortService.open(target);
    const url = res.data?.url || "http://www.iort.gov.tn";
    if (win) {
      win.opener = null;
      win.location = url;
    } else {
      window.open(url, "_blank");
    }
  } catch {
    if (win) win.location = "http://www.iort.gov.tn";
  }
};

/* ───────────── one opened text: official PDF viewer tab ───────────── */
function PdfTab({ activity, t }) {
  const [state, setState] = useState({ status: "loading", url: null });

  useEffect(() => {
    let alive = true;
    let objectUrl = null;
    setState({ status: "loading", url: null });
    jortService
      .pdf(activity.id)
      .then(({ data }) => {
        if (!alive) return;
        objectUrl = URL.createObjectURL(data);
        setState({ status: "ready", url: objectUrl });
      })
      .catch(() => alive && setState({ status: "error", url: null }));
    return () => {
      alive = false;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [activity.id]);

  if (state.status === "loading") {
    return (
      <div className="h-full flex flex-col items-center justify-center gap-4 animate-fade-in px-6">
        <div className="relative">
          <div className="w-14 h-14 rounded-2xl bg-gradient-to-br from-brand to-[#F59E0B] flex items-center justify-center shadow-lg shadow-brand/30">
            <svg className="w-7 h-7 text-dark" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d={DOC_ICON} />
            </svg>
          </div>
          <span className="absolute -bottom-1 -right-1 w-5 h-5 rounded-full bg-white dark:bg-cream border border-border flex items-center justify-center">
            <span className="w-3 h-3 border-2 border-brand border-t-transparent rounded-full animate-spin" />
          </span>
        </div>
        <div className="text-center max-w-md">
          <p className="text-[13.5px] font-bold text-dark">{t("news.pdfLoading")}</p>
          <p className="text-[12px] text-muted mt-1 leading-relaxed line-clamp-2">{activity.title}</p>
        </div>
      </div>
    );
  }

  if (state.status === "error") {
    return (
      <div className="h-full flex flex-col items-center justify-center gap-4 px-6 animate-fade-in">
        <span className="w-12 h-12 rounded-full bg-orange-50 border border-orange-200 flex items-center justify-center">
          <svg className="w-6 h-6 text-orange-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m9-.75a9 9 0 11-18 0 9 9 0 0118 0zm-9 3.75h.008v.008H12v-.008z" />
          </svg>
        </span>
        <div className="text-center max-w-md">
          <p className="text-[13.5px] font-bold text-dark">{t("news.pdfUnavailable")}</p>
          <p className="text-[12px] text-muted mt-1 leading-relaxed">{t("news.pdfUnavailableHint")}</p>
        </div>
        <button
          onClick={() => openLiveJort("latest")}
          className="flex items-center gap-1.5 text-[12px] font-bold text-dark bg-brand rounded-lg px-4 py-2.5 hover:shadow-lg hover:shadow-brand/40 transition-all"
        >
          {t("news.openOnIortBtn")}
          <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2.2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M13.5 6H5.25A2.25 2.25 0 003 8.25v10.5A2.25 2.25 0 005.25 21h10.5A2.25 2.25 0 0018 18.75V10.5m-10.5 6L21 3m0 0h-5.25M21 3v5.25" />
          </svg>
        </button>
      </div>
    );
  }

  return (
    <div className="h-full flex flex-col">
      <div className="shrink-0 px-4 py-2 border-b border-border/60 bg-white flex items-center gap-2.5">
        <span className={`w-1.5 h-6 rounded-full shrink-0 ${(CAT_STYLE[activity.category] || CAT_STYLE.Autre).bar}`} />
        <p className="flex-1 min-w-0 text-[12.5px] font-semibold text-dark truncate" title={activity.title}>
          {activity.title}
        </p>
        <span className="text-[11px] text-muted shrink-0 tabular-nums">{fmt(activity.date, activity.dateText)}</span>
        <a
          href={state.url}
          download={`JORT_${(activity.dateText || "").replace(/\//g, "-")}.pdf`}
          className="shrink-0 flex items-center gap-1 text-[11px] font-bold text-dark bg-brand/20 border border-brand/40 rounded-md px-2.5 py-1.5 hover:bg-brand/40 transition-colors"
        >
          <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M3 16.5v2.25A2.25 2.25 0 005.25 21h13.5A2.25 2.25 0 0021 18.75V16.5M16.5 12L12 16.5m0 0L7.5 12m4.5 4.5V3" />
          </svg>
          PDF
        </a>
      </div>
      <iframe title={activity.title} src={state.url} className="flex-1 w-full border-0 bg-[#525659]" />
    </div>
  );
}

/* ───────────── fixed tab: the scraped feed ───────────── */
function Feed({ items, error, refreshing, onRefresh, onOpen, t }) {
  const [filter, setFilter] = useState("");
  const cats = ["", "Loi", "Decret", "Arrete", "Avis", "Autre"];
  const shown = (items || []).filter(
    (a) => !filter || a.category === filter || (filter === "Autre" && !CAT_STYLE[a.category]),
  );

  return (
    <div className="h-full flex flex-col">
      {/* hero header */}
      <div className="shrink-0 border-b border-border bg-white relative overflow-hidden">
        <div className="pointer-events-none absolute -top-12 right-8 w-44 h-44 rounded-full bg-brand/20 blur-3xl" />
        <div className="pointer-events-none absolute -bottom-10 left-1/3 w-32 h-32 rounded-full bg-amber-300/20 blur-3xl" />
        <div className="relative flex flex-wrap items-center gap-3 px-4 sm:px-6 py-4">
          <div className="w-10 h-10 rounded-xl bg-gradient-to-br from-brand to-[#F59E0B] flex items-center justify-center shadow-md shadow-brand/30 shrink-0">
            <svg className="w-5 h-5 text-dark" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d={NEWS_ICON} />
            </svg>
          </div>
          <div className="min-w-0 flex-1">
            <h1 className="text-[16px] font-extrabold text-dark leading-tight">{t("jort.title")}</h1>
            <p className="text-[11.5px] text-muted">{t("news.subtitle")}</p>
          </div>
          <button
            onClick={() => openLiveJort("latest")}
            className="flex items-center gap-1.5 text-[12px] font-semibold text-body hover:text-dark border border-border rounded-lg px-3 py-2 hover:border-brand/60 hover:bg-brand/10 transition-all"
          >
            iort.gov.tn
            <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2.2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M13.5 6H5.25A2.25 2.25 0 003 8.25v10.5A2.25 2.25 0 005.25 21h10.5A2.25 2.25 0 0018 18.75V10.5m-10.5 6L21 3m0 0h-5.25M21 3v5.25" />
            </svg>
          </button>
          <button
            onClick={onRefresh}
            disabled={refreshing}
            className="flex items-center gap-1.5 text-[12px] font-bold text-dark bg-brand rounded-lg px-3.5 py-2 hover:shadow-lg hover:shadow-brand/40 transition-all disabled:opacity-50"
          >
            <svg className={`w-3.5 h-3.5 ${refreshing ? "animate-spin" : ""}`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2.2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M16.023 9.348h4.992V4.356M2.985 19.644v-4.992h4.992m9.348-4.992a7.5 7.5 0 00-12.548-3.364L2.985 9.348m0 0h4.992m-4.992 0V4.356M20.015 14.652a7.5 7.5 0 01-12.548 3.364l-1.792-1.848m0 0H2.985" />
            </svg>
            {refreshing ? t("jort.refreshing") : t("jort.refresh")}
          </button>
        </div>

        {/* category filter chips */}
        <div className="relative flex flex-wrap gap-1.5 px-4 sm:px-6 pb-3">
          {cats.map((c) => {
            const active = filter === c;
            const style = CAT_STYLE[c];
            return (
              <button
                key={c || "all"}
                onClick={() => setFilter(c)}
                className={`px-3 py-1 rounded-full border text-[11px] font-semibold transition-all ${
                  active
                    ? c
                      ? `${style.chip} ring-2 ring-brand/40`
                      : "bg-dark text-brand border-dark"
                    : "bg-white text-body border-border/60 hover:border-brand/60 hover:bg-brand/5"
                }`}
              >
                {c === "" ? t("news.all") : t(`jort.cat.${c}`)}
              </button>
            );
          })}
        </div>
      </div>

      {/* list */}
      <div className="flex-1 overflow-y-auto bg-sand/50">
        <div className="max-w-3xl mx-auto w-full px-3 sm:px-5 py-4">
          {error && items?.length === 0 && (
            <div className="mt-6 text-center text-muted text-[13px]">{error}</div>
          )}

          {items === null && (
            <div className="space-y-2">
              {[0, 1, 2, 3, 4].map((i) => (
                <div key={i} className="bg-white border border-border rounded-xl p-4 animate-pulse" style={{ animationDelay: `${i * 80}ms` }}>
                  <div className="h-3.5 bg-light rounded w-1/3" />
                  <div className="mt-3 h-3 bg-light rounded w-full" />
                  <div className="mt-1.5 h-3 bg-light rounded w-4/5" />
                </div>
              ))}
            </div>
          )}

          {items?.length === 0 && !error && (
            <div className="mt-10 text-center text-muted text-[13px]">{t("jort.empty")}</div>
          )}

          <div className="space-y-2">
            {shown.map((a, i) => {
              const cat = CAT_STYLE[a.category] || CAT_STYLE.Autre;
              return (
                <button
                  key={a.id || i}
                  onClick={() => onOpen(a)}
                  style={{ animationDelay: `${Math.min(i, 8) * 40}ms` }}
                  title={t("news.openDocHint")}
                  className="group relative w-full text-left bg-white border border-border rounded-xl pl-4 pr-11 py-3.5 overflow-hidden hover:border-brand/60 hover:shadow-lg hover:shadow-brand/10 hover:-translate-y-0.5 transition-all animate-pop opacity-0"
                >
                  <span className={`absolute left-0 top-0 bottom-0 w-1 ${cat.bar}`} />
                  <div className="flex items-center gap-2 flex-wrap mb-1.5">
                    <span className={`text-[9px] font-bold rounded-full px-2 py-0.5 border ${cat.chip}`}>
                      {t(`jort.cat.${a.category}`)}
                    </span>
                    {a.isNew && (
                      <span className="text-[9px] font-bold rounded-full px-2 py-0.5 bg-brand text-dark animate-pulse">
                        {t("jort.new")}
                      </span>
                    )}
                    {a.description && (
                      <span className="text-[10px] font-semibold text-muted truncate max-w-[45%]">{a.description}</span>
                    )}
                    <span className="text-[11px] text-muted ml-auto shrink-0 tabular-nums">{fmt(a.date, a.dateText)}</span>
                  </div>
                  <p className="text-[13.5px] font-bold text-dark leading-snug">{a.title}</p>

                  {/* hover affordance: open as a document tab */}
                  <span className="absolute right-3 top-1/2 -translate-y-1/2 w-7 h-7 rounded-lg bg-brand/15 text-dark flex items-center justify-center opacity-0 group-hover:opacity-100 group-hover:bg-brand transition-all">
                    <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                      <path strokeLinecap="round" strokeLinejoin="round" d={DOC_ICON} />
                    </svg>
                  </span>
                </button>
              );
            })}
          </div>
        </div>
      </div>
    </div>
  );
}

/* ───────────── shell ───────────── */
export default function News() {
  const { t } = useLanguage();
  const { isAdmin } = useAuth();
  const [items, setItems] = useState(null);
  const [error, setError] = useState("");
  const [refreshing, setRefreshing] = useState(false);
  const [tabs, setTabs] = useState([]); // [{ id }] — activity ids
  const [activeId, setActiveId] = useState(null); // null = fixed feed tab

  const load = useCallback(async () => {
    try {
      const { data: res } = await jortService.activities(60);
      const list = res.data || [];
      setItems(list);
      setError("");
      // Mark the feed as seen so the sidebar dot clears.
      const newest = list.reduce((max, a) => (a?.date && a.date > max ? a.date : max), "");
      localStorage.setItem(NEWS_SEEN_KEY, newest || new Date().toISOString());
      window.dispatchEvent(new Event("news-seen"));
    } catch {
      setError(t("jort.empty"));
      setItems([]);
    }
  }, [t]);

  useEffect(() => {
    load();
    // Keep the feed live — new texts appear without a manual reload.
    const id = setInterval(load, 120000);
    return () => clearInterval(id);
  }, [load]);

  const refresh = async () => {
    if (refreshing) return;
    setRefreshing(true);
    try {
      if (isAdmin) {
        try { await jortService.refresh(); } catch { /* plain reload below */ }
      }
      await load();
    } finally {
      setRefreshing(false);
    }
  };

  const openTab = (activity) => {
    setTabs((prev) => (prev.some((x) => x.id === activity.id) ? prev : [...prev, { id: activity.id }]));
    setActiveId(activity.id);
  };

  const closeTab = (id) => {
    setTabs((prev) => {
      const next = prev.filter((x) => x.id !== id);
      if (activeId === id) {
        const fallback = next[next.length - 1];
        setActiveId(fallback ? fallback.id : null);
      }
      return next;
    });
  };

  const activityFor = (id) => (items || []).find((a) => a.id === id);
  const activeActivity = activeId ? activityFor(activeId) : null;
  const feedActive = !activeId || !activeActivity;

  const shortTitle = (title = "") => (title.length > 34 ? `${title.slice(0, 31)}…` : title || "…");

  return (
    <div className="h-full flex flex-col bg-sand">
      {/* Chrome-style tab bar */}
      <div className="shrink-0 flex items-end bg-sand px-2 pt-1.5 border-b border-border overflow-x-auto scrollbar-hide">
        {/* Fixed feed tab */}
        <button onClick={() => setActiveId(null)} className={`${TAB_BASE} ${feedActive ? TAB_ACTIVE : TAB_IDLE}`}>
          <svg className={`w-3.5 h-3.5 shrink-0 ${feedActive ? "text-brand" : ""}`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d={NEWS_ICON} />
          </svg>
          {t("news.tabFeed")}
          {feedActive && <span className="absolute bottom-0 left-2 right-2 h-[2px] rounded-full bg-brand" />}
        </button>

        {/* One closeable onglet per opened text */}
        {tabs.map((tab) => {
          const activity = activityFor(tab.id);
          const active = activeId === tab.id && Boolean(activity);
          return (
            <button
              key={tab.id}
              onClick={() => setActiveId(tab.id)}
              className={`${TAB_BASE} ${active ? TAB_ACTIVE : TAB_IDLE} max-w-[240px]`}
              title={activity?.title}
            >
              <svg className={`w-3.5 h-3.5 shrink-0 ${active ? "text-brand" : ""}`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d={DOC_ICON} />
              </svg>
              <span className="truncate">{shortTitle(activity?.title)}</span>
              <span
                role="button"
                tabIndex={0}
                onClick={(e) => { e.stopPropagation(); closeTab(tab.id); }}
                onKeyDown={(e) => {
                  if (e.key === "Enter") { e.stopPropagation(); closeTab(tab.id); }
                }}
                className="ml-0.5 w-4 h-4 rounded-md flex items-center justify-center text-muted hover:text-dark hover:bg-dark/10 transition-colors shrink-0"
                aria-label={t("chat.closeTab")}
              >
                <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2.5}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                </svg>
              </span>
              {active && <span className="absolute bottom-0 left-2 right-2 h-[2px] rounded-full bg-brand" />}
            </button>
          );
        })}
      </div>

      {/* content area */}
      <div className="flex-1 min-h-0 bg-white">
        {feedActive ? (
          <Feed items={items} error={error} refreshing={refreshing} onRefresh={refresh} onOpen={openTab} t={t} />
        ) : (
          <PdfTab key={activeActivity.id} activity={activeActivity} t={t} />
        )}
      </div>
    </div>
  );
}
