using CCZModStudio;
using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.Text;

internal partial class Program
{
    private static readonly string[] RsMojibakeAuditMarkers =
    [
        "鍦", "璇", "鐨", "鑰", "鏂", "瀛", "淇", "妫", "缂", "绋", "鍓", "鏁",
        "瑙", "瀹", "鎺", "浠嬬粛", "锟", "\uFFFD", "Ã", "Â"
    ];

    static void RunRsMojibakeSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var dictionary = VerifyRsDictionary(project, tables);
        var rExpectedTexts = ResolveScenarioExpectedTexts(project, "R_00.eex",
        [
            ["多周目", "希布", "东吴霸王传"],
            ["功勋模式说明", "曹将军", "许子将"]
        ]);
        var sExpectedTexts = ResolveScenarioExpectedTexts(project, "S_00.eex",
        [
            ["战前设定", "胜利条件", "失败条件"],
            ["曹操军出场", "胜利条件", "失败条件"]
        ]);
        VerifyScenarioTextReader(project, "R_00.eex", rExpectedTexts);
        VerifyScenarioTextReader(project, "S_00.eex", sExpectedTexts);
        VerifyLegacyScenarioReader(project, dictionary, tables, "R_00.eex", rExpectedTexts);
        VerifyLegacyScenarioReader(project, dictionary, tables, "S_00.eex", sExpectedTexts);
        VerifySyntheticDictionaryEncoding();
        VerifyLowConfidenceScenarioScannerFilter();
        VerifyRsPreviewSourceAudit(project.WorkspaceRoot);

