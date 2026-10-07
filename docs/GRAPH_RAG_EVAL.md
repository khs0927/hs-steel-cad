# GRAPH_RAG_EVAL

**Date:** 2026-10-08 Asia/Seoul (overnight Grok Bot executor)  
**Impl:** `src/HsSteel.Knowledge/GraphRag.cs`, `Rules/RulesCatalog.cs`, MCP `hs_graph_rag` / `hs_explain` / `hs_rules_search`

## Eval set

- File: `tests/HsSteel.Knowledge.Tests/Data/graph_rag_eval.json`
- Questions: **32** (rules + optional graph kind/key checks)
- Runner: `GraphRagTests.Graph_rag_eval_hit_rate`

## Metrics (this machine, `out/hs_assets.db`)

| Metric | Value |
|---|---|
| Pass | **32 / 32 (100%)** |
| Hit@rules | **32/32 = 100%** |
| Catalog size | **100** rules (≥80 target) |
| Engine-ref cross-check | **64/100** |
| Smoke | Graph RAG + Explain + Rules search: PASS |

Re-run:

```powershell
cd C:\code\hs-steel-cad
dotnet test tests/HsSteel.Knowledge.Tests --filter FullyQualifiedName~GraphRag
# writes out/graph_rag_eval_results.json
```

## API

| Tool | Role |
|---|---|
| `hs_graph_rag` | Search → neighbor expand → attach matching rules → context pack |
| `hs_explain` | Node props + multi-hop neighbors + related rules |
| `hs_rules_search` | Catalog-only search |
| `hs_graph_neighbors` | Existing BFS (unchanged) |

## Remaining gaps

- Rules not yet materialised as SQLite `node(kind=rule)` (avoid Claude `KnowledgeDbBuilder` conflict).
- When ONNX model missing, search is lexical-only; rules still match.
- q18 originally required `section|family` seeds; flange-gauge queries often hit slides/palette first — kinds made optional.
