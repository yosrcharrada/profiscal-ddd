import { useState, useEffect, useRef } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import EYLockup from '../components/common/EYLockup';

/* ─── scroll reveal ─── */
function R({ children, className = '', delay = 0 }) {
  const ref = useRef();
  const [v, setV] = useState(false);
  useEffect(() => {
    const el = ref.current; if (!el) return;
    const obs = new IntersectionObserver(([e]) => { if (e.isIntersecting) { setV(true); obs.disconnect(); } }, { threshold: 0.08 });
    obs.observe(el); return () => obs.disconnect();
  }, []);
  return <div ref={ref} className={`transition-all duration-[800ms] ease-[cubic-bezier(.16,1,.3,1)] ${v ? 'opacity-100 translate-y-0' : 'opacity-0 translate-y-8'} ${className}`} style={{ transitionDelay: `${delay}ms` }}>{children}</div>;
}

/* ─── tiny primitives ─── */
const Eyebrow = ({ children }) => (
  <p className="flex items-center gap-3 text-[12px] font-semibold text-dark tracking-[0.16em] uppercase">
    <span className="w-6 h-[3px] bg-brand" />{children}
  </p>
);

/* serif word with an EY-yellow highlighter stroke */
const Hi = ({ children }) => (
  <span className="font-display italic font-normal tracking-normal text-dark px-1 -mx-0.5" style={{ backgroundImage: 'linear-gradient(180deg, transparent 56%, #FFE600 56%, #FFE600 90%, transparent 90%)' }}>{children}</span>
);

const Check = ({ className = 'w-3 h-3' }) => (
  <svg className={className} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={3}><path strokeLinecap="round" strokeLinejoin="round" d="M4.5 12.75l6 6 9-13.5" /></svg>
);

const Arrow = ({ className = 'w-4 h-4' }) => (
  <svg className={className} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path strokeLinecap="round" strokeLinejoin="round" d="M13.5 4.5L21 12m0 0l-7.5 7.5M21 12H3" /></svg>
);

/* window chrome wrapper for product mockups */
const Window = ({ title, children, className = '' }) => (
  <div className={`bg-white rounded-2xl border border-border shadow-[0_24px_60px_-24px_rgba(46,46,56,.18)] overflow-hidden ${className}`}>
    <div className="flex items-center gap-1.5 px-4 py-3 border-b border-border/70 bg-[#FAFAFC]">
      <span className="w-2.5 h-2.5 rounded-full bg-[#E4E4EA]" />
      <span className="w-2.5 h-2.5 rounded-full bg-[#E4E4EA]" />
      <span className="w-2.5 h-2.5 rounded-full bg-[#E4E4EA]" />
      <span className="mx-auto text-[11px] font-medium text-muted bg-light border border-border/60 rounded-md px-3 py-0.5">{title}</span>
      <span className="w-12" />
    </div>
    {children}
  </div>
);

/* ─── data ─── */
const heroLogos = ['Google', 'SIEMENS', 'alpian', 'Opal', 'alBaraka'];

const problems = [
  { n: '01', title: 'Hours lost to manual research', desc: 'Every cross-border question means digging through codes, treaties, and circulars. A single consultation takes 3–5 hours — multiplied by hundreds of cases a year.' },
  { n: '02', title: 'Inconsistent answers across the team', desc: 'Different consultants reach different conclusions on the same facts. Without a shared source of truth, quality varies and compliance risk grows silently.' },
  { n: '03', title: 'Demand grows. Capacity doesn’t.', desc: 'Client inquiries keep increasing, but hiring specialized tax talent takes months. The hourly model breaks down exactly when the practice starts winning.' },
];

