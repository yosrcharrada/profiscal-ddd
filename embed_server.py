from __future__ import annotations
"""
embed_server.py
===============
Python microservice for semantic vector search with cross-encoder re-ranking.
Called by C# EmbedSearchAgent.cs on http://127.0.0.1:8081/embed_search

Two-stage retrieval:
  Stage 1 — Bi-encoder (paraphrase-multilingual-mpnet-base-v2, 768-dim)
            Fast vector similarity, retrieves top_k * 3 candidates
  Stage 2 — Cross-encoder (mmarco-mMiniLMv2-L12-H384-v1, multilingual)
            Accurate re-ranking of candidates, returns top_k final results

ZSCALER SSL FIX (EY work PC):
  Place these two files in the same folder as this script:
    - ZscalerRootCertificate-2048-SHA256.pem
    - combined_bundle.pem (created automatically on first run)
"""

# ── ZSCALER SSL SETUP — must run before any SSL-using import ─────────────────
import os, certifi, pathlib

_here            = pathlib.Path(__file__).resolve().parent
_zscaler_cert    = _here / "ZscalerRootCertificate-2048-SHA256.pem"
_combined_bundle = _here / "combined_bundle.pem"

if _zscaler_cert.exists():
    if not _combined_bundle.exists():
        with open(certifi.where(), "rb") as f: _base    = f.read()
        with open(_zscaler_cert,   "rb") as f: _zscaler = f.read()
        with open(_combined_bundle, "wb") as f: f.write(_base + b"\n" + _zscaler)
        print(f"✅ Created combined SSL bundle: {_combined_bundle}")
    os.environ["SSL_CERT_FILE"]      = str(_combined_bundle)
    os.environ["REQUESTS_CA_BUNDLE"] = str(_combined_bundle)
    os.environ["CURL_CA_BUNDLE"]     = str(_combined_bundle)
    print(f"✅ Zscaler SSL configured: {_combined_bundle.name}")
else:
    print(f"⚠️  Zscaler cert not found at {_zscaler_cert}")
    print(f"   Sentence-transformers may fail to download on EY network.")
    print(f"   Place ZscalerRootCertificate-2048-SHA256.pem next to embed_server.py")

# ─────────────────────────────────────────────────────────────────────────────

import json
import time
import logging
from typing import List, Dict, Optional

try:
    from flask import Flask, request, jsonify
except ImportError:
    print("ERROR: flask not installed. Run: pip install flask"); raise

try:
    from sentence_transformers import SentenceTransformer, CrossEncoder
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

# Stage 1: Bi-encoder — multilingual semantic similarity
EMBED_MODEL  = "paraphrase-multilingual-mpnet-base-v2"

# Stage 2: Cross-encoder — multilingual re-ranking
# mmarco-mMiniLMv2 is specifically designed for multilingual passage re-ranking
# ~120MB, runs on CPU, significantly improves ranking accuracy for French text
CROSS_ENCODER_MODEL = "cross-encoder/mmarco-mMiniLMv2-L12-H384-v1"

MODEL_CACHE    = "./model_cache"
PORT           = 8081
MIN_SCORE      = 0.30
DEFAULT_TOP_K  = 20
MAX_TOP_K      = 40

# Cross-encoder candidate multiplier:
# Bi-encoder fetches top_k * CANDIDATE_MULTIPLIER, cross-encoder re-ranks to top_k
CANDIDATE_MULTIPLIER = 3

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [embed_server] %(levelname)s: %(message)s",
    datefmt="%H:%M:%S",
)
log = logging.getLogger("embed_server")

# ── Globals (loaded once at startup) ─────────────────────────────────────────

_model:         Optional[SentenceTransformer] = None
_cross_encoder: Optional[CrossEncoder]        = None
_driver                                       = None

# ── Model loading ─────────────────────────────────────────────────────────────

