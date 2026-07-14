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
  overview: (
    <path
      strokeLinecap="round"
      strokeLinejoin="round"
      d="M3 13.125C3 12.504 3.504 12 4.125 12h2.25c.621 0 1.125.504 1.125 1.125v6.75C7.5 20.496 6.996 21 6.375 21h-2.25A1.125 1.125 0 013 19.875v-6.75zM9.75 8.625c0-.621.504-1.125 1.125-1.125h2.25c.621 0 1.125.504 1.125 1.125v11.25c0 .621-.504 1.125-1.125 1.125h-2.25a1.125 1.125 0 01-1.125-1.125V8.625zM16.5 4.125c0-.621.504-1.125 1.125-1.125h2.25C20.496 3 21 3.504 21 4.125v15.75c0 .621-.504 1.125-1.125 1.125h-2.25a1.125 1.125 0 01-1.125-1.125V4.125z"
    />
  ),
  tasks: (
    <path
      strokeLinecap="round"
      strokeLinejoin="round"
      d="M11.35 3.836c-.065.21-.1.433-.1.664 0 .414.336.75.75.75h4.5a.75.75 0 00.75-.75 2.25 2.25 0 00-.1-.664m-5.8 0A2.251 2.251 0 0113.5 2.25H15c1.012 0 1.867.668 2.15 1.586m-5.8 0c-.376.023-.75.05-1.124.08C9.095 4.01 8.25 4.973 8.25 6.108V8.25m8.9-4.414c.376.023.75.05 1.124.08 1.131.094 1.976 1.057 1.976 2.192V16.5A2.25 2.25 0 0118 18.75h-2.25m-7.5-10.5H4.875c-.621 0-1.125.504-1.125 1.125v11.25c0 .621.504 1.125 1.125 1.125h9.75c.621 0 1.125-.504 1.125-1.125V18.75m-7.5-10.5h6.375c.621 0 1.125.504 1.125 1.125v9.375m-8.25-3l1.5 1.5 3-3.75"
    />
  ),
  team: (
    <path
      strokeLinecap="round"
      strokeLinejoin="round"
      d="M18 18.72a9.094 9.094 0 003.741-.479 3 3 0 00-4.682-2.72m.94 3.198l.001.031c0 .225-.012.447-.037.666A11.944 11.944 0 0112 21c-2.17 0-4.207-.576-5.963-1.584A6.062 6.062 0 016 18.719m12 0a5.971 5.971 0 00-.941-3.197m0 0A5.995 5.995 0 0012 12.75a5.995 5.995 0 00-5.058 2.772m0 0a3 3 0 00-4.681 2.72 8.986 8.986 0 003.74.477m.94-3.197a5.971 5.971 0 00-.94 3.197M15 6.75a3 3 0 11-6 0 3 3 0 016 0zm6 3a2.25 2.25 0 11-4.5 0 2.25 2.25 0 014.5 0zm-13.5 0a2.25 2.25 0 11-4.5 0 2.25 2.25 0 014.5 0z"
    />
  ),
  bug: (
    <path
      strokeLinecap="round"
      strokeLinejoin="round"
      d="M12 12.75c1.148 0 2.278.08 3.383.237 1.037.146 1.866.966 1.866 2.013 0 3.728-2.35 6.75-5.25 6.75S6.75 18.728 6.75 15c0-1.046.83-1.867 1.866-2.013A24.204 24.204 0 0112 12.75zm0 0c2.883 0 5.647.508 8.207 1.44a23.91 23.91 0 01-1.152 6.06M12 12.75c-2.883 0-5.647.508-8.208 1.44.125 2.104.52 4.136 1.153 6.06M12 12.75a2.25 2.25 0 002.248-2.354M12 12.75a2.25 2.25 0 01-2.248-2.354M12 8.25c.995 0 1.971-.08 2.922-.236.403-.066.74-.358.795-.762a3.778 3.778 0 00-.399-2.25M12 8.25c-.995 0-1.97-.08-2.922-.236-.402-.066-.74-.358-.795-.762a3.734 3.734 0 01.4-2.253M12 8.25a2.25 2.25 0 00-2.248 2.146M12 8.25a2.25 2.25 0 012.248 2.146M8.683 5a6.032 6.032 0 01-1.155-1.002c.07-.63.27-1.222.574-1.747m.581 2.749A3.75 3.75 0 0115.318 5m0 0c.427-.283.815-.62 1.155-.999a4.471 4.471 0 00-.575-1.752M4.921 6a24.048 24.048 0 00-.392 3.314c1.668.546 3.416.914 5.223 1.082M19.08 6c.205 1.08.337 2.187.392 3.314a23.882 23.882 0 01-5.223 1.082"
    />
  ),
  pulse: (
    <path
      strokeLinecap="round"
      strokeLinejoin="round"
      d="M3.75 12h3l2.25-6 4.5 12 2.25-6h4.5"
    />
  ),
  graph: (
    <path
      strokeLinecap="round"
      strokeLinejoin="round"
      d="M7.5 21L3 16.5m0 0L7.5 12M3 16.5h13.5m0-13.5L21 7.5m0 0L16.5 12M21 7.5H7.5"
    />
  ),
  support: (
    <path
      strokeLinecap="round"
      strokeLinejoin="round"
      d="M9.879 7.519c1.171-1.025 3.071-1.025 4.242 0 1.172 1.025 1.172 2.687 0 3.712-.203.179-.43.326-.67.442-.745.361-1.45.999-1.45 1.827v.75M21 12a9 9 0 11-18 0 9 9 0 0118 0zm-9 5.25h.008v.008H12v-.008z"
    />
  ),
};

