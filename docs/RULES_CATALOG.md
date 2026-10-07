# RULES_CATALOG

**Count:** 135 rules (schema `hs-steel-rules/2`) — trust: verified 20, stated 91, inferred 24.
**Machine source:** `src/HsSteel.Knowledge/Rules/rules.json` + `RulesCatalog.cs`; materialized into `out/hs_assets.db` by `RulesIngest` (tables `rule`, `rule_evidence`; nodes `rule`; edges `governs`, `evidenced_by`; FTS kind `rule`).
**Engine status:** implemented 62, differs 16, missing 43, n/a 14 (vs `src/HsSteel.Domain`, `Modeling`, `Drafting`).

## Trust grades

- **verified** — a numeric or cross-source check against an independent second source passed (`verification.result = pass`).
- **stated** — taken as written from one source (Project.dat key, workbook formula, VBA, engine code, doc); not re-derived.
- **inferred** — interpretation by `claude-opus-5-5`; `rationale` explains what is guessed and why.

Each rule carries `formula` (machine-readable `expr`/`table`/`params` JSON where applicable), `verification {method, result, detail}`, `engine_status` (+ `engine_note`) and evidence with a precise `locator` (sheet!cell, key=, VBA module, block extents, code symbol).

## Counts by category × trust

| Category | verified | stated | inferred | total |
|---|---:|---:|---:|---:|
| architecture | 0 | 5 | 0 | 5 |
| assets | 0 | 1 | 0 | 1 |
| bolting | 6 | 18 | 4 | 28 |
| bom | 2 | 4 | 3 | 9 |
| commands | 0 | 1 | 0 | 1 |
| connections | 5 | 17 | 4 | 26 |
| detailing | 0 | 1 | 0 | 1 |
| drafting | 0 | 9 | 5 | 14 |
| fabrication | 1 | 6 | 6 | 13 |
| finishing | 2 | 1 | 0 | 3 |
| materials | 4 | 2 | 0 | 6 |
| numbering | 0 | 18 | 1 | 19 |
| sections | 0 | 2 | 0 | 2 |
| welding | 0 | 6 | 1 | 7 |

## Verification results

| ID | Method | Result | Detail |
|---|---|---|---|
| PD-004 | cross-source | **pass** | SHOLE 22 = M20 + 2 per BT-001 hole rule and DetailRules default (DR-003); M20 is the dominant SCSS bolt (162/350 .dat bolt lines). |
| PD-005 | cross-source | **pass** | ENDGAGE 40 equals 단중.xlsx!SCSS edge columns AA/AE/AH = 40 on 324/350 rows. |
| PD-021 | cross-source | **pass** | m32-splise-gap-box 5 = SCSS-*.dat S22 = 5 on 175/175 rows = constant +5 in 단중.xlsx!SCSS!AK5 formula. |
| SP-006 | cross-source | **pass** | S22 = 5 on 175/175 SCSS .dat rows; Project.dat m32-splise-gap-box = 5; 단중.xlsx!SCSS!AK uses +5. |
| SP-008 | cross-source | **partial** | 2·(n+1)·(m+1) web bolt identity holds on 292/350 단중.xlsx!SCSS rows (AM=0); engine notes S25 agrees on 148/175 .dat rows. |
| RB-002 | recompute | **pass** | Density 7.85 now independently verified by WT-001/WT-002 (BH 159/159, H 81/81, FLAT-BAR 40/41 vs section .dat tables). |
| RB-003 | cross-source | **fail** | Spec loss_rule differs from VBA 커팅플랜계산 (kerf 3 mm per cut, stock rounding by 1000 mm increments). |
| BL-001 | numeric-fit | **pass** | SCSS-*.dat: 350/350 bolt lengths reproduced; 단중.xlsx!SCSS: 700/700; every resulting TS length exists in the TS bolt table (31/31). |
| BL-002 | numeric-fit | **pass** | Unique add value per diameter reproduces 350/350 .dat bolt lengths (flange grip tf+S11+S12, web grip tw+2·S1). |
| BL-003 | numeric-fit | **pass** | 700/700 rows fit; vs .dat the same (section, dia) differs in flange length on 174/175 rows (always +5). |
| BL-004 | numeric-fit | **pass** | These grip definitions are the ones under which BL-001 fits 100% of both tables. |
| BL-006 | recompute | **pass** | Cached JOINT-BASE!AG2 = 740, AG7 = 650 reproduce from the row inputs (same workbook; non-independent). |
| HL-001 | code-read | **fail** | Two engine call paths give different holes for d ≥ 24. |
| HL-002 | geometry | **pass** | half-height = d/2 on 11/11 blocks; half-length = L/2 on 8/11 (D18x40, D18x48, D26x56 differ). |
| SPL-001 | cross-source | **pass** | Key shape in 단중.xlsx matches the 6 .dat file names (role × {16,20,22}). |
| SPL-002 | identity-check | **pass** | Residual AK = 0 on 350/350 rows (plate lengths are hand-entered literals, check formulas are separate). |
| SPL-003 | identity-check | **pass** | Residual AL = 0 on 350/350 rows. |
| SPL-004 | identity-check | **partial** | Residual AM = 0 on 292/350 rows; remaining rows have hand-edited bolt counts. |
| SPL-005 | cross-source | **partial** | Matched 175 (section,d) pairs: flange t agree 172, web t agree 152. |
| SPL-006 | cross-source | **pass** | Recomputed 350/350 cached plate weights; density cross-checked by WT-001. |
| WT-001 | cross-source | **pass** | BH-BEAM.dat 159/159, H-BEAM.dat 81/81, FLAT-BAR.dat 40/41 (F915x3.2 is a mislabelled row). |
| WT-002 | cross-source | **pass** | BH-BEAM.dat 159/159, H-BEAM.dat 81/81 within max(0.1, 0.5%). |
| WT-003 | cross-source | **pass** | BH-BEAM.dat 159/159, H-BEAM.dat 81/81 within 0.011. |
| WT-004 | cross-source | **pass** | Workbook VBA, SCSS formula and engine agree; FLAT-BAR table 40/41. |
| WT-005 | cross-source | **pass** | 자재산출서 literal O = M×N on 8/8 rows. |
| WT-006 | cross-source | **pass** | Q = M × HLCCT paint on 8/8 rows. |
| BOM-003 | recompute | **pass** | 16868 kg subtotal + 16868 rows = 33736 × 0.0005 = 16.868 t. |
| QT-001 | cross-source | **pass** | 2017 VBA and the BOM template's 사용자 sheet agree; cached J2:J9 reproduce 8/8. |
| NUM-001 | cross-source | **fail** | Engine AssemblyTypes agrees for C/G/B/PU/GT only; SC, PT, RF, TR, CG, BR, ST, HR differ. |
| DFT-001 | cross-source | **fail** | Project.dat m32-dim-txt-box = 3 and engine DR-007 text 2.5 disagree with DIM-100 text 3.4; name says 1:100 but DIMSCALE is 50. |

## Engine differences worth fixing

- **DR-007** DetailRules text height 2.5 / dim gap 7 — Template DIM-100 text height 3.4 and Project.dat m32-dim-txt-box 3 vs engine 2.5 (see DFT-001).
- **SP-003** HoleDia = BoltDia+2 in pattern — BoltPattern.HoleDia is always d+2; Bolts.HoleFor gives d+3 from M24 (see HL-001).
- **AS-SC** Assembly mark prefix SubColumn→SC — Legacy Numbering.dat M83-SUBCOL-HD-BOX = C001 (engine SC).
- **AS-PT** Assembly mark prefix Post→PT — Legacy Numbering.dat M83-POST---HD-BOX = C001 (engine PT).
- **AS-CG** Assembly mark prefix CraneGirder→CG — Legacy Numbering.dat M83-CRANEG-HD-BOX = G001 (engine CG).
- **AS-BR** Assembly mark prefix Brace→BR — Legacy Numbering.dat M83-BRACE--HD-BOX = R001 (engine BR).
- **AS-RF** Assembly mark prefix Rafter→RF — Legacy Numbering.dat M83-RAFTER-HD-BOX = G001 (engine RF).
- **AS-TR** Assembly mark prefix Truss→TR — Legacy Numbering.dat M83-TRUSS--HD-BOX = G001 (engine TR).
- **AS-ST** Assembly mark prefix Stair→ST — Legacy Numbering.dat M83-STAIR--HD-BOX = S001 (engine ST).
- **AS-HR** Assembly mark prefix HandRail→HR — Legacy Numbering.dat M83-HANDRL-HD-BOX = H001 (engine HR).
- **HL-001** Engine hole-diameter rules disagree from M24 — Fix: BoltPattern.HoleDia should delegate to Bolts.HoleFor.
- **WT-002** H/BH unit weight formula — Table lookup is used first; the parsed-spec fallback ignores the root fillet (4−π)r² and rounds to 2 dp.
- **NUM-001** Legacy assembly mark heads (Numbering.dat) — Decide whether to keep engine prefixes or honour Numbering.dat heads (make them configurable).
- **DFT-001** Template dimension style DIM-100 — Engine paper text is 2.5 mm; pick one canonical dimension text height (3 or 3.4) and set DIMSCALE from the view scale.

High-value *missing* engine features: grip-based bolt length (BL-001/002), material markup (QT-001), cutting plan (CUT-001..004), anchor bolt length (BL-006), splice plate geometry checks (SPL-002/003), stud quantities (BOM-002).

## Full index

