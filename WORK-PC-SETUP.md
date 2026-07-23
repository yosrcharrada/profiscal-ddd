# ProFiscal-Taxmind — full setup on a BLANK work PC

From nothing to a running app. Four processes at run time:

| Process | Port | Required for | Version |
|---|---|---|---|
| **Neo4j** (`taxmindvf`) | 7687 | consultations — **required** | 2026.05.x (Neo4j Desktop 2) |
| **Elasticsearch** (`tunisian_legal`) | 9200 | the search page — **required** | 8.13.0 |
| **embed_server.py** | 8081 | semantic search inside consultations (degrades gracefully if down) | Python 3.10/3.11 |
| **.NET API** | 5131 | everything | .NET **8** SDK |
| React frontend | 3000 | the UI | Node 18+ |

> Consultations run fine with the embed server DOWN (BM25 in Neo4j is the fallback). Elasticsearch
> is **required** for the search page — the search agent has no fuzzy matching without it.

---

## PHASE 0 — Install the runtimes

1. **.NET 8 SDK** — https://dotnet.microsoft.com/download/dotnet/8.0 → `dotnet --version` shows 8.x.
2. **Node 18+** — https://nodejs.org → `node -v`.
3. **Python 3.10 or 3.11** — https://python.org — **NOT 3.14** (no prebuilt wheels; pip fails). `py -0p` lists what you have.
4. **Neo4j Desktop 2** — https://neo4j.com/download/ (bundles its own Java).
5. **Elasticsearch 8.13.0**, Windows ZIP — https://www.elastic.co/downloads/past-releases/elasticsearch-8-13-0 (bundles its own Java; **no Docker needed**). Extract to `C:\es\elasticsearch-8.13.0`.

---

## PHASE 1 — Carry these from a working machine

None of it is in git — the repo has code, not data.

