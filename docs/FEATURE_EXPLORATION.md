# Feature exploration pack (K3 / D2 / bridge)

작성: 2026-10-08 · tip `c10cf55` (hs-steel-cad) · power-cad main `97d6f0d` (cad_create_many + XData land)  
범위: **풀 M 머지 전**에 새로 올라간 기능을 직접 만져보기 위한 안내. HsSteel.Mcp → PowerCad.Server 이전은 **아직 하지 않음**.

---

## 0. 한 줄 요약

| 영역 | 뭘 해볼 수 있나 | 풀 M 없이도? |
|---|---|---|
| **K3** | 자산 DB 하이브리드 검색 (exact → FTS → e5 벡터) | ✅ hs-steel MCP / CLI |
| **Knowledge** | 섹션·블록·팔레트·명령·문서 청크 그래프 | ✅ (의도적 갭 있음) |
| **D2** | 14 강종 단면 확장 + 샵 디테일 콜아웃/레이아웃 | ✅ `dotnet test` / demo DXF |
| **Bridge** | `hs_drawings_to_powercad` → `cad_create_many` | ✅ 페이로드 핸드오프만 (서버 흡수 X) |

---

## 1. K3 — 오프라인 multilingual-e5-small 하이브리드 검색

### 무엇이 들어갔나
- MCP: `hs_asset_search` (`mode`: **auto** | **lexical** | **semantic**)
- exact / alias → FTS → `doc_chunk_vec` 벡터를 RRF로 합침 (`단중.xlsx` 다운웨이트)
- 평가: `tests/HsSteel.Knowledge.Tests/Data/eval_queries.json` (~40쿼리) **Hit@5 ≥ 0.8**
- 쿼리 임베더: ONNX + SentencePiece → `out/knowledge/model/`  
  - `model.onnx`, `sentencepiece.bpe.model`  
  - 환경변수 `HS_KNOWLEDGE_MODEL_DIR` 로 경로 오버라이드 가능  
  - DB: `out/hs_assets.db` (또는 `HS_ASSETS_DB`)

### 바로 실행

```powershell
cd C:\code\hs-steel-cad

# 전체 테스트 (Knowledge 포함 Hit@5)
dotnet test

# CLI 검색 (DB가 있을 때)
dotnet run --project src/HsSteel.Knowledge -- search "H-400x200" --mode auto
dotnet run --project src/HsSteel.Knowledge -- search "고장력볼트" --mode semantic --limit 10
dotnet run --project src/HsSteel.Knowledge -- search "HTB M20" --kind bolt --mode lexical

# MCP stdio (클라이언트가 hs_asset_search 호출)
dotnet run --project src/HsSteel.Mcp
```

MCP에서 시도할 쿼리 예:
- `hs_asset_search` query=`H400x200`, mode=`auto`
- query=`볼트`, kind=`block`, mode=`semantic`
- `hs_asset_get` kind=`section` key=`H-BEAM/H400x200x8x13`
- `hs_graph_neighbors` 로 section → family / splice / palette 탐색
- `hs_assets_stats` 리소스로 테이블 카운트 확인

### DB / 임베딩 재빌드 (필요할 때)

```powershell
# 1) 문서 청크 + 임베딩 (레거시 C:\HS-STEEL 필요)
cd C:\code\hs-steel-cad\tools\docs_chunker
python chunk.py          # -> out/knowledge/chunks.jsonl
python embed.py          # -> embeddings.npy + embeddings_ids.json (e5-small)
# ONNX export (모델 디렉터리용): python export_onnx.py  -> out/knowledge/model/

# 2) 매니페스트 + SQLite
cd C:\code\hs-steel-cad
dotnet run --project src/HsSteel.Knowledge -- manifest --root C:\HS-STEEL
dotnet run --project src/HsSteel.Knowledge -- build-db --out out/hs_assets.db
```

모델이 없으면 `mode=semantic`/`auto`의 벡터 다리가 빠지고 **lexical만** 동작한다 (의도된 폴백).

---

## 2. Knowledge DB — 들어 있는 것 / 의도적 갭

### 들어 있는 것
- `section`, `bolt`, `block`, `palette_item`, `command`, `command_alias`, `linetype`, `doc_chunk` (+ `doc_chunk_vec`)
- 그래프 엣지: `family`, `spliced_with`, `uses_layer`, `alias_of`, `inserts`(palette→block), `invokes`(palette→command) 등
- 블록 미리보기/속성, 레거시 경로·sha256 provenance

### 의도적 갭 (가짜 링크 안 만듦)
- **palette → block**: 블록 DWG가 **실제로 존재할 때만** `inserts` 엣지 생성 (추측 타깃 금지). 레거시 트리 기준 `inserts` ≥ 50 정도.
- **palette → command**: 이름/매크로가 명확할 때만 `invokes`. `TargetKind=none` 은 엣지 없음.
- 존재하지 않는 블록을 가리키는 팔레트 항목은 **노드만** 남고 블록 엣지는 없음 → `hs_graph_neighbors`로 “끊긴” 항목을 확인할 수 있음.