| ID | Category | Trust | Verification | Engine | Title |
|---|---|---|---|---|---|
| PD-001 | detailing | stated | unverified | implemented | Project.dat WDGAP |
| PD-002 | fabrication | stated | unverified | implemented | Project.dat SCALLOP |
| PD-003 | fabrication | inferred | unverified | missing | Project.dat RCCR |
| PD-004 | bolting | verified | pass | implemented | Project.dat SHOLE |
| PD-005 | bolting | verified | pass | implemented | Project.dat ENDGAGE |
| PD-006 | bolting | inferred | unverified | missing | Project.dat HOLELEN |
| PD-007 | fabrication | inferred | unverified | missing | Project.dat stffsqgage |
| PD-008 | fabrication | inferred | unverified | missing | Project.dat STFFGAP |
| PD-009 | fabrication | inferred | unverified | missing | Project.dat STFFIN |
| PD-010 | materials | stated | unverified | differs | Project.dat SWS |
| PD-011 | welding | inferred | unverified | missing | Project.dat WELDTIP |
| PD-012 | finishing | stated | unverified | missing | Project.dat PAINT |
| PD-013 | fabrication | inferred | unverified | missing | Project.dat SET_COL_STFF_JBX |
| PD-014 | bom | inferred | unverified | missing | Project.dat SET_ASY_BOMLEN |
| PD-015 | drafting | stated | unverified | missing | Project.dat hs-dimrnd-box |
| PD-016 | drafting | stated | unverified | missing | Project.dat m32-dim-txt-box |
| PD-017 | drafting | stated | unverified | missing | Project.dat m32-marktxt-box |
| PD-018 | drafting | inferred | unverified | missing | Project.dat m32-gridasy-box |
| PD-019 | drafting | inferred | unverified | missing | Project.dat m32-griddan-box |
| PD-020 | drafting | stated | unverified | missing | Project.dat m32-bom-txt-box |
| PD-021 | connections | verified | pass | missing | Project.dat m32-splise-gap-box |
| PD-022 | connections | inferred | unverified | missing | Project.dat m32-share1-gap-box |
| PD-023 | connections | inferred | unverified | missing | Project.dat m32-share2-gap-box |
| PD-024 | connections | inferred | unverified | missing | Project.dat m32-pipejun-ang-box |
| PD-025 | connections | inferred | unverified | missing | Project.dat m32-pipejun-las-123 |
| PD-026 | drafting | inferred | unverified | missing | Project.dat m32-dimtad-pop |
| PD-027 | drafting | inferred | unverified | missing | Project.dat m32-dimdec-pop |
| PD-028 | drafting | inferred | unverified | missing | Project.dat m32-dimlunit-pop |
| PD-029 | bolting | inferred | unverified | missing | Project.dat m32-gitabolt-pop |
| PD-030 | bolting | inferred | unverified | missing | Project.dat m32-spgpbolt-pop |
| PD-031 | bom | inferred | unverified | missing | Project.dat set-bom-matl-elss-tog |
| DR-001 | fabrication | stated | unverified | implemented | DetailRules scallop default 30 |
| DR-002 | bolting | stated | unverified | implemented | DetailRules end gauge default 40 |
| DR-003 | bolting | stated | unverified | implemented | DetailRules hole dia default 22 |
| DR-004 | welding | stated | unverified | implemented | DetailRules weld gap default 5 |
| DR-005 | materials | stated | unverified | differs | DetailRules material default SS275 |
| DR-006 | connections | stated | unverified | implemented | DetailRules connection gap default 10 |
| DR-007 | drafting | stated | unverified | differs | DetailRules text height 2.5 / dim gap 7 |
| WL-6 | welding | stated | unverified | implemented | Min fillet leg for t≤6 |
| WL-12 | welding | stated | unverified | implemented | Min fillet leg for t≤12 |
| WL-20 | welding | stated | unverified | implemented | Min fillet leg for t≤20 |
| WL-999 | welding | stated | unverified | implemented | Min fillet leg for t≤>20 |
| WL-CAP | welding | stated | unverified | implemented | Fillet leg capped by thinner part |
| BT-001 | bolting | stated | unverified | implemented | Standard hole bolt+2 (≤M22) |
| BT-002 | bolting | stated | unverified | implemented | Standard hole bolt+3 (≥M24) |
| BT-003 | bolting | stated | unverified | implemented | TS / S10T grade mapping |
| BT-004 | bolting | stated | unverified | implemented | HTB / F10T grade mapping |
| BT-005 | bolting | stated | unverified | implemented | Ordinary M-BOLT grade |
| BT-006 | bolting | stated | unverified | implemented | Unknown bolt name defaults F10T |
| GG-001 | bolting | stated | unverified | implemented | Angle gauge AIJ/KS table |
| GG-002 | bolting | stated | unverified | implemented | H/T flange gauge table |
| GG-003 | bolting | stated | unverified | implemented | Minimum edge distance practice 1.5d / 20 |
| GG-004 | bolting | stated | unverified | implemented | Default bolt by profile family |
| GG-005 | bolting | stated | unverified | implemented | I/Channel web pitch 3d to 10mm |
| ST-001 | connections | stated | unverified | implemented | Clear web T=D-2(tf+r) |
| ST-002 | connections | stated | unverified | implemented | Shop edge distance 40 mm |
| ST-003 | connections | stated | unverified | implemented | Shop pitch 70 mm / 3d |
| ST-004 | connections | stated | unverified | implemented | Sheared edge M20 = 34 mm |
| ST-005 | connections | stated | unverified | implemented | Rows n=floor((T-2e)/p)+1 |
| ST-006 | connections | stated | unverified | implemented | n<2 flagged |
| ST-007 | connections | stated | unverified | implemented | Prefer splice-standard WebY rows |
| SP-001 | connections | stated | unverified | implemented | BoltPattern grammar |
| SP-002 | connections | stated | unverified | implemented | BoltAxis nAp group |
| SP-003 | bolting | stated | unverified | differs | HoleDia = BoltDia+2 in pattern |
| SP-004 | connections | stated | unverified | implemented | SCSS S1–S10 web plate fields |
| SP-005 | connections | stated | unverified | implemented | SCSS S11–S21 flange fields |
| SP-006 | connections | verified | pass | implemented | SCSS S22 splice gap |
| SP-007 | connections | stated | unverified | implemented | Flange lines from S23 |
| SP-008 | connections | stated | partial | implemented | Web bolt count 2×rows×cols |
| SP-009 | connections | stated | unverified | implemented | Splice Find prefers bolt size then 22/20/16 |
| AS-C | numbering | stated | unverified | implemented | Assembly mark prefix Column→C |
| AS-SC | numbering | stated | unverified | differs | Assembly mark prefix SubColumn→SC |
| AS-PT | numbering | stated | unverified | differs | Assembly mark prefix Post→PT |
| AS-G | numbering | stated | unverified | implemented | Assembly mark prefix Girder→G |
| AS-B | numbering | stated | unverified | implemented | Assembly mark prefix Beam→B |
| AS-CG | numbering | stated | unverified | differs | Assembly mark prefix CraneGirder→CG |
| AS-BR | numbering | stated | unverified | differs | Assembly mark prefix Brace→BR |
| AS-PU | numbering | stated | unverified | implemented | Assembly mark prefix Purlin→PU |
| AS-GT | numbering | stated | unverified | implemented | Assembly mark prefix Girth→GT |
| AS-RF | numbering | stated | unverified | differs | Assembly mark prefix Rafter→RF |
| AS-TR | numbering | stated | unverified | differs | Assembly mark prefix Truss→TR |
| AS-ST | numbering | stated | unverified | differs | Assembly mark prefix Stair→ST |
| AS-HR | numbering | stated | unverified | differs | Assembly mark prefix HandRail→HR |
| AS-EM | numbering | stated | unverified | implemented | Assembly mark prefix Embed→EM |
| AS-LYR | numbering | stated | unverified | implemented | RAFTER layer alias FAFTER |
| DRF-001 | drafting | stated | unverified | implemented | Weld leader min 3 paper mm |
| DRF-002 | drafting | stated | unverified | implemented | HS-KOR text style txt+whgtxt |
| DRF-003 | drafting | stated | unverified | implemented | Empty text excluded from power-cad payload |
| RB-001 | bom | stated | unverified | n/a | BOM workbook 9 sheets |
| RB-002 | materials | verified | pass | n/a | Weight table density 7.85 unverified |
| RB-003 | fabrication | inferred | fail | n/a | Cut plan loss spec exists |
| RB-004 | commands | stated | unverified | n/a | Command catalog 302 |
| RB-005 | assets | stated | unverified | n/a | dwg_blocks_v2 verified |
| RB-006 | architecture | stated | unverified | n/a | VLX not decompiled into engine |
| RB-007 | architecture | stated | unverified | n/a | Dongle DLLs excluded |
| SEC-001 | sections | stated | unverified | implemented | 14 steel families in D2 |
| SEC-002 | sections | stated | unverified | implemented | Section catalog skips Project and SCSS |
| ARCH-001 | architecture | stated | unverified | n/a | Model-first input |
| ARCH-002 | architecture | stated | unverified | n/a | No AutoCAD.NET in Drafting |
| ARCH-003 | architecture | stated | unverified | n/a | Deterministic rules |
| BL-001 | bolting | verified | pass | missing | Bolt length = ROUNDUP((grip + add(d))/5)*5 |
| BL-002 | bolting | verified | pass | missing | Bolt add-length table (HS-STEEL SCSS .dat) |
| BL-003 | bolting | verified | pass | n/a | Bolt add-length table (단중.xlsx SCSS) — +5 vs program |
| BL-004 | bolting | verified | pass | missing | Splice bolt grip definitions |
| BL-005 | bolting | stated | unverified | n/a | BOLTADDLEN named range columns (HTB/TS/TUB) — broken ref |
| BL-006 | bolting | stated | pass | missing | Anchor bolt length & naming |
| BL-007 | bolting | stated | unverified | missing | Bolt set weight table |
| BL-008 | bom | stated | unverified | missing | SCSS bolt weight per splice |
| HL-001 | bolting | stated | fail | differs | Engine hole-diameter rules disagree from M24 |
| HL-002 | bolting | inferred | pass | n/a | Hole/bolt symbol blocks D{d}x{L} |
| SPL-001 | connections | verified | pass | implemented | Splice table key = {C|G}{bolt d}{spec} |
| SPL-002 | connections | verified | pass | missing | Web splice plate length |
| SPL-003 | connections | verified | pass | missing | Web splice plate height |
| SPL-004 | connections | stated | partial | implemented | Web splice bolt count |
| SPL-005 | connections | stated | partial | implemented | Splice plate thickness per section (xlsx vs .dat) |
| SPL-006 | fabrication | verified | pass | implemented | Splice plate weight |
| WT-001 | materials | verified | pass | implemented | Steel density 7.85 (0.00785 kg/mm²/m) |
| WT-002 | materials | verified | pass | differs | H/BH unit weight formula |
| WT-003 | finishing | verified | pass | implemented | H/BH paint area per metre |
| WT-004 | materials | verified | pass | implemented | Plate weight = t × area × 7.85 |
| WT-005 | bom | verified | pass | implemented | BOM weight = length[m] × unit kg/m |
| WT-006 | finishing | verified | pass | implemented | BOM paint area = length[m] × paint m²/m |
| BOM-001 | numbering | stated | unverified | missing | BOM part name = {drawing}-{MARK}-{seq} |
| BOM-002 | bom | stated | unverified | missing | Shear stud quantity |
| BOM-003 | bom | inferred | pass | n/a | Cover-sheet tonnage factor 0.0005 |
| QT-001 | bom | verified | pass | missing | Material markup (할증): shapes 9 %, plates 12 %, bolts 3 % |
| QT-002 | bom | stated | unverified | missing | Markup override by ordered lengths |
| CUT-001 | fabrication | stated | unverified | missing | Cutting kerf 3 mm per cut |
| CUT-002 | fabrication | stated | unverified | missing | Stock lengths for cutting plan |
| CUT-003 | fabrication | stated | unverified | missing | Stock length formula |
| CUT-004 | fabrication | stated | unverified | missing | Cutting-plan loss thresholds |
| NUM-001 | numbering | stated | fail | differs | Legacy assembly mark heads (Numbering.dat) |
| NUM-002 | numbering | stated | unverified | implemented | Section spec key = 구분 + A*B*C*D |
| NUM-003 | numbering | inferred | unverified | missing | Numbering minimum assembly length / layers |
| DFT-001 | drafting | stated | fail | differs | Template dimension style DIM-100 |

## Statements

### PD-001 — Project.dat WDGAP

Default project setting WDGAP = 5: Weld fit-up gap (mm).

- trust: **stated** · verification: none / unverified — Single source (Project.dat key); meaning evident from key name.
- engine: implemented (`src/HsSteel.Domain/Model.cs:DetailRules.From`)
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=WDGAP` — ("WDGAP" 5)

### PD-002 — Project.dat SCALLOP

Default project setting SCALLOP = 30: Flange scallop/cope radius (mm).

- trust: **stated** · verification: none / unverified — Single source (Project.dat key); meaning evident from key name.
- engine: implemented (`src/HsSteel.Domain/Model.cs:DetailRules.From`)
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=SCALLOP` — ("SCALLOP" 30)

### PD-003 — Project.dat RCCR

Default project setting RCCR = 30: Column corner radius / related clearance (mm).

