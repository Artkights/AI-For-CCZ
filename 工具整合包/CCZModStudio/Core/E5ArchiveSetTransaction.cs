using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// Coordinates mutations that must either all commit or all return to their
/// verified pre-save SHA. Each member still uses E5ImageReplaceService's
/// temporary-file verification and atomic replacement.
/// </summary>
public sealed class E5ArchiveSetTransaction
{
    private readonly E5ImageReplaceService _e5;

    internal Action<string, int>? BeforeCommitTestHook { get; set; }

    public E5ArchiveSetTransaction()
        : this(new E5ImageReplaceService())
    {
    }

    internal E5ArchiveSetTransaction(E5ImageReplaceService e5)
    {
        _e5 = e5;
    }

    public E5ArchiveSetTransactionPreview Prepare(
        CczProject project,
        IEnumerable<E5ArchiveMutationPlan> plans)
    {
        var normalized = plans
            .Where(plan => plan.Requests.Count > 0)
            .Select(plan => plan with { TargetPath = Path.GetFullPath(plan.TargetPath) })
            .ToArray();
        if (normalized.Length == 0)
            throw new InvalidOperationException("The archive-set transaction contains no E5 mutations.");
        var duplicate = normalized.GroupBy(plan => plan.TargetPath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
            throw new InvalidOperationException($"Archive-set transaction contains duplicate target path: {duplicate.Key}.");

        var prepared = new List<E5PreparedMutation>(normalized.Length);
        foreach (var plan in normalized)
        {
            var probe = _e5.ProbeIndex(plan.TargetPath);
            if (!probe.IsComplete) throw new E5ArchiveIntegrityException(probe);
            var preview = _e5.PreviewBatchReplacement(project, plan.TargetPath, plan.Requests);
            prepared.Add(new E5PreparedMutation { Plan = plan, Preview = preview });
        }
        return new E5ArchiveSetTransactionPreview { Files = prepared };
    }

    public E5ArchiveSetTransactionResult Commit(
        CczProject project,
        E5ArchiveSetTransactionPreview prepared)
    {
        if (prepared.Files.Count == 0)
            throw new InvalidOperationException("The prepared archive-set transaction is empty.");

        // Re-run all preflights before the first file changes. The bound archive,
        // index and target hashes in each request make this a stale-plan check.
        foreach (var file in prepared.Files)
        {
            var current = _e5.PreviewBatchReplacement(project, file.Plan.TargetPath, file.Plan.Requests);
            if (!current.OldFileSha256.Equals(file.Preview.OldFileSha256, StringComparison.OrdinalIgnoreCase) ||
                !current.NewFileSha256.Equals(file.Preview.NewFileSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Archive-set preview became stale before commit: {file.Plan.TargetPath}.");
        }

        var committed = new List<E5ImageBatchReplaceResult>(prepared.Files.Count);
        try
        {
            for (var index = 0; index < prepared.Files.Count; index++)
            {
                var file = prepared.Files[index];
                BeforeCommitTestHook?.Invoke(file.Plan.TargetPath, index);
                var result = _e5.ReplaceBatch(project, file.Plan.TargetPath, file.Plan.Requests);
                if (!result.NewFileSha256.Equals(file.Preview.NewFileSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Archive-set member produced a different SHA than its bound preview: {file.Plan.TargetPath}.");
                committed.Add(result);
            }
        }
        catch (Exception commitError)
        {
            var rollbackErrors = new List<Exception>();
            foreach (var result in committed.AsEnumerable().Reverse())
            {
                try
                {
                    _e5.RestoreVerifiedBackup(result.BackupPath, result.TargetPath, result.OldFileSha256);
                }
                catch (Exception rollbackError)
                {
                    rollbackErrors.Add(rollbackError);
                }
            }

            if (rollbackErrors.Count > 0)
                throw new AggregateException(
                    "The E5 archive-set commit failed and at least one verified rollback also failed.",
                    new[] { commitError }.Concat(rollbackErrors));
            throw new InvalidOperationException(
                "The E5 archive-set commit failed. Every previously committed archive was restored and verified against its pre-save SHA.",
                commitError);
        }

        return new E5ArchiveSetTransactionResult { Prepared = prepared, Files = committed };
    }

    public E5ArchiveSetTransactionResult Execute(
        CczProject project,
        IEnumerable<E5ArchiveMutationPlan> plans)
        => Commit(project, Prepare(project, plans));
}