확인:

```powershell
# 테스트가 실DB를 잡는 환경이면:
dotnet test --filter "FullyQualifiedName~Real_PaletteToBlockEdges"
```

---

## 3. D2 — 단면 확장 + 샵 디테일 콜아웃/레이아웃

### 커버 패밀리 (14)
`H-BEAM`, `BH-BEAM`, `LH-BEAM`, `PEB-BEAM`, `I-BEAM`, `T-BAR`, `ANGLE`, `CHANNEL`, `C-CHANNEL`, `Z-BAR`, `SQ-PIPE`, `STEEL-PIPE`, `ROUND-BAR`, `FLAT-BAR`  
(+ 플레이트 디테일)

### 무엇이 늘었나
- 루트/토/코너 필렛·게이지 (`Profile` / `Fabrication`)
- 샵 디테일 **callouts** (볼트 표기 예: `6-M20 HTB (ø22)`) 및 어노테이션 패킹/`Layout`
- 외곽 면적 vs 테이블 kg/m 교차 검증 (패밀리별 ±3%)

### 바로 실행

```powershell
cd C:\code\hs-steel-cad

dotnet test --filter "FullyQualifiedName~DomainProfileTests"
dotnet test --filter "FullyQualifiedName~DraftingLayoutTests"
dotnet test --filter "FullyQualifiedName~Part_and_plate_details_cover_every_steel_family"

# 데모 DXF (레거시 자산 경로 필요 시 HS_STEEL_LEGACY)
dotnet run --project src/HsSteel.Mcp -- --demo out/demo_B1.dxf
```

MCP 드로잉 쪽:
- `hs_section_search` / `hs_section_catalog_handoff`
- `hs_project_*` → `hs_model_build` → `hs_drawings_generate` / `hs_draw_plan` / `hs_draw_plan_handoff`

---

## 4. Bridge — 풀 M 없이 오늘 되는 것

### power-cad main에 이미 랜딩됨 (`97d6f0d`)
- `cad_create_many` — 최대 5000 엔티티, 레이어 생성, `hs` XData, `xdata_app`(기본 `HS-STEEL`), `offset`, `skip_missing_blocks`, `dry_run`
- `cad_xdata_get` — 핸들별 XData / tags
- `cad_query` / `cad_get` 의 xdata 필터

### hs-steel 쪽 페이로드 생산
- `hs_drawings_to_powercad` → `{ payloads: [ { entities, layers?, xdata_app:"HS-STEEL" } ] }`  
  각 entity에 `hs: { kind, mark, spec, length, assembly, sheet, ... }`
- `hs_draw_plan_handoff` / `DrawPlan.ToPowerCadHandoff()` → schema `hs-steel-draw-plan/1` (create spec과 tag 분리)

### 오늘 워크플로 (서버 흡수 없이)

1. hs-steel MCP에서 프로젝트 빌드 후 `hs_drawings_to_powercad`
2. 반환 `payloads[i]` 를 power-cad `cad_create_many` 인자로 그대로 전달  
   (`entities`, `layers`, `xdata_app`; 필요 시 `skip_missing_blocks=true`, 먼저 `dry_run=true`)
3. 그린 뒤 `cad_xdata_get` / `cad_query(xdata="mark=B1")` 로 태그 확인
4. 프레임/디테일 블록은 `blocks_needed` → `cad_import_block` 선행 또는 skip

```powershell
# power-cad 쪽 회귀
cd C:\code\power-cad-latest
dotnet test dotnet\PowerCad.Tests\PowerCad.Tests.csproj --filter "FullyQualifiedName~BulkCreate"
```

### 아직 안 한 것 (나중 M)
- `HsSteel.Mcp` 툴 클래스를 `PowerCad.Server`로 이전
- `DxfExporter`를 `PowerCad.Dxf`에 흡수
- hs-steel-cad 레포 흡수/삭제 — **하지 말 것** (탐색 후 결정)

---

## 5. 추천 탐색 순서 (30–60분)

1. `dotnet test` (hs-steel) — 119+ 근처 전부 확인  
2. `hs_asset_search` auto vs lexical vs semantic 같은 쿼리로 비교  
3. `hs_graph_neighbors`로 palette↔block 끊김 샘플 보기  
4. D2 필터 테스트 + `out/demo_B1.dxf` 열기  
5. `hs_drawings_to_powercad` 페이로드 JSON 한 장 저장 → simulate power-cad에서 `cad_create_many(dry_run=true)`

질문/이슈 생기면 tip 해시(`c10cf55` / `97d6f0d`)와 함께 남겨 두면 이후 M 설계에 바로 쓸 수 있다.
