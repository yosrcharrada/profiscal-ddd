"""
================================================================================
IN-PLACE GRAPH ENRICHMENT — provision_uid
================================================================================
Fixes the taxmindvf article-collision problem WITHOUT re-chunking or re-embedding.

Problem: many distinct provisions (a code article, unrelated decrees, annexed
rate tables) share the same article_number (e.g. code_tva Art.7 = 47 chunks),
so fetch-by-number returns a polluted blob and the operative rate drowns.

Fix: give every chunk a stable `provision_uid` = the chunk_id of its provision
HEAD, where a head is a part_number=1 chunk (or a single-chunk provision).
Each chunk is assigned to its NEAREST preceding part_number=1 head along the
NEXT_PART chain — robust, exactly one uid per chunk, no double-assignment.

This is ADDITIVE and REVERSIBLE:
  - only SETs new properties (provision_uid, provision_head, provision_size)
  - never touches content, embeddings, relationships, or any existing property
  - rollback:  MATCH (c:Chunk) REMOVE c.provision_uid, c.provision_head, c.provision_size

Run:
    python enrich_provision_uid.py            # apply
    python enrich_provision_uid.py --verify   # report only, no writes
    python enrich_provision_uid.py --rollback # remove the added properties
================================================================================
"""
import sys
from neo4j import GraphDatabase

URI  = "neo4j://127.0.0.1:7687"
AUTH = ("neo4j", "neo4j123")
DB   = "taxmindvf"

def report_art7(session, label):
    print(f"\n  --- CTVA Art.7 state [{label}] ---")
    rows = list(session.run("""
        MATCH (c:Chunk)
        WHERE coalesce(c.doc_id,c.document_id)='code_tva_2023' AND c.article_number='7'
        RETURN c.provision_uid AS uid, count(*) AS n,
               head(collect(left(c.content,46))) AS sample
        ORDER BY n DESC
    """))
    total = sum(r["n"] for r in rows)
    print(f"    {total} chunks share article_number='7' -> {len(rows)} distinct provision(s)")
    for r in rows[:8]:
        uid = r["uid"] or "(none)"
        sample = (r["sample"] or "").replace("\n", " ")
        print(f"      uid={str(uid)[:34]:34} n={r['n']:2}  e.g. '{sample}'")

def rollback(session):
    print("Rolling back provision_uid enrichment...")
    r = session.run("""
        MATCH (c:Chunk) WHERE c.provision_uid IS NOT NULL
        REMOVE c.provision_uid, c.provision_head, c.provision_size
        RETURN count(c) AS n
    """).single()
    print(f"  removed provision properties from {r['n']} chunks")

def enrich(session):
    print("=" * 70)
    print("  ENRICH: assigning provision_uid")
    print("=" * 70)
    report_art7(session, "BEFORE")

    # 0) Idempotent reset — clear any prior run so re-running is always clean.
    print("\n  [0/4] clearing any prior provision_* props...")
    session.run("MATCH (c:Chunk) WHERE c.provision_uid IS NOT NULL "
                "REMOVE c.provision_uid, c.provision_head, c.provision_size").consume()

    # 1) Multi-part members -> nearest preceding part_number=1 head (via NEXT_PART),
    #    but ONLY along a path that stays inside ONE article_number. This is the key
    #    guard: ~861 NEXT_PART edges wrongly bridge different articles, so an unguarded
    #    walk leaks members across article boundaries. Requiring every node on the path
    #    to share the head's article_number keeps each provision inside its own article.
    print("  [1/4] assigning multi-part chunks to their provision head (article-guarded)...")
    r = session.run("""
        MATCH (m:Chunk) WHERE m.part_number IS NOT NULL
        MATCH p = (h:Chunk)-[:NEXT_PART*0..200]->(m)
        WHERE h.part_number = 1
          AND all(x IN nodes(p) WHERE coalesce(x.article_number,'∅') = coalesce(h.article_number,'∅'))
        WITH m, h, length(p) AS d ORDER BY d ASC
        WITH m, head(collect(h)) AS head
        WHERE head IS NOT NULL
        SET m.provision_uid = head.chunk_id
        RETURN count(m) AS n
    """).single()
    print(f"        {r['n']} chunks tagged")

    # 2) Single-chunk provisions (no part structure) -> own chunk_id.
    print("  [2/4] tagging single-chunk provisions...")
    r = session.run("""
        MATCH (c:Chunk)
        WHERE c.provision_uid IS NULL AND (c.part_number IS NULL OR c.part_number = 0)
        SET c.provision_uid = c.chunk_id
        RETURN count(c) AS n
    """).single()
    print(f"        {r['n']} chunks tagged")

    # 2b) Orphan parts (part_number set but no valid same-article head reachable) ->
    #     own chunk_id, so every chunk is addressable. Better a singleton than untagged.
    print("  [2b/4] tagging orphan parts as their own provision...")
    r = session.run("""
        MATCH (c:Chunk) WHERE c.provision_uid IS NULL
        SET c.provision_uid = c.chunk_id
        RETURN count(c) AS n
    """).single()
    print(f"        {r['n']} chunks tagged")

    # 3) Mark heads + stamp provision size (member count) for fast coalescing.
    print("  [3/4] marking provision heads + sizes...")
    session.run("""
        MATCH (c:Chunk) WHERE c.provision_uid = c.chunk_id
        SET c.provision_head = true
    """).consume()
    session.run("""
        MATCH (c:Chunk) WHERE c.provision_uid IS NOT NULL
        WITH c.provision_uid AS uid, count(*) AS sz
        MATCH (h:Chunk) WHERE h.chunk_id = uid
        SET h.provision_size = sz
    """).consume()

    # coverage
    cov = session.run("""
        MATCH (c:Chunk)
        RETURN sum(CASE WHEN c.provision_uid IS NULL THEN 1 ELSE 0 END) AS untagged,
               count(c) AS total,
               count(DISTINCT c.provision_uid) AS provisions
    """).single()
    print(f"\n  coverage: {cov['total']-cov['untagged']}/{cov['total']} chunks tagged "
          f"({cov['untagged']} untagged) across {cov['provisions']} distinct provisions")

    report_art7(session, "AFTER")

    # integrity: any chunk assigned to a provision whose head is a DIFFERENT article? (leakage)
    leak = session.run("""
        MATCH (c:Chunk) WHERE c.provision_uid IS NOT NULL
        MATCH (h:Chunk) WHERE h.chunk_id = c.provision_uid
        WITH sum(CASE WHEN coalesce(c.article_number,'x') <> coalesce(h.article_number,'x') THEN 1 ELSE 0 END) AS bad
        RETURN bad
    """).single()
    print(f"\n  integrity: {leak['bad']} chunks assigned across an article-number boundary (expect 0)")

def main():
    driver = GraphDatabase.driver(URI, auth=AUTH)
    with driver.session(database=DB) as s:
        if "--rollback" in sys.argv:
            rollback(s)
        elif "--verify" in sys.argv:
            report_art7(s, "current")
            cov = s.run("MATCH (c:Chunk) RETURN count(c) AS total, "
                        "count(c.provision_uid) AS tagged, "
                        "count(DISTINCT c.provision_uid) AS provisions").single()
            print(f"\n  {cov['tagged']}/{cov['total']} tagged, {cov['provisions']} provisions")
        else:
            enrich(s)
    driver.close()
    print("\nDone.")

if __name__ == "__main__":
    main()
