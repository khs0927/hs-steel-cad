# GAP_AUDIT — content-level missing pieces (2026-10-08 overnight)

**Owner:** Grok Bot executor (see `docs/COLLAB.md` overnight zone)  
**Scope:** What is *present as files* but still **missing as usable content** in hs-steel-cad (formulas/VBA bodies, VLX/DLL behaviour, image/PDF page text, proprietary project DWGs).  
**Not in scope:** Full power-cad M merge; Claude WIP (`Legacy/`, `GoldenMxxTests`, asset phase-2 dirs).

Manifest coverage for ingestable HS-STEEL rows is already ~100% at the *file* layer (`out/knowledge/coverage.json`: covered 1635 / resolved 100%). This audit is about **content extraction depth**.

---

## 1. Excel VBA / formulas

| Asset | Has VBA project? | Sheets (zip parts) | Ingested today | Gap |
|---|---|---|---|---|
| `HS03/물량산출-2017/견적용-2017.xlsm` | **Yes** | 8 | workbook + workbook_sheet + thin `doc_chunk` | **VBA modules not extracted**; cell formulas / named ranges not in KG |
| `HSSTEEL/**/BOM산출내역서.xlsm` (7 copies: 새공사-*, 작업) | **Yes** | 9 each | same (quantities target) | Same BOM macro workbook duplicated per project folder; **macro logic + sheet formulas** not ported — REBORN `bom_schema.json` / `bomlist_spec.json` are structured substitutes (trust: unverified) |
| `HS03/Excel/XLSTART/단중.xlsx` | No VBA (xlsx) | — | doc_chunk + workbook | Numeric tables partially chunked; **formula cells / dependency graph** not modelled |
| Manual xlsx under `HSSTEEL/매뉴얼/` | No | — | reference / partial chunks | Authoring notes not treated as executable rules |

**Decision:** Do **not** decompile or redistribute VBA. Prefer REBORN JSON specs + engine BOM ports. Track formula extraction as a future optional OpenXML pass (values + formula text only, no OLE).

---

## 2. VLX / DLL (behaviour not content)

| File | Size | Ledger | Gap |
|---|---:|---|---|
| `HSSTEEL/hs02.VLX` | ~5.9 MB | reference | Compiled LISP; **no bytecode port** (ARCHITECTURE clean-room). FAS symbols via REBORN |
| `HSSTEEL/HS-DETAIL.VLX` | ~389 KB | reference | Same |
| `allplot/hjhvlx/allplot_dcl.VLX` | ~215 KB | reference | Plot plugin — out of product Plot/PDF scope for now |
| `CSHSSTEELNET40.dll`, `CsMenuLoad40.dll`, `CsNewProject40.dll` | small | reference | .NET 4 plugins — API surface partially in REBORN `net_asm.json` (unverified) |
| `CSROCKEY2013.dll`, `r4nd_class.dll`, `HsRockey/*` | — | **excluded** | License dongle — never redistribute |
| `CsMultiPlotNet40/45.dll` | — | reference | Plot executables |

**Gap metric:** Behaviour coverage is via dialogs/commands/docs/rules catalog, not VLX. Rule `RB-006` documents this.

---

## 3. Images / slides / icons

| Kind | Count (approx) | DB | Gap |
|---|---:|---|---|
| `Icons/*.sld` slides | 416 | `slide` + `has_icon` edges | **Pixel/SVG semantics** not OCR’d |
| support / Icons BMP/PNG | ~1000+ | `icon` | Binary only — no caption OCR |
| Manual menu JPG | 1 | reference | Not OCR’d into command graph |

---

## 4. PDF manuals (page text)

| PDF | Approx pages | Gap |
|---|---:|---|
| `Cad2017사용자설명서.pdf` | ~12 | Prefer xlsx twin; PDF text layer may be incomplete |
| `Hssteel2020사용자설명서[작성중자료].pdf` | ~39 | Same |
| `설치대 사용법 요약.pdf` | ~1 | Ensure single chunk exists |

---

## 5. DCL dialogs

| Finding | Detail |
|---|---|
| Raw `.DCL` files under tree | Only **3** paths |
| DB `dialog` nodes | **46** (CUIX / REBORN / embedded) |
| Gap | Many dialogs live **inside VLX/FAS** → `dialog_field` sparse |

---

## 6. Proprietary project DWGs / Mxx

| Item | Treatment | Gap |
|---|---|---|
| `새공사-*/*.dwg` (~250 KB templates) | reference(회사) | Template frames |
| `작업/[회사프로젝트] … 698-14 …_2026.05.19.dwg` (~5.9 MB) | **reference — do not push to GitHub** | Real project; gold = **numeric summaries only** in `docs/GOLD_SUMMARIES.md` / `out/gold/` |
| Legacy `.Mxx` | Claude `Legacy/` | Macro option files (prior finding) |

---

## 7. Knowledge / Graph RAG residuals

| Gap | Status after overnight |
|---|---|
| `hs_graph_rag` / `hs_explain` / `hs_rules_search` | **Implemented** |
| Rules as DB `kind=rule` nodes | Embedded JSON catalog (100); DB seed deferred (avoid `KnowledgeDbBuilder` conflict) |
| palette→block inserts | Still sparse |
| Semantic model absent → lexical fallback | Unchanged |

---

## 8. Priority backlog (content depth)

1. OpenXML **formula text** from BOM/견적 xlsm → chunks/rules (no VBA).  
2. PDF text/OCR for manual pages not covered by xlsx.  
3. Optional `kind=rule` upsert after Claude phase-2 builder hooks.  
4. More gold summaries from non-sensitive CSV BOM exports — never raw DWG in git.

---

## 9. Counts snapshot

| Metric | Value |
|---|---|
| Manifest files (HS-STEEL) | 1,801 |
| Ingest covered rows | 1,635 (100% of ingest disposition) |
| RULES_CATALOG | **100** evidence-backed |
| xlsm with VBA | 8 workbooks |
| VLX+DLL listed | 13 binaries |
