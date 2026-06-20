# EY | TAXMIND — Backend

.NET 8 Web API for the EY Tunisia fiscal consulting platform. Clean architecture with two bounded contexts, JWT authentication, and an AI-powered fiscal engine backed by Neo4j + GPT-4o.

---

## Architecture — Two Bounded Contexts

The backend is split into **two completely independent groups of projects**. This is intentional clean-architecture DDD (Domain-Driven Design). Each context owns its own domain, application logic, and infrastructure. They only meet in `Profiscal.API`.

### Context 1 — Identity & Auth (`Profiscal.*`)

| Project | Responsibility |
|---|---|
| `Profiscal.Domain` | EF entities: `AppUser`, `RefreshToken`, `AuthAuditLog`, `FiscalConsultation` (persistence model) |
| `Profiscal.Application` | Interfaces: `IAuthService`, `IJwtTokenService`, `IUserAdminService`, `IApplicationDbContext` |
| `Profiscal.Infrastructure` | Implementations: `AuthService`, `JwtTokenService`, `UserAdminService`, EF/SQLite `AppDbContext` + migrations |
| `Profiscal.Contracts` | Request/Response DTOs for auth endpoints |

**What it does:** JWT access tokens (15 min) + rotating hashed refresh tokens (7 days). Brute-force lockout (5 attempts → 15 min lock). Full audit log of every login/logout/token event. Multi-device session management. ASP.NET Core Identity for password hashing and role management.

### Context 2 — Fiscal Engine (`FiscalPlatform.*`)

| Project | Responsibility |
|---|---|
| `FiscalPlatform.Domain` | `Consultation` aggregate root, `LegalBranch` / `Country` value objects, domain events |
| `FiscalPlatform.Application` | CQRS handlers: `GenerateConsultation`, `RefineConsultation`, `ChatQuery`, `SearchLegalDocuments`, `GetKnowledgeBaseStats` |
| `FiscalPlatform.Infrastructure` | AI agents: `LlmAgent`, `EmbedSearchAgent`, `RetrievalAgent`, `RetrievalPlannerAgent`, `DocumentGenerationAgent`. Domain services: `BranchDetector`, `CountryDetector`, `KeywordExtractor`. Semantic Kernel plugins for refinement. |

**What it does:** Generates Word-formatted fiscal consultations via an 8-step AI pipeline (planner → embed → Neo4j → 3 parallel LLM phases → Word export). Provides a legal chatbot and semantic search over a 67k+ chunk knowledge base.

### Why two sets of projects?

- **Independent evolution:** Auth changes (adding OAuth, 2FA) don't touch fiscal engine code. Fiscal engine upgrades (new LLM, new Neo4j schema) don't touch auth code.
- **Different databases:** Auth uses SQLite via EF Core. The fiscal knowledge base lives in Neo4j. Keeping them separate makes this boundary explicit.
- **Different tech stacks:** Auth uses ASP.NET Core Identity. Fiscal engine uses Semantic Kernel, Neo4j.Driver, OpenXML.
- **This is standard DDD practice** for a product that will grow. It is NOT duplication — each project serves a different purpose.

---

## Projects at a Glance

