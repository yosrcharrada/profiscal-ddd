# Running the taxmind version on the WORK PC (EY / Zscaler / EY Azure OpenAI)

Goal: a brand-new folder on the work PC running the taxmind build end-to-end.
No Elasticsearch needed (BM25 is now native in Neo4j). Three processes at run time:
**Neo4j (taxmind) → embed_server.py → .NET API**, plus the React frontend.

---

## Phase 0 — Carry these 3 things to the work PC (USB / OneDrive)

1. **The graph dump** — `neo4j-2026-06-25T15-47-35.dump` (the taxmind dump).
2. **The embedding model folder** — to avoid Zscaler blocking the HuggingFace download,
   copy the model that the home-PC embed server downloads into `model_cache/`.
   *(If you haven't run it on the home PC yet: run the embed server there once so it
   downloads `paraphrase-multilingual-MiniLM-L12-v2` into `ProFiscal-Taxmind/model_cache/`,
   then copy that whole `model_cache` folder over.)*
3. **Your Zscaler root cert** — `ZscalerRootCertificate-2048-SHA256.pem` (only needed if you
   choose to let pip / the model download over the network instead of copying).

---

## Phase 1 — Get the code into a new folder

```powershell
cd "C:\Users\TW961FX\OneDrive - EY\Desktop"
git clone -b taxmind-graph https://github.com/yosrcharrada/profiscal-ddd.git profiscal-taxmind
cd profiscal-taxmind
```

`.env`, `venv/`, `model_cache/`, `node_modules/` are NOT in git — we recreate them below.
Drop the copied `model_cache/` folder (Phase 0.2) into the project root now.

---

## Phase 2 — Load the taxmind graph into Neo4j

