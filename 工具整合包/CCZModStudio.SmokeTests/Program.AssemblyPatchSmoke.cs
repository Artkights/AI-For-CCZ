using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using CCZModStudio;
using System.Globalization;
using System.Text.Json;

internal partial class Program
{
    static void RunAssemblyPatchSmoke(CczProject project)
    {
        var sourceProject = ResolveAssemblyPatchSmokeSourceProject(project);
        var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "AssemblyPatchSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(smokeRoot);
        foreach (var coreFile in new[] { "Ekd5.exe", "Data.e5", "Imsg.e5", "Star.e5" })
        {
            var source = Path.Combine(sourceProject.GameRoot, coreFile);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("汇编补丁烟测缺少核心文件。", source);
            }

            File.Copy(source, Path.Combine(smokeRoot, coreFile), overwrite: false);
        }

        File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={sourceProject.GameRoot}\r\nPurpose=assembly patch smoke\r\n");

        var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
        var scanner = new ExeCodeCaveScanner();
        var scan = scanner.Scan(testProject, "Ekd5.exe", minimumLength: 8, includeZeroFill: false);
        var permissiveScan = scanner.Scan(testProject, "Ekd5.exe", minimumLength: 8, includeZeroFill: true, includeMixedFill: true);
        if (!scan.IsKnownEngine ||
            scan.EngineVersionHint != "6.5" ||
            scan.BlockedRanges.All(range => range.CaveId != "legacy-large-cave-E") ||
            scan.Candidates.Count == 0 ||
            scan.Candidates.All(candidate => candidate.FillKind != "nop"))
        {
            throw new InvalidOperationException("EXE 代码洞扫描没有命中预期 6.5 基底/NOP 候选/禁用空洞 E。");
        }

