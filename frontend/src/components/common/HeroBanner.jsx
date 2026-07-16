/* Shared dashboard hero — the "Hello, {name}" welcome box.
   One gradient card, brand-yellow accents, theme-aware: soft warm light surface
   in light mode, deep ink surface in dark mode. Action buttons live inside. */
export default function HeroBanner({ greeting, name, subtitle, badge, actions }) {
  return (
    <div className="relative overflow-hidden rounded-2xl border border-brand/40 dark:border-brand/25 bg-gradient-to-br from-brand/25 via-amber-100/40 to-white dark:from-brand/15 dark:via-[#23232f] dark:to-[#1a1a24] shadow-lg shadow-brand/10 dark:shadow-black/20">
      {/* decorative shapes */}
      <div className="pointer-events-none absolute -top-14 -right-10 w-52 h-52 rounded-full bg-brand/30 dark:bg-brand/15 blur-3xl" />
      <div className="pointer-events-none absolute -bottom-16 left-1/3 w-44 h-44 rounded-full bg-amber-300/30 dark:bg-violet-500/10 blur-3xl" />
      <div className="pointer-events-none absolute top-4 right-6 hidden sm:block">
        <svg width="54" height="18" viewBox="0 0 26 8" aria-hidden="true" className="opacity-70">
          <polygon points="0,8 26,0 26,8" fill="#FFE600" />
        </svg>
      </div>

      <div className="relative px-5 sm:px-7 py-5 sm:py-6 flex flex-col sm:flex-row sm:items-center gap-4">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2 flex-wrap">
            <h1 className="text-2xl sm:text-[26px] font-extrabold text-dark tracking-tight">
              {greeting}
              {name ? (
                <>
                  , <span className="text-transparent bg-clip-text bg-gradient-to-r from-[#b8860b] to-[#8a6d00] dark:from-brand dark:to-[#F59E0B]">{name}</span>
                </>
              ) : null}{" "}
              <span className="align-middle">👋</span>
            </h1>
            {badge && (
              <span className="text-[10px] font-bold uppercase tracking-[0.1em] text-dark bg-brand rounded-full px-2.5 py-1 shadow-sm">
                {badge}
              </span>
            )}
          </div>
          {subtitle && (
            <p className="text-[13.5px] text-body dark:text-[#b8b8c8] mt-1.5 max-w-xl leading-relaxed">
              {subtitle}
            </p>
          )}
        </div>

        {actions && (
          <div className="flex items-center gap-2 shrink-0 flex-wrap">{actions}</div>
        )}
      </div>
    </div>
  );
}
