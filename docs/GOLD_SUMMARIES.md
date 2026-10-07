# GOLD_SUMMARIES (numeric only)

**Policy:** Proprietary DWG/xlsm contents are **not** committed. Only counts and redacted summaries land in-repo.

## Work project DWG (redacted)

| Metric | Value |
|---|---|
| Source (redacted) | `work_project_redacted_2026-05-19.dwg` |
| Size | 5,899,652 bytes |
| CAD version | AC1015 |
| Model entities | 21,127 |
| Layers | 216 |
| Block definitions | 129 |

### Entity type counts (top)

| Type | Count |
|---|---:|
| Line | 10691 |
| LwPolyline | 2789 |
| Insert | 1917 |
| TextEntity | 1817 |
| Arc | 1506 |
| Circle | 759 |
| DimensionLinear | 564 |
| Hatch | 558 |
| DimensionAligned | 310 |
| Solid | 105 |
| MText | 69 |
| Leader | 19 |

### High-strength bolt block inserts (counts)

| Block | Inserts |
|---|---:|
| HEADED_HT_BOLT-TAIL-M20 | 209 |
| HEADED_HT_BOLT-HEAD-M20 | 199 |
| HEADED_HT_BOLT-PLAN-M20F10T | 194 |
| HEADED_HT_BOLT-PLAN-M22F10T | 120 |
| HEADED_HT_BOLT-PLAN-M16F10T | 20 |
| HEADED_HT_BOLT-HEAD-M16 | 6 |
| HEADED_HT_BOLT-TAIL-M16 | 6 |

### Layer entity concentration (top 10)

| Layer | Entities |
|---|---:|
| E | 4384 |
| 0 | 2062 |
| Anchor Bolt | 1308 |
| SEC | 1169 |
| E2 | 1124 |
| WIN | 1063 |
| S-TEXT | 675 |
| PAPER | 600 |
| S-STEEL BEAM | 553 |
| ELE | 483 |

### Notes

- Machine-local JSON (gitignored `out/`): `out/gold/work_project_summary.json` (redacted basename).
- Member/BOM mark extraction from XData was weak via text heuristic; bolt insert counts are the strongest connection-density signal.
- Do **not** add the original DWG or company/project title to git.
- Extractor: `tools/GoldDwgSummary/` (dev utility).
