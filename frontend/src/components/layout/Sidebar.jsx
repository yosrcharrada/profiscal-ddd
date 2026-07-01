import { NavLink, Link } from "react-router-dom";
import { useAuth } from "../../context/AuthContext";
import { useLanguage } from "../../context/LanguageContext";

const ICONS = {
  dashboard: (
    <path
      strokeLinecap="round"
      strokeLinejoin="round"
      d="M3.75 6A2.25 2.25 0 016 3.75h2.25A2.25 2.25 0 0110.5 6v2.25a2.25 2.25 0 01-2.25 2.25H6a2.25 2.25 0 01-2.25-2.25V6zM3.75 15.75A2.25 2.25 0 016 13.5h2.25a2.25 2.25 0 012.25 2.25V18a2.25 2.25 0 01-2.25 2.25H6A2.25 2.25 0 013.75 18v-2.25zM13.5 6a2.25 2.25 0 012.25-2.25H18A2.25 2.25 0 0120.25 6v2.25A2.25 2.25 0 0118 10.5h-2.25a2.25 2.25 0 01-2.25-2.25V6zM13.5 15.75a2.25 2.25 0 012.25-2.25H18a2.25 2.25 0 012.25 2.25V18A2.25 2.25 0 0118 20.25h-2.25A2.25 2.25 0 0113.5 18v-2.25z"
    />
  ),
  search: (
    <path
      strokeLinecap="round"
      strokeLinejoin="round"
      d="M21 21l-5.197-5.197m0 0A7.5 7.5 0 105.196 5.196a7.5 7.5 0 0010.607 10.607z"
    />
  ),
  chat: (
    <path
      strokeLinecap="round"
      strokeLinejoin="round"
      d="M9.813 15.904L9 18.75l-.813-2.846a4.5 4.5 0 00-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 003.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 003.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 00-3.09 3.09zM18.259 8.715L18 9.75l-.259-1.035a3.375 3.375 0 00-2.455-2.456L14.25 6l1.036-.259a3.375 3.375 0 002.455-2.456L18 2.25l.259 1.035a3.375 3.375 0 002.456 2.456L21.75 6l-1.035.259a3.375 3.375 0 00-2.456 2.456z"
    />
  ),
  docs: (
    <path
      strokeLinecap="round"
      strokeLinejoin="round"
      d="M19.5 14.25v-2.625a3.375 3.375 0 00-3.375-3.375h-1.5A1.125 1.125 0 0113.5 7.125v-1.5a3.375 3.375 0 00-3.375-3.375H8.25m2.25 0H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 00-9-9z"
    />
  ),
  users: (
    <path
      strokeLinecap="round"
      strokeLinejoin="round"
      d="M15 19.128a9.38 9.38 0 002.625.372 9.337 9.337 0 004.121-.952 4.125 4.125 0 00-7.533-2.493M15 19.128v-.003c0-1.113-.285-2.16-.786-3.07M15 19.128v.106A12.318 12.318 0 018.624 21c-2.331 0-4.512-.645-6.374-1.766l-.001-.109a6.375 6.375 0 0111.964-3.07M12 6.375a3.375 3.375 0 11-6.75 0 3.375 3.375 0 016.75 0zm8.25 2.25a2.625 2.625 0 11-5.25 0 2.625 2.625 0 015.25 0z"
    />
  ),
  settings: (
    <>
      <path
        strokeLinecap="round"
        strokeLinejoin="round"
        d="M9.594 3.94c.09-.542.56-.94 1.11-.94h2.593c.55 0 1.02.398 1.11.94l.213 1.281c.063.374.313.686.645.87.074.04.147.083.22.127.324.196.72.257 1.075.124l1.217-.456a1.125 1.125 0 011.37.49l1.296 2.247a1.125 1.125 0 01-.26 1.431l-1.003.827c-.293.24-.438.613-.431.992a6.759 6.759 0 010 .255c-.007.378.138.75.43.99l1.005.828c.424.35.534.954.26 1.43l-1.298 2.247a1.125 1.125 0 01-1.369.491l-1.217-.456c-.355-.133-.75-.072-1.076.124a6.57 6.57 0 01-.22.128c-.331.183-.581.495-.644.869l-.213 1.28c-.09.543-.56.941-1.11.941h-2.594c-.55 0-1.02-.398-1.11-.94l-.213-1.281c-.062-.374-.312-.686-.644-.87a6.52 6.52 0 01-.22-.127c-.325-.196-.72-.257-1.076-.124l-1.217.456a1.125 1.125 0 01-1.369-.49l-1.297-2.247a1.125 1.125 0 01.26-1.431l1.004-.827c.292-.24.437-.613.43-.992a6.932 6.932 0 010-.255c.007-.378-.138-.75-.43-.99l-1.004-.828a1.125 1.125 0 01-.26-1.43l1.297-2.247a1.125 1.125 0 011.37-.491l1.216.456c.356.133.751.072 1.076-.124.072-.044.146-.087.22-.128.332-.183.582-.495.644-.869l.214-1.281z"
      />
      <path
        strokeLinecap="round"
        strokeLinejoin="round"
        d="M15 12a3 3 0 11-6 0 3 3 0 016 0z"
      />
    </>
  ),
  panel: (
    <>
      <path
        strokeLinecap="round"
        strokeLinejoin="round"
        d="M3.75 5.25a1.5 1.5 0 011.5-1.5h13.5a1.5 1.5 0 011.5 1.5v13.5a1.5 1.5 0 01-1.5 1.5H5.25a1.5 1.5 0 01-1.5-1.5V5.25z"
      />
      <path strokeLinecap="round" d="M9.75 3.75v16.5" />
    </>
  ),
};

