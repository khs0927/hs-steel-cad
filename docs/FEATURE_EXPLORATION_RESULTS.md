# Feature Exploration Results (K3 / Knowledge / D2 / Bridge)

- **실행 시각**: 2026-10-08 01:41:05 +09:00 (KST / Asia/Seoul)
- **머신**: DESKTOP-KTQHS1I (a0056d74-b107-4102-ac4c-a2c22ebcd7c4)
- **hs-steel-cad tip**: `c06e8cb9f35e6d0579a56b25e68a6e9790e51230`
- **power-cad-latest tip**: `97d6f0de8949bb922332c1bc681869f9c08c37dc` (확인: `97d6f0d` cad_create_many + XData land)
- **환경**: `HS_ASSETS_DB=C:\code\hs-steel-cad\out\hs_assets.db`, `HS_KNOWLEDGE_MODEL_DIR=C:\code\hs-steel-cad\out\knowledge\model`, `chcp 65001`

---

## 1. Full hs-steel test suite — **PASS**

| Assembly | Failed | Passed | Skipped | Total |
|---|---:|---:|---:|---:|
| HsSteel.Tests | 0 | 88 | 0 | 88 |
| HsSteel.Knowledge.Tests | 0 | 31 | 0 | 31 |
| **합계** | **0** | **119** | **0** | **119** |

---

## 2. K3 search mode comparison — **PASS** (일부 쿼리 품질 약함)

CLI: `dotnet run --project src/HsSteel.Knowledge -- search … --db out/hs_assets.db`

### 지정 쿼리 top hits

| Query | Mode | Top hits (요약) | 판정 |
|---|---|---|---|
| `H-400x200` | auto | **998 exact** `section H-BEAM/H400x200x8x13` + FTS doc_chunks | PASS |
| `고장력볼트` | semantic | vector ~0.85 doc_chunks (3PLT 볼트 UI / JOINT-BASE 수식 등) | PASS (의미 히트는 있으나 일부 단중.xlsx 노이즈) |
| `HTB M20` | lexical | **998 exact** bolt `TS M20*100` … `TS M20*50` | PASS |
| `전단접합 스캘럽` | auto | vector **~0.02** 약한 점수; PLATE블럭이름정리 / 형강분석 PDF | **WEAK** |
| `엔드플레이트` | auto | hybrid RRF ~0.02 (semantic 성분) | 동작 OK, 점수 낮음 |
| `엔드플레이트` | lexical | **히트 0건** | **WEAK** (키워드 미수록) |
| `엔드플레이트` | semantic | vector **~0.83** PLATE/3PLT 관련 청크 | PASS — auto/lexical 대비 차이 명확 |

### auto vs lexical vs semantic (동일 쿼리 `엔드플레이트`)

- **lexical**: 빈 결과 → 별칭/FTS에 「엔드플레이트」 없음.
- **semantic**: 강한 벡터 히트 (0.83).
- **auto**: 하이브리드이지만 RRF 점수가 낮게 보임(0.02); 순위는 semantic과 유사 청크.

### Hit@5 eval — **PASS**

- 필터: `FullyQualifiedName~HitAt5|Eval|…`
- `SemanticSearchTests.Real_EvalHitAt5_AtLeast80Percent` → **통과** (기준 ≥ 0.8)

---

## 3. Knowledge graph / intentional gaps — **PASS**

### edge counts by `rel` (총 2143)

| rel | count |
|---|---:|
| family | 866 |
| invokes | 533 |
| spliced_with | 334 |
| on_layer | 269 |
| alias_of | 59 |
| **inserts** | **55** |
| calls_lisp | 27 |

### node kinds (발췌)

palette_item 2246, section 866, command 732, block 114, bolt 31, family 19 …

### palette → block inserts 샘플

