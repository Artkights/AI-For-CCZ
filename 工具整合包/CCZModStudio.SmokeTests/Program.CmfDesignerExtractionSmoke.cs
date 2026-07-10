using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;

internal partial class Program
{
    static void RunCmfDesignerExtractionSmoke()
    {
        var project = new ProjectDetector().DetectDefaultProject();
        var oldToolsRoot = CheatMakerCmfProbe.FindDefaultOldToolsRoot(project.WorkspaceRoot)
            ?? throw new DirectoryNotFoundException("Old tools root was not found under workspace.");
        var corpus = new CheatMakerCmfProbe().ScanCorpus(oldToolsRoot);
        var star65Path = FindCmfByLength(corpus, 768_391);
        var relativePath = Path.GetRelativePath(oldToolsRoot, star65Path);
        var beforeSha = ComputeSmokeSha256(star65Path);
        var fixturePath = Path.Combine(Path.GetTempPath(), "CCZModStudio_CmfDesignerFixture_" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            var fixture = BuildComboBox18DesignerFixture(relativePath);
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            File.WriteAllText(fixturePath, JsonSerializer.Serialize(fixture, jsonOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            var service = new CmfDesignerExtractionService();
            var result = service.ExtractDesignerSnapshot(
                project,
                relativePath,
                new CmfDesignerExtractionOptions
                {
                    Mode = "StaticOnly",
                    FixtureSnapshotPath = fixturePath
                });

            var afterSha = ComputeSmokeSha256(star65Path);
            if (!beforeSha.Equals(afterSha, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Original CMF SHA changed during designer extraction smoke.");
            }

            var snapshot = result.Snapshot;
            if (!snapshot.Pages.Any(page => page.Name == "策略修改his"))
            {
                throw new InvalidOperationException("Designer snapshot smoke did not preserve page 策略修改his.");
            }

            var binding = snapshot.Bindings.FirstOrDefault(item => item.ControlName == "ComboBox18")
                ?? throw new InvalidOperationException("Designer snapshot smoke did not include ComboBox18.");
            if (binding.UeOffsetHex != "0x3B591" || binding.UeOffset != 0x3B591)
            {
                throw new InvalidOperationException("ComboBox18 UE address mismatch: " + binding.UeOffsetHex);
            }

            if (binding.ByteLength != 1)
            {
                throw new InvalidOperationException("ComboBox18 byte length mismatch: " + binding.ByteLength);
            }

            if (binding.DataType != "Hex")
            {
                throw new InvalidOperationException("ComboBox18 data type mismatch: " + binding.DataType);
            }

            foreach (var reportPath in new[] { result.SnapshotJsonPath, result.FieldsCsvPath, result.ModulesMarkdownPath, result.AddressesMarkdownPath, result.RawUiTreeJsonPath })
            {
                if (!File.Exists(reportPath))
                {
                    throw new InvalidOperationException("Designer extraction report file was not created: " + reportPath);
                }
            }

            if (!File.ReadAllText(result.AddressesMarkdownPath, Encoding.UTF8).Contains("0x3B591", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("addresses.md did not contain 0x3B591.");
            }

            var imported = new CmfKnowledgeExtractor().ImportDesignerSnapshot(star65Path, snapshot, oldToolsRoot);
            if (imported.DesignerSnapshot == null || imported.DataBindings.All(item => item.TrustLevel != CmfTrustLevel.ExtractedFromCheatMakerDesigner))
            {
                throw new InvalidOperationException("Designer snapshot import did not promote designer trust level.");
            }

            var importedThroughService = new CmfDerivedCapabilityService().ImportDesignerSnapshot(project, relativePath, result.SnapshotJsonPath);
            if (importedThroughService.DesignerSnapshot == null ||
                importedThroughService.DesignerSnapshot.Bindings.All(field => field.ControlName != "ComboBox18" || field.UeOffsetHex != "0x3B591"))
            {
                throw new InvalidOperationException("Derived designer snapshot import did not load ComboBox18 from the explicit smoke snapshot.");
            }

            Console.WriteLine(
                "CMF_DESIGNER_EXTRACTION_SMOKE_OK " +
                $"fields={snapshot.Bindings.Count} address={binding.UeOffsetHex} report={result.ReportDirectory}");
        }
        finally
        {
            try
            {
                if (File.Exists(fixturePath))
                {
                    File.Delete(fixturePath);
                }
            }
            catch
            {
                // Temp cleanup is best effort.
            }
        }
    }

    private static CmfDesignerSnapshot BuildComboBox18DesignerFixture(string relativePath)
    {
        const string pageId = "page-strategy-his";
        const string moduleId = "module-strategy-damage-type";
        const string controlId = "control-combobox18";
        const string bindingId = "binding-combobox18-3b591";

        return new CmfDesignerSnapshot
        {
            RelativePath = relativePath,
            ExtractionMode = "Fixture",
            Pages =
            [
                new CmfDesignerPage
                {
                    PageId = pageId,
                    Name = "策略修改his",
                    WindowTitle = "策略修改his",
                    Bounds = new CmfUiRect(0, 0, 1040, 760)
                }
            ],
            Modules =
            [
                new CmfDesignerModule
                {
                    ModuleId = moduleId,
                    PageId = pageId,
                    Title = "策略伤害类型",
                    Bounds = new CmfUiRect(760, 650, 250, 80),
                    Notes =
                    [
                        new CmfModuleNote
                        {
                            Text = "截图验收样本：选择 ComboBox18 后右侧属性栏显示地址和数据绑定。",
                            Bounds = new CmfUiRect(760, 630, 260, 16),
                            Color = "Red"
                        }
                    ],
                    ControlIds = [controlId],
                    BindingIds = [bindingId]
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
                    Text = "00-大兵种00",
                    Bounds = new CmfUiRect(846, 697, 90, 20),
                    Properties = new Dictionary<string, string>
                    {
                        ["控件类型"] = "ComboBox",
                        ["名称"] = "ComboBox18",
                        ["坐标"] = "846, 697",
                        ["大小"] = "90, 20"
                    }
                }
            ],
            Bindings =
            [
                new CmfDesignerBinding
                {
                    BindingId = bindingId,
                    PageId = pageId,
                    ModuleId = moduleId,
                    ControlId = controlId,
                    ControlName = "ComboBox18",
                    ControlType = "ComboBox",
                    DisplayName = "策略伤害类型",
                    TargetFile = "Ekd5.exe",
                    AddressKind = "UeFileOffset",
                    UeOffsetHex = "0x3B591",
                    UeOffset = 0x3B591,
                    ByteLength = 1,
                    DataType = "Hex",
                    FunctionType = "数据操作",
                    DefaultValueRaw = "00-大兵种00",
                    DefaultValueParsed = "0x00",
                    DataListRaw = "00-大兵种00\r\n01-小兵种01",
                    ValidationStatus = "ExtractedFromDesigner",
                    SourceProperties = new Dictionary<string, string>
                    {
                        ["控件类型"] = "ComboBox",
                        ["名称"] = "ComboBox18",
                        ["坐标"] = "846, 697",
                        ["大小"] = "90, 20",
                        ["地址(HEX)"] = "3B591",
                        ["数据列表"] = "00-大兵种00\r\n01-小兵种01",
                        ["数据大小"] = "1",
                        ["默认值"] = "00-大兵种00",
                        ["功能类型"] = "数据操作",
                        ["数据类型"] = "十六进制",
                        ["列表高度"] = "160"
                    }
                }
            ]
        };
    }

    private static string ComputeSmokeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
