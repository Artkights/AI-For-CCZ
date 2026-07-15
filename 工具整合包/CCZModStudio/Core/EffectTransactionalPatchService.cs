using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class EffectTransactionalPatchService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public EffectPatchPreviewResult Preview(CczProject project, EffectPackage package)
    {
        var result = new EffectPatchPreviewResult { CanApply = package.PatchSegments.Count > 0 };
        if (package.PatchSegments.Count == 0) result.Warnings.Add("补丁包没有写入段。");
        try { new CodeCaveRegistry().EnsureNoPatchSegmentOverlap(package.PatchSegments); }
        catch (Exception ex) { result.Warnings.Add(ex.Message); }

        foreach (var group in package.PatchSegments.GroupBy(segment =>
                     string.IsNullOrWhiteSpace(segment.TargetFile) ? "Ekd5.exe" : segment.TargetFile,
                     StringComparer.OrdinalIgnoreCase))
        {
            var path = ResolveTargetPath(project, group.Key);
            var wholeFile = group.All(segment => IsWholeFile(segment) || IsDeleteFile(segment));
            if (!File.Exists(path) && !wholeFile)
            {
                result.Warnings.Add("目标文件不存在：" + group.Key);
                continue;
            }
            var file = File.Exists(path) ? File.ReadAllBytes(path) : [];
            ExeCodeCaveScanner.PeImage? pe = null;
            foreach (var segment in group)
            {
                try
                {
                    var next = EffectPatchByteService.ParseHex(segment.BytesHex);
                    var expected = EffectPatchByteService.ParseHex(segment.ExpectedOldBytesHex);
                    var wholeSegment = IsWholeFile(segment) || IsDeleteFile(segment);
                    var offset = wholeSegment ? 0 : ResolveOffset(file, segment, ref pe);
                    if (!wholeSegment) ValidateExecutablePatchRange(group.Key, file, segment, offset, next.Length, ref pe);
                    if (!IsDeleteFile(segment) && next.Length == 0 || offset < 0 || !wholeSegment && offset + next.Length > file.LongLength)
                        throw new InvalidOperationException("写入范围越界或没有字节内容。");
                    var old = wholeSegment ? file : file.AsSpan(checked((int)offset), next.Length).ToArray();
                    if (expected.Length > 0 && !old.SequenceEqual(expected))
                        throw new InvalidOperationException("旧字节与预览锁不一致。");
                    result.Segments.Add(new EffectPackageChangePreview
                    {
                        Category = "特效事务写入段",
                        Target = group.Key,
                        Field = string.IsNullOrWhiteSpace(segment.HookPoint) ? segment.Comment : segment.HookPoint,
                        OldValue = EffectPatchByteService.ToHex(old),
                        NewValue = EffectPatchByteService.ToHex(next),
                        Changed = !old.SequenceEqual(next),
                        Note = $"文件偏移 0x{offset:X}，长度 {next.Length} 字节。"
                    });
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"{group.Key} 的写入段“{segment.HookPoint}”不能应用：{ex.Message}");
                }
            }
        }
        try
        {
            result.WriteDecisions = new EffectWriteAuthorizationService().AuthorizePreview(project, package).ToList();
            result.WriteAuthorization = package.WriteAuthorization;
            foreach (var decision in result.WriteDecisions.Where(item => !item.CanApply))
            {
                var blockers = decision.BlockerCodes.Count > 0
                    ? string.Join(",", decision.BlockerCodes)
                    : "WRITE_AUTHORIZATION_DENIED";
                result.Warnings.Add($"写入权限未通过 [{blockers}]：{string.Join("；", decision.ReasonsZh)}");
            }
        }
        catch (Exception ex)
        {
            result.Warnings.Add("无法生成结构化写入授权：" + ex.Message);
        }
        result.CanApply = result.CanApply && result.Warnings.Count == 0 &&
                          result.Segments.Count == package.PatchSegments.Count &&
                          result.WriteAuthorization != null &&
                          result.WriteDecisions.Count == package.PatchSegments.Count &&
                          result.WriteDecisions.All(item => item.CanApply);
        result.Summary = result.CanApply
            ? $"特效事务预览通过，共 {result.Segments.Count} 个锁定写入段。"
            : "特效事务预览未通过：" + string.Join("；", result.Warnings.Take(8));
        return result;
    }

    public EffectTransactionalApplyResult Apply(
        CczProject project,
        EffectPackage package,
        string manifestKind,
        string? expectedReceiptOperationKind = "__manifest_kind__")
    {
        using var projectLock = ProjectEffectWriteLock.Acquire(project.GameRoot);
        new EffectReleaseConsistencyService().EnsureWriteAllowed(forceRefresh: true);
        var targets = package.PatchSegments
            .GroupBy(segment => string.IsNullOrWhiteSpace(segment.TargetFile) ? "Ekd5.exe" : segment.TargetFile,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var originals = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var outputs = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var originallyExisted = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var backups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var diskIdentities = new Dictionary<string, FileDiskIdentity>(StringComparer.OrdinalIgnoreCase);
        var changedBytes = 0;

        foreach (var pair in targets)
        {
            var path = ResolveTargetPath(project, pair.Key);
            if (!pair.Value.All(segment => IsWholeFile(segment) || IsDeleteFile(segment))) ProjectVersionGuardService.EnsureCoreFileCompatibleForWrite(project, pair.Key);
            var existed = File.Exists(path);
            var original = existed ? File.ReadAllBytes(path) : [];
            PerformanceMetrics.Increment("EffectTransaction.ApplyFullReadCount");
            PerformanceMetrics.Increment("EffectTransaction.ApplyFullReadBytes", original.Length);
            diskIdentities[path] = FileDiskIdentity.Capture(path);
            originals[path] = original;
            originallyExisted[path] = existed;
        }

        var exePath = project.ResolveGameFile("Ekd5.exe");
        if (!originals.TryGetValue(exePath, out var currentExeBytes))
        {
            currentExeBytes = File.ReadAllBytes(exePath);
            PerformanceMetrics.Increment("EffectTransaction.ApplyFullReadCount");
            PerformanceMetrics.Increment("EffectTransaction.ApplyFullReadBytes", currentExeBytes.Length);
            diskIdentities[exePath] = FileDiskIdentity.Capture(exePath);
        }
        var audit = new ExecutableProfileAuditService().AuditBytes(currentExeBytes);
        var writable = new EffectWritableProfileService().EvaluateFromAudit(project, audit);
        var trustedRepair = new TrustedEffectRepairService().Evaluate(project, package, currentExeBytes).IsTrusted;
        var verifiedIdentity = new ProjectPatchIdentityService().BuildKnown(
            project, "Ekd5.exe", currentExeBytes.LongLength, audit.CurrentSha256, writable.ProfileId);
        var legacySafeRemoval = package.WriteAuthorization == null && IsLegacySafeRemoval(package);
        if (package.WriteAuthorization != null)
            new EffectWriteAuthorizationService().ValidateApply(project, package, audit, writable, verifiedIdentity, currentExeBytes);
        else if (!legacySafeRemoval)
            throw new InvalidOperationException("补丁包缺少结构化写入授权；请重新预览。旧包只允许按已锁定旧字节安全删除。");
        var localProfile = new LocalEffectProfileService().FindVerified(project, audit.CurrentSha256);
        var sandboxAuthorized = package.WriteAuthorization?.SandboxOnly == true && EffectSandboxService.IsSandbox(project);
        if (!writable.CanWrite && localProfile == null && !sandboxAuthorized && !legacySafeRemoval && !trustedRepair)
            throw new InvalidOperationException("当前 EXE 不属于允许写入的 6.5 特效档案：" + writable.ReasonZh);
        if (package.Metadata.TryGetValue("EngineProfileSha256", out var expectedSha) &&
            !string.IsNullOrWhiteSpace(expectedSha) && !expectedSha.Equals(audit.CurrentSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("当前 EXE 与预览时的完整摘要不一致，请重新预览。");
        if (package.Metadata.TryGetValue("ProfileAuditHash", out var expectedAudit) &&
            !string.IsNullOrWhiteSpace(expectedAudit) && !expectedAudit.Equals(audit.AuditSummaryHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("当前 EXE 的规范化档案审计已在预览后变化，请重新预览。");
        new LockedEffectWriteReceiptService().ValidateAndConsume(
            project,
            package,
            expectedReceiptOperationKind == "__manifest_kind__" ? manifestKind : expectedReceiptOperationKind,
            verifiedIdentity);

        foreach (var pair in targets)
        {
            var path = ResolveTargetPath(project, pair.Key);
            var original = originals[path];
            var output = (byte[])original.Clone();
            ExeCodeCaveScanner.PeImage? pe = null;
            var intervals = new List<(long Start, long End)>();
            foreach (var segment in pair.Value)
            {
                var next = EffectPatchByteService.ParseHex(segment.BytesHex);
                var expected = EffectPatchByteService.ParseHex(segment.ExpectedOldBytesHex);
                if (IsWholeFile(segment) || IsDeleteFile(segment))
                {
                    if (expected.Length > 0 && !original.SequenceEqual(expected)) throw new InvalidOperationException($"{pair.Key} 的整文件旧内容已变化，请重新预览。");
                    output = next;
                    continue;
                }
                var offset = ResolveOffset(original, segment, ref pe);
                ValidateExecutablePatchRange(pair.Key, original, segment, offset, next.Length, ref pe);
                if (offset < 0 || offset + next.Length > output.LongLength) throw new InvalidOperationException("特效写入段超出文件范围。");
                var end = checked(offset + next.Length);
                if (intervals.Any(item => offset < item.End && end > item.Start))
                    throw new InvalidOperationException($"{pair.Key} 包含相互重叠的写入段，事务已阻断。");
                intervals.Add((offset, end));
                var current = original.AsSpan(checked((int)offset), next.Length).ToArray();
                if (expected.Length > 0 && !current.SequenceEqual(expected))
                {
                    throw new InvalidOperationException($"{pair.Key} 的旧字节已变化：{segment.HookPoint} @0x{offset:X}，" +
                                                        $"预期 {EffectPatchByteService.ToHex(expected)}，当前 {EffectPatchByteService.ToHex(current)}；请重新扫描和预览。");
                }
                next.CopyTo(output, checked((int)offset));
            }
            changedBytes += original.Zip(output).Count(item => item.First != item.Second);
            outputs[path] = output;
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var backupRoot = Path.Combine(ProjectBackupPathService.EnsureBackupRootWritable(project), "EffectTransactions", stamp);
        Directory.CreateDirectory(backupRoot);
        try
        {
            foreach (var pair in originals)
            {
                var backup = Path.Combine(backupRoot, Path.GetFileName(pair.Key));
                File.WriteAllBytes(backup, pair.Value);
                backups[pair.Key] = backup;
            }

            foreach (var pair in outputs)
            {
                var targetKey = targets.Keys.First(key => ResolveTargetPath(project, key).Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
                if (targets[targetKey].Any(IsDeleteFile))
                {
                    EnsureUnchangedSinceRead(pair.Key, diskIdentities[pair.Key]);
                    File.Delete(pair.Key);
                    continue;
                }
                EnsureUnchangedSinceRead(pair.Key, diskIdentities[pair.Key]);
                Directory.CreateDirectory(Path.GetDirectoryName(pair.Key)!);
                var temp = pair.Key + ".ccz-effect-" + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(temp, pair.Value);
                File.Move(temp, pair.Key, overwrite: true);
                PerformanceMetrics.Increment("EffectTransaction.ApplyWriteCount");
            }

            var verified = VerifyWritten(project, package);
            foreach (var path in outputs.Keys.Where(path => verified.ContainsKey(path)))
            {
                if (!verified[path].SequenceEqual(outputs[path]))
                    throw new InvalidOperationException("特效事务写后完整内容与内存事务副本不一致，已开始恢复备份。");
            }
        }
        catch
        {
            foreach (var pair in backups)
            {
                try
                {
                    if (!originallyExisted[pair.Key]) File.Delete(pair.Key);
                    else File.Copy(pair.Value, pair.Key, overwrite: true);
                }
                catch { }
            }
            throw;
        }

        var manifestRoot = ProjectPatchIdentityService.EffectManifestRoot(project);
        Directory.CreateDirectory(manifestRoot);
        var manifestPath = Path.Combine(manifestRoot, $"{manifestKind}-{package.EffectId:X2}-{stamp}.json");
        var finalExeBytes = outputs.GetValueOrDefault(exePath) ?? currentExeBytes;
        var currentExeSha = Convert.ToHexString(SHA256.HashData(finalExeBytes));
        var manifest = new EffectManifest
        {
            ManifestId = Path.GetFileNameWithoutExtension(manifestPath),
            ProjectRoot = project.GameRoot,
            ProjectIdentity = new ProjectPatchIdentityService().BuildKnown(
                project, "Ekd5.exe", finalExeBytes.LongLength, currentExeSha, writable.ProfileId),
            Mode = "transactional-patch",
            Domain = package.Domain,
            EffectId = package.EffectId,
            PackageId = package.PackageId,
            Package = package,
            BackupNote = "多文件特效事务写入；所有目标已在写入前统一备份。",
            BackupPaths = backups.Values.ToList(),
            Backups = backups.Select(pair => new EffectManifestBackup
            {
                TargetPath = pair.Key,
                TargetRelativePath = Path.GetRelativePath(project.GameRoot, pair.Key),
                BackupPath = pair.Value,
                Category = manifestKind,
                TargetExisted = originallyExisted[pair.Key]
            }).ToList(),
            Metadata =
            {
                ["ManifestKind"] = manifestKind,
                ["EngineProfileSha256Before"] = audit.CurrentSha256,
                ["EngineProfileSha256After"] = currentExeSha,
                ["WritableProfileId"] = writable.ProfileId,
                ["WriteAuthorizationId"] = package.WriteAuthorization?.AuthorizationId ?? "legacy-safe-removal",
                ["WriteAuthorizationHash"] = package.WriteAuthorization?.DecisionHash ?? string.Empty,
                ["ProfileTrustTier"] = package.WriteAuthorization?.ProfileTrustTier ?? EffectProfileTrustTier.ReadOnlyUnknown,
                ["NormalizedProfileIdentity"] = audit.NormalizedProfileIdentity,
                ["ProfileAuditHash"] = audit.AuditSummaryHash,
                ["ChangedBytes"] = changedBytes.ToString(CultureInfo.InvariantCulture)
            }
        };
        UserBoundSignatureService.Sign(manifest, static (item, value) => item.Signature = value);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), Encoding.UTF8);
        InvalidateWrittenResources(project, outputs.Keys.Append(manifestPath));
        return new EffectTransactionalApplyResult
        {
            Applied = true,
            SummaryZh = $"特效事务写入完成，共修改 {changedBytes} 个字节，已复读校验。",
            ManifestPath = manifestPath,
            BackupPaths = backups.Values.ToList(),
            Backups = manifest.Backups.ToList(),
            ChangedBytes = changedBytes
        };
    }

    public void Restore(CczProject project, EffectTransactionalApplyResult result)
    {
        if (!result.Applied || result.Backups.Count == 0)
            throw new InvalidOperationException("事务结果没有可用于恢复的完整备份记录。");

        var gameRoot = Path.GetFullPath(project.GameRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var backup in result.Backups)
        {
            var target = Path.GetFullPath(backup.TargetPath);
            if (!target.StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("备份记录的目标文件不属于当前项目，已拒绝恢复。");
            if (backup.TargetExisted)
            {
                if (!File.Exists(backup.BackupPath))
                    throw new InvalidOperationException("事务备份文件缺失，无法自动恢复：" + backup.TargetRelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(backup.BackupPath, target, overwrite: true);
            }
            else if (File.Exists(target))
            {
                File.Delete(target);
            }
        }

        if (!string.IsNullOrWhiteSpace(result.ManifestPath) && File.Exists(result.ManifestPath))
            File.Delete(result.ManifestPath);
        EffectInventoryService.Invalidate(project);
        InvalidateWrittenResources(project, result.Backups.Select(item => item.TargetPath));
    }

    private static void InvalidateWrittenResources(CczProject project, IEnumerable<string> paths)
    {
        var normalized = paths.Select(ProjectResourceFingerprint.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var executable = normalized.Where(path => Path.GetFileName(path).Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase)).ToArray();
        var release = normalized.Where(IsReleaseComponent).ToArray();
        var metadata = normalized.Where(IsEffectMetadata).Except(release, StringComparer.OrdinalIgnoreCase).ToArray();
        var tables = normalized.Except(executable, StringComparer.OrdinalIgnoreCase)
            .Except(release, StringComparer.OrdinalIgnoreCase).Except(metadata, StringComparer.OrdinalIgnoreCase).ToArray();
        if (executable.Length > 0) ProjectResourceInvalidationBus.Publish(executable, ProjectResourceKind.Executable);
        if (tables.Length > 0) ProjectResourceInvalidationBus.Publish(tables, ProjectResourceKind.HexTable);
        if (metadata.Length > 0) ProjectResourceInvalidationBus.Publish(metadata, ProjectResourceKind.EffectMetadata);
        if (release.Length > 0)
        {
            ProjectResourceInvalidationBus.Publish(release, ProjectResourceKind.ReleaseComponents);
            EffectReleaseConsistencyService.Invalidate();
        }
        foreach (var path in executable)
        {
            CczEngineProfileService.Invalidate(path);
            InjectedEffectDiscoveryService.Invalidate(path);
        }
        if (executable.Length > 0)
        {
            ExecutableAnalysisSnapshotCache.Shared.Invalidate(executable);
            ExecutableProfileAuditService.Invalidate(executable);
            EffectAnalysisCoordinator.Shared.Invalidate(executable);
        }
        EffectInventoryService.Invalidate(project);
    }

    private static long ResolveOffset(byte[] image, EffectPatchSegment segment, ref ExeCodeCaveScanner.PeImage? pe)
    {
        var kind = (segment.AddressKind ?? string.Empty).Trim();
        if (kind.Equals("FileOffset", StringComparison.OrdinalIgnoreCase) || kind.Equals("UeFileOffset", StringComparison.OrdinalIgnoreCase))
            return segment.Address;
        pe ??= ExeCodeCaveScanner.ParsePe(image);
        if (segment.Address < pe.ImageBase) throw new InvalidOperationException("虚拟地址小于 ImageBase。");
        var rva = segment.Address - pe.ImageBase;
        foreach (var section in pe.Sections)
        {
            var size = Math.Max(section.VirtualSize, section.RawSize);
            if (rva >= section.VirtualAddress && rva < section.VirtualAddress + size)
                return checked((long)section.RawPointer + rva - section.VirtualAddress);
        }
        throw new InvalidOperationException($"无法映射虚拟地址 0x{segment.Address:X8}。");
    }

    private static void ValidateExecutablePatchRange(
        string target,
        byte[] image,
        EffectPatchSegment segment,
        long offset,
        int length,
        ref ExeCodeCaveScanner.PeImage? pe)
    {
        if (!Path.GetFileName(target).Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase)) return;
        if (length <= 0 || offset < 0) throw new InvalidOperationException("EXE 写入段没有有效范围。");

        pe ??= ExeCodeCaveScanner.ParsePe(image);
        var end = checked(offset + length);
        var section = pe.Sections.FirstOrDefault(item =>
        {
            var start = (long)item.RawPointer;
            var sectionEnd = checked(start + item.RawSize);
            return offset >= start && end <= sectionEnd;
        });
        if (section == null)
            throw new InvalidOperationException("EXE 写入段不属于任何 PE 节，可能落入 PE 文件头或未登记区域，已阻断。");
        if (IsRegisteredDataField(segment, offset, length)) return;
        if (!section.IsExecutable)
            throw new InvalidOperationException($"EXE 写入段落在非可执行节 {section.Name}，可能涉及导入表、重定位表或未知数据，已阻断。");
    }

    private static bool IsRegisteredDataField(EffectPatchSegment segment, long offset, int length)
    {
        if (!segment.RequiredCapability.Equals(EffectWriteCapability.RegisteredData, StringComparison.OrdinalIgnoreCase))
            return false;
        var field = EngineEffectProfileRegistry.Current65.RegisteredFields.FirstOrDefault(item =>
            item.FileOffset == offset && item.Width == length);
        if (field == null) return false;

        var bytes = EffectPatchByteService.ParseHex(segment.BytesHex);
        var hex = Convert.ToHexString(bytes);
        if (field.AllowedValuesHex.Count > 0 &&
            !field.AllowedValuesHex.Contains(hex, StringComparer.OrdinalIgnoreCase)) return false;
        ulong value = 0;
        for (var index = 0; index < bytes.Length; index++) value |= (ulong)bytes[index] << (index * 8);
        if (field.AllowedBitMask.HasValue && (value & ~field.AllowedBitMask.Value) != 0) return false;
        if (field.Minimum.HasValue && value < (ulong)field.Minimum.Value) return false;
        if (field.Maximum.HasValue && value > (ulong)field.Maximum.Value) return false;
        return true;
    }

    private static Dictionary<string, byte[]> VerifyWritten(CczProject project, EffectPackage package)
    {
        var verified = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in package.PatchSegments.GroupBy(segment => segment.TargetFile, StringComparer.OrdinalIgnoreCase))
        {
            var target = string.IsNullOrWhiteSpace(group.Key) ? "Ekd5.exe" : group.Key;
            var path = ResolveTargetPath(project, target);
            if (group.All(IsDeleteFile))
            {
                if (File.Exists(path)) throw new InvalidOperationException("特效事务删除文件后复读失败，已开始恢复备份。");
                continue;
            }
            var bytes = File.ReadAllBytes(path);
            PerformanceMetrics.Increment("EffectTransaction.VerifyFullReadCount");
            PerformanceMetrics.Increment("EffectTransaction.VerifyFullReadBytes", bytes.Length);
            verified[path] = bytes;
            ExeCodeCaveScanner.PeImage? pe = null;
            foreach (var segment in group)
            {
                var expected = EffectPatchByteService.ParseHex(segment.BytesHex);
                var offset = IsWholeFile(segment) ? 0 : ResolveOffset(bytes, segment, ref pe);
                if (IsWholeFile(segment) ? !bytes.SequenceEqual(expected) : !bytes.AsSpan(checked((int)offset), expected.Length).SequenceEqual(expected))
                    throw new InvalidOperationException("特效事务写后复读失败，已开始恢复备份。");
            }
        }
        return verified;
    }

    private static void EnsureUnchangedSinceRead(string path, FileDiskIdentity identity)
    {
        var current = FileDiskIdentity.Capture(path);
        if (current != identity) throw new InvalidOperationException("目标文件在事务读取后被外部修改，已在写盘前中止。" + path);
    }

    private static bool IsEffectMetadata(string path)
        => path.Contains("CCZModStudio_Notes", StringComparison.OrdinalIgnoreCase) ||
           Path.GetFileName(path).Contains("manifest", StringComparison.OrdinalIgnoreCase);

    private static bool IsReleaseComponent(string path)
        => Path.GetFileName(path).Equals(EffectReleaseConsistencyService.ManifestFileName, StringComparison.OrdinalIgnoreCase) ||
           path.EndsWith("CCZModStudio.dll", StringComparison.OrdinalIgnoreCase) || path.EndsWith("CCZModStudio.Runtime.dll", StringComparison.OrdinalIgnoreCase) ||
           path.EndsWith("CCZModStudio.McpServer.dll", StringComparison.OrdinalIgnoreCase) || path.EndsWith("CCZModStudio.GameDebugMcpServer.dll", StringComparison.OrdinalIgnoreCase);

    private readonly record struct FileDiskIdentity(bool Exists, long Length, long LastWriteTimeUtcTicks)
    {
        public static FileDiskIdentity Capture(string path)
        {
            var info = new FileInfo(path);
            return new FileDiskIdentity(info.Exists, info.Exists ? info.Length : -1, info.Exists ? info.LastWriteTimeUtc.Ticks : 0);
        }
    }

    private static bool IsWholeFile(EffectPatchSegment segment)
        => segment.AddressKind.Equals("WholeFile", StringComparison.OrdinalIgnoreCase);

    private static bool IsDeleteFile(EffectPatchSegment segment)
        => segment.AddressKind.Equals("DeleteFile", StringComparison.OrdinalIgnoreCase);

    private static bool IsLegacySafeRemoval(EffectPackage package)
    {
        var kind = package.Metadata.GetValueOrDefault("LogicalPatchKind", string.Empty);
        return kind.Contains("remove", StringComparison.OrdinalIgnoreCase) &&
               package.PatchSegments.Count > 0 &&
               package.PatchSegments.All(segment => !string.IsNullOrWhiteSpace(segment.ExpectedOldBytesHex));
    }

    private static string ResolveTargetPath(CczProject project, string target)
        => Path.IsPathRooted(target) ? target : project.ResolveGameFile(target);
}
