# qEntropy Chunker — admin integration

The **Knowledge base** admin page (`/admin/knowledge`) is the document-chunking module.
It embeds the **qEntropy chunker** — an 8-stage chunking + evaluation platform (profiler →
multi-strategy chunkers → Tsallis *q*-entropy boundary refinement → graph → embeddings →
genetic-algorithm tuning → Table-I scoring). The chunker is vendored in this repo under
[`chunker/`](chunker/) and runs as a **sidecar service**; the admin page shows its own UI in
an `<iframe>`.

---

## Why a sidecar, not a rewrite

The chunker is a self-contained **FastAPI (Python) + React/Vite** application with a real ML
core (Tsallis entropy, LSTM boundary ranking, a genetic-algorithm tuner, sentence-transformer
embeddings). Re-implementing that in the .NET backend, or folding it into `embed_server.py`,
would be wasteful and fragile. Instead we treat it as a black-box service with a clean HTTP
API — the same pattern the platform already uses for the embedding server. The Taxmind admin
simply embeds the chunker's UI; the chunker keeps its full research surface (strategy
comparison, GA tuning, per-strategy metrics).

```
Taxmind frontend (React, :3000)
  └─ /admin/knowledge  →  <iframe src=CHUNKER_URL>
                                │
                                ▼
        qEntropy chunker frontend (Vite, :5173)
                                │  XHR
                                ▼
        qEntropy chunker backend (FastAPI, :8000)
                                │  optional
                                ▼
        EY Azure OpenAI  (embeddings + QA + answerability judge)
        — or local sentence-transformers when no key
```

**Dependency direction stays clean:** the chunker depends on nothing in Taxmind; Taxmind only
points an iframe at a URL. Either can run without the other.

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

### Frontend (Vite, port 5173)

```powershell
cd chunker\frontend
npm install
npm run dev          # serves http://localhost:5173
```

Its UI has a field to set the backend URL (defaults to `http://localhost:8000`).

### The Taxmind admin iframe

`AdminKnowledge.jsx` reads **`REACT_APP_CHUNKER_URL`** at build time, defaulting to
`http://localhost:5173`. To point at a different origin, set it before building the Taxmind
frontend:

```
REACT_APP_CHUNKER_URL=http://localhost:5173
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
3. Open the Taxmind admin → **Knowledge base**: the chunker UI loads in the iframe. Upload one
   of `chunker/test_documents/*`, run it, and confirm chunks + metrics appear.
4. LLM path (needs a valid key): the QA/answerability columns populate. With no key they are
   simply absent — not an error.

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| Admin page iframe blank | The chunker isn't running — start its backend + frontend, confirm `REACT_APP_CHUNKER_URL` matches the Vite origin. |
| `pip install` fails on torch | Wrong Python — use 3.10/3.11, not 3.14. |
| `couldn't connect to huggingface.co` + pipeline crash | huggingface.co is blocked on the EY network. Point the chunker at the model cache the platform already ships (same model): set `CHUNKER_MODEL_CACHE` — see **Offline models** below. |
| Embedding calls 404 in Azure mode | EY has no `text-embedding-*` deployment — unset `OPENAI_EMBED_MODEL` to use the local backend. |
| HTTPS cert errors to the EY endpoint | Set `ZSCALER_CERT` (or `OPENAI_CA_BUNDLE`) in `chunker/.env`. |
| 401 from the EY endpoint | Invalid/rotated key — the platform and the chunker share the same key situation. |

See also [`chunker/README.md`](chunker/README.md) and
[`chunker/DOCUMENTATION_INDEX.md`](chunker/DOCUMENTATION_INDEX.md) for the pipeline internals.
