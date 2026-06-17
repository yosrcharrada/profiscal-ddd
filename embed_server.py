from __future__ import annotations
"""
embed_server.py — Two-stage semantic search microservice
Stage 1: Bi-encoder (paraphrase-multilingual-mpnet-base-v2, 768-dim)
Stage 2: Cross-encoder re-ranker (mmarco-mMiniLMv2-L12-H384-v1, multilingual)
"""

# ── ZSCALER SSL — must run before any SSL-using import ───────────────────────
import os, certifi, pathlib

_here            = pathlib.Path(__file__).resolve().parent
_zscaler_cert    = _here / "ZscalerRootCertificate-2048-SHA256.pem"
_combined_bundle = _here / "combined_bundle.pem"

if _zscaler_cert.exists():
    if not _combined_bundle.exists():
        with open(certifi.where(), "rb") as f: _base    = f.read()
        with open(_zscaler_cert,   "rb") as f: _zscaler = f.read()
        with open(_combined_bundle, "wb") as f: f.write(_base + b"\n" + _zscaler)
    os.environ["SSL_CERT_FILE"]      = str(_combined_bundle)
    os.environ["REQUESTS_CA_BUNDLE"] = str(_combined_bundle)
    os.environ["CURL_CA_BUNDLE"]     = str(_combined_bundle)
    print(f"✅ Zscaler SSL configured: {_combined_bundle.name}")
else:
    print(f"⚠️  Zscaler cert not found at {_zscaler_cert}")

# ── Imports ───────────────────────────────────────────────────────────────────
import time, logging
from typing import List, Dict, Optional

from flask import Flask, request, jsonify
from sentence_transformers import SentenceTransformer, CrossEncoder
from neo4j import GraphDatabase

try:
    from dotenv import load_dotenv
    load_dotenv()
except ImportError:
    pass

# ── Flask app — MUST be defined before any @app.route ────────────────────────
app = Flask(__name__)

# ── Config ────────────────────────────────────────────────────────────────────
NEO4J_URI  = os.getenv("NEO4J_URI",      "neo4j://127.0.0.1:7687")
NEO4J_USER = os.getenv("NEO4J_USERNAME", os.getenv("NEO4J_USER", "neo4j"))
NEO4J_PASS = os.getenv("NEO4J_PASSWORD", "neo4j123")
NEO4J_DB   = os.getenv("NEO4J_DATABASE", "tunisian-fiscal")

EMBED_MODEL         = "paraphrase-multilingual-mpnet-base-v2"
CROSS_ENCODER_MODEL = "cross-encoder/mmarco-mMiniLMv2-L12-H384-v1"
MODEL_CACHE         = str(_here / "model_cache")
PORT                = 8081
MIN_SCORE           = float(os.getenv("MIN_SCORE", "0.30"))
DEFAULT_TOP_K       = 20
MAX_TOP_K           = 40
CANDIDATE_MULTIPLIER = 3

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [embed_server] %(levelname)s: %(message)s",
    datefmt="%H:%M:%S",
)
log = logging.getLogger("embed_server")

# ── Globals ───────────────────────────────────────────────────────────────────
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
    cache = pathlib.Path(MODEL_CACHE)
    local_paths = [
        cache / "sentence-transformers_paraphrase-multilingual-mpnet-base-v2",
        cache / "paraphrase-multilingual-mpnet-base-v2",
        _here  / "paraphrase-multilingual-mpnet-base-v2",
    ]
    local = next((p for p in local_paths if (p / "config.json").exists()), None)
    if local:
        _model = SentenceTransformer(str(local))
    else:
        try:
            _model = SentenceTransformer(EMBED_MODEL, cache_folder=MODEL_CACHE,
                                         local_files_only=True)
        except Exception:
            import ssl; ssl._create_default_https_context = ssl._create_unverified_context
            log.warning("Downloading bi-encoder with SSL verification disabled")
            _model = SentenceTransformer(EMBED_MODEL, cache_folder=MODEL_CACHE)
    log.info(f"Bi-encoder loaded in {time.time()-t0:.1f}s — "
             f"dim={_model.get_sentence_embedding_dimension()}")
    return _model


def load_cross_encoder() -> Optional[CrossEncoder]:
    global _cross_encoder
    if _cross_encoder is not None:
        return _cross_encoder
    try:
        log.info(f"Loading cross-encoder: {CROSS_ENCODER_MODEL}…")
        t0 = time.time()
        cache = pathlib.Path(MODEL_CACHE)
        local_paths = [
            cache / CROSS_ENCODER_MODEL.replace("/", "_"),
            cache / "cross-encoder_mmarco-mMiniLMv2-L12-H384-v1",
        ]
        local = next((p for p in local_paths if p.exists()), None)
        if local:
            _cross_encoder = CrossEncoder(str(local))
        else:
            try:
                _cross_encoder = CrossEncoder(CROSS_ENCODER_MODEL, cache_dir=MODEL_CACHE)
            except Exception:
                import ssl; ssl._create_default_https_context = ssl._create_unverified_context
                _cross_encoder = CrossEncoder(CROSS_ENCODER_MODEL, cache_dir=MODEL_CACHE)
        log.info(f"Cross-encoder loaded in {time.time()-t0:.1f}s ✅")
    except Exception as e:
        log.warning(f"Cross-encoder unavailable — bi-encoder only. Reason: {e}")
    return _cross_encoder


def get_driver():
    global _driver
    if _driver is not None:
        return _driver
    log.info(f"Connecting to Neo4j: {NEO4J_URI} / {NEO4J_DB}")
    _driver = GraphDatabase.driver(NEO4J_URI, auth=(NEO4J_USER, NEO4J_PASS))
    _driver.verify_connectivity()
    log.info("Neo4j connected")
    return _driver

