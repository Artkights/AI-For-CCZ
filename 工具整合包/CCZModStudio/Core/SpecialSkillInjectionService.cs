using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class SpecialSkillInjectionService
{
    private const string LogicalPatchKind = "inline-special-skill";
    private const string ParameterEncodingPolicy = "auto-wide";
    private const uint CoreEffectEngineAddress = 0x004101D9;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public InlineSpecialSkillPatchDraft Draft(
        CczProject project,
        string prompt,
        int? effectId,
        string? hookHint,
        int? personalEffectId,
        int? itemEffectId,
        string? mode)
    {
        var profile = new EnginePatchProfileService().Build(project);
        var normalizedMode = string.IsNullOrWhiteSpace(mode) ? "damage-adjust" : mode.Trim();
        var spec = SelectHookSpec(profile, prompt, hookHint, normalizedMode);
        var returnAddress = spec.HookAddress == 0 ? 0 : checked(spec.HookAddress + (uint)spec.OverwriteLength);
        var expectedOldBytes = spec.HookAddress == 0
            ? string.Empty
            : TryReadExpectedOldBytes(project, "Ekd5.exe", spec.HookAddress, spec.OverwriteLength);
        var personal = NormalizeEffectId(personalEffectId ?? effectId ?? 0);
        var item = NormalizeEffectId(itemEffectId ?? 0);
        var functionBody = BuildDefaultFunctionBody(spec);
        var draft = new InlineSpecialSkillPatchDraft
        {
            Prompt = prompt ?? string.Empty,
            TargetFile = "Ekd5.exe",
            EngineVersion = profile.EngineVersion,
            EffectId = effectId ?? personal,
            Mode = normalizedMode,
            TemplateId = spec.TemplateId,
            HookPoint = spec.HookPoint,
            HookAddress = spec.HookAddress,
            HookAddressHex = spec.HookAddress == 0 ? string.Empty : $"0x{spec.HookAddress:X8}",
            OverwriteLength = spec.OverwriteLength,
            ExpectedOldBytesHex = expectedOldBytes,
            ReturnAddress = returnAddress,
            ReturnAddressHex = returnAddress == 0 ? string.Empty : $"0x{returnAddress:X8}",
            PersonalEffectId = personal,
            ItemEffectId = item,
            EffectValueFlag = normalizedMode.Contains("value", StringComparison.OrdinalIgnoreCase) ? 0 : 1,
            StackFlag = 1,
            ParameterEncodingPolicy = ParameterEncodingPolicy,
            UnitPointerSource = spec.UnitPointerSource,
            FunctionAssemblySource = functionBody,
            RequiredCodeCaveBytes = spec.RequiredCodeCaveBytes,
            AllowPreview = profile.EngineVersion == "6.5" && profile.IsKnown && spec.AllowAutoPreview,
            Risks =
            {
                "Preview only compiles a reviewed four-module scaffold; it is not proof that an arbitrary natural-language mechanic is correct.",
                spec.SafetyLevel == "known-safe-template"
                    ? "Known hook template still requires old-byte locks and dynamic validation before formal use."
                    : "Manual-review template: fill a reviewed FunctionAssemblySource before preview/apply."
            },
            DynamicValidationPlan = spec.DynamicValidationPlan.ToList(),
            Metadata =
            {
                ["LogicalPatchKind"] = LogicalPatchKind,
                ["TemplateId"] = spec.TemplateId,
                ["HookPoint"] = spec.HookPoint,
                ["Mode"] = normalizedMode,
                ["ParameterEncodingPolicy"] = ParameterEncodingPolicy,
                ["PreviewRequiresKnown65"] = "true",
                ["EngineProfileSha256"] = profile.ExeSha256,
                ["ConflictGroup"] = spec.ConflictGroup,
                ["DamageSlot"] = spec.DamageSlot,
                ["SafetyLevel"] = spec.SafetyLevel
            }
        };

        if (!profile.IsKnown || profile.EngineVersion != "6.5")
        {
            draft.Warnings.Add("Only the known 6.5 unencrypted engine profile can auto-preview/apply special-skill patches.");
            draft.AllowPreview = false;
        }

        if (!spec.AllowAutoPreview)
        {
            draft.Warnings.Add("Selected hook spec is manual-review only; draft is generated but preview/apply should be rejected until a reviewed template enables it.");
        }

        draft.LogicalPatch = BuildLogicalPatch(draft);
        return draft;
    }

    public SpecialSkillPatchPreviewResult Preview(CczProject project, InlineSpecialSkillPatchDraft draft, string? allocatorPolicy)
    {
        NormalizeDraftForPreview(draft);
        if (!draft.AllowPreview)
        {
            return new SpecialSkillPatchPreviewResult
            {
                CanApply = false,
                Draft = draft,
                LogicalPatch = draft.LogicalPatch,
                Warnings = ["Special-skill draft is not allowed to preview/apply for this profile or hook spec."],
                Summary = "Special-skill preview rejected before compilation."
            };
        }

        var assemblyDraft = BuildAssemblyDraft(draft);
        var assemblyPreview = new AssemblyPatchCompiler().Preview(project, assemblyDraft, allocatorPolicy ?? "smallest-fit");
        var result = new SpecialSkillPatchPreviewResult
        {
            Draft = draft,
            AssemblyPreview = assemblyPreview,
            PatchPreview = assemblyPreview.PatchPreview,
            Package = assemblyPreview.Package,
            CanApply = assemblyPreview.CanApply
        };
        result.Warnings.AddRange(draft.Warnings);
        result.Warnings.AddRange(assemblyPreview.Warnings);

        if (!assemblyPreview.CanApply)
        {
            result.Summary = "Special-skill preview failed: " + string.Join("; ", result.Warnings.Take(6));
            result.LogicalPatch = draft.LogicalPatch;
            result.CanApply = false;
            return result;
        }

        var caveAddress = assemblyPreview.Allocation.Allocation?.StartVirtualAddress ?? 0;
        var logical = BuildLogicalPatch(draft, assemblyPreview.Package.PatchSegments.LastOrDefault(), caveAddress, assemblyPreview.CodeCaveBytes);
        if (logical.PersonalEffectPatchPoint.ValueAddress == 0 ||
            logical.ItemEffectPatchPoint.ValueAddress == 0)
        {
            result.Warnings.Add("Compiled special-skill code did not expose both imm32 personal/item parameter patch points.");
            result.LogicalPatch = logical;
            result.CanApply = false;
            result.Summary = "Special-skill preview failed: parameter patch points were not found in compiled code.";
            return result;
        }

        AddLogicalMetadata(assemblyPreview.Package, draft, logical, assemblyPreview);
        result.LogicalPatch = logical;
        result.Package = assemblyPreview.Package;
        result.CanApply = assemblyPreview.PatchPreview.CanApply;
        result.Summary = $"Special-skill preview: canApply={result.CanApply}, hook={draft.HookPoint}, cave=0x{caveAddress:X8}, segments={result.Package.PatchSegments.Count}.";
        return result;
    }

    public SpecialSkillPatchApplyResult Apply(CczProject project, EffectPackage compiledPackage)
    {
        if (!compiledPackage.Metadata.TryGetValue("LogicalPatchKind", out var kind) ||
            !kind.Equals(LogicalPatchKind, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Special-skill apply only accepts packages produced by preview_special_skill_patch.");
        }

        if (!compiledPackage.Metadata.TryGetValue("AssemblyPatchPreviewPassed", out var passed) ||
            !passed.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Special-skill package did not pass preview.");
        }

        var currentProfile = new EnginePatchProfileService().Build(project);
        if (!compiledPackage.Metadata.TryGetValue("PreviewExeSha256", out var expectedSha) ||
            string.IsNullOrWhiteSpace(expectedSha) ||
            !expectedSha.Equals(currentProfile.ExeSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Special-skill engine SHA lock failed. Expected {expectedSha}, actual {currentProfile.ExeSha256}.");
        }

        var effectPackageService = new EffectPackageService();
        var preview = effectPackageService.PreviewPatch(project, compiledPackage);
        if (!preview.CanApply)
        {
            throw new InvalidOperationException("Special-skill package no longer previews cleanly: " + string.Join("; ", preview.Warnings.Take(8)));
        }

        var apply = effectPackageService.ApplyPatch(project, compiledPackage);
        return new SpecialSkillPatchApplyResult
        {
            Applied = true,
            Summary = apply.Summary,
            PatchApplyResult = apply
        };
    }

    public SpecialSkillParamRebindPreviewResult RebindParameters(
        CczProject project,
        string manifestIdOrPackageId,
        int? personalEffectId,
        int? itemEffectId)
    {
        var manifest = LoadManifest(project, manifestIdOrPackageId);
        var sourcePackage = manifest.Package;
        if (!sourcePackage.Metadata.TryGetValue("LogicalPatchKind", out var kind) ||
            !kind.Equals(LogicalPatchKind, StringComparison.OrdinalIgnoreCase))
        {
            return new SpecialSkillParamRebindPreviewResult
            {
                CanApply = false,
                Warnings = ["Manifest/package is not an inline special-skill patch."],
                Summary = "Special-skill parameter rebind rejected."
            };
        }

        var points = ReadParameterPatchPoints(sourcePackage);
        var package = new EffectPackage
        {
            PackageId = $"special-skill-rebind-{sourcePackage.EffectId}-{DateTime.Now:yyyyMMddHHmmssfff}",
            Domain = "patch",
            EffectId = sourcePackage.EffectId,
            Name = sourcePackage.Name + " parameter rebind",
            Description = "Rebind inline special-skill personal/item effect ids.",
            SourcePrompt = sourcePackage.SourcePrompt,
            BackupNote = "Parameter-only rebind generated from a special-skill manifest.",
            Metadata =
            {
                ["LogicalPatchKind"] = LogicalPatchKind,
                ["ParameterOnlyRebind"] = "true",
                ["SourceManifestId"] = manifest.ManifestId,
                ["SourcePackageId"] = sourcePackage.PackageId,
                ["ParameterEncodingPolicy"] = ParameterEncodingPolicy
            }
        };

        foreach (var point in points)
        {
            var nextValue = point.Kind.Equals("personal", StringComparison.OrdinalIgnoreCase)
                ? personalEffectId
                : point.Kind.Equals("item", StringComparison.OrdinalIgnoreCase)
                    ? itemEffectId
                    : null;
            if (nextValue is null) continue;
            ValidateRebindValue(point, nextValue.Value);
            package.PatchSegments.Add(new EffectPatchSegment
            {
                TargetFile = "Ekd5.exe",
                AddressKind = "OdVirtualAddress",
                Address = point.ValueAddress,
                AddressHex = point.ValueAddressHex,
                BytesHex = EncodeParameterValue(point, nextValue.Value),
                ExpectedOldBytesHex = EncodeParameterValue(point, point.EffectId),
                HookPoint = sourcePackage.Metadata.GetValueOrDefault("HookPoint") ?? string.Empty,
                CodeCaveId = sourcePackage.Metadata.GetValueOrDefault("AllocatedRange") ?? string.Empty,
                AllocatedRange = sourcePackage.Metadata.GetValueOrDefault("AllocatedRange") ?? string.Empty,
                EngineProfileSha256 = sourcePackage.Metadata.GetValueOrDefault("PreviewExeSha256") ?? string.Empty,
                Comment = $"Special-skill {point.Kind} effect id rebind."
            });
        }

        if (package.PatchSegments.Count == 0)
        {
            return new SpecialSkillParamRebindPreviewResult
            {
                CanApply = false,
                Package = package,
                ParameterPatchPoints = points,
                Warnings = ["No personal_effect_id or item_effect_id value was supplied for rebind."],
                Summary = "Special-skill parameter rebind has no changes."
            };
        }

        var patchPreview = new EffectPackageService().PreviewPatch(project, package);
        return new SpecialSkillParamRebindPreviewResult
        {
            CanApply = patchPreview.CanApply,
            Package = package,
            PatchPreview = patchPreview,
            ParameterPatchPoints = points,
            Warnings = patchPreview.Warnings.ToList(),
            Summary = $"Special-skill parameter rebind preview: canApply={patchPreview.CanApply}, segments={package.PatchSegments.Count}."
        };
    }

    private static SpecialSkillHookSpec SelectHookSpec(EnginePatchProfile profile, string? prompt, string? hookHint, string mode)
    {
        if (!string.IsNullOrWhiteSpace(hookHint) &&
            profile.SpecialSkillHookSpecs.TryGetValue(hookHint.Trim(), out var hinted))
        {
            return hinted;
        }

        var text = ((prompt ?? string.Empty) + " " + (hookHint ?? string.Empty) + " " + mode).ToLowerInvariant();
        foreach (var spec in profile.SpecialSkillHookSpecs.Values.DistinctBy(spec => spec.HookPoint))
        {
            if (text.Contains(spec.TemplateId.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase) ||
                text.Contains(spec.HookPoint.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
            {
                return spec;
            }
        }

        if (mode.Contains("damage", StringComparison.OrdinalIgnoreCase) &&
            profile.SpecialSkillHookSpecs.TryGetValue("strategy-damage-adjust-after-move", out var damageSpec))
        {
            return damageSpec;
        }

        return new SpecialSkillHookSpec
        {
            TemplateId = "manual-review-required",
            HookPoint = string.IsNullOrWhiteSpace(hookHint) ? "manual-review-required" : hookHint.Trim(),
            Mode = mode,
            SafetyLevel = "manual-review-template",
            AllowAutoPreview = false,
            Notes = { "No trusted profile hook matched the request." }
        };
    }

    private static AssemblyPatchDraft BuildAssemblyDraft(InlineSpecialSkillPatchDraft draft)
        => new()
        {
            Prompt = draft.Prompt,
            TargetFile = draft.TargetFile,
            EngineVersion = draft.EngineVersion,
            EffectId = draft.EffectId,
            HookPoint = draft.HookPoint,
            HookAddress = draft.HookAddress,
            HookAddressHex = draft.HookAddressHex,
            OverwriteLength = draft.OverwriteLength,
            ExpectedOldBytesHex = draft.ExpectedOldBytesHex,
            ReturnAddress = draft.ReturnAddress,
            ReturnAddressHex = draft.ReturnAddressHex,
            AssemblySource = BuildAssemblySource(draft),
            RequiredCodeCaveBytes = draft.RequiredCodeCaveBytes,
            RegisterStrategy = "Generated inline special-skill scaffold uses pushad/popad around the stub and reviewed body.",
            Dependencies =
            {
                "core_effect_engine 0x004101D9",
                $"personal_effect_id {draft.PersonalEffectId:X2}",
                $"item_effect_id {draft.ItemEffectId:X2}"
            },
            Risks = draft.Risks.ToList(),
            DynamicValidationPlan = draft.DynamicValidationPlan.ToList(),
            Metadata = new Dictionary<string, string>(draft.Metadata, StringComparer.OrdinalIgnoreCase)
        };

    private static string BuildAssemblySource(InlineSpecialSkillPatchDraft draft)
    {
        var personalPush = BuildPushImm32Line("special_skill_personal_push", draft.PersonalEffectId);
        var itemPush = BuildPushImm32Line("special_skill_item_push", draft.ItemEffectId);
        return string.Join("\n", new[]
        {
            "pushad",
            $"mov ecx, {NormalizeUnitPointerSource(draft.UnitPointerSource)}",
            $"push 0x{draft.EffectValueFlag:X8}",
            $"push 0x{draft.StackFlag:X8}",
            itemPush,
            personalPush,
            $"call 0x{CoreEffectEngineAddress:X8}",
            "test eax, eax",
            "jz .special_skill_exit",
            StripOuterWhitespace(draft.FunctionAssemblySource),
            ".special_skill_exit:",
            "popad",
            "jmp {return}"
        });
    }

    private static InlineSpecialSkillPatch BuildLogicalPatch(
        InlineSpecialSkillPatchDraft draft,
        EffectPatchSegment? codeCaveSegment = null,
        uint codeCaveAddress = 0,
        byte[]? codeCaveBytes = null)
    {
        var (itemOffset, personalOffset) = FindParameterInstructionOffsets(draft, codeCaveBytes ?? []);
        var personalInstruction = codeCaveAddress == 0 || personalOffset < 0 ? 0 : checked(codeCaveAddress + (uint)personalOffset);
        var itemInstruction = codeCaveAddress == 0 || itemOffset < 0 ? 0 : checked(codeCaveAddress + (uint)itemOffset);
        return new InlineSpecialSkillPatch
        {
            HookJump = new SpecialSkillHookJumpModule
            {
                HookPoint = draft.HookPoint,
                HookAddress = draft.HookAddress,
                OverwriteLength = draft.OverwriteLength,
                ExpectedOldBytesHex = draft.ExpectedOldBytesHex,
                ReturnAddress = draft.ReturnAddress,
                CodeCaveId = codeCaveSegment?.CodeCaveId ?? string.Empty,
                CodeCaveAddress = codeCaveAddress
            },
            StubAndBody = new SpecialSkillStubAndBodyModule
            {
                UnitPointerSource = draft.UnitPointerSource,
                EffectValueFlag = draft.EffectValueFlag,
                StackFlag = draft.StackFlag,
                ItemEffectId = draft.ItemEffectId,
                PersonalEffectId = draft.PersonalEffectId,
                FunctionAssemblySource = draft.FunctionAssemblySource,
                CoreEffectEngineAddressHex = $"0x{CoreEffectEngineAddress:X8}"
            },
            PersonalEffectPatchPoint = new SpecialSkillParameterPatchPoint
            {
                Kind = "personal",
                EffectId = draft.PersonalEffectId,
                Encoding = "push-imm32",
                InstructionAddress = personalInstruction,
                ValueAddress = personalInstruction == 0 ? 0 : personalInstruction + 1,
                ValueByteLength = 4,
                Note = "Logical module 3: personal effect id. Initial install is carried by the code-cave body segment."
            },
            ItemEffectPatchPoint = new SpecialSkillParameterPatchPoint
            {
                Kind = "item",
                EffectId = draft.ItemEffectId,
                Encoding = "push-imm32",
                InstructionAddress = itemInstruction,
                ValueAddress = itemInstruction == 0 ? 0 : itemInstruction + 1,
                ValueByteLength = 4,
                Note = "Logical module 4: item effect id. Initial install is carried by the code-cave body segment."
            }
        };
    }

    private static void AddLogicalMetadata(
        EffectPackage package,
        InlineSpecialSkillPatchDraft draft,
        InlineSpecialSkillPatch logical,
        AssemblyPatchPreviewResult assemblyPreview)
    {
        package.Metadata["LogicalPatchKind"] = LogicalPatchKind;
        package.Metadata["LogicalModulesJson"] = JsonSerializer.Serialize(logical, JsonOptions);
        package.Metadata["ParameterPatchPointsJson"] = JsonSerializer.Serialize(
            new[] { logical.PersonalEffectPatchPoint, logical.ItemEffectPatchPoint },
            JsonOptions);
        package.Metadata["ParameterEncodingPolicy"] = ParameterEncodingPolicy;
        package.Metadata["PreviewExeSha256"] = package.Metadata.GetValueOrDefault("EngineProfileSha256") ?? string.Empty;
        package.Metadata["DynamicValidationPlan"] = string.Join("; ", draft.DynamicValidationPlan);
        package.Metadata["SpecialSkillTemplateId"] = draft.TemplateId;
        package.Metadata["SpecialSkillMode"] = draft.Mode;
        package.Metadata["SpecialSkillCodeCaveBytesSha256"] = Convert.ToHexString(SHA256.HashData(assemblyPreview.CodeCaveBytes));
    }

    private static List<SpecialSkillParameterPatchPoint> ReadParameterPatchPoints(EffectPackage package)
    {
        if (!package.Metadata.TryGetValue("ParameterPatchPointsJson", out var json) ||
            string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<SpecialSkillParameterPatchPoint>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static EffectManifest LoadManifest(CczProject project, string manifestIdOrPackageId)
    {
        var root = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Notes", "EffectManifests");
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Effect manifest directory was not found: " + root);
        var needle = (manifestIdOrPackageId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(needle)) throw new InvalidOperationException("manifest_id_or_package_id is required.");

        foreach (var path in Directory.GetFiles(root, "*.json"))
        {
            try
            {
                var manifest = JsonSerializer.Deserialize<EffectManifest>(File.ReadAllText(path, Encoding.UTF8), JsonOptions);
                if (manifest == null) continue;
                if (manifest.ManifestId.Equals(needle, StringComparison.OrdinalIgnoreCase) ||
                    manifest.PackageId.Equals(needle, StringComparison.OrdinalIgnoreCase) ||
                    manifest.Package.PackageId.Equals(needle, StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileNameWithoutExtension(path).Equals(needle, StringComparison.OrdinalIgnoreCase))
                {
                    return manifest;
                }
            }
            catch
            {
                // Ignore malformed old manifests.
            }
        }

        throw new FileNotFoundException("Special-skill source manifest was not found.", Path.Combine(root, needle + ".json"));
    }

    private static void NormalizeDraftForPreview(InlineSpecialSkillPatchDraft draft)
    {
        draft.TargetFile = string.IsNullOrWhiteSpace(draft.TargetFile) ? "Ekd5.exe" : draft.TargetFile;
        draft.ParameterEncodingPolicy = string.IsNullOrWhiteSpace(draft.ParameterEncodingPolicy) ? ParameterEncodingPolicy : draft.ParameterEncodingPolicy;
        if (!draft.ParameterEncodingPolicy.Equals(ParameterEncodingPolicy, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("v1 special-skill patches require parameterEncodingPolicy=auto-wide.");
        }

        if (draft.HookAddress == 0 && TryParseUInt(draft.HookAddressHex, out var hook)) draft.HookAddress = hook;
        if (draft.ReturnAddress == 0 && TryParseUInt(draft.ReturnAddressHex, out var ret)) draft.ReturnAddress = ret;
        if (draft.ReturnAddress == 0 && draft.HookAddress != 0) draft.ReturnAddress = checked(draft.HookAddress + (uint)draft.OverwriteLength);
        if (draft.OverwriteLength < 5) throw new InvalidOperationException("OverwriteLength must be at least 5.");
        if (string.IsNullOrWhiteSpace(draft.ExpectedOldBytesHex)) throw new InvalidOperationException("ExpectedOldBytesHex is required.");
        if (draft.RequiredCodeCaveBytes <= 0) draft.RequiredCodeCaveBytes = 96;
        draft.PersonalEffectId = NormalizeEffectId(draft.PersonalEffectId);
        draft.ItemEffectId = NormalizeEffectId(draft.ItemEffectId);
        draft.LogicalPatch = BuildLogicalPatch(draft);
    }

    private static string TryReadExpectedOldBytes(CczProject project, string targetFile, uint virtualAddress, int length)
    {
        try
        {
            var exePath = project.ResolveGameFile(string.IsNullOrWhiteSpace(targetFile) ? "Ekd5.exe" : targetFile);
            var mapper = PeAddressMapper.Load(exePath);
            var offset = mapper.VirtualAddressToFileOffset(virtualAddress);
            var bytes = File.ReadAllBytes(exePath);
            if (offset < 0 || offset + length > bytes.LongLength) return string.Empty;
            return ToHex(bytes.Skip(checked((int)offset)).Take(length).ToArray());
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string BuildDefaultFunctionBody(SpecialSkillHookSpec spec)
        => spec.SafetyLevel.Equals("known-safe-template", StringComparison.OrdinalIgnoreCase)
            ? "nop"
            : "nop";

    private static (int ItemOffset, int PersonalOffset) FindParameterInstructionOffsets(InlineSpecialSkillPatchDraft draft, byte[] codeCaveBytes)
    {
        if (codeCaveBytes.Length == 0) return (-1, -1);
        var itemPattern = BuildPushImm32Bytes(draft.ItemEffectId);
        var personalPattern = BuildPushImm32Bytes(draft.PersonalEffectId);
        for (var i = 0; i <= codeCaveBytes.Length - itemPattern.Length - personalPattern.Length; i++)
        {
            if (Matches(codeCaveBytes, i, itemPattern) &&
                Matches(codeCaveBytes, i + itemPattern.Length, personalPattern))
            {
                return (i, i + itemPattern.Length);
            }
        }

        return (-1, -1);
    }

    private static string BuildPushImm32Line(string label, int value)
    {
        var bytes = BitConverter.GetBytes(NormalizeEffectId(value));
        return $"db 0x68, 0x{bytes[0]:X2}, 0x{bytes[1]:X2}, 0x{bytes[2]:X2}, 0x{bytes[3]:X2} ;{label}";
    }

    private static byte[] BuildPushImm32Bytes(int value)
    {
        var imm = BitConverter.GetBytes(NormalizeEffectId(value));
        return [0x68, imm[0], imm[1], imm[2], imm[3]];
    }

    private static bool Matches(byte[] bytes, int offset, byte[] pattern)
    {
        if (offset < 0 || offset + pattern.Length > bytes.Length) return false;
        for (var i = 0; i < pattern.Length; i++)
        {
            if (bytes[offset + i] != pattern[i]) return false;
        }

        return true;
    }

    private static string EncodeParameterValue(SpecialSkillParameterPatchPoint point, int value)
    {
        value = NormalizeEffectId(value);
        if (point.Encoding.Equals("push-imm8", StringComparison.OrdinalIgnoreCase))
        {
            if (value > 0x7F) throw new InvalidOperationException("push-imm8 parameter points can only be rebound to 0x00-0x7F.");
            return value.ToString("X2", CultureInfo.InvariantCulture);
        }

        return ToHex(BitConverter.GetBytes(value));
    }

    private static void ValidateRebindValue(SpecialSkillParameterPatchPoint point, int value)
    {
        value = NormalizeEffectId(value);
        if (point.Encoding.Equals("push-imm8", StringComparison.OrdinalIgnoreCase) && value > 0x7F)
        {
            throw new InvalidOperationException($"{point.Kind} parameter uses push imm8 and cannot be rebound above 0x7F without recompilation.");
        }
    }

    private static int NormalizeEffectId(int value)
    {
        if (value < 0 || value > 0xFF) throw new InvalidOperationException("Special-skill personal/item effect ids must be between 0x00 and 0xFF.");
        return value;
    }

    private static string NormalizeUnitPointerSource(string value)
        => string.IsNullOrWhiteSpace(value) ? "dword [ebp-04]" : value.Trim();

    private static string StripOuterWhitespace(string value)
        => string.IsNullOrWhiteSpace(value) ? "nop" : value.Trim();

    private static bool TryParseUInt(string? text, out uint value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[2..];
        return uint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static string ToHex(byte[] bytes)
        => BitConverter.ToString(bytes).Replace("-", " ");
}
