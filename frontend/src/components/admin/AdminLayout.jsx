import { useNavigate, useLocation } from "react-router-dom";
import { useLanguage } from "../../context/LanguageContext";

/**
 * Shared shell for every admin page — same layout as the fiscal Search page:
 * a sand background with a Chrome-style tab bar whose active tab is a raised
 * white "onglet" connecting into the white content area below.
 *
 * - Fixed section tabs (Overview, Users, Reclamations, Activity, Knowledge)
 *   route between pages and can't be closed.
 * - `extraTabs` are transient onglets opened by an action (e.g. "New user"):
 *   highlighted while active and closeable with an ✕, just like a browser tab.
 */
const SECTIONS = [
  {
    to: "/admin",
    end: true,
    key: "sidebar.adminOverview",
    color: "text-brand",
    activeBg: "bg-brand",
    icon: "M3 13.125C3 12.504 3.504 12 4.125 12h2.25c.621 0 1.125.504 1.125 1.125v6.75C7.5 20.496 6.996 21 6.375 21h-2.25A1.125 1.125 0 013 19.875v-6.75zM9.75 8.625c0-.621.504-1.125 1.125-1.125h2.25c.621 0 1.125.504 1.125 1.125v11.25c0 .621-.504 1.125-1.125 1.125h-2.25a1.125 1.125 0 01-1.125-1.125V8.625zM16.5 4.125c0-.621.504-1.125 1.125-1.125h2.25C20.496 3 21 3.504 21 4.125v15.75c0 .621-.504 1.125-1.125 1.125h-2.25a1.125 1.125 0 01-1.125-1.125V4.125z",
  },
  {
    to: "/admin/users",
    key: "sidebar.users",
    color: "text-blue-500",
    activeBg: "bg-blue-500",
    icon: "M15 19.128a9.38 9.38 0 002.625.372 9.337 9.337 0 004.121-.952 4.125 4.125 0 00-7.533-2.493M15 19.128v-.003c0-1.113-.285-2.16-.786-3.07M15 19.128v.106A12.318 12.318 0 018.624 21c-2.331 0-4.512-.645-6.374-1.766l-.001-.109a6.375 6.375 0 0111.964-3.07M12 6.375a3.375 3.375 0 11-6.75 0 3.375 3.375 0 016.75 0zm8.25 2.25a2.625 2.625 0 11-5.25 0 2.625 2.625 0 015.25 0z",
  },
  {
    to: "/admin/reclamations",
    key: "sidebar.reclamations",
    color: "text-orange-500",
    activeBg: "bg-orange-500",
    icon: "M12 9v3.75m9-.75a9 9 0 11-18 0 9 9 0 0118 0zm-9 3.75h.008v.008H12v-.008z",
  },
  {
    to: "/admin/activity",
    key: "sidebar.activity",
    color: "text-purple-500",
    activeBg: "bg-purple-500",
    icon: "M3.75 12h3l2.25-6 4.5 12 2.25-6h4.5",
  },
  {
    to: "/admin/knowledge",
    key: "sidebar.knowledge",
    color: "text-emerald-500",
    activeBg: "bg-emerald-500",
    icon: "M7.5 21L3 16.5m0 0L7.5 12M3 16.5h13.5m0-13.5L21 7.5m0 0L16.5 12M21 7.5H7.5",
  },
];

const TAB_BASE =
  "relative flex items-center gap-1.5 text-[12.5px] font-semibold px-4 py-2 rounded-t-lg whitespace-nowrap transition-all duration-200";
const TAB_ACTIVE =
  "bg-white text-dark border border-border border-b-white -mb-px z-10";
const TAB_IDLE =
  "text-muted hover:text-dark hover:bg-white/50 border border-transparent";

export default function AdminLayout({ extraTabs = [], badges = {}, children }) {
  const { t } = useLanguage();
  const navigate = useNavigate();
  const { pathname } = useLocation();

  const activeExtra = extraTabs.some((x) => x.active);

  return (
    <div className="h-full flex flex-col bg-sand">
      {/* Chrome-style tab bar */}
      <div className="shrink-0 flex items-end bg-sand px-2 pt-1.5 border-b border-border overflow-x-auto scrollbar-hide">
        {SECTIONS.map((s) => {
          const active =
            !activeExtra &&
            (s.end ? pathname === s.to : pathname.startsWith(s.to));
          const badge = badges[s.to];
          return (
            <button
              key={s.to}
              onClick={() => navigate(s.to)}
              className={`${TAB_BASE} ${active ? TAB_ACTIVE : TAB_IDLE}`}
            >
              <svg
                className={`w-3.5 h-3.5 shrink-0 ${active ? s.color : ""}`}
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                strokeWidth={2}
              >
                <path strokeLinecap="round" strokeLinejoin="round" d={s.icon} />
              </svg>
              {t(s.key)}
              {badge > 0 && (
                <span className="min-w-[17px] h-[17px] px-1 rounded-full bg-brand text-[#2e2e38] text-[10px] font-bold flex items-center justify-center">
                  {badge}
                </span>
              )}
              {active && (
                <span
                  className={`absolute bottom-0 left-2 right-2 h-[2px] rounded-full ${s.activeBg}`}
                />
              )}
            </button>
          );
        })}

        {extraTabs.map((tab) => (
          <button
            key={tab.id}
            onClick={tab.onActivate}
            className={`${TAB_BASE} ${tab.active ? TAB_ACTIVE : TAB_IDLE}`}
          >
            {tab.iconPath && (
              <svg
                className={`w-3.5 h-3.5 shrink-0 ${tab.active ? tab.color || "text-brand" : ""}`}
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                strokeWidth={2}
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  d={tab.iconPath}
                />
              </svg>
            )}
            {tab.label}
            <span
              role="button"
              tabIndex={0}
              onClick={(e) => {
                e.stopPropagation();
                tab.onClose();
              }}
              onKeyDown={(e) => {
                if (e.key === "Enter") {
                  e.stopPropagation();
                  tab.onClose();
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
            {tab.active && (
              <span
                className={`absolute bottom-0 left-2 right-2 h-[2px] rounded-full ${tab.activeBg || "bg-brand"}`}
              />
            )}
          </button>
        ))}
      </div>

      {/* White content area */}
      <div className="flex-1 min-h-0 overflow-y-auto bg-white">
        <div className="w-full max-w-[1100px] mx-auto px-4 sm:px-6 py-10">
          {children}
        </div>
      </div>
    </div>
  );
}
