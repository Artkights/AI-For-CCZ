using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class NativeEffectConfigurationService
{
    private readonly HexTableParser _parser = new();
    private static readonly JsonSerializerOptions CatalogJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };
    private static readonly ConcurrentDictionary<string, Lazy<Task<NativeEffectDefinition>>> ReadCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, long> ReadCacheAccessOrder = new(StringComparer.OrdinalIgnoreCase);
    private static long _readCacheAccessSequence;

    public NativeEffectDefinition Read(CczProject project, string channel, int effectId)
        => ReadAsync(project, channel, effectId).GetAwaiter().GetResult();

    public async Task<NativeEffectDefinition> ReadAsync(CczProject project, string channel, int effectId, CancellationToken cancellationToken = default)
    {
        ValidateChannelAndId(channel, effectId);
        var fingerprint = new UnifiedEffectCatalogService().BuildFingerprint(project);
        var key = string.Join("|", ProjectPatchIdentityService.NormalizePath(project.GameRoot), channel, effectId, fingerprint);
        var candidate = new Lazy<Task<NativeEffectDefinition>>(
            () => ReadCoreAsync(project, channel, effectId, CancellationToken.None), LazyThreadSafetyMode.ExecutionAndPublication);
        var lazy = ReadCache.GetOrAdd(key, candidate);
        ReadCacheAccessOrder[key] = Interlocked.Increment(ref _readCacheAccessSequence);
        var miss = ReferenceEquals(lazy, candidate);
        try
        {
            var result = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            PerformanceMetrics.Increment(miss ? "NativeEffectRead.CacheMisses" : "NativeEffectRead.CacheHits");
            var prefix = ProjectPatchIdentityService.NormalizePath(project.GameRoot) + "|" + channel + "|" + effectId + "|";
            foreach (var obsolete in ReadCacheAccessOrder.Where(item => item.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(item => item.Value).Skip(2).Select(item => item.Key).ToArray())
            {
                ReadCache.TryRemove(obsolete, out _);
                ReadCacheAccessOrder.TryRemove(obsolete, out _);
            }
            return result;
        }
        catch
        {
            if (lazy.IsValueCreated && lazy.Value.IsFaulted)
            {
                ReadCache.TryRemove(new KeyValuePair<string, Lazy<Task<NativeEffectDefinition>>>(key, lazy));
                ReadCacheAccessOrder.TryRemove(key, out _);
            }
            throw;
        }
    }

    private async Task<NativeEffectDefinition> ReadCoreAsync(CczProject project, string channel, int effectId, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        ValidateChannelAndId(channel, effectId);
        var executableTask = ExecutableAnalysisSnapshotCache.Shared.GetBaseAsync(project, cancellationToken: cancellationToken);
        var tablesTask = Task.Run(() => _parser.Load(project.HexTableXmlPath), cancellationToken);
        await Task.WhenAll(executableTask, tablesTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var executable = await executableTask.ConfigureAwait(false);
        var tables = await tablesTask.ConfigureAwait(false);
        var catalog = await Task.Run(() => new UnifiedEffectCatalogService().Build(project, tables), cancellationToken).ConfigureAwait(false);
        var reference = channel == CompositeEffectChannel.Item
            ? catalog.ResolveItem(effectId)
            : catalog.ResolvePersonalJob(effectId);
        var inventory = await Task.Run(() => new EffectInventoryService().Scan(project), cancellationToken).ConfigureAwait(false);
        var instances = inventory.Effects.Where(effect =>
                channel == CompositeEffectChannel.Item
                    ? effect.ItemChannel?.EffectId == effectId
                    : effect.PersonalChannel?.EffectId == effectId)
            .ToList();
        var gameName = ReadNativeName(project, tables, channel, effectId);
        var profile = new EnginePatchProfileService().Build(project);
        var writable = new EffectWritableProfileService().Evaluate(project);
        var sandboxMode = EffectSandboxService.IsSandbox(project);
        var definition = new NativeEffectDefinition
        {
            Channel = channel,
            EffectId = effectId,
            Name = reference.Name,
            Description = ReadNativeDescription(project, tables, channel, effectId),
            GameName = gameName,
            CatalogName = reference.Name,
            HasNativeNameSlot = channel == CompositeEffectChannel.PersonalJob ? effectId <= 0xFE : effectId is >= 0x1A and <= 0x7F,
            IsWritable = ((profile.IsKnown && profile.EngineVersion == "6.5" && writable.CanWrite) || sandboxMode) &&
                         (channel == CompositeEffectChannel.PersonalJob ? effectId <= 0xFE : effectId is >= 0x1A and <= 0x7F),
            EditabilityReasonZh = (profile.IsKnown && profile.EngineVersion == "6.5" && writable.CanWrite) || sandboxMode
                ? channel == CompositeEffectChannel.PersonalJob && effectId == 0xFF
                    ? "个人编号 FF 没有原生名称槽，只能维护项目侧别名。"
                    : channel == CompositeEffectChannel.Item && effectId is < 0x1A or > 0x7F
                        ? "该宝物编号不在 6.5 原生名称池范围内。"
                        : "可生成带当前字节锁的原生配置预览。"
                : writable.ReasonZh,
            Bindings = reference.Bindings.ToList(),
            Stubs = instances.Select(instance => new NativeEffectStubDefinition
            {
                InstanceId = instance.InstanceId,
                DisplayNameZh = instance.Name,
                PersonalEffectId = instance.PersonalChannel?.EffectId,
                ItemEffectId = instance.ItemChannel?.EffectId,
                EffectValueMode = instance.Parameters.FirstOrDefault(item => item.Role == InjectedEffectParameterRole.EffectValue)?.Value,
                StackingMode = instance.Parameters.FirstOrDefault(item => item.Role == InjectedEffectParameterRole.BooleanOption)?.Value,
                CallSites = instance.CoreCalls.ToList(),
                IsWritable = CanWriteStub(project, instance, out _),
                EditabilityReasonZh = instance.CoreCalls.Count == 0
                    ? "没有恢复出直接核心调用点。"
                    : CanWriteStub(project, instance, out var stubReason)
                        ? stubReason
                        : stubReason
            }).ToList()
        };
        var managedAdapters = new ManagedNativeParameterAdapterService().ReadActive(project)
            .Where(adapter => channel == CompositeEffectChannel.Item
                ? adapter.ItemEffectId == effectId
                : adapter.PersonalEffectId == effectId)
            .ToList();
        foreach (var adapter in managedAdapters.Where(adapter => definition.Stubs.All(stub =>
                     !stub.InstanceId.Equals(adapter.OriginalInstanceId, StringComparison.OrdinalIgnoreCase))))
        {
            definition.Stubs.Add(new NativeEffectStubDefinition
            {
                InstanceId = adapter.OriginalInstanceId,
                DisplayNameZh = adapter.OriginalDisplayNameZh,
                PersonalEffectId = adapter.PersonalEffectId,
                ItemEffectId = adapter.ItemEffectId,
                EffectValueMode = adapter.EffectValueMode,
                StackingMode = adapter.StackingMode,
                CallSites = adapter.CallSites.ToList(),
                IsWritable = true,
                EditabilityReasonZh = "已安装受管理参数适配器，后续修改只更新版本化参数块。"
            });
        }
        if (channel == CompositeEffectChannel.PersonalJob)
            definition.ExtendedBindingCapability = new ExtendedPersonalJobBindingService().ReadCapability(project);
        definition.FieldCapabilities = BuildFieldCapabilities(project, definition);
        definition.AnalysisFingerprint = $"{executable.Fingerprint.Length}:{executable.Fingerprint.LastWriteTimeUtcTicks}:{executable.Fingerprint.ChangeGeneration}";
        definition.CacheState = "SharedSnapshot";
        definition.CompletedStages = ["读取表", "分析 EXE", "构建位置索引", "检查写入契约"];
        definition.ProfileAudit = writable.ProfileAudit;
        definition.Performance["TotalMilliseconds"] = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
        return definition;
    }

    public NativeEffectEditPreview Preview(CczProject project, NativeEffectEditDraft draft)
    {
        ValidateChannelAndId(draft.Channel, draft.EffectId);
        var result = new NativeEffectEditPreview();
        var profile = new EnginePatchProfileService().Build(project);
        var currentDefinition = Read(project, draft.Channel, draft.EffectId);
        var package = new EffectPackage
        {
            SchemaVersion = "1.0",
            PackageId = $"native-effect-{draft.Channel.ToLowerInvariant()}-{draft.EffectId:X2}-{DateTime.Now:yyyyMMddHHmmssfff}",
            Domain = "patch",
            EffectId = draft.EffectId,
            Name = draft.Name ?? currentDefinition.CatalogName,
            Description = draft.Description ?? currentDefinition.Description,
            Metadata =
            {
                ["LogicalPatchKind"] = "native-effect-update",
                ["EngineProfileSha256"] = profile.ExeSha256,
                ["ProfileAuditHash"] = currentDefinition.ProfileAudit.AuditSummaryHash,
                ["Channel"] = draft.Channel,
                ["EffectWriteRunMode"] = EffectSandboxService.IsSandbox(project)
                    ? EffectWriteRunMode.SandboxValidation
                    : draft.RunMode
            }
        };
        result.Package = package;
        result.FieldResults = currentDefinition.FieldCapabilities;
        var writable = new EffectWritableProfileService().Evaluate(project);
        var sandboxValidation = EffectSandboxService.IsSandbox(project) &&
                                package.Metadata["EffectWriteRunMode"] == EffectWriteRunMode.SandboxValidation;
        if ((!profile.IsKnown || profile.EngineVersion != "6.5" || !writable.CanWrite) && !sandboxValidation)
        {
            result.WarningsZh.Add("当前 EXE 不允许修改原生特效：" + writable.ReasonZh);
            result.SummaryZh = "原生特效预览未通过。";
            return result;
        }

        var tables = _parser.Load(project.HexTableXmlPath);
        IReadOnlyList<EffectPackageBinding> requestedBindings;
        try { requestedBindings = ResolveRequestedBindings(currentDefinition, draft); }
        catch (Exception ex) { result.WarningsZh.Add(ex.Message); requestedBindings = draft.Bindings; }
        IReadOnlyList<EffectPackageBinding> effectiveBindings = requestedBindings;
        if (draft.Channel == CompositeEffectChannel.PersonalJob && requestedBindings.Count > 0)
        {
            result.ExtendedBindingPreview = new ExtendedPersonalJobBindingService().Preview(project, draft.EffectId, requestedBindings);
            if (!result.ExtendedBindingPreview.CanApply)
                result.WarningsZh.AddRange(result.ExtendedBindingPreview.WarningsZh);
            var activeAssignments = requestedBindings.Where(item =>
                    (item.Kind ?? string.Empty).Equals("job_assignment", StringComparison.OrdinalIgnoreCase) && !item.Remove)
                .ToList();
            if (activeAssignments.Count > 0 && result.ExtendedBindingPreview.NativeBinding != null)
            {
                effectiveBindings = requestedBindings.Except(activeAssignments)
                    .Append(result.ExtendedBindingPreview.NativeBinding)
                    .ToList();
            }
        }
        try
        {
            if (draft.Name != null) AddNameSegment(project, tables, draft.Channel, draft.EffectId, draft.Name, package);
            if (draft.Description != null)
            {
                if (draft.Channel == CompositeEffectChannel.Item)
                {
                    if (draft.Name == null)
                    {
                        var current = Read(project, draft.Channel, draft.EffectId);
                        AddItemCatalogSegment(project, draft.EffectId,
                            FirstNonEmpty(current.CatalogName, current.GameName, $"未命名宝物特效 {draft.EffectId:X2}"), draft.Description, package);
                    }
                }
                else AddDescriptionSegment(project, tables, draft.Channel, draft.EffectId, draft.Description, package);
            }
            if (!string.IsNullOrWhiteSpace(draft.InstanceId) &&
                new[] { draft.PersonalEffectId, draft.ItemEffectId, draft.EffectValueMode, draft.StackingMode }.Any(value => value.HasValue))
            {
                AddStubAdapterSegments(project, draft, package);
            }
            if (draft.ReplaceAllBindings || requestedBindings.Count > 0 || draft.EffectValue.HasValue)
            {
                AddBindingSegments(project, tables, draft, package, effectiveBindings);
            }
        }
        catch (Exception ex)
        {
            result.WarningsZh.Add(ex.Message);
        }

        if (package.PatchSegments.Count == 0)
        {
            result.WarningsZh.Add("没有生成可写入的原生配置段。");
        }
        result.PatchPreview = new EffectTransactionalPatchService().Preview(project, package);
        result.WarningsZh.AddRange(result.PatchPreview.Warnings.Select(TranslatePatchWarning));
        result.CanApply = package.PatchSegments.Count > 0 && result.WarningsZh.Count == 0 && result.PatchPreview.CanApply;
        package.Metadata["NativeEffectPreviewPassed"] = result.CanApply ? "true" : "false";
        if (result.CanApply)
            new LockedEffectWriteReceiptService().Issue(project, package, "native-effect");
        result.SummaryZh = result.CanApply
            ? $"原生特效预览通过，将修改 {package.PatchSegments.Count} 个锁定位置。"
            : "原生特效预览未通过：" + string.Join("；", result.WarningsZh.Take(6));
        return result;
    }

    private static IReadOnlyList<EffectPackageBinding> ResolveRequestedBindings(
        NativeEffectDefinition current,
        NativeEffectEditDraft draft)
    {
        var desired = draft.Bindings.Select(CloneBinding).ToList();
        ValidateBindingUniqueness(draft.Channel, desired.Where(item => !item.Remove));
        if (!draft.ReplaceAllBindings) return desired;

        var desiredKeys = desired.Where(item => !item.Remove)
            .Select(item => BindingKey(draft.Channel, item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existing = current.Bindings.Select(item => item.PackageBinding).Where(item => item != null)
            .Select(item => CloneBinding(item!)).ToList();
        foreach (var binding in existing)
        {
            var key = BindingKey(draft.Channel, binding);
            if (desiredKeys.Contains(key) || desired.Any(item => item.Remove && BindingKey(draft.Channel, item).Equals(key, StringComparison.OrdinalIgnoreCase)))
                continue;
            binding.Remove = true;
            binding.Note = "全量替换时自动清除目标列表中省略的旧绑定。";
            desired.Add(binding);
        }
        return desired;
    }

    private static void ValidateBindingUniqueness(string channel, IEnumerable<EffectPackageBinding> bindings)
    {
        var groups = bindings.GroupBy(item => BindingKey(channel, item), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).ToList();
        if (groups.Count == 0) return;
        throw new InvalidOperationException("绑定配置存在重复固定槽或重复物品：" + string.Join("、", groups.Select(item => item.Key)));
    }

    private static string BindingKey(string channel, EffectPackageBinding binding)
    {
        if (channel == CompositeEffectChannel.Item)
        {
            if (!binding.ItemId.HasValue) throw new InvalidOperationException("宝物绑定必须提供物品编号。");
            return "item:" + binding.ItemId.Value.ToString(CultureInfo.InvariantCulture);
        }
        var kind = NormalizeBindingKind(binding.Kind);
        return kind switch
        {
            "job_assignment" or "person_item_1" or "person_item_2" or "set_3" or "set_4" => kind,
            _ => throw new InvalidOperationException("不支持的绑定类型：" + binding.Kind)
        };
    }

    private static string NormalizeBindingKind(string? kind)
        => (kind ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "person_item" or "exclusive" => "person_item_1",
            "three_piece" => "set_3",
            "four_piece" => "set_4",
            var value => value
        };

    private static EffectPackageBinding CloneBinding(EffectPackageBinding source)
        => new()
        {
            Kind = NormalizeBindingKind(source.Kind), RowId = source.RowId, ItemId = source.ItemId,
            PersonId = source.PersonId, PersonId2 = source.PersonId2, PersonId3 = source.PersonId3,
            JobId = source.JobId, ItemId2 = source.ItemId2, ItemId3 = source.ItemId3, ItemId4 = source.ItemId4,
            EffectValue = source.EffectValue, Values = new Dictionary<string, int>(source.Values, StringComparer.Ordinal),
            Note = source.Note, Remove = source.Remove
        };

    private static List<NativeEffectFieldCapability> BuildFieldCapabilities(CczProject project, NativeEffectDefinition definition)
    {
        var index = new EffectIdLocationIndexService().Scan(project, exportReports: false);
        var channel = definition.Channel == CompositeEffectChannel.Item ? EffectChannelKind.Item : EffectChannelKind.PersonalJob;
        var locations = index.Locations.Where(item => item.Channel == channel &&
                                                      (item.EffectId == definition.EffectId ||
                                                       item.OwnerInstanceId.StartsWith($"definition-{definition.EffectId:X2}", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var result = new List<NativeEffectFieldCapability>();
        result.Add(BuildCapability(project, "name", "名称", definition.GameName, locations.Where(item => item.ParameterRoleZh.Contains("名称", StringComparison.Ordinal)), definition.IsWritable, definition.EditabilityReasonZh));
        result.Add(BuildCapability(project, "description", "说明", definition.Description, locations.Where(item => item.ParameterRoleZh.Contains("说明", StringComparison.Ordinal)), definition.IsWritable, definition.EditabilityReasonZh));
        result.Add(BuildCapability(project, "bindings", "绑定配置", $"{definition.Bindings.Count} 项", locations.Where(item => item.Kind == EffectIdLocationKind.NativeTableField || item.ParameterRoleZh.Contains("绑定", StringComparison.Ordinal)), definition.IsWritable, "绑定变更必须整体事务预览。"));
        foreach (var stub in definition.Stubs)
        {
            var owned = index.Locations.Where(item => item.OwnerInstanceId.Equals(stub.InstanceId, StringComparison.OrdinalIgnoreCase)).ToList();
            result.Add(BuildCapability(project, "personal:" + stub.InstanceId, "个人特效号", stub.PersonalEffectId?.ToString("X2") ?? "未解析", owned.Where(item => item.ParameterRole == InjectedEffectParameterRole.Personal), stub.IsWritable, stub.EditabilityReasonZh, stub.CallSites.Count));
            result.Add(BuildCapability(project, "item:" + stub.InstanceId, "宝物特效号", stub.ItemEffectId?.ToString("X2") ?? "未解析", owned.Where(item => item.ParameterRole == InjectedEffectParameterRole.Equipment), stub.IsWritable, stub.EditabilityReasonZh, stub.CallSites.Count));
            result.Add(BuildCapability(project, "value-mode:" + stub.InstanceId, "效果值方式", stub.EffectValueMode?.ToString(CultureInfo.InvariantCulture) ?? "未解析", [], stub.IsWritable, stub.EditabilityReasonZh, stub.CallSites.Count));
            result.Add(BuildCapability(project, "stacking:" + stub.InstanceId, "叠加方式", stub.StackingMode?.ToString(CultureInfo.InvariantCulture) ?? "未解析", [], stub.IsWritable, stub.EditabilityReasonZh, stub.CallSites.Count));
        }
        return result;
    }

    private static NativeEffectFieldCapability BuildCapability(
        CczProject project,
        string fieldId,
        string name,
        string value,
        IEnumerable<EffectIdLocationRecord> locationSource,
        bool profileWritable,
        string reason,
        int affectedConsumers = 0)
    {
        var locations = locationSource.ToList();
        var capability = locations.Select(item => item.WriteCapability).OrderByDescending(CapabilityRank).FirstOrDefault()
                         ?? EffectIdWriteCapability.ReadOnlyVerified;
        var exactLocation = fieldId == "bindings"
            ? locations.Count > 0
            : IsExactLogicalLocationSet(locations, allowFixedWidthText: fieldId is "name" or "description");
        var canEdit = profileWritable && exactLocation &&
                      capability is EffectIdWriteCapability.DirectWritable or EffectIdWriteCapability.AdapterRequired or EffectIdWriteCapability.TransactionWritable;
        var modificationKind = fieldId is "name" or "description" ? EffectModificationKind.NameOrDescription
            : fieldId == "bindings" ? EffectModificationKind.Binding
            : capability == EffectIdWriteCapability.AdapterRequired ? EffectModificationKind.SignedImmediateAdapter
            : EffectModificationKind.DirectEffectId;
        var decision = new EffectWriteDecisionService().Decide(project, new EffectWriteRequest
        {
            ModificationKind = modificationKind,
            SemanticFieldId = fieldId,
            SourceLocationId = string.Join(";", locations.Select(item => item.LocationId).Order(StringComparer.OrdinalIgnoreCase)),
            HasExactLocation = exactLocation,
            HasStaticContract = capability != EffectIdWriteCapability.AdapterRequired || canEdit,
            AffectedConsumers = affectedConsumers,
            RunMode = EffectSandboxService.IsSandbox(project) ? EffectWriteRunMode.SandboxValidation : EffectWriteRunMode.Formal
        });
        canEdit = canEdit && decision.CanEdit;
        return new NativeEffectFieldCapability
        {
            FieldId = fieldId,
            DisplayNameZh = name,
            CurrentValueZh = value,
            WriteCapability = capability,
            WriteCapabilityZh = CapabilityZh(capability),
            CanEdit = canEdit,
            ReasonZh = canEdit ? (capability == EffectIdWriteCapability.AdapterRequired ? "修改时将自动创建安全适配桩。" : reason) : reason,
            AffectedConsumerCount = affectedConsumers,
            LocationIds = locations.Select(item => item.LocationId).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            WriteDecision = decision
        };
    }

    private static bool IsExactLogicalLocationSet(
        IReadOnlyList<EffectIdLocationRecord> locations,
        bool allowFixedWidthText)
    {
        if (locations.Count == 0 ||
            locations.Select(item => item.LocationId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != locations.Count ||
            locations.Any(item => !item.FileOffset.HasValue || item.ByteLength < 1 || !allowFixedWidthText && item.ByteLength > 4 ||
                                  string.IsNullOrWhiteSpace(item.ExpectedOldBytesHex) ||
                                  item.ExpectedOldBytesHex.Length != item.ByteLength * 2))
            return false;
        if (locations.Select(item => item.OwnerInstanceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1 ||
            locations.Select(item => item.ParameterRole).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1 ||
            locations.Select(item => item.CurrentValue).Distinct().Count() != 1)
            return false;
        return locations.GroupBy(item => item.TargetFile, StringComparer.OrdinalIgnoreCase)
            .All(group => group.Select(item => (item.FileOffset!.Value, item.ByteLength)).Distinct().Count() == group.Count());
    }

    private static int CapabilityRank(string value) => value switch
    {
        EffectIdWriteCapability.DirectWritable => 4,
        EffectIdWriteCapability.TransactionWritable => 3,
        EffectIdWriteCapability.AdapterRequired => 2,
        EffectIdWriteCapability.ReadOnlyVerified => 1,
        _ => 0
    };

    private static string CapabilityZh(string value) => value switch
    {
        EffectIdWriteCapability.DirectWritable => "可修改",
        EffectIdWriteCapability.TransactionWritable => "可整体修改",
        EffectIdWriteCapability.AdapterRequired => "需要安全适配",
        EffectIdWriteCapability.ReadOnlyVerified => "只读",
        _ => "尚未完整解析"
    };

    public EffectTransactionalApplyResult Apply(CczProject project, EffectPackage package)
    {
        if (!package.Metadata.TryGetValue("NativeEffectPreviewPassed", out var passed) || passed != "true")
            throw new InvalidOperationException("只能写入由原生特效预览生成并通过校验的事务包。");
        var transaction = new EffectTransactionalPatchService();
        var applied = transaction.Apply(project, package, "native-effect");
        try
        {
            if (package.Metadata.GetValueOrDefault("LogicalPatchKind") == ManagedNativeParameterAdapterService.LogicalPatchKind)
                new ManagedNativeParameterAdapterService().VerifyApplied(project, package);
            EffectInventoryService.Invalidate(project);
            if (package.Metadata.GetValueOrDefault("NativeStubOperation") == "DirectOperandUpdate")
                VerifyDirectStubSemanticReread(project, package);
            return applied;
        }
        catch (Exception verificationError)
        {
            try { transaction.Restore(project, applied); }
            catch (Exception restoreError)
            {
                throw new InvalidOperationException("原生特效写后语义复读失败，且自动恢复不完整。复读原因：" +
                                                    verificationError.Message + "；恢复原因：" + restoreError.Message,
                    verificationError);
            }
            throw new InvalidOperationException("原生特效写后语义复读失败，已恢复全部文件：" + verificationError.Message,
                verificationError);
        }
    }

    private static void VerifyDirectStubSemanticReread(CczProject project, EffectPackage package)
    {
        var calls = package.Metadata.GetValueOrDefault("NativeCallSites", string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0)
            .Where(value => value != 0)
            .ToHashSet();
        var expectedPersonal = int.Parse(package.Metadata["ExpectedPersonalEffectId"], CultureInfo.InvariantCulture);
        var expectedItem = int.Parse(package.Metadata["ExpectedItemEffectId"], CultureInfo.InvariantCulture);
        var expectedStacking = int.Parse(package.Metadata["ExpectedStackingMode"], CultureInfo.InvariantCulture);
        var expectedValueMode = int.Parse(package.Metadata["ExpectedEffectValueMode"], CultureInfo.InvariantCulture);
        var reread = new EffectInventoryService().Scan(project).Effects.FirstOrDefault(instance =>
            calls.Count > 0 && calls.All(call => instance.CoreCalls.Contains(call)) &&
            instance.PersonalChannel?.EffectId == expectedPersonal && instance.ItemChannel?.EffectId == expectedItem &&
            instance.Parameters.FirstOrDefault(parameter => parameter.Role == InjectedEffectParameterRole.BooleanOption)?.Value == expectedStacking &&
            instance.Parameters.FirstOrDefault(parameter => parameter.Role == InjectedEffectParameterRole.EffectValue)?.Value == expectedValueMode);
        if (reread == null)
            throw new InvalidOperationException("写后库存没有在原消费者调用点恢复出新的个人号、宝物号、效果值方式和叠加方式。");
    }

    private static void AddStubAdapterSegments(CczProject project, NativeEffectEditDraft draft, EffectPackage package)
    {
        var managedService = new ManagedNativeParameterAdapterService();
        var existingAdapter = managedService.FindByInstance(project, draft.InstanceId!);
        if (existingAdapter != null)
        {
            var updatedPersonal = ValidateByte(draft.PersonalEffectId ?? existingAdapter.PersonalEffectId, "个人特技号");
            var updatedItem = ValidateByte(draft.ItemEffectId ?? existingAdapter.ItemEffectId, "宝物特效号");
            var updatedStacking = ValidateMode(draft.StackingMode ?? existingAdapter.StackingMode, "叠加方式", 2);
            var updatedValueMode = ValidateMode(draft.EffectValueMode ?? existingAdapter.EffectValueMode, "效果值方式", 1);
            if (updatedPersonal == existingAdapter.OriginalPersonalEffectId && updatedItem == existingAdapter.OriginalItemEffectId &&
                updatedStacking == existingAdapter.OriginalStackingMode && updatedValueMode == existingAdapter.OriginalEffectValueMode)
                managedService.AddRestoreSegments(project, existingAdapter, package);
            else
                managedService.AddUpdateSegments(project, existingAdapter, updatedPersonal, updatedItem, updatedStacking, updatedValueMode, package);
            return;
        }

        var inventory = new EffectInventoryService().Scan(project);
        var instance = inventory.Effects.FirstOrDefault(item => item.InstanceId.Equals(draft.InstanceId, StringComparison.OrdinalIgnoreCase))
                       ?? throw new InvalidOperationException("找不到指定的原生特效实例，请重新扫描。");
        if (!CanWriteStub(project, instance, out var reason))
            throw new InvalidOperationException(reason);

        var currentPersonal = instance.PersonalChannel?.EffectId ?? 0;
        var currentItem = instance.ItemChannel?.EffectId ?? 0;
        var currentStack = instance.Parameters.FirstOrDefault(item => item.Role == InjectedEffectParameterRole.BooleanOption)?.Value ?? 1;
        var currentValueMode = instance.Parameters.FirstOrDefault(item => item.Role == InjectedEffectParameterRole.EffectValue)?.Value ?? 1;
        var personal = ValidateByte(draft.PersonalEffectId ?? currentPersonal, "个人特技号");
        var item = ValidateByte(draft.ItemEffectId ?? currentItem, "宝物特效号");
        var stacking = ValidateMode(draft.StackingMode ?? currentStack, "叠加方式", 2);
        var valueMode = ValidateMode(draft.EffectValueMode ?? currentValueMode, "效果值方式", 1);

        if (stacking == currentStack && valueMode == currentValueMode &&
            TryAddDirectStubParameterSegments(project, instance, currentPersonal, currentItem, personal, item, package))
            return;

        var scanner = new ExeCodeCaveScanner();
        var caveScan = scanner.Scan(project, minimumLength: 80, includeZeroFill: false);
        var engine = new EnginePatchProfileService().Build(project);
        var registry = new CodeCaveRegistry();
        var allocation = registry.Allocate(caveScan, engine, new CodeCaveAllocationRequest
        {
            RequiredBytes = 65,
            ReserveBytes = 8,
            ExistingAllocations = registry.LoadExistingAllocations(project),
            AllocatorPolicy = "smallest-fit"
        });
        if (!allocation.Success || allocation.Allocation == null) throw new InvalidOperationException("没有足够的安全代码洞用于原生参数适配器。");
        var cave = allocation.Allocation.StartVirtualAddress;
        var entry = checked(cave + (uint)ManagedNativeParameterAdapterService.HeaderSize);
        var callTarget = instance.WrapperEntries.Count == 1
            ? instance.WrapperEntries[0]
            : EffectPatchByteService.CoreEffectEngineAddress;
        var code = managedService.BuildPayload(cave, personal, item, stacking, valueMode, callTarget);
        var managed = new ManagedNativeParameterAdapter
        {
            AdapterId = managedService.BuildAdapterId(instance, callTarget),
            OriginalInstanceId = instance.InstanceId,
            OriginalDisplayNameZh = instance.Name,
            PayloadAddress = cave,
            EntryAddress = entry,
            OriginalCallTarget = callTarget,
            CallSites = instance.CoreCalls.Distinct().OrderBy(item => item).ToList(),
            PersonalEffectId = personal,
            ItemEffectId = item,
            StackingMode = stacking,
            EffectValueMode = valueMode,
            OriginalPersonalEffectId = currentPersonal,
            OriginalItemEffectId = currentItem,
            OriginalStackingMode = currentStack,
            OriginalEffectValueMode = currentValueMode
        };
        ManagedNativeParameterAdapterService.AddCommonMetadata(package, managed, personal, item, stacking, valueMode);
        package.Metadata["AdapterOperation"] = "Install";
        var adapterKind = instance.WrapperEntries.Count == 1
            ? EffectModificationKind.WrapperParameter
            : EffectModificationKind.SignedImmediateAdapter;
        var adapterSourceHash = Convert.ToHexString(SHA256.HashData(code));
        package.PatchSegments.Add(new EffectPatchSegment
        {
            TargetFile = "Ekd5.exe", AddressKind = "OdVirtualAddress", Address = cave,
            BytesHex = EffectPatchByteService.ToHex(code), ExpectedOldBytesHex = EffectPatchByteService.ReadVirtualBytes(project, cave, code.Length),
            CodeCaveId = allocation.Allocation.CaveId, HookPoint = "原生桩参数适配器",
            EngineProfileSha256 = engine.ExeSha256, Comment = "CCNA 版本化参数块、只读执行体和原特技判定跳转。",
            SemanticFieldId = "native-adapter-payload:" + instance.InstanceId,
            ModificationKind = adapterKind,
            SourceLocationId = $"managed-adapter:{allocation.Allocation.CaveId}:0x{cave:X8}",
            RequiredCapability = EffectWriteCapability.Adapter,
            AssemblySourceHash = adapterSourceHash
        });
        foreach (var call in instance.CoreCalls.Distinct())
        {
            if (!EffectPatchByteService.IsDirectCallTo(project, call, callTarget)) throw new InvalidOperationException($"调用点 {call:X8} 已变化，请重新扫描。");
            package.PatchSegments.Add(new EffectPatchSegment
            {
                TargetFile = "Ekd5.exe", AddressKind = "OdVirtualAddress", Address = call,
                BytesHex = EffectPatchByteService.ToHex(EffectPatchByteService.BuildRelativeCall(call, entry)),
                ExpectedOldBytesHex = EffectPatchByteService.ReadVirtualBytes(project, call, 5), HookPoint = "原生桩调用重定向",
                EngineProfileSha256 = engine.ExeSha256, Comment = instance.WrapperEntries.Count == 1
                    ? "修改完整 32 位参数后继续进入原包装函数。"
                    : "将直接核心调用重定向到参数适配器。",
                SemanticFieldId = "native-adapter-call:" + instance.InstanceId,
                ModificationKind = adapterKind,
                SourceLocationId = $"managed-adapter-call:0x{call:X8}",
                RequiredCapability = EffectWriteCapability.Adapter,
                AssemblySourceHash = adapterSourceHash
            });
        }
    }

    private static bool TryAddDirectStubParameterSegments(
        CczProject project,
        LogicalEffectInstance instance,
        int currentPersonal,
        int currentItem,
        int personal,
        int item,
        EffectPackage package)
    {
        var changes = new List<(string Role, int Current, int Next, string Name)>
        {
            (InjectedEffectParameterRole.Personal, currentPersonal, personal, "个人/兵种特效号"),
            (InjectedEffectParameterRole.Equipment, currentItem, item, "宝物特效号")
        }.Where(change => change.Current != change.Next).ToList();
        if (changes.Count == 0) return true;

        var index = new EffectIdLocationIndexService().Scan(project, exportReports: false);
        var segments = new List<EffectPatchSegment>();
        foreach (var change in changes)
        {
            var locations = index.Locations.Where(location =>
                    location.OwnerInstanceId.Equals(instance.InstanceId, StringComparison.OrdinalIgnoreCase) &&
                    location.ParameterRole == change.Role && location.CurrentValue == change.Current)
                .ToList();
            if (locations.Count == 0) return false;
            foreach (var location in locations)
            {
                var direct = location.WriteCapability == EffectIdWriteCapability.DirectWritable ||
                             location.WriteCapability == EffectIdWriteCapability.AdapterRequired &&
                             change.Next <= (location.InPlaceMaximum ?? sbyte.MaxValue);
                if (!direct || !location.FileOffset.HasValue || location.ByteLength is < 1 or > 4 ||
                    change.Next > (location.InPlaceMaximum ?? int.MaxValue)) return false;
                var encoded = BitConverter.GetBytes(change.Next).Take(location.ByteLength).ToArray();
                segments.Add(new EffectPatchSegment
                {
                    TargetFile = location.TargetFile,
                    AddressKind = "FileOffset",
                    Address = checked((uint)location.FileOffset.Value),
                    BytesHex = EffectPatchByteService.ToHex(encoded),
                    ExpectedOldBytesHex = location.ExpectedOldBytesHex,
                    EngineProfileSha256 = location.EvidenceExeSha256,
                    HookPoint = "原生桩精确参数",
                    Comment = $"{change.Name}从 {change.Current:X2} 修改为 {change.Next:X2}；位置索引 {location.LocationId}。",
                    SemanticFieldId = $"native-direct-parameter:{instance.InstanceId}:{change.Role}",
                    ModificationKind = EffectModificationKind.DirectEffectId,
                    SourceLocationId = location.LocationId,
                    RequiredCapability = EffectWriteCapability.DirectParameter
                });
            }
        }

        try { new CodeCaveRegistry().EnsureNoPatchSegmentOverlap(segments); }
        catch { return false; }
        package.PatchSegments.AddRange(segments);
        package.Metadata["NativeStubOperation"] = "DirectOperandUpdate";
        package.Metadata["NativeInstanceId"] = instance.InstanceId;
        package.Metadata["NativeCallSites"] = string.Join(",", instance.CoreCalls.Distinct().OrderBy(value => value).Select(value => value.ToString("X8")));
        package.Metadata["ExpectedPersonalEffectId"] = personal.ToString(CultureInfo.InvariantCulture);
        package.Metadata["ExpectedItemEffectId"] = item.ToString(CultureInfo.InvariantCulture);
        package.Metadata["ExpectedStackingMode"] = (instance.Parameters.FirstOrDefault(value => value.Role == InjectedEffectParameterRole.BooleanOption)?.Value ?? 1).ToString(CultureInfo.InvariantCulture);
        package.Metadata["ExpectedEffectValueMode"] = (instance.Parameters.FirstOrDefault(value => value.Role == InjectedEffectParameterRole.EffectValue)?.Value ?? 1).ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private static void AddBindingSegments(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        NativeEffectEditDraft draft,
        EffectPackage package,
        IReadOnlyList<EffectPackageBinding>? effectiveBindings = null)
    {
        var hints = new CczEngineProfileService().Detect(project).TableHints;
        foreach (var binding in effectiveBindings ?? draft.Bindings)
        {
            var kind = (binding.Kind ?? string.Empty).Trim().ToLowerInvariant();
            if (binding.Remove)
            {
                AddBindingRemovalSegments(project, tables, draft, package, binding, kind);
                continue;
            }
            if (draft.Channel == CompositeEffectChannel.Item)
            {
                if (!binding.ItemId.HasValue) throw new InvalidOperationException("宝物绑定必须提供物品编号。");
                var table = ResolveTable(project, tables, binding.ItemId.Value < 104 ? hints.ItemLowTable : hints.ItemHighTable);
                AddNumericField(project, package, table, binding.ItemId.Value, "装备特效号", draft.EffectId, "宝物特效号绑定");
                AddNumericField(project, package, table, binding.ItemId.Value, "装备特效号-效果值",
                    binding.EffectValue ?? draft.EffectValue ?? 0, "宝物特效值绑定");
                continue;
            }

            if (kind == "job_assignment")
            {
                var table = ResolveTable(project, tables, hints.JobEffectAssignmentTable);
                AddOptionalNumeric(project, package, table, draft.EffectId, "1号武将", binding.PersonId, "兵种特效分配");
                AddOptionalNumeric(project, package, table, draft.EffectId, "2号武将", binding.PersonId2, "兵种特效分配");
                AddOptionalNumeric(project, package, table, draft.EffectId, "3号武将", binding.PersonId3, "兵种特效分配");
                AddOptionalNumeric(project, package, table, draft.EffectId, "兵种", binding.JobId, "兵种特效分配");
                AddNumericField(project, package, table, draft.EffectId, "特效值", binding.EffectValue ?? draft.EffectValue ?? 0, "兵种特效值");
                continue;
            }

            var personal = ResolveTable(project, tables, hints.PersonalEffectTable);
            var mapping = kind switch
            {
                "person_item_1" or "person_item" or "exclusive" => new[] { ("武将1", binding.PersonId), ("装备1", binding.ItemId), ("特效值1", binding.EffectValue ?? draft.EffectValue) },
                "person_item_2" => new[] { ("武将2", binding.PersonId), ("装备2", binding.ItemId), ("特效值2", binding.EffectValue ?? draft.EffectValue) },
                "set_3" or "three_piece" => new[] { ("装备3-1", binding.ItemId), ("装备3-2", binding.ItemId2), ("装备3-3", binding.ItemId3), ("特效值3", binding.EffectValue ?? draft.EffectValue) },
                "set_4" or "four_piece" => new[] { ("装备4-1", binding.ItemId), ("装备4-2", binding.ItemId2), ("装备4-3", binding.ItemId3), ("特效值4", binding.EffectValue ?? draft.EffectValue) },
                _ => throw new InvalidOperationException("不支持的人物/套装绑定类型：" + binding.Kind)
            };
            foreach (var (column, value) in mapping) AddOptionalNumeric(project, package, personal, draft.EffectId, column, value, "人物/套装特效绑定");
        }
    }

    private static void AddBindingRemovalSegments(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        NativeEffectEditDraft draft,
        EffectPackage package,
        EffectPackageBinding binding,
        string kind)
    {
        var hints = new CczEngineProfileService().Detect(project).TableHints;
        if (draft.Channel == CompositeEffectChannel.Item)
        {
            if (!binding.ItemId.HasValue) throw new InvalidOperationException("删除宝物绑定必须提供物品编号。 ");
            var table = ResolveTable(project, tables, binding.ItemId.Value < 104 ? hints.ItemLowTable : hints.ItemHighTable);
            AddNumericField(project, package, table, binding.ItemId.Value, "装备特效号", 0, "删除宝物特效绑定");
            AddNumericField(project, package, table, binding.ItemId.Value, "装备特效号-效果值", 0, "清除宝物特效值");
            return;
        }
        if (kind == "job_assignment")
        {
            var table = ResolveTable(project, tables, hints.JobEffectAssignmentTable);
            foreach (var column in new[] { "1号武将", "2号武将", "3号武将" })
                AddNumericField(project, package, table, draft.EffectId, column, 1024, "删除兵种特效分配");
            AddNumericField(project, package, table, draft.EffectId, "兵种", 255, "删除兵种特效分配");
            AddNumericField(project, package, table, draft.EffectId, "特效值", 0, "删除兵种特效分配");
            return;
        }
        var personal = ResolveTable(project, tables, hints.PersonalEffectTable);
        var columns = kind switch
        {
            "person_item_1" or "person_item" or "exclusive" => new[] { "武将1", "装备1", "特效值1" },
            "person_item_2" => new[] { "武将2", "装备2", "特效值2" },
            "set_3" or "three_piece" => new[] { "装备3-1", "装备3-2", "装备3-3", "特效值3" },
            "set_4" or "four_piece" => new[] { "装备4-1", "装备4-2", "装备4-3", "特效值4" },
            _ => throw new InvalidOperationException("不支持删除的绑定类型：" + binding.Kind)
        };
        foreach (var column in columns) AddNumericField(project, package, personal, draft.EffectId, column, 0, "删除人物/套装特效绑定");
    }

    private static void AddOptionalNumeric(CczProject project, EffectPackage package, HexTableDefinition table, int rowId, string column, int? value, string comment)
    {
        if (value.HasValue) AddNumericField(project, package, table, rowId, column, value.Value, comment);
    }

    private static void AddNumericField(CczProject project, EffectPackage package, HexTableDefinition table, int rowId, string column, int value, string comment)
    {
        var rowIndex = rowId - table.BeginId;
        if (rowIndex < 0 || rowIndex >= table.RowCount) throw new InvalidOperationException($"表“{table.TableName}”没有编号 {rowId}。 ");
        var fieldOffset = 0;
        HexFieldDefinition? target = null;
        foreach (var field in table.Fields)
        {
            if (field.ColumnName.Equals(column, StringComparison.Ordinal)) { target = field; break; }
            if (field.ConsumesBytes) fieldOffset += field.Size;
        }
        if (target == null || !target.ConsumesBytes || target.Size is < 1 or > 4)
            throw new InvalidOperationException($"表“{table.TableName}”没有可写数值字段“{column}”。");
        var maximum = target.Size switch { 1 => byte.MaxValue, 2 => ushort.MaxValue, _ => int.MaxValue };
        if (value < 0 || value > maximum) throw new InvalidOperationException($"字段“{column}”的值必须在 0-{maximum} 之间。");
        var encoded = BitConverter.GetBytes(value).Take(target.Size).ToArray();
        var offset = checked(table.DataPos + ((long)rowIndex * table.RowSize) + fieldOffset);
        AddFileSegment(project, package, table.FileName, offset, encoded, comment + "：" + column);
    }

    private static bool CanWriteStub(CczProject project, LogicalEffectInstance instance, out string reason)
    {
        if (instance.CoreCalls.Count == 0)
        {
            reason = "没有恢复出可重定向的调用点。";
            return false;
        }
        if (instance.WrapperEntries.Count == 0)
        {
            reason = "可通过直接核心调用适配器修改完整 32 位参数。";
            return true;
        }
        if (instance.WrapperEntries.Count != 1)
        {
            reason = "包装入口不唯一，不能自动改写参数。";
            return false;
        }
        var mechanism = new EngineEffectMechanismService().Build(project);
        var contract = mechanism.WrapperContracts.FirstOrDefault(item => item.EntryAddress == instance.WrapperEntries[0]);
        if (contract?.IsVerifiedForWrite != true)
        {
            reason = contract?.ReasonZh ?? "包装函数没有当前 SHA 对应的可写契约。";
            return false;
        }
        reason = "包装链和四参数映射唯一，可在保留原包装语义的前提下改写。";
        return true;
    }

    private static void AddNameSegment(CczProject project, IReadOnlyList<HexTableDefinition> tables, string channel, int effectId, string name, EffectPackage package)
    {
        if (channel == CompositeEffectChannel.PersonalJob)
        {
            if (effectId == 0xFF) throw new InvalidOperationException("个人编号 FF 没有原生名称槽。");
            var table = ResolveTable(project, tables, new CczEngineProfileService().Detect(project).TableHints.JobEffectNameTable);
            var encoded = EncodeFixedGbk(name, table.RowSize, table.RowSize - 1, "人物/兵种特效名称");
            var offset = checked(table.DataPos + ((long)(effectId - table.BeginId) * table.RowSize));
            AddFileSegment(project, package, table.FileName, offset, encoded, "人物/兵种特效名称");
            return;
        }
        AddItemNamePoolSegment(project, tables, effectId, name, package);
        AddItemCatalogSegment(project, effectId, name, package.Description, package);
    }

    private static void AddDescriptionSegment(CczProject project, IReadOnlyList<HexTableDefinition> tables, string channel, int effectId, string description, EffectPackage package)
    {
        if (channel == CompositeEffectChannel.Item) return;
        if (effectId == 0xFF) throw new InvalidOperationException("个人编号 FF 没有原生说明槽。");
        var table = ResolveTable(project, tables, new CczEngineProfileService().Detect(project).TableHints.JobEffectDescriptionTable);
        var encoded = EncodeFixedGbk(description, table.RowSize, table.RowSize - 1, "人物/兵种特效说明");
        var offset = checked(table.DataPos + ((long)(effectId - table.BeginId) * table.RowSize));
        AddFileSegment(project, package, table.FileName, offset, encoded, "人物/兵种特效说明");
    }

    private static void AddItemNamePoolSegment(CczProject project, IReadOnlyList<HexTableDefinition> tables, int effectId, string name, EffectPackage package)
    {
        if (effectId is < 0x1A or > 0x7F) throw new InvalidOperationException("6.5 宝物原生名称只能写入 1A-7F。");
        var hints = new CczEngineProfileService().Detect(project).TableHints;
        var table = ResolveTable(project, tables, effectId <= 0x57 ? hints.ItemEffectNameLowTable : hints.ItemEffectNameHighTable);
        var reader = new ItemEffectNameReader();
        var current = reader.ReadNames(project, table).ToDictionary(pair => pair.Key, pair => pair.Value);
        current[effectId] = name.Trim();
        var ids = table.Columns.Select(ParseLeadingHexId).Where(value => value.HasValue).Select(value => value!.Value).ToList();
        if (current.Count != ids.Count)
            throw new InvalidOperationException("宝物原生名称池没有完整解析出全部编号，不能在可能错位的情况下重建。");
        var buffer = new List<byte>();
        foreach (var id in ids)
        {
            var text = current.GetValueOrDefault(id, string.Empty);
            buffer.AddRange(EncodingService.Gbk.GetBytes(text));
            buffer.Add(0);
        }
        if (buffer.Count > table.RowSize) throw new InvalidOperationException($"宝物名称池容量不足：需要 {buffer.Count} 字节，可用 {table.RowSize} 字节。");
        while (buffer.Count < table.RowSize) buffer.Add(0);
        AddFileSegment(project, package, table.FileName, table.DataPos, buffer.ToArray(), "宝物原生名称池");
    }

    private static void AddItemCatalogSegment(CczProject project, int effectId, string name, string description, EffectPackage package)
    {
        var service = new ItemEffectCatalogService();
        var path = service.GetStorePath(project);
        var entries = service.Load(project).Where(item => item.EffectId != effectId).ToList();
        entries.Add(new ItemEffectCatalogEntry { EffectId = effectId, Name = name.Trim(), Description = description.Trim() });
        var next = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entries, CatalogJsonOptions));
        var old = File.Exists(path) ? File.ReadAllBytes(path) : [];
        package.PatchSegments.Add(new EffectPatchSegment
        {
            TargetFile = path, AddressKind = "WholeFile", Address = 0,
            BytesHex = EffectPatchByteService.ToHex(next), ExpectedOldBytesHex = EffectPatchByteService.ToHex(old),
            HookPoint = "宝物特效项目目录", Comment = "同步维护 UTF-8 宝物特效名称与说明。"
        });
    }

    private static string ReadNativeName(CczProject project, IReadOnlyList<HexTableDefinition> tables, string channel, int effectId)
    {
        var hints = new CczEngineProfileService().Detect(project).TableHints;
        if (channel == CompositeEffectChannel.PersonalJob)
        {
            if (effectId == 0xFF) return string.Empty;
            var table = ResolveTable(project, tables, hints.JobEffectNameTable);
            return new JobEffectNameReader().ReadNames(project, table).GetValueOrDefault(effectId, string.Empty);
        }
        if (effectId is < 0x1A or > 0x7F) return string.Empty;
        var itemTable = ResolveTable(project, tables, effectId <= 0x57 ? hints.ItemEffectNameLowTable : hints.ItemEffectNameHighTable);
        return new ItemEffectNameReader().ReadNames(project, itemTable).GetValueOrDefault(effectId, string.Empty);
    }

    private static string ReadNativeDescription(CczProject project, IReadOnlyList<HexTableDefinition> tables, string channel, int effectId)
    {
        if (channel == CompositeEffectChannel.Item)
        {
            return new ItemEffectCatalogService().Load(project)
                .Where(item => item.EffectId == effectId)
                .Select(item => item.Description)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        }
        if (effectId == 0xFF) return string.Empty;
        var table = ResolveTable(project, tables, new CczEngineProfileService().Detect(project).TableHints.JobEffectDescriptionTable);
        var read = new HexTableReader().Read(project, table, tables);
        if (!read.Validation.IsUsable) return string.Empty;
        var row = read.Data.Rows.Cast<System.Data.DataRow>().FirstOrDefault(item =>
            int.TryParse(Convert.ToString(item["ID"], CultureInfo.InvariantCulture), out var id) && id == effectId);
        return row == null || !read.Data.Columns.Contains("介绍")
            ? string.Empty
            : Convert.ToString(row["介绍"], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
    }

    private static void AddFileSegment(CczProject project, EffectPackage package, string targetFile, long offset, byte[] bytes, string comment)
    {
        if (offset > uint.MaxValue) throw new InvalidOperationException("文件偏移超出补丁格式支持范围。");
        package.PatchSegments.Add(new EffectPatchSegment
        {
            TargetFile = targetFile, AddressKind = "FileOffset", Address = checked((uint)offset), AddressHex = $"0x{offset:X}",
            BytesHex = EffectPatchByteService.ToHex(bytes), ExpectedOldBytesHex = EffectPatchByteService.ReadFileBytes(project, targetFile, offset, bytes.Length),
            HookPoint = comment, Comment = comment
        });
    }

    private static byte[] EncodeFixedGbk(string value, int length, int maxPayload, string field)
    {
        var payload = EncodingService.Gbk.GetBytes(value.Trim());
        if (payload.Length > maxPayload) throw new InvalidOperationException($"{field}最多允许 {maxPayload} 个 GBK 字节，当前为 {payload.Length} 字节。");
        var result = new byte[length];
        payload.CopyTo(result, 0);
        return result;
    }

    private static HexTableDefinition ResolveTable(CczProject project, IReadOnlyList<HexTableDefinition> tables, string name)
        => HexTableNameResolver.TryResolveForProject(project, tables, name, out var table)
            ? table
            : throw new InvalidOperationException("找不到原生特效表：" + name);

    private static int? ParseLeadingHexId(string value)
    {
        var token = new string(value.TakeWhile(Uri.IsHexDigit).ToArray());
        return int.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id) ? id : null;
    }

    private static int ValidateByte(int value, string name)
    {
        if (value is < 0 or > 255) throw new InvalidOperationException(name + "必须在 00-FF 之间。");
        return value;
    }

    private static int ValidateMode(int value, string name, int maximum)
    {
        if (value is < 0 || value > maximum) throw new InvalidOperationException($"{name}必须在 0-{maximum} 之间。");
        return value;
    }

    private static void ValidateChannelAndId(string channel, int effectId)
    {
        if (channel is not CompositeEffectChannel.PersonalJob and not CompositeEffectChannel.Item)
            throw new InvalidOperationException("渠道必须是 PersonalJob 或 Item。");
        if (effectId is < 0 or > 255) throw new InvalidOperationException("特效号必须在 00-FF 之间。");
    }

    private static string TranslatePatchWarning(string value)
        => value.Replace("Patch package", "补丁包", StringComparison.OrdinalIgnoreCase)
            .Replace("Patch segment", "补丁段", StringComparison.OrdinalIgnoreCase)
            .Replace("cannot apply", "不能写入", StringComparison.OrdinalIgnoreCase)
            .Replace("expected old bytes", "预期旧字节", StringComparison.OrdinalIgnoreCase)
            .Replace("actual", "实际", StringComparison.OrdinalIgnoreCase);

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
