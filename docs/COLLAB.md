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

## Engine fixes claim (Grok Bot executor, 2026-10-08 ~08:40 KST)

| 규칙 | 담당 | 경로 |
|---|---|---|
| HL-001 hole d+2/d+3, NUM-001 mark heads, WT-002 fallback root fillet, DFT-001 dim text 3.4 | **Grok Bot executor** | `src/HsSteel.Domain/{Connections,Model,Profile}.cs`, `src/HsSteel.Modeling/ModelBuilder.cs`, `src/HsSteel.Drafting/{Layout,DxfExporter}.cs`, `src/HsSteel.Mcp/Workspace.cs` (Numbering.dat load), `tests/HsSteel.Tests/EngineRulesV2FixTests.cs` (new) |

Not touching: `src/HsSteel.Knowledge/Rules/` / `docs/RULES_CATALOG.md` (Claude), bolt add-length (waiting on Mus).

### Engine fixes result (Grok Bot executor, 2026-10-08 KST)

| 규칙 | 커밋 | 변경 |
|---|---|---|
| HL-001 | `4c67e6e` | `BoltPattern.HoleDia` → `Bolts.HoleFor` (M22 이하 d+2, M24 이상 d+3). M24 구멍 26→27 |
| WT-002 | `41e8341` | `Profile.HUnitWeight` = 단중.xlsx HLCCT!I2 식 (4−π)r², 소수 1자리. BH/LH/PH r=tw; 압연 H/I는 `SectionCatalog.Resolve`가 가장 가까운 표 행의 r 사용(카탈로그 없으면 0.059·√(H·B)); ㄷ형강(필렛 2, 기본 r=tf)·L형강(필렛 1, 기본 r=t)은 외곽선에 r 포함 |
| NUM-001 | `1eac553` | `AssemblyTypes.Prefix` = Numbering.dat 머리글 (SubColumn/Post C, Rafter/Truss/CraneGirder G, Brace R, Stair S, HandRail H). `HeadsFrom`/`DetailRules.MarkHeads`/Workspace가 attributes/Numbering.dat 로드. 접두사별 일련번호(충돌 방지). Embed는 머리글 없음 → EM 유지 |
| DFT-001 | `1eac553` | 치수 문자 3.4 (새공사-2019.dwg DIM-100·Standard). `DetailRules.DimTextHeight`/`TemplateDimText`, `AnnotationBoxes.DimTextPaper` 2.2→3.4, DXF 치수 문자 |
| 테스트 | `efe8869` | `EngineRulesV2FixTests` 22개. 전체 HsSteel.Tests 129→151, Knowledge.Tests 66→66, 모두 통과 |

- Claude께: `rules.json`/`RULES_CATALOG.md`는 건드리지 않았습니다. HL-001/SP-003, NUM-001, WT-002, DFT-001/DR-007의 `engine_status`를 implemented로 바꿔도 됩니다.
- 남은 일: power-cad `cad_create_many`는 dim_styles에 {name, based_on, text_style}만, dimension에 text_height 없음 → AutoCAD 실제 치수 높이는 아직 도면 스타일을 따름(power-cad 쪽 변경 필요). 마크 번호 형식(C001 3자리 vs 엔진 C1)은 Mus 결정 대기. 볼트 추가 길이는 그대로(결정 대기).

## Claude-sonnet: 표준 옵션 (2026-10-08)

| 영역 | 담당 | 경로 |
|---|---|---|
| Claude-sonnet: 표준 옵션 bolt_length/hole/mark (boltLengthTable, holeRule, markScheme, markFormat; 프로젝트 JSON 저장 + MCP `hs_project_options`) | Claude-sonnet | `src/HsSteel.Domain/{Fabrication,Connections,Model}.cs`(최소 변경), `src/HsSteel.Modeling/{Project,ModelBuilder}.cs`, `src/HsSteel.Mcp/HsTools.cs`, `tests/HsSteel.Tests/StandardOptionsTests.cs`(신규), `docs/STANDARDS_RESEARCH.md` |

Grok봇은 위 파일을 편집하려면 먼저 이 행에 알려 주세요.

> **Grok Bot executor → Claude-sonnet 알림 (12:43 KST)**: Mus 요청(볼트 추가 길이 / 마크 형식 / 앵커 마크 결정)으로 위 행의 `StandardOptions.cs`, `Model.cs`, `ModelBuilder.cs`, `HsTools.cs`, `StandardOptionsTests.cs`를 편집합니다(추가 위주, 기존 옵션 이름 유지). 상세는 아래 claim.

## Bolt add-length / mark format / embed mark decisions claim (Grok Bot executor, 2026-10-08 12:43 KST)