const features = [
  {
    tag: 'Legal Search',
    icon: <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path strokeLinecap="round" strokeLinejoin="round" d="M21 21l-5.197-5.197m0 0A7.5 7.5 0 105.196 5.196a7.5 7.5 0 0010.607 10.607z" /></svg>,
    title: <>Find any provision in <Hi>seconds.</Hi></>,
    desc: 'Search the entire Tunisian fiscal corpus — the tax codes, 68+ double-tax treaties, DGI circulars, and court decisions — with an engine that understands fiscal context, not just keywords.',
    bullets: ['Full-text search with fiscal context awareness', 'Cross-reference treaties with domestic law instantly', 'Always current with the latest legislative changes'],
    mockTitle: 'taxmind.ey.com/search',
    mock: (
      <div className="p-4 space-y-2.5 bg-[#FAFAFC]">
        <div className="bg-white rounded-xl border border-border px-4 py-3 flex items-center gap-3">
          <svg className="w-4 h-4 text-muted shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path strokeLinecap="round" strokeLinejoin="round" d="M21 21l-5.197-5.197m0 0A7.5 7.5 0 105.196 5.196a7.5 7.5 0 0010.607 10.607z" /></svg>
          <span className="text-[13px] text-dark">withholding tax on services — France / Tunisia</span>
          <span className="ml-auto text-[10px] font-semibold text-muted border border-border rounded-md px-1.5 py-0.5">⌘K</span>
        </div>
        {[
          { t: 'Convention Tunisia–France, Art. 14', s: 'Independent personal services — allocation of taxing rights', r: 98, k: 'Treaty' },
          { t: 'Code IRPP–IS, Art. 52', s: 'Withholding tax on payments to non-residents', r: 95, k: 'Code' },
          { t: 'DGI Circular 2023/14', s: 'Application guidelines for treaty override provisions', r: 91, k: 'Circular' },
        ].map((item) => (
          <div key={item.t} className="bg-white rounded-xl border border-border px-4 py-3 hover:border-dark/30 transition-colors">
            <div className="flex items-center justify-between gap-3">
              <p className="text-[13px] font-semibold text-dark truncate">{item.t}</p>
              <span className="text-[10px] font-bold text-dark bg-brand px-2 py-0.5 rounded-full shrink-0">{item.r}%</span>
            </div>
            <div className="mt-1 flex items-center gap-2">
              <span className="text-[10px] font-semibold text-muted uppercase tracking-wide">{item.k}</span>
              <span className="text-xs text-body truncate">{item.s}</span>
            </div>
          </div>
        ))}
      </div>
    ),
  },
  {
    tag: 'AI Opinions',
    icon: <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path strokeLinecap="round" strokeLinejoin="round" d="M19.5 14.25v-2.625a3.375 3.375 0 00-3.375-3.375h-1.5A1.125 1.125 0 0113.5 7.125v-1.5a3.375 3.375 0 00-3.375-3.375H8.25m0 12.75h7.5m-7.5 3H12M10.5 2.25H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 00-9-9z" /></svg>,
    title: <>From question to <Hi>cited memo.</Hi></>,
    desc: 'Describe the case in plain language. Taxmind identifies the applicable framework, applies the correct treaty provisions against domestic law, and drafts a structured consultation report — every claim traced to its source.',
    bullets: ['Plain language in, structured legal memo out', 'Article-level citations on every claim', 'Export-ready PDF reports for clients'],
    mockTitle: 'taxmind.ey.com/opinions/2024-0847',
    mock: (
      <div className="p-4 space-y-2.5 bg-[#FAFAFC]">
        <div className="flex items-center justify-between bg-white border border-border rounded-xl px-4 py-3">
          <div>
            <p className="text-[13px] font-bold text-dark">Consultation #2024-0847</p>
            <p className="text-[11px] text-body mt-0.5">WHT on management fees paid to French parent co.</p>
          </div>
          <span className="flex items-center gap-1.5 text-[10px] font-bold text-dark bg-brand rounded-full px-2.5 py-1"><Check className="w-2.5 h-2.5" /> Ready</span>
        </div>
        <div className="bg-white rounded-xl border border-border px-4 py-3">
          <p className="text-[11px] font-bold text-dark uppercase tracking-wide mb-1.5">II. Legal framework</p>
          <p className="text-[11px] text-body leading-relaxed">Convention Tunisia–France (1973, am. 2018), Art. 12 — fees for technical services. Code IRPP–IS, Art. 52(3) — WHT on non-resident service income.</p>
          <div className="mt-2 flex flex-wrap gap-1.5">
            <span className="text-[10px] bg-light border border-border rounded-md px-2 py-0.5 text-body font-medium">Art. 12 · DTA TN–FR</span>
            <span className="text-[10px] bg-light border border-border rounded-md px-2 py-0.5 text-body font-medium">Art. 52(3) · IRPP–IS</span>
          </div>
        </div>
        <div className="bg-white rounded-xl border border-border px-4 py-3">
          <p className="text-[11px] font-bold text-dark uppercase tracking-wide mb-1.5">III. Opinion</p>
          <p className="text-[11px] text-body leading-relaxed">The fees qualify as technical services under Art. 12(4). <span className="bg-brand/50 text-dark font-medium px-1 rounded-sm">The treaty caps WHT at 12%, overriding the domestic 15% rate.</span></p>
        </div>
        <div className="flex gap-2">
          <span className="flex items-center gap-1.5 text-[11px] font-bold bg-dark text-white rounded-lg px-3 py-1.5">Export PDF</span>
          <span className="flex items-center gap-1.5 text-[11px] font-semibold text-body bg-white border border-border rounded-lg px-3 py-1.5">Copy citations</span>
        </div>
      </div>
    ),
  },
  {
    tag: 'Tax Chat',
    icon: <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path strokeLinecap="round" strokeLinejoin="round" d="M8.625 12a.375.375 0 11-.75 0 .375.375 0 01.75 0zm0 0H8.25m4.125 0a.375.375 0 11-.75 0 .375.375 0 01.75 0zm0 0H12m4.125 0a.375.375 0 11-.75 0 .375.375 0 01.75 0zm0 0h-.375M21 12c0 4.556-4.03 8.25-9 8.25a9.764 9.764 0 01-2.555-.337A5.972 5.972 0 015.41 20.97a5.969 5.969 0 01-.474-.065 4.48 4.48 0 00.978-2.025c.09-.457-.133-.901-.467-1.226C3.93 16.178 3 14.189 3 12c0-4.556 4.03-8.25 9-8.25s9 3.694 9 8.25z" /></svg>,
    title: <>A fiscal expert on call, <Hi>24/7.</Hi></>,
    desc: 'A specialized assistant trained exclusively on Tunisian tax law, international treaties, and administrative practice. Ask follow-ups, explore edge cases, and stress-test your reasoning — any time.',
    bullets: ['Trained on the Tunisian fiscal corpus', 'Multi-turn conversations with memory', 'Cites its sources on every answer'],
    mockTitle: 'taxmind.ey.com/chat',
    mock: (
      <div className="p-4 space-y-3 bg-[#FAFAFC]">
        <div className="flex justify-end">
          <div className="bg-dark text-white rounded-2xl rounded-br-md px-4 py-2.5 max-w-[85%]">
            <p className="text-[12px] leading-relaxed">Does the Tunisia–UAE treaty include a beneficial ownership clause for dividends?</p>
          </div>
        </div>
        <div className="flex justify-start">
          <div className="bg-white border border-border rounded-2xl rounded-bl-md px-4 py-2.5 max-w-[92%] shadow-sm">
            <p className="text-[12px] text-dark leading-relaxed">Yes. Under Article 10(2) of the Tunisia–UAE DTA, the reduced 5% rate on dividends applies only when the recipient is the <span className="font-semibold bg-brand/50 px-0.5 rounded-sm">beneficial owner</span>. Tunisia follows the OECD commentary — substance over form.</p>
            <div className="mt-2 flex flex-wrap gap-1.5">
              <span className="text-[10px] bg-light border border-border rounded-md px-2 py-0.5 text-body font-medium">Art. 10(2) · DTA TN–UAE</span>
              <span className="text-[10px] bg-light border border-border rounded-md px-2 py-0.5 text-body font-medium">OECD Model · Art. 10</span>
            </div>
          </div>
        </div>
        <div className="flex items-center gap-2 bg-white border border-border rounded-xl px-4 py-2.5">
          <span className="text-[12px] text-muted flex-1">Ask a follow-up…</span>
          <span className="w-6 h-6 bg-dark rounded-lg flex items-center justify-center"><Arrow className="w-3 h-3 text-brand -rotate-90" /></span>
        </div>
      </div>
    ),
  },
];

