using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// 根据当前项目状态生成中文制作向导。该服务只读取项目状态，不修改游戏文件。
/// </summary>
public sealed class ProjectWorkflowGuideService
{
    private readonly ProjectEvidenceService _projectEvidenceService = new();

    public IReadOnlyList<WorkflowDashboardItem> BuildDashboard(
        CczProject? project,
        int tableDefinitionCount,
        IReadOnlyList<ProjectAuditItem> auditItems,
        IReadOnlyList<ResourceDiagnosticItem> resourceDiagnostics,
        IReadOnlyList<ProjectDiffItem> diffItems,
        IReadOnlyList<BackupHistoryItem> backupItems,
        IReadOnlyList<CreatorNote> creatorNotes,
        IReadOnlyList<ScenarioMapLinkInfo>? scenarioMapLinks = null,
        ScenarioStructureProbeResult? scenarioStructure = null,
        IReadOnlyList<EexArchiveInfo>? eexArchives = null,
        IReadOnlyList<LsResourceInfo>? lsResources = null,
        HexzmapProbeResult? hexzmapProbe = null)
    {
        if (project == null)
        {
            return new[]
            {
                new WorkflowDashboardItem
                {
                    Area = "项目状态",
                    Level = "风险",
                    Value = "尚未加载",
                    Summary = "还没有可分析的曹操传 MOD 项目。",
                    Suggestion = "先点击“打开项目”，选择包含 Ekd5.exe、Data.e5、Imsg.e5、Star.e5 的目录。",
                    RelatedPage = "制作向导",
                    Evidence = "未加载项目"
                }
            };
        }

        var isTestCopy = project.IsTestCopy;
        var auditErrors = auditItems.Count(x => x.Severity == "Error");
        var auditWarnings = auditItems.Count(x => x.Severity == "Warn");
        var resourceErrors = resourceDiagnostics.Count(x => x.Severity == "Error");
        var resourceWarnings = resourceDiagnostics.Count(x => x.Severity == "Warn");
        var modified = diffItems.Count(x => x.Status == "已修改");
        var added = diffItems.Count(x => x.Status == "新增");
        var missing = diffItems.Count(x => x.Status == "缺失");
        var todoNotes = creatorNotes.Count(IsTodoNote);
        var riskNotes = creatorNotes.Count(IsRiskNote);
        scenarioMapLinks ??= Array.Empty<ScenarioMapLinkInfo>();
        eexArchives ??= Array.Empty<EexArchiveInfo>();
        lsResources ??= Array.Empty<LsResourceInfo>();
        var mapComplete = scenarioMapLinks.Count(x => x.Status == "完整候选");
        var mapIncomplete = scenarioMapLinks.Count(x => x.Status.Contains("缺", StringComparison.Ordinal));
        var scenarioRiskService = new ScenarioStructureFilterService();
        var highRiskSvCommands = scenarioStructure?.Rows.Count(scenarioRiskService.IsHighRisk) ?? 0;
        var eexInvalid = eexArchives.Count(x => !x.MagicValid);
        var lsInvalid = lsResources.Count(x => !x.MagicValid);
        var hexzmapBlocks = hexzmapProbe?.Blocks.Count ?? 0;
        var hexzmapUnknown = hexzmapProbe?.Blocks.Count(x => !string.IsNullOrWhiteSpace(x.UnknownTerrainIds)) ?? 0;
        var latestBackup = backupItems
            .Where(x => x.CreatedAt != DateTime.MinValue)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefault();
        var projectEvidence = _projectEvidenceService.Scan(project);
        var latestReport = projectEvidence
            .OrderByDescending(item => item.LastWriteTime)
            .FirstOrDefault();
        var importantEvidence = projectEvidence
            .OrderByDescending(item => EvidenceKindRank(item.Kind))
            .ThenByDescending(item => item.LastWriteTime)
            .FirstOrDefault();

        return new[]
        {
            new WorkflowDashboardItem
            {
                Area = "安全模式",
                Level = isTestCopy ? "正常" : "提醒",
                Value = isTestCopy ? "测试副本" : "原始只读",
                Summary = isTestCopy
                    ? "当前项目带有测试副本标记，可在自动备份和报告保护下使用已确认写入功能。"
                    : "当前打开的是原始目录，工具会保持只读保护。",
                Suggestion = isTestCopy ? "可以继续编辑；每次写入后请查看差异和备份。" : "请先创建测试副本，再进行任何实际修改。",
                RelatedPage = "顶部工具栏 / 测试副本差异/发布",
                Evidence = project.GameRoot
            },
            new WorkflowDashboardItem
            {
                Area = "数据表与中文注释",
                Level = tableDefinitionCount > 0 ? "正常" : "风险",
                Value = $"{tableDefinitionCount} 个表定义",
                Summary = tableDefinitionCount > 0
                    ? "HexTable.xml 已读取，数据表页可显示字段中文注释、风险说明、样例值和跨表引用。"
                    : "尚未读取到表定义，数据表解析不可用。",
                Suggestion = tableDefinitionCount > 0 ? "优先阅读字段说明，再修改人物、物品、策略等核心表。" : "检查 HexTable.xml 路径或重新打开项目。",
                RelatedPage = "数据表编辑",
                Evidence = project.HexTableXmlPath
            },
            new WorkflowDashboardItem
            {
                Area = "项目体检",
                Level = auditErrors > 0 ? "风险" : auditWarnings > 0 ? "提醒" : auditItems.Count > 0 ? "正常" : "提醒",
                Value = auditItems.Count > 0 ? $"错误 {auditErrors} / 警告 {auditWarnings}" : "未运行",
                Summary = auditItems.Count > 0
                    ? "项目体检已生成，可用于确认核心文件、偏移护栏和只读/测试副本边界。"
                    : "尚未运行项目体检。",
                Suggestion = auditErrors > 0 ? "先处理红色错误，再继续制作。" : auditItems.Count > 0 ? "保留体检结果作为发布前证据。" : "点击“项目体检”。",
                RelatedPage = "项目体检/发布检查",
                Evidence = auditItems.Count > 0 ? string.Join("；", auditItems.Take(3).Select(x => $"{x.Severity}:{x.Category}/{x.Name}")) : "无体检结果"
            },
            new WorkflowDashboardItem
            {
                Area = "资源诊断",
                Level = resourceErrors > 0 ? "风险" : resourceWarnings > 0 ? "提醒" : resourceDiagnostics.Count > 0 ? "正常" : "提醒",
                Value = resourceDiagnostics.Count > 0 ? $"错误 {resourceErrors} / 警告 {resourceWarnings}" : "未运行",
                Summary = resourceDiagnostics.Count > 0
                    ? "资源诊断已汇总地图、剧本、图片、音频、R/S 等资源线索。"
                    : "尚未运行资源诊断。",
                Suggestion = resourceDiagnostics.Count > 0 ? "在资源诊断页优先查看缺失、编号缺口、格式线索异常，并为确认结论添加备注。" : "点击“资源诊断”，检查替换资源前的风险。",
                RelatedPage = "资源诊断 / 游戏资源索引",
                Evidence = resourceDiagnostics.Count > 0 ? string.Join("；", resourceDiagnostics.Take(3).Select(x => $"{x.Severity}:{x.Category}/{x.Rule}")) : "无资源诊断结果"
            },
            new WorkflowDashboardItem
            {
                Area = "创作者备注",
                Level = riskNotes > 0 || todoNotes > 0 ? "提醒" : creatorNotes.Count > 0 ? "正常" : "提醒",
                Value = $"{creatorNotes.Count} 条 / 待办 {todoNotes} / 风险 {riskNotes}",
                Summary = "备注是项目侧知识库，不写入游戏文件，可记录剧情设计、资源用途、风险和实机验证。",
                Suggestion = creatorNotes.Count > 0 ? "继续用“抓取当前选择”把关键修改点与备注绑定。" : "开始为关键数据、资源、剧本和差异建立备注。",
                RelatedPage = "创作者备注",
                Evidence = creatorNotes.Count > 0 ? string.Join("；", creatorNotes.Take(3).Select(x => $"{x.Scope}:{x.Title}")) : "暂无备注"
            },
            new WorkflowDashboardItem
            {
                Area = "差异与备份",
                Level = !isTestCopy ? "提醒" : diffItems.Count > 0 && backupItems.Count > 0 ? "正常" : "提醒",
                Value = $"差异 {diffItems.Count} / 备份 {backupItems.Count}",
                Summary = diffItems.Count > 0
                    ? $"当前差异：已修改 {modified}，新增 {added}，缺失 {missing}。"
                    : "尚未生成测试副本差异。",
                Suggestion = isTestCopy ? "修改后生成差异，并在备份历史中确认可回滚点。" : "切换到测试副本后再分析差异和备份。",
                RelatedPage = "测试副本差异/发布 / 备份历史/回滚",
                Evidence = latestBackup != null ? $"最近备份：{latestBackup.CreatedAtText} {latestBackup.TargetRelativePath}" : "暂无已加载备份"
            },
            new WorkflowDashboardItem
            {
                Area = "关卡地图联动",
                Level = scenarioMapLinks.Count == 0 ? "提醒" : mapIncomplete > 0 ? "提醒" : "正常",
                Value = scenarioMapLinks.Count == 0 ? "未生成" : $"完整 {mapComplete} / 不完整 {mapIncomplete}",
                Summary = scenarioMapLinks.Count == 0
                    ? "尚未生成 SV -> Map -> Hexzmap 的关卡地图联动候选。"
                    : "已汇总关卡脚本、战场底图和 Hexzmap 地形候选块的成套状态。",
                Suggestion = scenarioMapLinks.Count == 0 ? "点击“生成关卡地图联动”，检查关卡是否缺地图图或地形块。" : "优先处理不完整行；新增关卡或替换地图后建议重新生成联动。",
                RelatedPage = "关卡地图联动",
                Evidence = scenarioMapLinks.Count == 0
                    ? "无联动结果"
                    : string.Join("；", scenarioMapLinks.Where(x => x.Status.Contains("缺", StringComparison.Ordinal)).DefaultIfEmpty(scenarioMapLinks[0]).Take(3).Select(x => $"{x.ScenarioFileName}->{x.MapId}:{x.Status}"))
            },
            new WorkflowDashboardItem
            {
                Area = "SV高风险命令",
                Level = scenarioStructure == null ? "提醒" : highRiskSvCommands > 0 ? "提醒" : "正常",
                Value = scenarioStructure == null ? "未构建" : $"高风险 {highRiskSvCommands} / 命令 {scenarioStructure.CommandCandidateCount}",
                Summary = scenarioStructure == null
                    ? "尚未生成当前 SV 的结构草图，无法汇总高风险命令。"
                    : $"当前结构草图：{scenarioStructure.FileName}，Scene {scenarioStructure.SceneCount}，Section {scenarioStructure.SectionCount}。",
                Suggestion = scenarioStructure == null ? "在 SV 剧本页选择关卡并生成结构草图；优先查看“高风险/需核对”。" : "优先为高风险命令添加备注，修改剧情流程前必须和旧编辑器/实机核对。",
                RelatedPage = "R/S eex高级探针",
                Evidence = scenarioStructure == null
                    ? "无结构草图"
                    : string.Join("；", scenarioStructure.Rows.Where(scenarioRiskService.IsHighRisk).Take(3).Select(row => $"{row.OffsetHex}:{row.CommandName}"))
            },
            new WorkflowDashboardItem
            {
                Area = "EEX/Ls/Hexzmap探针",
                Level = eexInvalid > 0 || lsInvalid > 0 || hexzmapUnknown > 0 ? "提醒" : (eexArchives.Count + lsResources.Count + hexzmapBlocks) > 0 ? "正常" : "提醒",
                Value = $"EEX {eexArchives.Count} / Ls {lsResources.Count} / 地形块 {hexzmapBlocks}",
                Summary = "汇总 R/S/Map EEX、Ls/E5 封装资源和 Hexzmap 地形探针的只读研究状态。",
                Suggestion = (eexArchives.Count + lsResources.Count + hexzmapBlocks) == 0
                    ? "按需读取 EEX、Ls/E5 或 Hexzmap 探针；未确认内部写回规则前保持只读。"
                    : "优先查看无效 Magic、未知地形 ID 或大体量资源，为可疑区段添加备注。",
                RelatedPage = "EEX资源探针 / Ls/E5地图资源探针 / Hexzmap地形探针",
                Evidence = $"EEX无效 {eexInvalid}；Ls无效 {lsInvalid}；未知地形块 {hexzmapUnknown}"
            },
            new WorkflowDashboardItem
            {
                Area = "最近报告/发布证据",
                Level = projectEvidence.Count > 0 ? "正常" : "提醒",
                Value = projectEvidence.Count > 0 ? $"{projectEvidence.Count} 项 / 最新 {latestReport!.LastWriteTime:yyyy-MM-dd HH:mm}" : "未发现",
                Summary = projectEvidence.Count > 0 ? $"最近证据：{latestReport!.Kind} / {latestReport.FileName}" : "尚未发现发布前综合报告、差异/体检报告、SV 命令清单或预览 PNG。",
                Suggestion = projectEvidence.Count > 0 ? "发布前打开重点证据复查安全边界、差异清单、地图联动和 SV 命令核对结果。" : "生成综合报告、关卡地图联动报告、SV 命令引用清单或预览 PNG，留下可追溯证据。",
                RelatedPage = "测试副本差异/发布 / 项目体检/发布检查",
                Evidence = importantEvidence?.FullPath ?? "无报告文件"
            }
        };
    }

