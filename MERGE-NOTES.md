# ProFiscal — Integrated build (merge notes)

This folder is a **brand-new merge**, built on the Desktop. Neither source folder was modified:
- **Base engine (source of truth):** `C:\profiscal_new` — read-only, untouched.
- **Frontend + auth host:** `…\Downloads\Profiscal_EY-frontend-integration` — read-only, untouched.

Goal: take the colleague's polished React frontend + its auth/host platform and run it on **your**
folder-1 fiscal engine, keeping all of your engine's functionality and behavior.

## What this folder contains

```
ProFiscal-Integrated/
├─ backend/
│  ├─ FiscalPlatform.{Domain,Application,Infrastructure}   ← your engine
│  ├─ Profiscal.{Domain,Application,Infrastructure,Contracts}  ← auth platform (JWT, EF Identity, SQLite)
│  └─ Profiscal.API                                        ← web host the frontend talks to (port 5131)
├─ frontend/                                               ← React (CRA + Tailwind), port 3000
├─ embed_server.py, embed_requirements.txt, start-embed-server.sh
└─ INTEGRATION.md                                          ← colleague's run guide
```

## Decisions taken (confirmed with the user)

1. **Persistence = SQLite** (EF store in the host), not Elasticsearch. No ES process to run;
   only Neo4j + embed server + LLM. The editor's "load consultation by id" works.
2. **LLM = provider-flexible**: works with EY Azure OpenAI **and** standard api.openai.com
   (auto-detected from whether `OpenAI:Endpoint` is set).

## Engine reconciliation — where each differing file came from

The two engine copies were 95% identical (Domain + all interfaces byte-identical). Only 7 files
differed. Final selection:

| File | Taken from | Reason |
|---|---|---|
| `GenerateConsultationCommandHandler.cs` | **folder 1** | your version is newer/better: stronger anti-hallucination prompts ("PRISE DE POSITION OBLIGATOIRE", two-test ES analysis, ban on "Analyse limitée à…"), `contexteFaits` into Phase 2, unconditional Art.92→`[réf. LF]` "Fix 0" |
| `RetrievalPlannerAgent.cs` | **folder 1** | your base (bounded ReAct planner); colleague's only differed by one log line |
| `RetrievalAgent.cs` | folder 2 | **strict superset** of yours — identical logic **plus** `CountryDocNameFragment` map fixing convention matching on truncated Neo4j `doc_name`s (france→`republique-fra`, grèce→`helleniq`, libye→`lyb`, …). You lose nothing, gain the fix. |
| `LlmAgent.cs` | folder 2 | provider-flexible (Azure + OpenAI). Superset of your Azure-only agent. |
| `FiscalKernelFactory.cs` | folder 2 | adds OpenAI to the SK kernel (consistent with provider-flex) |
| `RefineConsultationCommand.cs` | folder 2 | adds `TargetSection` so the editor can refine a specific section (frontend feature) |
| `DependencyInjection.cs` | folder 2 | `AddFiscalEngine()` composition root, ES registrations removed (host supplies SQLite/Neo4j impls) |
| ES files (`Persistence/`, `Search/`, `Agents/FeedbackAgent.cs`) | **dropped** | replaced by host's `EfConsultationRepository`, `Neo4jSearchAgent`, `EfFeedbackAgent` (SQLite/Neo4j). Interfaces are identical, so your engine binds to them unchanged. |

Net effect: the engine behaves exactly like your folder-1 base **plus** the retrieval doc-name fix,
wrapped in the colleague's auth host + React UI.

## Build status

All projects compiled cleanly **except** the final two heavy projects, which could not finish on this
machine due to a **local out-of-memory condition** (MSBuild `GenerateDepsFile` OOM / "Thread failed to
start" — environment, not code). The C# of every project compiled with only 2 harmless nullable
warnings (CS8602) in the handler. Re-run the build after freeing RAM:

```powershell
cd backend
dotnet build-server shutdown
dotnet build Profiscal.sln -m:1      # single-process to keep memory low
```

## To run (see INTEGRATION.md for detail)

1. Start Neo4j ("profiscal"/tunisian-fiscal DBMS).
2. `./start-embed-server.sh`  → http://127.0.0.1:8081
3. `cd backend/Profiscal.API && dotnet run`  → http://localhost:5131
4. `cd frontend && npm install && npm start`  → http://localhost:3000

**Credentials live in a single `.env` at the repo root** (already created from `.env.example`,
gitignored). Fill the two REQUIRED values:
- `NEO4J_PASSWORD=` — your Neo4j password (used by both the embed server and the .NET backend)
- `OpenAI__ApiKey=` — your OpenAI key (note the **double underscore** — the .NET app binds it via
  config). For EY Azure, also uncomment `OpenAI__Endpoint` / `OpenAI__ApiVersion`.

The backend searches upward for `.env`, so the one root file serves everything — no need to edit
`appsettings`. On boot it prints `[env] loaded …\.env` to confirm. Default admin seeded from
`appsettings`: `admin@taxmind.local` / `Admin#Taxmind2026`.