def load_model() -> SentenceTransformer:
    global _model
    if _model is not None:
        return _model
    log.info(f"Loading bi-encoder: {EMBED_MODEL}…")
    t0 = time.time()
    local_paths = [
        pathlib.Path(MODEL_CACHE) / "sentence-transformers_paraphrase-multilingual-mpnet-base-v2",
        pathlib.Path(MODEL_CACHE) / "paraphrase-multilingual-mpnet-base-v2",
        pathlib.Path("./paraphrase-multilingual-mpnet-base-v2"),
    ]
    local_path = next((p for p in local_paths if (p / "config.json").exists()), None)
    if local_path is not None:
        log.info(f"Loading bi-encoder from local path: {local_path}")
        _model = SentenceTransformer(str(local_path))
    else:
        try:
            _model = SentenceTransformer(EMBED_MODEL, cache_folder=MODEL_CACHE,
                                         local_files_only=True)
        except Exception:
            import ssl
            ssl._create_default_https_context = ssl._create_unverified_context
            log.warning("Downloading bi-encoder with SSL verification disabled")
            _model = SentenceTransformer(EMBED_MODEL, cache_folder=MODEL_CACHE)
    log.info(f"Bi-encoder loaded in {time.time()-t0:.1f}s — "
             f"dim={_model.get_sentence_embedding_dimension()}")
    return _model


def load_cross_encoder() -> Optional[CrossEncoder]:
    """
    Loads the cross-encoder re-ranker.
    Returns None on failure — the system falls back to bi-encoder ranking gracefully.
    Cross-encoder is an enhancement, not a requirement.
    """
    global _cross_encoder
    if _cross_encoder is not None:
        return _cross_encoder
    try:
        log.info(f"Loading cross-encoder: {CROSS_ENCODER_MODEL}…")
        t0 = time.time()
        # Try local cache first
        local_paths = [
            pathlib.Path(MODEL_CACHE) / CROSS_ENCODER_MODEL.replace("/", "_"),
            pathlib.Path(MODEL_CACHE) / "cross-encoder_mmarco-mMiniLMv2-L12-H384-v1",
        ]
        local_path = next((p for p in local_paths if p.exists()), None)
        if local_path is not None:
            _cross_encoder = CrossEncoder(str(local_path))
        else:
            try:
                _cross_encoder = CrossEncoder(CROSS_ENCODER_MODEL,
                                              cache_dir=MODEL_CACHE)
            except Exception:
                import ssl
                ssl._create_default_https_context = ssl._create_unverified_context
                log.warning("Downloading cross-encoder with SSL verification disabled")
                _cross_encoder = CrossEncoder(CROSS_ENCODER_MODEL,
                                              cache_dir=MODEL_CACHE)
        log.info(f"Cross-encoder loaded in {time.time()-t0:.1f}s ✅")
        return _cross_encoder
    except Exception as e:
        log.warning(f"Cross-encoder load failed — falling back to bi-encoder ranking only. "
                    f"Reason: {e}")
        return None


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
    vec = load_model().encode([text], normalize_embeddings=True)
    return vec[0].tolist()

# ── Vector search (Stage 1) ───────────────────────────────────────────────────

