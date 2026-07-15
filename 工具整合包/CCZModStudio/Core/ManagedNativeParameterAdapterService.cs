using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class ManagedNativeParameterAdapterService
{
    public const string LogicalPatchKind = "native-parameter-adapter-v1";
    public const int HeaderSize = 24;
    public const int ParameterCount = 4;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    public List<string> DiagnosticsZh { get; } = [];

    public string BuildAdapterId(LogicalEffectInstance instance, uint target)
    {
        var identity = instance.InstanceId + "|" + target.ToString("X8") + "|" +
                       string.Join(",", instance.CoreCalls.Distinct().OrderBy(item => item).Select(item => item.ToString("X8")));
        return "native-adapter-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..20].ToLowerInvariant();
    }

    public ManagedNativeParameterAdapter? FindByInstance(CczProject project, string instanceId)
        => ReadActive(project).FirstOrDefault(item => item.OriginalInstanceId.Equals(instanceId, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<ManagedNativeParameterAdapter> ReadActive(CczProject project)
    {
        DiagnosticsZh.Clear();
        var result = new List<ManagedNativeParameterAdapter>();
        var root = ProjectPatchIdentityService.EffectManifestRoot(project);
        if (!Directory.Exists(root)) return result;
        foreach (var path in Directory.GetFiles(root, "*.json"))
        {
            EffectManifest? manifest = null;
            try { manifest = JsonSerializer.Deserialize<EffectManifest>(File.ReadAllText(path, Encoding.UTF8), JsonOptions); }
            catch (Exception ex) { DiagnosticsZh.Add(Path.GetFileName(path) + "：清单无法解析，" + ex.Message); }
            if (manifest == null) continue;
            if (!new ProjectPatchIdentityService().Matches(project, manifest.ProjectIdentity, manifest.ProjectRoot))
            {
                DiagnosticsZh.Add(Path.GetFileName(path) + "：项目身份不匹配。");
                continue;
            }
            var metadata = manifest.Package.Metadata;
            if (metadata.GetValueOrDefault("LogicalPatchKind") != LogicalPatchKind ||
                metadata.GetValueOrDefault("AdapterStatus", "Active") != "Active" ||
                metadata.GetValueOrDefault("AdapterOperation") != "Install") continue;
            if (!TryAddress(metadata, "PayloadAddress", out var payload) ||
                !TryAddress(metadata, "EntryAddress", out var entry) ||
                !TryAddress(metadata, "OriginalCallTarget", out var target))
            {
                DiagnosticsZh.Add(Path.GetFileName(path) + "：参数块、入口或原调用目标地址无法解析。");
                continue;
            }
            var calls = metadata.GetValueOrDefault("CallSites", string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => uint.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var parsed) ? parsed : 0)
                .Where(value => value != 0).ToList();
            try
            {
                var values = ReadValues(project, payload);
                var payloadSegment = manifest.Package.PatchSegments.FirstOrDefault(item => item.HookPoint == "原生桩参数适配器");
                var callSegments = manifest.Package.PatchSegments.Where(item => item.HookPoint == "原生桩调用重定向").ToList();
                if (payloadSegment == null || calls.Count == 0 || callSegments.Count != calls.Count)
                {
                    DiagnosticsZh.Add(Path.GetFileName(path) + "：载荷段或调用重定向数量不完整。");
                    continue;
                }
                var magic = EffectPatchByteService.ReadVirtualBytes(project, payload, 4);
                var magicBytes = EffectPatchByteService.ParseHex(magic);
                if (magicBytes.Length != 4 || Encoding.ASCII.GetString(magicBytes) != "CCNA")
                {
                    DiagnosticsZh.Add(Path.GetFileName(path) + "：CCNA 魔数不匹配，当前为 " + magic + "。");
                    continue;
                }
                var wrongCall = calls.FirstOrDefault(call => !EffectPatchByteService.IsDirectCallTo(project, call, entry));
                if (wrongCall != 0)
                {
                    DiagnosticsZh.Add(Path.GetFileName(path) + $"：调用点 {wrongCall:X8} 未指向适配器入口 {entry:X8}。");
                    continue;
                }
                result.Add(new ManagedNativeParameterAdapter
                {
                    AdapterId = metadata.GetValueOrDefault("ManagedAdapterId", manifest.ManifestId),
                    ManifestPath = path,
                    OriginalInstanceId = metadata.GetValueOrDefault("OriginalInstanceId", string.Empty),
                    OriginalDisplayNameZh = metadata.GetValueOrDefault("OriginalDisplayNameZh", manifest.Package.Name),
                    PayloadAddress = payload,
                    EntryAddress = entry,
                    OriginalCallTarget = target,
                    CallSites = calls,
                    PersonalEffectId = values[0], ItemEffectId = values[1], StackingMode = values[2], EffectValueMode = values[3],
                    OriginalPersonalEffectId = ParseInt(metadata, "OriginalPersonalEffectId"),
                    OriginalItemEffectId = ParseInt(metadata, "OriginalItemEffectId"),
                    OriginalStackingMode = ParseInt(metadata, "OriginalStackingMode"),
                    OriginalEffectValueMode = ParseInt(metadata, "OriginalEffectValueMode"),
                    PayloadSegment = payloadSegment,
                    CallSegments = callSegments
                });
            }
            catch (Exception ex)
            {
                DiagnosticsZh.Add(Path.GetFileName(path) + "：适配器复读失败，" + ex.Message);
            }
        }
        return result.GroupBy(item => item.AdapterId, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToList();
    }

    public byte[] BuildPayload(uint payloadAddress, int personal, int item, int stacking, int valueMode, uint target)
    {
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.ASCII.GetBytes("CCNA"));
        bytes.AddRange(BitConverter.GetBytes((ushort)1));
        bytes.AddRange(BitConverter.GetBytes((ushort)ParameterCount));
        bytes.AddRange(BitConverter.GetBytes(personal));
        bytes.AddRange(BitConverter.GetBytes(item));
        bytes.AddRange(BitConverter.GetBytes(stacking));
        bytes.AddRange(BitConverter.GetBytes(valueMode));
        for (var index = 0; index < ParameterCount; index++)
        {
            bytes.Add(0xA1); // mov eax,[absolute parameter]
            bytes.AddRange(BitConverter.GetBytes(checked(payloadAddress + 8u + (uint)(index * 4))));
            bytes.AddRange([0x89, 0x44, 0x24, checked((byte)(4 + index * 4))]);
        }
        bytes.AddRange(EffectPatchByteService.BuildRelativeJump(checked(payloadAddress + (uint)bytes.Count), target));
        return bytes.ToArray();
    }

    public void AddUpdateSegments(CczProject project, ManagedNativeParameterAdapter adapter, int personal, int item, int stacking, int valueMode, EffectPackage package)
    {
        var values = new[] { personal, item, stacking, valueMode };
        for (var index = 0; index < values.Length; index++)
        {
            var address = checked(adapter.PayloadAddress + 8u + (uint)(index * 4));
            var field = index switch
            {
                0 => "personal-effect-id",
                1 => "item-effect-id",
                2 => "stacking-mode",
                _ => "effect-value-mode"
            };
            package.PatchSegments.Add(new EffectPatchSegment
            {
                TargetFile = "Ekd5.exe", AddressKind = "OdVirtualAddress", Address = address,
                ExpectedOldBytesHex = EffectPatchByteService.ReadVirtualBytes(project, address, 4),
                BytesHex = EffectPatchByteService.ToHex(BitConverter.GetBytes(values[index])),
                HookPoint = "原生桩适配器参数", Comment = "复用既有受管理参数块，不分配新代码洞。",
                SemanticFieldId = $"managed-native-parameter:{adapter.AdapterId}:{field}",
                ModificationKind = EffectModificationKind.ManagedParameter,
                SourceLocationId = $"managed-native-parameter:0x{address:X8}:4",
                RequiredCapability = EffectWriteCapability.DirectParameter
            });
        }
        AddCommonMetadata(package, adapter, personal, item, stacking, valueMode);
        package.Metadata["AdapterOperation"] = "Update";
    }

    public void AddRestoreSegments(CczProject project, ManagedNativeParameterAdapter adapter, EffectPackage package)
    {
        foreach (var segment in adapter.CallSegments)
        {
            package.PatchSegments.Add(new EffectPatchSegment
            {
                TargetFile = segment.TargetFile, AddressKind = segment.AddressKind, Address = segment.Address,
                ExpectedOldBytesHex = EffectPatchByteService.ReadVirtualBytes(project, segment.Address, EffectPatchByteService.ParseHex(segment.BytesHex).Length),
                BytesHex = segment.ExpectedOldBytesHex, HookPoint = "恢复原生桩调用", Comment = "恢复适配器安装前的原始调用。",
                SemanticFieldId = $"managed-native-restore-call:{adapter.AdapterId}:0x{segment.Address:X8}",
                ModificationKind = EffectModificationKind.Maintenance,
                SourceLocationId = $"managed-native-restore-call:0x{segment.Address:X8}",
                RequiredCapability = EffectWriteCapability.RegisteredData
            });
        }
        var payloadLength = EffectPatchByteService.ParseHex(adapter.PayloadSegment.BytesHex).Length;
        package.PatchSegments.Add(new EffectPatchSegment
        {
            TargetFile = adapter.PayloadSegment.TargetFile, AddressKind = adapter.PayloadSegment.AddressKind, Address = adapter.PayloadAddress,
            ExpectedOldBytesHex = EffectPatchByteService.ReadVirtualBytes(project, adapter.PayloadAddress, payloadLength),
            BytesHex = adapter.PayloadSegment.ExpectedOldBytesHex, HookPoint = "释放原生桩参数适配器", Comment = "恢复代码洞原字节。",
            SemanticFieldId = $"managed-native-restore-payload:{adapter.AdapterId}",
            ModificationKind = EffectModificationKind.Maintenance,
            SourceLocationId = $"managed-native-restore-payload:0x{adapter.PayloadAddress:X8}",
            RequiredCapability = EffectWriteCapability.RegisteredData
        });
        var manifest = JsonSerializer.Deserialize<EffectManifest>(File.ReadAllText(adapter.ManifestPath, Encoding.UTF8), JsonOptions)
                       ?? throw new InvalidOperationException("受管理参数适配器清单无法读取。");
        manifest.Package.Metadata["AdapterStatus"] = "Released";
        manifest.Package.Metadata["ReleasedAt"] = DateTime.Now.ToString("O");
        UserBoundSignatureService.Sign(manifest, static (item, value) => item.Signature = value);
        var relative = Path.GetRelativePath(project.GameRoot, adapter.ManifestPath);
        package.PatchSegments.Add(new EffectPatchSegment
        {
            TargetFile = relative, AddressKind = "WholeFile", Address = 0,
            ExpectedOldBytesHex = EffectPatchByteService.ToHex(File.ReadAllBytes(adapter.ManifestPath)),
            BytesHex = EffectPatchByteService.ToHex(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, JsonOptions))),
            HookPoint = "释放参数适配器清单", Comment = "标记旧代码洞已释放，后续可重新分配。",
            SemanticFieldId = $"managed-native-restore-manifest:{adapter.AdapterId}",
            ModificationKind = EffectModificationKind.Maintenance,
            SourceLocationId = "managed-native-restore-manifest:" + relative,
            RequiredCapability = EffectWriteCapability.RegisteredData
        });
        AddCommonMetadata(package, adapter, adapter.OriginalPersonalEffectId, adapter.OriginalItemEffectId,
            adapter.OriginalStackingMode, adapter.OriginalEffectValueMode);
        package.Metadata["AdapterOperation"] = "Restore";
    }

    public void VerifyApplied(CczProject project, EffectPackage package)
    {
        if (!TryAddress(package.Metadata, "PayloadAddress", out var payload) ||
            !TryAddress(package.Metadata, "EntryAddress", out var entry)) return;
        if (package.Metadata.GetValueOrDefault("AdapterOperation") == "Restore")
        {
            foreach (var callText in package.Metadata.GetValueOrDefault("CallSites", string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!uint.TryParse(callText, System.Globalization.NumberStyles.HexNumber, null, out var call)) continue;
                if (EffectPatchByteService.IsDirectCallTo(project, call, entry))
                    throw new InvalidOperationException("参数适配器恢复后仍有调用指向旧入口。");
            }
            return;
        }
        var expected = new[]
        {
            ParseInt(package.Metadata, "CurrentPersonalEffectId"), ParseInt(package.Metadata, "CurrentItemEffectId"),
            ParseInt(package.Metadata, "CurrentStackingMode"), ParseInt(package.Metadata, "CurrentEffectValueMode")
        };
        if (!ReadValues(project, payload).SequenceEqual(expected))
            throw new InvalidOperationException("受管理参数块写后复读值不一致。");
        foreach (var callText in package.Metadata.GetValueOrDefault("CallSites", string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (uint.TryParse(callText, System.Globalization.NumberStyles.HexNumber, null, out var call) &&
                !EffectPatchByteService.IsDirectCallTo(project, call, entry))
                throw new InvalidOperationException($"原消费者调用点 {call:X8} 没有指向受管理参数适配器。");
        }
    }

    public static void AddCommonMetadata(EffectPackage package, ManagedNativeParameterAdapter adapter, int personal, int item, int stacking, int valueMode)
    {
        package.Metadata["LogicalPatchKind"] = LogicalPatchKind;
        package.Metadata["ManagedAdapterId"] = adapter.AdapterId;
        package.Metadata["AdapterStatus"] = "Active";
        package.Metadata["OriginalInstanceId"] = adapter.OriginalInstanceId;
        package.Metadata["OriginalDisplayNameZh"] = adapter.OriginalDisplayNameZh;
        package.Metadata["PayloadAddress"] = $"0x{adapter.PayloadAddress:X8}";
        package.Metadata["EntryAddress"] = $"0x{adapter.EntryAddress:X8}";
        package.Metadata["OriginalCallTarget"] = $"0x{adapter.OriginalCallTarget:X8}";
        package.Metadata["CallSites"] = string.Join(",", adapter.CallSites.Select(item => item.ToString("X8")));
        package.Metadata["OriginalPersonalEffectId"] = adapter.OriginalPersonalEffectId.ToString();
        package.Metadata["OriginalItemEffectId"] = adapter.OriginalItemEffectId.ToString();
        package.Metadata["OriginalStackingMode"] = adapter.OriginalStackingMode.ToString();
        package.Metadata["OriginalEffectValueMode"] = adapter.OriginalEffectValueMode.ToString();
        package.Metadata["CurrentPersonalEffectId"] = personal.ToString();
        package.Metadata["CurrentItemEffectId"] = item.ToString();
        package.Metadata["CurrentStackingMode"] = stacking.ToString();
        package.Metadata["CurrentEffectValueMode"] = valueMode.ToString();
    }

    private static int[] ReadValues(CczProject project, uint payload)
    {
        var raw = EffectPatchByteService.ParseHex(EffectPatchByteService.ReadVirtualBytes(project, checked(payload + 8), 16));
        return Enumerable.Range(0, 4).Select(index => BitConverter.ToInt32(raw, index * 4)).ToArray();
    }

    private static bool TryAddress(IReadOnlyDictionary<string, string> metadata, string key, out uint value)
    {
        var text = metadata.GetValueOrDefault(key, string.Empty).Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out value);
    }

    private static int ParseInt(IReadOnlyDictionary<string, string> metadata, string key)
        => int.TryParse(metadata.GetValueOrDefault(key, "0"), out var value) ? value : 0;
}

public sealed class ManagedNativeParameterAdapter
{
    public string AdapterId { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public string OriginalInstanceId { get; set; } = string.Empty;
    public string OriginalDisplayNameZh { get; set; } = string.Empty;
    public uint PayloadAddress { get; set; }
    public uint EntryAddress { get; set; }
    public uint OriginalCallTarget { get; set; }
    public List<uint> CallSites { get; set; } = [];
    public int PersonalEffectId { get; set; }
    public int ItemEffectId { get; set; }
    public int StackingMode { get; set; }
    public int EffectValueMode { get; set; }
    public int OriginalPersonalEffectId { get; set; }
    public int OriginalItemEffectId { get; set; }
    public int OriginalStackingMode { get; set; }
    public int OriginalEffectValueMode { get; set; }
    public EffectPatchSegment PayloadSegment { get; set; } = new();
    public List<EffectPatchSegment> CallSegments { get; set; } = [];
}
