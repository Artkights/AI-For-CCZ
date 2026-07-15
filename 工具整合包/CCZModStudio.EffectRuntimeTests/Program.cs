using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CCZModStudio.Core;
using CCZModStudio.Models;

if (args.Length != 2)
    throw new ArgumentException("用法：CCZModStudio.EffectRuntimeTests <6.5基底目录> <HexTable.xml>");

var sourceRoot = Path.GetFullPath(args[0]);
var tablePath = Path.GetFullPath(args[1]);
var source = Project(sourceRoot, tablePath);
var sourceExe = source.ResolveGameFile("Ekd5.exe");
var baselineSha = Sha256(sourceExe);
Assert(baselineSha.Equals(EffectWritableProfileStatus.Current65BaselineSha256, StringComparison.OrdinalIgnoreCase),
    "测试输入不是唯一允许写入的 F9A35758... 基底。");

RunDispatcherRegistryTests();
RunReceiptTests(source, tablePath);
RunEvidenceAndContractTests(source);
RunReleaseConsistencyTests();
RunManifestOwnershipTests(source, tablePath);
RunDispatcherPackageOwnershipTest();
RunMaintenanceDefinitionPreviewTest(source, tablePath, baselineSha);
RunTransactionalReadWriteCountTests(source, tablePath);

var finalSha = Sha256(sourceExe);
Assert(finalSha.Equals(baselineSha, StringComparison.OrdinalIgnoreCase), "Runtime-only 测试修改了真实基底。");
Console.WriteLine("EFFECT_RUNTIME_TESTS_OK registry=pass receipts=pass evidence=pass ownership=pass maintenance=pass baseline_readonly=pass");

static void RunTransactionalReadWriteCountTests(CczProject source, string tablePath)
{
    WithTempProject(source, tablePath, copyDataFiles: true, project =>
    {
        var dataPath = project.ResolveGameFile("Data.e5");
        var before = File.ReadAllBytes(dataPath);
        Assert(before.Length >= 4, "事务读写计数烟测 Data.e5 太短。");
        var package = new EffectPackage
        {
            PackageId = "transaction-read-write-count",
            Domain = "patch",
            Name = "事务单文件多段计数",
            Metadata =
            {
                ["EngineProfileSha256"] = new ProjectPatchIdentityService().Build(project).CurrentSha256,
                ["ProfileAuditHash"] = new ExecutableProfileAuditService().Audit(project).AuditSummaryHash
            },
            PatchSegments =
            [
                Segment(0, before[0], (byte)(before[0] ^ 0x01)),
                Segment(2, before[2], (byte)(before[2] ^ 0x01))
            ]
        };
        var preview = new EffectTransactionalPatchService().Preview(project, package);
        Assert(preview.CanApply, "同文件多段事务预览失败：" + preview.Summary);
        new LockedEffectWriteReceiptService().Issue(project, package, "transaction-count");
        PerformanceMetrics.Reset();
        var service = new EffectTransactionalPatchService();
        var applied = service.Apply(project, package, "transaction-count");
        var counters = PerformanceMetrics.GetSnapshot().Counters;
        Assert(counters.GetValueOrDefault("EffectTransaction.ApplyFullReadCount") == 2,
            "Apply 应只读取 Data.e5 一次，并额外读取 EXE 一次做强制身份审计。");
        Assert(counters.GetValueOrDefault("EffectTransaction.ApplyWriteCount") == 1,
            "同一个目标文件的多个补丁段没有合并为一次写回。");
        Assert(counters.GetValueOrDefault("EffectTransaction.VerifyFullReadCount") == 1,
            "同一个目标文件写后没有只复读一次。");
        service.Restore(project, applied);
        Assert(File.ReadAllBytes(dataPath).SequenceEqual(before), "事务恢复后 Data.e5 与原始内容不一致。");

        static EffectPatchSegment Segment(uint offset, byte oldValue, byte newValue) => new()
        {
            TargetFile = "Data.e5", AddressKind = "FileOffset", Address = offset,
            ExpectedOldBytesHex = oldValue.ToString("X2"), BytesHex = newValue.ToString("X2"),
            HookPoint = "transaction-count"
        };
    });
}