- **with inserts**: 55 / 2246 (의도적 갭 — 문서 §2; 테스트 기준 ≥50 충족)
- with 예: `C형강 기타/FSNS10` → block `FSNS10`, lifting-lug 블록들
- without 예: `1시작/1110 스케일설정…` 등 (주로 command/invoke 쪽)
- with invokes 예: 스케일 팔레트 → `scl10` / `CsNewProject`

### 필터 테스트

- `Real_PaletteToBlockEdges_AtLeast50_ExistingBlocksOnly` — **PASS**
- `Graph_GetAndNeighbors` + `Real_Neighbors_FromBlock_ReturnPaletteItems` — **PASS** (2)

---

## 4. D2 — **PASS**

| Filter | Result |
|---|---|
| `DomainProfileTests` | PASS 3/3 |
| `DraftingLayoutTests` | PASS 8/8 |
| `Part_and_plate_details_cover_every_steel_family` | PASS 1/1 |

### Demo DXF

```
dotnet run --project src/HsSteel.Mcp -- --demo out/demo_B1_explore.dxf
```

- **파일**: `out/demo_B1_explore.dxf`
- **크기**: 1695698 bytes (~1.62 MB)
- **mtime**: 2026-10-08 01:38:41 KST
- **내용**: 28 sheets, 16 assemblies, 5 shape parts, 7 plates (E/A/S/P/M 시트 출력 확인)

---

## 5. Bridge / power-cad — **PASS** (AutoCAD 없이 dry 샘플)

### BulkCreate (power-cad `97d6f0d`)

```
dotnet test dotnet\PowerCad.Tests\PowerCad.Tests.csproj --filter "FullyQualifiedName~BulkCreate"
```

- **15/15 PASS** (dry_run, XData HS-STEEL, skip_missing_blocks, offset, query filter 등)

### hs-steel handoff 테스트

- `PowerCadHandoffTests` + `Drawings_to_powercad_tags_and_chunks` — **3/3 PASS**

### dry 샘플 (AutoCAD 미사용)

생성 위치: `out/explore_results/`

| 파일 | 검증 |
|---|---|
| `handoff_drawplan_sample.json` | schema `hs-steel-draw-plan/1`, entities 분리(spec/tag), digest 존재 |
| `cad_create_many_dry_sample.json` | `entities` + `xdata_app=HS-STEEL` + `dry_run=true` + per-entity `hs` |
| `hs_drawings_to_powercad_summary.json` | SAMPLE-FRAME assembly: **total_entities=2996**, payloads=1, sheets=16, layers=13, **all_have_hs=true**, xdata_app=HS-STEEL |
| `hs_drawings_to_powercad_payload0_dry.json` | cad_create_many 인자 형태 + `dry_run=true` (~646 KB) |

`hs.kinds` in payload0: assembly, sheet, weld

---

## 깨진/약한 점 (풀 M 전 메모)

1. **`전단접합 스캘럽`**: auto 점수가 ~0.02로 매우 약함. 스캘럽/전단접합 전용 청크·별칭 부족 가능.
2. **`엔드플레이트` lexical 0건**: semantic은 강함 → 한글 별칭/FTS 보강 여지.
3. **semantic `고장력볼트`**: 볼트 UI/수식 시트 혼합; 단중.xlsx 다운웨이트는 테스트 통과하나 실사용 노이즈 체감 가능.
4. **palette inserts 갭**: 2246 중 55만 inserts — **의도적** (문서 §2). invokes는 533으로 더 풍부.
5. 풀 M 미수행: HsSteel.Mcp ↔ PowerCad.Server 라이브 배선, DxfExporter 통합은 이번 범위 밖.

---

## 한줄 요약

**K3/Knowledge/D2/Bridge 탐색 팩 §5 순서 전부 완료. 테스트·데모·dry handoff 전부 녹색. 품질 이슈는 스캘럽/엔드플레이트 lexical 약점과 palette inserts 의도 갭뿐.** 풀 M 머지 없음.