```
backend/
├── Profiscal.API/               ← Entry point: controllers, middleware, Program.cs
│   ├── Controllers/
│   │   ├── AuthController.cs    ← /api/auth/*
│   │   ├── UsersController.cs   ← /api/users/* (admin only)
│   │   ├── FiscalController.cs  ← /api/fiscal/* (search, chat, consultations, refine)
│   │   └── DocumentController.cs← /api/document/extract (file text extraction)
│   ├── Fiscal/                  ← Fiscal persistence adapters (not part of domain)
│   │   ├── ConsultationStore.cs ← Writes ConsultationOutput to SQLite
│   │   ├── EfConsultationRepository.cs
│   │   ├── EfFeedbackAgent.cs
│   │   ├── Neo4jSearchAgent.cs
│   │   └── FileTextExtractor.cs ← Extracts text from .txt / .docx / .pdf uploads
│   ├── appsettings.json         ← Non-secret config (URLs, JWT issuer/audience)
│   └── appsettings.Development.json ← Dev overrides (NO secrets — use user-secrets)
│
├── Profiscal.Domain/            ← Auth entities + domain exceptions
├── Profiscal.Application/       ← Auth interfaces (contracts between layers)
├── Profiscal.Infrastructure/    ← Auth implementations (EF, Identity, JWT)
├── Profiscal.Contracts/         ← Auth DTOs (request/response shapes)
│
├── FiscalPlatform.Domain/       ← Consultation aggregate, value objects, events
├── FiscalPlatform.Application/  ← CQRS commands/queries/handlers for fiscal engine
└── FiscalPlatform.Infrastructure/ ← AI agents, Neo4j queries, Semantic Kernel
```

---

## Consultation Generation Pipeline

Each consultation goes through this 8-step pipeline (`GenerateConsultationCommandHandler`):

```
Step 1  Non-LLM detection      BranchDetector, CountryDetector, KeywordExtractor         ~100ms
Step 2  RetrievalPlannerAgent   TRUE ReAct agent (2 rounds, parallel tool calls)          ~13-16s
         → Identifies income type (redevance/salaire/dividende), ES risk,
           fetches targeted convention articles + Note Commune N°2/2015
Step 3  Embed search            Python server @ :8081, sentence-transformers 768-dim       ~2s
Step 4  Neo4j retrieval         Keyword + graph expansion over 67k chunks                 ~3s
Step 4b Source merge            Planner > Embed > Neo4j, deduplication, diversity cap
Step 5  LLM Phase 1             contexte_faits, etendue, sommaire, pays_non_resident       ~15s
Step 5b Convention fetch        If Phase 1 detects a country not in Step 1, fetch convs    ~2s
Step 6  LLM Phase 2+3 (‖)      analyses ‖ analysis_table — run in parallel               ~25s
Step 7  Document generation     Fill template_fr.docx (EY branded), return .docx bytes    ~500ms
Step 8  Persist                 Write to SQLite FiscalConsultations table (fire & forget)
```

Anti-hallucination rules baked into the system prompt:
- Every rate (15%, 5%, 2.5%...) MUST cite `[Sn]` — never from memory
- ES mandatory sequence for foreign providers (Art.5 → Art.12 → Art.52)
- Note Commune N°2/2015 applied for all countries except Allemagne
- Art.92 CIRPPIS = LF reference, not an autonomous code article
- Convention exclusion when no country is detected
- Reflective loop: retry Phase 1 if JSON is malformed

---

## Database

| Store | What | Where |
|---|---|---|
| **SQLite** | Users, sessions, audit logs, saved consultations | `Profiscal.API/profiscal.db` |
| **Neo4j** | 67k+ legal document chunks, entity graph, vector index | `neo4j://127.0.0.1:7687` db `tunisian-fiscal` |

SQLite tables: `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `RefreshTokens`, `AuthAuditLogs`, `FiscalConsultations`, `__EFMigrationsHistory`.

---

## Setting Up Secrets

**Never commit real secrets.** The `appsettings*.json` files are committed with **empty strings** for all sensitive values. Set them in one of two ways:

### Option A — .NET User Secrets (recommended for local dev)

```bash
cd backend/Profiscal.API
dotnet user-secrets init
dotnet user-secrets set "Jwt:Key"          "YourRandomSecretKey32CharsMinimum!!"
dotnet user-secrets set "Neo4j:Password"   "your-neo4j-password"
dotnet user-secrets set "OpenAI:ApiKey"    "sk-proj-..."
# For Azure OpenAI instead of standard OpenAI:
dotnet user-secrets set "OpenAI:Endpoint"  "https://your-azure-endpoint.com"
```

User secrets are stored in `~/.microsoft/usersecrets/` and **never committed**.

### Option B — Environment Variables

ASP.NET Core automatically reads env vars with `__` as the section separator:

```bash
export JWT__KEY="YourRandomSecretKey32CharsMinimum!!"
export NEO4J__PASSWORD="your-neo4j-password"
export OPENAI__APIKEY="sk-proj-..."
```

---

## Running Locally

You need 4 terminals:

```bash
# 1. Neo4j (must already be installed with the tunisian-fiscal database loaded)
#    Start from Neo4j Desktop or: neo4j start

