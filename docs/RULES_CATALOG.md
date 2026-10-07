# RULES_CATALOG

**Count:** 100 evidence-backed rules (schema `hs-steel-rules/1`).
**Machine source:** `src/HsSteel.Knowledge/Rules/rules.json` + `RulesCatalog.cs`.
**Engine cross-check:** 64/100 rules cite `engine_refs` paths under Domain/Modeling/Drafting/Mcp.

## Categories

| Category | Count |
|---|---:|
| connections | 21 |
| bolting | 19 |
| numbering | 15 |
| drafting | 13 |
| fabrication | 8 |
| welding | 7 |
| architecture | 5 |
| materials | 3 |
| bom | 3 |
| sections | 2 |
| detailing | 1 |
| finishing | 1 |
| commands | 1 |
| assets | 1 |

## Sources

- `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` (PD-*)
- `src/HsSteel.Domain/{Model,Fabrication,Connections}.cs` (DR/WL/BT/GG/SP/AS/SEC)
- `src/HsSteel.Modeling/ShearTabLayout.cs` + `docs/SHEAR-TAB-ROWS.md` (ST-*)
- `docs/{ARCHITECTURE,COLLAB,REBORN_LEDGER,D4_RESULTS}.md` (RB/ARCH/DRF)
- REBORN derived specs (bom, cut plan, trust grades)

## Cross-check notes

- `DetailRules.From` reads SCALLOP/ENDGAGE/SHOLE/WDGAP/SWS; other Project.dat keys are catalogued for drafting/BOM parity but not all wired into `DetailRules` yet (intentional gap).
- Legacy Project.dat `SWS=SS400` vs engine default `SS275` called out in DR-005 / PD-010.
- Shear-tab / fillet / hole rules cite KDS 14 31 25 / AISC tables as external evidence.
- VLX behaviour is **not** claimed as rules; RB-006 states clean-room policy.

## Full index

