# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**ProFiscal** is an AI-powered Tunisian fiscal consultation platform for EY Tunisia. It generates professional Word (.docx) consultations in under 60 seconds using GraphRAG, Semantic Kernel agents, Azure OpenAI (GPT-4o), Neo4j, and Elasticsearch.

## Build & Run

```bash
# Restore and build
dotnet restore FiscalPlatform.sln
dotnet build FiscalPlatform.sln

# Run API (http://localhost:8080, Swagger at /swagger)
dotnet run --project FiscalPlatform.API/FiscalPlatform.API.csproj
```

```bash
# Python embed server (must be running before the API)
python -m venv venv
venv\Scripts\activate
pip install flask neo4j python-dotenv certifi sentence-transformers==2.7.0 huggingface_hub==0.21.4
python embed_server.py   # listens on http://127.0.0.1:8081
```

Full stack requires three processes running in order: Elasticsearch → embed_server.py → .NET API.

There are no automated tests in this project.

## Architecture

Four-layer DDD + Clean Architecture + CQRS with strict dependency enforcement:

```
FiscalPlatform.API           → thin HTTP controllers only
FiscalPlatform.Application   → CQRS commands/queries via MediatR, interfaces
FiscalPlatform.Infrastructure → external integrations (Neo4j, ES, Azure OpenAI, SK)
FiscalPlatform.Domain        → aggregates, value objects, events — zero external deps
```

Dependency rule: API → Application → Domain; Infrastructure → Domain + Application. Application never references Infrastructure.

## Core Patterns

### Consultation Generation (the main flow)

`GenerateConsultationCommandHandler` runs 8 steps:
1. Non-LLM detection: branches (`BranchDetector`), countries (`CountryDetector`), keywords (`KeywordExtractor`)
2. Vector search via Python embed server (`EmbedSearchAgent` → POST `http://127.0.0.1:8081/embed_search`)
3. Graph queries via Neo4j (`RetrievalAgent`) + merge results
4. LLM Phase 1 → extract `etendue_items` + sommaire
5. **Parallel** LLM Phase 2 (analyses) + Phase 3 (documents/table) via `Task.WhenAll`
6. Resolve `[S1]..[Sn]` citations
7. Fill Word template via OpenXML (`DocumentGenerationAgent`)
8. Publish domain events → async Elasticsearch persistence

Exactly 3 LLM calls per generation. Target: 45–55 seconds total.

### Refinement (SK ChatCompletionAgent)

`RefineConsultationCommandHandler` uses a true **Semantic Kernel 1.45 `ChatCompletionAgent`** — not simple tool routing. The agent reasons autonomously and invokes `[KernelFunction]` tools registered in `FiscalKernelFactory`:

- **RetrievalPlugin**: `semantic_search`, `search_convention`, `keyword_search`, `full_retrieval`
- **LegalAnalysisPlugin**: `analyze_fiscal_point`, `refine_section`, `generate_sommaire`, `answer_fiscal_question`

### Guardrails

`FiscalGuardrails` runs at two points:
- **Input** — fiscal keyword check, blocks off-topic before any LLM call
- **Output** — validates citations exist, analyses non-empty, minimum 3 citations, no hallucination patterns

### Reward Memory

`RewardMemory` reads Elasticsearch star ratings (1–5) and applies score multipliers to future source retrievals (RLHF loop).

## Configuration

All secrets via `.env` file at project root (never committed). Key variables:

```env
NEO4J_URI=neo4j://127.0.0.1:7687
NEO4J_DATABASE=tunisian-fiscal
NEO4J_USERNAME=neo4j
NEO4J_PASSWORD=...

OPENAI_API_KEY=...
OPENAI_ENDPOINT=https://eyq-incubator.europe.fabric.ey.com/eyq/eu/api
OPENAI_API_VERSION=2024-02-15-preview
OPENAI_CHAT_MODEL=gpt-4o

ES_HOST=http://localhost:9200
ES_INDEX=tunisian_legal
```

