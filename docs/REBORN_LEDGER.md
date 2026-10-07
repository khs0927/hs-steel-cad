# HS-STEEL_REBORN 대장 (요약판)

원본: `C:\HS-STEEL_REBORN` — 레거시 HS-STEEL 분석을 시도했던 **이전 프로젝트**의 산출물. 파생·부분 미검증 지식이며,
그 프로젝트의 `ORCHESTRATION_PLAN.md`가 스스로 여러 주장(순환 단중 검증, BOM 9시트 중 3시트, 미검증 프로브 등)을 정정했다.
파일 단위 대장: `assets/reborn_manifest.json` (파일 1개 = 1행: relPath, size, sha256, disposition, reason, category; relPath 순 정렬, 타임스탬프 없음).
재생성: `dotnet run --project src/HsSteel.Knowledge -- reborn-manifest`.

합계 **16,415 파일** = `find -type f` 결과와 일치 (디렉터리 심볼릭 링크 17개는 따라가지 않음 — `engine/out/_mut*` 아래 원본 트리를 가리킴).
처분: ingest 33 / reference 11,580 / excluded 4,802.
sha256은 `.venv/**`("venv bulk-excluded")와 Win32 장치명 파일 `nul` 2개(읽기 불가)를 제외한 모든 파일에 기록.

## 폴더별 집계

| 범주(최상위) | 파일 | MB | ingest | reference | excluded | 주요 사유 |
|---|---:|---:|---:|---:|---:|---|
| `.venv` | 3,584 | 127.6 | 0 | 0 | 3,584 | venv bulk-excluded (3584) |
| `_docs_backup_20260926` | 2 | 0.0 | 0 | 2 | 0 | pre-correction originals (superseded by corrected docs) (2) |
| `bridge` | 21 | 0.5 | 0 | 18 | 3 | analysis/engine source (behaviour reference, not ported) (7); other derived file (4); python bytecode cache (3) |
| `catalog` | 6 | 0.1 | 3 | 3 | 0 | derived data (not ingested) (3); derived spec loaded into derived_spec/doc_chunk with trust grade (3) |
| `engine` | 12,194 | 980.1 | 0 | 11,045 | 1,149 | engine run output / mutation-test artifact (10660); run log / scratch output (593); python bytecode cache (528) |
| `materials` | 22 | 2.9 | 3 | 13 | 6 | derived data (not ingested) (8); python bytecode cache (5); analysis/engine source (behaviour reference, not ported) (4) |
| `oda_in` | 1 | 0.1 | 0 | 1 | 0 | converted drawing / probe output (1) |
| `orch` | 173 | 22.8 | 23 | 143 | 7 | converted drawing / probe output (122); derived spec loaded into derived_spec/doc_chunk with trust grade (23); analysis/engine source (behaviour reference, not ported) (14) |
| `out` | 103 | 0.5 | 0 | 103 | 0 | derived data (not ingested) (74); analysis note (derived, unverified) (12); rendered output / figure (10) |
| `pipeline` | 5 | 0.0 | 0 | 4 | 1 | analysis/engine source (behaviour reference, not ported) (2); workbook output / fixture (1); python bytecode cache (1) |
| `root-doc` | 2 | 0.0 | 2 | 0 | 0 | derived spec loaded into derived_spec/doc_chunk with trust grade (2) |
| `root-other` | 3 | 0.5 | 0 | 0 | 3 | scratch probe script/output (ZWCAD/COM/UIA experiments) (3) |
| `root-script` | 46 | 0.1 | 0 | 0 | 46 | scratch probe script/output (ZWCAD/COM/UIA experiments) (46) |
| `work` | 253 | 6.8 | 2 | 248 | 3 | extracted compiled LISP module (symbols already in orch/fas_symbols.json) (220); analysis note (derived, unverified) (13); analysis/engine source (behaviour reference, not ported) (13) |

