using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Text;
using System.Text.Json;

internal partial class Program
{
    static void RunEffectAuthoringSmoke(CczProject project, bool includeWrite)
    {
        var source = ResolveEffectInjectionDiscoverySmokeSourceProject(project);
        var mechanism = new EngineEffectMechanismService().Build(source);
        if (!mechanism.Functions.Any(item => item.Address == 0x004101D9 && item.DisplayNameZh.Contains("判定", StringComparison.Ordinal)) ||
            mechanism.Consumers.Count == 0 ||
            mechanism.IdentityContracts.Count != 2 ||
            !mechanism.IsKnownWritableProfile ||
            mechanism.ExeSha256 != EffectWritableProfileStatus.Current65BaselineSha256 ||
            mechanism.WrapperContracts.All(item => item.EntryAddress != 0x0042518F || !item.IsVerifiedForWrite) ||
            mechanism.ComplexFamilyContracts.Count < 4 ||
            mechanism.IdentityContracts.Any(item => string.IsNullOrWhiteSpace(item.EvidenceZh)) ||
            mechanism.IdentityContracts.First(item => item.Channel == CompositeEffectChannel.Item).IsVerified)
        {
            throw new InvalidOperationException("特效机制档案没有包含核心函数、消费者或双渠道身份契约。");
        }
        RunEffectShaIsolationSmoke(source);
        RunForeignManifestIsolationSmoke(source);
        RunNativeTableAndExtendedBindingSmoke(source);

        var chinese = new EffectChineseDisplayService();
        if (chinese.DetectionLevel("KnownVariant").Contains("Known", StringComparison.OrdinalIgnoreCase) ||
            chinese.ParameterRole(InjectedEffectParameterRole.Personal) != "个人特技号" ||
            chinese.StackingMode(2) != "宝物优先，个人作为备选")
        {
            throw new InvalidOperationException("特效中文显示映射不完整。");
        }

        var composites = new CompositeEffectService();
        var personalFree = composites.FindFreeIds(source, CompositeEffectChannel.PersonalJob);
        var itemFree = composites.FindFreeIds(source, CompositeEffectChannel.Item);
        if (personalFree.FreeIds.Concat(personalFree.ReclaimableIds).Any(id => id == 0xFF) ||
            itemFree.FreeIds.Concat(itemFree.ReclaimableIds).Any(id => id is < 0x1A or > 0x7F))
        {
            throw new InvalidOperationException("复合特效空闲编号范围不符合人物/宝物渠道规则。");
        }

        var inventory = new EffectInventoryService().Scan(source);
        var compatibility = new CompositeEffectMemberCompatibilityService();
        var compatibleMembers = inventory.Effects
            .Where(item => compatibility.Evaluate(source, inventory, new CompositeEffectMember { InstanceId = item.InstanceId }).IsCompatible)
            .Take(2).ToList();
        var allocatable = personalFree.FreeIds.Concat(personalFree.ReclaimableIds).ToList();
        if (allocatable.Count > 0 && compatibleMembers.Count >= 2)
        {
            var draft = composites.Draft(source, CompositeEffectChannel.PersonalJob, allocatable[0], "复合烟测", "只读预览，不写入基底。",
                compatibleMembers.Select(item => item.InstanceId));
            draft.Bindings.Add(new EffectPackageBinding { Kind = "job_assignment", PersonId = 0, EffectValue = 1 });
            var preview = composites.Preview(source, draft);
            if (!preview.CanApply)
                throw new InvalidOperationException("当前 6.5 基底的两成员复合特效预览应当通过：" + preview.SummaryZh);
            if (preview.Package.PatchSegments.Count < 3 || preview.ParameterBlock.Records.Count != 2)
                throw new InvalidOperationException("复合特效预览没有生成参数块、适配器和调用重定向。");
            if (!preview.Preflight.CanApply || preview.Preflight.Stages.Count < 7 || preview.Preflight.Stages.Any(item => !item.Passed) ||
                preview.Receipt == null || string.IsNullOrWhiteSpace(preview.Receipt.PackageHash))
                throw new InvalidOperationException("复合特效预检阶段或短期预览凭据不完整。");
            AssertCompositePayload(preview);
        }
        else throw new InvalidOperationException("当前 6.5 基底没有可分配编号或两个可验证成员，无法完成复合特效烟测。");

        if (includeWrite)
        {
            RunNativeDefinitionWriteRoundTrip(source, personalFree);
            RunManagedNativeAdapterRoundTrip(source);
            RunCompositeWriteRoundTrip(source);
        }
        Console.WriteLine($"EFFECT_AUTHORING_SMOKE_OK functions={mechanism.Functions.Count} consumers={mechanism.Consumers.Count} personalFree={personalFree.FreeIds.Count} personalReclaimable={personalFree.ReclaimableIds.Count} itemFree={itemFree.FreeIds.Count} itemReclaimable={itemFree.ReclaimableIds.Count} write={includeWrite}");
    }

