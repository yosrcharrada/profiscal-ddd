# -*- coding: utf-8 -*-
"""
migrate_cdpf_bct_into_taxmindvf.py
==================================
ONE-TIME data migration. Run this ON THE WORK PC (or any machine whose Neo4j holds the
`taxmindvf` graph the app actually queries) to:

  1. BACK UP the current CDPF chunks (2023/2024/2025) to a JSON file (reversible).
  2. DELETE the old, mislabelled CDPF French editions from `taxmindvf`
     (the graph had Art.112's real text stamped under article_number=110, while
      article_number=112 was actually "112 bis").
  3. RE-IMPORT the corrected CDPF editions + the Circulaire BCT N°2016-9 (Art.21 — the
     transfer-of-funds formalism, previously ABSENT from the corpus) FROM the `taxmindfinal`
     graph, reshaped into taxmindvf's own schema
     (document_id / folder / chunk_type / article_number / part_number / total_parts / NEXT_PART).
  4. BACKFILL article_number on the BCT chunks from their "Article N :" headers.
  5. COMPUTE 384-dim embeddings for every new chunk with the SAME model that built the graph
     (paraphrase-multilingual-MiniLM-L12-v2), so vector search covers them too.
  6. VERIFY the result.

WHY: the code (case-agent checklists) already asks for CDPF Art.112 (number-anchored) and the
BCT circulaire, but that data only exists in taxmindvf AFTER this migration runs. Without it,
those sources come back "NON DOCUMENTÉ".

PREREQUISITES on the machine you run this on:
  * Neo4j running, containing BOTH `taxmindvf` (target) AND `taxmindfinal` (source of correct data).
  * pip install neo4j sentence-transformers   (the embed_server venv already has these).
  * The .env NEO4J_* creds (or the defaults below) must point at that Neo4j.

If `taxmindfinal` is NOT on this machine, stop and ask for the JSON-export alternative instead.

Idempotent: safe to re-run — it deletes the target docs before re-importing.
"""
import os, sys, io, re, json, time
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

try:
    from neo4j import GraphDatabase
except ImportError:
    sys.exit("ERROR: pip install neo4j")

# ── config (env first, then defaults) ────────────────────────────────────────
URI  = os.getenv("NEO4J_URI",      "neo4j://127.0.0.1:7687")
USER = os.getenv("NEO4J_USERNAME", os.getenv("NEO4J_USER", "neo4j"))
PASS = os.getenv("NEO4J_PASSWORD", "neo4j")
SRC_DB = os.getenv("MIGRATE_SOURCE_DB", "taxmindfinal")
DST_DB = os.getenv("MIGRATE_TARGET_DB", "taxmindvf")
EMBED_MODEL = os.getenv("EMBED_MODEL", "paraphrase-multilingual-MiniLM-L12-v2")
EXPECTED_DIM = int(os.getenv("EMBED_DIM", "384"))

CDPF_EDITIONS = [
    "code_droits_procedures_fiscaux_2023",
    "code_droits_procedures_fiscaux_2024",
    "code_droits_procedures_fiscaux_2025",
]
BCT_DOC = "doctrine_circulaire_bct_2016_09"
BACKUP_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "taxmindvf_cdpf_backup.json")

driver = GraphDatabase.driver(URI, auth=(USER, PASS))
def src(q, **kw):
    with driver.session(database=SRC_DB) as s: return list(s.run(q, **kw))
def dst(q, **kw):
    with driver.session(database=DST_DB) as s: return list(s.run(q, **kw))

# ── 0) sanity: both DBs reachable, source has the data ───────────────────────
print(f"Source graph : {SRC_DB}\nTarget graph : {DST_DB}\n")
try:
    have_src = src("MATCH (c:Chunk)-[:BELONGS_TO]->(:Document {document_id:$d}) RETURN count(c) AS n",
                   d="code_droits_procedures_fiscaux_2025")[0]["n"]
except Exception as e:
    sys.exit(f"ERROR: cannot read source graph '{SRC_DB}': {e}\n"
             f"       This machine must have the '{SRC_DB}' graph. Ask for the JSON-export alternative.")
