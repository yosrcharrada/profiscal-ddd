"""
================================================================================
BACKFILL PAGE NUMBERS — one-time enrichment of processed_documents/*.json
================================================================================
WHY
  document_processor.py builds `full_text` by concatenating every page, then
  chunks that concatenated string — so page attribution is lost for the text
  chunks. Only table extractions (image_table / text_table), which are produced
  per-page, carry a page_number. Result: ~58% of the corpus (every `article` and
  `article_part` chunk — exactly what users search for) has page_number = null,
  so the app's "Voir le PDF" can open the right document but cannot jump to the
  right page.

WHAT
  Re-opens each source PDF and locates each page-less chunk by searching the
  per-page text for a normalised snippet of the chunk's opening. Writes
  page_number back into the JSON in place. Does NOT re-chunk, re-extract or
  re-OCR: it only ADDS a field, so it cannot change search content or chunk ids.

  Scanned pages whose text layer is empty (the ones the processor OCR'd) can't
  be matched this way and are simply left as null — the PDF still opens, just
  without a jump. Reported at the end as "unmatched".

RUN
  python backfill_page_numbers.py --docs "C:\\path\\to\\documents"
  python backfill_page_numbers.py --docs "..." --dry-run    # report only
Then re-index so Elasticsearch picks the new field up:
  python elasticsearch_indexer.py --force
================================================================================
"""

import argparse
import json
import re
import sys
from pathlib import Path

try:
    import fitz  # PyMuPDF
except ImportError:
    raise SystemExit("Run: pip install pymupdf")

PROCESSED_DIR = Path(__file__).parent / "processed_documents"
SNIPPET_CHARS = 70          # enough to be unique, short enough to survive line-wrap
MIN_SNIPPET   = 18          # below this a match is too likely to be coincidental

_ws = re.compile(r"\s+")


def norm(s: str) -> str:
    """Whitespace/­case-insensitive form — PDF text extraction line-wraps
    differently than the concatenated full_text the chunks were cut from."""
    return _ws.sub(" ", (s or "").replace(" ", " ")).strip().lower()


def build_index(pdf_path: Path):
    """[normalised text] per page, 1-indexed by position."""
    with fitz.open(pdf_path) as doc:
        return [norm(p.get_text()) for p in doc]


def candidates(content: str):
    """Snippets to try, best-anchor first.

    `_chunk_loi._split()` prepends the article's header line to EVERY part, so an
    `article_part` chunk reads "Article 5 …\n\n<the part>" — that opening never appears
    contiguously in the PDF (the header sits once, at the article's start, pages earlier).
    So: try the raw opening (correct for `article`/`section`/`preamble`), then the text
    AFTER the first line (skips the synthetic header), then a mid-chunk anchor as a last
    resort for parts whose body also starts with boilerplate.
    """
    raw = content or ""
    outs = []

    first = norm(raw)[:SNIPPET_CHARS]
    if len(first) >= MIN_SNIPPET:
        outs.append(first)

    after_header = raw.split("\n", 1)[1] if "\n" in raw else ""
    a = norm(after_header)[:SNIPPET_CHARS]
    if len(a) >= MIN_SNIPPET and a != first:
        outs.append(a)

    body = norm(after_header or raw)
    if len(body) > 240:
        mid = body[len(body) // 3:][:SNIPPET_CHARS]
        if len(mid) >= MIN_SNIPPET:
            outs.append(mid)

    return outs


def find_pdf(root: Path, filename: str, cache: dict):
    if not cache:
        for p in root.rglob("*.pdf"):
            cache.setdefault(p.name.lower(), p)
    return cache.get((filename or "").lower())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--docs", required=True, help="root folder holding the source PDFs")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    root = Path(args.docs)
    if not root.is_dir():
        raise SystemExit(f"--docs folder not found: {root}")

    files = sorted(f for f in PROCESSED_DIR.glob("*_processed.json"))
    if not files:
        raise SystemExit(f"No *_processed.json under {PROCESSED_DIR}")

    pdf_cache: dict = {}
    tot = filled = already = unmatched = no_pdf = 0

    for n, jf in enumerate(files, 1):
        try:
            chunks = json.loads(jf.read_text(encoding="utf-8"))
        except Exception as e:
            print(f"[{n}/{len(files)}] SKIP {jf.name}: unreadable ({e})")
            continue

        todo = [c for c in chunks if c.get("page_number") is None]
        already += len(chunks) - len(todo)
        tot += len(chunks)
        if not todo:
            continue

        filename = next((c.get("filename") for c in chunks if c.get("filename")), None)
        pdf = find_pdf(root, filename, pdf_cache) if filename else None
        if not pdf:
            no_pdf += len(todo)
            print(f"[{n}/{len(files)}] no PDF for {filename!r} — {len(todo)} chunk(s) left null")
            continue

        try:
            pages = build_index(pdf)
        except Exception as e:
            no_pdf += len(todo)
            print(f"[{n}/{len(files)}] SKIP {pdf.name}: {e}")
            continue

        hit = 0
        for c in todo:
            page = None
            for snip in candidates(c.get("content")):
                for i, ptext in enumerate(pages):
                    if snip in ptext:
                        page = i + 1
                        break
                if page:
                    break
            if page:
                c["page_number"] = page
                hit += 1
            else:
                unmatched += 1

        filled += hit
        if hit and not args.dry_run:
            jf.write_text(json.dumps(chunks, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"[{n}/{len(files)}] {pdf.name[:58]:<58} +{hit}/{len(todo)}")

    print("\n" + "=" * 62)
    print(f"  chunks total      : {tot}")
    print(f"  already had page  : {already}")
    print(f"  page filled in    : {filled}")
    print(f"  unmatched (OCR'd) : {unmatched}")
    print(f"  no source PDF     : {no_pdf}")
    if args.dry_run:
        print("  DRY RUN — nothing written")
    else:
        print("  Written. Next: python elasticsearch_indexer.py --force")
    print("=" * 62)


if __name__ == "__main__":
    sys.exit(main())