| ID | Category | Title | Engine refs |
|---|---|---|---|
| PD-001 | detailing | Project.dat WDGAP | src/HsSteel.Domain/Model.cs:DetailRules.From |
| PD-002 | fabrication | Project.dat SCALLOP | src/HsSteel.Domain/Model.cs:DetailRules.From |
| PD-003 | fabrication | Project.dat RCCR | — |
| PD-004 | bolting | Project.dat SHOLE | src/HsSteel.Domain/Model.cs:DetailRules.From |
| PD-005 | bolting | Project.dat ENDGAGE | src/HsSteel.Domain/Model.cs:DetailRules.From |
| PD-006 | bolting | Project.dat HOLELEN | — |
| PD-007 | fabrication | Project.dat stffsqgage | — |
| PD-008 | fabrication | Project.dat STFFGAP | — |
| PD-009 | fabrication | Project.dat STFFIN | — |
| PD-010 | materials | Project.dat SWS | src/HsSteel.Domain/Model.cs:DetailRules.From |
| PD-011 | welding | Project.dat WELDTIP | — |
| PD-012 | finishing | Project.dat PAINT | — |
| PD-013 | fabrication | Project.dat SET_COL_STFF_JBX | — |
| PD-014 | bom | Project.dat SET_ASY_BOMLEN | — |
| PD-015 | drafting | Project.dat hs-dimrnd-box | — |
| PD-016 | drafting | Project.dat m32-dim-txt-box | — |
| PD-017 | drafting | Project.dat m32-marktxt-box | — |
| PD-018 | drafting | Project.dat m32-gridasy-box | — |
| PD-019 | drafting | Project.dat m32-griddan-box | — |
| PD-020 | drafting | Project.dat m32-bom-txt-box | — |
| PD-021 | connections | Project.dat m32-splise-gap-box | — |
| PD-022 | connections | Project.dat m32-share1-gap-box | — |
| PD-023 | connections | Project.dat m32-share2-gap-box | — |
| PD-024 | connections | Project.dat m32-pipejun-ang-box | — |
| PD-025 | connections | Project.dat m32-pipejun-las-123 | — |
| PD-026 | drafting | Project.dat m32-dimtad-pop | — |
| PD-027 | drafting | Project.dat m32-dimdec-pop | — |
| PD-028 | drafting | Project.dat m32-dimlunit-pop | — |
| PD-029 | bolting | Project.dat m32-gitabolt-pop | — |
| PD-030 | bolting | Project.dat m32-spgpbolt-pop | — |
| PD-031 | bom | Project.dat set-bom-matl-elss-tog | — |
| DR-001 | fabrication | DetailRules scallop default 30 | src/HsSteel.Domain/Model.cs |
| DR-002 | bolting | DetailRules end gauge default 40 | src/HsSteel.Domain/Model.cs |
| DR-003 | bolting | DetailRules hole dia default 22 | src/HsSteel.Domain/Model.cs, src/HsSteel.Domain/Fabrication.cs |
| DR-004 | welding | DetailRules weld gap default 5 | src/HsSteel.Domain/Model.cs |
| DR-005 | materials | DetailRules material default SS275 | src/HsSteel.Domain/Model.cs |
| DR-006 | connections | DetailRules connection gap default 10 | src/HsSteel.Domain/Model.cs |
| DR-007 | drafting | DetailRules text height 2.5 / dim gap 7 | src/HsSteel.Domain/Model.cs |
| WL-6 | welding | Min fillet leg for t≤6 | src/HsSteel.Domain/Fabrication.cs:WeldRules |
| WL-12 | welding | Min fillet leg for t≤12 | src/HsSteel.Domain/Fabrication.cs:WeldRules |
| WL-20 | welding | Min fillet leg for t≤20 | src/HsSteel.Domain/Fabrication.cs:WeldRules |
| WL-999 | welding | Min fillet leg for t≤>20 | src/HsSteel.Domain/Fabrication.cs:WeldRules |
| WL-CAP | welding | Fillet leg capped by thinner part | src/HsSteel.Domain/Fabrication.cs |
| BT-001 | bolting | Standard hole bolt+2 (≤M22) | src/HsSteel.Domain/Fabrication.cs:Bolts.HoleFor |
| BT-002 | bolting | Standard hole bolt+3 (≥M24) | src/HsSteel.Domain/Fabrication.cs |
| BT-003 | bolting | TS / S10T grade mapping | src/HsSteel.Domain/Fabrication.cs |
| BT-004 | bolting | HTB / F10T grade mapping | src/HsSteel.Domain/Fabrication.cs |
| BT-005 | bolting | Ordinary M-BOLT grade | src/HsSteel.Domain/Fabrication.cs |
| BT-006 | bolting | Unknown bolt name defaults F10T | src/HsSteel.Domain/Fabrication.cs |
| GG-001 | bolting | Angle gauge AIJ/KS table | src/HsSteel.Domain/Fabrication.cs:BoltGauges |
| GG-002 | bolting | H/T flange gauge table | src/HsSteel.Domain/Fabrication.cs |
| GG-003 | bolting | Minimum edge distance practice 1.5d / 20 | src/HsSteel.Domain/Fabrication.cs |
| GG-004 | bolting | Default bolt by profile family | src/HsSteel.Domain/Fabrication.cs |
| GG-005 | bolting | I/Channel web pitch 3d to 10mm | src/HsSteel.Domain/Fabrication.cs |
| ST-001 | connections | Clear web T=D-2(tf+r) | src/HsSteel.Modeling/ShearTabLayout.cs |
| ST-002 | connections | Shop edge distance 40 mm | src/HsSteel.Modeling/ShearTabLayout.cs |
| ST-003 | connections | Shop pitch 70 mm / 3d | src/HsSteel.Modeling/ShearTabLayout.cs |
| ST-004 | connections | Sheared edge M20 = 34 mm | src/HsSteel.Modeling/ShearTabLayout.cs |
| ST-005 | connections | Rows n=floor((T-2e)/p)+1 | src/HsSteel.Modeling/ShearTabLayout.cs |
| ST-006 | connections | n<2 flagged | src/HsSteel.Modeling/ShearTabLayout.cs |
| ST-007 | connections | Prefer splice-standard WebY rows | src/HsSteel.Modeling/ModelBuilder.cs, src/HsSteel.Modeling/ShearTabLayout.cs |
| SP-001 | connections | BoltPattern grammar | src/HsSteel.Domain/Connections.cs |
| SP-002 | connections | BoltAxis nAp group | src/HsSteel.Domain/Connections.cs |
| SP-003 | bolting | HoleDia = BoltDia+2 in pattern | src/HsSteel.Domain/Connections.cs |
| SP-004 | connections | SCSS S1–S10 web plate fields | src/HsSteel.Domain/Connections.cs:SpliceSpec |
| SP-005 | connections | SCSS S11–S21 flange fields | src/HsSteel.Domain/Connections.cs |
| SP-006 | connections | SCSS S22 splice gap | src/HsSteel.Domain/Connections.cs |
| SP-007 | connections | Flange lines from S23 | src/HsSteel.Domain/Connections.cs |
| SP-008 | connections | Web bolt count 2×rows×cols | src/HsSteel.Domain/Connections.cs |
| SP-009 | connections | Splice Find prefers bolt size then 22/20/16 | src/HsSteel.Domain/Connections.cs |
| AS-C | numbering | Assembly mark prefix Column→C | src/HsSteel.Domain/Model.cs:AssemblyTypes |
| AS-SC | numbering | Assembly mark prefix SubColumn→SC | src/HsSteel.Domain/Model.cs:AssemblyTypes |
| AS-PT | numbering | Assembly mark prefix Post→PT | src/HsSteel.Domain/Model.cs:AssemblyTypes |
| AS-G | numbering | Assembly mark prefix Girder→G | src/HsSteel.Domain/Model.cs:AssemblyTypes |
| AS-B | numbering | Assembly mark prefix Beam→B | src/HsSteel.Domain/Model.cs:AssemblyTypes |
| AS-CG | numbering | Assembly mark prefix CraneGirder→CG | src/HsSteel.Domain/Model.cs:AssemblyTypes |
| AS-BR | numbering | Assembly mark prefix Brace→BR | src/HsSteel.Domain/Model.cs:AssemblyTypes |
| AS-PU | numbering | Assembly mark prefix Purlin→PU | src/HsSteel.Domain/Model.cs:AssemblyTypes |
| AS-GT | numbering | Assembly mark prefix Girth→GT | src/HsSteel.Domain/Model.cs:AssemblyTypes |
| AS-RF | numbering | Assembly mark prefix Rafter→RF | src/HsSteel.Domain/Model.cs:AssemblyTypes |
| AS-TR | numbering | Assembly mark prefix Truss→TR | src/HsSteel.Domain/Model.cs:AssemblyTypes |
| AS-ST | numbering | Assembly mark prefix Stair→ST | src/HsSteel.Domain/Model.cs:AssemblyTypes |
| AS-HR | numbering | Assembly mark prefix HandRail→HR | src/HsSteel.Domain/Model.cs:AssemblyTypes |
| AS-EM | numbering | Assembly mark prefix Embed→EM | src/HsSteel.Domain/Model.cs:AssemblyTypes |
| AS-LYR | numbering | RAFTER layer alias FAFTER | src/HsSteel.Domain/Model.cs |
| DRF-001 | drafting | Weld leader min 3 paper mm | src/HsSteel.Drafting |
| DRF-002 | drafting | HS-KOR text style txt+whgtxt | src/HsSteel.Drafting |
| DRF-003 | drafting | Empty text excluded from power-cad payload | src/HsSteel.Mcp/AssetTools.cs |
| RB-001 | bom | BOM workbook 9 sheets | — |
| RB-002 | materials | Weight table density 7.85 unverified | — |
| RB-003 | fabrication | Cut plan loss spec exists | — |
| RB-004 | commands | Command catalog 302 | — |
| RB-005 | assets | dwg_blocks_v2 verified | — |
| RB-006 | architecture | VLX not decompiled into engine | — |
| RB-007 | architecture | Dongle DLLs excluded | — |
| SEC-001 | sections | 14 steel families in D2 | src/HsSteel.Domain/Profile.cs |
| SEC-002 | sections | Section catalog skips Project and SCSS | src/HsSteel.Domain/Model.cs |
| ARCH-001 | architecture | Model-first input | — |
| ARCH-002 | architecture | No AutoCAD.NET in Drafting | — |
| ARCH-003 | architecture | Deterministic rules | — |