static void RunDispatcherRegistryTests()
{
    var service = new EffectTriggerDispatcherService();
    var draft = new EffectTriggerDispatcherDraft
    {
        DispatcherId = "dispatcher-test",
        HookContractId = "strategy-damage-formula-v2",
        Capacity = 16,
        Entries =
        [
            Entry("first", 0xD3, SemanticEffectAction.AddDamageFixed, 17, 10),
            Entry("second", 0xD9, SemanticEffectAction.SubtractDamagePercent, 25, 20)
        ]
    };
    var bytes = service.BuildRegistry(draft);
    Assert(bytes.Length == 16 + 16 * EffectTriggerDispatcherService.EntrySize, "CCZD 16 条容量长度错误。");
    Assert(Encoding.ASCII.GetString(bytes, 0, 4) == EffectTriggerDispatcherService.Magic, "CCZD magic 缺失。");
    var parsed = service.ReadRegistry(bytes, draft.DispatcherId, draft.HookContractId);
    Assert(parsed.Capacity == 16 && parsed.Entries.Count == 2, "CCZD 往返没有恢复容量和条目数。");
    Assert(parsed.Entries[0].PersonalEffectId == 0xD3 && parsed.Entries[0].Value == 17 &&
           parsed.Entries[1].PersonalEffectId == 0xD9 && parsed.Entries[1].Value == 25,
        "CCZD 往返没有恢复条目参数。");
    Assert(service.NextCapacity(16, 17) == 32 && service.NextCapacity(32, 33) == 64, "CCZD 扩容阶梯错误。");
    ExpectFailure(() => service.NextCapacity(64, 65), "最多登记 64");

    draft.Entries[1].ExecutionOrder = draft.Entries[0].ExecutionOrder;
    ExpectFailure(() => service.BuildRegistry(draft), "执行顺序不能重复");
}

static void RunReceiptTests(CczProject source, string tablePath)
{
    var service = new LockedEffectWriteReceiptService();
    var package = ReceiptPackage(source, "receipt-tamper");
    service.Issue(source, package, "receipt-test");
    var originalName = package.Name;
    package.Name = "已篡改";
    ExpectFailure(() => service.ValidateAndConsume(source, package, "receipt-test"), "已被修改");
    package.Name = originalName;
    ExpectFailure(() => service.ValidateAndConsume(source, package, "receipt-test"), "已使用");

    var oneShot = ReceiptPackage(source, "receipt-one-shot");
    service.Issue(source, oneShot, "receipt-test");
    service.ValidateAndConsume(source, oneShot, "receipt-test");
    ExpectFailure(() => service.ValidateAndConsume(source, oneShot, "receipt-test"), "已使用");

    var expired = ReceiptPackage(source, "receipt-expired");
    service.Issue(source, expired, "receipt-test", TimeSpan.FromMilliseconds(-1));
    ExpectFailure(() => service.ValidateAndConsume(source, expired, "receipt-test"), "不存在");

    WithTempProject(source, tablePath, copyDataFiles: false, foreign =>
    {
        var cross = ReceiptPackage(source, "receipt-cross-project");
        service.Issue(source, cross, "receipt-test");
        ExpectFailure(() => service.ValidateAndConsume(foreign, cross, "receipt-test"), "项目身份");
        service.ValidateAndConsume(source, cross, "receipt-test");
    });
}

