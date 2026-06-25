using CCZModStudio.Core;
using CCZModStudio.Models;

internal partial class Program
{
    static void RunAccessoryJobGroupSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var sampleBytes = new byte[] { 0xFF, 0x00, 0x01, 0x02, 0xFF, 0x03, 0x04, 0x05, 0xFF };
        var parsed = AccessoryJobGroupService.ParseForTest(sampleBytes);
        if (parsed.Count != 2 ||
            !parsed[0].SequenceEqual(new[] { 0, 1, 2 }) ||
            !parsed[1].SequenceEqual(new[] { 3, 4, 5 }))
        {
            throw new InvalidOperationException("辅助装备多兵种分组解析样例失败。");
        }

        var serialized = AccessoryJobGroupService.SerializeForTest(parsed);
        if (!serialized.SequenceEqual(sampleBytes))
        {
            throw new InvalidOperationException("辅助装备多兵种分组样例未能无损序列化。");
        }

        ExpectAccessoryGroupError(() => AccessoryJobGroupService.ParseForTest(new byte[] { 0, 1, 0xFF }), "开头");
        ExpectAccessoryGroupError(() => AccessoryJobGroupService.ParseForTest(new byte[] { 0xFF, 0, 1 }), "结尾");
        ExpectAccessoryGroupError(() => AccessoryJobGroupService.ParseForTest(new byte[] { 0xFF, 0xFF, 1, 0xFF }), "空分组");
        ExpectAccessoryGroupError(() => AccessoryJobGroupService.SerializeForTest(new[] { (IReadOnlyList<int>)new[] { 1, 1 } }), "重复");
        ExpectAccessoryGroupError(() => AccessoryJobGroupService.SerializeForTest(new[] { (IReadOnlyList<int>)new[] { 40 } }), "越界");

        var service = new AccessoryJobGroupService();
        var profile = service.Read(project, tables);
        if (profile.FileOffset <= 0 ||
            profile.WritableLength <= 0 ||
            profile.Groups.Count == 0 ||
            profile.Diagnostics.Any(line => line.StartsWith("错误：", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"辅助装备多兵种分组只读烟测失败：offset={profile.FileOffsetHex}, writable={profile.WritableLength}, groups={profile.Groups.Count}, diagnostics={string.Join("|", profile.Diagnostics)}");
        }

        var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "AccessoryJobGroupSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(smokeRoot);
        foreach (var coreFile in new[] { "Ekd5.exe", "Data.e5", "Star.e5", "Imsg.e5" })
        {
            var source = Path.Combine(project.GameRoot, coreFile);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("辅助装备多兵种分组写入烟测缺少核心文件。", source);
            }

            File.Copy(source, Path.Combine(smokeRoot, coreFile), overwrite: false);
        }

        File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=Accessory job group write smoke\r\n");
        var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
        var testProfile = service.Read(testProject, tables);
        var groups = new[]
        {
            (IReadOnlyList<int>)new[] { 0, 1, 2 },
            new[] { 3, 4, 5 }
        };
        var preview = service.Preview(testProject, tables, groups);
        if (!preview.CanWrite || preview.PayloadBytesHex != "FF 00 01 02 FF 03 04 05 FF")
        {
            throw new InvalidOperationException("辅助装备多兵种分组写入预览不符合预期：" + preview.PayloadBytesHex);
        }

        var save = service.Save(testProject, tables, groups);
        if (!File.Exists(save.BackupPath) || !File.Exists(save.ReportJsonPath) || save.BytesWritten != testProfile.WritableLength)
        {
            throw new InvalidOperationException("辅助装备多兵种分组写入没有生成预期备份/报告。");
        }

        var verify = service.Read(testProject, tables);
        if (verify.Groups.Count != 2 ||
            !verify.Groups[0].JobSeriesIds.SequenceEqual(new[] { 0, 1, 2 }) ||
            !verify.Groups[1].JobSeriesIds.SequenceEqual(new[] { 3, 4, 5 }))
        {
            throw new InvalidOperationException("辅助装备多兵种分组写入后复读失败。");
        }

        var beforeOverflowHash = WriteOperationReportService.ComputeSha256(testProject.ResolveGameFile("Ekd5.exe"));
        var overflow = Enumerable.Range(0, testProfile.WritableLength + 2)
            .Select(i => (IReadOnlyList<int>)new[] { i % (AccessoryJobGroupService.MaxJobSeriesId + 1) })
            .ToArray();
        var overflowPreview = service.Preview(testProject, tables, overflow);
        if (overflowPreview.CanWrite)
        {
            throw new InvalidOperationException("辅助装备多兵种分组超长预览未拒绝。");
        }

        var afterOverflowHash = WriteOperationReportService.ComputeSha256(testProject.ResolveGameFile("Ekd5.exe"));
        if (!string.Equals(beforeOverflowHash, afterOverflowHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("辅助装备多兵种分组超长预览不应改变文件。");
        }

        Console.WriteLine($"ACCESSORY_JOB_GROUP_SMOKE_OK offset={profile.FileOffsetHex} writable={profile.WritableLength} groups={profile.Groups.Count} testRoot={smokeRoot}");
    }

    private static void ExpectAccessoryGroupError(Action action, string expectedText)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex.Message.Contains(expectedText, StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException("辅助装备多兵种分组错误用例未按预期失败：" + expectedText);
    }
}
