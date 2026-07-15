using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed partial class AssemblyPatchCompiler
{
    public AssemblyPatchPreviewResult Preview(
        CczProject project,
        AssemblyPatchDraft draft,
        string allocatorPolicy = "smallest-fit")
    {
        NormalizeDraft(draft);
        var scanner = new ExeCodeCaveScanner();
        var registry = new CodeCaveRegistry();
        var profileService = new EnginePatchProfileService();
        var effectPackageService = new EffectPackageService();

        var scan = scanner.Scan(project, draft.TargetFile, minimumLength: 8, includeZeroFill: true);
        var profile = profileService.Build(project);
        var allocation = registry.Allocate(scan, profile, new CodeCaveAllocationRequest
        {
            RequiredBytes = Math.Max(draft.RequiredCodeCaveBytes, 5),
            ReserveBytes = 8,
            AllocatorPolicy = allocatorPolicy,
            AllowZeroFillCave = false,
            AllowMixedFillCave = false,
            ExistingAllocations = registry.LoadExistingAllocations(project, draft.TargetFile)
        });

        var result = new AssemblyPatchPreviewResult
        {
            Draft = draft,
            Allocation = allocation,
            CanApply = false
        };

        if (!allocation.Success || allocation.Allocation == null)
        {
            result.Warnings.Add(allocation.Reason);
            result.Summary = "汇编补丁预览失败：" + allocation.Reason;
            return result;
        }

        var caveAddress = allocation.Allocation.StartVirtualAddress;
        var hookSafety = new HookSafetyAnalyzer().Analyze(project, draft, caveAddress);
        result.HookSafety = hookSafety;
        if (!hookSafety.IsSafe)
        {
            result.Warnings.AddRange(hookSafety.Warnings);
            result.Summary = "汇编补丁预览失败：" + hookSafety.Summary;
            return result;
        }

        // HookSafetyAnalyzer may widen the overwrite window to full instructions.
        // Synchronize the old-byte lock and all downstream return/manifest fields.
        draft.OverwriteLength = hookSafety.RequiredOverwriteLength;
        draft.ExpectedOldBytesHex = hookSafety.CurrentBytesHex;
        draft.ReturnAddress = hookSafety.ReturnAddress;
        draft.ReturnAddressHex = $"0x{hookSafety.ReturnAddress:X8}";

        var source = BuildSource(draft, caveAddress, hookSafety.RelocatedOriginalBytes);
        var codeBytes = CompileSource(source, caveAddress);
        if (codeBytes.Length > allocation.Allocation.Length)
        {
            result.Warnings.Add($"Compiled code is {codeBytes.Length} bytes but allocated cave length is {allocation.Allocation.Length} bytes.");
            result.Summary = "汇编补丁预览失败：编译后的代码超过已分配代码洞。";
            return result;
        }

        var executableCodeBytes = SliceExecutableCode(codeBytes, draft.Metadata.GetValueOrDefault("EmbeddedDataMagic"));
        var compiledWarnings = ValidateCompiledControlFlow(executableCodeBytes, caveAddress, hookSafety.ReturnAddress, profile);
        if (draft.Metadata.TryGetValue("SemanticProgramJson", out var semanticJson) && !string.IsNullOrWhiteSpace(semanticJson))
        {
            try
            {
                var program = System.Text.Json.JsonSerializer.Deserialize<SemanticEffectProgram>(semanticJson)
                              ?? throw new InvalidOperationException("语义程序为空。");
                var contract = new HookExecutionContractService().Read(project, program.HookContractId);
                var semanticValidation = new SemanticPatchValidator().ValidateCompiled(executableCodeBytes, caveAddress, contract, hookSafety.ReturnAddress);
                if (!semanticValidation.IsValid) compiledWarnings.AddRange(semanticValidation.WarningsZh);
            }
            catch (Exception ex)
            {
                compiledWarnings.Add("语义补丁编译后验证失败：" + ex.Message);
            }
        }
        if (compiledWarnings.Count > 0)
        {
            result.Warnings.AddRange(compiledWarnings);
            result.Summary = "汇编补丁预览失败：编译后的控制流校验未通过。";
            return result;
        }

        var hookBytes = BuildHookBytes(draft.HookAddress, caveAddress, draft.OverwriteLength);
        var codeCaveExpectedOldBytesHex = ReadExpectedOldBytes(project, draft.TargetFile, allocation.Allocation.StartVirtualAddress, codeBytes.Length);
        var package = BuildPackage(project, draft, profile, allocation.Allocation, hookBytes, codeBytes, codeCaveExpectedOldBytesHex, source);
        try
        {
            registry.EnsureNoPatchSegmentOverlap(package.PatchSegments);
        }
        catch (Exception ex)
        {
            result.Warnings.Add(ex.Message);
            result.Summary = "汇编补丁预览失败：写入段发生重叠。";
            return result;
        }

        var patchPreview = effectPackageService.PreviewPatch(project, package);
        result.Package = package;
        result.CodeCaveBytes = codeBytes;
        result.HookBytes = hookBytes;
        result.DisassemblyPreview = TryDisassemble(codeBytes, caveAddress);
        result.PatchPreview = patchPreview;
        result.Warnings.AddRange(patchPreview.Warnings);
        result.CanApply = patchPreview.CanApply;
        package.Metadata["AssemblyPatchPreviewPassed"] = result.CanApply ? "true" : "false";
        if (result.CanApply)
            new LockedEffectWriteReceiptService().Issue(project, package, "assembly-patch");
        result.Summary = $"汇编补丁预览：可写入={(result.CanApply ? "是" : "否")}，代码洞=0x{caveAddress:X8}，代码长度={codeBytes.Length} 字节，警告={result.Warnings.Count} 条。";
        return result;
    }

    public AssemblyPatchApplyResult Apply(CczProject project, EffectPackage compiledPackage)
    {
        if (!compiledPackage.Metadata.TryGetValue("AssemblyPatchPreviewPassed", out var passed) ||
            !passed.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("汇编补丁包不是由成功的汇编预览生成的。");
        }

        var currentProfile = new EnginePatchProfileService().Build(project);
        if (!compiledPackage.Metadata.TryGetValue("EngineProfileSha256", out var expectedSha256) ||
            string.IsNullOrWhiteSpace(expectedSha256) ||
            !expectedSha256.Equals(currentProfile.ExeSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"汇编补丁的 EXE 摘要锁校验失败。预期 {expectedSha256}，实际 {currentProfile.ExeSha256}。");
        }

        var effectPackageService = new EffectPackageService();
        var apply = effectPackageService.ApplyPatch(project, compiledPackage);
        return new AssemblyPatchApplyResult
        {
            Applied = true,
            Summary = apply.Summary,
            PatchApplyResult = apply
        };
    }

    public AssemblyPatchDraft Draft(string prompt, string? engineVersion, int? effectId, string? hookHint)
    {
        var draft = new AssemblyPatchDraft
        {
            Prompt = prompt ?? string.Empty,
            EngineVersion = string.IsNullOrWhiteSpace(engineVersion) ? "6.5" : engineVersion!,
            EffectId = effectId ?? 0,
            HookPoint = string.IsNullOrWhiteSpace(hookHint) ? "manual-review-required" : hookHint!,
            RequiredCodeCaveBytes = 16,
            AssemblySource = "nop\njmp {return}",
            RegisterStrategy = "Draft only: no register mutation except eip through jmp.",
            Risks =
            {
                "自动草案未选择真实业务 hook 点，必须由后续 preview 输入 HookAddress/ExpectedOldBytes 后才能编译。",
                "复杂机制需要基于知识库和 x32dbg 证据补全汇编。"
            },
            DynamicValidationPlan =
            {
                "Set a breakpoint at the hook point and at the allocated code cave entry.",
                "Capture registers, stack, nearby memory, and battle state for the triggering action."
            }
        };
        return draft;
    }

    public AssemblyPatchDraft Draft(CczProject project, string prompt, string? engineVersion, int? effectId, string? hookHint)
    {
        var profile = new EnginePatchProfileService().Build(project);
        var template = SelectDraftTemplate(prompt, hookHint, profile);
        var draft = new AssemblyPatchDraft
        {
            Prompt = prompt ?? string.Empty,
            TargetFile = "Ekd5.exe",
            EngineVersion = string.IsNullOrWhiteSpace(engineVersion) ? profile.EngineVersion : engineVersion!,
            EffectId = effectId ?? 0,
            HookPoint = template.HookPoint,
            HookAddress = template.HookAddress,
            HookAddressHex = template.HookAddress == 0 ? string.Empty : $"0x{template.HookAddress:X8}",
            OverwriteLength = template.OverwriteLength,
            ExpectedOldBytesHex = template.HookAddress == 0 ? string.Empty : TryReadExpectedOldBytes(project, "Ekd5.exe", template.HookAddress, template.OverwriteLength),
            ReturnAddress = template.HookAddress == 0 ? 0 : checked(template.HookAddress + (uint)template.OverwriteLength),
            ReturnAddressHex = template.HookAddress == 0 ? string.Empty : $"0x{checked(template.HookAddress + (uint)template.OverwriteLength):X8}",
            AssemblySource = template.AssemblySource,
            RequiredCodeCaveBytes = template.RequiredCodeCaveBytes,
            RegisterStrategy = template.RegisterStrategy,
            Risks =
            {
                "Draft only: it never writes EXE files and must pass preview_assembly_patch before apply.",
                "Natural-language intent is not proof of correctness; business logic must be reviewed against the local knowledge base.",
                template.Risk
            },
            DynamicValidationPlan =
            {
                $"Set a breakpoint at {template.HookPoint}.",
                "Set a breakpoint at the allocated code cave entry after preview.",
                "Capture registers, stack, nearby memory, and battle state for the triggering action."
            },
            Metadata =
            {
                ["TemplateId"] = template.TemplateId,
                ["TemplateSource"] = "EnginePatchProfile + local injection index",
                ["UserPromptInterpretation"] = template.Interpretation,
                ["DraftWorkflow"] = "profile-keyword",
                ["PreviewRequired"] = "true",
                ["ApplyRequiresPreviewPackage"] = "true",
                ["EngineProfileSha256"] = profile.ExeSha256,
                ["EngineKnown"] = profile.IsKnown ? "true" : "false",
                ["ProfileHookCount"] = profile.HookPoints.Count.ToString(CultureInfo.InvariantCulture),
                ["ProfileReservedRangeCount"] = profile.ReservedRanges.Count.ToString(CultureInfo.InvariantCulture)
            }
        };

        foreach (var dependency in template.Dependencies)
        {
            draft.Dependencies.Add(dependency);
        }

        return draft;
    }

    private static DraftTemplate SelectDraftTemplate(string? prompt, string? hookHint, EnginePatchProfile profile)
    {
        var text = ((prompt ?? string.Empty) + " " + (hookHint ?? string.Empty)).ToLowerInvariant();
        if (ContainsAny(text, "回mp", "回 mp", "mp回复", "回复mp", "吸蓝", "physical_after_damage_mp_restore"))
        {
            return KnownTemplate("known-65-mp-restore", "physical_after_damage_mp_restore", profile, 0x00418335, 5, 64,
                "Physical after-damage MP restore hook from the 6.5 injection index.",
                "Known sample variants overlap at 0x004528FC-0x004529A6; preview must allocate a fresh cave or intentionally import that package.",
                ["damage context", "effect value lookup"]);
        }

        if (ContainsAny(text, "噬心", "随机状态", "异常状态", "中毒", "麻痹", "禁咒", "混乱", "strategy_after_damage_random_status"))
        {
            return KnownTemplate("known-65-random-status", "strategy_after_damage_random_status", profile, 0x004259AF, 5, 192,
                "Strategy after-damage random status hook from the 6.5 injection index.",
                "This hook chains with strategy money at 0x004259B4; preserve return flow and verify strategy damage context.",
                ["strategy damage context", "status application routine", "effect value lookup"]);
        }

        if (ContainsAny(text, "策略偷钱", "偷钱", "金钱", "strategy_after_damage_money"))
        {
            return KnownTemplate("known-65-strategy-money", "strategy_after_damage_money", profile, 0x004259B4, 5, 144,
                "Strategy after-damage money modification hook from the 6.5 injection index.",
                "This can chain after random-status at 0x004259AF; verify money address and signed arithmetic.",
                ["strategy damage context", "money address 0x004B077C"]);
        }

        if (ContainsAny(text, "殭屍", "僵尸", "殭尸", "mp防御", "zombie_mp_defense"))
        {
            return KnownTemplate("known-65-zombie-mp-defense", "zombie_mp_defense", profile, 0x00406006, 5, 64,
                "Zombie MP-defense hook from the 6.5 injection index.",
                "Original sample range overlaps guard-final; never import both unchanged.",
                ["unit current HP", "unit current MP"]);
        }

        if (ContainsAny(text, "护卫", "護衛", "guard_final"))
        {
            return KnownTemplate("known-65-guard-final", "guard_final_a", profile, 0x004105C8, 5, 288,
                "Guard-final multi-entry mechanism from the 6.5 injection index.",
                "High risk: guard-final has multiple hook entries and overlaps zombie MP-defense sample code; dynamic x32dbg validation is mandatory.",
                ["unit array", "range check", "target redirection"]);
        }

        if (ContainsAny(text, "无视策略减伤", "策略减伤", "ignore_strategy_reduction"))
        {
            return KnownTemplate("known-65-ignore-strategy-reduction", "ignore_strategy_reduction_a", profile, 0x0043C242, 5, 64,
                "Ignore strategy damage reduction hook from the 6.5 injection index.",
                "There are two related hook points; this draft covers the first and requires manual review for the second.",
                ["strategy damage formula", "effect value lookup"]);
        }

        if (ContainsAny(text, "策略保底", "策略限伤", "保底", "限伤", "strategy_floor_cap"))
        {
            return KnownTemplate("known-65-strategy-floor-cap", "strategy_floor_cap", profile, 0x0043C2D5, 5, 192,
                "Strategy damage floor/cap hook from the 6.5 injection index.",
                "High risk formula hook: verify floor/cap ordering and percent/fixed-value semantics.",
                ["strategy damage formula", "effect value lookup"]);
        }

        var manualHook = string.IsNullOrWhiteSpace(hookHint) ? "manual-review-required" : hookHint!;
        return new DraftTemplate(
            TemplateId: "manual-review-required",
            HookPoint: manualHook,
            HookAddress: 0,
            OverwriteLength: 5,
            RequiredCodeCaveBytes: 32,
            AssemblySource: "nop\njmp {return}",
            RegisterStrategy: "Draft only: no register mutation except eip through jmp.",
            Interpretation: "No trusted 6.5 injection-index template matched the request. Fill hook address and expected old bytes manually after knowledge-base review.",
            Risk: "No trusted hook was selected automatically; preview will reject this draft until HookAddress and ExpectedOldBytesHex are supplied.",
            Dependencies: []);
    }

    private static DraftTemplate KnownTemplate(string templateId, string hookPoint, EnginePatchProfile profile, uint fallbackAddress, int overwriteLength, int requiredBytes, string interpretation, string risk, List<string> dependencies)
    {
        var address = fallbackAddress;
        if (profile.HookPoints.TryGetValue(hookPoint, out var text) && TryParseUInt(text, out var parsed))
        {
            address = parsed;
        }

        return new DraftTemplate(
            TemplateId: templateId,
            HookPoint: hookPoint,
            HookAddress: address,
            OverwriteLength: overwriteLength,
            RequiredCodeCaveBytes: requiredBytes,
            AssemblySource: "nop\njmp {return}",
            RegisterStrategy: "Use eax/ecx/edx for scratch work; preserve ebx/esi/edi/ebp unless the reviewed template proves otherwise.",
            Interpretation: interpretation,
            Risk: risk,
            Dependencies: dependencies);
    }

    private static string TryReadExpectedOldBytes(CczProject project, string targetFile, uint hookAddress, int length)
    {
        try
        {
            return ReadExpectedOldBytes(project, targetFile, hookAddress, length);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ReadExpectedOldBytes(CczProject project, string targetFile, uint virtualAddress, int length)
    {
        if (length < 0) throw new InvalidOperationException("Byte length must not be negative.");
        if (length == 0) return string.Empty;

        var exePath = project.ResolveGameFile(string.IsNullOrWhiteSpace(targetFile) ? "Ekd5.exe" : targetFile);
        var mapper = PeAddressMapper.Load(exePath);
        var offset = mapper.VirtualAddressToFileOffset(virtualAddress);
        var bytes = File.ReadAllBytes(exePath);
        if (offset < 0 || offset + length > bytes.LongLength)
        {
            throw new InvalidOperationException(
                $"Cannot read expected old bytes at 0x{virtualAddress:X8}: file offset 0x{offset:X} length {length} exceeds target file.");
        }

        return ToHex(bytes.Skip(checked((int)offset)).Take(length).ToArray());
    }

    private static bool ContainsAny(string text, params string[] needles)
        => needles.Any(needle => text.Contains(needle.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase));

    private static EffectPackage BuildPackage(
        CczProject project,
        AssemblyPatchDraft draft,
        EnginePatchProfile profile,
        AllocatedCodeCaveRange allocation,
        byte[] hookBytes,
        byte[] codeBytes,
        string codeCaveExpectedOldBytesHex,
        string source)
    {
        var sourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        var package = new EffectPackage
        {
            PackageId = $"assembly-patch-{draft.EffectId}-{DateTime.Now:yyyyMMddHHmmssfff}",
            Domain = "patch",
            EffectId = draft.EffectId,
            Name = string.IsNullOrWhiteSpace(draft.HookPoint) ? "汇编补丁" : draft.HookPoint,
            Description = string.IsNullOrWhiteSpace(draft.Prompt) ? "Generated assembly patch." : draft.Prompt,
            SourcePrompt = draft.Prompt,
            BackupNote = "汇编补丁由预览流程生成；需要恢复时使用 manifest 记录的备份。",
            Metadata =
            {
                ["AssemblyPatch"] = "true",
                ["AssemblyPatchPreviewPassed"] = "false",
                ["AssemblySource"] = source,
                ["AssemblySourceHash"] = sourceHash,
                ["AllocatedRange"] = $"{allocation.StartVirtualAddressHex}-{allocation.EndVirtualAddressHex}",
                ["EngineVersion"] = profile.EngineVersion,
                ["EngineProfileSha256"] = profile.ExeSha256,
                ["ProjectRoot"] = project.GameRoot,
                ["HookPoint"] = draft.HookPoint,
                ["HookAddress"] = $"0x{draft.HookAddress:X8}",
                ["ReturnAddress"] = $"0x{(draft.ReturnAddress == 0 ? checked(draft.HookAddress + (uint)draft.OverwriteLength) : draft.ReturnAddress):X8}",
                ["OverwriteLength"] = draft.OverwriteLength.ToString(CultureInfo.InvariantCulture),
                ["RequiredCodeCaveBytes"] = draft.RequiredCodeCaveBytes.ToString(CultureInfo.InvariantCulture),
                ["RegisterStrategy"] = draft.RegisterStrategy,
                ["HookContractId"] = draft.HookContractId,
                ["OriginalInstructionPolicy"] = draft.OriginalInstructionPolicy,
                ["OriginalInstructionPlacement"] = draft.OriginalInstructionPlacement,
                ["PreserveFlags"] = draft.PreserveFlags ? "true" : "false",
                ["ExpectedStackDelta"] = draft.ExpectedStackDelta.ToString(CultureInfo.InvariantCulture),
                ["RequiredSymbols"] = string.Join("; ", draft.RequiredSymbols),
                ["Dependencies"] = string.Join("; ", draft.Dependencies),
                ["Risks"] = string.Join("; ", draft.Risks),
                ["DynamicValidationPlan"] = string.Join("; ", draft.DynamicValidationPlan)
            }
        };

        foreach (var pair in draft.Metadata)
        {
            package.Metadata[pair.Key] = pair.Value;
        }

        package.PatchSegments.Add(new EffectPatchSegment
        {
            TargetFile = draft.TargetFile,
            AddressKind = "OdVirtualAddress",
            Address = draft.HookAddress,
            AddressHex = $"0x{draft.HookAddress:X8}",
            BytesHex = ToHex(hookBytes),
            ExpectedOldBytesHex = draft.ExpectedOldBytesHex,
            HookPoint = draft.HookPoint,
            CodeCaveId = allocation.CaveId,
            AssemblySourceHash = sourceHash,
            AllocatedRange = $"{allocation.StartVirtualAddressHex}-{allocation.EndVirtualAddressHex}",
            EngineProfileSha256 = profile.ExeSha256,
            Comment = "汇编补丁入口跳转段。"
        });

        package.PatchSegments.Add(new EffectPatchSegment
        {
            TargetFile = draft.TargetFile,
            AddressKind = "OdVirtualAddress",
            Address = allocation.StartVirtualAddress,
            AddressHex = allocation.StartVirtualAddressHex,
            BytesHex = ToHex(codeBytes),
            ExpectedOldBytesHex = codeCaveExpectedOldBytesHex,
            HookPoint = draft.HookPoint,
            CodeCaveId = allocation.CaveId,
            AssemblySourceHash = sourceHash,
            AllocatedRange = $"{allocation.StartVirtualAddressHex}-{allocation.EndVirtualAddressHex}",
            EngineProfileSha256 = profile.ExeSha256,
            Comment = "汇编补丁代码洞主体。"
        });

        return package;
    }

    private static string BuildSource(AssemblyPatchDraft draft, uint caveAddress, byte[] relocatedOriginalBytes)
    {
        var returnAddress = draft.ReturnAddress == 0 ? checked(draft.HookAddress + (uint)draft.OverwriteLength) : draft.ReturnAddress;
        var source = (draft.AssemblySource ?? string.Empty)
            .Replace("{return}", $"0x{returnAddress:X8}", StringComparison.OrdinalIgnoreCase)
            .Replace("{hook}", $"0x{draft.HookAddress:X8}", StringComparison.OrdinalIgnoreCase)
            .Replace("{cave}", $"0x{caveAddress:X8}", StringComparison.OrdinalIgnoreCase);
        if (relocatedOriginalBytes.Length == 0) return source.Replace("{original}", string.Empty, StringComparison.OrdinalIgnoreCase);
        var original = "db " + string.Join(", ", relocatedOriginalBytes.Select(value => $"0x{value:X2}"));
        if (source.Contains("{original}", StringComparison.OrdinalIgnoreCase))
        {
            return source.Replace("{original}", original, StringComparison.OrdinalIgnoreCase);
        }

        return original + "\n" + source;
    }

    private static byte[] CompileSource(string source, uint origin)
    {
        return CompileWithNasm(source, origin);
    }

    private static byte[] SliceExecutableCode(byte[] bytes, string? magic)
    {
        if (string.IsNullOrWhiteSpace(magic)) return bytes;
        var marker = Encoding.ASCII.GetBytes(magic);
        for (var index = 0; index <= bytes.Length - marker.Length; index++)
        {
            if (bytes.AsSpan(index, marker.Length).SequenceEqual(marker))
                return bytes[..index];
        }
        return bytes;
    }

    private static List<string> ValidateCompiledControlFlow(
        byte[] codeBytes,
        uint caveAddress,
        uint returnAddress,
        EnginePatchProfile profile)
    {
        var warnings = new List<string>();
        var instructions = new X86InstructionScanner().DecodeBlock(codeBytes, caveAddress, "compiled-cave");
        var caveEnd = checked(caveAddress + (uint)codeBytes.Length);
        var allowedTargets = new HashSet<uint> { returnAddress };
        foreach (var value in profile.PublicFunctions.Values)
        {
            if (TryParseUInt(value, out var address)) allowedTargets.Add(address);
        }

        foreach (var instruction in instructions)
        {
            if (instruction.IsReturn)
            {
                warnings.Add($"代码洞 0x{instruction.Address:X8} 包含 ret；注入代码必须显式跳回 HookContract 返回地址。");
                continue;
            }

            if (!instruction.BranchTarget.HasValue) continue;
            var target = instruction.BranchTarget.Value;
            var insideCave = target >= caveAddress && target < caveEnd;
            if (!insideCave && !allowedTargets.Contains(target))
            {
                warnings.Add($"代码洞 0x{instruction.Address:X8} 的 {instruction.Mnemonic} 指向未登记地址 0x{target:X8}；请先加入当前引擎符号/Hook 契约。" );
            }
        }

        if (!instructions.Any(instruction => instruction.IsDirectJump && instruction.BranchTarget == returnAddress))
        {
            warnings.Add($"代码洞没有显式跳回 0x{returnAddress:X8}。" );
        }

        return warnings;
    }

    private static bool TryCompileTinyAssembler(string source, uint origin, out byte[] bytes)
    {
        var output = new List<byte>();
        foreach (var rawLine in source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var line = StripComment(rawLine).Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.EndsWith(':')) continue;

            if (line.Equals("nop", StringComparison.OrdinalIgnoreCase))
            {
                output.Add(0x90);
                continue;
            }

            if (line.Equals("ret", StringComparison.OrdinalIgnoreCase))
            {
                output.Add(0xC3);
                continue;
            }

            if (line.StartsWith("db ", StringComparison.OrdinalIgnoreCase))
            {
                output.AddRange(ParseDb(line[3..]));
                continue;
            }

            if (line.StartsWith("jmp ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("call ", StringComparison.OrdinalIgnoreCase))
            {
                var isCall = line.StartsWith("call ", StringComparison.OrdinalIgnoreCase);
                var targetText = line[(isCall ? 5 : 4)..].Trim();
                if (!TryParseUInt(targetText, out var target))
                {
                    bytes = [];
                    return false;
                }

                var current = checked(origin + (uint)output.Count);
                output.Add(isCall ? (byte)0xE8 : (byte)0xE9);
                var rel64 = (long)target - ((long)current + 5L);
                if (rel64 < int.MinValue || rel64 > int.MaxValue)
                {
                    throw new InvalidOperationException($"Relative {(isCall ? "call" : "jmp")} target out of rel32 range: 0x{current:X8} -> 0x{target:X8}.");
                }

                var rel = (int)rel64;
                output.AddRange(BitConverter.GetBytes(rel));
                continue;
            }

            bytes = [];
            return false;
        }

        bytes = output.ToArray();
        return true;
    }

    private static byte[] CompileWithNasm(string source, uint origin)
    {
        var nasm = ResolveTool("nasm.exe");
        if (string.IsNullOrWhiteSpace(nasm))
        {
            throw new InvalidOperationException("未找到 nasm.exe；汇编补丁预览无法编译 bytes。请先安装或配置 NASM。");
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "ccz-asm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var asmPath = Path.Combine(tempRoot, "patch.asm");
            var binPath = Path.Combine(tempRoot, "patch.bin");
            File.WriteAllText(asmPath, $"[BITS 32]\r\n[ORG 0x{origin:X8}]\r\n" + source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var psi = new ProcessStartInfo
            {
                FileName = nasm,
                Arguments = $"-f bin \"{asmPath}\" -o \"{binPath}\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 nasm.exe。");
            process.WaitForExit(10_000);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("NASM 汇编失败：" + process.StandardError.ReadToEnd() + process.StandardOutput.ReadToEnd());
            }

            return File.ReadAllBytes(binPath);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static string TryDisassemble(byte[] bytes, uint origin)
    {
        var ndisasm = ResolveTool("ndisasm.exe");
        if (string.IsNullOrWhiteSpace(ndisasm) || bytes.Length == 0) return string.Empty;

        var tempRoot = Path.Combine(Path.GetTempPath(), "ccz-disasm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var binPath = Path.Combine(tempRoot, "patch.bin");
            File.WriteAllBytes(binPath, bytes);
            var psi = new ProcessStartInfo
            {
                FileName = ndisasm,
                Arguments = $"-b 32 -o 0x{origin:X8} \"{binPath}\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null) return string.Empty;
            process.WaitForExit(10_000);
            return process.ExitCode == 0 ? process.StandardOutput.ReadToEnd().Trim() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static byte[] BuildHookBytes(uint hookAddress, uint caveAddress, int overwriteLength)
    {
        if (overwriteLength < 5) throw new InvalidOperationException("Hook overwrite length must be at least 5 bytes.");
        var bytes = new byte[overwriteLength];
        bytes[0] = 0xE9;
        var rel64 = (long)caveAddress - ((long)hookAddress + 5L);
        if (rel64 < int.MinValue || rel64 > int.MaxValue)
        {
            throw new InvalidOperationException($"Hook jump target out of rel32 range: 0x{hookAddress:X8} -> 0x{caveAddress:X8}.");
        }

        var rel = (int)rel64;
        BitConverter.GetBytes(rel).CopyTo(bytes, 1);
        for (var i = 5; i < bytes.Length; i++) bytes[i] = 0x90;
        return bytes;
    }

    private static void NormalizeDraft(AssemblyPatchDraft draft)
    {
        draft.TargetFile = string.IsNullOrWhiteSpace(draft.TargetFile) ? "Ekd5.exe" : draft.TargetFile;
        draft.HookAddress = ResolveAddress(draft.HookAddress, draft.HookAddressHex, "HookAddress");
        if (draft.ReturnAddress == 0 && !string.IsNullOrWhiteSpace(draft.ReturnAddressHex))
        {
            draft.ReturnAddress = ResolveAddress(0, draft.ReturnAddressHex, "ReturnAddress");
        }

        if (draft.OverwriteLength < 5) throw new InvalidOperationException("OverwriteLength must be at least 5.");
        if (string.IsNullOrWhiteSpace(draft.ExpectedOldBytesHex)) throw new InvalidOperationException("ExpectedOldBytesHex is required.");
        var expectedCount = ParseHexByteCount(draft.ExpectedOldBytesHex);
        if (expectedCount != draft.OverwriteLength)
        {
            throw new InvalidOperationException($"ExpectedOldBytesHex length {expectedCount} must equal OverwriteLength {draft.OverwriteLength}.");
        }

        if (string.IsNullOrWhiteSpace(draft.AssemblySource)) throw new InvalidOperationException("AssemblySource is required.");
        if (draft.RequiredCodeCaveBytes <= 0) draft.RequiredCodeCaveBytes = 32;
    }

    private static uint ResolveAddress(uint numeric, string text, string fieldName)
    {
        if (numeric != 0) return numeric;
        if (TryParseUInt(text, out var parsed)) return parsed;
        throw new InvalidOperationException(fieldName + " is required.");
    }

    private static bool TryParseUInt(string? text, out uint value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[2..];
        return uint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static string ResolveTool(string name)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path)) return path;
        }

        return string.Empty;
    }

    private static string StripComment(string line)
    {
        var semi = line.IndexOf(';');
        return semi >= 0 ? line[..semi] : line;
    }

    private static IEnumerable<byte> ParseDb(string text)
    {
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryParseByte(part, out var value)) throw new InvalidOperationException("Unsupported db byte: " + part);
            yield return value;
        }
    }

    private static bool TryParseByte(string text, out byte value)
    {
        value = 0;
        var trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[2..];
        return byte.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value) ||
               byte.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static int ParseHexByteCount(string text)
    {
        var chars = text.Count(Uri.IsHexDigit);
        if (chars == 0) return 0;
        if (chars % 2 != 0) throw new InvalidOperationException("Hex byte string has odd length.");
        return chars / 2;
    }

    private static string ToHex(byte[] bytes)
        => BitConverter.ToString(bytes).Replace("-", " ");

    private static string RepeatHex(string value, int count)
        => string.Join(' ', Enumerable.Repeat(value, count));

    [GeneratedRegex(@"^\s*(?<mnemonic>[A-Za-z]+)\s+")]
    private static partial Regex MnemonicRegex();

    private sealed record DraftTemplate(
        string TemplateId,
        string HookPoint,
        uint HookAddress,
        int OverwriteLength,
        int RequiredCodeCaveBytes,
        string AssemblySource,
        string RegisterStrategy,
        string Interpretation,
        string Risk,
        List<string> Dependencies);
}
