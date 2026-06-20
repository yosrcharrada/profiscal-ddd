import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import useSystemHealth from "../hooks/useSystemHealth";
import fiscalService from "../services/fiscalService";

const fmtDate = (d) =>
  d
    ? new Date(d).toLocaleDateString(undefined, {
        day: "numeric",
        month: "short",
        year: "numeric",
      })
    : "—";
const greeting = () => {
  const h = new Date().getHours();
  return h < 12 ? "Bonjour" : h < 18 ? "Bon après-midi" : "Bonsoir";
};

function Stat({ label, value, hint, icon, loading }) {
  return (
    <div className="bg-white border border-border rounded-2xl p-5 hover:border-dark/15 hover:shadow-sm transition-all">
      <div className="flex items-center justify-between">
        <p className="text-[11px] font-bold text-muted uppercase tracking-[0.14em]">
          {label}
        </p>
        <span className="w-8 h-8 rounded-lg bg-brand/25 flex items-center justify-center">
          <svg
            className="w-4 h-4 text-dark"
            fill="none"
            viewBox="0 0 24 24"
            stroke="currentColor"
            strokeWidth={1.8}
          >
            {icon}
          </svg>
        </span>
      </div>
      {loading ? (
        <div className="h-8 w-20 bg-light rounded-lg mt-2 animate-pulse" />
      ) : (
        <p className="text-[28px] font-extrabold text-dark mt-1 leading-none tracking-tight">
          {value}
        </p>
      )}
      <p className="text-xs text-muted mt-2">{hint}</p>
    </div>
  );
}