        if (!permissiveScan.IncludeMixedFill ||
            permissiveScan.Candidates.Any(candidate => candidate.FillKind == "zero" && !candidate.RiskLevel.Contains("low-confidence", StringComparison.OrdinalIgnoreCase)) ||
            permissiveScan.Candidates.Any(candidate => candidate.FillKind == "mixed" &&
                                                       (!candidate.RiskLevel.Contains("low-confidence", StringComparison.OrdinalIgnoreCase) ||
                                                        !candidate.Status.Contains("manual", StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidOperationException("EXE code cave scanner did not classify zero/mixed fill as low-confidence manual candidates.");
        }
        if (permissiveScan.Candidates.Any(candidate => candidate.FillByteCounts.Count == 0 ||
                                                       string.IsNullOrWhiteSpace(candidate.FillBytesSummary)))
        {
            throw new InvalidOperationException("EXE code cave scanner did not report current fill-byte evidence for every candidate.");
        }

        var profile = new EnginePatchProfileService().Build(testProject);
        if (profile.ReservedRanges.All(range => range.CaveId != "known-65-guard-final") ||
            profile.HookPoints.All(pair => pair.Key != "guard_final_a"))
        {
            throw new InvalidOperationException("Engine patch profile did not expose expected 6.5 reserved ranges/hook points.");
        }
        var allocation = new CodeCaveRegistry().Allocate(scan, profile, new CodeCaveAllocationRequest
        {
            RequiredBytes = 6,
            ReserveBytes = 8
        });
        if (!allocation.Success || allocation.Allocation == null || allocation.Allocation.Length < 14)
        {
            throw new InvalidOperationException("代码洞分配失败：" + allocation.Reason);
        }

        if (profile.ReservedRanges.Any(range => RangesIntersect(allocation.Allocation.StartVirtualAddress, allocation.Allocation.EndVirtualAddress, range.StartVirtualAddress, range.EndVirtualAddress)))
        {
            throw new InvalidOperationException("Code cave allocation selected a profile-reserved knowledge-base range.");
        }

        var forcedReservedScan = new ExeCodeCaveScanResult
        {
            TargetFilePath = scan.TargetFilePath,
            TargetFileName = scan.TargetFileName,
            ExeSha256 = scan.ExeSha256,
            ExeSize = scan.ExeSize,
            ImageBase = scan.ImageBase,
            EngineVersionHint = scan.EngineVersionHint,
            IsKnownEngine = scan.IsKnownEngine,
            MinimumLength = scan.MinimumLength,
            IncludeZeroFill = scan.IncludeZeroFill,
            IncludeMixedFill = scan.IncludeMixedFill,
            Sections = scan.Sections,
            Candidates =
            [
                new ExeCodeCaveCandidate
                {
                    CaveId = "forced-reserved-candidate",
                    SectionName = ".code",
                    FillKind = "nop",
                    StartVirtualAddress = profile.ReservedRanges[0].StartVirtualAddress,
                    EndVirtualAddress = profile.ReservedRanges[0].EndVirtualAddress,
                    Length = profile.ReservedRanges[0].Length,
                    RiskLevel = "candidate-recommended",
                    Status = "candidate"
                }
            ],
            BlockedRanges = scan.BlockedRanges
        };
        var reservedAllocation = new CodeCaveRegistry().Allocate(forcedReservedScan, profile, new CodeCaveAllocationRequest
        {
            RequiredBytes = 6,
            ReserveBytes = 0
        });
        if (reservedAllocation.Success)
        {
            throw new InvalidOperationException("Code cave allocator did not reject a forced profile-reserved range.");
        }

        var splitCandidateStart = profile.ReservedRanges[0].StartVirtualAddress - 0x10;
        var splitCandidateEnd = profile.ReservedRanges[0].EndVirtualAddress + 0x10;
        var splitCandidateScan = new ExeCodeCaveScanResult
        {
            TargetFilePath = scan.TargetFilePath,
            TargetFileName = scan.TargetFileName,
            ExeSha256 = scan.ExeSha256,
            ExeSize = scan.ExeSize,
            ImageBase = scan.ImageBase,
            EngineVersionHint = scan.EngineVersionHint,
            IsKnownEngine = scan.IsKnownEngine,
            MinimumLength = scan.MinimumLength,
            IncludeZeroFill = scan.IncludeZeroFill,
            IncludeMixedFill = scan.IncludeMixedFill,
            Sections = scan.Sections,
            Candidates =
            [
                new ExeCodeCaveCandidate
                {
                    CaveId = "forced-split-candidate",
                    SectionName = ".code",
                    FillKind = "nop",
                    StartVirtualAddress = splitCandidateStart,
                    EndVirtualAddress = splitCandidateEnd,
                    FileOffset = 0x1000,
                    Length = checked((int)(splitCandidateEnd - splitCandidateStart + 1)),
                    RiskLevel = "candidate-recommended",
                    Status = "candidate"
                }
            ],
            BlockedRanges = scan.BlockedRanges
        };
        var splitAllocation = new CodeCaveRegistry().Allocate(splitCandidateScan, profile, new CodeCaveAllocationRequest
        {
            RequiredBytes = 6,
            ReserveBytes = 0
        });
        if (!splitAllocation.Success ||
            splitAllocation.Allocation == null ||
            splitAllocation.FreeRange == null ||
            RangesIntersect(splitAllocation.Allocation.StartVirtualAddress, splitAllocation.Allocation.EndVirtualAddress, profile.ReservedRanges[0].StartVirtualAddress, profile.ReservedRanges[0].EndVirtualAddress))
        {
            throw new InvalidOperationException("Code cave allocator did not split a candidate around a profile-reserved subrange.");
        }

        var relayOnlyAllocation = new CodeCaveRegistry().Allocate(scan, profile, new CodeCaveAllocationRequest
        {
            RequiredBytes = 4,
            ReserveBytes = 0
        });
        if (relayOnlyAllocation.Success || relayOnlyAllocation.Status != "relay-only")
        {
            throw new InvalidOperationException("Code cave allocator did not enforce relay-only handling for allocations smaller than 5 bytes.");
        }

        var mapper = PeAddressMapper.Load(testProject.ResolveGameFile("Ekd5.exe"));
        var hookCandidate = scan.Candidates
            .Where(candidate => candidate.FillKind == "nop" && candidate.Length >= 10)
            .Where(candidate => allocation.Allocation == null ||
                                candidate.EndVirtualAddress < allocation.Allocation.StartVirtualAddress ||
                                candidate.StartVirtualAddress > allocation.Allocation.EndVirtualAddress)
            .OrderBy(candidate => candidate.Length)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("汇编补丁烟测找不到可用 NOP hook 候选。");
        var hookAddress = hookCandidate.StartVirtualAddress;
        var hookOffset = mapper.VirtualAddressToFileOffset(hookAddress);
        var exeBytes = File.ReadAllBytes(testProject.ResolveGameFile("Ekd5.exe"));
        var expected = exeBytes.Skip(checked((int)hookOffset)).Take(5).ToArray();
        if (expected.Length != 5 || expected.Any(b => b != 0x90))
        {
            throw new InvalidOperationException("汇编补丁烟测 hook 点不是预期 5 字节 NOP。");
        }

        var draft = new AssemblyPatchDraft
        {
            Prompt = "Smoke test: jump into an automatically allocated code cave and return.",
            TargetFile = "Ekd5.exe",
            EngineVersion = "6.5",
            EffectId = 65001,
            HookPoint = "assembly-smoke-nop-hook",
            HookAddress = hookAddress,
            OverwriteLength = 5,
            ExpectedOldBytesHex = BitConverter.ToString(expected).Replace("-", " "),
            ReturnAddress = hookAddress + 5,
            RequiredCodeCaveBytes = 6,
            AssemblySource = "nop\njmp {return}"
        };
        draft.Metadata["LogicalPatchKind"] = "assembly-patch-repair-smoke";

        var compiler = new AssemblyPatchCompiler();
        var realInstructionHook = 0x004101D9u;
        var realInstructionOffset = mapper.VirtualAddressToFileOffset(realInstructionHook);
        var realInstructionBytes = exeBytes.Skip(checked((int)realInstructionOffset)).Take(5).ToArray();
        var unsafeDraft = new AssemblyPatchDraft
        {
            Prompt = "Smoke: non-padding hook without a contract must be rejected.",
            TargetFile = "Ekd5.exe",
            EngineVersion = "6.5",
            EffectId = 65004,
            HookPoint = "unsafe-no-contract",
            HookAddress = realInstructionHook,
            OverwriteLength = 5,
            ExpectedOldBytesHex = BitConverter.ToString(realInstructionBytes).Replace("-", " "),
            ReturnAddress = realInstructionHook + 5,
            RequiredCodeCaveBytes = 16,
            AssemblySource = "nop\njmp {return}"
        };
        var unsafePreview = compiler.Preview(testProject, unsafeDraft);
        if (unsafePreview.CanApply ||
            unsafePreview.HookSafety.Warnings.All(warning => !warning.Contains("HookContract", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Non-padding hook without HookContract was not rejected.");
        }

        var agentDraft = compiler.Draft(testProject, "生成一个回MP攻击补丁草案，只生成草案不要写入", "6.5", 65003, null);
        if (agentDraft.HookPoint != "physical_after_damage_mp_restore" ||
            agentDraft.HookAddress == 0 ||
            string.IsNullOrWhiteSpace(agentDraft.ExpectedOldBytesHex) ||
            agentDraft.Metadata.GetValueOrDefault("TemplateId") != "known-65-mp-restore" ||
            agentDraft.Metadata.GetValueOrDefault("PreviewRequired") != "true" ||
            agentDraft.Dependencies.Count == 0)
        {
            throw new InvalidOperationException("Project-aware assembly draft did not select the expected profile/knowledge-base hook template.");
        }

        var preview = compiler.Preview(testProject, draft);
        if (!preview.CanApply ||
            preview.Package.PatchSegments.Count != 2 ||
            preview.HookBytes.Length != 5 ||
            preview.CodeCaveBytes.Length != 6 ||
            preview.Package.Metadata.TryGetValue("AssemblyPatchPreviewPassed", out var passed) == false ||
            passed != "true" ||
            !preview.Package.Metadata.ContainsKey("AssemblySource") ||
            !preview.Package.Metadata.ContainsKey("HookAddress") ||
            !preview.Package.Metadata.ContainsKey("ReturnAddress"))
        {
            throw new InvalidOperationException("汇编补丁预览失败：" + string.Join("; ", preview.Warnings));
        }

        var codeCaveSegment = preview.Package.PatchSegments[1];
        var codeCaveOffset = mapper.VirtualAddressToFileOffset(codeCaveSegment.Address);
        var expectedCodeCaveOldBytes = exeBytes.Skip(checked((int)codeCaveOffset)).Take(preview.CodeCaveBytes.Length).ToArray();
        if (!codeCaveSegment.ExpectedOldBytesHex.Equals(BitConverter.ToString(expectedCodeCaveOldBytes).Replace("-", " "), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Assembly patch preview did not lock code-cave expected old bytes from the actual target EXE.");
        }

        var overlapPackage = ClonePatchPackage(preview.Package);
        overlapPackage.PatchSegments.Add(new EffectPatchSegment
        {
            TargetFile = codeCaveSegment.TargetFile,
            AddressKind = codeCaveSegment.AddressKind,
            Address = codeCaveSegment.Address + 1,
            AddressHex = $"0x{codeCaveSegment.Address + 1:X8}",
            BytesHex = "90",
            ExpectedOldBytesHex = "90",
            CodeCaveId = codeCaveSegment.CodeCaveId,
            HookPoint = codeCaveSegment.HookPoint,
            Comment = "Intentional overlap smoke segment."
        });
        var overlapPreview = new EffectPackageService().PreviewPatch(testProject, overlapPackage);
        if (overlapPreview.CanApply ||
            overlapPreview.Warnings.All(warning => !warning.Contains("overlap", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Patch preview did not reject overlapping patch segments.");
        }

        var specialService = new SpecialSkillInjectionService();
        var specialDraft = specialService.Draft(
            testProject,
            "Smoke: xx 条件增加伤害，用四模块内联桩草案。",
            65010,
            "strategy-damage-adjust-after-move",
            personalEffectId: 0xB0,
            itemEffectId: 0x00,
            mode: "damage-adjust");
        if (specialDraft.AllowPreview ||
            specialDraft.LogicalPatch.HookJump.HookPoint.Length == 0 ||
            specialDraft.ParameterEncodingPolicy != "auto-wide" ||
            specialDraft.Metadata.GetValueOrDefault("LogicalPatchKind") != "inline-special-skill")
        {
            throw new InvalidOperationException("Special-skill draft did not expose the expected four-module metadata.");
        }

        var specialPreview = specialService.Preview(testProject, specialDraft, "smallest-fit");
        if (specialPreview.CanApply || specialPreview.Warnings.All(item =>
                !item.Contains("动态", StringComparison.Ordinal) &&
                !item.Contains("人工", StringComparison.Ordinal) &&
                !item.Contains("契约", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Static-only strategy NOP hook was not blocked by the execution-contract gate: " + string.Join("; ", specialPreview.Warnings));
        }

        var badPackage = ClonePatchPackage(preview.Package);
        badPackage.PatchSegments[0].ExpectedOldBytesHex = "CC CC CC CC CC";
        var badPreview = new EffectPackageService().PreviewPatch(testProject, badPackage);
        if (badPreview.CanApply ||
            badPreview.Warnings.All(warning => !warning.Contains("expected old bytes", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("ExpectedOldBytesHex 不匹配时未阻断补丁预览。");
        }

        var beforeHash = WriteOperationReportService.ComputeSha256(testProject.ResolveGameFile("Ekd5.exe"));
        var apply = compiler.Apply(testProject, preview.Package);
        var afterHash = WriteOperationReportService.ComputeSha256(testProject.ResolveGameFile("Ekd5.exe"));
        if (!apply.Applied ||
            string.IsNullOrWhiteSpace(apply.PatchApplyResult.ManifestPath) ||
            apply.PatchApplyResult.BackupPaths.Count == 0 ||
            beforeHash.Equals(afterHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("汇编补丁测试副本写入未生成备份/manifest 或未改变 EXE。");
        }

        var writtenBytes = File.ReadAllBytes(testProject.ResolveGameFile("Ekd5.exe"));
        var hookWritten = writtenBytes.Skip(checked((int)hookOffset)).Take(5).ToArray();
        if (hookWritten[0] != 0xE9)
        {
            throw new InvalidOperationException("汇编补丁写入后 hook 点不是 JMP。");
        }

        var manifestAllocations = new CodeCaveRegistry().LoadExistingAllocations(testProject, "Ekd5.exe");
        if (manifestAllocations.Count < 2 ||
            manifestAllocations.All(range => range.StartVirtualAddress != preview.Allocation.Allocation!.StartVirtualAddress))
        {
            throw new InvalidOperationException("Code cave registry did not load applied patch allocations from EffectManifests.");
        }

        var secondDraft = new AssemblyPatchDraft
        {
            Prompt = "Smoke test: verify manifest allocation lock picks a different cave.",
            TargetFile = "Ekd5.exe",
            EngineVersion = "6.5",
            EffectId = 65002,
            HookPoint = "assembly-smoke-second-preview",
            HookAddress = hookAddress + 5,
            OverwriteLength = 5,
            ExpectedOldBytesHex = "90 90 90 90 90",
            ReturnAddress = hookAddress + 10,
            RequiredCodeCaveBytes = 6,
            AssemblySource = "nop\njmp {return}"
        };
        secondDraft.Metadata["LogicalPatchKind"] = "assembly-patch-repair-smoke";
        var secondPreview = compiler.Preview(testProject, secondDraft);
        if (!secondPreview.CanApply ||
            secondPreview.Allocation.Allocation == null ||
            RangesIntersect(secondPreview.Allocation.Allocation.StartVirtualAddress, secondPreview.Allocation.Allocation.EndVirtualAddress, preview.Allocation.Allocation!.StartVirtualAddress, preview.Allocation.Allocation.EndVirtualAddress))
        {
            throw new InvalidOperationException("Assembly patch preview did not avoid a cave range already recorded in manifest allocations.");
        }

        var schema = new EffectPackageService().GetSchemaResource();
        var schemaJson = JsonSerializer.Serialize(schema);
        var schemaProperties = schema.GetType()
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        var hasPreviewAssemblyPatch = schemaJson.Contains("preview_assembly_patch", StringComparison.Ordinal);
        var hasExpectedOldBytes = schemaJson.Contains("expectedOldBytesHex", StringComparison.Ordinal);
        var hasEngineProfileSha = schemaJson.Contains("engineProfileSha256", StringComparison.Ordinal);
        var hasSpecialWorkflow = schemaProperties.Contains("SpecialSkillPatchWorkflow");
        var hasInlineSpecial = schemaProperties.Contains("InlineSpecialSkillPatch");
        if (!hasPreviewAssemblyPatch ||
            !hasExpectedOldBytes ||
            !hasEngineProfileSha ||
            !hasSpecialWorkflow ||
            !hasInlineSpecial)
        {
            throw new InvalidOperationException(
                "EffectPackage schema does not advertise assembly/special-skill patch workflow and metadata fields. " +
                $"preview={hasPreviewAssemblyPatch}, expected={hasExpectedOldBytes}, sha={hasEngineProfileSha}, specialWorkflow={hasSpecialWorkflow}, inlineSpecial={hasInlineSpecial}, properties={string.Join('|', schemaProperties)}");
        }

        Console.WriteLine($"ASSEMBLY_PATCH_SMOKE_OK root={smokeRoot} caves={scan.Candidates.Count} hook=0x{hookAddress:X8} cave={preview.Allocation.Allocation!.StartVirtualAddressHex} manifest={Path.GetFileName(apply.PatchApplyResult.ManifestPath)}");
    }

    private static bool RangesIntersect(uint leftStart, uint leftEnd, uint rightStart, uint rightEnd)
        => leftStart <= rightEnd && rightStart <= leftEnd;

    private static CczProject ResolveAssemblyPatchSmokeSourceProject(CczProject project)
    {
        var standardRoot = Path.Combine(project.WorkspaceRoot, "基底", "加强版6.5未加密版", "加强版6.5未加密版");
        if (File.Exists(Path.Combine(standardRoot, "Ekd5.exe")))
        {
            return new ProjectDetector().CreateProjectFromGameRoot(standardRoot);
        }

        return project;
    }

    private static EffectPackage ClonePatchPackage(EffectPackage source)
    {
        var clone = new EffectPackage
        {
            SchemaVersion = source.SchemaVersion,
            PackageId = source.PackageId + "-clone",
            Domain = source.Domain,
            EffectId = source.EffectId,
            Name = source.Name,
            Description = source.Description,
            EffectValue = source.EffectValue,
            SourcePrompt = source.SourcePrompt,
            BackupNote = source.BackupNote,
            Metadata = new Dictionary<string, string>(source.Metadata, StringComparer.OrdinalIgnoreCase)
        };

        foreach (var segment in source.PatchSegments)
        {
            clone.PatchSegments.Add(new EffectPatchSegment
            {
                TargetFile = segment.TargetFile,
                AddressKind = segment.AddressKind,
                Address = segment.Address,
                AddressHex = segment.AddressHex,
                BytesHex = segment.BytesHex,
                ExpectedOldBytesHex = segment.ExpectedOldBytesHex,
                CodeCaveId = segment.CodeCaveId,
                HookPoint = segment.HookPoint,
                AssemblySourceHash = segment.AssemblySourceHash,
                AllocatedRange = segment.AllocatedRange,
                EngineProfileSha256 = segment.EngineProfileSha256,
                Comment = segment.Comment
            });
        }

        return clone;
    }
}