def vector_search(query_emb: List[float], top_k: int,
                  doc_filter: str = "") -> List[Dict]:
    """
    Stage 1: Bi-encoder vector search in Neo4j.
    Fetches top_k candidates (will be re-ranked by cross-encoder).
    """
    results = []
    try:
        driver = get_driver()
        with driver.session(database=NEO4J_DB) as session:
            if doc_filter:
                res = session.run("""
                    CALL db.index.vector.queryNodes('chunk_embeddings', $topK, $emb)
                    YIELD node AS c, score
                    WHERE score >= $min_score
                      AND c.chunk_type = 'text'
                      AND toLower(c.doc_name) CONTAINS toLower($filter)
                    RETURN c.chunk_id      AS chunk_id,
                           c.text         AS text,
                           c.doc_name     AS doc_name,
                           c.doc_type     AS doc_type,
                           c.article_ref  AS article_ref,
                           c.section_title AS section_title,
                           c.annee        AS annee,
                           score
                    ORDER BY score DESC
                    LIMIT $topK
                """, topK=top_k, emb=query_emb,
                     min_score=MIN_SCORE, filter=doc_filter)
            else:
                res = session.run("""
                    CALL db.index.vector.queryNodes('chunk_embeddings', $topK, $emb)
                    YIELD node AS c, score
                    WHERE score >= $min_score
                      AND c.chunk_type = 'text'
                    RETURN c.chunk_id       AS chunk_id,
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


# ── Cross-encoder re-ranking (Stage 2) ────────────────────────────────────────

def rerank(query: str, candidates: List[Dict], top_k: int) -> List[Dict]:
    """
    Stage 2: Cross-encoder re-ranks bi-encoder candidates.

    The cross-encoder reads (query, document) pairs together — giving it
    much better relevance judgement than cosine similarity alone.

    For French legal text: mmarco-mMiniLMv2 was trained on multilingual
    MS MARCO data and handles French, Arabic, and mixed-language documents.

    Falls back to bi-encoder ranking if cross-encoder unavailable.
    """
    if not candidates:
        return candidates

    ce = load_cross_encoder()
    if ce is None:
        log.debug("Cross-encoder unavailable — using bi-encoder ranking")
        return candidates[:top_k]

    try:
        t0 = time.time()
        # Build (query, document_text) pairs for cross-encoder
        # Use first 512 chars of text — cross-encoder has token limit
        pairs = [(query, c["text"][:512]) for c in candidates]

        # Predict relevance scores for all pairs simultaneously
        ce_scores = ce.predict(pairs)

        # Attach cross-encoder scores and re-sort
        for i, candidate in enumerate(candidates):
            candidate["ce_score"] = float(ce_scores[i])

        reranked = sorted(candidates, key=lambda x: x["ce_score"], reverse=True)[:top_k]

        elapsed = (time.time() - t0) * 1000
        top_ce  = reranked[0]["ce_score"]  if reranked else 0
        top_be  = reranked[0]["score"]     if reranked else 0
        log.info(f"Cross-encoder: {len(candidates)} → {len(reranked)} in {elapsed:.0f}ms "
                 f"(top ce={top_ce:.3f} be={top_be:.3f})")

        # Use cross-encoder score as the final score for C# to use
        for r in reranked:
            r["score"] = r["ce_score"]  # overwrite bi-encoder score

        return reranked

    except Exception as e:
        log.warning(f"Cross-encoder re-ranking failed: {e} — using bi-encoder ranking")
        return candidates[:top_k]


# ── Routes ────────────────────────────────────────────────────────────────────

@app.route("/health", methods=["GET"])
def health():
    try:
        driver = get_driver()
        driver.verify_connectivity()
        model  = load_model()
        ce     = _cross_encoder  # don't re-load, just check if loaded
        return jsonify({
            "status":        "ok",
            "neo4j":         NEO4J_URI,
            "database":      NEO4J_DB,
            "bi_encoder":    EMBED_MODEL,
            "cross_encoder": CROSS_ENCODER_MODEL if ce else "not loaded",
            "dim":           model.get_sentence_embedding_dimension(),
            "two_stage":     ce is not None,
        })
    except Exception as e:
        return jsonify({"status": "error", "error": str(e)}), 503


@app.route("/embed_search", methods=["POST"])
def embed_search():
    """
    Two-stage retrieval endpoint called by C# EmbedSearchAgent.cs.

    Stage 1 — Bi-encoder: retrieves top_k * CANDIDATE_MULTIPLIER candidates fast
    Stage 2 — Cross-encoder: re-ranks candidates, returns top_k

    Request:  { "query": "retenue à la source non résident", "top_k": 20 }
    Response: ranked list of chunk dicts (same format as before)

    The C# code sees no difference — it still gets a ranked list.
    The ranking is simply much more accurate with cross-encoder.
    """
    t0 = time.time()
    try:
        body       = request.get_json(silent=True) or {}
        query      = str(body.get("query", "")).strip()
        top_k      = min(int(body.get("top_k", DEFAULT_TOP_K)), MAX_TOP_K)
        doc_filter = str(body.get("doc_filter", "")).strip().lower()

        if not query:
            return jsonify({"error": "query is required"}), 400

        log.info(f"embed_search: '{query[:80]}' top_k={top_k} filter='{doc_filter}'")

        # ── Stage 1: Bi-encoder retrieves more candidates than needed ──────────
        # Fetch top_k * CANDIDATE_MULTIPLIER so cross-encoder has enough to work with
        candidate_k = min(top_k * CANDIDATE_MULTIPLIER, MAX_TOP_K)
        emb         = embed(query)
        candidates  = vector_search(emb, candidate_k, doc_filter=doc_filter)

        if not candidates:
            log.info(f"embed_search: 0 candidates in {(time.time()-t0)*1000:.0f}ms")
            return jsonify([])

        log.info(f"Stage 1 (bi-encoder): {len(candidates)} candidates "
                 f"(top={candidates[0]['score']:.3f})")

        # ── Stage 2: Cross-encoder re-ranks to top_k ──────────────────────────
        results = rerank(query, candidates, top_k)

        elapsed = (time.time() - t0) * 1000
        log.info(f"embed_search done: {len(results)} results in {elapsed:.0f}ms")

        # Return fields expected by C# EmbedSearchAgent (matches EmbedHit class)
        response = [{
            "chunk_id":      h.get("chunk_id",      ""),
            "doc_name":      h.get("doc_name",      ""),
            "doc_type":      h.get("doc_type",      ""),
            "article_ref":   h.get("article_ref",   ""),
            "section_title": h.get("section_title", ""),
            "annee":         h.get("annee",         ""),
            "text":          h.get("text",          ""),
            "score":         h.get("score",         0.0),
        } for h in results]

        return jsonify(response)

    except Exception as e:
        log.error(f"embed_search error: {e}", exc_info=True)
        return jsonify({"error": str(e)}), 500


@app.route("/embed_only", methods=["POST"])
def embed_only():
    """Utility: embed text and return vector. Not called by C# platform."""
    body = request.get_json(silent=True) or {}
    text = str(body.get("text", "")).strip()
    if not text:
        return jsonify({"error": "text is required"}), 400
    vec = embed(text)
    return jsonify({"dim": len(vec), "vector": vec[:10],
                    "note": "First 10 dims shown"})


# ── Flask app ─────────────────────────────────────────────────────────────────

app = Flask(__name__)


# ── Main ──────────────────────────────────────────────────────────────────────

if __name__ == "__main__":
    print("=" * 60)
    print(f"  Fiscal Platform — Embed Server (Two-Stage Retrieval)")
    print(f"  Stage 1 : {EMBED_MODEL}")
    print(f"  Stage 2 : {CROSS_ENCODER_MODEL}")
    print(f"  Neo4j   : {NEO4J_URI} / {NEO4J_DB}")
    print(f"  Port    : {PORT}")
    print("=" * 60)
    print()
    print("Pre-loading models and connecting to Neo4j…")
    try:
        load_model()          # Required — fails fast if missing
        load_cross_encoder()  # Optional — falls back gracefully if unavailable
        get_driver()
        print()
        ce_status = "✅ Two-stage retrieval active" if _cross_encoder else \
                    "⚠️  Cross-encoder unavailable — bi-encoder only"
        print(f"Server ready. {ce_status}")
        print(f"Listening on http://127.0.0.1:{PORT}/embed_search")
        print("Press Ctrl+C to stop.")
        print()
    except Exception as e:
        print(f"STARTUP ERROR: {e}")
        print("Server will still start but embed_search will fail until fixed.")

    app.run(
        host="127.0.0.1",
        port=PORT,
        debug=False,
        threaded=True,
    )