- trust: **inferred** · verification: none / unverified — Value read verbatim from Project.dat; meaning not corroborated.
- rationale (claude-opus-5-5): RCCR meaning (column corner radius/clearance) is guessed from the key name; no DCL label or manual page found.
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=RCCR` — ("RCCR" 30)

### PD-004 — Project.dat SHOLE

Default project setting SHOLE = 22: Default bolt hole diameter (mm).

- trust: **verified** · verification: cross-source / pass — SHOLE 22 = M20 + 2 per BT-001 hole rule and DetailRules default (DR-003); M20 is the dominant SCSS bolt (162/350 .dat bolt lines).
- engine: implemented (`src/HsSteel.Domain/Model.cs:DetailRules.From`)
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=SHOLE` — ("SHOLE" 22)

### PD-005 — Project.dat ENDGAGE

Default project setting ENDGAGE = 40: End distance to first bolt row (mm).

- trust: **verified** · verification: cross-source / pass — ENDGAGE 40 equals 단중.xlsx!SCSS edge columns AA/AE/AH = 40 on 324/350 rows.
- engine: implemented (`src/HsSteel.Domain/Model.cs:DetailRules.From`)
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=ENDGAGE` — ("ENDGAGE" 40)

### PD-006 — Project.dat HOLELEN

Default project setting HOLELEN = 60: Default slotted/long hole length related (mm).

- trust: **inferred** · verification: none / unverified — Value read verbatim from Project.dat; meaning not corroborated.
- rationale (claude-opus-5-5): HOLELEN read as slotted-hole length from the key name only.
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=HOLELEN` — ("HOLELEN" 60)

### PD-007 — Project.dat stffsqgage

Default project setting stffsqgage = 60: Stiffener square gauge (mm).

- trust: **inferred** · verification: none / unverified — Value read verbatim from Project.dat; meaning not corroborated.
- rationale (claude-opus-5-5): stffsqgage read as stiffener square gauge from the key name only.
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=stffsqgage` — ("stffsqgage" "60")

### PD-008 — Project.dat STFFGAP

Default project setting STFFGAP = 0: Stiffener gap toggle/value.

- trust: **inferred** · verification: none / unverified — Value read verbatim from Project.dat; meaning not corroborated.
- rationale (claude-opus-5-5): STFFGAP semantics (toggle vs value) not confirmed by any dialog/manual.
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=STFFGAP` — ("STFFGAP" 0)

### PD-009 — Project.dat STFFIN

Default project setting STFFIN = 0: Stiffener inset toggle/value.

- trust: **inferred** · verification: none / unverified — Value read verbatim from Project.dat; meaning not corroborated.
- rationale (claude-opus-5-5): STFFIN semantics (toggle vs value) not confirmed by any dialog/manual.
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=STFFIN` — ("STFFIN" 0)

### PD-010 — Project.dat SWS

Default project setting SWS = SS400: Default steel grade (legacy SS400; engine maps toward SS275).

- trust: **stated** · verification: none / unverified — Single source (Project.dat key); meaning evident from key name.
- engine: differs — Engine default SS275 vs Project.dat SS400 (intentional KS D 3503:2018 rename). (`src/HsSteel.Domain/Model.cs:DetailRules.From`)
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=SWS` — ("SWS" "SS400")

### PD-011 — Project.dat WELDTIP

Default project setting WELDTIP = AWS_EXXXX_GRADE: Weld tip/electrode grade placeholder.

- trust: **inferred** · verification: none / unverified — Value read verbatim from Project.dat; meaning not corroborated.
- rationale (claude-opus-5-5): WELDTIP value is a literal placeholder string; 'electrode grade' role inferred from the key name.
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=WELDTIP` — ("WELDTIP" "AWS_EXXXX_GRADE")

### PD-012 — Project.dat PAINT

Default project setting PAINT = SEE_PAINT_SPEC.: Paint specification note.

- trust: **stated** · verification: none / unverified — Single source (Project.dat key); meaning evident from key name.
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=PAINT` — ("PAINT" "SEE_PAINT_SPEC.")

### PD-013 — Project.dat SET_COL_STFF_JBX

Default project setting SET_COL_STFF_JBX = 75: Column stiffener J-box related size.

- trust: **inferred** · verification: none / unverified — Value read verbatim from Project.dat; meaning not corroborated.
- rationale (claude-opus-5-5): SET_COL_STFF_JBX: 'J-box related size' is a guess from the key name.
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=SET_COL_STFF_JBX` — ("SET_COL_STFF_JBX" 75)

### PD-014 — Project.dat SET_ASY_BOMLEN

Default project setting SET_ASY_BOMLEN = 73: Assembly BOM length field width/setting.

- trust: **inferred** · verification: none / unverified — Value read verbatim from Project.dat; meaning not corroborated.
- rationale (claude-opus-5-5): SET_ASY_BOMLEN: 'BOM length field width' is a guess from the key name.
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=SET_ASY_BOMLEN` — ("SET_ASY_BOMLEN" 73)

### PD-015 — Project.dat hs-dimrnd-box

Default project setting hs-dimrnd-box = 0.5: Dimension round-off (mm).

- trust: **stated** · verification: none / unverified — Single source (Project.dat key); meaning evident from key name.
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=hs-dimrnd-box` — ("hs-dimrnd-box" "0.5")

### PD-016 — Project.dat m32-dim-txt-box

Default project setting m32-dim-txt-box = 3: Dimension text height (paper mm).

- trust: **stated** · verification: none / unverified — Single source (Project.dat key); meaning evident from key name.
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=m32-dim-txt-box` — ("m32-dim-txt-box" 3)

### PD-017 — Project.dat m32-marktxt-box

Default project setting m32-marktxt-box = 1.6: Mark text height (paper mm).

- trust: **stated** · verification: none / unverified — Single source (Project.dat key); meaning evident from key name.
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=m32-marktxt-box` — ("m32-marktxt-box" 1.6)

### PD-018 — Project.dat m32-gridasy-box

Default project setting m32-gridasy-box = 5: Assembly grid text size.

- trust: **inferred** · verification: none / unverified — Value read verbatim from Project.dat; meaning not corroborated.
- rationale (claude-opus-5-5): Grid text size meaning of m32-gridasy-box inferred from key name (no DCL ingested).
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=m32-gridasy-box` — ("m32-gridasy-box" 5)

### PD-019 — Project.dat m32-griddan-box

Default project setting m32-griddan-box = 4: Part grid text size.

- trust: **inferred** · verification: none / unverified — Value read verbatim from Project.dat; meaning not corroborated.
- rationale (claude-opus-5-5): Grid text size meaning of m32-griddan-box inferred from key name (no DCL ingested).
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=m32-griddan-box` — ("m32-griddan-box" 4)

### PD-020 — Project.dat m32-bom-txt-box

Default project setting m32-bom-txt-box = 1.5: BOM table text height.

- trust: **stated** · verification: none / unverified — Single source (Project.dat key); meaning evident from key name.
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=m32-bom-txt-box` — ("m32-bom-txt-box" 1.5)

### PD-021 — Project.dat m32-splise-gap-box

Default project setting m32-splise-gap-box = 5: Splice gap (mm).

- trust: **verified** · verification: cross-source / pass — m32-splise-gap-box 5 = SCSS-*.dat S22 = 5 on 175/175 rows = constant +5 in 단중.xlsx!SCSS!AK5 formula.
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=m32-splise-gap-box` — ("m32-splise-gap-box" "5")

### PD-022 — Project.dat m32-share1-gap-box

Default project setting m32-share1-gap-box = 5: Shear connection gap setting 1 (mm).

- trust: **inferred** · verification: none / unverified — Value read verbatim from Project.dat; meaning not corroborated.
- rationale (claude-opus-5-5): share1 read as shear-connection gap 1 from key name only.
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=m32-share1-gap-box` — ("m32-share1-gap-box" "5")

### PD-023 — Project.dat m32-share2-gap-box

Default project setting m32-share2-gap-box = 5: Shear connection gap setting 2 (mm).

- trust: **inferred** · verification: none / unverified — Value read verbatim from Project.dat; meaning not corroborated.
- rationale (claude-opus-5-5): share2 read as shear-connection gap 2 from key name only.
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=m32-share2-gap-box` — ("m32-share2-gap-box" "5")

### PD-024 — Project.dat m32-pipejun-ang-box

Default project setting m32-pipejun-ang-box = 3: Pipe junction angle tolerance.

- trust: **inferred** · verification: none / unverified — Value read verbatim from Project.dat; meaning not corroborated.
- rationale (claude-opus-5-5): Pipe junction angle tolerance meaning inferred from key name.
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=m32-pipejun-ang-box` — ("m32-pipejun-ang-box" "3")

### PD-025 — Project.dat m32-pipejun-las-123

Default project setting m32-pipejun-las-123 = 3: Pipe junction LAS setting.

- trust: **inferred** · verification: none / unverified — Value read verbatim from Project.dat; meaning not corroborated.
- rationale (claude-opus-5-5): pipejun-las-123 meaning unknown beyond key name.
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=m32-pipejun-las-123` — ("m32-pipejun-las-123" "3")

### PD-026 — Project.dat m32-dimtad-pop

Default project setting m32-dimtad-pop = 2: DIMTAD dimension text above-line pop index.

- trust: **inferred** · verification: none / unverified — Value read verbatim from Project.dat; meaning not corroborated.
- rationale (claude-opus-5-5): *-pop values are popup-list indexes; mapping index→DIMTAD value is not documented in ingested DCL.
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=m32-dimtad-pop` — ("m32-dimtad-pop" "2")

### PD-027 — Project.dat m32-dimdec-pop

Default project setting m32-dimdec-pop = 1: DIMDEC decimal places pop index.

- trust: **inferred** · verification: none / unverified — Value read verbatim from Project.dat; meaning not corroborated.
- rationale (claude-opus-5-5): *-pop values are popup-list indexes; mapping index→DIMDEC value is not documented in ingested DCL.
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=m32-dimdec-pop` — ("m32-dimdec-pop" "1")

### PD-028 — Project.dat m32-dimlunit-pop

Default project setting m32-dimlunit-pop = 1: DIMLUNIT linear units pop index.

- trust: **inferred** · verification: none / unverified — Value read verbatim from Project.dat; meaning not corroborated.
- rationale (claude-opus-5-5): *-pop values are popup-list indexes; mapping index→DIMLUNIT value is not documented in ingested DCL.
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=m32-dimlunit-pop` — ("m32-dimlunit-pop" "1")

### PD-029 — Project.dat m32-gitabolt-pop

Default project setting m32-gitabolt-pop = 3: Gita/secondary bolt pop index.

- trust: **inferred** · verification: none / unverified — Value read verbatim from Project.dat; meaning not corroborated.
- rationale (claude-opus-5-5): gitabolt-pop is a popup index; bolt list order not in ingested DCL.
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=m32-gitabolt-pop` — ("m32-gitabolt-pop" "3")

### PD-030 — Project.dat m32-spgpbolt-pop

Default project setting m32-spgpbolt-pop = 0: Splice group bolt pop index.

- trust: **inferred** · verification: none / unverified — Value read verbatim from Project.dat; meaning not corroborated.
- rationale (claude-opus-5-5): spgpbolt-pop is a popup index; bolt list order not in ingested DCL.
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=m32-spgpbolt-pop` — ("m32-spgpbolt-pop" "0")

### PD-031 — Project.dat set-bom-matl-elss-tog

Default project setting set-bom-matl-elss-tog = 1: BOM material ELSS toggle on.

