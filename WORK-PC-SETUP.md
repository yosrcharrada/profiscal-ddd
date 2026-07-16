# Running taxmind on the WORK PC (EY / Zscaler / EY Azure OpenAI)

Goal: the taxmind build running end-to-end on the work PC.

**Four processes at run time:**

| Process | Port | Required for |
|---|---|---|
| Neo4j (`taxmindvf`) | 7687 | consultations — **required** |
| `embed_server.py` | 8081 | semantic search inside consultations (degrades gracefully if down) |
| Elasticsearch (`tunisian_legal`) | 9200 | the **legal search page** — **required** for fuzzy search |
| .NET API | 5131 | everything |
| React frontend | 3000 | the UI |

> **Elasticsearch is required now.** An earlier version of this doc said it wasn't —
> that was when search ran on Neo4j BM25. The search page is now served by
> `ElasticsearchSearchAgent` (fuzziness AUTO multi_match, field-collapse to one hit per
> document). `Neo4jSearchAgent` remains in `Program.cs` as a commented-out fallback with
> **no** fuzzy/edit-distance matching — the typo/accent tolerance the tax team relies on
> only exists in the ES path.

---

## Phase 0 — Carry these to the work PC

| # | What | Size | Needed for |
|---|---|---|---|
| 1 | `C:\taxmind_index_build\processed_documents\` | 51 MB | the Elasticsearch index |
| 2 | `C:\taxmind_index_build\elasticsearch_indexer.py` | tiny | ditto (**not** in git) |
| 3 | `model_cache\` (from repo root) | 458 MB | embed server, avoids a Zscaler-blocked HF download |
| 4 | `ZscalerRootCertificate-2048-SHA256.pem` | tiny | pip / SSL behind the EY proxy |
| 5 | The PDFs — `...\OneDrive\Desktop\FiscalPlatform\FiscalPlatform\documents\` | 553 MB, 757 PDFs | the "Voir le PDF" button |

The PDFs are already in OneDrive: sign in on the work PC, then right-click the
`documents` folder → **"Always keep on this device"**, or they stay cloud-only
placeholders the API cannot open.

**No Neo4j dump needed** if the work PC already has the same `taxmindvf` — but verify
`provision_uid` in Phase 2 before trusting that.

---

## Phase 1 — Code

```powershell
cd "C:\Users\TW961FX\OneDrive - EY\Desktop\profiscal-taxmind"
git fetch origin
git checkout taxmind-sommaire-executif      # superset of taxmind-provision-fix
git pull
```

If the work PC has local uncommitted changes, `git stash` them first or the checkout
will refuse. `.env`, `venv/`, `model_cache/`, `node_modules/` are gitignored — recreate
them below. `appsettings.Development.json` **is** tracked, so Neo4j/ES/seed-admin
settings arrive with the pull.

---

## Phase 2 — Neo4j (`taxmindvf`) — CHECK `provision_uid` FIRST

Start the `taxmindvf` DBMS in Neo4j Desktop, then run this in Neo4j Browser
(database `taxmindvf`):

```cypher
MATCH (c:Chunk) RETURN count(c);                                        // expect 45239
MATCH (c:Chunk) WHERE c.provision_uid IS NOT NULL RETURN count(c);      // expect 45239
SHOW INDEXES;                                                           // chunk_embeddings (VECTOR) + chunk_content (FULLTEXT)
```

**If `provision_uid` returns 0**, the work PC's graph predates the in-place enrichment
(commit `a0dcc9e`) and fetch-by-article-number will return polluted blobs — the CTVA
Art.7 collision, where one article number maps to a code article *plus* unrelated
decrees and annexed rate tables, drowning the operative rate. Fix it in place — no
dump, no re-chunking, no re-embedding:

```powershell
# EDIT enrich_provision_uid.py FIRST — URI / AUTH / DB are HARDCODED at lines ~30-32.
# Set AUTH to the work PC's Neo4j password.
python enrich_provision_uid.py --verify     # report only, no writes
python enrich_provision_uid.py              # apply
```

It only SETs `provision_uid` / `provision_head` / `provision_size` — it never touches
content, embeddings or relationships. Rollback: `python enrich_provision_uid.py --rollback`.

---

## Phase 3 — Python venv

Use **Python 3.10 or 3.11** — NOT 3.14 (no prebuilt wheels; pip fails). Check with `py -0p`.

```powershell
py -3.11 -m venv venv
.\venv\Scripts\Activate.ps1
python -m pip install --upgrade pip
pip install -r embed_requirements.txt
# If Zscaler blocks pip:
#   pip install --cert "C:\path\to\ZscalerRootCertificate-2048-SHA256.pem" -r embed_requirements.txt
```

Also needed for the indexer in Phase 4:

```powershell
pip install elasticsearch==8.13.0 python-dotenv
```

Drop the copied `model_cache/` into the repo root so the model loads locally instead of
downloading. Put `ZscalerRootCertificate-2048-SHA256.pem` next to `embed_server.py` —
it builds `combined_bundle.pem` from it on first run.

---

## Phase 4 — Elasticsearch + the search fix

### 4a. Install / start ES 8.13.0 (no Docker on the work PC)

The Windows ZIP bundles its own JDK — `java` on PATH is irrelevant. `C:\es\elasticsearch-8.13.0`
already exists on the work PC.

`config\elasticsearch.yml` — **mind the YAML**: a space after every `:`, and forward
slashes (backslashes are escape chars inside double quotes and will break parsing):

```yaml
discovery.type: single-node
xpack.security.enabled: false
```

```powershell
C:\es\elasticsearch-8.13.0\bin\elasticsearch.bat
```

Leave that terminal open — it *is* the server. Verify in a **second** terminal:
`http://localhost:9200` should return JSON with a `cluster_name`.

