# ProFiscal — Plateforme de Consultation Fiscale Augmentée
### EY Tunisia · Tax Technology · 2026

---

## What is ProFiscal?

ProFiscal is an AI-powered fiscal consultation platform built for EY Tunisia's tax team.
It generates professional Word (.docx) consultations in under 2 minutes by combining:

- **GraphRAG** — 30,000+ legal document chunks indexed in Neo4j
- **Semantic search** — multilingual sentence embeddings (768-dim)
- **GPT-4o** — Azure OpenAI for structured legal analysis
- **Elasticsearch** — BM25 full-text search + consultation history

---

## Architecture

This project follows **DDD + Clean Architecture + CQRS** principles, split into 4 layers:

```
FiscalPlatform.sln
├── FiscalPlatform.Domain/          ← Business rules, no external dependencies
│   ├── Aggregates/Consultation/    ← Consultation aggregate root
│   ├── ValueObjects/               ← LegalBranch, Country
│   ├── Repositories/               ← IConsultationRepository (interface)
│   └── Events/                     ← Domain events
│
├── FiscalPlatform.Application/     ← Use cases (CQRS with MediatR)
│   ├── Consultation/Commands/      ← Generate, Rate, Refine
│   ├── Consultation/Queries/       ← GetHistory
│   ├── Search/Queries/             ← SearchLegalDocuments
│   ├── Chat/Queries/               ← GraphRAG chat
│   └── Common/                     ← Interfaces, Behaviours, DTOs
│
├── FiscalPlatform.Infrastructure/  ← External services
│   ├── Agents/                     ← LlmAgent, RetrievalAgent, EmbedSearchAgent,
│   │                                  DocumentGenerationAgent, FeedbackAgent
│   ├── DomainServices/             ← BranchDetector, CountryDetector, KeywordExtractor
│   ├── Persistence/                ← ElasticsearchConsultationRepository
│   └── Search/                     ← ElasticsearchSearchAgent
│
└── FiscalPlatform.API/             ← HTTP layer (thin controllers only)
    ├── Controllers/                ← 7 controllers, one per bounded context
    ├── Middleware/                 ← Global exception handling
    └── Requests/                  ← API-layer DTOs
```

### Multi-Agent Pipeline

| Agent | Technology | Responsibility |
|---|---|---|
| BranchDetector | Rule-based | Detect IS / IRPP / TVA / Retenue / PrixTransfert |
| CountryDetector | Rule-based | Detect country + international flag |
| KeywordExtractor | Rule-based | Extract keywords and entities |
| EmbedSearchAgent | Sentence Transformers (768-dim) | Semantic vector search |
| RetrievalAgent | Neo4j Cypher | Graph-based legal source retrieval |
| LlmAgent | Azure OpenAI GPT-4o | Consultation generation (3 calls only) |
| DocumentGenerationAgent | OpenXML | Word .docx generation |
| FeedbackAgent | Elasticsearch | Rating storage |

### Workflow Orchestration Patterns

| Feature | Pattern |
|---|---|
| Consultation generation | Parallel Execution (Phase 2 ‖ Phase 3) inside Orchestrator |
| Branch / country detection | Conditional Branching |
| Rating, persistence | Event-Driven Choreography |
| Conversation / refinement | State Machine + Human-in-the-Loop |
| Source retrieval | Hierarchical Delegation |
| JSON parse failure | Reflective Loop (max 2 iterations) |

---

## API Endpoints

| Method | URL | Description |
|---|---|---|
| POST | /api/consultation/generate | Generate a fiscal consultation (.docx) |
| GET | /api/consultation/history?client=X | Get consultation history by client name |
| POST | /api/consultation/session/start | Start a refinement conversation session |
| POST | /api/consultation/session/end | End and archive the session |
| POST | /api/refinement/message | Send a message in the refinement conversation |
| POST | /api/rating | Submit a 1–5 star rating |
| POST | /api/search | Full-text legal document search |
| GET | /api/stats | Knowledge base statistics |
| POST | /api/chat | GraphRAG chat |
| POST | /api/document/extract | Extract text from uploaded file |
| GET | /api/search/health | Elasticsearch health |
| GET | /api/stats/health | Neo4j health |

