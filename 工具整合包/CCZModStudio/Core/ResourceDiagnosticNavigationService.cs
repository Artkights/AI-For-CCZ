using System.Globalization;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// 资源诊断导航解析服务。
/// 把诊断行解析为可跳转对象：资源索引、地图浏览、E5S旧兼容探针、Hexzmap 地形块、关卡地图联动。
/// </summary>
public sealed class ResourceDiagnosticNavigationService
{
    public ResourceDiagnosticNavigationTarget Resolve(
        ResourceDiagnosticItem diagnostic,
        IReadOnlyList<ScenarioMapLinkInfo> scenarioMapLinks,
        IReadOnlyList<ResourceIndexItem> resources)
    {
        var resource = FindResourceForDiagnostic(diagnostic, resources);
        var scenarioFileName = ExtractScenarioFileName(diagnostic, resource);
        var mapId = ExtractMapId(diagnostic, resource);
        var imageAssignmentPrefix = ExtractImageAssignmentPrefix(diagnostic, resource);
        var imageAssignmentRowId = ExtractImageAssignmentRowId(diagnostic);
        var imageResourceId = ExtractImageResourceId(diagnostic, resource, imageAssignmentPrefix);
        var tableName = ExtractTableName(diagnostic, imageAssignmentPrefix);
        var tableFieldName = ExtractTableFieldName(diagnostic, imageAssignmentPrefix);
        var tableRowId = ExtractTableRowId(diagnostic, imageAssignmentRowId);
        var link = FindScenarioMapLink(diagnostic, scenarioMapLinks, resource, scenarioFileName, mapId);

        scenarioFileName = FirstNonEmpty(link?.ScenarioFileName, scenarioFileName);
        mapId = FirstNonEmpty(link?.MapId, mapId);
        var scenarioPath = FirstNonEmpty(link?.ScenarioPath, LooksLikeScenarioPath(diagnostic.Path) ? diagnostic.Path : string.Empty);
        var mapImageName = FirstNonEmpty(link?.MapImageName, resource?.Category == "地图图片" ? resource.Name : string.Empty);
        var mapImagePath = FirstNonEmpty(link?.MapImagePath, resource?.Category == "地图图片" ? resource.Path : string.Empty);
        var mapExists = link?.MapImageExists == true || (!string.IsNullOrWhiteSpace(mapImagePath) && File.Exists(mapImagePath));
        var hexOffset = link?.HexzmapOffsetHex ?? string.Empty;
        var hexExists = link?.HexzmapBlockExists == true;

        var recognized =
            link != null ||
            resource != null ||
            !string.IsNullOrWhiteSpace(scenarioFileName) ||
            !string.IsNullOrWhiteSpace(mapId) ||
            !string.IsNullOrWhiteSpace(imageAssignmentPrefix) ||
            !string.IsNullOrWhiteSpace(tableName);

        return new ResourceDiagnosticNavigationTarget
        {
            IsRecognized = recognized,
            Summary = BuildSummary(
                diagnostic,
                link,
                resource,
                scenarioFileName,
                mapId,
                imageAssignmentPrefix,
                imageAssignmentRowId,
                imageResourceId,
                tableName,
                tableRowId,
                tableFieldName),
            ScenarioFileName = scenarioFileName,
            ScenarioPath = scenarioPath,
            MapId = mapId,
            MapImageName = mapImageName,
            MapImagePath = mapImagePath,
            MapImageExists = mapExists,
            HexzmapOffsetHex = hexOffset,
            HexzmapBlockExists = hexExists,
            ResourceCategory = resource?.Category ?? string.Empty,
            ResourceName = resource?.Name ?? string.Empty,
            ResourcePath = resource?.Path ?? string.Empty,
            ImageAssignmentPrefix = imageAssignmentPrefix,
            ImageAssignmentRowId = imageAssignmentRowId,
            ImageResourceId = imageResourceId,
            TableName = tableName,
            TableRowId = tableRowId,
            TableFieldName = tableFieldName
        };
    }