- trust: **inferred** · verification: none / unverified — Value read verbatim from Project.dat; meaning not corroborated.
- rationale (claude-opus-5-5): ELSS toggle meaning inferred from key name.
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=set-bom-matl-elss-tog` — ("set-bom-matl-elss-tog" "1")

### DR-001 — DetailRules scallop default 30

When Project.dat is missing SCALLOP, DetailRules.Scallop defaults to 30 mm.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Model.cs`)
- evidence: `src/HsSteel.Domain/Model.cs` @ `Model.cs` — DetailRules record default Scallop=30

### DR-002 — DetailRules end gauge default 40

EndGauge defaults to 40 mm (ENDGAGE).

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Model.cs`)
- evidence: `src/HsSteel.Domain/Model.cs` @ `Model.cs` — EndGauge=40

### DR-003 — DetailRules hole dia default 22

HoleDia defaults to 22 mm (SHOLE) matching M20 + 2.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Model.cs, src/HsSteel.Domain/Fabrication.cs`)
- evidence: `src/HsSteel.Domain/Model.cs` @ `Model.cs` — HoleDia=22
- evidence: `src/HsSteel.Domain/Fabrication.cs` @ `Fabrication.cs` — Bolts.HoleFor: bolt+2 up to M22

### DR-004 — DetailRules weld gap default 5

WeldGap defaults to 5 mm (WDGAP).

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Model.cs`)
- evidence: `src/HsSteel.Domain/Model.cs` @ `Model.cs` — WeldGap=5

### DR-005 — DetailRules material default SS275

Engine default Material is SS275 when SWS missing (legacy Project.dat often SS400).

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: differs — Engine default SS275 vs Project.dat SS400 (intentional KS D 3503:2018 rename). (`src/HsSteel.Domain/Model.cs`)
- evidence: `src/HsSteel.Domain/Model.cs` @ `Model.cs` — Material default SS275; Project.dat SWS=SS400

### DR-006 — DetailRules connection gap default 10

ConnectionGap (beam end to supporting web clearance) defaults to 10 mm.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Model.cs`)
- evidence: `src/HsSteel.Domain/Model.cs` @ `Model.cs` — ConnectionGap=10

### DR-007 — DetailRules text height 2.5 / dim gap 7

Paper text height 2.5 mm and dimension row gap 7 mm are drafting defaults.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: differs — Template DIM-100 text height 3.4 and Project.dat m32-dim-txt-box 3 vs engine 2.5 (see DFT-001). (`src/HsSteel.Domain/Model.cs`)
- evidence: `src/HsSteel.Domain/Model.cs` @ `Model.cs` — TextHeight=2.5 DimGap=7

### WL-6 — Min fillet leg for t≤6

Minimum fillet weld leg is 3 mm when thicker part thickness ≤ 6 mm (capped by thinner part).

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Fabrication.cs:WeldRules`)
- evidence: `src/HsSteel.Domain/Fabrication.cs` @ `Fabrication.cs:WeldRules` — WeldRules.MinFillet KDS 14 31 25 / AWS D1.1 Table 5.7
- evidence: `KDS 14 31 25` @ `KDS 14 31 25` — min fillet by thicker part

### WL-12 — Min fillet leg for t≤12

Minimum fillet weld leg is 5 mm when thicker part thickness ≤ 12 mm (capped by thinner part).

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Fabrication.cs:WeldRules`)
- evidence: `src/HsSteel.Domain/Fabrication.cs` @ `Fabrication.cs:WeldRules` — WeldRules.MinFillet KDS 14 31 25 / AWS D1.1 Table 5.7
- evidence: `KDS 14 31 25` @ `KDS 14 31 25` — min fillet by thicker part

### WL-20 — Min fillet leg for t≤20

Minimum fillet weld leg is 6 mm when thicker part thickness ≤ 20 mm (capped by thinner part).

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Fabrication.cs:WeldRules`)
- evidence: `src/HsSteel.Domain/Fabrication.cs` @ `Fabrication.cs:WeldRules` — WeldRules.MinFillet KDS 14 31 25 / AWS D1.1 Table 5.7
- evidence: `KDS 14 31 25` @ `KDS 14 31 25` — min fillet by thicker part

### WL-999 — Min fillet leg for t≤>20

Minimum fillet weld leg is 8 mm when thicker part thickness ≤ ∞ mm (capped by thinner part).

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Fabrication.cs:WeldRules`)
- evidence: `src/HsSteel.Domain/Fabrication.cs` @ `Fabrication.cs:WeldRules` — WeldRules.MinFillet KDS 14 31 25 / AWS D1.1 Table 5.7
- evidence: `KDS 14 31 25` @ `KDS 14 31 25` — min fillet by thicker part

### WL-CAP — Fillet leg capped by thinner part

FilletLeg(t1,t2) = min(table_min(max(t1,t2)), min(t1,t2)).

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Fabrication.cs`)
- evidence: `src/HsSteel.Domain/Fabrication.cs` @ `Fabrication.cs` — WeldRules.FilletLeg

### BT-001 — Standard hole bolt+2 (≤M22)

Standard hole diameter = bolt diameter + 2 mm for bolts up to M22.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Fabrication.cs:Bolts.HoleFor`)
- evidence: `src/HsSteel.Domain/Fabrication.cs` @ `Fabrication.cs:Bolts.HoleFor` — Bolts.HoleFor
- evidence: `Project.dat SHOLE` @ `Project.dat SHOLE` — 22 for M20

### BT-002 — Standard hole bolt+3 (≥M24)

Standard hole diameter = bolt diameter + 3 mm for bolts M24 and larger.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Fabrication.cs`)
- evidence: `src/HsSteel.Domain/Fabrication.cs` @ `Fabrication.cs` — Bolts.HoleFor boltDia>=24

### BT-003 — TS / S10T grade mapping

Bolt names starting with TS or containing S10T map to grade S10T (KS B 2819).

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Fabrication.cs`)
- evidence: `src/HsSteel.Domain/Fabrication.cs` @ `Fabrication.cs` — Bolts.GradeOf

### BT-004 — HTB / F10T grade mapping

HTB / F10T names map to F10T (KS B 1010); F8T preserved as F8T.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Fabrication.cs`)
- evidence: `src/HsSteel.Domain/Fabrication.cs` @ `Fabrication.cs` — Bolts.GradeOf

### BT-005 — Ordinary M-BOLT grade

Names starting with M / SB or containing 4.6 map to M-BOLT.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Fabrication.cs`)
- evidence: `src/HsSteel.Domain/Fabrication.cs` @ `Fabrication.cs` — Bolts.GradeOf

### BT-006 — Unknown bolt name defaults F10T

Unrecognized bolt names default to F10T.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Fabrication.cs`)
- evidence: `src/HsSteel.Domain/Fabrication.cs` @ `Fabrication.cs` — GradeOf return F10T

### GG-001 — Angle gauge AIJ/KS table

Angle leg gauges follow AIJ/KS table (e.g. L90→g1=50 max M24; L150→g1=55 g2=55).

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Fabrication.cs:BoltGauges`)
- evidence: `src/HsSteel.Domain/Fabrication.cs` @ `Fabrication.cs:BoltGauges` — BoltGauges.Angle

### GG-002 — H/T flange gauge table

Flange gauge between bolt lines: W200→120, W250→150, W300→150, W350→140.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Fabrication.cs`)
- evidence: `src/HsSteel.Domain/Fabrication.cs` @ `Fabrication.cs` — BoltGauges.Flange

### GG-003 — Minimum edge distance practice 1.5d / 20

BoltGauges.For uses e = max(1.5*bolt, 20) mm as minimum edge distance practice.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Fabrication.cs`)
- evidence: `src/HsSteel.Domain/Fabrication.cs` @ `Fabrication.cs` — BoltGauges.For e=max(1.5*bolt,20)

### GG-004 — Default bolt by profile family

DefaultBolt: L by angle max; lip/Z 12 or 16; flat by depth; box/pipe by size; channel/H by depth bands.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Fabrication.cs`)
- evidence: `src/HsSteel.Domain/Fabrication.cs` @ `Fabrication.cs` — BoltGauges.DefaultBolt

### GG-005 — I/Channel web pitch 3d to 10mm

For I/Channel end connections, web pitch = ceil(3d/10)*10, rows capped at 6.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Fabrication.cs`)
- evidence: `src/HsSteel.Domain/Fabrication.cs` @ `Fabrication.cs` — BoltGauges.For ShapeKind.I/Channel

### ST-001 — Clear web T=D-2(tf+r)

Shear-tab plate must fit clear web depth T = D − 2(tf + r).

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Modeling/ShearTabLayout.cs`)
- evidence: `docs/SHEAR-TAB-ROWS.md` @ `SHEAR-TAB-ROWS.md § clear web rule` — clear web rule
- evidence: `src/HsSteel.Modeling/ShearTabLayout.cs` @ `ShearTabLayout.cs` — ClearWebDepth

### ST-002 — Shop edge distance 40 mm

Vertical edge distance e ≥ max(40, MinEdgeSheared(d)).

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Modeling/ShearTabLayout.cs`)
- evidence: `docs/SHEAR-TAB-ROWS.md` @ `SHEAR-TAB-ROWS.md § ShopEdge=40` — ShopEdge=40
- evidence: `KDS 14 31 25` @ `KDS 14 31 25` — sheared edge table

### ST-003 — Shop pitch 70 mm / 3d

Pitch p ≥ max(70, ceil(3d/5)*5).

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Modeling/ShearTabLayout.cs`)
- evidence: `docs/SHEAR-TAB-ROWS.md` @ `SHEAR-TAB-ROWS.md § ShopPitch=70 preferred 3d` — ShopPitch=70 preferred 3d

### ST-004 — Sheared edge M20 = 34 mm

MinEdgeSheared(M20)=34, M16=28, M22=38, M24=42, M27=48, M30=52; >M30 → 1.75d.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Modeling/ShearTabLayout.cs`)
- evidence: `src/HsSteel.Modeling/ShearTabLayout.cs` @ `ShearTabLayout.cs` — MinEdgeSheared
- evidence: `AISC 360-05 Table J3.4M` @ `AISC 360-05 Table J3.4M` — same as KDS

### ST-005 — Rows n=floor((T-2e)/p)+1

Bolt rows n = ⌊(T−2e)/p⌋+1; plate height 2e+(n−1)p ≤ T, centred.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Modeling/ShearTabLayout.cs`)
- evidence: `docs/SHEAR-TAB-ROWS.md` @ `SHEAR-TAB-ROWS.md § row formula` — row formula

### ST-006 — n<2 flagged

Fewer than 2 rows flags warning (AISC Manual Part 10 conventional 2–12).

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Modeling/ShearTabLayout.cs`)
- evidence: `docs/SHEAR-TAB-ROWS.md` @ `SHEAR-TAB-ROWS.md § n<2 warning` — n<2 warning

### ST-007 — Prefer splice-standard WebY rows

ModelBuilder.ShearTab prefers SpliceStandards.WebY when present; else ShearTabLayout.Default.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Modeling/ModelBuilder.cs, src/HsSteel.Modeling/ShearTabLayout.cs`)
- evidence: `docs/SHEAR-TAB-ROWS.md` @ `SHEAR-TAB-ROWS.md § splice override` — splice override

### SP-001 — BoltPattern grammar

Plate/bolt string: thicknesses (*T)*, then X<axis>, Y<axis>, D<dia> in any order.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Connections.cs`)
- evidence: `src/HsSteel.Domain/Connections.cs` @ `Connections.cs` — BoltPattern.Parse

### SP-002 — BoltAxis nAp group