    private static void RunNativeTableAndExtendedBindingSmoke(CczProject source)
    {
        var tables = new CCZModStudio.Formats.HexTableParser().Load(source.HexTableXmlPath);
        var assignments = tables.Single(item => item.TableName == "6.5-7-2 兵种特效分配");
        if (assignments.FileName != "Ekd5.exe" || assignments.DataPos != 888832 ||
            assignments.RowCount != 255 || assignments.RowSize != 8 || assignments.EndOffsetExclusive != 888832 + 255L * 8)
            throw new InvalidOperationException("6.5-7-2 原生三个武将和一个兵种的 255×8 表结构已变化。 ");

        var native = new NativeEffectConfigurationService().Read(source, CompositeEffectChannel.PersonalJob, 0x01);
        var writableFields = native.FieldCapabilities.Where(item =>
            item.FieldId is "name" or "description" or "bindings" ||
            item.FieldId.StartsWith("personal:", StringComparison.OrdinalIgnoreCase) ||
            item.FieldId.StartsWith("item:", StringComparison.OrdinalIgnoreCase)).ToList();
        var derivedOnlyFields = native.FieldCapabilities.Where(item =>
            item.FieldId.StartsWith("value-mode:", StringComparison.OrdinalIgnoreCase) ||
            item.FieldId.StartsWith("stacking:", StringComparison.OrdinalIgnoreCase)).ToList();
        if (native.FieldCapabilities.Count != 7 || writableFields.Count != 5 || writableFields.Any(item => !item.CanEdit) ||
            derivedOnlyFields.Count != 2 || derivedOnlyFields.Any(item => item.CanEdit ||
                !item.WriteDecision.BlockerCodes.Contains("LOCATION_NOT_UNIQUE", StringComparer.Ordinal)))
            throw new InvalidOperationException("原生编号 01 的字段级权限没有按物理位置规则返回。 ");

        var bindings = new List<EffectPackageBinding>
        {
            new() { Kind = "job_assignment", PersonId = 0, PersonId2 = 1, PersonId3 = 2, JobId = 0, EffectValue = 9 },
            new() { Kind = "job_assignment", PersonId = 3, PersonId2 = 4, JobId = 1, EffectValue = 9 }
        };
        var extended = new ExtendedPersonalJobBindingService().Preview(source, 0x01, bindings);
        if (!extended.RequiresExtension || extended.CanApply || extended.NativeBinding == null ||
            extended.NativeBinding.PersonId != 0 || extended.NativeBinding.PersonId2 != 1 || extended.NativeBinding.PersonId3 != 2 ||
            extended.NativeBinding.JobId != 0 || extended.ExtendedEntries.Count != 3 ||
            extended.ExtendedEntries.Count(item => item.SourceKind == ExtendedBindingSourceKind.Character) != 2 ||
            extended.ExtendedEntries.Count(item => item.SourceKind == ExtendedBindingSourceKind.Job) != 1 ||
            extended.ProbePlan.ReadWatchAddresses.Count != 5 || extended.ProbePlan.Scenarios.Count != 4 ||
            extended.ProbePlan.Batches.Count != 4 || extended.ProbePlan.Batches.Any(item => item.X32dbgCommands.Count != 4))
            throw new InvalidOperationException("五武将两兵种没有正确拆分为原生槽和被证据门槛阻断的扩展候选。 ");

        var nativePreview = new NativeEffectConfigurationService().Preview(source, new NativeEffectEditDraft
        {
            Channel = CompositeEffectChannel.PersonalJob,
            EffectId = 0x01,
            Bindings = bindings
        });
        if (nativePreview.CanApply || nativePreview.ExtendedBindingPreview?.RequiresExtension != true ||
            nativePreview.WarningsZh.All(item => !item.Contains("扩展", StringComparison.Ordinal)))
            throw new InvalidOperationException("原生配置服务没有阻止未经动态验证的扩展绑定写入。 ");

        var forged = new EffectContractEvidence
        {
            ContractId = "strategy-damage-formula-v2",
            ExeSha256 = EffectWritableProfileStatus.Current65BaselineSha256,
            HookHitCount = 9,
            CompletedScenariosZh = ["普通场景", "边界场景"],
            SourceTool = "GameDebug MCP",
            CallStackZh = ["伪造调用栈"],
            ObservedSlots =
            {
                ["strategy-effect-subject"] = "fake",
                ["strategy-current-damage"] = "fake",
                ["strategy-target-context"] = "fake"
            },
            ObservedSlotMinimums = { ["strategy-current-damage"] = 1 },
            ObservedSlotMaximums = { ["strategy-current-damage"] = 9999 }
        };
        var forgedImport = new HookExecutionContractService().ImportEvidence(source, forged);
        if (!forgedImport.Accepted || forgedImport.ContractPromoted)
            throw new InvalidOperationException("旧结构化证据应仅作为诊断保存，不能提升执行契约。 ");

        var forgedPackage = new EffectPackage
        {
            PackageId = "forged-preview", Domain = "patch", EffectId = 1,
            Metadata = { ["NativeEffectPreviewPassed"] = "true" }
        };
        forgedPackage.PatchSegments.Add(new EffectPatchSegment
        {
            TargetFile = "Ekd5.exe", AddressKind = "OdVirtualAddress", Address = 0x004101D9,
            BytesHex = EffectPatchByteService.ReadVirtualBytes(source, 0x004101D9, 1),
            ExpectedOldBytesHex = EffectPatchByteService.ReadVirtualBytes(source, 0x004101D9, 1)
        });
        try
        {
            new NativeEffectConfigurationService().Apply(source, forgedPackage);
            throw new InvalidOperationException("伪造 PreviewPassed 标志错误地通过了写入门禁。 ");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("凭据", StringComparison.Ordinal) ||
                                                    ex.Message.Contains("结构化写入授权", StringComparison.Ordinal))
        {
        }
    }

