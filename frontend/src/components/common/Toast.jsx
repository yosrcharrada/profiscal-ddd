import { createContext, useCallback, useContext, useState } from 'react';

const ToastContext = createContext(null);

const ICONS = {
  success: <path strokeLinecap="round" strokeLinejoin="round" d="M9 12.75L11.25 15 15 9.75M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />,
  error:   <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m9-.75a9 9 0 11-18 0 9 9 0 0118 0zm-9 3.75h.008v.008H12v-.008z" />,
  info:    <path strokeLinecap="round" strokeLinejoin="round" d="M11.25 11.25l.041-.02a.75.75 0 011.063.852l-.708 2.836a.75.75 0 001.063.853l.041-.021M21 12a9 9 0 11-18 0 9 9 0 0118 0zm-9-3.75h.008v.008H12V8.25z" />,
};

const STYLES = {
  success: 'border-green-200 bg-green-50 text-green-700',
  error:   'border-red-200 bg-red-50 text-red-600',
  info:    'border-border bg-white text-dark',
};

export function ToastProvider({ children }) {
  const [toasts, setToasts] = useState([]);

  const dismiss = useCallback((id) => setToasts((t) => t.filter((x) => x.id !== id)), []);

  const toast = useCallback((message, type = 'info', ms = 3500) => {
    const id = Math.random().toString(36).slice(2);
    setToasts((t) => [...t, { id, message, type }]);
    if (ms) setTimeout(() => dismiss(id), ms);
  }, [dismiss]);

  return (
    <ToastContext.Provider value={{ toast }}>
      {children}
      <div className="fixed bottom-6 right-6 z-[100] flex flex-col gap-2.5 max-w-sm">
        {toasts.map((t) => (
          <div key={t.id} role="status"
            className={`flex items-start gap-3 rounded-xl border px-4 py-3 shadow-lg shadow-dark/10 animate-scale-in ${STYLES[t.type]}`}>
            <svg className="w-5 h-5 shrink-0 mt-0.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>{ICONS[t.type]}</svg>
            <p className="text-sm font-medium leading-snug flex-1">{t.message}</p>
            <button onClick={() => dismiss(t.id)} className="text-current/50 hover:text-current shrink-0">
              <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" /></svg>
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export const useToast = () => {
  const ctx = useContext(ToastContext);
  if (!ctx) return { toast: () => {} };
  return ctx;
};