Axis term nAp means n intervals of pitch p → n+1 holes; plain terms are edges/gaps.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Connections.cs`)
- evidence: `src/HsSteel.Domain/Connections.cs` @ `Connections.cs` — BoltAxis.Parse

### SP-003 — HoleDia = BoltDia+2 in pattern

BoltPattern.HoleDia returns BoltDia+2 when BoltDia>0.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: differs — BoltPattern.HoleDia is always d+2; Bolts.HoleFor gives d+3 from M24 (see HL-001). (`src/HsSteel.Domain/Connections.cs`)
- evidence: `src/HsSteel.Domain/Connections.cs` @ `Connections.cs` — BoltPattern.HoleDia

### SP-004 — SCSS S1–S10 web plate fields

SCSS row: S1 web t; S2–S5 web X; S6–S9 web Y; S10 web bolt dia.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Connections.cs:SpliceSpec`)
- evidence: `src/HsSteel.Domain/Connections.cs` @ `Connections.cs:SpliceSpec` — SpliceSpec remarks
- evidence: `HSSTEEL/attributes/SCSS-*.dat` @ `SCSS-*.dat` — 6 tables 175 rows

### SP-005 — SCSS S11–S21 flange fields

S11/S12 flange plate t; S13 flange bolt dia; S14–S17 flange X; S18 gauge; S19 second gauge; S20–S21 edges.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Connections.cs`)
- evidence: `src/HsSteel.Domain/Connections.cs` @ `Connections.cs` — SpliceSpec

### SP-006 — SCSS S22 splice gap

S22 is splice gap (typically 5 mm; Project.dat m32-splise-gap-box).

- trust: **verified** · verification: cross-source / pass — S22 = 5 on 175/175 SCSS .dat rows; Project.dat m32-splise-gap-box = 5; 단중.xlsx!SCSS!AK uses +5.
- engine: implemented (`src/HsSteel.Domain/Connections.cs`)
- evidence: `src/HsSteel.Domain/Connections.cs` @ `Connections.cs` — Gap => N(22)
- evidence: `Project.dat` @ `key=gap` — m32-splise-gap-box 5

### SP-007 — Flange lines from S23

FlangeLines = max(2, FlangeBoltTotal/(4*FlangeX.Count)); 2 or 4 lines across flange.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Connections.cs`)
- evidence: `src/HsSteel.Domain/Connections.cs` @ `Connections.cs` — FlangeLines

### SP-008 — Web bolt count 2×rows×cols

WebBoltCount = 2 * WebX.Count * WebY.Count (both sides).

- trust: **stated** · verification: cross-source / partial — 2·(n+1)·(m+1) web bolt identity holds on 292/350 단중.xlsx!SCSS rows (AM=0); engine notes S25 agrees on 148/175 .dat rows.
- engine: implemented (`src/HsSteel.Domain/Connections.cs`)
- evidence: `src/HsSteel.Domain/Connections.cs` @ `Connections.cs` — WebBoltCount

### SP-009 — Splice Find prefers bolt size then 22/20/16

SpliceStandards.Find tries requested size then 22, 20, 16 for C or G tables.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Connections.cs`)
- evidence: `src/HsSteel.Domain/Connections.cs` @ `Connections.cs` — Find

### AS-C — Assembly mark prefix Column→C

AssemblyType.Column uses mark prefix 'C' and centre-line layer CL-3D-* convention.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Model.cs:AssemblyTypes`)
- evidence: `src/HsSteel.Domain/Model.cs` @ `Model.cs:AssemblyTypes` — AssemblyTypes.Prefix(Column)

### AS-SC — Assembly mark prefix SubColumn→SC

AssemblyType.SubColumn uses mark prefix 'SC' and centre-line layer CL-3D-* convention.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: differs — Legacy Numbering.dat M83-SUBCOL-HD-BOX = C001 (engine SC). (`src/HsSteel.Domain/Model.cs:AssemblyTypes`)
- evidence: `src/HsSteel.Domain/Model.cs` @ `Model.cs:AssemblyTypes` — AssemblyTypes.Prefix(SubColumn)

### AS-PT — Assembly mark prefix Post→PT

AssemblyType.Post uses mark prefix 'PT' and centre-line layer CL-3D-* convention.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: differs — Legacy Numbering.dat M83-POST---HD-BOX = C001 (engine PT). (`src/HsSteel.Domain/Model.cs:AssemblyTypes`)
- evidence: `src/HsSteel.Domain/Model.cs` @ `Model.cs:AssemblyTypes` — AssemblyTypes.Prefix(Post)

### AS-G — Assembly mark prefix Girder→G

AssemblyType.Girder uses mark prefix 'G' and centre-line layer CL-3D-* convention.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Model.cs:AssemblyTypes`)
- evidence: `src/HsSteel.Domain/Model.cs` @ `Model.cs:AssemblyTypes` — AssemblyTypes.Prefix(Girder)

### AS-B — Assembly mark prefix Beam→B

AssemblyType.Beam uses mark prefix 'B' and centre-line layer CL-3D-* convention.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Model.cs:AssemblyTypes`)
- evidence: `src/HsSteel.Domain/Model.cs` @ `Model.cs:AssemblyTypes` — AssemblyTypes.Prefix(Beam)

### AS-CG — Assembly mark prefix CraneGirder→CG

AssemblyType.CraneGirder uses mark prefix 'CG' and centre-line layer CL-3D-* convention.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: differs — Legacy Numbering.dat M83-CRANEG-HD-BOX = G001 (engine CG). (`src/HsSteel.Domain/Model.cs:AssemblyTypes`)
- evidence: `src/HsSteel.Domain/Model.cs` @ `Model.cs:AssemblyTypes` — AssemblyTypes.Prefix(CraneGirder)

### AS-BR — Assembly mark prefix Brace→BR

AssemblyType.Brace uses mark prefix 'BR' and centre-line layer CL-3D-* convention.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: differs — Legacy Numbering.dat M83-BRACE--HD-BOX = R001 (engine BR). (`src/HsSteel.Domain/Model.cs:AssemblyTypes`)
- evidence: `src/HsSteel.Domain/Model.cs` @ `Model.cs:AssemblyTypes` — AssemblyTypes.Prefix(Brace)

### AS-PU — Assembly mark prefix Purlin→PU

AssemblyType.Purlin uses mark prefix 'PU' and centre-line layer CL-3D-* convention.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Model.cs:AssemblyTypes`)
- evidence: `src/HsSteel.Domain/Model.cs` @ `Model.cs:AssemblyTypes` — AssemblyTypes.Prefix(Purlin)

### AS-GT — Assembly mark prefix Girth→GT

AssemblyType.Girth uses mark prefix 'GT' and centre-line layer CL-3D-* convention.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Model.cs:AssemblyTypes`)
- evidence: `src/HsSteel.Domain/Model.cs` @ `Model.cs:AssemblyTypes` — AssemblyTypes.Prefix(Girth)

### AS-RF — Assembly mark prefix Rafter→RF

AssemblyType.Rafter uses mark prefix 'RF' and centre-line layer CL-3D-* convention.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: differs — Legacy Numbering.dat M83-RAFTER-HD-BOX = G001 (engine RF). (`src/HsSteel.Domain/Model.cs:AssemblyTypes`)
- evidence: `src/HsSteel.Domain/Model.cs` @ `Model.cs:AssemblyTypes` — AssemblyTypes.Prefix(Rafter)

### AS-TR — Assembly mark prefix Truss→TR

AssemblyType.Truss uses mark prefix 'TR' and centre-line layer CL-3D-* convention.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: differs — Legacy Numbering.dat M83-TRUSS--HD-BOX = G001 (engine TR). (`src/HsSteel.Domain/Model.cs:AssemblyTypes`)
- evidence: `src/HsSteel.Domain/Model.cs` @ `Model.cs:AssemblyTypes` — AssemblyTypes.Prefix(Truss)

### AS-ST — Assembly mark prefix Stair→ST

AssemblyType.Stair uses mark prefix 'ST' and centre-line layer CL-3D-* convention.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: differs — Legacy Numbering.dat M83-STAIR--HD-BOX = S001 (engine ST). (`src/HsSteel.Domain/Model.cs:AssemblyTypes`)
- evidence: `src/HsSteel.Domain/Model.cs` @ `Model.cs:AssemblyTypes` — AssemblyTypes.Prefix(Stair)

### AS-HR — Assembly mark prefix HandRail→HR

AssemblyType.HandRail uses mark prefix 'HR' and centre-line layer CL-3D-* convention.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: differs — Legacy Numbering.dat M83-HANDRL-HD-BOX = H001 (engine HR). (`src/HsSteel.Domain/Model.cs:AssemblyTypes`)
- evidence: `src/HsSteel.Domain/Model.cs` @ `Model.cs:AssemblyTypes` — AssemblyTypes.Prefix(HandRail)

### AS-EM — Assembly mark prefix Embed→EM

AssemblyType.Embed uses mark prefix 'EM' and centre-line layer CL-3D-* convention.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Model.cs:AssemblyTypes`)
- evidence: `src/HsSteel.Domain/Model.cs` @ `Model.cs:AssemblyTypes` — AssemblyTypes.Prefix(Embed)

### AS-LYR — RAFTER layer alias FAFTER

Layer CL-3D-FAFTER maps to AssemblyType.Rafter.

- trust: **stated** · verification: code-read / unverified — Engine constant/table; external standard (KDS/AISC/AIJ) cited but not re-checked against a second source.
- engine: implemented (`src/HsSteel.Domain/Model.cs`)
- evidence: `src/HsSteel.Domain/Model.cs` @ `Model.cs` — FromLayer FAFTER

### DRF-001 — Weld leader min 3 paper mm

Callouts.LeaderIfLongEnough omits leaders shorter than 3 paper mm × scale (COLLAB #3).

- trust: **stated** · verification: doc / unverified — Stated in project docs/ledger; not re-derived.
- engine: implemented (`src/HsSteel.Drafting`)
- evidence: `docs/COLLAB.md` @ `COLLAB.md § issue #3` — issue #3
- evidence: `docs/D4_RESULTS.md` @ `D4_RESULTS.md § LeaderIfLongEnough` — LeaderIfLongEnough

### DRF-002 — HS-KOR text style txt+whgtxt

Korean text style HS-KOR uses txt.shx + bigfont whgtxt.shx (power-cad must create styles).

- trust: **stated** · verification: doc / unverified — Stated in project docs/ledger; not re-derived.
- engine: implemented (`src/HsSteel.Drafting`)
- evidence: `docs/COLLAB.md` @ `COLLAB.md § Claude-sonnet #4` — Claude-sonnet #4

### DRF-003 — Empty text excluded from power-cad payload

DrawingsToPowerCad drops text/mtext with empty string to avoid cad_create_many rejection.

- trust: **stated** · verification: doc / unverified — Stated in project docs/ledger; not re-derived.
- engine: implemented (`src/HsSteel.Mcp/AssetTools.cs`)
- evidence: `docs/COLLAB.md` @ `COLLAB.md § live verify #1` — live verify #1

### RB-001 — BOM workbook 9 sheets

Legacy BOM workbook schema targets 9 sheets (BomList, AssyList, cover, by-spec totals, …).

- trust: **stated** · verification: doc / unverified — Stated in project docs/ledger; not re-derived.
- engine: n/a
- evidence: `HS-STEEL_REBORN/orch/bom_schema.json` @ `bom_schema.json` — derived_spec trust unverified
- evidence: `docs/REBORN_LEDGER.md` @ `REBORN_LEDGER.md § ingest` — ingest

### RB-002 — Weight table density 7.85 unverified

REBORN weight_table agrees 775/775 vs section kg/m ±0.5%, but density 7.85 assumption remains unverified trust.