    private static void AssertCompositePayload(CompositeEffectPreview preview)
    {
        var payload = preview.Package.PatchSegments.FirstOrDefault(segment => segment.HookPoint == "复合参数块与成员适配器")
                      ?? throw new InvalidOperationException("复合预览缺少代码洞主体段。");
        var bytes = Convert.FromHexString(payload.BytesHex);
        if (bytes.Length < 8 || Encoding.ASCII.GetString(bytes, 0, 4) != "CCZE" || BitConverter.ToUInt16(bytes, 4) != 2)
            throw new InvalidOperationException("复合参数块缺少 CCZE 版本标记。");
        var memberCount = BitConverter.ToUInt16(bytes, 6);
        var codeOffset = 8 + memberCount * 16;
        var instructions = new X86InstructionScanner().DecodeBlock(bytes[codeOffset..], checked(payload.Address + (uint)codeOffset));
        var coreCalls = instructions.Count(item => item.IsDirectCall && item.BranchTarget == 0x004101D9);
        var returnCount = instructions.Count(item => item.Mnemonic.Equals("ret", StringComparison.OrdinalIgnoreCase) && item.Bytes.SequenceEqual(new byte[] { 0xC2, 0x10, 0x00 }));
        if (coreCalls != memberCount * 2 || returnCount != memberCount ||
            instructions.Count(item => item.Mnemonic.Equals("test", StringComparison.OrdinalIgnoreCase)) < memberCount * 2 ||
            instructions.Count(item => item.IsConditionalBranch) < memberCount * 2)
        {
            throw new InvalidOperationException("复合成员适配器的核心调用、条件分支或栈清理结构不完整。");
        }
        var payloadStart = payload.Address;
        var payloadEnd = checked(payload.Address + (uint)bytes.Length);
        foreach (var redirect in preview.Package.PatchSegments.Where(segment => segment.HookPoint == "复合成员调用重定向"))
        {
            var call = Convert.FromHexString(redirect.BytesHex);
            var target = checked((uint)((long)redirect.Address + 5 + BitConverter.ToInt32(call, 1)));
            if (call[0] != 0xE8 || target < payloadStart + codeOffset || target >= payloadEnd)
                throw new InvalidOperationException("复合成员调用重定向没有指向代码洞中的成员适配器。");
        }
    }

