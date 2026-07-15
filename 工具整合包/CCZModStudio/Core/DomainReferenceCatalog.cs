using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

[Flags]
public enum DomainReferenceSlice
{
    None = 0,
    Items = 1,
    Jobs = 2,
    People = 4,
    Effects = 8,
    Campaigns = 16,
    All = Items | Jobs | People | Effects | Campaigns
}

public sealed record DomainReferenceSnapshot(
    IReadOnlyDictionary<int, string> ItemNames,
    IReadOnlyDictionary<int, ItemClassification> ItemClassifications,
    ItemCategoryBoundary ItemBoundary,
    IReadOnlyDictionary<int, string> JobNames,
    IReadOnlyDictionary<int, string> PersonNames,
    IReadOnlyDictionary<int, string> CampaignNames,
    IReadOnlyDictionary<int, string> EffectNames,
    string ResourceIdentity);

/// <summary>Immutable, project-scoped lookups shared by role/job/item/shop pages.</summary>
public sealed class DomainReferenceCatalog
{
    public static DomainReferenceCatalog Shared { get; } = new();

    private readonly ConcurrentDictionary<string, Lazy<Task<DomainReferenceSnapshot>>> _snapshots =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _snapshotAccessOrder = new(StringComparer.OrdinalIgnoreCase);
    private long _snapshotAccessSequence;

