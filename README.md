# ProFiscal-DDD — Plateforme de Consultation Fiscale Augmentée
### EY Tunisia · Tax Technology · 2026

---

## What is ProFiscal-DDD?

ProFiscal-DDD is an AI-powered fiscal consultation platform built for EY Tunisia's tax team.
It generates professional Word (.docx) consultations in under 2 minutes by combining:

- **GraphRAG** — 30,000+ legal document chunks indexed in Neo4j
- **Semantic search** — multilingual sentence embeddings (768-dim)
- **GPT-4o via Azure OpenAI** — structured legal analysis
- **Semantic Kernel Agents** — true agentic refinement with tool-calling
- **Elasticsearch** — BM25 full-text search + consultation history + ratings

---

## Architecture

This project follows **DDD + Clean Architecture + CQRS** across 4 layers with strict compile-time dependency enforcement:

```
FiscalPlatform.sln
├── FiscalPlatform.Domain/           ← Business rules, zero external dependencies
├── FiscalPlatform.Application/      ← Use cases (CQRS with MediatR + SK Agents)
├── FiscalPlatform.Infrastructure/   ← External services (Neo4j, ES, OpenAI, SK)
└── FiscalPlatform.API/              ← HTTP layer (thin controllers only)
```

### Project Structure

```
FiscalPlatform.Domain/
├── Aggregates/Consultation/    ← Rich aggregate root: Rate(), Refine(), BeginSession()
├── ValueObjects/               ← LegalBranch (IS/IRPP/TVA/Retenue/PrixTransfert), Country
├── Repositories/               ← IConsultationRepository (interface only)
├── Events/                     ← ConsultationGeneratedEvent, RatedEvent, RefinedEvent
└── Exceptions/                 ← ConsultationGenerationException, NoSourcesFoundException

FiscalPlatform.Application/
├── Consultation/Commands/
│   ├── GenerateConsultation/   ← Orchestrator: Parallel Execution (Phase 2‖3)
│   ├── RefineConsultation/     ← TRUE SK ChatCompletionAgent with tool-calling
│   └── RateConsultation/       ← Star rating + FeedbackAgent trigger
├── Consultation/Queries/       ← GetConsultationHistory
├── Search/Queries/             ← SearchLegalDocuments
├── KnowledgeBase/Queries/      ← GetStats
├── Chat/Queries/               ← GraphRAG chat
└── Common/
    ├── Interfaces/Agents/      ← IRetrievalAgent, IEmbedSearchAgent, ILlmAgent...
    ├── Interfaces/Services/    ← IBranchDetector, ICountryDetector, ISessionStore
    ├── Behaviours/             ← LoggingBehaviour, ValidationBehaviour (MediatR pipeline)
    └── DTOs/                   ← ConsultationOutput, LegalSourceDto, AnalysisRow

FiscalPlatform.Infrastructure/
├── Agents/
│   ├── LlmAgent.cs             ← Azure OpenAI HTTP client (3 calls per consultation)
│   ├── RetrievalAgent.cs       ← All Neo4j Cypher queries + newest-doc filtering
│   ├── EmbedSearchAgent.cs     ← Python embed server HTTP client (768-dim vectors)
│   ├── DocumentGenerationAgent.cs ← Word .docx generation via OpenXML
│   └── FeedbackAgent.cs        ← ES ratings storage
├── Kernel/                     ← Semantic Kernel infrastructure
│   ├── FiscalKernelFactory.cs  ← Builds SK Kernel with AzureOpenAI + plugins
│   └── Plugins/
│       ├── RetrievalPlugin.cs  ← [KernelFunction] semantic_search, keyword_search, search_convention, full_retrieval
│       └── LegalAnalysisPlugin.cs ← [KernelFunction] analyze_fiscal_point, refine_section, generate_sommaire, answer_fiscal_question
├── Guardrails/
│   └── FiscalGuardrails.cs     ← Input guardrail (fiscal keyword check) + Output guardrail (citation validation)
├── Memory/
│   └── RewardMemory.cs         ← RLHF: ES ratings → source score boost/penalty
├── DomainServices/             ← BranchDetector, CountryDetector, KeywordExtractor (rule-based)
├── Persistence/                ← ElasticsearchConsultationRepository
├── Search/                     ← ElasticsearchSearchAgent (BM25 full-text)
└── DependencyInjection.cs      ← Composition root — all registrations

FiscalPlatform.API/
├── Controllers/                ← 7 controllers, one per bounded context
├── Middleware/                 ← Global exception handling
└── Requests/                  ← API-layer DTOs + FileTextExtractor
```