## Statements (compact)

### PD-001 — Project.dat WDGAP

Default project setting WDGAP = 5: Weld fit-up gap (mm).

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("WDGAP" 5)

### PD-002 — Project.dat SCALLOP

Default project setting SCALLOP = 30: Flange scallop/cope radius (mm).

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("SCALLOP" 30)

### PD-003 — Project.dat RCCR

Default project setting RCCR = 30: Column corner radius / related clearance (mm).

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("RCCR" 30)

### PD-004 — Project.dat SHOLE

Default project setting SHOLE = 22: Default bolt hole diameter (mm).

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("SHOLE" 22)

### PD-005 — Project.dat ENDGAGE

Default project setting ENDGAGE = 40: End distance to first bolt row (mm).

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("ENDGAGE" 40)

### PD-006 — Project.dat HOLELEN

Default project setting HOLELEN = 60: Default slotted/long hole length related (mm).

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("HOLELEN" 60)

### PD-007 — Project.dat stffsqgage

Default project setting stffsqgage = 60: Stiffener square gauge (mm).

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("stffsqgage" "60")

### PD-008 — Project.dat STFFGAP

Default project setting STFFGAP = 0: Stiffener gap toggle/value.

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("STFFGAP" 0)

### PD-009 — Project.dat STFFIN