Swagger UI available at: `http://localhost:8080/swagger`

---

## Prerequisites

| Tool | Version | Purpose |
|---|---|---|
| .NET SDK | 8.0+ | C# backend |
| Python | 3.11+ | Embed server |
| Neo4j | 5.x | Graph knowledge base |
| Elasticsearch | 9.x | Full-text search + consultation history |
| GPT-4o | via EY Azure | LLM generation |

---

## Setup

### 1 — Clone

```bash
git clone https://github.com/YOUR_USERNAME/profiscal-ddd.git
cd profiscal-ddd
```

### 2 — Environment variables

Create a `.env` file at the root (never commit this):

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

### 3 — Python venv (for embed server)

```bash
python -m venv venv
venv\Scripts\activate          # Windows
pip install flask neo4j python-dotenv certifi
pip install sentence-transformers==2.7.0
pip install huggingface_hub==0.21.4
```

### 4 — NuGet restore

```bash
dotnet restore FiscalPlatform.sln
```

### 5 — Build

```bash
dotnet build FiscalPlatform.sln
```

---

## Running the Platform

Start all three services in separate terminals:

**Terminal 1 — Elasticsearch**
```bash
# Navigate to your Elasticsearch installation
.\bin\elasticsearch.bat
```

**Terminal 2 — Embed server**
```bash
cd profiscal-ddd
venv\Scripts\activate
python embed_server.py
```
Waits for: `Model loaded in Xs — dim=768`

**Terminal 3 — .NET API**
```bash
cd profiscal-ddd
dotnet run --project FiscalPlatform.API\FiscalPlatform.API.csproj
```
Waits for: `Now listening on: http://localhost:8080`

Open browser: **http://localhost:8080**

---

## Key Features

### Consultation Generation
- Detects fiscal branches (IS, IRPP, TVA, Retenue, Prix de Transfert)
- Detects countries and applies international hierarchy (Convention first)
- Retrieves 30 sources from the legal knowledge base
- Corrects article_ref metadata errors at runtime
- Generates structured Word .docx with EY branding
- Auto-saves to Elasticsearch for history

### Legal Hierarchy (strictly enforced)
```
International case:  Convention → Codes → Lois de Finances → Doctrine
Local case:          Codes → Lois de Finances → Doctrine
```

### Consultation History
- Every generated consultation is automatically saved to ES
- Search by client name from the Search tab
- Results show sommaire + expandable full analyses

### Conversation / Refinement
- After generation, open a session and chat to refine any section
- Session stored in-memory (volatile) while active
- Archived to Elasticsearch when session ends

### Star Rating
- Rate any consultation 1–5 stars
- Ratings stored in `profiscal_ratings` ES index

---

## Knowledge Base

| Metric | Value |
|---|---|
| Total chunks | 30,125 |
| Entities | 34,771 |
| Relations | 291,715 |
| Embedding model | paraphrase-multilingual-mpnet-base-v2 (768-dim) |
| Similarity threshold | 0.30 |

Document types covered: Lois de Finances, Codes fiscaux (CIRPPIS, CTVA, CDPF), Conventions de non-double imposition, Notes Communes, Commentaires Faiez Choyakh, Décrets & Arrêtés.

---

## Project Structure Notes

- `embed_server.py` — standalone Python microservice on port 8081
- `template_fr.docx` — EY Word template with placeholders
- `model_cache/` — local sentence-transformer model (not committed, ~1GB)
- `.env` — secrets file (never committed)

---

## Built With

- **C# .NET 8** — backend
- **MediatR 12** — CQRS command/query bus
- **FluentValidation** — command validation pipeline
- **Neo4j.Driver 5** — graph database client
- **DocumentFormat.OpenXml 3** — Word document generation
- **DotNetEnv** — `.env` file loading
- **Swashbuckle** — Swagger API documentation
- **Python Flask** — embed microservice
- **sentence-transformers** — multilingual embeddings
- **Elasticsearch 9** — full-text search + persistence

---

## EY Tunisia — Tax Technology Team · 2026
