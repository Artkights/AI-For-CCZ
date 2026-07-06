namespace CCZModStudio.Models;

public sealed class CheatMakerCmfProbeResult
{
    public string Path { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public bool Exists { get; init; }
    public long Length { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public bool HasUtf16Bom { get; init; }
    public string Signature { get; init; } = string.Empty;
    public string FormatSignature { get; init; } = string.Empty;
    public string FormatVersion { get; init; } = string.Empty;
    public bool IsCheatMakerCmf { get; init; }
    public int Utf16CrlfCount { get; init; }
    public IReadOnlyList<long> FirstUtf16CrlfOffsets { get; init; } = Array.Empty<long>();
    public bool LooksProtectedOrObfuscated { get; init; }
    public IReadOnlyList<string> VisibleKeywordHits { get; init; } = Array.Empty<string>();
    public IReadOnlyList<CmfSegmentAnalysis> Segments { get; init; } = Array.Empty<CmfSegmentAnalysis>();
    public string EvidenceCategory { get; init; } = string.Empty;
    public bool EvidenceOnly { get; init; } = false;
    public bool AuthoritativeToolSource { get; init; } = true;
    public string SafetyNote { get; init; } = "CMF is a high-trust old CheatMaker modifier project. Do not convert it to writable rules until field metadata, address semantics, version match, and reread validation are confirmed.";
    public string EvidenceRole { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public CheatMakerCmfComparison? Comparison { get; init; }
}

public sealed class CheatMakerCmfCatalogEntry
{
    public string RelativePath { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public long Length { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public string FormatSignature { get; init; } = string.Empty;
    public string FormatVersion { get; init; } = string.Empty;
    public int Utf16CrlfCount { get; init; }
    public bool IsCheatMakerCmf { get; init; }
    public bool LooksProtectedOrObfuscated { get; init; }
    public IReadOnlyList<string> VisibleKeywordHits { get; init; } = Array.Empty<string>();
    public IReadOnlyList<CmfSegmentAnalysis> Segments { get; init; } = Array.Empty<CmfSegmentAnalysis>();
    public string EvidenceCategory { get; init; } = string.Empty;
    public bool EvidenceOnly { get; init; } = false;
    public bool AuthoritativeToolSource { get; init; } = true;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class CheatMakerCmfCorpusReport
{
    public string RootPath { get; init; } = string.Empty;
    public int TotalFiles { get; init; }
    public int CheatMakerCmfCount { get; init; }
    public bool EvidenceOnly { get; init; } = false;
    public bool AuthoritativeToolSource { get; init; } = true;
    public string SafetyNote { get; init; } = "CMF corpus is a high-trust old modifier knowledge source. Writable rules require extracted field metadata, address classification, version match, and reread validation.";
    public IReadOnlyDictionary<string, int> SignatureCounts { get; init; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, int> CategoryCounts { get; init; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<CheatMakerCmfCatalogEntry> Entries { get; init; } = Array.Empty<CheatMakerCmfCatalogEntry>();
}

public sealed class CheatMakerCmfComparison
{
    public string BaselinePath { get; init; } = string.Empty;
    public bool BaselineExists { get; init; }
    public long BaselineLength { get; init; }
    public string BaselineSha256 { get; init; } = string.Empty;
    public string BaselineSignature { get; init; } = string.Empty;
    public int BaselineUtf16CrlfCount { get; init; }
    public int ExtraUtf16CrlfSegments { get; init; }
    public long LengthDelta { get; init; }
    public long SamePrefixBytes { get; init; }
    public long EqualBytesInComparableLength { get; init; }
    public double EqualRatioInComparableLength { get; init; }
    public bool EvidenceOnly { get; init; } = false;
    public bool AuthoritativeToolSource { get; init; } = true;
    public string Summary { get; init; } = string.Empty;
}
