using System.Drawing;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// 项目资源替换服务。
/// 用于 Map/JPG、RS/EEX、WAV、MP3、SV 等已索引文件的“整文件替换”，不尝试重封包未知格式。
/// </summary>
public sealed class ResourceReplaceService
{
    private readonly WriteOperationReportService _reportService = new();

    public ResourceReplacePreviewResult PreviewReplacement(CczProject project, string targetPath, string replacementPath, bool requireSameExtension = true)
    {
        return BuildPreviewData(project, targetPath, replacementPath, requireSameExtension, requireTestCopy: false).ToPreviewResult();
    }

    public ResourceReplaceResult ReplaceInTestCopy(CczProject project, string targetPath, string replacementPath, bool requireSameExtension = true)
        => ReplaceCore(project, targetPath, replacementPath, requireSameExtension, requireTestCopy: true);

    public ResourceReplaceResult Replace(CczProject project, string targetPath, string replacementPath, bool requireSameExtension = true)
        => ReplaceCore(project, targetPath, replacementPath, requireSameExtension, requireTestCopy: false);

    private ResourceReplaceResult ReplaceCore(CczProject project, string targetPath, string replacementPath, bool requireSameExtension, bool requireTestCopy)
    {
        var preview = BuildPreviewData(project, targetPath, replacementPath, requireSameExtension, requireTestCopy);
        var backupPath = CreateBeforeSaveBackup(project, preview.TargetPath);
        var tempPath = preview.TargetPath + ".CCZModStudio.tmp";
        File.WriteAllBytes(tempPath, preview.NewBytes);
        File.Move(tempPath, preview.TargetPath, overwrite: true);
        var reportPath = WriteReport(project, preview.TargetPath, preview.ReplacementPath, backupPath, preview);
        var reportJsonPath = WriteStructuredReport(project, preview, backupPath, reportPath);

        return new ResourceReplaceResult
        {
            TargetPath = preview.TargetPath,
            ReplacementPath = preview.ReplacementPath,
            BackupPath = backupPath,
            ReportPath = reportPath,
            ReportJsonPath = reportJsonPath,
            OldSizeBytes = preview.OldBytes.LongLength,
            NewSizeBytes = preview.NewBytes.LongLength,
            ChangedBytesEstimate = preview.ChangedBytes,
            OldSha256 = preview.OldHash,
            NewSha256 = preview.NewHash,
            FormatCheckSummary = preview.FormatCheck.Summary,
            FormatWarnings = preview.FormatCheck.Warnings,
            RiskSummary = preview.RiskSummary
        };
    }

    private string WriteStructuredReport(
        CczProject project,
        ReplacementPreviewData preview,
        string backupPath,
        string textReportPath)
    {
        var targetRelative = WriteOperationReportService.ToProjectRelativePath(project, preview.TargetPath);
        var warnings = preview.FormatCheck.Warnings.Count == 0
            ? "无"
            : string.Join("；", preview.FormatCheck.Warnings);
        var sizeDelta = preview.NewBytes.LongLength - preview.OldBytes.LongLength;
        var report = new WriteOperationReport
        {
            OperationKind = "资源整文件替换/还原",
            SourceAction = "资源整文件写入前自动备份",
            ProjectRoot = project.GameRoot,
            TargetRelativePath = targetRelative,
            TargetPath = preview.TargetPath,
            BackupPath = backupPath,
            TextReportPath = textReportPath,
            BeforeSha256 = preview.OldHash,
            AfterSha256 = preview.NewHash,
            ChangedBytes = preview.ChangedBytes,
            Summary = $"整文件写入资源“{targetRelative}”，来源 {preview.ReplacementPath}，大小变化 {sizeDelta:+#;-#;0} 字节，估算改动 {preview.ChangedBytes:N0} 字节。",
            SafetyNotes = "该报告由项目资源整文件替换/还原流程自动生成。保存前已备份目标文件；当前不会解析或重封包 EEX/E5/SV 等未知内部结构。",
            FormatCheckSummary = preview.FormatCheck.Summary,
            RiskSummary = preview.RiskSummary,
            Changes =
            [
                new WriteOperationChange
                {
                    Category = "资源整文件",
                    TableName = targetRelative,
                    ColumnName = Path.GetFileName(preview.TargetPath),
                    OffsetHex = "整文件",
                    ByteLength = preview.NewBytes.LongLength <= int.MaxValue ? (int)preview.NewBytes.LongLength : null,
                    OldValue = $"旧大小={preview.OldBytes.LongLength:N0} 字节；SHA256={preview.OldHash}",
                    NewValue = $"新大小={preview.NewBytes.LongLength:N0} 字节；SHA256={preview.NewHash}；来源={preview.ReplacementPath}",
                    Annotation = $"对项目资源 {targetRelative} 执行整文件覆盖。格式检查：{preview.FormatCheck.Summary}；格式警告：{warnings}；风险提示：{preview.RiskSummary}"
                }
            ],
            Metadata =
            {
                ["ReplacementPath"] = preview.ReplacementPath,
                ["Extension"] = preview.Extension,
                ["OldSizeBytes"] = preview.OldBytes.LongLength.ToString(CultureInfo.InvariantCulture),
                ["NewSizeBytes"] = preview.NewBytes.LongLength.ToString(CultureInfo.InvariantCulture),
                ["SizeDeltaBytes"] = sizeDelta.ToString(CultureInfo.InvariantCulture),
                ["ChangedBytesEstimate"] = preview.ChangedBytes.ToString(CultureInfo.InvariantCulture),
                ["FormatWarnings"] = warnings
            }
        };

        return _reportService.WriteJsonReport(report, backupPath);
    }