| # | What | From (source machine) | Size |
|---|---|---|---|
| 1 | **Neo4j graph dump** `taxmindvf.dump` | created in Phase 3 | ~250 MB |
| 2 | **ES corpus** `processed_documents\` + `elasticsearch_indexer.py` | `C:\taxmind_index_build\` | ~51 MB |
| 3 | **Embedding model** `model_cache\` | repo root | ~458 MB |
| 4 | **Zscaler root cert** `ZscalerRootCertificate-2048-SHA256.pem` | your EY IT / browser cert export | tiny |
| 5 | **PDF corpus** `documents\` (only for the "Voir le PDF" button) | `…\OneDrive\Desktop\FiscalPlatform\FiscalPlatform\documents` | ~553 MB |

USB or OneDrive. The PDFs may already sync via OneDrive — if so, right-click the folder →
**"Always keep on this device"** (otherwise they are cloud placeholders the API cannot read).

---

## PHASE 2 — Get the code

```powershell
cd "C:\Users\<you>\OneDrive - EY\Desktop"
git clone https://github.com/yosrcharrada/profiscal-ddd.git profiscal-taxmind
cd profiscal-taxmind
git checkout taxmind-frontend-integration
```

`.env`, `venv\`, `model_cache\`, `node_modules\` are gitignored — recreated below.
Drop the copied `model_cache\` into the repo root now.

---

## PHASE 3 — Neo4j (the graph)

### 3a. Create the dump on the SOURCE machine (once)

Neo4j Desktop → the `lastdb` DBMS holding `taxmindvf` → **⋯** menu → **Dump**. Find the `.dump`
under the DBMS's `data\dumps\` and copy it over. (CLI alt., DBMS stopped:
`neo4j-admin database dump taxmindvf --to-path=C:\dumps`.)

### 3b. Load it on the WORK PC

1. Neo4j Desktop → create a local DBMS (set a password you'll remember) — use **2026.05.x** to match the dump.
2. Its **⋯** menu → **Load dump** (or "Create database from dump") → pick `taxmindvf.dump` → target database name **`taxmindvf`**.
3. **Start** the DBMS.

### 3c. Verify — before trusting anything

In Neo4j Browser, database **taxmindvf**:

```cypher
MATCH (n) RETURN count(n);                                           // expect 51 668
MATCH (c:Chunk) RETURN count(c);                                     // expect 45 239
MATCH (c:Chunk) WHERE c.provision_uid IS NOT NULL RETURN count(c);   // expect 45 239  ← critical
SHOW INDEXES;                                                        // chunk_embeddings (VECTOR) + chunk_content (FULLTEXT)
```

**If `provision_uid` is 0**, the dump predates the in-place enrichment and fetch-by-article
returns polluted blobs. Fix in place (edit URI/AUTH/DB at the top of the script first):
`python enrich_provision_uid.py` — additive, reversible (`--rollback`).

---

## PHASE 4 — Python venv + embed server

```powershell
cd "C:\Users\<you>\OneDrive - EY\Desktop\profiscal-taxmind"
py -3.11 -m venv venv
.\venv\Scripts\Activate.ps1
python -m pip install --upgrade pip
pip install -r embed_requirements.txt
# If Zscaler blocks pip:
#   pip install --cert "C:\path\to\ZscalerRootCertificate-2048-SHA256.pem" -r embed_requirements.txt
```

Put `ZscalerRootCertificate-2048-SHA256.pem` next to `embed_server.py` — it builds
`combined_bundle.pem` from it on first run so the model loads over EY's proxy. Better still,
the copied `model_cache\` means no download at all.

---

## PHASE 5 — Elasticsearch + the search index

### 5a. Configure & start ES

Edit `C:\es\elasticsearch-8.13.0\config\elasticsearch.yml` — **mind the YAML** (a space after every
`:`, forward slashes only — a backslash inside double quotes is an escape and breaks parsing):

```yaml
discovery.type: single-node
xpack.security.enabled: false
```

(Optional, low-RAM machines — create `config\jvm.options.d\heap.options`:)
```
-Xms1g
-Xmx1g
```

```powershell
C:\es\elasticsearch-8.13.0\bin\elasticsearch.bat
```
Leave that window open — it IS the server. Verify in a **second** PowerShell:
```powershell
Invoke-RestMethod -Uri "http://localhost:9200"      # want a cluster_name
```

### 5b. Index the corpus

Copy `C:\taxmind_index_build\processed_documents\` and `elasticsearch_indexer.py` to the SAME
path on the work PC, then:

```powershell
pip install elasticsearch==8.13.0 python-dotenv
cd C:\taxmind_index_build
python elasticsearch_indexer.py --force
```

**`--force` is mandatory:** it drops any old/incompatible index first, and it ignores the copied
`_index_progress.json` checkpoint (which otherwise makes the indexer skip every file on a fresh ES).

### 5c. Verify the index

```powershell
Invoke-RestMethod -Uri "http://localhost:9200/tunisian_legal/_count"     # ~26 685
```
Doc types must be `Code / Convention / LoiFinances / Doctrine / Commentaire`:
```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:9200/tunisian_legal/_search" -ContentType "application/json" -Body '{"size":0,"aggs":{"t":{"terms":{"field":"document_type"}}}}' | ForEach-Object { $_.aggregations.t.buckets }
```
If you see `loi` / `note_commune`, the old index survived — re-run 5b with `--force`.

Do **not** run `tools/backfill_page_numbers.py` — its output is already baked into `processed_documents`.

---

## PHASE 6 — `.env` (repo root, gitignored — never commit)

```env
# ── Neo4j ──
NEO4J_URI=neo4j://127.0.0.1:7687
NEO4J_USERNAME=neo4j
NEO4J_PASSWORD=<the password you set in Phase 3b>
NEO4J_DATABASE=taxmindvf

# ── Embedding model (MUST match what built the graph) ──
EMBED_MODEL=paraphrase-multilingual-MiniLM-L12-v2
EMBED_DIM=384

