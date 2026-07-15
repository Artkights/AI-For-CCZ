using System.Text;
using System.Text.Json;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class ExtendedPersonalJobBindingService
{
    public const string ContractId = "personal-job-binding-query-v1";
    public const uint NativeTableAddress = 0x00507800;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ExtendedBindingCapability ReadCapability(CczProject project)
    {
        var sha = EffectPatchByteService.Sha256(project.ResolveGameFile("Ekd5.exe"));
        var evidence = EffectEvidenceBundleService.ReadTrusted(project, sha, ContractId).ToList();
        var hasEvidence = evidence.Any(bundle =>
            RequiredScenarioIds.All(id => bundle.CompletedScenarioIds.Contains(id, StringComparer.OrdinalIgnoreCase)) &&
            RequiredObservationKeys.All(key => bundle.DerivedObservations.Any(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase))));
        return new ExtendedBindingCapability
        {
            EvidenceExeSha256 = sha,
            HasRequiredDynamicEvidence = hasEvidence,
            // The shared fallback query compiler is intentionally unavailable until its ABI contract is proven.
            QueryCompilerAvailable = false,
            StatusZh = hasEvidence ? "动态证据已导入，等待共享查询器编译器" : "等待当前 SHA 动态验证",
            ReasonZh = hasEvidence
                ? "人物、兵种、未命中和特效值场景已有证据，但共享查询器的入口搬迁、寄存器和返回值契约尚未实现。"
                : "尚未证明原生表未命中后的安全接入点，以及单位指针、武将 ID、兵种 ID、特效值和返回值 ABI。"
        };
    }

    public ExtendedBindingPreview Preview(
        CczProject project,
        int effectId,
        IReadOnlyList<EffectPackageBinding> bindings)
    {
        var result = new ExtendedBindingPreview { Capability = ReadCapability(project) };
        var assignments = bindings.Where(item =>
                (item.Kind ?? string.Empty).Equals("job_assignment", StringComparison.OrdinalIgnoreCase) && !item.Remove)
            .ToList();
        if (assignments.Count == 0)
        {
            result.CanApply = true;
            result.SummaryZh = "没有武将/兵种分配需要容量扩展。";
            return result;
        }

        var characters = assignments
            .SelectMany(item => new[] { item.PersonId, item.PersonId2, item.PersonId3 })
            .Where(item => item.HasValue && item.Value is >= 0 and < 1024)
            .Select(item => item!.Value)
            .Distinct()
            .ToList();
        var jobs = assignments.Select(item => item.JobId)
            .Where(item => item.HasValue && item.Value is >= 0 and < 255)
            .Select(item => item!.Value)
            .Distinct()
            .ToList();
        var effectValue = assignments.Select(item => item.EffectValue).LastOrDefault(item => item.HasValue) ?? 0;
        result.NativeBinding = new EffectPackageBinding
        {
            Kind = "job_assignment",
            PersonId = characters.Count > 0 ? characters[0] : null,
            PersonId2 = characters.Count > 1 ? characters[1] : null,
            PersonId3 = characters.Count > 2 ? characters[2] : null,
            JobId = jobs.Count > 0 ? jobs[0] : null,
            EffectValue = effectValue
        };
        result.ExtendedEntries.AddRange(characters.Skip(3).Select(id => new ExtendedPersonalJobBindingEntry
        {
            EffectId = effectId,
            SourceKind = ExtendedBindingSourceKind.Character,
            SourceId = id,
            EffectValue = effectValue
        }));
        result.ExtendedEntries.AddRange(jobs.Skip(1).Select(id => new ExtendedPersonalJobBindingEntry
        {
            EffectId = effectId,
            SourceKind = ExtendedBindingSourceKind.Job,
            SourceId = id,
            EffectValue = effectValue
        }));
        result.RequiresExtension = result.ExtendedEntries.Count > 0;
        result.ProbePlan = CreateProbePlan(project, effectId);
        if (result.RequiresExtension && !result.Capability.CanInstallRuntimeQuery)
        {
            result.WarningsZh.Add($"该配置超出原生三个武将和一个兵种槽位，共有 {result.ExtendedEntries.Count} 项需要扩展绑定表。");
            result.WarningsZh.Add(result.Capability.ReasonZh);
            result.WarningsZh.Add("可先使用其他等价特效号、人物装备专属、套装或物品来源；也可按动态探针计划补齐证据。");
        }
        result.CanApply = !result.RequiresExtension || result.Capability.CanInstallRuntimeQuery;
        result.SummaryZh = result.RequiresExtension
            ? result.CanApply
                ? $"原生槽位和 {result.ExtendedEntries.Count} 项扩展绑定可整体预览。"
                : "检测到容量溢出，但共享扩展查询器尚未达到可写条件。"
            : $"全部绑定可放入原生三个武将和一个兵种槽位。";
        return result;
    }

    public ExtendedBindingProbePlan CreateProbePlan(CczProject project, int effectId)
    {
        if (effectId is < 0 or > 0xFE) throw new ArgumentOutOfRangeException(nameof(effectId));
        var sha = EffectPatchByteService.Sha256(project.ResolveGameFile("Ekd5.exe"));
        var row = checked(NativeTableAddress + (uint)(effectId * 8));
        return new ExtendedBindingProbePlan
        {
            PlanId = $"extended-binding-{effectId:X2}-{sha[..Math.Min(8, sha.Length)]}",
            ExeSha256 = sha,
            ReadWatchAddresses = [row, checked(row + 2), checked(row + 4), checked(row + 6), checked(row + 7)],
            BreakpointAddresses = [0x0041301E, 0x00413009],
            RequiredCaptureFieldsZh =
            [
                "每次读取的指令地址、读取宽度和实际表地址",
                "EAX、EBX、ECX、EDX、ESI、EDI、EBP、ESP 与 EFLAGS",
                "断点前后至少 64 字节反汇编和完整调用栈",
                "当前单位指针、武将 ID、兵种 ID、特效号、返回拥有状态和特效值",
                "当前 Ekd5.exe SHA256 与测试场景说明"
            ],
            Scenarios =
            [
                Scenario("character-hit", "武将槽命中", "确认匹配第一个、第二个和第三个武将槽时的输入与返回值。"),
                Scenario("job-hit", "兵种槽命中", "确认武将未命中而兵种命中时的分支、返回值和特效值。"),
                Scenario("native-miss", "原生表未命中", "确认所有原生槽未命中后的唯一安全回退路径和仍然有效的单位上下文。"),
                Scenario("effect-value-read", "特效值读取", "确认拥有状态与第八字节特效值的传递方式、宽度和有无符号。")
            ],
            Batches = BuildGameDebugBatches(row),
            SummaryZh = "只读动态探针计划：不修改 EXE。必须在当前 SHA 的临时副本中完成四类场景，才能设计共享扩展查询器。"
        };
    }

    public ExtendedBindingEvidenceImportResult ImportEvidence(CczProject project, ExtendedBindingEvidence evidence)
    {
        var result = new ExtendedBindingEvidenceImportResult();
        var currentSha = EffectPatchByteService.Sha256(project.ResolveGameFile("Ekd5.exe"));
        if (!evidence.ContractId.Equals(ContractId, StringComparison.OrdinalIgnoreCase))
            result.WarningsZh.Add("证据契约 ID 不属于扩展人物/兵种绑定查询器。");
        if (!evidence.ExeSha256.Equals(currentSha, StringComparison.OrdinalIgnoreCase))
            result.WarningsZh.Add("证据 SHA 与当前 EXE 不一致，不能跨版本提升扩展绑定能力。");
        if (!evidence.SourceTool.Contains("GameDebug", StringComparison.OrdinalIgnoreCase) &&
            !evidence.SourceTool.Contains("x32dbg", StringComparison.OrdinalIgnoreCase))
            result.WarningsZh.Add("证据来源必须是 GameDebug MCP 或 x32dbg。");
        foreach (var scenario in RequiredScenarioIds.Where(id => !evidence.CompletedScenarioIds.Contains(id, StringComparer.OrdinalIgnoreCase)))
            result.WarningsZh.Add("缺少动态场景：" + scenario + "。");
        foreach (var key in RequiredObservationKeys.Where(key => !evidence.Observations.ContainsKey(key)))
            result.WarningsZh.Add("缺少动态观测：" + key + "。");
        if (evidence.CallStacksZh.Count < RequiredScenarioIds.Length)
            result.WarningsZh.Add("每个必需场景都必须保存可复查调用栈。");
        if (result.WarningsZh.Count > 0)
        {
            result.Capability = ReadCapability(project);
            result.SummaryZh = "扩展绑定证据未达到当前 SHA 的接受条件。";
            return result;
        }

        evidence.EvidenceId = string.IsNullOrWhiteSpace(evidence.EvidenceId)
            ? "extended-binding-" + DateTime.Now.ToString("yyyyMMddHHmmssfff")
            : Path.GetFileNameWithoutExtension(evidence.EvidenceId);
        var root = EvidenceRoot(project, currentSha);
        Directory.CreateDirectory(root);
        result.SavedPath = Path.Combine(root, evidence.EvidenceId + ".json");
        File.WriteAllText(result.SavedPath, JsonSerializer.Serialize(evidence, JsonOptions), Encoding.UTF8);
        result.WarningsZh.Add("旧结构化证据只保存为诊断材料；请使用 GameDebug 签发的可信证据包提升扩展查询契约。");
        result.Accepted = true;
        result.Capability = ReadCapability(project);
        result.SummaryZh = "扩展绑定动态证据已保存；共享查询器编译器完成前仍保持只读。";
        return result;
    }

    public EffectEvidenceBundleImportResult ImportEvidenceBundle(CczProject project, EffectEvidenceBundleV1 bundle)
    {
        if (!bundle.ContractId.Equals(ContractId, StringComparison.OrdinalIgnoreCase))
            return new EffectEvidenceBundleImportResult { WarningsZh = ["证据包不属于扩展人物/兵种绑定契约。"], SummaryZh = "证据包导入失败。" };
        var warnings = new List<string>();
        foreach (var scenario in RequiredScenarioIds.Where(id => !bundle.CompletedScenarioIds.Contains(id, StringComparer.OrdinalIgnoreCase)))
            warnings.Add("缺少动态场景：" + scenario + "。");
        foreach (var key in RequiredObservationKeys.Where(key => !bundle.DerivedObservations.Any(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase))))
            warnings.Add("缺少可从原始采集复算的观测：" + key + "。");
        if (warnings.Count > 0)
            return new EffectEvidenceBundleImportResult { WarningsZh = warnings, SummaryZh = "扩展绑定场景或 ABI 观测不完整，未保存为可信证据。" };
        var result = EffectEvidenceBundleService.VerifyAndStore(project, bundle);
        if (!result.Accepted) return result;
        result.ContractPromoted = true;
        result.SummaryZh = "扩展绑定可信证据已完整验证；可进入共享查询器 ABI 编译。";
        return result;
    }

    private static ExtendedBindingProbeScenario Scenario(string id, string name, string observation)
        => new() { ScenarioId = id, DisplayNameZh = name, RequiredObservationZh = observation };

    private static List<EffectProbeBatch> BuildGameDebugBatches(uint row)
    {
        var scenarios = new[]
        {
            ("character-hit", "武将槽命中"),
            ("job-hit", "兵种槽命中"),
            ("native-miss", "原生表未命中"),
            ("effect-value-read", "特效值读取")
        };
        return scenarios.Select((scenario, index) => new EffectProbeBatch
        {
            BatchIndex = index + 1,
            ScenarioId = scenario.Item1,
            DisplayNameZh = scenario.Item2,
            X32dbgCommands =
            [
                $"bphws {row:X8},r,4",
                $"bphws {row + 4:X8},r,4",
                "bp 0041301E",
                "bp 00413009"
            ]
        }).ToList();
    }

    private static readonly string[] RequiredScenarioIds = ["character-hit", "job-hit", "native-miss", "effect-value-read"];
    private static readonly string[] RequiredObservationKeys = ["unit-pointer", "person-id", "job-id", "effect-id", "owned-result", "effect-value", "native-miss-path"];

    private static bool IsCompleteEvidence(ExtendedBindingEvidence evidence)
        => RequiredScenarioIds.All(id => evidence.CompletedScenarioIds.Contains(id, StringComparer.OrdinalIgnoreCase)) &&
           RequiredObservationKeys.All(evidence.Observations.ContainsKey) && evidence.CallStacksZh.Count >= RequiredScenarioIds.Length;

    private static IEnumerable<ExtendedBindingEvidence> ReadAcceptedEvidence(CczProject project, string sha)
    {
        var root = EvidenceRoot(project, sha);
        if (!Directory.Exists(root)) yield break;
        foreach (var path in Directory.GetFiles(root, "*.json"))
        {
            ExtendedBindingEvidence? evidence = null;
            try { evidence = JsonSerializer.Deserialize<ExtendedBindingEvidence>(File.ReadAllText(path, Encoding.UTF8), JsonOptions); } catch { }
            if (evidence != null && evidence.ExeSha256.Equals(sha, StringComparison.OrdinalIgnoreCase)) yield return evidence;
        }
    }

    private static string EvidenceRoot(CczProject project, string sha)
        => Path.Combine(project.GameRoot, "CCZModStudio_Notes", "EffectContractEvidence", sha, ContractId);
}
