import { useEffect, useState } from "react";

export default function ThemeToggle({ className = "" }) {
  const [dark, setDark] = useState(() =>
    document.documentElement.classList.contains("dark"),
  );

  useEffect(() => {
    document.documentElement.classList.toggle("dark", dark);
    try {
      localStorage.setItem("theme", dark ? "dark" : "light");
    } catch {}
  }, [dark]);

  return (
    <button
      type="button"
      onClick={() => setDark((d) => !d)}
      aria-label={dark ? "Activer le mode clair" : "Activer le mode sombre"}
      title={dark ? "Mode clair" : "Mode sombre"}
      className={`relative w-[52px] h-[28px] rounded-full transition-all duration-500 ease-[cubic-bezier(.16,1,.3,1)] ${
        dark
          ? "bg-[#5f5f90] shadow-[inset_0_1px_4px_rgba(0,0,0,0.5)]"
          : "bg-gradient-to-r from-[#fde68a] to-[#fef4db] shadow-[inset_0_1px_4px_rgba(0,0,0,0.08)]"
      } ${className}`}
    >
      {/* Stars (visible only in dark mode) */}
      <span
        className={`absolute top-[6px] left-[8px] w-[3px] h-[3px] rounded-full bg-white/70 transition-opacity duration-500 ${dark ? "opacity-100" : "opacity-0"}`}
      />
      <span
        className={`absolute top-[14px] left-[14px] w-[2px] h-[2px] rounded-full bg-white/50 transition-opacity duration-500 delay-75 ${dark ? "opacity-100" : "opacity-0"}`}
      />
      <span
        className={`absolute top-[8px] left-[18px] w-[2px] h-[2px] rounded-full bg-white/40 transition-opacity duration-500 delay-150 ${dark ? "opacity-100" : "opacity-0"}`}
      />

      {/* Knob */}
      <span
        className={`absolute top-[3px] w-[22px] h-[22px] rounded-full flex items-center justify-center transition-all duration-500 ease-[cubic-bezier(.16,1,.3,1)] ${
          dark
            ? "left-[27px] bg-[#60a5fa] shadow-[0_0_8px_rgba(96,165,250,0.5)]"
            : "left-[3px] bg-[#fcd34d] shadow-[0_0_10px_rgba(252,211,77,0.6)]"
        }`}
      >
        {dark ? (
          <svg
            className="w-[13px] h-[13px] text-white"
            fill="currentColor"
            viewBox="0 0 24 24"
          >
            <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z" />
          </svg>
        ) : (
          <svg
            className="w-[13px] h-[13px] text-[#f59e0b]"
            fill="none"
            viewBox="0 0 24 24"
            stroke="currentColor"
            strokeWidth={2.5}
          >
            <circle cx="12" cy="12" r="4" />
            <path
              strokeLinecap="round"
              d="M12 2v2m0 16v2M4.93 4.93l1.41 1.41m11.32 11.32l1.41 1.41M2 12h2m16 0h2M4.93 19.07l1.41-1.41m11.32-11.32l1.41-1.41"
            />
          </svg>
        )}
      </span>
    </button>
  );
}
