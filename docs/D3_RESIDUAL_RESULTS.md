# D3 Residual Results — 대화형 그리드 MCP + shop-detail polish (2026-10-08)

## Deliverables
| 항목 | 내용 |
|---|---|
| `hs_project_frame` | `beam_connection` (shear_tab\|end_plate\|한글 alias), `scallop`/`connection_gap`/`weld_gap`/`material` |
| `hs_grid_from_bays` | bay → X1..Xn / Y1..Yn (+ optional levels) without placing members |
| `hs_project_rules` | get/set `DetailRules` on existing project |
| Cope 주기 | `Callouts.CopeNote` + Part/Assembly detail notes (`COPE L=… D=… R=…`) |
| Summary | returns `beam_connection` + `rules` when present |

## Tests
`ModelingD3ResidualTests` + full suite green after this chunk.

## Note
Cope outline drawing already landed with D4; this chunk closes the MCP/rules gap called out in ARCHITECTURE D3 residual.

## Test results
- HsSteel.Tests: **128/128 PASS**
- HsSteel.Knowledge.Tests: **57/57 PASS**
