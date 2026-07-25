# qEntropy Chunker — admin integration

The **Base de connaissances** admin page (`/admin/knowledge`) is the document-chunking module.
An administrator uploads a legal document, the **qEntropy** pipeline runs its 8 stages
(profiler → multi-strategy chunkers → Tsallis *q*-entropy boundary refinement → merge → graph
→ embeddings → GA tuning → Table-I scoring), and the resulting chunks come back **in the
Taxmind UI** for review before anything is published into the graph.

The chunker itself is vendored under [`chunker/`](chunker/) and runs as a **sidecar service**.

---

## Architecture

The admin page is a **native Taxmind page** — not an embedded copy of the chunker's own UI.
It calls our API, which proxies to the chunking service:

```
Taxmind frontend (React, :3000)
  └─ /admin/knowledge          ← native EY-styled page
        │  api/chunker/*       (JWT, Admin role)
        ▼
  Taxmind API (.NET, :5131)
  └─ ChunkerController          ← the authorisation boundary
        │  http://127.0.0.1:8000
        ▼
  qEntropy chunker (FastAPI)    ← upload → run → status → results
        │  optional
        ▼
  EY Azure OpenAI  — or local sentence-transformers when no key
```

**Why proxy instead of calling the chunker from the browser:**

* **Authorisation.** Chunking ingests documents into the legal corpus, so it is admin-only.
  The proxy applies `[Authorize(Roles = "Admin")]`; the chunker has no concept of users, so a
  direct browser call would bypass identity entirely.
* **Exposure.** The chunking service never needs to be reachable from the browser — only the
  API talks to it, over loopback.
* **One origin.** No second CORS surface to configure per environment.

The chunker's own React UI (`chunker/frontend`) is **not needed for the admin flow**. It
remains available for research work — strategy comparison, GA fitness curves, Table-I metric
tables — by running it standalone.

---

## Running the chunker

Two processes, both from `chunker/`. First-time setup needs a Python venv and Node modules.

### Backend (FastAPI, port 8000)

```powershell
cd chunker\backend
py -3.11 -m venv .venv            # Python 3.10/3.11 — NOT 3.14 (no torch wheels)
.\.venv\Scripts\Activate.ps1
pip install -r ..\requirements.txt
uvicorn main:app --port 8000
```