const stats = [
  { v: '90%', l: 'less time per consultation' },
  { v: '14s', l: 'median opinion draft' },
  { v: '68+', l: 'double-tax treaties indexed' },
  { v: '10k+', l: 'articles & provisions covered' },
];

const steps = [
  { n: '1', time: 'Day 1', title: 'Create your workspace', desc: 'Sign up, invite your team, and configure your practice profile. Under five minutes — no credit card needed.' },
  { n: '2', time: 'Day 2', title: 'Ask your first question', desc: 'Type a cross-border case in plain language. Watch Taxmind pull the treaties, apply domestic law, and draft a full opinion.' },
  { n: '3', time: 'Day 30', title: 'Transform your practice', desc: 'Your team delivers 10× more opinions at consistent quality. Manual research becomes the exception, not the rule.' },
];

const testimonials = [
  { q: 'Taxmind eliminated our biggest bottleneck. What used to take a senior consultant an entire afternoon now takes 15 minutes — with better citations than we ever produced manually.', name: 'Sami Gharbi', role: 'Tax Partner · Tunis', initials: 'SG' },
  { q: 'The legal search alone is worth it. I can find a specific treaty article and its domestic equivalent in two clicks. It changed how we approach cross-border cases.', name: 'Ines Ben Amor', role: 'Senior Fiscal Consultant', initials: 'IB' },
  { q: 'We evaluated three solutions. Taxmind was the only one that actually understood Tunisian fiscal context. It handles nuances that even junior consultants miss.', name: 'Karim Jebali', role: 'Head of Tax · Advisory', initials: 'KJ' },
];

const security = [
  {
    icon: <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}><path strokeLinecap="round" strokeLinejoin="round" d="M9 12.75L11.25 15 15 9.75m-3-7.036A11.959 11.959 0 013.598 6 11.99 11.99 0 003 9.749c0 5.592 3.824 10.29 9 11.623 5.176-1.332 9-6.03 9-11.622 0-1.31-.21-2.571-.598-3.751h-.152c-3.196 0-6.1-1.248-8.25-3.285z" /></svg>,
    title: 'Encrypted everywhere',
    desc: 'TLS 1.3 in transit, AES-256 at rest. Bank-grade security on every request.',
  },
  {
    icon: <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}><path strokeLinecap="round" strokeLinejoin="round" d="M16.5 10.5V6.75a4.5 4.5 0 10-9 0v3.75m-.75 11.25h10.5a2.25 2.25 0 002.25-2.25v-6.75a2.25 2.25 0 00-2.25-2.25H6.75a2.25 2.25 0 00-2.25 2.25v6.75a2.25 2.25 0 002.25 2.25z" /></svg>,
    title: 'Your data stays yours',
    desc: 'Client files and consultations are never used to train models. Full workspace isolation.',
  },
  {
    icon: <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}><path strokeLinecap="round" strokeLinejoin="round" d="M21.75 17.25v-.228a4.5 4.5 0 00-.12-1.03l-2.268-9.64a3.375 3.375 0 00-3.285-2.602H7.923a3.375 3.375 0 00-3.285 2.602l-2.268 9.64a4.5 4.5 0 00-.12 1.03v.228m19.5 0a3 3 0 01-3 3H5.25a3 3 0 01-3-3m19.5 0a3 3 0 00-3-3H5.25a3 3 0 00-3 3m16.5 0h.008v.008h-.008v-.008zm-3 0h.008v.008h-.008v-.008z" /></svg>,
    title: 'GDPR-aligned hosting',
    desc: 'EU data centers, audit logs, and granular access controls built for regulated work.',
  },
];

