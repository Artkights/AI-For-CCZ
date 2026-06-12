using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class ExclusiveSetScenarioService
{
    private const int EmptyItemId = 255;
    private const int CommandIdInformationTransfer = 0x72;
    private const int ExclusiveSetSubFunction = 10;

    private readonly ScenarioFileReader _scenarioFileReader = new();
    private readonly LegacyScenarioReader _scenarioReader = new();
    private readonly LegacyScenarioWriter _scenarioWriter = new();

    public ExclusiveSetScenarioReadResult Read(
        CczProject project,
        SceneStringDocument dictionary,
        IReadOnlyDictionary<int, string> effectNames,
        IReadOnlyDictionary<int, string> personNames,
        IReadOnlyDictionary<int, string> itemNames)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(dictionary);

        var entries = new List<ExclusiveSetScenarioEntry>();
        var malformed = new List<ExclusiveSetScenarioMalformedEntry>();
        var warnings = new List<string>();
        var totalCommandCount = 0;
        var boundary = ResolveItemCategoryBoundary(project);
        var files = _scenarioFileReader.ReadAllIndex(project)
            .Where(file => ScenarioFileReader.IsRsScriptFile(file.FileName))
            .OrderBy(file => ScenarioFileReader.IsBattlefieldScriptFile(file.FileName) ? 1 : 0)
            .ThenBy(file => int.TryParse(file.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : int.MaxValue)
            .ThenBy(file => file.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        foreach (var file in files)
        {
            LegacyScenarioDocument document;
            try
            {
                document = _scenarioReader.Read(file.Path, dictionary);
            }
            catch (Exception ex)
            {
                warnings.Add($"{file.FileName} 读取失败：{ex.Message}");
                continue;
            }

            totalCommandCount += document.CommandCount;
            var relativePath = BuildScenarioRelativePath(file.FileName);
            foreach (var command in document.EnumerateCommands())
            {
                if (!TryGetExclusiveSetTextParameter(command, out var textParameter)) continue;
                var sourceText = textParameter.Text;
                if (TryParseEntry(
                        relativePath,
                        file.FileName,
                        command,
                        sourceText,
                        effectNames,
                        personNames,
                        itemNames,
                        boundary.DefenseStartId,
                        boundary.AccessoryStartId,
                        out var entry,
                        out var malformedEntry))
                {
                    entries.Add(entry);
                }
                else if (malformedEntry != null)
                {
                    malformed.Add(malformedEntry);
                }
            }
        }

        return new ExclusiveSetScenarioReadResult
        {
            Entries = entries,
            MalformedEntries = malformed,
            Warnings = warnings,
            ScannedFileCount = files.Count,
            TotalCommandCount = totalCommandCount
        };
    }

    public ExclusiveSetScenarioSaveResult Save(
        CczProject project,
        SceneStringDocument dictionary,
        IReadOnlyList<ExclusiveSetScenarioUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(updates);

        if (updates.Count == 0)
        {
            return new ExclusiveSetScenarioSaveResult();
        }

        var writes = new List<LegacyScenarioWriteResult>();
        var warnings = new List<string>();
        var changedEntryCount = 0;

        foreach (var group in updates.GroupBy(update => NormalizeRelativePath(update.RelativePath), StringComparer.OrdinalIgnoreCase))
        {
            var relativePath = group.Key;
            var filePath = ResolveScenarioPath(project, relativePath);
            var document = _scenarioReader.Read(filePath, dictionary);
            var changed = false;

            foreach (var update in group)
            {
                var command = FindTargetCommand(document, update);
                if (command == null)
                {
                    throw new InvalidOperationException($"未找到目标 72/10 命令：{relativePath} Scene {update.SceneIndex} / Section {update.SectionIndex} / Command {update.CommandIndex} / 0x{update.FileOffset:X6}");
                }

                if (!TryGetExclusiveSetTextParameter(command, out var textParameter))
                {
                    throw new InvalidOperationException($"目标命令已不再是 72/10：{relativePath} Scene {update.SceneIndex} / Section {update.SectionIndex} / Command {update.CommandIndex}");
                }

                var currentHash = ComputeTextHash(textParameter.Text);
                if (!string.Equals(currentHash, update.OriginalTextHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"目标命令已被外部修改，拒绝覆盖：{relativePath} Scene {update.SceneIndex} / Section {update.SectionIndex} / Command {update.CommandIndex} / 0x{update.FileOffset:X6}");
                }

                var rebuilt = BuildText(update);
                if (string.Equals(textParameter.Text, rebuilt, StringComparison.Ordinal))
                {
                    continue;
                }

                textParameter.Text = rebuilt;
                changed = true;
                changedEntryCount++;
            }

            if (!changed) continue;
            writes.Add(_scenarioWriter.Save(
                project,
                relativePath,
                document,
                dictionary,
                "个人专属/套装 72/10 剧本文本写回"));
        }

        return new ExclusiveSetScenarioSaveResult
        {
            Writes = writes,
            Warnings = warnings,
            ChangedEntryCount = changedEntryCount
        };
    }

    public static bool TryParseSample(
        string sourceText,
        IReadOnlyDictionary<int, string> effectNames,
        IReadOnlyDictionary<int, string> personNames,
        IReadOnlyDictionary<int, string> itemNames,
        out ExclusiveSetScenarioEntry? entry,
        out string error)
    {
        var command = new LegacyScenarioCommandNode
        {
            SceneIndex = 1,
            SectionIndex = 1,
            CommandIndex = 1,
            CommandId = CommandIdInformationTransfer,
            CommandName = "信息传送",
            FileOffset = 0
        };
        command.Parameters.Add(new LegacyScenarioCommandParameter
        {
            Index = 0,
            Kind = LegacyScenarioParameterKind.Dword32,
            IntValue = ExclusiveSetSubFunction
        });
        command.Parameters.Add(new LegacyScenarioCommandParameter
        {
            Index = 1,
            Kind = LegacyScenarioParameterKind.Text,
            Text = sourceText
        });

        if (TryParseEntry(
                "RS/R_00.eex",
                "R_00.eex",
                command,
                sourceText,
                effectNames,
                personNames,
                itemNames,
                defenseStartId: 70,
                accessoryStartId: 109,
                out var parsed,
                out var malformed))
        {
            entry = parsed;
            error = string.Empty;
            return true;
        }

        entry = null;
        error = malformed?.Reason ?? "解析失败";
        return false;
    }

    public static string FormatEffect(int effectId, IReadOnlyDictionary<int, string> effectNames)
        => $"{effectId}:{NormalizeDisplayName(effectNames.TryGetValue(effectId, out var name) && !string.IsNullOrWhiteSpace(name) ? name : "未命名")}";

    public static string FormatItem(int itemId, IReadOnlyDictionary<int, string> itemNames)
    {
        if (itemId >= EmptyItemId) return EmptyItemId.ToString(CultureInfo.InvariantCulture);
        return itemNames.TryGetValue(itemId, out var name) && !string.IsNullOrWhiteSpace(name)
            ? $"{itemId}:{NormalizeDisplayName(name)}"
            : $"{itemId}:未命名";
    }

    public static string FormatPerson(int personId, IReadOnlyDictionary<int, string> personNames)
    {
        if (personId >= EmptyItemId) return EmptyItemId.ToString(CultureInfo.InvariantCulture);
        return personNames.TryGetValue(personId, out var name) && !string.IsNullOrWhiteSpace(name)
            ? $"{personId}:{NormalizeDisplayName(name)}"
            : $"{personId}:未命名";
    }

    public static string FormatEquipment(
        int weaponId,
        int armorId,
        int accessoryId,
        IReadOnlyDictionary<int, string> itemNames)
        => $"武器:{FormatItem(weaponId, itemNames)} | 防具:{FormatItem(armorId, itemNames)} | 辅助:{FormatItem(accessoryId, itemNames)}";

    public static string ComputeTextHash(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(NormalizeLineEndings(text));
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static bool TryParseEntry(
        string relativePath,
        string fileName,
        LegacyScenarioCommandNode command,
        string sourceText,
        IReadOnlyDictionary<int, string> effectNames,
        IReadOnlyDictionary<int, string> personNames,
        IReadOnlyDictionary<int, string> itemNames,
        int defenseStartId,
        int accessoryStartId,
        out ExclusiveSetScenarioEntry entry,
        out ExclusiveSetScenarioMalformedEntry? malformed)
    {
        entry = null!;
        malformed = null;
        var normalized = NormalizeLineEndings(sourceText);
        var lines = normalized.Split('\n');
        var sourceDisplay = BuildSourceDisplay(fileName, command);
        var hash = ComputeTextHash(sourceText);

        if (lines.Length < 2)
        {
            malformed = BuildMalformed(relativePath, fileName, command, sourceText, hash, sourceDisplay, "文本不足两行，无法读取类型与数据行。");
            return false;
        }

        if (!int.TryParse(lines[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var kindValue) ||
            kindValue is not 0 and not 1)
        {
            malformed = BuildMalformed(relativePath, fileName, command, sourceText, hash, sourceDisplay, "第一行必须为 0（专属）或 1（套装）。");
            return false;
        }

        if (!TryParseCsvIntegers(lines[1], out var values, out var reason))
        {
            malformed = BuildMalformed(relativePath, fileName, command, sourceText, hash, sourceDisplay, reason);
            return false;
        }

        var remarks = lines.Length > 2 ? string.Join("\n", lines.Skip(2)) : string.Empty;
        var kind = kindValue == 0 ? ExclusiveSetDesignKind.Personal : ExclusiveSetDesignKind.Set;
        int position;
        int effectId;
        int personId = EmptyItemId;
        int weaponId = EmptyItemId;
        int armorId = EmptyItemId;
        int accessoryId = EmptyItemId;
        int effectValue;

        if (kind == ExclusiveSetDesignKind.Personal)
        {
            if (values.Count != 5)
            {
                malformed = BuildMalformed(relativePath, fileName, command, sourceText, hash, sourceDisplay, "专属数据行必须为 5 个数字：位置,特效,角色,装备,特效值。");
                return false;
            }

            position = values[0];
            effectId = values[1];
            personId = values[2];
            var itemId = NormalizeEmptyItem(values[3]);
            effectValue = values[4];
            AssignPersonalItemSlot(itemId, defenseStartId, accessoryStartId, out weaponId, out armorId, out accessoryId);
        }
        else
        {
            if (values.Count != 6)
            {
                malformed = BuildMalformed(relativePath, fileName, command, sourceText, hash, sourceDisplay, "套装数据行必须为 6 个数字：位置,特效,武器,防具,辅助,特效值。");
                return false;
            }

            position = values[0];
            effectId = values[1];
            weaponId = NormalizeEmptyItem(values[2]);
            armorId = NormalizeEmptyItem(values[3]);
            accessoryId = NormalizeEmptyItem(values[4]);
            effectValue = values[5];
        }

        entry = new ExclusiveSetScenarioEntry
        {
            EntryId = BuildEntryId(relativePath, command),
            Kind = kind,
            RelativePath = relativePath,
            FileName = fileName,
            SceneIndex = command.SceneIndex,
            SectionIndex = command.SectionIndex,
            CommandIndex = command.CommandIndex,
            CommandOrdinal = command.CommandOrdinal,
            FileOffset = command.FileOffset,
            SourceText = sourceText,
            SourceTextHash = hash,
            Remarks = remarks,
            Position = position,
            EffectId = effectId,
            PersonId = personId,
            WeaponId = weaponId,
            ArmorId = armorId,
            AccessoryId = accessoryId,
            EffectValue = effectValue,
            EffectDisplay = FormatEffect(effectId, effectNames),
            EquipmentDisplay = FormatEquipment(weaponId, armorId, accessoryId, itemNames),
            PersonDisplay = kind == ExclusiveSetDesignKind.Personal ? FormatPerson(personId, personNames) : EmptyItemId.ToString(CultureInfo.InvariantCulture),
            SourceDisplay = sourceDisplay
        };
        return true;
    }

    private static string BuildText(ExclusiveSetScenarioUpdate update)
    {
        if (update.Kind == ExclusiveSetDesignKind.Personal)
        {
            var itemIds = new[] { NormalizeEmptyItem(update.WeaponId), NormalizeEmptyItem(update.ArmorId), NormalizeEmptyItem(update.AccessoryId) };
            var nonEmpty = itemIds.Where(id => id != EmptyItemId).ToList();
            if (nonEmpty.Count != 1)
            {
                throw new InvalidOperationException($"专属行必须且只能填写一个装备槽：{update.RelativePath} Scene {update.SceneIndex} / Section {update.SectionIndex} / Command {update.CommandIndex}");
            }

            return BuildTextCore(
                "0",
                string.Join(",", update.Position, update.EffectId, update.PersonId, nonEmpty[0], update.EffectValue),
                update.Remarks);
        }

        return BuildTextCore(
            "1",
            string.Join(",", update.Position, update.EffectId, NormalizeEmptyItem(update.WeaponId), NormalizeEmptyItem(update.ArmorId), NormalizeEmptyItem(update.AccessoryId), update.EffectValue),
            update.Remarks);
    }

    private static string BuildTextCore(string kindLine, string dataLine, string remarks)
    {
        if (string.IsNullOrEmpty(remarks))
        {
            return kindLine + "\n" + dataLine;
        }

        return kindLine + "\n" + dataLine + "\n" + remarks;
    }

    private static bool TryGetExclusiveSetTextParameter(LegacyScenarioCommandNode command, out LegacyScenarioCommandParameter textParameter)
    {
        textParameter = null!;
        if (command.CommandId != CommandIdInformationTransfer || command.Parameters.Count < 2)
        {
            return false;
        }

        var first = command.Parameters[0];
        if (first.Kind != LegacyScenarioParameterKind.Dword32 || first.IntValue != ExclusiveSetSubFunction)
        {
            return false;
        }

        var text = command.Parameters.FirstOrDefault(parameter => parameter.Kind == LegacyScenarioParameterKind.Text);
        if (text == null)
        {
            return false;
        }

        textParameter = text;
        return true;
    }

    private static LegacyScenarioCommandNode? FindTargetCommand(LegacyScenarioDocument document, ExclusiveSetScenarioUpdate update)
    {
        var candidates = document.EnumerateCommands()
            .Where(command => command.CommandId == CommandIdInformationTransfer)
            .ToList();

        return candidates.FirstOrDefault(command =>
                   command.CommandOrdinal == update.CommandOrdinal &&
                   command.SceneIndex == update.SceneIndex &&
                   command.SectionIndex == update.SectionIndex &&
                   command.CommandIndex == update.CommandIndex &&
                   command.FileOffset == update.FileOffset)
               ?? candidates.FirstOrDefault(command =>
                   command.SceneIndex == update.SceneIndex &&
                   command.SectionIndex == update.SectionIndex &&
                   command.CommandIndex == update.CommandIndex &&
                   command.FileOffset == update.FileOffset)
               ?? candidates.FirstOrDefault(command =>
                   command.CommandOrdinal == update.CommandOrdinal &&
                   command.SceneIndex == update.SceneIndex &&
                   command.SectionIndex == update.SectionIndex);
    }

    private static bool TryParseCsvIntegers(string text, out IReadOnlyList<int> values, out string error)
    {
        var result = new List<int>();
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                values = Array.Empty<int>();
                error = $"数据行第 {i + 1} 项不是整数：{parts[i]}";
                return false;
            }

            result.Add(value);
        }

        values = result;
        error = string.Empty;
        return true;
    }

    private static void AssignPersonalItemSlot(
        int itemId,
        int defenseStartId,
        int accessoryStartId,
        out int weaponId,
        out int armorId,
        out int accessoryId)
    {
        weaponId = EmptyItemId;
        armorId = EmptyItemId;
        accessoryId = EmptyItemId;
        itemId = NormalizeEmptyItem(itemId);
        if (itemId == EmptyItemId) return;

        if (itemId < defenseStartId)
        {
            weaponId = itemId;
        }
        else if (itemId < accessoryStartId)
        {
            armorId = itemId;
        }
        else
        {
            accessoryId = itemId;
        }
    }

    private static int NormalizeEmptyItem(int itemId)
        => itemId >= EmptyItemId ? EmptyItemId : itemId;

    private static ExclusiveSetScenarioMalformedEntry BuildMalformed(
        string relativePath,
        string fileName,
        LegacyScenarioCommandNode command,
        string sourceText,
        string hash,
        string sourceDisplay,
        string reason)
        => new()
        {
            RelativePath = relativePath,
            FileName = fileName,
            SceneIndex = command.SceneIndex,
            SectionIndex = command.SectionIndex,
            CommandIndex = command.CommandIndex,
            CommandOrdinal = command.CommandOrdinal,
            FileOffset = command.FileOffset,
            SourceText = sourceText,
            SourceTextHash = hash,
            SourceDisplay = sourceDisplay,
            Reason = reason
        };

    private static string NormalizeLineEndings(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string NormalizeDisplayName(string name)
        => name.Replace('：', ':').Trim();

    private static string BuildSourceDisplay(string fileName, LegacyScenarioCommandNode command)
        => Path.GetFileNameWithoutExtension(fileName);

    private static string BuildEntryId(string relativePath, LegacyScenarioCommandNode command)
        => $"{NormalizeRelativePath(relativePath)}|{command.SceneIndex}|{command.SectionIndex}|{command.CommandIndex}|{command.CommandOrdinal}|{command.FileOffset}";

    private static string BuildScenarioRelativePath(string fileName)
        => NormalizeRelativePath(Path.Combine("RS", fileName));

    private static string NormalizeRelativePath(string relativePath)
        => relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);

    private static string ResolveScenarioPath(CczProject project, string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(project.GameRoot, NormalizeRelativePath(relativePath)));
        var root = Path.GetFullPath(project.GameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("目标剧本不在当前项目目录内，拒绝写入：" + fullPath);
        }

        return fullPath;
    }

    public static IReadOnlyDictionary<int, string> BuildItemNameLookup(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var result = new Dictionary<int, string>();
        var reader = new HexTableReader();
        foreach (var tableName in new[] { "6.5-1 物品（0-103）", "6.5-2 物品（104-255）" })
        {
            var table = FindTable(tables, tableName);
            var read = reader.Read(project, table, tables);
            if (!read.Validation.IsUsable || !read.Data.Columns.Contains("名称")) continue;
            foreach (DataRow row in read.Data.Rows)
            {
                var id = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
                result[id] = Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
            }
        }

        return result;
    }

    public static IReadOnlyDictionary<int, string> BuildIdNameLookup(CczProject project, IReadOnlyList<HexTableDefinition> tables, string tableName)
    {
        var table = FindTable(tables, tableName);
        var read = new HexTableReader().Read(project, table, tables);
        var result = new Dictionary<int, string>();
        if (!read.Validation.IsUsable || !read.Data.Columns.Contains("名称")) return result;
        foreach (DataRow row in read.Data.Rows)
        {
            var id = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
            result[id] = Convert.ToString(row["名称"], CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return result;
    }

    private static HexTableDefinition FindTable(IReadOnlyList<HexTableDefinition> tables, string name)
        => tables.FirstOrDefault(table => table.TableName.Equals(name, StringComparison.OrdinalIgnoreCase))
           ?? tables.FirstOrDefault(table => table.TableName.Contains(name, StringComparison.OrdinalIgnoreCase))
           ?? throw new InvalidOperationException("未找到数据表：" + name);

    private static (int DefenseStartId, int AccessoryStartId, string Source) ResolveItemCategoryBoundary(CczProject project)
    {
        var path = !string.IsNullOrWhiteSpace(project.ImageAssignerSystemIniPath) && File.Exists(project.ImageAssignerSystemIniPath)
            ? project.ImageAssignerSystemIniPath
            : ProjectDetector.FindPortableFile(
                project,
                "System.ini",
                Path.Combine("老版游戏制作工具", "B形象指定器", "6.6x形象指定器", "System.ini"),
                Path.Combine("B形象指定器", "6.6x形象指定器", "System.ini"),
                Path.Combine("老版游戏制作工具", "B形象指定器", "形象指定器6.5", "System.ini"),
                Path.Combine("B形象指定器", "形象指定器6.5", "System.ini"));
        if (path != null && File.Exists(path))
        {
            var defId = 70;
            var assId = 109;
            foreach (var rawLine in File.ReadLines(path, EncodingService.Gbk))
            {
                var line = rawLine.Split(';')[0].Trim();
                if (line.StartsWith("DefID=", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(line["DefID=".Length..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDef))
                {
                    defId = parsedDef;
                }
                else if (line.StartsWith("AssID=", StringComparison.OrdinalIgnoreCase) &&
                         int.TryParse(line["AssID=".Length..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAss))
                {
                    assId = parsedAss;
                }
            }

            return (defId, assId, path);
        }

        return (70, 109, "默认：B形象指定器 System.ini 未找到");
    }
}