    private static ScenarioMapLinkInfo? FindScenarioMapLink(
        ResourceDiagnosticItem diagnostic,
        IReadOnlyList<ScenarioMapLinkInfo> links,
        ResourceIndexItem? resource,
        string scenarioFileName,
        string mapId)
    {
        if (links.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(scenarioFileName) && !string.IsNullOrWhiteSpace(mapId))
        {
            var exact = links.FirstOrDefault(link =>
                link.ScenarioFileName.Equals(scenarioFileName, StringComparison.OrdinalIgnoreCase) &&
                link.MapId.Equals(mapId, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
        }

        if (!string.IsNullOrWhiteSpace(scenarioFileName))
        {
            var byScenario = links.FirstOrDefault(link => link.ScenarioFileName.Equals(scenarioFileName, StringComparison.OrdinalIgnoreCase));
            if (byScenario != null) return byScenario;
        }

        if (!string.IsNullOrWhiteSpace(mapId))
        {
            var byMap = links.FirstOrDefault(link => link.MapId.Equals(mapId, StringComparison.OrdinalIgnoreCase));
            if (byMap != null) return byMap;
        }

        var path = FirstNonEmpty(resource?.Path, diagnostic.Path);
        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                var byPath = links.FirstOrDefault(link =>
                    PathEquals(link.ScenarioPath, fullPath) ||
                    PathEquals(link.MapImagePath, fullPath));
                if (byPath != null) return byPath;
            }
            catch
            {
                // 诊断 Path 可能不是实际文件路径，继续按名称/编号匹配。
            }
        }

        return null;
    }

    private static ResourceIndexItem? FindResourceForDiagnostic(ResourceDiagnosticItem diagnostic, IReadOnlyList<ResourceIndexItem> resources)
    {
        if (resources.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(diagnostic.Path))
        {
            try
            {
                var fullPath = Path.GetFullPath(diagnostic.Path);
                var exact = resources.FirstOrDefault(item => PathEquals(item.Path, fullPath));
                if (exact != null) return exact;

                var fileName = Path.GetFileName(fullPath);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    var byFileName = resources.FirstOrDefault(item => item.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
                    if (byFileName != null) return byFileName;
                }
            }
            catch
            {
                // 继续用名称、编号和证据文字匹配。
            }
        }

        var byName = resources.FirstOrDefault(item => item.Name.Equals(diagnostic.Name, StringComparison.OrdinalIgnoreCase));
        if (byName != null) return byName;

        if (!string.IsNullOrWhiteSpace(diagnostic.Id))
        {
            var normalized = NormalizeMapId(diagnostic.Id);
            var byId = resources.FirstOrDefault(item =>
                item.Id.Equals(diagnostic.Id, StringComparison.OrdinalIgnoreCase) ||
                item.Id.Equals(normalized.TrimStart('M', 'm'), StringComparison.OrdinalIgnoreCase) ||
                NormalizeMapId(item.Id).Equals(normalized, StringComparison.OrdinalIgnoreCase));
            if (byId != null) return byId;
        }

        return resources.FirstOrDefault(item =>
            Contains(diagnostic.Detail, item.Name) ||
            Contains(diagnostic.Suggestion, item.Name));
    }

    private static string ExtractScenarioFileName(ResourceDiagnosticItem diagnostic, ResourceIndexItem? resource)
    {
        if (resource?.Category == "E5S存档信息")
        {
            return resource.Name;
        }

        if (LooksLikeScenarioPath(diagnostic.Path))
        {
            return Path.GetFileName(diagnostic.Path);
        }

        foreach (var source in new[] { diagnostic.Name, diagnostic.Detail, diagnostic.Status, diagnostic.Suggestion })
        {
            var found = ExtractToken(source, "SV", ".E5S");
            if (!string.IsNullOrWhiteSpace(found)) return found;
        }

        return string.Empty;
    }

