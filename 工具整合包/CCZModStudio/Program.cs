using CCZModStudio.Core;

namespace CCZModStudio;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (TryRunCommandLineTool(args))
        {
            return;
        }

        if (ActiveReleaseStartupService.TryRedirectToActive(args)) return;

        ApplicationErrorService.RegisterWinFormsHandlers();
        ApplicationConfiguration.Initialize();
        try
        {
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            ApplicationErrorService.Report(ex, "Application.Run", notify: false);
        }
    }

    private static bool TryRunCommandLineTool(string[] args)
    {
        if (args.Length == 0) return false;
        if (args[0].Equals("--repair-material-autotile-metadata", StringComparison.OrdinalIgnoreCase))
        {
            return RunAutoTileMetadataRepairTool(args);
        }

        if (!args[0].Equals("--migrate-material-library", StringComparison.OrdinalIgnoreCase)) return false;

        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: CCZModStudio.exe --migrate-material-library <oldRoot> <newRoot> [--hextex <HexTex.txt>] [--overwrite]");
            Environment.ExitCode = 2;
            return true;
        }

        var oldRoot = args[1];
        var newRoot = args[2];
        string? hexTexPath = null;
        var overwrite = false;
        for (var i = 3; i < args.Length; i++)
        {
            if (args[i].Equals("--overwrite", StringComparison.OrdinalIgnoreCase))
            {
                overwrite = true;
            }
            else if (args[i].Equals("--hextex", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                hexTexPath = args[++i];
            }
        }

        try
        {
            var result = new Core.MaterialLibraryMigrationService().Migrate(oldRoot, newRoot, hexTexPath, overwrite);
            Console.WriteLine($"MATERIAL_LIBRARY_MIGRATION_OK terrain={result.TerrainImageCount} building={result.BuildingImageCount} scenery={result.SceneryImageCount} variants={result.GeneratedVariantMetadataCount}");
            Console.WriteLine(result.NewRoot);
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("MATERIAL_LIBRARY_MIGRATION_FAILED");
            Console.Error.WriteLine(ex.Message);
            Environment.ExitCode = 1;
            return true;
        }
    }

    private static bool RunAutoTileMetadataRepairTool(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: CCZModStudio.exe --repair-material-autotile-metadata <materialRootOrGroupDir> [--keep-existing]");
            Environment.ExitCode = 2;
            return true;
        }

        var root = Path.GetFullPath(args[1]);
        var overwrite = !args.Contains("--keep-existing", StringComparer.OrdinalIgnoreCase);
        try
        {
            var service = new Core.MaterialAutoTileMetadataService();
            var groups = EnumerateAutoTileGroupDirectories(root).ToList();
            var variantCount = 0;
            var writtenGroups = 0;
            foreach (var group in groups)
            {
                var written = service.RepairGroupDirectory(group, overwrite);
                if (written <= 0) continue;
                variantCount += written;
                writtenGroups++;
            }

            Console.WriteLine($"MATERIAL_AUTOTILE_METADATA_REPAIR_OK groups={writtenGroups}/{groups.Count} variants={variantCount}");
            Console.WriteLine(root);
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("MATERIAL_AUTOTILE_METADATA_REPAIR_FAILED");
            Console.Error.WriteLine(ex.Message);
            Environment.ExitCode = 1;
            return true;
        }
    }

    private static IEnumerable<string> EnumerateAutoTileGroupDirectories(string root)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);

        if (Directory.GetFiles(root).Any(IsImageFile))
        {
            yield return root;
            yield break;
        }

        var buildingRoot = Path.Combine(root, "建筑");
        if (Directory.Exists(buildingRoot))
        {
            foreach (var group in Directory.GetDirectories(buildingRoot))
            {
                if (Directory.GetFiles(group).Any(IsImageFile))
                {
                    yield return group;
                }
            }

            yield break;
        }

        foreach (var group in Directory.GetDirectories(root))
        {
            if (Directory.GetFiles(group).Any(IsImageFile))
            {
                yield return group;
            }
        }
    }

    private static bool IsImageFile(string path)
        => new[] { ".png", ".jpg", ".jpeg", ".bmp" }
            .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