Default project setting STFFIN = 0: Stiffener inset toggle/value.

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("STFFIN" 0)

### PD-010 — Project.dat SWS

Default project setting SWS = SS400: Default steel grade (legacy SS400; engine maps toward SS275).

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("SWS" "SS400")

### PD-011 — Project.dat WELDTIP

Default project setting WELDTIP = AWS_EXXXX_GRADE: Weld tip/electrode grade placeholder.

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("WELDTIP" "AWS_EXXXX_GRADE")

### PD-012 — Project.dat PAINT

Default project setting PAINT = SEE_PAINT_SPEC.: Paint specification note.

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("PAINT" "SEE_PAINT_SPEC.")

### PD-013 — Project.dat SET_COL_STFF_JBX

Default project setting SET_COL_STFF_JBX = 75: Column stiffener J-box related size.

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("SET_COL_STFF_JBX" 75)

### PD-014 — Project.dat SET_ASY_BOMLEN

Default project setting SET_ASY_BOMLEN = 73: Assembly BOM length field width/setting.

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("SET_ASY_BOMLEN" 73)

### PD-015 — Project.dat hs-dimrnd-box

Default project setting hs-dimrnd-box = 0.5: Dimension round-off (mm).

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("hs-dimrnd-box" "0.5")

### PD-016 — Project.dat m32-dim-txt-box

Default project setting m32-dim-txt-box = 3: Dimension text height (paper mm).

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("m32-dim-txt-box" 3)

### PD-017 — Project.dat m32-marktxt-box

Default project setting m32-marktxt-box = 1.6: Mark text height (paper mm).

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("m32-marktxt-box" 1.6)

### PD-018 — Project.dat m32-gridasy-box

Default project setting m32-gridasy-box = 5: Assembly grid text size.

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("m32-gridasy-box" 5)

### PD-019 — Project.dat m32-griddan-box

Default project setting m32-griddan-box = 4: Part grid text size.

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("m32-griddan-box" 4)

### PD-020 — Project.dat m32-bom-txt-box

Default project setting m32-bom-txt-box = 1.5: BOM table text height.

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("m32-bom-txt-box" 1.5)

### PD-021 — Project.dat m32-splise-gap-box

Default project setting m32-splise-gap-box = 5: Splice gap (mm).

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("m32-splise-gap-box" "5")

### PD-022 — Project.dat m32-share1-gap-box

Default project setting m32-share1-gap-box = 5: Shear connection gap setting 1 (mm).

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("m32-share1-gap-box" "5")

### PD-023 — Project.dat m32-share2-gap-box

Default project setting m32-share2-gap-box = 5: Shear connection gap setting 2 (mm).

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("m32-share2-gap-box" "5")

### PD-024 — Project.dat m32-pipejun-ang-box

Default project setting m32-pipejun-ang-box = 3: Pipe junction angle tolerance.

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("m32-pipejun-ang-box" "3")

### PD-025 — Project.dat m32-pipejun-las-123

Default project setting m32-pipejun-las-123 = 3: Pipe junction LAS setting.

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("m32-pipejun-las-123" "3")

### PD-026 — Project.dat m32-dimtad-pop

Default project setting m32-dimtad-pop = 2: DIMTAD dimension text above-line pop index.

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("m32-dimtad-pop" "2")

### PD-027 — Project.dat m32-dimdec-pop

Default project setting m32-dimdec-pop = 1: DIMDEC decimal places pop index.

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("m32-dimdec-pop" "1")

### PD-028 — Project.dat m32-dimlunit-pop

Default project setting m32-dimlunit-pop = 1: DIMLUNIT linear units pop index.

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("m32-dimlunit-pop" "1")

### PD-029 — Project.dat m32-gitabolt-pop

Default project setting m32-gitabolt-pop = 3: Gita/secondary bolt pop index.

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("m32-gitabolt-pop" "3")

### PD-030 — Project.dat m32-spgpbolt-pop

Default project setting m32-spgpbolt-pop = 0: Splice group bolt pop index.

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("m32-spgpbolt-pop" "0")

### PD-031 — Project.dat set-bom-matl-elss-tog

Default project setting set-bom-matl-elss-tog = 1: BOM material ELSS toggle on.

- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` — ("set-bom-matl-elss-tog" "1")

### DR-001 — DetailRules scallop default 30

When Project.dat is missing SCALLOP, DetailRules.Scallop defaults to 30 mm.

- evidence: `src/HsSteel.Domain/Model.cs` — DetailRules record default Scallop=30

### DR-002 — DetailRules end gauge default 40

EndGauge defaults to 40 mm (ENDGAGE).

- evidence: `src/HsSteel.Domain/Model.cs` — EndGauge=40

### DR-003 — DetailRules hole dia default 22

HoleDia defaults to 22 mm (SHOLE) matching M20 + 2.

- evidence: `src/HsSteel.Domain/Model.cs` — HoleDia=22
- evidence: `src/HsSteel.Domain/Fabrication.cs` — Bolts.HoleFor: bolt+2 up to M22

### DR-004 — DetailRules weld gap default 5

WeldGap defaults to 5 mm (WDGAP).

- evidence: `src/HsSteel.Domain/Model.cs` — WeldGap=5

### DR-005 — DetailRules material default SS275

Engine default Material is SS275 when SWS missing (legacy Project.dat often SS400).

- evidence: `src/HsSteel.Domain/Model.cs` — Material default SS275; Project.dat SWS=SS400

### DR-006 — DetailRules connection gap default 10

ConnectionGap (beam end to supporting web clearance) defaults to 10 mm.

- evidence: `src/HsSteel.Domain/Model.cs` — ConnectionGap=10

### DR-007 — DetailRules text height 2.5 / dim gap 7

Paper text height 2.5 mm and dimension row gap 7 mm are drafting defaults.

- evidence: `src/HsSteel.Domain/Model.cs` — TextHeight=2.5 DimGap=7

### WL-6 — Min fillet leg for t≤6

Minimum fillet weld leg is 3 mm when thicker part thickness ≤ 6 mm (capped by thinner part).

- evidence: `src/HsSteel.Domain/Fabrication.cs` — WeldRules.MinFillet KDS 14 31 25 / AWS D1.1 Table 5.7
- evidence: `KDS 14 31 25` — min fillet by thicker part

### WL-12 — Min fillet leg for t≤12

Minimum fillet weld leg is 5 mm when thicker part thickness ≤ 12 mm (capped by thinner part).

- evidence: `src/HsSteel.Domain/Fabrication.cs` — WeldRules.MinFillet KDS 14 31 25 / AWS D1.1 Table 5.7
- evidence: `KDS 14 31 25` — min fillet by thicker part

### WL-20 — Min fillet leg for t≤20

Minimum fillet weld leg is 6 mm when thicker part thickness ≤ 20 mm (capped by thinner part).

- evidence: `src/HsSteel.Domain/Fabrication.cs` — WeldRules.MinFillet KDS 14 31 25 / AWS D1.1 Table 5.7
- evidence: `KDS 14 31 25` — min fillet by thicker part

### WL-999 — Min fillet leg for t≤>20

Minimum fillet weld leg is 8 mm when thicker part thickness ≤ ∞ mm (capped by thinner part).

- evidence: `src/HsSteel.Domain/Fabrication.cs` — WeldRules.MinFillet KDS 14 31 25 / AWS D1.1 Table 5.7
- evidence: `KDS 14 31 25` — min fillet by thicker part

### WL-CAP — Fillet leg capped by thinner part

FilletLeg(t1,t2) = min(table_min(max(t1,t2)), min(t1,t2)).

- evidence: `src/HsSteel.Domain/Fabrication.cs` — WeldRules.FilletLeg

### BT-001 — Standard hole bolt+2 (≤M22)

Standard hole diameter = bolt diameter + 2 mm for bolts up to M22.

- evidence: `src/HsSteel.Domain/Fabrication.cs` — Bolts.HoleFor
- evidence: `Project.dat SHOLE` — 22 for M20

### BT-002 — Standard hole bolt+3 (≥M24)

Standard hole diameter = bolt diameter + 3 mm for bolts M24 and larger.

- evidence: `src/HsSteel.Domain/Fabrication.cs` — Bolts.HoleFor boltDia>=24

### BT-003 — TS / S10T grade mapping

Bolt names starting with TS or containing S10T map to grade S10T (KS B 2819).

- evidence: `src/HsSteel.Domain/Fabrication.cs` — Bolts.GradeOf

### BT-004 — HTB / F10T grade mapping

HTB / F10T names map to F10T (KS B 1010); F8T preserved as F8T.

- evidence: `src/HsSteel.Domain/Fabrication.cs` — Bolts.GradeOf

### BT-005 — Ordinary M-BOLT grade

Names starting with M / SB or containing 4.6 map to M-BOLT.

- evidence: `src/HsSteel.Domain/Fabrication.cs` — Bolts.GradeOf

### BT-006 — Unknown bolt name defaults F10T

Unrecognized bolt names default to F10T.

- evidence: `src/HsSteel.Domain/Fabrication.cs` — GradeOf return F10T

### GG-001 — Angle gauge AIJ/KS table

Angle leg gauges follow AIJ/KS table (e.g. L90→g1=50 max M24; L150→g1=55 g2=55).

- evidence: `src/HsSteel.Domain/Fabrication.cs` — BoltGauges.Angle

### GG-002 — H/T flange gauge table

Flange gauge between bolt lines: W200→120, W250→150, W300→150, W350→140.

- evidence: `src/HsSteel.Domain/Fabrication.cs` — BoltGauges.Flange

### GG-003 — Minimum edge distance practice 1.5d / 20

BoltGauges.For uses e = max(1.5*bolt, 20) mm as minimum edge distance practice.

- evidence: `src/HsSteel.Domain/Fabrication.cs` — BoltGauges.For e=max(1.5*bolt,20)

### GG-004 — Default bolt by profile family

DefaultBolt: L by angle max; lip/Z 12 or 16; flat by depth; box/pipe by size; channel/H by depth bands.

- evidence: `src/HsSteel.Domain/Fabrication.cs` — BoltGauges.DefaultBolt

### GG-005 — I/Channel web pitch 3d to 10mm

For I/Channel end connections, web pitch = ceil(3d/10)*10, rows capped at 6.

- evidence: `src/HsSteel.Domain/Fabrication.cs` — BoltGauges.For ShapeKind.I/Channel

### ST-001 — Clear web T=D-2(tf+r)

Shear-tab plate must fit clear web depth T = D − 2(tf + r).

- evidence: `docs/SHEAR-TAB-ROWS.md` — clear web rule
- evidence: `src/HsSteel.Modeling/ShearTabLayout.cs` — ClearWebDepth

### ST-002 — Shop edge distance 40 mm

Vertical edge distance e ≥ max(40, MinEdgeSheared(d)).

- evidence: `docs/SHEAR-TAB-ROWS.md` — ShopEdge=40
- evidence: `KDS 14 31 25` — sheared edge table

### ST-003 — Shop pitch 70 mm / 3d

Pitch p ≥ max(70, ceil(3d/5)*5).

- evidence: `docs/SHEAR-TAB-ROWS.md` — ShopPitch=70 preferred 3d

### ST-004 — Sheared edge M20 = 34 mm

MinEdgeSheared(M20)=34, M16=28, M22=38, M24=42, M27=48, M30=52; >M30 → 1.75d.

- evidence: `src/HsSteel.Modeling/ShearTabLayout.cs` — MinEdgeSheared
- evidence: `AISC 360-05 Table J3.4M` — same as KDS

### ST-005 — Rows n=floor((T-2e)/p)+1

Bolt rows n = ⌊(T−2e)/p⌋+1; plate height 2e+(n−1)p ≤ T, centred.

- evidence: `docs/SHEAR-TAB-ROWS.md` — row formula

### ST-006 — n<2 flagged

Fewer than 2 rows flags warning (AISC Manual Part 10 conventional 2–12).

- evidence: `docs/SHEAR-TAB-ROWS.md` — n<2 warning

### ST-007 — Prefer splice-standard WebY rows

ModelBuilder.ShearTab prefers SpliceStandards.WebY when present; else ShearTabLayout.Default.

- evidence: `docs/SHEAR-TAB-ROWS.md` — splice override

### SP-001 — BoltPattern grammar

Plate/bolt string: thicknesses (*T)*, then X<axis>, Y<axis>, D<dia> in any order.

- evidence: `src/HsSteel.Domain/Connections.cs` — BoltPattern.Parse

### SP-002 — BoltAxis nAp group

Axis term nAp means n intervals of pitch p → n+1 holes; plain terms are edges/gaps.

- evidence: `src/HsSteel.Domain/Connections.cs` — BoltAxis.Parse

### SP-003 — HoleDia = BoltDia+2 in pattern

BoltPattern.HoleDia returns BoltDia+2 when BoltDia>0.

- evidence: `src/HsSteel.Domain/Connections.cs` — BoltPattern.HoleDia

### SP-004 — SCSS S1–S10 web plate fields

SCSS row: S1 web t; S2–S5 web X; S6–S9 web Y; S10 web bolt dia.

- evidence: `src/HsSteel.Domain/Connections.cs` — SpliceSpec remarks
- evidence: `HSSTEEL/attributes/SCSS-*.dat` — 6 tables 175 rows

### SP-005 — SCSS S11–S21 flange fields

S11/S12 flange plate t; S13 flange bolt dia; S14–S17 flange X; S18 gauge; S19 second gauge; S20–S21 edges.

- evidence: `src/HsSteel.Domain/Connections.cs` — SpliceSpec

### SP-006 — SCSS S22 splice gap

S22 is splice gap (typically 5 mm; Project.dat m32-splise-gap-box).

- evidence: `src/HsSteel.Domain/Connections.cs` — Gap => N(22)
- evidence: `Project.dat` — m32-splise-gap-box 5

### SP-007 — Flange lines from S23

FlangeLines = max(2, FlangeBoltTotal/(4*FlangeX.Count)); 2 or 4 lines across flange.

- evidence: `src/HsSteel.Domain/Connections.cs` — FlangeLines

### SP-008 — Web bolt count 2×rows×cols

WebBoltCount = 2 * WebX.Count * WebY.Count (both sides).

- evidence: `src/HsSteel.Domain/Connections.cs` — WebBoltCount

### SP-009 — Splice Find prefers bolt size then 22/20/16

SpliceStandards.Find tries requested size then 22, 20, 16 for C or G tables.

- evidence: `src/HsSteel.Domain/Connections.cs` — Find

### AS-C — Assembly mark prefix Column→C

AssemblyType.Column uses mark prefix 'C' and centre-line layer CL-3D-* convention.

- evidence: `src/HsSteel.Domain/Model.cs` — AssemblyTypes.Prefix(Column)

### AS-SC — Assembly mark prefix SubColumn→SC

AssemblyType.SubColumn uses mark prefix 'SC' and centre-line layer CL-3D-* convention.

- evidence: `src/HsSteel.Domain/Model.cs` — AssemblyTypes.Prefix(SubColumn)

### AS-PT — Assembly mark prefix Post→PT

AssemblyType.Post uses mark prefix 'PT' and centre-line layer CL-3D-* convention.

- evidence: `src/HsSteel.Domain/Model.cs` — AssemblyTypes.Prefix(Post)

### AS-G — Assembly mark prefix Girder→G

AssemblyType.Girder uses mark prefix 'G' and centre-line layer CL-3D-* convention.

- evidence: `src/HsSteel.Domain/Model.cs` — AssemblyTypes.Prefix(Girder)

### AS-B — Assembly mark prefix Beam→B

AssemblyType.Beam uses mark prefix 'B' and centre-line layer CL-3D-* convention.

- evidence: `src/HsSteel.Domain/Model.cs` — AssemblyTypes.Prefix(Beam)

### AS-CG — Assembly mark prefix CraneGirder→CG

AssemblyType.CraneGirder uses mark prefix 'CG' and centre-line layer CL-3D-* convention.

- evidence: `src/HsSteel.Domain/Model.cs` — AssemblyTypes.Prefix(CraneGirder)

### AS-BR — Assembly mark prefix Brace→BR

AssemblyType.Brace uses mark prefix 'BR' and centre-line layer CL-3D-* convention.

- evidence: `src/HsSteel.Domain/Model.cs` — AssemblyTypes.Prefix(Brace)

### AS-PU — Assembly mark prefix Purlin→PU

AssemblyType.Purlin uses mark prefix 'PU' and centre-line layer CL-3D-* convention.

- evidence: `src/HsSteel.Domain/Model.cs` — AssemblyTypes.Prefix(Purlin)

### AS-GT — Assembly mark prefix Girth→GT

AssemblyType.Girth uses mark prefix 'GT' and centre-line layer CL-3D-* convention.

- evidence: `src/HsSteel.Domain/Model.cs` — AssemblyTypes.Prefix(Girth)

### AS-RF — Assembly mark prefix Rafter→RF

AssemblyType.Rafter uses mark prefix 'RF' and centre-line layer CL-3D-* convention.

- evidence: `src/HsSteel.Domain/Model.cs` — AssemblyTypes.Prefix(Rafter)

### AS-TR — Assembly mark prefix Truss→TR

AssemblyType.Truss uses mark prefix 'TR' and centre-line layer CL-3D-* convention.

- evidence: `src/HsSteel.Domain/Model.cs` — AssemblyTypes.Prefix(Truss)

### AS-ST — Assembly mark prefix Stair→ST

AssemblyType.Stair uses mark prefix 'ST' and centre-line layer CL-3D-* convention.

- evidence: `src/HsSteel.Domain/Model.cs` — AssemblyTypes.Prefix(Stair)

### AS-HR — Assembly mark prefix HandRail→HR

AssemblyType.HandRail uses mark prefix 'HR' and centre-line layer CL-3D-* convention.

- evidence: `src/HsSteel.Domain/Model.cs` — AssemblyTypes.Prefix(HandRail)

### AS-EM — Assembly mark prefix Embed→EM

AssemblyType.Embed uses mark prefix 'EM' and centre-line layer CL-3D-* convention.

- evidence: `src/HsSteel.Domain/Model.cs` — AssemblyTypes.Prefix(Embed)

### AS-LYR — RAFTER layer alias FAFTER

Layer CL-3D-FAFTER maps to AssemblyType.Rafter.

- evidence: `src/HsSteel.Domain/Model.cs` — FromLayer FAFTER

### DRF-001 — Weld leader min 3 paper mm

Callouts.LeaderIfLongEnough omits leaders shorter than 3 paper mm × scale (COLLAB #3).

- evidence: `docs/COLLAB.md` — issue #3
- evidence: `docs/D4_RESULTS.md` — LeaderIfLongEnough

### DRF-002 — HS-KOR text style txt+whgtxt

Korean text style HS-KOR uses txt.shx + bigfont whgtxt.shx (power-cad must create styles).

- evidence: `docs/COLLAB.md` — Claude-sonnet #4

### DRF-003 — Empty text excluded from power-cad payload

DrawingsToPowerCad drops text/mtext with empty string to avoid cad_create_many rejection.

- evidence: `docs/COLLAB.md` — live verify #1

### RB-001 — BOM workbook 9 sheets

Legacy BOM workbook schema targets 9 sheets (BomList, AssyList, cover, by-spec totals, …).

- evidence: `HS-STEEL_REBORN/orch/bom_schema.json` — derived_spec trust unverified
- evidence: `docs/REBORN_LEDGER.md` — ingest

### RB-002 — Weight table density 7.85 unverified

REBORN weight_table agrees 775/775 vs section kg/m ±0.5%, but density 7.85 assumption remains unverified trust.

- evidence: `docs/REBORN_LEDGER.md` — reborn_weight_crosscheck
- evidence: `meta.reborn_weight_crosscheck` — DB meta

### RB-003 — Cut plan loss spec exists

형강커팅플랜 cut_plan_spec defines cutting loss rules (derived, unverified).

- evidence: `HS-STEEL_REBORN/orch/cut_plan_spec.json` — derived_spec

### RB-004 — Command catalog 302

REBORN command_triage_v2 lists 302 commands; 232 linked to our command nodes.

- evidence: `docs/REBORN_LEDGER.md` — 232/302 described_by

### RB-005 — dwg_blocks_v2 verified

Only derived_spec with trust=verified is dwg_blocks_v2 (113 basenames match block table).

- evidence: `docs/REBORN_LEDGER.md` — verified 1

### RB-006 — VLX not decompiled into engine

hs02.VLX / HS-DETAIL.VLX are reference-only; behaviour reconstructed from FAS symbols and dialogs, not bytecode port.

- evidence: `docs/ARCHITECTURE.md` — clean-room
- evidence: `docs/ASSET_LEDGER.md` — VLX reference

### RB-007 — Dongle DLLs excluded

CSROCKEY2013 / r4nd_class / HsRockey are excluded (license dongle), never redistributed.

- evidence: `docs/ASSET_LEDGER.md` — excluded license

### SEC-001 — 14 steel families in D2

Engine covers 14 families: H/BH/LH/PEB/I/T/ANGLE/CHANNEL/C-CHANNEL/Z/SQ-PIPE/STEEL-PIPE/ROUND-BAR/FLAT-BAR (+ plate).

- evidence: `docs/FEATURE_EXPLORATION.md` — D2 list

### SEC-002 — Section catalog skips Project and SCSS

SectionCatalog.Load skips Project.dat and SCSS-*.dat when scanning attributes.

- evidence: `src/HsSteel.Domain/Model.cs` — SectionCatalog.Load

### ARCH-001 — Model-first input

Primary input is the 3D/project model; drawings are projections (ARCHITECTURE §1).

- evidence: `docs/ARCHITECTURE.md` — model-first

### ARCH-002 — No AutoCAD.NET in Drafting

Drafting emits DrawPlan/DXF only; AutoCAD execution via power-cad, not AutoCAD.NET in Drafting.

- evidence: `docs/ARCHITECTURE.md` — Drafting rule

### ARCH-003 — Deterministic rules

Same input → same DrawPlan; tests lock behaviour.

- evidence: `docs/ARCHITECTURE.md` — design principles
