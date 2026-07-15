using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class EffectIdLocationIndexService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly ConcurrentDictionary<string, Lazy<Task<EffectIdLocationIndex>>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, long> CacheAccessOrder = new(StringComparer.OrdinalIgnoreCase);
    private static long _cacheAccessSequence;

    public static void Invalidate(CczProject? project = null)
    {
        if (project == null) { Cache.Clear(); CacheAccessOrder.Clear(); return; }
        var root = Path.GetFullPath(project.GameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var key in Cache.Keys.Where(key => key.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
        {
            Cache.TryRemove(key, out _);
            CacheAccessOrder.TryRemove(key, out _);
        }
    }

    public EffectIdLocationIndex Scan(CczProject project, string targetFile = "Ekd5.exe", bool exportReports = true)
        => ScanAsync(project, targetFile, exportReports).GetAwaiter().GetResult();

    public async Task<EffectIdLocationIndex> ScanAsync(CczProject project, string targetFile = "Ekd5.exe", bool exportReports = true, CancellationToken cancellationToken = default)
    {
        var targetPath = project.ResolveGameFile(targetFile);
        var fingerprint = new UnifiedEffectCatalogService().BuildFingerprint(project, targetFile);
        var cacheKey = Path.GetFullPath(project.GameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar + targetFile + "|" + fingerprint;
        var candidate = new Lazy<Task<EffectIdLocationIndex>>(
            () => Task.Run(() => Build(project, targetFile, targetPath), CancellationToken.None),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var lazy = Cache.GetOrAdd(cacheKey, candidate);
        CacheAccessOrder[cacheKey] = Interlocked.Increment(ref _cacheAccessSequence);
        var miss = ReferenceEquals(lazy, candidate);
        try
        {
            var cached = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (exportReports && cached.ReportPaths.Count == 0) Export(project, cached);
            PerformanceMetrics.Increment(miss ? "EffectLocationIndex.CacheMisses" : "EffectLocationIndex.CacheHits");
            Trim(cacheKey[..(cacheKey.LastIndexOf('|') + 1)]);
            return cached;
        }
        catch
        {
            if (lazy.IsValueCreated && lazy.Value.IsFaulted)
            {
                Cache.TryRemove(new KeyValuePair<string, Lazy<Task<EffectIdLocationIndex>>>(cacheKey, lazy));
                CacheAccessOrder.TryRemove(cacheKey, out _);
            }
            throw;
        }
    }

    private static void Trim(string prefix)
    {
        foreach (var key in CacheAccessOrder.Where(pair => pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(pair => pair.Value).Skip(2).Select(pair => pair.Key).ToArray())
        {
            Cache.TryRemove(key, out _);
            CacheAccessOrder.TryRemove(key, out _);
        }
    }

    private static EffectIdLocationIndex Build(CczProject project, string targetFile, string targetPath)
    {
        var inventory = new EffectInventoryService().Scan(project, targetFile);
        var writable = new EffectWritableProfileService().Evaluate(project);
        var executable = ExecutableAnalysisSnapshotCache.Shared.GetBase(targetPath);
        var result = new EffectIdLocationIndex
        {
            AnalysisFingerprint = inventory.AnalysisFingerprint,
            CacheState = "BuiltFromSharedSnapshot",
            CompletedStages = ["复用库存", "复用 EXE 反汇编", "索引机器码参数", "索引原生表", "索引复合参数块"],
            ProfileAudit = writable.ProfileAudit,
            TargetFilePath = targetPath,
            ExeSha256 = inventory.ExeSha256,
            ImageBase = executable.PeImage.ImageBase,
            WritableProfileId = writable.ProfileId,
            CurrentProfileCanWrite = writable.CanWrite
        };

        AddInstructionAndPatchLocations(result, inventory, executable, writable.CanWrite);
        AddNativeTableLocations(result, project, inventory, writable.CanWrite);
        AddCompositeLocations(result, project, inventory, executable, writable.CanWrite);
        Deduplicate(result);
        result.CountsByKind = result.Locations.GroupBy(item => item.Kind, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        result.CountsByWriteCapability = result.Locations.GroupBy(item => item.WriteCapability, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        result.SummaryZh = $"已索引 {result.Locations.Count} 个特效相关物理位置，其中可直接修改 {Count(result, EffectIdWriteCapability.DirectWritable)} 个，需要整体事务 {Count(result, EffectIdWriteCapability.TransactionWritable)} 个，需要适配桩 {Count(result, EffectIdWriteCapability.AdapterRequired)} 个。";
        if (!writable.CanWrite) result.WarningsZh.Add("当前 EXE 不属于可写档案，全部位置仅供读取和定位。" + writable.ReasonZh);
        return result;
    }

    public EffectIdLocationRecord Read(CczProject project, string locationId, string targetFile = "Ekd5.exe")
        => Scan(project, targetFile, exportReports: false).Locations.FirstOrDefault(item => item.LocationId.Equals(locationId, StringComparison.OrdinalIgnoreCase))
           ?? throw new InvalidOperationException($"没有找到特效位置“{locationId}”。");

    public EffectIdUpdatePreview PreviewUpdate(CczProject project, EffectIdUpdateRequest request, string targetFile = "Ekd5.exe")
    {
        var result = new EffectIdUpdatePreview();
        EffectIdLocationRecord location;
        try { location = Read(project, request.LocationId, targetFile); }
        catch (Exception ex) { result.WarningsZh.Add(ex.Message); result.SummaryZh = "位置读取失败。"; return result; }
        result.Location = location;
        var effectiveCapability = location.WriteCapability;
        if (request.NewValue < 0 || request.NewValue > (location.ExtendedMaximum ?? Maximum(location.ByteLength))) result.WarningsZh.Add($"新值必须在 0-{location.ExtendedMaximum ?? Maximum(location.ByteLength)} 之间。");
        if (location.WriteCapability == EffectIdWriteCapability.AdapterRequired && request.NewValue > (location.InPlaceMaximum ?? sbyte.MaxValue))
            return new EffectParameterAdapterService().Preview(project, location, request.NewValue);
        if (location.WriteCapability == EffectIdWriteCapability.AdapterRequired && request.NewValue <= (location.InPlaceMaximum ?? sbyte.MaxValue))
            effectiveCapability = EffectIdWriteCapability.DirectWritable;
        if (effectiveCapability is not EffectIdWriteCapability.DirectWritable and not EffectIdWriteCapability.TransactionWritable)
            result.WarningsZh.Add(string.IsNullOrWhiteSpace(location.BlockingReasonZh) ? "该位置没有开放自动写入。" : location.BlockingReasonZh);
        if (!location.FileOffset.HasValue || location.ByteLength is < 1 or > 4) result.WarningsZh.Add("位置缺少精确文件偏移或编码宽度。");
        if (result.WarningsZh.Count > 0) { result.SummaryZh = "特效号修改预览未通过：" + string.Join("；", result.WarningsZh); return result; }

        var encoded = BitConverter.GetBytes(request.NewValue).Take(location.ByteLength).ToArray();
        result.Package = new EffectPackage
        {
            PackageId = $"effect-location-{location.LocationId}-{DateTime.Now:yyyyMMddHHmmssfff}",
            Domain = "patch",
            EffectId = request.NewValue,
            Name = "修改特效号物理位置",
            Description = $"{location.OwnerNameZh}：{location.ParameterRoleZh} {location.EffectIdHex} → {request.NewValue:X2}",
            BackupNote = "按位置索引修改；必须保留 EXE 身份和旧字节锁。",
            Metadata =
            {
                ["LogicalPatchKind"] = "effect-id-location-update",
                ["LocationId"] = location.LocationId,
                ["EvidenceExeSha256"] = location.EvidenceExeSha256
            },
            PatchSegments =
            {
                new EffectPatchSegment
                {
                    TargetFile = location.TargetFile,
                    AddressKind = "FileOffset",
                    Address = checked((uint)location.FileOffset!.Value),
                    BytesHex = EffectPatchByteService.ToHex(encoded),
                    ExpectedOldBytesHex = location.ExpectedOldBytesHex,
                    EngineProfileSha256 = location.TargetFile.Equals("Ekd5.exe", StringComparison.OrdinalIgnoreCase) ? location.EvidenceExeSha256 : string.Empty,
                    HookPoint = location.ParameterRoleZh,
                    Comment = "特效号位置索引锁定修改",
                    SemanticFieldId = "effect-id:" + location.ParameterRole,
                    ModificationKind = EffectModificationKind.DirectEffectId,
                    SourceLocationId = location.LocationId,
                    RequiredCapability = EffectWriteCapability.DirectParameter
                }
            }
        };
        result.PatchPreview = new EffectTransactionalPatchService().Preview(project, result.Package);
        result.CanApply = result.PatchPreview.CanApply;
        result.SummaryZh = result.CanApply ? "预览通过，已生成带旧字节锁的修改包。" : "底层补丁预览未通过：" + result.PatchPreview.Summary;
        return result;
    }

    private static void AddInstructionAndPatchLocations(EffectIdLocationIndex result, EffectInventoryReport inventory, ExecutableAnalysisSnapshot executable, bool profileWritable)
    {
        var scan = executable.InstructionScan;
        foreach (var candidate in inventory.Discovery.Candidates)
        {
            foreach (var slot in candidate.ParameterSlots.Where(item => item.Role is InjectedEffectParameterRole.Personal or InjectedEffectParameterRole.Equipment))
            {
                var channel = slot.Role == InjectedEffectParameterRole.Personal ? EffectChannelKind.PersonalJob : EffectChannelKind.Item;
                var owner = FindOwner(inventory, slot.Address, candidate.Address);
                var instruction = slot.DefinitionInstructionAddress.HasValue && scan.InstructionsByAddress.TryGetValue(slot.DefinitionInstructionAddress.Value, out var found) ? found : null;
                // OperandFileOffset can be relative to a sliced code body. The operand VA is the
                // authoritative coordinate and must be mapped through the shared PE snapshot.
                var fileOffset = TryMap(executable, slot.Address);
                var current = ReadHex(executable.Bytes, fileOffset, slot.ByteLength);
                var signedImm8 = instruction?.Mnemonic == "push" && instruction.ImmediateSize == 1;
                var kind = LocationKind(slot, candidate);
                var sampleOnly = kind == EffectIdLocationKind.KnownSampleLocation;
                var canPatch = profileWritable && slot.IsDirectlyPatchable && slot.Address.HasValue && fileOffset.HasValue && slot.ByteLength is 1 or 2 or 4;
                var capability = sampleOnly ? EffectIdWriteCapability.DiagnosticOnly
                    : !profileWritable ? EffectIdWriteCapability.ReadOnlyVerified
                    : !slot.IsDirectlyPatchable ? EffectIdWriteCapability.ReadOnlyVerified
                    : signedImm8 ? EffectIdWriteCapability.AdapterRequired
                    : canPatch ? EffectIdWriteCapability.DirectWritable : EffectIdWriteCapability.DiagnosticOnly;
                result.Locations.Add(FinalizeRecord(new EffectIdLocationRecord
                {
                    Kind = kind,
                    KindZh = KindZh(kind),
                    Channel = channel,
                    ChannelZh = ChannelZh(channel),
                    EffectId = slot.Value,
                    EffectNameZh = ResolveName(inventory, channel, slot.Value),
                    ParameterRole = slot.Role,
                    ParameterRoleZh = slot.Role == InjectedEffectParameterRole.Personal ? "个人/兵种特效号" : "宝物特效号",
                    TargetFile = "Ekd5.exe",
                    VirtualAddress = slot.Address,
                    Rva = slot.Address.HasValue && slot.Address.Value >= executable.PeImage.ImageBase ? slot.Address.Value - executable.PeImage.ImageBase : null,
                    FileOffset = fileOffset,
                    InstructionAddress = slot.DefinitionInstructionAddress,
                    OperandOffset = slot.OperandOffset,
                    ByteLength = slot.ByteLength,
                    IsSigned = signedImm8,
                    Encoding = signedImm8 ? "push imm8（有符号扩展）" : instruction == null ? slot.SourceKind : instruction.Mnemonic + " immediate",
                    CurrentBytesHex = current,
                    CurrentValue = slot.Value,
                    OwnerInstanceId = owner?.InstanceId ?? string.Empty,
                    OwnerNameZh = owner?.Name ?? candidate.Name,
                    WrapperChain = candidate.WrapperEntryAddress.HasValue ? [candidate.WrapperEntryAddress.Value] : [],
                    EvidenceExeSha256 = inventory.ExeSha256,
                    EvidenceSource = slot.SourceComment,
                    EvidenceLevel = candidate.DetectionLevel,
                    WriteCapability = capability,
                    WriteCapabilityZh = CapabilityZh(capability),
                    BlockingReasonZh = sampleOnly ? "知识库样本位置不代表当前 EXE 已安装，禁止作为写入依据。" : BlockingReason(capability, profileWritable, signedImm8, slot.IsDirectlyPatchable),
                    ExpectedOldBytesHex = current,
                    InPlaceMinimum = 0,
                    InPlaceMaximum = signedImm8 ? sbyte.MaxValue : Maximum(slot.ByteLength),
                    ExtendedMinimum = 0,
                    ExtendedMaximum = slot.Role is InjectedEffectParameterRole.Personal or InjectedEffectParameterRole.Equipment ? byte.MaxValue : Maximum(slot.ByteLength),
                    AdapterStrategy = signedImm8 ? "RelocateWindowAndReplacePushImm32" : string.Empty,
                    DefinitionChain = slot.DefinitionChain.ToList()
                }));
            }
        }
    }

    private static void AddNativeTableLocations(EffectIdLocationIndex result, CczProject project, EffectInventoryReport inventory, bool profileWritable)
    {
        var tables = new Ccz66HexTableAugmentationService().LoadForProject(project, new HexTableParser());
        foreach (var table in tables.Where(item => item.Enabled && !item.ReadOnly))
        {
            var path = project.ResolveGameFile(table.FileName);
            if (!File.Exists(path) || table.RowSize <= 0 || table.RowCount <= 0) continue;
            var bytes = File.ReadAllBytes(path);
            var fieldOffset = 0;
            foreach (var field in table.Fields)
            {
                if (!field.ConsumesBytes) continue;
                if (field.ColumnName.Equals("装备特效号", StringComparison.Ordinal))
                {
                    for (var row = 0; row < table.RowCount; row++)
                    {
                        var offset = checked(table.DataPos + (long)row * table.RowSize + fieldOffset);
                        var value = ReadUnsigned(bytes, offset, field.Size);
                        if (!value.HasValue) continue;
                        var capability = profileWritable ? EffectIdWriteCapability.TransactionWritable : EffectIdWriteCapability.ReadOnlyVerified;
                        result.Locations.Add(FinalizeRecord(new EffectIdLocationRecord
                        {
                            Kind = EffectIdLocationKind.NativeTableField, KindZh = "原生表字段",
                            Channel = EffectChannelKind.Item, ChannelZh = "宝物渠道", EffectId = value,
                            EffectNameZh = ResolveName(inventory, EffectChannelKind.Item, value),
                            ParameterRole = InjectedEffectParameterRole.Equipment, ParameterRoleZh = "物品绑定的宝物特效号",
                            TargetFile = table.FileName, FileOffset = offset, ByteLength = field.Size,
                            Encoding = $"无符号小端 {field.Size} 字节", CurrentBytesHex = ReadHex(bytes, offset, field.Size), CurrentValue = value,
                            OwnerInstanceId = $"table-{table.Id}-row-{row + table.BeginId}", OwnerNameZh = $"{table.TableName} 第 {row + table.BeginId} 行",
                            EvidenceExeSha256 = inventory.ExeSha256, EvidenceSource = table.TableName + "/" + field.ColumnName,
                            EvidenceLevel = "VerifiedStatic", WriteCapability = capability, WriteCapabilityZh = CapabilityZh(capability),
                            BlockingReasonZh = profileWritable ? "必须与相关表和 EXE 修改一起事务预览。" : "当前 EXE 档案只读。",
                            ExpectedOldBytesHex = ReadHex(bytes, offset, field.Size)
                        }));
                    }
                }
                fieldOffset += field.Size;
            }

            if (table.TableName.Contains("6.5-7", StringComparison.Ordinal) && table.BeginId == 0 && table.RowCount == 255)
            {
                for (var row = 0; row < table.RowCount; row++)
                {
                    var offset = checked(table.DataPos + (long)row * table.RowSize);
                    var capability = profileWritable ? EffectIdWriteCapability.TransactionWritable : EffectIdWriteCapability.ReadOnlyVerified;
                    result.Locations.Add(FinalizeRecord(new EffectIdLocationRecord
                    {
                        Kind = EffectIdLocationKind.CatalogDefinition, KindZh = "原生定义或绑定行",
                        Channel = EffectChannelKind.PersonalJob, ChannelZh = "个人/兵种渠道", EffectId = row,
                        EffectNameZh = ResolveName(inventory, EffectChannelKind.PersonalJob, row),
                        ParameterRole = "DefinitionRow", ParameterRoleZh = table.TableName.Contains("说明", StringComparison.Ordinal) ? "特效说明槽" : table.TableName.Contains("分配", StringComparison.Ordinal) || table.TableName.Contains("专属", StringComparison.Ordinal) ? "特效绑定行" : "特效名称槽",
                        TargetFile = table.FileName, FileOffset = offset, ByteLength = table.RowSize,
                        Encoding = table.TableName.Contains("分配", StringComparison.Ordinal) || table.TableName.Contains("专属", StringComparison.Ordinal) ? "结构化表行" : "固定长度 GBK 槽",
                        CurrentBytesHex = ReadHex(bytes, offset, Math.Min(table.RowSize, 32)), CurrentValue = row,
                        OwnerInstanceId = $"definition-{row:X2}", OwnerNameZh = ResolveName(inventory, EffectChannelKind.PersonalJob, row),
                        EvidenceExeSha256 = inventory.ExeSha256, EvidenceSource = table.TableName,
                        EvidenceLevel = "VerifiedStatic", WriteCapability = capability, WriteCapabilityZh = CapabilityZh(capability),
                        BlockingReasonZh = "定义槽必须通过原生配置事务修改，不能按一个数值字节直接覆盖。",
                        ExpectedOldBytesHex = ReadHex(bytes, offset, table.RowSize)
                    }));
                }
            }
        }
    }

    private static void AddCompositeLocations(EffectIdLocationIndex result, CczProject project, EffectInventoryReport inventory, ExecutableAnalysisSnapshot executable, bool profileWritable)
    {
        foreach (var manifest in new CompositeEffectService().List(project))
        {
            foreach (var record in manifest.ParameterBlock.Records.Where(item => item.ValueAddress != 0))
            {
                var offset = TryMap(executable, record.ValueAddress);
                var current = ReadHex(executable.Bytes, offset, 4);
                var capability = profileWritable && offset.HasValue ? EffectIdWriteCapability.TransactionWritable : EffectIdWriteCapability.ReadOnlyVerified;
                result.Locations.Add(FinalizeRecord(new EffectIdLocationRecord
                {
                    Kind = EffectIdLocationKind.CompositeParameterBlock, KindZh = "复合特效参数块",
                    Channel = manifest.Draft.Channel == CompositeEffectChannel.Item ? EffectChannelKind.Item : EffectChannelKind.PersonalJob,
                    ChannelZh = manifest.Draft.Channel == CompositeEffectChannel.Item ? "宝物渠道" : "个人/兵种渠道",
                    EffectId = manifest.Draft.EffectId, EffectNameZh = manifest.Draft.Name,
                    ParameterRole = InjectedEffectParameterRole.EffectValue, ParameterRoleZh = "复合成员数值",
                    TargetFile = "Ekd5.exe", VirtualAddress = record.ValueAddress, Rva = record.ValueAddress - executable.PeImage.ImageBase,
                    FileOffset = offset, ByteLength = 4, Encoding = "CCZE v2 小端整数", CurrentBytesHex = current, CurrentValue = record.EffectValue,
                    OwnerInstanceId = manifest.ManifestId, OwnerNameZh = manifest.Draft.Name,
                    EvidenceExeSha256 = inventory.ExeSha256, EvidenceSource = manifest.ManifestId,
                    EvidenceLevel = "VerifiedStatic", WriteCapability = capability, WriteCapabilityZh = CapabilityZh(capability),
                    BlockingReasonZh = "复合成员参数必须整体预览，不能脱离 manifest 单独写入。", ExpectedOldBytesHex = current
                }));
            }
        }
    }

    private static void Deduplicate(EffectIdLocationIndex result)
    {
        result.Locations = result.Locations.GroupBy(item => $"{item.TargetFile}|{item.FileOffset}|{item.ByteLength}|{item.ParameterRole}|{item.EffectId}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => CapabilityRank(item.WriteCapability)).First())
            .OrderBy(item => item.TargetFile, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.FileOffset ?? long.MaxValue).ToList();
    }

    private static EffectIdLocationRecord FinalizeRecord(EffectIdLocationRecord record)
    {
        // Keep the physical location identity stable when the editable effect ID changes.
        var key = $"{record.Kind}|{record.Channel}|{record.TargetFile}|{record.FileOffset}|{record.VirtualAddress}|{record.ParameterRole}";
        record.LocationId = "effect-location-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant();
        return record;
    }

    private static void Export(CczProject project, EffectIdLocationIndex result)
    {
        var root = Path.Combine(project.GameRoot, "CCZModStudio_Reports", "EffectLocations", result.ExeSha256);
        Directory.CreateDirectory(root);
        var json = Path.Combine(root, "effect-id-locations.json");
        var markdown = Path.Combine(root, "特效号物理位置索引.md");
        result.ReportPaths = [json, markdown];
        File.WriteAllText(json, JsonSerializer.Serialize(result, JsonOptions), Encoding.UTF8);
        var builder = new StringBuilder().AppendLine("# 特效号物理位置索引").AppendLine()
            .AppendLine($"- EXE SHA256：`{result.ExeSha256}`").AppendLine($"- {result.SummaryZh}").AppendLine()
            .AppendLine("| 位置编号 | 渠道 | 特效 | 位置类型 | 参数 | 文件与偏移 | 写入能力 |").AppendLine("|---|---|---|---|---|---|---|");
        foreach (var item in result.Locations)
            builder.AppendLine($"| `{item.LocationId}` | {item.ChannelZh} | {item.EffectNameZh}（{item.EffectIdHex}） | {item.KindZh} | {item.ParameterRoleZh} | `{item.TargetFile}+0x{item.FileOffset:X}` | {item.WriteCapabilityZh} |");
        File.WriteAllText(markdown, builder.ToString(), Encoding.UTF8);
    }

    private static LogicalEffectInstance? FindOwner(EffectInventoryReport inventory, uint? address, uint fallback)
        => inventory.Effects.FirstOrDefault(item => item.Parameters.Any(parameter => parameter.PhysicalPatchPoints.Any(point => address.HasValue && point.Address == address.Value)))
           ?? inventory.Effects.FirstOrDefault(item => item.CoreCalls.Contains(fallback) || item.EntryHooks.Contains(fallback));

    private static string ResolveName(EffectInventoryReport inventory, string channel, int? id)
    {
        if (!id.HasValue) return "未解析";
        var source = channel == EffectChannelKind.Item ? inventory.ItemOptions : inventory.PersonalJobOptions;
        return source.FirstOrDefault(item => item.EffectId == id.Value)?.Name ?? $"未命名特效 {id.Value:X2}";
    }

    private static string LocationKind(InjectedEffectParameterSlot slot, InjectedEffectCandidate candidate)
    {
        if (!slot.Address.HasValue) return EffectIdLocationKind.UnresolvedSource;
        if (candidate.WrapperEntryAddress.HasValue) return EffectIdLocationKind.WrapperForwardedArgument;
        if (candidate.PatternKind == InjectedEffectPatternKind.KnownPatch)
        {
            return candidate.DetectionLevel is "KnownExact" or "KnownVariant" &&
                   candidate.MatchedAnchors.Any(anchor => anchor.StartsWith("segment:", StringComparison.OrdinalIgnoreCase) ||
                                                          anchor.StartsWith("hook:", StringComparison.OrdinalIgnoreCase) ||
                                                          anchor.StartsWith("core-call:", StringComparison.OrdinalIgnoreCase))
                ? EffectIdLocationKind.InjectedPatchParameter
                : EffectIdLocationKind.KnownSampleLocation;
        }
        return slot.SourceKind switch
        {
            X86ArgumentSourceKind.RegisterDefinitionImmediate => EffectIdLocationKind.RegisterDefinitionImmediate,
            X86ArgumentSourceKind.StackSlotDefinition => EffectIdLocationKind.StackSlotDefinition,
            X86ArgumentSourceKind.MemoryBackedSource => EffectIdLocationKind.MemoryBackedSource,
            _ => EffectIdLocationKind.StubImmediate
        };
    }

    private static long? TryMap(ExecutableAnalysisSnapshot executable, uint? address) { if (!address.HasValue) return null; try { return executable.VirtualAddressToFileOffset(address.Value); } catch { return null; } }
    private static string ReadHex(byte[] bytes, long? offset, int length) => offset.HasValue && offset.Value >= 0 && length > 0 && offset.Value + length <= bytes.LongLength ? EffectPatchByteService.ToHex(bytes.AsSpan((int)offset.Value, length).ToArray()) : string.Empty;
    private static int? ReadUnsigned(byte[] bytes, long offset, int length) => offset >= 0 && length is >= 1 and <= 4 && offset + length <= bytes.LongLength ? length switch { 1 => bytes[offset], 2 => BitConverter.ToUInt16(bytes, (int)offset), _ => BitConverter.ToInt32(bytes, (int)offset) } : null;
    private static int Maximum(int length) => length switch { 1 => byte.MaxValue, 2 => ushort.MaxValue, 4 => int.MaxValue, _ => -1 };
    private static int Count(EffectIdLocationIndex result, string capability) => result.Locations.Count(item => item.WriteCapability == capability);
    private static int CapabilityRank(string value) => value switch { EffectIdWriteCapability.DirectWritable => 5, EffectIdWriteCapability.TransactionWritable => 4, EffectIdWriteCapability.AdapterRequired => 3, EffectIdWriteCapability.ReadOnlyVerified => 2, _ => 1 };
    private static string ChannelZh(string channel) => channel == EffectChannelKind.Item ? "宝物渠道" : "个人/兵种渠道";
    private static string KindZh(string kind) => kind switch { EffectIdLocationKind.StubImmediate => "判定桩立即数", EffectIdLocationKind.RegisterDefinitionImmediate => "寄存器常量定义", EffectIdLocationKind.StackSlotDefinition => "栈参数定义", EffectIdLocationKind.MemoryBackedSource => "内存参数来源", EffectIdLocationKind.WrapperForwardedArgument => "包装函数转发参数", EffectIdLocationKind.InjectedPatchParameter => "已注入补丁参数", EffectIdLocationKind.KnownSampleLocation => "知识库样本位置", _ => "未解析来源" };
    private static string CapabilityZh(string capability) => capability switch { EffectIdWriteCapability.DirectWritable => "可直接修改", EffectIdWriteCapability.AdapterRequired => "需要适配桩", EffectIdWriteCapability.TransactionWritable => "需要整体事务", EffectIdWriteCapability.ReadOnlyVerified => "已确认，只读", _ => "仅供诊断" };
    private static string BlockingReason(string capability, bool profileWritable, bool signedImm8, bool direct) => !profileWritable ? "当前 EXE 不属于可写档案。" : signedImm8 ? "有符号单字节立即数不能安全表达 80-FF，必须生成宽立即数适配桩。" : !direct ? "逻辑值已恢复，但没有唯一且可原地修改的常量来源。" : capability == EffectIdWriteCapability.DirectWritable ? string.Empty : "位置证据不足。";
}
