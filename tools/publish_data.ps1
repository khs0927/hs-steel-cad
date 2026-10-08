# Stage HS-STEEL knowledge data into a versioned folder layout and (optionally) upload it to Google Drive.
#
#   powershell -File tools\publish_data.ps1            # stage only  -> D:\HS-STEEL-KG
#   powershell -File tools\publish_data.ps1 -Upload    # stage + rclone copy -> gdrive:HS-STEEL-KG
#
# Layout (same locally and on Drive):
#   HS-STEEL-KG\
#     README.md
#     db\<yyyy-MM-dd>_<commit>\hs_assets.db, SHA256SUMS.txt, build_info.json
#     db\latest\                      (copy of the newest version)
#     knowledge\<yyyy-MM-dd>_<commit>\chunks.jsonl, embeddings.npy, embeddings_ids.json, coverage.json, vba_procedures.jsonl, *_report.json
#     models\multilingual-e5-small-onnx\   (offline query embedder; versioned by model, not by date)
#     manifests\<yyyy-MM-dd>_<commit>\manifest.json, reborn_manifest.json
#     verification\<yyyy-MM-dd>_<commit>\  (live AutoCAD / RAG eval reports, small JSON only)
param(
    [string]$Repo = (Resolve-Path "$PSScriptRoot\.."),
    [string]$Stage = "D:\HS-STEEL-KG",
    [string]$Remote = "gdrive:HS-STEEL-KG",
    [switch]$Upload
)
$ErrorActionPreference = "Stop"
$commit = (git -C $Repo rev-parse --short HEAD).Trim()
$ver = "{0}_{1}" -f (Get-Date -Format "yyyy-MM-dd"), $commit
$out = Join-Path $Repo "out"

function Copy-Into([string]$dir, [string[]]$files) {
    New-Item -ItemType Directory -Force $dir | Out-Null
    foreach ($f in $files) { if (Test-Path $f) { Copy-Item $f $dir -Force } }
}

$db = Join-Path $Stage "db\$ver"
Copy-Into $db @("$out\hs_assets.db")
(Get-FileHash "$db\hs_assets.db" -Algorithm SHA256).Hash + "  hs_assets.db" | Set-Content "$db\SHA256SUMS.txt" -Encoding utf8
@{ version = $ver; commit = $commit; built_utc = (Get-Date).ToUniversalTime().ToString("o");
   rebuild = "dotnet run --project src/HsSteel.Knowledge -- build-db" } | ConvertTo-Json | Set-Content "$db\build_info.json" -Encoding utf8
$latest = Join-Path $Stage "db\latest"
if (Test-Path $latest) { Remove-Item $latest -Recurse -Force }
Copy-Item $db $latest -Recurse

$k = "$out\knowledge"
Copy-Into (Join-Path $Stage "knowledge\$ver") @("$k\chunks.jsonl", "$k\embeddings.npy", "$k\embeddings_ids.json", "$k\coverage.json",
    "$k\vba_procedures.jsonl", "$k\chunks_report.json", "$k\content_gaps_report.json")
$model = Join-Path $Stage "models\multilingual-e5-small-onnx"
if (-not (Test-Path "$model\model.onnx")) { Copy-Into $model (Get-ChildItem "$k\model" -File | ForEach-Object FullName) }
Copy-Into (Join-Path $Stage "manifests\$ver") @("$Repo\assets\manifest.json", "$Repo\assets\reborn_manifest.json")
Copy-Into (Join-Path $Stage "verification\$ver") @("$out\explore_results\live_cad_smoke_report.json", "$out\graph_rag_eval_results.json")
Copy-Item "$Repo\docs\DATA_STORAGE.md" (Join-Path $Stage "README.md") -Force

Write-Host "staged $ver -> $Stage"
if ($Upload) {
    rclone copy $Stage $Remote --progress
    if ($LASTEXITCODE -ne 0) { throw "rclone failed; if the token expired run: rclone config reconnect gdrive:" }
    Write-Host "uploaded -> $Remote"
}