**Easiest (Neo4j Desktop):**
1. Open Neo4j Desktop → create/choose a DBMS (set a password you'll remember).
2. Use **"Import"/"Create from dump"** (or the DBMS's ⋯ menu → *Import dump*) and pick
   `neo4j-2026-06-25T15-47-35.dump`. Import it as database name **`taxmind`**.
3. **Start** the DBMS. Note its **Bolt URI** (e.g. `neo4j://127.0.0.1:7687`).

**Or CLI** (DBMS stopped):
```powershell
# from the Neo4j install's bin folder; --database name must be 'taxmind'
neo4j-admin database load taxmind --from-path="C:\path\to\dump-folder" --overwrite-destination=true
```
The dump already contains the `chunk_embeddings` (384-dim vector) and `chunk_content`
(BM25 full-text) indexes — nothing to rebuild.

**Verify** in Neo4j Browser (select database `taxmind`):
```cypher
MATCH (c:Chunk) RETURN count(c);          // expect 35429
SHOW INDEXES;                              // expect chunk_embeddings (VECTOR) + chunk_content (FULLTEXT)
```

---

## Phase 3 — Python venv + requirements (the part that bit us before)

Use **Python 3.10 or 3.11** — NOT 3.14 (no prebuilt wheels → pip fails on the work PC).
Check what you have: `py -0p` (lists installed Pythons).

```powershell
cd "C:\Users\TW961FX\OneDrive - EY\Desktop\profiscal-taxmind"
py -3.11 -m venv venv          # or -3.10
.\venv\Scripts\Activate.ps1
python -m pip install --upgrade pip
```

Install deps. If pip is blocked by Zscaler SSL, add your cert as a trusted bundle:
```powershell
# Option A (network works): normal install
pip install -r embed_requirements.txt

# Option B (Zscaler SSL errors): point pip at the Zscaler cert
pip install --cert "C:\path\to\ZscalerRootCertificate-2048-SHA256.pem" -r embed_requirements.txt
# (or, last resort) pip install --trusted-host pypi.org --trusted-host files.pythonhosted.org -r embed_requirements.txt
```

> Tip: if you'd rather not rebuild a venv, you can reuse the OLD project's working venv —
> just run THIS folder's `embed_server.py` with that venv's python. It has the same deps.

---

## Phase 4 — The embedding model (384-dim)

The embed server needs the **exact model that built the taxmind embeddings**.
- **Preferred:** you copied `model_cache/` in Phase 0.2 → it loads locally, no download. Done.
- **Otherwise**, on first run it tries to download `paraphrase-multilingual-MiniLM-L12-v2`.
  Place `ZscalerRootCertificate-2048-SHA256.pem` next to `embed_server.py` so the SSL bundle
  is built automatically (the script handles this).

⚠️ If semantic search returns 0 hits / tiny scores, the model is wrong — set `EMBED_MODEL`
in `.env` to the real one and restart. BM25 legal search works regardless of the model.

---

## Phase 5 — Create `.env` (EY Azure OpenAI on the work PC)

Create `.env` in the project root (it's gitignored — never commit it):

```env
# ── Neo4j — taxmind ──
NEO4J_URI=neo4j://127.0.0.1:7687
NEO4J_USERNAME=neo4j
NEO4J_PASSWORD=<the password you set in Phase 2>
NEO4J_DATABASE=taxmind

# ── Embedding model ──
EMBED_MODEL=paraphrase-multilingual-MiniLM-L12-v2
EMBED_DIM=384

# ── LLM = EY Azure OpenAI (work PC) ──
OpenAI__ChatModel=gpt-4o
OpenAI__ApiKey=<EY AZURE KEY>
OpenAI__Endpoint=https://eyq-incubator.europe.fabric.ey.com/eyq/eu/api
OpenAI__ApiVersion=2024-02-15-preview
```

(Setting `OpenAI__Endpoint` is what switches the backend into EY-Azure mode.)

---

## Phase 6 — Run it (3 terminals, in order)

```powershell
# Terminal 1 — Neo4j: already started in Phase 2 (Desktop) — leave it running.

# Terminal 2 — embed server (venv active)
$env:PYTHONIOENCODING="utf-8"        # avoids the emoji/cp1252 crash
python embed_server.py
#   wait for "Model loaded ... dim=384" and "Neo4j connected"

# Terminal 3 — .NET API
dotnet restore backend/Profiscal.API/Profiscal.API.csproj
dotnet run --project backend/Profiscal.API/Profiscal.API.csproj
#   Swagger at the URL it prints (e.g. http://localhost:5131/swagger)
```

**Smoke tests:**
```powershell
# embed server returns hits
curl -s -X POST http://127.0.0.1:8081/embed_search -H "Content-Type: application/json" -d "{\"query\":\"retenue a la source honoraires\",\"top_k\":5}"

# health
curl http://127.0.0.1:8081/health
```

---

## Phase 7 — Frontend (optional, for the UI)

```powershell
cd frontend
npm install
npm start            # http://localhost:3000
```
If the API isn't on the default port, set `REACT_APP_API_URL` to match the .NET URL.

---

## Troubleshooting (known gotchas)

| Symptom | Fix |
|---|---|
| `pip install` fails on tokenizers/torch wheels | Wrong Python — use 3.10/3.11, not 3.14. |
| pip SSL / HuggingFace download blocked | Use `--cert` with the Zscaler pem, or copy `model_cache/` over. |
| embed server crashes on a `⚠️` print (cp1252) | `$env:PYTHONIOENCODING="utf-8"` before running. |
| "API key missing → LLM Phase 1 null" | `.env` must use the **double-underscore** `OpenAI__ApiKey` form (single-underscore is a fallback). |
| Build error MSB3021 (file locked) | The API is still running — stop it (Ctrl+C / `taskkill /PID <pid> /F`) then rebuild. |
| Semantic search returns 0 hits | Wrong `EMBED_MODEL` (must match what built the graph) OR `taxmind` DB/index missing. |
| Search tab empty | Confirm `chunk_content` full-text index exists (`SHOW INDEXES`). |
| OOM / "Thread failed to start" on build | Build single-threaded: `dotnet build ... -m:1`. |
