using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class ModularEffectLifecycleService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public AssemblyPatchPreviewResult PreviewCreate(
        CczProject project,
        ModularCompositeEffectBlueprint blueprint,
        SemanticEffectProgram program,
        CompiledSemanticBody body)
    {
        var existing = ListDispatchers(project).FirstOrDefault(item =>
            item.ContractId.Equals(program.HookContractId, StringComparison.OrdinalIgnoreCase) &&
            ResolveDispatcherStatus(project, item) == CompositeInstallationStatus.Complete);
        var dispatcherDraft = existing == null
            ? new EffectTriggerDispatcherDraft
            {
                DispatcherId = "dispatcher-" + body.Contract.ContractFamilyId,
                HookContractId = program.HookContractId,
                Capacity = 16,
                HookAddress = body.Contract.HookAddress
            }
            : new EffectTriggerDispatcherDraft
            {
                DispatcherId = existing.DispatcherId,
                HookContractId = existing.ContractId,
                Capacity = existing.Capacity,
                HookAddress = existing.HookAddress,
                CodeAddress = existing.CodeAddress,
                RegistryAddress = existing.RegistryAddress,
                Entries = existing.Entries.Select(CloneEntry).ToList()
            };
        if (dispatcherDraft.Entries.Any(item => item.PersonalEffectId == program.PersonalEffectId && item.ItemEffectId == program.ItemEffectId))
            return Failed("共享调度器中已经存在相同渠道编号的特效。");
        if (dispatcherDraft.Entries.Any(item => item.ExecutionOrder == program.ExecutionOrder))
            program.ExecutionOrder = dispatcherDraft.Entries.Max(item => item.ExecutionOrder) + 1;
        dispatcherDraft.Capacity = new EffectTriggerDispatcherService().NextCapacity(dispatcherDraft.Capacity, dispatcherDraft.Entries.Count + 1);
        dispatcherDraft.Entries.Add(ToEntry(program));

        AssemblyPatchPreviewResult preview;
        if (existing == null)
        {
            var compiled = new EffectTriggerDispatcherService().Compile(project, dispatcherDraft);
            if (!compiled.CanPreview) return Failed(compiled.SummaryZh, compiled.WarningsZh);
            var legacy = new HookContractService().BuildContracts(project).FirstOrDefault(item =>
                item.ConflictGroup == body.Contract.ConflictGroup && item.AllowPreview);
            if (legacy == null) return Failed("执行契约没有可供汇编编译器使用的兼容视图。");
            var draft = new AssemblyPatchDraft
            {
                Prompt = compiled.SummaryZh, TargetFile = "Ekd5.exe", EngineVersion = "6.5", EffectId = blueprint.EffectId,
                HookPoint = body.Contract.ContractFamilyId, HookAddress = body.Contract.HookAddress,
                OverwriteLength = 5, ExpectedOldBytesHex = EffectPatchByteService.ReadVirtualBytes(project, body.Contract.HookAddress, 5),
                HookContractId = legacy.ContractId, OriginalInstructionPolicy = legacy.OriginalInstructionPolicy,
                OriginalInstructionPlacement = OriginalInstructionPlacements.BeforeBody, PreserveFlags = true, ExpectedStackDelta = 0,
                RequiredSymbols = ["core_effect_engine"], RequiredCodeCaveBytes = Math.Max(1024, compiled.RegistryBytes.Length + 512),
                AssemblySource = compiled.AssemblySource, RegisterStrategy = "共享调度器统一保护寄存器和 EFLAGS。"
            };
            draft.Metadata["SemanticCompiler"] = "dispatcher-v2";
            draft.Metadata["SemanticProgramJson"] = JsonSerializer.Serialize(program, JsonOptions);
            draft.Metadata["EmbeddedDataMagic"] = EffectTriggerDispatcherService.Magic;
            if (EffectSandboxService.IsSandbox(project)) draft.Metadata["EffectWriteRunMode"] = EffectWriteRunMode.SandboxValidation;
            preview = new AssemblyPatchCompiler().Preview(project, draft);
            if (!preview.CanApply) return preview;
            var offset = FindMagic(preview.CodeCaveBytes, EffectTriggerDispatcherService.Magic);
            if (offset < 0) return Failed("编译后的调度器载荷没有恢复出 CCZD 注册表。");
            dispatcherDraft.CodeAddress = preview.Allocation.Allocation!.StartVirtualAddress;
            dispatcherDraft.RegistryAddress = checked(dispatcherDraft.CodeAddress + (uint)offset);
            SplitEmbeddedRegistrySegment(preview.Package, dispatcherDraft.CodeAddress, dispatcherDraft.RegistryAddress, compiled.RegistryBytes.Length);
        }
        else
        {
            var registry = new EffectTriggerDispatcherService().BuildRegistry(dispatcherDraft);
            var expected = ReadExpandableRegistryOldBytes(project, existing, registry.Length);
            var package = NewPackage(project, blueprint, program, dispatcherDraft, existing.ManifestId);
            package.PatchSegments.Add(new EffectPatchSegment
            {
                TargetFile = "Ekd5.exe", AddressKind = "OdVirtualAddress", Address = existing.RegistryAddress,
                AddressHex = $"0x{existing.RegistryAddress:X8}", BytesHex = EffectPatchByteService.ToHex(registry),
                ExpectedOldBytesHex = expected, HookPoint = "更新共享特效注册表", Comment = "不重复安装入口 Hook。"
            });
            preview = new AssemblyPatchPreviewResult { Package = package };
        }

        AddDefinitionSegments(project, blueprint, preview.Package);
        AddLifecycleMetadata(project, preview.Package, blueprint, program, dispatcherDraft, existing?.ManifestId ?? string.Empty);
        preview.Package.Metadata["HookExecutionContractId"] = body.Contract.ContractId;
        preview.Package.Metadata["HookExecutionContractHash"] = body.Contract.ContractHash;
        preview.Package.Metadata["ContractCodeIdentityHash"] = body.Contract.ContractCodeIdentityHash;
        preview.Package.Metadata["HookExecutionContractJson"] = JsonSerializer.Serialize(body.Contract, JsonOptions);
        var patchPreview = new EffectTransactionalPatchService().Preview(project, preview.Package);
        preview.PatchPreview = patchPreview;
        preview.Warnings.AddRange(patchPreview.Warnings);
        preview.CanApply = patchPreview.CanApply && preview.Warnings.Count == 0;
        preview.Summary = preview.CanApply
            ? existing == null ? "共享调度器首装预览通过：将安装一个入口并登记首个特效。" : "共享调度器追加预览通过：只增加一个注册条目。"
            : "共享调度器预览未通过：" + string.Join("；", preview.Warnings.Take(8));
        return preview;
    }

    public CompositeEffectApplyResult ApplyCreate(CczProject project, EffectPackage package)
    {
        if (package.Metadata.GetValueOrDefault("LogicalPatchKind") != "modular-semantic-effect-v2")
            throw new InvalidOperationException("只接受模块化语义特效预览返回的锁定包。");
        var beforeSha = EffectPatchByteService.Sha256(project.ResolveGameFile("Ekd5.exe"));
        var dispatcherDraft = JsonSerializer.Deserialize<EffectTriggerDispatcherDraft>(package.Metadata["DispatcherDraftJson"], JsonOptions)
                              ?? throw new InvalidOperationException("共享调度器草案无法读取。");
        var blueprint = JsonSerializer.Deserialize<ModularCompositeEffectBlueprint>(package.Metadata["BlueprintJson"], JsonOptions)
                        ?? throw new InvalidOperationException("模块蓝图无法读取。");
        var program = JsonSerializer.Deserialize<SemanticEffectProgram>(package.Metadata["SemanticProgramJson"], JsonOptions)
                      ?? throw new InvalidOperationException("语义程序无法读取。");
        var contractSnapshot = JsonSerializer.Deserialize<HookExecutionContract>(package.Metadata["HookExecutionContractJson"], JsonOptions)
                               ?? throw new InvalidOperationException("执行契约快照无法读取。");
        if (!contractSnapshot.ContractHash.Equals(package.Metadata["HookExecutionContractHash"], StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("执行契约快照与预览锁定摘要不一致。");
        var transaction = new EffectTransactionalPatchService();
        var apply = transaction.Apply(project, package, "modular-semantic-effect");
        var dispatcherPath = string.Empty;
        var modularPath = string.Empty;
        string? previousDispatcherJson = null;
        try
        {
            var dispatcherId = package.Metadata.GetValueOrDefault("ExistingDispatcherManifest", string.Empty);
            EffectDispatcherManifestV2 dispatcherManifest;
            if (string.IsNullOrWhiteSpace(dispatcherId))
            {
                dispatcherManifest = new EffectDispatcherManifestV2
                {
                    ManifestId = dispatcherDraft.DispatcherId,
                    DispatcherId = dispatcherDraft.DispatcherId,
                    ProjectIdentity = new ProjectPatchIdentityService().Build(project),
                    ContractId = program.HookContractId,
                    ContractHash = package.Metadata["HookExecutionContractHash"],
                    ContractVersion = contractSnapshot.ContractVersion,
                    ContractSnapshot = contractSnapshot,
                    HookAddress = dispatcherDraft.HookAddress,
                    CodeAddress = dispatcherDraft.CodeAddress,
                    RegistryAddress = dispatcherDraft.RegistryAddress,
                    RegistryLength = new EffectTriggerDispatcherService().BuildRegistry(dispatcherDraft).Length,
                    Capacity = dispatcherDraft.Capacity,
                    ExeSha256Before = beforeSha,
                    InstallationPackage = BuildDispatcherInstallationPackage(package, dispatcherDraft)
                };
            }
            else
            {
                dispatcherManifest = ReadDispatcher(project, dispatcherId);
                dispatcherPath = DispatcherPath(project, dispatcherManifest.ManifestId);
                previousDispatcherJson = File.ReadAllText(dispatcherPath, Encoding.UTF8);
                UpdateDispatcherRegistryOwnership(dispatcherManifest, dispatcherDraft, package);
            }
            dispatcherManifest.Entries = dispatcherDraft.Entries.Select(CloneEntry).ToList();
            dispatcherManifest.Capacity = dispatcherDraft.Capacity;
            dispatcherManifest.RegistryLength = new EffectTriggerDispatcherService().BuildRegistry(dispatcherDraft).Length;
            dispatcherManifest.ExeSha256After = EffectPatchByteService.Sha256(project.ResolveGameFile("Ekd5.exe"));
            dispatcherManifest.UpdatedAt = DateTime.Now;
            dispatcherPath = SaveDispatcher(project, dispatcherManifest);

            var modular = new EffectTriggerDispatcherService().BuildManifest(blueprint, [program], [dispatcherDraft], package,
                beforeSha, dispatcherManifest.ExeSha256After);
            modular.ProjectIdentity = new ProjectPatchIdentityService().Build(project);
            modular.DispatcherManifestId = dispatcherManifest.ManifestId;
            modular.DispatcherEntryId = program.ProgramId;
            modular.RegistryAddress = dispatcherManifest.RegistryAddress;
            modularPath = SaveModular(project, modular);
            EffectInventoryService.Invalidate(project);
            var reread = ReadModular(project, modular.ManifestId);
            if (ResolveModularStatus(project, reread) != CompositeInstallationStatus.Complete)
                throw new InvalidOperationException("写后无法从共享注册表恢复出完整模块化特效。");
            return new CompositeEffectApplyResult
            {
                Applied = true, SummaryZh = existingSummary(package), ManifestPath = modularPath,
                BackupPaths = apply.BackupPaths
            };
        }
        catch (Exception verificationError)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(modularPath) && File.Exists(modularPath)) File.Delete(modularPath);
                if (previousDispatcherJson != null && !string.IsNullOrWhiteSpace(dispatcherPath)) File.WriteAllText(dispatcherPath, previousDispatcherJson, Encoding.UTF8);
                else if (!string.IsNullOrWhiteSpace(dispatcherPath) && File.Exists(dispatcherPath)) File.Delete(dispatcherPath);
                transaction.Restore(project, apply);
            }
            catch (Exception restoreError)
            {
                throw new InvalidOperationException("模块化特效写后校验失败且恢复不完整：" + verificationError.Message + "；" + restoreError.Message, verificationError);
            }
            throw new InvalidOperationException("模块化特效写后校验失败，已恢复全部文件：" + verificationError.Message, verificationError);
        }
    }

    public IReadOnlyList<EffectDispatcherManifestV2> ListDispatchers(CczProject project)
        => ReadJsonFiles<EffectDispatcherManifestV2>(ProjectPatchIdentityService.DispatcherManifestRoot(project))
            .Where(item => new ProjectPatchIdentityService().Matches(project, item.ProjectIdentity)).ToList();

    public IReadOnlyList<ModularEffectManifestV2> ListModular(CczProject project)
        => ReadJsonFiles<ModularEffectManifestV2>(ProjectPatchIdentityService.ModularManifestRoot(project))
            .Where(item => new ProjectPatchIdentityService().Matches(project, item.ProjectIdentity)).ToList();

    public ModularEffectManifestV2 ReadModular(CczProject project, string manifestId)
        => ListModular(project).FirstOrDefault(item => item.ManifestId.Equals(Path.GetFileNameWithoutExtension(manifestId), StringComparison.OrdinalIgnoreCase) ||
                                                     item.Blueprint.BlueprintId.Equals(manifestId, StringComparison.OrdinalIgnoreCase))
           ?? throw new InvalidOperationException("找不到模块化特效清单。");

    public string ResolveDispatcherStatus(CczProject project, EffectDispatcherManifestV2 manifest)
    {
        try
        {
            var removed = manifest.InstallationStatus == CompositeInstallationStatus.Removed || manifest.StatusZh == "已移除";
            foreach (var segment in manifest.InstallationPackage.PatchSegments)
            {
                var expected = removed ? segment.ExpectedOldBytesHex : segment.BytesHex;
                var length = EffectPatchByteService.ParseHex(expected).Length;
                if (length == 0) return CompositeInstallationStatus.Incomplete;
                var currentSegmentBytes = segment.AddressKind.Equals("OdVirtualAddress", StringComparison.OrdinalIgnoreCase)
                    ? EffectPatchByteService.ReadVirtualBytes(project, segment.Address, length)
                    : ReadFileBytes(project, segment, length);
                if (!currentSegmentBytes.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    return CompositeInstallationStatus.ExternallyModified;
            }
            if (removed) return CompositeInstallationStatus.Removed;
            var current = EffectPatchByteService.ParseHex(EffectPatchByteService.ReadVirtualBytes(project, manifest.RegistryAddress, manifest.RegistryLength));
            var parsed = new EffectTriggerDispatcherService().ReadRegistry(current, manifest.DispatcherId, manifest.ContractId);
            if (parsed.Entries.Count != manifest.Entries.Count) return CompositeInstallationStatus.ExternallyModified;
            return parsed.Entries.All(actual => manifest.Entries.Any(expected => SameEntry(actual, expected)))
                ? CompositeInstallationStatus.Complete : CompositeInstallationStatus.ExternallyModified;
        }
        catch { return CompositeInstallationStatus.Incomplete; }
    }

    public string ResolveModularStatus(CczProject project, ModularEffectManifestV2 manifest)
    {
        if (manifest.InstallationStatus == CompositeInstallationStatus.Removed || manifest.StatusZh == "已移除")
            return CompositeInstallationStatus.Removed;
        var dispatcher = ListDispatchers(project).FirstOrDefault(item => item.ManifestId.Equals(manifest.DispatcherManifestId, StringComparison.OrdinalIgnoreCase));
        if (dispatcher == null || ResolveDispatcherStatus(project, dispatcher) != CompositeInstallationStatus.Complete)
            return CompositeInstallationStatus.Incomplete;
        var entry = dispatcher.Entries.FirstOrDefault(item => item.EntryId.Equals(manifest.DispatcherEntryId, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return CompositeInstallationStatus.Incomplete;
        return entry.Enabled ? CompositeInstallationStatus.Complete : CompositeInstallationStatus.Disabled;
    }

    public CompositeEffectMaintenancePreview PreviewUpdate(CczProject project, ModularEffectMaintenanceDraft draft)
    {
        var manifest = ReadModular(project, draft.ManifestId);
        var dispatcher = ReadDispatcher(project, manifest.DispatcherManifestId);
        if (!IsV2Dispatcher(dispatcher)) return FailedMaintenance("旧契约 v1 调度器只允许读取和安全删除/恢复；禁止更新或追加条目。请先导入 v2 证据并生成迁移预览。");
        var entry = dispatcher.Entries.First(item => item.EntryId.Equals(manifest.DispatcherEntryId, StringComparison.OrdinalIgnoreCase));
        if (draft.Value.HasValue) entry.Value = draft.Value.Value;
        if (draft.ExecutionOrder.HasValue) entry.ExecutionOrder = draft.ExecutionOrder.Value;
        var result = BuildMaintenancePreview(project, manifest, dispatcher, "Update", "修改模块化特效", entry);
        if (result.WarningsZh.Count == 0 && (draft.Name != null || draft.Description != null || draft.ReplaceBindings))
        {
            var native = new NativeEffectConfigurationService().Preview(project, new NativeEffectEditDraft
            {
                Channel = manifest.Blueprint.Channel, EffectId = manifest.Blueprint.EffectId,
                Name = draft.Name, Description = draft.Description,
                ReplaceAllBindings = draft.ReplaceBindings, Bindings = draft.Bindings.Select(CloneBinding).ToList()
            });
            if (!native.CanApply) result.WarningsZh.AddRange(native.WarningsZh);
            else result.Package.PatchSegments.AddRange(native.Package.PatchSegments);
        }
        result.Package.Metadata["ModularMaintenanceDraftJson"] = JsonSerializer.Serialize(draft, JsonOptions);
        return FinalizeMaintenance(project, result);
    }

    public CompositeEffectMaintenancePreview PreviewToggle(CczProject project, string manifestId, bool enable)
    {
        var manifest = ReadModular(project, manifestId);
        var dispatcher = ReadDispatcher(project, manifest.DispatcherManifestId);
        if (!IsV2Dispatcher(dispatcher) && enable) return FailedMaintenance("旧契约 v1 调度器禁止重新启用；只能保持读取或执行安全删除/恢复。");
        var entry = dispatcher.Entries.First(item => item.EntryId.Equals(manifest.DispatcherEntryId, StringComparison.OrdinalIgnoreCase));
        entry.Enabled = enable;
        return FinalizeMaintenance(project, BuildMaintenancePreview(project, manifest, dispatcher,
            enable ? "Enable" : "Disable", enable ? "启用模块化特效" : "停用模块化特效", entry));
    }

    public CompositeEffectMaintenancePreview PreviewRepair(CczProject project, string manifestId)
    {
        var manifest = ReadModular(project, manifestId);
        var dispatcher = ReadDispatcher(project, manifest.DispatcherManifestId);
        if (!IsV2Dispatcher(dispatcher)) return FailedMaintenance("旧契约 v1 调度器禁止自动修复；不能把历史 F0/FC 字符串替换后继续使用。");
        var status = ResolveDispatcherStatus(project, dispatcher);
        if (status == CompositeInstallationStatus.Complete)
            return FailedMaintenance("共享调度器字节完整，不需要修复。");
        var package = NewMaintenancePackage(project, manifest, "Repair", "修复模块化特效");
        package.Metadata["DispatcherDraftJson"] = JsonSerializer.Serialize(new EffectTriggerDispatcherDraft
        {
            DispatcherId = dispatcher.DispatcherId, HookContractId = dispatcher.ContractId, Capacity = dispatcher.Capacity,
            RegistryAddress = dispatcher.RegistryAddress, HookAddress = dispatcher.HookAddress, CodeAddress = dispatcher.CodeAddress,
            Entries = dispatcher.Entries.Select(CloneEntry).ToList()
        }, JsonOptions);
        foreach (var segment in dispatcher.InstallationPackage.PatchSegments.Where(item => item.TargetFile.Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase)))
        {
            var current = EffectPatchByteService.ReadVirtualBytes(project, segment.Address, EffectPatchByteService.ParseHex(segment.BytesHex).Length);
            if (current.Equals(segment.BytesHex, StringComparison.OrdinalIgnoreCase)) continue;
            if (!current.Equals(segment.ExpectedOldBytesHex, StringComparison.OrdinalIgnoreCase))
                return FailedMaintenance("共享调度器存在未知外部修改，不能自动修复。");
            package.PatchSegments.Add(new EffectPatchSegment
            {
                TargetFile = segment.TargetFile, AddressKind = segment.AddressKind, Address = segment.Address,
                AddressHex = segment.AddressHex, BytesHex = segment.BytesHex, ExpectedOldBytesHex = current,
                HookPoint = "修复共享调度器", Comment = "只恢复仍等于安装前旧字节的位置。"
            });
        }
        var result = new CompositeEffectMaintenancePreview { Operation = "Repair", Package = package };
        if (package.PatchSegments.Count == 0) result.WarningsZh.Add("没有可安全修复的锁定位置。");
        return FinalizeMaintenance(project, result);
    }

    public CompositeEffectMaintenancePreview PreviewRemove(CczProject project, string manifestId)
    {
        var manifest = ReadModular(project, manifestId);
        var dispatcher = ReadDispatcher(project, manifest.DispatcherManifestId);
        var remaining = dispatcher.Entries.Where(item => !item.EntryId.Equals(manifest.DispatcherEntryId, StringComparison.OrdinalIgnoreCase)).Select(CloneEntry).ToList();
        var package = NewMaintenancePackage(project, manifest, "Remove", "删除模块化特效");
        if (remaining.Count > 0)
        {
            var draft = new EffectTriggerDispatcherDraft
            {
                DispatcherId = dispatcher.DispatcherId, HookContractId = dispatcher.ContractId, Capacity = dispatcher.Capacity,
                RegistryAddress = dispatcher.RegistryAddress, HookAddress = dispatcher.HookAddress, CodeAddress = dispatcher.CodeAddress,
                Entries = remaining
            };
            package.PatchSegments.Add(BuildRegistryUpdateSegment(project, dispatcher, draft, "删除共享调度注册条目"));
            package.Metadata["DispatcherDraftJson"] = JsonSerializer.Serialize(draft, JsonOptions);
        }
        else
        {
            foreach (var segment in dispatcher.InstallationPackage.PatchSegments)
            {
                package.PatchSegments.Add(new EffectPatchSegment
                {
                    TargetFile = segment.TargetFile, AddressKind = segment.AddressKind, Address = segment.Address,
                    AddressHex = segment.AddressHex, BytesHex = segment.ExpectedOldBytesHex, ExpectedOldBytesHex = segment.BytesHex,
                    HookPoint = "删除最后一个模块化特效", Comment = "恢复共享调度器安装前字节并释放代码洞。"
                });
            }
            package.Metadata["RemoveDispatcher"] = "true";
        }
        var native = new NativeEffectConfigurationService().Preview(project, new NativeEffectEditDraft
        {
            Channel = manifest.Blueprint.Channel, EffectId = manifest.Blueprint.EffectId,
            Name = string.Empty, Description = string.Empty, ReplaceAllBindings = true, Bindings = []
        });
        if (!native.CanApply) return FailedMaintenance("无法生成名称、说明和绑定清理段：" + string.Join("；", native.WarningsZh));
        package.PatchSegments.AddRange(native.Package.PatchSegments);
        return FinalizeMaintenance(project, new CompositeEffectMaintenancePreview { Operation = "Remove", Package = package });
    }

    public EffectTransactionalApplyResult ApplyMaintenance(CczProject project, EffectPackage package)
    {
        var operation = package.Metadata.GetValueOrDefault("ModularMaintenanceOperation")
                        ?? throw new InvalidOperationException("模块化维护包缺少操作类型。");
        var manifest = ReadModular(project, package.Metadata["SourceModularManifest"]);
        var dispatcher = ReadDispatcher(project, manifest.DispatcherManifestId);
        var transaction = new EffectTransactionalPatchService();
        var result = transaction.Apply(project, package, "modular-semantic-" + operation.ToLowerInvariant());
        var modularPath = Path.Combine(ProjectPatchIdentityService.ModularManifestRoot(project), manifest.ManifestId + ".json");
        var dispatcherPath = DispatcherPath(project, dispatcher.ManifestId);
        var previousModularJson = File.ReadAllText(modularPath, Encoding.UTF8);
        var previousDispatcherJson = File.ReadAllText(dispatcherPath, Encoding.UTF8);
        try
        {
            if (operation == "Remove")
            {
                manifest.InstallationStatus = CompositeInstallationStatus.Removed; manifest.StatusZh = "已移除";
                File.WriteAllText(modularPath, JsonSerializer.Serialize(manifest, JsonOptions), Encoding.UTF8);
                if (package.Metadata.GetValueOrDefault("RemoveDispatcher") == "true")
                {
                    dispatcher.InstallationStatus = CompositeInstallationStatus.Removed; dispatcher.StatusZh = "已移除";
                }
                else
                {
                    var updated = JsonSerializer.Deserialize<EffectTriggerDispatcherDraft>(package.Metadata["DispatcherDraftJson"], JsonOptions)!;
                    dispatcher.Entries = updated.Entries.Select(CloneEntry).ToList();
                    UpdateDispatcherRegistryOwnership(dispatcher, updated, package);
                }
            }
            else
            {
                var updated = JsonSerializer.Deserialize<EffectTriggerDispatcherDraft>(package.Metadata["DispatcherDraftJson"], JsonOptions)!;
                dispatcher.Entries = updated.Entries.Select(CloneEntry).ToList();
                if (operation != "Repair") UpdateDispatcherRegistryOwnership(dispatcher, updated, package);
                var program = manifest.Programs.First(item => item.ProgramId.Equals(manifest.DispatcherEntryId, StringComparison.OrdinalIgnoreCase));
                var entry = dispatcher.Entries.First(item => item.EntryId.Equals(manifest.DispatcherEntryId, StringComparison.OrdinalIgnoreCase));
                program.Value = entry.Value; program.ExecutionOrder = entry.ExecutionOrder; program.Enabled = entry.Enabled;
                manifest.InstallationStatus = entry.Enabled ? CompositeInstallationStatus.Complete : CompositeInstallationStatus.Disabled;
                manifest.StatusZh = entry.Enabled ? "已安装" : "已停用";
                if (package.Metadata.TryGetValue("ModularMaintenanceDraftJson", out var json))
                {
                    var draft = JsonSerializer.Deserialize<ModularEffectMaintenanceDraft>(json, JsonOptions);
                    if (draft?.Name != null) manifest.Blueprint.Name = draft.Name;
                    if (draft?.Description != null) manifest.Blueprint.Description = draft.Description;
                    if (draft?.ReplaceBindings == true) manifest.Blueprint.Bindings = draft.Bindings.Select(CloneBinding).ToList();
                }
                SaveModular(project, manifest);
            }
            dispatcher.ExeSha256After = EffectPatchByteService.Sha256(project.ResolveGameFile("Ekd5.exe"));
            dispatcher.UpdatedAt = DateTime.Now;
            SaveDispatcher(project, dispatcher);
            EffectInventoryService.Invalidate(project);
            var rereadDispatcher = ReadDispatcher(project, dispatcher.ManifestId);
            var dispatcherStatus = ResolveDispatcherStatus(project, rereadDispatcher);
            if (operation == "Remove" && package.Metadata.GetValueOrDefault("RemoveDispatcher") == "true")
            {
                if (dispatcherStatus != CompositeInstallationStatus.Removed)
                    throw new InvalidOperationException("删除最后一个注册条目后，共享调度器入口或代码洞没有完整恢复。");
            }
            else if (dispatcherStatus != CompositeInstallationStatus.Complete)
            {
                throw new InvalidOperationException("共享调度器入口、代码体或注册表写后复读不完整。");
            }
            var rereadModular = ReadModular(project, manifest.ManifestId);
            var expectedModularStatus = operation switch
            {
                "Remove" => CompositeInstallationStatus.Removed,
                "Disable" => CompositeInstallationStatus.Disabled,
                "Enable" => CompositeInstallationStatus.Complete,
                _ => dispatcher.Entries.FirstOrDefault(item => item.EntryId.Equals(manifest.DispatcherEntryId, StringComparison.OrdinalIgnoreCase))?.Enabled == false
                    ? CompositeInstallationStatus.Disabled
                    : CompositeInstallationStatus.Complete
            };
            if (ResolveModularStatus(project, rereadModular) != expectedModularStatus)
                throw new InvalidOperationException("模块化特效清单与共享注册表写后状态不一致。");
            return result;
        }
        catch (Exception ex)
        {
            Exception? restoreError = null;
            try
            {
                transaction.Restore(project, result);
                File.WriteAllText(modularPath, previousModularJson, Encoding.UTF8);
                File.WriteAllText(dispatcherPath, previousDispatcherJson, Encoding.UTF8);
                EffectInventoryService.Invalidate(project);
            }
            catch (Exception inner)
            {
                restoreError = inner;
            }
            if (restoreError != null)
                throw new InvalidOperationException("模块化维护写后复读失败，事务或清单恢复不完整：" + ex.Message + "；" + restoreError.Message, ex);
            throw new InvalidOperationException("模块化维护写后复读失败，已恢复文件和清单：" + ex.Message, ex);
        }
    }

    private CompositeEffectMaintenancePreview BuildMaintenancePreview(CczProject project, ModularEffectManifestV2 manifest,
        EffectDispatcherManifestV2 dispatcher, string operation, string name, EffectDispatcherEntry updatedEntry)
    {
        var draft = new EffectTriggerDispatcherDraft
        {
            DispatcherId = dispatcher.DispatcherId, HookContractId = dispatcher.ContractId, Capacity = dispatcher.Capacity,
            RegistryAddress = dispatcher.RegistryAddress, HookAddress = dispatcher.HookAddress, CodeAddress = dispatcher.CodeAddress,
            Entries = dispatcher.Entries.Select(item => item.EntryId.Equals(updatedEntry.EntryId, StringComparison.OrdinalIgnoreCase) ? CloneEntry(updatedEntry) : CloneEntry(item)).ToList()
        };
        var package = NewMaintenancePackage(project, manifest, operation, name);
        package.PatchSegments.Add(BuildRegistryUpdateSegment(project, dispatcher, draft, name));
        package.Metadata["DispatcherDraftJson"] = JsonSerializer.Serialize(draft, JsonOptions);
        return new CompositeEffectMaintenancePreview { Operation = operation, Package = package };
    }

    private static EffectPatchSegment BuildRegistryUpdateSegment(CczProject project, EffectDispatcherManifestV2 dispatcher,
        EffectTriggerDispatcherDraft draft, string comment)
    {
        var bytes = new EffectTriggerDispatcherService().BuildRegistry(draft);
        var old = ReadExpandableRegistryOldBytes(project, dispatcher, bytes.Length);
        return new EffectPatchSegment
        {
            TargetFile = "Ekd5.exe", AddressKind = "OdVirtualAddress", Address = dispatcher.RegistryAddress,
            AddressHex = $"0x{dispatcher.RegistryAddress:X8}", BytesHex = EffectPatchByteService.ToHex(bytes),
            ExpectedOldBytesHex = old,
            HookPoint = comment, Comment = comment
        };
    }

    private EffectPackage NewMaintenancePackage(CczProject project, ModularEffectManifestV2 manifest, string operation, string name)
    {
        var dispatcher = ReadDispatcher(project, manifest.DispatcherManifestId);
        var contract = dispatcher.ContractSnapshot;
        var package = new EffectPackage
        {
            SchemaVersion = "2.0", PackageId = "modular-" + operation.ToLowerInvariant() + "-" + manifest.ManifestId + "-" + DateTime.Now.ToString("yyyyMMddHHmmssfff"),
            Domain = "patch", EffectId = manifest.Blueprint.EffectId, Name = name,
            Metadata =
            {
                ["LogicalPatchKind"] = "modular-semantic-maintenance-v2",
                ["SourceModularManifest"] = manifest.ManifestId,
                ["ModularMaintenanceOperation"] = operation,
                ["EngineProfileSha256"] = new EnginePatchProfileService().Build(project).ExeSha256,
                ["EffectWriteRunMode"] = EffectSandboxService.IsSandbox(project)
                    ? EffectWriteRunMode.SandboxValidation
                    : EffectWriteRunMode.Formal
            }
        };
        if (contract != null)
        {
            package.Metadata["HookExecutionContractId"] = contract.ContractId;
            package.Metadata["HookExecutionContractHash"] = contract.ContractHash;
            package.Metadata["ContractCodeIdentityHash"] = contract.ContractCodeIdentityHash;
        }
        return package;
    }

    private static CompositeEffectMaintenancePreview FinalizeMaintenance(CczProject project, CompositeEffectMaintenancePreview result)
    {
        if (result.WarningsZh.Count == 0)
        {
            result.PatchPreview = new EffectTransactionalPatchService().Preview(project, result.Package);
            result.WarningsZh.AddRange(result.PatchPreview.Warnings);
        }
        result.CanApply = result.Package.PatchSegments.Count > 0 && result.WarningsZh.Count == 0 && result.PatchPreview.CanApply;
        if (result.CanApply)
            new LockedEffectWriteReceiptService().Issue(project, result.Package, "modular-semantic-" + result.Operation.ToLowerInvariant());
        result.SummaryZh = result.CanApply ? $"模块化特效{result.Operation}预览通过。" : "模块化维护预览未通过：" + string.Join("；", result.WarningsZh.Take(8));
        return result;
    }

    private static CompositeEffectMaintenancePreview FailedMaintenance(string warning)
        => new() { WarningsZh = [warning], SummaryZh = warning };

    private static bool IsV2Dispatcher(EffectDispatcherManifestV2 dispatcher)
        => dispatcher.ContractVersion >= 2 && dispatcher.ContractSnapshot?.ContractVersion >= 2 &&
           dispatcher.ContractId.EndsWith("-v2", StringComparison.OrdinalIgnoreCase);

    private static void AddDefinitionSegments(CczProject project, ModularCompositeEffectBlueprint blueprint, EffectPackage package)
    {
        var native = new NativeEffectConfigurationService().Preview(project, new NativeEffectEditDraft
        {
            Channel = blueprint.Channel, EffectId = blueprint.EffectId, Name = blueprint.Name,
            Description = blueprint.Description, ReplaceAllBindings = true, Bindings = blueprint.Bindings.Select(CloneBinding).ToList()
        });
        if (!native.CanApply) throw new InvalidOperationException("新特效定义与绑定预览失败：" + string.Join("；", native.WarningsZh.Take(8)));
        package.PatchSegments.AddRange(native.Package.PatchSegments);
        package.Bindings = blueprint.Bindings.Select(CloneBinding).ToList();
        package.Name = blueprint.Name;
        package.Description = blueprint.Description;
    }

    private static void AddLifecycleMetadata(CczProject project, EffectPackage package, ModularCompositeEffectBlueprint blueprint,
        SemanticEffectProgram program, EffectTriggerDispatcherDraft dispatcher, string existingManifest)
    {
        package.Metadata.Remove(LockedEffectWriteReceiptService.MetadataKey);
        package.Metadata["LogicalPatchKind"] = "modular-semantic-effect-v2";
        package.Metadata["BlueprintJson"] = JsonSerializer.Serialize(blueprint, JsonOptions);
        package.Metadata["SemanticProgramJson"] = JsonSerializer.Serialize(program, JsonOptions);
        package.Metadata["DispatcherDraftJson"] = JsonSerializer.Serialize(dispatcher, JsonOptions);
        package.Metadata["ExistingDispatcherManifest"] = existingManifest;
        var contract = new HookExecutionContractService().Read(project, program.HookContractId);
        package.Metadata["HookExecutionContractId"] = contract.ContractId;
        package.Metadata["HookExecutionContractHash"] = contract.ContractHash;
        package.Metadata["ContractCodeIdentityHash"] = contract.ContractCodeIdentityHash;
        package.Metadata["EffectWriteRunMode"] = EffectSandboxService.IsSandbox(project)
            ? EffectWriteRunMode.SandboxValidation
            : EffectWriteRunMode.Formal;
        package.Metadata["RegistryAddress"] = $"0x{dispatcher.RegistryAddress:X8}";
    }

    private static EffectPackage NewPackage(CczProject project, ModularCompositeEffectBlueprint blueprint, SemanticEffectProgram program,
        EffectTriggerDispatcherDraft dispatcher, string existingManifest)
    {
        var package = new EffectPackage
        {
            SchemaVersion = "2.0", PackageId = "modular-semantic-" + program.ProgramId + "-" + DateTime.Now.ToString("yyyyMMddHHmmssfff"),
            Domain = "patch", EffectId = blueprint.EffectId, Name = blueprint.Name, Description = blueprint.Description,
            Metadata = { ["EngineProfileSha256"] = new EnginePatchProfileService().Build(project).ExeSha256 }
        };
        AddLifecycleMetadata(project, package, blueprint, program, dispatcher, existingManifest);
        return package;
    }

    private static AssemblyPatchPreviewResult Failed(string warning, IEnumerable<string>? details = null)
    {
        var result = new AssemblyPatchPreviewResult { Summary = warning };
        result.Warnings.Add(warning);
        if (details != null) result.Warnings.AddRange(details);
        return result;
    }

    private static EffectDispatcherEntry ToEntry(SemanticEffectProgram program) => new()
    {
        EntryId = program.ProgramId, Enabled = program.Enabled, PersonalEffectId = program.PersonalEffectId,
        ItemEffectId = program.ItemEffectId, EffectValueMode = program.EffectValueMode, StackingMode = program.StackingMode,
        Action = program.Action, ValueSource = program.ValueSource, Value = program.Value, ExecutionOrder = program.ExecutionOrder
    };
    private static EffectDispatcherEntry CloneEntry(EffectDispatcherEntry item) => new()
    {
        EntryId = item.EntryId, Enabled = item.Enabled, PersonalEffectId = item.PersonalEffectId, ItemEffectId = item.ItemEffectId,
        EffectValueMode = item.EffectValueMode, StackingMode = item.StackingMode, Action = item.Action,
        ValueSource = item.ValueSource, Value = item.Value, ExecutionOrder = item.ExecutionOrder
    };
    private static EffectPackageBinding CloneBinding(EffectPackageBinding item) => new()
    {
        Kind = item.Kind, RowId = item.RowId, ItemId = item.ItemId, PersonId = item.PersonId, PersonId2 = item.PersonId2,
        PersonId3 = item.PersonId3, JobId = item.JobId, ItemId2 = item.ItemId2, ItemId3 = item.ItemId3, ItemId4 = item.ItemId4,
        EffectValue = item.EffectValue, Values = new Dictionary<string, int>(item.Values), Note = item.Note, Remove = item.Remove
    };
    private static EffectPackage ClonePackage(EffectPackage source) => JsonSerializer.Deserialize<EffectPackage>(JsonSerializer.Serialize(source, JsonOptions), JsonOptions)!;

    private static EffectPackage BuildDispatcherInstallationPackage(
        EffectPackage source,
        EffectTriggerDispatcherDraft dispatcher)
    {
        var package = ClonePackage(source);
        package.Bindings.Clear();
        package.Name = string.Empty;
        package.Description = string.Empty;
        package.EffectValue = null;
        package.Metadata.Remove(LockedEffectWriteReceiptService.MetadataKey);
        package.PatchSegments = package.PatchSegments
            .Where(segment =>
                segment.TargetFile.Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase) &&
                segment.AddressKind.Equals("OdVirtualAddress", StringComparison.OrdinalIgnoreCase) &&
                (segment.Address == dispatcher.HookAddress ||
                 segment.Address == dispatcher.CodeAddress ||
                 segment.Address == dispatcher.RegistryAddress))
            .Select(CloneSegment)
            .ToList();

        var requiredAddresses = new[]
        {
            dispatcher.HookAddress,
            dispatcher.CodeAddress,
            dispatcher.RegistryAddress
        };
        var missing = requiredAddresses
            .Where(address => package.PatchSegments.All(segment => segment.Address != address))
            .Select(address => $"0x{address:X8}")
            .ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException("共享调度器安装记录缺少写入段：" + string.Join("、", missing));
        return package;
    }

    private static EffectPatchSegment CloneSegment(EffectPatchSegment source) => new()
    {
        TargetFile = source.TargetFile,
        AddressKind = source.AddressKind,
        Address = source.Address,
        AddressHex = source.AddressHex,
        BytesHex = source.BytesHex,
        ExpectedOldBytesHex = source.ExpectedOldBytesHex,
        CodeCaveId = source.CodeCaveId,
        HookPoint = source.HookPoint,
        AssemblySourceHash = source.AssemblySourceHash,
        AllocatedRange = source.AllocatedRange,
        EngineProfileSha256 = source.EngineProfileSha256,
        Comment = source.Comment
    };
    private static bool SameEntry(EffectDispatcherEntry left, EffectDispatcherEntry right)
        => left.Enabled == right.Enabled && left.PersonalEffectId == right.PersonalEffectId && left.ItemEffectId == right.ItemEffectId &&
           left.EffectValueMode == right.EffectValueMode && left.StackingMode == right.StackingMode && left.Action == right.Action &&
           left.ValueSource == right.ValueSource && left.Value == right.Value && left.ExecutionOrder == right.ExecutionOrder;
    private static int FindMagic(byte[] bytes, string magic)
    {
        var marker = Encoding.ASCII.GetBytes(magic);
        for (var index = 0; index <= bytes.Length - marker.Length; index++) if (bytes.AsSpan(index, marker.Length).SequenceEqual(marker)) return index;
        return -1;
    }
    private static string SaveDispatcher(CczProject project, EffectDispatcherManifestV2 manifest)
    {
        var path = DispatcherPath(project, manifest.ManifestId); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions), Encoding.UTF8); return path;
    }
    private static string SaveModular(CczProject project, ModularEffectManifestV2 manifest)
    {
        var root = ProjectPatchIdentityService.ModularManifestRoot(project); Directory.CreateDirectory(root);
        var path = Path.Combine(root, Path.GetFileNameWithoutExtension(manifest.ManifestId) + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions), Encoding.UTF8); return path;
    }
    private static string DispatcherPath(CczProject project, string id)
        => Path.Combine(ProjectPatchIdentityService.DispatcherManifestRoot(project), Path.GetFileNameWithoutExtension(id) + ".json");
    private EffectDispatcherManifestV2 ReadDispatcher(CczProject project, string id)
        => ListDispatchers(project).First(item => item.ManifestId.Equals(Path.GetFileNameWithoutExtension(id), StringComparison.OrdinalIgnoreCase));
    private static IEnumerable<T> ReadJsonFiles<T>(string root)
    {
        if (!Directory.Exists(root)) yield break;
        foreach (var path in Directory.GetFiles(root, "*.json"))
        {
            T? item = default; try { item = JsonSerializer.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8), JsonOptions); } catch { }
            if (item != null) yield return item;
        }
    }
    private static string existingSummary(EffectPackage package)
        => string.IsNullOrWhiteSpace(package.Metadata.GetValueOrDefault("ExistingDispatcherManifest"))
            ? "模块化特效已创建，共享调度器和首个注册条目已通过复读。"
            : "模块化特效已加入现有共享调度器，没有重复安装入口 Hook。";

    private static void SplitEmbeddedRegistrySegment(EffectPackage package, uint codeAddress, uint registryAddress, int registryLength)
    {
        var segment = package.PatchSegments.FirstOrDefault(item => item.Address == codeAddress && !string.IsNullOrWhiteSpace(item.CodeCaveId))
                      ?? throw new InvalidOperationException("找不到共享调度器代码洞主体段。");
        var bytes = EffectPatchByteService.ParseHex(segment.BytesHex);
        var old = EffectPatchByteService.ParseHex(segment.ExpectedOldBytesHex);
        var offset = checked((int)(registryAddress - codeAddress));
        if (offset <= 0 || offset + registryLength != bytes.Length || old.Length != bytes.Length)
            throw new InvalidOperationException("共享调度器代码与注册表边界不一致。");
        segment.BytesHex = EffectPatchByteService.ToHex(bytes[..offset]);
        segment.ExpectedOldBytesHex = EffectPatchByteService.ToHex(old[..offset]);
        package.PatchSegments.Add(new EffectPatchSegment
        {
            TargetFile = segment.TargetFile, AddressKind = segment.AddressKind, Address = registryAddress,
            AddressHex = $"0x{registryAddress:X8}", BytesHex = EffectPatchByteService.ToHex(bytes[offset..]),
            ExpectedOldBytesHex = EffectPatchByteService.ToHex(old[offset..]), CodeCaveId = segment.CodeCaveId,
            HookPoint = "共享特效注册表", AllocatedRange = segment.AllocatedRange,
            EngineProfileSha256 = segment.EngineProfileSha256, Comment = "CCZD 版本化运行时注册表。"
        });
    }

    private static string ReadExpandableRegistryOldBytes(CczProject project, EffectDispatcherManifestV2 dispatcher, int requestedLength)
    {
        if (requestedLength < dispatcher.RegistryLength)
            throw new InvalidOperationException("共享调度注册表不能通过普通更新缩小容量。");
        var current = EffectPatchByteService.ParseHex(EffectPatchByteService.ReadVirtualBytes(project, dispatcher.RegistryAddress, requestedLength));
        if (requestedLength == dispatcher.RegistryLength) return EffectPatchByteService.ToHex(current);
        var tail = current.AsSpan(dispatcher.RegistryLength);
        if (tail.ToArray().Any(value => value != 0x90))
            throw new InvalidOperationException("共享调度注册表扩容尾部不是连续 NOP，不能安全原位扩容。");
        var tailStart = checked(dispatcher.RegistryAddress + (uint)dispatcher.RegistryLength);
        var tailEnd = checked(dispatcher.RegistryAddress + (uint)requestedLength - 1);
        var conflict = new CodeCaveRegistry().LoadExistingAllocations(project)
            .FirstOrDefault(range => range.StartVirtualAddress <= tailEnd && range.EndVirtualAddress >= tailStart);
        if (conflict != null)
            throw new InvalidOperationException("共享调度注册表扩容尾部已被其他补丁占用：" + conflict.Reason);
        return EffectPatchByteService.ToHex(current);
    }

    private static void UpdateDispatcherRegistryOwnership(EffectDispatcherManifestV2 dispatcher,
        EffectTriggerDispatcherDraft draft, EffectPackage updatePackage)
    {
        var registry = dispatcher.InstallationPackage.PatchSegments.FirstOrDefault(item => item.Address == dispatcher.RegistryAddress && item.HookPoint == "共享特效注册表")
                       ?? throw new InvalidOperationException("共享调度器安装记录缺少独立注册表段。");
        var updated = new EffectTriggerDispatcherService().BuildRegistry(draft);
        if (updated.Length > dispatcher.RegistryLength)
        {
            var update = updatePackage.PatchSegments.First(item => item.Address == dispatcher.RegistryAddress);
            var currentBefore = EffectPatchByteService.ParseHex(update.ExpectedOldBytesHex);
            var original = EffectPatchByteService.ParseHex(registry.ExpectedOldBytesHex).ToList();
            original.AddRange(currentBefore.Skip(dispatcher.RegistryLength));
            registry.ExpectedOldBytesHex = EffectPatchByteService.ToHex(original.ToArray());
        }
        registry.BytesHex = EffectPatchByteService.ToHex(updated);
    }

    private static string ReadFileBytes(CczProject project, EffectPatchSegment segment, int length)
    {
        var path = project.ResolveGameFile(string.IsNullOrWhiteSpace(segment.TargetFile) ? "Ekd5.exe" : segment.TargetFile);
        var bytes = File.ReadAllBytes(path);
        var offset = checked((int)segment.Address);
        if (offset < 0 || offset + length > bytes.Length) throw new InvalidOperationException("调度器安装段越出目标文件范围。");
        return EffectPatchByteService.ToHex(bytes.AsSpan(offset, length).ToArray());
    }
}
