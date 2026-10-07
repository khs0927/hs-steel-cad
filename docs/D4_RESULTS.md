# D4 Results — 조립도·BOM (2026-10-08)

- **단계**: D4 조립도·배치/입면·BOM/볼트표
- **시각**: 2026-10-08 Asia/Seoul (UTC+9)
- **범위**: full power-cad M merge 없음, SAMPLE-FRAME 2996 live draw 생략

## Deliverables

| 항목 | 내용 |
|---|---|
| `BomTable` / CSV·JSON | `src/HsSteel.Drafting/Bom.cs` — 조립목록·자재집계표·볼트집계표 structured + `ToCsv`/`ToJson` |
| 도면 BOM | `BomSheets` 한글 헤더 + **볼트집계표** 시트 |
| 입면 | `LayoutElevation` — 그리드별 YZ/XZ 입면, Plan kinds에 `V-` 시트 동봉 |
| cope 작도 | `MemberViews` Front/Top + Part/Assembly detail에 flange cope/scallop |
| weld leader | `Callouts.LeaderIfLongEnough` — 최소 3 paper mm × scale로 신장 (COLLAB #3) |
| MCP | `hs_bom` optional `csv_path`/`json_path`; kinds alias `elevation`/`입면` |

## Tests

- `HsSteel.Tests`: **123/123 PASS** (incl. new `DraftingD4Tests` ×7)
- Filter smoke: `DraftingD4|Assembly_sheets_carry` → 8/8 PASS

## Next (sequential)

1. K3 weak queries (`전단접합 스캘럽`, lexical `엔드플레이트`) — QueryExpand/FTS
2. D3 residual: interactive grid MCP / more shop-detail polish
3. Optional later: SAMPLE-FRAME live create_many (not required for D4)
4. Skip: full M merge; leave Claude WIP (`Legacy/`, `GoldenMxxTests`, asset phase-2 dirs)