function Tip({ label }) {
  return (
    <span className="pointer-events-none absolute left-full top-1/2 -translate-y-1/2 ml-2.5 px-2 py-1 rounded-md bg-dark text-white text-[10px] font-semibold whitespace-nowrap opacity-0 -translate-x-1 group-hover:opacity-100 group-hover:translate-x-0 transition-all duration-150 shadow-lg z-[60]">
      {label}
    </span>
  );
}

function Item({ to, label, icon, badge, onNavigate, collapsed }) {
  return (
    <NavLink
      to={to}
      onClick={onNavigate}
      className={({ isActive }) =>
        `group relative flex items-center text-[13px] font-medium transition-all duration-150 ${
          collapsed
            ? "justify-center gap-0 w-9 h-9 mx-auto rounded-lg"
            : "gap-2.5 px-2.5 py-[7px]"
        } ${
          isActive
            ? "text-[#e9d200] font-semibold"
            : "text-body hover:text-dark"
        }`
      }
    >
      {({ isActive }) => (
        <>
          <svg
            className={`w-4 h-4 shrink-0 transition-colors ${isActive ? "text-[#e9d200]" : "text-muted dark:text-[#a0a0b0] group-hover:text-body dark:group-hover:text-[#d0d0dd]"}`}
            fill="none"
            viewBox="0 0 24 24"
            stroke="currentColor"
            strokeWidth={1.8}
          >
            {icon}
          </svg>
          <span
            className={`whitespace-nowrap overflow-hidden transition-all duration-200 ${
              collapsed
                ? "max-w-0 opacity-0"
                : "max-w-[140px] opacity-100 flex-1"
            }`}
          >
            {label}
          </span>
          {badge && !collapsed && (
            <span
              className={`text-[9px] font-bold rounded-full px-1.5 py-0.5 uppercase tracking-wide ${isActive ? "bg-brand text-dark" : "bg-brand/30 text-dark"}`}
            >
              {badge}
            </span>
          )}
          {collapsed && <Tip label={label} />}
        </>
      )}
    </NavLink>
  );
}

function SectionLabel({ children, collapsed }) {
  return (
    <p
      className={`mb-1.5 text-[10px] font-semibold text-muted uppercase tracking-[0.12em] whitespace-nowrap overflow-hidden transition-all duration-200 ${
        collapsed ? "opacity-0 max-h-0 mb-0" : "opacity-100 max-h-4 px-2.5"
      }`}
    >
      {children}
    </p>
  );
}

