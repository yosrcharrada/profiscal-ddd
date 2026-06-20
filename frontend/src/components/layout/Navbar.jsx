import { useState, useRef, useEffect } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../../context/AuthContext";
import EYLockup from "../common/EYLockup";
export default function Navbar() {
  const { user, logout, isAdmin } = useAuth();
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
  const handleLogout = async () => {
    await logout();
    navigate("/login");
  };
  const I =
    `${user?.firstName?.[0] || ""}${user?.lastName?.[0] || ""}`.toUpperCase();
  return (
    <nav className="bg-white border-b border-border sticky top-0 z-50">
      <div className="max-w-[1200px] mx-auto px-6 h-16 flex items-center justify-between">
        <Link to="/dashboard" className="inline-flex">
          <EYLockup dark compact />
        </Link>
        <div className="flex items-center gap-4">
          <Link
            to="/dashboard"
            className="text-sm font-medium text-muted hover:text-dark transition-colors hidden sm:block"
          >
            Dashboard
          </Link>
          <Link
            to="/app/search"
            className="text-sm font-medium text-muted hover:text-dark transition-colors hidden sm:block"
          >
            Search
          </Link>
          <Link
            to="/app/chat"
            className="text-sm font-medium text-muted hover:text-dark transition-colors hidden sm:block"
          >
            Chatbot
          </Link>
          <Link
            to="/app/consultations"
            className="text-sm font-medium text-muted hover:text-dark transition-colors hidden sm:block"
          >
            Consultations
          </Link>
          {isAdmin && (
            <Link
              to="/admin/users"
              className="hidden sm:flex items-center gap-1.5 text-sm font-medium text-muted hover:text-dark transition-colors"
            >
              Users
              <span className="text-[9px] font-bold bg-brand text-dark rounded-full px-1.5 py-0.5 uppercase tracking-wide">
                Admin
              </span>
            </Link>
          )}
          <div className="relative" ref={ref}>
            <button
              onClick={() => setOpen(!open)}
              className="flex items-center gap-2 px-2 py-1.5 rounded-xl hover:bg-light transition-colors"
            >
              <div className="w-6 h-6 rounded-lg bg-dark text-white text-xs font-bold flex items-center justify-center">
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
              <div className="absolute right-0 mt-2 w-56 bg-white rounded-xl shadow-xl border border-border py-1 animate-scale-in origin-top-right">
                <div className="px-4 py-3 border-b border-border">
                  <p className="text-sm font-bold text-dark">
                    {user?.firstName} {user?.lastName}
                  </p>
                  <p className="text-xs text-muted mt-0.5 truncate">
                    {user?.email}
                  </p>
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
                  Settings
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
                  Change Password
                </Link>
                <div className="border-t border-border">
                  <button
                    onClick={handleLogout}
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
                    Sign Out
                  </button>
                </div>
              </div>
            )}
          </div>
        </div>
      </div>
    </nav>
  );
}
