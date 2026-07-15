using System.Globalization;
using System.Security.Cryptography;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using Iced.Intel;

namespace CCZModStudio.Core;

public sealed class EffectParameterAdapterService
{
    public EffectIdUpdatePreview Preview(CczProject project, EffectIdLocationRecord location, int newValue)
    {
        var result = new EffectIdUpdatePreview { Location = location };
        if (location.WriteCapability != EffectIdWriteCapability.AdapterRequired)
            result.WarningsZh.Add("该位置没有声明需要参数适配器。");
        if (newValue is < 0 or > 0xFF) result.WarningsZh.Add("特效号必须在 00-FF 范围内。");
        if (!location.InstructionAddress.HasValue) result.WarningsZh.Add("位置缺少参数定义指令地址。");
        if (!location.EvidenceExeSha256.Equals(new EnginePatchProfileService().Build(project).ExeSha256, StringComparison.OrdinalIgnoreCase))
            result.WarningsZh.Add("位置证据的 EXE 摘要与当前项目不一致。");
        if (result.WarningsZh.Count > 0) return Finish(result);

        var hookAddress = location.InstructionAddress!.Value;
        var owners = new EffectIdLocationIndexService().Scan(project, location.TargetFile, exportReports: false).Locations
            .Where(item => item.InstructionAddress == hookAddress)
            .Select(item => item.OwnerInstanceId)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (owners.Count > 1)
        {
            result.WarningsZh.Add($"该参数定义被 {owners.Count} 个逻辑特效共享，修改影响范围不唯一，不能自动安装适配器。");
            return Finish(result);
        }
        var targetPath = project.ResolveGameFile(location.TargetFile);
        var executable = ExecutableAnalysisSnapshotCache.Shared.GetBase(targetPath);
        var pe = executable.PeImage;
        var scan = executable.InstructionScan;
        if (!scan.InstructionsByAddress.TryGetValue(hookAddress, out var defining) ||
            defining.Mnemonic != "push" || defining.ImmediateSize != 1 || defining.Bytes.Length != 2 || defining.Bytes[0] != 0x6A)
        {
            result.WarningsZh.Add("定义位置不是可识别的 push imm8 指令。");
            return Finish(result);
        }

        var section = scan.InstructionsBySection[defining.SectionName];
        var index = section.FindIndex(item => item.Address == hookAddress);
        var selected = new List<X86InstructionInfo>();
        var length = 0;
        for (var i = index; i >= 0 && i < section.Count && length < 5; i++)
        {
            var instruction = section[i];
            if (instruction.IsReturn || instruction.IsIndirectJump || instruction.IsConditionalBranch || instruction.IsDirectJump)
            {
                result.WarningsZh.Add("适配覆盖窗口包含分支、返回或间接跳转，不能自动搬迁。");
                return Finish(result);
            }
            selected.Add(instruction);
            length += instruction.Length;
        }
        if (length < 5)
        {
            result.WarningsZh.Add("无法从参数定义位置取得至少 5 字节的完整指令窗口。");
            return Finish(result);
        }
        var returnAddress = checked(hookAddress + (uint)length);
        if (scan.Instructions.Any(item => item.BranchTarget.HasValue && item.BranchTarget.Value > hookAddress && item.BranchTarget.Value < returnAddress))
        {
            result.WarningsZh.Add("存在跳入适配覆盖窗口中部的控制流，不能自动安装适配器。");
            return Finish(result);
        }

        var scanner = new ExeCodeCaveScanner();
        var caveScan = scanner.Scan(project, location.TargetFile, minimumLength: 24, includeZeroFill: false);
        var engine = new EnginePatchProfileService().Build(project);
        var registry = new CodeCaveRegistry();
        var allocation = registry.Allocate(caveScan, engine, new CodeCaveAllocationRequest
        {
            RequiredBytes = 32,
            ReserveBytes = 8,
            ExistingAllocations = registry.LoadExistingAllocations(project, location.TargetFile),
            AllocatorPolicy = "smallest-fit"
        });
        if (!allocation.Success || allocation.Allocation == null)
        {
            result.WarningsZh.Add("没有足够的安全代码洞用于宽立即数参数适配器。" + allocation.Reason);
            return Finish(result);
        }

        var cave = allocation.Allocation.StartVirtualAddress;
        byte[] relocated;
        try { relocated = Relocate(selected.Skip(1).ToList(), cave + 5); }
        catch (Exception ex) { result.WarningsZh.Add("原指令搬迁失败：" + ex.Message); return Finish(result); }
        var code = new List<byte> { 0x68 };
        code.AddRange(BitConverter.GetBytes(newValue));
        code.AddRange(relocated);
        var jumpAddress = checked(cave + (uint)code.Count);
        code.AddRange(EffectPatchByteService.BuildRelativeJump(jumpAddress, returnAddress));
        var hook = EffectPatchByteService.BuildRelativeJump(hookAddress, cave).Concat(Enumerable.Repeat((byte)0x90, length - 5)).ToArray();
        var oldWindow = EffectPatchByteService.ReadVirtualBytes(project, hookAddress, length, location.TargetFile);
        var oldCave = EffectPatchByteService.ReadVirtualBytes(project, cave, code.Count, location.TargetFile);
        var manifestId = $"effect-parameter-adapter-{location.LocationId}-{DateTime.Now:yyyyMMddHHmmssfff}";
        var adapterSourceHash = Convert.ToHexString(SHA256.HashData(code.ToArray()));
        result.Package = new EffectPackage
        {
            SchemaVersion = "2.0", PackageId = manifestId, Domain = "patch", EffectId = newValue,
            Name = "特效号宽参数适配器", Description = $"{location.OwnerNameZh}：{location.ParameterRoleZh}改为 {newValue:X2}",
            BackupNote = "参数适配器完整搬迁覆盖窗口；移除时按 manifest 恢复入口和代码洞。",
            Metadata =
            {
                ["LogicalPatchKind"] = "effect-parameter-adapter-v2",
                ["AdapterPreviewPassed"] = "false",
                ["LocationId"] = location.LocationId,
                ["AdapterManifestId"] = manifestId,
                ["EngineProfileSha256"] = engine.ExeSha256,
                ["HookAddress"] = $"0x{hookAddress:X8}",
                ["ReturnAddress"] = $"0x{returnAddress:X8}",
                ["ParameterAddress"] = $"0x{cave + 1:X8}",
                ["OriginalWindowBytes"] = oldWindow,
                ["DefinitionChain"] = string.Join("；", location.DefinitionChain)
            },
            PatchSegments =
            {
                new EffectPatchSegment
                {
                    TargetFile = location.TargetFile, AddressKind = "OdVirtualAddress", Address = hookAddress,
                    BytesHex = EffectPatchByteService.ToHex(hook), ExpectedOldBytesHex = oldWindow,
                    CodeCaveId = allocation.Allocation.CaveId, HookPoint = "特效号参数适配入口",
                    EngineProfileSha256 = engine.ExeSha256, Comment = "完整指令窗口跳转，不截断原指令。",
                    SemanticFieldId = "wide-effect-parameter-hook:" + location.LocationId,
                    ModificationKind = EffectModificationKind.SignedImmediateAdapter,
                    SourceLocationId = location.LocationId + ":hook",
                    RequiredCapability = EffectWriteCapability.Adapter,
                    AssemblySourceHash = adapterSourceHash
                },
                new EffectPatchSegment
                {
                    TargetFile = location.TargetFile, AddressKind = "OdVirtualAddress", Address = cave,
                    BytesHex = EffectPatchByteService.ToHex(code), ExpectedOldBytesHex = oldCave,
                    CodeCaveId = allocation.Allocation.CaveId, HookPoint = "特效号宽参数适配器",
                    EngineProfileSha256 = engine.ExeSha256, Comment = "push imm32、搬迁原指令和显式回跳。",
                    SemanticFieldId = "wide-effect-parameter-adapter:" + location.LocationId,
                    ModificationKind = EffectModificationKind.SignedImmediateAdapter,
                    SourceLocationId = location.LocationId + ":code-cave",
                    RequiredCapability = EffectWriteCapability.Adapter,
                    AssemblySourceHash = adapterSourceHash
                }
            }
        };
        try { registry.EnsureNoPatchSegmentOverlap(result.Package.PatchSegments); }
        catch (Exception ex) { result.WarningsZh.Add(ex.Message); return Finish(result); }
        result.PatchPreview = new EffectTransactionalPatchService().Preview(project, result.Package);
        result.WarningsZh.AddRange(result.PatchPreview.Warnings);
        result.CanApply = result.WarningsZh.Count == 0 && result.PatchPreview.CanApply;
        result.Package.Metadata["AdapterPreviewPassed"] = result.CanApply ? "true" : "false";
        if (result.CanApply)
            new LockedEffectWriteReceiptService().Issue(project, result.Package, "effect-parameter-adapter");
        return Finish(result);
    }

