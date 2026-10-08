# HsSteel.AssetExtract — CAD 없이 DWG → Drive DB 카드

AutoCAD도, DXF 변환(LibreDWG)도 없이 **ACadSharp 3.8.0으로 DWG를 직접 읽어** power-cad 자산 DB 카드
(`powercad.asset.card/v1`)를 만든다. `HsSteel.Assets`(BlockCatalog·DwgStyleReader)와 같은 리더를 쓴다.

```bash
dotnet run --project tools/AssetExtract -- \
  --root /path/to/HSSTEEL --out /path/to/PowerCad-Assets/hs-steel-acad \
  --namespace hs-steel-acad --prefix hsa- --source-root 'C:\cad\HSSTEEL' --modelspace-as-block
```

| 옵션 | 뜻 |
|---|---|
| `--root` | DWG 폴더(하위 폴더 포함, `*.dwg`) |
| `--out` | 출력 폴더 (Drive DB 하위 폴더) |
| `--namespace` | 카드 `namespace` |
| `--prefix` | `asset_id` 접두사 (기본 `hsa-`) |
| `--source-root` | provenance에 남길 원본 경로 표기 (예: `C:\cad\HSSTEEL`) |
| `--modelspace-as-block` | wblock 라이브러리처럼 파일 모형 공간 전체를 파일 이름의 블록 카드로도 만든다 (`extract_blocks.py --modelspace-as-block`와 동일, 5,000 객체 이하) |

## 출력 (hs-steel-blocks와 같은 배치)

- `manifest.json` — `powercad.asset.manifest/v1` (+ `acad_tables_seen`, `table_cards`, `table_cells_non_empty`, `counts_by_source_kind`)
- `catalog.jsonl` — 카드 한 줄씩
- `blocks/<asset_id>.json` — 카드 (`attribute_defs`(tag/prompt/default/position), `nested_blocks`, `insert_count`, `base_point`/`original_base_point`/`origin_offset`, `is_dynamic`, `acad_table`)
- `geometry/<asset_id>.json` — 블록: `powercad.block.geometry/v1` (기준점 기준 좌표), 표: `powercad.table.content/v1` (행·열·셀 텍스트·행 높이·열 너비)

규칙(extract_blocks.py와 같음): `*D/*X/*E` 익명 블록 제외, 객체 2개 미만 익명 블록 제외, 기준점이 도형 bbox에서 10,000mm 넘게 떨어지면 bbox 좌하단으로 재기준(`origin_normalized`), 도형 해시로 중복 제거, `asset_id` = 접두사 + 이름 슬러그(겹치면 `-해시8`).

**추가:** LibreDWG가 버리는 `ACAD_TABLE`을 읽어 셀 텍스트를 `category:"table"` 카드로 만든다. `*T` 표 그래픽 블록 카드는 `acad_table`로 그 표 카드를 가리킨다.

미리보기 PNG(`thumbs/`)는 만들지 않는다(`thumb: null`). 필요하면 extract_blocks.py의 렌더러를 쓴다.
