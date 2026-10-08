# HS-STEEL asset registry (`hs-steel-asset-registry/1`)

`asset-registry.json` lists every drawing asset hs-steel-cad knows about, in one machine-readable file, so a
client (power-cad-mcp, an agent, a script) can find an asset by id/category/tag/drawing type and know **where it
is, what knobs it takes, which layer it goes on, and which tool loads it today**.

| File | |
|---|---|
| `asset-registry.json` | The registry (generated; deterministic, no timestamps). Do not edit by hand. |
| `asset-registry.schema.json` | JSON Schema (draft 2020-12). |
| `../../tools/AssetRegistry/` | Generator / sync checker (`HsSteel.AssetRegistry`). |
| `../../tests/HsSteel.Tests/AssetRegistryTests.cs` | Schema validation, sync check, id uniqueness, source-path checks. |

```bash
dotnet run --project tools/AssetRegistry              # regenerate after changing manifest.json or the drafting/domain standards
dotnet run --project tools/AssetRegistry -- --check   # exit 1 when the committed file is stale (also covered by the tests)
```

## Sources

- **Legacy files** (`source.root = "legacy"`): every non-excluded row of `assets/manifest.json` (blocks, section and
  SCSS tables, Project.dat, new-project set, linetypes, font map, aliases, dialogs, palettes, workbooks, slides,
  icons (one asset per folder), menus/plugins, plot files, manuals, golden project sets). The files themselves are
  licensed and not in git: resolve `source.path` against the legacy root = parent of `HS_STEEL_LEGACY`
  (default `C:\HS-STEEL`). Block base point / extents / attribute tags / layers need the DWG, so the registry
  leaves `insertion.basePoint = null` and points at `hs_asset_get kind=block` (hs_assets.db) for them.
  `props.blockCategory` is a first-pass classification from the file name (`BlockCategory` in the generator).
- **Code-defined standards** (`source.root = "repo"`, `disposition = "code"`): layers (`Layers.All`), HS-KOR text and
  dimension styles, the A3 placeholder frame, sheet generators with sheet prefixes, callouts and weld-symbol blocks,
  data tables (scales, fillet weld, bolt gauges, bolt grades, assembly mark heads, paper constants), project options
  and DetailRules (with defaults), the rules catalog, shape kinds, connection kinds (`ConnectionDef` subtypes, with
  the `hs_connection_add` fields), and the frame model template. These are read by reflection from the compiled
  code, so the registry follows code changes after a regenerate.

## Asset fields

`id` (`<category>/<slug>`), `category`, `name`, `description`, `source {root, path, symbol, sha256, size}`, `format`,
`disposition` (`ingest` | `reference` | `code`), `insertion {basePoint, basePointSource, units, scaleRule}` or null,
`parameters [{name, type, unit, default, required, enum, description}]`, `layer`, `tags`, `drawingTypes`
(`assembly` `part` `plate` `plan` `elevation` `bom` `any`), `loadedBy [{kind, ref, note}]`
(`mcp-tool` | `mcp-resource` | `csharp` | `knowledge-db` | `power-cad`), `props` (category specific).
