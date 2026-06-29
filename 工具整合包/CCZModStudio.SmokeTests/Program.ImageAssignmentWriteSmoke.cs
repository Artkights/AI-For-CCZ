using CCZModStudio;
using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.Data;
using System.Globalization;

internal partial class Program
{
    static void RunImageAssignmentWriteSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var sourceEkd5 = Path.Combine(project.GameRoot, "Ekd5.exe");
        if (!File.Exists(sourceEkd5))
        {
            throw new FileNotFoundException("人物形象设定写回烟测缺少 Ekd5.exe。", sourceEkd5);
        }

        var smokeRoot = Path.Combine(
            project.WorkspaceRoot,
            "CCZModStudio_TestCopies",
            "ImageAssignmentWriteSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(smokeRoot);

        foreach (var coreFile in new[] { "Ekd5.exe", "Data.e5", "Star.e5", "Imsg.e5", "Hexzmap.e5" })
        {
            var source = Path.Combine(project.GameRoot, coreFile);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(smokeRoot, coreFile), overwrite: false);
            }
        }

        File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=Image assignment write smoke\r\n");

        var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
        var service = new ImageAssignmentService();
        var data = service.Load(testProject, tables);
        if (data.Rows.Count == 0)
        {
            throw new InvalidOperationException("人物形象设定写回烟测没有读取到任何人物行。");
        }

        AssertImageAssignmentEditableColumns(data);

        const int rowIndex = 0;
        var originalFace = ReadImageAssignmentInt(data, rowIndex, "头像编号");
        var originalR = ReadImageAssignmentInt(data, rowIndex, "R形象编号");
        var originalS = ReadImageAssignmentInt(data, rowIndex, "S形象编号");

        var faceOnly = ToggleOneTwo(originalFace);
        data.Rows[rowIndex]["头像编号"] = faceOnly;
        AssertImageAssignmentSave(service.SaveToTestCopy(testProject, tables, data), expectedSaves: 1, "头像编号独立写回");
        data = service.Load(testProject, tables);
        AssertImageAssignmentValue(data, rowIndex, "头像编号", faceOnly, "头像编号独立写回复读");
        AssertImageAssignmentValue(data, rowIndex, "R形象编号", originalR, "头像编号独立写回不应改动 R");
        AssertImageAssignmentValue(data, rowIndex, "S形象编号", originalS, "头像编号独立写回不应改动 S");

        var rOnly = ToggleZeroOne(originalR);
        data.Rows[rowIndex]["R形象编号"] = rOnly;
        AssertImageAssignmentSave(service.SaveToTestCopy(testProject, tables, data), expectedSaves: 1, "R形象编号独立写回");
        data = service.Load(testProject, tables);
        AssertImageAssignmentValue(data, rowIndex, "头像编号", faceOnly, "R形象编号独立写回不应改动头像");
        AssertImageAssignmentValue(data, rowIndex, "R形象编号", rOnly, "R形象编号独立写回复读");
        AssertImageAssignmentValue(data, rowIndex, "S形象编号", originalS, "R形象编号独立写回不应改动 S");

        var sOnly = ToggleZeroOne(originalS);
        data.Rows[rowIndex]["S形象编号"] = sOnly;
        AssertImageAssignmentSave(service.SaveToTestCopy(testProject, tables, data), expectedSaves: 1, "S形象编号独立写回");
        data = service.Load(testProject, tables);
        AssertImageAssignmentValue(data, rowIndex, "头像编号", faceOnly, "S形象编号独立写回不应改动头像");
        AssertImageAssignmentValue(data, rowIndex, "R形象编号", rOnly, "S形象编号独立写回不应改动 R");
        AssertImageAssignmentValue(data, rowIndex, "S形象编号", sOnly, "S形象编号独立写回复读");

        var faceTogether = ToggleOneTwo(faceOnly);
        var rTogether = ToggleZeroOne(rOnly);
        var sTogether = ToggleZeroOne(sOnly);
        data.Rows[rowIndex]["头像编号"] = faceTogether;
        data.Rows[rowIndex]["R形象编号"] = rTogether;
        data.Rows[rowIndex]["S形象编号"] = sTogether;
        AssertImageAssignmentSave(service.SaveToTestCopy(testProject, tables, data), expectedSaves: 3, "头像/R/S 同时写回");
        data = service.Load(testProject, tables);
        AssertImageAssignmentValue(data, rowIndex, "头像编号", faceTogether, "头像/R/S 同时写回复读头像");
        AssertImageAssignmentValue(data, rowIndex, "R形象编号", rTogether, "头像/R/S 同时写回复读 R");
        AssertImageAssignmentValue(data, rowIndex, "S形象编号", sTogether, "头像/R/S 同时写回复读 S");

        Console.WriteLine($"IMAGE_ASSIGNMENT_WRITE_SMOKE_OK root={smokeRoot} row={rowIndex} Face={originalFace}->{faceOnly}->{faceTogether} R={originalR}->{rOnly}->{rTogether} S={originalS}->{sOnly}->{sTogether}");
    }

    private static void AssertImageAssignmentEditableColumns(DataTable data)
    {
        foreach (var columnName in new[] { "头像编号", "R形象编号", "S形象编号" })
        {
            if (!data.Columns.Contains(columnName))
            {
                throw new InvalidOperationException($"人物形象设定写回烟测缺少列：{columnName}");
            }

            if (data.Columns[columnName]!.ReadOnly)
            {
                throw new InvalidOperationException($"人物形象设定列应可编辑：{columnName}");
            }
        }
    }

    private static void AssertImageAssignmentSave(ImageAssignmentSaveResult result, int expectedSaves, string label)
    {
        if (result.Saves.Count != expectedSaves)
        {
            throw new InvalidOperationException($"{label} 保存表数量不符合预期：expected={expectedSaves}, actual={result.Saves.Count}");
        }

        if (result.Saves.Any(save => string.IsNullOrWhiteSpace(save.BackupPath) || !File.Exists(save.BackupPath)))
        {
            throw new InvalidOperationException($"{label} 未生成有效备份。");
        }
    }

    private static void AssertImageAssignmentValue(DataTable data, int rowIndex, string columnName, int expected, string label)
    {
        var actual = ReadImageAssignmentInt(data, rowIndex, columnName);
        if (actual != expected)
        {
            throw new InvalidOperationException($"{label} 失败：{columnName} expected={expected}, actual={actual}");
        }
    }

    private static int ReadImageAssignmentInt(DataTable data, int rowIndex, string columnName)
        => Convert.ToInt32(data.Rows[rowIndex][columnName], CultureInfo.InvariantCulture);

    private static int ToggleOneTwo(int current) => current == 1 ? 2 : 1;

    private static int ToggleZeroOne(int current) => current == 0 ? 1 : 0;
}
