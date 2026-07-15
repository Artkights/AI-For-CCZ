using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class InstalledEffectEvidenceService
{
    public InstalledEffectEvidenceDecision Evaluate(InjectedEffectCandidate candidate, string exeSha256)
    {
        var decision = new InstalledEffectEvidenceDecision { EvidenceExeSha256 = exeSha256 };
        var exactSample = candidate.DetectionLevel.Equals("KnownExact", StringComparison.OrdinalIgnoreCase) &&
                          candidate.MissingAnchors.Count == 0;
        var variantSample = candidate.DetectionLevel.Equals("KnownVariant", StringComparison.OrdinalIgnoreCase) ||
                            candidate.Confidence.Equals("KnownPatchVariant", StringComparison.OrdinalIgnoreCase);

        var knownSampleCandidate = candidate.Type.StartsWith("KnownPatch", StringComparison.OrdinalIgnoreCase) ||
                                   candidate.PatternKind == InjectedEffectPatternKind.KnownPatch;
        decision.HasCurrentHookBytes = candidate.JumpOutAddress.HasValue &&
                                       (!knownSampleCandidate || candidate.MatchedAnchors.Any(item => item.StartsWith("hook-current:", StringComparison.OrdinalIgnoreCase)));
        decision.HasReachableCodeBody = candidate.CodeCaveEntryAddress.HasValue &&
                                        (!knownSampleCandidate || candidate.MatchedAnchors.Any(item => item.StartsWith("body-current:", StringComparison.OrdinalIgnoreCase)));
        decision.HasNormalizedBodySignature = exactSample ||
                                              candidate.MatchedAnchors.Any(item => item.StartsWith("body-signature:", StringComparison.OrdinalIgnoreCase));
        decision.HasCoreCall = candidate.CheckGroups.Any(group => group.GuardFunctionAddress == EffectPatchByteService.CoreEffectEngineAddress);
        decision.HasValidReturnPath = candidate.ReturnAddress.HasValue ||
                                      candidate.MatchedAnchors.Any(item => item.StartsWith("return-current:", StringComparison.OrdinalIgnoreCase));

        var completeCurrentChain = decision.HasCurrentHookBytes && decision.HasReachableCodeBody &&
                                   decision.HasNormalizedBodySignature && decision.HasCoreCall && decision.HasValidReturnPath;
        if (variantSample)
        {
            decision.IsPresent = completeCurrentChain;
            decision.OwnershipStatus = decision.IsPresent
                ? InstalledEffectOwnershipStatus.LegacyVariant
                : InstalledEffectOwnershipStatus.SampleSimilar;
            decision.Status = decision.IsPresent ? "LegacyVariantPresent" : "SampleSimilar";
            decision.StatusZh = decision.IsPresent ? "遗留变体已存在，未受工具管理" : "发现相似样本，未确认安装";
            if (decision.IsPresent)
            {
                decision.SatisfiedEvidenceZh.AddRange(["当前入口跳转已确认", "当前功能体和核心判定调用已确认", "原流程返回路径已确认"]);
                decision.MissingEvidenceZh.Add("缺少当前项目受管清单；只能链式保留，不能原地改写遗留代码体。");
            }
            else
            {
                decision.MissingEvidenceZh.Add("可变签名或局部样本命中不能证明当前 EXE 已安装完整补丁。");
            }
            return decision;
        }

        if (exactSample)
        {
            decision.HasCurrentHookBytes = candidate.JumpOutAddress.HasValue;
            decision.HasReachableCodeBody = candidate.CodeCaveEntryAddress.HasValue;
            decision.HasNormalizedBodySignature = true;
            decision.HasRequiredComplexFamilyEvidence = candidate.PatchCategory != InjectedEffectPatchCategory.ComplexMultiHookPatch ||
                                                        candidate.MatchedAnchors.Count(item => item.StartsWith("segment-current:", StringComparison.OrdinalIgnoreCase)) >= 3;
            decision.IsPresent = decision.HasCurrentHookBytes && decision.HasReachableCodeBody && decision.HasRequiredComplexFamilyEvidence;
        }
        else
        {
            decision.IsPresent = candidate.PatternKind != InjectedEffectPatternKind.RawJumpCandidate && completeCurrentChain;
        }

        decision.IsToolManaged = decision.IsPresent && decision.HasCurrentProjectManifest;
        decision.OwnershipStatus = decision.IsToolManaged
            ? InstalledEffectOwnershipStatus.Managed
            : decision.IsPresent ? InstalledEffectOwnershipStatus.LegacyExact : InstalledEffectOwnershipStatus.DiagnosticOnly;
        decision.Status = decision.IsToolManaged ? "ManagedInstalled" : decision.IsPresent ? "LegacyExactPresent" : "DiagnosticOnly";
        decision.StatusZh = decision.IsToolManaged ? "工具受管安装" : decision.IsPresent ? "遗留精确实现已存在" : "证据不足，仅供诊断";
        if (decision.HasCurrentHookBytes) decision.SatisfiedEvidenceZh.Add("当前入口字节已确认");
        else decision.MissingEvidenceZh.Add("缺少当前入口跳转证据");
        if (decision.HasReachableCodeBody) decision.SatisfiedEvidenceZh.Add("当前功能体已确认");
        else decision.MissingEvidenceZh.Add("缺少当前功能体证据");
        if (!decision.IsPresent && !decision.HasCoreCall && !exactSample) decision.MissingEvidenceZh.Add("缺少核心判定调用");
        if (!decision.IsPresent && !decision.HasNormalizedBodySignature && !exactSample)
            decision.MissingEvidenceZh.Add("缺少当前功能体的稳定归一化签名");
        return decision;
    }
}
