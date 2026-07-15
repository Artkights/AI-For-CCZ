namespace CCZModStudio.Models;

public sealed record E5ArchiveMutationPlan(
    string TargetPath,
    IReadOnlyList<E5ImageBatchReplaceRequest> Requests,
    string LogicalLabel = "");

public sealed class E5PreparedMutation
{
    public E5ArchiveMutationPlan Plan { get; init; } = new(string.Empty, Array.Empty<E5ImageBatchReplaceRequest>());
    public E5ImageBatchReplacePreviewResult Preview { get; init; } = new();
}

public sealed class E5ArchiveSetTransactionPreview
{
    public IReadOnlyList<E5PreparedMutation> Files { get; init; } = Array.Empty<E5PreparedMutation>();
    public int FileCount => Files.Count;
    public int OperationCount => Files.Sum(file => file.Preview.OperationCount);
}

public sealed class E5ArchiveSetTransactionResult
{
    public E5ArchiveSetTransactionPreview Prepared { get; init; } = new();
    public IReadOnlyList<E5ImageBatchReplaceResult> Files { get; init; } = Array.Empty<E5ImageBatchReplaceResult>();
}
