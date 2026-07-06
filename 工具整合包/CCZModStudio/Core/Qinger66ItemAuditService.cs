using System.Globalization;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class Qinger66ItemAuditService
{
    private readonly HexTableReader _reader = new();
    private readonly ItemIconMappingService _iconMapping = new();
    private readonly ItemEffectResolutionService _effectResolution = new();

    public (Qinger66ItemAuditSummary Summary, IReadOnlyList<Qinger66ItemAuditRow> Rows) Build(
        CczProject project,
        CczEngineProfile engine,
        IReadOnlyList<HexTableDefinition> tables)
    {
        if (!Qinger66DiagnosticsService.IsQinger66Candidate(project, engine))
        {
            return (new Qinger66ItemAuditSummary { Applies = false }, Array.Empty<Qinger66ItemAuditRow>());
        }

        var warnings = new List<string>();
        var rows = new List<Qinger66ItemAuditRow>();
        var boundary = ItemCategoryBoundaryService.Resolve(project);
        foreach (var table in HexTableNameResolver.ResolveItemTables(project, tables).Where(Ccz66ItemNameEncodingService.IsItemBaseTable))
        {
            var validation = _reader.Validate(project, table);
            if (!validation.IsUsable)
            {
                warnings.Add($"Item table unusable: {table.TableName}; status={validation.TableStatus}.");
                continue;
            }

            var path = project.ResolveGameFile(table.FileName);
            var bytes = File.ReadAllBytes(path);
            for (var rowIndex = 0; rowIndex < table.RowCount; rowIndex++)
            {
                var rowOffset = checked((int)(table.DataPos + rowIndex * (long)table.RowSize));
                if (rowOffset < 0 || rowOffset + table.RowSize > bytes.Length)
                {
                    warnings.Add($"Item row out of bounds: {table.TableName} row={rowIndex} offset={rowOffset}.");
                    continue;
                }

                var raw = new byte[table.RowSize];
                Buffer.BlockCopy(bytes, rowOffset, raw, 0, raw.Length);
                var nameBytes = raw.AsSpan(Ccz66ItemLayoutService.NameOffset, Math.Min(Ccz66ItemLayoutService.NameSize, raw.Length));
                var displayName = Ccz66ItemNameEncodingService.DecodeVisibleName(nameBytes);
                var hiddenTail = Ccz66ItemNameEncodingService.GetHiddenTailHex(nameBytes);
                var typeId = raw.Length > Ccz66ItemLayoutService.TypeOffset ? raw[Ccz66ItemLayoutService.TypeOffset] : 0;
                var rawEffectMarker = raw.Length > Ccz66ItemLayoutService.RawEffectMarkerOffset ? raw[Ccz66ItemLayoutService.RawEffectMarkerOffset] : 0;
                var effectId = Ccz66ItemLayoutService.IsAccessoryOrConsumableMarker(rawEffectMarker)
                    ? typeId
                    : rawEffectMarker;
                var iconField = raw.Length > Ccz66ItemLayoutService.IconOffset ? raw[Ccz66ItemLayoutService.IconOffset] : 0;
                var itemId = table.BeginId + rowIndex;
                var kind = ItemClassificationService.ClassifyKind(
                    itemId,
                    string.IsNullOrWhiteSpace(displayName),
                    rawEffectMarker,
                    boundary);
                var majorCategory = ItemClassificationService.BuildKindDisplayName(kind);
                var effect = _effectResolution.Resolve(project, tables, majorCategory, typeId, rawEffectMarker);
                var mapping = _iconMapping.Resolve(project, iconField, "item");
                var rowWarnings = new List<string>();
                if (Ccz66ItemNameEncodingService.ContainsVisibleControlChars(displayName))
                {
                    rowWarnings.Add("visible name contains control characters");
                }

                if (!string.IsNullOrWhiteSpace(mapping.Error))
                {
                    rowWarnings.Add(mapping.Error);
                }

                rows.Add(new Qinger66ItemAuditRow
                {
                    ItemId = itemId,
                    TableName = table.TableName,
                    RowIndex = rowIndex,
                    RawBytesHex = Ccz66ItemNameEncodingService.FormatRawBytes(raw),
                    DisplayName = displayName,
                    HiddenTailBytesHex = hiddenTail,
                    TypeId = typeId,
                    RawEffectId = rawEffectMarker,
                    EffectiveEffectId = effect.EffectiveEffectId,
                    IconField = iconField,
                    SmallImageNumber = mapping.SmallImageNumber ?? 0,
                    LargeImageNumber = mapping.LargeImageNumber,
                    TableStatus = validation.TableStatus,
                    WriteRisk = validation.WriteRisk,
                    EffectSource = effect.Source,
                    EffectConfidence = effect.Confidence,
                    Warnings = rowWarnings.Concat(effect.Warnings).Concat(mapping.Warnings).ToArray()
                });
            }
        }

        var itemE5Path = Ccz66RevisedLayout.ResolveResourcePath(project, "E5\\Item.e5");
        var entryCount = File.Exists(itemE5Path) ? new E5ImageReplaceService().ReadIndex(itemE5Path).Count : 0;
        var maxIcon = rows.Count == 0 ? 0 : rows.Max(row => row.IconField);
        var minIcon = rows.Count == 0 ? 0 : rows.Min(row => row.IconField);
        var maxRequired = rows.Count == 0 ? 0 : rows.Max(row => row.LargeImageNumber);
        if (entryCount > 0 && maxRequired > entryCount)
        {
            warnings.Add($"Item.e5 has {entryCount} entries but item icon fields require #{maxRequired}.");
        }

        if (rows.Any(row => row.Warnings.Any()))
        {
            warnings.Add("At least one item row has semantic warnings; inspect row diagnostics before writing.");
        }

        var summary = new Qinger66ItemAuditSummary
        {
            Applies = true,
            ItemRowCount = rows.Count,
            HiddenTailRowCount = rows.Count(row => !string.IsNullOrWhiteSpace(row.HiddenTailBytesHex)),
            NameControlCharacterRowCount = rows.Count(row => Ccz66ItemNameEncodingService.ContainsVisibleControlChars(row.DisplayName)),
            MinIconField = minIcon,
            MaxIconField = maxIcon,
            ItemE5EntryCount = entryCount,
            MaxRequiredImageNumber = maxRequired,
            ItemIconRangeCovered = entryCount >= maxRequired && maxRequired > 0,
            TableStatusCounts = rows.GroupBy(row => row.TableStatus).ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
            EffectResolutionSourceCounts = rows.GroupBy(row => row.EffectSource).ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
            EffectConfidenceCounts = rows.GroupBy(row => row.EffectConfidence).ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
            Warnings = warnings
        };
        return (summary, rows);
    }
}
