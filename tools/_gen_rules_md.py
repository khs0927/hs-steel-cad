# -*- coding: utf-8 -*-
import json
from pathlib import Path
from collections import Counter
ROOT = Path(r"C:\code\hs-steel-cad")
rules = json.loads((ROOT / "src/HsSteel.Knowledge/Rules/rules.json").read_text(encoding="utf-8"))["rules"]
cats = Counter(r["category"] for r in rules)
eng = sum(1 for r in rules if r.get("engine_refs"))
lines = []
lines.append("# RULES_CATALOG")
lines.append("")
lines.append(f"**Count:** {len(rules)} evidence-backed rules (schema `hs-steel-rules/1`).")
lines.append("**Machine source:** `src/HsSteel.Knowledge/Rules/rules.json` + `RulesCatalog.cs`.")
lines.append(f"**Engine cross-check:** {eng}/{len(rules)} rules cite `engine_refs` paths under Domain/Modeling/Drafting/Mcp.")
lines.append("")
lines.append("## Categories")
lines.append("")
lines.append("| Category | Count |")
lines.append("|---|---:|")
for k, v in sorted(cats.items(), key=lambda x: -x[1]):
    lines.append(f"| {k} | {v} |")
lines.append("")
lines.append("## Sources")
lines.append("")
lines.append("- `C:\\HS-STEEL\\HSSTEEL\\attributes\\Project.dat` (PD-*)")
lines.append("- `src/HsSteel.Domain/{Model,Fabrication,Connections}.cs` (DR/WL/BT/GG/SP/AS/SEC)")
lines.append("- `src/HsSteel.Modeling/ShearTabLayout.cs` + `docs/SHEAR-TAB-ROWS.md` (ST-*)")
lines.append("- `docs/{ARCHITECTURE,COLLAB,REBORN_LEDGER,D4_RESULTS}.md` (RB/ARCH/DRF)")
lines.append("- REBORN derived specs (bom, cut plan, trust grades)")
lines.append("")
lines.append("## Cross-check notes")
lines.append("")
lines.append("- `DetailRules.From` reads SCALLOP/ENDGAGE/SHOLE/WDGAP/SWS; other Project.dat keys are catalogued for drafting/BOM parity but not all wired into `DetailRules` yet (intentional gap).")
lines.append("- Legacy Project.dat `SWS=SS400` vs engine default `SS275` called out in DR-005 / PD-010.")
lines.append("- Shear-tab / fillet / hole rules cite KDS 14 31 25 / AISC tables as external evidence.")
lines.append("- VLX behaviour is **not** claimed as rules; RB-006 states clean-room policy.")
lines.append("")
lines.append("## Full index")
lines.append("")
lines.append("| ID | Category | Title | Engine refs |")
lines.append("|---|---|---|---|")
for r in rules:
    er = ", ".join(r.get("engine_refs") or []) or "—"
    title = r["title"].replace("|", "/")
    lines.append("| {} | {} | {} | {} |".format(r["id"], r["category"], title, er))
lines.append("")
lines.append("## Statements (compact)")
lines.append("")
for r in rules:
    lines.append("### {} — {}".format(r["id"], r["title"]))
    lines.append("")
    lines.append(r["statement"])
    lines.append("")
    for e in r["evidence"]:
        lines.append("- evidence: `{}` — {}".format(e["source"], e["note"]))
    lines.append("")
(ROOT / "docs/RULES_CATALOG.md").write_text("\n".join(lines), encoding="utf-8")
print("RULES_CATALOG.md", len(rules), "engine", eng)