function Tip({ label }) {
  return (
    <span className="pointer-events-none absolute left-full top-1/2 -translate-y-1/2 ml-2.5 px-2 py-1 rounded-md bg-dark text-white text-[10px] font-semibold whitespace-nowrap opacity-0 -translate-x-1 group-hover:opacity-100 group-hover:translate-x-0 transition-all duration-150 shadow-lg z-[60]">
      {label}
    </span>
  );
}

function Item({ to, label, icon, badge, onNavigate, collapsed, end }) {
  return (
    <NavLink
      to={to}
      end={end}
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
  const { isAdmin, isManager, isConsultant } = useAuth();
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
            {isConsultant && (
              <Item
                to="/app/tasks"
                label={t("sidebar.tasks")}
                icon={ICONS.tasks}
                onNavigate={onNavigate}
                collapsed={collapsed}
              />
            )}
          </div>
        </div>
        {isManager && (
          <div>
            <SectionLabel collapsed={collapsed}>
              {t("sidebar.management")}
            </SectionLabel>
            {collapsed && (
              <div className="mx-1.5 mb-2 border-t border-border/50" />
            )}
            <div className="space-y-0.5">
              <Item
                to="/manager/consultants"
                label={t("sidebar.consultants")}
                icon={ICONS.team}
                badge="Team"
                onNavigate={onNavigate}
                collapsed={collapsed}
              />
            </div>
          </div>
        )}
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
                to="/admin"
                end
                label={t("sidebar.adminOverview")}
                icon={ICONS.overview}
                onNavigate={onNavigate}
                collapsed={collapsed}
              />
              <Item
                to="/admin/users"
                label={t("sidebar.users")}
                icon={ICONS.users}
                onNavigate={onNavigate}
                collapsed={collapsed}
              />
              <Item
                to="/admin/reclamations"
                label={t("sidebar.reclamations")}
                icon={ICONS.bug}
                onNavigate={onNavigate}
                collapsed={collapsed}
              />
              <Item
                to="/admin/activity"
                label={t("sidebar.activity")}
                icon={ICONS.pulse}
                onNavigate={onNavigate}
                collapsed={collapsed}
              />
              <Item
                to="/admin/knowledge"
                label={t("sidebar.knowledge")}
                icon={ICONS.graph}
                badge="Soon"
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
          to="/app/support"
          label={t("sidebar.support")}
          icon={ICONS.support}
          onNavigate={onNavigate}
          collapsed={collapsed}
        />
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