if have_src == 0:
    sys.exit(f"ERROR: '{SRC_DB}' has no CDPF 2025 chunks — wrong source graph?")

# ── 1) backup current CDPF in target ─────────────────────────────────────────
backup = {"chunks": [], "next_part_edges": []}
for ed in CDPF_EDITIONS:
    for r in dst("MATCH (c:Chunk {document_id:$d}) RETURN c", d=ed):
        backup["chunks"].append(dict(r["c"]))
    for r in dst("""MATCH (a:Chunk {document_id:$d})-[:NEXT_PART]->(b:Chunk {document_id:$d})
                    RETURN a.chunk_id AS a, b.chunk_id AS b""", d=ed):
        backup["next_part_edges"].append((r["a"], r["b"]))
# strip embeddings from the backup to keep it small
for c in backup["chunks"]:
    c.pop("embedding", None)
with open(BACKUP_PATH, "w", encoding="utf-8") as f:
    json.dump(backup, f, ensure_ascii=False)
print(f"[1] backed up {len(backup['chunks'])} old CDPF chunks -> {BACKUP_PATH}")

# ── 2) delete old CDPF + any prior BCT import from target ─────────────────────
for ed in CDPF_EDITIONS + [BCT_DOC]:
    dst("MATCH (c:Chunk {document_id:$d}) DETACH DELETE c", d=ed)
print(f"[2] deleted old CDPF editions + prior BCT (if any) from {DST_DB}")

# ── 3) import corrected docs from source, taxmindvf-shaped ───────────────────
def year_from(doc):
    m = re.search(r"_(\d{4})", doc); return m.group(1) if m else None

def import_document(document_id, folder):
    rows = src("""
        MATCH (c:Chunk)-[:BELONGS_TO]->(d:Document {document_id:$d})
        RETURN c.chunk_id AS chunk_id, c.chunk_type AS chunk_type, c.article_number AS article_number,
               c.article_display AS article_display, c.title AS title, c.content AS content,
               c.chunk_index AS chunk_index, c.has_table AS has_table
        ORDER BY c.chunk_index
    """, d=document_id)
    if not rows:
        print(f"    ! no source chunks for {document_id} — skipped"); return 0
    year = year_from(document_id)

    # part_number/total_parts scoped PER ARTICLE (article_number), not globally: the app's
    # FetchArticleLinesAsync treats part_number=1 as the article header AFTER filtering to one
    # article number, so the counter must restart per article.
    group_total, seen = {}, {}
    for r in rows:
        k = r["article_number"] or ""
        group_total[k] = group_total.get(k, 0) + 1
    batch = []
    for r in rows:
        k = r["article_number"] or ""
        seen[k] = seen.get(k, 0) + 1
        title = r["article_display"] or r["title"] or ""
        batch.append({
            "chunk_id": r["chunk_id"], "document_id": document_id, "folder": folder, "year": year,
            "chunk_type": r["chunk_type"] or "article", "article_number": r["article_number"],
            "article_display": title, "title": title, "content": r["content"] or "",
            "chunk_index": r["chunk_index"],
            "has_table": bool(r["has_table"]) if r["has_table"] is not None else False,
            "part_number": seen[k], "total_parts": group_total[k], "article_group": r["article_number"],
        })
    dst("UNWIND $rows AS row CREATE (c:Chunk) SET c = row", rows=batch)
    pairs = [{"a": batch[i]["chunk_id"], "b": batch[i+1]["chunk_id"]} for i in range(len(batch)-1)]
    if pairs:
        dst("""UNWIND $pairs AS p MATCH (a:Chunk {chunk_id:p.a}),(b:Chunk {chunk_id:p.b})
               CREATE (a)-[:NEXT_PART]->(b)""", pairs=pairs)
    print(f"    imported {document_id}: {len(batch)} chunks, {len(pairs)} NEXT_PART edges")
    return len(batch)

print("[3] importing corrected CDPF (folder=Recueils_textes_fiscaux) + BCT (folder=Notes_Communes):")
total = 0
for ed in CDPF_EDITIONS:
    total += import_document(ed, "Recueils_textes_fiscaux")
total += import_document(BCT_DOC, "Notes_Communes")   # Notes_Communes -> doc_type Doctrine in the app

