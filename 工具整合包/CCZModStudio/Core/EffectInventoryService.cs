using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;
using System.Diagnostics;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class EffectInventoryService
{
    private const uint CoreEffectEngineAddress = 0x004101D9;
    private static readonly ConcurrentDictionary<string, Lazy<Task<EffectInventoryReport>>> InventoryCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, long> InventoryAccessOrder = new(StringComparer.OrdinalIgnoreCase);
    private static long _inventoryAccessSequence;

    public static void Invalidate(CczProject? project = null)
    {
        EffectIdLocationIndexService.Invalidate(project);
        if (project == null)
        {
            InventoryCache.Clear();
            InventoryAccessOrder.Clear();
            return;
        }

        var gameRoot = Path.GetFullPath(project.GameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var key in InventoryCache.Keys.Where(key => key.StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase)))
        {
            InventoryCache.TryRemove(key, out _);
            InventoryAccessOrder.TryRemove(key, out _);
        }
    }

    public EffectInventoryReport Scan(CczProject project, string targetFile = "Ekd5.exe")
        => ScanAsync(project, targetFile).GetAwaiter().GetResult();

    public async Task<EffectInventoryReport> ScanAsync(CczProject project, string targetFile = "Ekd5.exe", CancellationToken cancellationToken = default)
    {
        var targetPath = project.ResolveGameFile(targetFile);
        var catalogService = new UnifiedEffectCatalogService();
        var cacheKey = targetPath + "|" + catalogService.BuildFingerprint(project, targetFile);
        var candidate = new Lazy<Task<EffectInventoryReport>>(
            () => Task.Run(() => Build(project, targetFile, targetPath, catalogService), CancellationToken.None),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var lazy = InventoryCache.GetOrAdd(cacheKey, candidate);
        InventoryAccessOrder[cacheKey] = Interlocked.Increment(ref _inventoryAccessSequence);
        var miss = ReferenceEquals(lazy, candidate);
        try
        {
            var report = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            PerformanceMetrics.Increment(miss ? "EffectInventory.CacheMisses" : "EffectInventory.CacheHits");
            TrimCache(targetPath);
            return report;
        }
        catch
        {
            if (lazy.IsValueCreated && lazy.Value.IsFaulted)
            {
                InventoryCache.TryRemove(new KeyValuePair<string, Lazy<Task<EffectInventoryReport>>>(cacheKey, lazy));
                InventoryAccessOrder.TryRemove(cacheKey, out _);
            }
            throw;
        }
    }

    private static EffectInventoryReport Build(CczProject project, string targetFile, string targetPath, UnifiedEffectCatalogService catalogService)
    {
        var started = Stopwatch.GetTimestamp();

        var tables = new Ccz66HexTableAugmentationService().LoadForProject(project, new HexTableParser());
        var catalog = catalogService.Build(project, tables);
        var discovery = new InjectedEffectDiscoveryService().Discover(project, targetFile);
        var report = new EffectInventoryReport
        {
            TargetFilePath = discovery.TargetFilePath,
            ExeSha256 = discovery.ExeSha256,
            EngineVersion = discovery.EngineVersionHint,
            Discovery = discovery,
            Diagnostics = discovery.Diagnostics.ToList()
        };

        var rules = BuildSemanticRules();
        // Discovery candidates belong to a shared immutable analysis snapshot.
        // Inventory evidence is a derived view and must never mutate those objects.
        var evidence = new Dictionary<InjectedEffectCandidate, InstalledEffectEvidenceDecision>(
            ReferenceEqualityComparer.Instance);
        foreach (var candidate in discovery.Candidates)
            evidence[candidate] = new InstalledEffectEvidenceService().Evaluate(candidate, discovery.ExeSha256);
        report.ManagedInjectedCount = evidence.Values.Count(item => item.IsPresent && item.IsToolManaged);
        report.LegacyPresentCount = evidence.Values.Count(item => item.IsPresent && !item.IsToolManaged);
        report.ConfirmedInjectedEffects = BuildInjectedInstances(discovery.Candidates, evidence, rules, catalog);
        report.InjectedEffects = report.ConfirmedInjectedEffects;
        report.NativeEffects = BuildNativeInstances(discovery.Candidates, rules, report.InjectedEffects, catalog);
        report.Effects = MergeLogicalEffects(report.InjectedEffects, report.NativeEffects);
        report.IncompleteOrHistoricalEffects = BuildHistoricalInstances(discovery.Candidates, evidence, rules, catalog);
        AddCompositeManifestInstances(project, report, catalog);
        AddModularManifestInstances(project, report, catalog);
        foreach (var instance in report.Effects.Concat(report.InjectedEffects).Concat(report.NativeEffects))
            instance.EvidenceExeSha256 = report.ExeSha256;
        report.PersonalJobOptions = catalog.PersonalJobEffects;
        report.ItemOptions = catalog.ItemEffects;
        HydratePatchPointBytes(report.Effects, ExecutableAnalysisSnapshotCache.Shared.GetBase(targetPath));
        ApplyWriteDecisions(project, report.Effects);
        report.Summary = $"扫描完成：识别到 {report.Effects.Count} 个逻辑特技；工具受管注入 {report.ManagedInjectedCount} 项，遗留实现 {report.LegacyPresentCount} 项；另有 {report.IncompleteOrHistoricalEffects.Count} 个样本相似或历史记录、{report.Diagnostics.Count} 条高级诊断。";
        var executable = ExecutableAnalysisSnapshotCache.Shared.GetBase(targetPath);
        report.AnalysisFingerprint = $"{executable.Fingerprint.Length}:{executable.Fingerprint.LastWriteTimeUtcTicks}:{executable.Fingerprint.ChangeGeneration}";
        report.CacheState = "BuiltFromSharedSnapshot";
        report.CompletedStages = ["读取表", "分析 EXE", "发现注入特效", "构建逻辑库存", "补齐物理位置旧字节"];
        report.ProfileAudit = new ExecutableProfileAuditService().AuditSnapshot(executable);
        report.Performance["BuildMilliseconds"] = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
        return report;
    }

    private static void TrimCache(string targetPath)
    {
        var prefix = targetPath + "|";
        foreach (var key in InventoryAccessOrder.Where(pair => pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(pair => pair.Value).Skip(2).Select(pair => pair.Key).ToArray())
        {
            InventoryCache.TryRemove(key, out _);
            InventoryAccessOrder.TryRemove(key, out _);
        }
    }

    private static void AddCompositeManifestInstances(CczProject project, EffectInventoryReport report, UnifiedEffectCatalog catalog)
    {
        foreach (var manifest in new CompositeEffectService().List(project))
        {
            var compositeService = new CompositeEffectService();
            var installationStatus = compositeService.ResolveInstallationStatus(project, manifest);
            var complete = installationStatus == CompositeInstallationStatus.Complete;
            var channel = manifest.Draft.Channel == CompositeEffectChannel.Item
                ? catalog.ResolveItem(manifest.Draft.EffectId)
                : catalog.ResolvePersonalJob(manifest.Draft.EffectId);
            var instance = new LogicalEffectInstance
            {
                InstanceId = manifest.Draft.CompositeId,
                SourceKind = EffectInstanceSourceKind.Injected,
                HasInjectedImplementation = complete || installationStatus == CompositeInstallationStatus.Disabled,
                Name = manifest.Draft.Name,
                TriggerPhase = string.Join("、", manifest.Members.Select(item => item.TriggerPhaseZh).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct()),
                NaturalLanguageDescription = $"复合特效，包含 {string.Join("、", manifest.Members.Select(item => item.DisplayNameZh))}。",
                CurrentParameterSummary = string.Join("；", manifest.ParameterBlock.Records.Select(item =>
                    $"{manifest.Members.FirstOrDefault(member => member.InstanceId == item.InstanceId)?.DisplayNameZh ?? item.InstanceId}：{item.EffectValue}")),
                PatchCategory = "CompositeEffect",
                IsEditable = complete,
                EditabilityReason = complete ? "所有成员适配器和参数块均与安装记录一致。" : "安装字节不完整或已被其他补丁修改，不能一键编辑。",
                DetectionScore = complete ? 100 : 50,
                EvidenceLevel = complete ? "VerifiedStatic" : "Hypothesis",
                InstallationStatus = installationStatus,
                InstallationStatusZh = InstallationStatusZh(installationStatus),
                WritableContractId = manifest.ManifestId,
                EvidenceExeSha256 = report.ExeSha256,
                PersonalChannel = manifest.Draft.Channel == CompositeEffectChannel.PersonalJob ? CloneChannel(channel) : null,
                ItemChannel = manifest.Draft.Channel == CompositeEffectChannel.Item ? CloneChannel(channel) : null,
                EntryHooks = manifest.Members.SelectMany(item => item.RedirectedCallSites).Distinct().OrderBy(value => value).ToList(),
                CodeEntries = manifest.ParameterBlock.Address == 0 ? [] : [manifest.ParameterBlock.Address],
                MatchedEvidence = complete ? ["复合 manifest", "当前字节复读", "成员调用重定向"] : ["复合 manifest"],
                MissingEvidence = complete ? [] : [$"当前安装状态：{InstallationStatusZh(installationStatus)}"]
            };
            foreach (var record in manifest.ParameterBlock.Records)
            {
                instance.Parameters.Add(new LogicalEffectParameter
                {
                    SlotId = $"composite-value-{record.InstanceId}",
                    Role = InjectedEffectParameterRole.EffectValue,
                    DisplayName = manifest.Members.FirstOrDefault(item => item.InstanceId == record.InstanceId)?.DisplayNameZh + "数值",
                    MeaningKind = record.EffectValueMode == 0 ? EffectParameterMeaningKind.FixedValue : EffectParameterMeaningKind.Switch,
                    Value = record.EffectValue,
                    Address = record.ValueAddress,
                    ByteLength = 4,
                    Minimum = int.MinValue,
                    Maximum = int.MaxValue,
                    IsEditable = complete && record.EffectValueMode == 0,
                    IsConsistent = true,
                    ObservedValues = [record.EffectValue],
                    NaturalLanguageMeaning = record.EffectValueMode == 0 ? "复合版本的独立成员数值。" : "开关成员固定返回拥有。",
                    PhysicalPatchPoints = record.ValueAddress == 0 ? [] :
                    [
                        new EffectPhysicalPatchPoint
                        {
                            Address = record.ValueAddress, ByteLength = 4, Value = record.EffectValue,
                            GroupName = manifest.ManifestId,
                            ExpectedOldBytesHex = EffectPatchByteService.ReadVirtualBytes(project, record.ValueAddress, 4)
                        }
                    ]
                });
            }
            instance.Implementations.Add(new EffectImplementationReference
            {
                SourceKind = EffectInstanceSourceKind.Injected,
                Name = manifest.Draft.Name,
                SignatureId = manifest.ManifestId,
                EntryHooks = instance.EntryHooks.ToList(),
                CodeEntries = instance.CodeEntries.ToList()
            });
            report.Effects.RemoveAll(item => item.InstanceId.Equals(instance.InstanceId, StringComparison.OrdinalIgnoreCase));
            report.Effects.Add(instance);
        }
        QualifyDuplicateNames(report.Effects);
        report.Effects = report.Effects.OrderBy(item => item.Name, StringComparer.CurrentCulture).ToList();
    }

    private static void AddModularManifestInstances(CczProject project, EffectInventoryReport report, UnifiedEffectCatalog catalog)
    {
        var lifecycle = new ModularEffectLifecycleService();
        foreach (var manifest in lifecycle.ListModular(project))
        {
            var status = lifecycle.ResolveModularStatus(project, manifest);
            var complete = status == CompositeInstallationStatus.Complete;
            var program = manifest.Programs.FirstOrDefault();
            if (program == null) continue;
            var channel = manifest.Blueprint.Channel == CompositeEffectChannel.Item
                ? catalog.ResolveItem(manifest.Blueprint.EffectId)
                : catalog.ResolvePersonalJob(manifest.Blueprint.EffectId);
            var instance = new LogicalEffectInstance
            {
                InstanceId = manifest.Blueprint.BlueprintId,
                SourceKind = EffectInstanceSourceKind.Injected,
                HasInjectedImplementation = complete || status == CompositeInstallationStatus.Disabled,
                Name = manifest.Blueprint.Name,
                TriggerPhase = manifest.Blueprint.TriggerModuleId,
                NaturalLanguageDescription = manifest.Blueprint.Description,
                CurrentParameterSummary = $"数值 {program.Value}，执行顺序 {program.ExecutionOrder}",
                PatchCategory = "ModularSemanticEffectV2",
                IsEditable = complete,
                EditabilityReason = complete ? "共享调度注册条目、定义与绑定均可复读。" : "共享调度器或注册条目不完整。",
                DetectionScore = complete ? 100 : 50,
                EvidenceLevel = complete ? "VerifiedStatic" : "Hypothesis",
                InstallationStatus = status,
                InstallationStatusZh = InstallationStatusZh(status),
                WritableContractId = manifest.ManifestId,
                EvidenceExeSha256 = report.ExeSha256,
                PersonalChannel = manifest.Blueprint.Channel == CompositeEffectChannel.PersonalJob ? CloneChannel(channel) : null,
                ItemChannel = manifest.Blueprint.Channel == CompositeEffectChannel.Item ? CloneChannel(channel) : null,
                EntryHooks = manifest.Dispatchers.Select(item => item.HookAddress).Where(value => value != 0).Distinct().ToList(),
                CodeEntries = manifest.Dispatchers.Select(item => item.CodeAddress).Where(value => value != 0).Distinct().ToList(),
                MatchedEvidence = complete ? ["模块化 V2 manifest", "CCZD 注册表", "当前字节复读"] : ["模块化 V2 manifest"],
                MissingEvidence = complete ? [] : [$"当前安装状态：{InstallationStatusZh(status)}"]
            };
            instance.Parameters.Add(new LogicalEffectParameter
            {
                SlotId = "dispatcher-value-" + program.ProgramId,
                Role = InjectedEffectParameterRole.EffectValue,
                DisplayName = "动作数值",
                MeaningKind = program.Action.Contains("Percent", StringComparison.OrdinalIgnoreCase)
                    ? EffectParameterMeaningKind.Percentage : EffectParameterMeaningKind.FixedValue,
                Value = program.Value,
                ByteLength = 4,
                Minimum = 0,
                Maximum = program.Action.Contains("Percent", StringComparison.OrdinalIgnoreCase) ? 100 : 65535,
                IsEditable = complete,
                IsConsistent = true,
                ObservedValues = [program.Value],
                NaturalLanguageMeaning = "共享调度注册条目中的动作数值。"
            });
            report.Effects.RemoveAll(item => item.InstanceId.Equals(instance.InstanceId, StringComparison.OrdinalIgnoreCase));
            report.Effects.Add(instance);
        }
        QualifyDuplicateNames(report.Effects);
        report.Effects = report.Effects.OrderBy(item => item.Name, StringComparer.CurrentCulture).ToList();
    }

    private static string InstallationStatusZh(string status) => status switch
    {
        CompositeInstallationStatus.Complete => "完整",
        CompositeInstallationStatus.Disabled => "已停用",
        CompositeInstallationStatus.Repairable => "可修复",
        CompositeInstallationStatus.Incomplete => "安装不完整",
        CompositeInstallationStatus.ExternallyModified => "外部修改",
        CompositeInstallationStatus.Removed => "已移除",
        _ => "状态未知"
    };

    private static bool CompositeManifestMatchesCurrentBytes(CczProject project, CompositeEffectManifest manifest)
    {
        try
        {
            foreach (var group in manifest.Package.PatchSegments.GroupBy(segment => segment.TargetFile, StringComparer.OrdinalIgnoreCase))
            {
                var target = string.IsNullOrWhiteSpace(group.Key) ? "Ekd5.exe" : group.Key;
                var path = project.ResolveGameFile(target);
                var bytes = File.ReadAllBytes(path);
                PeAddressMapper? mapper = null;
                foreach (var segment in group)
                {
                    var expected = EffectPatchByteService.ParseHex(segment.BytesHex);
                    var offset = segment.AddressKind.Equals("FileOffset", StringComparison.OrdinalIgnoreCase)
                        ? segment.Address
                        : (mapper ??= PeAddressMapper.Load(path)).VirtualAddressToFileOffset(segment.Address);
                    if (offset < 0 || offset + expected.Length > bytes.LongLength ||
                        !bytes.AsSpan(checked((int)offset), expected.Length).SequenceEqual(expected)) return false;
                }
            }
            return true;
        }
        catch { return false; }
    }

    public LogicalEffectInstance ReadInstance(CczProject project, string instanceId, string targetFile = "Ekd5.exe")
    {
        var report = Scan(project, targetFile);
        return report.Effects.Concat(report.InjectedEffects).Concat(report.NativeEffects)
                   .FirstOrDefault(item => item.InstanceId.Equals(instanceId, StringComparison.OrdinalIgnoreCase))
               ?? throw new KeyNotFoundException("未找到特技实例：" + instanceId);
    }

    public EffectParameterUpdatePreview PreviewParameterUpdate(
        CczProject project,
        EffectParameterUpdateRequest request,
        string targetFile = "Ekd5.exe")
    {
        var instance = ReadInstance(project, request.InstanceId, targetFile);
        var result = new EffectParameterUpdatePreview();
        if (!instance.IsEditable)
        {
            result.Warnings.Add(instance.EditabilityReason);
            result.Summary = "参数修改预览失败：该特技没有经过验证的安全参数位。";
            return result;
        }

        if (request.Updates.Count == 0)
        {
            result.Warnings.Add("没有要修改的参数。");
            result.Summary = "参数修改预览失败。";
            return result;
        }

        var exePath = project.ResolveGameFile(targetFile);
        var executable = ExecutableAnalysisSnapshotCache.Shared.GetBase(exePath);
        var image = executable.Bytes;
        var profile = new EnginePatchProfileService().Build(project);
        var package = new EffectPackage
        {
            PackageId = $"effect-parameter-{SanitizeId(instance.InstanceId)}-{DateTime.Now:yyyyMMddHHmmssfff}",
            Domain = "patch",
            Name = instance.Name + " 参数修改",
            Description = "仅修改扫描确认的特技参数位。",
            BackupNote = "参数修改使用当前字节锁和 EXE SHA 锁，可通过 manifest 备份恢复。",
            Metadata =
            {
                ["LogicalEffectInstanceId"] = instance.InstanceId,
                ["ParameterOnlyUpdate"] = "true",
                ["EngineProfileSha256"] = profile.ExeSha256
            }
        };

        foreach (var update in request.Updates)
        {
            var parameter = instance.Parameters.FirstOrDefault(item => item.SlotId.Equals(update.SlotId, StringComparison.OrdinalIgnoreCase));
            if (parameter == null)
            {
                result.Warnings.Add("未知参数位：" + update.SlotId);
                continue;
            }

            if (!parameter.IsConsistent)
            {
                result.Warnings.Add(parameter.DisplayName + "在多个判定位置的当前值不一致，不能一键修改。");
                continue;
            }

            if (!parameter.IsEditable || parameter.PhysicalPatchPoints.Count == 0)
            {
                result.Warnings.Add(parameter.DisplayName + "没有可验证的物理写入点或编码宽度。");
                continue;
            }

            if (parameter.Minimum.HasValue && update.Value < parameter.Minimum.Value ||
                parameter.Maximum.HasValue && update.Value > parameter.Maximum.Value)
            {
                result.Warnings.Add($"{parameter.DisplayName} 必须在 {parameter.Minimum ?? int.MinValue} 到 {parameter.Maximum ?? int.MaxValue} 之间。");
                continue;
            }

            foreach (var point in parameter.PhysicalPatchPoints)
            {
                if (point.ByteLength is < 1 or > 4)
                {
                    result.Warnings.Add($"{parameter.DisplayName}在 {point.Address:X8} 的编码宽度无效。");
                    continue;
                }

                var offset = executable.VirtualAddressToFileOffset(point.Address);
                if (offset < 0 || offset + point.ByteLength > image.LongLength)
                {
                    result.Warnings.Add($"{parameter.DisplayName}在 {point.Address:X8} 的文件映射超出范围。");
                    continue;
                }

                var oldBytes = image.AsSpan(checked((int)offset), point.ByteLength).ToArray();
                if (!string.IsNullOrWhiteSpace(point.ExpectedOldBytesHex) &&
                    !point.ExpectedOldBytesHex.Equals(ToHex(oldBytes), StringComparison.OrdinalIgnoreCase))
                {
                    result.Warnings.Add($"{parameter.DisplayName}在 {point.Address:X8} 的当前字节已变化，请重新扫描。");
                    continue;
                }

                var existing = package.PatchSegments.FirstOrDefault(segment => segment.Address == point.Address);
                var newBytes = EncodeLittleEndian(update.Value, point.ByteLength);
                if (existing != null)
                {
                    if (!existing.BytesHex.Equals(ToHex(newBytes), StringComparison.OrdinalIgnoreCase))
                    {
                        result.Warnings.Add($"地址 {point.Address:X8} 被多个逻辑参数要求写入不同值。");
                    }
                    continue;
                }

                package.PatchSegments.Add(new EffectPatchSegment
                {
                    TargetFile = targetFile,
                    AddressKind = "OdVirtualAddress",
                    Address = point.Address,
                    AddressHex = $"0x{point.Address:X8}",
                    BytesHex = ToHex(newBytes),
                    ExpectedOldBytesHex = ToHex(oldBytes),
                    HookPoint = instance.InstanceId,
                    EngineProfileSha256 = profile.ExeSha256,
                    Comment = $"{parameter.DisplayName}（同步写入 {parameter.PhysicalPatchPoints.Count} 处）：{parameter.NaturalLanguageMeaning}",
                    SemanticFieldId = "instance-parameter:" + parameter.SlotId,
                    ModificationKind = parameter.Role is InjectedEffectParameterRole.Personal or InjectedEffectParameterRole.Equipment
                        ? EffectModificationKind.DirectEffectId
                        : EffectModificationKind.ManagedParameter,
                    SourceLocationId = $"{instance.InstanceId}:0x{point.Address:X8}:{point.ByteLength}",
                    RequiredCapability = parameter.Role is InjectedEffectParameterRole.Personal or InjectedEffectParameterRole.Equipment
                        ? EffectWriteCapability.DirectParameter
                        : EffectWriteCapability.DirectParameter
                });
            }
        }

        var expectedAddresses = request.Updates
            .Select(update => instance.Parameters.FirstOrDefault(item => item.SlotId.Equals(update.SlotId, StringComparison.OrdinalIgnoreCase)))
            .Where(parameter => parameter != null)
            .SelectMany(parameter => parameter!.PhysicalPatchPoints)
            .Select(point => point.Address)
            .Distinct()
            .Count();
        if (result.Warnings.Count > 0 || package.PatchSegments.Count != expectedAddresses)
        {
            result.Package = package;
            result.Summary = "参数修改预览失败：存在未通过校验的参数。";
            return result;
        }

        var preview = new EffectPackageService().PreviewPatch(project, package);
        result.Package = package;
        result.PatchPreview = preview;
        result.Warnings.AddRange(preview.Warnings);
        result.CanApply = preview.CanApply;
        if (result.CanApply)
            new LockedEffectWriteReceiptService().Issue(project, package, "effect-patch");
        result.Summary = preview.CanApply
            ? $"参数修改可以写入，将同步修改 {package.PatchSegments.Count} 处。"
            : "参数修改预览未通过：" + string.Join("；", preview.Warnings);
        return result;
    }

    private static void ApplyWriteDecisions(CczProject project, IEnumerable<LogicalEffectInstance> instances)
    {
        var service = new EffectWriteDecisionService();
        foreach (var instance in instances.Distinct())
        {
            foreach (var parameter in instance.Parameters)
            {
                var exact = parameter.IsConsistent && parameter.PhysicalPatchPoints.Count > 0 &&
                            parameter.PhysicalPatchPoints.All(point => !string.IsNullOrWhiteSpace(point.ExpectedOldBytesHex));
                var kind = parameter.Role is InjectedEffectParameterRole.Personal or InjectedEffectParameterRole.Equipment
                    ? EffectModificationKind.DirectEffectId
                    : EffectModificationKind.ManagedParameter;
                parameter.WriteDecision = service.Decide(project, new EffectWriteRequest
                {
                    ModificationKind = kind,
                    SemanticFieldId = "instance-parameter:" + parameter.SlotId,
                    SourceLocationId = string.Join(";", parameter.PhysicalPatchPoints.Select(point => $"0x{point.Address:X8}:{point.ByteLength}")),
                    HasExactLocation = exact,
                    HasStaticContract = true,
                    AffectedConsumers = parameter.PhysicalPatchPoints.Count,
                    RunMode = EffectSandboxService.IsSandbox(project) ? EffectWriteRunMode.SandboxValidation : EffectWriteRunMode.Formal
                });
                parameter.IsEditable = parameter.IsEditable && parameter.WriteDecision.CanEdit;
            }
            instance.WriteDecision = instance.Parameters.Select(item => item.WriteDecision)
                .Where(item => item != null).OrderByDescending(item => item!.CanApply).ThenByDescending(item => item!.CanEdit).FirstOrDefault();
            instance.IsEditable = instance.IsEditable && instance.Parameters.Any(item => item.WriteDecision?.CanEdit == true);
        }
    }

    public IReadOnlyList<EffectSemanticRuleManifest> GetSemanticRules() => BuildSemanticRules();

    private static List<LogicalEffectInstance> BuildInjectedInstances(
        IReadOnlyList<InjectedEffectCandidate> candidates,
        IReadOnlyDictionary<InjectedEffectCandidate, InstalledEffectEvidenceDecision> evidence,
        IReadOnlyList<EffectSemanticRuleManifest> rules,
        UnifiedEffectCatalog catalog)
    {
        var injected = candidates.Where(candidate => IsPresentInjectionEvidence(candidate, evidence)).ToList();
        var groups = injected.GroupBy(BuildInjectedGroupKey, StringComparer.OrdinalIgnoreCase);
        return groups.Select(group =>
            {
                var members = group.ToList();
                var instance = BuildInstance(members, EffectInstanceSourceKind.Injected, rules, catalog);
                var ownership = members.Select(member => evidence[member]).OrderByDescending(item => item.IsToolManaged).First();
                instance.InstallationStatus = ownership.OwnershipStatus;
                instance.InstallationStatusZh = ownership.StatusZh;
                if (!ownership.IsToolManaged)
                {
                    instance.IsEditable = false;
                    instance.EditabilityReason = "遗留实现没有当前项目受管清单；只允许在沙箱中链式保留。";
                }
                return instance;
            })
            .OrderBy(item => item.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    private static List<LogicalEffectInstance> BuildHistoricalInstances(
        IReadOnlyList<InjectedEffectCandidate> candidates,
        IReadOnlyDictionary<InjectedEffectCandidate, InstalledEffectEvidenceDecision> evidence,
        IReadOnlyList<EffectSemanticRuleManifest> rules,
        UnifiedEffectCatalog catalog)
        => candidates
            .Where(candidate => evidence.TryGetValue(candidate, out var decision) &&
                                !decision.IsPresent && decision.Status == "SampleSimilar")
            .GroupBy(BuildInjectedGroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var instance = BuildInstance(group.ToList(), EffectInstanceSourceKind.Injected, rules, catalog);
                instance.HasInjectedImplementation = false;
                instance.IsEditable = false;
                instance.InstallationStatus = "SampleSimilar";
                instance.InstallationStatusZh = "发现相似样本，未确认安装";
                instance.EditabilityReason = "当前 EXE 没有形成完整安装证据。";
                return instance;
            })
            .OrderBy(item => item.Name, StringComparer.CurrentCulture)
            .ToList();

    private static List<LogicalEffectInstance> BuildNativeInstances(
        IReadOnlyList<InjectedEffectCandidate> candidates,
        IReadOnlyList<EffectSemanticRuleManifest> rules,
        IReadOnlyList<LogicalEffectInstance> injected,
        UnifiedEffectCatalog catalog)
    {
        var injectedCandidateKeys = injected.SelectMany(item => item.CandidateKeys).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var native = candidates.Where(candidate =>
                candidate.PatternKind == InjectedEffectPatternKind.InlineCoreStub &&
                candidate.PersonalEffectId.HasValue &&
                candidate.EquipmentEffectId.HasValue &&
                !injectedCandidateKeys.Contains(BuildCandidateKey(candidate)))
            .GroupBy(candidate => string.Create(
                CultureInfo.InvariantCulture,
                $"{candidate.ConsumerFunctionAddress.GetValueOrDefault(candidate.Address):X8}:{candidate.PersonalEffectId:X2}:{candidate.EquipmentEffectId:X2}:{candidate.EffectValueFlag:X1}:{candidate.StackingFlag:X1}"),
                StringComparer.OrdinalIgnoreCase);

        return native.Select(group => BuildInstance(group.ToList(), EffectInstanceSourceKind.Native, rules, catalog))
            .OrderBy(item => item.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    private static bool IsPresentInjectionEvidence(
        InjectedEffectCandidate candidate,
        IReadOnlyDictionary<InjectedEffectCandidate, InstalledEffectEvidenceDecision> evidence)
        => evidence.TryGetValue(candidate, out var decision) && decision.IsPresent;

    private static string BuildInjectedGroupKey(InjectedEffectCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.NormalizedSignatureId)) return "sig:" + candidate.NormalizedSignatureId;
        if (candidate.CodeCaveEntryAddress.HasValue) return $"cave:{candidate.CodeCaveEntryAddress.Value:X8}";
        return $"hook:{candidate.Address:X8}:{candidate.PersonalEffectId:X2}:{candidate.EquipmentEffectId:X2}";
    }

    private static LogicalEffectInstance BuildInstance(
        IReadOnlyList<InjectedEffectCandidate> candidates,
        string sourceKind,
        IReadOnlyList<EffectSemanticRuleManifest> rules,
        UnifiedEffectCatalog catalog)
    {
        var primary = candidates.OrderByDescending(candidate => candidate.DetectionScore).First();
        var rule = FindRule(primary.Name, rules);
        var parameterSlots = MergeParameters(candidates, rule);
        var personalChannel = primary.PersonalEffectId.HasValue ? CloneChannel(catalog.ResolvePersonalJob(primary.PersonalEffectId.Value)) : null;
        var itemChannel = primary.EquipmentEffectId.HasValue ? CloneChannel(catalog.ResolveItem(primary.EquipmentEffectId.Value)) : null;
        var implementationName = IsGenericStubName(primary.Name) ? string.Empty : primary.Name;
        var resolvedName = ResolveInstanceName(implementationName, personalChannel, itemChannel, sourceKind);
        AddNameConflict(personalChannel, implementationName);
        AddNameConflict(itemChannel, implementationName);
        var identity = sourceKind + ":" + BuildInjectedGroupKey(primary) + ":" +
                       string.Join(",", parameterSlots.Select(item => item.Role + "=" + item.Value));
        var instance = new LogicalEffectInstance
        {
            InstanceId = "effect-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16].ToLowerInvariant(),
            SourceKind = sourceKind,
            HasInjectedImplementation = sourceKind == EffectInstanceSourceKind.Injected,
            HasNativeImplementation = sourceKind == EffectInstanceSourceKind.Native,
            Name = resolvedName,
            TriggerPhase = rule?.TriggerPhase ?? InferTriggerPhase(primary),
            PatchCategory = primary.PatchCategory,
            DetectionScore = primary.DetectionScore,
            EvidenceLevel = primary.DetectionLevel,
            Parameters = parameterSlots,
            EntryHooks = candidates.SelectMany(candidate => new[] { candidate.JumpOutAddress, candidate.Address == 0 ? null : candidate.Address })
                .Where(value => value.HasValue).Select(value => value!.Value).Distinct().OrderBy(value => value).ToList(),
            CodeEntries = candidates.Select(candidate => candidate.CodeCaveEntryAddress).Where(value => value.HasValue)
                .Select(value => value!.Value).Distinct().OrderBy(value => value).ToList(),
            CoreCalls = candidates.SelectMany(candidate => candidate.CheckGroups.Select(group => group.GuardCallAddress))
                .Where(value => value.HasValue).Select(value => value!.Value).Distinct().OrderBy(value => value).ToList(),
            ConsumerFunctionAddresses = candidates.Select(candidate => candidate.ConsumerFunctionAddress)
                .Where(value => value.HasValue && value.Value != 0).Select(value => value!.Value).Distinct().OrderBy(value => value).ToList(),
            WrapperEntries = candidates.Select(candidate => candidate.WrapperEntryAddress).Where(value => value.HasValue)
                .Select(value => value!.Value).Distinct().OrderBy(value => value).ToList(),
            PersonalChannel = personalChannel,
            ItemChannel = itemChannel,
            MatchedEvidence = candidates.SelectMany(candidate => candidate.MatchedAnchors).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            MissingEvidence = candidates.SelectMany(candidate => candidate.MissingAnchors.Concat(candidate.FailureReasons)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            CandidateKeys = candidates.Select(BuildCandidateKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };

        instance.Implementations.Add(new EffectImplementationReference
        {
            SourceKind = sourceKind,
            Name = FirstNonEmpty(implementationName, resolvedName),
            SignatureId = primary.NormalizedSignatureId,
            EntryHooks = instance.EntryHooks.ToList(),
            CodeEntries = instance.CodeEntries.ToList(),
            WrapperEntries = instance.WrapperEntries.ToList()
        });

        instance.NaturalLanguageDescription = BuildNaturalLanguage(instance, primary, rule);
        instance.CurrentParameterSummary = string.Join("；", parameterSlots
            .Where(item => item.Value.HasValue || !item.IsConsistent)
            .Select(item => item.Role switch
            {
                InjectedEffectParameterRole.Personal when personalChannel != null => personalChannel.DisplayName,
                InjectedEffectParameterRole.Equipment when itemChannel != null => itemChannel.DisplayName,
                _ => item.DisplayName + " " + FormatParameterValue(item)
            }));
        instance.IsEditable = sourceKind == EffectInstanceSourceKind.Injected &&
                              parameterSlots.Any(item => item.IsEditable) &&
                              primary.PatchCategory is not InjectedEffectPatchCategory.ComplexMultiHookPatch and
                                  not InjectedEffectPatchCategory.FunctionExtensionPatch;
        instance.EditabilityReason = instance.IsEditable
            ? "已恢复安全参数位，修改前仍需通过当前字节锁预览。"
            : sourceKind == EffectInstanceSourceKind.Native
                ? "原生桩仅供索引，不作为注入补丁参数修改。"
                : "复杂补丁或参数地址不完整，只允许查看。";
        return instance;
    }

    private static List<LogicalEffectParameter> MergeParameters(
        IReadOnlyList<InjectedEffectCandidate> candidates,
        EffectSemanticRuleManifest? rule)
    {
        var result = new List<LogicalEffectParameter>();
        foreach (var group in candidates.SelectMany(candidate => candidate.ParameterSlots)
                     .Where(slot => slot.Value.HasValue)
                     .GroupBy(slot => BuildLogicalParameterKey(slot, rule), StringComparer.OrdinalIgnoreCase))
        {
            var slots = group.GroupBy(slot => $"{slot.Address:X8}:{slot.ByteLength}", StringComparer.OrdinalIgnoreCase)
                .Select(slotGroup => slotGroup.First()).ToList();
            var slot = slots[0];
            var observedValues = slots.Select(item => item.Value!.Value).Distinct().OrderBy(value => value).ToList();
            var isConsistent = observedValues.Count == 1;
            var slotValue = slot.Value.GetValueOrDefault();
            var meaningKind = GetMeaningKind(slot.Role, rule);
            var (minimum, maximum) = GetSafeRange(slot.Role, meaningKind);
            var points = slots.Where(item => item.Address.HasValue && item.ByteLength is >= 1 and <= 4)
                .Select(item => new EffectPhysicalPatchPoint
                {
                    Address = item.Address!.Value,
                    ByteLength = item.ByteLength,
                    Value = item.Value!.Value,
                    GroupName = item.GroupName
                }).OrderBy(item => item.Address).ToList();
            result.Add(new LogicalEffectParameter
            {
                SlotId = "slot-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(group.Key)))[..12].ToLowerInvariant(),
                Role = slot.Role,
                DisplayName = TranslateRole(slot.Role, slot.DisplayName),
                MeaningKind = meaningKind,
                Unit = GetUnit(meaningKind, rule),
                Value = isConsistent ? slot.Value : null,
                Address = slot.Address,
                ByteLength = slot.ByteLength,
                Minimum = minimum,
                Maximum = maximum,
                IsConsistent = isConsistent,
                ObservedValues = observedValues,
                PhysicalPatchPoints = points,
                IsEditable = isConsistent && points.Count > 0 &&
                             (rule?.SafelyEditableRoles.Count is not > 0 || rule.SafelyEditableRoles.Contains(slot.Role, StringComparer.OrdinalIgnoreCase)),
                NaturalLanguageMeaning = isConsistent
                    ? ExplainParameter(slot.Role, slotValue, meaningKind, rule)
                    : "多个判定位置的当前值不一致，请在高级诊断中复核后再修改。",
                GroupName = string.Join(" / ", slots.Select(item => item.GroupName).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal))
            });
        }

        return result.OrderBy(item => ParameterOrder(item.Role)).ThenBy(item => item.GroupName, StringComparer.Ordinal).ToList();
    }

    private static string BuildLogicalParameterKey(InjectedEffectParameterSlot slot, EffectSemanticRuleManifest? rule)
    {
        if (slot.Role is InjectedEffectParameterRole.Personal or InjectedEffectParameterRole.Equipment or
            InjectedEffectParameterRole.EffectValue)
        {
            return slot.Role;
        }

        if (rule?.SafelyEditableRoles.Contains(slot.Role, StringComparer.OrdinalIgnoreCase) == true)
        {
            return slot.Role;
        }

        var normalizedGroup = string.Concat(slot.GroupName.Where(char.IsLetterOrDigit));
        return slot.Role + ":" + normalizedGroup;
    }

    private static List<LogicalEffectInstance> MergeLogicalEffects(
        IReadOnlyList<LogicalEffectInstance> injected,
        IReadOnlyList<LogicalEffectInstance> native)
    {
        var rules = BuildSemanticRules();
        var result = new List<LogicalEffectInstance>();
        foreach (var group in injected.Concat(native).GroupBy(item => BuildLogicalEffectKey(item, rules), StringComparer.OrdinalIgnoreCase))
        {
            var members = group.OrderByDescending(item => item.HasInjectedImplementation).ThenByDescending(item => item.DetectionScore).ToList();
            var primary = members[0];
            primary.HasInjectedImplementation = members.Any(item => item.HasInjectedImplementation);
            primary.HasNativeImplementation = members.Any(item => item.HasNativeImplementation);
            primary.SourceKind = primary.HasInjectedImplementation && primary.HasNativeImplementation
                ? EffectInstanceSourceKind.Combined
                : primary.HasInjectedImplementation ? EffectInstanceSourceKind.Injected : EffectInstanceSourceKind.Native;
            primary.InstanceId = BuildStableId("logical:" + group.Key);
            primary.EntryHooks = members.SelectMany(item => item.EntryHooks).Distinct().OrderBy(value => value).ToList();
            primary.CodeEntries = members.SelectMany(item => item.CodeEntries).Distinct().OrderBy(value => value).ToList();
            primary.CoreCalls = members.SelectMany(item => item.CoreCalls).Distinct().OrderBy(value => value).ToList();
            primary.ConsumerFunctionAddresses = members.SelectMany(item => item.ConsumerFunctionAddresses).Distinct().OrderBy(value => value).ToList();
            primary.WrapperEntries = members.SelectMany(item => item.WrapperEntries).Distinct().OrderBy(value => value).ToList();
            primary.Implementations = members.SelectMany(item => item.Implementations).ToList();
            primary.CandidateKeys = members.SelectMany(item => item.CandidateKeys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            primary.MatchedEvidence = members.SelectMany(item => item.MatchedEvidence).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            primary.MissingEvidence = members.SelectMany(item => item.MissingEvidence).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            result.Add(primary);
        }

        QualifyDuplicateNames(result);
        return result.OrderBy(item => item.Name, StringComparer.CurrentCulture).ToList();
    }

    private static string BuildLogicalEffectKey(LogicalEffectInstance item, IReadOnlyList<EffectSemanticRuleManifest> rules)
    {
        var signature = item.Implementations.Select(implementation => implementation.SignatureId)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        var rule = FindRule(item.Name, rules);
        var personal = item.PersonalChannel?.EffectId ?? -1;
        var equipment = item.ItemChannel?.EffectId ?? -1;
        var valueMode = item.Parameters.FirstOrDefault(parameter => parameter.Role == InjectedEffectParameterRole.EffectValue)?.Value ?? -1;
        var stackMode = item.Parameters.FirstOrDefault(parameter => parameter.Role == InjectedEffectParameterRole.BooleanOption)?.Value ?? -1;
        if (!string.IsNullOrWhiteSpace(signature)) return $"signature:{signature}:p{personal:X2}:e{equipment:X2}";
        if (rule != null) return $"rule:{rule.RuleId}:p{personal:X2}:e{equipment:X2}:v{valueMode}:s{stackMode}";
        var consumer = item.ConsumerFunctionAddresses.Count == 0
            ? "unknown"
            : string.Join(",", item.ConsumerFunctionAddresses.Select(value => value.ToString("X8", CultureInfo.InvariantCulture)));
        return $"abi:p{personal:X2}:e{equipment:X2}:v{valueMode}:s{stackMode}:phase:{item.TriggerPhase}:consumer:{consumer}";
    }

    private static void QualifyDuplicateNames(IReadOnlyList<LogicalEffectInstance> effects)
    {
        foreach (var group in effects.GroupBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).Where(group => group.Count() > 1))
        {
            foreach (var item in group)
            {
                var channel = item.PersonalChannel?.EffectId is int personal
                    ? $"个人 {personal:X2}"
                    : item.ItemChannel?.EffectId is int equipment ? $"宝物 {equipment:X2}" : item.TriggerPhase;
                item.Name = $"{item.Name}（{channel}）";
            }
        }

        foreach (var group in effects.GroupBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).Where(group => group.Count() > 1))
        {
            var index = 1;
            foreach (var item in group.OrderBy(item => item.TriggerPhase, StringComparer.CurrentCulture).ThenBy(item => item.InstanceId, StringComparer.Ordinal))
            {
                item.Name = $"{item.Name}（{FirstNonEmpty(item.TriggerPhase, "实现")} {index++}）";
            }
        }
    }

    private static void HydratePatchPointBytes(IReadOnlyList<LogicalEffectInstance> effects, ExecutableAnalysisSnapshot executable)
    {
        var image = executable.Bytes;
        foreach (var point in effects.SelectMany(item => item.Parameters).SelectMany(parameter => parameter.PhysicalPatchPoints))
        {
            var offset = executable.VirtualAddressToFileOffset(point.Address);
            if (offset < 0 || offset + point.ByteLength > image.LongLength) continue;
            point.ExpectedOldBytesHex = ToHex(image.AsSpan(checked((int)offset), point.ByteLength).ToArray());
        }
    }

    private static string ResolveInstanceName(
        string implementationName,
        EffectChannelReference? personal,
        EffectChannelReference? item,
        string sourceKind)
    {
        if (!string.IsNullOrWhiteSpace(implementationName)) return implementationName;
        if (personal != null && !personal.Name.StartsWith("未命名", StringComparison.Ordinal)) return personal.Name;
        if (item?.IsEnabled == true && !item.Name.StartsWith("未命名", StringComparison.Ordinal)) return item.Name;
        if (personal != null) return personal.DisplayName;
        if (item?.IsEnabled == true) return item.DisplayName;
        return sourceKind == EffectInstanceSourceKind.Injected ? "未命名注入特技" : "未命名原生特技";
    }

    private static bool IsGenericStubName(string name)
        => string.IsNullOrWhiteSpace(name) || name is "个人/宝物特技判定桩" or "包装特技判定桩" or
            "未知特技桩" or "仅识别到特技核心调用" || name.StartsWith("Wrapper", StringComparison.OrdinalIgnoreCase);

    private static EffectChannelReference CloneChannel(EffectChannelReference source)
        => new()
        {
            Channel = source.Channel,
            EffectId = source.EffectId,
            Name = source.Name,
            DisplayName = source.DisplayName,
            Description = source.Description,
            NameSource = source.NameSource,
            IsEnabled = source.IsEnabled,
            IsConfigured = source.IsConfigured,
            Bindings = source.Bindings.Select(binding => new EffectBindingReference
            {
                Kind = binding.Kind,
                Summary = binding.Summary,
                EffectValue = binding.EffectValue,
                PackageBinding = binding.PackageBinding
            }).ToList(),
            Conflicts = source.Conflicts.ToList()
        };

    private static void AddNameConflict(EffectChannelReference? channel, string implementationName)
    {
        if (channel == null || string.IsNullOrWhiteSpace(implementationName) || channel.Name.StartsWith("未命名", StringComparison.Ordinal) ||
            implementationName.Contains(channel.Name, StringComparison.CurrentCultureIgnoreCase) ||
            channel.Name.Contains(implementationName, StringComparison.CurrentCultureIgnoreCase)) return;
        channel.Conflicts.Add($"注入实现“{implementationName}”与{(channel.Channel == EffectChannelKind.Item ? "宝物" : "人物/兵种")}目录名称“{channel.Name}”不一致。请按当前代码语义复核绑定。");
    }

    private static string BuildStableId(string value)
        => "effect-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16].ToLowerInvariant();

    private static string BuildNaturalLanguage(
        LogicalEffectInstance instance,
        InjectedEffectCandidate candidate,
        EffectSemanticRuleManifest? rule)
    {
        var personal = instance.Parameters.FirstOrDefault(item => item.Role == InjectedEffectParameterRole.Personal)?.Value;
        var equipment = instance.Parameters.FirstOrDefault(item => item.Role == InjectedEffectParameterRole.Equipment)?.Value;
        var personalText = personal.HasValue ? instance.PersonalChannel?.DisplayName ?? $"个人特技 {personal.Value:X2}" : string.Empty;
        var equipmentEnabled = equipment.HasValue && instance.ItemChannel?.IsEnabled != false;
        var equipmentText = equipmentEnabled ? instance.ItemChannel?.DisplayName ?? $"装备特效 {equipment.GetValueOrDefault():X2}" : string.Empty;
        var condition = personal.HasValue || equipmentEnabled
            ? $"单位拥有{personalText}{(personal.HasValue && equipmentEnabled ? "或" : string.Empty)}{equipmentText}时"
            : "满足补丁判定条件时";
        var body = rule?.DescriptionTemplate;
        if (string.IsNullOrWhiteSpace(body))
        {
            body = FirstNonEmpty(candidate.UserReadableDiagnosis, "已恢复特技判定结构，但功能体的实际战斗语义尚未完整解析");
            if (LooksLikeMojibake(body))
            {
                body = "已恢复特技判定结构，但功能体的实际战斗语义尚未完整解析";
            }
        }

        return $"{FirstNonEmpty(instance.TriggerPhase, "相关战斗阶段")}，{condition}，{body.TrimEnd('。')}。";
    }

    private static string ExplainParameter(string role, int value, string meaningKind, EffectSemanticRuleManifest? rule)
        => role switch
        {
            InjectedEffectParameterRole.Personal => $"个人特技编号 {value:X2}（十进制 {value}）",
            InjectedEffectParameterRole.Equipment => $"装备或宝物特效编号 {value:X2}（十进制 {value}）",
            InjectedEffectParameterRole.EffectValue => value == 0 ? "读取配置的特效值" : "只判断是否拥有特技，返回 0 或 1",
            InjectedEffectParameterRole.BooleanOption when value == 0 => "个人与装备渠道的特效值允许相加",
            InjectedEffectParameterRole.BooleanOption when value == 1 => "个人与装备渠道不可叠加，只取一个渠道",
            InjectedEffectParameterRole.BooleanOption when value == 2 => "优先装备渠道，未命中时回退个人渠道",
            InjectedEffectParameterRole.Range => $"范围编号 {value}" + (value == 0 ? "，通常表示四方向" : value == 1 ? "，通常表示八方向" : string.Empty),
            _ when meaningKind == EffectParameterMeaningKind.Percentage => $"按 {value}% 解释" + AppendRule(rule),
            _ when meaningKind == EffectParameterMeaningKind.Probability => $"触发概率为 {value}%" + AppendRule(rule),
            _ when meaningKind == EffectParameterMeaningKind.Count => $"每个触发周期最多 {value} 次" + AppendRule(rule),
            _ when meaningKind == EffectParameterMeaningKind.Multiplier => $"倍率参数为 {value}" + AppendRule(rule),
            _ when meaningKind == EffectParameterMeaningKind.Segmented => $"分段值 {value}：{rule?.ValueRule}".TrimEnd('：'),
            _ => $"当前值 {value}" + AppendRule(rule)
        };

    private static string AppendRule(EffectSemanticRuleManifest? rule)
        => string.IsNullOrWhiteSpace(rule?.ValueRule) ? string.Empty : "；" + rule.ValueRule;

    private static string FormatParameterValue(LogicalEffectParameter parameter)
        => parameter.MeaningKind switch
        {
            EffectParameterMeaningKind.Percentage or EffectParameterMeaningKind.Probability => parameter.Value + "%",
            EffectParameterMeaningKind.Count => parameter.Value + " 次",
            _ when parameter.Role is InjectedEffectParameterRole.Personal or InjectedEffectParameterRole.Equipment => $"{parameter.Value:X2}",
            _ => parameter.Value?.ToString(CultureInfo.InvariantCulture) ?? "未知"
        };

    private static string GetMeaningKind(string role, EffectSemanticRuleManifest? rule)
    {
        if (role == InjectedEffectParameterRole.Personal || role == InjectedEffectParameterRole.Equipment) return EffectParameterMeaningKind.Identifier;
        if (role == InjectedEffectParameterRole.BooleanOption) return EffectParameterMeaningKind.Switch;
        if (role == InjectedEffectParameterRole.Range) return EffectParameterMeaningKind.Range;
        if (role == InjectedEffectParameterRole.EffectValue) return EffectParameterMeaningKind.Switch;
        return rule?.ValueMeaningKind ?? EffectParameterMeaningKind.Unknown;
    }

    private static string GetUnit(string meaningKind, EffectSemanticRuleManifest? rule)
        => !string.IsNullOrWhiteSpace(rule?.ValueUnit) ? rule.ValueUnit : meaningKind switch
        {
            EffectParameterMeaningKind.Percentage or EffectParameterMeaningKind.Probability => "%",
            EffectParameterMeaningKind.Count => "次",
            _ => string.Empty
        };

    private static (int? Minimum, int? Maximum) GetSafeRange(string role, string meaningKind)
        => role switch
        {
            InjectedEffectParameterRole.Personal or InjectedEffectParameterRole.Equipment => (0, 255),
            InjectedEffectParameterRole.EffectValue => (0, 1),
            InjectedEffectParameterRole.BooleanOption => (0, 2),
            InjectedEffectParameterRole.Range => (0, 255),
            _ when meaningKind is EffectParameterMeaningKind.Percentage or EffectParameterMeaningKind.Probability => (0, 100),
            _ => (0, 255)
        };

    private static EffectSemanticRuleManifest? FindRule(string name, IReadOnlyList<EffectSemanticRuleManifest> rules)
        => rules.FirstOrDefault(rule => rule.NamePatterns.Any(pattern => name.Contains(pattern, StringComparison.OrdinalIgnoreCase)));

    private static string InferTriggerPhase(InjectedEffectCandidate candidate)
    {
        var text = candidate.Name + " " + candidate.HookPoint + " " + candidate.UserReadableDiagnosis;
        if (text.Contains("策略", StringComparison.OrdinalIgnoreCase)) return "策略伤害阶段";
        if (text.Contains("攻击", StringComparison.OrdinalIgnoreCase)) return "攻击结算阶段";
        if (text.Contains("回合", StringComparison.OrdinalIgnoreCase)) return "回合阶段";
        return "引擎特技判定阶段";
    }

    private static string TranslateRole(string role, string fallback)
        => role switch
        {
            InjectedEffectParameterRole.Personal => "个人特技号",
            InjectedEffectParameterRole.Equipment => "装备特效号",
            InjectedEffectParameterRole.EffectValue => "效果值模式",
            InjectedEffectParameterRole.BooleanOption => fallback.Contains("说话", StringComparison.Ordinal) ? "是否显示台词" : "叠加方式",
            InjectedEffectParameterRole.Range => "生效范围",
            InjectedEffectParameterRole.MessageText => "提示文字",
            _ => FirstNonEmpty(fallback, "参数")
        };

    private static int ParameterOrder(string role) => role switch
    {
        InjectedEffectParameterRole.Personal => 0,
        InjectedEffectParameterRole.Equipment => 1,
        InjectedEffectParameterRole.EffectValue => 2,
        InjectedEffectParameterRole.BooleanOption => 3,
        _ => 4
    };

    private static string BuildNativeName(InjectedEffectCandidate candidate)
        => $"原生特技 {candidate.PersonalEffectId:X2}/{candidate.EquipmentEffectId:X2}";

    private static string BuildCandidateKey(InjectedEffectCandidate candidate)
        => $"{candidate.Address:X8}:{candidate.CodeCaveEntryAddress:X8}:{candidate.PersonalEffectId:X2}:{candidate.EquipmentEffectId:X2}";

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static bool LooksLikeMojibake(string value)
        => value.IndexOfAny(['鏈', '妗', '鍙', '璇', '鏁', '鐗', '鎶', '鍊', '缁', '瑁']) >= 0;

    private static byte[] EncodeLittleEndian(int value, int length)
    {
        var bytes = BitConverter.GetBytes(value);
        return bytes.Take(length).ToArray();
    }

    private static string ToHex(byte[] bytes) => BitConverter.ToString(bytes).Replace('-', ' ');
    private static string SanitizeId(string value) => new(value.Where(char.IsLetterOrDigit).Take(24).ToArray());

    private static List<EffectSemanticRuleManifest> BuildSemanticRules()
        =>
        [
            Rule("random-status", ["噬心毒咒", "随机状态"], "策略伤害结算后", "按特效值百分比遍历受击目标并施加一种随机异常状态", EffectParameterMeaningKind.Probability, "%", "0 到 100 表示触发概率", "single-hook", ["EffectValue", "Personal", "Equipment"]),
            Rule("massacre", ["大杀四方"], "击杀结算后", "击杀敌人后再次行动，每回合最多触发特效值指定的次数", EffectParameterMeaningKind.Count, "次", "特效值是每回合触发上限且不可叠加", "complex-multi-hook", ["Personal", "Equipment"]),
            Rule("guard", ["护卫"], "攻击目标确定阶段", "九宫范围内友军被攻击时把攻击目标改为护卫者；围攻等不满足触发时机的攻击不触发", EffectParameterMeaningKind.Range, "", "范围 0 通常为四方向，1 通常为八方向；特效值不等于 1 时还要求护卫者位于敌人攻击范围", "complex-multi-hook", ["Personal", "Equipment", "Range", "BooleanOption"]),
            Rule("mp-recover", ["回MP攻击", "回mp攻击"], "物理攻击伤害结算后", "回复当前伤害乘特效值所得到的 MP", EffectParameterMeaningKind.Multiplier, "倍率", "特效值作为回复倍率，样本标记为不可叠加", "single-hook", ["Personal", "Equipment"]),
            Rule("strategy-charge", ["策略冲锋"], "策略伤害计算阶段", "伤害增加量由攻击前移动步数乘特效值百分比决定", EffectParameterMeaningKind.Percentage, "%", "特效值 K 作为每步增伤百分比", "single-hook", ["Personal", "Equipment"]),
            Rule("strategy-group", ["聚势伐谋"], "策略伤害计算阶段", "伤害增加量由目标周围同阵营人数加一后乘特效值百分比决定", EffectParameterMeaningKind.Percentage, "%", "特效值 K 作为人数系数百分比", "single-hook", ["Personal", "Equipment"]),
            Rule("enemy-tide", ["敌潮逆噬"], "能力或伤害计算阶段", "根据持有者周围敌军数量按特效值百分比削弱相关能力或伤害", EffectParameterMeaningKind.Percentage, "%", "按九宫距离统计敌军，特效值 K 为每名敌军的百分比系数", "single-hook", ["Personal", "Equipment"]),
            Rule("strategy-floor-cap", ["策略保底", "策略限伤"], "策略伤害计算阶段", "对策略伤害应用保底或上限，并在已满足阈值时应用样本规定的额外增减伤", EffectParameterMeaningKind.Segmented, "", "16 到 30 按百分比解释，其他值按固定伤害解释", "multi-check", ["Personal", "Equipment"]),
            Rule("strategy-money", ["策略偷钱"], "策略伤害结算后", "根据本次策略伤害增减金钱", EffectParameterMeaningKind.Switch, "", "纯开关型特技", "single-hook", ["Personal", "Equipment"]),
            Rule("many-allies", ["多多益善"], "攻击范围计算阶段", "周围每多一名友军增加一格攻击范围，达到该类范围上限后停止", EffectParameterMeaningKind.Switch, "", "纯开关，可与远距攻击叠加", "single-hook", ["Personal", "Equipment"]),
            Rule("zombie", ["殭屍大法", "僵尸大法"], "MP 防御结算阶段", "拥有 MP 防御时限制本次 MP 损失不超过当前 HP", EffectParameterMeaningKind.Switch, "", "纯开关型特技", "single-hook", ["Personal", "Equipment"]),
            Rule("ignore-reduction", ["无视策略减伤"], "策略伤害计算阶段", "忽略减少策略伤害的效果", EffectParameterMeaningKind.Switch, "", "纯开关型特技", "multi-hook", ["Personal", "Equipment"]),
            Rule("pierce", ["强化攻击穿透"], "攻击穿透计算阶段", "按补丁内的穿透映射规则强化攻击穿透", EffectParameterMeaningKind.Switch, "", "纯开关；组合参数语义仍需复核", "single-hook", []),
            Rule("int-4003", ["整形4003", "整型4003"], "伤害计算阶段", "由剧本整型变量 4003 控制保底伤害", EffectParameterMeaningKind.FixedValue, "点", "属于引擎扩展，不是普通个人或装备特技", "engine-extension", []),
            Rule("message-29", ["信息传送29"], "剧本信息传送阶段", "根据大兵种号选择特殊 S 形象", EffectParameterMeaningKind.Unknown, "", "属于函数指针扩展，不是普通个人或装备特技", "engine-extension", []),
            Rule("attribute-down", ["盛气凌人"], "暴击结算阶段", "暴击时使目标六项属性各下降一档", EffectParameterMeaningKind.Switch, "", "读取战斗上下文中的暴击标志", "single-hook", ["Personal", "Equipment"])
        ];

    private static EffectSemanticRuleManifest Rule(
        string id,
        string[] names,
        string phase,
        string description,
        string kind,
        string unit,
        string valueRule,
        string complexity,
        string[] editableRoles)
        => new()
        {
            RuleId = id,
            NamePatterns = names.ToList(),
            TriggerPhase = phase,
            DescriptionTemplate = description,
            ValueMeaningKind = kind,
            ValueUnit = unit,
            ValueRule = valueRule,
            Complexity = complexity,
            SafelyEditableRoles = editableRoles.ToList()
        };
}