### 4b. Reindex — this is the actual fix for "aucun texte trouvé"

The restored `snapshot_1` index is **incompatible** and collides by name (`tunisian_legal`):

- its `document_type` holds `'loi'` / `'note_commune'`, but the UI sends
  `Code` / `Convention` / `LoiFinances` / `Doctrine` / `Commentaire`, which
  `ElasticsearchSearchAgent` passes straight through as a `term` filter → **every filter
  matches nothing**;
- it has no `seq` field, which the backend sorts on to stitch a full document → "click to
  read full text" breaks.

`processed_documents` already carries the correct values (LoiFinances 10298, Code 8817,
Doctrine 4444, Convention 3024, Commentaire 90) and page numbers (98.8% coverage), so
reindexing from it is the whole fix.

```powershell
cd C:\taxmind_index_build          # must contain processed_documents\
python elasticsearch_indexer.py --force
```

**`--force` is mandatory, for two reasons:**
1. it **drops** the old index first — without it the indexer prints *"Index already
   exists - adding new documents only"* and keeps the broken mapping;
2. it **ignores** the copied `_index_progress.json` checkpoint, which would otherwise
   skip files this machine's ES has never seen.

Env knobs if your setup differs: `ES_HOST` (default `http://localhost:9200`),
`ES_INDEX` (default `tunisian_legal`), `PROCESSED_DIR` (default `./processed_documents`).

Do **not** run `tools/backfill_page_numbers.py` here — its output is already baked into
`processed_documents`.

---

## Phase 5 — `.env` (gitignored, create it in the repo root)

