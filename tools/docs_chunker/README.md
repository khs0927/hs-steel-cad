# docs_chunker
Chunks legacy HS-STEEL reference docs (PDF/XLSX/XLSM/DCL under C:\HS-STEEL) for retrieval.

- `python chunk.py` -> `out/knowledge/chunks.jsonl` (id, source, sha256, page_or_sheet, kind, text; ~500-900 chars, 100 overlap, deterministic ids) and `chunks_report.json` (pages without text; no OCR).
- `python embed.py` -> `embeddings.npy` (row order = `embeddings_ids.json`.ids), model `intfloat/multilingual-e5-small` (prefix `passage: `; queries use `query: `). Falls back to hashed char n-gram vectors if the model is unavailable.
- Deps: pypdf, openpyxl, numpy, sentence-transformers.
- `python content_gaps.py` (after chunk.py, before embed.py) -> merges kinds `vba` (oletools, read-only), `xlsx_formula` (formula text grouped by column pattern in R1C1 + named ranges) and `pdf_ocr` (`ocr.ps1`, Windows.Media.Ocr ko; pages with <40 text chars; blank pages recorded as `blank_page`, no engine -> `ocr_unavailable`) into chunks.jsonl; writes `vba_procedures.jsonl` (-> table `vba_procedure` at build-db) and `content_gaps_report.json`. Deps: oletools, pymupdf.
