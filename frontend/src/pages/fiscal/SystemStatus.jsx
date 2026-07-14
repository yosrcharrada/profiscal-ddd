import { useState } from "react";
import useSystemHealth from "../../hooks/useSystemHealth";
import { useLanguage } from "../../context/LanguageContext";

function Dot({ ok }) {
  return (
    <span
      className={`w-2 h-2 rounded-full ${ok ? "bg-green-500" : "bg-red-400"}`}
    />
  );
}

/**
 * Compact connection-status pill for the fiscal workspace header.
 * Expands to a panel explaining exactly what to do when something is offline.
 */
export default function SystemStatus() {
  const { data, loading } = useSystemHealth({ poll: 20000 });
  const { t } = useLanguage();
  const [open, setOpen] = useState(false);
  if (loading || !data) {
    return (
      <span className="inline-flex items-center gap-1.5 text-[11px] font-semibold text-muted border border-border rounded-lg h-8 px-2.5">
        <span className="w-1.5 h-1.5 rounded-full bg-muted animate-pulse shrink-0" />
        <span className="hidden sm:inline">{t("system.checking")}</span>
      </span>
    );
  }

  const ready = data.ready;
  const items = [
    {
      label: t("system.knowledgeBase"),
      ok: data.neo4j,
      hint: data.neo4j
        ? t("system.chunksIndexed", { n: data.chunks.toLocaleString() })
        : t("system.neo4jHint"),
    },
    {
      label: t("system.aiModel"),
      ok: data.llmConfigured,
      hint: data.llmConfigured
        ? t("system.configured")
        : t("system.llmHint"),
    },
    {
      label: t("system.embedServer"),
      ok: data.embedServer,
      hint: data.embedServer
        ? t("system.embedRunning")
        : t("system.embedHint"),
    },
  ];

  return (
    <div className="relative">
      <button
        onClick={() => setOpen((o) => !o)}
        className={`inline-flex items-center gap-1.5 text-[11px] font-semibold rounded-lg h-8 px-2.5 border transition-colors ${ready ? "bg-green-50 text-green-700 border-green-200" : "bg-amber-50 text-amber-700 border-amber-200"}`}
      >
        <span
          className={`w-1.5 h-1.5 rounded-full shrink-0 ${ready ? "bg-green-500" : "bg-amber-500 animate-pulse"}`}
        />
        <span className="hidden sm:inline">{ready ? t("system.connected") : t("system.setupNeeded")}</span>
        <svg
          className={`w-3.5 h-3.5 transition-transform duration-200 ${open ? "rotate-180" : ""}`}
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
        <div className="absolute right-0 mt-2 w-80 bg-white rounded-2xl border border-border shadow-xl shadow-dark/10 p-4 z-30 animate-scale-in origin-top-right">
          <p className="text-[11px] font-bold text-muted uppercase tracking-widest mb-3">
            {t("system.engineStatus")}
          </p>
          <div className="space-y-3">
            {items.map((it) => (
              <div key={it.label} className="flex items-start gap-2.5">
                <span className="mt-1.5">
                  <Dot ok={it.ok} />
                </span>
                <div>
                  <p className="text-sm font-semibold text-dark">{it.label}</p>
                  <p className="text-xs text-muted leading-snug">{it.hint}</p>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
