import SourcePanel, { normalizeSource } from "./SourcePanel";

/* Slide-over overlay that opens a legal source "in the document".
   Thin wrapper around SourcePanel — used by Search, ConsultationEditor and the
   quick-ask bubble. The full Chat page embeds SourcePanel inline instead. */

export { normalizeSource };

export default function SourceDrawer({
  source,
  onClose,
  sources = [],
  onNavigate,
}) {
  if (!source) return null;
  return (
    <div className="fixed inset-0 z-[80]" role="dialog" aria-modal="true">
      <div
        className="absolute inset-0 bg-dark/40 backdrop-blur-[2px] animate-fade-in"
        onClick={onClose}
      />
      <div className="absolute right-0 top-0 h-full w-full max-w-xl shadow-2xl animate-slide-in-right">
        <SourcePanel
          source={source}
          sources={sources}
          onNavigate={onNavigate}
          onClose={onClose}
        />
      </div>
    </div>
  );
}