- trust: **verified** · verification: recompute / pass — Density 7.85 now independently verified by WT-001/WT-002 (BH 159/159, H 81/81, FLAT-BAR 40/41 vs section .dat tables).
- engine: n/a
- evidence: `docs/REBORN_LEDGER.md` @ `REBORN_LEDGER.md § reborn_weight_crosscheck` — reborn_weight_crosscheck
- evidence: `meta.reborn_weight_crosscheck` @ `meta.reborn_weight_crosscheck` — DB meta

### RB-003 — Cut plan loss spec exists

형강커팅플랜 cut_plan_spec defines cutting loss rules (derived, unverified).

- trust: **inferred** · verification: cross-source / fail — Spec loss_rule differs from VBA 커팅플랜계산 (kerf 3 mm per cut, stock rounding by 1000 mm increments).
- rationale (claude-opus-5-5): REBORN cut_plan_spec summarises loss as quotient/remainder; BOM자재산출서.xlsm VBA 형강커팅플랜.bas actually does priority-stock bin packing with kerf (see CUT-001..CUT-004).
- engine: n/a
- evidence: `HS-STEEL_REBORN/orch/cut_plan_spec.json` @ `cut_plan_spec.json` — derived_spec

### RB-004 — Command catalog 302

REBORN command_triage_v2 lists 302 commands; 232 linked to our command nodes.

- trust: **stated** · verification: doc / unverified — Stated in project docs/ledger; not re-derived.
- engine: n/a
- evidence: `docs/REBORN_LEDGER.md` @ `REBORN_LEDGER.md § 232/302 described_by` — 232/302 described_by

### RB-005 — dwg_blocks_v2 verified

Only derived_spec with trust=verified is dwg_blocks_v2 (113 basenames match block table).

- trust: **stated** · verification: doc / unverified — Stated in project docs/ledger; not re-derived.
- engine: n/a
- evidence: `docs/REBORN_LEDGER.md` @ `REBORN_LEDGER.md § verified 1` — verified 1

### RB-006 — VLX not decompiled into engine

hs02.VLX / HS-DETAIL.VLX are reference-only; behaviour reconstructed from FAS symbols and dialogs, not bytecode port.

- trust: **stated** · verification: doc / unverified — Stated in project docs/ledger; not re-derived.
- engine: n/a
- evidence: `docs/ARCHITECTURE.md` @ `ARCHITECTURE.md § clean-room` — clean-room
- evidence: `docs/ASSET_LEDGER.md` @ `ASSET_LEDGER.md § VLX reference` — VLX reference

### RB-007 — Dongle DLLs excluded

CSROCKEY2013 / r4nd_class / HsRockey are excluded (license dongle), never redistributed.

- trust: **stated** · verification: doc / unverified — Stated in project docs/ledger; not re-derived.
- engine: n/a
- evidence: `docs/ASSET_LEDGER.md` @ `ASSET_LEDGER.md § excluded license` — excluded license

### SEC-001 — 14 steel families in D2

Engine covers 14 families: H/BH/LH/PEB/I/T/ANGLE/CHANNEL/C-CHANNEL/Z/SQ-PIPE/STEEL-PIPE/ROUND-BAR/FLAT-BAR (+ plate).

- trust: **stated** · verification: doc / unverified — Stated in project docs/ledger; not re-derived.
- engine: implemented (`src/HsSteel.Domain/Profile.cs`)
- evidence: `docs/FEATURE_EXPLORATION.md` @ `FEATURE_EXPLORATION.md § D2 list` — D2 list

### SEC-002 — Section catalog skips Project and SCSS

SectionCatalog.Load skips Project.dat and SCSS-*.dat when scanning attributes.

- trust: **stated** · verification: doc / unverified — Stated in project docs/ledger; not re-derived.
- engine: implemented (`src/HsSteel.Domain/Model.cs`)
- evidence: `src/HsSteel.Domain/Model.cs` @ `Model.cs` — SectionCatalog.Load

### ARCH-001 — Model-first input

Primary input is the 3D/project model; drawings are projections (ARCHITECTURE §1).

- trust: **stated** · verification: doc / unverified — Stated in project docs/ledger; not re-derived.
- engine: n/a
- evidence: `docs/ARCHITECTURE.md` @ `ARCHITECTURE.md § model-first` — model-first

### ARCH-002 — No AutoCAD.NET in Drafting

Drafting emits DrawPlan/DXF only; AutoCAD execution via power-cad, not AutoCAD.NET in Drafting.

- trust: **stated** · verification: doc / unverified — Stated in project docs/ledger; not re-derived.
- engine: n/a
- evidence: `docs/ARCHITECTURE.md` @ `ARCHITECTURE.md § Drafting rule` — Drafting rule

### ARCH-003 — Deterministic rules

Same input → same DrawPlan; tests lock behaviour.

- trust: **stated** · verification: doc / unverified — Stated in project docs/ledger; not re-derived.
- engine: n/a
- evidence: `docs/ARCHITECTURE.md` @ `ARCHITECTURE.md § design principles` — design principles

### BL-001 — Bolt length = ROUNDUP((grip + add(d))/5)*5

High-strength (TS/HTB/TUB) bolt length is the grip plus a diameter-dependent add-length, rounded up to the next 5 mm.

- trust: **verified** · verification: numeric-fit / pass — SCSS-*.dat: 350/350 bolt lengths reproduced; 단중.xlsx!SCSS: 700/700; every resulting TS length exists in the TS bolt table (31/31).
- formula: `{"expr": "ceil((grip + add[d]) / 5) * 5", "vars": {"grip": "mm", "d": "bolt diameter mm"}, "returns": "bolt length mm", "table_ref": ["BL-002", "BL-003"]}`
- engine: missing — Engine takes tabulated bolt names (SpliceSpec.FlangeBolt/WebBolt = S24/S26); no grip-based length computation.
- evidence: `C:\HS-STEEL\HS03\Excel\XLSTART\단중.xlsx` @ `SCSS!F5:F547,T5:T547 (bolt lengths) vs I/M (plate t) and B (section)` — lengths fit the rule on every row
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\SCSS-G20.dat` @ `S24/S26 bolt names vs S1/S11/S12` — HS-STEEL program splice tables
- evidence: `C:\HS-STEEL\HS03\견적용-2017\자재산출-2017.xlsm` @ `JOINT-BASE!AF2` — ROUNDUP((R2+VLOOKUP(Q2,BOLTADDLEN,3,0))/5,0)*5 — same shape, add-length from named range BOLTADDLEN

### BL-002 — Bolt add-length table (HS-STEEL SCSS .dat)

In the HS-STEEL program splice tables the add-length is M16 25, M20 30, M22 35 mm (grip + add, ceil to 5).

- trust: **verified** · verification: numeric-fit / pass — Unique add value per diameter reproduces 350/350 .dat bolt lengths (flange grip tf+S11+S12, web grip tw+2·S1).
- formula: `{"expr": "add[d]", "table": {"add": {"16": 25, "20": 30, "22": 35}}}`
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\SCSS-C16.dat` @ `S24/S26 vs S1/S11/S12, all rows`
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\SCSS-G22.dat` @ `S24/S26 vs S1/S11/S12, all rows`

### BL-003 — Bolt add-length table (단중.xlsx SCSS) — +5 vs program

The estimating workbook 단중.xlsx!SCSS uses add-lengths M16 30, M20 35, M22 40 mm, i.e. 5 mm longer bolts than the program .dat tables.

- trust: **verified** · verification: numeric-fit / pass — 700/700 rows fit; vs .dat the same (section, dia) differs in flange length on 174/175 rows (always +5).
- formula: `{"expr": "add[d]", "table": {"add": {"16": 30, "20": 35, "22": 40}}}`
- engine: n/a — Estimating-only table; engine follows .dat (BL-002). Decide which add-length is canonical.
- evidence: `C:\HS-STEEL\HS03\Excel\XLSTART\단중.xlsx` @ `SCSS!F5:F547 / T5:T547` — flange and web TS lengths

### BL-004 — Splice bolt grip definitions

Flange splice grip = tf + outer plate t + inner plate t; web splice grip = tw + 2 × web plate t.

- trust: **verified** · verification: numeric-fit / pass — These grip definitions are the ones under which BL-001 fits 100% of both tables.
- formula: `{"expr": "flange: tf + t_out + t_in; web: tw + 2*t_wp", "vars": {"tf": "mm", "tw": "mm", "t_out": "mm", "t_in": "mm", "t_wp": "mm"}}`
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\SCSS-G16.dat` @ `S11 outer t, S12 inner t, S1 web plate t`
- evidence: `C:\HS-STEEL\HS03\Excel\XLSTART\단중.xlsx` @ `SCSS!I (outer t), N (inner t), W (web plate t)`

### BL-005 — BOLTADDLEN named range columns (HTB/TS/TUB) — broken ref

