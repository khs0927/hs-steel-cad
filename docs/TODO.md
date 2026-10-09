# HS-STEEL × power-cad 남은 작업 (TODO)

기준: 2026-10-09 KST, 메인 PC `DESKTOP-KTQHS1I`. 완료되면 줄을 지우지 말고 `[x]` + 커밋/PR 번호를 적는다.
관련 문서: [PLAYBOOK](PLAYBOOK.md) · [COLLAB](COLLAB.md) · [RULES_CATALOG](RULES_CATALOG.md) · [DECISIONS_BOLT_MARKS](DECISIONS_BOLT_MARKS.md) · [DATA_STORAGE](DATA_STORAGE.md) · power-cad `docs/todo/TODO_MAIN_PC.md`(#49)

## 0. 현재 상태 요약

| 영역 | 상태 |
|---|---|
| 자산 DB `hs_assets.db` | 완료 — HS-STEEL 1,801 파일(적재 1,635 = 100%), REBORN 16,415 대장, 규칙 135(verified 20), Graph RAG eval 32/32 |
| 표준 옵션 | 완료 — 볼트 추가길이 `by_bolt_set`, 구멍 d+2/d+3(M24~), 마크 `C001`(markDigits), 매입 `EB` |
| 도면 생성기 | 단품·조립·판재·배치·입면·BOM + (8168ba4) 표지·일반사항·표(용접/볼트/구멍)·앵커 플랜 |
| power-cad `cad_hs_*` | 실 AutoCAD 검증 완료(section_draw, grid, members_place 2×2, member_list, styles_apply, HS-KOR 한글) — #51 |
| Drive `PowerCad-Assets` | `hs-steel-acad` 537 카드 교체, `_index` 4,036 자산/4,874 벡터로 교체(hit@1 0.966, hit@3 1.0). 이전본 `_archive/2026-10-09/` |
| Drive `HS-STEEL-KG` | `db/latest/hs_assets.db` 업로드, SHA256 일치 |

## 1. 지금 해야 충돌이 없는 일 (머지 대기) — 사용자 실행 필요

자동 머지는 권한 검사(Merge Without Review)로 막힘. 아래 순서대로 실행. 각 PR은 리뷰·수정·테스트 완료.

- [x] power-cad #47 헤드리스 DXF (덮어쓰기 방지·cp949·.bak 수정, CI green) — 머지됨 2026-10-09
- [ ] power-cad #46 자산 파이프라인 (`gh pr merge 46 -R khs0927/power-cad-mcp --merge`)
- [ ] power-cad #48 `cad_hs_search` / `cad_hs_index_status` (#46 인덱스 스키마 사용 → #46 다음)
- [ ] power-cad #45 스킬 3종 + 다중 PC 설정 (문서; 머지 후 "PR 대기 중" 표기 정리 필요)
- [ ] power-cad #51 `cad_hs_*` 실 AutoCAD 응답 분리, C001 마크, 평면 라벨 겹침 해소
- [ ] power-cad #50 hs-steel 도구 + `hs_draw_to_cad` (main과 충돌 해소 완료, 마지막)
- [ ] power-cad #49 TODO_MAIN_PC (§0 상태표 갱신 후 마지막)
- [ ] hs-steel-cad #8 ACadSharp 추출기 (`HsSteel.sln` 프로젝트 목록이 겹치면 나중 PR에서 재추가)

또는 `/permissions`에 `gh pr merge` 허용 규칙을 추가하면 Claude가 Jev 판정 + CI 확인 후 순서대로 머지한다.

## 2. 머지 직후 (Claude가 진행)

- [ ] power-cad main에서 `dotnet test` + `PYTHONPATH=src pytest` 전체 통과 확인
- [ ] 플러그인 재설치(`scripts/install_autocad_plugin.ps1 -SkipBuild`, AutoCAD 종료 필요) — 서버 경로를 `C:\CODE\power-cad-latest\dist\server\win-x64-new\` 로 복귀(현재 Claude Desktop 설정은 `C:\CODE\pc-live\dist\...`, 백업 `.bak`)
- [ ] 실 AutoCAD 스모크(`C:\CODE\_live\smoke.py`, Drawing* 스크래치 도면만): `cad_hs_search` "앵커볼트"/"도곽 A3"/"H400x200", `hs_draw_to_cad` 프로젝트 1개 전체 세트, 표지·앵커 플랜 시트
- [ ] 플러그인 버전 번호 올리기(빌드는 새로 했는데 0.5.0으로 표시됨)
- [ ] #45/#48 문서의 "PR 대기 중 / 미구현 계약 초안" 표기 갱신
- [ ] 임시 worktree 정리: `C:\CODE\wt-pr45, wt-pr46, wt-pr47, wt-pr50, wt-pr8, pc-live, power-cad-wt-merge, power-cad-wt-hs` (`git worktree remove`), `.git/worktrees` 권한 오류 항목(`hs-wt-phase2`, `wt`) 정리
- [ ] 57개 질문 평가셋(스테이징 ID 기준)을 Drive 58개 평가셋으로 대체

## 3. 사용자 자료가 필요한 일

- [ ] **회사 도곽 DWG** — `HS_FRAME_A3`는 임시. 속성 태그(공사명·도면명·도면번호·축척·일자·설계/검토/승인) 매핑을 power-cad `docs/standards/hs_steel_drafting_standard.json`에 반영. A2 도곽 없음.
- [ ] **실제 공사 BOM**(데이터가 채워진 xlsx/CSV) 또는 HS-STEEL로 그린 실제 공사 DWG — 골든 회귀(우리 엔진 물량 vs 실제). 원본 BOM xlsm은 전부 빈 양식이었음.
- [ ] **KS B 2819 / KCS 14 31 25 원문 표** — TS 볼트 추가길이(+25/30/35/40) 1차 출처 확인(현재 2차 자료 근거).

## 4. 미구현 기능 (우선순위 제안 순)

| # | 기능 | 위치 | 비고 |
|---|---|---|---|
| 1 | 부재표·BOM을 AutoCAD TABLE 엔티티로 출력 | power-cad Core/Plugin + `cad_hs_member_list` | 지금은 선+문자 |
| 2 | `cad_hs_sheet_frame` (도곽 배치·속성 채우기) | power-cad | §3 회사 도곽 필요 |
| 3 | `cad_hs_workflow` (도면 세트 한 번에) | power-cad | `hs_draw_to_cad`(#50)와 역할 정리 |
| 4 | 엔진 미구현 규칙: 커팅플랜(VBA 기준 — REBORN 명세는 틀림), 자재 할증(형강 9%·판 12%·볼트 3%), 앵커 길이 | hs-steel Domain/Modeling | RULES_CATALOG engine_status=missing |
| 5 | 웹 볼트 수 규칙 불일치 292/350, 판 두께 xlsx vs .dat 불일치(웹 152/175) 원인 조사 | hs-steel Rules | RULES_CATALOG |
| 6 | `.lin` 선종류 로딩 | power-cad | |
| 7 | 페이퍼공간 handle 지원(get/편집 도구) | power-cad | #47 범위 밖 |
| 8 | C# 의미 검색 임베더(`cad_hs_search`가 `query_vector` 없이 동작) | power-cad | 지금은 Python 쪽만 |
| 9 | `cad_hs_block_from_geometry` | power-cad | create_many 블록 정의 가능 여부 먼저 확인 |
| 10 | 파이프라인 블록 추출을 LibreDWG → ACadSharp(#8)로 전환, 썸네일 C# 생성 | asset-pipeline | 표(ACAD_TABLE) 손실 해결 |

## 5. 데이터·품질 정리

- [ ] **자산 검색 이원화 결정**: hs-steel `hs_assets.db`(규칙·근거 그래프·신뢰 등급) vs Drive `PowerCad-Assets/_index`(블록 카드·썸네일·벡터). 제안: 규칙/근거 = hs_assets.db, 형상/썸네일 = Drive 카드, 서로 id로 참조.
- [ ] Drive `hs-steel` 형상: 이 PC는 dwg2dxf 없음 → 125개 pending(ODA 변환기로 대체 가능 확인됨). Drive본(122 extracted) 유지 중.
- [ ] 블록 고유 개수 LibreDWG 323 vs ACadSharp 300(이름 277 동일), 동적 블록 판정 39 vs 25 — AutoCAD에서 표본 확인.
- [ ] `a3-1b2/b3/b4` 삽입점 좌표 비정상 — 원본 DWG 기준점 확인.
- [ ] `query.py` `card_path` 구분자(`/`·`\`) 정규화.
- [ ] RULES: inferred 24개 중 검증 가능한 것 승격, 구멍 기호 블록 규칙 확인.

## 6. 환경 메모 (이 PC)

- 메모리 15.3GB 중 여유 1~2GB로 빠듯함(e5 ONNX 로드 실패 원인이었음 → #46 a3a6b2e에서 저메모리 로드로 수정). Drive G: 마운트가 간헐적으로 끊김 → Drive 작업은 로컬 복사 후 마지막에만 쓰기.
- 실 AutoCAD 쓰기는 `Drawing*` 스크래치 도면에 명시적 document_id 바인딩 후만. 다른 세션이 실제 도면을 같은 AutoCAD에 연다.
- GitHub 푸시는 허용, 머지는 권한 검사로 차단됨(§1).