    public EffectTransactionalApplyResult Apply(CczProject project, EffectPackage package)
    {
        if (package.Metadata.GetValueOrDefault("LogicalPatchKind") != "effect-parameter-adapter-v2" ||
            package.Metadata.GetValueOrDefault("AdapterPreviewPassed") != "true")
            throw new InvalidOperationException("只能应用由宽参数适配预览生成并通过校验的锁定包。");
        return new EffectTransactionalPatchService().Apply(project, package, "effect-parameter-adapter");
    }

    private static EffectIdUpdatePreview Finish(EffectIdUpdatePreview result)
    {
        result.SummaryZh = result.CanApply
            ? "参数适配预览通过：将安装宽立即数适配器并保留完整原指令。"
            : result.WarningsZh.Count == 0 ? "参数适配预览未通过。" : "参数适配预览未通过：" + string.Join("；", result.WarningsZh.Take(8));
        return result;
    }

    private static byte[] Relocate(IReadOnlyList<X86InstructionInfo> source, uint address)
    {
        if (source.Count == 0) return [];
        var raw = source.SelectMany(item => item.Bytes).ToArray();
        var decoder = Decoder.Create(32, raw, source[0].Address, DecoderOptions.None);
        var instructions = new List<Instruction>();
        while (decoder.IP < source[0].Address + (uint)raw.Length)
        {
            var instruction = decoder.Decode();
            if (instruction.Length == 0) break;
            instructions.Add(instruction);
        }
        var writer = new BufferWriter();
        if (!BlockEncoder.TryEncode(32, new InstructionBlock(writer, instructions, address), out var error, out _))
            throw new InvalidOperationException(error ?? "Iced BlockEncoder 未提供详细错误。");
        return writer.Bytes.ToArray();
    }

    private sealed class BufferWriter : CodeWriter
    {
        public List<byte> Bytes { get; } = [];
        public override void WriteByte(byte value) => Bytes.Add(value);
    }
}
