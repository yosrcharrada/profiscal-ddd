import { useEffect, useState } from "react";

/* Light/Dark theme toggle. Persists the choice and flips the `dark` class on <html>,
   which swaps the CSS variables defined in index.css (so the whole app re-themes). */
export default function ThemeToggle({ className = "" }) {
  const [dark, setDark] = useState(
    () => document.documentElement.classList.contains("dark"),
  );

  useEffect(() => {
    document.documentElement.classList.toggle("dark", dark);
    try {
      localStorage.setItem("theme", dark ? "dark" : "light");
    } catch {
      /* ignore */
    }
  }, [dark]);

  return (
    <button
      type="button"
      onClick={() => setDark((d) => !d)}
      aria-label={dark ? "Activer le mode clair" : "Activer le mode sombre"}
      title={dark ? "Mode clair" : "Mode sombre"}
      className={`w-9 h-9 rounded-xl border border-border text-muted hover:text-dark hover:border-dark/30 flex items-center justify-center transition-colors ${className}`}
    >
      {dark ? (
        /* sun */
        <svg className="w-[18px] h-[18px]" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <circle cx="12" cy="12" r="4" />
          <path strokeLinecap="round" d="M12 2v2m0 16v2M4.93 4.93l1.41 1.41m11.32 11.32l1.41 1.41M2 12h2m16 0h2M4.93 19.07l1.41-1.41m11.32-11.32l1.41-1.41" />
        </svg>
      ) : (
        /* moon */
        <svg className="w-[18px] h-[18px]" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z" />
        </svg>
      )}
    </button>
  );
}