static void RunEvidenceAndContractTests(CczProject source)
{
    var contracts = new HookExecutionContractService().BuildContracts(source);
    var strategy = contracts.Single(item => item.ContractId == "strategy-damage-formula-v2");
    Assert(!string.IsNullOrWhiteSpace(strategy.ContractHash), "执行契约没有先生成稳定 hash。");
    Assert(strategy.VerificationStatus != HookContractVerificationStatus.DynamicVerified && !strategy.AllowSemanticPreview,
        "缺少可信动态证据时策略伤害配方被错误开放。");
    var repeated = new HookExecutionContractService().Read(source, strategy.ContractId);
    Assert(repeated.ContractHash == strategy.ContractHash, "静态执行契约 hash 不稳定。");

    var extended = new ExtendedPersonalJobBindingService().ReadCapability(source);
    Assert(!extended.HasRequiredDynamicEvidence && !extended.QueryCompilerAvailable && !extended.CanInstallRuntimeQuery,
        "扩展绑定在 ABI 未闭环时被错误开放。");

    var identity = new ProjectPatchIdentityService().Build(source);
    var bundle = new EffectEvidenceBundleV1
    {
        BundleId = "unsigned-forgery",
        ContractId = strategy.ContractId,
        ContractHash = strategy.ContractHash,
        ProjectId = identity.ProjectId,
        GameRoot = identity.GameRoot,
        SessionRoot = Path.Combine(source.GameRoot, "CCZModStudio_Notes", "EffectContractEvidence", identity.CurrentSha256, strategy.ContractId, "forged"),
        ProcessId = 1,
        ProcessPath = source.ResolveGameFile("Ekd5.exe"),
        LoadedModulePath = source.ResolveGameFile("Ekd5.exe"),
        LoadedModuleSize = identity.TargetFileSize,
        LoadedModuleSha256 = identity.CurrentSha256,
        DebuggerVersion = "forged",
        ToolBuildId = "forged",
        CreatedAtUtc = DateTime.UtcNow,
        CompletedScenarioIds = ["strategy-normal"]
    };
    var import = EffectEvidenceBundleService.VerifyAndStore(source, bundle);
    Assert(!import.Accepted && !import.SignatureVerified && !import.ContractPromoted,
        "无 GameDebug 签名的证据包被错误接受。");
}

static void RunReleaseConsistencyTests()
{
    var root = Path.Combine(Path.GetTempPath(), "CCZModStudio_EffectReleaseTests_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var previous = Environment.GetEnvironmentVariable("CCZ_EFFECT_RELEASE_MANIFEST");
    try
    {
        var source = typeof(EffectCapabilityVersion).Assembly.Location;
        var components = new List<EffectReleaseComponent>();
        foreach (var (id, name, relative) in new[]
                 {
                     ("desktop-core", "桌面/Core", "desktop-core.dll"),
                     ("runtime", "Runtime", "runtime.dll"),
                     ("mcp", "MCP", "mcp.dll"),
                     ("gamedebug", "GameDebug", "gamedebug.dll")
                 })
        {
            var path = Path.Combine(root, relative);
            File.Copy(source, path);
            var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
            components.Add(new EffectReleaseComponent
            {
                ComponentId = id, DisplayNameZh = name, RelativePath = relative,
                Length = new FileInfo(path).Length, Sha256 = Sha256(path),
                FileVersion = version.FileVersion ?? string.Empty,
                BuildIdentity = version.ProductVersion ?? string.Empty
            });
        }
        var manifest = new EffectReleaseManifest
        {
            EffectCapabilitySchemaVersion = EffectCapabilityVersion.SchemaVersion,
            BuildChannel = EffectCapabilityVersion.BuildChannel,
            BuildIdentity = EffectCapabilityVersion.BuildIdentity,
            CreatedAtUtc = DateTime.UtcNow,
            Components = components
        };
        var manifestPath = Path.Combine(root, EffectReleaseConsistencyService.ManifestFileName);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest), Encoding.UTF8);
        Environment.SetEnvironmentVariable("CCZ_EFFECT_RELEASE_MANIFEST", manifestPath);
        var valid = new EffectReleaseConsistencyService().Read();
        Assert(valid.HasReleaseManifest && valid.IsConsistent && valid.CanWrite,
            "同批发布组件没有通过 release manifest 完整性校验：" + valid.ReasonZh);

        File.AppendAllText(Path.Combine(root, "mcp.dll"), "tamper", Encoding.ASCII);
        var tampered = new EffectReleaseConsistencyService().Read();
        Assert(!tampered.IsConsistent && !tampered.CanWrite &&
               tampered.WarningsZh.Any(item => item.Contains("MCP", StringComparison.OrdinalIgnoreCase)),
            "发布组件摘要变化后没有阻断特效写入。");
        ExpectFailure(() => new EffectReleaseConsistencyService().EnsureWriteAllowed(), "组件发布不一致");
    }
    finally
    {
        Environment.SetEnvironmentVariable("CCZ_EFFECT_RELEASE_MANIFEST", previous);
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}

