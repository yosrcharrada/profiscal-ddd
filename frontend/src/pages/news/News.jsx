import { useCallback, useEffect, useState } from "react";
import { useAuth } from "../../context/AuthContext";
import { useLanguage } from "../../context/LanguageContext";
import { jortService } from "../../services/authService";

// localStorage key shared with the sidebar "new" dot — writing the newest activity
// date here (on view) clears the dot the next time the sidebar re-checks.
export const NEWS_SEEN_KEY = "taxmind.news.lastSeen";

const CAT_STYLE = {
  Tender: { bg: "bg-[#FFEDD5]", text: "text-[#C2410C]", border: "border-[#FDBA74]" },
  Plan: { bg: "bg-[#EDE9FE]", text: "text-[#5B21B6]", border: "border-[#C4B5FD]" },
  Publication: { bg: "bg-[#DBEAFE]", text: "text-[#1D4ED8]", border: "border-[#93C5FD]" },
  Notice: { bg: "bg-brand/20", text: "text-dark", border: "border-brand/40" },
};

const fmt = (d, fallback) =>
  fallback ||
  (d
    ? new Date(d).toLocaleDateString(undefined, {
        day: "numeric",
        month: "short",
        year: "numeric",
      })
    : "—");

export default function News() {
  const { t } = useLanguage();
  const { isAdmin } = useAuth();
  const [items, setItems] = useState(null);
  const [error, setError] = useState("");
  const [refreshing, setRefreshing] = useState(false);

  const load = useCallback(async () => {
    try {
      const { data: res } = await jortService.activities(50);
      const list = res.data || [];
      setItems(list);
      setError("");
      // Mark the feed as seen so the sidebar dot clears.
      const newest = list.reduce(
        (max, a) => (a?.date && a.date > max ? a.date : max),
        "",
      );
      localStorage.setItem(NEWS_SEEN_KEY, newest || new Date().toISOString());
      window.dispatchEvent(new Event("news-seen"));
    } catch {
      setError(t("jort.empty"));
      setItems([]);
    }
  }, [t]);

  useEffect(() => {
    load();
  }, [load]);

  const refresh = async () => {
    if (refreshing) return;
    setRefreshing(true);
    try {
      // Admins can trigger a live scrape; everyone else just re-reads the feed.
      if (isAdmin) {
        try {
          await jortService.refresh();
        } catch {
          /* fall through to a plain reload */
        }
      }
      await load();
    } finally {
      setRefreshing(false);
    }
  };

  return (
    <div className="h-full flex flex-col bg-sand">
      <div className="shrink-0 flex items-center justify-between px-4 sm:px-6 h-14 border-b border-border bg-white">
        <div className="flex items-center gap-2.5">
          <div className="w-8 h-8 rounded-lg bg-red-100 dark:bg-red-500/15 flex items-center justify-center">
            <svg
              className="w-4 h-4 text-red-500"
              fill="none"
              viewBox="0 0 24 24"
              stroke="currentColor"
              strokeWidth={1.8}
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                d="M12 7.5h1.5m-1.5 3h1.5m-7.5 3h7.5m-7.5 3h7.5m3-9h3.375c.621 0 1.125.504 1.125 1.125V18a2.25 2.25 0 01-2.25 2.25M16.5 7.5V18a2.25 2.25 0 002.25 2.25M16.5 7.5V4.875c0-.621-.504-1.125-1.125-1.125H4.125C3.504 3.75 3 4.254 3 4.875V18a2.25 2.25 0 002.25 2.25h13.5M6 7.5h3v3H6v-3z"
              />
            </svg>
          </div>
          <div>
            <h1 className="text-[15px] font-bold text-dark dark:text-white leading-tight">
              {t("jort.title")}
            </h1>
            <p className="text-[11px] text-muted">{t("news.subtitle")}</p>
          </div>
        </div>
        <button
          onClick={refresh}
          disabled={refreshing}
          className="text-[12px] font-semibold text-body hover:text-dark border border-border rounded-lg px-3 py-1.5 hover:bg-light/60 transition-colors disabled:opacity-50"
        >
          {refreshing ? t("jort.refreshing") : t("jort.refresh")}
        </button>
      </div>

      <div className="flex-1 overflow-y-auto">
        <div className="max-w-3xl mx-auto w-full px-3 sm:px-5 py-4">
          {error && items?.length === 0 && (
            <div className="mt-6 text-center text-muted text-[13px]">{error}</div>
          )}

          {items === null && (
            <div className="space-y-2">
              {[0, 1, 2, 3, 4].map((i) => (
                <div
                  key={i}
                  className="bg-white border border-border rounded-xl p-4 animate-pulse"
                  style={{ animationDelay: `${i * 80}ms` }}
                >
                  <div className="h-3.5 bg-light rounded w-1/3" />
                  <div className="mt-3 h-3 bg-light rounded w-full" />
                  <div className="mt-1.5 h-3 bg-light rounded w-4/5" />
                </div>
              ))}
            </div>
          )}

          {items?.length === 0 && !error && (
            <div className="mt-10 text-center text-muted text-[13px]">
              {t("jort.empty")}
            </div>
          )}

          <div className="space-y-1.5">
            {(items || []).map((a, i) => {
              const cat = CAT_STYLE[a.category] || {
                bg: "bg-light",
                text: "text-body",
                border: "border-border",
              };
              return (
                <div
                  key={a.id || i}
                  style={{ animationDelay: `${Math.min(i, 8) * 40}ms` }}
                  className="bg-white border border-border rounded-xl px-4 py-3 hover:shadow-sm transition-all animate-pop opacity-0"
                >
                  <div className="flex items-center gap-2 flex-wrap mb-1">
                    <span
                      className={`text-[9px] font-bold rounded-full px-2 py-0.5 border ${cat.bg} ${cat.text} ${cat.border}`}
                    >
                      {t(`jort.cat.${a.category}`)}
                    </span>
                    {a.isNew && (
                      <span className="text-[9px] font-bold rounded-full px-2 py-0.5 bg-red-500 text-white">
                        {t("jort.new")}
                      </span>
                    )}
                    <span className="text-[11px] text-muted ml-auto shrink-0">
                      {fmt(a.date, a.dateText)}
                    </span>
                  </div>
                  <p
                    className="text-[13px] font-semibold text-dark dark:text-white leading-snug"
                    dir="auto"
                  >
                    {a.title}
                  </p>
                  {a.description && (
                    <p
                      className="text-[12px] text-body dark:text-[#c0c0cc] mt-1 leading-relaxed"
                      dir="auto"
                    >
                      {a.description}
                    </p>
                  )}
                </div>
              );
            })}
          </div>
        </div>
      </div>
    </div>
  );
}
