using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class EffectKnowledgeFusionService
{
    private const uint ImageBaseDefault = 0x00400000;
    private const uint CoreEffectEngineAddress = 0x004101D9;
    private const uint AbilityCheckWrapperAddress = 0x0042518F;
    private const uint DualChannelCheckAddress = 0x0041301E;
    private const uint GetEffectValueAddress = 0x00413009;
    private const uint BattleDataAddress = EngineRuntimeSemanticRegistry.PhysicalAttackContextAddress;
    private const uint GetUnitHpAddress = 0x0041B500;
    private const uint GetMaxMpAddress = 0x0040728F;
    private static readonly Regex AddressRegex = new(@"\b(?:0x)?(?:00)?4[0-9A-Fa-f]{5}\b", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public FunctionSemanticCatalog Build(CczProject project, string targetFileName = "Ekd5.exe", bool writeReports = true)
    {
        var targetPath = project.ResolveGameFile(string.IsNullOrWhiteSpace(targetFileName) ? "Ekd5.exe" : targetFileName);
        var discovery = new InjectedEffectDiscoveryService().Discover(project, targetFileName);
        var executable = File.Exists(targetPath) ? ExecutableAnalysisSnapshotCache.Shared.GetBase(targetPath) : null;
        var catalog = new FunctionSemanticCatalog
        {
            TargetFilePath = targetPath,
            TargetFileName = Path.GetFileName(targetPath),
            ExeSha256 = executable?.Sha256 ?? discovery.ExeSha256,
            ImageBase = discovery.ImageBase == 0 ? ImageBaseDefault : discovery.ImageBase
        };

        catalog.SourceDocuments.AddRange(ResolvePrimaryKnowledgeDocuments(project.WorkspaceRoot));
        var knowledge = LoadKnowledgeSeeds(catalog.SourceDocuments);
        AddAnchorFunctions(catalog, knowledge);
        AddInjectedDiscoveryFunctions(catalog, discovery);
        AddInjectedEffectMeanings(catalog, project, discovery);
        EnrichFromExecutable(catalog, executable);
        BuildAgentKnowledge(project, catalog);
        catalog.Summary = BuildSummary(catalog);

        if (writeReports)
        {
            WriteReports(project, catalog);
        }

        return catalog;
    }

    private static void AddAnchorFunctions(FunctionSemanticCatalog catalog, IReadOnlyDictionary<uint, KnowledgeSeed> knowledge)
    {
        AddOrMergeFunction(catalog, new FunctionSemanticRecord
        {
            Address = CoreEffectEngineAddress,
            Name = "core_effect_engine",
            Phase = "core-effect",
            Role = "Dual-channel equipment/personal special-skill check",
            ConfidenceScore = 92,
            EvidenceLevel = EffectSemanticEvidenceLevel.VerifiedStatic,
            SemanticKind = EffectSemanticKind.SwitchEffect,
            MatchedEvidence =
            {
                "knowledge: core engine address 4101D9",
                "knowledge: effect-value flag controls boolean/value return",
                "static-anchor: required callable function for local-agent special-skill drafts"
            },
            MissingEvidence = { "dynamic battle-stage hit for current target EXE if not already captured" },
            SourceSummary = "Local core engine knowledge and static address baseline."
        });

        AddOrMergeFunction(catalog, new FunctionSemanticRecord
        {
            Address = AbilityCheckWrapperAddress,
            Name = "ability_check_wrapper",
            Phase = "core-effect",
            Role = "Wrapper that validates before calling 4101D9 or fallback dual-channel check",
            ConfidenceScore = 88,
            EvidenceLevel = EffectSemanticEvidenceLevel.VerifiedStatic,
            SemanticKind = EffectSemanticKind.SwitchEffect,
            MatchedEvidence = { "knowledge: wrapper path 42518F -> 4101D9 / 41301E", "dynamic-evidence-candidate: documented title-stage hit" },
            MissingEvidence = { "battle-stage hit with effective 4101D9 branch" },
            SourceSummary = "Local function catalog and core engine notes."
        });

        AddOrMergeFunction(catalog, new FunctionSemanticRecord
        {
            Address = DualChannelCheckAddress,
            Name = "dual_channel_check",
            Phase = "core-effect",
            Role = "Fallback dual-channel special-skill check",
            ConfidenceScore = 84,
            EvidenceLevel = EffectSemanticEvidenceLevel.VerifiedStatic,
            SemanticKind = EffectSemanticKind.SwitchEffect,
            MatchedEvidence = { "knowledge: 41301E fallback from ability_check_wrapper", "knowledge: calls get_effect_value" },
            MissingEvidence = { "more effect-id specific battle samples" },
            SourceSummary = "Local x32dbg baseline notes."
        });

        AddOrMergeFunction(catalog, new FunctionSemanticRecord
        {
            Address = GetEffectValueAddress,
            Name = "get_effect_value",
            Phase = "core-effect",
            Role = "Effect value lookup helper",
            ConfidenceScore = 84,
            EvidenceLevel = EffectSemanticEvidenceLevel.VerifiedStatic,
            SemanticKind = EffectSemanticKind.ValueEffect,
            MatchedEvidence = { "knowledge: 413009 value lookup", "knowledge: effect-value mechanism" },
            MissingEvidence = { "per-effect value source confirmation" },
            SourceSummary = "Local function catalog and effect-value notes."
        });

        AddOrMergeFunction(catalog, new FunctionSemanticRecord
        {
            Address = BattleDataAddress,
            Name = "battle_data_context",
            Phase = "battle",
            Role = "Static battle context base used by attack and effect code",
            ConfidenceScore = 82,
            EvidenceLevel = EffectSemanticEvidenceLevel.VerifiedStatic,
            SemanticKind = EffectSemanticKind.EngineExtension,
            MatchedEvidence = { "knowledge: battle data structure base 4927F0", "static-data-anchor" },
            MissingEvidence = { "write/read role per effect function" },
            SourceSummary = "Local battle data structure notes."
        });

        foreach (var semantic in EngineRuntimeSemanticRegistry.Functions.Values)
        {
            AddOrMergeFunction(catalog, new FunctionSemanticRecord
            {
                Address = semantic.Address,
                Name = semantic.Name,
                Phase = semantic.OutputType == "StrategyRecord*" ? "strategy" : "battle",
                Role = $"{semantic.InputType} -> {semantic.OutputType}",
                ConfidenceScore = 92,
                EvidenceLevel = EffectSemanticEvidenceLevel.VerifiedStatic,
                SemanticKind = EffectSemanticKind.EngineExtension,
                MatchedEvidence = { "runtime-semantic-registry", $"static-function-summary:{semantic.Address:X8}" },
                SourceSummary = "6.5 runtime semantic registry and canonical EXE disassembly."
            });
        }

        foreach (var seed in knowledge.Values.Where(seed => !IsAnchorAddress(seed.Address)))
        {
            AddOrMergeFunction(catalog, new FunctionSemanticRecord
            {
                Address = seed.Address,
                Name = FirstNonEmpty(seed.Name, "knowledge_function_" + seed.Address.ToString("X8", CultureInfo.InvariantCulture)),
                Phase = InferPhase(seed.Name + " " + seed.Context),
                Role = FirstNonEmpty(seed.Role, "Function candidate from local knowledge base"),
                ConfidenceScore = seed.DynamicEvidence ? 78 : 66,
                EvidenceLevel = seed.DynamicEvidence ? EffectSemanticEvidenceLevel.VerifiedDynamic : EffectSemanticEvidenceLevel.VerifiedStatic,
                SemanticKind = InferSemanticKind(seed.Name + " " + seed.Context),
                MatchedEvidence = { "knowledge-address:" + FormatVa(seed.Address) },
                MissingEvidence = seed.DynamicEvidence ? [] : ["dynamic hit not attached to this semantic pass"],
                SourceSummary = seed.Source
            });
        }
    }

    private static void AddInjectedDiscoveryFunctions(FunctionSemanticCatalog catalog, InjectedEffectDiscoveryReport discovery)
    {
        foreach (var candidate in discovery.Candidates)
        {
            var evidenceLevel = candidate.DetectionLevel switch
            {
                "KnownExact" => EffectSemanticEvidenceLevel.KnownSample,
                "KnownVariant" => EffectSemanticEvidenceLevel.KnownSample,
                "SemanticCandidate" => EffectSemanticEvidenceLevel.Hypothesis,
                _ => EffectSemanticEvidenceLevel.Hypothesis
            };
            var score = Math.Clamp(candidate.DetectionScore, 35, 95);
            if (candidate.JumpOutAddress.HasValue)
            {
                AddOrMergeFunction(catalog, new FunctionSemanticRecord
                {
                    Address = candidate.JumpOutAddress.Value,
                    Name = NormalizeName(candidate.Name, "effect_hook"),
                    Phase = InferPhase(candidate.Name + " " + candidate.PatchCategory),
                    Role = "Effect hook / original-flow patch point",
                    ConfidenceScore = score,
                    EvidenceLevel = evidenceLevel,
                    SemanticKind = InferSemanticKind(candidate.Name + " " + candidate.UserReadableDiagnosis),
                    MatchedEvidence = candidate.MatchedAnchors.Count == 0 ? ["injected-discovery"] : candidate.MatchedAnchors.ToList(),
                    MissingEvidence = candidate.MissingAnchors.ToList(),
                    RelatedEffectIds = BuildRelatedEffectIds(candidate),
                    SourceSummary = candidate.Source
                });
            }

            if (candidate.CodeCaveEntryAddress.HasValue)
            {
                AddOrMergeFunction(catalog, new FunctionSemanticRecord
                {
                    Address = candidate.CodeCaveEntryAddress.Value,
                    Name = NormalizeName(candidate.Name, "effect_code_cave"),
                    Phase = InferPhase(candidate.Name + " " + candidate.PatchCategory),
                    Role = "Injected effect implementation / code cave entry",
                    ConfidenceScore = Math.Max(50, score - 5),
                    EvidenceLevel = evidenceLevel,
                    SemanticKind = InferSemanticKind(candidate.Name + " " + candidate.UserReadableDiagnosis),
                    MatchedEvidence = candidate.MatchedAnchors.Count == 0 ? ["code-cave"] : candidate.MatchedAnchors.ToList(),
                    MissingEvidence = candidate.MissingAnchors.ToList(),
                    RelatedEffectIds = BuildRelatedEffectIds(candidate),
                    SourceSummary = candidate.Source
                });
            }

            foreach (var group in candidate.CheckGroups.Where(group => group.GuardFunctionAddress.HasValue))
            {
                AddOrMergeFunction(catalog, new FunctionSemanticRecord
                {
                    Address = group.GuardFunctionAddress!.Value,
                    Name = group.GuardFunctionAddress == CoreEffectEngineAddress ? "core_effect_engine" : "guard_function",
                    Phase = "core-effect",
                    Role = "Special-skill guard function referenced by injected candidate",
                    ConfidenceScore = Math.Max(60, score),
                    EvidenceLevel = evidenceLevel == EffectSemanticEvidenceLevel.Hypothesis ? EffectSemanticEvidenceLevel.VerifiedStatic : evidenceLevel,
                    SemanticKind = group.EquipmentSlot?.Value.HasValue == true || group.PersonalSlot?.Value.HasValue == true
                        ? EffectSemanticKind.SwitchEffect
                        : EffectSemanticKind.UnknownCandidate,
                    MatchedEvidence = { "guard-call-from:" + candidate.AddressHex },
                    RelatedEffectIds = BuildRelatedEffectIds(candidate),
                    SourceSummary = candidate.Source
                });
            }
        }
    }

    private static void AddInjectedEffectMeanings(FunctionSemanticCatalog catalog, CczProject project, InjectedEffectDiscoveryReport discovery)
    {
        foreach (var candidate in discovery.Candidates)
        {
            foreach (var (effectId, channel) in EnumerateCandidateEffectIds(candidate))
            {
                var existing = catalog.Effects.FirstOrDefault(item => item.EffectId == effectId && item.Channel.Equals(channel, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    existing = new EffectMeaningRecord
                    {
                        EffectId = effectId,
                        Channel = channel,
                        EvidenceLevel = candidate.Type.StartsWith("KnownPatch", StringComparison.OrdinalIgnoreCase)
                            ? EffectSemanticEvidenceLevel.KnownSample
                            : EffectSemanticEvidenceLevel.Hypothesis,
                        ConfidenceScore = Math.Clamp(candidate.DetectionScore, 45, 92),
                        TriggerPhase = InferPhase(candidate.Name + " " + candidate.UserReadableDiagnosis),
                        SemanticKind = InferSemanticKind(candidate.Name + " " + candidate.UserReadableDiagnosis),
                        ImplementationFunction = FormatVa(candidate.CodeCaveEntryAddress ?? candidate.JumpOutAddress ?? candidate.Address),
                        SourceSummary = candidate.Source
                    };
                    catalog.Effects.Add(existing);
                }

                if (!string.IsNullOrWhiteSpace(candidate.Name) &&
                    !existing.NameCandidates.Contains(candidate.Name, StringComparer.OrdinalIgnoreCase))
                {
                    existing.NameCandidates.Add(candidate.Name);
                }

                existing.ObservedMeaning = FirstNonEmpty(existing.ObservedMeaning, candidate.UserReadableDiagnosis, candidate.StructureDiagnosis, candidate.ModuleSummary);
                existing.ValueFlagMeaning = candidate.EffectValueFlag switch
                {
                    0 => "returns configured effect value",
                    1 => "returns boolean ownership flag",
                    _ => FirstNonEmpty(existing.ValueFlagMeaning, "unknown from current candidate")
                };
                existing.StackingMeaning = candidate.StackingFlag switch
                {
                    0 => "equipment and personal channels can stack",
                    1 => "non-stacking; one channel is used",
                    2 => "equipment first, personal fallback",
                    _ => FirstNonEmpty(existing.StackingMeaning, "unknown from current candidate")
                };
                existing.RecommendedInjectionTemplate = BuildRecommendedTemplate(existing);
                AddDistinct(existing.MatchedEvidence, candidate.MatchedAnchors.Count == 0 ? ["injected-effect-candidate"] : candidate.MatchedAnchors);
                AddDistinct(existing.MissingEvidence, candidate.MissingAnchors);
                if (candidate.PatchCategory == InjectedEffectPatchCategory.ComplexMultiHookPatch)
                {
                    AddDistinct(existing.MissingEvidence, ["complex multi-hook patch; do not generate single-hook write automatically"]);
                }
            }
        }

        foreach (var catalogEntry in new InjectedEffectDiscoveryService().LoadKnownPatchCatalog(project))
        {
            foreach (var (effectId, channel) in EnumerateCandidateEffectIds(catalogEntry))
            {
                var effect = catalog.Effects.FirstOrDefault(item => item.EffectId == effectId && item.Channel.Equals(channel, StringComparison.OrdinalIgnoreCase));
                if (effect == null)
                {
                    effect = new EffectMeaningRecord
                    {
                        EffectId = effectId,
                        Channel = channel,
                        EvidenceLevel = EffectSemanticEvidenceLevel.KnownSample,
                        ConfidenceScore = 72,
                        TriggerPhase = InferPhase(catalogEntry.Name + " " + catalogEntry.PatchCategory),
                        SemanticKind = InferSemanticKind(catalogEntry.Name + " " + catalogEntry.StructureDiagnosis),
                        ImplementationFunction = FormatVa(catalogEntry.CodeCaveEntryAddress ?? catalogEntry.JumpOutAddress ?? catalogEntry.Address),
                        SourceSummary = catalogEntry.Source
                    };
                    catalog.Effects.Add(effect);
                }

                AddDistinct(effect.NameCandidates, [catalogEntry.Name]);
                effect.ObservedMeaning = FirstNonEmpty(effect.ObservedMeaning, catalogEntry.StructureDiagnosis, catalogEntry.UserReadableDiagnosis);
                effect.RecommendedInjectionTemplate = BuildRecommendedTemplate(effect);
                AddDistinct(effect.MatchedEvidence, ["known-sample-catalog"]);
                if (catalogEntry.PatchCategory is InjectedEffectPatchCategory.ComplexMultiHookPatch or InjectedEffectPatchCategory.FunctionExtensionPatch)
                {
                    AddDistinct(effect.MissingEvidence, ["requires manual review before local-agent generated write"]);
                }
            }
        }

        catalog.Effects = catalog.Effects
            .OrderBy(item => item.EffectId)
            .ThenBy(item => item.Channel, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void EnrichFromExecutable(FunctionSemanticCatalog catalog, ExecutableAnalysisSnapshot? executable)
    {
        if (executable == null)
        {
            catalog.Warnings.Add("Target EXE not found; executable xref enrichment skipped.");
            return;
        }
        var bytes = executable.Bytes;
        var imageBase = executable.PeImage.ImageBase;
        var sections = executable.PeImage.Sections;
        catalog.ImageBase = imageBase;
        var scan = executable.InstructionScan;
        var functionAddresses = catalog.Functions.Select(item => item.Address).ToHashSet();
        var knownTargets = catalog.Functions.Select(item => item.Address).ToHashSet();
        knownTargets.Add(CoreEffectEngineAddress);
        var sectionIndexByAddress = BuildSectionInstructionIndex(scan);
        var callsByTarget = scan.Instructions
            .Where(instruction => instruction.IsDirectCall &&
                                  instruction.BranchTarget.HasValue &&
                                  knownTargets.Contains(instruction.BranchTarget.Value))
            .GroupBy(instruction => instruction.BranchTarget!.Value)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Address).Distinct().OrderBy(item => item).Take(16).ToList());

        foreach (var function in catalog.Functions)
        {
            if (TryVaToOffset(sections, imageBase, function.Address, out var offset))
            {
                function.FileOffset = offset;
                AddDistinct(function.MatchedEvidence, ["pe-map:file-offset"]);
            }
            else if (function.Address >= imageBase)
            {
                AddDistinct(function.MissingEvidence, ["pe-map:address-outside-sections"]);
            }

            if (scan.InstructionsByAddress.TryGetValue(function.Address, out var entry))
            {
                AddDistinct(function.MatchedEvidence, ["decoded-entry:" + entry.Mnemonic]);
            }

            if (callsByTarget.TryGetValue(function.Address, out var callers))
            {
                function.CalledBy = callers;
                function.ConfidenceScore = Math.Min(100, function.ConfidenceScore + Math.Min(8, callers.Count));
                AddDistinct(function.MatchedEvidence, ["static-xref:called-by=" + callers.Count.ToString(CultureInfo.InvariantCulture)]);
            }

            var nearby = GetInstructionWindow(scan, sectionIndexByAddress, function.Address, maximumBytes: 0x160, maximumInstructions: 96);
            var localCalls = nearby
                .Where(instruction => instruction.Address < function.Address + 0x120 &&
                                      instruction.IsDirectCall &&
                                      instruction.BranchTarget.HasValue)
                .Select(instruction => instruction.BranchTarget!.Value)
                .Distinct()
                .Take(16)
                .ToList();
            function.Calls = localCalls;
            if (localCalls.Contains(CoreEffectEngineAddress))
            {
                AddDistinct(function.MatchedEvidence, ["static-xref:calls-core-effect-engine"]);
                function.SemanticKind = function.SemanticKind == EffectSemanticKind.UnknownCandidate ? EffectSemanticKind.SwitchEffect : function.SemanticKind;
                function.ConfidenceScore = Math.Min(100, function.ConfidenceScore + 8);
            }

            if (nearby.Any(instruction => instruction.Bytes.Any(value => value == 0xF0) || instruction.Operands.Any(operand => operand.MemoryText.Contains("4927", StringComparison.OrdinalIgnoreCase))))
            {
                AddDistinct(function.Reads, ["battle-data-candidate:004927F0"]);
            }
            if (nearby.Any(instruction => instruction.Mnemonic.StartsWith("mov", StringComparison.OrdinalIgnoreCase) &&
                                          instruction.Operands.FirstOrDefault()?.Kind == "Memory"))
            {
                AddDistinct(function.Writes, ["memory-write-candidate"]);
            }
        }

        foreach (var section in scan.InstructionsBySection.Values)
        {
            for (var index = 0; index < section.Count; index++)
            {
                var call = section[index];
                if (!call.IsDirectCall || call.BranchTarget != CoreEffectEngineAddress) continue;
                var callerAddress = EstimateFunctionStart(section, index);
                if (functionAddresses.Contains(callerAddress)) continue;

                AddOrMergeFunction(catalog, new FunctionSemanticRecord
                {
                    Address = callerAddress,
                    Name = "core_call_site_" + callerAddress.ToString("X8", CultureInfo.InvariantCulture),
                    Phase = "core-effect",
                    Role = "Static caller of 004101D9 discovered from executable",
                    ConfidenceScore = 62,
                    EvidenceLevel = EffectSemanticEvidenceLevel.VerifiedStatic,
                    SemanticKind = EffectSemanticKind.SwitchEffect,
                    Calls = [CoreEffectEngineAddress],
                    MatchedEvidence = { "static-xref:calls-core-effect-engine", "decoded-call:" + FormatVa(call.Address) },
                    MissingEvidence = { "semantic source name not found in local knowledge" },
                    SourceSummary = executable.Fingerprint.Path
                });
                functionAddresses.Add(callerAddress);
            }
        }

        catalog.Functions = catalog.Functions
            .OrderByDescending(item => item.ConfidenceScore)
            .ThenBy(item => item.Address)
            .ToList();
    }

    private static void BuildAgentKnowledge(CczProject project, FunctionSemanticCatalog catalog)
    {
        var callable = catalog.Functions
            .Where(item => item.EvidenceLevel is EffectSemanticEvidenceLevel.VerifiedDynamic or EffectSemanticEvidenceLevel.VerifiedStatic &&
                           item.ConfidenceScore >= 70 &&
                           item.SemanticKind != EffectSemanticKind.UnknownCandidate)
            .OrderByDescending(item => item.ConfidenceScore)
            .Take(20)
            .ToList();
        var effects = catalog.Effects
            .Where(item => item.EvidenceLevel is EffectSemanticEvidenceLevel.VerifiedDynamic or EffectSemanticEvidenceLevel.VerifiedStatic or EffectSemanticEvidenceLevel.KnownSample &&
                           item.ConfidenceScore >= 65)
            .OrderByDescending(item => item.ConfidenceScore)
            .Take(32)
            .ToList();

        catalog.AgentKnowledge = new AgentSpecialEffectKnowledge
        {
            CallableFunctions = callable,
            EffectTemplates = effects,
            Guardrails =
            {
                "Do not write patches directly from semantic inference.",
                "Local agents must generate structured drafts and pass MCP preview before apply.",
                "Use 004101D9 for guarded special-skill checks only when hook, old bytes, return path, and preview/re-read pass.",
                "Treat KnownSample as sample format, not proof that the current EXE is installed.",
                "Complex multi-hook and engine-extension effects require manual review and are not one-click inline-special-skill drafts."
            },
            HookContracts = new HookContractService().BuildContracts(project).ToList(),
            DraftSafetyFields =
            {
                "HookContractId",
                "OriginalInstructionPolicy",
                "OriginalInstructionPlacement",
                "PreserveFlags",
                "ExpectedStackDelta",
                "RequiredSymbols"
            }
        };
        catalog.AgentKnowledge.AgentContext = BuildAgentContext(catalog.AgentKnowledge);
    }

    private static IReadOnlyDictionary<uint, KnowledgeSeed> LoadKnowledgeSeeds(IEnumerable<string> paths)
    {
        var result = new Dictionary<uint, KnowledgeSeed>();
        foreach (var path in paths.Where(File.Exists))
        {
            var text = ReadTextSmart(path);
            foreach (Match match in AddressRegex.Matches(text))
            {
                if (!TryParseAddress(match.Value, out var address)) continue;
                var context = SliceContext(text, match.Index, 180);
                if (!result.TryGetValue(address, out var seed))
                {
                    seed = new KnowledgeSeed(address, string.Empty, string.Empty, context, path, DynamicEvidence: false);
                }

                seed = seed with
                {
                    Name = FirstNonEmpty(seed.Name, InferNameFromContext(context)),
                    Role = FirstNonEmpty(seed.Role, InferRoleFromContext(context)),
                    Context = FirstNonEmpty(seed.Context, context),
                    DynamicEvidence = seed.DynamicEvidence || ContainsDynamicEvidence(context)
                };
                result[address] = seed;
            }
        }

        return result;
    }

    private static IReadOnlyList<string> ResolvePrimaryKnowledgeDocuments(string workspaceRoot)
    {
        var resolved = ResolvePrimaryKnowledgeDocumentsFromKnownRoots(workspaceRoot);
        if (resolved.Count > 0) return resolved;

        var root = Path.Combine(workspaceRoot, "工具整合包", "本地知识库");
        if (!Directory.Exists(root))
        {
            root = Path.Combine(Directory.GetParent(workspaceRoot)?.FullName ?? workspaceRoot, "工具整合包", "本地知识库");
        }

        var relative = new[]
        {
            "README.md",
            Path.Combine("04-函数速查", "函数速查表.md"),
            Path.Combine("01-核心引擎", "核心引擎.md"),
            Path.Combine("01-核心引擎", "桩函数.md"),
            Path.Combine("03-机制详解", "6.5特效注入索引.md"),
            Path.Combine("03-机制详解", "特效值机制.md"),
            Path.Combine("02-数据结构", "战斗数据结构.md"),
            Path.Combine("00-总览与规范", "待验证清单.md"),
            Path.Combine("09-版本与外部资料", "联网资料索引-曹操传模组6.5.md"),
            Path.Combine("09-版本与外部资料", "联网深度专题.md")
        };

        return relative.Select(item => Path.Combine(root, item)).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<string> ResolvePrimaryKnowledgeDocumentsFromKnownRoots(string workspaceRoot)
    {
        var parent = Directory.GetParent(workspaceRoot)?.FullName ?? workspaceRoot;
        var candidateRoots = new[]
            {
                Path.Combine(workspaceRoot, "\u5de5\u5177\u6574\u5408\u5305", "\u672c\u5730\u77e5\u8bc6\u5e93"),
                Path.Combine(parent, "\u5de5\u5177\u6574\u5408\u5305", "\u672c\u5730\u77e5\u8bc6\u5e93"),
                Path.Combine(workspaceRoot, "\u672c\u5730\u77e5\u8bc6\u5e93"),
                Path.Combine(parent, "\u672c\u5730\u77e5\u8bc6\u5e93")
            }
            .Concat(FindKnowledgeRoots(workspaceRoot).Take(4))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var root = candidateRoots.FirstOrDefault(Directory.Exists);
        if (string.IsNullOrWhiteSpace(root)) return [];

        var relative = new[]
        {
            "README.md",
            Path.Combine("04-\u51fd\u6570\u901f\u67e5", "\u51fd\u6570\u901f\u67e5\u8868.md"),
            Path.Combine("01-\u6838\u5fc3\u5f15\u64ce", "\u6838\u5fc3\u5f15\u64ce.md"),
            Path.Combine("01-\u6838\u5fc3\u5f15\u64ce", "\u6869\u51fd\u6570.md"),
            Path.Combine("03-\u673a\u5236\u8be6\u89e3", "6.5\u7279\u6548\u6ce8\u5165\u7d22\u5f15.md"),
            Path.Combine("03-\u673a\u5236\u8be6\u89e3", "\u7279\u6548\u503c\u673a\u5236.md"),
            Path.Combine("02-\u6570\u636e\u7ed3\u6784", "\u6218\u6597\u6570\u636e\u7ed3\u6784.md"),
            Path.Combine("00-\u603b\u89c8\u4e0e\u89c4\u8303", "\u5f85\u9a8c\u8bc1\u6e05\u5355.md"),
            Path.Combine("09-\u7248\u672c\u4e0e\u5916\u90e8\u8d44\u6599", "\u8054\u7f51\u8d44\u6599\u7d22\u5f15-\u66f9\u64cd\u4f20\u6a21\u7ec46.5.md"),
            Path.Combine("09-\u7248\u672c\u4e0e\u5916\u90e8\u8d44\u6599", "\u8054\u7f51\u6df1\u5ea6\u4e13\u9898.md")
        };

        return relative.Select(item => Path.Combine(root, item))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> FindKnowledgeRoots(string workspaceRoot)
    {
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((workspaceRoot, 0));
        while (queue.Count > 0)
        {
            var (path, depth) = queue.Dequeue();
            if (depth > 3) continue;
            IEnumerable<string> directories;
            try { directories = Directory.EnumerateDirectories(path); }
            catch { continue; }

            foreach (var directory in directories)
            {
                var name = Path.GetFileName(directory);
                if (name.Equals("\u672c\u5730\u77e5\u8bc6\u5e93", StringComparison.OrdinalIgnoreCase))
                {
                    yield return directory;
                }

                if (depth < 3 &&
                    !name.Equals("bin", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("obj", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("CCZModStudio_Reports", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("CCZModStudio_TestCopies", StringComparison.OrdinalIgnoreCase))
                {
                    queue.Enqueue((directory, depth + 1));
                }
            }
        }
    }

    private static void WriteReports(CczProject project, FunctionSemanticCatalog catalog)
    {
        var root = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Reports", "EffectSemantics");
        Directory.CreateDirectory(root);
        var jsonPath = Path.Combine(root, "effect_semantic_catalog.json");
        var markdownPath = Path.Combine(root, "effect_semantic_catalog.md");
        var agentJsonPath = Path.Combine(root, "agent_special_effect_knowledge.json");
        var agentMarkdownPath = Path.Combine(root, "agent_special_effect_knowledge.md");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(catalog, JsonOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        File.WriteAllText(markdownPath, BuildMarkdown(catalog), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        File.WriteAllText(agentJsonPath, JsonSerializer.Serialize(catalog.AgentKnowledge, JsonOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        File.WriteAllText(agentMarkdownPath, BuildAgentKnowledgeMarkdown(catalog), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        catalog.ReportPaths.Clear();
        catalog.ReportPaths.Add(jsonPath);
        catalog.ReportPaths.Add(markdownPath);
        catalog.ReportPaths.Add(agentJsonPath);
        catalog.ReportPaths.Add(agentMarkdownPath);
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(catalog, JsonOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string BuildMarkdown(FunctionSemanticCatalog catalog)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Effect Semantic Catalog");
        builder.AppendLine();
        builder.AppendLine(catalog.Summary);
        builder.AppendLine();
        builder.AppendLine("## Functions");
        foreach (var function in catalog.Functions.Take(80))
        {
            builder.AppendLine($"- `{function.AddressHex}` `{function.Name}` phase={function.Phase} kind={function.SemanticKind} score={function.ConfidenceScore} evidence={function.EvidenceLevel}");
            if (function.CalledBy.Count > 0) builder.AppendLine("  - calledBy: " + string.Join(", ", function.CalledBy.Take(6).Select(FormatVa)));
            if (function.Calls.Count > 0) builder.AppendLine("  - calls: " + string.Join(", ", function.Calls.Take(6).Select(FormatVa)));
            if (function.MatchedEvidence.Count > 0) builder.AppendLine("  - matched: " + string.Join("; ", function.MatchedEvidence.Take(5)));
            if (function.MissingEvidence.Count > 0) builder.AppendLine("  - missing: " + string.Join("; ", function.MissingEvidence.Take(4)));
        }

        builder.AppendLine();
        builder.AppendLine("## Effects");
        foreach (var effect in catalog.Effects.Take(80))
        {
            builder.AppendLine($"- `{effect.EffectIdHex}` channel={effect.Channel} names={string.Join(" / ", effect.NameCandidates.Take(4))} kind={effect.SemanticKind} score={effect.ConfidenceScore} evidence={effect.EvidenceLevel}");
            builder.AppendLine("  - meaning: " + effect.ObservedMeaning);
            builder.AppendLine("  - injection-template: " + effect.RecommendedInjectionTemplate);
            if (effect.MissingEvidence.Count > 0) builder.AppendLine("  - missing: " + string.Join("; ", effect.MissingEvidence.Take(4)));
        }

        builder.AppendLine();
        builder.AppendLine("## Local Agent Knowledge");
        builder.AppendLine("```text");
        builder.AppendLine(catalog.AgentKnowledge.AgentContext);
        builder.AppendLine("```");
        return builder.ToString();
    }

    private static string BuildAgentKnowledgeMarkdown(FunctionSemanticCatalog catalog)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Local Agent Special Effect Knowledge");
        builder.AppendLine();
        builder.AppendLine("This is the compact knowledge pack for a local agent that generates injectable special-effect code. CCZModStudio validates, previews, applies, backs up, and reports the patch through UI/MCP; it does not need an embedded AI generator.");
        builder.AppendLine();
        builder.AppendLine("## Target");
        builder.AppendLine("- file: " + catalog.TargetFilePath);
        builder.AppendLine("- sha256: " + catalog.ExeSha256);
        builder.AppendLine("- imageBase: " + catalog.ImageBaseHex);
        builder.AppendLine();
        builder.AppendLine("## Required Workflow");
        builder.AppendLine("1. Read this pack and the semantic catalog.");
        builder.AppendLine("2. Generate an `InlineSpecialSkillPatchDraft` for simple guarded special-skill hooks, or an `AssemblyPatchDraft` only when a reviewed hook spec exists.");
        builder.AppendLine("3. Call MCP `preview_special_skill_patch` or `preview_assembly_patch`; never call apply on raw assembly text.");
        builder.AppendLine("4. Apply only the compiled `EffectPackage` returned by a successful preview, using `apply_special_skill_patch` or `apply_assembly_patch`.");
        builder.AppendLine("5. Keep complex multi-hook, function-extension, and hypothesis-only ideas as manual-review drafts.");
        builder.AppendLine();
        builder.AppendLine("## Agent Context");
        builder.AppendLine("```text");
        builder.AppendLine(catalog.AgentKnowledge.AgentContext);
        builder.AppendLine("```");
        return builder.ToString();
    }

    private static string BuildAgentContext(AgentSpecialEffectKnowledge context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Usage: " + context.UsagePolicy);
        builder.AppendLine("Guardrails:");
        foreach (var guardrail in context.Guardrails) builder.AppendLine("- " + guardrail);
        builder.AppendLine("MCP flow:");
        builder.AppendLine("- discovery/context: scan_installed_effects, read_effect_instance, explain_exe_address, read_effect_generation_context");
        builder.AppendLine("- simple special skill: draft InlineSpecialSkillPatchDraft, preview_special_skill_patch, apply_special_skill_patch");
        builder.AppendLine("- custom assembly: draft AssemblyPatchDraft, preview_assembly_patch, apply_assembly_patch");
        builder.AppendLine("- raw package: preview_effect_patch, apply_effect_patch");
        builder.AppendLine("Draft safety fields: " + string.Join(", ", context.DraftSafetyFields));
        builder.AppendLine("Hook contracts:");
        foreach (var contract in context.HookContracts)
        {
            builder.AppendLine($"- {contract.ContractId}: hook={contract.HookAddressHex}; phase={contract.TriggerPhase}; original={contract.OriginalInstructionPolicy}/{contract.OriginalInstructionPlacement}; conflict={contract.ConflictGroup}");
        }
        builder.AppendLine("Callable functions:");
        foreach (var function in context.CallableFunctions)
        {
            builder.AppendLine($"- {function.AddressHex} {function.Name}: {function.Role}; phase={function.Phase}; evidence={function.EvidenceLevel}");
        }

        builder.AppendLine("Effect templates:");
        foreach (var effect in context.EffectTemplates)
        {
            builder.AppendLine($"- {effect.EffectIdHex} {string.Join("/", effect.NameCandidates.Take(2))}: {effect.SemanticKind}; {effect.RecommendedInjectionTemplate}");
        }

        var text = builder.ToString();
        return text.Length <= context.ContextBudgetCharacters ? text : text[..context.ContextBudgetCharacters];
    }

    private static string BuildSummary(FunctionSemanticCatalog catalog)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"functions={catalog.Functions.Count}, effects={catalog.Effects.Count}, agentCallableFunctions={catalog.AgentKnowledge.CallableFunctions.Count}, agentTemplates={catalog.AgentKnowledge.EffectTemplates.Count}, sources={catalog.SourceDocuments.Count}");

    private static bool TryReadPe(string path, out byte[] bytes, out uint imageBase, out List<ExeSectionInfo> sections, out string warning)
    {
        bytes = [];
        imageBase = ImageBaseDefault;
        sections = [];
        warning = string.Empty;
        try
        {
            var peBytes = File.ReadAllBytes(path);
            bytes = peBytes;
            ushort U16(int offset) => BitConverter.ToUInt16(peBytes, offset);
            uint U32(int offset) => BitConverter.ToUInt32(peBytes, offset);
            var peOffset = checked((int)U32(0x3C));
            if (peOffset < 0 || peOffset + 24 > bytes.Length)
            {
                warning = "PE header offset outside file.";
                return false;
            }

            if (U32(peOffset) != 0x00004550)
            {
                warning = "Invalid PE signature.";
                return false;
            }

            var sectionCount = U16(peOffset + 6);
            var optionalHeaderSize = U16(peOffset + 20);
            var optionalHeaderStart = peOffset + 24;
            imageBase = U16(optionalHeaderStart) == 0x10B ? U32(optionalHeaderStart + 28) : ImageBaseDefault;
            var sectionStart = optionalHeaderStart + optionalHeaderSize;
            for (var index = 0; index < sectionCount; index++)
            {
                var offset = sectionStart + index * 40;
                if (offset + 40 > bytes.Length) break;
                var name = Encoding.ASCII.GetString(bytes, offset, 8).TrimEnd('\0');
                var virtualSize = U32(offset + 8);
                var virtualAddress = U32(offset + 12);
                var rawSize = U32(offset + 16);
                var rawPointer = U32(offset + 20);
                var characteristics = U32(offset + 36);
                sections.Add(new ExeSectionInfo
                {
                    Name = name,
                    VirtualAddress = virtualAddress,
                    VirtualSize = virtualSize,
                    RawPointer = rawPointer,
                    RawSize = rawSize,
                    Characteristics = characteristics,
                    IsExecutable = (characteristics & 0x20000000) != 0
                });
            }

            return true;
        }
        catch (Exception ex)
        {
            warning = ex.Message;
            return false;
        }
    }

    private static bool TryVaToOffset(IEnumerable<ExeSectionInfo> sections, uint imageBase, uint address, out int offset)
    {
        offset = -1;
        if (address < imageBase) return false;
        var rva = address - imageBase;
        foreach (var section in sections)
        {
            var size = Math.Max(section.VirtualSize, section.RawSize);
            if (rva < section.VirtualAddress || rva >= section.VirtualAddress + size) continue;
            offset = checked((int)(section.RawPointer + (rva - section.VirtualAddress)));
            return true;
        }

        return false;
    }

    private static Dictionary<uint, (List<X86InstructionInfo> Instructions, int Index)> BuildSectionInstructionIndex(X86ScanResult scan)
    {
        var result = new Dictionary<uint, (List<X86InstructionInfo>, int)>();
        foreach (var section in scan.InstructionsBySection.Values)
        {
            for (var index = 0; index < section.Count; index++)
            {
                result[section[index].Address] = (section, index);
            }
        }

        return result;
    }

    private static IReadOnlyList<X86InstructionInfo> GetInstructionWindow(
        X86ScanResult scan,
        IReadOnlyDictionary<uint, (List<X86InstructionInfo> Instructions, int Index)> indexByAddress,
        uint address,
        int maximumBytes,
        int maximumInstructions)
    {
        if (!indexByAddress.TryGetValue(address, out var location))
        {
            var entry = scan.Instructions
                .Where(item => item.Address >= address)
                .OrderBy(item => item.Address)
                .FirstOrDefault();
            if (entry == null || !indexByAddress.TryGetValue(entry.Address, out location))
            {
                return [];
            }
        }

        var endAddress = checked(address + (uint)Math.Max(0, maximumBytes));
        var result = new List<X86InstructionInfo>();
        for (var cursor = location.Index; cursor < location.Instructions.Count && result.Count < maximumInstructions; cursor++)
        {
            var instruction = location.Instructions[cursor];
            if (instruction.Address < address) continue;
            if (instruction.Address >= endAddress) break;
            result.Add(instruction);
        }

        return result;
    }

    private static uint EstimateFunctionStart(List<X86InstructionInfo> instructions, int index)
    {
        if (index < 0 || index >= instructions.Count) return 0;
        for (var cursor = index; cursor >= Math.Max(0, index - 80); cursor--)
        {
            var instruction = instructions[cursor];
            if (instruction.Bytes.Length >= 3 &&
                instruction.Bytes[0] == 0x55 &&
                instruction.Bytes[1] == 0x8B &&
                instruction.Bytes[2] == 0xEC)
            {
                return instruction.Address;
            }
        }

        return instructions[Math.Max(0, index - 12)].Address;
    }

    private static void AddOrMergeFunction(FunctionSemanticCatalog catalog, FunctionSemanticRecord incoming)
    {
        var existing = catalog.Functions.FirstOrDefault(item => item.Address == incoming.Address);
        if (existing == null)
        {
            catalog.Functions.Add(incoming);
            return;
        }

        existing.Name = PreferKnown(existing.Name, incoming.Name);
        existing.Phase = PreferKnown(existing.Phase, incoming.Phase);
        existing.Role = PreferKnown(existing.Role, incoming.Role);
        existing.SemanticKind = existing.SemanticKind == EffectSemanticKind.UnknownCandidate ? incoming.SemanticKind : existing.SemanticKind;
        existing.ConfidenceScore = Math.Max(existing.ConfidenceScore, incoming.ConfidenceScore);
        existing.EvidenceLevel = StrongerEvidence(existing.EvidenceLevel, incoming.EvidenceLevel);
        AddDistinct(existing.MatchedEvidence, incoming.MatchedEvidence);
        AddDistinct(existing.MissingEvidence, incoming.MissingEvidence);
        AddDistinct(existing.Reads, incoming.Reads);
        AddDistinct(existing.Writes, incoming.Writes);
        AddDistinct(existing.Calls, incoming.Calls);
        AddDistinct(existing.CalledBy, incoming.CalledBy);
        AddDistinct(existing.RelatedEffectIds, incoming.RelatedEffectIds);
        existing.SourceSummary = FirstNonEmpty(existing.SourceSummary, incoming.SourceSummary);
    }

    private static IEnumerable<(int EffectId, string Channel)> EnumerateCandidateEffectIds(InjectedEffectCandidate candidate)
    {
        if (candidate.PersonalEffectId.HasValue) yield return (candidate.PersonalEffectId.Value, "Personal");
        if (candidate.EquipmentEffectId.HasValue) yield return (candidate.EquipmentEffectId.Value, "Equipment");
        foreach (var slot in candidate.ParameterSlots.Where(slot => slot.Value.HasValue &&
                                                                    slot.Role is InjectedEffectParameterRole.Personal or InjectedEffectParameterRole.Equipment))
        {
            yield return (slot.Value!.Value, slot.Role == InjectedEffectParameterRole.Personal ? "Personal" : "Equipment");
        }
    }

    private static List<int> BuildRelatedEffectIds(InjectedEffectCandidate candidate)
        => EnumerateCandidateEffectIds(candidate).Select(item => item.EffectId).Distinct().ToList();

    private static string BuildRecommendedTemplate(EffectMeaningRecord effect)
        => effect.SemanticKind switch
        {
            EffectSemanticKind.ValueEffect => "Use 004101D9 with effect-value flag 0; consume EAX as configured value after preview.",
            EffectSemanticKind.DamageModifier => "Use a guarded damage-flow hook; call 004101D9 before modifying damage; require manual return-point review.",
            EffectSemanticKind.Recovery => "Use a turn or post-action recovery hook; call 004101D9 with value flag 0 and add bounded EAX.",
            EffectSemanticKind.ActionControl => "Use 004101D9 with effect-value flag 1; branch on EAX without changing data tables.",
            EffectSemanticKind.EngineExtension => "Do not auto-generate a write patch; use as reference only.",
            _ => "Use 004101D9 with explicit four parameters; generate draft only and require preview/re-read."
        };

    private static string InferSemanticKind(string text)
    {
        if (ContainsAny(text, "恢复", "回復", "MP", "HP", "recover", "recovery")) return EffectSemanticKind.Recovery;
        if (ContainsAny(text, "伤", "傷", "damage", "冲锋", "伐谋", "减伤", "增伤", "保底", "限伤")) return EffectSemanticKind.DamageModifier;
        if (ContainsAny(text, "状态", "中毒", "眩", "随机状态", "status")) return EffectSemanticKind.StatusInflict;
        if (ContainsAny(text, "二次行动", "连击", "追击", "引导", "行动")) return EffectSemanticKind.ActionControl;
        if (ContainsAny(text, "策略", "法术", "计策")) return EffectSemanticKind.StrategyModifier;
        if (ContainsAny(text, "整型", "信息传送", "函数指针", "扩展")) return EffectSemanticKind.EngineExtension;
        if (ContainsAny(text, "效果值", "特效值", "value")) return EffectSemanticKind.ValueEffect;
        if (ContainsAny(text, "4101D9", "特技", "特效")) return EffectSemanticKind.SwitchEffect;
        return EffectSemanticKind.UnknownCandidate;
    }

    private static string InferPhase(string text)
    {
        if (ContainsAny(text, "strategy", "策略", "法术", "计策")) return "strategy";
        if (ContainsAny(text, "turn", "回合", "恢复", "recovery")) return "turn";
        if (ContainsAny(text, "attack_after", "post", "伤后", "击后", "暴击", "crit")) return "attack-after";
        if (ContainsAny(text, "attack", "攻击", "伤害", "damage", "冲锋", "伐谋")) return "attack";
        if (ContainsAny(text, "battle", "战斗", "战场")) return "battle";
        if (ContainsAny(text, "core", "4101D9", "特技判定", "特效值")) return "core-effect";
        return "unknown";
    }

    private static string InferNameFromContext(string context)
    {
        var match = Regex.Match(context, @"\|\s*`?(?:0x)?(?:00)?4[0-9A-Fa-f]{5}`?\s*\|\s*([^|\r\n]+)\|");
        if (match.Success) return NormalizeName(match.Groups[1].Value, string.Empty);
        match = Regex.Match(context, @"\b([A-Za-z_][A-Za-z0-9_]{3,})\b");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static string InferRoleFromContext(string context)
        => context.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static bool ContainsDynamicEvidence(string text)
        => ContainsAny(text, "已动态命中", "x32dbg-hit", "VerifiedDynamic", "动态命中");

    private static string SliceContext(string text, int index, int radius)
    {
        var start = Math.Max(0, index - radius);
        var length = Math.Min(text.Length - start, radius * 2);
        return text.Substring(start, length).Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    private static string NormalizeName(string text, string fallback)
    {
        var cleaned = Regex.Replace(text ?? string.Empty, @"[^\p{L}\p{Nd}_\-/]+", "_").Trim('_');
        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
    }

    private static bool TryParseAddress(string text, out uint address)
    {
        var trimmed = text.Trim().Trim('`');
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[2..];
        return uint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
    }

    private static string ReadTextSmart(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes[3..]);
        try { return Encoding.UTF8.GetString(bytes); }
        catch { return Encoding.GetEncoding("GBK").GetString(bytes); }
    }

    private static string StrongerEvidence(string left, string right)
    {
        static int Rank(string value) => value switch
        {
            EffectSemanticEvidenceLevel.VerifiedDynamic => 5,
            EffectSemanticEvidenceLevel.VerifiedStatic => 4,
            EffectSemanticEvidenceLevel.KnownSample => 3,
            EffectSemanticEvidenceLevel.ExternalCorroboration => 2,
            EffectSemanticEvidenceLevel.Hypothesis => 1,
            _ => 0
        };

        return Rank(right) > Rank(left) ? right : left;
    }

    private static string PreferKnown(string left, string right)
        => string.IsNullOrWhiteSpace(left) || left.StartsWith("knowledge_function_", StringComparison.OrdinalIgnoreCase)
            ? right
            : left;

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static bool ContainsAny(string text, params string[] needles)
        => needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static bool IsAnchorAddress(uint address)
        => address is CoreEffectEngineAddress or AbilityCheckWrapperAddress or DualChannelCheckAddress or GetEffectValueAddress or BattleDataAddress ||
           EngineRuntimeSemanticRegistry.Functions.ContainsKey(address);

    private static void AddDistinct<T>(ICollection<T> target, IEnumerable<T> values)
    {
        foreach (var value in values)
        {
            if (!target.Contains(value)) target.Add(value);
        }
    }

    private static string FormatVa(uint value) => $"0x{value:X8}";

    private static string ComputeSha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes));

    private sealed record KnowledgeSeed(
        uint Address,
        string Name,
        string Role,
        string Context,
        string Source,
        bool DynamicEvidence);
}