static void RunManifestOwnershipTests(CczProject source, string tablePath)
{
    WithTempProject(source, tablePath, copyDataFiles: false, project =>
    {
        var cave = new ExeCodeCaveScanner().Scan(project, minimumLength: 128).Candidates
            .First(item => item.FillKind == "nop" && item.Length >= 128);
        var segment = new EffectPatchSegment
        {
            TargetFile = "Ekd5.exe",
            AddressKind = "OdVirtualAddress",
            Address = cave.StartVirtualAddress,
            AddressHex = $"0x{cave.StartVirtualAddress:X8}",
            BytesHex = new string('9', 256),
            ExpectedOldBytesHex = new string('9', 256),
            CodeCaveId = cave.CaveId,
            HookPoint = "共享调度器代码体"
        };
        var identity = new ProjectPatchIdentityService().Build(project);
        var generic = new EffectManifest
        {
            ManifestId = "generic-modular-audit",
            ProjectRoot = project.GameRoot,
            ProjectIdentity = identity,
            Package = new EffectPackage
            {
                PackageId = "generic-modular-audit",
                Metadata = { ["LogicalPatchKind"] = "modular-semantic-effect-v2" },
                PatchSegments = [CloneSegment(segment)]
            }
        };
        var dispatcher = new EffectDispatcherManifestV2
        {
            ManifestId = "dispatcher-owner",
            DispatcherId = "dispatcher-owner",
            ProjectIdentity = identity,
            ContractId = "strategy-damage-formula-v2",
            CodeAddress = cave.StartVirtualAddress,
            RegistryAddress = cave.StartVirtualAddress + 64,
            InstallationPackage = new EffectPackage { PackageId = "dispatcher-owner", PatchSegments = [CloneSegment(segment)] }
        };
        WriteManifest(ProjectPatchIdentityService.EffectManifestRoot(project), generic.ManifestId, generic);
        var dispatcherPath = WriteManifest(ProjectPatchIdentityService.DispatcherManifestRoot(project), dispatcher.ManifestId, dispatcher);
        var registry = new CodeCaveRegistry();
        Assert(registry.LoadExistingAllocations(project).Count(item => item.CaveId == cave.CaveId) == 1,
            "通用事务清单与调度器清单重复占用同一代码洞。");
        File.Delete(dispatcherPath);
        Assert(registry.LoadExistingAllocations(project).All(item => item.CaveId != cave.CaveId),
            "删除调度器清单后，普通审计清单仍错误占用代码洞。");
    });
}

