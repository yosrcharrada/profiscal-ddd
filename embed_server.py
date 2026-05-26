from __future__ import annotations
"""
embed_server.py
===============
Python microservice for semantic vector search.
Called by C# EmbedSearchService.cs on http://127.0.0.1:8081/embed_search

ZSCALER SSL FIX (EY work PC):
  Place these two files in the same folder as this script:
    - ZscalerRootCertificate-2048-SHA256.pem   (Zscaler root cert)
    - combined_bundle.pem                       (created automatically on first run)
  The SSL setup below must run BEFORE any import of sentence_transformers or requests.
"""

# ── ZSCALER SSL SETUP — must run before any SSL-using import ─────────────────
import os, certifi, pathlib

_here = pathlib.Path(__file__).resolve().parent
_zscaler_cert    = _here / "ZscalerRootCertificate-2048-SHA256.pem"
_combined_bundle = _here / "combined_bundle.pem"

if _zscaler_cert.exists():
    if not _combined_bundle.exists():
        # First run: merge default certifi bundle + Zscaler cert
        with open(certifi.where(), "rb") as f:
            _base = f.read()
        with open(_zscaler_cert, "rb") as f:
            _zscaler = f.read()
        with open(_combined_bundle, "wb") as f:
            f.write(_base + b"\n" + _zscaler)
        print(f"✅ Created combined SSL bundle: {_combined_bundle}")
    # Apply to all Python SSL/requests/urllib consumers
    os.environ["SSL_CERT_FILE"]       = str(_combined_bundle)
    os.environ["REQUESTS_CA_BUNDLE"]  = str(_combined_bundle)
    os.environ["CURL_CA_BUNDLE"]      = str(_combined_bundle)
    print(f"✅ Zscaler SSL configured: {_combined_bundle.name}")
else:
    print(f"⚠️  Zscaler cert not found at {_zscaler_cert}")
    print(f"   Sentence-transformers may fail to download on EY network.")
    print(f"   Place ZscalerRootCertificate-2048-SHA256.pem next to embed_server.py")

# ─────────────────────────────────────────────────────────────────────────────



import os
import json
import time
import logging
from pathlib import Path
from typing import List, Dict, Optional

# ── Dependencies ──────────────────────────────────────────────────────────────

try:
    from flask import Flask, request, jsonify
except ImportError:
    print("ERROR: flask not installed. Run: pip install flask"); raise

try:
    from sentence_transformers import SentenceTransformer
except ImportError:
    print("ERROR: sentence_transformers not installed. Run: pip install sentence-transformers"); raise

try:
    from neo4j import GraphDatabase
except ImportError:
    print("ERROR: neo4j not installed. Run: pip install neo4j"); raise

try:
    from dotenv import load_dotenv
    load_dotenv()
except ImportError:
    pass

# ── Config ────────────────────────────────────────────────────────────────────

NEO4J_URI  = os.getenv("NEO4J_URI",      "neo4j://127.0.0.1:7687")
NEO4J_USER = os.getenv("NEO4J_USERNAME", os.getenv("NEO4J_USER", "neo4j"))
NEO4J_PASS = os.getenv("NEO4J_PASSWORD", "neo4j123")
NEO4J_DB   = os.getenv("NEO4J_DATABASE", "tunisian-fiscal")

EMBED_MODEL    = "paraphrase-multilingual-mpnet-base-v2"   # MUST match graph build model
MODEL_CACHE    = "./model_cache"                            # cached model folder
PORT           = 8081
MIN_SCORE      = 0.30       # lowered from 0.45 — legal text vs descriptive query needs lower threshold
DEFAULT_TOP_K  = 20
MAX_TOP_K      = 40

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [embed_server] %(levelname)s: %(message)s",
    datefmt="%H:%M:%S",
)
log = logging.getLogger("embed_server")

# ── Globals (loaded once at startup) ─────────────────────────────────────────

_model:  Optional[SentenceTransformer] = None
_driver = None

# ── Flask app ─────────────────────────────────────────────────────────────────

app = Flask(__name__)


# ── Startup: load model + connect Neo4j ──────────────────────────────────────