    private static void RunNativeDefinitionWriteRoundTrip(CczProject source, FreeEffectIdReport free)
    {
        var allocatable = free.FreeIds.Concat(free.ReclaimableIds).ToList();
        if (allocatable.Count == 0) throw new InvalidOperationException("没有空白或可回收的人物/兵种编号用于写入烟测。");
        var root = Path.Combine(Path.GetTempPath(), "ccz-effect-authoring-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var file in new[] { "Ekd5.exe", "Imsg.e5", "Data.e5", "Star.e5" })
            {
                var sourcePath = source.ResolveGameFile(file);
                if (File.Exists(sourcePath)) File.Copy(sourcePath, Path.Combine(root, file));
            }
            File.WriteAllText(Path.Combine(root, "_CCZModStudio_TestCopy.txt"), "effect authoring smoke");
            var test = new CczProject
            {
                WorkspaceRoot = root,
                GameRoot = root,
                HexTableXmlPath = source.HexTableXmlPath
            };
            var id = allocatable[0];
            var service = new NativeEffectConfigurationService();
            var preview = service.Preview(test, new NativeEffectEditDraft
            {
                Channel = CompositeEffectChannel.PersonalJob,
                EffectId = id,
                Name = "复合烟测",
                Description = "原生特效定义事务烟测。"
            });
            if (!preview.CanApply) throw new InvalidOperationException("原生定义写入烟测预览失败：" + preview.SummaryZh);
            var apply = service.Apply(test, preview.Package);
            if (!apply.Applied || apply.BackupPaths.Count < 2) throw new InvalidOperationException("原生定义事务没有生成全部备份。");
            var reread = service.Read(test, CompositeEffectChannel.PersonalJob, id);
            if (reread.GameName != "复合烟测") throw new InvalidOperationException("原生名称写后复读不一致。");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    internal static void RunCompositeWriteRoundTrip(CczProject source)
    {
        var root = Path.Combine(Path.GetTempPath(), "ccz-composite-authoring-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var file in new[] { "Ekd5.exe", "Imsg.e5", "Data.e5", "Star.e5" })
            {
                var sourcePath = source.ResolveGameFile(file);
                if (File.Exists(sourcePath)) File.Copy(sourcePath, Path.Combine(root, file));
            }
            File.WriteAllText(Path.Combine(root, "_CCZModStudio_TestCopy.txt"), "composite effect smoke");
            var test = new CczProject { WorkspaceRoot = root, GameRoot = root, HexTableXmlPath = source.HexTableXmlPath };
            var originalSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(test.ResolveGameFile("Ekd5.exe"))));
            var service = new CompositeEffectService();
            var free = service.FindFreeIds(test, CompositeEffectChannel.PersonalJob);
            var id = free.FreeIds.Concat(free.ReclaimableIds).FirstOrDefault(-1);
            var inventory = new EffectInventoryService().Scan(test);
            var members = inventory.Effects.Where(item => item.CoreCalls.Count > 0 && item.WrapperEntries.Count == 0).Take(2).ToList();
            if (id < 0 || members.Count < 2) throw new InvalidOperationException("临时副本没有复合特效所需的编号或成员。");
            var blueprint = new ModularCompositeEffectBlueprint
            {
                BlueprintId = "modular-write-" + Guid.NewGuid().ToString("N"),
                RecipeId = "recipe.compose-existing-effects",
                Channel = CompositeEffectChannel.PersonalJob,
                EffectId = id,
                Name = "复合闭环烟测",
                Description = "临时副本通过模块服务注入、重扫和恢复。",
                ConditionModuleIds = ["condition.personal-or-item"],
                ActionModuleId = "action.compose-existing",
                ValueModuleId = "value.fixed",
                SafetyModuleId = "safety.direct-core",
                Members =
                [
                    new CompositeEffectMember { InstanceId = members[0].InstanceId, EffectValue = 7 },
                    new CompositeEffectMember { InstanceId = members[1].InstanceId, EffectValue = 11 }
                ],
                Bindings = [new EffectPackageBinding { Kind = "job_assignment", PersonId = 0, EffectValue = 1 }]
            };
            var modularService = new ModularEffectAuthoringService();
            var modularPreview = modularService.Preview(test, blueprint);
            var preview = modularPreview.CompositePreview;
            var draft = modularPreview.Validation.CompositeDraft
                        ?? throw new InvalidOperationException("模块化写入烟测没有生成复合草案。");
            if (!modularPreview.CanApply) throw new InvalidOperationException("临时模块化复合注入预览失败：" + modularPreview.SummaryZh);
            if (modularPreview.Package.Metadata.GetValueOrDefault("LogicalPatchKind") != "modular-composite-effect")
                throw new InvalidOperationException("模块化最终元数据没有在凭据签发前写入事务包。");
            AssertCompositeReceiptTamperRejected(test, service, preview);
            var apply = modularService.Apply(test, modularPreview.Package);
            if (!apply.Applied || !File.Exists(apply.ManifestPath)) throw new InvalidOperationException("临时复合特效没有生成安装 manifest。");
            try
            {
                service.Apply(test, preview.Package);
                throw new InvalidOperationException("同一复合预览凭据被错误地重复使用。 ");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("已使用", StringComparison.Ordinal) ||
                                                       ex.Message.Contains("已失效", StringComparison.Ordinal) ||
                                                       ex.Message.Contains("预览后变化", StringComparison.Ordinal) ||
                                                       ex.Message.Contains("项目身份", StringComparison.Ordinal))
            {
            }
            EffectInventoryService.Invalidate(test);
            var installed = new EffectInventoryService().Scan(test).Effects.SingleOrDefault(item => item.InstanceId == draft.CompositeId);
            if (installed == null || !installed.IsEditable || installed.Parameters.Count != 2)
                throw new InvalidOperationException("复合特效写入后没有重新聚合为一个可读逻辑实例。");

            var update = service.PreviewUpdate(test, new CompositeEffectMaintenanceDraft
            {
                ManifestId = apply.ManifestPath,
                Members = [new CompositeEffectMember { InstanceId = draft.Members[0].InstanceId, EffectValue = 19 }]
            });
            if (!update.CanApply) throw new InvalidOperationException("复合成员参数更新预览失败：" + update.SummaryZh);
            service.ApplyMaintenance(test, update.Package);
            EffectInventoryService.Invalidate(test);
            var updated = new EffectInventoryService().Scan(test).Effects.Single(item => item.InstanceId == draft.CompositeId);
            if (updated.Parameters.First(item => item.SlotId.Contains(draft.Members[0].InstanceId, StringComparison.OrdinalIgnoreCase)).Value != 19)
                throw new InvalidOperationException("复合成员参数更新后复读不一致。 ");

            var disable = service.PreviewToggle(test, apply.ManifestPath, enable: false);
            if (!disable.CanApply) throw new InvalidOperationException("复合停用预览失败：" + disable.SummaryZh);
            service.ApplyMaintenance(test, disable.Package);
            EffectInventoryService.Invalidate(test);
            var disabled = new EffectInventoryService().Scan(test).Effects.Single(item => item.InstanceId == draft.CompositeId);
            if (disabled.InstallationStatus != CompositeInstallationStatus.Disabled)
                throw new InvalidOperationException("复合停用状态没有被重新扫描识别。 ");

            var enable = service.PreviewToggle(test, apply.ManifestPath, enable: true);
            if (!enable.CanApply) throw new InvalidOperationException("复合启用预览失败：" + enable.SummaryZh);
            service.ApplyMaintenance(test, enable.Package);

            var currentManifest = service.Read(test, apply.ManifestPath);
            var entry = currentManifest.Package.PatchSegments.First(segment => segment.HookPoint == "复合成员调用重定向");
            WriteVirtualSmokeBytes(test, entry.Address, Convert.FromHexString(entry.ExpectedOldBytesHex));
            var signedCompositeBytes = File.ReadAllBytes(apply.ManifestPath);
            var unsignedComposite = JsonSerializer.Deserialize<CompositeEffectManifest>(File.ReadAllText(apply.ManifestPath, Encoding.UTF8))
                                    ?? throw new InvalidOperationException("复合签名负向烟测无法读取 manifest。");
            unsignedComposite.Signature = string.Empty;
            File.WriteAllText(apply.ManifestPath, JsonSerializer.Serialize(unsignedComposite));
            var unsignedRepair = service.PreviewRepair(test, apply.ManifestPath);
            if (unsignedRepair.CanApply || unsignedRepair.WarningsZh.All(item => !item.Contains("签名", StringComparison.Ordinal)))
                throw new InvalidOperationException("未签名复合 manifest 错误获得了受管修复权限。");
            File.WriteAllBytes(apply.ManifestPath, signedCompositeBytes);
            var repair = service.PreviewRepair(test, apply.ManifestPath);
            if (!repair.CanApply) throw new InvalidOperationException("复合修复预览失败：" + repair.SummaryZh);
            service.ApplyMaintenance(test, repair.Package);

            var removePackage = service.PreviewRemove(test, apply.ManifestPath);
            service.Remove(test, removePackage);
            EffectInventoryService.Invalidate(test);
            var restoredSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(test.ResolveGameFile("Ekd5.exe"))));
            if (!restoredSha.Equals(originalSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("复合特效移除后 EXE 摘要没有恢复到注入前。 ");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    internal static void RunManagedNativeAdapterRoundTrip(CczProject source)
    {
        var root = Path.Combine(Path.GetTempPath(), "ccz-native-adapter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var file in new[] { "Ekd5.exe", "Imsg.e5", "Data.e5", "Star.e5" })
            {
                var sourcePath = source.ResolveGameFile(file);
                if (File.Exists(sourcePath)) File.Copy(sourcePath, Path.Combine(root, file));
            }
            File.WriteAllText(Path.Combine(root, "_CCZModStudio_TestCopy.txt"), "managed native adapter smoke");
            var test = new CczProject { WorkspaceRoot = root, GameRoot = root, HexTableXmlPath = source.HexTableXmlPath };
            var service = new NativeEffectConfigurationService();
            var inventory = new EffectInventoryService().Scan(test);
            var instance = inventory.Effects.FirstOrDefault(item => item.CoreCalls.Count > 0 && item.WrapperEntries.Count == 0 &&
                                                                    item.PersonalChannel?.EffectId is >= 0 and < 0xFE)
                           ?? throw new InvalidOperationException("临时副本没有可安装受管理参数适配器的原生消费者。 ");
            var originalExeBytes = File.ReadAllBytes(test.ResolveGameFile("Ekd5.exe"));
            var originalSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(originalExeBytes));
            var originalPersonal = instance.PersonalChannel?.EffectId ?? 0;
            var originalItem = instance.ItemChannel?.EffectId ?? 0;
            var originalValueMode = instance.Parameters.FirstOrDefault(item => item.Role == InjectedEffectParameterRole.EffectValue)?.Value ?? 1;
            var originalStacking = instance.Parameters.FirstOrDefault(item => item.Role == InjectedEffectParameterRole.BooleanOption)?.Value ?? 1;
            var changedPersonal = originalPersonal == 0xD3 ? 0xD9 : 0xD3;
            var changedStacking = originalStacking == 0 ? 1 : 0;

            var direct = service.Preview(test, new NativeEffectEditDraft
            {
                Channel = CompositeEffectChannel.PersonalJob,
                EffectId = originalPersonal,
                InstanceId = instance.InstanceId,
                PersonalEffectId = changedPersonal
            });
            if (!direct.CanApply || direct.Package.Metadata.GetValueOrDefault("NativeStubOperation") != "DirectOperandUpdate" ||
                direct.Package.PatchSegments.Count == 0 || direct.Package.PatchSegments.Any(item => !string.IsNullOrWhiteSpace(item.CodeCaveId)))
                throw new InvalidOperationException("可原位表达的个人号没有优先生成精确操作数写入。 ");
            service.Apply(test, direct.Package);
            EffectInventoryService.Invalidate(test);
            var directlyChanged = new EffectInventoryService().Scan(test).Effects.FirstOrDefault(item =>
                instance.CoreCalls.All(call => item.CoreCalls.Contains(call)) && item.PersonalChannel?.EffectId == changedPersonal)
                                  ?? throw new InvalidOperationException("个人号原位写入后没有按原消费者复读。 ");
            var directRestore = service.Preview(test, new NativeEffectEditDraft
            {
                Channel = CompositeEffectChannel.PersonalJob,
                EffectId = changedPersonal,
                InstanceId = directlyChanged.InstanceId,
                PersonalEffectId = originalPersonal
            });
            if (!directRestore.CanApply || directRestore.Package.Metadata.GetValueOrDefault("NativeStubOperation") != "DirectOperandUpdate")
                throw new InvalidOperationException("个人号原位写入没有生成对称恢复预览。 ");
            service.Apply(test, directRestore.Package);
            EffectInventoryService.Invalidate(test);
            instance = new EffectInventoryService().Scan(test).Effects.FirstOrDefault(item =>
                           directlyChanged.CoreCalls.All(call => item.CoreCalls.Contains(call)) && item.PersonalChannel?.EffectId == originalPersonal)
                       ?? throw new InvalidOperationException("个人号原位恢复后没有找回原消费者。 ");
            var directRestoredBytes = File.ReadAllBytes(test.ResolveGameFile("Ekd5.exe"));
            if (!directRestoredBytes.SequenceEqual(originalExeBytes))
            {
                var differences = originalExeBytes.Zip(directRestoredBytes).Select((pair, index) => (pair, index))
                    .Where(item => item.pair.First != item.pair.Second).Take(16)
                    .Select(item => $"0x{item.index:X}:{item.pair.First:X2}->{item.pair.Second:X2}");
                var firstSegments = string.Join(";", direct.Package.PatchSegments.Select(item =>
                    $"{item.AddressKind}:0x{item.Address:X}:{item.ExpectedOldBytesHex}->{item.BytesHex}"));
                var restoreSegments = string.Join(";", directRestore.Package.PatchSegments.Select(item =>
                    $"{item.AddressKind}:0x{item.Address:X}:{item.ExpectedOldBytesHex}->{item.BytesHex}"));
                throw new InvalidOperationException("个人号原位恢复后 EXE 尚未回到安装适配器前：" +
                                                    string.Join(",", differences) +
                                                    "；首次段=" + firstSegments +
                                                    "；恢复段=" + restoreSegments);
            }

            var install = service.Preview(test, new NativeEffectEditDraft
            {
                Channel = CompositeEffectChannel.PersonalJob,
                EffectId = originalPersonal,
                InstanceId = instance.InstanceId,
                PersonalEffectId = changedPersonal,
                StackingMode = changedStacking
            });
            if (!install.CanApply || install.Package.Metadata.GetValueOrDefault("AdapterOperation") != "Install")
                throw new InvalidOperationException("受管理参数适配器首次安装预览失败：" + install.SummaryZh);
            service.Apply(test, install.Package);
            var managedService = new ManagedNativeParameterAdapterService();
            var installed = managedService.FindByInstance(test, instance.InstanceId)
                            ?? throw new InvalidOperationException("首次安装后没有复读出 CCNA 受管理参数适配器：" +
                                                                   string.Join("；", managedService.DiagnosticsZh));
            if (installed.PersonalEffectId != changedPersonal || installed.CallSites.Count != instance.CoreCalls.Distinct().Count())
                throw new InvalidOperationException("首次安装后的参数或调用点复读不一致。 ");

            var changedItem = originalItem == 0x1B ? 0x1C : 0x1B;
            var update = service.Preview(test, new NativeEffectEditDraft
            {
                Channel = CompositeEffectChannel.PersonalJob,
                EffectId = changedPersonal,
                InstanceId = instance.InstanceId,
                ItemEffectId = changedItem
            });
            if (!update.CanApply || update.Package.Metadata.GetValueOrDefault("AdapterOperation") != "Update" ||
                update.Package.PatchSegments.Count != 4 || update.Package.PatchSegments.Any(item => !string.IsNullOrWhiteSpace(item.CodeCaveId)))
                throw new InvalidOperationException("已安装参数适配器没有复用原参数块。 ");
            service.Apply(test, update.Package);
            var updated = managedService.FindByInstance(test, instance.InstanceId)
                          ?? throw new InvalidOperationException("参数块更新后没有复读出受管理适配器。 ");
            if (updated.PayloadAddress != installed.PayloadAddress || updated.EntryAddress != installed.EntryAddress || updated.ItemEffectId != changedItem)
                throw new InvalidOperationException("重复修改错误分配了新代码洞或参数复读不一致。 ");

            var restore = service.Preview(test, new NativeEffectEditDraft
            {
                Channel = CompositeEffectChannel.PersonalJob,
                EffectId = changedPersonal,
                InstanceId = instance.InstanceId,
                PersonalEffectId = originalPersonal,
                ItemEffectId = originalItem,
                EffectValueMode = originalValueMode,
                StackingMode = originalStacking
            });
            if (!restore.CanApply || restore.Package.Metadata.GetValueOrDefault("AdapterOperation") != "Restore")
                throw new InvalidOperationException("恢复原参数没有生成适配器释放预览：" + restore.SummaryZh);
            service.Apply(test, restore.Package);
            if (managedService.FindByInstance(test, instance.InstanceId) != null)
                throw new InvalidOperationException("恢复原参数后旧适配器仍被视为活动。 ");
            if (new CodeCaveRegistry().LoadExistingAllocations(test).Any(item =>
                    item.StartVirtualAddress == installed.PayloadAddress))
                throw new InvalidOperationException("恢复原参数后旧适配器代码洞仍被占用。 ");
            var restoredBytes = File.ReadAllBytes(test.ResolveGameFile("Ekd5.exe"));
            if (!restoredBytes.SequenceEqual(originalExeBytes))
            {
                var differences = originalExeBytes.Zip(restoredBytes).Select((pair, index) => (pair, index))
                    .Where(item => item.pair.First != item.pair.Second).Take(16)
                    .Select(item => $"0x{item.index:X}:{item.pair.First:X2}->{item.pair.Second:X2}");
                throw new InvalidOperationException("受管理参数适配器恢复后 EXE 摘要未回到安装前：" + string.Join(",", differences));
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void RunEffectShaIsolationSmoke(CczProject source)
    {
        var writable = new EffectWritableProfileService().Evaluate(source);
        if (!writable.CanWrite || !writable.IsOriginalBaseline)
            throw new InvalidOperationException("当前 6.5 基底没有通过特效 SHA 写入身份校验。 ");
        var legacyRoot = Path.Combine(source.WorkspaceRoot, "基底", "东吴霸王传6.5");
        if (!File.Exists(Path.Combine(legacyRoot, "Ekd5.exe"))) return;
        var legacy = new CczProject { WorkspaceRoot = source.WorkspaceRoot, GameRoot = legacyRoot, HexTableXmlPath = source.HexTableXmlPath };
        var legacyStatus = new EffectWritableProfileService().Evaluate(legacy);
        if (!legacyStatus.CanWrite || legacyStatus.CurrentExeSha256 != EffectWritableProfileStatus.LegacyDynamicEvidenceSha256 ||
            legacyStatus.ProfileAudit.TrustStatus != ExecutableProfileTrustStatus.AutoDerivedDataOnly ||
            legacyStatus.ProfileAudit.RegisteredDifferences.Count != 1 ||
            legacyStatus.ProfileAudit.RegisteredDifferences[0].FileOffset != 0xA2CE0)
            throw new InvalidOperationException("已知 84E3 数据型 6.5 变体没有按登记字段派生规则开放。 ");

        var root = Path.Combine(Path.GetTempPath(), "ccz-effect-unknown-byte-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var exe = Path.Combine(root, "Ekd5.exe");
            File.Copy(source.ResolveGameFile("Ekd5.exe"), exe);
            var bytes = File.ReadAllBytes(exe);
            bytes[0x420] ^= 0x01;
            File.WriteAllBytes(exe, bytes);
            var unknown = new CczProject { WorkspaceRoot = root, GameRoot = root, HexTableXmlPath = source.HexTableXmlPath };
            var unknownStatus = new EffectWritableProfileService().Evaluate(unknown);
            if (unknownStatus.CanWrite || !unknownStatus.ProfileAudit.BlockerCodes.Contains("UNKNOWN_DIFFERENCE", StringComparer.Ordinal))
                throw new InvalidOperationException("未登记代码区即使只改 1 字节也必须保持只读。 ");
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    private static void RunForeignManifestIsolationSmoke(CczProject source)
    {
        var root = Path.Combine(Path.GetTempPath(), "ccz-effect-identity-" + Guid.NewGuid().ToString("N"));
        var foreignRoot = Path.Combine(Path.GetTempPath(), "ccz-effect-foreign-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(foreignRoot);
        try
        {
            File.Copy(source.ResolveGameFile("Ekd5.exe"), Path.Combine(root, "Ekd5.exe"));
            File.Copy(source.ResolveGameFile("Ekd5.exe"), Path.Combine(foreignRoot, "Ekd5.exe"));
            File.WriteAllText(Path.Combine(root, "_CCZModStudio_TestCopy.txt"), "project identity smoke");
            var project = new CczProject { WorkspaceRoot = root, GameRoot = root, HexTableXmlPath = source.HexTableXmlPath };
            var foreign = new CczProject { WorkspaceRoot = foreignRoot, GameRoot = foreignRoot, HexTableXmlPath = source.HexTableXmlPath };

            var exe = Path.Combine(root, "Ekd5.exe");
            var bytes = File.ReadAllBytes(exe);
            bytes[^1] ^= 0x01;
            File.WriteAllBytes(exe, bytes);
            var changedSha = EffectPatchByteService.Sha256(exe);
            var manifestRoot = ProjectPatchIdentityService.EffectManifestRoot(project);
            Directory.CreateDirectory(manifestRoot);
            var foreignManifest = new EffectManifest
            {
                ManifestId = "foreign-project-smoke",
                ProjectRoot = foreignRoot,
                ProjectIdentity = new ProjectPatchIdentityService().Build(foreign),
                Package = new EffectPackage
                {
                    PatchSegments =
                    [
                        new EffectPatchSegment
                        {
                            TargetFile = "Ekd5.exe", AddressKind = "OdVirtualAddress", Address = 0x004D3500,
                            BytesHex = "90 90 90 90 90", ExpectedOldBytesHex = "00 00 00 00 00", CodeCaveId = "foreign-cave"
                        }
                    ]
                },
                Metadata =
                {
                    ["EngineProfileSha256Before"] = EffectWritableProfileStatus.Current65BaselineSha256,
                    ["EngineProfileSha256After"] = changedSha
                }
            };
            File.WriteAllText(Path.Combine(manifestRoot, "foreign-project-smoke.json"), JsonSerializer.Serialize(foreignManifest));
            if (new CodeCaveRegistry().LoadExistingAllocations(project).Any(item => item.CaveId.Contains("foreign", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("其他项目的 manifest 错误占用了当前项目代码洞。 ");
            if (new EffectWritableProfileService().Evaluate(project).CanWrite)
                throw new InvalidOperationException("其他项目的 manifest 错误建立了当前 EXE 的可信 SHA 祖先链。 ");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
            try { Directory.Delete(foreignRoot, recursive: true); } catch { }
        }
    }

    private static void AssertCompositeReceiptTamperRejected(
        CczProject project,
        CompositeEffectService service,
        CompositeEffectPreview preview)
    {
        var exe = project.ResolveGameFile("Ekd5.exe");
        var beforeSha = EffectPatchByteService.Sha256(exe);
        var segment = preview.Package.PatchSegments.First();
        var original = segment.BytesHex;
        var bytes = EffectPatchByteService.ParseHex(original);
        bytes[0] ^= 0x01;
        segment.BytesHex = EffectPatchByteService.ToHex(bytes);
        try
        {
            service.Apply(project, preview.Package);
            throw new InvalidOperationException("篡改后的复合预览包被错误接受。 ");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("不一致", StringComparison.Ordinal) ||
                                                   ex.Message.Contains("预览凭据", StringComparison.Ordinal) ||
                                                   ex.Message.Contains("结构化写入授权", StringComparison.Ordinal) ||
                                                   ex.Message.Contains("已被修改", StringComparison.Ordinal))
        {
        }
        finally
        {
            segment.BytesHex = original;
        }
        if (!EffectPatchByteService.Sha256(exe).Equals(beforeSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("篡改预览包的阻断测试修改了临时 EXE。 ");
    }

    private static void WriteVirtualSmokeBytes(CczProject project, uint address, byte[] bytes)
    {
        var path = project.ResolveGameFile("Ekd5.exe");
        var mapper = CCZModStudio.Formats.PeAddressMapper.Load(path);
        var offset = mapper.VirtualAddressToFileOffset(address);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read);
        stream.Position = offset;
        stream.Write(bytes);
    }
}
