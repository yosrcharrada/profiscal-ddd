# -*- coding: utf-8 -*-
"""
import_cdpf_bct_from_json.py
============================
Load the corrected CDPF (2023/2024/2025) + Circulaire BCT N°2016-9 into the work-PC `taxmindvf`
FROM the portable export file `cdpf_bct_export.json` — with the 384-dim embeddings already baked
in. This is the "just ship the fixed data" path: it needs NEITHER the `taxmindfinal` graph NOR the
sentence-transformers embedding model on this machine — only the `neo4j` Python driver (the same
one embed_server.py already uses).

WHAT IT DOES
  1. Back up the current CDPF chunks in taxmindvf to a JSON (reversible).
  2. Delete the old CDPF 2023/24/25 + any prior BCT import from taxmindvf.
  3. CREATE the corrected chunks from cdpf_bct_export.json (embeddings included → the existing
     chunk_embeddings vector index picks them up automatically).
  4. Re-create the NEXT_PART reading-order chain.
  5. Verify.

It touches ONLY those four documents; the rest of your taxmindvf is left untouched.

HOW TO RUN (on the work PC)
  * Put cdpf_bct_export.json next to this script (transfer it from the machine that produced it).
  * Make sure Neo4j is running and holds `taxmindvf`.
  * pip install neo4j   (already present in the embed_server venv)
  * python import_cdpf_bct_from_json.py

Idempotent: safe to re-run (deletes the target docs first).
"""
import os, sys, io, json, time
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
try:
    from neo4j import GraphDatabase
except ImportError:
    sys.exit("ERROR: pip install neo4j")

URI  = os.getenv("NEO4J_URI",      "neo4j://127.0.0.1:7687")
USER = os.getenv("NEO4J_USERNAME", os.getenv("NEO4J_USER", "neo4j"))
PASS = os.getenv("NEO4J_PASSWORD", "neo4j")
DB   = os.getenv("MIGRATE_TARGET_DB", "taxmindvf")

HERE = os.path.dirname(os.path.abspath(__file__))
EXPORT_PATH = os.path.join(HERE, "cdpf_bct_export.json")
BACKUP_PATH = os.path.join(HERE, "taxmindvf_cdpf_backup.json")

CDPF_EDITIONS = [
    "code_droits_procedures_fiscaux_2023",
    "code_droits_procedures_fiscaux_2024",
    "code_droits_procedures_fiscaux_2025",
]
BCT_DOC = "doctrine_circulaire_bct_2016_09"

if not os.path.exists(EXPORT_PATH):
    sys.exit(f"ERROR: {EXPORT_PATH} not found. Copy cdpf_bct_export.json next to this script first.")

with open(EXPORT_PATH, "r", encoding="utf-8") as f:
    payload = json.load(f)
chunks = payload["chunks"]
edges  = payload["next_part_edges"]
print(f"Loaded export: {len(chunks)} chunks, {len(edges)} NEXT_PART edges")

driver = GraphDatabase.driver(URI, auth=(USER, PASS))
def dst(q, **kw):
    with driver.session(database=DB) as s: return list(s.run(q, **kw))

# 0) sanity: target reachable
try:
    dst("RETURN 1")
except Exception as e:
    sys.exit(f"ERROR: cannot reach Neo4j '{DB}' at {URI}: {e}")

# 1) backup current CDPF (without embeddings, to keep the file small)
backup = {"chunks": [], "next_part_edges": []}
for ed in CDPF_EDITIONS:
    for r in dst("MATCH (c:Chunk {document_id:$d}) RETURN c", d=ed):
        d = dict(r["c"]); d.pop("embedding", None); backup["chunks"].append(d)
    for r in dst("""MATCH (a:Chunk {document_id:$d})-[:NEXT_PART]->(b:Chunk {document_id:$d})
                    RETURN a.chunk_id AS a, b.chunk_id AS b""", d=ed):
        backup["next_part_edges"].append([r["a"], r["b"]])
with open(BACKUP_PATH, "w", encoding="utf-8") as f:
    json.dump(backup, f, ensure_ascii=False)
print(f"[1] backed up {len(backup['chunks'])} current CDPF chunks -> {BACKUP_PATH}")

# 2) delete old CDPF + any prior BCT
for ed in CDPF_EDITIONS + [BCT_DOC]:
    dst("MATCH (c:Chunk {document_id:$d}) DETACH DELETE c", d=ed)
print(f"[2] deleted old CDPF editions + prior BCT (if any) from {DB}")

# 3) create corrected chunks (embeddings baked in) — batched
BATCH = 500
t0 = time.time()
for i in range(0, len(chunks), BATCH):
    dst("UNWIND $rows AS row CREATE (c:Chunk) SET c = row", rows=chunks[i:i+BATCH])
print(f"[3] created {len(chunks)} chunks (with embeddings) in {time.time()-t0:.1f}s")

# 4) re-create NEXT_PART edges
t0 = time.time()
for i in range(0, len(edges), BATCH):
    pairs = [{"a": a, "b": b} for a, b in edges[i:i+BATCH]]
    dst("""UNWIND $pairs AS p MATCH (a:Chunk {chunk_id:p.a}),(b:Chunk {chunk_id:p.b})
           CREATE (a)-[:NEXT_PART]->(b)""", pairs=pairs)
print(f"[4] created {len(edges)} NEXT_PART edges in {time.time()-t0:.1f}s")

# 5) verify
n112 = dst("MATCH (c:Chunk {document_id:'code_droits_procedures_fiscaux_2025', article_number:'112'}) RETURN count(c) AS n")[0]["n"]
bis  = dst("MATCH (c:Chunk {document_id:'code_droits_procedures_fiscaux_2025', article_number:'112 bis'}) RETURN count(c) AS n")[0]["n"]
bct21= dst("MATCH (c:Chunk {document_id:$d, article_number:'21'}) RETURN count(c) AS n", d=BCT_DOC)[0]["n"]
emb  = dst("MATCH (c:Chunk {document_id:'code_droits_procedures_fiscaux_2025'}) WHERE c.embedding IS NOT NULL RETURN count(c) AS n")[0]["n"]
dim  = dst("MATCH (c:Chunk {document_id:$d, article_number:'21'}) RETURN size(c.embedding) AS d", d=BCT_DOC)[0]["d"]
anchor = dst("""MATCH (c:Chunk {document_id:'code_droits_procedures_fiscaux_2025', article_number:'112'})
                WHERE toLower(c.content) CONTAINS 'régularisation de leur situation fiscale' RETURN count(c) AS n""")[0]["n"]
print("\n[5] verification:")
print(f"    CDPF2025 Art.112 chunks : {n112}  (expect 8)")
print(f"    CDPF2025 Art.112 bis    : {bis}   (expect 1, distinct)")
print(f"    Art.112 header w/ anchor: {anchor} (expect >=1)")
print(f"    BCT Art.21 chunks       : {bct21} (expect 1)")
print(f"    CDPF2025 embeddings     : {emb}   (expect 860)")
print(f"    embedding dimension     : {dim}   (expect 384)")
ok = n112 == 8 and bct21 == 1 and anchor >= 1 and emb > 0 and dim == 384
print("\n" + ("✅ IMPORT OK — set the app to taxmindvf, rebuild, restart, and re-test."
              if ok else "⚠️  Something looks off — check the counts above."))
driver.close()
sys.exit(0 if ok else 1)
