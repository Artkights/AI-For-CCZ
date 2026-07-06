using System.Text.RegularExpressions;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed partial class Ccz66HexTableAugmentationService
{
    public IReadOnlyList<HexTableDefinition> LoadForProject(CczProject project, HexTableParser parser)
        => AugmentForProject(project, parser.Load(project.HexTableXmlPath));

    public IReadOnlyList<HexTableDefinition> AugmentForProject(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var profile = new CczEngineProfileService().Detect(project);
        if (!Ccz66RevisedLayout.Is66(profile))
        {
            return tables;
        }

        var result = tables.ToList();
        var nextId = result.Count == 0 ? 660_001 : Math.Max(660_001, result.Max(table => table.Id) + 1);
        var existing66Keys = result
            .Where(table => table.Version.Equals(Ccz66RevisedLayout.Version, StringComparison.OrdinalIgnoreCase))
            .Select(table => HexTableNameResolver.BuildSemanticKey(table.TableName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var source in tables
                     .Where(table => table.Version.Equals("6.5", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(table => table.Id))
        {
            if (!TryGetTableCode(source, out _)) continue;
            var targetName = HexTableNameResolver.BuildVersionedTableName(Ccz66RevisedLayout.Version, source.TableName);
            if (!existing66Keys.Add(HexTableNameResolver.BuildSemanticKey(targetName))) continue;

            var (canUse, reason) = VerifyNativeReuseCandidate(project, source);
            if (ItemEffectNameReader.IsItemEffectNameTable(source))
            {
                canUse = false;
                reason = "6.6 equipment effect name tables are null-separated EXE evidence; generic HexTable writes are blocked until a dedicated name-pack writer is implemented.";
            }

            result.Add(Ccz66ItemLayoutService.IsSourceItemBaseTable(source)
                ? Ccz66ItemLayoutService.CloneSourceItemTableFor66(source, targetName, nextId++, canUse, reason)
                : CloneFor66(source, targetName, nextId++, canUse, reason));
        }

        return result.OrderBy(table => table.Id).ToList();
    }

    private static HexTableDefinition CloneFor66(HexTableDefinition source, string tableName, int id, bool writable, string reason)
        => new()
        {
            Id = id,
            Enabled = source.Enabled,
            TableName = tableName,
            FileName = source.FileName,
            DataPos = source.DataPos,
            RowCount = source.RowCount,
            RowSize = source.RowSize,
            Columns = source.Columns,
            ByteSizes = source.ByteSizes,
            IndexTable = RewriteIndexTable(source.IndexTable),
            BeginId = source.BeginId,
            OnMem = source.OnMem,
            ReadOnly = !writable || source.ReadOnly,
            Version = Ccz66RevisedLayout.Version,
            Fields = source.Fields,
            EvidenceStatus = writable
                ? "Native66GeneratedFromVerified65Layout"
                : "ReadOnly66EvidenceOnly",
            SourceTableName = source.TableName,
            IsGeneratedCompatibilityTable = true,
            IsEvidenceReadOnlyTable = !writable,
        };

    private static (bool CanUse, string Reason) VerifyNativeReuseCandidate(CczProject project, HexTableDefinition table)
    {
        if (table.RowCount <= 0 || table.RowSize <= 0)
        {
            return (false, "row count or row size is not positive");
        }

        if (table.Columns.Count != table.ByteSizes.Count)
        {
            return (false, "column count does not match byte layout");
        }

        if (table.PositiveBytesSum > table.RowSize)
        {
            return (false, "field byte sum exceeds row size");
        }

        var filePath = project.ResolveGameFile(table.FileName);
        if (!File.Exists(filePath))
        {
            return (false, "target file was not found");
        }

        var length = new FileInfo(filePath).Length;
        if (length < table.EndOffsetExclusive)
        {
            return (false, $"target file length {length} is smaller than table end offset {table.EndOffsetExclusive}");
        }

        return (true, "target file, offset, row size, and capacity verified against the local 6.6 sample");
    }

    private static string RewriteIndexTable(string indexTable)
    {
        if (string.IsNullOrWhiteSpace(indexTable)) return indexTable;
        if (indexTable.Equals("6.5", StringComparison.OrdinalIgnoreCase)) return Ccz66RevisedLayout.Version;
        return HexTableNameResolver.BuildVersionedTableName(Ccz66RevisedLayout.Version, indexTable);
    }

    private static bool TryGetTableCode(HexTableDefinition table, out string code)
    {
        var match = TableCodeRegex().Match(table.TableName.Trim());
        code = match.Success ? match.Groups["code"].Value : string.Empty;
        return match.Success;
    }

    [GeneratedRegex(@"^6\.5-(?<code>\d+(?:-\d+)*)\b", RegexOptions.CultureInvariant)]
    private static partial Regex TableCodeRegex();
}
