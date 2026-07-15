using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class EffectConfirmationLedgerService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public EffectConfirmationLedger Build(CczProject project, EffectAnalysisSnapshot? snapshot = null)
    {
        var started = Stopwatch.GetTimestamp();
        snapshot ??= EffectAnalysisCoordinator.Shared.Scan(project);
        var ledger = new EffectConfirmationLedger
        {
            AnalysisFingerprint = snapshot.AnalysisFingerprint,
            FullExeSha256 = snapshot.FullExeSha256,
            ProfileId = snapshot.ProfileAudit.ProfileId,
            ProfileTrustStatus = snapshot.ProfileAudit.TrustStatus,
            BuildIdentity = EffectCapabilityVersion.BuildIdentity,
            CapabilitySchema = EffectCapabilityVersion.SchemaVersion,
            GeneratedAtUtc = DateTime.UtcNow
        };
        foreach (var instance in snapshot.Inventory.Effects)
            ledger.Effects.Add(BuildEntry(project, instance));
        ledger.Performance["BuildMilliseconds"] =
            (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
        var fields = ledger.Effects.Sum(item => item.Fields.Count);
        var writable = ledger.Effects.Sum(item => item.Fields.Count(field => field.Decision.CanApply));
        ledger.SummaryZh = $"确认台账包含 {ledger.Effects.Count} 个逻辑特效、{fields} 个字段，其中 {writable} 个字段当前允许写入。";
        return ledger;
    }

    public EffectConfirmationExportResult Export(CczProject project, EffectAnalysisSnapshot? snapshot = null)
    {
        var ledger = Build(project, snapshot);
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ledger.AnalysisFingerprint)))[..12];
        var root = Path.Combine(project.WorkspaceRoot, "CCZModStudio_Reports", "EffectConfirmation", identity);
        Directory.CreateDirectory(root);
        var jsonPath = Path.Combine(root, "effect-confirmation-ledger.json");
        var markdownPath = Path.Combine(root, "effect-confirmation-ledger.md");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(ledger, JsonOptions), new UTF8Encoding(true));
        File.WriteAllText(markdownPath, BuildMarkdown(ledger), new UTF8Encoding(true));
        return new EffectConfirmationExportResult
        {
            JsonPath = jsonPath,
            MarkdownPath = markdownPath,
            EffectCount = ledger.Effects.Count,
            FieldCount = ledger.Effects.Sum(item => item.Fields.Count),
            SummaryZh = ledger.SummaryZh
        };
    }

    private static EffectConfirmationEntry BuildEntry(CczProject project, LogicalEffectInstance instance)
    {
        var fields = new List<EffectConfirmationField>();
        AddNativeFields(project, instance, CompositeEffectChannel.PersonalJob, instance.PersonalChannel?.EffectId, fields);
        AddNativeFields(project, instance, CompositeEffectChannel.Item,
            instance.ItemChannel?.IsEnabled == true ? instance.ItemChannel.EffectId : null, fields);
        fields.AddRange(instance.Parameters.Select(parameter => new EffectConfirmationField
        {
            FieldId = "parameter:" + parameter.SlotId,
            DisplayNameZh = parameter.DisplayName,
            CurrentValueZh = parameter.Value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "未恢复",
            PhysicalLocationCount = parameter.PhysicalPatchPoints.Count,
            HasExactLocation = parameter.IsConsistent && parameter.PhysicalPatchPoints.Count > 0,
            ContractId = instance.WritableContractId,
            Decision = parameter.WriteDecision ?? new EffectWriteDecision
            {
                BlockerCodes = { "FIELD_DECISION_MISSING" },
                ReasonsZh = { "该参数尚未生成字段级写入决定。" }
            }
        }));
        fields = fields.GroupBy(field => field.FieldId, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToList();
        var decisions = fields.Select(field => field.Decision).Append(instance.WriteDecision ?? new EffectWriteDecision()).ToList();
        var blockers = decisions.SelectMany(item => item.BlockerCodes).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
        var physicalCount = fields.Sum(field => field.PhysicalLocationCount);
        var dynamicRequired = decisions.Any(item => item.BlockerCodes.Contains("DYNAMIC_V3_EVIDENCE_REQUIRED"));
        var dynamicConfirmed = decisions.Any(item => item.Capability == EffectWriteCapability.DynamicBehavior && item.CanApply);
        return new EffectConfirmationEntry
        {
            InstanceId = instance.InstanceId,
            Name = instance.Name,
            SourceKind = instance.SourceKind,
            PatchCategory = instance.PatchCategory,
            PersonalEffectId = instance.PersonalChannel?.EffectId,
            ItemEffectId = instance.ItemChannel?.IsEnabled == true ? instance.ItemChannel.EffectId : null,
            TriggerPhase = instance.TriggerPhase,
            PhysicalEvidenceZh = physicalCount > 0 ? $"已登记 {physicalCount} 个物理补丁位置。" : "尚无唯一物理位置。",
            StructuralEvidenceZh = instance.CoreCalls.Count > 0 || instance.WrapperContractIds.Count > 0 || !string.IsNullOrWhiteSpace(instance.ComplexFamilyContractId)
                ? $"核心调用 {instance.CoreCalls.Count} 个，包装/家族契约 {instance.WrapperContractIds.Count + (string.IsNullOrWhiteSpace(instance.ComplexFamilyContractId) ? 0 : 1)} 个。"
                : "尚未形成可写结构契约。",
            SemanticEvidenceZh = string.IsNullOrWhiteSpace(instance.NaturalLanguageDescription)
                ? "尚未完整解释。" : instance.NaturalLanguageDescription,
            DynamicEvidenceZh = dynamicConfirmed ? "同身份 V3 动态证据已满足。"
                : dynamicRequired ? "缺少同档案、同代码身份、同契约的 V3 证据。" : "该字段类型不要求动态证据。",
            ConclusionZh = fields.Any(field => field.Decision.CanApply) ? "存在已验证可写字段"
                : fields.Any(field => field.Decision.CanPreview) ? "静态预览" : "只读诊断",
            NextActionZh = decisions.Select(item => item.NextAction).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "补齐字段级证据",
            AffectedConsumers = instance.ConsumerFunctionAddresses.Distinct().Count(),
            BlockerCodes = blockers,
            EvidenceReferences = instance.MatchedEvidence.Concat(instance.WrapperContractIds)
                .Append(instance.ComplexFamilyContractId).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().ToList(),
            Fields = fields
        };
    }

    private static void AddNativeFields(
        CczProject project,
        LogicalEffectInstance instance,
        string channel,
        int? effectId,
        ICollection<EffectConfirmationField> fields)
    {
        if (!effectId.HasValue) return;
        try
        {
            var definition = new NativeEffectConfigurationService().Read(project, channel, effectId.Value);
            foreach (var capability in definition.FieldCapabilities)
            {
                fields.Add(new EffectConfirmationField
                {
                    FieldId = channel + ":" + capability.FieldId,
                    DisplayNameZh = capability.DisplayNameZh,
                    CurrentValueZh = capability.CurrentValueZh,
                    PhysicalLocationCount = capability.LocationIds.Count,
                    HasExactLocation = capability.LocationIds.Count > 0,
                    ContractId = instance.WritableContractId,
                    Decision = capability.WriteDecision
                });
            }
        }
        catch (Exception ex)
        {
            fields.Add(new EffectConfirmationField
            {
                FieldId = channel + ":diagnostic",
                DisplayNameZh = "原生配置读取",
                CurrentValueZh = ex.Message,
                Decision = new EffectWriteDecision
                {
                    BlockerCodes = { "NATIVE_CONFIGURATION_READ_FAILED" },
                    ReasonsZh = { ex.Message }
                }
            });
        }
    }

    private static string BuildMarkdown(EffectConfirmationLedger ledger)
    {
        var builder = new StringBuilder()
            .AppendLine("# 特效确认台账报告").AppendLine()
            .AppendLine($"- EXE SHA256：`{ledger.FullExeSha256}`")
            .AppendLine($"- 档案：`{ledger.ProfileId}` / `{ledger.ProfileTrustStatus}`")
            .AppendLine($"- 构建：`{ledger.BuildIdentity}` / `{ledger.CapabilitySchema}`")
            .AppendLine($"- 结论：{ledger.SummaryZh}").AppendLine()
            .AppendLine("| 特效 | 渠道编号 | 来源 | 物理证据 | 结构证据 | 动态证据 | 当前结论 | 下一步 |")
            .AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var item in ledger.Effects)
        {
            var ids = string.Join(" / ", new[]
            {
                item.PersonalEffectId.HasValue ? $"人物 {item.PersonalEffectId:X2}" : null,
                item.ItemEffectId.HasValue ? $"宝物 {item.ItemEffectId:X2}" : null
            }.Where(value => value != null));
            builder.Append('|').Append(Escape(item.Name)).Append('|').Append(Escape(ids)).Append('|')
                .Append(Escape(item.SourceKind)).Append('|').Append(Escape(item.PhysicalEvidenceZh)).Append('|')
                .Append(Escape(item.StructuralEvidenceZh)).Append('|').Append(Escape(item.DynamicEvidenceZh)).Append('|')
                .Append(Escape(item.ConclusionZh)).Append('|').Append(Escape(item.NextActionZh)).AppendLine("|");
        }
        return builder.ToString();
    }

    private static string Escape(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
}