static void RunDispatcherPackageOwnershipTest()
{
    var draft = new EffectTriggerDispatcherDraft
    {
        HookAddress = 0x00401000,
        CodeAddress = 0x00402000,
        RegistryAddress = 0x00402100
    };
    var source = new EffectPackage
    {
        Name = "首个特效名称",
        Description = "首个特效说明",
        Bindings = [new EffectPackageBinding { Kind = "job_assignment", PersonId = 1 }],
        PatchSegments =
        [
            Segment(draft.HookAddress, "入口 Hook", false),
            Segment(draft.CodeAddress, "共享调度器代码体", true),
            Segment(draft.RegistryAddress, "共享特效注册表", true),
            new EffectPatchSegment { TargetFile = "Ekd5.exe", AddressKind = "FileOffset", Address = 888832, BytesHex = "00", ExpectedOldBytesHex = "00", HookPoint = "首个特效绑定" }
        ],
        Metadata = { [LockedEffectWriteReceiptService.MetadataKey] = "untrusted-test-value" }
    };
    var method = typeof(ModularEffectLifecycleService).GetMethod("BuildDispatcherInstallationPackage", BindingFlags.NonPublic | BindingFlags.Static)
                 ?? throw new InvalidOperationException("找不到调度器安装包所有权构造器。");
    var owned = (EffectPackage)(method.Invoke(null, [source, draft])
                ?? throw new InvalidOperationException("调度器安装包构造器没有返回结果。"));
    Assert(owned.PatchSegments.Count == 3 && owned.PatchSegments.All(item => item.AddressKind == "OdVirtualAddress"),
        "调度器安装包仍包含名称或绑定等首个特效专属写入段。");
    Assert(owned.Name.Length == 0 && owned.Description.Length == 0 && owned.Bindings.Count == 0,
        "调度器安装包仍拥有首个特效的定义元数据。");
    Assert(!owned.Metadata.ContainsKey(LockedEffectWriteReceiptService.MetadataKey), "调度器清单保存了已失效的一次性凭据。");
}

static void RunMaintenanceDefinitionPreviewTest(CczProject source, string tablePath, string baselineSha)
{
    WithTempProject(source, tablePath, copyDataFiles: true, project =>
    {
        var dispatcherService = new EffectTriggerDispatcherService();
        var cave = new ExeCodeCaveScanner().Scan(project, minimumLength: 512).Candidates
            .First(item => item.FillKind == "nop" && item.Length >= 512);
        var draft = new EffectTriggerDispatcherDraft
        {
            DispatcherId = "dispatcher-maintenance-test",
            HookContractId = "strategy-damage-formula-v2",
            Capacity = 16,
            HookAddress = 0x0043C2B0,
            CodeAddress = cave.StartVirtualAddress,
            RegistryAddress = cave.StartVirtualAddress,
            Entries = [Entry("program-maintenance-test", 0x01, SemanticEffectAction.AddDamageFixed, 10, 10)]
        };
        var registryBytes = dispatcherService.BuildRegistry(draft);
        WriteVirtual(project, draft.RegistryAddress, registryBytes);
        var currentSha = Sha256(project.ResolveGameFile("Ekd5.exe"));
        var identity = new ProjectPatchIdentityService().Build(project);
        var contract = new HookExecutionContractService().Read(project, draft.HookContractId);
        var dispatcher = new EffectDispatcherManifestV2
        {
            ManifestId = draft.DispatcherId,
            DispatcherId = draft.DispatcherId,
            ProjectIdentity = identity,
            ContractId = draft.HookContractId,
            ContractHash = contract.ContractHash,
            ContractVersion = contract.ContractVersion,
            ContractSnapshot = contract,
            HookAddress = draft.HookAddress,
            CodeAddress = draft.CodeAddress,
            RegistryAddress = draft.RegistryAddress,
            RegistryLength = registryBytes.Length,
            Capacity = draft.Capacity,
            ExeSha256Before = baselineSha,
            ExeSha256After = currentSha,
            Entries = draft.Entries.Select(CloneEntry).ToList(),
            InstallationPackage = new EffectPackage
            {
                PackageId = "dispatcher-maintenance-test",
                PatchSegments =
                [
                    new EffectPatchSegment
                    {
                        TargetFile = "Ekd5.exe", AddressKind = "OdVirtualAddress", Address = draft.RegistryAddress,
                        BytesHex = Convert.ToHexString(registryBytes),
                        ExpectedOldBytesHex = new string('9', registryBytes.Length * 2), CodeCaveId = cave.CaveId,
                        HookPoint = "共享特效注册表"
                    }
                ]
            }
        };
        var blueprint = new ModularCompositeEffectBlueprint
        {
            BlueprintId = "modular-maintenance-test",
            Channel = CompositeEffectChannel.PersonalJob,
            EffectId = 0x01,
            Name = "原名称",
            Description = "原说明",
            Bindings = []
        };
        var modular = new ModularEffectManifestV2
        {
            ManifestId = blueprint.BlueprintId,
            ProjectIdentity = identity,
            DispatcherManifestId = dispatcher.ManifestId,
            DispatcherEntryId = draft.Entries[0].EntryId,
            RegistryAddress = draft.RegistryAddress,
            Blueprint = blueprint,
            Programs =
            [
                new SemanticEffectProgram
                {
                    ProgramId = draft.Entries[0].EntryId, HookContractId = draft.HookContractId,
                    PersonalEffectId = 0x01, ItemEffectId = 0, Action = draft.Entries[0].Action,
                    ValueSource = draft.Entries[0].ValueSource, Value = draft.Entries[0].Value,
                    ExecutionOrder = draft.Entries[0].ExecutionOrder
                }
            ],
            Dispatchers = [draft]
        };
        WriteManifest(ProjectPatchIdentityService.DispatcherManifestRoot(project), dispatcher.ManifestId, dispatcher);
        WriteManifest(ProjectPatchIdentityService.ModularManifestRoot(project), modular.ManifestId, modular);
        WriteShaLineageManifest(project, identity, currentSha);

        var preview = new ModularEffectLifecycleService().PreviewUpdate(project, new ModularEffectMaintenanceDraft
        {
            ManifestId = modular.ManifestId,
            Name = "维护后名称"
        });
        Assert(preview.CanApply, "模块化名称维护预览未通过：" + preview.SummaryZh);
        Assert(preview.Package.PatchSegments.Any(item => item.Address != draft.RegistryAddress),
            "模块化名称维护没有把定义写入段加入最终锁定包。");
        Assert(preview.Package.Metadata.ContainsKey(LockedEffectWriteReceiptService.MetadataKey),
            "模块化维护最终包没有签发一次性凭据。");
    });
}