# ── Core functions ────────────────────────────────────────────────────────────

def embed(text: str) -> List[float]:
    return load_model().encode([text], normalize_embeddings=True)[0].tolist()


def vector_search(query_emb: List[float], top_k: int,
                  doc_filter: str = "") -> List[Dict]:
    results = []
    try:
        driver = get_driver()
        with driver.session(database=NEO4J_DB) as session:
            if doc_filter:
                res = session.run("""
                    CALL db.index.vector.queryNodes('chunk_embeddings', $topK, $emb)
                    YIELD node AS c, score
                    WHERE score >= $min_score AND c.chunk_type = 'text'
                      AND toLower(c.doc_name) CONTAINS toLower($filter)
                    RETURN c.chunk_id AS chunk_id, c.text AS text,
                           c.doc_name AS doc_name, c.doc_type AS doc_type,
                           c.article_ref AS article_ref,
                           c.section_title AS section_title,
                           c.annee AS annee, score
                    ORDER BY score DESC LIMIT $topK
                """, topK=top_k, emb=query_emb, min_score=MIN_SCORE, filter=doc_filter)
            else:
                res = session.run("""
                    CALL db.index.vector.queryNodes('chunk_embeddings', $topK, $emb)
                    YIELD node AS c, score
                    WHERE score >= $min_score AND c.chunk_type = 'text'
                    RETURN c.chunk_id AS chunk_id, c.text AS text,
                           c.doc_name AS doc_name, c.doc_type AS doc_type,
                           c.article_ref AS article_ref,
                           c.section_title AS section_title,
                           c.annee AS annee, score
                    ORDER BY score DESC LIMIT $topK
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


def rerank(query: str, candidates: List[Dict], top_k: int) -> List[Dict]:
    if not candidates:
        return candidates
    ce = load_cross_encoder()
    if ce is None:
        return candidates[:top_k]
    try:
        t0     = time.time()
        pairs  = [(query, c["text"][:512]) for c in candidates]
        scores = ce.predict(pairs)
        for i, c in enumerate(candidates):
            c["ce_score"] = float(scores[i])
        reranked = sorted(candidates, key=lambda x: x["ce_score"], reverse=True)[:top_k]
        elapsed  = (time.time() - t0) * 1000
        log.info(f"Cross-encoder: {len(candidates)}→{len(reranked)} in {elapsed:.0f}ms")
        for r in reranked:
            r["score"] = r["ce_score"]
        return reranked
    except Exception as e:
        log.warning(f"Cross-encoder failed: {e} — using bi-encoder ranking")
        return candidates[:top_k]

# ── Routes ────────────────────────────────────────────────────────────────────

@app.route("/health", methods=["GET"])
def health():
    try:
        get_driver().verify_connectivity()
        model = load_model()
        return jsonify({
            "status":        "ok",
            "bi_encoder":    EMBED_MODEL,
            "cross_encoder": CROSS_ENCODER_MODEL if _cross_encoder else "not loaded",
            "dim":           model.get_sentence_embedding_dimension(),
            "two_stage":     _cross_encoder is not None,
        })
    except Exception as e:
        return jsonify({"status": "error", "error": str(e)}), 503


@app.route("/embed_search", methods=["POST"])
def embed_search():
    t0 = time.time()
    try:
        body       = request.get_json(silent=True) or {}
        query      = str(body.get("query", "")).strip()
        top_k      = min(int(body.get("top_k", DEFAULT_TOP_K)), MAX_TOP_K)
        doc_filter = str(body.get("doc_filter", "")).strip().lower()

        if not query:
            return jsonify({"error": "query is required"}), 400

        log.info(f"embed_search: '{query[:80]}' top_k={top_k} filter='{doc_filter}'")

        candidate_k = min(top_k * CANDIDATE_MULTIPLIER, MAX_TOP_K)
        emb         = embed(query)
        candidates  = vector_search(emb, candidate_k, doc_filter=doc_filter)

        if not candidates:
            return jsonify([])

        results = rerank(query, candidates, top_k)
        elapsed = (time.time() - t0) * 1000
        log.info(f"embed_search done: {len(results)} results in {elapsed:.0f}ms")

        return jsonify([{
            "chunk_id":      h.get("chunk_id",      ""),
            "doc_name":      h.get("doc_name",      ""),
            "doc_type":      h.get("doc_type",      ""),
            "article_ref":   h.get("article_ref",   ""),
            "section_title": h.get("section_title", ""),
            "annee":         h.get("annee",         ""),
            "text":          h.get("text",          ""),
            "score":         h.get("score",         0.0),
        } for h in results])

    except Exception as e:
        log.error(f"embed_search error: {e}", exc_info=True)
        return jsonify({"error": str(e)}), 500


@app.route("/embed_only", methods=["POST"])
def embed_only():
    body = request.get_json(silent=True) or {}
    text = str(body.get("text", "")).strip()
    if not text:
        return jsonify({"error": "text is required"}), 400
    vec = embed(text)
    return jsonify({"dim": len(vec), "vector": vec[:10]})

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
        load_model()
        load_cross_encoder()
        get_driver()
        ce_status = "✅ Two-stage retrieval active" if _cross_encoder else \
                    "⚠️  Cross-encoder unavailable — bi-encoder only"
        print(f"\nServer ready. {ce_status}")
        print(f"Listening on http://127.0.0.1:{PORT}/embed_search")
        print("Press Ctrl+C to stop.\n")
    except Exception as e:
        print(f"STARTUP ERROR: {e}")
        print("Server will start but embed_search may fail.\n")

    app.run(host="127.0.0.1", port=PORT, debug=False, threaded=True)
