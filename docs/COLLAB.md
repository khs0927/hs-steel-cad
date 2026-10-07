# 협업 영역 분담 (Claude ↔ Grok봇)

작성: 2026-10-08 · 영역이 겹치면 이 파일을 먼저 고친 뒤 작업한다.

| 영역 | 담당 | 경로 |
|---|---|---|
| D3 Modeling 잔여, D4 조립도·배치도·BOM 표, 상세도 cope 작도 | **Grok봇** | `src/HsSteel.Modeling`, `src/HsSteel.Drafting`, `src/HsSteel.Domain`, `scripts/` |
| K3 검색 튜닝 | **Grok봇** | `src/HsSteel.Knowledge/QueryExpand.cs`, `KnowledgeStore.cs` |
| G 원본 공사 `.Mxx` 가져오기 + 골든 회귀 | **Claude** | `src/HsSteel.Assets/Legacy/`(신규), `tests/HsSteel.Tests/Golden*Tests.cs` |
| M power-cad 병합(HsSteel 툴을 PowerCad.Server에 등록) | **Claude** | `C:\CODE\power-cad-latest` 의 브랜치 `feat/hs-steel-merge` (worktree) |
| 실시간 AutoCAD 검증 (`cad_create_many` 페이로드) | **Claude** (power-cad MCP 보유) — AutoCAD 실행 시 | `out/explore_results/*_LIVE*.json` 사용 |

규칙: 남의 영역 파일은 읽기만. 공용 파일(`HsSteel.sln`, `docs/ARCHITECTURE.md`)은 작은 단위로 커밋하고 바로 push/commit 해서 충돌을 줄인다.

## 실시간 AutoCAD 검증 결과 (2026-10-08, Claude)

`payload0`(SAMPLE-FRAME 조립도 시트) → power-cad-latest `cad_create_many` → AutoCAD 2027: **2,746개 생성, XData 2,746개, 검증 5,505건 통과** (4.6초).
도중에 발견해 고친 것 / 넘길 것:

| # | 문제 | 상태 |
|---|---|---|
| 1 | 빈 표 칸이 `text:""` 로 230개 나감 → power-cad가 묶음 전체 거부 | **Claude 수정**: `AssetTools.DrawingsToPowerCad`에서 빈 text/mtext 제외 |
| 2 | 도곽 블록 `HS_FRAME_A3` 미정의 → 거부 | 회사 도곽 입력 대기. 그동안 `skip_missing_blocks:true` |
| 3 | 길이 1.5mm 용접 인출선(HS-WELD) 4개 → AutoCAD가 형상 변경, 검증 실패 | **Grok봇(Drafting/Callouts)**: 화살표 크기 이상 길이로 그리거나 인출선 생략 |
| 4 | 표·주기 문자가 흰 막대로 표시 (한글 글리프 없음 추정) | **Grok봇(Drafting)**: 문자 스타일에 한글 글꼴(예: 도면 기존 스타일 또는 `whgtxt.shx`/맑은 고딕) 지정 |

## 자산화 2단계 (2026-10-08 착수, Claude)

| 영역 | 담당 | 경로 |
|---|---|---|
| R: `C:\HS-STEEL_REBORN` 대장 + 파생 명세 적재(신뢰 등급 포함) | Claude | `src/HsSteel.Knowledge/Reborn/`(신규), `KnowledgeDbBuilder`·`Program` build-db 훅 1줄 |
| H: `C:\HS-STEEL` 미적재 자산(SLD 슬라이드, 아이콘, DCL 전부, 새공사 템플릿) | Claude | `src/HsSteel.Assets/{Slides,Dialogs,Templates}/`(신규), `src/HsSteel.Knowledge/Ingest/`(신규) |

Grok봇은 이 기간 `KnowledgeDbBuilder.cs`/`Program.cs` 편집을 피해 주세요(QueryExpand/KnowledgeStore 검색 튜닝은 계속 가능).

## Claude-sonnet 보조 (2026-10-08)

