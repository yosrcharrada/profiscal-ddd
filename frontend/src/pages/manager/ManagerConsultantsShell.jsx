import { useCallback, useEffect, useMemo, useState } from "react";
import { Outlet, useLocation, useNavigate } from "react-router-dom";
import { taskService } from "../../services/authService";
import { useLanguage } from "../../context/LanguageContext";

/**
 * Chrome-style tab shell for the manager → consultants space (same look as the
 * admin AdminLayout). One fixed "Consultants" tab (the team list) that can't be
 * closed, plus a transient onglet per consultant the manager opens — each shows
 * the consultant's name and can be activated or closed like a browser tab.
 *
 * Open tabs live in sessionStorage so they survive route changes within the
 * space; opening a consultant by URL (or the badge on the list) adds its tab.
 */
const TAB_BASE =
  "relative flex items-center gap-1.5 text-[12.5px] font-semibold px-4 py-2 rounded-t-lg whitespace-nowrap transition-all duration-200";
const TAB_ACTIVE =
  "bg-white text-dark border border-border border-b-white -mb-px z-10";
const TAB_IDLE =
  "text-muted hover:text-dark hover:bg-white/50 border border-transparent";

const BASE = "/manager/consultants";
const STORAGE_KEY = "mgr.consultantTabs";

const TEAM_ICON =
  "M18 18.72a9.094 9.094 0 003.741-.479 3 3 0 00-4.682-2.72m.94 3.198l.001.031c0 .225-.012.447-.037.666A11.944 11.944 0 0112 21c-2.17 0-4.207-.576-5.963-1.584A6.062 6.062 0 016 18.719m12 0a5.971 5.971 0 00-.941-3.197m0 0A5.995 5.995 0 0012 12.75a5.995 5.995 0 00-5.058 2.772m0 0a3 3 0 00-4.681 2.72 8.986 8.986 0 003.74.477m.94-3.197a5.971 5.971 0 00-.94 3.197M15 6.75a3 3 0 11-6 0 3 3 0 016 0zm6 3a2.25 2.25 0 11-4.5 0 2.25 2.25 0 014.5 0zm-13.5 0a2.25 2.25 0 11-4.5 0 2.25 2.25 0 014.5 0z";
const PERSON_ICON =
  "M15.75 6a3.75 3.75 0 11-7.5 0 3.75 3.75 0 017.5 0zM4.501 20.118a7.5 7.5 0 0114.998 0A17.933 17.933 0 0112 21.75c-2.676 0-5.216-.584-7.499-1.632z";

const readStore = () => {
  try {
    return JSON.parse(sessionStorage.getItem(STORAGE_KEY) || "[]");
  } catch {
    return [];
  }
};

export default function ManagerConsultantsShell() {
  const { t } = useLanguage();
  const navigate = useNavigate();
  const location = useLocation();

  const selectedId = useMemo(() => {
    const m = location.pathname.match(/^\/manager\/consultants\/([^/]+)/);
    return m ? m[1] : null;
  }, [location.pathname]);

  const [tabs, setTabs] = useState(readStore); // [{ id, name }]
  const [nameMap, setNameMap] = useState({});

  // Resolve consultant names once so tabs get proper labels.
  useEffect(() => {
    taskService
      .consultants()
      .then(({ data: res }) => {
        const map = {};
        (res.data || []).forEach((c) => {
          map[c.id] = `${c.firstName ?? ""} ${c.lastName ?? ""}`.trim() || c.email;
        });
        setNameMap(map);
      })
      .catch(() => {});
  }, []);

  // Persist tabs across navigation.
  useEffect(() => {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(tabs));
  }, [tabs]);

  // When a consultant route is active, make sure it has an open tab.
  useEffect(() => {
    if (!selectedId) return;
    setTabs((prev) => {
      if (prev.some((x) => x.id === selectedId)) return prev;
      const name = location.state?.name || nameMap[selectedId] || "…";
      return [...prev, { id: selectedId, name }];
    });
  }, [selectedId, location.state, nameMap]);

  // Backfill labels once names arrive.
  useEffect(() => {
    if (!Object.keys(nameMap).length) return;
    setTabs((prev) =>
      prev.map((x) => (nameMap[x.id] ? { ...x, name: nameMap[x.id] } : x)),
    );
  }, [nameMap]);

  const closeTab = useCallback(
    (id) => {
      setTabs((prev) => {
        const next = prev.filter((x) => x.id !== id);
        if (selectedId === id) {
          const fallback = next[next.length - 1];
          navigate(fallback ? `${BASE}/${fallback.id}` : BASE);
        }
        return next;
      });
    },
    [selectedId, navigate],
  );

  const listActive = !selectedId;

  return (
    <div className="h-full flex flex-col bg-sand">
      {/* Chrome-style tab bar */}
      <div className="shrink-0 flex items-end bg-sand px-2 pt-1.5 border-b border-border overflow-x-auto scrollbar-hide">
        {/* Fixed "Consultants" tab */}
        <button
          onClick={() => navigate(BASE)}
          className={`${TAB_BASE} ${listActive ? TAB_ACTIVE : TAB_IDLE}`}
        >
          <svg
            className={`w-3.5 h-3.5 shrink-0 ${listActive ? "text-brand" : ""}`}
            fill="none"
            viewBox="0 0 24 24"
            stroke="currentColor"
            strokeWidth={2}
          >
            <path strokeLinecap="round" strokeLinejoin="round" d={TEAM_ICON} />
          </svg>
          {t("sidebar.consultants")}
          {listActive && (
            <span className="absolute bottom-0 left-2 right-2 h-[2px] rounded-full bg-brand" />
          )}
        </button>

        {/* One onglet per open consultant */}
        {tabs.map((tab) => {
          const active = selectedId === tab.id;
          return (
            <button
              key={tab.id}
              onClick={() => navigate(`${BASE}/${tab.id}`)}
              className={`${TAB_BASE} ${active ? TAB_ACTIVE : TAB_IDLE}`}
            >
              <svg
                className={`w-3.5 h-3.5 shrink-0 ${active ? "text-brand" : ""}`}
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                strokeWidth={2}
              >
                <path strokeLinecap="round" strokeLinejoin="round" d={PERSON_ICON} />
              </svg>
              {tab.name}
              <span
                role="button"
                tabIndex={0}
                onClick={(e) => {
                  e.stopPropagation();
                  closeTab(tab.id);
                }}
                onKeyDown={(e) => {
                  if (e.key === "Enter") {
                    e.stopPropagation();
                    closeTab(tab.id);
                  }
                }}
                className="ml-0.5 w-4 h-4 rounded-md flex items-center justify-center text-muted hover:text-dark hover:bg-dark/10 transition-colors"
                aria-label="Close tab"
              >
                <svg
                  className="w-3 h-3"
                  fill="none"
                  viewBox="0 0 24 24"
                  stroke="currentColor"
                  strokeWidth={2.5}
                >
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    d="M6 18L18 6M6 6l12 12"
                  />
                </svg>
              </span>
              {active && (
                <span className="absolute bottom-0 left-2 right-2 h-[2px] rounded-full bg-brand" />
              )}
            </button>
          );
        })}
      </div>

      {/* White content area */}
      <div className="flex-1 min-h-0 overflow-y-auto bg-white">
        <div className="w-full max-w-[1100px] mx-auto px-4 sm:px-6 py-10">
          <Outlet />
        </div>
      </div>
    </div>
  );
}
