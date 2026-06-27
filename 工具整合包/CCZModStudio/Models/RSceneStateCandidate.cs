namespace CCZModStudio.Models;

public sealed class RSceneStateCandidate
{
    public int Index { get; init; }
    public string SceneTitle { get; init; } = string.Empty;
    public string TargetKey { get; init; } = string.Empty;
    public int SceneIndex { get; init; }
    public int SectionIndex { get; init; }
    public int StartCommandIndex { get; init; }
    public int CurrentCommandIndex { get; init; }
    public int EndCommandIndex { get; init; }
    public string OffsetHex { get; init; } = string.Empty;
    public int? BackgroundImageNumber { get; init; }
    public string BackgroundReferenceText => BackgroundReference?.DisplayText ?? (BackgroundImageNumber.HasValue ? $"背景 {BackgroundImageNumber.Value}" : "背景未识别");
    public RSceneBackgroundReference? BackgroundReference { get; init; }
    public int ActorCount { get; init; }
    public int MapFaceCount { get; init; }
    public string Summary { get; init; } = string.Empty;
}