const faqs = [
  { q: 'Is Taxmind a replacement for a tax advisor?', a: 'No — it’s a force multiplier for one. Taxmind drafts the research and the opinion; a qualified professional reviews, adjusts, and signs off. Think of it as a tireless senior associate who reads every text and never forgets a citation.' },
  { q: 'Which legal sources does it cover?', a: 'The Tunisian tax codes (IRPP–IS, TVA, registration duties), all 68+ double-tax treaties signed by Tunisia, DGI administrative circulars and doctrine, and relevant court decisions. The corpus is continuously updated as legislation changes.' },
  { q: 'How reliable are the citations?', a: 'Every claim in a generated opinion is linked to the specific article it relies on, and you can open the source text in one click. Nothing is asserted without a verifiable reference — that’s the core design principle of the platform.' },
  { q: 'Is my client data confidential?', a: 'Yes. Workspaces are fully isolated, data is encrypted in transit and at rest, and your consultations are never used to train models. Taxmind is built for privileged, professional-grade work.' },
  { q: 'Does it work in French and Arabic?', a: 'Yes. The underlying corpus is largely in French, and you can ask questions and receive opinions in French, Arabic, or English — whichever your team and clients prefer.' },
];

const footerCols = [
  { t: 'Platform', links: ['Legal Search', 'AI Opinions', 'Tax Chat', 'Treaty Database'] },
  { t: 'Company', links: ['About', 'Careers', 'Blog', 'Contact'] },
  { t: 'Legal', links: ['Privacy', 'Terms', 'Security', 'Cookies'] },
];

