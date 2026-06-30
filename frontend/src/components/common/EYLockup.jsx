/* EY beam + TAXMIND brand lockup.
   dark = ink text for light surfaces; default = white text for dark surfaces. */
export default function EYLockup({ dark = false, compact = false }) {
  return (
    <span className="flex items-center gap-3.5">
      <span className="flex flex-col items-start">
        <svg
          width={compact ? 22 : 26}
          height={compact ? 7 : 8}
          viewBox="0 0 26 8"
          className="mb-[3px]"
          aria-hidden="true"
        >
          <polygon points="0,8 26,0 26,8" fill="#FFE600" />
        </svg>
        <span
          className={`${compact ? "text-[18px]" : "text-[21px]"} font-bold leading-none tracking-tight ${dark ? "text-dark" : "text-white"}`}
        >
          EY
        </span>
      </span>
      <span
        className={`${compact ? "h-6" : "h-7"} w-px ${dark ? "bg-dark/20" : "bg-white/25"}`}
      />
      <span
        className={`${compact ? "text-[13px]" : "text-[14px]"} font-semibold tracking-[0.24em] ${dark ? "text-dark" : "text-white"}`}
      >
        TAXMIND
      </span>
    </span>
  );
}