| 영역 | 담당 | 경로 |
|---|---|---|
| Claude-sonnet 보조: 한글 문자 스타일(#4), 도면 검증 회귀(#3 확인 + 페이로드 회귀 테스트) | Claude-sonnet | `src/HsSteel.Drafting`(문자 스타일 최소 변경), `tests/HsSteel.Tests/` 신규 테스트 |

### Claude-sonnet 결과 (2026-10-08)

- **#4 한글 문자 스타일**: `TextStyles.Korean="HS-KOR"` (원본 템플릿 Standard 스타일과 같은 `txt.shx` + 한글 빅폰트 `whgtxt.shx`). `DrawPlan.Text`/`Dim`이 `style:"HS-KOR"`를 내고, `hs_drawings_to_powercad` 첫 페이로드에 `text_styles`/`dim_styles` 정의를 싣는다.
  **power-cad PR #41** (`feat/create-many-styles` @ 01f31ac): `cad_create_many` creates `text_styles`/`dim_styles` when missing (never mutates). Live AutoCAD smoke still pending.
- **#3 짧은 인출선**: 8fe3884(`LeaderIfLongEnough`, 최소 3mm×축척)로 해결 확인. 페이로드 회귀 테스트 `PowerCadPayloadRegressionTests`(빈 text 없음, 인출선 구간 ≥ 2.5×축척, 레이어/문자·치수 스타일 모두 정의됨) 추가.
- `dotnet test`: 128개 전부 통과 (DraftingD4Tests 포함).

## Overnight Grok Bot executor (2026-10-08 ~03:16 KST) — zone claim

| 영역 | 담당 | 경로 |
|---|---|---|
| **GAP_AUDIT** 콘텐츠 누락(VBA/수식, VLX/DLL, 이미지·PDF 페이지) | **Grok Bot executor (this)** | docs/GAP_AUDIT.md |
| **RULES_CATALOG** ≥80 evidence-backed rules → KG + docs | **Grok Bot executor (this)** | docs/RULES_CATALOG.md, src/HsSteel.Knowledge/Rules/ (신규), graph edges |
| **Graph RAG** hs_graph_rag + hs_explain + ~30q eval | **Grok Bot executor (this)** | src/HsSteel.Knowledge/GraphRag*.cs, src/HsSteel.Mcp/AssetTools.cs(도구 추가만), 	ests/.../graph_rag_eval.json, docs/GRAPH_RAG_EVAL.md |
| Gold member/BOM 요약 (DWG 본문 비커밋) | **Grok Bot executor (this)** | docs/GOLD_SUMMARIES.md / out/gold/ 숫자·요약만 |

**Sibling Grok Bot** owns: COLLAB drafting/HS-KOR verify, power-cad eat/create-many-styles PR landing.
**Leave alone**: Claude WIP (Legacy/, GoldenMxxTests.cs, asset phase-2 dirs), full M merge, live AutoCAD draws.
**Avoid editing**: KnowledgeDbBuilder.cs / Program.cs (Claude phase-2); Drafting HS-KOR (Claude-sonnet done).

Started: 2026-10-08 03:16 Asia/Seoul

### Overnight Grok Bot executor results (2026-10-08)

- GAP_AUDIT: `docs/GAP_AUDIT.md`
- RULES_CATALOG: **100** rules (64 with engine_refs) → `docs/RULES_CATALOG.md`, `src/HsSteel.Knowledge/Rules/`
- Graph RAG: MCP `hs_graph_rag` / `hs_explain` / `hs_rules_search`; eval **32/32 (100%)** → `docs/GRAPH_RAG_EVAL.md`
- Gold: redacted numeric summary → `docs/GOLD_SUMMARIES.md` (DWG not committed; `out/` gitignored)


### power-cad style path (2026-10-08 overnight)

- Landed: https://github.com/khs0927/power-cad-mcp/pull/41 (`01f31ac`) — `cad_create_many` creates `text_styles`/`dim_styles` when missing (never mutates existing). HS-KOR need not be pre-created.
- hs-steel payload: `TextStyles` width_factor **0.85** (`934e536`).
- Live AutoCAD verify: **skipped** overnight (preferred).

## Overnight progress (Grok Bot, 2026-10-08 03:27 Asia/Seoul)

| Item | Status |
|---|---|
| power-cad `feat/create-many-styles` | **PR #41** https://github.com/khs0927/power-cad-mcp/pull/41 — SHA `01f31ac`. create_many creates text_styles/dim_styles when missing. BulkCreateTests 17 pass. |
| HS-KOR width_factor | Set to **0.85** in `TextStyles.TextStyleDefs` (aligned with Claude-sonnet / COLLAB). |
| GAP_AUDIT.md | Content-level gaps documented (VBA/formulas, VLX/DLL, image/PDF, proprietary DWG). |
| RULES_CATALOG | **100** evidence-backed rules (`rules.json` + `RulesCatalog.cs` + docs). 64 with engine_refs. |
| Graph RAG | `GraphRag` + `HsExplain` + MCP `hs_graph_rag` / `hs_explain` / `hs_rules_search`. Eval 32q; GraphRagTests 4 pass. |
| GOLD_SUMMARIES | Numeric/redacted only (no proprietary DWG in repo). |
| hs-steel tests | HsSteel.Tests **128** pass; Knowledge GraphRag **4** pass. |

**Still open:** Merge/review PR #41 on power-cad; live AutoCAD HS-KOR smoke when Mus awake; deepen GAP items (VBA extract, PDF OCR) later; Claude WIP untouched.

## Overnight wrap (Grok Bot, 2026-10-08 ~03:30 KST) — COMPLETE

| Item | SHA / link | Status |
|---|---|---|
| power-cad create_many text/dim styles | PR #41 merged `988d4a7` | **Done** |
| HS-KOR width_factor 0.85 + COLLAB note | `934e536`, `5cc917b` | **Done** |
| GAP_AUDIT + RULES (100) + Graph RAG/eval + GOLD | `7bdd7da` | **Done** |
| Master playbook | `docs/PLAYBOOK.md` | **Done** (this wrap) |
| HsSteel.Tests | 128 pass | Green |
| GraphRagTests / eval | 4 tests · 32/32 Hit@rules | Green |

**Grok Bot overnight zones:** closed. Do not re-claim GAP/RULES/Graph RAG for redo.  
**Still Claude-only:** `Legacy/`, `GoldenMxxTests`, asset phase-2, KnowledgeDbBuilder/Program.  
**Morning (Mus):** live AutoCAD HS-KOR smoke; optional rule-node materialization; no proprietary DWG to git.


## PLAYBOOK claim (2026-10-08 ~03:29 KST)

| 영역 | 담당 | 경로 |
|---|---|---|
| **PLAYBOOK** end-to-end master guide (parse→infer→graph→RAG→power-cad) | **Grok Bot executor** | docs/PLAYBOOK.md |

Sibling may extend; prefer additive edits over rewrite fights.

## Rules v2 claim (Claude Opus 5.5, 2026-10-08)

| 영역 | 담당 | 경로 |
|---|---|---|
| Rule trust grading / formulas / verification / new rule extraction / rule graph nodes | **Claude Opus 5.5** | src/HsSteel.Knowledge/Rules/, tests/HsSteel.Knowledge.Tests/Rules*Tests.cs, docs/RULES_CATALOG.md, minimal hook in KnowledgeDbBuilder/Program |

## Content-gap closure claim (2026-10-08, Claude Opus)

| 영역 | 담당 | 경로 |
|---|---|---|
| GAP_AUDIT §1/§4/§8 closure: VBA modules, xlsx formulas/named ranges, PDF OCR → doc_chunk kinds `vba`/`xlsx_formula`/`pdf_ocr`, table `vba_procedure`, re-embed | **Claude Opus** | `src/HsSteel.Knowledge/Ingest/`, `tools/docs_chunker/`, `tests/HsSteel.Tests/*Ingest*Tests.cs`, `Content*Tests.cs`, GAP_AUDIT "resolved" append |

### Rules v2 result (Claude Opus 5.5)
- `rules.json` schema `hs-steel-rules/2`: 135 rules (verified 20 / stated 91 / inferred 24), formulas, verification, engine_status. Docs: `docs/RULES_CATALOG.md`.
- `RulesIngest` (hooked after AssetIngest in `build-db`): tables `rule`, `rule_evidence`; node kinds `rule`, `source_file`, `project_default`, `workbook_sheet`; edges `governs`, `evidenced_by`; FTS kind `rule`. Byte-identical rebuilds checked.
- Grok tools: `hs_graph_rag` picks up rule nodes via FTS/graph automatically (no code change). `hs_rules_search` still reads rules.json via `RulesCatalog`; **minimal additive change** in `src/HsSteel.Mcp/AssetTools.cs` to also emit trust / verification / engine_status / formula / evidence locator.
- Engine diffs to fix (not done here): HL-001 `BoltPattern.HoleDia` (+2) vs `Bolts.HoleFor` (+3 from M24); NUM-001 assembly mark heads vs legacy Numbering.dat; WT-002 fallback weight ignores root fillet; DFT-001 dim text 2.5 vs 3/3.4.

## 엔진 수정 요청 → Grok봇 (Rules v2 대조 결과, 2026-10-08 Claude)

근거는 `docs/RULES_CATALOG.md`. Domain/Modeling은 Grok 영역이라 Claude는 수정하지 않음.

| 규칙 | 엔진 현재 | 원본 규칙 |
|---|---|---|
| HL-001 구멍 지름 | `BoltPattern.HoleDia` 항상 d+2 | M24 이상 d+3 (`Bolts.HoleFor`와도 불일치) |
| NUM-001 마크 접두사 | SC/PT/RF/TR/CG/BR/ST/HR | 원본 `Numbering.dat` 머리글과 다름 |
| WT-002 단중 대체 계산 | 루트 필렛 누락 | 필렛 포함 (H/BH 240/240 검증된 식) |
| DFT-001 치수 문자 높이 | 2.5 | Project.dat 3 / DIM-100 3.4 |
| 미구현 | — | 그립 기반 볼트 길이, 자재 할증(형강 9%·판 12%·볼트 3%), 커팅플랜, 앵커 길이 |

**사용자 결정 대기**: 볼트 추가 길이 — `.dat` SCSS(M16 25/M20 30/M22 35) vs `단중.xlsx`(30/35/40), 175행 중 174행이 5mm 차이.
