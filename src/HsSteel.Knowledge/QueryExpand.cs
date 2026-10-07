using System.Text;
using System.Text.RegularExpressions;

namespace HsSteel.Knowledge;

/// <summary>
/// Korean/English query expansion for hybrid search: compound Hangul splitting, detailing synonyms,
/// and FTS body enrichment so lexical hits work when manuals use GUSSET/3PLT instead of 엔드플레이트/스캘럽.
/// </summary>
public static class QueryExpand
{
    /// <summary>Trigger phrases (any match → expand) paired with FTS/semantic expansion terms.</summary>
    private static readonly (string[] Triggers, string[] Terms)[] Rules =
    [
        (
            ["엔드플레이트", "엔드 플레이트", "엔드플랜지", "endplate", "end plate", "end-plate"],
            ["엔드플레이트", "엔드", "플레이트", "end", "plate", "형판", "3PLT", "PLATE", "GUSSET", "끝판"]
        ),
        (
            ["스캘럽", "스캘로프", "스캘로프컷", "scallop", "cope", "웹컷", "플랜지컷"],
            ["스캘럽", "scallop", "cope", "SCALLOP", "접합", "GUSSET", "플랜지", "형판"]
        ),
        (
            ["전단접합", "전단 접합", "전단탭", "shear connection", "shear tab", "1면마찰"],
            ["전단접합", "전단", "접합", "GUSSET", "shear", "1면마찰", "형판", "3PLT", "BEAM", "GIRDER"]
        ),
        (
            ["거셋", "거셋플레이트", "gusset"],
            ["거셋", "GUSSET", "형판", "3PLT", "접합", "BRACE"]
        ),
        (
            ["고장력볼트", "고력볼트", "HTB"],
            ["고장력볼트", "고력볼트", "HTB", "TS", "볼트", "BOLT", "JOINT-BASE"]
        ),
    ];

    /// <summary>Corpus markers → synonym bag appended to FTS body (does not change chunk text or embeddings).</summary>
    private static readonly (string[] Markers, string Bag)[] EnrichRules =
    [
        (["GUSSET", "거셋", "1면마찰", "FRG", "형판"], " 엔드플레이트 end plate 전단접합 스캘럽 shear tab scallop cope GUSSET 형판"),
        (["3PLT", "PLATE블럭", "PLATE 생성", "형판생성", "형판블럭"], " 엔드플레이트 end plate 3PLT 형판 PLATE 전단접합"),
        (["SCALLOP", "scallop", "cope"], " 스캘럽 scallop cope 전단접합"),
        (["JOINT-BASE", "고력볼트", "고장력"], " 고장력볼트 고력볼트 HTB BOLT"),
    ];

    private static readonly Regex HangulRun = new(@"\p{IsHangulSyllables}{2,}", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Lexicon used to split long Hangul compounds (longest-first).</summary>
    private static readonly string[] HangulLexicon =
    [
        "엔드플레이트", "전단접합", "데크플레이트", "고장력볼트", "고력볼트",
        "스캘로프", "스캘럽", "플레이트", "거셋", "형판", "접합", "전단", "엔드", "볼트",
    ];

    /// <summary>Expanded whitespace-separated query for FTS (original tokens + synonyms + Hangul splits).</summary>
    public static string ForFts(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var terms = new List<string>();
        void Add(string t)
        {
            t = t.Trim();
            if (t.Length > 0 && !terms.Contains(t, StringComparer.OrdinalIgnoreCase))
            {
                terms.Add(t);
            }
        }

        foreach (var raw in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            Add(raw);
            foreach (var part in SplitHangulCompound(raw))
            {
                Add(part);
            }
        }

        var joined = string.Join(' ', terms);
        foreach (var (triggers, expand) in Rules)
        {
            if (triggers.Any(t => joined.Contains(t, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var e in expand)
                {
                    Add(e);
                }
            }
        }

        return string.Join(' ', terms);
    }

    /// <summary>Optional rewrite for the vector embedder when the raw Korean query is synonym-mapped (corpus uses English/legacy labels).</summary>
    public static string ForSemantic(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return query;
        }

        var bags = new List<string> { query };
        foreach (var (triggers, expand) in Rules)
        {
            if (triggers.Any(t => query.Contains(t, StringComparison.OrdinalIgnoreCase)))
            {
                bags.Add(string.Join(' ', expand.Take(8)));
            }
        }

        return bags.Count == 1 ? query : string.Join(' ', bags);
    }

    /// <summary>Synonym bag to append to an FTS body when the source text matches known detailing markers.</summary>
    public static string EnrichmentFor(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var (markers, bag) in EnrichRules)
        {
            if (markers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase)))
            {
                sb.Append(bag);
            }
        }

        return sb.ToString();
    }

    /// <summary>Split a Hangul compound into lexicon parts when possible (e.g. 엔드플레이트 → 엔드, 플레이트).</summary>
    public static IReadOnlyList<string> SplitHangulCompound(string token)
    {
        if (string.IsNullOrEmpty(token) || !HangulRun.IsMatch(token) || token.Length < 4)
        {
            return [];
        }

        var parts = new List<string>();
        var i = 0;
        while (i < token.Length)
        {
            string? hit = null;
            foreach (var lex in HangulLexicon)
            {
                // Skip trivial / whole-token matches; allow a lexicon hit that consumes the remainder after a prior part.
                if (lex.Length <= 1 || (i == 0 && lex.Length == token.Length) || lex.Length > token.Length - i)
                {
                    continue;
                }

                if (string.Compare(token, i, lex, 0, lex.Length, StringComparison.Ordinal) == 0)
                {
                    if (hit is null || lex.Length > hit.Length)
                    {
                        hit = lex;
                    }
                }
            }

            if (hit is not null)
            {
                parts.Add(hit);
                i += hit.Length;
            }
            else
            {
                i++;
            }
        }

        return parts.Count >= 2 ? parts : [];
    }
}