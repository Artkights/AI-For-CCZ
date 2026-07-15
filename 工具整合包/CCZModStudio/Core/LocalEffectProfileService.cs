using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class LocalEffectProfileService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string ProfileRoot(CczProject project)
        => Path.Combine(project.GameRoot, "CCZModStudio_Notes", "EffectProfiles");

    public LocalEffectProfileRecord? FindVerified(CczProject project, string fullExeSha256)
    {
        var root = ProfileRoot(project);
        if (!Directory.Exists(root)) return null;
        foreach (var path in Directory.EnumerateFiles(root, "*.profile.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var item = JsonSerializer.Deserialize<LocalEffectProfileRecord>(File.ReadAllText(path, Encoding.UTF8), JsonOptions);
                if (item != null && item.TrustTier == EffectProfileTrustTier.LocalVerified &&
                    item.FullExeSha256.Equals(fullExeSha256, StringComparison.OrdinalIgnoreCase) &&
                    UserBoundSignatureService.Verify(item, static value => value.Signature, static (value, signature) => value.Signature = signature))
                    return item;
            }
            catch { }
        }
        return null;
    }

    public EffectProfileOnboardingPlan Prepare(CczProject project, CancellationToken cancellationToken = default)
    {
        var audit = new ExecutableProfileAuditService().Audit(project);
        var sandbox = new EffectSandboxService().Create(project, cancellationToken);
        var same65Layout = TryValidateCanonical65PeLayout(project.ResolveGameFile("Ekd5.exe"), out var peBlocker);
        var plan = new EffectProfileOnboardingPlan
        {
            PlanId = "profile-onboarding-" + Guid.NewGuid().ToString("N"),
            OriginalGameRoot = project.GameRoot,
            SandboxRoot = sandbox.GameRoot,
            SandboxExeSha256 = EffectPatchByteService.Sha256(sandbox.ResolveGameFile("Ekd5.exe")),
            ProfileAudit = audit,
            ProfileTrustTier = audit.CanWriteRegisteredData ? EffectProfileTrustTier.BuiltInVerified : EffectProfileTrustTier.SandboxCandidate,
            CanRunSandboxValidation = same65Layout,
            CanPromote = false,
            RequiredContractIds = ["strategy-damage-formula-v2", "physical-after-damage-recovery-v2"],
            StepsZh = ["核对 PE 和代码身份", "在自动副本中执行参数写入与回滚", "完成行为契约 V3 场景", "签发本地可信档案"]
        };
        if (!same65Layout) plan.BlockerCodes.Add(peBlocker);
        if (audit.UnknownDifferences.Count > 0) plan.BlockerCodes.Add("UNKNOWN_CODE_REQUIRES_SANDBOX_VALIDATION");
        plan.SummaryZh = plan.CanRunSandboxValidation
            ? "已创建独立验证副本；原项目保持只读，等待静态和动态验证。"
            : "已创建验证副本，但当前 EXE 布局与规范 6.5 不同，必须先恢复函数和 Hook 定位。";
        return plan;
    }

    public EffectProfileOnboardingResult Promote(
        CczProject originalProject,
        CczProject sandboxProject,
        IReadOnlyList<EffectEvidenceBundleV3> evidence)
    {
        if (!EffectSandboxService.TryRead(sandboxProject.GameRoot, out var sandbox) ||
            !Path.GetFullPath(sandbox.OriginalGameRoot).Equals(Path.GetFullPath(originalProject.GameRoot), StringComparison.OrdinalIgnoreCase))
            return Failed("验证副本身份无效或不属于当前原项目。");
        var originalSha = EffectPatchByteService.Sha256(originalProject.ResolveGameFile("Ekd5.exe"));
        if (!originalSha.Equals(sandbox.OriginalExeSha256, StringComparison.OrdinalIgnoreCase))
            return Failed("原项目 EXE 已在验证期间变化。");
        var originalLayoutValid = TryValidateCanonical65PeLayout(originalProject.ResolveGameFile("Ekd5.exe"), out var originalLayoutBlocker);
        var sandboxLayoutValid = TryValidateCanonical65PeLayout(sandboxProject.ResolveGameFile("Ekd5.exe"), out var sandboxLayoutBlocker);
        if (!originalLayoutValid || !sandboxLayoutValid)
            return Failed("PE 节布局不属于可自动入库的 6.5 家族：" + originalLayoutBlocker + "/" + sandboxLayoutBlocker);
        var sandboxSha = EffectPatchByteService.Sha256(sandboxProject.ResolveGameFile("Ekd5.exe"));
        if (!sandboxSha.Equals(originalSha, StringComparison.OrdinalIgnoreCase))
            return Failed("验证副本仍含未恢复的磁盘探针或测试补丁，不能签发正式档案。");
        if (!ComputeTableLayoutIdentity(originalProject).Equals(ComputeTableLayoutIdentity(sandboxProject), StringComparison.OrdinalIgnoreCase))
            return Failed("验证期间表布局发生变化，不能把该结果提升为原项目档案。");
        var required = new[] { "strategy-damage-formula-v2", "physical-after-damage-recovery-v2" };
        var sandboxContracts = new HookExecutionContractService().BuildContracts(sandboxProject)
            .ToDictionary(item => item.ContractId, StringComparer.OrdinalIgnoreCase);
        foreach (var contractId in required)
        {
            var bundle = evidence.FirstOrDefault(item => item.ContractId.Equals(contractId, StringComparison.OrdinalIgnoreCase));
            if (bundle == null || !bundle.ProbeRestored || !EffectEvidenceBundleCrypto.Verify(bundle))
                return Failed("缺少通过签名和探针恢复验证的 V3 契约证据：" + contractId);
            if (!sandboxContracts.TryGetValue(contractId, out var contract) ||
                !contract.ContractHash.Equals(bundle.ContractHash, StringComparison.OrdinalIgnoreCase) ||
                !contract.ContractCodeIdentityHash.Equals(bundle.ContractCodeIdentityHash, StringComparison.OrdinalIgnoreCase))
                return Failed("验证副本的静态契约或代码身份与 V3 证据不一致：" + contractId);
            var import = EffectEvidenceBundleService.VerifyAndStoreV3(originalProject, bundle);
            if (!import.Accepted) return Failed(import.SummaryZh + " " + string.Join("；", import.WarningsZh));
        }
        var bytes = File.ReadAllBytes(originalProject.ResolveGameFile("Ekd5.exe"));
        var pe = ExeCodeCaveScanner.ParsePe(bytes);
        var audit = new ExecutableProfileAuditService().AuditBytes(bytes, originalSha, pe);
        var record = new LocalEffectProfileRecord
        {
            ProfileId = "ccz65-local-" + originalSha[..12].ToLowerInvariant(),
            FullExeSha256 = originalSha,
            FileLength = bytes.LongLength,
            ImageBase = pe.ImageBase,
            NormalizedProfileIdentity = audit.NormalizedProfileIdentity,
            PeLayoutIdentityHash = ComputePeLayoutIdentity(pe),
            TableLayoutIdentityHash = ComputeTableLayoutIdentity(originalProject),
            ExplainedDifferences = audit.RegisteredDifferences.Concat(audit.UnknownDifferences).ToList(),
            ContractCodeIdentities = evidence.ToDictionary(item => item.ContractId, item => item.ContractCodeIdentityHash, StringComparer.OrdinalIgnoreCase),
            EvidenceBundleIds = evidence.Select(item => item.BundleId).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            ForbiddenRangeKinds = EngineEffectProfileRegistry.Current65.ForbiddenRangeKinds.ToList(),
            SourceSandboxId = sandbox.SandboxId,
            ToolBuildIdentity = EffectCapabilityVersion.BuildIdentity,
            VerifiedAtUtc = DateTime.UtcNow
        };
        UserBoundSignatureService.Sign(record, static (item, value) => item.Signature = value);
        var root = ProfileRoot(originalProject);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, record.ProfileId + ".profile.json");
        File.WriteAllText(path, JsonSerializer.Serialize(record, JsonOptions), Encoding.UTF8);
        ProjectResourceInvalidationBus.Publish([path], ProjectResourceKind.EffectMetadata);
        return new EffectProfileOnboardingResult { Completed = true, Promoted = true, Profile = record, ProfilePath = path, SummaryZh = "本地 6.5 特效档案已签发；重新扫描后可按该代码身份计算正式权限。" };
    }

    private static string ComputePeLayoutIdentity(ExeCodeCaveScanner.PeImage pe)
    {
        var token = pe.ImageBase + "|" + string.Join(";", pe.Sections.Select(item => $"{item.Name}:{item.VirtualAddress:X8}:{item.VirtualSize}:{item.RawPointer}:{item.RawSize}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    private static string ComputeTableLayoutIdentity(CczProject project)
    {
        var token = string.Join("|", new[] { "Data.e5", "Star.e5", "Imsg.e5" }.Select(name =>
        {
            var path = project.ResolveGameFile(name);
            return File.Exists(path) ? name + ":" + new FileInfo(path).Length : name + ":missing";
        })) + "|" + (File.Exists(project.HexTableXmlPath) ? EffectPatchByteService.Sha256(project.HexTableXmlPath) : "missing");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    private static bool TryValidateCanonical65PeLayout(string exePath, out string blocker)
    {
        blocker = string.Empty;
        if (!File.Exists(exePath)) { blocker = "EXECUTABLE_MISSING"; return false; }
        var bytes = File.ReadAllBytes(exePath);
        if (bytes.LongLength != EngineEffectProfileRegistry.Current65.FileLength)
        {
            blocker = "PE_FILE_LENGTH_MISMATCH";
            return false;
        }
        ExeCodeCaveScanner.PeImage pe;
        try { pe = ExeCodeCaveScanner.ParsePe(bytes); }
        catch { blocker = "PE_PARSE_FAILED"; return false; }
        if (pe.ImageBase != EngineEffectProfileRegistry.Current65.ImageBase)
        {
            blocker = "PE_IMAGE_BASE_MISMATCH";
            return false;
        }
        var actual = pe.Sections.Select(item =>
            $"{item.VirtualAddress:X8}:{item.VirtualSize:X8}:{item.RawPointer:X8}:{item.RawSize:X8}:{item.Characteristics:X8}").ToArray();
        if (!actual.SequenceEqual(EngineEffectProfileRegistry.Current65.PeSectionLayout, StringComparer.OrdinalIgnoreCase))
        {
            blocker = "PE_SECTION_LAYOUT_MISMATCH";
            return false;
        }
        return true;
    }

    private static EffectProfileOnboardingResult Failed(string warning)
        => new() { WarningsZh = [warning], SummaryZh = "本地档案晋升未通过：" + warning };
}
