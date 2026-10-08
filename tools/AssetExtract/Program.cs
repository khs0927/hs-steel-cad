// HsSteel.AssetExtract — CAD 없이(AutoCAD/DXF 변환 없이) ACadSharp로 DWG를 직접 읽어
// Drive DB 카드(powercad.asset.card/v1)를 만든다.
//
//   dotnet run --project tools/AssetExtract -- --root <dwg dir> --out <dir> --namespace <ns> [--prefix hsa-] [--source-root "C:\cad\HSSTEEL"] [--modelspace-as-block]
//
// 출력: manifest.json, catalog.jsonl, blocks/<id>.json, geometry/<id>.json (+ tables/<id>.json: ACAD_TABLE 셀 텍스트)
using HsSteel.AssetExtract;

bool msp = false; int mspMax = 5000;
string? root = null, outDir = null, ns = "hs-steel-acad", prefix = "hsa-", sourceRoot = null;
for (int i = 0; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"missing value for {args[i]}");
    switch (args[i])
    {
        case "--root": root = Next(); break;
        case "--out": outDir = Next(); break;
        case "--namespace": ns = Next(); break;
        case "--prefix": prefix = Next(); break;
        case "--source-root": sourceRoot = Next(); break;
        case "--modelspace-as-block": msp = true; break;
        case "--modelspace-max": mspMax = int.Parse(Next()); break;
        case "-h": case "--help":
            Console.WriteLine("usage: --root <dwg dir> --out <dir> --namespace <ns> [--prefix hsa-] [--source-root <label>] [--modelspace-as-block] [--modelspace-max 5000]");
            return 0;
        default: Console.Error.WriteLine($"unknown arg {args[i]}"); return 2;
    }
}
if (root is null || outDir is null) { Console.Error.WriteLine("--root and --out are required"); return 2; }

var sw = System.Diagnostics.Stopwatch.StartNew();
var result = Extractor.Run(new ExtractOptions(root, outDir, ns, prefix, sourceRoot ?? root, msp, mspMax));
Console.WriteLine($"files {result.FilesParsed}/{result.FilesTotal} parsed, definitions {result.DefinitionsSeen}, unique blocks {result.UniqueBlocks}, " +
                  $"tables {result.TablesSeen} ({result.TableCards} unique, {result.TableCellsNonEmpty} non-empty cells), failed {result.Failed.Count}, {sw.ElapsedMilliseconds} ms");
foreach (var f in result.Failed) Console.WriteLine($"  FAIL {f}");
return result.Failed.Count == result.FilesTotal && result.FilesTotal > 0 ? 1 : 0;
