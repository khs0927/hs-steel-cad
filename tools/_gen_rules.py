# -*- coding: utf-8 -*-
"""Generate rules.json (>=80 evidence-backed rules) for hs-steel-cad overnight work."""
import json
from pathlib import Path

ROOT = Path(r"C:\code\hs-steel-cad")
rules = []

def R(rid, title, statement, category, evidence, engine=None, tags=None, value=None):
    rules.append({
        "id": rid,
        "title": title,
        "statement": statement,
        "category": category,
        "evidence": evidence,  # list of {source, note}
        "engine_refs": engine or [],
        "tags": tags or [],
        "value": value,
    })

# --- Project.dat keys (defaults from C:\HS-STEEL\HSSTEEL\attributes\Project.dat) ---
proj = [
    ("WDGAP", 5, "Weld fit-up gap (mm)", "detailing"),
    ("SCALLOP", 30, "Flange scallop/cope radius (mm)", "fabrication"),
    ("RCCR", 30, "Column corner radius / related clearance (mm)", "fabrication"),
    ("SHOLE", 22, "Default bolt hole diameter (mm)", "bolting"),
    ("ENDGAGE", 40, "End distance to first bolt row (mm)", "bolting"),
    ("HOLELEN", 60, "Default slotted/long hole length related (mm)", "bolting"),
    ("stffsqgage", "60", "Stiffener square gauge (mm)", "fabrication"),
    ("STFFGAP", 0, "Stiffener gap toggle/value", "fabrication"),
    ("STFFIN", 0, "Stiffener inset toggle/value", "fabrication"),
    ("SWS", "SS400", "Default steel grade (legacy SS400; engine maps toward SS275)", "materials"),
    ("WELDTIP", "AWS_EXXXX_GRADE", "Weld tip/electrode grade placeholder", "welding"),
    ("PAINT", "SEE_PAINT_SPEC.", "Paint specification note", "finishing"),
    ("SET_COL_STFF_JBX", 75, "Column stiffener J-box related size", "fabrication"),
    ("SET_ASY_BOMLEN", 73, "Assembly BOM length field width/setting", "bom"),
    ("hs-dimrnd-box", "0.5", "Dimension round-off (mm)", "drafting"),
    ("m32-dim-txt-box", 3, "Dimension text height (paper mm)", "drafting"),
    ("m32-marktxt-box", 1.6, "Mark text height (paper mm)", "drafting"),
    ("m32-gridasy-box", 5, "Assembly grid text size", "drafting"),
    ("m32-griddan-box", 4, "Part grid text size", "drafting"),
    ("m32-bom-txt-box", 1.5, "BOM table text height", "drafting"),
    ("m32-splise-gap-box", "5", "Splice gap (mm)", "connections"),
    ("m32-share1-gap-box", "5", "Shear connection gap setting 1 (mm)", "connections"),
    ("m32-share2-gap-box", "5", "Shear connection gap setting 2 (mm)", "connections"),
    ("m32-pipejun-ang-box", "3", "Pipe junction angle tolerance", "connections"),
    ("m32-pipejun-las-123", "3", "Pipe junction LAS setting", "connections"),
    ("m32-dimtad-pop", "2", "DIMTAD dimension text above-line pop index", "drafting"),
    ("m32-dimdec-pop", "1", "DIMDEC decimal places pop index", "drafting"),
    ("m32-dimlunit-pop", "1", "DIMLUNIT linear units pop index", "drafting"),
    ("m32-gitabolt-pop", "3", "Gita/secondary bolt pop index", "bolting"),
    ("m32-spgpbolt-pop", "0", "Splice group bolt pop index", "bolting"),
    ("set-bom-matl-elss-tog", "1", "BOM material ELSS toggle on", "bom"),
]
for i, (k, v, desc, cat) in enumerate(proj, 1):
    R(f"PD-{i:03d}", f"Project.dat {k}",
      f"Default project setting {k} = {v}: {desc}.",
      cat,
      [{"source": "C:\\HS-STEEL\\HSSTEEL\\attributes\\Project.dat", "note": f'("{k}" {json.dumps(v) if isinstance(v,str) else v})'}],
      ["src/HsSteel.Domain/Model.cs:DetailRules.From"] if k in ("SCALLOP","ENDGAGE","SHOLE","WDGAP","SWS") else [],
      ["project.dat", k.lower()],
      v)