static EffectPackage ReceiptPackage(CczProject project, string id) => new()
{
    PackageId = id,
    Domain = "patch",
    EffectId = 1,
    Name = "凭据测试",
    PatchSegments =
    [
        new EffectPatchSegment
        {
            TargetFile = "Ekd5.exe", AddressKind = "OdVirtualAddress", Address = 0x004101D9,
            BytesHex = ReadVirtualHex(project, 0x004101D9, 1),
            ExpectedOldBytesHex = ReadVirtualHex(project, 0x004101D9, 1)
        }
    ]
};

static EffectDispatcherEntry Entry(string id, int effectId, string action, int value, int order) => new()
{
    EntryId = id,
    Enabled = true,
    PersonalEffectId = effectId,
    ItemEffectId = 0,
    EffectValueMode = 1,
    StackingMode = 0,
    Action = action,
    ValueSource = SemanticEffectValueSource.Constant,
    Value = value,
    ExecutionOrder = order
};

static EffectDispatcherEntry CloneEntry(EffectDispatcherEntry item) => new()
{
    EntryId = item.EntryId, Enabled = item.Enabled, PersonalEffectId = item.PersonalEffectId,
    ItemEffectId = item.ItemEffectId, EffectValueMode = item.EffectValueMode, StackingMode = item.StackingMode,
    Action = item.Action, ValueSource = item.ValueSource, Value = item.Value, ExecutionOrder = item.ExecutionOrder
};

static EffectPatchSegment Segment(uint address, string hookPoint, bool codeCave) => new()
{
    TargetFile = "Ekd5.exe", AddressKind = "OdVirtualAddress", Address = address,
    AddressHex = $"0x{address:X8}", BytesHex = "90909090", ExpectedOldBytesHex = "90909090",
    CodeCaveId = codeCave ? "test-cave" : string.Empty, HookPoint = hookPoint
};