    public async Task<DomainReferenceSnapshot> GetSnapshotAsync(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        DomainReferenceSlice requiredSlices = DomainReferenceSlice.All,
        CancellationToken cancellationToken = default)
    {
        var identity = BuildIdentity(project, tables, requiredSlices);
        var candidate = new Lazy<Task<DomainReferenceSnapshot>>(
            () => Task.Run(() => Build(project, tables, requiredSlices), CancellationToken.None),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var lazy = _snapshots.GetOrAdd(identity, candidate);
        _snapshotAccessOrder[identity] = Interlocked.Increment(ref _snapshotAccessSequence);
        var miss = ReferenceEquals(lazy, candidate);
        try
        {
            var result = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            PerformanceMetrics.Increment(miss ? "DomainReference.CacheMisses" : "DomainReference.CacheHits");
            Trim(ProjectResourceFingerprint.Normalize(project.GameRoot) + "|");
            return result;
        }
        catch
        {
            if (lazy.IsValueCreated && lazy.Value.IsFaulted)
            {
                _snapshots.TryRemove(new KeyValuePair<string, Lazy<Task<DomainReferenceSnapshot>>>(identity, lazy));
                _snapshotAccessOrder.TryRemove(identity, out _);
            }
            throw;
        }
    }

    public DomainReferenceSnapshot GetSnapshot(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        DomainReferenceSlice requiredSlices = DomainReferenceSlice.All)
        => GetSnapshotAsync(project, tables, requiredSlices).GetAwaiter().GetResult();

    public void Invalidate(CczProject project, DomainReferenceSlice slices = DomainReferenceSlice.All)
    {
        var root = ProjectResourceFingerprint.Normalize(project.GameRoot) + "|";
        foreach (var key in _snapshots.Keys.Where(key => key.StartsWith(root, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            _snapshots.TryRemove(key, out _);
            _snapshotAccessOrder.TryRemove(key, out _);
        }
        PerformanceMetrics.Increment("DomainReference.Invalidations");
    }

    public void Clear()
    {
        _snapshots.Clear();
        _snapshotAccessOrder.Clear();
    }

    private void Trim(string prefix)
    {
        foreach (var key in _snapshotAccessOrder.Where(pair => pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(pair => pair.Value).Skip(2).Select(pair => pair.Key).ToArray())
        {
            _snapshots.TryRemove(key, out _);
            _snapshotAccessOrder.TryRemove(key, out _);
        }
    }

    private static DomainReferenceSnapshot Build(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        DomainReferenceSlice slices)
    {
        using var operation = PerformanceMetrics.Begin("DomainReference.Build");
        var reader = new HexTableReader();
        var profile = new CczEngineProfileService().Detect(project);
        var itemNames = new Dictionary<int, string>();
        var classifications = new Dictionary<int, ItemClassification>();
        var jobs = new Dictionary<int, string>();
        var people = new Dictionary<int, string>();
        var campaigns = new Dictionary<int, string>();
        var effects = new Dictionary<int, string>();
        var boundary = ItemCategoryBoundaryService.Resolve(project);

        if (slices.HasFlag(DomainReferenceSlice.Items))
        {
            foreach (var name in new[] { profile.TableHints.ItemLowTable, profile.TableHints.ItemHighTable })
            {
                if (!HexTableNameResolver.TryResolveForProject(project, tables, name, out var table)) continue;
                var read = reader.Read(project, table, tables);
                if (!read.Validation.IsUsable) continue;
                foreach (DataRow row in read.Data.Rows)
                {
                    var id = ReadInt(row, "ID");
                    itemNames[id] = ReadText(row, "名称");
                    classifications[id] = ItemClassificationService.Classify(row, boundary);
                }
            }
        }

        if (slices.HasFlag(DomainReferenceSlice.Jobs))
        {
            AddNames(reader, project, tables, profile.TableHints.JobTable, jobs);
            AddNames(reader, project, tables, profile.TableHints.DetailedJobTable, jobs, overwrite: true);
        }
        if (slices.HasFlag(DomainReferenceSlice.People)) AddNames(reader, project, tables, profile.TableHints.PersonTable, people);
        if (slices.HasFlag(DomainReferenceSlice.Campaigns)) AddNames(reader, project, tables, profile.TableHints.CampaignNameTable, campaigns);
        if (slices.HasFlag(DomainReferenceSlice.Effects))
        {
            AddNames(reader, project, tables, profile.TableHints.ItemEffectNameLowTable, effects);
            AddNames(reader, project, tables, profile.TableHints.ItemEffectNameHighTable, effects);
            AddNames(reader, project, tables, profile.TableHints.JobEffectNameTable, effects, overwrite: false);
        }

        return new DomainReferenceSnapshot(
            ReadOnly(itemNames), ReadOnly(classifications), boundary, ReadOnly(jobs), ReadOnly(people),
            ReadOnly(campaigns), ReadOnly(effects), BuildIdentity(project, tables, slices));
    }

    private static void AddNames(
        HexTableReader reader,
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        string tableName,
        Dictionary<int, string> output,
        bool overwrite = false)
    {
        if (!HexTableNameResolver.TryResolveForProject(project, tables, tableName, out var table)) return;
        var read = reader.Read(project, table, tables);
        if (!read.Validation.IsUsable) return;
        foreach (DataRow row in read.Data.Rows)
        {
            var id = ReadInt(row, "ID");
            var value = ReadText(row, "名称");
            if (overwrite || !output.ContainsKey(id)) output[id] = value;
        }
    }

    private static string BuildIdentity(CczProject project, IReadOnlyList<HexTableDefinition> tables, DomainReferenceSlice slices)
    {
        var files = tables.Select(table => project.ResolveGameFile(table.FileName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => ProjectResourceFingerprint.Create(path, "domain-reference-v1"))
            .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(item => $"{item.Path}:{item.Length}:{item.LastWriteTimeUtcTicks}:{item.ChangeGeneration}");
        return ProjectResourceFingerprint.Normalize(project.GameRoot) + "|" + slices + "|" + string.Join(";", files);
    }

    private static int ReadInt(DataRow row, string column)
        => row.Table.Columns.Contains(column) && row[column] != DBNull.Value
            ? Convert.ToInt32(row[column], CultureInfo.InvariantCulture)
            : 0;

    private static string ReadText(DataRow row, string column)
        => row.Table.Columns.Contains(column)
            ? Convert.ToString(row[column], CultureInfo.InvariantCulture) ?? string.Empty
            : string.Empty;

    private static IReadOnlyDictionary<TKey, TValue> ReadOnly<TKey, TValue>(Dictionary<TKey, TValue> source) where TKey : notnull
        => new ReadOnlyDictionary<TKey, TValue>(source);
}