# 2. Python embed server (semantic search)
./start-embed-server.sh

# 3. .NET API
cd backend/Profiscal.API
dotnet run

# 4. React frontend
cd frontend
npm install && npm start
```

The API starts on `https://localhost:5001` (or `http://localhost:5000`). Swagger UI at `/swagger`.

**Minimum viable run** (no AI features): start only Neo4j + the .NET API. Search, auth, and saved consultation listing work. Chat and generation require the embed server + OpenAI key.

---

## API Overview

### Auth — `/api/auth`

| Method | Endpoint | Description |
|---|---|---|
| POST | `/api/auth/register` | Create account |
| POST | `/api/auth/login` | Login, get JWT + refresh token |
| POST | `/api/auth/refresh` | Rotate refresh token |
| POST | `/api/auth/logout` | Revoke session |
| GET  | `/api/auth/me` | Current user profile |
| GET  | `/api/auth/sessions` | Active sessions (multi-device) |
| DELETE | `/api/auth/sessions/{id}` | Revoke a specific session |
| PUT  | `/api/auth/change-password` | Change password (invalidates all sessions) |

### Fiscal — `/api/fiscal` _(requires JWT)_

| Method | Endpoint | Description |
|---|---|---|
| POST | `/api/fiscal/search` | Keyword search over legal knowledge base |
| POST | `/api/fiscal/chat` | Legal chatbot (RAG over Neo4j) |
| GET  | `/api/fiscal/health` | Check Neo4j, embed server, LLM status |
| GET  | `/api/fiscal/stats` | Knowledge base statistics |
| POST | `/api/fiscal/consultations/generate` | Generate full consultation (8-step pipeline) |
| GET  | `/api/fiscal/consultations` | List consultations (mine / all for admins) |
| GET  | `/api/fiscal/consultations/{id}` | Get saved consultation with full output |
| POST | `/api/fiscal/consultations/rate` | Rate a consultation (1-5 stars) |
| POST | `/api/fiscal/consultations/export` | Export consultation as Word .docx |
| PUT  | `/api/fiscal/consultations/{id}/output` | Save edited consultation sections |
| POST | `/api/fiscal/refine/session/start` | Start an editing session |
| POST | `/api/fiscal/refine/message` | Send edit instruction (SK agent refines section) |
| POST | `/api/fiscal/refine/session/end` | End editing session |

### Documents — `/api/document` _(requires JWT)_

| Method | Endpoint | Description |
|---|---|---|
| POST | `/api/document/extract` | Upload .txt/.docx/.pdf, get extracted text |

---

## Tech Stack

| Layer | Technology |
|---|---|
| Framework | ASP.NET Core 8.0 |
| ORM | Entity Framework Core 8 (SQLite) |
| Auth | ASP.NET Core Identity + JWT Bearer |
| CQRS | MediatR 12.2.0 |
| Validation | FluentValidation 11.9.0 |
| AI | Microsoft.SemanticKernel 1.45.0 |
| Graph DB | Neo4j.Driver 5.18.0 |
| Word generation | DocumentFormat.OpenXml 3.0.2 |
| Embed server | Python/Flask + sentence-transformers (external) |

---

## Default Admin Account

Seeded at startup (development only, from `Seed:*` config):
- Email: `admin@taxmind.local`
- Password: set via user-secrets `Seed:AdminPassword`

Change this before any shared deployment.