자재산출-2017.xlsm defines BOLTADDLEN (=TITLE!#REF!, broken); formulas read column 2 for HTB, 3 for TS, 4 for TUB.

- trust: **stated** · verification: none / unverified — Range is broken in the shipped workbook; values recovered only via BL-002/BL-003 fits.
- engine: n/a
- evidence: `C:\HS-STEEL\HS03\견적용-2017\자재산출-2017.xlsm` @ `definedName BOLTADDLEN = TITLE!#REF!`
- evidence: `C:\HS-STEEL\HS03\견적용-2017\자재산출-2017.xlsm` @ `JOINT-BASE!AF18 (col 2, HTB), AF2 (col 3, TS), AF19 (col 4, TUB)`

### BL-006 — Anchor bolt length & naming

Anchor bolt length = ROUND((S11+S12+3d+H)/10)*10 (newer rows round to 50 mm: ROUND(x/50)*50); named "AB M{d}(L-{len})" or J-type "AB M{d}(J-{len})". Used when S11 > 99, else a TS bolt is emitted.

- trust: **stated** · verification: recompute / pass — Cached JOINT-BASE!AG2 = 740, AG7 = 650 reproduce from the row inputs (same workbook; non-independent).
- formula: `{"expr": "round((embed + proj + 3*d + t_base) / step) * step", "params": {"step": [10, 50]}, "returns": "anchor length mm"}`
- engine: missing
- evidence: `C:\HS-STEEL\HS03\견적용-2017\자재산출-2017.xlsm` @ `JOINT-BASE!AF2,AG2` — ROUND((R2+S2+Q2*3+H2)*0.1,0)*10
- evidence: `C:\HS-STEEL\HS03\견적용-2017\자재산출-2017.xlsm` @ `JOINT-BASE!AG7,AF9` — ROUND(...*0.02,0)*50
- evidence: `C:\HS-STEEL\HS03\견적용-2017\자재산출-2017.xlsm` @ `JOINT-BASE!AO11` — '앙카길이를 50mm 단위로 계산함'

### BL-007 — Bolt set weight table

Bolt set weight [g] = L·π·r²·0.00785 + const(d); bolt alone = set − nut − 2·washer. const/nut/washer: M12 71.8/30/10, M16 138.56/57/20, M20 237.5/97/32, M22 346.3/137/52, M24 470.3/201/62.

- trust: **stated** · verification: none / unverified — No second bolt-weight source in the asset set.
- formula: `{"expr": "L * pi * (d/2)^2 * 0.00785 + c[d]", "table": {"c": {"12": 71.8, "16": 138.56, "20": 237.5, "22": 346.3, "24": 470.3}, "nut": {"12": 30, "16": 57, "20": 97, "22": 137, "24": 201}, "washer": {"12": 10, "16": 20, "20": 32, "22": 52, "24": 62}}, "returns": "g per set"}`
- engine: missing
- evidence: `C:\HS-STEEL\HS03\Excel\XLSTART\단중.xlsx` @ `bolt!H3:AA12 (=H$2*$AD3+$AE3), AD3:AD11 (=r^2*3.141592*0.00785), AB/AC nut/washer`

### BL-008 — SCSS bolt weight per splice

Splice bolt weight [kg] = ROUND(set_g(d, L) × 0.001 × qty, 2), looking up the bolt table column L/5.

- trust: **stated** · verification: none / unverified
- formula: `{"expr": "round(set_g(d, L) * 0.001 * qty, 2)", "returns": "kg"}`
- engine: missing
- evidence: `C:\HS-STEEL\HS03\Excel\XLSTART\단중.xlsx` @ `SCSS!G5` — =ROUND(VLOOKUP(E5,bolt,F5*0.2,0)*0.001*D5,2)
- evidence: `C:\HS-STEEL\HS03\Excel\XLSTART\단중.xlsx` @ `SCSS!U5` — web bolts, same shape

### HL-001 — Engine hole-diameter rules disagree from M24

Bolts.HoleFor returns d+2 up to M22 and d+3 from M24 (KDS 14 31 25 standard hole), but BoltPattern.HoleDia always returns d+2 — M24 splice holes come out 26 instead of 27.

- trust: **stated** · verification: code-read / fail — Two engine call paths give different holes for d ≥ 24.
- formula: `{"expr": "d + (d >= 24 ? 3 : 2)", "vars": {"d": "bolt diameter mm"}, "returns": "hole mm"}`
- engine: differs — Fix: BoltPattern.HoleDia should delegate to Bolts.HoleFor. (`src/HsSteel.Domain/Fabrication.cs:Bolts.HoleFor, src/HsSteel.Domain/Connections.cs:BoltPattern.HoleDia`)
- evidence: `src/HsSteel.Domain/Fabrication.cs` @ `Bolts.HoleFor (line ~71)`
- evidence: `src/HsSteel.Domain/Connections.cs` @ `BoltPattern.HoleDia (line ~69)`
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\Project.dat` @ `key=SHOLE` — 22 (= M20+2)

### HL-002 — Hole/bolt symbol blocks D{d}x{L}

Blocks D12x22 … D28x78 are side-view symbols whose half-height is d/2; most are drawn L/2 long from the base point. The name encodes diameter × length.

- trust: **inferred** · verification: geometry / pass — half-height = d/2 on 11/11 blocks; half-length = L/2 on 8/11 (D18x40, D18x48, D26x56 differ).
- formula: `{"expr": "half_height == d/2", "vars": {"d": "from name"}}`
- rationale (claude-opus-5-5): Geometry confirms d; whether d is the bolt or the hole diameter (D26 → M24 hole 26 would contradict KDS 27) cannot be decided without the HOLE-DIA-TABLE text, which is not extractable from the DWG here.
- engine: n/a
- evidence: `C:\HS-STEEL\HSSTEEL\block\D22x52.dwg` @ `block extents (ext_maxy − base_y = 11)`
- evidence: `C:\HS-STEEL\HSSTEEL\block\D28x78.dwg` @ `block extents (ext_maxx − base_x = 39)`

### SPL-001 — Splice table key = {C|G}{bolt d}{spec}

Splice standards are keyed by member role (C column / G girder), bolt diameter and section spec; program files are SCSS-{C|G}{16|20|22}.dat.

- trust: **verified** · verification: cross-source / pass — Key shape in 단중.xlsx matches the 6 .dat file names (role × {16,20,22}).
- formula: `{"expr": "role[0] + str(d) + spec"}`
- engine: implemented (`src/HsSteel.Domain/Connections.cs:SpliceStandards.Find`)
- evidence: `C:\HS-STEEL\HS03\Excel\XLSTART\단중.xlsx` @ `SCSS!A5` — =LEFT(C5,1)&E5&B5 → "G16H198*99*4.5*7"
- evidence: `C:\HS-STEEL\HSSTEEL\attributes` @ `SCSS-C16/C20/C22/G16/G20/G22.dat file names`

### SPL-002 — Web splice plate length

Web splice plate length = 2·e_end + 2·e_gap + 2·n·p + 5 (splice gap).

- trust: **verified** · verification: identity-check / pass — Residual AK = 0 on 350/350 rows (plate lengths are hand-entered literals, check formulas are separate).
- formula: `{"expr": "2*e_end + 2*e_gap + 2*n*p + gap", "params": {"gap": 5}, "returns": "mm"}`
- engine: missing — Engine derives plate extents from BoltAxis patterns; no explicit check.
- evidence: `C:\HS-STEEL\HS03\Excel\XLSTART\단중.xlsx` @ `SCSS!AK5:AK547` — =AA5*2+AD5*2+AB5*AC5*2+5-Y5 (residual)

### SPL-003 — Web splice plate height

Web splice plate height = e_top + m·g + e_bot (bolt rows m intervals at pitch g).

- trust: **verified** · verification: identity-check / pass — Residual AL = 0 on 350/350 rows.
- formula: `{"expr": "e_top + m*g + e_bot", "returns": "mm"}`
- engine: missing
- evidence: `C:\HS-STEEL\HS03\Excel\XLSTART\단중.xlsx` @ `SCSS!AL5:AL547` — =AE5+AF5*AG5+AH5-X5 (residual)

### SPL-004 — Web splice bolt count

Web splice bolts = 2·(n+1)·(m+1) (both sides of the joint).

- trust: **stated** · verification: identity-check / partial — Residual AM = 0 on 292/350 rows; remaining rows have hand-edited bolt counts.
- formula: `{"expr": "2*(n+1)*(m+1)"}`
- engine: implemented (`src/HsSteel.Domain/Connections.cs:SpliceSpec.WebBoltCount`)
- evidence: `C:\HS-STEEL\HS03\Excel\XLSTART\단중.xlsx` @ `SCSS!AM5:AM547` — =(AB5+1)*(AF5+1)*2-R5 (residual)

### SPL-005 — Splice plate thickness per section (xlsx vs .dat)

Flange/web splice plate thicknesses per (section, bolt d) are tabulated; the program .dat and 단중.xlsx agree on most rows.

- trust: **stated** · verification: cross-source / partial — Matched 175 (section,d) pairs: flange t agree 172, web t agree 152.
- engine: implemented (`src/HsSteel.Domain/Connections.cs:SpliceSpec`)
- evidence: `C:\HS-STEEL\HS03\Excel\XLSTART\단중.xlsx` @ `SCSS!I,N,W`
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\SCSS-G20.dat` @ `S1,S11,S12`

### SPL-006 — Splice plate weight

Plate weight [kg] = ROUNDDOWN(qty·t·w·l·7.85e-6, 2).

- trust: **verified** · verification: cross-source / pass — Recomputed 350/350 cached plate weights; density cross-checked by WT-001.
- formula: `{"expr": "floor(qty*t*w*l*7.85e-6, 2)", "returns": "kg"}`
- engine: implemented — Engine rounds to 2 dp (Math.Round, not floor). (`src/HsSteel.Modeling/Parts.cs:PlatePart.Weight`)
- evidence: `C:\HS-STEEL\HS03\Excel\XLSTART\단중.xlsx` @ `SCSS!L5,Q5,Z5` — =ROUNDDOWN(K5*J5*I5*H5*0.00000785,2)

### WT-001 — Steel density 7.85 (0.00785 kg/mm²/m)

All weight formulas use ρ = 7.85 t/m³: kg/m = area[mm²]·0.00785; kg = volume[mm³]·7.85e-6.

- trust: **verified** · verification: cross-source / pass — BH-BEAM.dat 159/159, H-BEAM.dat 81/81, FLAT-BAR.dat 40/41 (F915x3.2 is a mislabelled row).
- formula: `{"expr": "rho = 7.85e-6", "units": "kg/mm³"}`
- engine: implemented (`src/HsSteel.Domain/Profile.cs:FromSpec, src/HsSteel.Modeling/Parts.cs`)
- evidence: `C:\HS-STEEL\HS03\Excel\XLSTART\단중.xlsx` @ `HLCCT!I2` — …*0.00785
- evidence: `C:\HS-STEEL\HSSTEEL\새공사-설치용\BOM자재산출서.xlsm` @ `VBA 철판단품정리/BomList: TmpThk * 7.85` — plate kg/m²
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\FLAT-BAR.dat` @ `all rows`

### WT-002 — H/BH unit weight formula

H/BH kg/m = ROUND((2·B·tf + (H−2·tf)·tw + (4−π)·r²)·0.00785, 1), r = root radius (BH: r = tw).

- trust: **verified** · verification: cross-source / pass — BH-BEAM.dat 159/159, H-BEAM.dat 81/81 within max(0.1, 0.5%).
- formula: `{"expr": "round((2*B*tf + (H-2*tf)*tw + (4-pi)*r^2) * 0.00785, 1)", "vars": {"H": "mm", "B": "mm", "tw": "mm", "tf": "mm", "r": "mm"}, "returns": "kg/m"}`
- engine: differs — Table lookup is used first; the parsed-spec fallback ignores the root fillet (4−π)r² and rounds to 2 dp. (`src/HsSteel.Domain/Profile.cs:Area`)
- evidence: `C:\HS-STEEL\HS03\Excel\XLSTART\단중.xlsx` @ `HLCCT!I2` — =ROUND((D2*F2*2+(C2-F2*2)*E2+(G2*2)^2-G2^2*3.141592)*0.00785,1)
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\BH-BEAM.dat` @ `unit weight column`
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\H-BEAM.dat` @ `unit weight column`

### WT-003 — H/BH paint area per metre

H/BH paint area [m²/m] = ROUND((2H + 4B − 2tw)/1000, 3).

- trust: **verified** · verification: cross-source / pass — BH-BEAM.dat 159/159, H-BEAM.dat 81/81 within 0.011.
- formula: `{"expr": "round((2*H + 4*B - 2*tw) / 1000, 3)", "returns": "m2/m"}`
- engine: implemented (`src/HsSteel.Domain/Profile.cs:Perimeter`)
- evidence: `C:\HS-STEEL\HS03\Excel\XLSTART\단중.xlsx` @ `HLCCT!J2` — =ROUND((C2*2+D2*4-E2*2)*0.001,3)
- evidence: `C:\HS-STEEL\HSSTEEL\attributes\BH-BEAM.dat` @ `paint column`

### WT-004 — Plate weight = t × area × 7.85

Plate unit weight = t[mm]·7.85 kg/m²; plate weight = t·w·l·7.85e-6 kg.

- trust: **verified** · verification: cross-source / pass — Workbook VBA, SCSS formula and engine agree; FLAT-BAR table 40/41.
- formula: `{"expr": "t * area_m2 * 7.85", "returns": "kg"}`
- engine: implemented (`src/HsSteel.Modeling/Parts.cs`)
- evidence: `C:\HS-STEEL\HSSTEEL\새공사-설치용\BOM자재산출서.xlsm` @ `VBA BomList.bas: Cells(NthScsRow,13) = TmpThk * 7.85 '단중`
- evidence: `C:\HS-STEEL\HS03\Excel\XLSTART\단중.xlsx` @ `SCSS!L5`
- evidence: `src/HsSteel.Modeling/Parts.cs` @ `PlatePart.Weight = |Area|·t·7.85e-6`

### WT-005 — BOM weight = length[m] × unit kg/m

BOM line length [m] = qty·L[mm]·0.001; weight [kg] = length[m] × unit weight.

- trust: **verified** · verification: cross-source / pass — 자재산출서 literal O = M×N on 8/8 rows.
- formula: `{"expr": "qty * L / 1000 * unit_kg_m", "returns": "kg"}`
- engine: implemented (`src/HsSteel.Drafting/Bom.cs`)
- evidence: `C:\HS-STEEL\HS03\견적용-2017\자재산출-2017.xlsm` @ `BOM_작성!M2` — =L2*K2*0.001
- evidence: `C:\HS-STEEL\HS03\견적용-2017\자재산출-2017.xlsm` @ ` 자재산출서!O2:O9` — literal weights

### WT-006 — BOM paint area = length[m] × paint m²/m

Paint area per BOM line = length[m] × PAINT[m²/m] from the section table, bucketed by finish (내화페인트/내화피복/광명단 …).

- trust: **verified** · verification: cross-source / pass — Q = M × HLCCT paint on 8/8 rows.
- formula: `{"expr": "length_m * paint_m2_m"}`
- engine: implemented (`src/HsSteel.Domain/Profile.cs:PaintFor`)
- evidence: `C:\HS-STEEL\HS03\견적용-2017\자재산출-2017.xlsm` @ `BOM_작성!V2:Y2` — =IF(finish=V$1,$M2*$U2,0)
- evidence: `C:\HS-STEEL\HS03\견적용-2017\자재산출-2017.xlsm` @ ` 자재산출서!Q2:Q9` — literal areas

### BOM-001 — BOM part name = {drawing}-{MARK}-{seq}

BOM piece names are '{drawing no}-{member mark}-{running number}', the running number continuing within a drawing.

- trust: **stated** · verification: none / unverified
- formula: `{"expr": "f'{dwg}-{mark}-{seq}'"}`
- engine: missing
- evidence: `C:\HS-STEEL\HS03\견적용-2017\자재산출-2017.xlsm` @ `BOM_작성!D2,D3` — =IF(C3=C2,C3&"-"&G3&"-"&(seq+1),C3&"-"&G3&"-1") → "100-SC1-1"

### BOM-002 — Shear stud quantity

Studs per metre = (1000/pitch)·ROUNDUP(B/225); stud rows across the flange = ROUNDUP(B/225); row spacing = ROUNDDOWN(B/2).

- trust: **stated** · verification: none / unverified
- formula: `{"expr": "(1000/pitch) * ceil(B/225)"}`
- engine: missing
- evidence: `C:\HS-STEEL\HS03\견적용-2017\자재산출-2017.xlsm` @ `member!Q2,T2,U2` — =IF(5<LEN(TRIM(R2)),(1000/S2)*T2,0); =ROUNDUP(G2/225,0); =ROUNDDOWN(G2*0.5,0)

### BOM-003 — Cover-sheet tonnage factor 0.0005

견적표지 tonnage = SUM(자재집계표!F:F)·0.0005: the whole-column SUM double-counts the subtotal row, so ×0.0005 equals kg/1000.

- trust: **inferred** · verification: recompute / pass — 16868 kg subtotal + 16868 rows = 33736 × 0.0005 = 16.868 t.
- formula: `{"expr": "sum(kg) / 1000", "returns": "t"}`
- rationale (claude-opus-5-5): 0.0005 is only correct while F:F holds rows + one subtotal; adding a second subtotal would silently overstate tonnage.
- engine: n/a
- evidence: `C:\HS-STEEL\HS03\견적용-2017\자재산출-2017.xlsm` @ `견적표지!J9` — =SUM(자재집계표!F:F)*0.0005 → 16.868 TON
- evidence: `C:\HS-STEEL\HS03\견적용-2017\자재산출-2017.xlsm` @ `자재집계표!F10` — =SUM($F$2:F9) = 16868

### QT-001 — Material markup (할증): shapes 9 %, plates 12 %, bolts 3 %

Ordered quantity = ROUNDUP(net × (1 + m), 0) with m = 0.09 for rolled/built shapes and deck, 0.12 for steel plate, 0.03 for bolts/anchors/studs/turnbuckles.

- trust: **verified** · verification: cross-source / pass — 2017 VBA and the BOM template's 사용자 sheet agree; cached J2:J9 reproduce 8/8.
- formula: `{"expr": "ceil(net * (1 + m[kind]))", "table": {"m": {"shape": 0.09, "plate": 0.12, "bolt": 0.03}}}`
- engine: missing
- evidence: `C:\HS-STEEL\HS03\견적용-2017\자재산출-2017.xlsm` @ `VBA 자재집계표.bas 매크로5: Case "STEEL-PLATE" → ROUNDUP(RC[-4]*1.12,0); HTB/TS/AB/STUD → *1.03; shapes → *1.09`
- evidence: `C:\HS-STEEL\HSSTEEL\새공사-설치용\BOM자재산출서.xlsm` @ `사용자!J36:L36` — 0.09 / 0.12 / 0.03 '자재종류별 표준할증'
- evidence: `C:\HS-STEEL\HS03\견적용-2017\자재산출-2017.xlsm` @ `자재집계표!J2:J9` — =IF(ISBLANK(H2),ROUNDUP(F2*1.09,0),…)

### QT-002 — Markup override by ordered lengths

If an ordered length (H) is given, ordered weight = count × length[m] × unit weight instead of the % markup.

- trust: **stated** · verification: none / unverified
- formula: `{"expr": "count * len_m * unit_kg_m"}`
- engine: missing
- evidence: `C:\HS-STEEL\HS03\견적용-2017\자재산출-2017.xlsm` @ `자재집계표!J2` — …,I2*H2*E2)