Heavy deps (`torch`, `sentence-transformers`, `pdfplumber`, `pymupdf`, `scikit-learn`). On the
EY/Zscaler network the first run downloads the sentence-transformer model — carry a
pre-seeded `model_cache` (same trick as the platform's embed server) if the download is
blocked, or set `HF_HOME` to a folder you copied over.

**That is all the admin page needs** — the FastAPI service on :8000. The API reaches it at
`Chunker:BaseUrl` (default `http://127.0.0.1:8000`; override in appsettings or with the
`Chunker__BaseUrl` environment variable). The Taxmind frontend needs no chunker setting at
all: it calls its own API.

### Optional — the chunker's own research UI (Vite, port 5173)

Only for research work (strategy comparison, GA fitness curves, Table-I metric tables). The
admin flow does **not** use it:

```powershell
cd chunker\frontend
npm install
npm run dev          # http://localhost:5173
```

---

## EY Azure OpenAI — already supported in code

The chunker's `chunker/backend/engine/openai_client.py` supports **both** a personal OpenAI
key and the EY Azure endpoint, selected the same way as the platform's `LlmAgent`: if
`OPENAI_ENDPOINT` is set → Azure mode (deployment name in the URL, `api-key` header,
`api-version`); otherwise standard `api.openai.com`. It also builds the **Zscaler CA bundle**
so HTTPS verifies behind EY's proxy.

OpenAI is **optional** — with no key the chunker falls back to local
`sentence-transformers` / TF-IDF embeddings and skips the LLM QA/answerability judge.

Create `chunker/.env` (git-ignored) from `chunker/.env.example`:

```env
# ── EY work PC ──
OPENAI_API_KEY=<the EY Azure key>
OPENAI_ENDPOINT=https://eyq-incubator.europe.fabric.ey.com/eyq/eu/api
OPENAI_API_VERSION=2024-02-15-preview
OPENAI_CHAT_MODEL=gpt-4o                     # Azure DEPLOYMENT name (QA + judge)
# OPENAI_EMBED_MODEL=text-embedding-3-large  # only if EY has an embedding deployment;
#                                            # omit to use the local embedding backend
# ZSCALER_CERT=C:\path\to\ZscalerRootCertificate-2048-SHA256.pem   # if HTTPS fails to verify
```

> **Deployment names, not model ids.** In Azure mode the API `model` argument is the
> *deployment* name. `gpt-4o` is confirmed to exist on the EY endpoint (the platform uses it).
> Whether EY exposes a `text-embedding-*` deployment is unconfirmed — **leave
> `OPENAI_EMBED_MODEL` unset** to use the local sentence-transformer backend until you know the
> deployment exists, otherwise embedding calls 404.

### Home PC (personal OpenAI)

```env
OPENAI_API_KEY=sk-...
# leave OPENAI_ENDPOINT and the Azure vars blank
```

---

## Offline models (EY / Zscaler network) — required on the work PC

huggingface.co is blocked on the EY network, so `sentence-transformers` cannot download
`paraphrase-multilingual-MiniLM-L12-v2` at run time. Both local backends then fail and the
service degrades to TF-IDF — lower quality, and its width varies per batch, which used to
crash S3 with an `inhomogeneous shape` error.

**You already have the model.** The platform's embed server ships the *same* model in
`model_cache/`. Point the chunker at it — no download, no extra copy:

```powershell
# PowerShell, before starting uvicorn
$env:CHUNKER_MODEL_CACHE = "C:\dev\profiscal-taxmind\model_cache"
uvicorn main:app --port 8000
```

`CHUNKER_MODEL_CACHE`, `HF_HUB_CACHE` and `SENTENCE_TRANSFORMERS_HOME` are all honoured
(first one wins). To make it permanent, add it to `chunker/.env`.

Confirm it worked: the log should NOT print `backend 'multilingual' failed`.

---

## Verify

1. `curl http://localhost:8000/health` → healthy.
2. `curl http://localhost:8000/backends` → lists the embedding backends (shows `openai` when a
   key is set, always lists the local ones).
3. Open the Taxmind admin → **Base de connaissances**: upload one of
   `chunker/test_documents/*`, press **Lancer le découpage**, watch the stage/progress bar,
   and confirm the chunks + summary render for review.
4. LLM path (needs a valid key): the QA/answerability columns populate. With no key they are
   simply absent — not an error.

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| Admin page says "service hors ligne" | The chunker's FastAPI isn't running — start it on :8000 (the page shows the address it tried). |
| Chunking is slow with a bad key | Fixed: an OpenAI 401/403 now disables the OpenAI backend for the run instead of retrying every batch. Clear `OPENAI_API_KEY` from the environment to use local embeddings outright. |
| `pip install` fails on torch | Wrong Python — use 3.10/3.11, not 3.14. |
| `couldn't connect to huggingface.co` + pipeline crash | huggingface.co is blocked on the EY network. Point the chunker at the model cache the platform already ships (same model): set `CHUNKER_MODEL_CACHE` — see **Offline models** below. |
| Embedding calls 404 in Azure mode | EY has no `text-embedding-*` deployment — unset `OPENAI_EMBED_MODEL` to use the local backend. |
| HTTPS cert errors to the EY endpoint | Set `ZSCALER_CERT` (or `OPENAI_CA_BUNDLE`) in `chunker/.env`. |
| 401 from the EY endpoint | Invalid/rotated key — the platform and the chunker share the same key situation. |

See also [`chunker/README.md`](chunker/README.md) and
[`chunker/DOCUMENTATION_INDEX.md`](chunker/DOCUMENTATION_INDEX.md) for the pipeline internals.
