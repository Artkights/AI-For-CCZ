using System.Data;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Reflection;
using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.McpServer;

public sealed partial class CczMcpRuntime
{
    private static (string RuntimeVersion, string RuntimeIdentity, string McpVersion, string McpIdentity, bool IsConsistent) ReadEffectBuildConsistency()
    {
        var mcp = typeof(CczMcpRuntime).Assembly;
        var runtimeVersion = EffectCapabilityVersion.CoreVersion;
        var runtimeIdentity = EffectCapabilityVersion.BuildIdentity;
        var mcpVersion = mcp.GetName().Version?.ToString() ?? "未知";
        var mcpIdentity = mcp.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? mcpVersion;
        return (runtimeVersion, runtimeIdentity, mcpVersion, mcpIdentity,
            runtimeVersion == mcpVersion && runtimeIdentity == mcpIdentity);
    }

    private static void EnsureEffectBuildConsistency()
    {
        var build = ReadEffectBuildConsistency();
        if (!build.IsConsistent)
            throw new InvalidOperationException($"特效 Core/Runtime 与 MCP 发布版本不一致，已禁止写入。Runtime {build.RuntimeVersion}/{build.RuntimeIdentity}，MCP {build.McpVersion}/{build.McpIdentity}。");
        new EffectReleaseConsistencyService().EnsureWriteAllowed();
    }
    public object ListEffects(string? gameRoot, string domain, string? keyword, int limit)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var effectiveLimit = NormalizeLimit(limit, 100, 1000);
        var effects = _effectPackageService.ListEffects(project, tables, domain, keyword, effectiveLimit);
        return new
        {
            project.GameRoot,
            Domain = domain,
            Keyword = keyword ?? string.Empty,
            ReturnedEffects = effects.Count,
            Effects = effects,
            SafetyNote = "Read-only effect catalog. Use preview_effect_package before apply_effect_package."
        };
    }

    public object ReadEffect(string? gameRoot, string domain, int effectId)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        return new
        {
            project.GameRoot,
            Effect = _effectPackageService.ReadEffect(project, tables, domain, effectId)
        };
    }

    public object ExportEffectPackage(string? gameRoot, string domain, int effectId)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        return new
        {
            project.GameRoot,
            Package = _effectPackageService.ExportPackage(project, tables, domain, effectId),
            SafetyNote = "Exported packages can be passed to preview_effect_package/apply_effect_package."
        };
    }

    public object PreviewEffectPackage(string? gameRoot, EffectPackage package, string? mode)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        return new
        {
            project.GameRoot,
            Preview = _effectPackageService.Preview(project, tables, package, mode ?? "import")
        };
    }

    public object ApplyEffectPackage(string? gameRoot, EffectPackage package, string? mode, string? writeMode)
    {
        EnsureEffectBuildConsistency();
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var tables = LoadTables(project);
        return new
        {
            project.GameRoot,
            Result = _effectPackageService.Apply(project, tables, package, mode ?? "import"),
            SafetyNote = "Effect package writes use table/catalog services with backups, reports, and a manifest under CCZModStudio_Notes/EffectManifests."
        };
    }

    public object ListEffectTemplates()
        => new
        {
            Templates = _effectPackageService.ListTemplates(),
            SafetyNote = "Templates are declarative. Patch templates are draft-only unless explicit bytes are provided and preview_effect_patch passes."
        };

    public object BuildEffectPackageFromTemplate(string templateId, Dictionary<string, string>? parameters)
        => new
        {
            Package = _effectPackageService.BuildPackageFromTemplate(templateId, parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            SafetyNote = "Generated packages are drafts. Always preview before applying."
        };

    public object PreviewEffectPatch(string? gameRoot, EffectPackage package)
    {
        var project = LoadProject(gameRoot);
        return new
        {
            project.GameRoot,
            Preview = _effectPackageService.PreviewPatch(project, package)
        };
    }

    public object ScanExeCodeCaves(string? gameRoot, string? targetFile, int minLength, bool includeZero, bool includeMixed)
    {
        var project = LoadProject(gameRoot);
        var scanner = new ExeCodeCaveScanner();
        var result = scanner.Scan(project, string.IsNullOrWhiteSpace(targetFile) ? "Ekd5.exe" : targetFile!, minLength <= 0 ? 8 : minLength, includeZero, includeMixed);
        return new
        {
            project.GameRoot,
            Scan = result,
            SafetyNote = "Read-only scan. Candidates are not write permission; patch preview must still pass version, old-byte, allocation, and overlap checks."
        };
    }

    public object ScanInstalledEffects(string? gameRoot, string? targetFile)
    {
        var project = LoadProject(gameRoot);
        var analysis = EffectAnalysisCoordinator.Shared.Scan(project, string.IsNullOrWhiteSpace(targetFile) ? "Ekd5.exe" : targetFile!);
        var report = analysis.Inventory;
        return new
        {
            project.GameRoot,
            analysis.AnalysisFingerprint,
            analysis.CacheState,
            analysis.CompletedStages,
            analysis.Performance,
            analysis.ProfileAudit,
            Inventory = report,
            confirmedInjectedEffects = report.ConfirmedInjectedEffects,
            historicalOrIncompleteEffects = report.IncompleteOrHistoricalEffects,
            SafetyNote = "只读扫描。只有 ConfirmedInjectedEffects 代表当前已确认安装；样本相似项和历史记录不能作为安装事实。"
        };
    }

    public object ReadEffectInstance(string? gameRoot, string instanceId, string? targetFile)
    {
        var project = LoadProject(gameRoot);
        var analysis = EffectAnalysisCoordinator.Shared.Scan(project, string.IsNullOrWhiteSpace(targetFile) ? "Ekd5.exe" : targetFile!);
        var instance = analysis.Inventory.Effects.Concat(analysis.Inventory.InjectedEffects).Concat(analysis.Inventory.NativeEffects)
            .FirstOrDefault(item => item.InstanceId.Equals(instanceId, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException("未找到特技实例：" + instanceId);
        return new
        {
            project.GameRoot,
            analysis.AnalysisFingerprint,
            analysis.CacheState,
            Instance = instance
        };
    }

    public object ScanEffectIdLocations(string? gameRoot, string? targetFile, bool includeDiagnostics)
    {
        var project = LoadProject(gameRoot);
        var analysis = EffectAnalysisCoordinator.Shared.Scan(project, string.IsNullOrWhiteSpace(targetFile) ? "Ekd5.exe" : targetFile!);
        var index = analysis.LocationIndex;
        return new
        {
            project.GameRoot,
            analysis.AnalysisFingerprint,
            analysis.CacheState,
            analysis.CompletedStages,
            analysis.Performance,
            analysis.ProfileAudit,
            index.ExeSha256,
            index.SummaryZh,
            index.ReportPaths,
            Locations = includeDiagnostics ? index.Locations : index.Locations.Where(item => item.WriteCapability != EffectIdWriteCapability.DiagnosticOnly),
            SafetyNoteZh = "一个逻辑特效号可能对应多个物理位置；写入能力必须逐位置判断。"
        };
    }

    public object ReadEffectIdLocation(string? gameRoot, string locationId, string? targetFile)
    {
        var project = LoadProject(gameRoot);
        return new { project.GameRoot, Location = new EffectIdLocationIndexService().Read(project, locationId, string.IsNullOrWhiteSpace(targetFile) ? "Ekd5.exe" : targetFile!) };
    }

    public object ReadHookExecutionContract(string? gameRoot, string contractId)
    {
        var project = LoadProject(gameRoot);
        var analysis = EffectAnalysisCoordinator.Shared.Scan(project);
        var contract = analysis.HookContracts.FirstOrDefault(item => item.ContractId.Equals(contractId, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException("未找到执行契约：" + contractId);
        return new { project.GameRoot, analysis.AnalysisFingerprint, analysis.CacheState, Contract = contract };
    }

    public object CreateEffectContractProbePlan(string? gameRoot, string contractId)
    {
        var project = LoadProject(gameRoot);
        return new { project.GameRoot, Plan = new HookExecutionContractService().CreateProbePlan(project, contractId), SafetyNoteZh = "只生成动态证据采集计划，不启动游戏、不下断点、不写文件。" };
    }

    public object ImportEffectContractEvidence(string? gameRoot, EffectContractEvidence evidence)
    {
        var project = LoadProject(gameRoot);
        return new { project.GameRoot, Result = new HookExecutionContractService().ImportEvidence(project, evidence) };
    }

    public object ImportEffectEvidenceBundle(string? gameRoot, EffectEvidenceBundleV1 bundle)
    {
        var project = LoadProject(gameRoot);
        var result = bundle.ContractId.Equals(ExtendedPersonalJobBindingService.ContractId, StringComparison.OrdinalIgnoreCase)
            ? new ExtendedPersonalJobBindingService().ImportEvidenceBundle(project, bundle)
            : new HookExecutionContractService().ImportEvidenceBundle(project, bundle);
        return new
        {
            project.GameRoot,
            Result = result,
            SafetyNoteZh = "只有签名、原始文件摘要、项目身份、加载模块 SHA 和契约摘要全部一致的证据包才能提升契约。"
        };
    }

    public object ImportEffectEvidenceBundleV2(string? gameRoot, EffectEvidenceBundleV2 bundle)
    {
        var project = LoadProject(gameRoot);
        return new
        {
            project.GameRoot,
            Result = new HookExecutionContractService().ImportEvidenceBundleV2(project, bundle),
            SafetyNoteZh = "v2 证据必须同时匹配规范档案身份、完整 EXE SHA、代码身份、契约哈希和原始观测。"
        };
    }

    public object ImportEffectEvidenceBundleV3(string? gameRoot, EffectEvidenceBundleV3 bundle)
    {
        var project = LoadProject(gameRoot);
        return new
        {
            project.GameRoot,
            Result = new HookExecutionContractService().ImportEvidenceBundleV3(project, bundle),
            SafetyNoteZh = "V3 证据必须匹配原项目、签名沙箱、代码身份、边界、关系语义和探针恢复结果。"
        };
    }

    public object ReadEffectWriteMatrix(string? gameRoot)
    {
        var project = LoadProject(gameRoot);
        return new { project.GameRoot, Matrix = new EffectWriteAuthorizationService().ReadMatrix(project) };
    }

    public object PrepareEffectProfileOnboarding(string? gameRoot)
    {
        var project = LoadProject(gameRoot);
        return new { project.GameRoot, Plan = new LocalEffectProfileService().Prepare(project) };
    }

    public object RunEffectProfileOnboarding(
        string? gameRoot,
        string sandboxRoot,
        IReadOnlyList<EffectEvidenceBundleV3> evidence)
    {
        var original = LoadProject(gameRoot);
        var sandbox = new ProjectDetector().CreateProjectFromGameRoot(sandboxRoot);
        return new
        {
            original.GameRoot,
            SandboxRoot = sandbox.GameRoot,
            Result = new LocalEffectProfileService().Promote(original, sandbox, evidence)
        };
    }

    public object PreviewEffectWriteV2(string? gameRoot, EffectPackage package)
    {
        var project = LoadProject(gameRoot);
        var preview = new EffectTransactionalPatchService().Preview(project, package);
        if (preview.CanApply)
            new LockedEffectWriteReceiptService().Issue(project, package, "effect-write-v2");
        return new { project.GameRoot, Preview = preview, Package = package };
    }

    public object ApplyEffectWriteV2(string? gameRoot, EffectPackage package, string? writeMode)
    {
        EnsureEffectBuildConsistency();
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        return new
        {
            project.GameRoot,
            Result = new EffectTransactionalPatchService().Apply(project, package, "effect-write-v2")
        };
    }

    public object AuditEffectExecutableProfile(string? gameRoot)
    {
        var project = LoadProject(gameRoot);
        return new { project.GameRoot, ProfileAudit = new ExecutableProfileAuditService().Audit(project) };
    }

    public object ExplainEffectContext(string? gameRoot, string? contractId, uint? callAddress)
    {
        var project = LoadProject(gameRoot);
        if (!string.IsNullOrWhiteSpace(contractId))
        {
            var contract = new HookExecutionContractService().Read(project, contractId);
            return new
            {
                project.GameRoot,
                contract.ContractId,
                contract.ContractVersion,
                contract.ContractCodeIdentityHash,
                Slots = contract.Slots,
                PointerInference = contract.PointerInference
            };
        }
        if (!callAddress.HasValue) throw new ArgumentException("contract_id 与 call_address 至少提供一个。");
        return new
        {
            project.GameRoot,
            CallAddress = callAddress,
            PointerInference = new X86ContextDataFlowAnalyzer().Analyze(project, callAddress.Value)
        };
    }

    public object CompileSemanticEffect(string? gameRoot, SemanticEffectProgram program)
    {
        var project = LoadProject(gameRoot);
        return new { project.GameRoot, Result = new SemanticEffectCompiler().Compile(project, program), SafetyNoteZh = "未通过动态执行契约时只返回语义代码诊断，不生成可应用包。" };
    }

    public object ApplyEffectParameterAdapter(string? gameRoot, EffectPackage package, string? writeMode)
    {
        EnsureEffectBuildConsistency();
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var result = new EffectParameterAdapterService().Apply(project, package);
        EffectIdLocationIndexService.Invalidate(project);
        return new { project.GameRoot, Result = result };
    }

    public object PreviewEffectIdUpdate(string? gameRoot, EffectIdUpdateRequest request, string? targetFile)
    {
        var project = LoadProject(gameRoot);
        return new
        {
            project.GameRoot,
            Preview = new EffectIdLocationIndexService().PreviewUpdate(project, request, string.IsNullOrWhiteSpace(targetFile) ? "Ekd5.exe" : targetFile!),
            SafetyNoteZh = "只生成带当前字节锁和 EXE 身份锁的补丁包；实际写入仍需显式调用 apply_effect_patch。"
        };
    }

    public object ListEffectModules(string? gameRoot, string? kind, bool authoringOnly)
    {
        var project = LoadProject(gameRoot);
        var catalog = new EffectModuleCatalogService().Build(project);
        var modules = catalog.Modules.Where(item => string.IsNullOrWhiteSpace(kind) || item.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase));
        if (authoringOnly) modules = modules.Where(item => item.IsAvailableForAuthoring);
        return new { project.GameRoot, catalog.ExeSha256, catalog.SummaryZh, Modules = modules, catalog.Recipes, catalog.InstanceTags };
    }

    public object ReadEffectModule(string? gameRoot, string moduleId)
    {
        var project = LoadProject(gameRoot);
        return new { project.GameRoot, Module = new EffectModuleCatalogService().Read(project, moduleId) };
    }

    public object ValidateEffectBlueprint(string? gameRoot, ModularCompositeEffectBlueprint blueprint)
    {
        var project = LoadProject(gameRoot);
        return new { project.GameRoot, Validation = new ModularEffectAuthoringService().Validate(project, blueprint) };
    }

    public object PreviewModularEffect(string? gameRoot, ModularCompositeEffectBlueprint blueprint)
    {
        var project = LoadProject(gameRoot);
        return new { project.GameRoot, Preview = new ModularEffectAuthoringService().Preview(project, blueprint), SafetyNoteZh = "只有验证模块配方可生成锁定写入包。" };
    }

    public object ApplyModularEffect(string? gameRoot, EffectPackage package, string? writeMode)
    {
        EnsureEffectBuildConsistency();
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        return new { project.GameRoot, Result = new ModularEffectAuthoringService().Apply(project, package) };
    }

    public object ReadModularEffect(string? gameRoot, string manifestId)
    {
        var project = LoadProject(gameRoot);
        var service = new ModularEffectLifecycleService();
        var manifest = service.ReadModular(project, manifestId);
        return new { project.GameRoot, ModularEffect = manifest, InstallationStatus = service.ResolveModularStatus(project, manifest) };
    }

    public object PreviewUpdateModularEffect(string? gameRoot, ModularEffectMaintenanceDraft draft)
    {
        var project = LoadProject(gameRoot);
        return new { project.GameRoot, Preview = new ModularEffectLifecycleService().PreviewUpdate(project, draft) };
    }

    public object PreviewToggleModularEffect(string? gameRoot, string manifestId, bool enable)
    {
        var project = LoadProject(gameRoot);
        return new { project.GameRoot, Preview = new ModularEffectLifecycleService().PreviewToggle(project, manifestId, enable) };
    }

    public object PreviewRepairModularEffect(string? gameRoot, string manifestId)
    {
        var project = LoadProject(gameRoot);
        return new { project.GameRoot, Preview = new ModularEffectLifecycleService().PreviewRepair(project, manifestId) };
    }

    public object PreviewRemoveModularEffect(string? gameRoot, string manifestId)
    {
        var project = LoadProject(gameRoot);
        return new { project.GameRoot, Preview = new ModularEffectLifecycleService().PreviewRemove(project, manifestId) };
    }

    public object ApplyModularMaintenance(string? gameRoot, EffectPackage package, string? writeMode)
    {
        EnsureEffectBuildConsistency();
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        return new { project.GameRoot, Result = new ModularEffectLifecycleService().ApplyMaintenance(project, package) };
    }

    public object ExplainExeAddress(string? gameRoot, uint address, string? targetFile)
    {
        var project = LoadProject(gameRoot);
        return new
        {
            project.GameRoot,
            Address = new ExeAddressSemanticService().Explain(project, address, string.IsNullOrWhiteSpace(targetFile) ? "Ekd5.exe" : targetFile!)
        };
    }

    public object ReadEffectGenerationContext(
        string? gameRoot,
        string? keyword,
        string? phase,
        string? semanticKind,
        string? hookContractId,
        int characterBudget)
    {
        var project = LoadProject(gameRoot);
        var context = new ExeAddressSemanticService().BuildGenerationContext(
            project,
            keyword,
            phase,
            semanticKind,
            hookContractId,
            characterBudget <= 0 ? 12000 : characterBudget);
        return new
        {
            project.GameRoot,
            Context = context,
            CharacterCount = context.Length,
            HookContracts = new HookContractService().BuildContracts(project),
            SafetyNote = "供本地 agent 生成草案使用；不得直接写 EXE，必须调用 preview_special_skill_patch 或 preview_assembly_patch。"
        };
    }

    public object PreviewEffectParameterUpdate(string? gameRoot, EffectParameterUpdateRequest request)
    {
        var project = LoadProject(gameRoot);
        return new
        {
            project.GameRoot,
            Preview = new EffectInventoryService().PreviewParameterUpdate(project, request),
            SafetyNote = "仅生成带当前字节锁的 patch 包；写入时把预览返回的 Package 交给 apply_effect_patch。"
        };
    }

    public object ReadEngineEffectMechanism(string? gameRoot, string? targetFile)
    {
        var project = LoadProject(gameRoot);
        var analysis = EffectAnalysisCoordinator.Shared.Scan(project);
        return new
        {
            project.GameRoot,
            analysis.AnalysisFingerprint,
            analysis.CacheState,
            Profile = analysis.MechanismProfile,
            SafetyNoteZh = "只读机制档案。外部资料只提供分析方法，当前 EXE 复读结果决定是否可写。"
        };
    }

    public object SearchCompatibleEffectMembers(string? gameRoot, string? keyword, string? compatibilityKind, int limit)
    {
        var project = LoadProject(gameRoot);
        var inventory = new EffectInventoryService().Scan(project);
        var rows = new CompositeEffectMemberCompatibilityService().Search(project, keyword)
            .Where(item => string.IsNullOrWhiteSpace(compatibilityKind) || item.CompatibilityKind.Equals(compatibilityKind, StringComparison.OrdinalIgnoreCase))
            .Take(Math.Clamp(limit, 1, 500)).ToList();
        return new { project.GameRoot, inventory.ExeSha256, count = rows.Count, members = rows };
    }

    public object ReadWrapperContract(string? gameRoot, string contractId)
    {
        var project = LoadProject(gameRoot);
        return new EngineEffectMechanismService().Build(project).WrapperContracts.FirstOrDefault(item =>
                   item.ContractId.Equals(contractId, StringComparison.OrdinalIgnoreCase))
               ?? throw new KeyNotFoundException("未找到包装函数契约：" + contractId);
    }

    public object ReadComplexEffectFamilyContract(string? gameRoot, string contractId)
    {
        var project = LoadProject(gameRoot);
        return new EngineEffectMechanismService().Build(project).ComplexFamilyContracts.FirstOrDefault(item =>
                   item.ContractId.Equals(contractId, StringComparison.OrdinalIgnoreCase))
               ?? throw new KeyNotFoundException("未找到复杂补丁家族契约：" + contractId);
    }

    public object ReadNativeEffectDefinition(string? gameRoot, string channel, int effectId)
    {
        var project = LoadProject(gameRoot);
        return new
        {
            project.GameRoot,
            Definition = new NativeEffectConfigurationService().Read(project, channel, effectId),
            SafetyNoteZh = "定义配置与原生桩参数分层返回；读取不会修改文件。"
        };
    }

    public object PreviewNativeEffectUpdate(string? gameRoot, NativeEffectEditDraft draft)
    {
        var project = LoadProject(gameRoot);
        return new
        {
            project.GameRoot,
            Preview = new NativeEffectConfigurationService().Preview(project, draft),
            SafetyNoteZh = "只生成带当前字节锁的多文件事务包，不写文件。"
        };
    }

    public object CreateExtendedBindingProbePlan(string? gameRoot, int effectId)
    {
        var project = LoadProject(gameRoot);
        var service = new ExtendedPersonalJobBindingService();
        return new
        {
            project.GameRoot,
            Capability = service.ReadCapability(project),
            Plan = service.CreateProbePlan(project, effectId),
            SafetyNoteZh = "只生成当前 SHA 的读断点与采集计划，不启动游戏、不修改 EXE。"
        };
    }

    public object ImportExtendedBindingEvidence(string? gameRoot, ExtendedBindingEvidence evidence)
    {
        var project = LoadProject(gameRoot);
        return new
        {
            project.GameRoot,
            Result = new ExtendedPersonalJobBindingService().ImportEvidence(project, evidence)
        };
    }

    public object ApplyNativeEffectUpdate(string? gameRoot, EffectPackage package, string? writeMode)
    {
        EnsureEffectBuildConsistency();
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var result = new NativeEffectConfigurationService().Apply(project, package);
        EffectInventoryService.Invalidate(project);
        return new { project.GameRoot, Result = result, SafetyNoteZh = "写入前已统一备份，写后已逐段复读。" };
    }

    public object FindFreeEffectIds(string? gameRoot, string channel)
    {
        var project = LoadProject(gameRoot);
        return new { project.GameRoot, Report = new CompositeEffectService().FindFreeIds(project, channel) };
    }

    public object DraftCompositeEffect(string? gameRoot, string channel, int? effectId, string name, string description, IReadOnlyList<string> memberInstanceIds)
    {
        var project = LoadProject(gameRoot);
        return new
        {
            project.GameRoot,
            Draft = new CompositeEffectService().Draft(project, channel, effectId, name, description, memberInstanceIds),
            SafetyNoteZh = "只生成结构化复合草案，不分配代码洞、不写文件。"
        };
    }

    public object PreviewCompositeEffect(string? gameRoot, CompositeEffectDraft draft)
    {
        var project = LoadProject(gameRoot);
        return new
        {
            project.GameRoot,
            Preview = new CompositeEffectService().Preview(project, draft),
            SafetyNoteZh = "只有直接核心调用、四参数和当前字节均验证通过的成员才能进入预览包。"
        };
    }

    public object PreflightCompositeEffect(string? gameRoot, CompositeEffectDraft draft)
    {
        var project = LoadProject(gameRoot);
        var preview = new CompositeEffectService().Preview(project, draft);
        return new { project.GameRoot, preview.Preflight, preview.Receipt, Preview = preview };
    }

    public object ReadEffectCapabilities(string? gameRoot)
    {
        var project = LoadProject(gameRoot);
        var identity = new ProjectPatchIdentityService().Build(project);
        var writable = new EffectWritableProfileService().Evaluate(project);
        var extendedBindings = new ExtendedPersonalJobBindingService().ReadCapability(project);
        var build = ReadEffectBuildConsistency();
        var release = new EffectReleaseConsistencyService().Read();
        return new
        {
            project.GameRoot,
            CoreVersion = build.RuntimeVersion,
            RuntimeBuildIdentity = build.RuntimeIdentity,
            McpVersion = build.McpVersion,
            McpBuildIdentity = build.McpIdentity,
            build.IsConsistent,
            ReleaseConsistency = release,
            CanWriteWithCurrentComponents = build.IsConsistent && release.CanWrite,
            WriteConsistencyReasonZh = !build.IsConsistent
                ? "Runtime 与 MCP 版本不一致，特效写入已禁用。"
                : release.ReasonZh,
            SchemaVersion = EffectCapabilityVersion.SchemaVersion,
            EffectCapabilityVersion.BuildChannel,
            EffectCapabilityVersion.BuildIdentity,
            ProjectIdentity = identity,
            writable.CanWrite,
            writable.ReasonZh,
            ExtendedPersonalJobBindings = extendedBindings,
            Endpoints = new[]
            {
                "scan_installed_effects", "scan_effect_id_locations", "read_effect_id_location",
                "read_native_effect_definition", "preview_native_effect_update", "apply_native_effect_update",
                "create_extended_binding_probe_plan", "import_effect_evidence_bundle",
                "audit_effect_executable_profile", "explain_effect_context", "import_effect_evidence_bundle_v2",
                "import_effect_evidence_bundle_v3", "read_effect_write_matrix",
                "prepare_effect_profile_onboarding", "run_effect_profile_onboarding",
                "preview_effect_write_v2", "apply_effect_write_v2",
                "read_hook_execution_contract", "create_effect_contract_probe_plan",
                "preflight_composite_effect", "apply_composite_effect",
                "read_modular_effect", "preview_update_modular_effect", "apply_update_modular_effect",
                "preview_toggle_modular_effect", "apply_toggle_modular_effect",
                "preview_repair_modular_effect", "apply_repair_modular_effect",
                "preview_remove_modular_effect", "apply_remove_modular_effect"
            }
        };
    }

    public object ApplyCompositeEffect(string? gameRoot, EffectPackage package, string? writeMode)
    {
        EnsureEffectBuildConsistency();
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var result = new CompositeEffectService().Apply(project, package);
        EffectInventoryService.Invalidate(project);
        return new { project.GameRoot, Result = result, SafetyNoteZh = "复合参数块、所有成员适配器和调用重定向已作为一个事务写入。" };
    }

    public object ReadCompositeEffect(string? gameRoot, string manifestId)
    {
        var project = LoadProject(gameRoot);
        return new { project.GameRoot, CompositeEffect = new CompositeEffectService().Read(project, manifestId) };
    }

    public object PreviewUpdateCompositeEffect(string? gameRoot, CompositeEffectMaintenanceDraft draft)
    {
        var project = LoadProject(gameRoot);
        return new { project.GameRoot, Preview = new CompositeEffectService().PreviewUpdate(project, draft) };
    }

    public object ApplyUpdateCompositeEffect(string? gameRoot, EffectPackage package, string? writeMode)
    {
        EnsureEffectBuildConsistency();
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        return new { project.GameRoot, Result = new CompositeEffectService().ApplyMaintenance(project, package) };
    }

    public object PreviewToggleCompositeEffect(string? gameRoot, string manifestId, bool enable)
    {
        var project = LoadProject(gameRoot);
        return new { project.GameRoot, Preview = new CompositeEffectService().PreviewToggle(project, manifestId, enable) };
    }

    public object ApplyToggleCompositeEffect(string? gameRoot, EffectPackage package, string? writeMode)
        => ApplyUpdateCompositeEffect(gameRoot, package, writeMode);

    public object PreviewRepairCompositeEffect(string? gameRoot, string manifestId)
    {
        var project = LoadProject(gameRoot);
        return new { project.GameRoot, Preview = new CompositeEffectService().PreviewRepair(project, manifestId) };
    }

    public object ApplyRepairCompositeEffect(string? gameRoot, EffectPackage package, string? writeMode)
        => ApplyUpdateCompositeEffect(gameRoot, package, writeMode);

    public object PreviewRemoveCompositeEffect(string? gameRoot, string manifestId)
    {
        var project = LoadProject(gameRoot);
        return new
        {
            project.GameRoot,
            Package = new CompositeEffectService().PreviewRemove(project, manifestId),
            SafetyNoteZh = "只有当前安装字节与 manifest 完全一致时才生成恢复包。"
        };
    }

    public object RemoveCompositeEffect(string? gameRoot, EffectPackage package, string? writeMode)
    {
        EnsureEffectBuildConsistency();
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var result = new CompositeEffectService().Remove(project, package);
        EffectInventoryService.Invalidate(project);
        return new { project.GameRoot, Result = result, SafetyNoteZh = "已按安装前旧字节整体恢复并复读。" };
    }

    public object DraftAssemblyPatch(string? gameRoot, string prompt, string? engineVersion, int? effectId, string? hookHint)
    {
        var project = LoadProject(gameRoot);
        var compiler = new AssemblyPatchCompiler();
        var draft = compiler.Draft(project, prompt, engineVersion, effectId, hookHint);
        return new
        {
            project.GameRoot,
            Draft = draft,
            SafetyNote = "Draft only. It does not compile bytes or write files; provide hook address, expected old bytes, and assembly source to preview_assembly_patch."
        };
    }

    public object PreviewAssemblyPatch(string? gameRoot, AssemblyPatchDraft draft, string? allocatorPolicy)
    {
        var project = LoadProject(gameRoot);
        var compiler = new AssemblyPatchCompiler();
        return new
        {
            project.GameRoot,
            Preview = compiler.Preview(project, draft, allocatorPolicy ?? "smallest-fit"),
            SafetyNote = "Preview compiles bytes and builds a patch-domain EffectPackage, but does not write files."
        };
    }

    public object ApplyAssemblyPatch(string? gameRoot, EffectPackage compiledPackage, string? writeMode)
    {
        EnsureEffectBuildConsistency();
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var compiler = new AssemblyPatchCompiler();
        return new
        {
            project.GameRoot,
            Result = compiler.Apply(project, compiledPackage),
            SafetyNote = "Assembly patch writes use EffectPackage/PatchApplyService backups, reports, old-byte locks, and manifests."
        };
    }

    public object DraftSpecialSkillPatch(
        string? gameRoot,
        string prompt,
        int? effectId,
        string? hookHint,
        int? personalEffectId,
        int? itemEffectId,
        string? mode)
    {
        var project = LoadProject(gameRoot);
        var service = new SpecialSkillInjectionService();
        return new
        {
            project.GameRoot,
            Draft = service.Draft(project, prompt, effectId, hookHint, personalEffectId, itemEffectId, mode),
            SafetyNote = "Draft only. It records the four logical modules but does not compile bytes or write files."
        };
    }

    public object PreviewSpecialSkillPatch(string? gameRoot, InlineSpecialSkillPatchDraft draft, string? allocatorPolicy)
    {
        var project = LoadProject(gameRoot);
        var service = new SpecialSkillInjectionService();
        return new
        {
            project.GameRoot,
            Preview = service.Preview(project, draft, allocatorPolicy ?? "smallest-fit"),
            SafetyNote = "Preview compiles the controlled inline-special-skill scaffold, emits non-overlapping patch segments, and does not write files."
        };
    }

    public object ApplySpecialSkillPatch(string? gameRoot, EffectPackage compiledPackage, string? writeMode)
    {
        EnsureEffectBuildConsistency();
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var service = new SpecialSkillInjectionService();
        return new
        {
            project.GameRoot,
            Result = service.Apply(project, compiledPackage),
            SafetyNote = "Special-skill apply re-previews the package, checks SHA/old-byte locks, and writes through EffectPackage/PatchApplyService."
        };
    }

    public object RebindSpecialSkillParams(
        string? gameRoot,
        string manifestIdOrPackageId,
        int? personalEffectId,
        int? itemEffectId)
    {
        var project = LoadProject(gameRoot);
        var service = new SpecialSkillInjectionService();
        return new
        {
            project.GameRoot,
            Preview = service.RebindParameters(project, manifestIdOrPackageId, personalEffectId, itemEffectId),
            SafetyNote = "Parameter rebind preview only writes the recorded personal/item immediate bytes; call apply_effect_patch on the returned package to write."
        };
    }

    public object ApplyEffectPatch(string? gameRoot, EffectPackage package, string? writeMode)
    {
        EnsureEffectBuildConsistency();
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        return new
        {
            project.GameRoot,
            Result = _effectPackageService.ApplyPatch(project, package),
            SafetyNote = "Patch writes use PatchApplyService. A manifest records the generated backup files for manual recovery."
        };
    }

    public object ReadEffectResource(string? gameRoot, string uri)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        uri = (uri ?? string.Empty).Trim();
        if (uri.Equals("ccz://effects/schema", StringComparison.OrdinalIgnoreCase))
        {
            return _effectPackageService.GetSchemaResource();
        }

        if (uri.StartsWith("ccz://effects/catalog/", StringComparison.OrdinalIgnoreCase))
        {
            var domain = uri["ccz://effects/catalog/".Length..];
            return new
            {
                Uri = uri,
                Effects = _effectPackageService.ListEffects(project, tables, domain, null, 1000)
            };
        }

        if (uri.Equals("ccz://effects/templates", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                Uri = uri,
                Templates = _effectPackageService.ListTemplates()
            };
        }

        if (uri.Equals("ccz://effects/manifests", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                Uri = uri,
                Effects = _effectPackageService.ListEffects(project, tables, "patch", null, 1000)
            };
        }

        if (uri.Equals("ccz://knowledge/effects", StringComparison.OrdinalIgnoreCase))
        {
            return SearchKnowledgeEntries("特效", 80, 1);
        }

        if (uri.Equals("ccz://knowledge/effects/agent-special-effect", StringComparison.OrdinalIgnoreCase))
        {
            var catalog = new EffectKnowledgeFusionService().Build(project, "Ekd5.exe", writeReports: true);
            return new
            {
                Uri = uri,
                project.GameRoot,
                CatalogSummary = catalog.Summary,
                catalog.TargetFilePath,
                catalog.ExeSha256,
                catalog.ImageBaseHex,
                Knowledge = catalog.AgentKnowledge,
                Reports = catalog.ReportPaths,
                SafetyNote = "Local-agent knowledge pack only. Generate InlineSpecialSkillPatchDraft or AssemblyPatchDraft externally, then call preview before apply."
            };
        }

        if (uri.StartsWith("ccz://effects/installed/", StringComparison.OrdinalIgnoreCase))
        {
            var instanceId = uri["ccz://effects/installed/".Length..];
            return ReadEffectInstance(gameRoot, instanceId, "Ekd5.exe");
        }

        if (uri.StartsWith("ccz://knowledge/effects/address/", StringComparison.OrdinalIgnoreCase))
        {
            var text = uri["ccz://knowledge/effects/address/".Length..].Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            if (!uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var address))
            {
                throw new InvalidOperationException("Address resource requires a hexadecimal VA.");
            }

            return ExplainExeAddress(gameRoot, address, "Ekd5.exe");
        }

        throw new InvalidOperationException("Unsupported effect resource URI: " + uri);
    }

    public object ReadEffectPrompt(string name)
    {
        name = (name ?? string.Empty).Trim();
        var prompts = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["make_ccz_effect"] = new
            {
                Name = "make_ccz_effect",
                Description = "把自然语言特效需求转为 EffectPackage 草案。",
                Instructions = "本地 agent 负责生成结构化草案，CCZModStudio/MCP 负责预览、校验和写入。先调用 read_effect_resource ccz://knowledge/effects/agent-special-effect、list_effects、list_effect_templates；优先生成配置级包。需要 EXE 新逻辑时，简单四参数特技走 InlineSpecialSkillPatchDraft -> preview_special_skill_patch -> apply_special_skill_patch；自定义汇编走 AssemblyPatchDraft -> preview_assembly_patch -> apply_assembly_patch。草案不得直接写 EXE，apply 必须使用 preview 返回的 compiled package。"
            },
            ["import_ccz_effect"] = new
            {
                Name = "import_ccz_effect",
                Description = "导入外部 EffectPackage JSON 或补丁段。",
                Instructions = "校验 domain/effect_id/bindings/patch_segments；patch 段必须优先带 expectedOldBytesHex；调用 preview_effect_package/preview_effect_patch；只有预览通过后才能 apply_effect_package/apply_effect_patch。assembly patch package 必须来自 preview_assembly_patch。"
            },
            ["delete_ccz_effect"] = new
            {
                Name = "delete_ccz_effect",
                Description = "删除或禁用特效并处理引用。",
                Instructions = "固定行表不缩表：宝物清特效号和值，兵种清武将=1024/兵种=255/值=0，个人/套装行清 0；补丁删除不提供自动入口，按 manifest 里的备份文件手动处理。"
            }
        };

        if (prompts.TryGetValue(name, out var prompt)) return prompt;
        return new
        {
            Prompts = prompts.Values
        };
    }
}