    private static string ExtractMapId(ResourceDiagnosticItem diagnostic, ResourceIndexItem? resource)
    {
        if (resource?.Category == "地图图片")
        {
            return NormalizeMapId(resource.Id);
        }

        var id = NormalizeMapId(diagnostic.Id);
        if (!string.IsNullOrWhiteSpace(id)) return id;

        foreach (var source in new[] { diagnostic.Name, diagnostic.Detail, diagnostic.Status, diagnostic.Suggestion, resource?.Name ?? string.Empty })
        {
            var found = ExtractMapToken(source);
            if (!string.IsNullOrWhiteSpace(found)) return found;
        }

        return string.Empty;
    }

    private static string ExtractImageAssignmentPrefix(ResourceDiagnosticItem diagnostic, ResourceIndexItem? resource)
    {
        if (IsScenarioEexCategory(resource?.Category) || IsScenarioEexCategory(diagnostic.Category))
        {
            return string.Empty;
        }

        if (resource?.Category == "R形象") return "R";
        if (resource?.Category == "S形象") return "S";

        foreach (var source in new[] { diagnostic.Category, diagnostic.Rule, diagnostic.Name, diagnostic.Status, diagnostic.Detail, diagnostic.Suggestion, diagnostic.Path })
        {
            if (ContainsImagePrefix(source, "R")) return "R";
            if (ContainsImagePrefix(source, "S")) return "S";
        }

        return string.Empty;
    }

    private static bool IsScenarioEexCategory(string? category) =>
        category is "R剧本EEX" or "S剧本EEX";

    private static int? ExtractImageAssignmentRowId(ResourceDiagnosticItem diagnostic)
    {
        if (diagnostic.Category.StartsWith("表格引用/", StringComparison.Ordinal) &&
            diagnostic.Rule == "人物形象缺失" &&
            TryParseInt(diagnostic.Id, out var directRowId))
        {
            return directRowId;
        }

        var fromDetail = ExtractNumberBetween(diagnostic.Detail, "第", "行");
        if (fromDetail.HasValue) return fromDetail.Value;

        if (diagnostic.Rule == "多个已命名人物共享形象")
        {
            var sharedRow = ExtractLeadingNumber(diagnostic.Detail);
            if (sharedRow.HasValue) return sharedRow.Value;
        }

        if (diagnostic.Rule == "空名占位缺失")
        {
            var sampleRow = ExtractFirstNumberAfterAnyMarker(diagnostic.Detail, "ID：", "ID:", "行 ID：", "行 ID:");
            if (sampleRow.HasValue) return sampleRow.Value;
        }

        return null;
    }

    private static int? ExtractImageResourceId(ResourceDiagnosticItem diagnostic, ResourceIndexItem? resource, string prefix)
    {
        if (prefix is not ("R" or "S")) return null;

        if ((resource?.Category == $"{prefix}形象") && TryParseInt(resource.Id, out var resourceId))
        {
            return resourceId;
        }

        var idMayBeResourceId =
            diagnostic.Category == $"{prefix}形象" ||
            diagnostic.Rule is "资源未被人物表引用" or "多个已命名人物共享形象";
        if (idMayBeResourceId && TryParseInt(diagnostic.Id, out var directId))
        {
            return directId;
        }

        foreach (var source in new[] { diagnostic.Path, diagnostic.Status, diagnostic.Detail, diagnostic.Name, diagnostic.Suggestion })
        {
            var fromFileName = ExtractImageFileId(source, prefix);
            if (fromFileName.HasValue) return fromFileName.Value;

            var fromMarker = ExtractFirstNumberAfterAnyMarker(
                source,
                $"{prefix}形象编号=",
                $"{prefix}形象编号＝",
                $"{prefix}形象编号:",
                $"{prefix}形象编号：",
                $"{prefix} 形象编号=",
                $"{prefix} 形象编号：");
            if (fromMarker.HasValue) return fromMarker.Value;
        }

        return null;
    }

