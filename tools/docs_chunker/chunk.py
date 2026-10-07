#!/usr/bin/env python
"""Chunk legacy HS-STEEL reference docs into out/knowledge/chunks.jsonl (offline, deterministic)."""
import hashlib, json, os, sys
from pathlib import Path

ROOT = Path(r"C:\HS-STEEL")
OUT = Path(__file__).resolve().parents[2] / "out" / "knowledge"
TARGET, MAXLEN, OVERLAP = 700, 900, 100


def sha256_file(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""):
            h.update(b)
    return h.hexdigest()


def split(text):
    text = "\n".join(l.rstrip() for l in text.replace("\r", "").split("\n"))
    text = "\n".join(l for l in text.split("\n") if l.strip())
    if not text.strip():
        return []
    if len(text) <= MAXLEN:
        return [text]
    out, i = [], 0
    while i < len(text):
        end = min(i + TARGET, len(text))
        if end < len(text):
            # prefer newline/space break between 500 and 900
            window = text[i + 500:min(i + MAXLEN, len(text))]
            k = max(window.rfind("\n"), window.rfind(". "), window.rfind(" "))
            end = i + 500 + k + 1 if k > 0 else min(i + MAXLEN, len(text))
        out.append(text[i:end])
        if end >= len(text):
            break
        i = end - OVERLAP
    return out


def pdf_units(p, report):
    from pypdf import PdfReader
    r = PdfReader(str(p))
    for n, pg in enumerate(r.pages, 1):
        try:
            t = pg.extract_text() or ""
        except Exception as e:
            t = ""
            report["errors"].append({"source": rel(p), "page": n, "error": str(e)})
        if not t.strip():
            report["pages_without_text"].append({"source": rel(p), "page": n})
        yield str(n), t


def xlsx_units(p, report):
    import openpyxl
    for data_only in (False,):
        wb = openpyxl.load_workbook(str(p), data_only=False, keep_vba=False)
        names = []
        try:
            dn = wb.defined_names
            items = dn.items() if hasattr(dn, "items") else [(d.name, d) for d in dn.definedName]
            names = [f"{k} = {v.attr_text}" for k, v in items]
        except Exception:
            pass
        if names:
            yield "[named ranges]", "Named ranges:\n" + "\n".join(sorted(names))
        for ws in wb.worksheets:
            lines = [f"Sheet: {ws.title} (dims {ws.dimensions})"]
            for row in ws.iter_rows():
                cells = []
                for c in row:
                    v = c.value
                    if v is None or (isinstance(v, str) and not v.strip()):
                        continue
                    cells.append(f"{c.coordinate}={v}")
                if cells:
                    lines.append(" | ".join(cells))
            yield ws.title, "\n".join(lines)


def dcl_units(p, report):
    raw = p.read_bytes()
    try:
        t = raw.decode("cp949")
    except UnicodeDecodeError:
        t = raw.decode("cp949", errors="replace")
    yield "1", t


def rel(p):
    return str(Path(p).relative_to(ROOT)).replace("\\", "/")


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    files = []
    for p in (ROOT / "HSSTEEL" / "도움말").glob("*"):
        if p.suffix.lower() in (".pdf", ".xlsx"):
            files.append((p, p.suffix.lower()[1:]))
    for d in ("support", "Icons"):
        for p in (ROOT / "HSSTEEL" / d).rglob("*"):
            if p.suffix.lower() == ".dcl":
                files.append((p, "dcl"))
    for p in (ROOT / "HS03").rglob("*"):
        if p.suffix.lower() in (".xlsx", ".xlsm") and not p.name.startswith("~$"):
            files.append((p, "xlsx"))
    files.sort(key=lambda x: rel(x[0]))
    report = {"pages_without_text": [], "errors": [], "per_source": {}, "images_skipped": []}
    for p in (ROOT / "HSSTEEL" / "도움말").glob("*"):
        if p.suffix.lower() in (".jpg", ".jpeg", ".png"):
            report["images_skipped"].append(rel(p))
    recs = []
    for p, kind in files:
        src = rel(p)
        sha = sha256_file(p)
        fn = {"pdf": pdf_units, "xlsx": xlsx_units, "dcl": dcl_units}[kind]
        n = 0
        try:
            for unit, text in fn(p, report):
                for i, ch in enumerate(split(text)):
                    cid = hashlib.sha256(f"{src}|{unit}|{i}".encode("utf-8")).hexdigest()[:16]
                    recs.append({"id": cid, "source": src, "sha256": sha, "page_or_sheet": unit,
                                 "kind": kind, "text": ch})
                    n += 1
        except Exception as e:
            report["errors"].append({"source": src, "error": repr(e)})
        report["per_source"][src] = n
    recs.sort(key=lambda r: (r["source"], r["page_or_sheet"].zfill(6), r["id"]))
    with open(OUT / "chunks.jsonl", "w", encoding="utf-8", newline="\n") as f:
        for r in recs:
            f.write(json.dumps(r, ensure_ascii=False) + "\n")
    report["total_chunks"] = len(recs)
    (OUT / "chunks_report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=1))


if __name__ == "__main__":
    main()
