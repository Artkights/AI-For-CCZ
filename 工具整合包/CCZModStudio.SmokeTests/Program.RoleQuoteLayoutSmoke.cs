using System.Data;
using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;

internal partial class Program
{
    private static readonly List<string> RoleQuoteLayoutSmokeRoots = [];

    static void RunRoleQuoteLayoutSmoke()
    {
        try
        {
            var workspace = Environment.CurrentDirectory;
            var root65 = Path.Combine(workspace, "基底", "加强版6.5未加密版");
            var root66 = Path.Combine(workspace, "基底", "新改曹操傳6.6修正版");
            var rootQinger = Path.Combine(workspace, "基底", "清儿吕布传");
            foreach (var root in new[] { root65, root66, rootQinger })
            {
                if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Role quote layout smoke baseline missing: " + root);
            }

            var detector = new ProjectDetector();
            var project65 = detector.CreateProjectFromGameRoot(root65);
            var project66 = detector.CreateProjectFromGameRoot(root66);
            var projectQinger = detector.CreateProjectFromGameRoot(rootQinger);
            var layoutService = new RoleQuoteLayoutService();
            AssertRoleQuoteLayout(layoutService.Resolve(project65), "6.5", 140_000, 440_200, false);
            AssertRoleQuoteLayout(layoutService.Resolve(project66), "6.6", 140_000, 491_600, true);
            AssertRoleQuoteLayout(layoutService.Resolve(projectQinger), "6.6", 140_000, 491_600, true);

            AssertGenerated66QuoteTables(project66);
            AssertGenerated66QuoteTables(projectQinger);
            RunRoleQuoteTextWriteRoundTrip(project65, expectedRetreatOffset: 440_200, legacyOffsetMustRemainUnchanged: null);
            RunRoleQuoteTextWriteRoundTrip(project66, expectedRetreatOffset: 491_600, legacyOffsetMustRemainUnchanged: 440_200);
            RunSpecialCriticalWriteRoundTrip(project66);
            AssertDamaged66Layouts(project66);

            Console.WriteLine("ROLE_QUOTE_LAYOUT_SMOKE_OK 6.5=0x6B788 6.6=0x78050 critical=0x222E0 special=0x89C30");
        }
        finally
        {
            foreach (var root in RoleQuoteLayoutSmokeRoots)
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            RoleQuoteLayoutSmokeRoots.Clear();
        }
    }