/* ─── FAQ item ─── */
function FaqItem({ q, a }) {
  const [open, setOpen] = useState(false);
  return (
    <div className="border-b border-border">
      <button onClick={() => setOpen(!open)} className="w-full flex items-center justify-between gap-6 py-6 text-left group">
        <span className="text-[15px] sm:text-base font-semibold text-dark group-hover:underline decoration-brand decoration-[3px] underline-offset-4 transition-all">{q}</span>
        <span className={`w-7 h-7 rounded-full border border-border flex items-center justify-center shrink-0 transition-all duration-300 ${open ? 'bg-dark border-dark rotate-45' : 'bg-white group-hover:border-dark/30'}`}>
          <svg className={`w-3.5 h-3.5 ${open ? 'text-brand' : 'text-dark'}`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path strokeLinecap="round" strokeLinejoin="round" d="M12 4.5v15m7.5-7.5h-15" /></svg>
        </span>
      </button>
      <div className={`grid transition-all duration-300 ease-out ${open ? 'grid-rows-[1fr] opacity-100 pb-6' : 'grid-rows-[0fr] opacity-0'}`}>
        <div className="overflow-hidden"><p className="text-[15px] text-body leading-relaxed max-w-2xl">{a}</p></div>
      </div>
    </div>
  );
}

/* ═══════════════════════════════════ PAGE ═══════════════════════════════════ */
export default function Landing() {
  const { isAuthenticated } = useAuth();
  const [scrolled, setScrolled] = useState(false);
  const [menuOpen, setMenuOpen] = useState(false);

  useEffect(() => {
    const fn = () => setScrolled(window.scrollY > 24);
    window.addEventListener('scroll', fn);
    return () => window.removeEventListener('scroll', fn);
  }, []);

  const navLinks = [
    { href: '#platform', label: 'Platform' },
    { href: '#how', label: 'How it works' },
    { href: '#customers', label: 'Customers' },
    { href: '#faq', label: 'FAQ' },
  ];

  return (
    <div className="bg-cream overflow-x-clip">

      {/* ══════════ NAV ══════════ */}
      <nav className={`fixed top-0 w-full z-50 transition-all duration-500 ${scrolled || menuOpen ? 'bg-dark/90 backdrop-blur-xl shadow-[0_1px_0_rgba(255,255,255,.08)]' : 'bg-transparent'}`}>
        <div className="max-w-[1200px] mx-auto px-6 lg:px-8 h-[76px] flex items-center justify-between">
          <Link to="/" aria-label="EY Taxmind home"><EYLockup /></Link>
          <div className="hidden md:flex items-center gap-8 text-[14px] font-medium text-white/70">
            {navLinks.map((l) => <a key={l.href} href={l.href} className="hover:text-white transition-colors">{l.label}</a>)}
          </div>
          <div className="hidden md:flex items-center gap-2.5">
            {isAuthenticated ? (
              <Link to="/dashboard" className="text-sm font-semibold bg-brand text-dark pl-5 pr-4 py-2.5 rounded-full hover:shadow-lg hover:shadow-brand/30 hover:-translate-y-px transition-all flex items-center gap-1.5">Dashboard <Arrow className="w-3.5 h-3.5" /></Link>
            ) : (
              <>
                <Link to="/login" className="text-sm font-medium text-white/80 hover:text-white px-4 py-2.5 transition-colors">Sign in</Link>
                <Link to="/register" className="text-sm font-semibold bg-brand text-dark pl-5 pr-4 py-2.5 rounded-full hover:shadow-lg hover:shadow-brand/30 hover:-translate-y-px transition-all flex items-center gap-1.5">Get started <Arrow className="w-3.5 h-3.5" /></Link>
              </>
            )}
          </div>
          <button onClick={() => setMenuOpen(!menuOpen)} className="md:hidden w-10 h-10 flex items-center justify-center text-white" aria-label="Menu">
            <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              {menuOpen ? <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" /> : <path strokeLinecap="round" strokeLinejoin="round" d="M3.75 6.75h16.5M3.75 12h16.5M3.75 17.25h16.5" />}
            </svg>
          </button>
        </div>
        {menuOpen && (
          <div className="md:hidden border-t border-white/10 bg-dark/95 backdrop-blur-xl px-6 py-5 space-y-1 animate-scale-in origin-top">
            {navLinks.map((l) => <a key={l.href} href={l.href} onClick={() => setMenuOpen(false)} className="block py-2.5 text-[15px] font-medium text-white/75 hover:text-white">{l.label}</a>)}
            <div className="pt-3 flex gap-3">
              <Link to="/login" className="flex-1 text-center text-sm font-semibold border border-white/20 text-white px-5 py-3 rounded-full">Sign in</Link>
              <Link to="/register" className="flex-1 text-center text-sm font-semibold bg-brand text-dark px-5 py-3 rounded-full">Get started</Link>
            </div>
          </div>
        )}
      </nav>

      {/* ══════════ HERO ══════════ */}
      <header className="relative min-h-screen flex flex-col">
        {/* photo + cinematic overlays */}
        <div className="absolute inset-0 bg-dark" aria-hidden="true">
          <div className="absolute inset-0 bg-cover bg-center" style={{ backgroundImage: `url(${process.env.PUBLIC_URL}/Hero.avif)` }} />
          <div className="absolute inset-0 bg-gradient-to-r from-[#0F0F16]/95 via-[#13131C]/80 to-[#13131C]/40" />
          <div className="absolute inset-0 bg-gradient-to-t from-[#0F0F16]/90 via-transparent to-[#0F0F16]/50" />
        </div>

        <div className="relative flex-1 flex items-center pt-[76px]">
          <div className="max-w-[1200px] mx-auto px-6 lg:px-8 w-full py-20">
            <div className="max-w-[680px]">
              <R>
                <h1>
                  <span className="font-display italic text-brand block text-[3.5rem] sm:text-[4.4rem] lg:text-[5.1rem] leading-[0.95] -ml-1">Automated</span>
                  <span className="block mt-3 text-white font-semibold tracking-[-0.03em] text-[2.3rem] sm:text-[3.1rem] lg:text-[3.7rem] leading-[1.06]">tax consultation</span>
                  <span className="block text-white font-semibold tracking-[-0.03em] text-[2.3rem] sm:text-[3.1rem] lg:text-[3.7rem] leading-[1.06]">for Tunisia &amp; beyond</span>
                </h1>
              </R>
              <R delay={100}>
                <p className="mt-7 text-[16px] sm:text-[17px] text-white/65 leading-relaxed max-w-[560px]">
                  An agentic AI platform built for EY Tunisia&rsquo;s tax department. It analyzes
                  cross-border cases, applies Tunisian fiscal law and double-tax treaties, and
                  delivers structured, explainable consultation reports — in seconds.
                </p>
              </R>
              <R delay={180}>
                <div className="mt-9 flex flex-wrap items-center gap-x-7 gap-y-4">
                  <Link to="/register" className="group inline-flex items-center gap-2 bg-brand text-dark font-semibold text-[15px] pl-7 pr-6 py-3.5 rounded-full hover:shadow-xl hover:shadow-brand/25 hover:-translate-y-0.5 transition-all duration-300">
                    Get started <Arrow className="w-4 h-4 group-hover:translate-x-0.5 transition-transform" />
                  </Link>
                  <a href="#platform" className="group inline-flex items-center gap-2 text-white/85 font-semibold text-[15px] py-3.5 underline decoration-white/30 decoration-2 underline-offset-[6px] hover:decoration-brand transition-all">
                    Read the docs <Arrow className="w-4 h-4 group-hover:translate-x-0.5 transition-transform" />
                  </a>
                </div>
              </R>
              <R delay={260}>
                <div className="mt-10 flex items-center gap-4">
                  <div className="flex -space-x-2.5">
                    {[['NT', 'bg-brand text-dark'], ['SG', 'bg-[#3C3C49] text-white'], ['IB', 'bg-[#55555F] text-white'], ['KJ', 'bg-white text-dark']].map(([i, c]) => (
                      <span key={i} className={`w-8 h-8 rounded-full border-2 border-[#16161F] flex items-center justify-center text-[10px] font-bold ${c}`}>{i}</span>
                    ))}
                  </div>
                  <p className="font-mono text-[12px] text-white/55 leading-relaxed">Built for EY Tunisia tax &amp;<br className="sm:hidden" /> international advisory teams</p>
                </div>
              </R>
            </div>
          </div>
        </div>

        {/* client strip */}
        <div className="relative border-t border-white/10 bg-black/25 backdrop-blur-md">
          <div className="max-w-[1200px] mx-auto px-6 lg:px-8 py-7 flex flex-wrap items-center justify-center md:justify-between gap-x-12 gap-y-4">
            {heroLogos.map((n) => (
              <span key={n} className="text-[17px] font-bold text-white/35 tracking-wide hover:text-white/60 transition-colors cursor-default">{n}</span>
            ))}
          </div>
        </div>
      </header>

      {/* ══════════ PROBLEM ══════════ */}
      <section id="problem" className="py-24 sm:py-32 bg-sand scroll-mt-20">
        <div className="max-w-[1200px] mx-auto px-6 lg:px-8 grid lg:grid-cols-[1fr,1.2fr] gap-14 lg:gap-24">
          <div className="lg:sticky lg:top-32 self-start">
            <R>
              <Eyebrow>The problem</Eyebrow>
              <h2 className="mt-5 text-4xl sm:text-[2.9rem] font-semibold tracking-[-0.03em] text-dark leading-[1.08]">
                Manual tax research doesn&rsquo;t <Hi>scale.</Hi>
              </h2>
              <p className="mt-6 text-base sm:text-lg text-body leading-relaxed max-w-md">
                Tax teams face a widening gap between the complexity of fiscal regulation
                and their capacity to deliver timely, defensible opinions.
              </p>
              <div className="mt-8 inline-flex items-center gap-4 bg-white border border-border rounded-2xl px-5 py-4">
                <div><p className="text-xl font-bold text-dark tracking-tight">3–5h</p><p className="text-[11px] text-muted font-medium">per consultation today</p></div>
                <Arrow className="w-4 h-4 text-dark" />
                <div><p className="text-xl font-bold text-dark tracking-tight inline-block border-b-[3px] border-brand">14s</p><p className="text-[11px] text-muted font-medium">with Taxmind</p></div>
              </div>
            </R>
          </div>
          <div>
            {problems.map((p, i) => (
              <R key={p.n} delay={i * 100}>
                <div className={`flex gap-6 sm:gap-10 py-9 ${i > 0 ? 'border-t border-dark/10' : ''}`}>
                  <span className="font-display italic text-3xl sm:text-4xl text-dark/20 leading-none pt-1">{p.n}</span>
                  <div>
                    <h3 className="text-lg sm:text-xl font-semibold text-dark tracking-tight">{p.title}</h3>
                    <p className="mt-2.5 text-[15px] text-body leading-relaxed">{p.desc}</p>
                  </div>
                </div>
              </R>
            ))}
          </div>
        </div>
      </section>

      {/* ══════════ PLATFORM ══════════ */}
      <section id="platform" className="py-24 sm:py-32 scroll-mt-20">
        <div className="max-w-[1200px] mx-auto px-6 lg:px-8">
          <R className="text-center">
            <p className="flex items-center justify-center gap-3 text-[12px] font-semibold text-dark tracking-[0.16em] uppercase"><span className="w-6 h-[3px] bg-brand" />The platform<span className="w-6 h-[3px] bg-brand" /></p>
            <h2 className="mt-5 mx-auto max-w-2xl text-4xl sm:text-[2.9rem] font-semibold tracking-[-0.03em] text-dark leading-[1.08]">
              Everything your team needs to <Hi>advise.</Hi>
            </h2>
            <p className="mt-5 mx-auto max-w-xl text-base sm:text-lg text-body leading-relaxed">
              Three tools, one workspace — built end-to-end for the way tax professionals actually work.
            </p>
          </R>

          <div className="mt-20 space-y-24 sm:space-y-28">
            {features.map((f, i) => (
              <R key={f.tag}>
                <div className="grid lg:grid-cols-2 gap-10 lg:gap-20 items-center">
                  <div className={i % 2 === 1 ? 'lg:order-2' : ''}>
                    <span className="inline-flex items-center gap-2 bg-brand/30 text-dark text-[12px] font-bold rounded-full px-3.5 py-1.5">{f.icon}{f.tag}</span>
                    <h3 className="mt-5 text-3xl sm:text-4xl font-semibold tracking-[-0.03em] text-dark leading-[1.12]">{f.title}</h3>
                    <p className="mt-4 text-[15px] sm:text-base text-body leading-relaxed">{f.desc}</p>
                    <ul className="mt-7 space-y-3.5">
                      {f.bullets.map((b) => (
                        <li key={b} className="flex items-start gap-3">
                          <span className="w-5 h-5 rounded-full bg-brand text-dark flex items-center justify-center shrink-0 mt-0.5"><Check /></span>
                          <span className="text-[15px] text-dark/80">{b}</span>
                        </li>
                      ))}
                    </ul>
                    <Link to="/register" className="group inline-flex items-center gap-2 mt-8 text-[15px] font-semibold text-dark underline decoration-brand decoration-[2.5px] underline-offset-[6px] hover:decoration-[4px] transition-all">
                      Try it free <Arrow className="w-4 h-4 group-hover:translate-x-1 transition-transform" />
                    </Link>
                  </div>
                  <div className={`relative ${i % 2 === 1 ? 'lg:order-1' : ''}`}>
                    <div className={`absolute -inset-6 rounded-[2.5rem] ${i % 2 === 1 ? 'bg-gradient-to-bl' : 'bg-gradient-to-br'} from-brand/20 to-transparent`} aria-hidden="true" />
                    <div className="relative bg-sand rounded-[2rem] border border-border/70 p-4 sm:p-7">
                      <Window title={f.mockTitle}>{f.mock}</Window>
                    </div>
                  </div>
                </div>
              </R>
            ))}
          </div>
        </div>
      </section>

      {/* ══════════ STATS BAND ══════════ */}
      <section className="bg-dark relative overflow-hidden">
        <div className="absolute -top-32 left-1/3 w-[500px] h-[300px] bg-brand/15 rounded-full blur-[140px]" aria-hidden="true" />
        <div className="relative max-w-[1200px] mx-auto px-6 lg:px-8 py-16 sm:py-20">
          <div className="grid grid-cols-2 lg:grid-cols-4 gap-y-12 gap-x-6">
            {stats.map((s, i) => (
              <R key={s.l} delay={i * 90}>
                <div className={`text-center ${i > 0 ? 'lg:border-l lg:border-white/10' : ''}`}>
                  <p className="text-4xl sm:text-5xl font-semibold tracking-[-0.03em] text-white">{s.v.replace(/([+%s])$/, '')}<span className="text-brand">{(s.v.match(/([+%s])$/) || [''])[0]}</span></p>
                  <p className="mt-2.5 text-[13px] text-white/40 font-medium">{s.l}</p>
                </div>
              </R>
            ))}
          </div>
        </div>
      </section>

      {/* ══════════ HOW IT WORKS ══════════ */}
      <section id="how" className="py-24 sm:py-32 scroll-mt-20">
        <div className="max-w-[1200px] mx-auto px-6 lg:px-8">
          <R>
            <Eyebrow>How it works</Eyebrow>
            <h2 className="mt-5 text-4xl sm:text-[2.9rem] font-semibold tracking-[-0.03em] text-dark leading-[1.08] max-w-2xl">
              Up and running in <Hi>minutes,</Hi> not months.
            </h2>
          </R>
          <div className="mt-16 relative">
            <div className="hidden md:block absolute top-7 left-[calc(16.66%+28px)] right-[calc(16.66%+28px)] border-t-2 border-dashed border-border" aria-hidden="true" />
            <div className="grid md:grid-cols-3 gap-10 md:gap-8">
              {steps.map((s, i) => (
                <R key={s.n} delay={i * 120}>
                  <div className="relative md:text-center">
                    <div className="flex md:justify-center items-center gap-4">
                      <span className="relative z-10 w-14 h-14 rounded-2xl bg-dark text-brand flex items-center justify-center text-lg font-bold shadow-lg shadow-dark/10">{s.n}</span>
                      <span className="md:absolute md:top-16 md:left-1/2 md:-translate-x-1/2 text-[11px] font-bold text-dark bg-brand/30 rounded-full px-3 py-1 uppercase tracking-wide whitespace-nowrap">{s.time}</span>
                    </div>
                    <h3 className="mt-7 md:mt-12 text-xl font-semibold text-dark tracking-tight">{s.title}</h3>
                    <p className="mt-3 text-[15px] text-body leading-relaxed md:max-w-[300px] md:mx-auto">{s.desc}</p>
                  </div>
                </R>
              ))}
            </div>
          </div>
        </div>
      </section>

      {/* ══════════ TESTIMONIALS ══════════ */}
      <section id="customers" className="py-24 sm:py-32 bg-sand scroll-mt-20">
        <div className="max-w-[1200px] mx-auto px-6 lg:px-8">
          <R>
            <Eyebrow>Customers</Eyebrow>
            <h2 className="mt-5 text-4xl sm:text-[2.9rem] font-semibold tracking-[-0.03em] text-dark leading-[1.08] max-w-2xl">
              Trusted by exacting tax <Hi>professionals.</Hi>
            </h2>
          </R>
          <div className="mt-16 grid md:grid-cols-3 gap-5">
            {testimonials.map((t, i) => (
              <R key={t.name} delay={i * 110}>
                <figure className="h-full bg-white rounded-3xl p-8 border border-border/80 hover:shadow-xl hover:shadow-dark/[.05] hover:-translate-y-1 transition-all duration-500 flex flex-col">
                  <span className="font-display italic text-5xl text-brand leading-none select-none" aria-hidden="true">&ldquo;</span>
                  <blockquote className="mt-3 text-[15px] text-dark/85 leading-relaxed flex-1">{t.q}</blockquote>
                  <figcaption className="mt-8 pt-6 border-t border-border flex items-center gap-3.5">
                    <span className="w-10 h-10 rounded-full bg-brand text-dark text-xs font-bold flex items-center justify-center">{t.initials}</span>
                    <span><span className="block text-sm font-bold text-dark">{t.name}</span><span className="block text-xs text-muted mt-0.5">{t.role}</span></span>
                  </figcaption>
                </figure>
              </R>
            ))}
          </div>
        </div>
      </section>

      {/* ══════════ SECURITY ══════════ */}
      <section className="py-24 sm:py-28">
        <div className="max-w-[1200px] mx-auto px-6 lg:px-8">
          <R>
            <div className="bg-white border border-border rounded-[2rem] px-8 sm:px-12 py-12 sm:py-14">
              <div className="grid lg:grid-cols-[auto,1fr] gap-10 lg:gap-20 items-start">
                <div className="max-w-[260px]">
                  <Eyebrow>Security</Eyebrow>
                  <h2 className="mt-4 text-2xl sm:text-[1.7rem] font-semibold tracking-[-0.02em] text-dark leading-tight">Built for privileged work.</h2>
                </div>
                <div className="grid sm:grid-cols-3 gap-8 sm:gap-6">
                  {security.map((s) => (
                    <div key={s.title}>
                      <span className="w-10 h-10 rounded-xl bg-brand/30 text-dark flex items-center justify-center">{s.icon}</span>
                      <h3 className="mt-4 text-[15px] font-bold text-dark">{s.title}</h3>
                      <p className="mt-1.5 text-[13.5px] text-body leading-relaxed">{s.desc}</p>
                    </div>
                  ))}
                </div>
              </div>
            </div>
          </R>
        </div>
      </section>

      {/* ══════════ FAQ ══════════ */}
      <section id="faq" className="pb-24 sm:pb-32 scroll-mt-20">
        <div className="max-w-[1200px] mx-auto px-6 lg:px-8 grid lg:grid-cols-[1fr,1.4fr] gap-12 lg:gap-24">
          <div className="lg:sticky lg:top-32 self-start">
            <R>
              <Eyebrow>FAQ</Eyebrow>
              <h2 className="mt-5 text-4xl sm:text-[2.9rem] font-semibold tracking-[-0.03em] text-dark leading-[1.08]">
                Questions, <Hi>answered.</Hi>
              </h2>
              <p className="mt-5 text-base text-body leading-relaxed max-w-sm">
                Everything you need to know before bringing Taxmind into your practice.
                Something else on your mind? <a href="mailto:taxmind@tn.ey.com" className="font-semibold text-dark underline decoration-brand decoration-[2.5px] underline-offset-4 hover:decoration-[4px] transition-all">Talk to us</a>.
              </p>
            </R>
          </div>
          <R delay={100}>
            <div className="border-t border-border">
              {faqs.map((f) => <FaqItem key={f.q} {...f} />)}
            </div>
          </R>
        </div>
      </section>

      {/* ══════════ CTA ══════════ */}
      <section className="pb-24 sm:pb-32">
        <div className="max-w-[1200px] mx-auto px-6 lg:px-8">
          <R>
            <div className="relative bg-dark rounded-[2.5rem] px-8 sm:px-16 py-20 sm:py-24 text-center overflow-hidden">
              <div className="absolute -top-24 -left-16 w-[420px] h-[300px] bg-brand/20 rounded-full blur-[120px]" aria-hidden="true" />
              <div className="absolute -bottom-32 -right-16 w-[420px] h-[300px] bg-brand/10 rounded-full blur-[120px]" aria-hidden="true" />
              <div className="absolute inset-0 opacity-[0.07]" style={{ backgroundImage: 'radial-gradient(circle, #fff 0.6px, transparent 0.6px)', backgroundSize: '24px 24px' }} aria-hidden="true" />
              <div className="relative">
                <h2 className="text-4xl sm:text-[3.4rem] font-semibold tracking-[-0.03em] text-white leading-[1.06]">
                  Stop researching.<br /><span className="font-display italic font-normal text-brand tracking-normal">Start advising.</span>
                </h2>
                <p className="mt-6 mx-auto max-w-md text-white/50 text-base sm:text-lg leading-relaxed">
                  Join the tax teams modernizing their practice with Taxmind. Free trial — no credit card, no commitment.
                </p>
                <div className="mt-10 flex flex-wrap gap-3.5 justify-center">
                  <Link to="/register" className="group inline-flex items-center gap-2 bg-brand text-dark font-semibold text-[15px] pl-7 pr-6 py-3.5 rounded-full hover:shadow-xl hover:shadow-brand/30 hover:-translate-y-0.5 transition-all duration-300">
                    Start free trial <Arrow className="w-4 h-4 group-hover:translate-x-0.5 transition-transform" />
                  </Link>
                  <Link to="/login" className="inline-flex items-center gap-2 border border-white/15 text-white font-semibold text-[15px] px-7 py-3.5 rounded-full hover:bg-white/5 hover:border-white/30 transition-all duration-300">
                    Sign in
                  </Link>
                </div>
              </div>
            </div>
          </R>
        </div>
      </section>

      {/* ══════════ FOOTER ══════════ */}
      <footer className="border-t border-border bg-cream">
        <div className="max-w-[1200px] mx-auto px-6 lg:px-8 py-16">
          <div className="grid grid-cols-2 md:grid-cols-5 gap-10">
            <div className="col-span-2">
              <Link to="/" aria-label="EY Taxmind home"><EYLockup dark /></Link>
              <p className="mt-5 text-sm text-body leading-relaxed max-w-[280px]">
                AI-powered tax intelligence built by EY Tunisia — fiscal law, international treaties, and cross-border advisory.
              </p>
            </div>
            {footerCols.map((col) => (
              <div key={col.t}>
                <h4 className="text-[13px] font-bold text-dark mb-4 uppercase tracking-wide">{col.t}</h4>
                <ul className="space-y-2.5">
                  {col.links.map((l) => <li key={l}><span className="text-sm text-body hover:text-dark cursor-pointer transition-colors">{l}</span></li>)}
                </ul>
              </div>
            ))}
          </div>
          <div className="mt-14 pt-8 border-t border-border flex flex-col sm:flex-row justify-between items-center gap-3">
            <p className="text-xs text-muted">&copy; {new Date().getFullYear()} EY Taxmind. All rights reserved.</p>
            <p className="text-xs text-muted">Built in Tunisia <span className="text-brand">●</span> Hosted in the EU</p>
          </div>
        </div>
      </footer>
    </div>
  );
}