    public IReadOnlyList<WorkflowGuideStep> BuildSteps(
        CczProject? project,
        int tableDefinitionCount,
        int auditItemCount,
        int diffItemCount,
        int backupItemCount,
        int creatorNoteCount)
    {
        var hasProject = project != null && Directory.Exists(project.GameRoot);
        var hasHexTable = project != null && File.Exists(project.HexTableXmlPath);
        var hasCoreFiles = project?.GetFileStatuses()
            .Where(x => x.Kind == "核心")
            .All(x => x.Exists) == true;
        var isTestCopy = project?.IsTestCopy == true;

        return new[]
        {
            new WorkflowGuideStep
            {
                StepNo = 1,
                Stage = "准备",
                Title = "打开曹操传 MOD 项目",
                Status = hasProject && hasHexTable && hasCoreFiles ? "已就绪" : "需要处理",
                RecommendedAction = hasProject
                    ? "确认左侧核心文件、HexTable.xml 和项目路径；如缺文件，请重新选择正确项目目录。"
                    : "点击“打开项目目录”，选择包含 Ekd5.exe、Data.e5、Imsg.e5、Star.e5 的游戏目录。",
                RelatedPage = "左侧项目文件检查",
                WhyItMatters = "只有先确认版本和核心文件，后续表格、剧本、资源解析才有可靠偏移依据。",
                SafetyNote = "打开项目本身不会修改任何游戏文件。"
            },
            new WorkflowGuideStep
            {
                StepNo = 2,
                Stage = "体检",
                Title = "运行项目体检和发布检查",
                Status = auditItemCount > 0 ? "已生成体检结果" : hasProject ? "建议执行" : "等待项目",
                RecommendedAction = "点击“项目体检”，优先处理红色错误和黄色警告；体检报告可留作发布前证据。",
                RelatedPage = "项目体检/发布检查",
                WhyItMatters = "体检会检查 6.5 核心文件尺寸、只读/测试副本边界、偏移护栏和常见风险。",
                SafetyNote = "项目体检为只读分析，不写入游戏文件。"
            },
            new WorkflowGuideStep
            {
                StepNo = 3,
                Stage = "安全",
                Title = "创建并切换到测试副本",
                Status = isTestCopy ? "已在测试副本" : hasProject ? "强烈建议" : "等待项目",
                RecommendedAction = isTestCopy
                    ? "当前目录带有 _CCZModStudio_TestCopy.txt 标记，可以进行受保护写入。"
                    : "点击“创建测试副本”，并在提示中切换到副本后再编辑。",
                RelatedPage = "顶部工具栏",
                WhyItMatters = "所有可写功能都应在测试副本里操作，避免直接破坏原始游戏目录。",
                SafetyNote = "原始目录保持只读；测试副本写入前自动备份并生成报告。"
            },
            new WorkflowGuideStep
            {
                StepNo = 4,
                Stage = "理解",
                Title = "读取数据表、字段注释和跨表解释",
                Status = tableDefinitionCount > 0 ? "可使用" : hasHexTable ? "待读取" : "等待 HexTable",
                RecommendedAction = "在左侧选择 6.5 数据表，查看中文字段说明、样例值、风险字段、跨表引用和可见列 CSV 导出。",
                RelatedPage = "数据表编辑",
                WhyItMatters = "表格是人物、物品、策略、兵种等 MOD 内容的基础；中文注释能降低误改概率。",
                SafetyNote = "在原始目录下表格只读；只有测试副本且表定义允许时才可保存。"
            },
            new WorkflowGuideStep
            {
                StepNo = 5,
                Stage = "制作",
                Title = "安全编辑表格、文本、形象和资源",
                Status = isTestCopy ? "可编辑" : hasProject ? "只读保护中" : "等待项目",
                RecommendedAction = "在测试副本中修改表格、SV 短文本、人物 R/S、资源整文件替换；写入后查看备份和结构化报告。",
                RelatedPage = "数据表编辑 / R/S eex高级探针 / 人物R/S形象 / 游戏资源索引",
                WhyItMatters = "创作者常用改动需要可复读、可回滚、可发布，不能只靠手工覆盖文件。",
                SafetyNote = "EEX、Ls/E5 内部重封包、完整 SV 命令树、Hexzmap 写回仍保持只读研究。"
            },
            new WorkflowGuideStep
            {
                StepNo = 6,
                Stage = "注释",
                Title = "记录创作者备注和制作证据",
                Status = creatorNoteCount > 0 ? $"已有 {creatorNoteCount} 条备注" : hasProject ? "建议补充" : "等待项目",
                RecommendedAction = "点击“抓取当前选择”，为数据表单元格、资源、SV、地图、差异或备份记录用途、风险和实机验证。",
                RelatedPage = "创作者备注",
                WhyItMatters = "备注不会写入游戏文件，但能沉淀剧情设计、资源用途、待办和验证结论，方便长期维护。",
                SafetyNote = "备注保存到 CCZModStudio_Notes，发布副本默认不包含。"
            },
            new WorkflowGuideStep
            {
                StepNo = 7,
                Stage = "复查",
                Title = "查看差异、备份历史和回滚点",
                Status = diffItemCount > 0
                    ? $"已有 {diffItemCount} 项差异"
                    : backupItemCount > 0
                        ? $"已有 {backupItemCount} 条备份"
                        : isTestCopy ? "建议执行" : "等待测试副本",
                RecommendedAction = "修改后生成测试副本差异；选中差异可筛出相关备份，必要时从备份历史还原。",
                RelatedPage = "测试副本差异/发布 / 备份历史/回滚",
                WhyItMatters = "发布前必须知道改了哪些文件，并确保每次写入都有可追溯备份。",
                SafetyNote = "差异分析和备份浏览为只读；还原备份也会再次生成保护性备份。"
            },
            new WorkflowGuideStep
            {
                StepNo = 8,
                Stage = "发布",
                Title = "生成发布副本和综合报告",
                Status = isTestCopy && diffItemCount > 0 ? "可发布前检查" : isTestCopy ? "等待差异" : "等待测试副本",
                RecommendedAction = "确认差异和备份后，生成发布前综合报告；需要交付时再生成干净发布副本。",
                RelatedPage = "测试副本差异/发布",
                WhyItMatters = "发布包应排除测试标记、备份、报告、导出和临时目录，降低误交付风险。",
                SafetyNote = "发布副本从测试副本生成，不会覆盖原始目录。"
            }
        };
    }

