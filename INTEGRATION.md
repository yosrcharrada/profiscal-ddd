# Fiscal engine integration — go-live guide

The colleague's `profiscal-ddd` project (semantic search, legal chatbot, AI consultations
with editable sections) is now integrated **into this platform**, behind the existing
Profiscal login, on a single backend (port 5131) and the existing React app (port 3000).

What changed vs. the original:
- **No Elasticsearch.** Consultations/history/ratings persist in our SQLite DB; the search
  engine runs over Neo4j directly.
- **LLM is provider-flexible.** Works with standard **OpenAI** or **EY Azure OpenAI**
  (auto-detected from the endpoint).
- **Everything is behind our JWT auth** — every `/api/fiscal/*` endpoint requires login.

## Architecture

```
React (3000)  ──JWT──▶  Profiscal.API (5131)
                          ├─ /api/auth, /api/users      (existing auth + admin)
                          └─ /api/fiscal/*              (NEW: search, chat, consultations, refine)
                                 │
                                 ├─▶ Neo4j  (tunisian-fiscal, 67k chunks)   ← knowledge base
                                 ├─▶ embed_server.py (:8081)                ← query embeddings
                                 └─▶ OpenAI / Azure GPT-4o                   ← generation
```

Frontend pages (all under the authenticated app shell):
- `/app/search` — semantic legal search
- `/app/chat` — legal chatbot (cites its sources)
- `/app/consultations` — list + create
- `/app/consultations/:id` — **editor with prompt-refinable sections**

## To go live — fill 2 credentials

Edit `backend/Profiscal.API/appsettings.Development.json`:

```jsonc
"Neo4j":  { "Password": "YOUR_NEO4J_PASSWORD" },   // local "profiscal" DBMS
"OpenAI": { "ApiKey":   "sk-..." }                 // OR set Endpoint+ApiKey for EY Azure
```

Mirror the Neo4j password into `.env` (copy from `.env.example`) for the embed server.

## Run (4 terminals)

```bash
# 1. Neo4j — start the "profiscal" DBMS in Neo4j Desktop (already has the data)

# 2. Embed server (semantic search). First run downloads a ~1GB model.
./start-embed-server.sh                # → http://127.0.0.1:8081

# 3. Backend
cd backend/Profiscal.API && dotnet run  # → http://localhost:5131

# 4. Frontend
cd frontend && npm start                # → http://localhost:3000
```

Then sign in and open **Search / Chatbot / Consultations** from the nav.

## Graceful degradation
- **No Neo4j password** → search/chat/stats return empty; the Search page shows
  "Knowledge base offline". No crashes.
- **No embed server** → search/retrieval fall back to Neo4j keyword matching (no vectors).
- **No LLM key** → chat and consultation generation return a clear error; the search engine
  still works (it needs no LLM).

## Default admin
`admin@taxmind.local` / `Admin#Taxmind2026` (change in appsettings before sharing).
