using System.Globalization;
using System.Text.RegularExpressions;
using CCZModStudio.Core;

namespace CCZModStudio;

internal static partial class LegacyMfcDialogResourceLoader
{
    public static IReadOnlyDictionary<string, LegacyMfcDialogSpec> LoadFromWorkspace()
    {
        var path = FindLegacyResourceScript();
        return string.IsNullOrEmpty(path) ? new Dictionary<string, LegacyMfcDialogSpec>(StringComparer.Ordinal) : Load(path);
    }

    public static IReadOnlyDictionary<int, string> LoadDialog114TemplatesFromWorkspace()
    {
        var path = FindLegacyViewSource();
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new Dictionary<int, string>();

        try
        {
            var source = File.ReadAllText(path, System.Text.Encoding.UTF8);
            var function = Dialog114TemplateFunctionRegex().Match(source);
            if (!function.Success) return new Dictionary<int, string>();

            var result = new Dictionary<int, string>();
            foreach (Match match in Dialog114TemplateRegex().Matches(function.Groups["body"].Value))
            {
                var index = int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture);
                result[index] = DecodeCString(match.Groups["text"].Value);
            }

            return result;
        }
        catch
        {
            return new Dictionary<int, string>();
        }
    }

    private static IReadOnlyDictionary<string, LegacyMfcDialogSpec> Load(string path)
    {
        var text = File.ReadAllText(path, System.Text.Encoding.Unicode);
        var result = new Dictionary<string, LegacyMfcDialogSpec>(StringComparer.Ordinal);

        foreach (Match match in DialogRegex().Matches(text))
        {
            var resourceId = match.Groups["id"].Value;
            var number = resourceId["IDD_DIALOG".Length..];
            var dialogName = "Dialog_" + number;
            var spec = new LegacyMfcDialogSpec
            {
                DialogName = dialogName,
                ResourceId = resourceId,
                DialogUnits = new Size(
                    int.Parse(match.Groups["w"].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture))
            };

            foreach (var control in ParseControls(match.Groups["body"].Value))
            {
                spec.Controls.Add(control);
            }

            result[dialogName] = spec;
        }

        ApplyDialogInit(text, result);
        return result;
    }

    private static IEnumerable<LegacyMfcControlSpec> ParseControls(string body)
    {
        var generatedLabelIndex = 0;
        foreach (var rawLine in body.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            var control = ParseButton(line)
                          ?? ParseLabel(line, ref generatedLabelIndex)
                          ?? ParseEdit(line)
                          ?? ParseCombo(line)
                          ?? ParseList(line)
                          ?? ParseControl(line);
            if (control != null)
            {
                yield return control;
            }
        }
    }

    private static LegacyMfcControlSpec? ParseButton(string line)
    {
        var match = ButtonRegex().Match(line);
        if (!match.Success) return null;
        return new LegacyMfcControlSpec
        {
            Kind = LegacyMfcControlKind.Button,
            Id = match.Groups["id"].Value.Trim(),
            Text = Unescape(match.Groups["text"].Value),
            DialogUnits = ParseRect(match.Groups["rect"].Value)
        };
    }

    private static LegacyMfcControlSpec? ParseLabel(string line, ref int generatedLabelIndex)
    {
        var match = LabelRegex().Match(line);
        if (!match.Success) return null;
        var id = match.Groups["id"].Value.Trim();
        if (id is "-1" or "IDC_STATIC")
        {
            id = "__label" + (++generatedLabelIndex).ToString(CultureInfo.InvariantCulture);
        }

        return new LegacyMfcControlSpec
        {
            Kind = LegacyMfcControlKind.Label,
            Id = id,
            Text = Unescape(match.Groups["text"].Value),
            DialogUnits = ParseRect(match.Groups["rect"].Value)
        };
    }

    private static LegacyMfcControlSpec? ParseEdit(string line)
    {
        var match = EditRegex().Match(line);
        if (!match.Success) return null;
        var raw = match.Groups["rest"].Value;
        return new LegacyMfcControlSpec
        {
            Kind = LegacyMfcControlKind.TextBox,
            Id = match.Groups["id"].Value.Trim(),
            DialogUnits = ParseRect(raw),
            Multiline = raw.Contains("ES_MULTILINE", StringComparison.Ordinal),
            Scrollable = raw.Contains("WS_VSCROLL", StringComparison.Ordinal)
        };
    }

    private static LegacyMfcControlSpec? ParseCombo(string line)
    {
        var match = ComboRegex().Match(line);
        if (!match.Success) return null;
        var raw = match.Groups["rest"].Value;
        return new LegacyMfcControlSpec
        {
            Kind = LegacyMfcControlKind.ComboBox,
            Id = match.Groups["id"].Value.Trim(),
            DialogUnits = ParseRect(raw),
            Sorted = raw.Contains("CBS_SORT", StringComparison.Ordinal)
        };
    }

    private static LegacyMfcControlSpec? ParseList(string line)
    {
        var match = ListRegex().Match(line);
        if (!match.Success) return null;
        return new LegacyMfcControlSpec
        {
            Kind = LegacyMfcControlKind.ListBox,
            Id = match.Groups["id"].Value.Trim(),
            DialogUnits = ParseRect(match.Groups["rest"].Value)
        };
    }

    private static LegacyMfcControlSpec? ParseControl(string line)
    {
        var match = ControlRegex().Match(line);
        if (!match.Success) return null;

        var className = match.Groups["class"].Value;
        var style = match.Groups["style"].Value;
        var kind = className.Equals("Button", StringComparison.OrdinalIgnoreCase) &&
                   style.Contains("BS_AUTOCHECKBOX", StringComparison.Ordinal)
            ? LegacyMfcControlKind.CheckBox
            : LegacyMfcControlKind.Label;

        return new LegacyMfcControlSpec
        {
            Kind = kind,
            Id = match.Groups["id"].Value.Trim(),
            Text = Unescape(match.Groups["text"].Value),
            DialogUnits = ParseRect(match.Groups["rect"].Value)
        };
    }

    private static void ApplyDialogInit(string text, IDictionary<string, LegacyMfcDialogSpec> specs)
    {
        foreach (Match match in DialogInitRegex().Matches(text))
        {
            var resourceId = match.Groups["id"].Value;
            var dialogName = "Dialog_" + resourceId["IDD_DIALOG".Length..];
            if (!specs.TryGetValue(dialogName, out var spec)) continue;

            var itemsByControl = DecodeDialogInitItems(match.Groups["body"].Value);
            if (itemsByControl.Count == 0) continue;

            var previousInitialize = spec.Initialize;
            var enriched = new LegacyMfcDialogSpec
            {
                DialogName = spec.DialogName,
                ResourceId = spec.ResourceId,
                DialogUnits = spec.DialogUnits,
                Initialize = session =>
                {
                    foreach (var pair in itemsByControl)
                    {
                        session.AddComboItems(pair.Key, pair.Value);
                    }

                    previousInitialize?.Invoke(session);
                },
                Commit = spec.Commit
            };
            enriched.Controls.AddRange(spec.Controls);
            specs[dialogName] = enriched;
        }
    }

    private static Dictionary<string, List<string>> DecodeDialogInitItems(string body)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (Match match in DialogInitItemRegex().Matches(body))
        {
            var id = match.Groups["id"].Value;
            var bytes = new List<byte>();
            foreach (Match hex in HexWordRegex().Matches(match.Groups["words"].Value))
            {
                var value = Convert.ToUInt16(hex.Groups["hex"].Value, 16);
                bytes.Add((byte)(value & 0xFF));
                bytes.Add((byte)(value >> 8));
            }

            while (bytes.Count > 0 && bytes[^1] == 0)
            {
                bytes.RemoveAt(bytes.Count - 1);
            }

            if (bytes.Count == 0) continue;
            var valueText = EncodingService.Gbk.GetString(bytes.ToArray());
            if (!result.TryGetValue(id, out var items))
            {
                items = [];
                result[id] = items;
            }
            items.Add(valueText);
        }

        return result;
    }

    private static Rectangle ParseRect(string text)
    {
        var values = NumberRegex().Matches(text)
            .Select(match => int.Parse(match.Value, CultureInfo.InvariantCulture))
            .Take(4)
            .ToArray();
        return values.Length >= 4 ? new Rectangle(values[0], values[1], values[2], values[3]) : Rectangle.Empty;
    }

    private static string Unescape(string text)
        => text.Replace("\"\"", "\"", StringComparison.Ordinal);

    private static string FindLegacyResourceScript()
    {
        var candidates = new List<string>();
        AddCandidateRoots(candidates, AppContext.BaseDirectory);
        AddCandidateRoots(candidates, Environment.CurrentDirectory);

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        return string.Empty;
    }

    private static string FindLegacyViewSource()
    {
        var candidates = new List<string>();
        AddCandidateRoots(candidates, AppContext.BaseDirectory, "cczEditor2View.cpp");
        AddCandidateRoots(candidates, Environment.CurrentDirectory, "cczEditor2View.cpp");

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        return string.Empty;
    }

    private static void AddCandidateRoots(List<string> candidates, string start)
        => AddCandidateRoots(candidates, start, "cczEditor2.rc");

    private static void AddCandidateRoots(List<string> candidates, string start, string fileName)
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            candidates.Add(Path.Combine(directory.FullName, "LegacyResources", "a新剧本编辑器v0.23", "cczEditor2", fileName));
            candidates.Add(Path.Combine(directory.FullName, "工具整合包", "CCZModStudio", "Assets", "LegacyResources", "a新剧本编辑器v0.23", "cczEditor2", fileName));
            candidates.Add(Path.Combine(directory.FullName, "老版游戏制作工具", "a新剧本编辑器v0.23", "ccz-SceneEditor-main", "cczEditor2", fileName));
            candidates.Add(Path.Combine(directory.FullName, "..", "老版游戏制作工具", "a新剧本编辑器v0.23", "ccz-SceneEditor-main", "cczEditor2", fileName));
            directory = directory.Parent;
        }
    }

    private static string DecodeCString(string text)
        => text
            .Replace("\\r", "\r", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\t", "\t", StringComparison.Ordinal)
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);

    [GeneratedRegex(@"^(?<id>IDD_DIALOG\d+)\s+DIALOGEX\s+0,\s*0,\s*(?<w>\d+),\s*(?<h>\d+).*?^BEGIN\s*\r?\n(?<body>.*?)^END", RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex DialogRegex();

    [GeneratedRegex(@"^(DEFPUSHBUTTON|PUSHBUTTON)\s+""(?<text>(?:[^""]|"""")*)"",\s*(?<id>[^,]+),\s*(?<rect>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex ButtonRegex();

    [GeneratedRegex(@"^(LTEXT|CTEXT|RTEXT)\s+""(?<text>(?:[^""]|"""")*)"",\s*(?<id>[^,]+),\s*(?<rect>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex LabelRegex();

    [GeneratedRegex(@"^EDITTEXT\s+(?<id>[^,]+),\s*(?<rest>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex EditRegex();

    [GeneratedRegex(@"^COMBOBOX\s+(?<id>[^,]+),\s*(?<rest>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex ComboRegex();

    [GeneratedRegex(@"^LISTBOX\s+(?<id>[^,]+),\s*(?<rest>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex ListRegex();

    [GeneratedRegex(@"^CONTROL\s+""(?<text>(?:[^""]|"""")*)"",\s*(?<id>[^,]+),\s*""(?<class>[^""]+)"",\s*(?<style>[^,]+),\s*(?<rect>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex ControlRegex();

    [GeneratedRegex(@"^(?<id>IDD_DIALOG\d+)\s+DLGINIT\s*\r?\nBEGIN\s*\r?\n(?<body>.*?)^END", RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex DialogInitRegex();

    [GeneratedRegex(@"(?<id>IDC_\w+),\s*0x403,\s*\d+,\s*0\s*\r?\n(?<words>.*?)(?=\r?\n\s*(?:IDC_\w+,\s*0x403|0\s*$))", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex DialogInitItemRegex();

    [GeneratedRegex(@"0x(?<hex>[0-9A-Fa-f]+)", RegexOptions.CultureInvariant)]
    private static partial Regex HexWordRegex();

    [GeneratedRegex(@"(?<![A-Z_])-?\d+", RegexOptions.CultureInvariant)]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"void\s+Dialog_114::OnBnClickedButton1\(\)\s*\{(?<body>.*?)^\}", RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Dialog114TemplateFunctionRegex();

    [GeneratedRegex(@"(?:if|else\s+if)\s*\(p\s*==\s*(?<index>\d+)\)\s*edit2\.SetWindowTextW\(L""(?<text>(?:\\.|[^""])*)""\);", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Dialog114TemplateRegex();
}
