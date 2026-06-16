using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Drawing;
using System.Globalization;

internal partial class Program
{
    static void RunE5RoleRawNormalizeSmoke(CczProject project)
    {
        var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "E5RoleRawNormalizeSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(smokeRoot);
        foreach (var fileName in new[] { "Ekd5.exe", "Data.e5", "Imsg.e5", "Star.e5", "Hexzmap.e5", "Pmapobj.e5", "Unit_mov.e5", "Unit_atk.e5", "Unit_spc.e5" })
        {
            var source = CharacterImageResourceService.ResolveGameFile(project, fileName);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(smokeRoot, fileName), overwrite: false);
            }
        }

        File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=E5 role RAW normalize smoke\r\n");
        var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
        var materialPath = Path.Combine(smokeRoot, "_normalize_source.bmp");
        CreateSmokeBmp(materialPath, 48, 128, Color.FromArgb(255, 80, 200, 230), Color.FromArgb(255, 230, 80, 140));

        var targetPath = CharacterImageResourceService.ResolveGameFile(testProject, "Pmapobj.e5");
        var replace = new E5ImageReplaceService();
        var seeded = replace.Replace(testProject, targetPath, imageNumber: 3, sourcePath: materialPath);
        if (!seeded.NewKind.Equals("BMP", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("E5 role RAW normalize seed did not create a BMP entry.");
        }

        var service = new E5RoleRawNormalizeService();
        var preview = service.Preview(testProject);
        var targetPreview = preview.Files
            .FirstOrDefault(file => file.TargetFileName.Equals("Pmapobj.e5", StringComparison.OrdinalIgnoreCase))?
            .Entries.FirstOrDefault(entry => entry.ImageNumber == 3);
        if (targetPreview == null ||
            !targetPreview.Status.Equals("convert", StringComparison.OrdinalIgnoreCase) ||
            targetPreview.Encode?.RawLength != 48 * 128 ||
            preview.ConvertCount < 1)
        {
            throw new InvalidOperationException("E5 role RAW normalize preview did not mark seeded Pmapobj.e5 #3 for conversion.");
        }

        var result = service.Normalize(testProject);
        if (result.ConvertCount < 1 ||
            string.IsNullOrWhiteSpace(result.AggregateReportPath) ||
            !File.Exists(result.AggregateReportPath))
        {
            throw new InvalidOperationException("E5 role RAW normalize did not convert entries or write an aggregate report.");
        }

        VerifyRawEntry(replace, targetPath, 3, 48 * 128);
        var pmapResult = result.Files.FirstOrDefault(file => file.TargetFileName.Equals("Pmapobj.e5", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("E5 role RAW normalize result lost Pmapobj.e5.");
        if (pmapResult.WriteResult == null ||
            string.IsNullOrWhiteSpace(pmapResult.WriteResult.BackupPath) ||
            !File.Exists(pmapResult.WriteResult.BackupPath) ||
            string.IsNullOrWhiteSpace(pmapResult.WriteResult.ReportJsonPath) ||
            !File.Exists(pmapResult.WriteResult.ReportJsonPath))
        {
            throw new InvalidOperationException("E5 role RAW normalize did not produce backup/report evidence for Pmapobj.e5.");
        }

        Console.WriteLine($"E5_ROLE_RAW_NORMALIZE_SMOKE OK root={smokeRoot} convert={result.ConvertCount} skip={result.SkipCount} report={Path.GetFileName(result.AggregateReportPath)}");
    }
}
