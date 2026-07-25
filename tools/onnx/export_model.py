"""
export_model.py — produce the ONNX embedding model used by the .NET backend.

The platform embeds search queries with `sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2`,
the same model that produced the `chunk_embeddings` vectors stored in Neo4j. That model used to be
served by a Python sidecar (embed_server.py); it now runs in-process in .NET via ONNX Runtime
(see Profiscal.Infrastructure/Embeddings/OnnxEmbedder.cs).

This script exports the encoder to ONNX and copies the tokenizer next to it. Run it ONCE per
model version — the output is a build artefact, not source, and is git-ignored (~450 MB, over
GitHub's file limit). Copy the two output files between machines instead of re-exporting on a
network where huggingface.co is blocked.

    python tools/onnx/export_model.py [--model <path-or-hf-id>] [--out <dir>]

Defaults:
    --model  the local model_cache snapshot if present, else the HF id (requires network)
    --out    backend/Profiscal.Application/models/embedding

Outputs:
    model.onnx       the transformer encoder (input_ids, attention_mask -> last_hidden_state)
    tokenizer.json   the HuggingFace tokenizer spec the C# tokenizer reads

Requires: torch, transformers, onnx  (pip install torch transformers onnx)

NOTE — pooling and normalisation are NOT baked into the graph. The C# side applies
mean-pooling over the attention mask then L2-normalisation, matching
SentenceTransformer.encode(..., normalize_embeddings=True). Keeping them in C# keeps the graph
simple and lets the batch/padding handling live with the code that owns it.
"""
from __future__ import annotations

import argparse
import glob
import os
import shutil
import sys

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
HF_ID = "sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2"
DEFAULT_OUT = os.path.join(REPO_ROOT, "backend", "Profiscal.Application", "models", "embedding")


def find_local_snapshot() -> str | None:
    """Prefer the model already on disk (model_cache/) so the export works offline."""
    pattern = os.path.join(
        REPO_ROOT, "model_cache",
        "models--sentence-transformers--paraphrase-multilingual-MiniLM-L12-v2",
        "snapshots", "*",
    )
    snaps = [p for p in glob.glob(pattern) if os.path.isdir(p)]
    return snaps[0] if snaps else None


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", default=None, help="local model dir or HuggingFace id")
    ap.add_argument("--out", default=DEFAULT_OUT, help="output directory")
    args = ap.parse_args()

    source = args.model or find_local_snapshot() or HF_ID
    os.makedirs(args.out, exist_ok=True)
    print(f"[export] source : {source}")
    print(f"[export] output : {args.out}")

    try:
        import torch
        from transformers import AutoModel, AutoTokenizer
    except ImportError as exc:
        print(f"[export] missing dependency: {exc}\n"
              f"         pip install torch transformers onnx", file=sys.stderr)
        return 1

    tokenizer = AutoTokenizer.from_pretrained(source)
    model = AutoModel.from_pretrained(source).eval()

    sample = tokenizer(["texte de test"], padding=True, truncation=True,
                       max_length=512, return_tensors="pt")
    onnx_path = os.path.join(args.out, "model.onnx")

    # Only input_ids + attention_mask are exported: this is a single-sentence encoder, so
    # token_type_ids are all zeros and the C# side never needs to supply them.
    torch.onnx.export(
        model,
        (sample["input_ids"], sample["attention_mask"]),
        onnx_path,
        input_names=["input_ids", "attention_mask"],
        output_names=["last_hidden_state"],
        dynamic_axes={
            "input_ids":         {0: "batch", 1: "sequence"},
            "attention_mask":    {0: "batch", 1: "sequence"},
            "last_hidden_state": {0: "batch", 1: "sequence"},
        },
        opset_version=14,
    )
    print(f"[export] model.onnx     {os.path.getsize(onnx_path) // 1024 // 1024} MB")

    # The C# tokenizer reads tokenizer.json directly (vocabulary + Unigram log-probs).
    src_tok = os.path.join(source, "tokenizer.json")
    dst_tok = os.path.join(args.out, "tokenizer.json")
    if os.path.isfile(src_tok):
        shutil.copyfile(src_tok, dst_tok)
    else:
        tokenizer.save_pretrained(args.out)   # HF id case: materialise it
    print(f"[export] tokenizer.json {os.path.getsize(dst_tok) // 1024} KB")

    print("[export] done — restart the API to pick up the new model.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
