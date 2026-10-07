# LIVE CAD SMOKE RESULTS

- **When**: 2026-10-08 ~02:23 KST (Asia/Seoul)
- **Machine**: DESKTOP-KTQHS1I (`a0056d74-b107-4102-ac4c-a2c22ebcd7c4`)
- **hs-steel-cad tip (tests)**: `0c71927` (+ untracked sibling `src/HsSteel.Knowledge/QueryExpand.cs` left untouched)
- **power-cad-latest tip**: `97d6f0d` + local plugin fix (document_id stability; see below)
- **Server binary used**: `C:\code\power-cad-latest\dist\server\win-x64-publish-tmp\power-cad-server.exe` (published 2026-10-08 02:06 KST; contains `cad_create_many` / `cad_xdata_get`)

---

## 1. hs-steel suite — **PASS**

```
dotnet test HsSteel.sln
```

| Assembly | Failed | Passed | Skipped | Total |
|---|---:|---:|---:|---:|
| HsSteel.Tests | 0 | 88 | 0 | 88 |
| HsSteel.Knowledge.Tests | 0 | 36 | 0 | 36 |
| **합계** | **0** | **124** | **0** | **124** |

(Earlier tip note was 119; Knowledge grew to 36 with local/sibling tree — suite still green.)

---

## 2. K3 sample searches — **PASS** (weak queries remain weak / noisy)

CLI: `dotnet run --project src/HsSteel.Knowledge -- search … --db out/hs_assets.db`  
Logs: `out/explore_results/k3_live_*_20261008_020619.txt`

| Query | Mode | Top | Verdict |
|---|---|---|---|
| `H-400x200` | auto | **998 exact** `section H-BEAM/H400x200x8x13` | **PASS** |
| `고장력볼트` | semantic | vector ~0.89 doc_chunks | **PASS** |
| `HTB M20` | lexical (kind=bolt) | **998 exact** `TS M20*…` | **PASS** |
| `전단접합 스캘럽` | auto | hybrid/vector ~0.03 | **WEAK** |
| `엔드플레이트` | lexical | FTS-any plate macros (noisy; not empty) | **WEAK** (quality) |
| `엔드플레이트` | semantic | vector ~0.88 | **PASS** |
| `엔드플레이트` | auto | hybrid ~0.03 | OK / low RRF score |

Note: previously lexical `엔드플레이트` was 0 hits; now returns noisy plate/palette FTS hits (sibling `QueryExpand` untracked may affect this). Still weak for intent.

---

## 3. Live AutoCAD / `cad_create_many` — **PASS**

### Link

- AutoCAD 2027 plugin pipe up; smoke used **PID 2276** / target `autocad-2027-2276` / plugin **0.5.0**.
- MCP server lists **55 tools** including `cad_create_many`, `cad_xdata_get`, `cad_bind_document`.
- Empty `Drawing1.dwg` (entity_count 0 → 3).

### Plugin upgrade note (important)

- Running AppData plugin was **0.4.0** (no `create_many` / `document_identity` / `xdata_get`).
- Built + installed plugin from `power-cad-latest` into `%APPDATA%\Autodesk\ApplicationPlugins\PowerCad.bundle`.
- **Original Mus AutoCAD PID 49908 was closed during plugin swap/relaunch** (session disruption). Smoke continued on a fresh acad.exe. Please reopen any prior drawing Mus still needs.

### document_id stability fix (required for bind)

Symptom: `cad_get_document_identity` returned a **new** `document_id` every call → `cad_bind_document` always `[DOCUMENT_CHANGED]`.  
Cause: `ConditionalWeakTable<Database, …>` — AutoCAD `Database` RCW identity not stable across main-thread invokes.  
Fix (local in `power-cad-latest`): key identity map by `db.FingerprintGuid.ToString()` via `ConcurrentDictionary`.  
File: `dotnet/PowerCad.Plugin.A27/AcadDocument.cs`.

### Sequence / results

Script: `scripts/live_cad_create_many_smoke.py`  
JSON: `out/explore_results/live_cad_smoke_report.json`

1. `cad_select_target` → `autocad-2027-2276`
2. `cad_get_document_identity` ×3 → **stable** id `ce202181…`
3. `cad_bind_document` → OK
4. `cad_create_many` **dry_run** → `created_count=3`, `by_type={LINE:2,CIRCLE:1}`, `committed=false`, checks_passed=7
5. `cad_create_many` **live** (`skip_missing_blocks=true`, offset `[1000,1000]`) → handles `2DF`,`2E1`,`2E2`, `committed=true`
6. `cad_xdata_get` → HS-STEEL tags `mark/spec/kind` present on all 3
7. `cad_query` `xdata=mark=SMOKE1` → 2 LINE hits
8. `cad_status` after → **entity_count=3**

Tiny payload only (2 lines + 1 circle on layer `HS-SMOKE`).

---

## 4. hs handoff / `hs_drawings_to_powercad` — **PASS (dry tiny)** / full sample not live-drawn

- Tiny explore sample `out/explore_results/cad_create_many_dry_sample.json` dry_run over live link → **OK** (`created_count=2`, layer `STEEL`, xdata HS-STEEL, `committed=false`). Saved: `out/explore_results/handoff_dry_live_link.json`.
- Full SAMPLE-FRAME handoff summary still reports **total_entities=2996**, `all_have_hs=true` — **not** executed live (would flood drawing). Unit dry path remains the safe proof for bulk size.

### BulkCreate unit tests (power-cad-latest)

```
dotnet test … --filter FullyQualifiedName~BulkCreate
```

**15/15 PASS**

---

## 5. Artifacts

| Path | Role |
|---|---|
| `scripts/live_cad_create_many_smoke.py` | stdio MCP smoke client |
| `docs/LIVE_CAD_SMOKE_RESULTS.md` | this report |
| `out/explore_results/live_cad_smoke_report.json` | raw live report |
| `out/explore_results/handoff_dry_live_link.json` | tiny handoff dry |
| `out/explore_results/k3_live_*_20261008_020619.txt` | K3 CLI logs |
| `C:\code\power-cad-latest\dist\server\win-x64-publish-tmp\power-cad-server.exe` | server with create_many |

---

## Verdict

**GREEN for live smoke goals**: hs-steel tests green; K3 samples re-run (weak queries noted); AutoCAD linked; `cad_create_many` dry+live+xdata verify OK on tiny entities; handoff dry sample OK; full 2996-entity handoff not live-drawn by design.

**Follow-ups for Mus / parent**
1. Prior AutoCAD session (PID 49908) was restarted — reopen needed drawings.
2. Commit/push the `AcadDocument` FingerprintGuid identity fix on `power-cad-latest` (currently local working tree).
3. Keep using published `win-x64-publish-tmp` server (or reinstall) so MCP clients see `cad_create_many`.
4. No full M merge performed.