def load_model() -> SentenceTransformer:
    global _model
    if _model is not None:
        return _model
    log.info(f"Loading embedding model: {EMBED_MODEL} (this may take ~30s first time)…")
    t0 = time.time()
    # Try local cache first (avoids SSL/httpx issues with Zscaler).
    # local_files_only=True means: never call HuggingFace, use cached model only.
    # If model is not cached, falls back to download with SSL bypass.
    # Try loading from the manually-downloaded local folder first
    # (files downloaded with verify=False directly into model_cache subfolder)
    local_paths = [
        pathlib.Path(MODEL_CACHE) / "sentence-transformers_paraphrase-multilingual-mpnet-base-v2",
        pathlib.Path(MODEL_CACHE) / "paraphrase-multilingual-mpnet-base-v2",
        pathlib.Path("./paraphrase-multilingual-mpnet-base-v2"),
    ]
    local_path = next((p for p in local_paths if (p / "config.json").exists()), None)

    if local_path is not None:
        log.info(f"Loading model from local path: {local_path}")
        # Load directly from local folder path — no network call at all
        _model = SentenceTransformer(str(local_path))
        log.info("✅ Model loaded successfully from local files")
    else:
        # No local files found — try HuggingFace cache
        try:
            _model = SentenceTransformer(EMBED_MODEL, cache_folder=MODEL_CACHE,
                                         local_files_only=True)
            log.info("Model loaded from HuggingFace cache")
        except Exception:
            import ssl
            ssl._create_default_https_context = ssl._create_unverified_context
            log.warning("Downloading model with SSL verification disabled")
            _model = SentenceTransformer(EMBED_MODEL, cache_folder=MODEL_CACHE)
    log.info(f"Model loaded in {time.time()-t0:.1f}s — dim={_model.get_sentence_embedding_dimension()}")
    return _model


def get_driver():
    global _driver
    if _driver is not None:
        return _driver
    log.info(f"Connecting to Neo4j: {NEO4J_URI} / {NEO4J_DB}")
    _driver = GraphDatabase.driver(NEO4J_URI, auth=(NEO4J_USER, NEO4J_PASS))
    _driver.verify_connectivity()
    log.info("Neo4j connected")
    return _driver


# ── Embed ─────────────────────────────────────────────────────────────────────

def embed(text: str) -> List[float]:
    model = load_model()
    vec   = model.encode([text], normalize_embeddings=True)
    return vec[0].tolist()


# ── Vector search ─────────────────────────────────────────────────────────────

def vector_search(query_emb: List[float], top_k: int,
                  doc_filter: str = "") -> List[Dict]:
    """
    Query Neo4j chunk_embeddings vector index.
    doc_filter: optional substring to filter c.doc_name (e.g. 'maroc' for Maroc convention).
    Returns list of chunk dicts with score.
    """
    driver = get_driver()
    results = []

    # When filtering by doc_name we over-fetch to compensate for the filter
    # For scoped searches, over-fetch massively so relevant docs are found
    # even if they are not in the globally top-similar chunks
    fetch_k = top_k * 50 if doc_filter else top_k

    with driver.session(database=NEO4J_DB) as session:
        try:
            if doc_filter:
                cypher = """
                    CALL db.index.vector.queryNodes('chunk_embeddings', $topK, $emb)
                    YIELD node AS c, score
                    WHERE c.chunk_type = 'text' AND score >= $min_score
                      AND toLower(c.doc_name) CONTAINS $filter
                    RETURN
                        c.chunk_id      AS chunk_id,
                        c.text          AS text,
                        c.doc_name      AS doc_name,
                        c.doc_type      AS doc_type,
                        c.article_ref   AS article_ref,
                        c.section_title AS section_title,
                        c.annee         AS annee,
                        score
                    ORDER BY score DESC
                    LIMIT $topK
                """
                res = session.run(cypher, topK=fetch_k, emb=query_emb,
                                  min_score=MIN_SCORE, filter=doc_filter)
            else:
                res = session.run("""
                    CALL db.index.vector.queryNodes('chunk_embeddings', $topK, $emb)
                    YIELD node AS c, score
                    WHERE c.chunk_type = 'text' AND score >= $min_score
                    RETURN
                        c.chunk_id      AS chunk_id,
                        c.text          AS text,
                        c.doc_name      AS doc_name,
                        c.doc_type      AS doc_type,
                        c.article_ref   AS article_ref,
                        c.section_title AS section_title,
                        c.annee         AS annee,
                        score
                    ORDER BY score DESC
                    LIMIT $topK
                """, topK=top_k, emb=query_emb, min_score=MIN_SCORE)

            for r in res:
                results.append({
                    "chunk_id":      r.get("chunk_id",      ""),
                    "text":          r.get("text",          ""),
                    "doc_name":      r.get("doc_name",      ""),
                    "doc_type":      r.get("doc_type",      ""),
                    "article_ref":   r.get("article_ref",   ""),
                    "section_title": r.get("section_title", ""),
                    "annee":         r.get("annee",         ""),
                    "score":         float(r.get("score",   0.0)),
                })

        except Exception as e:
            log.error(f"Vector search error: {e}")

    return results