# DetailRules engine defaults
R("DR-001", "DetailRules scallop default 30",
  "When Project.dat is missing SCALLOP, DetailRules.Scallop defaults to 30 mm.",
  "fabrication",
  [{"source": "src/HsSteel.Domain/Model.cs", "note": "DetailRules record default Scallop=30"}],
  ["src/HsSteel.Domain/Model.cs"], ["detailrules"], 30)
R("DR-002", "DetailRules end gauge default 40",
  "EndGauge defaults to 40 mm (ENDGAGE).",
  "bolting",
  [{"source": "src/HsSteel.Domain/Model.cs", "note": "EndGauge=40"}],
  ["src/HsSteel.Domain/Model.cs"], ["detailrules"], 40)
R("DR-003", "DetailRules hole dia default 22",
  "HoleDia defaults to 22 mm (SHOLE) matching M20 + 2.",
  "bolting",
  [{"source": "src/HsSteel.Domain/Model.cs", "note": "HoleDia=22"},
   {"source": "src/HsSteel.Domain/Fabrication.cs", "note": "Bolts.HoleFor: bolt+2 up to M22"}],
  ["src/HsSteel.Domain/Model.cs", "src/HsSteel.Domain/Fabrication.cs"], ["detailrules"], 22)
R("DR-004", "DetailRules weld gap default 5",
  "WeldGap defaults to 5 mm (WDGAP).",
  "welding",
  [{"source": "src/HsSteel.Domain/Model.cs", "note": "WeldGap=5"}],
  ["src/HsSteel.Domain/Model.cs"], ["detailrules"], 5)
R("DR-005", "DetailRules material default SS275",
  "Engine default Material is SS275 when SWS missing (legacy Project.dat often SS400).",
  "materials",
  [{"source": "src/HsSteel.Domain/Model.cs", "note": "Material default SS275; Project.dat SWS=SS400"}],
  ["src/HsSteel.Domain/Model.cs"], ["detailrules", "grade"], "SS275")
R("DR-006", "DetailRules connection gap default 10",
  "ConnectionGap (beam end to supporting web clearance) defaults to 10 mm.",
  "connections",
  [{"source": "src/HsSteel.Domain/Model.cs", "note": "ConnectionGap=10"}],
  ["src/HsSteel.Domain/Model.cs"], ["detailrules"], 10)
R("DR-007", "DetailRules text height 2.5 / dim gap 7",
  "Paper text height 2.5 mm and dimension row gap 7 mm are drafting defaults.",
  "drafting",
  [{"source": "src/HsSteel.Domain/Model.cs", "note": "TextHeight=2.5 DimGap=7"}],
  ["src/HsSteel.Domain/Model.cs"], ["detailrules"], {"text": 2.5, "dim_gap": 7})

# Weld rules
for up, leg in [(6,3),(12,5),(20,6),(999,8)]:
    R(f"WL-{up}", f"Min fillet leg for t≤{up if up<999 else '>20'}",
      f"Minimum fillet weld leg is {leg} mm when thicker part thickness ≤ {up if up<999 else '∞'} mm (capped by thinner part).",
      "welding",
      [{"source": "src/HsSteel.Domain/Fabrication.cs", "note": "WeldRules.MinFillet KDS 14 31 25 / AWS D1.1 Table 5.7"},
       {"source": "KDS 14 31 25", "note": "min fillet by thicker part"}],
      ["src/HsSteel.Domain/Fabrication.cs:WeldRules"], ["fillet", "kds"], {"upto": up, "min_leg": leg})

R("WL-CAP", "Fillet leg capped by thinner part",
  "FilletLeg(t1,t2) = min(table_min(max(t1,t2)), min(t1,t2)).",
  "welding",
  [{"source": "src/HsSteel.Domain/Fabrication.cs", "note": "WeldRules.FilletLeg"}],
  ["src/HsSteel.Domain/Fabrication.cs"], ["fillet"])

# Bolt hole / grade
R("BT-001", "Standard hole bolt+2 (≤M22)",
  "Standard hole diameter = bolt diameter + 2 mm for bolts up to M22.",
  "bolting",
  [{"source": "src/HsSteel.Domain/Fabrication.cs", "note": "Bolts.HoleFor"},
   {"source": "Project.dat SHOLE", "note": "22 for M20"}],
  ["src/HsSteel.Domain/Fabrication.cs:Bolts.HoleFor"], ["hole"])