    public string BuildSummary(CczProject? project, IReadOnlyList<WorkflowGuideStep> steps, IReadOnlyList<WorkflowDashboardItem>? dashboardItems = null)
    {
        if (project == null)
        {
            return "制作向导：尚未加载项目。请先打开曹操传 MOD 项目目录。";
        }

        var ready = steps.Count(x => x.Status.Contains("已", StringComparison.Ordinal) || x.Status.Contains("可", StringComparison.Ordinal));
        var risks = dashboardItems?.Count(x => x.Level == "风险") ?? 0;
        var reminders = dashboardItems?.Count(x => x.Level == "提醒") ?? 0;
        var risky = project.IsTestCopy
            ? "当前是测试副本，可在备份和报告保护下进行已确认格式的写入。"
            : "当前是原始目录，只读保护中；请先创建测试副本再进行编辑。";
        return
            $"制作向导：{project.Name}\r\n" +
            $"项目路径：{project.GameRoot}\r\n" +
            $"流程进度：{ready}/{steps.Count} 个步骤已就绪或可执行。\r\n" +
            (dashboardItems == null ? string.Empty : $"工作台提示：风险 {risks} 项，提醒 {reminders} 项。\r\n") +
            $"安全边界：{risky}\r\n" +
            "建议顺序：项目体检 -> 测试副本 -> 读取/注释 -> 安全编辑 -> 差异/备份 -> 综合报告/发布副本。";
    }

