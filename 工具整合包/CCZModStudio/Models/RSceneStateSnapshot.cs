namespace CCZModStudio.Models;

public sealed class RSceneStateSnapshot
{
    public int SceneIndex { get; init; }
    public int SectionIndex { get; init; }
    public int? StartCommandIndex { get; init; }
    public int CurrentCommandIndex { get; init; }
    public int? BackgroundImageNumber { get; init; }
    public List<RSceneActorState> Actors { get; init; } = [];
    public List<RSceneMapFaceState> MapFaces { get; init; } = [];
}
