using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class CmfDesignerWriteVerificationService
{
    private static readonly Regex LeadingHexValueRegex = new(@"^\s*(?:0x)?([0-9A-Fa-f]{1,32})(?=\s*(?:[-:：]|\s|$))", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly PatchApplyService _patchApplyService = new();

    public CmfDesignerWriteVerificationReport VerifyOnTestCopy(
        CczProject project,
        CmfDesignerSnapshot snapshot,
        CmfDesignerWriteVerificationOptions? options = null)
    {
        options ??= new CmfDesignerWriteVerificationOptions();
        var selectedIds = new HashSet<string>(options.BindingIds, StringComparer.OrdinalIgnoreCase);
        var bindings = snapshot.Bindings
            .Where(binding => selectedIds.Count == 0 || selectedIds.Contains(binding.BindingId))
            .Where(binding => options.IncludeNeedsManualReview || !binding.ValidationStatus.Equals("NeedsManualReview", StringComparison.OrdinalIgnoreCase))
            .Take(options.MaxFields <= 0 ? 500 : options.MaxFields)
            .ToArray();

        var warnings = new List<string>();
        var testRoot = new BackupManager().CreateTestCopy(project);
        var testProject = new CczProject
        {
            WorkspaceRoot = project.WorkspaceRoot,
            GameRoot = testRoot,
            HexTableXmlPath = project.HexTableXmlPath,
            SceneDictionaryPath = project.SceneDictionaryPath,
            SceneEditorDirectory = project.SceneEditorDirectory,
            ImageAssignerDirectory = project.ImageAssignerDirectory,
            ImageAssignerSystemIniPath = project.ImageAssignerSystemIniPath,
            MaterialLibraryRoot = project.MaterialLibraryRoot,
            PatchConfigRoot = project.PatchConfigRoot,
            PathDiagnostics = project.PathDiagnostics
        };

        var results = bindings.Select(binding => VerifyOne(testProject, snapshot, binding)).ToArray();
        var report = new CmfDesignerWriteVerificationReport
        {
            SourceCmfRelativePath = snapshot.RelativePath,
            SourceSha256 = snapshot.SourceSha256,
            SourceGameRoot = project.GameRoot,
            TestCopyRoot = testRoot,
            TotalFields = results.Length,
            WriteVerifiedCount = results.Count(result => result.CanPromoteToWrite),
            Fields = results,
            Warnings = warnings
        };

        return WriteReport(project, snapshot, report);
    }

    private CmfDesignerFieldWriteVerification VerifyOne(CczProject testProject, CmfDesignerSnapshot snapshot, CmfDesignerBinding binding)
    {
        var stages = new List<string> { "ExtractedFromDesigner" };
        var warnings = new List<string>();
        var targetFile = ResolveTargetFile(snapshot, binding);
        var pageName = snapshot.Pages.FirstOrDefault(page => page.PageId.Equals(binding.PageId, StringComparison.OrdinalIgnoreCase))?.Name ?? binding.PageId;
        var moduleTitle = snapshot.Modules.FirstOrDefault(module => module.ModuleId.Equals(binding.ModuleId, StringComparison.OrdinalIgnoreCase))?.Title ?? binding.ModuleId;
        var addressKind = ResolvePatchAddressKind(binding);
        var address = ResolvePatchAddress(binding, addressKind);

        if (string.IsNullOrWhiteSpace(targetFile) || addressKind == PatchAddressKind.Unknown || !address.HasValue || binding.ByteLength <= 0)
        {
            return BuildResult("NeedsManualReview", false, pageName, moduleTitle, binding, targetFile, null, string.Empty, string.Empty, string.Empty, string.Empty, stages, warnings.Append("Missing target file, address kind, address, or byte length.").ToArray());
        }

        stages.Add("AddressClassified");
        var targetPath = testProject.ResolveGameFile(targetFile);
        if (!File.Exists(targetPath))
        {
            return BuildResult("AddressClassified", false, pageName, moduleTitle, binding, targetFile, null, string.Empty, string.Empty, string.Empty, string.Empty, stages, warnings.Append("Target file was not found in test copy: " + targetFile).ToArray());
        }

        long fileOffset;
        try
        {
            fileOffset = addressKind == PatchAddressKind.OdVirtualAddress
                ? PeAddressMapper.Load(targetPath).VirtualAddressToFileOffset(ToUIntAddress(address.Value))
                : address.Value;
        }
        catch (Exception ex)
        {
            return BuildResult("AddressClassified", false, pageName, moduleTitle, binding, targetFile, null, string.Empty, string.Empty, string.Empty, string.Empty, stages, warnings.Append("Address mapping failed: " + ex.Message).ToArray());
        }

        var targetBytes = File.ReadAllBytes(targetPath);
        if (fileOffset < 0 || fileOffset + binding.ByteLength > targetBytes.LongLength)
        {
            return BuildResult("AddressClassified", false, pageName, moduleTitle, binding, targetFile, fileOffset, string.Empty, string.Empty, string.Empty, string.Empty, stages, warnings.Append("Target offset is out of bounds.").ToArray());
        }

        stages.Add("BoundsChecked");
        var originalBytes = new byte[binding.ByteLength];
        Buffer.BlockCopy(targetBytes, checked((int)fileOffset), originalBytes, 0, binding.ByteLength);
        var originalHex = ToHex(originalBytes);
        if (!CanInterpretBaseline(binding, originalBytes, warnings))
        {
            return BuildResult("BoundsChecked", false, pageName, moduleTitle, binding, targetFile, fileOffset, originalHex, string.Empty, string.Empty, string.Empty, stages, warnings.ToArray());
        }

        stages.Add("BaselineReadOk");
        if (!TryBuildSemanticCandidateBytes(binding, originalBytes, out var newBytes, out var candidateSource, warnings))
        {
            return BuildResult("BaselineReadOk", false, pageName, moduleTitle, binding, targetFile, fileOffset, originalHex, string.Empty, string.Empty, string.Empty, stages, warnings.ToArray());
        }

        if (newBytes.Length != binding.ByteLength)
        {
            return BuildResult("BaselineReadOk", false, pageName, moduleTitle, binding, targetFile, fileOffset, originalHex, ToHex(newBytes), candidateSource, string.Empty, stages, warnings.Append("Candidate byte length does not match field byte length.").ToArray());
        }

        var document = new PatchDocument
        {
            SourcePath = "CheatMaker Designer " + snapshot.RelativePath,
            Version = "CMF Designer Verification",
            AddressKind = addressKind,
            Entries =
            [
                new PatchEntry
                {
                    Index = 0,
                    SourceLine = 0,
                    Address = checked((uint)address.Value),
                    Bytes = newBytes,
                    Comment = $"{binding.ControlName} {binding.DisplayName}"
                }
            ],
            Comments = ["Generated from CheatMaker Designer snapshot for test-copy verification only."]
        };

        PatchPreviewResult preview;
        try
        {
            preview = _patchApplyService.Preview(testProject, document, targetFile);
        }
        catch (Exception ex)
        {
            return BuildResult("BaselineReadOk", false, pageName, moduleTitle, binding, targetFile, fileOffset, originalHex, ToHex(newBytes), candidateSource, string.Empty, stages, warnings.Append("Patch preview failed: " + ex.Message).ToArray());
        }

        if (!preview.CanApply)
        {
            var previewWarning = preview.Rows.FirstOrDefault(row => !row.CanApply)?.Status ?? "Patch preview did not allow apply.";
            return BuildResult("BaselineReadOk", false, pageName, moduleTitle, binding, targetFile, fileOffset, originalHex, ToHex(newBytes), candidateSource, string.Empty, stages, warnings.Append(previewWarning).ToArray());
        }

        stages.Add("WritePreviewOk");
        PatchApplyResult apply;
        try
        {
            apply = _patchApplyService.ApplyToTestCopy(testProject, document, targetFile);
        }
        catch (Exception ex)
        {
            return BuildResult("WritePreviewOk", false, pageName, moduleTitle, binding, targetFile, fileOffset, originalHex, ToHex(newBytes), candidateSource, string.Empty, stages, warnings.Append("Patch apply failed: " + ex.Message).ToArray());
        }

        var rereadBytes = File.ReadAllBytes(targetPath);
        var actual = new byte[binding.ByteLength];
        Buffer.BlockCopy(rereadBytes, checked((int)fileOffset), actual, 0, binding.ByteLength);
        if (!actual.SequenceEqual(newBytes))
        {
            return BuildResult("WritePreviewOk", false, pageName, moduleTitle, binding, targetFile, fileOffset, originalHex, ToHex(newBytes), candidateSource, apply.ReportJsonPath, stages, warnings.Append("Reread bytes did not match written bytes: " + ToHex(actual)).ToArray());
        }

        stages.Add("WriteVerified");
        return BuildResult("WriteVerified", true, pageName, moduleTitle, binding, targetFile, fileOffset, originalHex, ToHex(newBytes), candidateSource, apply.ReportJsonPath, stages, warnings.ToArray());
    }

    private static CmfDesignerFieldWriteVerification BuildResult(
        string finalStatus,
        bool canPromote,
        string pageName,
        string moduleTitle,
        CmfDesignerBinding binding,
        string targetFile,
        long? fileOffset,
        string originalBytesHex,
        string newBytesHex,
        string candidateSource,
        string patchReportJsonPath,
        IReadOnlyList<string> stages,
        IReadOnlyList<string> warnings)
        => new()
        {
            BindingId = binding.BindingId,
            PageName = pageName,
            ModuleTitle = moduleTitle,
            ControlName = binding.ControlName,
            DisplayName = binding.DisplayName,
            TargetFile = targetFile,
            AddressKind = binding.AddressKind,
            UeOffsetHex = binding.UeOffsetHex,
            FileOffset = fileOffset,
            FileOffsetHex = fileOffset.HasValue ? "0x" + fileOffset.Value.ToString("X", CultureInfo.InvariantCulture) : string.Empty,
            ByteLength = binding.ByteLength,
            DataType = binding.DataType,
            OriginalBytesHex = originalBytesHex,
            NewBytesHex = newBytesHex,
            CandidateSource = candidateSource,
            FinalStatus = finalStatus,
            CanPromoteToWrite = canPromote,
            PatchReportJsonPath = patchReportJsonPath,
            Stages = stages,
            Warnings = warnings
        };

    private static bool CanInterpretBaseline(CmfDesignerBinding binding, byte[] originalBytes, List<string> warnings)
    {
        if (binding.ByteLength <= 0)
        {
            warnings.Add("Missing byte length.");
            return false;
        }

        if (IsTextType(binding.DataType))
        {
            warnings.Add("Text fields require a dedicated fixed-encoding writer before automatic write verification.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(binding.DataType) || binding.DataType.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("Missing or unknown data type.");
            return false;
        }

        var parsedValues = ParseDataListValues(binding).ToArray();
        if (parsedValues.Length > 0 && parsedValues.All(value => !value.SequenceEqual(originalBytes)))
        {
            warnings.Add("Current value was not found in the designer data list; continuing only if a semantic candidate can still be generated.");
        }

        return true;
    }

    private static bool TryBuildSemanticCandidateBytes(CmfDesignerBinding binding, byte[] originalBytes, out byte[] bytes, out string source, List<string> warnings)
    {
        foreach (var value in ParseDataListValues(binding))
        {
            if (value.Length == binding.ByteLength && !value.SequenceEqual(originalBytes))
            {
                bytes = value;
                source = "DataListRaw";
                return true;
            }
        }

        foreach (var value in ParseDefaultValues(binding))
        {
            if (value.Length == binding.ByteLength && !value.SequenceEqual(originalBytes))
            {
                bytes = value;
                source = "DefaultValue";
                return true;
            }
        }

        warnings.Add("No semantic candidate value was available from data list or default value; field is not promoted to WriteVerified.");
        bytes = Array.Empty<byte>();
        source = string.Empty;
        return false;
    }

    private static IEnumerable<byte[]> ParseDataListValues(CmfDesignerBinding binding)
    {
        if (string.IsNullOrWhiteSpace(binding.DataListRaw)) yield break;
        foreach (var rawLine in binding.DataListRaw.Split(["\r\n", "\n", "\r", ";", "|"], StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryParseDesignerValue(rawLine, binding.ByteLength, out var bytes))
            {
                yield return bytes;
            }
        }
    }

    private static IEnumerable<byte[]> ParseDefaultValues(CmfDesignerBinding binding)
    {
        foreach (var value in new[] { binding.DefaultValueParsed, binding.DefaultValueRaw })
        {
            if (TryParseDesignerValue(value, binding.ByteLength, out var bytes))
            {
                yield return bytes;
            }
        }
    }

    private static bool TryParseDesignerValue(string value, int byteLength, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(value) || byteLength <= 0) return false;

        var match = LeadingHexValueRegex.Match(value);
        if (!match.Success) return false;

        var hex = match.Groups[1].Value;
        if (hex.Length == byteLength * 2)
        {
            bytes = ConvertHexPairs(hex);
            return bytes.Length == byteLength;
        }

        if (hex.Length <= 16 && ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var numeric))
        {
            bytes = ToLittleEndianBytes(numeric, byteLength);
            return true;
        }

        return false;
    }

    private static byte[] ConvertHexPairs(string hex)
    {
        if (hex.Length % 2 != 0) hex = "0" + hex;
        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = byte.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return bytes;
    }

    private static byte[] ToLittleEndianBytes(ulong value, int byteLength)
    {
        var bytes = new byte[byteLength];
        for (var i = 0; i < byteLength; i++)
        {
            bytes[i] = (byte)((value >> (i * 8)) & 0xFF);
        }

        return bytes;
    }

    private static PatchAddressKind ResolvePatchAddressKind(CmfDesignerBinding binding)
    {
        if (binding.AddressKind.Contains("OD", StringComparison.OrdinalIgnoreCase) ||
            binding.AddressKind.Contains("VA", StringComparison.OrdinalIgnoreCase) ||
            binding.AddressKind.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
        {
            return PatchAddressKind.OdVirtualAddress;
        }

        if (binding.AddressKind.Contains("UE", StringComparison.OrdinalIgnoreCase) ||
            binding.AddressKind.Contains("File", StringComparison.OrdinalIgnoreCase) ||
            binding.UeOffset.HasValue)
        {
            return PatchAddressKind.FileOffset;
        }

        return PatchAddressKind.Unknown;
    }

    private static long? ResolvePatchAddress(CmfDesignerBinding binding, PatchAddressKind addressKind)
        => addressKind == PatchAddressKind.OdVirtualAddress
            ? binding.OdVirtualAddress
            : binding.UeOffset;

    private static uint ToUIntAddress(long address)
    {
        if (address is < 0 or > uint.MaxValue)
        {
            throw new InvalidOperationException("Address is outside uint range: 0x" + address.ToString("X", CultureInfo.InvariantCulture));
        }

        return (uint)address;
    }

    private static string ResolveTargetFile(CmfDesignerSnapshot snapshot, CmfDesignerBinding binding)
    {
        if (!string.IsNullOrWhiteSpace(binding.TargetFile)) return binding.TargetFile;
        var source = (snapshot.RelativePath + " " + snapshot.SourcePath).ToLowerInvariant();
        return source.Contains("exe", StringComparison.OrdinalIgnoreCase) ||
               source.Contains("引擎", StringComparison.OrdinalIgnoreCase)
            ? "Ekd5.exe"
            : string.Empty;
    }

    private static bool IsTextType(string dataType)
        => dataType.Contains("Text", StringComparison.OrdinalIgnoreCase) ||
           dataType.Contains("文本", StringComparison.OrdinalIgnoreCase) ||
           dataType.Contains("String", StringComparison.OrdinalIgnoreCase);

    private static CmfDesignerWriteVerificationReport WriteReport(CczProject project, CmfDesignerSnapshot snapshot, CmfDesignerWriteVerificationReport report)
    {
        var root = Path.Combine(
            project.WorkspaceRoot,
            "CCZModStudio_Reports",
            "CmfDesignerWriteVerification",
            MakeSafeName(Path.GetFileNameWithoutExtension(snapshot.RelativePath)),
            DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(root);
        report.ReportDirectory = root;
        report.JsonReportPath = Path.Combine(root, "write-verification.json");
        File.WriteAllText(report.JsonReportPath, JsonSerializer.Serialize(report, JsonOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return report;
    }

    private static string MakeSafeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "cmf";
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value;
    }

    private static string ToHex(byte[] bytes)
        => string.Join(" ", bytes.Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
}
