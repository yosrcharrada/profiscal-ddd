/** @type {import('tailwindcss').Config} */
module.exports = {
  content: ["./src/**/*.{js,jsx,ts,tsx}"],
  theme: {
    extend: {
      colors: {
        /* ── Profiscal × EY design system ──
           brand  → EY yellow, the single accent (highlights, chips, primary CTAs — always with dark text)
           dark   → EY ink, primary text & dark sections
           cream  → page background (white)
           sand   → alternate section background (cool gray)
           light  → hover surface
        */
        brand: "#FFE600",
        dark: "#2E2E38",
        YellowLight: "#fffefa",
        YellowDrak: "#e6e3de",
        YellowMidLight: "#f5f5f0",
        body: "#585865",
        muted: "#9A9AA5",
        cream: "#FFFFFF",
        sand: "#fffffc",
        light: "#F2F2F6",
        border: "#E5E5EC",
      },
      fontFamily: {
        sans: ["Inter", "system-ui", "sans-serif"],
        display: ['"Instrument Serif"', "Georgia", "serif"],
      },
      animation: {
        "fade-up": "fadeUp .8s cubic-bezier(.16,1,.3,1) forwards",
        "fade-in": "fadeIn .35s ease forwards",
        "scale-in": "scaleIn .3s cubic-bezier(.16,1,.3,1) forwards",
        "slide-in-right": "slideInRight .35s cubic-bezier(.16,1,.3,1) forwards",
        "slide-in-left": "slideInLeft .35s cubic-bezier(.16,1,.3,1) forwards",
        pop: "pop .18s cubic-bezier(.16,1,.3,1) forwards",
        marquee: "marquee 45s linear infinite",
        float: "float 6s ease-in-out infinite",
        "float-2": "float 7s ease-in-out 1.2s infinite",
      },
      keyframes: {
        fadeUp: {
          from: { opacity: 0, transform: "translateY(32px)" },
          to: { opacity: 1, transform: "translateY(0)" },
        },
        fadeIn: { from: { opacity: 0 }, to: { opacity: 1 } },
        scaleIn: {
          from: { opacity: 0, transform: "scale(.95)" },
          to: { opacity: 1, transform: "scale(1)" },
        },
        slideInRight: {
          from: { opacity: 0, transform: "translateX(48px)" },
          to: { opacity: 1, transform: "translateX(0)" },
        },
        slideInLeft: {
          from: { opacity: 0, transform: "translateX(-48px)" },
          to: { opacity: 1, transform: "translateX(0)" },
        },
        pop: {
          from: { opacity: 0, transform: "scale(.9) translateY(4px)" },
          to: { opacity: 1, transform: "scale(1) translateY(0)" },
        },
        marquee: {
          from: { transform: "translateX(0)" },
          to: { transform: "translateX(-50%)" },
        },
        float: {
          "0%,100%": { transform: "translateY(0)" },
          "50%": { transform: "translateY(-10px)" },
        },
      },
    },
  },
  plugins: [],
};
