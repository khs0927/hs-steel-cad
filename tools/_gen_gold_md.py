# -*- coding: utf-8 -*-
from pathlib import Path
import json
g = json.loads(Path(r"C:\code\hs-steel-cad\out\gold\work_project_summary.json").read_text(encoding="utf-8"))
bolts = {k: v for k, v in g["insert_blocks_top"].items() if "BOLT" in k.upper()}
lines = [
"# GOLD_SUMMARIES (numeric only)",
"",
"**Policy:** Proprietary DWG/xlsm contents are **not** committed. Only counts and redacted summaries land in-repo.",
"",
"## Work project DWG (redacted)",
"",
"| Metric | Value |",
"|---|---|",
"| Source (redacted) | `work_project_redacted_2026-05-19.dwg` |",
f"| Size | {g['source_size_bytes']:,} bytes |",
f"| CAD version | {g['cad_version']} |",
f"| Model entities | {g['model_entity_count']:,} |",
f"| Layers | {g['layer_count']} |",
f"| Block definitions | {g['block_def_count']} |",
"",
"### Entity type counts (top)",
"",
"| Type | Count |",
"|---|---:|",
]
for k, v in list(g["entity_types_top"].items())[:12]:
    lines.append(f"| {k} | {v} |")
lines += ["", "### High-strength bolt block inserts (counts)", "", "| Block | Inserts |", "|---|---:|"]
for k, v in bolts.items():
    lines.append(f"| {k} | {v} |")
lines += ["", "### Layer entity concentration (top 10)", "", "| Layer | Entities |", "|---|---:|"]
for k, v in list(g["layers_top_by_entities"].items())[:10]:
    lines.append(f"| {k} | {v} |")
lines += [
"",
"### Notes",
"",
"- Machine-local JSON (gitignored `out/`): `out/gold/work_project_summary.json` (redacted basename).",
"- Member/BOM mark extraction from XData was weak via text heuristic; bolt insert counts are the strongest connection-density signal.",
"- Do **not** add the original DWG or company/project title to git.",
"- Extractor: `tools/GoldDwgSummary/` (dev utility).",
"",
]
Path(r"C:\code\hs-steel-cad\docs\GOLD_SUMMARIES.md").write_text("\n".join(lines), encoding="utf-8")
print("rewrote GOLD_SUMMARIES")