export default function Sidebar({ onNavigate, collapsed = false, onToggle }) {
  const { isAdmin } = useAuth();
  const { t } = useLanguage();
  return (
    <div className="h-full flex flex-col overflow-visible">
      <div
        className={`h-12 flex items-center shrink-0 ${
          collapsed ? "justify-center px-2" : "justify-between pl-4 pr-2"
        }`}
      >
        {collapsed ? (
          <Link
            to="/dashboard"
            onClick={onNavigate}
            className="flex flex-col items-center"
            aria-label="EY TAXMIND — Dashboard"
          >
            <svg width="18" height="5" viewBox="0 0 26 8" aria-hidden="true">
              <polygon points="0,8 26,0 26,8" fill="#FFE600" />
            </svg>
            <span className="text-[12px] font-bold text-dark dark:text-white leading-none mt-[2px]">
              EY
            </span>
          </Link>
        ) : (
          <>
            <Link
              to="/dashboard"
              onClick={onNavigate}
              className="inline-flex items-center gap-2"
            >
              <span className="flex flex-col items-start">
                <svg
                  width="18"
                  height="5"
                  viewBox="0 0 26 8"
                  aria-hidden="true"
                >
                  <polygon points="0,8 26,0 26,8" fill="#FFE600" />
                </svg>
                <span className="text-[13px] font-bold text-dark dark:text-white leading-none mt-[2px]">
                  EY
                </span>
              </span>
              <span className="h-4 w-px bg-border" />
              <span className="text-[11px] font-semibold tracking-[0.18em] text-dark/70 dark:text-[#b0b0c0]">
                TAXMIND
              </span>
            </Link>
            {onToggle && (
              <button
                onClick={onToggle}
                className="w-7 h-7 rounded-md text-muted dark:text-[#a0a0b0] hover:text-dark dark:hover:text-white hover:bg-light/80 flex items-center justify-center transition-colors"
                aria-label="Collapse sidebar"
                title="Toggle (⌘B)"
              >
                <svg
                  className="w-4 h-4"
                  fill="none"
                  viewBox="0 0 24 24"
                  stroke="currentColor"
                  strokeWidth={1.8}
                >
                  {ICONS.panel}
                </svg>
              </button>
            )}
          </>
        )}
      </div>

      {collapsed && onToggle && (
        <div className="flex justify-center pt-2 shrink-0">
          <button
            onClick={onToggle}
            className="group relative w-8 h-8 rounded-md text-muted dark:text-[#a0a0b0] hover:text-dark dark:hover:text-white hover:bg-light/80 flex items-center justify-center transition-colors"
            aria-label="Expand sidebar"
          >
            <svg
              className="w-4 h-4"
              fill="none"
              viewBox="0 0 24 24"
              stroke="currentColor"
              strokeWidth={1.8}
            >
              {ICONS.panel}
            </svg>
            <Tip label="Expand (⌘B)" />
          </button>
        </div>
      )}

      <nav className={`flex-1 py-3 space-y-4 ${collapsed ? "px-1.5" : "px-2"}`}>
        <div>
          <SectionLabel collapsed={collapsed}>
            {t("sidebar.workspace")}
          </SectionLabel>
          <div className="space-y-0.5">
            <Item
              to="/dashboard"
              label={t("sidebar.dashboard")}
              icon={ICONS.dashboard}
              onNavigate={onNavigate}
              collapsed={collapsed}
            />
            <Item
              to="/app/search"
              label={t("sidebar.search")}
              icon={ICONS.search}
              onNavigate={onNavigate}
              collapsed={collapsed}
            />
            <Item
              to="/app/chat"
              label={t("sidebar.chat")}
              icon={ICONS.chat}
              onNavigate={onNavigate}
              collapsed={collapsed}
            />
            <Item
              to="/app/consultations"
              label={t("sidebar.consultations")}
              icon={ICONS.docs}
              onNavigate={onNavigate}
              collapsed={collapsed}
            />
          </div>
        </div>
        {isAdmin && (
          <div>
            <SectionLabel collapsed={collapsed}>
              {t("sidebar.admin")}
            </SectionLabel>
            {collapsed && (
              <div className="mx-1.5 mb-2 border-t border-border/50" />
            )}
            <div className="space-y-0.5">
              <Item
                to="/admin/users"
                label={t("sidebar.users")}
                icon={ICONS.users}
                badge="Admin"
                onNavigate={onNavigate}
                collapsed={collapsed}
              />
            </div>
          </div>
        )}
      </nav>

      <div
        className={`py-2 border-t border-border/40 space-y-0.5 shrink-0 ${collapsed ? "px-1.5" : "px-2"}`}
      >
        <Item
          to="/settings"
          label={t("sidebar.settings")}
          icon={ICONS.settings}
          onNavigate={onNavigate}
          collapsed={collapsed}
        />
      </div>
    </div>
  );
}
