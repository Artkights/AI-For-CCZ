using System.Security.Cryptography;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class PhysicalRecoverySandboxTrial
{
    public string SandboxRoot { get; set; } = string.Empty;
    public string OriginalExeSha256 { get; set; } = string.Empty;
    public string SandboxPatchSha256 { get; set; } = string.Empty;
    public string PatchPackageHash { get; set; } = string.Empty;
    public string ManifestId { get; set; } = string.Empty;
    public int EffectId { get; set; }
    public int TestPersonId { get; set; }
    public uint HookAddress { get; set; }
    public uint DispatcherAddress { get; set; }
    public uint ContinuationAddress { get; set; }
    public string OriginalHookBytesHex { get; set; } = string.Empty;
    public string LegacyBodySha256 { get; set; } = string.Empty;
    public string SummaryZh { get; set; } = string.Empty;
}

public sealed class PhysicalRecoverySandboxTrialService
{
    private const int EmptyPersonId = 1024;
    private const int AnyJobId = 255;

    public PhysicalRecoverySandboxTrial PrepareAndApply(
        CczProject originalProject,
        string sandboxRoot,
        int testPersonId = 0)
    {
        if (testPersonId is < 0 or >= EmptyPersonId)
            throw new ArgumentOutOfRangeException(nameof(testPersonId), "测试攻击者人物号必须在 0-1023 范围内。");
        if (!EffectSandboxService.TryRead(sandboxRoot, out var descriptor) ||
            !Path.GetFullPath(descriptor.OriginalGameRoot).Equals(Path.GetFullPath(originalProject.GameRoot), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("物理恢复试点只允许在当前项目自动创建并签名的验证副本中安装。");

        var originalExe = originalProject.ResolveGameFile("Ekd5.exe");
        var originalSha = EffectPatchByteService.Sha256(originalExe);
        if (!originalSha.Equals(descriptor.OriginalExeSha256, StringComparison.OrdinalIgnoreCase) ||
            !originalSha.Equals(EngineEffectProfileRegistry.Canonical65Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("物理恢复首轮试点只接受未变化的规范 6.5 基础 SHA。");

        var sandboxProject = new ProjectDetector().CreateProjectFromGameRoot(descriptor.SandboxRoot);
        var sandboxExe = sandboxProject.ResolveGameFile("Ekd5.exe");
        var sandboxBeforeSha = EffectPatchByteService.Sha256(sandboxExe);
        if (!sandboxBeforeSha.Equals(descriptor.SandboxExeSha256, StringComparison.OrdinalIgnoreCase) ||
            !sandboxBeforeSha.Equals(originalSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("验证副本已包含其他磁盘改动；请从规范 SHA 重新创建独立副本。");

        var free = new CompositeEffectService().FindFreeIds(sandboxProject, CompositeEffectChannel.PersonalJob);
        var effectId = free.FreeIds.Where(id => id is >= 0x01 and <= 0xFE).DefaultIfEmpty(-1).First();
        if (effectId < 0)
        {
            var examples = free.OccupiedReasonsZh.OrderBy(item => item.Key).Take(8)
                .Select(item => $"{item.Key:X2}={string.Join('/', item.Value)}");
            throw new InvalidOperationException(
                $"人物/兵种渠道的 01-FE 范围内没有完全空白号（占用 {free.OccupiedIds.Count}，仅名称可回收 {free.ReclaimableIds.Count}）；" +
                "试点不会回收仅有名称的定义。样例：" + string.Join("；", examples));
        }

        var contract = new HookExecutionContractService().Read(sandboxProject, "physical-after-damage-recovery-v2");
        if (contract.ContractVersion != 3 ||
            contract.ContinuationPolicy != HookContinuationPolicies.ChainExistingJumpTarget ||
            contract.ContinuationAddress != EngineRuntimeSemanticRegistry.LegacyPhysicalRecoveryBodyAddress ||
            !contract.EvidenceDisposition.Equals(EffectEvidenceDispositions.ValidationOnly, StringComparison.Ordinal))
            throw new InvalidOperationException("当前物理恢复契约不是只验证的 v3 链式 continuation 契约。");

        var hookBytes = EffectPatchByteService.ReadVirtualBytes(sandboxProject, contract.HookAddress, 5);
        var legacyBody = ReadVirtualBytes(sandboxProject,
            EngineRuntimeSemanticRegistry.LegacyPhysicalRecoveryBodyAddress,
            checked((int)(EngineRuntimeSemanticRegistry.LegacyPhysicalRecoveryBodyEndAddress -
                          EngineRuntimeSemanticRegistry.LegacyPhysicalRecoveryBodyAddress + 1)));
        var blueprint = new ModularCompositeEffectBlueprint
        {
            BlueprintId = "sandbox-physical-mp-restore-" + Guid.NewGuid().ToString("N"),
            RecipeId = "recipe.physical-after-damage-mp",
            Channel = CompositeEffectChannel.PersonalJob,
            EffectId = effectId,
            Name = "沙箱物理攻击恢复策略值",
            Description = "物理攻击每个实际伤害段命中后恢复 5 点策略值，并按人物最大策略值动态封顶。",
            TriggerModuleId = "trigger.physical-after-damage",
            SubjectModuleId = "subject.effect-owner",
            ConditionModuleIds = ["condition.personal-or-item"],
            ActionModuleId = "action.restore-mp",
            ValueModuleId = "value.fixed",
            Value = 5,
            BindingModuleIds = ["binding.personal-job"],
            Bindings =
            [
                new EffectPackageBinding
                {
                    Kind = "job_assignment",
                    RowId = effectId,
                    PersonId = testPersonId,
                    PersonId2 = EmptyPersonId,
                    PersonId3 = EmptyPersonId,
                    JobId = AnyJobId,
                    EffectValue = 0,
                    Note = "自动验证副本测试攻击者"
                }
            ]
        };

        var authoring = new ModularEffectAuthoringService();
        var preview = authoring.Preview(sandboxProject, blueprint);
        if (!preview.CanApply)
            throw new InvalidOperationException("物理恢复沙箱包预览失败：" + preview.SummaryZh + " " + string.Join("；", preview.WarningsZh));
        if (!preview.Package.Metadata.GetValueOrDefault("EffectWriteRunMode", string.Empty)
                .Equals(EffectWriteRunMode.SandboxValidation, StringComparison.Ordinal))
            throw new InvalidOperationException("物理恢复试点包没有锁定为 SandboxValidation，拒绝应用。");
        var packageHash = LockedEffectWriteReceiptService.ComputePackageHash(preview.Package);
        var apply = authoring.Apply(sandboxProject, preview.Package);
        if (!apply.Applied || string.IsNullOrWhiteSpace(apply.ManifestPath))
            throw new InvalidOperationException("物理恢复沙箱包没有形成可回滚的模块化 manifest。");

        var sandboxAfterSha = EffectPatchByteService.Sha256(sandboxExe);
        if (sandboxAfterSha.Equals(sandboxBeforeSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("物理恢复沙箱包应用后 EXE SHA 未变化，试点安装无效。");
        if (!EffectPatchByteService.Sha256(originalExe).Equals(originalSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("正式 EXE 在沙箱应用期间发生变化，立即停止验证。");

        var lifecycle = new ModularEffectLifecycleService();
        var manifestId = Path.GetFileNameWithoutExtension(apply.ManifestPath);
        var manifest = lifecycle.ReadModular(sandboxProject, manifestId);
        var dispatcher = lifecycle.ListDispatchers(sandboxProject).Single(item =>
            item.ManifestId.Equals(manifest.DispatcherManifestId, StringComparison.OrdinalIgnoreCase));
        var installedHook = EffectPatchByteService.ParseHex(
            EffectPatchByteService.ReadVirtualBytes(sandboxProject, contract.HookAddress, 5));
        if (!TryReadRel32Target(installedHook, contract.HookAddress, out var dispatcherTarget) ||
            dispatcherTarget != dispatcher.CodeAddress)
            throw new InvalidOperationException("00418335 没有指向新安装的共享调度器。");
        if (!ContainsRel32JumpTo(preview.Package, contract.ContinuationAddress))
            throw new InvalidOperationException("新调度器代码体没有可复读的 rel32 尾跳指向 004528FC。");
        var currentLegacyBody = ReadVirtualBytes(sandboxProject,
            EngineRuntimeSemanticRegistry.LegacyPhysicalRecoveryBodyAddress, legacyBody.Length);
        if (!currentLegacyBody.AsSpan().SequenceEqual(legacyBody))
            throw new InvalidOperationException("沙箱试点改动了 004528FC 遗留代码体，已违反链式 continuation 契约。");

        return new PhysicalRecoverySandboxTrial
        {
            SandboxRoot = descriptor.SandboxRoot,
            OriginalExeSha256 = originalSha,
            SandboxPatchSha256 = sandboxAfterSha,
            PatchPackageHash = packageHash,
            ManifestId = manifestId,
            EffectId = effectId,
            TestPersonId = testPersonId,
            HookAddress = contract.HookAddress,
            DispatcherAddress = dispatcher.CodeAddress,
            ContinuationAddress = contract.ContinuationAddress,
            OriginalHookBytesHex = hookBytes,
            LegacyBodySha256 = Sha256(legacyBody),
            SummaryZh = $"已在签名验证副本安装特效 {effectId:X2}：固定恢复 5 MP；00418335 -> {dispatcher.CodeAddress:X8} -> 004528FC。"
        };
    }

    public void Rollback(CczProject originalProject, PhysicalRecoverySandboxTrial trial)
    {
        if (!EffectSandboxService.TryRead(trial.SandboxRoot, out var descriptor) ||
            !descriptor.OriginalExeSha256.Equals(trial.OriginalExeSha256, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(descriptor.OriginalGameRoot).Equals(Path.GetFullPath(originalProject.GameRoot), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("物理恢复试点的沙箱签名或原项目身份已变化，拒绝自动回滚。");
        var originalExe = originalProject.ResolveGameFile("Ekd5.exe");
        if (!EffectPatchByteService.Sha256(originalExe).Equals(trial.OriginalExeSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("正式 EXE 在试点期间发生变化，拒绝把沙箱状态覆盖到正式项目。");

        var sandboxProject = new ProjectDetector().CreateProjectFromGameRoot(trial.SandboxRoot);
        var lifecycle = new ModularEffectLifecycleService();
        var preview = lifecycle.PreviewRemove(sandboxProject, trial.ManifestId);
        if (!preview.CanApply)
            throw new InvalidOperationException("物理恢复试点回滚预览失败：" + preview.SummaryZh);
        lifecycle.ApplyMaintenance(sandboxProject, preview.Package);

        var hook = EffectPatchByteService.ReadVirtualBytes(sandboxProject, trial.HookAddress, 5);
        if (!hook.Equals(trial.OriginalHookBytesHex, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("物理恢复试点回滚后 00418335 原始跳转没有恢复。");
        var legacyBody = ReadVirtualBytes(sandboxProject,
            EngineRuntimeSemanticRegistry.LegacyPhysicalRecoveryBodyAddress,
            checked((int)(EngineRuntimeSemanticRegistry.LegacyPhysicalRecoveryBodyEndAddress -
                          EngineRuntimeSemanticRegistry.LegacyPhysicalRecoveryBodyAddress + 1)));
        if (!Sha256(legacyBody).Equals(trial.LegacyBodySha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("物理恢复试点回滚后遗留代码体发生变化。");
        var sandboxSha = EffectPatchByteService.Sha256(sandboxProject.ResolveGameFile("Ekd5.exe"));
        if (!sandboxSha.Equals(descriptor.SandboxExeSha256, StringComparison.OrdinalIgnoreCase) ||
            !sandboxSha.Equals(trial.OriginalExeSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("物理恢复试点回滚后验证副本没有恢复到基础 SHA。");
        if (!EffectPatchByteService.Sha256(originalExe).Equals(trial.OriginalExeSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("正式 EXE 在回滚期间发生变化。");
    }

    private static byte[] ReadVirtualBytes(CczProject project, uint address, int length)
        => EffectPatchByteService.ParseHex(EffectPatchByteService.ReadVirtualBytes(project, address, length));

    private static bool TryReadRel32Target(byte[] bytes, uint address, out uint target)
    {
        target = 0;
        if (bytes.Length != 5 || bytes[0] != 0xE9) return false;
        target = unchecked((uint)(address + 5 + BitConverter.ToInt32(bytes, 1)));
        return true;
    }

    private static bool ContainsRel32JumpTo(EffectPackage package, uint target)
    {
        foreach (var segment in package.PatchSegments.Where(item =>
                     item.TargetFile.Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase) &&
                     item.AddressKind.Equals("OdVirtualAddress", StringComparison.OrdinalIgnoreCase)))
        {
            var bytes = EffectPatchByteService.ParseHex(segment.BytesHex);
            for (var offset = 0; offset <= bytes.Length - 5; offset++)
            {
                if (bytes[offset] != 0xE9) continue;
                var address = checked(segment.Address + (uint)offset);
                if (TryReadRel32Target(bytes.AsSpan(offset, 5).ToArray(), address, out var actual) && actual == target)
                    return true;
            }
        }
        return false;
    }

    private static string Sha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes));
}