# ── 4) backfill article_number on BCT chunks from "Article N :" headers ──────
pat = re.compile(r"^Article\s+(premier|\d+)\s*:", re.IGNORECASE)
updates = []
for r in dst("MATCH (c:Chunk {document_id:$d}) RETURN c.chunk_id AS id, c.content AS content", d=BCT_DOC):
    m = pat.match((r["content"] or "").strip())
    if m:
        num = "1" if m.group(1).lower() == "premier" else m.group(1)
        updates.append({"id": r["id"], "art": num, "disp": f"Article {num}"})
if updates:
    dst("""UNWIND $rows AS row MATCH (c:Chunk {chunk_id:row.id})
           SET c.article_number = row.art, c.article_display = row.disp""", rows=updates)
print(f"[4] backfilled article_number on {len(updates)} BCT chunks (Art.1..Art.30)")

# ── 5) embeddings ────────────────────────────────────────────────────────────
print(f"[5] embedding {total} new chunks with {EMBED_MODEL} ...")
try:
    from sentence_transformers import SentenceTransformer
except ImportError:
    sys.exit("ERROR: pip install sentence-transformers  (needed to embed the new chunks)")
model = SentenceTransformer(EMBED_MODEL)
dim = model.get_sentence_embedding_dimension()
if dim != EXPECTED_DIM:
    sys.exit(f"ERROR: model dim {dim} != expected {EXPECTED_DIM}. Set EMBED_MODEL to the exact "
             f"model that built taxmindvf's chunk_embeddings index.")
for doc in CDPF_EDITIONS + [BCT_DOC]:
    rows = dst("MATCH (c:Chunk {document_id:$d}) RETURN c.chunk_id AS id, c.content AS content", d=doc)
    if not rows: continue
    ids   = [r["id"] for r in rows]
    texts = [(r["content"] or "")[:2000] for r in rows]
    t0 = time.time()
    vecs = model.encode(texts, normalize_embeddings=True, batch_size=64, show_progress_bar=False)
    dst("UNWIND $rows AS row MATCH (c:Chunk {chunk_id:row.id}) SET c.embedding = row.vec",
        rows=[{"id": i, "vec": v.tolist()} for i, v in zip(ids, vecs)])
    print(f"    embedded {doc}: {len(ids)} chunks in {time.time()-t0:.1f}s")

# ── 6) verify ────────────────────────────────────────────────────────────────
print("\n[6] verification:")
n112 = dst("MATCH (c:Chunk {document_id:'code_droits_procedures_fiscaux_2025', article_number:'112'}) RETURN count(c) AS n")[0]["n"]
bis  = dst("MATCH (c:Chunk {document_id:'code_droits_procedures_fiscaux_2025', article_number:'112 bis'}) RETURN count(c) AS n")[0]["n"]
bct21= dst("MATCH (c:Chunk {document_id:$d, article_number:'21'}) RETURN count(c) AS n", d=BCT_DOC)[0]["n"]
emb  = dst("MATCH (c:Chunk {document_id:'code_droits_procedures_fiscaux_2025'}) RETURN count(c.embedding) AS n")[0]["n"]
anchor = dst("""MATCH (c:Chunk {document_id:'code_droits_procedures_fiscaux_2025', article_number:'112'})
                WHERE toLower(c.content) CONTAINS 'régularisation de leur situation fiscale' RETURN count(c) AS n""")[0]["n"]
print(f"    CDPF2025 Art.112 chunks : {n112}  (expect 8)")
print(f"    CDPF2025 Art.112 bis    : {bis}   (expect 1, distinct)")
print(f"    Art.112 header w/ anchor: {anchor} (expect >=1)")
print(f"    BCT Art.21 chunks       : {bct21} (expect 1)")
print(f"    CDPF2025 embeddings     : {emb}   (expect 860)")
ok = n112 == 8 and bct21 == 1 and anchor >= 1 and emb > 0
print("\n" + ("✅ MIGRATION OK — restart the API pointed at taxmindvf and re-test."
              if ok else "⚠️  Something looks off — check the counts above."))
driver.close()
sys.exit(0 if ok else 1)
