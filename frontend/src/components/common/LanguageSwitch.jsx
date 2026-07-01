import { useLanguage } from "../../context/LanguageContext";

export default function LanguageSwitch({ className = "" }) {
  const { lang, setLang } = useLanguage();
  const next = lang === "fr" ? "en" : "fr";

  return (
    <button
      onClick={() => setLang(next)}
      className={`group relative flex items-center justify-center w-8 h-8 rounded-lg border border-border/60 hover:border-dark/30 hover:bg-light/80 transition-all duration-200 ${className}`}
      aria-label={lang === "fr" ? "Switch to English" : "Passer en français"}
      title={lang === "fr" ? "Switch to English" : "Passer en français"}
    >
      {lang === "fr" ? (
        <svg className="w-[18px] h-[14px] rounded-[2px] overflow-hidden" viewBox="0 0 30 20">
          <rect width="10" height="20" fill="#002395" />
          <rect x="10" width="10" height="20" fill="#fff" />
          <rect x="20" width="10" height="20" fill="#ED2939" />
        </svg>
      ) : (
        <svg className="w-[18px] h-[14px] rounded-[2px] overflow-hidden" viewBox="0 0 60 30">
          <clipPath id="t"><rect width="60" height="30" /></clipPath>
          <g clipPath="url(#t)">
            <rect width="60" height="30" fill="#012169" />
            <path d="M0,0 L60,30 M60,0 L0,30" stroke="#fff" strokeWidth="6" />
            <path d="M0,0 L60,30 M60,0 L0,30" stroke="#C8102E" strokeWidth="4" clipPath="url(#t)" />
            <path d="M30,0 V30 M0,15 H60" stroke="#fff" strokeWidth="10" />
            <path d="M30,0 V30 M0,15 H60" stroke="#C8102E" strokeWidth="6" />
          </g>
        </svg>
      )}
    </button>
  );
}