R("BT-002", "Standard hole bolt+3 (≥M24)",
  "Standard hole diameter = bolt diameter + 3 mm for bolts M24 and larger.",
  "bolting",
  [{"source": "src/HsSteel.Domain/Fabrication.cs", "note": "Bolts.HoleFor boltDia>=24"}],
  ["src/HsSteel.Domain/Fabrication.cs"], ["hole"])
R("BT-003", "TS / S10T grade mapping",
  "Bolt names starting with TS or containing S10T map to grade S10T (KS B 2819).",
  "bolting",
  [{"source": "src/HsSteel.Domain/Fabrication.cs", "note": "Bolts.GradeOf"}],
  ["src/HsSteel.Domain/Fabrication.cs"], ["grade", "s10t"])
R("BT-004", "HTB / F10T grade mapping",
  "HTB / F10T names map to F10T (KS B 1010); F8T preserved as F8T.",
  "bolting",
  [{"source": "src/HsSteel.Domain/Fabrication.cs", "note": "Bolts.GradeOf"}],
  ["src/HsSteel.Domain/Fabrication.cs"], ["grade", "f10t"])
R("BT-005", "Ordinary M-BOLT grade",
  "Names starting with M / SB or containing 4.6 map to M-BOLT.",
  "bolting",
  [{"source": "src/HsSteel.Domain/Fabrication.cs", "note": "Bolts.GradeOf"}],
  ["src/HsSteel.Domain/Fabrication.cs"], ["grade"])
R("BT-006", "Unknown bolt name defaults F10T",
  "Unrecognized bolt names default to F10T.",
  "bolting",
  [{"source": "src/HsSteel.Domain/Fabrication.cs", "note": "GradeOf return F10T"}],
  ["src/HsSteel.Domain/Fabrication.cs"], ["grade"])

# Gauge tables (sample + rule)
R("GG-001", "Angle gauge AIJ/KS table",
  "Angle leg gauges follow AIJ/KS table (e.g. L90→g1=50 max M24; L150→g1=55 g2=55).",
  "bolting",
  [{"source": "src/HsSteel.Domain/Fabrication.cs", "note": "BoltGauges.Angle"}],
  ["src/HsSteel.Domain/Fabrication.cs:BoltGauges"], ["gauge", "angle"])
R("GG-002", "H/T flange gauge table",
  "Flange gauge between bolt lines: W200→120, W250→150, W300→150, W350→140.",
  "bolting",
  [{"source": "src/HsSteel.Domain/Fabrication.cs", "note": "BoltGauges.Flange"}],
  ["src/HsSteel.Domain/Fabrication.cs"], ["gauge", "flange"])
R("GG-003", "Minimum edge distance practice 1.5d / 20",
  "BoltGauges.For uses e = max(1.5*bolt, 20) mm as minimum edge distance practice.",
  "bolting",
  [{"source": "src/HsSteel.Domain/Fabrication.cs", "note": "BoltGauges.For e=max(1.5*bolt,20)"}],
  ["src/HsSteel.Domain/Fabrication.cs"], ["edge"])
R("GG-004", "Default bolt by profile family",
  "DefaultBolt: L by angle max; lip/Z 12 or 16; flat by depth; box/pipe by size; channel/H by depth bands.",
  "bolting",
  [{"source": "src/HsSteel.Domain/Fabrication.cs", "note": "BoltGauges.DefaultBolt"}],
  ["src/HsSteel.Domain/Fabrication.cs"], ["default-bolt"])
R("GG-005", "I/Channel web pitch 3d to 10mm",
  "For I/Channel end connections, web pitch = ceil(3d/10)*10, rows capped at 6.",
  "bolting",
  [{"source": "src/HsSteel.Domain/Fabrication.cs", "note": "BoltGauges.For ShapeKind.I/Channel"}],
  ["src/HsSteel.Domain/Fabrication.cs"], ["pitch", "web"])

# Shear tab
R("ST-001", "Clear web T=D-2(tf+r)",
  "Shear-tab plate must fit clear web depth T = D − 2(tf + r).",
  "connections",
  [{"source": "docs/SHEAR-TAB-ROWS.md", "note": "clear web rule"},
   {"source": "src/HsSteel.Modeling/ShearTabLayout.cs", "note": "ClearWebDepth"}],
  ["src/HsSteel.Modeling/ShearTabLayout.cs"], ["shear-tab"])