static EffectPatchSegment CloneSegment(EffectPatchSegment item) => new()
{
    TargetFile = item.TargetFile, AddressKind = item.AddressKind, Address = item.Address, AddressHex = item.AddressHex,
    BytesHex = item.BytesHex, ExpectedOldBytesHex = item.ExpectedOldBytesHex, CodeCaveId = item.CodeCaveId,
    HookPoint = item.HookPoint, AssemblySourceHash = item.AssemblySourceHash, AllocatedRange = item.AllocatedRange,
    EngineProfileSha256 = item.EngineProfileSha256, Comment = item.Comment
};

static void WriteShaLineageManifest(CczProject project, ProjectPatchIdentity identity, string currentSha)
{
    var manifest = new EffectManifest
    {
        ManifestId = "test-sha-lineage",
        ProjectRoot = project.GameRoot,
        ProjectIdentity = identity,
        Package = new EffectPackage { PackageId = "test-sha-lineage" },
        Metadata =
        {
            ["EngineProfileSha256Before"] = EffectWritableProfileStatus.Current65BaselineSha256,
            ["EngineProfileSha256After"] = currentSha
        }
    };
    WriteManifest(ProjectPatchIdentityService.EffectManifestRoot(project), manifest.ManifestId, manifest);
}

static string WriteManifest<T>(string root, string id, T value)
{
    Directory.CreateDirectory(root);
    var path = Path.Combine(root, Path.GetFileNameWithoutExtension(id) + ".json");
    File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
    return path;
}

static void WriteVirtual(CczProject project, uint address, byte[] bytes)
{
    var path = project.ResolveGameFile("Ekd5.exe");
    var file = File.ReadAllBytes(path);
    var offset = checked((int)CCZModStudio.Formats.PeAddressMapper.Load(path).VirtualAddressToFileOffset(address));
    bytes.CopyTo(file, offset);
    File.WriteAllBytes(path, file);
    CczEngineProfileService.Invalidate(path);
    EffectInventoryService.Invalidate(project);
}

static string ReadVirtualHex(CczProject project, uint address, int length)
{
    var path = project.ResolveGameFile("Ekd5.exe");
    var offset = checked((int)CCZModStudio.Formats.PeAddressMapper.Load(path).VirtualAddressToFileOffset(address));
    var file = File.ReadAllBytes(path);
    return Convert.ToHexString(file.AsSpan(offset, length));
}

static string Sha256(string path)
    => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

static void WithTempProject(CczProject source, string tablePath, bool copyDataFiles, Action<CczProject> action)
{
    var root = Path.Combine(Path.GetTempPath(), "CCZModStudio_EffectRuntimeTests_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        File.Copy(source.ResolveGameFile("Ekd5.exe"), Path.Combine(root, "Ekd5.exe"));
        if (copyDataFiles)
        {
            foreach (var file in new[] { "Imsg.e5", "Data.e5", "Star.e5" })
            {
                var sourcePath = source.ResolveGameFile(file);
                if (File.Exists(sourcePath)) File.Copy(sourcePath, Path.Combine(root, file));
            }
        }
        File.WriteAllText(Path.Combine(root, "_CCZModStudio_TestCopy.txt"), "Runtime-only effect safety tests", Encoding.UTF8);
        action(Project(root, tablePath));
    }
    finally
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}

static CczProject Project(string root, string tablePath) => new()
{
    WorkspaceRoot = root,
    GameRoot = root,
    HexTableXmlPath = tablePath
};

static void ExpectFailure(Action action, string expectedText)
{
    try
    {
        action();
        throw new InvalidOperationException("预期操作失败，但实际成功：" + expectedText);
    }
    catch (TargetInvocationException ex) when (ex.InnerException != null && ex.InnerException.Message.Contains(expectedText, StringComparison.Ordinal))
    {
    }
    catch (Exception ex) when (ex.Message.Contains(expectedText, StringComparison.Ordinal))
    {
    }
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