    private static ReplacementPreviewData BuildPreviewData(CczProject project, string targetPath, string replacementPath, bool requireSameExtension, bool requireTestCopy)
    {
        targetPath = Path.GetFullPath(targetPath);
        replacementPath = Path.GetFullPath(replacementPath);
        var gameRoot = Path.GetFullPath(project.GameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!targetPath.StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("目标资源不在当前项目目录内，禁止替换：" + targetPath);
        }

        _ = requireTestCopy;

        if (!File.Exists(targetPath)) throw new FileNotFoundException("目标资源文件不存在。", targetPath);
        if (!File.Exists(replacementPath)) throw new FileNotFoundException("替换来源文件不存在。", replacementPath);

        var targetExtension = Path.GetExtension(targetPath);
        var replacementExtension = Path.GetExtension(replacementPath);
        if (requireSameExtension && !targetExtension.Equals(replacementExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"为避免格式误替换，要求扩展名一致：目标 {targetExtension}，来源 {replacementExtension}。");
        }

        var formatCheck = ValidateReplacementFormat(targetPath, replacementPath);
        var oldBytes = File.ReadAllBytes(targetPath);
        var newBytes = File.ReadAllBytes(replacementPath);
        var oldHash = ComputeSha256(oldBytes);
        var newHash = ComputeSha256(newBytes);
        var changedBytes = EstimateChangedBytes(oldBytes, newBytes);
        var relative = Path.GetRelativePath(project.GameRoot, targetPath);
        var riskSummary = BuildRiskSummary(targetPath, oldHash, newHash, oldBytes.LongLength, newBytes.LongLength, changedBytes, formatCheck);

        return new ReplacementPreviewData(targetPath, replacementPath, relative, targetExtension, oldBytes, newBytes, oldHash, newHash, changedBytes, formatCheck, riskSummary);
    }