        Console.WriteLine("RS_MOJIBAKE_SMOKE_OK");
    }

    private static IReadOnlyList<string> ResolveScenarioExpectedTexts(
        CczProject project,
        string fileName,
        IReadOnlyList<IReadOnlyList<string>> candidates)
    {
        var path = ResolveRsScenarioPath(project, fileName);
        var text = EncodingService.Gbk.GetString(File.ReadAllBytes(path));
        foreach (var candidate in candidates)
        {
            if (candidate.All(expected => text.Contains(expected, StringComparison.Ordinal)))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"{fileName} 未匹配任何已知 R/S 乱码烟测文本组：{string.Join("；", candidates.Select(candidate => string.Join("/", candidate)))}");
    }

    private static SceneStringDocument VerifyRsDictionary(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var dictionaryPath = ProjectDetector.FindSceneDictionaryPath(project);
        if (!File.Exists(dictionaryPath))
        {
            throw new FileNotFoundException("R/S 乱码烟测需要 CczString.ini。", dictionaryPath);
        }

        var dictionary = new SceneStringParser().Parse(dictionaryPath);
        AssertContainsText(dictionary.Commands.Select(command => command.Name), "事件结束", "CczString.ini command 00");
        AssertContainsText(dictionary.Commands.Select(command => command.Name), "子事件设定", "CczString.ini command 01");
        AssertNoMojibake(dictionary.Commands.Select(command => command.Name), "CczString.ini command names");

        if (string.IsNullOrWhiteSpace(dictionary.EncodingName) ||
            dictionary.SourceLineCount <= 0 ||
            dictionary.Commands.Count == 0 ||
            string.IsNullOrWhiteSpace(dictionary.DecodeDiagnostic))
        {
            throw new InvalidOperationException("CczString.ini 诊断信息不完整。");
        }

        var dataSources = LegacyMfcDialogDataSources.Create(project, tables);
        AssertContainsText([dataSources.CommandName(0, string.Empty)], "事件结束", "LegacyMfcDialogDataSources command 00");
        AssertContainsText([dataSources.CommandName(1, string.Empty)], "子事件设定", "LegacyMfcDialogDataSources command 01");
        AssertNoMojibake(
            [dictionary.DecodeDiagnostic, dataSources.CommandName(0, string.Empty), dataSources.CommandName(1, string.Empty)],
            "dictionary diagnostics and data sources");

        Console.WriteLine($"RS_MOJIBAKE_DICTIONARY path={dictionaryPath} {dictionary.DecodeDiagnostic}");
        return dictionary;
    }

    private static void VerifyScenarioTextReader(CczProject project, string fileName, IReadOnlyList<string> expectedTexts)
    {
        var path = ResolveRsScenarioPath(project, fileName);
        var entries = new ScenarioTextReader().Read(path, maxItems: 4096).ToList();
        if (entries.Count == 0)
        {
            throw new InvalidOperationException($"{fileName} 兼容扫描没有读出任何高置信度文本。");
        }

        var combined = string.Join("\n", entries.Select(entry => string.Join(
            "\n",
            entry.Text,
            entry.Preview,
            entry.Annotation,
            entry.SourceKind,
            entry.EncodingName,
            entry.DecodeConfidence,
            entry.DecodeWarning,
            entry.WriteStatus)));

        foreach (var expected in expectedTexts)
        {
            if (!combined.Contains(expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{fileName} 兼容扫描未包含期望文本：{expected}");
            }
        }

        if (entries.Any(entry =>
                string.IsNullOrWhiteSpace(entry.SourceKind) ||
                string.IsNullOrWhiteSpace(entry.EncodingName) ||
                string.IsNullOrWhiteSpace(entry.DecodeConfidence)))
        {
            throw new InvalidOperationException($"{fileName} 兼容扫描文本缺少来源/编码/置信度元数据。");
        }

        if (entries.Any(entry => string.Equals(entry.DecodeConfidence, "低", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"{fileName} 默认兼容扫描不应显示低置信度文本。");
        }

        AssertNoMojibake(entries.SelectMany(entry =>
            new[]
            {
                entry.Text,
                entry.Preview,
                entry.Annotation,
                entry.SourceKind,
                entry.EncodingName,
                entry.DecodeConfidence,
                entry.DecodeWarning,
                entry.WriteStatus
            }), $"{fileName} ScenarioTextReader output");
    }

    private static void VerifyLegacyScenarioReader(
        CczProject project,
        SceneStringDocument dictionary,
        IReadOnlyList<HexTableDefinition> tables,
        string fileName,
        IReadOnlyList<string> expectedTexts)
    {
        var path = ResolveRsScenarioPath(project, fileName);
        var document = new LegacyScenarioReader().Read(path, dictionary);
        if (document.CommandCount <= 0)
        {
            throw new InvalidOperationException($"{fileName} 完整树解析没有读出命令。");
        }

        var commands = document.EnumerateCommands().ToList();
        var textParameters = commands.SelectMany(command => command.TextParameters).ToList();
        if (textParameters.Count == 0)
        {
            throw new InvalidOperationException($"{fileName} 完整树解析没有读出文本参数。");
        }

        var displayTexts = new List<string>();
        var dataSources = LegacyMfcDialogDataSources.Create(project, tables);
        var formatter = new LegacyScenarioCommandDisplayFormatter(dataSources);
        foreach (var command in commands.Take(2048))
        {
            displayTexts.Add(command.CommandName);
            displayTexts.Add(command.DisplayText);
            displayTexts.Add(formatter.FormatCommand(command));
            displayTexts.Add(formatter.FormatValuesPreview(command, 8));
            displayTexts.AddRange(command.Parameters.Select(parameter => string.Join(
                " ",
                parameter.DisplayValue,
                parameter.Text,
                parameter.TextEncodingName,
                parameter.TextDecodeConfidence,
                parameter.TextDecodeWarning)));
        }

        var allText = string.Join("\n", displayTexts);
        foreach (var expected in expectedTexts)
        {
            if (!allText.Contains(expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{fileName} 完整树预览未包含期望文本：{expected}");
            }
        }

        if (textParameters.Any(parameter =>
                string.IsNullOrWhiteSpace(parameter.TextEncodingName) ||
                string.IsNullOrWhiteSpace(parameter.TextDecodeConfidence) ||
                parameter.RawTextBytes.Length == 0))
        {
            throw new InvalidOperationException($"{fileName} 完整树文本参数缺少编码诊断或原始字节。");
        }

        AssertNoMojibake(displayTexts, $"{fileName} LegacyScenarioReader display output");
    }

    private static void VerifySyntheticDictionaryEncoding()
    {
        EncodingService.EnsureCodePages();
        var smokeRoot = Path.Combine(Path.GetTempPath(), "CCZModStudio_RsMojibakeSmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(smokeRoot);
        try
        {
            var content = "00:事件结束,01:子事件设定,02:内部信息\n晴,雨\n";
            var utf8BomPath = Path.Combine(smokeRoot, "CczString.Utf8Bom.ini");
            var gbkPath = Path.Combine(smokeRoot, "CczString.Gbk.ini");
            File.WriteAllText(utf8BomPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            File.WriteAllText(gbkPath, content, EncodingService.Gbk);

            var parser = new SceneStringParser();
            var utf8Bom = parser.Parse(utf8BomPath);
            var gbk = parser.Parse(gbkPath);
            AssertContainsText(utf8Bom.Commands.Select(command => command.Name), "事件结束", "UTF-8 BOM CczString.ini");
            AssertContainsText(gbk.Commands.Select(command => command.Name), "事件结束", "GBK CczString.ini");
            AssertNoMojibake([utf8Bom.DecodeDiagnostic, gbk.DecodeDiagnostic], "synthetic dictionary diagnostics");
        }
        finally
        {
            try
            {
                Directory.Delete(smokeRoot, recursive: true);
            }
            catch
            {
                // Temp cleanup is best effort.
            }
        }
    }

    private static void VerifyLowConfidenceScenarioScannerFilter()
    {
        EncodingService.EnsureCodePages();
        var smokeRoot = Path.Combine(Path.GetTempPath(), "CCZModStudio_RsScannerSmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(smokeRoot);
        try
        {
            var path = Path.Combine(smokeRoot, "Synthetic.eex");
            using (var stream = File.Create(path))
            {
                WriteGbkNullTerminated(stream, "胜利条件：击破敌军");
                stream.Write([0x01, 0x02, 0x03, 0x04, 0x00]);
                WriteGbkNullTerminated(stream, "鍦板浘缂栬緫");
                WriteGbkNullTerminated(stream, "失败条件：主将撤退");
            }

            var reader = new ScenarioTextReader();
            var filtered = reader.Read(path, maxItems: 16).ToList();
            var raw = reader.Read(path, maxItems: 16, includeLowConfidence: true).ToList();

            AssertContainsText(filtered.Select(entry => entry.Text), "胜利条件：击破敌军", "filtered legal text");
            AssertContainsText(filtered.Select(entry => entry.Text), "失败条件：主将撤退", "filtered legal text");
            if (filtered.Any(entry => entry.Text.Contains("鍦板浘", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("默认兼容扫描不应显示二次乱码候选。");
            }

            var mojibake = raw.FirstOrDefault(entry => entry.Text.Contains("鍦板浘", StringComparison.Ordinal))
                ?? throw new InvalidOperationException("includeLowConfidence=true 应保留二次乱码候选用于诊断。");
            AssertEqual("低", mojibake.DecodeConfidence, "mojibake candidate confidence");
            AssertTrue(!mojibake.IsWritable, "低置信度兼容扫描候选应只读");
        }
        finally
        {
            try
            {
                Directory.Delete(smokeRoot, recursive: true);
            }
            catch
            {
                // Temp cleanup is best effort.
            }
        }
    }

    private static void VerifyRsPreviewSourceAudit(string workspaceRoot)
    {
        var files = new[]
        {
            Path.Combine(workspaceRoot, "工具整合包", "CCZModStudio", "MainForm.UnsavedChanges.cs"),
            Path.Combine(workspaceRoot, "工具整合包", "CCZModStudio", "MainForm.Battlefield.cs"),
            Path.Combine(workspaceRoot, "工具整合包", "CCZModStudio", "Core", "ProjectDetector.cs"),
            Path.Combine(workspaceRoot, "工具整合包", "CCZModStudio", "MainForm.RScene.cs"),
            Path.Combine(workspaceRoot, "工具整合包", "CCZModStudio", "MainForm.Scripts.cs"),
            Path.Combine(workspaceRoot, "工具整合包", "CCZModStudio", "LegacyScenarioCommandDisplayFormatter.cs")
        };

        foreach (var file in files)
        {
            if (!File.Exists(file))
            {
                throw new FileNotFoundException("源码乱码审计找不到目标文件。", file);
            }

            var text = File.ReadAllText(file, Encoding.UTF8);
            AssertNoMojibake([text], Path.GetFileName(file));
        }
    }

    private static string ResolveRsScenarioPath(CczProject project, string fileName)
    {
        var path = Path.Combine(project.GameRoot, "RS", fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("R/S 乱码烟测找不到目标脚本。", path);
        }

        return path;
    }

    private static void WriteGbkNullTerminated(Stream stream, string text)
    {
        var bytes = EncodingService.Gbk.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
        stream.WriteByte(0);
    }

    private static void AssertContainsText(IEnumerable<string> values, string expected, string description)
    {
        if (!values.Any(value => value?.Contains(expected, StringComparison.Ordinal) == true))
        {
            throw new InvalidOperationException($"{description} 未包含期望文本：{expected}");
        }
    }

    private static void AssertNoMojibake(IEnumerable<string?> values, string description)
    {
        var hit = values
            .Where(value => !string.IsNullOrEmpty(value))
            .Select(value => new
            {
                Text = value!,
                Marker = RsMojibakeAuditMarkers.FirstOrDefault(marker => value!.Contains(marker, StringComparison.Ordinal))
            })
            .FirstOrDefault(x => !string.IsNullOrEmpty(x.Marker));
        if (hit != null)
        {
            var sample = hit.Text.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
            if (sample.Length > 160)
            {
                sample = sample[..160] + "...";
            }

            throw new InvalidOperationException($"{description} 包含疑似乱码标记 {hit.Marker}：{sample}");
        }
    }
}
