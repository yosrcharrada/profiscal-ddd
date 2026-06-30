# ProFiscal — taxmind graph migration

This folder is a **copy** of `ProFiscal-Integrated`, re-pointed from the old flat
`tunisian-fiscal` graph onto the new **`taxmind`** knowledge graph. The original folder is
untouched — run either version (one at a time; both use Neo4j on `:7687` and embed server on `:8081`).

## What changed (and only what changed)

| File | Change |
|------|--------|
| `backend/.../Agents/RetrievalAgent.cs` | Rewritten for taxmind ontology. RETURN projections map taxmind props → the same aliases (`id/text/doc_name/doc_type/article_ref`) so `TryAdd`/`MapChunks` are unchanged. |
| `backend/.../Retrieval/FiscalRetrievalPolicy.cs` | Rule `DocFragment`s repointed to taxmind `doc_id`s (`code_irpp_is`, `code_tva`, `NC_2015_03`). |
| `backend/Profiscal.API/Fiscal/Neo4jSearchAgent.cs` | Now uses taxmind's native **BM25 full-text index `chunk_content`** (real ranking, no Elasticsearch). CONTAINS fallback kept. |
| `backend/Profiscal.API/Program.cs` | `ISearchAgent` → `Neo4jSearchAgent` (was `ElasticsearchSearchAgent`). |
| `backend/Profiscal.API/appsettings*.json` | `Neo4j:Database` → `taxmind`. |
| `embed_server.py` | **384-dim** model, taxmind property projection (`c.content`/`c.doc_id`/`c.article_display`, corpus-derived doc_type), `doc_id` filter, dim-mismatch warning. |
| `.env` | `NEO4J_DATABASE=taxmind`, `EMBED_MODEL`, `EMBED_DIM=384`. |

### Schema mapping (old → taxmind)
`c.text`→`c.content` · `c.doc_name`→`c.doc_id` · `c.doc_type`→derived from `c.corpus` ·
`c.article_ref`→`c.article_display`/`c.article_number` · `chunk_type='text'`→`content<>''` ·
`NEXT_CHUNK`→`NEXT` · entity/`APPEARS_IN`→`SAME_TOPIC`/`HAS_TOPIC` · citation expansion→`SIMILAR_TO`
(`CITES` goes Chunk→LegalReference, not Chunk→Chunk). Embeddings **768-dim → 384-dim**.

All new Cypher was tested live against the `taxmind` DB before committing. The rate problem is
now directly solvable: **CIRPPIS Art. 52 is a first-class `article` chunk** (`article_number='52'`),
fetched precisely by the rule policy + `FetchTargetedAsync`.

## ⚠️ The ONE thing you must confirm: the embedding model

taxmind's `chunk_embeddings` index is **384-dim**. Semantic search only works if the embed server
uses the **exact same model that built the graph**. Default is `paraphrase-multilingual-MiniLM-L12-v2`.
If that's not what built it, set `EMBED_MODEL` in `.env` to the right one (e.g.
`intfloat/multilingual-e5-small`).

**Quick check it's correct:** start the embed server, then POST a phrase copied verbatim from a
chunk's text to `/embed_search`. If the model matches, that exact chunk comes back with a score
near `1.0`. Low/zero top scores = wrong model.

> Note: BM25 legal search (`chunk_content`) works regardless of the embed model — it's pure Lucene.
> So generation/search degrade gracefully even before the embed model is confirmed.

## Run order (unchanged)
1. Start the `lastdb` Neo4j instance (database `taxmind`).
2. `python embed_server.py` (uses the 384-dim model).
3. `dotnet run --project backend/Profiscal.API`.
