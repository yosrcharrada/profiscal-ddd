import { useCallback, useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { notificationService } from "../../services/authService";

const POLL_MS = 30000;

/** Short two-tone chime via WebAudio — no asset needed. */
function useBeep() {
  const ctxRef = useRef(null);
  // Browsers require a user gesture before audio can play; resume on first interaction.
  useEffect(() => {
    const resume = () => {
      try {
        if (!ctxRef.current) {
          const AC = window.AudioContext || window.webkitAudioContext;
          if (AC) ctxRef.current = new AC();
        }
        ctxRef.current?.resume?.();
      } catch {}
    };
    window.addEventListener("pointerdown", resume, { once: true });
    window.addEventListener("keydown", resume, { once: true });
    return () => {
      window.removeEventListener("pointerdown", resume);
      window.removeEventListener("keydown", resume);
    };
  }, []);

  return useCallback(() => {
    try {
      const AC = window.AudioContext || window.webkitAudioContext;
      if (!AC) return;
      const ctx = ctxRef.current || (ctxRef.current = new AC());
      ctx.resume?.();
      const now = ctx.currentTime;
      [
        [880, 0.0],
        [1245, 0.13],
      ].forEach(([freq, at]) => {
        const osc = ctx.createOscillator();
        const gain = ctx.createGain();
        osc.type = "sine";
        osc.frequency.value = freq;
        gain.gain.setValueAtTime(0.0001, now + at);
        gain.gain.exponentialRampToValueAtTime(0.25, now + at + 0.02);
        gain.gain.exponentialRampToValueAtTime(0.0001, now + at + 0.22);
        osc.connect(gain).connect(ctx.destination);
        osc.start(now + at);
        osc.stop(now + at + 0.24);
      });
    } catch {}
  }, []);
}

const fmt = (d) =>
  d
    ? new Date(d).toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" })
    : "";

const ICON = {
  jort: "M12 7.5h1.5m-1.5 3h1.5m-7.5 3h7.5m-7.5 3h7.5m3-9h3.375c.621 0 1.125.504 1.125 1.125V18a2.25 2.25 0 01-2.25 2.25M16.5 7.5V18a2.25 2.25 0 002.25 2.25M16.5 7.5V4.875c0-.621-.504-1.125-1.125-1.125H4.125C3.504 3.75 3 4.254 3 4.875V18a2.25 2.25 0 002.25 2.25h13.5M6 7.5h3v3H6v-3z",
  task: "M9 12.75L11.25 15 15 9.75M21 12a9 9 0 11-18 0 9 9 0 0118 0z",
  info: "M11.25 11.25l.041-.02a.75.75 0 011.063.852l-.708 2.836a.75.75 0 001.063.853l.041-.021M21 12a9 9 0 11-18 0 9 9 0 0118 0zm-9-3.75h.008v.008H12V8.25z",
};

export default function NotificationBell() {
  const navigate = useNavigate();
  const beep = useBeep();
  const [items, setItems] = useState([]);
  const [unread, setUnread] = useState(0);
  const [open, setOpen] = useState(false);
  const ref = useRef(null);
  const lastUnread = useRef(null); // null until first load, so we never beep on mount

  const load = useCallback(async () => {
    try {
      const { data: res } = await notificationService.mine();
      const next = res.data;
      setItems(next.items || []);
      setUnread(next.unread || 0);
      if (lastUnread.current !== null && next.unread > lastUnread.current) {
        beep();
      }
      lastUnread.current = next.unread || 0;
    } catch {
      /* silently ignore polling errors */
    }
  }, [beep]);

  useEffect(() => {
    load();
    const id = setInterval(load, POLL_MS);
    return () => clearInterval(id);
  }, [load]);

  useEffect(() => {
    const fn = (e) => {
      if (ref.current && !ref.current.contains(e.target)) setOpen(false);
    };
    document.addEventListener("mousedown", fn);
    return () => document.removeEventListener("mousedown", fn);
  }, []);

  const openItem = async (n) => {
    if (!n.isRead) {
      try {
        await notificationService.read(n.id);
      } catch {}
      setItems((xs) => xs.map((x) => (x.id === n.id ? { ...x, isRead: true } : x)));
      setUnread((u) => {
        const v = Math.max(0, u - 1);
        lastUnread.current = v;
        return v;
      });
    }
    setOpen(false);
    if (n.linkUrl) navigate(n.linkUrl);
  };

  const markAll = async () => {
    try {
      await notificationService.readAll();
    } catch {}
    setItems((xs) => xs.map((x) => ({ ...x, isRead: true })));
    setUnread(0);
    lastUnread.current = 0;
  };

  return (
    <div className="relative" ref={ref}>
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-label="Notifications"
        title="Notifications"
        className="relative w-9 h-9 rounded-lg flex items-center justify-center text-muted dark:text-[#a0a0b0] hover:text-dark dark:hover:text-white hover:bg-light/80 transition-colors"
      >
        <svg
          className="w-[18px] h-[18px]"
          fill="none"
          viewBox="0 0 24 24"
          stroke="currentColor"
          strokeWidth={1.8}
        >
          <path
            strokeLinecap="round"
            strokeLinejoin="round"
            d="M14.857 17.082a23.848 23.848 0 005.454-1.31A8.967 8.967 0 0118 9.75V9A6 6 0 006 9v.75a8.967 8.967 0 01-2.312 6.022c1.733.64 3.56 1.085 5.455 1.31m5.714 0a24.255 24.255 0 01-5.714 0m5.714 0a3 3 0 11-5.714 0"
          />
        </svg>
        {unread > 0 && (
          <span className="absolute -top-0.5 -right-0.5 min-w-[16px] h-[16px] px-1 rounded-full bg-red-500 text-white text-[9px] font-bold flex items-center justify-center ring-2 ring-white dark:ring-[#1a1a24]">
            {unread > 9 ? "9+" : unread}
          </span>
        )}
      </button>

      {open && (
        <div className="absolute right-0 mt-2 w-80 max-w-[90vw] bg-white dark:bg-[#1f1f2b] rounded-xl shadow-xl border border-border py-1 animate-scale-in origin-top-right z-[60]">
          <div className="px-4 py-2.5 border-b border-border/60 flex items-center justify-between">
            <p className="text-[13px] font-bold text-dark dark:text-white">
              Notifications
            </p>
            {unread > 0 && (
              <button
                onClick={markAll}
                className="text-[11px] font-semibold text-muted hover:text-dark dark:hover:text-white transition-colors"
              >
                Tout marquer lu
              </button>
            )}
          </div>

          <div className="max-h-[360px] overflow-y-auto">
            {items.length === 0 && (
              <p className="px-4 py-8 text-center text-[12px] text-muted">
                Aucune notification.
              </p>
            )}
            {items.map((n) => (
              <button
                key={n.id}
                onClick={() => openItem(n)}
                className={`w-full text-left flex items-start gap-2.5 px-4 py-2.5 border-b border-border/30 last:border-0 transition-colors hover:bg-light/60 dark:hover:bg-white/5 ${
                  n.isRead ? "" : "bg-brand/5"
                }`}
              >
                <span
                  className={`mt-0.5 w-6 h-6 shrink-0 rounded-lg flex items-center justify-center ${
                    n.type === "jort"
                      ? "bg-red-100 text-red-500 dark:bg-red-500/15"
                      : n.type === "task"
                        ? "bg-emerald-100 text-emerald-600 dark:bg-emerald-500/15"
                        : "bg-blue-100 text-blue-600 dark:bg-blue-500/15"
                  }`}
                >
                  <svg
                    className="w-3.5 h-3.5"
                    fill="none"
                    viewBox="0 0 24 24"
                    stroke="currentColor"
                    strokeWidth={1.8}
                  >
                    <path
                      strokeLinecap="round"
                      strokeLinejoin="round"
                      d={ICON[n.type] || ICON.info}
                    />
                  </svg>
                </span>
                <span className="min-w-0 flex-1">
                  <span className="flex items-center gap-1.5">
                    <span className="text-[12px] font-bold text-dark dark:text-white truncate">
                      {n.title}
                    </span>
                    {!n.isRead && (
                      <span className="w-1.5 h-1.5 rounded-full bg-red-500 shrink-0" />
                    )}
                  </span>
                  <span className="block text-[11.5px] text-body dark:text-[#c0c0cc] line-clamp-2 mt-0.5">
                    {n.body}
                  </span>
                  <span className="block text-[10px] text-muted mt-1">
                    {fmt(n.createdAt)}
                  </span>
                </span>
              </button>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
