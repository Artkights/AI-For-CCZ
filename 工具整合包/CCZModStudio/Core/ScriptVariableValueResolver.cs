using System.Globalization;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class ScriptVariableValueResolver
{
    public ScriptVariableValueSnapshot BuildSnapshotToCommand(
        LegacyScenarioDocument document,
        LegacyScenarioCommandNode? currentCommand = null,
        IEnumerable<LegacyScenarioDocument>? precedingDocuments = null)
    {
        var state = new ScriptVariableValueState();
        if (precedingDocuments != null)
        {
            foreach (var preceding in precedingDocuments)
            {
                if (ReferenceEquals(preceding, document))
                {
                    continue;
                }

                ApplyDocument(preceding, state, null);
            }
        }

        ApplyDocument(document, state, currentCommand);
        return state.ToSnapshot();
    }

    public ScriptVariableValueSnapshot BuildSnapshotToCommand(
        IEnumerable<LegacyScenarioDocument> documents,
        LegacyScenarioDocument currentDocument,
        LegacyScenarioCommandNode? currentCommand = null)
    {
        var state = new ScriptVariableValueState();
        foreach (var document in documents)
        {
            ApplyDocument(document, state, ReferenceEquals(document, currentDocument) ? currentCommand : null);
            if (ReferenceEquals(document, currentDocument))
            {
                break;
            }
        }

        return state.ToSnapshot();
    }

    public IReadOnlyList<LegacyScenarioDocument> ReadPrecedingProjectDocuments(
        CczProject project,
        SceneStringDocument dictionary,
        string currentScenarioFileName,
        LegacyScenarioDocument? currentDocument = null)
    {
        var files = new ScenarioFileReader()
            .ReadAllIndex(project)
            .Where(file => ScenarioFileReader.IsRsScriptFile(file.FileName))
            .OrderBy(file => int.TryParse(file.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : int.MaxValue)
            .ThenBy(file => ScenarioFileReader.IsBattlefieldScriptFile(file.FileName) ? 1 : 0)
            .ThenBy(file => file.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var result = new List<LegacyScenarioDocument>();
        var reader = new LegacyScenarioReader();
        foreach (var file in files)
        {
            if (file.FileName.Equals(currentScenarioFileName, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            try
            {
                result.Add(reader.Read(file.Path, dictionary));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                // Variable context is best-effort; a malformed preceding script must not block the current preview.
            }
        }

        return result;
    }

    public static bool TryDecodePerson2VariableReference(int code, out int variableAddress)
    {
        variableAddress = -1;
        if (code >= 0 || code == -1)
        {
            return false;
        }

        var listIndex = Person2CodeToList(code);
        if (listIndex < 1278 || listIndex > 5373)
        {
            return false;
        }

        variableAddress = listIndex - 1278;
        return true;
    }

    public static string FormatPerson2Reference(int code)
        => TryDecodePerson2VariableReference(code, out var variableAddress)
            ? "V" + variableAddress.ToString(CultureInfo.InvariantCulture)
            : code.ToString(CultureInfo.InvariantCulture);

    public static int EncodePerson2VariableReference(int variableAddress)
        => 1276 - (1278 + Math.Clamp(variableAddress, 0, 4095));

    public static bool TryResolvePerson2Reference(
        int code,
        ScriptVariableValueSnapshot? snapshot,
        out int personId,
        out int? variableAddress)
    {
        variableAddress = null;
        if (code is >= 0 and <= 1023)
        {
            personId = code;
            return true;
        }

        if (TryDecodePerson2VariableReference(code, out var address))
        {
            variableAddress = address;
            if (snapshot != null && snapshot.TryGetInteger(address, out var value) && value is >= 0 and <= 1023)
            {
                personId = value;
                return true;
            }
        }

        personId = -1;
        return false;
    }

    private static void ApplyDocument(
        LegacyScenarioDocument document,
        ScriptVariableValueState state,
        LegacyScenarioCommandNode? stopCommand)
    {
        foreach (var command in document.EnumerateCommands())
        {
            ApplyCommand(command, state);
            if (stopCommand != null && ReferenceEquals(command, stopCommand))
            {
                break;
            }
        }
    }

    private static void ApplyCommand(LegacyScenarioCommandNode command, ScriptVariableValueState state)
    {
        var values = GetScalarValues(command);
        switch (command.CommandId)
        {
            case 0x0B when values.Count >= 2:
                state.SetBoolean(values[0], values[1] != 0);
                break;
            case 0x77 when values.Count >= 5:
                ApplyVariableOperation(values, state);
                break;
        }
    }

    private static void ApplyVariableOperation(IReadOnlyList<int> values, ScriptVariableValueState state)
    {
        var leftKind = values[0];
        var leftAddress = values[1];
        var operation = values[2];
        var rightKind = values[3];
        var rightValue = values[4];
        if (!IsIntegerTargetKind(leftKind))
        {
            return;
        }

        if (!TryResolveSourceValue(rightKind, rightValue, state, out var resolvedRight))
        {
            return;
        }

        var current = state.TryGetInteger(leftAddress, out var existing) ? existing : 0;
        var next = operation switch
        {
            0 => current + resolvedRight,
            1 => current - resolvedRight,
            2 => resolvedRight,
            3 => current * resolvedRight,
            4 => resolvedRight == 0 ? (int?)null : current / resolvedRight,
            5 => resolvedRight == 0 ? (int?)null : current % resolvedRight,
            6 => resolvedRight,
            _ => null
        };
        if (next.HasValue)
        {
            state.SetInteger(leftAddress, next.Value);
        }
    }

    private static bool TryResolveSourceValue(
        int kind,
        int value,
        ScriptVariableValueState state,
        out int resolved)
    {
        if (kind == 0)
        {
            resolved = value;
            return true;
        }

        if (IsIntegerSourceKind(kind) && state.TryGetInteger(value, out resolved))
        {
            return true;
        }

        resolved = 0;
        return false;
    }

    private static bool IsIntegerTargetKind(int kind)
        => kind == 2;

    private static bool IsIntegerSourceKind(int kind)
        => kind is 4 or 5;

    private static IReadOnlyList<int> GetScalarValues(LegacyScenarioCommandNode command)
        => command.Parameters
            .Where(parameter => parameter.Kind is not LegacyScenarioParameterKind.Text and not LegacyScenarioParameterKind.VariableArray)
            .Select(parameter => parameter.IntValue)
            .ToList();

    private static int Person2CodeToList(int value)
    {
        if (value >= 0) return value;
        return value == -1 ? 5374 : 1276 - value;
    }
}

public sealed class ScriptVariableValueSnapshot
{
    private readonly IReadOnlyDictionary<int, int> _integers;
    private readonly IReadOnlyDictionary<int, bool> _booleans;

    internal ScriptVariableValueSnapshot(
        IReadOnlyDictionary<int, int> integers,
        IReadOnlyDictionary<int, bool> booleans)
    {
        _integers = integers;
        _booleans = booleans;
    }

    public bool TryGetInteger(int address, out int value)
        => _integers.TryGetValue(address, out value);

    public bool TryGetBoolean(int address, out bool value)
        => _booleans.TryGetValue(address, out value);
}

internal sealed class ScriptVariableValueState
{
    private readonly Dictionary<int, int> _integers = new();
    private readonly Dictionary<int, bool> _booleans = new();

    public void SetInteger(int address, int value)
        => _integers[address] = value;

    public bool TryGetInteger(int address, out int value)
        => _integers.TryGetValue(address, out value);

    public void SetBoolean(int address, bool value)
        => _booleans[address] = value;

    public ScriptVariableValueSnapshot ToSnapshot()
        => new(new Dictionary<int, int>(_integers), new Dictionary<int, bool>(_booleans));
}