# ── LLM = EY Azure OpenAI (setting Endpoint is what switches to Azure mode) ──
OpenAI__ChatModel=gpt-4o
OpenAI__ApiKey=<EY AZURE KEY>
OpenAI__Endpoint=https://eyq-incubator.europe.fabric.ey.com/eyq/eu/api
OpenAI__ApiVersion=2024-02-15-preview

# ── PDFs for "Voir le PDF" (ships empty; the button 404s without it) ──
Documents__Root=C:\taxmind_corpus\documents

# ── Optional: real credential emails on user creation ──
# Smtp__Host=smtp.gmail.com
# Smtp__Port=587
# Smtp__EnableSsl=true
# Smtp__User=<your account>
# Smtp__Password=<Gmail App Password>
# Smtp__From=<same as User>
# Smtp__CopyCredentialsTo=<admin address to BCC>
```

The .NET app binds these from config, so the **double-underscore** form is required.
`appsettings.Development.json` is tracked and already sets `taxmindvf`, `tunisian_legal`, the seed
admin, and a dev JWT key — so `.env` only needs the secrets above.

---

## PHASE 7 — Run (4 terminals, in order)

```powershell
# 1  Neo4j — started in Phase 3b (Desktop). Leave running.
# 2  Elasticsearch — started in Phase 5a. Leave running.

# 3  Embed server  (venv active)
$env:PYTHONIOENCODING="utf-8"        # avoids the emoji/cp1252 crash on redirected output
python embed_server.py               # wait for "Model loaded … dim=384" + "Neo4j connected"

# 4  Backend API
dotnet run --project backend/Profiscal.API/Profiscal.API.csproj
#   → http://localhost:5131  (SQLite profiscal.db is created, migrated and seeded automatically)

# 5  Frontend
cd frontend
npm install
npm start                            # → http://localhost:3000 (defaults to localhost:5131/api)
```

Seed admin (from the tracked `appsettings.Development.json`):
**`yosr.charrada@esprit.tn` / `Admin#Taxmind2026`**

Health:
```powershell
Invoke-RestMethod -Uri "http://localhost:5131/api/fiscal/search/health"    # alive:true, count:~26685
```

---

## PHASE 8 — Smoke test

- **Consultation** — run the Italy/Interven case; the .docx should have a **Sommaire exécutif**
  section (with verdicts) **before Analyses**, and no "Tableau de synthèse".
- **Search** — pick a filter other than "tous" → results appear; a typo (`convension fiscal`) still
  matches; click a result → full text; **Voir le PDF** → the right PDF at the right page.

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| `Failed to load settings from [elasticsearch.yml]` | YAML: space after `:`, forward slashes (`C:/…`). |
| ES exits: `mmap … 16 GB` / `failed with [1]` | heap too big — set `-Xms1g -Xmx1g` (Phase 5a) or `ES_JAVA_OPTS`. |
| Search filter returns "aucun texte trouvé" | old index present — re-run the indexer **with `--force`**. |
| "Voir le PDF" 404s | `Documents__Root` unset, or OneDrive files are cloud-only placeholders. |
| `pip install` fails on tokenizers/torch | wrong Python — 3.10/3.11, not 3.14. |
| pip SSL / HuggingFace download blocked | `pip --cert <Zscaler pem>`, or copy `model_cache\` over. |
| embed server crashes on a `⚠️` print | `$env:PYTHONIOENCODING="utf-8"` before running. |
| "API key missing → LLM Phase 1 null" | `.env` must use the double-underscore `OpenAI__ApiKey` form. |
| Fetch-by-article returns polluted blobs | `provision_uid` missing — run `enrich_provision_uid.py` (Phase 3c). |
| Build error MSB3021 (file locked) | API still running — stop it, then rebuild. |
| Dashboard says "Neo4j" disconnected but Neo4j is up | known: that health field actually reports Elasticsearch. Cosmetic. |
