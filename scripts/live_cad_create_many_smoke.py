"""Live AutoCAD smoke via power-cad-latest stdio MCP (cad_create_many).

Spawns power-cad-server.exe, lists tools, proves AutoCAD link with a read tool,
dry_runs cad_create_many, then (if dry OK) creates a tiny payload and verifies XData.

Usage:
  python scripts/live_cad_create_many_smoke.py
  python scripts/live_cad_create_many_smoke.py --server PATH --out PATH
"""
from __future__ import annotations

import argparse
import asyncio
import json
import sys
import time
from datetime import datetime, timezone, timedelta
from pathlib import Path

from mcp import Client
from mcp.client.stdio import StdioServerParameters

KST = timezone(timedelta(hours=9))

DEFAULT_SERVER = Path(r"C:\code\power-cad-latest\dist\server\win-x64-publish-tmp\power-cad-server.exe")
DEFAULT_OUT = Path(r"C:\code\hs-steel-cad\out\explore_results\live_cad_smoke_report.json")

TINY_ENTITIES = [
    {
        "type": "line",
        "layer": "HS-SMOKE",
        "start": [0, 0],
        "end": [100, 0],
        "hs": {
            "mark": "SMOKE1",
            "spec": "H100x100x6x8",
            "kind": "part",
            "length": "100",
            "assembly": "SMOKE",
            "sheet": "SMOKE-001",
        },
    },
    {
        "type": "line",
        "layer": "HS-SMOKE",
        "start": [0, 0],
        "end": [0, 50],
        "hs": {
            "mark": "SMOKE1",
            "spec": "H100x100x6x8",
            "kind": "part",
            "length": "50",
            "assembly": "SMOKE",
            "sheet": "SMOKE-001",
        },
    },
    {
        "type": "circle",
        "layer": "HS-SMOKE",
        "center": [50, 25],
        "radius": 10,
        "hs": {
            "mark": "SMOKE-C",
            "spec": "HOLE-R10",
            "kind": "hole",
            "assembly": "SMOKE",
            "sheet": "SMOKE-001",
        },
    },
]

LAYERS = [{"name": "HS-SMOKE", "color": 3, "linetype": "Continuous"}]


def text_payload(result) -> dict | str:
    chunks = []
    for c in getattr(result, "content", []) or []:
        if getattr(c, "type", None) == "text" and getattr(c, "text", None):
            chunks.append(c.text)
    raw = "\n".join(chunks) if chunks else ""
    if not raw:
        return {"_empty": True, "is_error": bool(getattr(result, "is_error", False))}
    try:
        return json.loads(raw)
    except json.JSONDecodeError:
        return raw


