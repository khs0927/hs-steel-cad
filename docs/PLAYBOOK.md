# HS-Steel PLAYBOOK — parse → infer → graph → RAG → power-cad

**Audience:** overnight agents + morning Mus  
**Repo:** `C:\code\hs-steel-cad` · tip at write time includes Graph RAG `7bdd7da`  
**power-cad:** `C:\code\power-cad-latest` tip **`988d4a7`** (merge of PR **#41** `feat/create-many-styles`)  
**Coordination:** claim/extend in `docs/COLLAB.md` first. Do not fight siblings over this file — additive PRs preferred.

This is the deferred **master guidelines** for the full loop:

**파싱 → 추론 → 그래프 → RAG 호출 → power-cad 작도**  
(Parse → Infer → Graph → RAG → Draw via power-cad)

Related deep docs (do not duplicate wholesale): `ARCHITECTURE.md`, `FEATURE_EXPLORATION.md`, `GAP_AUDIT.md`, `RULES_CATALOG.md`, `GRAPH_RAG_EVAL.md`, `D3_RESIDUAL_RESULTS.md`, `D4_RESULTS.md`, `LIVE_CAD_SMOKE_RESULTS.md`, `GOLD_SUMMARIES.md`.

---

## 0. Hard boundaries (read first)

| Do | Do not |
|---|---|
| Commit engine code, docs, redacted numeric gold, tests | Push **proprietary** company originals (`C:\HS-STEEL` project DWGs, real BOM xlsm bodies, VLX/DLL, dongle libs) to GitHub |
| Read Claude WIP (`Legacy/`, `Golden*Tests`, asset phase-2 dirs) | Edit Claude WIP or full power-cad **M** merge unless COLLAB says so |
| Emit DrawPlan / `cad_create_many` **payloads** | Write to **live** AutoCAD drawings overnight without Mus (no live CAD in this playbook run) |
| Use `skip_missing_blocks:true` until company title block lands | Assume `HS_FRAME_A3` exists in every drawing |
| Keep `out/` local (gitignored) | Commit `out/hs_assets.db`, embeddings, live payload dumps with client data |

**Gitignore already drops:** `bin/`, `obj/`, **`out/`**, `.vs/`. Anything under `C:\HS-STEEL` / `C:\HS-STEEL_REBORN` stays on the machine as **source of truth for ingest**, not as git content.

---

## 1. Pipeline map (one page)

```
 C:\HS-STEEL (+ optional C:\HS-STEEL_REBORN)
        │  manifest / ingest / build-db
        ▼
 out/hs_assets.db   ←── sections, bolts, blocks, commands, doc_chunks,
        │                 graph nodes/edges, derived_spec (trust grades)
        │
        ├─ Lexical/FTS/semantic search     hs_asset_search
        ├─ Graph walk                      hs_graph_neighbors
        ├─ Graph RAG + rules               hs_graph_rag / hs_explain / hs_rules_search
        │
        ▼
 Domain + Modeling (DetailRules, connections, shear tab, marks)
        │  hs_project_* / hs_model_build / hs_bom
        ▼
 Drafting D2–D4 → DrawPlan (+ HS-KOR text/dim styles, BOM sheets, cope, leaders)
        │  hs_drawings_generate / hs_drawings_to_powercad / hs_draw_plan_handoff
        ▼
 power-cad cad_create_many (text_styles / dim_styles / layers / XData)
        │  dry_run → (optional, Mus awake) live AutoCAD
        ▼
 Regression: PowerCadPayloadRegressionTests · docs/LIVE_CAD_SMOKE_RESULTS.md
```

---

## 2. 파싱 (Parse) — assets into Knowledge

### 2.1 Roots

| Root | Role |
|---|---|
| `C:\HS-STEEL` | Legacy product tree (~1,801 files). Ledger: `docs/ASSET_LEDGER.md` |
| `C:\HS-STEEL_REBORN` | Prior analysis / derived specs. Ledger: `docs/REBORN_LEDGER.md` |
| `out/hs_assets.db` | Built SQLite knowledge DB (**gitignored**; rebuild on machine) |
| Env | `HS_ASSETS_DB`, `HS_STEEL_LEGACY` / manifest default root, `HS_KNOWLEDGE_MODEL_DIR` |

### 2.2 Build (when DB missing or stale)

```powershell
cd C:\code\hs-steel-cad

# Optional doc chunks + embeddings (needs legacy tree + model export)
cd tools\docs_chunker
python chunk.py          # -> out/knowledge/chunks.jsonl
python embed.py          # -> embeddings (e5-small)
# ONNX: export_onnx.py -> out/knowledge/model/

cd C:\code\hs-steel-cad
dotnet run --project src/HsSteel.Knowledge -- manifest --root C:\HS-STEEL
dotnet run --project src/HsSteel.Knowledge -- build-db --out out/hs_assets.db
# REBORN ledger (Claude phase-2): reborn-manifest / ingest hooks in Program — leave builder edits to Claude
```

Coverage snapshot: `out/knowledge/coverage.json` (file-level ingest). Content-depth holes: **`docs/GAP_AUDIT.md`** (VBA bodies, VLX bytecode, PDF OCR, proprietary DWGs).

### 2.3 What lands in the DB

Typical tables/kinds: `section`, `bolt`, `block`, `command`, `palette_item`, `doc_chunk` (+ `doc_chunk_vec`), `derived_spec`, `dialog`, `slide`, `workbook*`, graph `node` / `edge` (`family`, `spliced_with`, `invokes`, `described_by`, …).

**Clean-room:** do not decompile `hs02.VLX` / `HS-DETAIL.VLX` or redistribute Rockey dongle DLLs (see ASSET_LEDGER + rule `RB-006` / `RB-007`).

---

## 3. 추론 (Infer) — Domain / Modeling rules

Engine defaults and standards live in code + catalog:

| Concern | Where |
|---|---|
| Project.dat → `DetailRules` | `src/HsSteel.Domain/Model.cs` (`SCALLOP`, `ENDGAGE`, `SHOLE`, `WDGAP`, `SWS`, …) |
| Welds / holes / gauges | `src/HsSteel.Domain/Fabrication.cs` (`WeldRules`, `Bolts`, `BoltGauges`) |
| Bolt patterns / SCSS splices | `src/HsSteel.Domain/Connections.cs` |
| Shear-tab row layout | `src/HsSteel.Modeling/ShearTabLayout.cs` + `docs/SHEAR-TAB-ROWS.md` |
| Evidence-backed rule pack | **`src/HsSteel.Knowledge/Rules/rules.json`** (100) + `RulesCatalog.cs` · doc: **`docs/RULES_CATALOG.md`** |

MCP for interactive rules on a workspace project:

- `hs_project_rules` — get/set `DetailRules`
- `hs_project_frame` — grid + `beam_connection` + rule knobs
- `hs_grid_from_bays` — bay → grid without placing members
- `hs_model_build` / `hs_connection_add` / `hs_member_add` — build model
- `hs_bom` — structured BOM (+ optional csv/json paths)
- `hs_splice_standard` / `hs_section_search` — catalogue lookups

**Night test gate:** `dotnet test` (expect HsSteel.Tests 128+, Knowledge 61+ including Graph RAG).

---

## 4. 그래프 (Graph)

| Tool / API | Purpose |
|---|---|
| `KnowledgeStore.Neighbors` | BFS both directions on `edge` (depth 1–4) |
| MCP **`hs_graph_neighbors`** | `kind` + `key` + optional `rel` |
| Edge examples | `family`, `spliced_with`, `alias_of`, `invokes`, `inserts`, `described_by`, `implemented_by` |

Inspect:

```powershell
dotnet run --project src/HsSteel.Knowledge -- search "H400x200" --mode auto
# MCP: hs_graph_neighbors kind=section key=H-BEAM/H400x200x8x13 depth=2
# MCP: hs_assets_stats / hs_asset_get
```

Known softness: palette→block `inserts` still sparse (`FEATURE_EXPLORATION.md`).

---

## 5. RAG 호출 (Graph RAG + rules)

| Piece | Path / name |
|---|---|
| Implementation | `src/HsSteel.Knowledge/GraphRag.cs` (`GraphRag`, `HsExplain`) |
| Rules search | `RulesCatalog.Instance.Search` · embedded `Rules/rules.json` |
| MCP | **`hs_graph_rag`**, **`hs_explain`**, **`hs_rules_search`** (+ hybrid **`hs_asset_search`**) |
| Eval set | `tests/HsSteel.Knowledge.Tests/Data/graph_rag_eval.json` (~32 Q) |
| Metrics doc | **`docs/GRAPH_RAG_EVAL.md`** — overnight result **32/32 Hit@rules** |
| Tests | `tests/HsSteel.Knowledge.Tests/GraphRagTests.cs` |
| Local report | `out/graph_rag_eval_results.json` (gitignored) |

Suggested call pattern for an agent answering a detailing question:

1. `hs_graph_rag` query=… mode=`auto` expand_depth=`1`  
2. If a concrete node appears → `hs_explain` kind/key  
3. For pure code/standards questions → `hs_rules_search`  
4. Cite `engine_refs` + evidence sources from the rule pack; do not invent VLX behaviour.

Semantic path needs ONNX under `out/knowledge/model/` (or `HS_KNOWLEDGE_MODEL_DIR`); otherwise search **falls back to lexical** — rules catalog still works.

---

## 6. Drafting D2–D4 → DrawPlan (작도 준비)

| Stage | What | Pointers |
|---|---|---|
| **D2** | 14 section families, part/plate/assembly callouts | `FEATURE_EXPLORATION.md` §3; Domain `Profile` / Drafting layout tests |
| **D3** | Frame model, end plate / shear tab, flange cope/scallop, marks | `Modeling/*`; MCP `hs_project_frame`, cope notes; `docs/D3_RESIDUAL_RESULTS.md` |
| **D4** | Elevations (`V-`), BOM tables CSV/JSON, cope drawing, weld leaders | `Drafting/Bom.cs`, `Callouts.LeaderIfLongEnough` (≥3 paper mm×scale); `docs/D4_RESULTS.md` |
| **HS-KOR** | Hangul text/dim style | `TextStyles.Korean = "HS-KOR"` (`txt.shx` + bigfont `whgtxt.shx`), width_factor **0.85** (`934e536`). Payload carries `text_styles` / `dim_styles` on first `hs_drawings_to_powercad` chunk |

MCP drawing path:

1. `hs_project_new` / load → edit members/connections/rules  
2. `hs_model_build`  
3. `hs_drawings_generate` (kinds: assembly / part / plate / plan / bom / elevation aliases)  
4. `hs_drawings_to_powercad` → `{ payloads: [ { entities, layers, text_styles?, dim_styles?, xdata_app: "HS-STEEL" } ] }`  
5. Or `hs_draw_plan_handoff` → schema `hs-steel-draw-plan/1` (`execution_authorized=false`)

Safety reminders from live smoke / COLLAB:

- Strip **empty** `text`/`mtext` (power-cad rejects the whole batch).  
- Short weld leaders omitted via `LeaderIfLongEnough`.  
- Missing title block → `skip_missing_blocks: true`.

---

## 7. power-cad 작도 (cad_create_many)

### 7.1 Version pins

| Repo | Tip / PR | Notes |
|---|---|---|
| power-cad-latest | **`988d4a7`** | Merge PR **#41** |
| PR #41 commit | `01f31ac` | `cad_create_many` **creates** `text_styles` / `dim_styles` when missing; **never mutates** existing styles |
| Bridge land (earlier) | `97d6f0d` | `cad_create_many` + XData + `cad_xdata_get` |

### 7.2 Handoff recipe (no live CAD required overnight)

```text
hs_drawings_to_powercad(project=…)
  → for each payload:
      cad_create_many(
        entities, layers, xdata_app="HS-STEEL",
        text_styles, dim_styles,          # HS-KOR etc.
        skip_missing_blocks=true,
        dry_run=true                      # default overnight
      )
  → optional when Mus awake:
      dry_run=false on AutoCAD 2027
      cad_xdata_get / cad_query(xdata="mark=…")
```

Caps / flags: entity batches ≤5000; prefer dry_run first. Document binding / commit flow stays on the power-cad side (handoff `may_execute_mutation=false` until authorized).

### 7.3 Regression (always) vs live (optional)

| Check | Where | Overnight |
|---|---|---|
| Payload shape / styles / no empty text / leader length | `tests/HsSteel.Tests/PowerCadPayloadRegressionTests.cs` | **Run** |
| Handoff schema | `PowerCadHandoffTests.cs` | **Run** |
| Live SAMPLE-FRAME create_many | `docs/LIVE_CAD_SMOKE_RESULTS.md` · `out/explore_results/*_LIVE*.json` | **Optional** — Claude/Mus with AutoCAD; prior: 2,746 ents / XData OK @ ~02:23 KST |

```powershell
cd C:\code\hs-steel-cad
dotnet test --filter "FullyQualifiedName~PowerCadPayloadRegression"
dotnet test --filter "FullyQualifiedName~GraphRag"
dotnet test   # full green before push
```

---

## 8. COLLAB boundaries (who owns what)

Read **`docs/COLLAB.md`** before editing. Snapshot of overnight split:

| Zone | Owner |
|---|---|
| Modeling / Drafting D3–D4 residuals, K3 `QueryExpand`/`KnowledgeStore` | Grok |
| GAP_AUDIT / RULES_CATALOG / Graph RAG / gold summaries / **this PLAYBOOK** | Grok Bot executor |
| Drafting HS-KOR + payload regression / power-cad styles PR landing | Sibling Grok / Claude-sonnet (done) |
| Legacy `.Mxx`, Golden tests, asset phase-2 (`Ingest/`, `Reborn/`, `KnowledgeDbBuilder`/`Program`) | **Claude** — leave alone |
| Full M merge into PowerCad.Server | **Claude** `feat/hs-steel-merge` |
| Live AutoCAD | Claude when CAD is up — not overnight default |

Shared files (`HsSteel.sln`, `ARCHITECTURE.md`, `COLLAB.md`, `PLAYBOOK.md`): small commits, push early.

---

## 9. What never goes in git

- `C:\HS-STEEL\**` project DWGs, client names in paths, full BOM xlsm, manuals binaries as content dumps  
- `hs02.VLX`, `HS-DETAIL.VLX`, plot EXEs, **Rockey / license DLLs**  
- Raw `out/` DB, embeddings, live explore JSON with customer geometry  
- Gold: **numbers/summaries only** (`docs/GOLD_SUMMARIES.md`); redacted basenames — see overnight gold policy  

OK in git: engine, tests, docs, `rules.json`, redacted counts, generator scripts under `tools/`.

---

## 10. 30-minute morning checklist

1. `git pull` · read `docs/COLLAB.md` + this PLAYBOOK  
2. `dotnet test` (hs-steel)  
3. Confirm `out/hs_assets.db` exists (rebuild §2 if needed)  
4. Smoke: `hs_rules_search` “scallop” · `hs_graph_rag` “H400 shear tab” · `PowerCadPayloadRegression`  
5. If AutoCAD up: one `cad_create_many` dry_run=false on SAMPLE-FRAME with HS-KOR styles (PR #41 behaviour)  
6. Do **not** start full M merge or touch Claude phase-2 dirs without a new COLLAB claim  

---

## 11. Quick index

| Need | Open |
|---|---|
| Architecture principles | `docs/ARCHITECTURE.md` |
| Content gaps | `docs/GAP_AUDIT.md` |
| Rule list | `docs/RULES_CATALOG.md` · `src/HsSteel.Knowledge/Rules/rules.json` |
| RAG metrics | `docs/GRAPH_RAG_EVAL.md` |
| D3 / D4 results | `docs/D3_RESIDUAL_RESULTS.md` · `docs/D4_RESULTS.md` |
| Live CAD notes | `docs/LIVE_CAD_SMOKE_RESULTS.md` |
| Gold counts | `docs/GOLD_SUMMARIES.md` |
| Work split | `docs/COLLAB.md` |
| Exploration how-to | `docs/FEATURE_EXPLORATION.md` |

*End of PLAYBOOK.*