R("ST-002", "Shop edge distance 40 mm",
  "Vertical edge distance e ≥ max(40, MinEdgeSheared(d)).",
  "connections",
  [{"source": "docs/SHEAR-TAB-ROWS.md", "note": "ShopEdge=40"},
   {"source": "KDS 14 31 25", "note": "sheared edge table"}],
  ["src/HsSteel.Modeling/ShearTabLayout.cs"], ["shear-tab", "edge"], 40)
R("ST-003", "Shop pitch 70 mm / 3d",
  "Pitch p ≥ max(70, ceil(3d/5)*5).",
  "connections",
  [{"source": "docs/SHEAR-TAB-ROWS.md", "note": "ShopPitch=70 preferred 3d"}],
  ["src/HsSteel.Modeling/ShearTabLayout.cs"], ["shear-tab", "pitch"], 70)
R("ST-004", "Sheared edge M20 = 34 mm",
  "MinEdgeSheared(M20)=34, M16=28, M22=38, M24=42, M27=48, M30=52; >M30 → 1.75d.",
  "connections",
  [{"source": "src/HsSteel.Modeling/ShearTabLayout.cs", "note": "MinEdgeSheared"},
   {"source": "AISC 360-05 Table J3.4M", "note": "same as KDS"}],
  ["src/HsSteel.Modeling/ShearTabLayout.cs"], ["edge", "kds"])
R("ST-005", "Rows n=floor((T-2e)/p)+1",
  "Bolt rows n = ⌊(T−2e)/p⌋+1; plate height 2e+(n−1)p ≤ T, centred.",
  "connections",
  [{"source": "docs/SHEAR-TAB-ROWS.md", "note": "row formula"}],
  ["src/HsSteel.Modeling/ShearTabLayout.cs"], ["shear-tab"])
R("ST-006", "n<2 flagged",
  "Fewer than 2 rows flags warning (AISC Manual Part 10 conventional 2–12).",
  "connections",
  [{"source": "docs/SHEAR-TAB-ROWS.md", "note": "n<2 warning"}],
  ["src/HsSteel.Modeling/ShearTabLayout.cs"], ["shear-tab", "warning"])
R("ST-007", "Prefer splice-standard WebY rows",
  "ModelBuilder.ShearTab prefers SpliceStandards.WebY when present; else ShearTabLayout.Default.",
  "connections",
  [{"source": "docs/SHEAR-TAB-ROWS.md", "note": "splice override"}],
  ["src/HsSteel.Modeling/ModelBuilder.cs", "src/HsSteel.Modeling/ShearTabLayout.cs"], ["shear-tab", "scss"])

# Splice / bolt pattern
R("SP-001", "BoltPattern grammar",
  "Plate/bolt string: thicknesses (*T)*, then X<axis>, Y<axis>, D<dia> in any order.",
  "connections",
  [{"source": "src/HsSteel.Domain/Connections.cs", "note": "BoltPattern.Parse"}],
  ["src/HsSteel.Domain/Connections.cs"], ["bolt-pattern"])
R("SP-002", "BoltAxis nAp group",
  "Axis term nAp means n intervals of pitch p → n+1 holes; plain terms are edges/gaps.",
  "connections",
  [{"source": "src/HsSteel.Domain/Connections.cs", "note": "BoltAxis.Parse"}],
  ["src/HsSteel.Domain/Connections.cs"], ["bolt-axis"])
R("SP-003", "HoleDia = BoltDia+2 in pattern",
  "BoltPattern.HoleDia returns BoltDia+2 when BoltDia>0.",
  "bolting",
  [{"source": "src/HsSteel.Domain/Connections.cs", "note": "BoltPattern.HoleDia"}],
  ["src/HsSteel.Domain/Connections.cs"], ["hole"])
R("SP-004", "SCSS S1–S10 web plate fields",
  "SCSS row: S1 web t; S2–S5 web X; S6–S9 web Y; S10 web bolt dia.",
  "connections",
  [{"source": "src/HsSteel.Domain/Connections.cs", "note": "SpliceSpec remarks"},
   {"source": "HSSTEEL/attributes/SCSS-*.dat", "note": "6 tables 175 rows"}],
  ["src/HsSteel.Domain/Connections.cs:SpliceSpec"], ["scss"])