    public string BuildActionPlan(CczProject? project, IReadOnlyList<WorkflowDashboardItem> dashboardItems, int maxItems = 5)
    {
        if (project == null)
        {
            return "优先行动清单：\r\n1. 打开曹操传 MOD 项目目录，先确认 Ekd5.exe、Data.e5、Imsg.e5、Star.e5 和 HexTable.xml。\r\n安全提示：未加载项目前不会写入任何游戏文件。";
        }

        maxItems = Math.Clamp(maxItems, 1, 10);
        var priorities = dashboardItems
            .Select((item, index) => new { Item = item, Index = index, Rank = GetDashboardPriority(item.Level) })
            .Where(x => x.Rank < 2)
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Index)
            .Take(maxItems)
            .ToList();

        var lines = new List<string>
        {
            "优先行动清单（按风险/提醒排序）："
        };

        if (priorities.Count == 0)
        {
            lines.Add("1. 当前工作台没有风险或提醒项。发布前仍建议重新运行项目体检、资源诊断、差异分析和综合报告。");
        }
        else
        {
            for (var i = 0; i < priorities.Count; i++)
            {
                var item = priorities[i].Item;
                lines.Add($"{i + 1}. 【{item.Level}】{item.Area}：{item.Value}。建议：{item.Suggestion}");
            }
        }

