using System.Text.Json;
using ACadSharp;
using ACadSharp.IO;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: GoldDwgSummary <dwg> [out.json]");
    return 2;
}

var path = args[0];
var outPath = args.Length > 1 ? args[1] : Path.ChangeExtension(path, ".gold.json");
var doc = DwgReader.Read(path);

var layers = doc.Layers.Select(l => l.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
var blocks = doc.BlockRecords.Select(b => b.Name).Where(n => !n.StartsWith('*')).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

var entityTypeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
var layerEntityCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
var insertBlockCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
long modelEntities = 0;

foreach (var e in doc.Entities)
{
    modelEntities++;
    var tn = e.GetType().Name;
    entityTypeCounts[tn] = entityTypeCounts.GetValueOrDefault(tn) + 1;
    var ln = e.Layer?.Name ?? "(none)";
    layerEntityCounts[ln] = layerEntityCounts.GetValueOrDefault(ln) + 1;
    if (e is ACadSharp.Entities.Insert ins)
    {
        var bn = ins.Block?.Name ?? "?";
        insertBlockCounts[bn] = insertBlockCounts.GetValueOrDefault(bn) + 1;
    }
}

// Heuristic mark-like texts (short tokens matching C/G/B/… + digits) — counts only
var markPrefix = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
foreach (var t in doc.Entities.OfType<ACadSharp.Entities.TextEntity>())
{
    var v = (t.Value ?? "").Trim();
    if (v.Length is >= 2 and <= 12 && char.IsLetter(v[0]))
    {
        var pfx = new string(v.TakeWhile(char.IsLetter).ToArray());
        if (pfx.Length is >= 1 and <= 3)
        {
            markPrefix[pfx] = markPrefix.GetValueOrDefault(pfx) + 1;
        }
    }
}

var summary = new Dictionary<string, object?>
{
    ["source_basename"] = Path.GetFileName(path),
    ["source_size_bytes"] = new FileInfo(path).Length,
    ["cad_version"] = doc.Header.Version.ToString(),
    ["model_entity_count"] = modelEntities,
    ["layer_count"] = layers.Count,
    ["block_def_count"] = blocks.Count,
    ["entity_types_top"] = entityTypeCounts.OrderByDescending(kv => kv.Value).Take(25).ToDictionary(kv => kv.Key, kv => kv.Value),
    ["layers_top_by_entities"] = layerEntityCounts.OrderByDescending(kv => kv.Value).Take(30).ToDictionary(kv => kv.Key, kv => kv.Value),
    ["insert_blocks_top"] = insertBlockCounts.OrderByDescending(kv => kv.Value).Take(30).ToDictionary(kv => kv.Key, kv => kv.Value),
    ["text_mark_prefix_heuristic"] = markPrefix.OrderByDescending(kv => kv.Value).Take(40).ToDictionary(kv => kv.Key, kv => kv.Value),
    ["note"] = "Numeric/summary only. Proprietary DWG contents are NOT included. Do not commit the DWG.",
};

var json = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(outPath, json);
Console.WriteLine(json);
return 0;