R("SP-005", "SCSS S11–S21 flange fields",
  "S11/S12 flange plate t; S13 flange bolt dia; S14–S17 flange X; S18 gauge; S19 second gauge; S20–S21 edges.",
  "connections",
  [{"source": "src/HsSteel.Domain/Connections.cs", "note": "SpliceSpec"}],
  ["src/HsSteel.Domain/Connections.cs"], ["scss"])
R("SP-006", "SCSS S22 splice gap",
  "S22 is splice gap (typically 5 mm; Project.dat m32-splise-gap-box).",
  "connections",
  [{"source": "src/HsSteel.Domain/Connections.cs", "note": "Gap => N(22)"},
   {"source": "Project.dat", "note": "m32-splise-gap-box 5"}],
  ["src/HsSteel.Domain/Connections.cs"], ["scss", "gap"], 5)
R("SP-007", "Flange lines from S23",
  "FlangeLines = max(2, FlangeBoltTotal/(4*FlangeX.Count)); 2 or 4 lines across flange.",
  "connections",
  [{"source": "src/HsSteel.Domain/Connections.cs", "note": "FlangeLines"}],
  ["src/HsSteel.Domain/Connections.cs"], ["scss"])
R("SP-008", "Web bolt count 2×rows×cols",
  "WebBoltCount = 2 * WebX.Count * WebY.Count (both sides).",
  "connections",
  [{"source": "src/HsSteel.Domain/Connections.cs", "note": "WebBoltCount"}],
  ["src/HsSteel.Domain/Connections.cs"], ["scss"])
R("SP-009", "Splice Find prefers bolt size then 22/20/16",
  "SpliceStandards.Find tries requested size then 22, 20, 16 for C or G tables.",
  "connections",
  [{"source": "src/HsSteel.Domain/Connections.cs", "note": "Find"}],
  ["src/HsSteel.Domain/Connections.cs"], ["scss"])

# Assembly types
prefixes = [("Column","C"),("SubColumn","SC"),("Post","PT"),("Girder","G"),("Beam","B"),
            ("CraneGirder","CG"),("Brace","BR"),("Purlin","PU"),("Girth","GT"),("Rafter","RF"),
            ("Truss","TR"),("Stair","ST"),("HandRail","HR"),("Embed","EM")]
for t, pfx in prefixes:
    R(f"AS-{pfx}", f"Assembly mark prefix {t}→{pfx}",
      f"AssemblyType.{t} uses mark prefix '{pfx}' and centre-line layer CL-3D-* convention.",
      "numbering",
      [{"source": "src/HsSteel.Domain/Model.cs", "note": f"AssemblyTypes.Prefix({t})"}],
      ["src/HsSteel.Domain/Model.cs:AssemblyTypes"], ["mark", "assembly"])

R("AS-LYR", "RAFTER layer alias FAFTER",
  "Layer CL-3D-FAFTER maps to AssemblyType.Rafter.",
  "numbering",
  [{"source": "src/HsSteel.Domain/Model.cs", "note": "FromLayer FAFTER"}],
  ["src/HsSteel.Domain/Model.cs"], ["layer"])

# Drafting / leaders (cite, sibling zone for styles)
R("DRF-001", "Weld leader min 3 paper mm",
  "Callouts.LeaderIfLongEnough omits leaders shorter than 3 paper mm × scale (COLLAB #3).",
  "drafting",
  [{"source": "docs/COLLAB.md", "note": "issue #3"},
   {"source": "docs/D4_RESULTS.md", "note": "LeaderIfLongEnough"}],
  ["src/HsSteel.Drafting"], ["leader", "weld"])
R("DRF-002", "HS-KOR text style txt+whgtxt",
  "Korean text style HS-KOR uses txt.shx + bigfont whgtxt.shx (power-cad must create styles).",
  "drafting",
  [{"source": "docs/COLLAB.md", "note": "Claude-sonnet #4"}],
  ["src/HsSteel.Drafting"], ["text-style", "hangul"])
R("DRF-003", "Empty text excluded from power-cad payload",
  "DrawingsToPowerCad drops text/mtext with empty string to avoid cad_create_many rejection.",
  "drafting",
  [{"source": "docs/COLLAB.md", "note": "live verify #1"}],
  ["src/HsSteel.Mcp/AssetTools.cs"], ["power-cad"])