## 일괄 제외 규칙
`.venv/**` (해시 생략), `site-packages`/`node_modules`(벤더 패키지), `__pycache__`·`*.pyc`, `.pytest_cache`, 도구 바이너리(`*.exe/dll/pyd/so`),
루트의 스크래치 프로브(`com_probe*`, `focus*`, `uia*`, `listwins*`, `find_*`, `fix_dcom*`, `sendcmd*`, `shot*`, `oda_test*`, `verify_b*/c*`, `screen*.png` 등),
`*.log/*.err`, `tmp/`, `*.prev_attempt`, 셸 리다이렉트 잔재(`nul`, `"` 폴더). `*.fas`는 reference(심볼은 `orch/fas_symbols.json`으로 이미 추출).

## ingest → DB (`derived_spec`, 신뢰 등급 포함)
`out/hs_assets.db` 의 `derived_spec(id, source_file, source_sha256, topic, trust, trust_note, json)` + FTS(kind `derived_spec`),
md 3개는 `doc_chunk(kind='reborn_md')`. 신뢰 등급과 근거는 `src/HsSteel.Knowledge/Reborn/RebornTrust.cs` (ORCHESTRATION_PLAN.md 정정 인용).
대상 33개: `ARCHITECTURE.md`, `ORCHESTRATION_PLAN.md`, `catalog/catalog_summary.json`, `catalog/commands.json`, `catalog/feature_groups.json`, `materials/estimate_2017.json`, `materials/scss_bolt_tables.json`, `materials/weight_table.json`, `orch/_integration.json`, `orch/_integration_20260917_snapshot.json`, `orch/bom_schema.json`, `orch/bomlist_spec.json`, `orch/command_triage.json`, `orch/command_triage_v2.json`, `orch/cut_plan_spec.json`, `orch/dat_full.json`, `orch/dwg_blocks.json`, `orch/dwg_blocks_v2.json`, `orch/fas_symbols.json`, `orch/fas_symbols_summary.json`, `orch/lisp_dialect.json`, `orch/net_asm.json`, `orch/plate_spec.json`, `orch/plot_spec.json`, `orch/quote_logic.json`, `orch/spec_coverage_v2.json`, `orch/template_manifest.json`, `orch/ui_cuix_spec.json`, `orch/user_manual.md`, `orch/weight_audit.json`, `orch/zwcad_stability.json`, `work/fas_module_index.json`, `work/fas_strings.json`

| 등급 | 의미 | 예 |
|---|---|---|
| refuted | 계획 문서 또는 후속판이 거짓/대체를 명시 | `command_triage.json`(집계 4개, 321≠302), `weight_audit.json`(순환 검증), `dwg_blocks.json`, `_integration_20260917_snapshot.json`(수기), `zwcad_stability.json` |
| unverified | 산출됐으나 독립 검증 없음 (기본값) | `command_triage_v2.json`(302 전수, 38 undetermined), `bom_schema.json`, `weight_table.json` 등 |
| verified | 이번 적재에서 우리 데이터로 교차확인 | `dwg_blocks_v2.json` — per_file 113개 basename 전부가 우리 block 테이블에 존재(엔티티 수는 미확인) |

그래프(증거 있는 것만): command→catalog_entry `described_by`(이름 일치 또는 유일 매크로), command→fas_function `implemented_by`(FAS `C:` defun),
lisp→fas_function `described_by`(이름 일치), block→dwg_dxf_entry `described_by`(basename 일치), bom_sheet(9, 열 목록 포함)→derived_spec.
단중 교차검증 결과는 `meta.reborn_weight_crosscheck`.

## 적재 결과 (2026-10-08 빌드)
derived_spec 30 (verified 1 / unverified 24 / refuted 5), reborn_md 청크 56, 노드 +486, 간선 +360
(block→dwg_dxf_entry 113, command→catalog_entry 232, command→fas_function 4, bom_sheet→derived_spec 11, lisp→fas_function 0).
카탈로그 302개 중 232개가 우리 command 노드와 연결; 70개는 근거 부족으로 미연결.
단중 교차검증: weight_table 944행 중 775행이 우리 section 표와 규격 일치, 775/775(100%)가 ±0.5% 이내 일치.
단, 둘 다 레거시 HS-STEEL 자료(단중.xlsx vs attributes/*.dat)에서 온 값이라 **이론 계산(단면적×7.85) 검증은 여전히 아님** → trust는 unverified 유지.