```env
# ── Neo4j ──
NEO4J_URI=neo4j://127.0.0.1:7687
NEO4J_USERNAME=neo4j
NEO4J_PASSWORD=<the work PC's Neo4j password>
NEO4J_DATABASE=taxmindvf

# ── Embedding model (MUST match what built the graph) ──
EMBED_MODEL=paraphrase-multilingual-MiniLM-L12-v2
EMBED_DIM=384

# ── LLM = EY Azure OpenAI (setting Endpoint is what switches to Azure mode) ──
OpenAI__ChatModel=gpt-4o
OpenAI__ApiKey=<EY AZURE KEY>
OpenAI__Endpoint=https://eyq-incubator.europe.fabric.ey.com/eyq/eu/api
OpenAI__ApiVersion=2024-02-15-preview

# ── PDFs for "Voir le PDF" (appsettings ships this EMPTY — set it or the button 404s) ──
Documents__Root=C:\Users\TW961FX\OneDrive - EY\Desktop\...\FiscalPlatform\documents

# ── Only if ES is not on the default host/index ──
# Elasticsearch__Host=http://localhost:9200
# Elasticsearch__Index=tunisian_legal
```

The .NET app binds these from config, so the **double-underscore** form is required.

---

## Phase 6 — Run (4 terminals, in order)

```powershell
# 1 — Neo4j: started in Phase 2, leave running.
# 2 — Elasticsearch: started in Phase 4a, leave running.

# 3 — embed server (venv active)
$env:PYTHONIOENCODING="utf-8"       # avoids the emoji/cp1252 crash on redirected output
python embed_server.py
#   wait for "Model loaded ... dim=384" and "Neo4j connected"

# 4 — .NET API  (.NET 8 SDK)
dotnet run --project backend/Profiscal.API/Profiscal.API.csproj
#   http://localhost:5131 — SQLite profiscal.db is created, migrated and seeded automatically

# 5 — frontend
cd frontend
npm install
npm start                            # http://localhost:3000 → defaults to localhost:5131/api
```

Seed admin (from the tracked `appsettings.Development.json`):
**`yosr.charrada@esprit.tn` / `Admin#Taxmind2026`**

---

## Phase 7 — Verify

**Search page** — the thing you were fixing:
- pick a filter other than "tous" (e.g. `Code`) → results appear (was "aucun texte trouvé");
- a typo (e.g. `retenu a la surce`) still returns hits → fuzzy path is live;
- click a result → full document text opens (proves `seq` landed);
- **Voir le PDF** → correct PDF at the correct page (proves `page_number` + `Documents__Root`).

**Consultation** — run the Hong Kong case (you know its correct answer):
- the **Sommaire exécutif** appears in the .docx as its own section **before** "Analyses",
  and reads as verdicts, not a retelling of the question;
- there is **no** "Tableau de synthèse" — the sommaire replaced it;
- `NC_2015_03` appears in the sources for the assiette point;
- ES framed as "superfétatoire", not "en l'absence d'ES".

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| `Failed to load settings from [elasticsearch.yml]` | YAML: needs a space after `:`, and forward slashes (`C:/...`) — `\U` is a unicode escape inside double quotes. |
| Search filter returns "aucun texte trouvé" | Old index still present — rerun the indexer **with `--force`**. |
| "Voir le PDF" 404s | `Documents__Root` unset, or OneDrive files are cloud-only placeholders. |
| `pip install` fails on tokenizers/torch wheels | Wrong Python — use 3.10/3.11, not 3.14. |
| pip SSL / HuggingFace download blocked | `pip --cert <Zscaler pem>`, or copy `model_cache/` over. |
| embed server crashes on a `⚠️` print (cp1252) | `$env:PYTHONIOENCODING="utf-8"` before running. |
| "API key missing → LLM Phase 1 null" | `.env` must use the double-underscore `OpenAI__ApiKey` form. |
| Semantic search returns 0 hits | Wrong `EMBED_MODEL`, or `taxmindvf` not started. |
| Fetch-by-article returns polluted blobs | `provision_uid` missing — run `enrich_provision_uid.py` (Phase 2). |
| Build error MSB3021 (file locked) | API still running — stop it, then rebuild. |
| OOM / "Thread failed to start" on build | `dotnet build ... -m:1`. |
