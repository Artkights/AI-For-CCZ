namespace CCZModStudio.Models;

public sealed class LegacyScenarioDocument
{
    public required string FilePath { get; init; }
    public string FileName => Path.GetFileName(FilePath);
    public IReadOnlyList<int> SceneOffsets { get; init; } = Array.Empty<int>();
    public List<LegacyScenarioScene> Scenes { get; } = [];
    public int SceneCount => Scenes.Count;
    public int SectionCount => Scenes.Sum(scene => scene.Sections.Count);
    public int CommandCount => EnumerateCommands().Count();
    public string Summary => $"{FileName}: Scene={SceneCount}, Section={SectionCount}, Command={CommandCount}";

    public IEnumerable<LegacyScenarioCommandNode> EnumerateCommands()
    {
        foreach (var scene in Scenes)
        {
            foreach (var section in scene.Sections)
            {
                foreach (var command in section.EnumerateCommands())
                {
                    yield return command;
                }
            }
        }
    }
}

public sealed class LegacyScenarioScene
{
    public int SceneIndex { get; set; }
    public int FileOffset { get; init; }
    public List<LegacyScenarioSection> Sections { get; } = [];
}

public sealed class LegacyScenarioSection
{
    public int SceneIndex { get; set; }
    public int SectionIndex { get; set; }
    public int FileOffset { get; init; }
    public int LengthPrefixOffset { get; init; }
    public int DeclaredLength { get; init; }
    public List<LegacyScenarioCommandNode> Commands { get; } = [];

    public IEnumerable<LegacyScenarioCommandNode> EnumerateCommands()
    {
        var activeBlocks = new HashSet<LegacyScenarioCommandBlock>();
        var stack = new Stack<(IReadOnlyList<LegacyScenarioCommandNode> Commands, int Index, LegacyScenarioCommandBlock? Owner)>();
        stack.Push((Commands, 0, null));

        while (stack.Count > 0)
        {
            var frame = stack.Pop();
            if (frame.Index >= frame.Commands.Count)
            {
                if (frame.Owner != null)
                {
                    activeBlocks.Remove(frame.Owner);
                }
                continue;
            }

            var command = frame.Commands[frame.Index];
            frame.Index++;
            stack.Push(frame);

            yield return command;

            var childBlock = command.ChildBlock;
            if (childBlock == null || childBlock.Commands.Count == 0)
            {
                continue;
            }

            if (activeBlocks.Add(childBlock))
            {
                stack.Push((childBlock.Commands, 0, childBlock));
            }
        }
    }
}

public sealed class LegacyScenarioCommandBlock
{
    public string Kind { get; init; } = string.Empty;
    public int LengthPrefixOffset { get; init; }
    public int FileOffset { get; init; }
    public int DeclaredLength { get; init; }
    public List<LegacyScenarioCommandNode> Commands { get; } = [];
}

public sealed class LegacyScenarioCommandNode
{
    public int SceneIndex { get; set; }
    public int SectionIndex { get; set; }
    public int CommandIndex { get; set; }
    public int CommandOrdinal { get; set; }
    public int CommandId { get; init; }
    public string CommandIdHex => "0x" + CommandId.ToString("X2");
    public string CommandName { get; init; } = string.Empty;
    public int FileOffset { get; init; }
    public int ConsumedBytes { get; init; }
    public bool StartsBodyBlock { get; set; }
    public bool IsSubEventMarker { get; set; }
    public bool OpensSubEventBlock { get; set; }
    public bool EndsSubEventBlock { get; set; }
    public int? JumpTargetOrdinal { get; set; }
    public int? JumpTargetCommandIndex { get; set; }
    public int? OriginalJumpDisplacement { get; set; }
    public List<LegacyScenarioCommandParameter> Parameters { get; } = [];
    public LegacyScenarioCommandBlock? ChildBlock { get; set; }

    public IEnumerable<LegacyScenarioCommandParameter> TextParameters
        => Parameters.Where(parameter => parameter.Kind == LegacyScenarioParameterKind.Text);

    public string DisplayText
        => $"{CommandIndex:000} {CommandIdHex} {CommandName}";
}

public sealed class LegacyScenarioCommandParameter
{
    public int Index { get; init; }
    public int LayoutCode { get; init; }
    public int Tag { get; init; }
    public int FileOffset { get; init; }
    public LegacyScenarioParameterKind Kind { get; set; }
    public int IntValue { get; set; }
    public string Text { get; set; } = string.Empty;
    public List<int> Values { get; } = [];
    public int ByteLength { get; set; }

    public string LayoutCodeHex => "0x" + LayoutCode.ToString("X2");
    public string TagHex => "0x" + Tag.ToString("X2");

    public string DisplayValue => Kind switch
    {
        LegacyScenarioParameterKind.Text => Text,
        LegacyScenarioParameterKind.VariableArray => $"{Values.Count}: " + string.Join("/", Values.Take(12)),
        _ => IntValue.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };
}

public enum LegacyScenarioParameterKind
{
    Word16,
    Dword32,
    Text,
    VariableArray
}

public sealed class LegacyScenarioWriteResult
{
    public string FilePath { get; init; } = string.Empty;
    public string BackupPath { get; init; } = string.Empty;
    public string ReportJsonPath { get; init; } = string.Empty;
    public int ChangedBytes { get; init; }
    public int SceneCount { get; init; }
    public int SectionCount { get; init; }
    public int CommandCount { get; init; }
    public string ValidationSummary { get; init; } = string.Empty;
}
