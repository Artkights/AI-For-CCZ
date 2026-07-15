using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

internal sealed class TrustedEffectRepairService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public TrustedEffectRepairResult Evaluate(CczProject project, EffectPackage package, byte[]? currentExeBytes = null)
    {
        if (!package.Metadata.GetValueOrDefault("LogicalPatchKind", string.Empty)
                .Equals("composite-effect-repair", StringComparison.OrdinalIgnoreCase))
            return TrustedEffectRepairResult.Denied("不是受支持的复合特效修复事务。");
        if (package.PatchSegments.Count == 0)
            return TrustedEffectRepairResult.Denied("修复事务没有锁定写入段。");

        CompositeEffectManifest composite;
        try
        {
            composite = new CompositeEffectService().Read(
                project, package.Metadata.GetValueOrDefault("SourceCompositeManifest", string.Empty));
        }
        catch (Exception ex)
        {
            return TrustedEffectRepairResult.Denied("无法读取复合安装记录：" + ex.Message);
        }
        if (!UserBoundSignatureService.Verify(composite, static item => item.Signature, static (item, value) => item.Signature = value))
            return TrustedEffectRepairResult.Denied("复合安装记录缺少有效的当前用户签名。");

        if (!TryReadSignedTransaction(project, composite, out var transaction, out var reason))
            return TrustedEffectRepairResult.Denied(reason);
        if (transaction == null)
            return TrustedEffectRepairResult.Denied("签名事务记录为空。");

        foreach (var repair in package.PatchSegments)
        {
            var installed = composite.Package.PatchSegments.FirstOrDefault(source => SamePhysicalRange(source, repair));
            if (installed == null ||
                !installed.BytesHex.Equals(repair.BytesHex, StringComparison.OrdinalIgnoreCase) ||
                !installed.ExpectedOldBytesHex.Equals(repair.ExpectedOldBytesHex, StringComparison.OrdinalIgnoreCase))
                return TrustedEffectRepairResult.Denied("修复段与签名安装事务的地址或旧/新字节不一致。");
        }

        var exe = currentExeBytes ?? ExecutableAnalysisSnapshotCache.Shared.GetBase(project).Bytes;
        var normalized = (byte[])exe.Clone();
        ExeCodeCaveScanner.PeImage? pe = null;
        foreach (var segment in composite.Package.PatchSegments.Where(IsExecutableSegment))
        {
            if (segment.AddressKind.Equals("WholeFile", StringComparison.OrdinalIgnoreCase) ||
                segment.AddressKind.Equals("DeleteFile", StringComparison.OrdinalIgnoreCase))
                return TrustedEffectRepairResult.Denied("签名事务包含不支持自动修复的整文件 EXE 操作。");
            var installed = EffectPatchByteService.ParseHex(segment.BytesHex);
            var original = EffectPatchByteService.ParseHex(segment.ExpectedOldBytesHex);
            var offset = ResolveOffset(normalized, segment, ref pe);
            if (offset < 0 || offset + installed.Length > normalized.LongLength || installed.Length != original.Length)
                return TrustedEffectRepairResult.Denied("签名安装段的 EXE 范围无效。");
            var current = normalized.AsSpan(checked((int)offset), installed.Length);
            if (!current.SequenceEqual(installed) && !current.SequenceEqual(original))
                return TrustedEffectRepairResult.Denied("当前 EXE 在受管段内包含既非安装字节也非安装前字节的外部修改。");
            installed.CopyTo(normalized, checked((int)offset));
        }

        var expectedInstalledSha = composite.ExeSha256After;
        var normalizedSha = Convert.ToHexString(SHA256.HashData(normalized));
        if (string.IsNullOrWhiteSpace(expectedInstalledSha) ||
            !normalizedSha.Equals(expectedInstalledSha, StringComparison.OrdinalIgnoreCase))
            return TrustedEffectRepairResult.Denied("当前 EXE 不能仅通过签名受管段归一化为原安装后身份。");

        return new TrustedEffectRepairResult(true, transaction.ManifestId, expectedInstalledSha,
            "修复范围、项目身份、签名事务和安装后 EXE 身份均已验证。");
    }

    private static bool TryReadSignedTransaction(
        CczProject project,
        CompositeEffectManifest composite,
        out EffectManifest? transaction,
        out string reason)
    {
        transaction = null;
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(composite.EffectManifestPath))
        {
            reason = "复合安装记录没有关联事务 manifest。";
            return false;
        }
        var root = Path.GetFullPath(ProjectPatchIdentityService.EffectManifestRoot(project))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(composite.EffectManifestPath);
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            reason = "关联事务 manifest 不属于当前项目或文件缺失。";
            return false;
        }
        try
        {
            transaction = JsonSerializer.Deserialize<EffectManifest>(File.ReadAllText(path, Encoding.UTF8), JsonOptions);
        }
        catch (Exception ex)
        {
            reason = "关联事务 manifest 无法解析：" + ex.Message;
            return false;
        }
        if (transaction == null ||
            !UserBoundSignatureService.Verify(transaction, static item => item.Signature, static (item, value) => item.Signature = value))
        {
            reason = "关联事务 manifest 缺少有效的当前用户签名。";
            return false;
        }
        if (!new ProjectPatchIdentityService().Matches(project, transaction.ProjectIdentity, transaction.ProjectRoot) ||
            !transaction.Package.PackageId.Equals(composite.Package.PackageId, StringComparison.OrdinalIgnoreCase))
        {
            reason = "关联事务 manifest 的项目或包身份不匹配。";
            return false;
        }
        return true;
    }

    private static bool SamePhysicalRange(EffectPatchSegment left, EffectPatchSegment right)
        => (string.IsNullOrWhiteSpace(left.TargetFile) ? "Ekd5.exe" : left.TargetFile)
               .Equals(string.IsNullOrWhiteSpace(right.TargetFile) ? "Ekd5.exe" : right.TargetFile, StringComparison.OrdinalIgnoreCase) &&
           left.AddressKind.Equals(right.AddressKind, StringComparison.OrdinalIgnoreCase) &&
           left.Address == right.Address;

    private static bool IsExecutableSegment(EffectPatchSegment segment)
        => Path.GetFileName(string.IsNullOrWhiteSpace(segment.TargetFile) ? "Ekd5.exe" : segment.TargetFile)
            .Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase);

    private static long ResolveOffset(byte[] image, EffectPatchSegment segment, ref ExeCodeCaveScanner.PeImage? pe)
    {
        if (segment.AddressKind.Equals("FileOffset", StringComparison.OrdinalIgnoreCase) ||
            segment.AddressKind.Equals("UeFileOffset", StringComparison.OrdinalIgnoreCase)) return segment.Address;
        pe ??= ExeCodeCaveScanner.ParsePe(image);
        if (segment.Address < pe.ImageBase) return -1;
        var rva = segment.Address - pe.ImageBase;
        var section = pe.Sections.FirstOrDefault(item =>
            rva >= item.VirtualAddress && rva < item.VirtualAddress + Math.Max(item.VirtualSize, item.RawSize));
        return section == null ? -1 : checked((long)section.RawPointer + rva - section.VirtualAddress);
    }
}

internal sealed record TrustedEffectRepairResult(bool IsTrusted, string TransactionManifestId, string InstalledExeSha256, string ReasonZh)
{
    public static TrustedEffectRepairResult Denied(string reason)
        => new(false, string.Empty, string.Empty, reason);
}