---

## Multi-Agent Architecture

### Agent Classification

| Agent | Type | Responsibility |
|---|---|---|
| `BranchDetector` | Rule-based service | Detect IS / IRPP / TVA / Retenue / PrixTransfert |
| `CountryDetector` | Rule-based service | Detect country + international flag |
| `KeywordExtractor` | Rule-based service | Extract keywords and entities |
| `EmbedSearchAgent` | Vector search | Semantic similarity via 768-dim embeddings |
| `RetrievalAgent` | Graph queries | Neo4j Cypher — newest docs, diversity, hierarchy |
| `LlmAgent` | Azure OpenAI | GPT-4o calls — exactly 3 per consultation |
| `DocumentGenerationAgent` | OpenXML | Word .docx template filling |
| `FeedbackAgent` | ES writes | Rating storage |
| **`ChatCompletionAgent`** | **TRUE SK Agent** | **Refinement: Brain + Memory + Tool-calling** |

### True SK Agent — Consultation Refinement

The `RefineConsultationCommandHandler` uses a genuine `ChatCompletionAgent` (Semantic Kernel 1.45):

- **Brain** — Reasons about the user's instruction and decides which tools to invoke
- **Memory** — `ChatHistory` (volatile session) + `RewardMemory` (ES ratings influence future retrievals)
- **Actions/Tools** — 6 `[KernelFunction]` tools the agent can call autonomously:

```
Retrieval.semantic_search      → broad vector search for new sources
Retrieval.search_convention    → scoped search in a country's convention
Retrieval.keyword_search       → keyword search + reward memory boost
Retrieval.full_retrieval       → complete pipeline (Neo4j + embed combined)
Analysis.refine_section        → rewrite a section based on instruction
Analysis.analyze_fiscal_point  → deep analysis of a specific point
Analysis.generate_sommaire     → regenerate executive summary
Analysis.answer_fiscal_question → answer a direct question
```

Example agent flow:
```
User: "Ajoute des sources sur la retenue pour les non-résidents français"

Agent reasons: "User wants more sources → call keyword_search first"
Agent calls:   Retrieval.keyword_search("retenue non-résident", "Retenue")
Agent gets:    8 new sources from Neo4j with reward boost applied
Agent reasons: "Now refine the analyses section with new sources"
Agent calls:   Analysis.refine_section("analyses", currentContent, instruction, newSources)
Agent returns: Updated analyses with new [Sn] citations
```

---

## Workflow Orchestration Patterns

| Feature | Pattern | Why |
|---|---|---|
| Consultation generation | **Parallel Execution inside Orchestrator** | Phase 2‖Phase 3 run simultaneously — under 1 min |
| Branch/country detection | **Conditional Branching** | Decides which agents activate — zero LLM |
| Rating, persistence | **Event-Driven Choreography** | Non-blocking, decoupled from generation |
| Conversation/refinement | **State Machine + Human-in-the-Loop** | Generated → UnderReview → Refined → Approved |
| Source retrieval | **Hierarchical Delegation** | Handler → RetrievalAgent → sub-queries |
| JSON parse failure | **Reflective Loop** (max 2 iterations) | Retry with corrected prompt |

---

## Guardrails

### Input Guardrail
- Verifies the question contains fiscal keywords before calling GPT-4o
- Blocks off-topic requests — saves tokens and prevents misuse
- Returns a clear error message if non-fiscal

### Output Guardrail
After generation, automatically checks:
1. All `[Sn]` citations reference sources that actually exist
2. Analyses section is not empty or too short
3. Each analysis table row has a clear verdict (OUI/NON/SOUMIS/EXONÉRÉ)
4. No hallucinated citation patterns detected
5. Minimum 3 citations present

---

## Memory Architecture

| Type | Storage | Used for | Lifetime |
|---|---|---|---|
| Volatile (short-term) | `ConcurrentDictionary<sessionId, ChatHistory>` | Active refinement conversation | Active session |
| Episodic | Elasticsearch — full consultation JSON | History tab, search by client | Permanent |
| Reward (RLHF) | Elasticsearch — ratings + source metadata | Boost/demote sources in future retrievals | Permanent |

