using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using CCZModStudio;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

internal partial class Program
{
    static void RunLegacyE5sSmoke(CczProject project)
    {
        var svDir = Path.Combine(project.GameRoot, "SV");
        var files = Directory.Exists(svDir)
            ? Directory.GetFiles(svDir, "*.E5S", SearchOption.TopDirectoryOnly)
                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
            : new List<string>();
        Console.WriteLine($"LEGACY_E5S_INDEX count={files.Count} dir={svDir}");
        if (files.Count == 0)
        {
            throw new InvalidOperationException("未找到 SV\\*.E5S；E5S 兼容检查无法运行。");
        }
    
        var configPath = ProjectDetector.FindPortableFile(
            project,
            "System.ini",
            Path.Combine("老版游戏制作工具", "B形象指定器", "形象指定器6.5", "System.ini"),
            Path.Combine("B形象指定器", "形象指定器6.5", "System.ini"))
            ?? string.Empty;
        var countSvText = ReadIniValue(configPath, "CountSV") ?? "未找到";
        Console.WriteLine($"LEGACY_E5S_CONFIG CountSV={countSvText} source={configPath}");
        if (!string.Equals(countSvText, "900", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("LEGACY_E5S_CONFIG_WARN CountSV 与当前已知 6.5 默认值 900 不一致，请确认是否为用户扩展存档数。");
        }
    
        var first = files.First();
        var firstInfo = new FileInfo(first);
        var firstHeader = File.ReadAllBytes(first).Take(16).Select(x => x.ToString("X2", CultureInfo.InvariantCulture));
        Console.WriteLine($"LEGACY_E5S_ROW first={Path.GetFileName(first)} size={firstInfo.Length} head={string.Join(' ', firstHeader)}");
    
        var sv004 = files.FirstOrDefault(x => Path.GetFileName(x).Equals("SV004.E5S", StringComparison.OrdinalIgnoreCase)) ?? first;
        var textRows = new ScenarioTextReader().Read(sv004, maxItems: 16);
        Console.WriteLine($"LEGACY_E5S_TEXT file={Path.GetFileName(sv004)} rows={textRows.Count} note=compat_only_not_rs_script");
    
        var sceneStringPath = ProjectDetector.FindSceneDictionaryPath(project);
        if (File.Exists(sceneStringPath))
        {
            var sceneDoc = new SceneStringParser().Parse(sceneStringPath);
            var commandRows = new ScenarioCommandProbeReader().Probe(sv004, sceneDoc, maxRows: 40);
            Console.WriteLine($"LEGACY_E5S_COMMAND_PROBE file={Path.GetFileName(sv004)} rows={commandRows.Count} note=old_probe_only");
        }
        else
        {
            Console.WriteLine("LEGACY_E5S_COMMAND_PROBE skipped: CczString.ini not found");
        }
    
        Console.WriteLine("LEGACY_E5S_SMOKE OK");
    }
    
    static string? ReadIniValue(string path, string key)
    {
        if (!File.Exists(path)) return null;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("[", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal)) continue;
            var comment = line.IndexOf(';');
            if (comment >= 0) line = line[..comment].Trim();
            var index = line.IndexOf('=', StringComparison.Ordinal);
            if (index <= 0) continue;
            if (line[..index].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return line[(index + 1)..].Trim();
            }
        }
    
        return null;
    }
}
