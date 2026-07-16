import { useEffect, useRef } from "react";

/* Confirmation dialog for destructive actions.
   Replaces the "click once to arm, click again to confirm" pattern on deletes: that arming
   is invisible to a screen reader, expires on a 2.5s timer, and — worst — puts the confirm
   click on the same pixel as the first one, so a double-click deletes with no confirmation
   at all. Here the confirm button is a separate target that must be aimed at, the action is
   named in the prompt, and Escape / clicking the backdrop cancels.

   Cancel takes focus on open (never Confirm) so a stray Enter dismisses rather than deletes. */
export default function ConfirmDialog({
  open,
  title,
  message,
  confirmLabel = "Supprimer",
  cancelLabel = "Annuler",
  onConfirm,
  onCancel,
}) {
  const cancelRef = useRef();

  useEffect(() => {
    if (!open) return;
    cancelRef.current?.focus();
    const onKey = (e) => {
      if (e.key === "Escape") onCancel?.();
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open, onCancel]);

  if (!open) return null;

  return (
    <div
      className="fixed inset-0 z-[100] flex items-center justify-center p-4"
      role="dialog"
      aria-modal="true"
      aria-labelledby="confirm-title"
    >
      <div
        className="absolute inset-0 bg-dark/50 backdrop-blur-[2px] animate-fade-in"
        onClick={onCancel}
      />
      <div className="relative w-full max-w-sm bg-white rounded-2xl border border-border shadow-2xl p-5 animate-fade-in">
        <div className="flex items-start gap-3">
          <span className="w-9 h-9 rounded-xl bg-red-100 text-red-600 flex items-center justify-center shrink-0">
            <svg
              className="w-5 h-5"
              fill="none"
              viewBox="0 0 24 24"
              stroke="currentColor"
              strokeWidth={2}
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                d="M12 9v3.75m-9.303 3.376c-.866 1.5.217 3.374 1.948 3.374h14.71c1.73 0 2.813-1.874 1.948-3.374L13.949 3.378c-.866-1.5-3.032-1.5-3.898 0L2.697 16.126zM12 15.75h.007v.008H12v-.008z"
              />
            </svg>
          </span>
          <div className="min-w-0">
            <h2 id="confirm-title" className="text-[15px] font-extrabold text-dark">
              {title}
            </h2>
            {message && (
              <p className="mt-1 text-[13px] text-body leading-relaxed break-words">
                {message}
              </p>
            )}
          </div>
        </div>
        <div className="mt-5 flex items-center justify-end gap-2">
          <button
            ref={cancelRef}
            onClick={onCancel}
            className="text-[13px] font-bold rounded-lg px-3.5 py-2 border border-border text-body hover:text-dark hover:bg-light transition-colors"
          >
            {cancelLabel}
          </button>
          <button
            onClick={onConfirm}
            className="text-[13px] font-bold rounded-lg px-3.5 py-2 bg-red-600 text-white hover:bg-red-700 transition-colors shadow-sm"
          >
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
