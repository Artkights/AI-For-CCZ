namespace CCZModStudio.Models;

public sealed class RsPixelSampleLearningRequest
{
    public string UnitType { get; init; } = "spear_cavalry";
    public string OutputId { get; init; } = "spear_cavalry_mvp";
    public bool OverwriteExisting { get; init; }
    public int TopReviewCount { get; init; } = 12;
    public IReadOnlyList<string> ReferenceRoots { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> NegativeRoots { get; init; } = Array.Empty<string>();
}

public sealed class RsPixelSampleLearningResult
{
    public string UnitType { get; init; } = string.Empty;
    public string OutputRoot { get; init; } = string.Empty;
    public int CandidateCount { get; init; }
    public int CompleteCandidateCount { get; init; }
    public int RGroupCount { get; init; }
    public int SGroupCount { get; init; }
    public int StrongMachineCandidateCount { get; init; }
    public int PartialCandidateCount { get; init; }
    public int NegativeCandidateCount { get; init; }
    public IReadOnlyList<string> Reports { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ContactSheets { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public string SafetyNote { get; init; } = string.Empty;
}