async def run(server: Path, out: Path, skip_live: bool) -> dict:
    report: dict = {
        "started_at": datetime.now(KST).isoformat(),
        "server": str(server),
        "server_size": server.stat().st_size if server.exists() else None,
        "server_mtime": datetime.fromtimestamp(server.stat().st_mtime, KST).isoformat() if server.exists() else None,
        "passed": False,
        "steps": {},
    }
    if not server.is_file():
        report["error"] = f"server binary missing: {server}"
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
        return report

    params = StdioServerParameters(command=str(server))
    async with Client(params) as client:
        tools = await client.list_tools()
        tool_names = sorted(t.name for t in tools.tools)
        report["tool_count"] = len(tool_names)
        report["tools"] = tool_names
        report["has_cad_create_many"] = "cad_create_many" in tool_names
        report["has_cad_xdata_get"] = "cad_xdata_get" in tool_names

        async def call(name: str, arguments: dict | None = None):
            t0 = time.perf_counter()
            result = await client.call_tool(name, arguments or {})
            ms = int((time.perf_counter() - t0) * 1000)
            payload = text_payload(result)
            entry = {
                "ok": not bool(getattr(result, "is_error", False)),
                "ms": ms,
                "result": payload,
            }
            report["steps"][name if name not in report["steps"] else f"{name}#{len(report['steps'])}"] = entry
            return entry

        # Prefer safe read tools first to prove AutoCAD link.
        read_candidates = [
            ("cad_get_document_identity", {}),
            ("cad_inspect", {"sections": ["current", "extents"]}),
            ("cad_status", {}),
            ("cad_inventory", {}),
            ("cad_query", {"types": ["LINE"], "limit": 5, "summary": True}),
        ]
        linked = False
        for name, args in read_candidates:
            if name not in tool_names:
                continue
            entry = await call(name, args)
            if entry["ok"]:
                linked = True
                report["link_tool"] = name
                break
            report.setdefault("link_errors", []).append({name: entry["result"]})

        report["autocad_linked"] = linked
        if not linked:
            report["error"] = "No read tool succeeded; AutoCAD plugin pipe may be down."
            out.parent.mkdir(parents=True, exist_ok=True)
            out.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
            return report

        # Optional: open a new empty drawing if the tool exists (protects user's drawing).
        for new_name in ("cad_new_drawing", "cad_new", "cad_document_new"):
            if new_name in tool_names:
                await call(new_name, {})
                report["new_drawing_tool"] = new_name
                break

        if "cad_create_many" not in tool_names:
            report["error"] = "cad_create_many not listed by server"
            out.parent.mkdir(parents=True, exist_ok=True)
            out.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
            return report

        dry_args = {
            "entities": TINY_ENTITIES,
            "layers": LAYERS,
            "xdata_app": "HS-STEEL",
            "skip_missing_blocks": True,
            "dry_run": True,
            "offset": [900000, 900000],
        }
        dry = await call("cad_create_many", dry_args)
        report["dry_run_ok"] = bool(dry["ok"])
        if not dry["ok"]:
            report["error"] = "dry_run cad_create_many failed"
            out.parent.mkdir(parents=True, exist_ok=True)
            out.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
            return report

        if skip_live:
            report["passed"] = True
            report["note"] = "dry_run only (--skip-live)"
            out.parent.mkdir(parents=True, exist_ok=True)
            out.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
            return report

        live_args = {
            "entities": TINY_ENTITIES,
            "layers": LAYERS,
            "xdata_app": "HS-STEEL",
            "skip_missing_blocks": True,
            "dry_run": False,
            "offset": [900000, 900000],
        }
        live = await call("cad_create_many", live_args)
        report["live_ok"] = bool(live["ok"])
        handles = []
        if isinstance(live["result"], dict):
            handles = live["result"].get("handles") or []
            report["created_count"] = live["result"].get("created_count")
            report["by_type"] = live["result"].get("by_type")

        if live["ok"] and handles and "cad_xdata_get" in tool_names:
            xd = await call("cad_xdata_get", {"handles": handles[:5], "app": "HS-STEEL"})
            report["xdata_ok"] = bool(xd["ok"])
        elif live["ok"] and "cad_query" in tool_names:
            q = await call(
                "cad_query",
                {"xdata": "mark=SMOKE1", "xdata_app": "HS-STEEL", "limit": 10},
            )
            report["query_xdata_ok"] = bool(q["ok"])

        report["passed"] = bool(live["ok"]) and (
            report.get("xdata_ok") or report.get("query_xdata_ok") or True
        )
        # Prefer verification when available; still pass live create if verify tools missing.
        if live["ok"] and ("cad_xdata_get" in tool_names or "cad_query" in tool_names):
            report["passed"] = bool(report.get("xdata_ok") or report.get("query_xdata_ok"))

    report["finished_at"] = datetime.now(KST).isoformat()
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
    return report


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--server", type=Path, default=DEFAULT_SERVER)
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT)
    ap.add_argument("--skip-live", action="store_true")
    args = ap.parse_args()
    report = asyncio.run(run(args.server.resolve(), args.out.resolve(), args.skip_live))
    print(json.dumps({k: report[k] for k in report if k != "tools"}, indent=2, ensure_ascii=False))
    print("tools:", ",".join(report.get("tools") or [])[:500])
    print("FULL_REPORT", args.out)
    return 0 if report.get("passed") else 1


if __name__ == "__main__":
    sys.exit(main())
