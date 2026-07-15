using System.Text;
using System.Text.Json;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public static class EffectEvidenceBundleService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static EffectEvidenceBundleImportResult VerifyAndStore(CczProject project, EffectEvidenceBundleV1 bundle)
    {
        var result = new EffectEvidenceBundleImportResult();
        var identity = new ProjectPatchIdentityService().Build(project);
        var expectedExePath = Path.GetFullPath(project.ResolveGameFile("Ekd5.exe"));
        result.SignatureVerified = EffectEvidenceBundleCrypto.Verify(bundle);
        if (!result.SignatureVerified) result.WarningsZh.Add("证据包签名无效，不是当前 Windows 用户下的 GameDebug 可信输出。");
        if (!bundle.SchemaVersion.Equals(EffectEvidenceProtocol.SchemaVersion, StringComparison.Ordinal)) result.WarningsZh.Add("证据包 schema 版本不受支持。");
        if (!bundle.EffectCapabilitySchemaVersion.Equals(EffectCapabilityVersion.SchemaVersion, StringComparison.Ordinal))
            result.WarningsZh.Add("证据包的特效能力 schema 与当前 Core 不一致。");
        if (!bundle.BuildChannel.Equals(EffectCapabilityVersion.BuildChannel, StringComparison.Ordinal))
            result.WarningsZh.Add("证据包的构建通道与当前 Core 不一致。");
        if (!bundle.SignatureAlgorithm.Equals("HMAC-SHA256-DPAPI-CurrentUser", StringComparison.Ordinal)) result.WarningsZh.Add("证据包签名算法不受支持。");
        if (string.IsNullOrWhiteSpace(bundle.ContractId) || Path.GetFileName(bundle.ContractId) != bundle.ContractId || string.IsNullOrWhiteSpace(bundle.ContractHash))
            result.WarningsZh.Add("证据包契约身份无效。");
        if (!bundle.ProjectId.Equals(identity.ProjectId, StringComparison.OrdinalIgnoreCase)) result.WarningsZh.Add("证据包项目身份与当前项目不一致。");
        if (!ProjectPatchIdentityService.NormalizePath(bundle.GameRoot).Equals(identity.GameRoot, StringComparison.OrdinalIgnoreCase)) result.WarningsZh.Add("证据包游戏目录与当前项目不一致。");
        if (!bundle.LoadedModuleSha256.Equals(identity.CurrentSha256, StringComparison.OrdinalIgnoreCase)) result.WarningsZh.Add("证据中的已加载模块 SHA 与当前 EXE 不一致。");
        if (bundle.LoadedModuleSize != identity.TargetFileSize) result.WarningsZh.Add("证据中的已加载模块大小与当前 EXE 不一致。");
        if (bundle.ProcessId <= 0 || string.IsNullOrWhiteSpace(bundle.LoadedModulePath)) result.WarningsZh.Add("证据缺少有效进程和加载模块身份。");
        else if (!Path.GetFullPath(bundle.LoadedModulePath).Equals(expectedExePath, StringComparison.OrdinalIgnoreCase) ||
                 !Path.GetFullPath(bundle.ProcessPath).Equals(expectedExePath, StringComparison.OrdinalIgnoreCase))
            result.WarningsZh.Add("证据中的进程或加载模块路径不是当前项目 Ekd5.exe。");
        if (string.IsNullOrWhiteSpace(bundle.DebuggerVersion) || string.IsNullOrWhiteSpace(bundle.ToolBuildId))
            result.WarningsZh.Add("证据缺少调试器版本或 GameDebug 构建标识。");
        if (bundle.CreatedAtUtc == default || bundle.CreatedAtUtc > DateTime.UtcNow.AddMinutes(5))
            result.WarningsZh.Add("证据创建时间无效。");
        if (bundle.CompletedScenarioIds.Count == 0 || bundle.CompletedScenarioIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != bundle.CompletedScenarioIds.Count)
            result.WarningsZh.Add("证据场景列表为空或包含重复项。");
        if (bundle.RawFiles.Count == 0) result.WarningsZh.Add("证据包没有原始采集文件。");
        if (bundle.CompletedScenarioIds.Any(scenario => bundle.RawFiles.All(raw => !raw.ScenarioId.Equals(scenario, StringComparison.OrdinalIgnoreCase))))
            result.WarningsZh.Add("至少一个已完成场景没有对应的原始采集文件。");

        if (!string.IsNullOrWhiteSpace(bundle.ContractId) && !string.IsNullOrWhiteSpace(bundle.LoadedModuleSha256))
        {
            var requiredSessionRoot = Path.GetFullPath(EvidenceRoot(project, bundle.LoadedModuleSha256, bundle.ContractId))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var actualSessionRoot = Path.GetFullPath(bundle.SessionRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!actualSessionRoot.StartsWith(requiredSessionRoot, StringComparison.OrdinalIgnoreCase))
                result.WarningsZh.Add("证据会话目录不属于当前项目、SHA 和契约的证据目录。");
        }

        var rawOk = VerifyRawIntegrity(bundle, result.WarningsZh);
        result.RawIntegrityVerified = rawOk;
        result.Accepted = result.SignatureVerified && result.RawIntegrityVerified && result.WarningsZh.Count == 0;
        if (!result.Accepted)
        {
            result.SummaryZh = "可信证据包验证未通过。";
            return result;
        }
        var root = EvidenceRoot(project, bundle.LoadedModuleSha256, bundle.ContractId);
        Directory.CreateDirectory(root);
        result.SavedPath = Path.Combine(root, Path.GetFileNameWithoutExtension(bundle.BundleId) + ".trusted.json");
        File.WriteAllText(result.SavedPath, EffectEvidenceBundleCrypto.Serialize(bundle), Encoding.UTF8);
        result.SummaryZh = "可信证据包签名和全部原始文件摘要验证通过。";
        return result;
    }

    public static EffectEvidenceBundleImportResult VerifyAndStoreV2(CczProject project, EffectEvidenceBundleV2 bundle)
    {
        var result = new EffectEvidenceBundleImportResult();
        var audit = new ExecutableProfileAuditService().Audit(project);
        var identity = new ProjectPatchIdentityService().BuildKnown(project, "Ekd5.exe",
            new FileInfo(project.ResolveGameFile("Ekd5.exe")).Length, audit.CurrentSha256, audit.ProfileId);
        var expectedExePath = Path.GetFullPath(project.ResolveGameFile("Ekd5.exe"));
        result.SignatureVerified = EffectEvidenceBundleCrypto.Verify(bundle);
        if (!result.SignatureVerified) result.WarningsZh.Add("v2 证据包签名无效，不是当前 Windows 用户下的 GameDebug 可信输出。");
        if (bundle.SchemaVersion != EffectEvidenceProtocolV2.SchemaVersion || bundle.ContractVersion != 2)
            result.WarningsZh.Add("证据包不是执行契约 v2 协议。");
        if (bundle.EffectCapabilitySchemaVersion != EffectCapabilityVersion.SchemaVersion || bundle.BuildChannel != EffectCapabilityVersion.BuildChannel)
            result.WarningsZh.Add("证据包能力 schema 或构建通道与当前 Core 不一致。");
        if (!bundle.ProfileId.Equals(audit.ProfileId, StringComparison.Ordinal) ||
            !bundle.NormalizedProfileIdentity.Equals(audit.NormalizedProfileIdentity, StringComparison.OrdinalIgnoreCase))
            result.WarningsZh.Add("证据包绑定的规范档案身份与当前 EXE 不一致。");
        if (string.IsNullOrWhiteSpace(bundle.ContractCodeIdentityHash) || string.IsNullOrWhiteSpace(bundle.ContractHash))
            result.WarningsZh.Add("证据包缺少代码身份或契约摘要。");
        if (!bundle.ProjectId.Equals(identity.ProjectId, StringComparison.OrdinalIgnoreCase) ||
            !ProjectPatchIdentityService.NormalizePath(bundle.GameRoot).Equals(identity.GameRoot, StringComparison.OrdinalIgnoreCase))
            result.WarningsZh.Add("证据包项目身份与当前项目不一致。");
        if (!bundle.LoadedModuleSha256.Equals(audit.CurrentSha256, StringComparison.OrdinalIgnoreCase) || bundle.LoadedModuleSize != identity.TargetFileSize)
            result.WarningsZh.Add("证据中的完整 EXE SHA 或大小与当前写入对象不一致。");
        if (bundle.ProcessId <= 0 || !Path.GetFullPath(bundle.LoadedModulePath).Equals(expectedExePath, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(bundle.ProcessPath).Equals(expectedExePath, StringComparison.OrdinalIgnoreCase))
            result.WarningsZh.Add("证据进程或加载模块不是当前项目 Ekd5.exe。");
        if (bundle.CompletedScenarioIds.Count == 0 || bundle.RawFiles.Count == 0)
            result.WarningsZh.Add("v2 证据缺少动态场景或原始采集文件。");
        if (!string.IsNullOrWhiteSpace(bundle.ContractId) && !string.IsNullOrWhiteSpace(bundle.LoadedModuleSha256))
        {
            var required = Path.GetFullPath(EvidenceRootV2(project, bundle.LoadedModuleSha256, bundle.ContractId))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var actual = Path.GetFullPath(bundle.SessionRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!actual.StartsWith(required, StringComparison.OrdinalIgnoreCase)) result.WarningsZh.Add("v2 证据会话目录不属于当前项目、SHA 和契约目录。");
        }
        result.RawIntegrityVerified = VerifyRawIntegrity(ToV1(bundle), result.WarningsZh);
        result.Accepted = result.SignatureVerified && result.RawIntegrityVerified && result.WarningsZh.Count == 0;
        if (!result.Accepted) { result.SummaryZh = "可信 v2 证据包验证未通过。"; return result; }
        var root = EvidenceRootV2(project, bundle.LoadedModuleSha256, bundle.ContractId);
        Directory.CreateDirectory(root);
        result.SavedPath = Path.Combine(root, Path.GetFileNameWithoutExtension(bundle.BundleId) + ".trusted-v2.json");
        File.WriteAllText(result.SavedPath, EffectEvidenceBundleCrypto.Serialize(bundle), Encoding.UTF8);
        ProjectResourceInvalidationBus.Publish([result.SavedPath], ProjectResourceKind.EffectMetadata);
        result.SummaryZh = "可信 v2 证据包的档案、代码身份、签名和原始文件摘要验证通过。";
        return result;
    }

    public static IEnumerable<EffectEvidenceBundleV2> ReadTrustedV2(CczProject project, string sha, string contractId)
    {
        var root = EvidenceRootV2(project, sha, contractId);
        if (!Directory.Exists(root)) yield break;
        foreach (var path in Directory.GetFiles(root, "*.trusted-v2.json"))
        {
            EffectEvidenceBundleV2? bundle = null;
            try { bundle = JsonSerializer.Deserialize<EffectEvidenceBundleV2>(File.ReadAllText(path, Encoding.UTF8), JsonOptions); } catch { }
            if (bundle != null && bundle.LoadedModuleSha256.Equals(sha, StringComparison.OrdinalIgnoreCase) &&
                EffectEvidenceBundleCrypto.Verify(bundle) && VerifyRawIntegrity(ToV1(bundle), null)) yield return bundle;
        }
    }

    public static EffectEvidenceBundleImportResult VerifyAndStoreV3(CczProject project, EffectEvidenceBundleV3 bundle)
    {
        var result = new EffectEvidenceBundleImportResult();
        var audit = new ExecutableProfileAuditService().Audit(project);
        var identity = new ProjectPatchIdentityService().BuildKnown(project, "Ekd5.exe",
            new FileInfo(project.ResolveGameFile("Ekd5.exe")).Length, audit.CurrentSha256, audit.ProfileId);
        result.SignatureVerified = EffectEvidenceBundleCrypto.Verify(bundle);
        if (!result.SignatureVerified)
            result.WarningsZh.Add("V3 证据包签名无效，不是当前 Windows 用户下的可信验证输出。");
        if (!bundle.SchemaVersion.Equals(EffectEvidenceProtocolV3.SchemaVersion, StringComparison.Ordinal) || bundle.ContractVersion != 2)
            result.WarningsZh.Add("证据包不是受支持的执行契约 V3 协议。");
        if (!bundle.EffectCapabilitySchemaVersion.Equals(EffectEvidenceProtocolV3.EffectCapabilitySchemaVersion, StringComparison.Ordinal) ||
            !bundle.BuildChannel.Equals(EffectEvidenceProtocolV3.BuildChannel, StringComparison.Ordinal))
            result.WarningsZh.Add("证据包能力 schema 或构建通道与当前 Core 不一致。");
        if (!bundle.SignatureAlgorithm.Equals("HMAC-SHA256-DPAPI-CurrentUser", StringComparison.Ordinal))
            result.WarningsZh.Add("证据包签名算法不受支持。");
        if (!IsSafeIdentity(bundle.BundleId) || !IsSafeIdentity(bundle.ContractId) ||
            string.IsNullOrWhiteSpace(bundle.ContractHash) || string.IsNullOrWhiteSpace(bundle.ContractCodeIdentityHash))
            result.WarningsZh.Add("证据包的包、契约或代码身份无效。");
        if (!bundle.ProjectId.Equals(identity.ProjectId, StringComparison.OrdinalIgnoreCase) ||
            !PathsEqual(bundle.OriginalGameRoot, identity.GameRoot))
            result.WarningsZh.Add("证据包绑定的原项目身份与当前项目不一致。");
        if (!bundle.OriginalExeSha256.Equals(audit.CurrentSha256, StringComparison.OrdinalIgnoreCase))
            result.WarningsZh.Add("原项目 EXE 已在验证后变化，V3 证据不能导入。");
        var localProfile = new LocalEffectProfileService().FindVerified(project, audit.CurrentSha256);
        if (!bundle.ProfileId.Equals(audit.ProfileId, StringComparison.Ordinal) && localProfile == null)
            result.WarningsZh.Add("证据包绑定的档案不属于当前 EXE。");
        else if (!bundle.NormalizedProfileIdentity.Equals(
                     localProfile?.NormalizedProfileIdentity ?? audit.NormalizedProfileIdentity,
                     StringComparison.OrdinalIgnoreCase))
            result.WarningsZh.Add("证据包的规范化档案身份与当前 EXE 不一致。");

        if (!EffectSandboxService.TryRead(bundle.SandboxRoot, out var sandbox) ||
            !PathsEqual(sandbox.OriginalGameRoot, project.GameRoot) ||
            !sandbox.OriginalExeSha256.Equals(bundle.OriginalExeSha256, StringComparison.OrdinalIgnoreCase))
            result.WarningsZh.Add("证据包没有绑定到当前项目创建的可信验证副本。");
        var sandboxPrefix = SafePrefix(bundle.SandboxRoot);
        if (bundle.ProcessId <= 0 || !PathIsInside(bundle.ProcessPath, sandboxPrefix) ||
            !PathIsInside(bundle.LoadedModulePath, sandboxPrefix) ||
            !PathsEqual(bundle.ProcessPath, bundle.LoadedModulePath))
            result.WarningsZh.Add("证据中的进程和加载模块不是验证副本内的 Ekd5.exe。");
        else if (!File.Exists(bundle.LoadedModulePath) ||
                 new FileInfo(bundle.LoadedModulePath).Length != bundle.LoadedModuleSize ||
                 !EffectPatchByteService.Sha256(bundle.LoadedModulePath).Equals(bundle.LoadedModuleSha256, StringComparison.OrdinalIgnoreCase))
            result.WarningsZh.Add("验证副本中的加载模块大小或 SHA 已经变化。");

        if (string.IsNullOrWhiteSpace(bundle.ValidationRecipeId) || string.IsNullOrWhiteSpace(bundle.ValidationRecipeHash) ||
            string.IsNullOrWhiteSpace(bundle.ProbePackageHash) || string.IsNullOrWhiteSpace(bundle.ProbeExpectedOldBytesHash))
            result.WarningsZh.Add("V3 证据缺少验证配方、探针包或探针旧字节身份。");
        if (!bundle.ProbeRestored || string.IsNullOrWhiteSpace(bundle.ProbeRestoreEvidencePath))
            result.WarningsZh.Add("临时探针没有提供可复核的恢复证据。");
        if (bundle.CreatedAtUtc == default || bundle.CreatedAtUtc > DateTime.UtcNow.AddMinutes(5))
            result.WarningsZh.Add("证据创建时间无效。");
        if (bundle.CompletedScenarioIds.Count < 4 ||
            bundle.CompletedScenarioIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != bundle.CompletedScenarioIds.Count)
            result.WarningsZh.Add("V3 证据至少需要互不重复的正常、最小、最大和负向场景。");
        if (!HasRequiredScenarioKinds(bundle.CompletedScenarioIds))
            result.WarningsZh.Add("V3 证据缺少正常、最小边界、最大边界或负向场景。");
        if (bundle.DerivedObservations.Count == 0 || bundle.DerivedObservations.Any(item =>
                item.VerifiedMinimum is null || item.VerifiedMaximum is null || item.VerifiedMinimum > item.VerifiedMaximum ||
                string.IsNullOrWhiteSpace(item.BoundaryEvidenceZh)))
            result.WarningsZh.Add("每个可写观测都必须包含可复核的最小值、最大值和边界证据。");
        if (bundle.RelationshipAssertions.Count == 0 || bundle.RelationshipAssertions.Any(item =>
                !item.Verified || item.MatchingSamples <= 0 || item.NegativeSamples <= 0 ||
                string.IsNullOrWhiteSpace(item.Relationship) || item.BattlefieldUnitId is null ||
                string.IsNullOrWhiteSpace(item.Camp) || item.HpObserved is null || !item.CallChainVerified ||
                item.EvidencePaths.Count == 0))
            result.WarningsZh.Add("指针关系断言必须同时包含正向、负向样本和原始证据引用。");

        result.RawIntegrityVerified = VerifyRawIntegrity(ToV1(bundle), result.WarningsZh) &&
                                      VerifyV3References(bundle, result.WarningsZh);
        result.Accepted = result.SignatureVerified && result.RawIntegrityVerified && result.WarningsZh.Count == 0;
        if (!result.Accepted)
        {
            result.SummaryZh = "可信 V3 证据包验证未通过。";
            return result;
        }

        var root = EvidenceRootV3(project, bundle.OriginalExeSha256, bundle.ContractId);
        var importedRoot = Path.Combine(root, Path.GetFileNameWithoutExtension(bundle.BundleId));
        Directory.CreateDirectory(importedRoot);
        foreach (var raw in bundle.RawFiles)
        {
            var source = Path.GetFullPath(Path.Combine(bundle.SessionRoot, raw.RelativePath));
            var target = Path.GetFullPath(Path.Combine(importedRoot, raw.RelativePath));
            if (!PathIsInside(target, SafePrefix(importedRoot)))
                throw new InvalidOperationException("V3 原始证据目标路径越出证据目录。");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
        }
        bundle.SessionRoot = importedRoot;
        bundle.ProbeRestoreEvidencePath = NormalizeEvidenceReference(bundle.ProbeRestoreEvidencePath, bundle.RawFiles);
        EffectEvidenceBundleCrypto.Sign(bundle);
        result.SavedPath = Path.Combine(root, Path.GetFileNameWithoutExtension(bundle.BundleId) + ".trusted-v3.json");
        File.WriteAllText(result.SavedPath, EffectEvidenceBundleCrypto.Serialize(bundle), Encoding.UTF8);
        ProjectResourceInvalidationBus.Publish([result.SavedPath], ProjectResourceKind.EffectMetadata);
        result.SummaryZh = "可信 V3 证据的档案、代码身份、边界、关系语义、探针恢复和原始文件摘要均已验证。";
        return result;
    }

    public static IEnumerable<EffectEvidenceBundleV3> ReadTrustedV3(CczProject project, string contractId)
    {
        var executable = ExecutableAnalysisSnapshotCache.Shared.GetBase(project);
        return ReadTrustedV3(project, executable.Sha256, contractId);
    }

    public static IEnumerable<EffectEvidenceBundleV3> ReadTrustedV3(CczProject project, string exeSha256, string contractId)
    {
        if (!IsSafeIdentity(contractId)) yield break;
        var exePath = project.ResolveGameFile("Ekd5.exe");
        var exeLength = File.Exists(exePath) ? new FileInfo(exePath).Length : 0;
        var identity = new ProjectPatchIdentityService().BuildKnown(
            project, "Ekd5.exe", exeLength, exeSha256);
        var root = EvidenceRootV3(project, exeSha256, contractId);
        if (!Directory.Exists(root)) yield break;
        foreach (var path in Directory.GetFiles(root, "*.trusted-v3.json", SearchOption.TopDirectoryOnly))
        {
            EffectEvidenceBundleV3? bundle = null;
            try { bundle = JsonSerializer.Deserialize<EffectEvidenceBundleV3>(File.ReadAllText(path, Encoding.UTF8), JsonOptions); }
            catch { }
            if (bundle != null &&
                bundle.SchemaVersion.Equals(EffectEvidenceProtocolV3.SchemaVersion, StringComparison.Ordinal) &&
                bundle.ProjectId.Equals(identity.ProjectId, StringComparison.OrdinalIgnoreCase) &&
                bundle.OriginalExeSha256.Equals(identity.CurrentSha256, StringComparison.OrdinalIgnoreCase) &&
                PathsEqual(bundle.OriginalGameRoot, project.GameRoot) &&
                bundle.ContractId.Equals(contractId, StringComparison.OrdinalIgnoreCase) &&
                bundle.ProbeRestored &&
                EffectEvidenceBundleCrypto.Verify(bundle) &&
                VerifyRawIntegrity(ToV1(bundle), null) && VerifyV3References(bundle, null))
                yield return bundle;
        }
    }

    public static IEnumerable<EffectEvidenceBundleV1> ReadTrusted(CczProject project, string sha, string contractId)
    {
        var root = EvidenceRoot(project, sha, contractId);
        if (!Directory.Exists(root)) yield break;
        var identity = new ProjectPatchIdentityService().Build(project);
        var expectedExePath = Path.GetFullPath(project.ResolveGameFile("Ekd5.exe"));
        foreach (var path in Directory.GetFiles(root, "*.trusted.json"))
        {
            EffectEvidenceBundleV1? bundle = null;
            try { bundle = JsonSerializer.Deserialize<EffectEvidenceBundleV1>(File.ReadAllText(path, Encoding.UTF8), JsonOptions); } catch { }
            if (bundle != null &&
                bundle.ProjectId.Equals(identity.ProjectId, StringComparison.OrdinalIgnoreCase) &&
                ProjectPatchIdentityService.NormalizePath(bundle.GameRoot).Equals(identity.GameRoot, StringComparison.OrdinalIgnoreCase) &&
                bundle.LoadedModuleSha256.Equals(sha, StringComparison.OrdinalIgnoreCase) &&
                bundle.LoadedModuleSize == identity.TargetFileSize &&
                Path.GetFullPath(bundle.LoadedModulePath).Equals(expectedExePath, StringComparison.OrdinalIgnoreCase) &&
                EffectEvidenceBundleCrypto.Verify(bundle) && VerifyRawIntegrity(bundle, null))
                yield return bundle;
        }
    }

    public static string EvidenceRoot(CczProject project, string sha, string contractId)
        => Path.Combine(project.GameRoot, "CCZModStudio_Notes", "EffectContractEvidence", sha, contractId);

    public static string EvidenceRootV2(CczProject project, string sha, string contractId)
        => Path.Combine(project.GameRoot, "CCZModStudio_Notes", "EffectContractEvidenceV2", sha, contractId);

    public static string EvidenceRootV3(CczProject project, string sha, string contractId)
        => Path.Combine(project.GameRoot, "CCZModStudio_Notes", "EffectContractEvidenceV3", sha, contractId);

    private static EffectEvidenceBundleV1 ToV1(EffectEvidenceBundleV2 bundle)
        => new()
        {
            BundleId = bundle.BundleId, ContractId = bundle.ContractId, ContractHash = bundle.ContractHash,
            ProjectId = bundle.ProjectId, GameRoot = bundle.GameRoot, SessionRoot = bundle.SessionRoot,
            ProcessId = bundle.ProcessId, ProcessPath = bundle.ProcessPath, LoadedModulePath = bundle.LoadedModulePath,
            LoadedModuleSize = bundle.LoadedModuleSize, LoadedModuleSha256 = bundle.LoadedModuleSha256,
            DebuggerVersion = bundle.DebuggerVersion, ToolBuildId = bundle.ToolBuildId, CreatedAtUtc = bundle.CreatedAtUtc,
            CompletedScenarioIds = bundle.CompletedScenarioIds, RawFiles = bundle.RawFiles, DerivedObservations = bundle.DerivedObservations
        };

    private static EffectEvidenceBundleV1 ToV1(EffectEvidenceBundleV3 bundle)
        => new()
        {
            BundleId = bundle.BundleId, ContractId = bundle.ContractId, ContractHash = bundle.ContractHash,
            ProjectId = bundle.ProjectId, GameRoot = bundle.OriginalGameRoot, SessionRoot = bundle.SessionRoot,
            ProcessId = bundle.ProcessId, ProcessPath = bundle.ProcessPath, LoadedModulePath = bundle.LoadedModulePath,
            LoadedModuleSize = bundle.LoadedModuleSize, LoadedModuleSha256 = bundle.LoadedModuleSha256,
            DebuggerVersion = bundle.DebuggerVersion, ToolBuildId = bundle.ToolBuildId, CreatedAtUtc = bundle.CreatedAtUtc,
            CompletedScenarioIds = bundle.CompletedScenarioIds, RawFiles = bundle.RawFiles, DerivedObservations = bundle.DerivedObservations
        };

    private static bool VerifyV3References(EffectEvidenceBundleV3 bundle, ICollection<string>? warnings)
    {
        var rawPaths = bundle.RawFiles.Select(item => item.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var restore = NormalizeEvidenceReference(bundle.ProbeRestoreEvidencePath, bundle.RawFiles);
        var ok = rawPaths.Contains(restore);
        if (!ok) warnings?.Add("探针恢复证据不在已签名的原始文件列表中。");
        foreach (var assertion in bundle.RelationshipAssertions)
        {
            foreach (var reference in assertion.EvidencePaths)
            {
                if (rawPaths.Contains(NormalizeEvidenceReference(reference, bundle.RawFiles))) continue;
                warnings?.Add("关系断言引用了未签名的原始文件：" + reference);
                ok = false;
            }
        }
        return ok;
    }

    private static string NormalizeEvidenceReference(string reference, IReadOnlyList<EffectEvidenceRawFile> rawFiles)
    {
        if (string.IsNullOrWhiteSpace(reference)) return string.Empty;
        var exact = rawFiles.FirstOrDefault(item => item.RelativePath.Equals(reference, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact.RelativePath;
        var fileName = Path.GetFileName(reference);
        var byName = rawFiles.Where(item => Path.GetFileName(item.RelativePath).Equals(fileName, StringComparison.OrdinalIgnoreCase)).ToArray();
        return byName.Length == 1 ? byName[0].RelativePath : reference;
    }

    private static bool HasRequiredScenarioKinds(IEnumerable<string> scenarioIds)
    {
        var values = scenarioIds.Select(item => item.ToLowerInvariant()).ToArray();
        return HasAny("normal", "baseline") && HasAny("minimum", "min-boundary", "lower-boundary") &&
               HasAny("maximum", "max-boundary", "upper-boundary") && HasAny("negative", "rejected", "invalid");

        bool HasAny(params string[] tokens) => values.Any(value => tokens.Any(value.Contains));
    }

    private static bool IsSafeIdentity(string value)
        => !string.IsNullOrWhiteSpace(value) && Path.GetFileName(value).Equals(value, StringComparison.Ordinal) &&
           value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static bool PathsEqual(string left, string right)
    {
        try { return Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private static string SafePrefix(string root)
        => Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

    private static bool PathIsInside(string path, string prefix)
    {
        try { return Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private static bool VerifyRawIntegrity(EffectEvidenceBundleV1 bundle, ICollection<string>? warnings)
    {
        if (string.IsNullOrWhiteSpace(bundle.SessionRoot))
        {
            warnings?.Add("证据包缺少原始采集会话目录。");
            return false;
        }
        var sessionRoot = Path.GetFullPath(bundle.SessionRoot);
        var rootPrefix = sessionRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var rawOk = bundle.RawFiles.Count > 0;
        if (bundle.RawFiles.Select(item => item.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != bundle.RawFiles.Count)
        {
            warnings?.Add("原始证据文件列表包含重复路径。");
            rawOk = false;
        }
        foreach (var raw in bundle.RawFiles)
        {
            var path = Path.GetFullPath(Path.Combine(sessionRoot, raw.RelativePath));
            if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            {
                warnings?.Add("原始证据文件不存在或越出会话目录：" + raw.RelativePath);
                rawOk = false;
                continue;
            }
            var info = new FileInfo(path);
            if (info.Length != raw.Length || !EffectEvidenceBundleCrypto.ComputeFileSha256(path).Equals(raw.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                warnings?.Add("原始证据文件摘要不一致：" + raw.RelativePath);
                rawOk = false;
            }
            if (!TryReadJsonPath(path, "$.scenario_id", out var scenarioId) || !scenarioId.Equals(raw.ScenarioId, StringComparison.OrdinalIgnoreCase))
            {
                warnings?.Add("原始证据文件的场景身份不一致：" + raw.RelativePath);
                rawOk = false;
            }
        }
        foreach (var observation in bundle.DerivedObservations)
        {
            var raw = bundle.RawFiles.FirstOrDefault(item => item.RelativePath.Equals(observation.SourceRelativePath, StringComparison.OrdinalIgnoreCase));
            if (raw == null)
            {
                warnings?.Add("派生观测没有对应原始文件：" + observation.Key);
                rawOk = false;
                continue;
            }
            var path = Path.GetFullPath(Path.Combine(sessionRoot, raw.RelativePath));
            if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) ||
                !TryReadJsonPath(path, observation.JsonPath, out var value) ||
                !value.Equals(observation.Value, StringComparison.Ordinal))
            {
                warnings?.Add("派生观测无法从原始 JSON 复算：" + observation.Key);
                rawOk = false;
            }
        }
        return rawOk;
    }

    private static bool TryReadJsonPath(string path, string jsonPath, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(jsonPath) || !jsonPath.StartsWith("$.", StringComparison.Ordinal)) return false;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            var current = document.RootElement;
            foreach (var part in jsonPath[2..].Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current)) return false;
            }
            value = current.ValueKind == JsonValueKind.String ? current.GetString() ?? string.Empty : current.GetRawText();
            return true;
        }
        catch { return false; }
    }
}
