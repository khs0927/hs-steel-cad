namespace HsSteel.Knowledge;

// Plain ingest DTOs. Other readers (blocks, support, docs chunker) map into these; SourceRelPath is the
// manifest relPath ('/'-separated, relative to the HS-STEEL root) and is resolved to source_file.id.

public sealed record BlockAttributeRecord(string Tag, string? Prompt, string? Default);

public sealed record BlockRecord(
    string Name,
    string SourceRelPath,
    double BaseX, double BaseY, double BaseZ,
    double MinX, double MinY, double MaxX, double MaxY,
    int EntityCount,
    string? PreviewSvg,
    IReadOnlyList<BlockAttributeRecord> Attributes,
    IReadOnlyList<string> Layers,
    string? Sha256 = null,
    string? Description = null);

/// <summary>Tool palette item; Target is a block name or a command name/macro (TargetKind "block" | "command" | "none").
/// SourceRelPath may carry a "!part" suffix (CUIX member); the part before '!' is the manifest file.</summary>
public sealed record PaletteItemRecord(string Palette, string Name, string TargetKind, string? Target, string SourceRelPath, string? TargetFile = null);

public sealed record CommandRecord(string Name, string? Description, string? Macro, string SourceRelPath, string? LispFunction = null);

public sealed record CommandAliasRecord(string Alias, string Command, string SourceRelPath);

public sealed record LinetypeRecord(string Name, string? Description, string Definition, string SourceRelPath);

public sealed record MlineStyleRecord(string Name, string Raw, string SourceRelPath);

public sealed record FontMapRecord(string From, string To, string SourceRelPath);

/// <summary>Page is numeric when the chunk is a PDF page (else 0); PageOrSheet is the raw label (page number or sheet name).</summary>
public sealed record DocChunkRecord(string Id, string SourceRelPath, int Page, string Text, string? PageOrSheet = null, string? Kind = null, string? Sha256 = null);

/// <summary>Search hit. Kind: section | bolt | block | command | doc_chunk | ...</summary>
public sealed record SearchHit(string Kind, string Key, string Label, double Score, string MatchedBy);

public sealed record GraphNode(long Id, string Kind, string Key, string Label, string PropsJson);

public sealed record GraphEdge(long Src, string Rel, long Dst, string? EvidenceSourceFile, string? EvidenceNote);