# REBORN / BOM / manuals
R("RB-001", "BOM workbook 9 sheets",
  "Legacy BOM workbook schema targets 9 sheets (BomList, AssyList, cover, by-spec totals, …).",
  "bom",
  [{"source": "HS-STEEL_REBORN/orch/bom_schema.json", "note": "derived_spec trust unverified"},
   {"source": "docs/REBORN_LEDGER.md", "note": "ingest"}],
  [], ["bom", "reborn"])
R("RB-002", "Weight table density 7.85 unverified",
  "REBORN weight_table agrees 775/775 vs section kg/m ±0.5%, but density 7.85 assumption remains unverified trust.",
  "materials",
  [{"source": "docs/REBORN_LEDGER.md", "note": "reborn_weight_crosscheck"},
   {"source": "meta.reborn_weight_crosscheck", "note": "DB meta"}],
  [], ["weight", "trust"])
R("RB-003", "Cut plan loss spec exists",
  "형강커팅플랜 cut_plan_spec defines cutting loss rules (derived, unverified).",
  "fabrication",
  [{"source": "HS-STEEL_REBORN/orch/cut_plan_spec.json", "note": "derived_spec"}],
  [], ["cut-plan", "reborn"])
R("RB-004", "Command catalog 302",
  "REBORN command_triage_v2 lists 302 commands; 232 linked to our command nodes.",
  "commands",
  [{"source": "docs/REBORN_LEDGER.md", "note": "232/302 described_by"}],
  [], ["commands", "reborn"])
R("RB-005", "dwg_blocks_v2 verified",
  "Only derived_spec with trust=verified is dwg_blocks_v2 (113 basenames match block table).",
  "assets",
  [{"source": "docs/REBORN_LEDGER.md", "note": "verified 1"}],
  [], ["blocks", "trust"])
R("RB-006", "VLX not decompiled into engine",
  "hs02.VLX / HS-DETAIL.VLX are reference-only; behaviour reconstructed from FAS symbols and dialogs, not bytecode port.",
  "architecture",
  [{"source": "docs/ARCHITECTURE.md", "note": "clean-room"},
   {"source": "docs/ASSET_LEDGER.md", "note": "VLX reference"}],
  [], ["vlx"])
R("RB-007", "Dongle DLLs excluded",
  "CSROCKEY2013 / r4nd_class / HsRockey are excluded (license dongle), never redistributed.",
  "architecture",
  [{"source": "docs/ASSET_LEDGER.md", "note": "excluded license"}],
  [], ["license"])

# Section / families
R("SEC-001", "14 steel families in D2",
  "Engine covers 14 families: H/BH/LH/PEB/I/T/ANGLE/CHANNEL/C-CHANNEL/Z/SQ-PIPE/STEEL-PIPE/ROUND-BAR/FLAT-BAR (+ plate).",
  "sections",
  [{"source": "docs/FEATURE_EXPLORATION.md", "note": "D2 list"}],
  ["src/HsSteel.Domain/Profile.cs"], ["family"])
R("SEC-002", "Section catalog skips Project and SCSS",
  "SectionCatalog.Load skips Project.dat and SCSS-*.dat when scanning attributes.",
  "sections",
  [{"source": "src/HsSteel.Domain/Model.cs", "note": "SectionCatalog.Load"}],
  ["src/HsSteel.Domain/Model.cs"], ["catalog"])

# Architecture principles as rules
R("ARCH-001", "Model-first input",
  "Primary input is the 3D/project model; drawings are projections (ARCHITECTURE §1).",
  "architecture",
  [{"source": "docs/ARCHITECTURE.md", "note": "model-first"}],
  [], ["architecture"])
R("ARCH-002", "No AutoCAD.NET in Drafting",
  "Drafting emits DrawPlan/DXF only; AutoCAD execution via power-cad, not AutoCAD.NET in Drafting.",
  "architecture",
  [{"source": "docs/ARCHITECTURE.md", "note": "Drafting rule"}],
  [], ["architecture"])
R("ARCH-003", "Deterministic rules",
  "Same input → same DrawPlan; tests lock behaviour.",
  "architecture",
  [{"source": "docs/ARCHITECTURE.md", "note": "design principles"}],
  [], ["architecture"])

assert len(rules) >= 80, len(rules)
out = ROOT / "src" / "HsSteel.Knowledge" / "Rules" / "rules.json"
out.write_text(json.dumps({"schema": "hs-steel-rules/1", "count": len(rules), "rules": rules}, ensure_ascii=False, indent=2), encoding="utf-8")
print(f"Wrote {len(rules)} rules -> {out}")
