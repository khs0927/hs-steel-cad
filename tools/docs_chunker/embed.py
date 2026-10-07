#!/usr/bin/env python
"""Embed chunks.jsonl -> embeddings.npy + embeddings_ids.json.
Primary: sentence-transformers intfloat/multilingual-e5-small ("passage: " prefix, normalized).
Fallback (model unavailable): hashed char 2/3-gram TF vectors (dim 384), L2-normalized."""
import hashlib, json, sys
from pathlib import Path
import numpy as np

OUT = Path(__file__).resolve().parents[2] / "out" / "knowledge"
MODEL = "intfloat/multilingual-e5-small"


def fallback(texts, dim=384):
    X = np.zeros((len(texts), dim), dtype=np.float32)
    for i, t in enumerate(texts):
        t = " ".join(t.lower().split())
        for n in (2, 3):
            for j in range(len(t) - n + 1):
                h = int(hashlib.md5(t[j:j + n].encode("utf-8")).hexdigest()[:8], 16)
                X[i, h % dim] += 1.0 if (h >> 31) & 1 else -1.0
        nrm = np.linalg.norm(X[i])
        if nrm:
            X[i] /= nrm
    return X


def main():
    rows = [json.loads(l) for l in open(OUT / "chunks.jsonl", encoding="utf-8")]
    ids = [r["id"] for r in rows]
    texts = [r["text"] for r in rows]
    model_used = MODEL
    try:
        from sentence_transformers import SentenceTransformer
        m = SentenceTransformer(MODEL)
        X = m.encode(["passage: " + t for t in texts], batch_size=32, normalize_embeddings=True,
                     show_progress_bar=False).astype(np.float32)
    except Exception as e:
        print("model unavailable, using fallback:", repr(e), file=sys.stderr)
        X = fallback(texts)
        model_used = "fallback:hashed-char-ngram-384"
    np.save(OUT / "embeddings.npy", X)
    (OUT / "embeddings_ids.json").write_text(
        json.dumps({"model": model_used, "dim": int(X.shape[1]), "ids": ids}, ensure_ascii=False), encoding="utf-8")
    print(model_used, X.shape)


if __name__ == "__main__":
    main()
