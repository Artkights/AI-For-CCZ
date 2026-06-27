using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.Globalization;

internal partial class Program
{
    static void RunDongwuLegacyLayoutSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var maps = new MapResourceIndexer().Index(project);
        var legacyMaps = maps.Where(item => item.SourceKind.Equals("LegacyHmRaw", StringComparison.OrdinalIgnoreCase)).ToList();
        if (legacyMaps.Count != 69 ||
            legacyMaps.First().MapId != "M000" ||
            legacyMaps.Last().MapId != "M068" ||
            legacyMaps.Any(item => item.GridWidth <= 0 || item.GridHeight <= 0 || item.Width <= 0 || item.Height <= 0))
        {
            throw new InvalidOperationException($"Dongwu legacy Hm map index failed: count={legacyMaps.Count}, first={legacyMaps.FirstOrDefault()?.MapId}, last={legacyMaps.LastOrDefault()?.MapId}.");
        }

        using (var preview = new LegacyHmMapReader().RenderPreview(project, legacyMaps[0]))
        {
            if (preview.Width <= 0 || preview.Height <= 0 || CountColorPixels(preview) <= 0)
            {
                throw new InvalidOperationException("Dongwu legacy Hm preview rendered blank.");
            }
        }

        var reader = new HexTableReader();
        var itemLow = reader.Read(project, FindDongwuSmokeTable(tables, "6.5-1 物品（0-103）"), tables);
        var itemHigh = reader.Read(project, FindDongwuSmokeTable(tables, "6.5-2 物品（104-255）"), tables);
        var descLow = reader.Read(project, FindDongwuSmokeTable(tables, "6.5-1-1 物品说明（0-103）"), tables);
        var descHigh = reader.Read(project, FindDongwuSmokeTable(tables, "6.5-2-1 物品说明（104-255）"), tables);
        if (!itemLow.Validation.IsUsable || !itemHigh.Validation.IsUsable ||
            itemLow.Data.Rows.Count != 104 || itemHigh.Data.Rows.Count != 151)
        {
            throw new InvalidOperationException($"Dongwu item base tables failed: low={itemLow.Data.Rows.Count}/{itemLow.Validation.IsUsable}, high={itemHigh.Data.Rows.Count}/{itemHigh.Validation.IsUsable}.");
        }

        if (descLow.Validation.IsUsable || descHigh.Validation.IsUsable)
        {
            throw new InvalidOperationException("Dongwu item description tables should be unavailable for this layout.");
        }

        var catalog = new ImageResourceCatalogService().BuildCatalog(project);
        var mmap = catalog.FirstOrDefault(item => item.FileName.Equals("Mmap.e5", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Dongwu image catalog did not include Mmap.e5.");
        if (!mmap.Exists)
        {
            throw new InvalidOperationException("Dongwu image catalog did not find existing Mmap.e5.");
        }

        if (mmap.EntryCount <= 0)
        {
            if (mmap.SupportsE5Index || !mmap.Status.Contains("旧式 LS 大图封包", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Dongwu Mmap status did not expose old LS package when no index was readable: entries={mmap.EntryCount}, supports={mmap.SupportsE5Index}, status={mmap.Status}");
            }
        }
        else if (!mmap.SupportsE5Index)
        {
            throw new InvalidOperationException($"Dongwu Mmap had entries but did not mark E5 index support: entries={mmap.EntryCount}, status={mmap.Status}");
        }

        var document = BuildSyntheticRSceneDocumentForBackgroundReferences();
        var states = new RSceneDraftService().BuildSceneStateCandidates(document);
        var references = states.Select(state => state.BackgroundReference).Where(reference => reference != null).ToList();
        if (references.Count != 4 ||
            references[0]!.CategoryName != "外场景" || references[0]!.ResolvedImageNumber != 6 || references[0]!.TargetResourceKind != "Mmap" ||
            references[1]!.CategoryName != "中国地图" || references[1]!.ResolvedImageNumber != 7 || references[1]!.TargetResourceKind != "WorldMap" ||
            references[2]!.CategoryName != "内场景" || references[2]!.ResolvedImageNumber != 50 || references[2]!.TargetResourceKind != "Mmap" ||
            references[3]!.CategoryName != "战场地图" || references[3]!.ResolvedImageNumber != 11 || references[3]!.TargetResourceKind != "BattlefieldMap")
        {
            throw new InvalidOperationException("Dongwu R scene background category mapping failed: " + string.Join(" | ", references.Select(reference => reference!.DisplayText)));
        }

        Console.WriteLine(
            "DONGWU_LEGACY_LAYOUT_SMOKE_OK " +
            $"maps={legacyMaps.Count} first={legacyMaps[0].MapId}/{legacyMaps[0].GridWidth}x{legacyMaps[0].GridHeight} " +
            $"items={itemLow.Data.Rows.Count + itemHigh.Data.Rows.Count} descUsable={descLow.Validation.IsUsable}/{descHigh.Validation.IsUsable} " +
            $"mmapEntries={mmap.EntryCount} mmapStatus=\"{mmap.Status}\" r27=\"{string.Join(" | ", references.Select(reference => reference!.DisplayText))}\"");
    }

    private static HexTableDefinition FindDongwuSmokeTable(IReadOnlyList<HexTableDefinition> tables, string tableName)
        => tables.FirstOrDefault(table => table.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase))
           ?? throw new InvalidOperationException("Table not found: " + tableName);

    private static LegacyScenarioDocument BuildSyntheticRSceneDocumentForBackgroundReferences()
    {
        var document = new LegacyScenarioDocument { FilePath = "Synthetic_R_00.eex" };
        var scene = new LegacyScenarioScene { SceneIndex = 0 };
        document.Scenes.Add(scene);

        var rawValues = new[] { 5, 7, 9, 11 };
        for (var category = 0; category < rawValues.Length; category++)
        {
            var section = new LegacyScenarioSection { SceneIndex = 0, SectionIndex = category };
            section.Commands.Add(BuildBackgroundCommand(category, rawValues[category]));
            scene.Sections.Add(section);
        }

        return document;
    }

    private static LegacyScenarioCommandNode BuildBackgroundCommand(int category, int rawValue)
    {
        var command = new LegacyScenarioCommandNode
        {
            SceneIndex = 0,
            SectionIndex = category,
            CommandIndex = 0,
            CommandOrdinal = category,
            CommandId = 0x27,
            CommandName = "背景显示"
        };
        command.Parameters.Add(new LegacyScenarioCommandParameter
        {
            Index = 0,
            Kind = LegacyScenarioParameterKind.Word16,
            IntValue = category
        });
        for (var index = 0; index <= category; index++)
        {
            command.Parameters.Add(new LegacyScenarioCommandParameter
            {
                Index = index + 1,
                Kind = LegacyScenarioParameterKind.Word16,
                IntValue = index == category ? rawValue : 0
            });
        }

        return command;
    }
}
