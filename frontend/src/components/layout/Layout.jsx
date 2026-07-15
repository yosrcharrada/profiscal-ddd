import { useEffect, useRef, useState } from "react";
import { Link, Outlet, useLocation, useNavigate } from "react-router-dom";
import { useAuth } from "../../context/AuthContext";
import { useLanguage } from "../../context/LanguageContext";
import EYLockup from "../common/EYLockup";
import ThemeToggle from "../common/ThemeToggle";
import NotificationBell from "../common/NotificationBell";
import LanguageSwitch from "../common/LanguageSwitch";
import ChatBubble from "../fiscal/ChatBubble";
import SystemStatus from "../../pages/fiscal/SystemStatus";
import Sidebar from "./Sidebar";

function UserMenu() {
  const { user, logout } = useAuth();
  const { t } = useLanguage();
  const navigate = useNavigate();
  const [open, setOpen] = useState(false);
  const ref = useRef();
  useEffect(() => {
    const fn = (e) => {
      if (ref.current && !ref.current.contains(e.target)) setOpen(false);
    };
    document.addEventListener("mousedown", fn);
    return () => document.removeEventListener("mousedown", fn);
  }, []);
  const I =
    `${user?.firstName?.[0] || ""}${user?.lastName?.[0] || ""}`.toUpperCase();
  return (
    <div className="relative" ref={ref}>
      <button
        onClick={() => setOpen(!open)}
        className="flex items-center gap-1.5 px-1.5 py-1 rounded-lg hover:bg-light/80 transition-colors"
      >
        <div className="w-6 h-6 rounded-md bg-dark text-white text-[10px] font-bold flex items-center justify-center">
          {I}
        </div>
        <svg
          className={`w-3 h-3 text-muted transition-transform duration-150 ${open ? "rotate-180" : ""}`}
          fill="none"
          viewBox="0 0 24 24"
          stroke="currentColor"
          strokeWidth={2.5}
        >
          <path
            strokeLinecap="round"
            strokeLinejoin="round"
            d="M19.5 8.25l-7.5 7.5-7.5-7.5"
          />
        </svg>
      </button>
      {open && (
        <div className="absolute right-0 mt-1.5 w-56 bg-white rounded-xl shadow-xl shadow-dark/[0.08] border border-border/80 py-1 animate-scale-in origin-top-right z-50">
          <div className="px-3.5 py-2.5 border-b border-border/60">
            <p className="text-[13px] font-semibold text-dark">
              {user?.firstName} {user?.lastName}
            </p>
            <p className="text-[11px] text-muted mt-0.5 truncate">
              {user?.email}
            </p>
          </div>
          <Link
            to="/settings"
            onClick={() => setOpen(false)}
            className="flex items-center gap-2.5 px-3.5 py-2 text-[13px] text-body hover:bg-light/80 hover:text-dark transition-colors"
          >
            <svg
              className="w-3.5 h-3.5"
              fill="none"
              viewBox="0 0 24 24"
              stroke="currentColor"
              strokeWidth={1.5}
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                d="M15.75 6a3.75 3.75 0 11-7.5 0 3.75 3.75 0 017.5 0zM4.501 20.118a7.5 7.5 0 0114.998 0A17.933 17.933 0 0112 21.75c-2.676 0-5.216-.584-7.499-1.632z"
              />
            </svg>
            {t("sidebar.settings")}
          </Link>
          <Link
            to="/settings/password"
            onClick={() => setOpen(false)}
            className="flex items-center gap-2.5 px-3.5 py-2 text-[13px] text-body hover:bg-light/80 hover:text-dark transition-colors"
          >
            <svg
              className="w-3.5 h-3.5"
              fill="none"
              viewBox="0 0 24 24"
              stroke="currentColor"
              strokeWidth={1.5}
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                d="M16.5 10.5V6.75a4.5 4.5 0 10-9 0v3.75m-.75 11.25h10.5a2.25 2.25 0 002.25-2.25v-6.75a2.25 2.25 0 00-2.25-2.25H6.75a2.25 2.25 0 00-2.25 2.25v6.75a2.25 2.25 0 002.25 2.25z"
              />
            </svg>
            {t("settings.changePassword")}
          </Link>
          <div className="border-t border-border/60">
            <button
              onClick={async () => {
                await logout();
                navigate("/login");
              }}
              className="flex items-center gap-2.5 px-3.5 py-2 text-[13px] text-red-500 hover:bg-red-50 transition-colors w-full"
            >
              <svg
                className="w-3.5 h-3.5"
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                strokeWidth={1.5}
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  d="M15.75 9V5.25A2.25 2.25 0 0013.5 3h-6a2.25 2.25 0 00-2.25 2.25v13.5A2.25 2.25 0 007.5 21h6a2.25 2.25 0 002.25-2.25V15M12 9l-3 3m0 0l3 3m-3-3h12.75"
                />
              </svg>
              {t("settings.logout")}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

export default function Layout() {
  const { pathname } = useLocation();
  const [mobileNav, setMobileNav] = useState(false);
  const [collapsed, setCollapsed] = useState(
    () => localStorage.getItem("taxmind.sidebar.collapsed") === "1",
  );

  useEffect(() => {
    localStorage.setItem("taxmind.sidebar.collapsed", collapsed ? "1" : "0");
  }, [collapsed]);

  useEffect(() => {
    const fn = (e) => {
      if (
        (e.metaKey || e.ctrlKey) &&
        !e.shiftKey &&
        !e.altKey &&
        e.key.toLowerCase() === "b"
      ) {
        e.preventDefault();
        setCollapsed((c) => !c);
      }
    };
    window.addEventListener("keydown", fn);
    return () => window.removeEventListener("keydown", fn);
  }, []);

  const isChat = pathname === "/app/chat";
  const isSearch = pathname === "/app/search";
  const isConsultations = pathname.startsWith("/app/consultations");
  const isAdmin = pathname === "/admin" || pathname.startsWith("/admin/");
  const fullBleed = isChat || isSearch || isConsultations || isAdmin;
  const hideBubble = fullBleed;

  return (
    <div className="h-screen bg-sand flex overflow-hidden">
      <aside
        className={`hidden lg:block shrink-0 h-screen z-30 transition-[width] duration-250 ease-[cubic-bezier(.16,1,.3,1)] ${
          collapsed ? "w-[52px]" : "w-52"
        }`}
      >
        <Sidebar
          collapsed={collapsed}
          onToggle={() => setCollapsed((c) => !c)}
        />
      </aside>

      {mobileNav && (
        <div className="fixed inset-0 z-[70] lg:hidden">
          <div
            className="absolute inset-0 bg-dark/30 backdrop-blur-[3px] animate-fade-in"
            onClick={() => setMobileNav(false)}
          />
          <div className="absolute left-0 top-0 h-full w-56 animate-slide-in-left">
            <Sidebar onNavigate={() => setMobileNav(false)} />
          </div>
        </div>
      )}

      <div className="flex-1 min-w-0 h-screen flex flex-col overflow-hidden">
        <header className="h-11 shrink-0 flex items-center justify-between gap-3 px-3 sm:px-4">
          <div className="flex items-center gap-2 min-w-0">
            <button
              onClick={() => setMobileNav(true)}
              className="lg:hidden w-8 h-8 rounded-lg border border-border/60 text-muted flex items-center justify-center hover:bg-light/80 transition-colors"
              aria-label="Menu"
            >
              <svg
                className="w-4 h-4"
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                strokeWidth={1.8}
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  d="M3.75 6.75h16.5M3.75 12h16.5m-16.5 5.25h16.5"
                />
              </svg>
            </button>
            <span className="lg:hidden">
              <EYLockup dark compact />
            </span>
          </div>
          <div className="flex items-center gap-2">
            <LanguageSwitch />
            <ThemeToggle />
            <NotificationBell />
            <SystemStatus />
            <UserMenu />
          </div>
        </header>

        <main className="flex-1 min-h-0 pr-2 pb-2">
          {fullBleed ? (
            <div className="h-full bg-white rounded-2xl border-border/90 border shadow-sm overflow-hidden">
              <Outlet />
            </div>
          ) : (
            <div className="h-full bg-white rounded-2xl border-border/90 border shadow-sm overflow-y-auto">
              <div className="mx-auto w-full px-6 lg:px-6 pb-6 pt-9 max-w-[1100px]">
                <Outlet />
              </div>
            </div>
          )}
        </main>
      </div>

      {!hideBubble && <ChatBubble />}
    </div>
  );
}