`Program.cs` loads `.env` via `DotNetEnv` at startup. Configuration keys mirror `appsettings.json` sections: `Neo4j`, `Elasticsearch`, `OpenAI`.

## Key Files

| File | Role |
|------|------|
| [FiscalPlatform.API/Program.cs](FiscalPlatform.API/Program.cs) | Startup, DI wiring, MediatR pipeline |
| [FiscalPlatform.Infrastructure/DependencyInjection.cs](FiscalPlatform.Infrastructure/DependencyInjection.cs) | Composition root — all registrations |
| [FiscalPlatform.Application/Consultation/Commands/GenerateConsultation/GenerateConsultationCommandHandler.cs](FiscalPlatform.Application/Consultation/Commands/GenerateConsultation/GenerateConsultationCommandHandler.cs) | 8-step orchestrator |
| [FiscalPlatform.Application/Consultation/Commands/RefineConsultation/RefineConsultationCommandHandler.cs](FiscalPlatform.Application/Consultation/Commands/RefineConsultation/RefineConsultationCommandHandler.cs) | SK agent for interactive refinement |
| [FiscalPlatform.Infrastructure/Kernel/FiscalKernelFactory.cs](FiscalPlatform.Infrastructure/Kernel/FiscalKernelFactory.cs) | SK kernel + plugin registration |
| [FiscalPlatform.Infrastructure/Agents/RetrievalAgent.cs](FiscalPlatform.Infrastructure/Agents/RetrievalAgent.cs) | All Neo4j Cypher queries |
| [FiscalPlatform.Infrastructure/Guardrails/FiscalGuardrails.cs](FiscalPlatform.Infrastructure/Guardrails/FiscalGuardrails.cs) | Input/output validation |
| [FiscalPlatform.Infrastructure/Memory/RewardMemory.cs](FiscalPlatform.Infrastructure/Memory/RewardMemory.cs) | RLHF source boosting |
| [embed_server.py](embed_server.py) | Python microservice: multilingual mpnet embeddings (768-dim) |
| [FiscalPlatform.API/template_fr.docx](FiscalPlatform.API/template_fr.docx) | Word template (binary, committed) |

## Knowledge Base

- **Neo4j** `tunisian-fiscal`: 67,392 nodes, 291,715 relationships, vector index `chunk_embeddings` (768-dim cosine, threshold 0.30)
- **Elasticsearch** `tunisian_legal`: 13,590 legal documents (Finance Laws, CIRPPIS, CTVA, CDPF, Double-Tax Treaties, Official Notes, Expert Commentaries, Decrees)

Legal source hierarchy enforced in ranking:
- International: Convention → Code → LoiFinances → Doctrine
- Local: Code → LoiFinances → Doctrine

## Domain Terminology

| Term | Meaning |
|------|---------|
| IS / IRPP / TVA / Retenue / PrixTransfert | Fiscal branches (detected non-LLM by `BranchDetector`) |
| `[S1]`..`[Sn]` | Citation format within consultation text |
| Etendue | Scope of analysis |
| Contexte Faits | Statement of facts (no citations) |
| Sommaire Exécutif | Executive summary with verdicts |
| CIRPPIS / CTVA / CDPF | Tunisian tax codes |

## Common Extension Points

**New fiscal branch:** add to `LegalBranch.cs` → update `BranchDetector.cs` patterns → update Phase 2 prompt in `GenerateConsultationCommandHandler` → add Neo4j queries in `RetrievalAgent` if needed.

**New SK agent tool:** add `[KernelFunction]` + `[Description]` method to `RetrievalPlugin` or `LegalAnalysisPlugin` — auto-registered by `FiscalKernelFactory`. Update `AgentInstructions` in `RefineConsultationCommandHandler`.

**New API endpoint:** controller → request DTO in `Requests/ApiRequests.cs` → Application command/query → `IRequestHandler` implementation → DI registration if infrastructure needed.
