using System.Data;
using System.Globalization;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class ShopRuntimeDiagnosticService
{
    private const uint AutoShopVirtualAddress = 0x00482427;
    private const int AutoShopByteCount = 13;

    private static readonly HashSet<int> ScenarioCandidateCommandIds = new()
    {
        0x6F,
        0x11,
        0x77,
        0x78,
        0x79
    };

    private readonly HexTableReader _tableReader = new();
    private readonly CczEngineProfileService _engineProfile = new();
    private readonly ShopEditorService _shopEditorService = new();
    private readonly LegacyScenarioReader _legacyScenarioReader = new();
    private readonly SceneStringParser _sceneStringParser = new();

    public ShopRuntimeDiagnosticResult Diagnose(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        int limit = 120)
    {
        var hints = _engineProfile.Detect(project).TableHints;
        var shopTable = HexTableNameResolver.ResolveForProject(project, tables, hints.ShopDataTable);
        var shopRead = _tableReader.Read(project, shopTable, tables);
        var itemInfos = _shopEditorService.BuildShopItemInfoLookup(project, tables);
        var itemNames = itemInfos.ToDictionary(pair => pair.Key, pair => pair.Value.Name);
        var placeholderItems = itemInfos.Values
            .Where(item => item.Id is >= 0 and < ShopEditorService.EmptyShopItemId)
            .Where(item => ShopEditorService.IsPlaceholderShopItemName(item.Name))
            .OrderBy(item => item.Id)
            .Select(item => new ShopPlaceholderItemDiagnostic
            {
                ItemId = item.Id,
                Name = item.Name,
                Category = item.Category
            })
            .ToList();

        var explicitIssues = shopRead.Validation.IsUsable
            ? _shopEditorService.ValidateShopItemSlots(shopRead.Data, itemInfos)
            : Array.Empty<ShopSlotValidationIssue>();
        var autoShop = DiagnoseAutoShop(project, itemInfos, itemNames);
        var scenarios = DiagnoseScenarioPair(project, itemInfos, Math.Max(1, limit)).ToList();
        var warnings = BuildFileUseWarnings(project);
        var conclusion = explicitIssues.Count == 0 && autoShop.PlaceholderHitCount == 0
            ? "显式表未命中；需运行时断点确认商店列表来源。"
            : "发现商店占位风险。";

        return new ShopRuntimeDiagnosticResult
        {
            GameRoot = project.GameRoot,
            ShopTableName = shopTable.TableName,
            ExplicitShopIssueCount = explicitIssues.Count,
            ExplicitShopIssues = explicitIssues.Take(Math.Max(1, limit)).ToArray(),
            PlaceholderItems = placeholderItems.Take(Math.Max(1, limit)).ToArray(),
            AutoShop = autoShop,
            ScenarioFiles = scenarios,
            FileUseWarnings = warnings,
            Conclusion = conclusion
        };
    }

    private ShopAutoShopDiagnostic DiagnoseAutoShop(
        CczProject project,
        IReadOnlyDictionary<int, ShopItemInfo> itemInfos,
        IReadOnlyDictionary<int, string> itemNames)
    {
        var exePath = project.ResolveGameFile("Ekd5.exe");
        try
        {
            var mapper = PeAddressMapper.Load(exePath);
            var rawOffset = mapper.VirtualAddressToFileOffset(AutoShopVirtualAddress);
            var bytes = ReadBytesShared(exePath, rawOffset, AutoShopByteCount);
            var groups = new List<ShopAutoShopGroupDiagnostic>();
            for (var i = 0; i < bytes.Length; i++)
            {
                var value = bytes[i];
                var enabled = value != 0xFF;
                var items = new List<ShopAutoShopItemDiagnostic>();
                if (enabled)
                {
                    for (var itemId = value; itemId <= value + 2; itemId++)
                    {
                        var name = _shopEditorService.BuildItemName(itemInfos, itemNames, itemId);
                        items.Add(new ShopAutoShopItemDiagnostic
                        {
                            ItemId = itemId,
                            Name = name,
                            IsPlaceholder = itemId < ShopEditorService.EmptyShopItemId &&
                                            itemInfos.TryGetValue(itemId, out var item) &&
                                            ShopEditorService.IsPlaceholderShopItemName(item.Name)
                        });
                    }
                }

                groups.Add(new ShopAutoShopGroupDiagnostic
                {
                    GroupIndex = i,
                    BaseItemId = enabled ? value : ShopEditorService.EmptyShopItemId,
                    Enabled = enabled,
                    Items = items
                });
            }

            return new ShopAutoShopDiagnostic
            {
                ExePath = exePath,
                VirtualAddress = AutoShopVirtualAddress,
                RawOffset = rawOffset,
                BytesHex = string.Join(" ", bytes.Select(value => value.ToString("X2", CultureInfo.InvariantCulture))),
                Groups = groups
            };
        }
        catch (IOException ex)
        {
            return BuildAutoShopError(exePath, "目标文件正被占用；请关闭游戏后重试。", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return BuildAutoShopError(exePath, "目标文件正被占用；请关闭游戏后重试。", ex);
        }
        catch (Exception ex)
        {
            return BuildAutoShopError(exePath, ex.Message, ex);
        }
    }

    private static ShopAutoShopDiagnostic BuildAutoShopError(string exePath, string message, Exception ex)
    {
        System.Diagnostics.Debug.WriteLine("自动商店诊断失败：" + ex);
        return new ShopAutoShopDiagnostic
        {
            ExePath = exePath,
            VirtualAddress = AutoShopVirtualAddress,
            Error = message
        };
    }

    private IEnumerable<ShopScenarioFileDiagnostic> DiagnoseScenarioPair(
        CczProject project,
        IReadOnlyDictionary<int, ShopItemInfo> itemInfos,
        int limit)
    {
        var dictionary = LoadDictionary(project);
        foreach (var fileName in new[] { "R_00.eex", "S_00.eex", "R_01.eex", "S_01.eex" })
        {
            var relativePath = Path.Combine("RS", fileName);
            var fullPath = Path.Combine(project.GameRoot, relativePath);
            if (!File.Exists(fullPath))
            {
                yield return new ShopScenarioFileDiagnostic
                {
                    FileName = fileName,
                    RelativePath = relativePath,
                    Exists = false,
                    Error = "文件不存在"
                };
                continue;
            }

            ShopScenarioFileDiagnostic result;
            try
            {
                var document = _legacyScenarioReader.Read(fullPath, dictionary);
                var candidates = document.EnumerateCommands()
                    .Select(command => BuildScenarioCandidate(command, itemInfos))
                    .Where(candidate => candidate != null)
                    .Cast<ShopScenarioCommandCandidate>()
                    .Take(limit)
                    .ToList();
                result = new ShopScenarioFileDiagnostic
                {
                    FileName = fileName,
                    RelativePath = relativePath,
                    Exists = true,
                    CandidateCount = candidates.Count,
                    Candidates = candidates
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"商店场景诊断失败：{fileName}\r\n{ex}");
                result = new ShopScenarioFileDiagnostic
                {
                    FileName = fileName,
                    RelativePath = relativePath,
                    Exists = true,
                    Error = ex.GetType().Name + ": " + ex.Message
                };
            }

            yield return result;
        }
    }

    private ShopScenarioCommandCandidate? BuildScenarioCandidate(
        LegacyScenarioCommandNode command,
        IReadOnlyDictionary<int, ShopItemInfo> itemInfos)
    {
        var text = string.Join(" ", command.TextParameters.Select(parameter => parameter.Text));
        var nameClue = ContainsShopRuntimeClue(command.CommandName);
        var textClue = ContainsShopRuntimeClue(text);
        var commandClue = ScenarioCandidateCommandIds.Contains(command.CommandId);
        var placeholderIds = FindPlaceholderItemReferences(command, itemInfos).Distinct().OrderBy(id => id).ToArray();
        var has4088 = HasScalarReference(command, 4088);

        if (!commandClue && !nameClue && !textClue && placeholderIds.Length == 0 && !has4088)
        {
            return null;
        }

        var reasons = new List<string>();
        if (commandClue) reasons.Add(command.CommandIdHex);
        if (nameClue || textClue) reasons.Add("商店文本");
        if (placeholderIds.Length > 0) reasons.Add("占位物品");
        if (has4088) reasons.Add("4088");

        return new ShopScenarioCommandCandidate
        {
            CommandId = command.CommandId,
            CommandIdHex = command.CommandIdHex,
            CommandName = command.CommandName,
            SceneIndex = command.SceneIndex,
            SectionIndex = command.SectionIndex,
            CommandIndex = command.CommandIndex,
            FileOffset = command.FileOffset,
            OffsetHex = HexDisplayFormatter.FormatOffset(command.FileOffset),
            Reason = string.Join(",", reasons),
            PlaceholderItemIds = placeholderIds,
            Has4088Reference = has4088,
            TextPreview = TrimPreview(text, 80)
        };
    }

    private static bool ContainsShopRuntimeClue(string? text)
    {
        text ??= string.Empty;
        return text.Contains("商店", StringComparison.Ordinal) ||
               text.Contains("买卖", StringComparison.Ordinal) ||
               text.Contains("仓库", StringComparison.Ordinal);
    }

    private static IEnumerable<int> FindPlaceholderItemReferences(
        LegacyScenarioCommandNode command,
        IReadOnlyDictionary<int, ShopItemInfo> itemInfos)
    {
        foreach (var value in EnumerateScalarValues(command))
        {
            if (value is < 0 or >= ShopEditorService.EmptyShopItemId) continue;
            if (itemInfos.TryGetValue(value, out var item) && ShopEditorService.IsPlaceholderShopItemName(item.Name))
            {
                yield return value;
            }
        }
    }

    private static bool HasScalarReference(LegacyScenarioCommandNode command, int value)
        => EnumerateScalarValues(command).Any(candidate => candidate == value);

    private static IEnumerable<int> EnumerateScalarValues(LegacyScenarioCommandNode command)
    {
        foreach (var parameter in command.Parameters)
        {
            if (parameter.Kind == LegacyScenarioParameterKind.Text) continue;
            if (parameter.Kind == LegacyScenarioParameterKind.VariableArray)
            {
                foreach (var value in parameter.Values) yield return value;
            }
            else
            {
                yield return parameter.IntValue;
            }
        }
    }

    private SceneStringDocument LoadDictionary(CczProject project)
    {
        var path = ProjectDetector.FindSceneDictionaryPath(project);
        if (File.Exists(path)) return _sceneStringParser.Parse(path);
        return new SceneStringDocument { SourcePath = path };
    }

    private static byte[] ReadBytesShared(string path, long offset, int count)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (offset < 0 || offset + count > stream.Length)
        {
            throw new InvalidOperationException("自动商店表偏移超出文件范围。");
        }

        stream.Position = offset;
        var buffer = new byte[count];
        var read = stream.Read(buffer, 0, count);
        if (read != count)
        {
            throw new EndOfStreamException("自动商店表读取不完整。");
        }

        return buffer;
    }

    private static IReadOnlyList<string> BuildFileUseWarnings(CczProject project)
    {
        var warnings = new List<string>();
        foreach (var file in new[] { "Data.e5", "Ekd5.exe" })
        {
            var path = project.ResolveGameFile(file);
            if (!File.Exists(path)) continue;
            try
            {
                using var _ = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                warnings.Add("目标文件正被占用；请关闭游戏后重试。");
                break;
            }
            catch (UnauthorizedAccessException)
            {
                warnings.Add("目标文件正被占用；请关闭游戏后重试。");
                break;
            }
        }

        return warnings;
    }

    private static string TrimPreview(string text, int maxChars)
    {
        text = text.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        return text.Length <= maxChars ? text : text[..maxChars] + "...";
    }
}
