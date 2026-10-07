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
