using CCZModStudio.Core;
using CCZModStudio.Models;

internal partial class Program
{
    static void RunCmfDesignerWriteSmoke()
    {
        using var temp = new TemporarySmokeDirectory("CmfDesignerWrite");
        var gameRoot = Path.Combine(temp.Path, "Game");
        Directory.CreateDirectory(gameRoot);
        File.WriteAllText(Path.Combine(gameRoot, "_CCZModStudio_TestCopy.txt"), "CMF designer write smoke.");
        var exePath = Path.Combine(gameRoot, "Ekd5.exe");
        var exeBytes = new byte[0x3B600];
        exeBytes[0x3B591] = 0x00;
        File.WriteAllBytes(exePath, exeBytes);
        var hexTablePath = Path.Combine(temp.Path, "HexTable.xml");
        File.WriteAllText(hexTablePath, "<Root />");

        var project = new CczProject
        {
            WorkspaceRoot = temp.Path,
            GameRoot = gameRoot,
            HexTableXmlPath = hexTablePath
        };

        var snapshot = BuildComboBox18DesignerFixture("Star6.5引擎exe修改器.cmf");
        var report = new CmfDesignerWriteVerificationService().VerifyOnTestCopy(
            project,
            snapshot,
            new CmfDesignerWriteVerificationOptions
            {
                BindingIds = ["binding-combobox18-3b591"],
                MaxFields = 1
            });

        var field = report.Fields.Single();
        if (field.FinalStatus != "WriteVerified" || !field.CanPromoteToWrite)
        {
            throw new InvalidOperationException("CMF designer write verification did not reach WriteVerified: " + field.FinalStatus);
        }

        if (File.ReadAllBytes(exePath)[0x3B591] != 0x00)
        {
            throw new InvalidOperationException("CMF designer write verification modified the source project instead of only the test copy.");
        }

        if (string.IsNullOrWhiteSpace(report.TestCopyRoot) ||
            !File.Exists(Path.Combine(report.TestCopyRoot, "Ekd5.exe")) ||
            File.ReadAllBytes(Path.Combine(report.TestCopyRoot, "Ekd5.exe"))[0x3B591] != 0x01)
        {
            throw new InvalidOperationException("CMF designer write verification did not update the generated test copy.");
        }

        if (!File.Exists(report.JsonReportPath))
        {
            throw new InvalidOperationException("CMF designer write verification report was not written.");
        }

        var right = BuildChangedDesignerFixtureForDiff();
        var diff = new CmfDesignerSnapshotDiffService().Compare(project, snapshot, right);
        if (diff.BindingDiffs.Count == 0 ||
            diff.BindingDiffs.All(item => !item.Detail.Contains("0x3B591", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("CMF designer snapshot diff did not report the changed binding address.");
        }

        if (!File.Exists(diff.JsonReportPath) || !File.Exists(diff.MarkdownReportPath))
        {
            throw new InvalidOperationException("CMF designer snapshot diff reports were not written.");
        }

        Console.WriteLine(
            "CMF_DESIGNER_WRITE_SMOKE_OK " +
            $"status={field.FinalStatus} original={field.OriginalBytesHex} new={field.NewBytesHex} " +
            $"verifyReport={report.JsonReportPath} diffReport={diff.JsonReportPath}");
    }

    private static CmfDesignerSnapshot BuildChangedDesignerFixtureForDiff()
    {
        const string pageId = "page-strategy-his";
        const string moduleId = "module-strategy-damage-type";
        const string controlId = "control-combobox18";
        return new CmfDesignerSnapshot
        {
            RelativePath = "Star6.6X 引擎.cmf",
            SourceSha256 = "DIFF_SMOKE",
            ExtractionMode = "Fixture",
            Pages =
            [
                new CmfDesignerPage
                {
                    PageId = pageId,
                    Name = "绛栫暐淇敼his",
                    WindowTitle = "绛栫暐淇敼his",
                    Bounds = new CmfUiRect(0, 0, 1040, 760)
                }
            ],
            Modules =
            [
                new CmfDesignerModule
                {
                    ModuleId = moduleId,
                    PageId = pageId,
                    Title = "绛栫暐浼ゅ绫诲瀷",
                    Bounds = new CmfUiRect(760, 650, 250, 80),
                    ControlIds = [controlId],
                    BindingIds = ["binding-combobox18-3b592"]
                }
            ],
            Controls =
            [
                new CmfDesignerControl
                {
                    ControlId = controlId,
                    PageId = pageId,
                    ModuleId = moduleId,
                    ControlType = "ComboBox",
                    Name = "ComboBox18",
                    Text = "00-澶у叺绉?0",
                    Bounds = new CmfUiRect(846, 697, 90, 20)
                }
            ],
            Bindings =
            [
                new CmfDesignerBinding
                {
                    BindingId = "binding-combobox18-3b592",
                    PageId = pageId,
                    ModuleId = moduleId,
                    ControlId = controlId,
                    ControlName = "ComboBox18",
                    ControlType = "ComboBox",
                    DisplayName = "绛栫暐浼ゅ绫诲瀷",
                    TargetFile = "Ekd5.exe",
                    AddressKind = "UeFileOffset",
                    UeOffsetHex = "0x3B592",
                    UeOffset = 0x3B592,
                    ByteLength = 1,
                    DataType = "Hex",
                    FunctionType = "鏁版嵁鎿嶄綔",
                    DefaultValueRaw = "00-澶у叺绉?0",
                    DefaultValueParsed = "0x00",
                    DataListRaw = "00-澶у叺绉?0\r\n01-灏忓叺绉?1",
                    ValidationStatus = "ExtractedFromDesigner"
                }
            ]
        };
    }
}
