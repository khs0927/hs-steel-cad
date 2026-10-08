using HsSteel.AssetRegistry;

// Rebuilds assets/registry/asset-registry.json from assets/manifest.json + the C# drafting/domain standards.
//   dotnet run --project tools/AssetRegistry                 write the registry
//   dotnet run --project tools/AssetRegistry -- --check      exit 1 when the committed registry is stale
//   options: --repo <root>  --out <path>
var check = args.Contains("--check");
string? Opt(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

var repo = Opt("--repo") ?? AssetRegistryBuilder.FindRepoRoot();
var output = Opt("--out") ?? Path.Combine(repo, AssetRegistryBuilder.RegistryRel);
var text = AssetRegistryBuilder.Serialize(AssetRegistryBuilder.Build(repo));
if (check)
{
    var current = File.Exists(output) ? File.ReadAllText(output) : "";
    if (current.ReplaceLineEndings("\n") != text)
    {
        Console.Error.WriteLine($"STALE: {output} differs from the generated registry. Run: dotnet run --project tools/AssetRegistry");
        return 1;
    }

    Console.WriteLine($"OK: {output} is in sync.");
    return 0;
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output, text);
Console.WriteLine($"wrote {output}");
return 0;