export default function Dashboard() {
  const { user, isAdmin } = useAuth();
  const navigate = useNavigate();
  const { data: health } = useSystemHealth({ poll: 30000 });
  const [consultations, setConsultations] = useState(null);
  const [stats, setStats] = useState(null);

  useEffect(() => {
    fiscalService
      .list("")
      .then(({ data }) => setConsultations(data.data))
      .catch(() => setConsultations([]));
    fiscalService
      .stats()
      .then(({ data }) => setStats(data.data))
      .catch(() => setStats(null));
  }, []);

  const recent = (consultations || []).slice(0, 5);
  const kb =
    stats &&
    [
      { label: "Codes fiscaux", count: stats.codesCount },
      { label: "Conventions", count: stats.conventionsCount },
      { label: "Lois de finances", count: stats.loisCount },
      { label: "Doctrine & notes", count: stats.notesCount },
    ].filter((x) => x.count > 0);
  const kbMax = kb?.length ? Math.max(...kb.map((x) => Number(x.count))) : 0;

  return (
    <div className="animate-fade-up space-y-6">
      {/* hero */}
      <div className="bg-dark rounded-3xl px-8 py-9 relative overflow-hidden">
        <div className="absolute top-0 left-0 w-full h-[3px] bg-brand" />
        <div className="absolute -top-16 -right-16 w-64 h-64 bg-brand/10 rounded-full blur-3xl" />
        <div className="absolute -bottom-20 right-40 w-48 h-48 bg-brand/5 rounded-full blur-2xl" />
        <div className="relative flex items-end justify-between gap-6 flex-wrap">
          <div>
            <p className="text-brand text-[11px] font-bold uppercase tracking-[0.22em] mb-2">
              EY | Taxmind
            </p>
            <h1 className="text-[28px] sm:text-3xl font-extrabold text-white tracking-tight">
              {greeting()}, {user?.firstName} 👋
            </h1>
            <p className="text-white/50 mt-1.5 text-[15px]">
              Votre copilote fiscal — recherche, conseil et consultations
              sourcées.
            </p>
          </div>
          <div className="flex items-center gap-2.5">
            <Link
              to="/app/chat"
              className="text-sm font-semibold text-white/90 border border-white/20 hover:border-white/50 rounded-xl px-4 py-2.5 transition-colors"
            >
              Poser une question
            </Link>
            <Link
              to="/app/consultations?new=1"
              className="bg-brand text-dark text-sm font-bold rounded-xl px-4 py-2.5 hover:shadow-lg hover:shadow-brand/30 transition-all flex items-center gap-2"
            >
              <svg
                className="w-4 h-4"
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                strokeWidth={2.4}
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  d="M12 4.5v15m7.5-7.5h-15"
                />
              </svg>
              Nouvelle consultation
            </Link>
          </div>
        </div>
      </div>

      {/* KPIs */}
      <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-4 gap-4">
        <Stat
          label="Consultations"
          value={consultations ? consultations.length : "—"}
          loading={!consultations}
          hint="Mémos générés dans votre espace"
          icon={
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              d="M19.5 14.25v-2.625a3.375 3.375 0 00-3.375-3.375h-1.5A1.125 1.125 0 0113.5 7.125v-1.5a3.375 3.375 0 00-3.375-3.375H8.25m2.25 0H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 00-9-9z"
            />
          }
        />
        <Stat
          label="Passages indexés"
          value={health?.chunks ? health.chunks.toLocaleString() : "—"}
          loading={!health}
          hint="Corpus juridique tunisien complet"
          icon={
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              d="M12 21a9.004 9.004 0 008.716-6.747M12 21a9.004 9.004 0 01-8.716-6.747M12 21c2.485 0 4.5-4.03 4.5-9S14.485 3 12 3m0 18c-2.485 0-4.5-4.03-4.5-9S9.515 3 12 3m0 0a8.997 8.997 0 017.843 4.582M12 3a8.997 8.997 0 00-7.843 4.582m15.686 0A11.953 11.953 0 0112 10.5c-2.998 0-5.74-1.1-7.843-2.918m15.686 0A8.959 8.959 0 0121 12c0 .778-.099 1.533-.284 2.253m-18.432 0A8.959 8.959 0 013 12c0-.778.099-1.533.284-2.253"
            />
          }
        />
        <Stat
          label="Entités du graphe"
          value={
            stats?.totalEntities
              ? Number(stats.totalEntities).toLocaleString()
              : "—"
          }
          loading={!stats && !health}
          hint="Concepts liés dans le knowledge graph"
          icon={
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              d="M7.217 10.907a2.25 2.25 0 100 2.186m0-2.186c.18.324.283.696.283 1.093s-.103.77-.283 1.093m0-2.186l9.566-5.314m-9.566 7.5l9.566 5.314m0 0a2.25 2.25 0 103.935 2.186 2.25 2.25 0 00-3.935-2.186zm0-12.814a2.25 2.25 0 103.933-2.185 2.25 2.25 0 00-3.933 2.185z"
            />
          }
        />
        <Stat
          label="Moteur"
          value={health ? (health.ready ? "En ligne" : "Setup") : "—"}
          loading={!health}
          hint={
            health?.ready
              ? "Neo4j + LLM connectés"
              : "Vérifiez le statut en haut à droite"
          }
          icon={
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              d="M3.75 13.5l10.5-11.25L12 10.5h8.25L9.75 21.75 12 13.5H3.75z"
            />
          }
        />
      </div>

      <div className="grid lg:grid-cols-[1fr,340px] gap-5 items-start">
        {/* recent consultations */}
        <div className="bg-white border border-border rounded-2xl overflow-hidden">
          <div className="px-6 py-4 border-b border-border flex items-center justify-between">
            <h2 className="font-bold text-dark">Consultations récentes</h2>
            <Link
              to="/app/consultations"
              className="text-[13px] font-semibold text-body hover:text-dark flex items-center gap-1 transition-colors"
            >
              Tout voir
              <svg
                className="w-3.5 h-3.5"
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                strokeWidth={2.2}
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  d="M8.25 4.5l7.5 7.5-7.5 7.5"
                />
              </svg>
            </Link>
          </div>
          {!consultations && (
            <div className="p-6 space-y-3">
              {[0, 1, 2].map((i) => (
                <div
                  key={i}
                  className="h-12 bg-light rounded-xl animate-pulse"
                />
              ))}
            </div>
          )}
          {consultations?.length === 0 && (
            <div className="px-6 py-12 text-center">
              <div className="inline-flex w-12 h-12 rounded-2xl bg-brand/25 items-center justify-center mb-3">
                <svg
                  className="w-6 h-6 text-dark"
                  fill="none"
                  viewBox="0 0 24 24"
                  stroke="currentColor"
                  strokeWidth={1.6}
                >
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    d="M12 4.5v15m7.5-7.5h-15"
                  />
                </svg>
              </div>
              <p className="text-dark font-semibold">
                Aucune consultation pour l’instant
              </p>
              <p className="text-muted text-sm mt-1 mb-4">
                Générez votre premier mémo fiscal sourcé en moins d’une minute.
              </p>
              <Link
                to="/app/consultations?new=1"
                className="inline-block bg-dark text-white font-semibold text-sm rounded-xl px-5 py-2.5 hover:bg-black transition-colors"
              >
                Créer une consultation
              </Link>
            </div>
          )}
          <div className="divide-y divide-border/70">
            {recent.map((c) => (
              <button
                key={c.id}
                onClick={() => navigate(`/app/consultations/${c.id}`)}
                className="w-full text-left px-6 py-4 flex items-center gap-4 hover:bg-light/60 transition-colors group"
              >
                <span className="shrink-0 w-9 h-9 rounded-xl bg-brand/25 group-hover:bg-brand flex items-center justify-center transition-colors">
                  <svg
                    className="w-4 h-4 text-dark"
                    fill="none"
                    viewBox="0 0 24 24"
                    stroke="currentColor"
                    strokeWidth={1.8}
                  >
                    <path
                      strokeLinecap="round"
                      strokeLinejoin="round"
                      d="M19.5 14.25v-2.625a3.375 3.375 0 00-3.375-3.375h-1.5A1.125 1.125 0 0113.5 7.125v-1.5a3.375 3.375 0 00-3.375-3.375H8.25m6.75 12H9m5.25 3H9"
                    />
                  </svg>
                </span>
                <span className="min-w-0 flex-1">
                  <span className="flex items-center gap-2">
                    <span className="font-bold text-dark text-sm truncate">
                      {c.clientName}
                    </span>
                    <span className="text-[10px] font-bold text-dark bg-brand/30 rounded-full px-2 py-0.5 shrink-0">
                      {c.reference}
                    </span>
                    {c.isInternational && (
                      <span className="text-[10px] font-semibold text-body bg-light border border-border rounded-full px-2 py-0.5 shrink-0">
                        International
                      </span>
                    )}
                  </span>
                  <span className="block text-[12.5px] text-muted truncate mt-0.5">
                    {c.fiscalQuestion}
                  </span>
                </span>
                <span className="text-xs text-muted shrink-0 hidden sm:block">
                  {fmtDate(c.createdAt)}
                </span>
                <svg
                  className="w-4 h-4 text-border group-hover:text-dark group-hover:translate-x-0.5 transition-all shrink-0"
                  fill="none"
                  viewBox="0 0 24 24"
                  stroke="currentColor"
                  strokeWidth={2}
                >
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    d="M8.25 4.5l7.5 7.5-7.5 7.5"
                  />
                </svg>
              </button>
            ))}
          </div>
        </div>

        <div className="space-y-5">
          {/* quick actions */}
          <div className="bg-white border border-border rounded-2xl p-5">
            <h2 className="font-bold text-dark mb-3">Actions rapides</h2>
            <div className="space-y-2">
              {[
                {
                  l: "Rechercher dans la loi",
                  d: "Codes, conventions & doctrine",
                  to: "/app/search",
                  icon: (
                    <path
                      strokeLinecap="round"
                      strokeLinejoin="round"
                      d="M21 21l-5.197-5.197m0 0A7.5 7.5 0 105.196 5.196a7.5 7.5 0 0010.607 10.607z"
                    />
                  ),
                },
                {
                  l: "Interroger l’assistant",
                  d: "Réponses sourcées instantanées",
                  to: "/app/chat",
                  icon: (
                    <path
                      strokeLinecap="round"
                      strokeLinejoin="round"
                      d="M9.813 15.904L9 18.75l-.813-2.846a4.5 4.5 0 00-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 003.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 003.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 00-3.09 3.09z"
                    />
                  ),
                },
                {
                  l: "Nouvelle consultation",
                  d: "Mémo structuré en ~1 minute",
                  to: "/app/consultations?new=1",
                  icon: (
                    <path
                      strokeLinecap="round"
                      strokeLinejoin="round"
                      d="M12 4.5v15m7.5-7.5h-15"
                    />
                  ),
                },
                ...(isAdmin
                  ? [
                      {
                        l: "Gérer les utilisateurs",
                        d: "Rôles, accès & activité",
                        to: "/admin/users",
                        icon: (
                          <path
                            strokeLinecap="round"
                            strokeLinejoin="round"
                            d="M15 19.128a9.38 9.38 0 002.625.372 9.337 9.337 0 004.121-.952 4.125 4.125 0 00-7.533-2.493M15 19.128v-.003c0-1.113-.285-2.16-.786-3.07M15 19.128v.106A12.318 12.318 0 018.624 21c-2.331 0-4.512-.645-6.374-1.766l-.001-.109a6.375 6.375 0 0111.964-3.07M12 6.375a3.375 3.375 0 11-6.75 0 3.375 3.375 0 016.75 0z"
                          />
                        ),
                      },
                    ]
                  : []),
              ].map((a) => (
                <Link
                  key={a.l}
                  to={a.to}
                  className="flex items-center gap-3.5 rounded-xl border border-transparent hover:border-border hover:bg-light/60 p-2.5 transition-all group"
                >
                  <span className="w-9 h-9 rounded-xl bg-light group-hover:bg-brand flex items-center justify-center transition-colors shrink-0">
                    <svg
                      className="w-4 h-4 text-dark"
                      fill="none"
                      viewBox="0 0 24 24"
                      stroke="currentColor"
                      strokeWidth={1.9}
                    >
                      {a.icon}
                    </svg>
                  </span>
                  <span className="min-w-0">
                    <span className="block text-sm font-bold text-dark">
                      {a.l}
                    </span>
                    <span className="block text-xs text-muted truncate">
                      {a.d}
                    </span>
                  </span>
                </Link>
              ))}
            </div>
          </div>

          {/* knowledge base breakdown */}
          <div className="bg-white border border-border rounded-2xl p-5">
            <h2 className="font-bold text-dark mb-1">Base de connaissances</h2>
            <p className="text-xs text-muted mb-4">
              {health?.chunks
                ? `${health.chunks.toLocaleString()} passages répartis par type de texte`
                : "Connexion au moteur requise"}
            </p>
            {kb?.length ? (
              <div className="space-y-3">
                {kb.map((x) => (
                  <div key={x.label}>
                    <div className="flex items-center justify-between text-[12.5px] mb-1">
                      <span className="font-semibold text-dark">{x.label}</span>
                      <span className="text-muted font-medium">
                        {Number(x.count).toLocaleString()}
                      </span>
                    </div>
                    <div className="h-1.5 bg-light rounded-full overflow-hidden">
                      <div
                        className="h-full bg-brand rounded-full transition-all duration-700"
                        style={{
                          width: `${Math.max(6, (Number(x.count) / kbMax) * 100)}%`,
                        }}
                      />
                    </div>
                  </div>
                ))}
              </div>
            ) : (
              <div className="space-y-3">
                {[0, 1, 2, 3].map((i) => (
                  <div
                    key={i}
                    className="h-6 bg-light rounded-lg animate-pulse"
                  />
                ))}
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
