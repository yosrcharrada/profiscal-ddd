import { useEffect, useRef, useState } from "react";
import { Link, Outlet, useLocation, useNavigate } from "react-router-dom";
import { useAuth } from "../../context/AuthContext";
import EYLockup from "../common/EYLockup";
import ChatBubble from "../fiscal/ChatBubble";
import SystemStatus from "../../pages/fiscal/SystemStatus";
import Sidebar from "./Sidebar";
import ThemeToggle from "../common/ThemeToggle";

function UserMenu() {
  const { user, logout } = useAuth();
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
        className="flex items-center gap-2 px-1.5 py-1.5 rounded-xl hover:bg-light transition-colors"
      >
        <div className="w-6 h-6 rounded-lg bg-dark text-white text-[0.65rem] font-bold flex items-center justify-center">
          {I}
        </div>
        <svg
          className={`w-4 h-4 text-muted transition-transform duration-200 ${open ? "rotate-180" : ""}`}
          fill="none"
          viewBox="0 0 24 24"
          stroke="currentColor"
          strokeWidth={2}
        >
          <path
            strokeLinecap="round"
            strokeLinejoin="round"
            d="M19.5 8.25l-7.5 7.5-7.5-7.5"
          />
        </svg>
      </button>
      {open && (
        <div className="absolute right-0 mt-2 w-60 bg-white rounded-xl shadow-xl border border-border py-1 animate-scale-in origin-top-right z-50">
          <div className="px-4 py-3 border-b border-border">
            <p className="text-sm font-bold text-dark">
              {user?.firstName} {user?.lastName}
            </p>
            <p className="text-xs text-muted mt-0.5 truncate">{user?.email}</p>
          </div>
          <Link
            to="/settings"
            onClick={() => setOpen(false)}
            className="flex items-center gap-3 px-4 py-2.5 text-sm text-body hover:bg-light hover:text-dark transition-colors"
          >
            <svg
              className="w-4 h-4"
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
            Paramètres
          </Link>
          <Link
            to="/settings/password"
            onClick={() => setOpen(false)}
            className="flex items-center gap-3 px-4 py-2.5 text-sm text-body hover:bg-light hover:text-dark transition-colors"
          >
            <svg
              className="w-4 h-4"
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
            Mot de passe
          </Link>
          <div className="border-t border-border">
            <button
              onClick={async () => {
                await logout();
                navigate("/login");
              }}
              className="flex items-center gap-3 px-4 py-2.5 text-sm text-red-500 hover:bg-red-50 transition-colors w-full"
            >
              <svg
                className="w-4 h-4"
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
              Se déconnecter
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

/* App shell — fixed sidebar (Notion/Linear style), slim topbar with engine
   status + user menu, and the quick-ask bubble on every page except the
   full chat & the consultation workspace (which has its own assistant).
   The sidebar collapses to an icon rail (toggle or ⌘B), persisted. */
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
  const fullBleed = isChat || isSearch || isConsultations;
  /* the workspaces have their own assistant affordances — the floating bubble
     would overlap the inline document pane */
  const hideBubble = fullBleed;

  return (
    <div className="min-h-screen bg-sand flex">
      {/* desktop sidebar — animated icon rail ↔ full width */}
      <aside
        className={`hidden lg:block shrink-0 sticky top-0 h-screen z-30 transition-[width] duration-300 ease-[cubic-bezier(.16,1,.3,1)] ${
          collapsed ? "w-[68px]" : "w-60"
        }`}
      >
        <Sidebar
          collapsed={collapsed}
          onToggle={() => setCollapsed((c) => !c)}
        />
      </aside>

      {/* mobile drawer */}
      {mobileNav && (
        <div className="fixed inset-0 z-[70] lg:hidden">
          <div
            className="absolute inset-0 bg-dark/40 backdrop-blur-[2px] animate-fade-in"
            onClick={() => setMobileNav(false)}
          />
          <div className="absolute left-0 top-0 h-full w-64 animate-scale-in origin-left">
            <Sidebar onNavigate={() => setMobileNav(false)} />
          </div>
        </div>
      )}

      <div className="flex-1 min-w-0 flex flex-col">
        {/* topbar */}
        <header className="h-14 bg-YellowMidLight/60 backdrop-blur border-b border-border sticky top-0 z-40 flex items-center justify-between gap-4 px-4 sm:px-6">
          <div className="flex items-center gap-3 min-w-0">
            <button
              onClick={() => setMobileNav(true)}
              className="lg:hidden w-9 h-9 rounded-xl border border-border text-body flex items-center justify-center"
              aria-label="Menu"
            >
              <svg
                className="w-5 h-5"
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
          <div className="flex items-center gap-3">
            <SystemStatus />
            <ThemeToggle />
            <UserMenu />
          </div>
        </header>

        <main className="flex-1 min-w-0">
          {fullBleed ? (
            /* full-bleed workspaces (chat & search) manage their own columns/scroll;
               56px = topbar h-14 */
            <div className="h-[calc(100vh-56px)] overflow-hidden">
              <Outlet />
            </div>
          ) : (
            <div className="mx-auto w-full px-4 sm:px-6 lg:px-8 py-7 max-w-[1200px]">
              <Outlet />
            </div>
          )}
        </main>
      </div>

      {!hideBubble && <ChatBubble />}
    </div>
  );
}