---

## Legal Hierarchy (strictly enforced)

```
International case:  Convention → Codes → Lois de Finances → Doctrine
Local case:          Codes → Lois de Finances → Doctrine
```

---

## API Endpoints

| Method | URL | Description |
|---|---|---|
| POST | /api/consultation/generate | Generate a fiscal consultation (.docx) |
| GET  | /api/consultation/history?client=X | Get consultation history by client |
| POST | /api/consultation/session/start | Start a refinement conversation session |
| POST | /api/consultation/session/end | End and archive the session |
| POST | /api/refinement/message | Send a message to the SK refinement agent |
| POST | /api/rating | Submit a 1–5 star rating |
| POST | /api/search | Full-text legal document search |
| GET  | /api/stats | Knowledge base statistics |
| POST | /api/chat | GraphRAG chat |
| POST | /api/document/extract | Extract text from uploaded file |
| GET  | /api/search/health | Elasticsearch health |
| GET  | /api/stats/health | Neo4j health |

Swagger UI: `http://localhost:8080/swagger`

---

## Prerequisites

| Tool | Version | Purpose |
|---|---|---|
| .NET SDK | 8.0+ | C# backend |
| Python | 3.11+ | Embed server |
| Neo4j | 5.x | Graph knowledge base (67K nodes, 291K relations) |
| Elasticsearch | 9.x | Full-text search + history + ratings |
| GPT-4o | EY Azure OpenAI | LLM generation (EY network required) |

---

## Setup

### 1 — Clone

```bash
git clone https://github.com/yosrcharrada/profiscal-ddd.git
cd profiscal-ddd
```

### 2 — Environment variables

Create `.env` at the solution root (never commit this):

```env
NEO4J_URI=neo4j://127.0.0.1:7687
NEO4J_DATABASE=tunisian-fiscal
NEO4J_USERNAME=neo4j
NEO4J_PASSWORD=your_password

OPENAI_API_KEY=your_ey_azure_key
OPENAI_ENDPOINT=https://eyq-incubator.europe.fabric.ey.com/eyq/eu/api
OPENAI_API_VERSION=2024-02-15-preview
OPENAI_CHAT_MODEL=gpt-4o

ES_HOST=http://localhost:9200
ES_INDEX=tunisian_legal
```

### 3 — Python venv (embed server)

```bash
python -m venv venv
venv\Scripts\activate
pip install flask neo4j python-dotenv certifi
pip install sentence-transformers==2.7.0
pip install huggingface_hub==0.21.4
```

### 4 — Build

```bash
dotnet restore FiscalPlatform.sln
dotnet build FiscalPlatform.sln
```

---

## Running the Platform

**Terminal 1 — Elasticsearch** (start from your ES installation)

**Terminal 2 — Embed server**
```bash
venv\Scripts\activate
python embed_server.py
```
Wait for: `Model loaded — dim=768`

**Terminal 3 — .NET API**
```bash
dotnet run --project FiscalPlatform.API\FiscalPlatform.API.csproj
```
Wait for: `Now listening on: http://localhost:8080`

Open: **http://localhost:8080**

---

## Tech Stack

| Component | Technology |
|---|---|
| Backend | C# .NET 8 |
| CQRS | MediatR 12.2 |
| Validation | FluentValidation 11.9 |
| True Agent | Microsoft Semantic Kernel 1.45 (ChatCompletionAgent) |
| Graph DB | Neo4j.Driver 5.18 |
| Word generation | DocumentFormat.OpenXml 3.0 |
| Search | Elasticsearch 9 |
| Env loading | DotNetEnv 3.0 |
| API docs | Swashbuckle (Swagger) |
| Embeddings | sentence-transformers paraphrase-multilingual-mpnet-base-v2 (768-dim) |
| LLM | GPT-4o via EY Azure OpenAI |

---

## Knowledge Base

| Metric | Value |
|---|---|
| Total chunks | 67,392 nodes |
| Relationships | 291,715 |
| Embedding dimensions | 768 |
| Similarity threshold | 0.30 |
| ES legal documents | 13,590 |

Document types: Lois de Finances, CIRPPIS, CTVA, CDPF, Conventions de non-double imposition, Notes Communes, Commentaires Faiez Choyakh, Décrets & Arrêtés.

---

## EY Tunisia — Tax Technology · 2026
