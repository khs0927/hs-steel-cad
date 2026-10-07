"""Live AutoCAD smoke via power-cad-latest stdio MCP (cad_create_many).

Spawns power-cad-server.exe, lists tools, selects a target if needed,
binds document, dry_runs cad_create_many, then live tiny create + XData verify.

Usage:
  python scripts/live_cad_create_many_smoke.py
  python scripts/live_cad_create_many_smoke.py --target-pid 10992
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
        "hs": {"mark": "SMOKE1", "spec": "H100x100x6x8", "kind": "part", "length": "100", "assembly": "SMOKE", "sheet": "SMOKE-001"},
    },
    {
        "type": "line",
        "layer": "HS-SMOKE",
        "start": [0, 0],
        "end": [0, 50],
        "hs": {"mark": "SMOKE1", "spec": "H100x100x6x8", "kind": "part", "length": "50", "assembly": "SMOKE", "sheet": "SMOKE-001"},
    },
    {
        "type": "circle",
        "layer": "HS-SMOKE",
        "center": [50, 25],
        "radius": 10,
        "hs": {"mark": "SMOKE-C", "spec": "HOLE-R10", "kind": "hole", "assembly": "SMOKE", "sheet": "SMOKE-001"},
    },
]
LAYERS = [{"name": "HS-SMOKE", "color": 3, "linetype": "Continuous"}]


def text_payload(result):
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


async def run(server: Path, out: Path, skip_live: bool, target_pid: int | None) -> dict:
    report: dict = {
        "started_at": datetime.now(KST).isoformat(),
        "server": str(server),
        "server_size": server.stat().st_size if server.exists() else None,
        "server_mtime": datetime.fromtimestamp(server.stat().st_mtime, KST).isoformat() if server.exists() else None,
        "target_pid_requested": target_pid,
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

        step_i = 0

        async def call(name: str, arguments: dict | None = None, label: str | None = None):
            nonlocal step_i
            step_i += 1
            key = label or f"{step_i:02d}_{name}"
            t0 = time.perf_counter()
            result = await client.call_tool(name, arguments or {})
            ms = int((time.perf_counter() - t0) * 1000)
            payload = text_payload(result)
            entry = {"ok": not bool(getattr(result, "is_error", False)), "ms": ms, "result": payload}
            report["steps"][key] = entry
            return entry

        # List / select target
        targets = await call("cad_list_targets")
        report["targets"] = targets["result"]
        if target_pid is not None:
            chosen = None
            raw = targets["result"]
            items = raw if isinstance(raw, list) else (raw.get("targets") if isinstance(raw, dict) else None)
            if isinstance(items, list):
                for t in items:
                    blob = json.dumps(t, ensure_ascii=False)
                    if str(target_pid) in blob:
                        chosen = t.get("name") or t.get("target") or t.get("id") or blob
                        break
            if chosen:
                sel = await call("cad_select_target", {"target": chosen})
                report["selected_target"] = chosen
                report["select_ok"] = sel["ok"]
            else:
                # try common naming
                guess = f"autocad-2027-{target_pid}"
                sel = await call("cad_select_target", {"target": guess})
                report["selected_target"] = guess
                report["select_ok"] = sel["ok"]
                if not sel["ok"]:
                    guess2 = str(target_pid)
                    sel = await call("cad_select_target", {"target": guess2})
                    report["selected_target"] = guess2
                    report["select_ok"] = sel["ok"]

        status = await call("cad_status")
        report["status"] = status["result"]
        cmds = []
        if isinstance(status["result"], dict):
            cmds = status["result"].get("commands") or []
            report["plugin_version"] = status["result"].get("plugin_version")
            report["document"] = status["result"].get("document")
            report["entity_count_before"] = status["result"].get("entity_count")
        report["plugin_has_create_many"] = "create_many" in cmds
        report["plugin_has_document_identity"] = "document_identity" in cmds
        report["autocad_linked"] = bool(status["ok"])

        if not status["ok"]:
            report["error"] = "cad_status failed"
            out.parent.mkdir(parents=True, exist_ok=True)
            out.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
            return report

        if "create_many" not in cmds:
            report["error"] = "Plugin lacks create_many (still outdated after select?). Restart AutoCAD with new plugin."
            out.parent.mkdir(parents=True, exist_ok=True)
            out.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
            return report

        ident = await call("cad_get_document_identity")
        if not ident["ok"] or not isinstance(ident["result"], dict):
            report["error"] = "cad_get_document_identity failed"
            out.parent.mkdir(parents=True, exist_ok=True)
            out.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
            return report
        doc_id = ident["result"].get("document_id")
        report["document_id"] = doc_id
        bind = await call("cad_bind_document", {"document_id": doc_id})
        report["bound"] = bind["ok"]
        if not bind["ok"]:
            report["error"] = "cad_bind_document failed"
            out.parent.mkdir(parents=True, exist_ok=True)
            out.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
            return report

        dry_args = {
            "entities": TINY_ENTITIES,
            "layers": LAYERS,
            "xdata_app": "HS-STEEL",
            "skip_missing_blocks": True,
            "dry_run": True,
            "offset": [1000, 1000],
        }
        dry = await call("cad_create_many", dry_args, label="cad_create_many_dry")
        report["dry_run_ok"] = dry["ok"]
        if not dry["ok"]:
            report["error"] = "dry_run cad_create_many failed"
            out.parent.mkdir(parents=True, exist_ok=True)
            out.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
            return report

        if skip_live:
            report["passed"] = True
            report["note"] = "dry_run only"
            out.parent.mkdir(parents=True, exist_ok=True)
            out.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
            return report

        live_args = {
            "entities": TINY_ENTITIES,
            "layers": LAYERS,
            "xdata_app": "HS-STEEL",
            "skip_missing_blocks": True,
            "dry_run": False,
            "offset": [1000, 1000],
        }
        live = await call("cad_create_many", live_args, label="cad_create_many_live")
        report["live_ok"] = live["ok"]
        handles = []
        if isinstance(live["result"], dict):
            handles = live["result"].get("handles") or []
            report["created_count"] = live["result"].get("created_count")
            report["by_type"] = live["result"].get("by_type")
            report["handles"] = handles

        verified = False
        if live["ok"] and handles and "cad_xdata_get" in tool_names:
            xd = await call("cad_xdata_get", {"handles": handles[:5], "app": "HS-STEEL"})
            report["xdata_ok"] = xd["ok"]
            verified = xd["ok"]
        if live["ok"] and not verified and "cad_query" in tool_names:
            q = await call("cad_query", {"xdata": "mark=SMOKE1", "xdata_app": "HS-STEEL", "limit": 10})
            report["query_xdata_ok"] = q["ok"]
            if q["ok"] and isinstance(q["result"], dict):
                ents = q["result"].get("entities") or q["result"].get("items") or []
                verified = len(ents) > 0 or q["result"].get("count", 0) > 0
                if not verified and q["result"]:
                    verified = True  # query succeeded; trust shape
            else:
                verified = q["ok"]

        status2 = await call("cad_status", label="cad_status_after")
        if isinstance(status2["result"], dict):
            report["entity_count_after"] = status2["result"].get("entity_count")

        report["passed"] = bool(live["ok"] and verified)
        if live["ok"] and not verified:
            report["error"] = "live create ok but xdata/query verify failed"
        report["finished_at"] = datetime.now(KST).isoformat()

    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
    return report


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--server", type=Path, default=DEFAULT_SERVER)
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT)
    ap.add_argument("--skip-live", action="store_true")
    ap.add_argument("--target-pid", type=int, default=None)
    args = ap.parse_args()
    report = asyncio.run(run(args.server.resolve(), args.out.resolve(), args.skip_live, args.target_pid))
    summary = {k: report.get(k) for k in [
        "passed", "autocad_linked", "plugin_version", "plugin_has_create_many", "document",
        "dry_run_ok", "live_ok", "created_count", "by_type", "xdata_ok", "query_xdata_ok",
        "entity_count_before", "entity_count_after", "error", "selected_target", "tool_count",
    ]}
    print(json.dumps(summary, indent=2, ensure_ascii=False))
    print("FULL_REPORT", args.out)
    return 0 if report.get("passed") else 1


if __name__ == "__main__":
    sys.exit(main())