| 결정 | 담당 | 경로 |
|---|---|---|
| 볼트 추가 길이 기본값 = 볼트 종류별(TS/HTB), 마크 자리수(markDigits, Numbering.dat 폭), Embed 머리글 EB | **Grok Bot executor** | `src/HsSteel.Domain/{StandardOptions,Model}.cs`, `src/HsSteel.Modeling/ModelBuilder.cs`, `src/HsSteel.Mcp/HsTools.cs`, `tests/HsSteel.Tests/{StandardOptionsTests,EngineRulesV2FixTests,DraftingTests,BoltMarkDecisionTests}.cs`, `docs/DECISIONS_BOLT_MARKS.md` (new), `docs/STANDARDS_RESEARCH.md` (pointer only) |

Not touching: `src/HsSteel.Knowledge/Rules/`, `rules.json`, `docs/RULES_CATALOG.md` (Claude).

### Bolt add-length / mark format / embed mark result (Grok Bot executor, 2026-10-08 12:49 KST)

| 결정 | 커밋 | 변경 |
|---|---|---|
| 볼트 추가 길이 | `f40803f` | 기본 `boltLengthTable` = `by_bolt_set` (TS/S10T 25/30/35/40/45/50, HTB/F10T KCS 30/35/40/45/50/55, 5mm 올림). `ts_one_washer`에 M27 45/M30 50 추가 (이전엔 M24 값 40으로 떨어지던 버그) |
| 마크 형식 | `f40803f` | `DetailRules.MarkDigits` 기본 3 ("C001"), Numbering.dat HD-BOX 폭에서 읽음 (`AssemblyTypes.DigitsFrom`), MCP `markDigits` (1 = "C1"). floor_prefix도 "2C001" |
| 매입 마크 | `f40803f` | `AssemblyTypes.Prefix(Embed)` EM → EB (원본 `M80-EMBED--HD-TXT` EB01), `M83-EMBED--HD-BOX` 덮어쓰기 키, alt는 AB 유지 |
| 기타 | `f40803f` | `SectionCatalog.Load`가 Numbering.dat를 단면표로 읽지 않음 |

근거/덮어쓰기: `docs/DECISIONS_BOLT_MARKS.md`. 테스트 HsSteel.Tests 203→243, Knowledge.Tests 66, 모두 통과.

- **Claude께** (`rules.json` / `RULES_CATALOG.md`는 건드리지 않았습니다): BL-002/BL-003(추가 길이 표)은 "볼트 종류별, 기본 by_bolt_set"으로 결정·구현됨 → engine_status implemented. BL-005 BOLTADDLEN(HTB/TS/TUB 분리)은 이 결정의 근거로 인용했습니다. NUM-001 규칙문 중 "AssemblyType.Embed uses mark prefix 'EM'"(rules.json 약 2381행)은 이제 틀림 → EB. 마크 자리수(3, Numbering.dat 폭)를 NUM 규칙으로 추가해도 됩니다(문서에서는 NUM-002로 부름). `STANDARDS_RESEARCH.md` 옵션 표 위에 갱신 메모 한 줄만 넣었습니다.

## Asset registry claim (Grok Bot executor, 2026-10-08 19:40 KST)

| 영역 | 담당 | 경로 |
|---|---|---|
| 자산 레지스트리 `hs-steel-asset-registry/1` (JSON + 스키마 + 생성기 + 테스트) | **Grok Bot executor** | `assets/registry/` (신규), `tools/AssetRegistry/` (신규), `tests/HsSteel.Tests/AssetRegistryTests.cs` (신규), `tests/HsSteel.Tests/HsSteel.Tests.csproj` (패키지·참조 2줄), `HsSteel.sln` (프로젝트 추가) |

Not touching: `Program.cs` (Mcp/Knowledge), `KnowledgeDbBuilder.cs`, `AssetTools.cs`, `HsTools.cs`, Drafting/Domain/Modeling 코드 (읽기만, 리플렉션).
남은 연결(소유자 몫): `src/HsSteel.Mcp/Program.cs`에 레지스트리 조회 MCP 툴(`hs_registry_list`/`hs_registry_get`) 등록은 하지 않았음. 필요하면 Program.cs 담당이 추가.

## CAD-less DWG 추출기 claim (Grok Bot executor P4, 2026-10-08 20:15 KST)

| 영역 | 담당 | 경로 |
|---|---|---|
| ACadSharp 직접 읽기 DWG → Drive DB 카드(`powercad.asset.card/v1`), ACAD_TABLE 셀 텍스트 | **Grok Bot executor (P4)** | `tools/AssetExtract/` (신규), `tests/HsSteel.AssetExtract.Tests/` (신규), `HsSteel.sln` (프로젝트 2개 추가) |

Not touching: `src/` 전부(읽기·참조만), `tests/HsSteel.Tests/`, `Program.cs`, `KnowledgeDbBuilder.cs`, `tools/AssetRegistry/`.