    private static FormatCheckResult ValidateReplacementFormat(string targetPath, string replacementPath)
    {
        var extension = Path.GetExtension(targetPath).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" or ".png" or ".bmp" => ValidateImage(targetPath, replacementPath),
            ".wav" => ValidateWav(replacementPath),
            ".mp3" => ValidateMp3(replacementPath),
            ".eex" => ValidateEex(replacementPath),
            ".e5" => ValidateE5(replacementPath),
            ".e5s" => ValidateScenario(replacementPath),
            _ => new FormatCheckResult("未识别到专用格式检查规则；已完成扩展名一致性检查。", Array.Empty<string>())
        };
    }

    private static FormatCheckResult ValidateImage(string targetPath, string replacementPath)
    {
        using var targetImage = Image.FromFile(targetPath);
        using var replacementImage = Image.FromFile(replacementPath);
        var warnings = new List<string>();
        if (targetImage.Width != replacementImage.Width || targetImage.Height != replacementImage.Height)
        {
            warnings.Add($"图片尺寸变化：目标 {targetImage.Width}x{targetImage.Height}，来源 {replacementImage.Width}x{replacementImage.Height}。地图底图通常建议保持尺寸一致。");
        }

        var summary = $"图片格式检查通过：目标 {targetImage.Width}x{targetImage.Height}，来源 {replacementImage.Width}x{replacementImage.Height}。";
        return new FormatCheckResult(summary, warnings);
    }

    private static FormatCheckResult ValidateWav(string replacementPath)
    {
        var header = ReadPrefix(replacementPath, 12);
        if (header.Length < 12 || Encoding.ASCII.GetString(header, 0, 4) != "RIFF" || Encoding.ASCII.GetString(header, 8, 4) != "WAVE")
        {
            throw new InvalidOperationException("WAV 格式检查失败：来源文件不是标准 RIFF/WAVE 头。");
        }

        var info = TryReadWavInfo(replacementPath);
        if (info == null)
        {
            return new FormatCheckResult("WAV 格式检查通过：识别到 RIFF/WAVE 头；未能解析 fmt/data 详细参数。", Array.Empty<string>());
        }

        var warnings = new List<string>();
        if (info.AudioFormat != 1)
        {
            warnings.Add($"WAV 编码格式为 {HexDisplayFormatter.FormatWord(info.AudioFormat)}，不是最常见的 PCM(0001)；请确认游戏可播放。");
        }

        var durationText = info.DurationSeconds > 0 ? $"，估计时长 {FormatSeconds(info.DurationSeconds)}" : string.Empty;
        var summary = $"WAV 格式检查通过：{FormatWavCodec(info.AudioFormat)}，{info.Channels} 声道，{info.SampleRate:N0} Hz，{info.BitsPerSample} bit{durationText}。";
        return new FormatCheckResult(summary, warnings);
    }

    private static FormatCheckResult ValidateMp3(string replacementPath)
    {
        var header = ReadPrefix(replacementPath, 4);
        if (header.Length < 3)
        {
            throw new InvalidOperationException("MP3 格式检查失败：来源文件过短。");
        }

        var hasId3 = header[0] == (byte)'I' && header[1] == (byte)'D' && header[2] == (byte)'3';
        var hasFrameSync = header.Length >= 2 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0;
        if (!hasId3 && !hasFrameSync)
        {
            throw new InvalidOperationException("MP3 格式检查失败：未识别 ID3 头或 MPEG 帧同步。");
        }

        var frameInfo = TryReadMp3FrameInfo(replacementPath);
        if (frameInfo == null)
        {
            return new FormatCheckResult(hasId3 ? "MP3 格式检查通过：识别到 ID3 头；未能解析首帧采样率/码率。" : "MP3 格式检查通过：识别到 MPEG 帧同步；未能解析首帧采样率/码率。", Array.Empty<string>());
        }

        var fileSize = new FileInfo(replacementPath).Length;
        var approximateSeconds = frameInfo.BitRateKbps > 0 ? fileSize * 8.0 / (frameInfo.BitRateKbps * 1000.0) : 0;
        var durationText = approximateSeconds > 0 ? $"，估计时长 {FormatSeconds(approximateSeconds)}" : string.Empty;
        return new FormatCheckResult($"MP3 格式检查通过：{frameInfo.Version} {frameInfo.Layer}，{frameInfo.BitRateKbps} kbps，{frameInfo.SampleRate:N0} Hz，{frameInfo.ChannelMode}{durationText}。", Array.Empty<string>());
    }

    private static FormatCheckResult ValidateEex(string replacementPath)
    {
        var header = ReadPrefix(replacementPath, 4);
        if (header.Length < 4 || header[0] != (byte)'E' || header[1] != (byte)'E' || header[2] != (byte)'X' || header[3] != 0)
        {
            throw new InvalidOperationException("EEX 格式检查失败：来源文件缺少 EEX\\0 魔数。");
        }

        return new FormatCheckResult("EEX 格式检查通过：识别到 EEX\\0 魔数。", Array.Empty<string>());
    }

    private static FormatCheckResult ValidateE5(string replacementPath)
    {
        var header = ReadPrefix(replacementPath, 4);
        if (header.Length < 4)
        {
            throw new InvalidOperationException("E5 格式检查失败：来源文件过短。");
        }

        var magic = Encoding.ASCII.GetString(header, 0, Math.Min(4, header.Length));
        if (!magic.StartsWith("Ls1", StringComparison.Ordinal))
        {
            return new FormatCheckResult($"E5 基础检查：未识别 Ls10/Ls11/Ls12 头，头部={BitConverter.ToString(header)}。", new[] { "E5 来源不是 Ls 封装头；如果它是 Data/Imsg/Star 等核心表，仍需确认尺寸和偏移。"});
        }

        return new FormatCheckResult($"E5 格式检查通过：识别到 {magic} 头。", Array.Empty<string>());
    }

    private static FormatCheckResult ValidateScenario(string replacementPath)
    {
        var length = new FileInfo(replacementPath).Length;
        var warnings = new List<string>();
        if (length % 2 != 0)
        {
            warnings.Add("SV/E5S 文件长度不是 2 的倍数；16 位命令流解析可能异常。");
        }

        if (length < 256)
        {
            warnings.Add("SV/E5S 文件很短，可能是索引/配置文件而不是标准关卡。");
        }

        var known = length switch
        {
            263760 => "标准关卡长度 263760 字节",
            277126 => "扩展关卡长度 277126 字节",
            _ => "非标准/未知关卡长度"
        };
        return new FormatCheckResult($"SV/E5S 基础检查完成：{known}，长度 {length:N0} 字节。", warnings);
    }

    private static byte[] ReadPrefix(string path, int count)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[Math.Min(count, checked((int)Math.Min(count, stream.Length)))];
        _ = stream.Read(buffer, 0, buffer.Length);
        return buffer;
    }

    private static string CreateBeforeSaveBackup(CczProject project, string filePath)
    {
        var backupRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
        Directory.CreateDirectory(backupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var safeRelative = MakeSafeRelativeName(project, filePath);
        var backupPath = Path.Combine(backupRoot, $"{stamp}_{safeRelative}");
        var suffix = 1;
        while (File.Exists(backupPath))
        {
            backupPath = Path.Combine(backupRoot, $"{stamp}_{suffix++}_{safeRelative}");
        }
        File.Copy(filePath, backupPath, overwrite: false);
        return backupPath;
    }

    private static string WriteReport(CczProject project, string targetPath, string replacementPath, string backupPath, ReplacementPreviewData preview)
    {
        var reportRoot = Path.Combine(project.GameRoot, "_CCZModStudio_Backups");
        Directory.CreateDirectory(reportRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var reportPath = Path.Combine(reportRoot, $"{stamp}_ResourceReplaceReport.txt");
        var lines = new[]
        {
            "CCZModStudio Resource Replace Report",
            "CreatedAt=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            "GameRoot=" + project.GameRoot,
            "Target=" + targetPath,
            "TargetRelative=" + preview.TargetRelativePath,
            "Replacement=" + replacementPath,
            "Backup=" + backupPath,
            "OldSize=" + preview.OldBytes.LongLength.ToString(CultureInfo.InvariantCulture),
            "NewSize=" + preview.NewBytes.LongLength.ToString(CultureInfo.InvariantCulture),
            "SizeDelta=" + (preview.NewBytes.LongLength - preview.OldBytes.LongLength).ToString(CultureInfo.InvariantCulture),
            "ChangedBytesEstimate=" + preview.ChangedBytes.ToString(CultureInfo.InvariantCulture),
            "OldSHA256=" + preview.OldHash,
            "NewSHA256=" + preview.NewHash,
            "FormatCheck=" + preview.FormatCheck.Summary,
            "FormatWarnings=" + (preview.FormatCheck.Warnings.Count == 0 ? "无" : string.Join(" | ", preview.FormatCheck.Warnings)),
            "RiskSummary=" + preview.RiskSummary,
            string.Empty,
            "说明：这是整文件替换报告。工具不会解析或重封包 EEX/E5/SV 等未知内部结构；如替换后实机异常，请用备份文件恢复。"
        };
        File.WriteAllLines(reportPath, lines, Encoding.UTF8);
        return reportPath;
    }

    private static string MakeSafeRelativeName(CczProject project, string filePath)
    {
        var relative = Path.GetRelativePath(project.GameRoot, filePath);
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            relative = relative.Replace(invalid, '_');
        }
        return relative.Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_');
    }

    private static int EstimateChangedBytes(byte[] oldBytes, byte[] newBytes)
    {
        var count = Math.Abs(oldBytes.Length - newBytes.Length);
        var common = Math.Min(oldBytes.Length, newBytes.Length);
        for (var i = 0; i < common; i++)
        {
            if (oldBytes[i] != newBytes[i]) count++;
        }
        return count;
    }

    private static string ComputeSha256(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }

    private static WavInfo? TryReadWavInfo(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);
            if (stream.Length < 12) return null;
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF") return null;
            _ = reader.ReadUInt32();
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE") return null;

            ushort audioFormat = 0;
            ushort channels = 0;
            uint sampleRate = 0;
            uint byteRate = 0;
            ushort bitsPerSample = 0;
            uint dataSize = 0;

            while (stream.Position + 8 <= stream.Length)
            {
                var chunkId = Encoding.ASCII.GetString(reader.ReadBytes(4));
                var chunkSize = reader.ReadUInt32();
                var chunkStart = stream.Position;
                if (chunkId == "fmt " && chunkSize >= 16)
                {
                    audioFormat = reader.ReadUInt16();
                    channels = reader.ReadUInt16();
                    sampleRate = reader.ReadUInt32();
                    byteRate = reader.ReadUInt32();
                    _ = reader.ReadUInt16();
                    bitsPerSample = reader.ReadUInt16();
                }
                else if (chunkId == "data")
                {
                    dataSize = chunkSize;
                }

                var next = chunkStart + chunkSize + (chunkSize % 2);
                if (next <= chunkStart || next > stream.Length) break;
                stream.Position = next;
            }

            if (channels == 0 || sampleRate == 0) return null;
            var durationSeconds = byteRate > 0 && dataSize > 0 ? dataSize / (double)byteRate : 0;
            return new WavInfo(audioFormat, channels, sampleRate, byteRate, bitsPerSample, dataSize, durationSeconds);
        }
        catch
        {
            return null;
        }
    }

    private static Mp3FrameInfo? TryReadMp3FrameInfo(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            var offset = 0;
            if (bytes.Length >= 10 && bytes[0] == (byte)'I' && bytes[1] == (byte)'D' && bytes[2] == (byte)'3')
            {
                var tagSize = ((bytes[6] & 0x7F) << 21) | ((bytes[7] & 0x7F) << 14) | ((bytes[8] & 0x7F) << 7) | (bytes[9] & 0x7F);
                offset = 10 + tagSize;
            }

            for (var i = Math.Max(0, offset); i + 3 < bytes.Length; i++)
            {
                if (bytes[i] != 0xFF || (bytes[i + 1] & 0xE0) != 0xE0) continue;

                var versionBits = (bytes[i + 1] >> 3) & 0x03;
                var layerBits = (bytes[i + 1] >> 1) & 0x03;
                var bitrateIndex = (bytes[i + 2] >> 4) & 0x0F;
                var sampleRateIndex = (bytes[i + 2] >> 2) & 0x03;
                var channelModeIndex = (bytes[i + 3] >> 6) & 0x03;

                if (versionBits == 1 || layerBits == 0 || bitrateIndex is 0 or 15 || sampleRateIndex == 3) continue;

                var version = versionBits switch
                {
                    3 => "MPEG1",
                    2 => "MPEG2",
                    0 => "MPEG2.5",
                    _ => "MPEG?"
                };
                var layer = layerBits switch
                {
                    3 => "Layer I",
                    2 => "Layer II",
                    1 => "Layer III",
                    _ => "Layer ?"
                };
                var bitrate = LookupMp3BitrateKbps(versionBits, layerBits, bitrateIndex);
                var sampleRate = LookupMp3SampleRate(versionBits, sampleRateIndex);
                if (bitrate <= 0 || sampleRate <= 0) continue;

                var channelMode = channelModeIndex switch
                {
                    0 => "立体声",
                    1 => "联合立体声",
                    2 => "双声道",
                    3 => "单声道",
                    _ => "未知声道"
                };
                return new Mp3FrameInfo(version, layer, bitrate, sampleRate, channelMode);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static int LookupMp3BitrateKbps(int versionBits, int layerBits, int index)
    {
        // layerBits: 3=Layer I, 2=Layer II, 1=Layer III
        var mpeg1 = versionBits == 3;
        return (mpeg1, layerBits) switch
        {
            (true, 3) => new[] { 0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448 }[index],
            (true, 2) => new[] { 0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384 }[index],
            (true, 1) => new[] { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320 }[index],
            (false, 3) => new[] { 0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256 }[index],
            (false, 2 or 1) => new[] { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 }[index],
            _ => 0
        };
    }

    private static int LookupMp3SampleRate(int versionBits, int sampleRateIndex)
    {
        return versionBits switch
        {
            3 => new[] { 44100, 48000, 32000 }[sampleRateIndex],
            2 => new[] { 22050, 24000, 16000 }[sampleRateIndex],
            0 => new[] { 11025, 12000, 8000 }[sampleRateIndex],
            _ => 0
        };
    }

    private static string FormatWavCodec(ushort audioFormat)
    {
        return audioFormat switch
        {
            1 => "PCM",
            3 => "IEEE Float",
            6 => "A-law",
            7 => "μ-law",
            0x0055 => "MP3-in-WAV",
            _ => $"编码 {HexDisplayFormatter.FormatWord(audioFormat)}"
        };
    }

    private static string FormatSeconds(double seconds)
    {
        if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return "未知";
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1
            ? span.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : span.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    private static string BuildRiskSummary(string targetPath, string oldHash, string newHash, long oldSize, long newSize, int changedBytes, FormatCheckResult formatCheck)
    {
        var risks = new List<string>();
        var fileName = Path.GetFileName(targetPath);
        var extension = Path.GetExtension(targetPath).ToLowerInvariant();

        if (string.Equals(oldHash, newHash, StringComparison.OrdinalIgnoreCase))
        {
            risks.Add("来源文件与目标文件 SHA256 完全一致，替换后不会产生内容变化。");
        }

        if (ProjectVersionGuardService.Expected65CoreSizes.TryGetValue(fileName, out var expectedCoreSize))
        {
            var sizeText = newSize == expectedCoreSize
                ? "来源尺寸与 6.5 参考尺寸一致。"
                : $"来源尺寸 {newSize} 字节与 6.5 参考尺寸 {expectedCoreSize} 字节不一致。";
            risks.Add("目标是固定偏移核心文件；写入保护已取消，不再因尺寸差异拒绝整文件替换。" + sizeText);
        }

        if (extension is ".eex" or ".e5")
        {
            risks.Add("目标属于曹操传封包/压缩资源；当前只做整文件替换，不解析内部条目，也不重封包。");
        }
        else if (extension.Equals(".e5s", StringComparison.OrdinalIgnoreCase))
        {
            risks.Add("目标是 SV/E5S 关卡剧本；当前只做整文件替换，完整命令树和文本扩容仍需实机验证。");
        }

        if (oldSize != newSize)
        {
            risks.Add($"文件大小将变化 {newSize - oldSize:+#;-#;0} 字节；若该资源被引擎按固定长度读取，需要重点实机测试。");
        }

        if (formatCheck.Warnings.Count > 0)
        {
            risks.Add("格式检查存在警告：" + string.Join("；", formatCheck.Warnings));
        }

        if (changedBytes > 0 && risks.Count == 0)
        {
            risks.Add("未发现明显格式风险；仍建议替换后核对资源格式并进入游戏实测。");
        }

        return risks.Count == 0
            ? "未发现内容变化；无需替换，除非只是为了刷新备份/报告。"
            : string.Join("；", risks);
    }

    private sealed record FormatCheckResult(string Summary, IReadOnlyList<string> Warnings);
    private sealed record WavInfo(ushort AudioFormat, ushort Channels, uint SampleRate, uint ByteRate, ushort BitsPerSample, uint DataSize, double DurationSeconds);
    private sealed record Mp3FrameInfo(string Version, string Layer, int BitRateKbps, int SampleRate, string ChannelMode);

    private sealed record ReplacementPreviewData(
        string TargetPath,
        string ReplacementPath,
        string TargetRelativePath,
        string Extension,
        byte[] OldBytes,
        byte[] NewBytes,
        string OldHash,
        string NewHash,
        int ChangedBytes,
        FormatCheckResult FormatCheck,
        string RiskSummary)
    {
        public ResourceReplacePreviewResult ToPreviewResult()
        {
            return new ResourceReplacePreviewResult
            {
                TargetPath = TargetPath,
                TargetRelativePath = TargetRelativePath,
                ReplacementPath = ReplacementPath,
                Extension = Extension,
                OldSizeBytes = OldBytes.LongLength,
                NewSizeBytes = NewBytes.LongLength,
                ChangedBytesEstimate = ChangedBytes,
                OldSha256 = OldHash,
                NewSha256 = NewHash,
                FormatCheckSummary = FormatCheck.Summary,
                FormatWarnings = FormatCheck.Warnings,
                RiskSummary = RiskSummary
            };
        }
    }
}