        lines.Add(project.IsTestCopy
            ? "安全提示：当前是测试副本；写入前仍会备份，写入后应复读验证并查看差异。"
            : "安全提示：当前是原始目录，只读保护中；请先创建测试副本再进行任何实际修改。");
        lines.Add("注释提示：处理风险项时建议使用“创作者备注”记录修改意图、证据来源、实机验证结果和回滚点。");
        return string.Join("\r\n", lines);
    }

    public IReadOnlyList<WorkflowActionItem> BuildActionItems(
        CczProject? project,
        IReadOnlyList<WorkflowDashboardItem> dashboardItems,
        IReadOnlyList<CreatorNote>? creatorNotes = null,
        int maxItems = 6)
    {
        creatorNotes ??= Array.Empty<CreatorNote>();
        if (project == null)
        {
            return new[]
            {
                new WorkflowActionItem
                {
                    PriorityNo = 1,
                    Level = "风险",
                    TargetArea = "项目状态",
                    Action = "打开曹操传 MOD 项目目录，确认核心文件和 HexTable.xml。",
                    ExpectedResult = "工作台能够读取项目、表定义和安全边界。",
                    SafetyNote = "未加载项目时不会写入任何游戏文件。",
                    NoteHint = "备注提示：加载项目后可创建项目侧备注。"
                }
            };
        }

        maxItems = Math.Clamp(maxItems, 1, 10);
        var candidates = dashboardItems
            .Select((item, index) => new { Item = item, Index = index, Rank = GetDashboardPriority(item.Level) })
            .Where(x => x.Rank < 2)
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Index)
            .Take(maxItems)
            .ToList();

        if (candidates.Count == 0)
        {
            return new[]
            {
                new WorkflowActionItem
                {
                    PriorityNo = 1,
                    Level = "正常",
                    TargetArea = "最近报告/发布证据",
                    Action = "重新运行项目体检、资源诊断、差异分析和发布前综合报告。",
                    ExpectedResult = "形成发布前证据链，确认当前版本没有新增风险。",
                    SafetyNote = project.IsTestCopy ? "发布副本从测试副本生成，不覆盖原始目录。" : "当前仍是原始目录，只读保护中。",
                    NoteHint = BuildActionNoteHint(creatorNotes, "最近报告/发布证据")
                }
            };
        }

        return candidates
            .Select((x, priority) => new WorkflowActionItem
            {
                PriorityNo = priority + 1,
                Level = x.Item.Level,
                TargetArea = x.Item.Area,
                Action = x.Item.Suggestion,
                ExpectedResult = BuildActionExpectedResult(x.Item),
                SafetyNote = BuildActionSafetyNote(project, x.Item),
                NoteHint = BuildActionNoteHint(creatorNotes, x.Item.Area)
            })
            .ToArray();
    }

    private static bool IsTodoNote(CreatorNote note)
        => ContainsAny(note.Tags, "待办", "TODO", "todo") ||
           ContainsAny(note.Title, "待办", "TODO", "todo") ||
           ContainsAny(note.Content, "待办", "TODO", "todo");

    private static bool IsRiskNote(CreatorNote note)
        => ContainsAny(note.Tags, "风险", "待实测", "危险") ||
           ContainsAny(note.Title, "风险", "待实测", "危险") ||
           ContainsAny(note.Content, "风险", "待实测", "危险");

    private static bool ContainsAny(string value, params string[] keywords)
        => keywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static int GetDashboardPriority(string level)
        => level switch
        {
            "风险" => 0,
            "提醒" => 1,
            _ => 2
        };

    private static string BuildActionExpectedResult(WorkflowDashboardItem item)
        => item.Area switch
        {
            "安全模式" => "切换到受保护的测试副本，后续写入都有备份、报告和回滚点。",
            "项目体检" => "定位第一条错误/警告，确认核心文件、偏移护栏和安全边界。",
            "资源诊断" => "定位缺失、编号缺口、格式线索异常或资源引用风险。",
            "创作者备注" => "打开待办/风险备注，补充修改意图、证据和实机验证状态。",
            "差异与备份" => "看到测试副本相对原始目录的改动，并联动到可回滚备份。",
            "关卡地图联动" => "定位缺地图图、缺 Hexzmap 地形块或需要核对的关卡联动。",
            "SV高风险命令" => "定位影响剧情分支、变量、奖励、人物状态或地图资源的高风险命令。",
            "EEX/Ls/Hexzmap探针" => "定位未知地形、无效 Magic 或需要继续研究的资源封包。",
            "最近报告/发布证据" => "打开或生成发布前综合报告，留下可追溯证据。",
            _ => $"处理“{item.Area}”后刷新工作台，确认当前值从“{item.Value}”变为更安全的状态。"
        };

    private static string BuildActionSafetyNote(CczProject project, WorkflowDashboardItem item)
    {
        var prefix = project.IsTestCopy
            ? "当前是测试副本；"
            : "当前是原始目录，只读保护中；";
        var suffix = item.Area switch
        {
            "EEX/Ls/Hexzmap探针" => "该类格式仍保持只读研究，不开放内部重封包写入。",
            "SV高风险命令" => "完整 SV 命令树写回未开放；修改前请用备注记录证据并实机核对。",
            "关卡地图联动" => "联动页用于核对资源成套状态，替换资源仍需走测试副本备份流程。",
            "安全模式" => project.IsTestCopy ? "可继续编辑，但每次写入后要复读验证和查看差异。" : "请先创建测试副本再编辑。",
            _ => project.IsTestCopy ? "处理后建议刷新差异、备份历史和综合报告。" : "请先创建测试副本再做实际写入。"
        };
        return prefix + suffix;
    }

    public static string BuildWorkflowActionTargetKey(string targetArea)
        => $"WorkflowAction#Area={NormalizeActionTargetArea(targetArea)}";

    private static string BuildActionNoteHint(IReadOnlyList<CreatorNote> notes, string targetArea)
    {
        var normalized = NormalizeActionTargetArea(targetArea);
        var key = BuildWorkflowActionTargetKey(targetArea);
        var count = notes.Count(note =>
            note.Scope.Equals("工作台行动", StringComparison.OrdinalIgnoreCase) &&
            note.TargetKey.Equals(key, StringComparison.OrdinalIgnoreCase));

        if (count == 0)
        {
            return $"相关备注 0 条；建议为“{normalized}”建立待办/风险备注。";
        }

        return $"相关备注 {count} 条；可在创作者备注页筛选“工作台行动”或“{normalized}”。";
    }

    private static string NormalizeActionTargetArea(string targetArea)
        => string.IsNullOrWhiteSpace(targetArea)
            ? "未命名行动"
            : targetArea.Replace("#", "＃", StringComparison.Ordinal).Trim();

    private static int EvidenceKindRank(string kind) => kind switch
    {
        "发布前综合报告" => 100,
        "关卡地图联动报告" => 90,
        "R/S命令引用核对清单" => 88,
        "测试副本差异报告" => 80,
        "项目体检报告" => 75,
        "结构化写入报告" => 70,
        "可视化预览PNG" => 65,
        _ => 10
    };
}

