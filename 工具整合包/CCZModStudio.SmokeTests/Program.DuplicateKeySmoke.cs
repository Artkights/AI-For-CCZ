using CCZModStudio.Core;
using CCZModStudio.Formats;

internal partial class Program
{
    static void RunDuplicateKeySmoke()
    {
        RunDictionaryBuildDuplicateSmoke();
        RunSceneStringDuplicateCommandSmoke();
        Console.WriteLine("DUPLICATE_KEY_SMOKE_OK");
    }

    private static void RunDictionaryBuildDuplicateSmoke()
    {
        var values = new[]
        {
            new DuplicateSmokeRow(0, "first"),
            new DuplicateSmokeRow(1, "one"),
            new DuplicateSmokeRow(0, "last")
        };

        var first = values.ToDictionaryFirstByKey(row => row.Key, row => row.Value);
        var last = values.ToDictionaryLastByKey(row => row.Key, row => row.Value);
        var withDiagnostics = values.ToDictionaryWithDiagnostics(
            row => row.Key,
            row => row.Value,
            DuplicateKeyResolution.First,
            out var duplicates);

        if (first[0] != "first") throw new InvalidOperationException("First duplicate policy failed.");
        if (last[0] != "last") throw new InvalidOperationException("Last duplicate policy failed.");
        if (withDiagnostics[0] != "first") throw new InvalidOperationException("Diagnostic duplicate policy failed.");
        if (duplicates.Count != 1 || duplicates[0].Key != 0 || duplicates[0].Count != 2)
        {
            throw new InvalidOperationException("Duplicate diagnostics failed.");
        }
    }

    private static void RunSceneStringDuplicateCommandSmoke()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "CCZModStudio_DuplicateKeySmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var path = Path.Combine(tempRoot, "CczString.ini");
            File.WriteAllText(path, "00:FirstZero,00:SecondZero,46:AllyDeploy\r\nA,B,C\r\n");

            var document = new SceneStringParser().Parse(path);
            if (document.Commands.Count(command => command.Id == 0) != 1)
            {
                throw new InvalidOperationException("Scene command duplicate ID was not deduplicated.");
            }

            if (document.Commands.First(command => command.Id == 0).Name != "FirstZero")
            {
                throw new InvalidOperationException("Scene command duplicate ID did not keep first definition.");
            }

            if (!document.DecodeWarnings.Any(warning => warning.Contains("duplicated", StringComparison.OrdinalIgnoreCase) && warning.Contains("00", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("Scene command duplicate warning missing.");
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private sealed record DuplicateSmokeRow(int Key, string Value);
}
