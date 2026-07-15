using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class CompositeEffectService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public FreeEffectIdReport FindFreeIds(CczProject project, string channel)
        => FindFreeIds(project, channel, new EffectInventoryService().Scan(project));

    public FreeEffectIdReport FindFreeIds(CczProject project, string channel, EffectInventoryReport inventory)
    {
        ValidateChannel(channel);
        var options = channel == CompositeEffectChannel.Item ? inventory.ItemOptions : inventory.PersonalJobOptions;
        var minimum = channel == CompositeEffectChannel.Item ? 0x1A : 0x00;
        var maximum = channel == CompositeEffectChannel.Item ? 0x7F : 0xFE;
        var report = new FreeEffectIdReport { Channel = channel };
        var manifestIds = ReadInstalledCompositeManifests(project)
            .Where(item => item.Draft.Channel == channel)
            .Select(item => item.Draft.EffectId)
            .ToHashSet();
        for (var id = minimum; id <= maximum; id++)
        {
            var reasons = new List<string>();
            var option = options.First(item => item.EffectId == id);
            if (option.Bindings.Count > 0) reasons.Add("已有角色、兵种、套装或物品绑定");
            if (option.IsConfigured) reasons.Add("已在当前配置表中使用");
            var hasDefinitionName = !IsPlaceholderName(option.Name);
            if (hasDefinitionName) reasons.Add("已有原生或项目目录名称");
            var hasCodeReference = inventory.Effects.Any(effect => channel == CompositeEffectChannel.Item
                    ? effect.ItemChannel?.EffectId == id
                    : effect.PersonalChannel?.EffectId == id);
            if (hasCodeReference) reasons.Add("已被原生桩或注入实现引用");
            var hasManifest = manifestIds.Contains(id);
            if (hasManifest) reasons.Add("已有复合特效 manifest");
            if (reasons.Count == 0) report.FreeIds.Add(id);
            else
            {
                report.OccupiedIds.Add(id);
                report.OccupiedReasonsZh[id] = reasons;
                if (hasDefinitionName && option.Bindings.Count == 0 && !option.IsConfigured && !hasCodeReference && !hasManifest)
                {
                    report.ReclaimableIds.Add(id);
                    report.ReclaimableNames[id] = option.Name;
                }
            }
        }
        report.SummaryZh = $"{(channel == CompositeEffectChannel.Item ? "宝物" : "人物/兵种")}渠道空白编号 {report.FreeIds.Count} 个，可回收未引用编号 {report.ReclaimableIds.Count} 个。";
        return report;
    }

    public CompositeEffectDraft Draft(CczProject project, string channel, int? effectId, string name, string description, IEnumerable<string> memberInstanceIds)
    {
        var free = FindFreeIds(project, channel);
        var selected = effectId ?? free.FreeIds.Concat(free.ReclaimableIds).FirstOrDefault(-1);
        var reclaim = free.ReclaimableIds.Contains(selected);
        return new CompositeEffectDraft
        {
            CompositeId = "composite-" + Guid.NewGuid().ToString("N"),
            Channel = channel,
            EffectId = selected,
            Name = name?.Trim() ?? string.Empty,
            Description = description?.Trim() ?? string.Empty,
            AllocationMode = reclaim ? "ReclaimUnusedDefinition" : "Free",
            ReplacedDefinitionName = reclaim ? free.ReclaimableNames.GetValueOrDefault(selected, string.Empty) : string.Empty,
            Members = memberInstanceIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(id => new CompositeEffectMember { InstanceId = id.Trim() }).ToList()
        };
    }

    public CompositeEffectPreview Preview(
        CczProject project,
        CompositeEffectDraft draft,
        IReadOnlyDictionary<string, string>? finalMetadata = null)
    {
        ValidateChannel(draft.Channel);
        var result = new CompositeEffectPreview { Draft = draft };
        var engine = new EnginePatchProfileService().Build(project);
        var mechanism = new EngineEffectMechanismService().Build(project);
        var contract = mechanism.IdentityContracts.FirstOrDefault(item => item.Channel == draft.Channel);
        var writable = new EffectWritableProfileService().Evaluate(project);
        AddStage(result, "project", "项目身份", engine.IsKnown && writable.CanWrite, writable.ReasonZh);
        if (!engine.IsKnown || engine.EngineVersion != "6.5" || !writable.CanWrite) result.WarningsZh.Add("当前 EXE 不允许注入复合特效：" + writable.ReasonZh);
        if (contract?.IsVerified != true) result.WarningsZh.Add(contract?.BlockingReasonZh ?? "当前渠道没有经过验证的复合身份契约。");
        AddStage(result, "identity", "渠道契约", contract?.IsVerified == true, contract?.IsVerified == true ? "当前渠道身份判定已验证。" : contract?.BlockingReasonZh ?? "渠道契约未验证。");
        if (string.IsNullOrWhiteSpace(draft.Name)) result.WarningsZh.Add("必须填写复合特效名称。");
        if (draft.Members.Count < 2) result.WarningsZh.Add("复合特效至少需要两个成员。");
        var bindingWarnings = ValidateBindings(draft.Channel, draft.Bindings);
        result.WarningsZh.AddRange(bindingWarnings);
        AddStage(result, "bindings", "触发来源", bindingWarnings.Count == 0,
            bindingWarnings.Count == 0 ? $"已配置 {draft.Bindings.Count} 项有效绑定。" : string.Join("；", bindingWarnings));
        var freeIds = FindFreeIds(project, draft.Channel);
        if (!freeIds.FreeIds.Contains(draft.EffectId) && !freeIds.ReclaimableIds.Contains(draft.EffectId))
        {
            var reason = freeIds.OccupiedReasonsZh.TryGetValue(draft.EffectId, out var reasons) ? string.Join("、", reasons) : "编号超出允许范围";
            result.WarningsZh.Add($"编号 {draft.EffectId:X2} 不是空闲编号：{reason}。");
        }
        else if (freeIds.ReclaimableIds.Contains(draft.EffectId) &&
                 (draft.AllocationMode != "ReclaimUnusedDefinition" ||
                  !draft.ReplacedDefinitionName.Equals(freeIds.ReclaimableNames[draft.EffectId], StringComparison.CurrentCulture)))
        {
            result.WarningsZh.Add($"编号 {draft.EffectId:X2} 需要明确按“回收未引用定义”分配，并锁定原名称“{freeIds.ReclaimableNames[draft.EffectId]}”。");
        }
        AddStage(result, "effect-id", "编号分配", freeIds.FreeIds.Contains(draft.EffectId) || freeIds.ReclaimableIds.Contains(draft.EffectId),
            freeIds.ReclaimableIds.Contains(draft.EffectId) ? $"将回收未引用定义“{freeIds.ReclaimableNames[draft.EffectId]}”。" : $"编号 {draft.EffectId:X2} 可用。");

        var inventory = new EffectInventoryService().Scan(project);
        foreach (var member in draft.Members)
        {
            result.Members.Add(new CompositeEffectMemberCompatibilityService().Evaluate(project, inventory, member));
        }
        foreach (var member in result.Members.Where(item => !item.IsCompatible)) result.WarningsZh.Add(member.DisplayNameZh + "：" + member.ReasonZh);
        AddStage(result, "members", "成员兼容", result.Members.Count >= 2 && result.Members.All(item => item.IsCompatible),
            $"{result.Members.Count(item => item.IsCompatible)}/{result.Members.Count} 个成员可用。");
        if (result.WarningsZh.Count > 0)
        {
            result.SummaryZh = "复合特效预览未通过：" + string.Join("；", result.WarningsZh.Take(8));
            return result;
        }

        var parameterBytes = BuildParameterBlock(draft, result.Members, result.ParameterBlock);
        var estimatedAdapterBytes = result.Members.Count * 64;
        var registry = new CodeCaveRegistry();
        var allocation = registry.Allocate(new ExeCodeCaveScanner().Scan(project, minimumLength: parameterBytes.Length + estimatedAdapterBytes), engine,
            new CodeCaveAllocationRequest
            {
                RequiredBytes = parameterBytes.Length + estimatedAdapterBytes,
                ReserveBytes = 8,
                ExistingAllocations = registry.LoadExistingAllocations(project),
                AllocatorPolicy = "smallest-fit"
            });
        if (!allocation.Success || allocation.Allocation == null)
        {
            result.WarningsZh.Add("没有足够的安全代码洞容纳复合参数块和成员适配器。");
            result.SummaryZh = "复合特效预览未通过。";
            return result;
        }
        AddStage(result, "code-cave", "代码洞", true, $"已自动分配 {allocation.Allocation.Length} 字节安全空间。");

        var cave = allocation.Allocation.StartVirtualAddress;
        result.ParameterBlock.Address = cave;
        var payload = new List<byte>(parameterBytes);
        var adapterAddresses = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < result.Members.Count; index++)
        {
            var compatibility = result.Members[index];
            var member = draft.Members.First(item => item.InstanceId.Equals(compatibility.InstanceId, StringComparison.OrdinalIgnoreCase));
            var adapterAddress = checked(cave + (uint)payload.Count);
            adapterAddresses[compatibility.InstanceId] = adapterAddress;
            var valueAddress = checked(cave + 8u + (uint)(index * 16) + 4u);
            result.ParameterBlock.Records[index].ValueAddress = valueAddress;
            payload.AddRange(BuildMemberAdapter(adapterAddress, draft, member, compatibility, valueAddress));
        }
        if (payload.Count > allocation.Allocation.Length)
        {
            result.WarningsZh.Add("成员适配器编译后超过已分配代码洞长度。");
            result.SummaryZh = "复合特效预览未通过。";
            return result;
        }

        var package = new EffectPackage
        {
            SchemaVersion = "1.0",
            PackageId = $"composite-effect-{draft.Channel.ToLowerInvariant()}-{draft.EffectId:X2}-{DateTime.Now:yyyyMMddHHmmssfff}",
            Domain = "patch",
            EffectId = draft.EffectId,
            Name = draft.Name,
            Description = draft.Description,
            Bindings = draft.Bindings.ToList(),
            BackupNote = "真实复合特效：参数块、成员适配器和全部调用重定向必须整体写入或整体恢复。",
            Metadata =
            {
                ["LogicalPatchKind"] = "composite-effect",
                ["CompositeId"] = draft.CompositeId,
                ["Channel"] = draft.Channel,
                ["EngineProfileSha256"] = engine.ExeSha256,
                ["AllocatedRange"] = $"0x{cave:X8}-0x{checked(cave + (uint)payload.Count - 1):X8}",
                ["DraftJson"] = JsonSerializer.Serialize(draft, JsonOptions),
                ["CompatibilityJson"] = JsonSerializer.Serialize(result.Members, JsonOptions),
                ["ParameterBlockJson"] = JsonSerializer.Serialize(result.ParameterBlock, JsonOptions)
            }
        };
        if (draft.AllocationMode == "ReclaimUnusedDefinition")
            package.Metadata["ReclaimedDefinition"] = $"{draft.EffectId:X2}:{draft.ReplacedDefinitionName}";
        if (finalMetadata != null)
        {
            foreach (var item in finalMetadata)
            {
                if (item.Key.Equals("CompositePreviewReceipt", StringComparison.OrdinalIgnoreCase) ||
                    item.Key.Equals("CompositePreviewPassed", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("最终元数据不能覆盖复合预览凭据或预览状态。");
                package.Metadata[item.Key] = item.Value;
            }
        }
        var compositeSourceHash = Convert.ToHexString(SHA256.HashData(payload.ToArray()));
        package.PatchSegments.Add(new EffectPatchSegment
        {
            TargetFile = "Ekd5.exe", AddressKind = "OdVirtualAddress", Address = cave,
            BytesHex = EffectPatchByteService.ToHex(payload), ExpectedOldBytesHex = EffectPatchByteService.ReadVirtualBytes(project, cave, payload.Count),
            CodeCaveId = allocation.Allocation.CaveId, HookPoint = "复合参数块与成员适配器",
            EngineProfileSha256 = engine.ExeSha256, Comment = "版本化参数块及成员兼容判定代码。",
            SemanticFieldId = "composite-adapter-payload:" + draft.CompositeId,
            ModificationKind = EffectModificationKind.WrapperParameter,
            SourceLocationId = $"composite-cave:{allocation.Allocation.CaveId}:0x{cave:X8}",
            RequiredCapability = EffectWriteCapability.Adapter,
            AssemblySourceHash = compositeSourceHash
        });
        foreach (var compatibility in result.Members)
        {
            var adapter = adapterAddresses[compatibility.InstanceId];
            foreach (var call in compatibility.RedirectedCallSites)
            {
                package.PatchSegments.Add(new EffectPatchSegment
                {
                    TargetFile = "Ekd5.exe", AddressKind = "OdVirtualAddress", Address = call,
                    BytesHex = EffectPatchByteService.ToHex(EffectPatchByteService.BuildRelativeCall(call, adapter)),
                    ExpectedOldBytesHex = EffectPatchByteService.ReadVirtualBytes(project, call, 5),
                    HookPoint = "复合成员调用重定向", EngineProfileSha256 = engine.ExeSha256,
                    Comment = compatibility.DisplayNameZh + "：原判定未命中时检查复合编号。",
                    SemanticFieldId = "composite-member-call:" + compatibility.InstanceId,
                    ModificationKind = EffectModificationKind.WrapperParameter,
                    SourceLocationId = $"composite-member-call:0x{call:X8}",
                    RequiredCapability = EffectWriteCapability.Adapter,
                    AssemblySourceHash = compositeSourceHash
                });
            }
        }

        var namePreview = new NativeEffectConfigurationService().Preview(project, new NativeEffectEditDraft
        {
            Channel = draft.Channel,
            EffectId = draft.EffectId,
            Name = draft.Name,
            Description = draft.Description,
            Bindings = draft.Bindings.ToList()
        });
        if (!namePreview.CanApply)
        {
            result.WarningsZh.AddRange(namePreview.WarningsZh);
        }
        else
        {
            package.PatchSegments.AddRange(namePreview.Package.PatchSegments);
        }
        AddStage(result, "definition", "名称与绑定", namePreview.CanApply, namePreview.SummaryZh);

        result.Package = package;
        result.PatchPreview = new EffectTransactionalPatchService().Preview(project, package);
        result.WarningsZh.AddRange(result.PatchPreview.Warnings);
        result.CanApply = result.WarningsZh.Count == 0 && result.PatchPreview.CanApply;
        AddStage(result, "transaction", "事务锁", result.PatchPreview.CanApply, result.PatchPreview.Summary);
        package.Metadata["CompositePreviewPassed"] = result.CanApply ? "true" : "false";
        if (result.CanApply)
        {
            result.Receipt = CreateReceipt(project, draft, package);
            package.Metadata["CompositePreviewReceipt"] = JsonSerializer.Serialize(result.Receipt, JsonOptions);
            new LockedEffectWriteReceiptService().Issue(project, package, "composite-effect");
        }
        result.Preflight.CanApply = result.CanApply;
        result.Preflight.SummaryZh = result.CanApply ? "全部检查通过，可以注入。" : "检查未通过，不能注入。";
        result.SummaryZh = result.CanApply
            ? $"复合特效预览通过：编号 {draft.EffectId:X2}，成员 {draft.Members.Count} 个，将修改 {package.PatchSegments.Count} 个锁定位置。"
            : "复合特效预览未通过：" + string.Join("；", result.WarningsZh.Take(8));
        return result;
    }

    public CompositeEffectApplyResult Apply(CczProject project, EffectPackage package)
    {
        if (!package.Metadata.TryGetValue("CompositePreviewPassed", out var passed) || passed != "true")
            throw new InvalidOperationException("只能注入由复合特效预览生成并通过校验的事务包。");
        var beforeSha = EffectPatchByteService.Sha256(project.ResolveGameFile("Ekd5.exe"));
        var transaction = new EffectTransactionalPatchService();
        var apply = transaction.Apply(project, package, "composite-effect");
        string? path = null;
        try
        {
            var manifest = BuildCompositeManifest(package, apply, beforeSha, EffectPatchByteService.Sha256(project.ResolveGameFile("Ekd5.exe")));
            manifest.ProjectIdentity = new ProjectPatchIdentityService().Build(project);
            path = SaveCompositeManifest(project, manifest);
            EffectInventoryService.Invalidate(project);
            var installed = new EffectInventoryService().Scan(project).Effects.SingleOrDefault(item =>
                item.InstanceId.Equals(manifest.Draft.CompositeId, StringComparison.OrdinalIgnoreCase));
            if (installed == null || !installed.HasInjectedImplementation ||
                installed.InstallationStatus != CompositeInstallationStatus.Complete ||
                installed.Parameters.Count != manifest.ParameterBlock.Records.Count)
            {
                throw new InvalidOperationException("写后扫描没有恢复出唯一且完整的复合特效实例。");
            }

            return new CompositeEffectApplyResult
            {
                Applied = true,
                SummaryZh = $"复合特效“{package.Name}”已注入，并通过事务复读和库存重扫校验。",
                ManifestPath = path,
                BackupPaths = apply.BackupPaths
            };
        }
        catch (Exception verificationError)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path);
                transaction.Restore(project, apply);
            }
            catch (Exception restoreError)
            {
                throw new InvalidOperationException(
                    "复合特效写后校验失败，且自动恢复未能完整完成。校验原因：" + verificationError.Message +
                    "；恢复原因：" + restoreError.Message,
                    verificationError);
            }
            throw new InvalidOperationException("复合特效写后校验失败，已自动恢复全部目标文件：" + verificationError.Message, verificationError);
        }
    }

    public CompositeEffectPreflightReport Preflight(CczProject project, CompositeEffectDraft draft)
        => Preview(project, draft).Preflight;

    private static List<string> ValidateBindings(string channel, IReadOnlyList<EffectPackageBinding> bindings)
    {
        var warnings = new List<string>();
        if (bindings.Count == 0)
        {
            warnings.Add(channel == CompositeEffectChannel.Item
                ? "至少绑定一件物品，否则新宝物特效号不会被实机判定。"
                : "至少绑定一名武将、一个兵种、一个人物专属或一组套装，否则新特效号不会被实机判定。");
            return warnings;
        }

        foreach (var binding in bindings)
        {
            var kind = (binding.Kind ?? string.Empty).Trim().ToLowerInvariant();
            if (channel == CompositeEffectChannel.Item)
            {
                if (kind != "item" || !binding.ItemId.HasValue)
                    warnings.Add("宝物渠道只能使用包含物品编号的物品绑定。");
                continue;
            }

            switch (kind)
            {
                case "job_assignment" when !binding.PersonId.HasValue && !binding.PersonId2.HasValue &&
                                                !binding.PersonId3.HasValue && !binding.JobId.HasValue:
                    warnings.Add("武将/兵种分配至少需要一名武将或一个兵种。");
                    break;
                case "person_item_1" or "person_item_2" when !binding.PersonId.HasValue || !binding.ItemId.HasValue:
                    warnings.Add("人物装备专属必须同时提供武将和装备编号。");
                    break;
                case "set_3" when !binding.ItemId.HasValue || !binding.ItemId2.HasValue || !binding.ItemId3.HasValue:
                    warnings.Add("三件套必须提供三个装备编号。");
                    break;
                case "set_4" when !binding.ItemId.HasValue || !binding.ItemId2.HasValue || !binding.ItemId3.HasValue:
                    warnings.Add("四件套必须提供三个装备编号。");
                    break;
                case "job_assignment" or "person_item_1" or "person_item_2" or "set_3" or "set_4":
                    break;
                default:
                    warnings.Add("人物/兵种渠道包含不支持的绑定类型。");
                    break;
            }
        }
        return warnings.Distinct(StringComparer.Ordinal).ToList();
    }

    private static void AddStage(CompositeEffectPreview result, string id, string name, bool passed, string detail)
    {
        result.Preflight.Stages.RemoveAll(item => item.StageId.Equals(id, StringComparison.OrdinalIgnoreCase));
        result.Preflight.Stages.Add(new CompositeEffectPreflightStage { StageId = id, DisplayNameZh = name, Passed = passed, ResultZh = detail });
    }

    private static CompositePreviewReceipt CreateReceipt(CczProject project, CompositeEffectDraft draft, EffectPackage package)
    {
        var identity = new ProjectPatchIdentityService().Build(project);
        return new CompositePreviewReceipt
        {
            ReceiptId = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15),
            ProjectId = identity.ProjectId,
            ExeSha256 = identity.CurrentSha256,
            PackageHash = ComputePackageHash(package),
            EffectIdSnapshot = $"{draft.Channel}:{draft.EffectId:X2}:{draft.AllocationMode}:{draft.ReplacedDefinitionName}",
            AllocatedRange = package.Metadata.GetValueOrDefault("AllocatedRange", string.Empty)
        };
    }

    private static string ComputePackageHash(EffectPackage package)
    {
        var builder = new StringBuilder()
            .Append(package.SchemaVersion).Append('|').Append(package.PackageId).Append('|')
            .Append(package.Domain).Append('|').Append(package.EffectId).Append('|')
            .Append(package.Name).Append('|').Append(package.Description).Append('|');
        foreach (var binding in package.Bindings
                     .OrderBy(item => item.Kind, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.RowId)
                     .ThenBy(item => item.ItemId)
                     .ThenBy(item => item.PersonId)
                     .ThenBy(item => item.JobId))
        {
            builder.Append("binding:").Append(binding.Kind).Append(':')
                .Append(binding.RowId).Append(':').Append(binding.ItemId).Append(':')
                .Append(binding.PersonId).Append(':').Append(binding.PersonId2).Append(':')
                .Append(binding.PersonId3).Append(':').Append(binding.JobId).Append(':')
                .Append(binding.ItemId2).Append(':').Append(binding.ItemId3).Append(':')
                .Append(binding.ItemId4).Append(':').Append(binding.EffectValue).Append(':')
                .Append(binding.Remove).Append(':').Append(binding.Note).Append('|');
            foreach (var value in binding.Values.OrderBy(item => item.Key, StringComparer.Ordinal))
                builder.Append("binding-value:").Append(value.Key).Append(':').Append(value.Value).Append('|');
        }
        foreach (var metadata in package.Metadata
                     .Where(item => !item.Key.Equals("CompositePreviewReceipt", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append("metadata:").Append(metadata.Key).Append(':').Append(metadata.Value).Append('|');
        }
        foreach (var segment in package.PatchSegments.OrderBy(item => item.TargetFile, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Address))
            builder.Append(segment.TargetFile).Append(':').Append(segment.AddressKind).Append(':').Append(segment.Address)
                .Append(':').Append(segment.BytesHex).Append(':').Append(segment.ExpectedOldBytesHex)
                .Append(':').Append(segment.CodeCaveId).Append(':').Append(segment.HookPoint).Append('|');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    public IReadOnlyList<CompositeEffectManifest> List(CczProject project) => ReadInstalledCompositeManifests(project);

    public CompositeEffectManifest Read(CczProject project, string manifestId)
    {
        var normalized = Path.GetFileNameWithoutExtension((manifestId ?? string.Empty).Trim());
        return ReadInstalledCompositeManifests(project).FirstOrDefault(item =>
               item.ManifestId.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
               item.Draft.CompositeId.Equals(normalized, StringComparison.OrdinalIgnoreCase))
           ?? throw new InvalidOperationException("找不到指定的复合特效 manifest。");
    }

    public CompositeEffectMaintenancePreview PreviewUpdate(CczProject project, CompositeEffectMaintenanceDraft draft)
    {
        var manifest = Read(project, draft.ManifestId);
        var result = new CompositeEffectMaintenancePreview { Operation = "Update" };
        var writable = new EffectWritableProfileService().Evaluate(project);
        if (!writable.CanWrite) result.WarningsZh.Add(writable.ReasonZh);
        if (!IsComplete(project, manifest)) result.WarningsZh.Add("复合特效当前安装不完整，必须先修复后才能修改。 ");

        var package = NewMaintenancePackage(project, manifest, "composite-effect-update", "修改" + manifest.Draft.Name);
        foreach (var member in draft.Members)
        {
            var record = manifest.ParameterBlock.Records.FirstOrDefault(item => item.InstanceId.Equals(member.InstanceId, StringComparison.OrdinalIgnoreCase));
            if (record == null) { result.WarningsZh.Add("找不到复合成员：" + member.InstanceId); continue; }
            if (member.EffectValue.HasValue && member.EffectValue.Value != record.EffectValue)
            {
                if (record.ValueAddress == 0) { result.WarningsZh.Add("成员参数没有登记物理地址：" + member.InstanceId); continue; }
                package.PatchSegments.Add(new EffectPatchSegment
                {
                    TargetFile = "Ekd5.exe", AddressKind = "OdVirtualAddress", Address = record.ValueAddress,
                    BytesHex = EffectPatchByteService.ToHex(BitConverter.GetBytes(member.EffectValue.Value)),
                    ExpectedOldBytesHex = EffectPatchByteService.ReadVirtualBytes(project, record.ValueAddress, 4),
                    HookPoint = "复合成员参数", Comment = "更新成员独立效果值。",
                    SemanticFieldId = "composite-managed-value:" + member.InstanceId,
                    ModificationKind = EffectModificationKind.ManagedParameter,
                    SourceLocationId = $"composite-parameter:0x{record.ValueAddress:X8}",
                    RequiredCapability = EffectWriteCapability.DirectParameter
                });
            }
            if (member.EffectValueMode.HasValue && member.EffectValueMode.Value != record.EffectValueMode)
                result.WarningsZh.Add("效果值方式改变会修改适配器控制流，请删除后重新创建复合特效。 ");
            if (member.StackingMode.HasValue && member.StackingMode.Value != record.StackingMode)
                result.WarningsZh.Add("叠加方式改变会修改适配器代码，请删除后重新创建复合特效。 ");
        }

        if (draft.Name != null || draft.Description != null || draft.Bindings.Count > 0)
        {
            if (draft.ReplaceBindings) result.WarningsZh.Add("完整替换绑定必须先在原生配置页清除旧绑定；当前维护事务只追加或修改明确提供的绑定。 ");
            var native = new NativeEffectConfigurationService().Preview(project, new NativeEffectEditDraft
            {
                Channel = manifest.Draft.Channel, EffectId = manifest.Draft.EffectId,
                Name = draft.Name, Description = draft.Description,
                Bindings = draft.Bindings.ToList()
            });
            if (!native.CanApply) result.WarningsZh.AddRange(native.WarningsZh);
            else package.PatchSegments.AddRange(native.Package.PatchSegments);
        }
        package.Metadata["CompositeMaintenanceDraftJson"] = JsonSerializer.Serialize(draft, JsonOptions);
        return FinalizeMaintenancePreview(project, result, package, "复合特效修改预览");
    }

    public CompositeEffectMaintenancePreview PreviewToggle(CczProject project, string manifestId, bool enable)
    {
        var manifest = Read(project, manifestId);
        var result = new CompositeEffectMaintenancePreview { Operation = enable ? "Enable" : "Disable" };
        var writable = new EffectWritableProfileService().Evaluate(project);
        if (!writable.CanWrite) result.WarningsZh.Add(writable.ReasonZh);
        var package = NewMaintenancePackage(project, manifest, enable ? "composite-effect-enable" : "composite-effect-disable",
            (enable ? "启用" : "停用") + manifest.Draft.Name);
        foreach (var segment in manifest.Package.PatchSegments.Where(IsCompositeEntryRedirect))
        {
            package.PatchSegments.Add(new EffectPatchSegment
            {
                TargetFile = segment.TargetFile, AddressKind = segment.AddressKind, Address = segment.Address,
                BytesHex = enable ? segment.BytesHex : segment.ExpectedOldBytesHex,
                ExpectedOldBytesHex = enable ? segment.ExpectedOldBytesHex : segment.BytesHex,
                HookPoint = enable ? "启用复合特效" : "停用复合特效",
                Comment = enable ? "恢复成员入口到复合适配器。" : "只恢复成员入口，保留名称、参数块和绑定。"
            });
        }
        if (package.PatchSegments.Count == 0) result.WarningsZh.Add("安装记录中没有可切换的成员入口。 ");
        return FinalizeMaintenancePreview(project, result, package, enable ? "复合特效启用预览" : "复合特效停用预览");
    }

    public CompositeEffectMaintenancePreview PreviewRepair(CczProject project, string manifestId)
    {
        var manifest = Read(project, manifestId);
        var result = new CompositeEffectMaintenancePreview { Operation = "Repair" };
        var writable = new EffectWritableProfileService().Evaluate(project);
        var package = NewMaintenancePackage(project, manifest, "composite-effect-repair", "修复" + manifest.Draft.Name);
        package.Metadata["RepairBaselineSha256"] = manifest.ExeSha256Before;
        foreach (var segment in manifest.Package.PatchSegments)
        {
            var current = ReadCurrentSegmentBytes(project, segment);
            if (current.Equals(segment.BytesHex, StringComparison.OrdinalIgnoreCase)) continue;
            if (!current.Equals(segment.ExpectedOldBytesHex, StringComparison.OrdinalIgnoreCase))
            {
                result.WarningsZh.Add($"“{segment.HookPoint}”既不是安装字节，也不是安装前字节，属于外部修改，不能自动修复。 ");
                continue;
            }
            package.PatchSegments.Add(new EffectPatchSegment
            {
                TargetFile = segment.TargetFile, AddressKind = segment.AddressKind, Address = segment.Address,
                BytesHex = segment.BytesHex, ExpectedOldBytesHex = current,
                HookPoint = "修复复合特效", Comment = "仅恢复已被还原为安装前内容的锁定位置。"
            });
        }
        var repairTrust = new TrustedEffectRepairService().Evaluate(project, package);
        package.Metadata["TrustedRepairLineage"] = repairTrust.IsTrusted ? "true" : "false";
        package.Metadata["TrustedRepairTransactionManifestId"] = repairTrust.TransactionManifestId;
        if (!writable.CanWrite && !repairTrust.IsTrusted)
            result.WarningsZh.Add(writable.ReasonZh + "；受管修复证明未通过：" + repairTrust.ReasonZh);
        if (package.PatchSegments.Count == 0 && result.WarningsZh.Count == 0) result.WarningsZh.Add("当前复合特效字节完整，不需要修复。 ");
        return FinalizeMaintenancePreview(project, result, package, "复合特效修复预览");
    }

    public EffectTransactionalApplyResult ApplyMaintenance(CczProject project, EffectPackage package)
    {
        if (!package.Metadata.TryGetValue("CompositeMaintenancePreviewPassed", out var passed) || passed != "true")
            throw new InvalidOperationException("复合维护操作必须使用重新预览通过的事务包。 ");
        var operation = package.Metadata.GetValueOrDefault("CompositeMaintenanceOperation") ?? "Update";
        var result = new EffectTransactionalPatchService().Apply(project, package, "composite-effect-" + operation.ToLowerInvariant());
        var manifestId = package.Metadata.GetValueOrDefault("SourceCompositeManifest") ?? string.Empty;
        var manifest = Read(project, manifestId);
        if (operation == "Disable")
        {
            manifest.InstallationStatus = CompositeInstallationStatus.Disabled;
            manifest.StatusZh = "已停用";
        }
        else
        {
            manifest.InstallationStatus = CompositeInstallationStatus.Complete;
            manifest.StatusZh = "已安装";
        }
        if (operation == "Update") ApplyMaintenanceDraftToManifest(manifest, package);
        if (operation is "Update" or "Repair") RefreshManifestBytes(project, manifest);
        else manifest.ExeSha256After = EffectPatchByteService.Sha256(project.ResolveGameFile("Ekd5.exe"));
        manifest.UpdatedAt = DateTime.Now;
        SaveCompositeManifest(project, manifest);
        EffectInventoryService.Invalidate(project);
        return result;
    }

    public EffectPackage PreviewRemove(CczProject project, string manifestId)
    {
        var manifest = Read(project, manifestId);
        var package = new EffectPackage
        {
            PackageId = "remove-" + manifest.ManifestId + "-" + DateTime.Now.ToString("yyyyMMddHHmmssfff"),
            Domain = "patch", EffectId = manifest.Draft.EffectId, Name = "移除" + manifest.Draft.Name,
            Metadata =
            {
                ["LogicalPatchKind"] = "composite-effect-remove",
                ["SourceCompositeManifest"] = manifest.ManifestId,
                ["EngineProfileSha256"] = new EnginePatchProfileService().Build(project).ExeSha256
            }
        };
        foreach (var segment in manifest.Package.PatchSegments)
        {
            var restoreAbsentFile = segment.AddressKind.Equals("WholeFile", StringComparison.OrdinalIgnoreCase) &&
                                    string.IsNullOrWhiteSpace(segment.ExpectedOldBytesHex);
            package.PatchSegments.Add(new EffectPatchSegment
            {
                TargetFile = segment.TargetFile, AddressKind = restoreAbsentFile ? "DeleteFile" : segment.AddressKind, Address = segment.Address,
                AddressHex = segment.AddressHex, BytesHex = segment.ExpectedOldBytesHex, ExpectedOldBytesHex = segment.BytesHex,
                HookPoint = "移除复合特效", Comment = "恢复复合特效安装前的锁定字节。"
            });
        }
        var preview = new EffectTransactionalPatchService().Preview(project, package);
        if (!preview.CanApply) throw new InvalidOperationException("复合特效当前字节与 manifest 不一致，不能自动移除：" + string.Join("；", preview.Warnings.Take(8)));
        package.Metadata["CompositeRemovePreviewPassed"] = "true";
        new LockedEffectWriteReceiptService().Issue(project, package, "composite-effect-remove");
        return package;
    }

    public string ResolveInstallationStatus(CczProject project, CompositeEffectManifest manifest)
    {
        var installed = 0;
        var original = 0;
        var external = 0;
        foreach (var segment in manifest.Package.PatchSegments)
        {
            var current = ReadCurrentSegmentBytes(project, segment);
            if (current.Equals(segment.BytesHex, StringComparison.OrdinalIgnoreCase)) installed++;
            else if (current.Equals(segment.ExpectedOldBytesHex, StringComparison.OrdinalIgnoreCase)) original++;
            else external++;
        }
        if (external > 0) return CompositeInstallationStatus.ExternallyModified;
        if (installed == manifest.Package.PatchSegments.Count) return CompositeInstallationStatus.Complete;
        var entryCount = manifest.Package.PatchSegments.Count(IsCompositeEntryRedirect);
        var originalEntries = manifest.Package.PatchSegments.Where(IsCompositeEntryRedirect)
            .Count(segment => ReadCurrentSegmentBytes(project, segment).Equals(segment.ExpectedOldBytesHex, StringComparison.OrdinalIgnoreCase));
        if (entryCount > 0 && originalEntries == entryCount && installed + originalEntries == manifest.Package.PatchSegments.Count)
            return CompositeInstallationStatus.Disabled;
        if (original > 0 && installed + original == manifest.Package.PatchSegments.Count) return CompositeInstallationStatus.Repairable;
        return CompositeInstallationStatus.Incomplete;
    }

    private static bool IsComplete(CczProject project, CompositeEffectManifest manifest)
        => new CompositeEffectService().ResolveInstallationStatus(project, manifest) == CompositeInstallationStatus.Complete;

    private static EffectPackage NewMaintenancePackage(CczProject project, CompositeEffectManifest manifest, string kind, string name)
        => new()
        {
            SchemaVersion = "2.0", PackageId = kind + "-" + manifest.ManifestId + "-" + DateTime.Now.ToString("yyyyMMddHHmmssfff"),
            Domain = "patch", EffectId = manifest.Draft.EffectId, Name = name,
            Metadata =
            {
                ["LogicalPatchKind"] = kind,
                ["SourceCompositeManifest"] = manifest.ManifestId,
                ["EngineProfileSha256"] = new EnginePatchProfileService().Build(project).ExeSha256
            }
        };

    private static CompositeEffectMaintenancePreview FinalizeMaintenancePreview(
        CczProject project,
        CompositeEffectMaintenancePreview result,
        EffectPackage package,
        string title)
    {
        result.Package = package;
        if (result.WarningsZh.Count == 0)
        {
            result.PatchPreview = new EffectTransactionalPatchService().Preview(project, package);
            result.WarningsZh.AddRange(result.PatchPreview.Warnings);
        }
        result.CanApply = package.PatchSegments.Count > 0 && result.WarningsZh.Count == 0 && result.PatchPreview.CanApply;
        package.Metadata["CompositeMaintenancePreviewPassed"] = result.CanApply ? "true" : "false";
        package.Metadata["CompositeMaintenanceOperation"] = result.Operation;
        if (result.CanApply)
            new LockedEffectWriteReceiptService().Issue(project, package, "composite-effect-" + result.Operation.ToLowerInvariant());
        result.SummaryZh = result.CanApply
            ? $"{title}通过，将修改 {package.PatchSegments.Count} 个锁定位置。"
            : title + "未通过：" + string.Join("；", result.WarningsZh.Take(8));
        return result;
    }

    private static bool IsCompositeEntryRedirect(EffectPatchSegment segment)
        => segment.HookPoint.Equals("复合成员调用重定向", StringComparison.Ordinal);

    private static string ReadCurrentSegmentBytes(CczProject project, EffectPatchSegment segment)
    {
        if (segment.AddressKind.Equals("WholeFile", StringComparison.OrdinalIgnoreCase))
            return File.Exists(project.ResolveGameFile(segment.TargetFile))
                ? EffectPatchByteService.ToHex(File.ReadAllBytes(project.ResolveGameFile(segment.TargetFile)))
                : string.Empty;
        var length = EffectPatchByteService.ParseHex(segment.BytesHex).Length;
        return segment.AddressKind.Equals("FileOffset", StringComparison.OrdinalIgnoreCase)
            ? EffectPatchByteService.ReadFileBytes(project, segment.TargetFile, segment.Address, length)
            : EffectPatchByteService.ReadVirtualBytes(project, segment.Address, length, segment.TargetFile);
    }

    private static void ApplyMaintenanceDraftToManifest(CompositeEffectManifest manifest, EffectPackage package)
    {
        if (!package.Metadata.TryGetValue("CompositeMaintenanceDraftJson", out var json) || string.IsNullOrWhiteSpace(json)) return;
        var draft = JsonSerializer.Deserialize<CompositeEffectMaintenanceDraft>(json, JsonOptions);
        if (draft == null) return;
        if (draft.Name != null) manifest.Draft.Name = draft.Name;
        if (draft.Description != null) manifest.Draft.Description = draft.Description;
        if (draft.Bindings.Count > 0) manifest.Draft.Bindings.AddRange(draft.Bindings);
        foreach (var update in draft.Members)
        {
            var member = manifest.Draft.Members.FirstOrDefault(item => item.InstanceId.Equals(update.InstanceId, StringComparison.OrdinalIgnoreCase));
            var record = manifest.ParameterBlock.Records.FirstOrDefault(item => item.InstanceId.Equals(update.InstanceId, StringComparison.OrdinalIgnoreCase));
            if (member == null || record == null) continue;
            if (update.EffectValue.HasValue) member.EffectValue = record.EffectValue = update.EffectValue.Value;
        }
    }

    private static void RefreshManifestBytes(CczProject project, CompositeEffectManifest manifest)
    {
        foreach (var segment in manifest.Package.PatchSegments)
        {
            var current = ReadCurrentSegmentBytes(project, segment);
            if (!string.IsNullOrWhiteSpace(current)) segment.BytesHex = current;
        }
        manifest.ExeSha256After = EffectPatchByteService.Sha256(project.ResolveGameFile("Ekd5.exe"));
    }

    public EffectTransactionalApplyResult Remove(CczProject project, EffectPackage package)
    {
        if (!package.Metadata.TryGetValue("CompositeRemovePreviewPassed", out var passed) || passed != "true")
            throw new InvalidOperationException("移除操作必须使用重新预览通过的恢复包。");
        var result = new EffectTransactionalPatchService().Apply(project, package, "composite-effect-remove");
        if (package.Metadata.TryGetValue("SourceCompositeManifest", out var id))
        {
            var path = ResolveCompositeManifestPath(project, id);
            if (File.Exists(path))
            {
                var manifest = JsonSerializer.Deserialize<CompositeEffectManifest>(File.ReadAllText(path, Encoding.UTF8), JsonOptions);
                if (manifest != null)
                {
                    manifest.StatusZh = "已移除";
                    SaveCompositeManifest(project, manifest);
                }
            }
        }
        return result;
    }

    private static CompositeEffectCompatibility EvaluateMember(CczProject project, EffectInventoryReport inventory, CompositeEffectMember member)
    {
        var instance = inventory.Effects.FirstOrDefault(item => item.InstanceId.Equals(member.InstanceId, StringComparison.OrdinalIgnoreCase));
        if (instance == null) return new CompositeEffectCompatibility { InstanceId = member.InstanceId, DisplayNameZh = member.InstanceId, ReasonZh = "当前扫描中不存在该成员。" };
        var mechanism = new EngineEffectMechanismService().Build(project);
        var compatibility = new CompositeEffectCompatibility
        {
            InstanceId = instance.InstanceId,
            DisplayNameZh = instance.Name,
            TriggerPhaseZh = instance.TriggerPhase,
            OriginalPersonalEffectId = instance.PersonalChannel?.EffectId,
            OriginalItemEffectId = instance.ItemChannel?.EffectId,
            EffectValueMode = member.EffectValueMode ?? instance.Parameters.FirstOrDefault(item => item.Role == InjectedEffectParameterRole.EffectValue)?.Value ?? 1,
            StackingMode = member.StackingMode ?? instance.Parameters.FirstOrDefault(item => item.Role == InjectedEffectParameterRole.BooleanOption)?.Value ?? 1,
            RedirectedCallSites = instance.CoreCalls.Distinct().ToList(),
            OriginalCallTarget = EffectPatchByteService.CoreEffectEngineAddress
        };
        var wrapper = instance.WrapperEntries.Count == 1
            ? mechanism.WrapperContracts.FirstOrDefault(item => item.EntryAddress == instance.WrapperEntries[0])
            : null;
        var family = mechanism.ComplexFamilyContracts.FirstOrDefault(item => item.IsVerifiedForWrite &&
            (item.NamePatterns.Any(pattern => instance.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase)) ||
             item.SignatureIds.Any(signature => instance.Implementations.Any(implementation =>
                 implementation.SignatureId.Equals(signature, StringComparison.OrdinalIgnoreCase)))));
        if (instance.CoreCalls.Count == 0) compatibility.ReasonZh = "没有恢复出可重定向的判定调用点。";
        else if (instance.PatchCategory == InjectedEffectPatchCategory.FunctionExtensionPatch) compatibility.ReasonZh = "引擎扩展不是普通特效判定，不能作为复合成员。";
        else if (instance.PatchCategory == InjectedEffectPatchCategory.ComplexMultiHookPatch && family == null) compatibility.ReasonZh = "复杂多入口成员尚未形成当前 SHA 对应的家族契约。";
        else if (instance.WrapperEntries.Count > 1) compatibility.ReasonZh = "成员存在多个包装入口，参数映射不唯一。";
        else if (instance.WrapperEntries.Count == 1 && wrapper?.IsVerifiedForWrite != true) compatibility.ReasonZh = wrapper?.ReasonZh ?? "包装函数没有可写契约。";
        else if (compatibility.EffectValueMode is < 0 or > 1 || compatibility.StackingMode is < 0 or > 2) compatibility.ReasonZh = "成员的效果值方式或叠加方式超出已验证范围。";
        else
        {
            compatibility.CompatibilityKind = family != null
                ? EffectMemberCompatibilityKind.VerifiedComplexFamily
                : wrapper != null
                    ? EffectMemberCompatibilityKind.VerifiedWrapper
                    : EffectMemberCompatibilityKind.DirectCoreCall;
            compatibility.ContractId = family?.ContractId ?? wrapper?.ContractId ?? "ccz65-direct-core-call";
            compatibility.OriginalCallTarget = wrapper?.EntryAddress ?? EffectPatchByteService.CoreEffectEngineAddress;
            if (compatibility.RedirectedCallSites.Any(call => !EffectPatchByteService.IsDirectCallTo(project, call, compatibility.OriginalCallTarget)))
            {
                compatibility.ReasonZh = "成员调用点当前目标与可写契约不一致。";
                return compatibility;
            }
            compatibility.IsCompatible = true;
            compatibility.ReasonZh = compatibility.CompatibilityKind switch
            {
                EffectMemberCompatibilityKind.VerifiedWrapper => "包装链和四参数映射已验证，可保留原包装语义生成适配器。",
                EffectMemberCompatibilityKind.VerifiedComplexFamily => "复杂补丁家族、必要入口和判定调用均已验证。",
                _ => "直接核心调用和四参数均已恢复，可生成兼容适配器。"
            };
        }
        return compatibility;
    }

    private static byte[] BuildParameterBlock(CompositeEffectDraft draft, IReadOnlyList<CompositeEffectCompatibility> members, CompositeEffectParameterBlock block)
    {
        var bytes = new List<byte>(Encoding.ASCII.GetBytes("CCZE"));
        bytes.AddRange(BitConverter.GetBytes((ushort)2));
        bytes.AddRange(BitConverter.GetBytes((ushort)members.Count));
        for (var index = 0; index < members.Count; index++)
        {
            var member = draft.Members.First(item => item.InstanceId.Equals(members[index].InstanceId, StringComparison.OrdinalIgnoreCase));
            var value = member.EffectValue ?? (members[index].EffectValueMode == 0 ? 1 : 1);
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(member.InstanceId));
            bytes.AddRange(hash.Take(4));
            bytes.AddRange(BitConverter.GetBytes(value));
            bytes.AddRange(BitConverter.GetBytes(members[index].EffectValueMode));
            bytes.AddRange(BitConverter.GetBytes(members[index].StackingMode));
            var meaningKind = members[index].EffectValueMode == 0 ? EffectParameterMeaningKind.FixedValue : EffectParameterMeaningKind.Switch;
            block.Records.Add(new CompositeEffectParameterRecord
            {
                InstanceId = member.InstanceId, EffectValue = value,
                EffectValueMode = members[index].EffectValueMode, StackingMode = members[index].StackingMode,
                MeaningKind = meaningKind,
                UnitZh = members[index].EffectValueMode == 0 ? "成员配置值" : "开关",
                CompatibilityKind = members[index].CompatibilityKind,
                IntegrityHash = Convert.ToHexString(hash.Take(8).ToArray())
            });
        }
        return bytes.ToArray();
    }

    private static byte[] BuildMemberAdapter(uint address, CompositeEffectDraft draft, CompositeEffectMember member, CompositeEffectCompatibility compatibility, uint valueAddress)
    {
        var bytes = new List<byte> { 0x51 };
        for (var i = 0; i < 4; i++) bytes.AddRange([0xFF, 0x74, 0x24, 0x14]);
        bytes.AddRange(EffectPatchByteService.BuildRelativeCall(checked(address + (uint)bytes.Count), compatibility.OriginalCallTarget));
        bytes.AddRange([0x85, 0xC0, 0x59]);
        var jumpIndex = bytes.Count;
        bytes.AddRange([0x75, 0x00]);
        bytes.Add(0x68); bytes.AddRange(BitConverter.GetBytes(1));
        bytes.Add(0x68); bytes.AddRange(BitConverter.GetBytes(compatibility.StackingMode));
        var fallbackItem = draft.Channel == CompositeEffectChannel.Item ? draft.EffectId : compatibility.OriginalItemEffectId ?? 0;
        var fallbackPersonal = draft.Channel == CompositeEffectChannel.PersonalJob ? draft.EffectId : compatibility.OriginalPersonalEffectId ?? 0;
        bytes.Add(0x68); bytes.AddRange(BitConverter.GetBytes(fallbackItem));
        bytes.Add(0x68); bytes.AddRange(BitConverter.GetBytes(fallbackPersonal));
        bytes.AddRange(EffectPatchByteService.BuildRelativeCall(checked(address + (uint)bytes.Count), EffectPatchByteService.CoreEffectEngineAddress));
        bytes.AddRange([0x85, 0xC0, 0x74, compatibility.EffectValueMode == 0 ? (byte)0x05 : (byte)0x00]);
        if (compatibility.EffectValueMode == 0)
        {
            bytes.Add(0xA1); bytes.AddRange(BitConverter.GetBytes(valueAddress));
        }
        var returnOffset = bytes.Count;
        bytes.AddRange([0xC2, 0x10, 0x00]);
        var delta = returnOffset - (jumpIndex + 2);
        if (delta is < sbyte.MinValue or > sbyte.MaxValue) throw new InvalidOperationException("复合成员适配器条件跳转超出短跳范围。");
        bytes[jumpIndex + 1] = unchecked((byte)(sbyte)delta);
        return bytes.ToArray();
    }

    private static CompositeEffectManifest BuildCompositeManifest(EffectPackage package, EffectTransactionalApplyResult apply, string beforeSha, string afterSha)
    {
        return new CompositeEffectManifest
        {
            ManifestId = package.Metadata.GetValueOrDefault("CompositeId") ?? package.PackageId,
            ExeSha256Before = beforeSha, ExeSha256After = afterSha, EffectManifestPath = apply.ManifestPath,
            Draft = JsonSerializer.Deserialize<CompositeEffectDraft>(package.Metadata["DraftJson"], JsonOptions) ?? new(),
            Members = JsonSerializer.Deserialize<List<CompositeEffectCompatibility>>(package.Metadata["CompatibilityJson"], JsonOptions) ?? [],
            ParameterBlock = JsonSerializer.Deserialize<CompositeEffectParameterBlock>(package.Metadata["ParameterBlockJson"], JsonOptions) ?? new(),
            Package = package, BackupPaths = apply.BackupPaths
        };
    }

    private static string SaveCompositeManifest(CczProject project, CompositeEffectManifest manifest)
    {
        var root = GetCompositeManifestRoot(project);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, manifest.ManifestId + ".json");
        UserBoundSignatureService.Sign(manifest, static (item, value) => item.Signature = value);
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions), Encoding.UTF8);
        return path;
    }

    private static List<CompositeEffectManifest> ReadInstalledCompositeManifests(CczProject project)
    {
        var root = GetCompositeManifestRoot(project);
        if (!Directory.Exists(root)) return [];
        var result = new List<CompositeEffectManifest>();
        foreach (var path in Directory.GetFiles(root, "*.json"))
        {
            try
            {
                var item = JsonSerializer.Deserialize<CompositeEffectManifest>(File.ReadAllText(path, Encoding.UTF8), JsonOptions);
                if (item != null && item.StatusZh != "已移除" &&
                    new ProjectPatchIdentityService().Matches(project, item.ProjectIdentity))
                {
                    result.Add(item);
                }
            }
            catch { }
        }
        return result;
    }

    private static string ResolveCompositeManifestPath(CczProject project, string id)
        => Path.Combine(GetCompositeManifestRoot(project), Path.GetFileNameWithoutExtension(id) + ".json");

    private static string GetCompositeManifestRoot(CczProject project)
        => ProjectPatchIdentityService.CompositeManifestRoot(project);

    private static bool IsPlaceholderName(string name)
        => string.IsNullOrWhiteSpace(name) || name.StartsWith("未命名", StringComparison.Ordinal) || name.StartsWith('#');

    private static void ValidateChannel(string channel)
    {
        if (channel is not CompositeEffectChannel.PersonalJob and not CompositeEffectChannel.Item)
            throw new InvalidOperationException("复合特效渠道必须是 PersonalJob 或 Item。");
    }
}