    private static void AssertRoleQuoteLayout(RoleQuoteLayout layout, string version, long critical, long retreat, bool requireReference)
    {
        if (!layout.Version.Equals(version, StringComparison.OrdinalIgnoreCase) ||
            layout.CriticalText.Offset != critical || layout.RetreatText.Offset != retreat ||
            layout.SpecialCriticalMapping.Offset != RoleQuoteLayoutService.SpecialCriticalMappingOffset)
        {
            throw new InvalidOperationException($"Role quote layout mismatch: {RoleQuoteLayoutService.BuildSummary(layout)}");
        }

        if (!layout.CriticalText.CanWrite || !layout.RetreatText.CanWrite || !layout.SpecialCriticalMapping.CanWrite)
        {
            throw new InvalidOperationException($"Verified baseline was not writable: {RoleQuoteLayoutService.BuildSummary(layout)}");
        }

        if (requireReference && !layout.SpecialCriticalMapping.Evidence.Contains("Verified executable PE reference", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("6.6 special-critical mapping lacks executable-reference evidence.");
        }
    }

    private static void AssertGenerated66QuoteTables(CczProject project)
    {
        var tables = new Ccz66HexTableAugmentationService().LoadForProject(project, new HexTableParser());
        var critical = HexTableNameResolver.ResolveForProject(project, tables, "6.6-0-2 暴击台词");
        var retreat = HexTableNameResolver.ResolveForProject(project, tables, "6.6-0-3 撤退台词");
        if (critical.DataPos != 140_000 || retreat.DataPos != 491_600 || retreat.DataPos == 440_200)
        {
            throw new InvalidOperationException($"Generated 6.6 quote table offsets are wrong: critical={critical.DataPos}, retreat={retreat.DataPos}.");
        }

        var validation = new HexTableReader().Validate(project, retreat);
        if (!validation.IsNative66 || !validation.CanWrite || validation.IsCrossVersionFallback)
        {
            throw new InvalidOperationException($"6.6 retreat table should be verified Native66: {validation.TableStatus}, {validation.WriteRisk}");
        }
    }

    private static void RunRoleQuoteTextWriteRoundTrip(CczProject source, long expectedRetreatOffset, long? legacyOffsetMustRemainUnchanged)
    {
        var project = CreateMinimalQuoteTestCopy(source, "text");
        var tables = new Ccz66HexTableAugmentationService().LoadForProject(project, new HexTableParser());
        var hints = new CczEngineProfileService().Detect(project).TableHints;
        RunSingleTextTableWrite(project, tables, hints.CriticalQuoteTable, RoleQuoteLayoutService.CriticalTextOffset, "暴击布局烟测");
        var table = HexTableNameResolver.ResolveForProject(project, tables, hints.RetreatQuoteTable);
        if (table.DataPos != expectedRetreatOffset) throw new InvalidOperationException("Test-copy retreat table resolved to the wrong offset.");

        var imsgPath = project.ResolveGameFile("Imsg.e5");
        var before = File.ReadAllBytes(imsgPath);
        var read = new HexTableReader().Read(project, table, tables);
        var row = read.Data.Rows.Cast<DataRow>().Single(item => Convert.ToInt32(item["ID"]) == 0);
        row["介绍"] = "台词布局烟测";
        var save = new HexTableWriter().Save(project, table, read.Data);
        var after = File.ReadAllBytes(imsgPath);
        AssertOnlyRangeDiffers(before, after, checked((int)expectedRetreatOffset), RoleQuoteLayoutService.TextRowSize);
        if (legacyOffsetMustRemainUnchanged.HasValue &&
            !before.AsSpan(checked((int)legacyOffsetMustRemainUnchanged.Value), RoleQuoteLayoutService.RetreatTextCount * RoleQuoteLayoutService.TextRowSize)
                .SequenceEqual(after.AsSpan(checked((int)legacyOffsetMustRemainUnchanged.Value), RoleQuoteLayoutService.RetreatTextCount * RoleQuoteLayoutService.TextRowSize)))
        {
            throw new InvalidOperationException("6.6 text write changed the legacy 0x6B788 region.");
        }

        if (save.ChangedBytes <= 0 || !File.Exists(save.BackupPath)) throw new InvalidOperationException("Role quote text write lacked backup/change evidence.");
    }

    private static void RunSingleTextTableWrite(CczProject project, IReadOnlyList<HexTableDefinition> tables, string tableName, long expectedOffset, string text)
    {
        var table = HexTableNameResolver.ResolveForProject(project, tables, tableName);
        if (table.DataPos != expectedOffset) throw new InvalidOperationException($"Quote table {tableName} resolved to {table.DataPos}, expected {expectedOffset}.");
        var path = project.ResolveGameFile(table.FileName);
        var before = File.ReadAllBytes(path);
        var read = new HexTableReader().Read(project, table, tables);
        read.Data.Rows.Cast<DataRow>().Single(row => Convert.ToInt32(row["ID"]) == 0)["介绍"] = text;
        var save = new HexTableWriter().Save(project, table, read.Data);
        var after = File.ReadAllBytes(path);
        AssertOnlyRangeDiffers(before, after, checked((int)expectedOffset), RoleQuoteLayoutService.TextRowSize);
        if (save.ChangedBytes <= 0 || !File.Exists(save.BackupPath)) throw new InvalidOperationException("Critical quote write lacked backup/change evidence.");
    }

    private static void RunSpecialCriticalWriteRoundTrip(CczProject source)
    {
        var project = CreateMinimalQuoteTestCopy(source, "special");
        var service = new RoleQuoteMappingService();
        var ids = service.ReadSpecialCriticalRoleIds(project).ToArray();
        var before = File.ReadAllBytes(project.ResolveGameFile("Ekd5.exe"));
        ids[0] = ids[0] == 1023 ? 1022 : 1023;
        var save = service.SaveSpecialCriticalRoleIds(project, ids)
            ?? throw new InvalidOperationException("6.6 special-critical smoke made no change.");
        var after = File.ReadAllBytes(project.ResolveGameFile("Ekd5.exe"));
        AssertOnlyRangeDiffers(before, after, checked((int)save.Offset), sizeof(int));
        if (save.ExpectedOldBytesHex.Length != RoleQuoteLayoutService.SpecialCriticalMappingCount * sizeof(int) * 2 ||
            !save.EvidenceStatus.Equals(nameof(RoleQuoteLayoutEvidenceStatus.VerifiedWritable), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Special-critical save did not expose expected-old-bytes/evidence metadata.");
        }
        var reportText = File.ReadAllText(save.ReportJsonPath);
        if (!reportText.Contains("ExpectedOldBytesHex", StringComparison.Ordinal) ||
            !reportText.Contains(save.ExpectedOldBytesHex, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Special-critical report omitted ExpectedOldBytesHex.");
        }
    }

    private static void AssertDamaged66Layouts(CczProject source)
    {
        var truncated = CreateMinimalQuoteTestCopy(source, "truncated");
        using (var stream = File.Open(truncated.ResolveGameFile("Imsg.e5"), FileMode.Open, FileAccess.Write)) stream.SetLength(491_600);
        var truncatedLayout = new RoleQuoteLayoutService().Resolve(truncated);
        if (truncatedLayout.RetreatText.Status != RoleQuoteLayoutEvidenceStatus.Unsupported || truncatedLayout.RetreatText.CanWrite)
            throw new InvalidOperationException("Truncated 6.6 Imsg.e5 was not blocked.");

        var noReference = CreateMinimalQuoteTestCopy(source, "no-reference");
        var exePath = noReference.ResolveGameFile("Ekd5.exe");
        using (var stream = File.Open(exePath, FileMode.Open, FileAccess.ReadWrite))
        {
            stream.Position = 0xB4AC;
            stream.Write(new byte[4]);
        }
        var noReferenceLayout = new RoleQuoteLayoutService().Resolve(noReference);
        if (noReferenceLayout.SpecialCriticalMapping.Status != RoleQuoteLayoutEvidenceStatus.ReadOnlyEvidence || noReferenceLayout.SpecialCriticalMapping.CanWrite)
            throw new InvalidOperationException("6.6 EXE without mapping reference was not downgraded to read-only evidence.");

        var ids = new RoleQuoteMappingService().ReadSpecialCriticalRoleIds(noReference).ToArray();
        ids[0] = ids[0] == 1023 ? 1022 : 1023;
        try
        {
            _ = new RoleQuoteMappingService().SaveSpecialCriticalRoleIds(noReference, ids);
            throw new InvalidOperationException("Read-only 6.6 special-critical mapping unexpectedly allowed a write.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("不可写", StringComparison.Ordinal))
        {
        }
    }

    private static CczProject CreateMinimalQuoteTestCopy(CczProject source, string suffix)
    {
        var root = Path.Combine(source.WorkspaceRoot, "CCZModStudio_TestCopies", $"RoleQuoteLayout_{suffix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        RoleQuoteLayoutSmokeRoots.Add(root);
        foreach (var file in new[] { "Ekd5.exe", "Data.e5", "Imsg.e5", "Star.e5" })
            File.Copy(source.ResolveGameFile(file), Path.Combine(root, file));
        return new CczProject
        {
            WorkspaceRoot = source.WorkspaceRoot,
            GameRoot = root,
            HexTableXmlPath = source.HexTableXmlPath,
            SceneDictionaryPath = source.SceneDictionaryPath,
            SceneEditorDirectory = source.SceneEditorDirectory,
            ImageAssignerDirectory = source.ImageAssignerDirectory,
            ImageAssignerSystemIniPath = source.ImageAssignerSystemIniPath,
            MaterialLibraryRoot = source.MaterialLibraryRoot,
            PatchConfigRoot = source.PatchConfigRoot,
            PathDiagnostics = source.PathDiagnostics
        };
    }

    private static void AssertOnlyRangeDiffers(byte[] before, byte[] after, int offset, int length)
    {
        if (before.Length != after.Length) throw new InvalidOperationException("Smoke write changed file length.");
        for (var i = 0; i < before.Length; i++)
        {
            if (before[i] == after[i] || (i >= offset && i < offset + length)) continue;
            throw new InvalidOperationException($"Smoke write changed an out-of-range byte at 0x{i:X}.");
        }
    }
}