    private static string ExtractTableName(ResourceDiagnosticItem diagnostic, string imageAssignmentPrefix)
    {
        if (!diagnostic.Category.StartsWith("表格引用/", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return imageAssignmentPrefix switch
        {
            "R" => "6.5-0-4 R形象",
            "S" => "6.5-0-5 S形象",
            _ => ExtractFirstMarkedText(
                new[] { diagnostic.Detail, diagnostic.Status, diagnostic.Suggestion, diagnostic.Name },
                "源表：",
                "源表=",
                "数据表：",
                "数据表=",
                "表：",
                "表=")
        };
    }

    private static string ExtractTableFieldName(ResourceDiagnosticItem diagnostic, string imageAssignmentPrefix)
    {
        if (!diagnostic.Category.StartsWith("表格引用/", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return imageAssignmentPrefix switch
        {
            "R" => "R形象编号",
            "S" => "S形象编号",
            _ => ExtractFirstMarkedText(
                new[] { diagnostic.Detail, diagnostic.Status, diagnostic.Suggestion, diagnostic.Name },
                "字段：",
                "字段=",
                "列：",
                "列=")
        };
    }

    private static string ExtractTableRowId(ResourceDiagnosticItem diagnostic, int? imageAssignmentRowId)
    {
        if (!diagnostic.Category.StartsWith("表格引用/", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (imageAssignmentRowId.HasValue)
        {
            return imageAssignmentRowId.Value.ToString(CultureInfo.InvariantCulture);
        }

        var markedRow = ExtractFirstNumberAfterAnyMarker(
            diagnostic.Detail,
            "行 ID：",
            "行 ID:",
            "行ID：",
            "行ID:",
            "ID：",
            "ID=");
        if (markedRow.HasValue)
        {
            return markedRow.Value.ToString(CultureInfo.InvariantCulture);
        }

        return TryParseInt(diagnostic.Id, out var directRowId)
            ? directRowId.ToString(CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static string ExtractFirstMarkedText(IEnumerable<string> sources, params string[] markers)
    {
        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source)) continue;
            foreach (var marker in markers)
            {
                var index = source.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index < 0) continue;

                var start = index + marker.Length;
                while (start < source.Length && char.IsWhiteSpace(source[start])) start++;
                var end = start;
                while (end < source.Length && !IsMarkedTextDelimiter(source[end])) end++;
                var value = source[start..end].Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return string.Empty;
    }

    private static bool IsMarkedTextDelimiter(char ch)
        => ch is '；' or ';' or '\r' or '\n' or '\t';

    private static string NormalizeMapId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        value = value.Trim();
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return "M" + number.ToString("000", CultureInfo.InvariantCulture);
        }

        if (!value.StartsWith("M", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        var digits = new string(value.Skip(1).TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            ? "M" + number.ToString("000", CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static string ExtractMapToken(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        for (var i = 0; i < text.Length - 1; i++)
        {
            if (text[i] != 'M' && text[i] != 'm') continue;
            var start = i + 1;
            var end = start;
            while (end < text.Length && char.IsDigit(text[end])) end++;
            if (end == start) continue;
            var normalized = NormalizeMapId(text[i..end]);
            if (!string.IsNullOrWhiteSpace(normalized)) return normalized;
        }

        return string.Empty;
    }

    private static bool ContainsImagePrefix(string text, string prefix)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Contains($"{prefix}形象", StringComparison.Ordinal) ||
               text.Contains($"{prefix} 形象", StringComparison.Ordinal) ||
               text.Contains($"{prefix}_", StringComparison.OrdinalIgnoreCase) ||
               text.Contains($"{prefix}\\", StringComparison.OrdinalIgnoreCase) ||
               text.Contains($"{prefix}/", StringComparison.OrdinalIgnoreCase);
    }

    private static int? ExtractImageFileId(string text, string prefix)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var marker = prefix + "_";
        var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var start = index + marker.Length;
            var end = start;
            while (end < text.Length && char.IsDigit(text[end])) end++;
            if (end > start && int.TryParse(text[start..end], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                return number;
            }

            index = text.IndexOf(marker, index + marker.Length, StringComparison.OrdinalIgnoreCase);
        }

        return null;
    }

    private static int? ExtractFirstNumberAfterAnyMarker(string text, params string[] markers)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        foreach (var marker in markers)
        {
            var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;
            var number = ExtractFirstNumber(text[(index + marker.Length)..]);
            if (number.HasValue) return number.Value;
        }

        return null;
    }

    private static int? ExtractNumberBetween(string text, string leftMarker, string rightMarker)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var left = text.IndexOf(leftMarker, StringComparison.Ordinal);
        while (left >= 0)
        {
            var start = left + leftMarker.Length;
            var right = text.IndexOf(rightMarker, start, StringComparison.Ordinal);
            if (right > start)
            {
                var number = ExtractFirstNumber(text[start..right]);
                if (number.HasValue) return number.Value;
            }

            left = text.IndexOf(leftMarker, start, StringComparison.Ordinal);
        }

        return null;
    }

    private static int? ExtractLeadingNumber(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var index = 0;
        while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
        var start = index;
        while (index < text.Length && char.IsDigit(text[index])) index++;
        return index > start && int.TryParse(text[start..index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    private static int? ExtractFirstNumber(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsDigit(text[i])) continue;
            var end = i + 1;
            while (end < text.Length && char.IsDigit(text[end])) end++;
            return int.TryParse(text[i..end], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? number
                : null;
        }

        return null;
    }

    private static bool TryParseInt(string text, out int number)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);

    private static string ExtractToken(string text, string prefix, string suffix)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var index = text.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var end = text.IndexOf(suffix, index, StringComparison.OrdinalIgnoreCase);
            if (end >= 0)
            {
                return text[index..(end + suffix.Length)];
            }
            index = text.IndexOf(prefix, index + prefix.Length, StringComparison.OrdinalIgnoreCase);
        }

        return string.Empty;
    }

    private static bool LooksLikeScenarioPath(string path)
        => Path.GetExtension(path).Equals(".E5S", StringComparison.OrdinalIgnoreCase);

    private static bool PathEquals(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        try
        {
            return Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return left.Equals(right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool Contains(string value, string keyword)
        => !string.IsNullOrWhiteSpace(value) &&
           !string.IsNullOrWhiteSpace(keyword) &&
           value.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string BuildSummary(
        ResourceDiagnosticItem diagnostic,
        ScenarioMapLinkInfo? link,
        ResourceIndexItem? resource,
        string scenarioFileName,
        string mapId,
        string imageAssignmentPrefix,
        int? imageAssignmentRowId,
        int? imageResourceId,
        string tableName,
        string tableRowId,
        string tableFieldName)
    {
        var parts = new List<string>();
        if (link != null) parts.Add($"关卡联动：{link.ScenarioFileName}->{link.MapId}");
        if (resource != null) parts.Add($"资源：{resource.Category}/{resource.Name}");
        if (!string.IsNullOrWhiteSpace(scenarioFileName)) parts.Add($"SV：{scenarioFileName}");
        if (!string.IsNullOrWhiteSpace(mapId)) parts.Add($"地图编号：{mapId}");
        if (!string.IsNullOrWhiteSpace(imageAssignmentPrefix))
        {
            var imageParts = new List<string> { $"人物{imageAssignmentPrefix}形象" };
            if (imageAssignmentRowId.HasValue) imageParts.Add($"人物行={imageAssignmentRowId.Value}");
            if (imageResourceId.HasValue) imageParts.Add($"资源编号={imageResourceId.Value:00}");
            parts.Add(string.Join("/", imageParts));
        }
        if (!string.IsNullOrWhiteSpace(tableName))
        {
            var tableParts = new List<string> { $"数据表：{tableName}" };
            if (!string.IsNullOrWhiteSpace(tableRowId)) tableParts.Add($"ID={tableRowId}");
            if (!string.IsNullOrWhiteSpace(tableFieldName)) tableParts.Add($"字段={tableFieldName}");
            parts.Add(string.Join("/", tableParts));
        }
        return parts.Count == 0
            ? $"未识别直接跳转对象：{diagnostic.Category}/{diagnostic.Rule}/{diagnostic.Name}"
            : string.Join("；", parts.Distinct(StringComparer.OrdinalIgnoreCase));
    }
}