### CUT-001 — Cutting kerf 3 mm per cut

Each saw/torch cut consumes 3 mm (절단두께) in the shape cutting plan.

- trust: **stated** · verification: none / unverified
- formula: `{"expr": "kerf = 3", "units": "mm"}`
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\새공사-설치용\BOM자재산출서.xlsm` @ `사용자!N15 = 3` — '절단두께가 3mm 이면 1회 절단시 … 3mm 로 계산'
- evidence: `C:\HS-STEEL\HSSTEEL\새공사-설치용\BOM자재산출서.xlsm` @ `VBA 형강커팅플랜.bas: CutThk = 사용자.Cells(15,14)`

### CUT-002 — Stock lengths for cutting plan

Preferred stock 10000 then 12000 mm (3rd preference off), orderable 6000–15000 mm in 1000 mm steps; 21 mm over-length usable.

- trust: **stated** · verification: none / unverified
- formula: `{"params": {"pref": [10000, 12000], "min": 6000, "max": 15000, "step": 1000, "overuse": 21}}`
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\새공사-설치용\BOM자재산출서.xlsm` @ `사용자!N5,N6,N7,N10,N11,N12,N14` — 10000/12000/0/6000/15000/1000/21

### CUT-003 — Stock length formula

New stock length = MAX(INT((used + cuts·kerf + step − overuse)/step)·step, min).

- trust: **stated** · verification: none / unverified
- formula: `{"expr": "max(floor((used + cuts*kerf + step - overuse)/step)*step, min_len)"}`
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\새공사-설치용\BOM자재산출서.xlsm` @ `VBA 형강커팅플랜.bas 커팅플랜계산: FormulaR1C1 "=MAX(INT((RC5+(RC6*CutThk)+AddLen-AddUse)/AddLen)*AddLen,ShpMin)"`

### CUT-004 — Cutting-plan loss thresholds

Allowed residual before trying another combination: 150 mm on 1st-preference stock, 100 mm on 2nd, 50 mm otherwise (허용로스 50 ×3/×2/×1); max scrap 999 mm; initial squaring loss 0.

- trust: **stated** · verification: none / unverified
- formula: `{"params": {"loss": [150, 100, 50, 50], "max_scrap": 999, "initial_loss": 0}}`
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\새공사-설치용\BOM자재산출서.xlsm` @ `사용자!N18,N21:N24,N17,N19` — 50; =N18*3, =N18*2, =N18, =N18; 999; 0

### NUM-001 — Legacy assembly mark heads (Numbering.dat)

Column/SubColumn/Post C001; Rafter/Truss/CraneGirder/Girder G001; Beam B001; Arc A001; Brace/HBrace/VBrace R001; Purlin PU001; Girth GT001; Stair S001; HandRail H001; Joist J001; Gita Z001; sub-shape PS001.

- trust: **stated** · verification: cross-source / fail — Engine AssemblyTypes agrees for C/G/B/PU/GT only; SC, PT, RF, TR, CG, BR, ST, HR differ.
- formula: `{"table": {"head": {"Column": "C001", "SubColumn": "C001", "Post": "C001", "Rafter": "G001", "Truss": "G001", "CraneGirder": "G001", "Girder": "G001", "Beam": "B001", "Brace": "R001", "Purlin": "PU001", "Girth": "GT001", "Stair": "S001", "HandRail": "H001"}}}`
- engine: differs — Decide whether to keep engine prefixes or honour Numbering.dat heads (make them configurable). (`src/HsSteel.Domain/Model.cs:AssemblyTypes`)
- evidence: `C:\HS-STEEL\HSSTEEL\새공사-설치용\attributes\Numbering.dat` @ `keys M83-*-HD-BOX`

### NUM-002 — Section spec key = 구분 + A*B*C*D

Section keys are built as kind letters followed by '*'-joined dimensions (e.g. BH300*300*15*20).

- trust: **stated** · verification: none / unverified
- formula: `{"expr": "kind + '*'.join(dims)"}`
- engine: implemented (`src/HsSteel.Knowledge/SpecAliases.cs`)
- evidence: `C:\HS-STEEL\HS03\Excel\XLSTART\단중.xlsx` @ `HLCCT!A2` — =B2&C2&"*"&D2&"*"&E2&"*"&F2

### NUM-003 — Numbering minimum assembly length / layers

Assembly auto-numbering ignores members shorter than 1999 mm and filters layers '5,6,기존'.

- trust: **inferred** · verification: none / unverified
- rationale (claude-opus-5-5): Meaning taken from key names (ASY-MIN-LENGTH, LAYER-FILTER); no dialog/manual text ingested.
- engine: missing
- evidence: `C:\HS-STEEL\HSSTEEL\새공사-설치용\attributes\Numbering.dat` @ `key=M84-ASY-MIN-LENGTH-BOX` — 1999
- evidence: `C:\HS-STEEL\HSSTEEL\새공사-설치용\attributes\Numbering.dat` @ `key=M84-LAYER-FILTER-BOX` — 5,6,기존

### DFT-001 — Template dimension style DIM-100

새공사-2019.dwg defines DIM-100 with text 3.4, arrow 3, DIMSCALE 50, 1 decimal, gap 1, baseline increment 5, closed arrows.

- trust: **stated** · verification: cross-source / fail — Project.dat m32-dim-txt-box = 3 and engine DR-007 text 2.5 disagree with DIM-100 text 3.4; name says 1:100 but DIMSCALE is 50.
- formula: `{"params": {"text_height": 3.4, "arrow": 3, "dimscale": 50, "dec": 1, "gap": 1, "dli": 5}}`
- engine: differs — Engine paper text is 2.5 mm; pick one canonical dimension text height (3 or 3.4) and set DIMSCALE from the view scale. (`src/HsSteel.Domain/Model.cs:DetailRules`)
- evidence: `C:\HS-STEEL\HSSTEEL\새공사-설치용\새공사-2019.dwg` @ `dimstyle DIM-100` — text_height 3.4; arrow_size 3; scale_factor 50; decimal_places 1; dimension_line_gap 1; dimension_line_increment 5
