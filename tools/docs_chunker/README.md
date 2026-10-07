# docs_chunker
Chunks legacy HS-STEEL reference docs (PDF/XLSX/XLSM/DCL under C:\HS-STEEL) for retrieval.

- `python chunk.py` -> `out/knowledge/chunks.jsonl` (id, source, sha256, page_or_sheet, kind, text; ~500-900 chars, 100 overlap, deterministic ids) and `chunks_report.json` (pages without text; no OCR).
- `python embed.py` -> `embeddings.npy` (row order = `embeddings_ids.json`.ids), model `intfloat/multilingual-e5-small` (prefix `passage: `; queries use `query: `). Falls back to hashed char n-gram vectors if the model is unavailable.
- Deps: pypdf, openpyxl, numpy, sentence-transformers.
