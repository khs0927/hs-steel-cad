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
