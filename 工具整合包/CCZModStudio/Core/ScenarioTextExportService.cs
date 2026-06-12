using System.Text;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class ScenarioTextExportService
{
    public string ExportCsv(CczProject project, string scenarioFileName, IReadOnlyList<ScenarioTextEntry> entries)
    {
        var dir = CreateExportDirectory(project);
        var path = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(scenarioFileName)}_ScenarioTexts.csv");
        var lines = new List<string>
        {
            "Index,OffsetHex,ByteLength,CharLength,GbkByteCount,RemainingBytes,WriteStatus,Kind,HasNewLines,Preview,Text,OriginalText,Annotation"
        };
        lines.AddRange(entries.Select(x => string.Join(',',
            Csv(x.Index.ToString()),
            Csv(x.OffsetHex),
            Csv(x.ByteLength.ToString()),
            Csv(x.CharLength.ToString()),
            Csv(x.GbkByteCount.ToString()),
            Csv(x.RemainingBytes.ToString()),
            Csv(x.WriteStatus),
            Csv(x.Kind),
            Csv(x.HasNewLines ? "true" : "false"),
            Csv(x.Preview),
            Csv(x.Text),
            Csv(x.OriginalText),
            Csv(x.Annotation))));
        File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return path;
    }

    public string ExportTxt(CczProject project, string scenarioFileName, IReadOnlyList<ScenarioTextEntry> entries)
    {
        var dir = CreateExportDirectory(project);
        var path = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(scenarioFileName)}_ScenarioTexts.txt");
        var builder = new StringBuilder();
        builder.AppendLine("CCZModStudio Scenario Text Export");
        builder.AppendLine("Scenario=" + scenarioFileName);
        builder.AppendLine("Count=" + entries.Count);
        builder.AppendLine();
        foreach (var entry in entries)
        {
            builder.AppendLine($"[{entry.Index}] {entry.OffsetHex} bytes={entry.ByteLength} gbk={entry.GbkByteCount} remain={entry.RemainingBytes} status={entry.WriteStatus} chars={entry.CharLength} kind={entry.Kind}");
            builder.AppendLine("注释：" + entry.Annotation);
            builder.AppendLine(entry.Text);
            builder.AppendLine(new string('-', 60));
        }
        File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
        return path;
    }

    private static string CreateExportDirectory(CczProject project)
    {
        var dir = project.IsTestCopy
            ? Path.Combine(project.GameRoot, "_CCZModStudio_Exports", "ScenarioTexts")
            : Path.Combine(project.WorkspaceRoot, "CCZModStudio_Exports", "ScenarioTexts", project.Name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