# ── Routes ────────────────────────────────────────────────────────────────────

@app.route("/health", methods=["GET"])
def health():
    """Quick health check. C# EmbedSearchService does NOT call this — just for debugging."""
    try:
        driver = get_driver()
        driver.verify_connectivity()
        model  = load_model()
        return jsonify({
            "status":    "ok",
            "neo4j":     NEO4J_URI,
            "database":  NEO4J_DB,
            "model":     EMBED_MODEL,
            "dim":       model.get_sentence_embedding_dimension(),
        })
    except Exception as e:
        return jsonify({"status": "error", "error": str(e)}), 503


@app.route("/embed_search", methods=["POST"])
def embed_search():
    """
    Main endpoint called by C# EmbedSearchService.cs.

    Request body:
        { "query": "retenue à la source non résident", "top_k": 20 }

    Response:
        [
            {
                "doc_name":      "code_irpp_s_2023",
                "doc_type":      "Code",
                "article_ref":   "Art. 52",
                "section_title": "Retenue à la source",
                "annee":         "2023",
                "text":          "Art. 52 — Les revenus...",
                "score":         0.847
            },
            ...
        ]
    """
    t0 = time.time()

    try:
        body       = request.get_json(silent=True) or {}
        query      = str(body.get("query", "")).strip()
        top_k      = min(int(body.get("top_k", DEFAULT_TOP_K)), MAX_TOP_K)
        doc_filter = str(body.get("doc_filter", "")).strip().lower()  # optional doc_name filter

        if not query:
            return jsonify({"error": "query is required"}), 400

        log.info(f"embed_search: query='{query[:80]}' top_k={top_k} doc_filter='{doc_filter}'")

        # Embed the query
        emb = embed(query)

        # Vector search in Neo4j (scoped to doc_filter if provided)
        hits = vector_search(emb, top_k, doc_filter=doc_filter)

        elapsed = (time.time() - t0) * 1000
        log.info(f"embed_search: {len(hits)} hits in {elapsed:.0f}ms "
                 f"(top_score={hits[0]['score']:.3f})" if hits else
                 f"embed_search: 0 hits in {elapsed:.0f}ms")

        # Return only fields expected by C# EmbedSearchService (matches EmbedHit class)
        response = [{
            "doc_name":      h["doc_name"],
            "doc_type":      h["doc_type"],
            "article_ref":   h["article_ref"],
            "section_title": h["section_title"],
            "annee":         h["annee"],
            "text":          h["text"],
            "score":         h["score"],
        } for h in hits]

        return jsonify(response)

    except Exception as e:
        log.error(f"embed_search error: {e}", exc_info=True)
        return jsonify({"error": str(e)}), 500


@app.route("/embed_only", methods=["POST"])
def embed_only():
    """
    Utility endpoint: embed a text and return the vector.
    Useful for debugging — not called by the C# platform.

    Request: { "text": "retenue à la source" }
    Response: { "dim": 768, "vector": [0.012, -0.034, ...] }
    """
    body = request.get_json(silent=True) or {}
    text = str(body.get("text", "")).strip()
    if not text:
        return jsonify({"error": "text is required"}), 400
    vec = embed(text)
    return jsonify({"dim": len(vec), "vector": vec[:10], "note": "First 10 dims shown"})


# ── Main ──────────────────────────────────────────────────────────────────────

if __name__ == "__main__":
    print("=" * 60)
    print(f"  Fiscal Platform — Embed Server")
    print(f"  Model   : {EMBED_MODEL}")
    print(f"  Neo4j   : {NEO4J_URI} / {NEO4J_DB}")
    print(f"  Port    : {PORT}")
    print("=" * 60)
    print()
    print("Pre-loading model and connecting to Neo4j…")
    try:
        load_model()
        get_driver()
        print()
        print(f"Server ready. C# platform can now use vector search.")
        print(f"Listening on http://127.0.0.1:{PORT}/embed_search")
        print("Press Ctrl+C to stop.")
        print()
    except Exception as e:
        print(f"STARTUP ERROR: {e}")
        print("Server will still start but embed_search will fail until fixed.")

    app.run(
        host="127.0.0.1",
        port=PORT,
        debug=False,      # Don't use debug=True — it loads the model twice
        threaded=True,    # Handle concurrent requests from C# platform
    )
