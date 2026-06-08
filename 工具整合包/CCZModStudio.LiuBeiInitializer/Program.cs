using System.Data;
using System.Globalization;
using System.Text;
using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var workspace = FindWorkspaceRoot();
var gameRoot = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.Combine(workspace, "基底", "刘备传加强版6.5");

var project = new ProjectDetector().CreateProjectFromGameRoot(gameRoot);
var parser = new HexTableParser();
var tables = parser.Load(project.HexTableXmlPath);
var reader = new HexTableReader();
var writer = new HexTableWriter();

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine($"Workspace={project.WorkspaceRoot}");
Console.WriteLine($"GameRoot={project.GameRoot}");
Console.WriteLine($"HexTable={project.HexTableXmlPath}");
Console.WriteLine($"TABLE_COUNT={tables.Count}");

foreach (var status in project.GetFileStatuses())
{
    Console.WriteLine($"FILE {status.Name} exists={status.Exists} size={status.SizeBytes?.ToString(CultureInfo.InvariantCulture) ?? "-"}");
}

if (args.Any(x => x.Equals("--dump-scripts", StringComparison.OrdinalIgnoreCase)))
{
    DumpScenarioTextParameters(project);
    return;
}

if (args.Any(x => x.Equals("--dump-battle", StringComparison.OrdinalIgnoreCase)))
{
    DumpBattlefieldCommands(project);
    return;
}

if (args.Any(x => x.Equals("--dump-battle-summary", StringComparison.OrdinalIgnoreCase)))
{
    DumpBattlefieldSummary(project, tables, reader);
    return;
}

WriteRoles(project, tables, reader, writer);
WriteJobs(project, tables, reader, writer);
WriteScenarioScripts(project);
WriteDesignDocuments(project.GameRoot);
Verify(project, tables, reader);

Console.WriteLine("LIUBEI_INIT_OK");

static void WriteRoles(CczProject project, IReadOnlyList<HexTableDefinition> tables, HexTableReader reader, HexTableWriter writer)
{
    var roles = new[]
    {
        new Role(0, "刘备", 0, 0, 0, 1, 76, 88, 82, 78, 92, 120, 32, 0, 1, 0,
            "汉室远支，涿郡起兵。宽厚能容人，临阵不以勇冠军，却能使众心归附。",
            "众人随我，莫负此义！", "大丈夫当留有用之身！"),
        new Role(1, "关羽", 1, 1, 1, 1, 98, 94, 80, 76, 96, 126, 20, 1, 1, 0,
            "河东解良人，重义轻生。早年随刘备举义，以一身武艺护住草创军心。",
            "义在刀前，退者披靡！", "兄长暂退，云长断后。"),
        new Role(2, "张飞", 2, 2, 2, 1, 100, 84, 46, 72, 88, 132, 12, 2, 1, 0,
            "涿郡豪侠，性烈如火。冲阵勇不可当，但需刘备、关羽约束锋芒。",
            "燕人张飞在此！", "今日失利，来日再战！"),
        new Role(3, "简雍", 3, 3, 3, 1, 42, 58, 82, 70, 76, 96, 48, 3, 1, 0,
            "刘备旧友，谈笑能解急难。前期负责联络乡勇与安抚百姓。",
            "话已至此，请君自决。", "此地不可久留。"),
        new Role(4, "糜竺", 4, 4, 4, 1, 38, 62, 84, 62, 78, 100, 56, 4, 1, 0,
            "东海富商，通财货、识大势。以辎重和人脉支撑刘备军早期转战。",
            "军资既足，人心可定。", "钱粮可失，人不可失。"),
        new Role(5, "孙乾", 5, 5, 5, 1, 40, 60, 80, 68, 74, 98, 52, 4, 1, 0,
            "北海名士，擅长辞令。常为刘备奔走诸侯之间，维系外援。",
            "且听乾一言。", "使命未竟，不可恋战。"),
        new Role(6, "义勇队长", 6, 6, 6, 1, 70, 66, 52, 64, 68, 108, 10, 5, 1, 0,
            "涿郡义勇代表。并非名将，却是刘备军从乡土中生长出的第一批根基。",
            "乡亲在后，不能退！", "我等先撤，护住百姓。"),
        new Role(7, "黄巾渠帅", 7, 7, 7, 2, 78, 70, 50, 64, 72, 118, 10, 6, 2, 0,
            "地方黄巾首领，裹挟饥民与盗匪作乱。兵多而杂，败后仍有余部流窜。",
            "苍天已死，黄天当立！", "大势未绝，暂且退走！"),
        new Role(8, "黄巾悍卒", 8, 8, 8, 2, 74, 62, 38, 58, 70, 108, 8, 6, 1, 0,
            "黄巾军中敢死之徒。装备粗劣，却惯于乱战冲击。",
            "抢粮！破寨！", "风紧，先走！"),
        new Role(9, "乡勇", 9, 9, 9, 1, 60, 58, 44, 58, 62, 100, 8, 5, 1, 0,
            "受刘备号召而来的乡中壮丁。训练不足，但熟悉乡野地形。",
            "愿随刘君破贼！", "阵脚乱了！")
    };

    var personTable = Find(tables, "6.5-0 人物");
    var person = reader.Read(project, personTable, tables).Data;
    foreach (var role in roles)
    {
        var row = FindRow(person, role.Id);
        SetCell(row, "名称", role.Name);
        SetCell(row, "头像", role.Face);
        SetCell(row, "撤退台词", role.RetreatQuote);
        SetCell(row, "暴击台词", role.CriticalQuote);
        SetCell(row, "Army", role.Army);
        SetCell(row, "武力", role.Force);
        SetCell(row, "统帅", role.Command);
        SetCell(row, "智力", role.Intelligence);
        SetCell(row, "敏捷", role.Agility);
        SetCell(row, "士气", role.Morale);
        SetCell(row, "初始HP", role.Hp);
        SetCell(row, "初始MP", role.Mp);
        SetCell(row, "职业", role.Job);
        SetCell(row, "级别", role.Level);
        SetCell(row, "经验", role.Exp);
    }
    SaveIfChanged(project, writer, personTable, person);

    WriteTextTable(project, tables, reader, writer, "6.5-0-1 人物列传", roles.Select(r => (r.Id, r.Bio)).ToArray());
    WriteTextTable(project, tables, reader, writer, "6.5-0-2 暴击台词", roles.Select(r => (r.CriticalQuote, r.CriticalText)).ToArray());
    WriteTextTable(project, tables, reader, writer, "6.5-0-3 撤退台词", roles.Where(r => r.RetreatQuote < 49).Select(r => (r.RetreatQuote, r.RetreatText)).ToArray());
}

static void WriteJobs(CczProject project, IReadOnlyList<HexTableDefinition> tables, HexTableReader reader, HexTableWriter writer)
{
    var jobs = new[]
    {
        new Job(0, "仁主", "刘备准专属兵种。攻势不烈，但统帅、防御和士气成长稳定，适合居中支援。", 5, 1, 3, 4, 3, 3, 4, 4, 3),
        new Job(1, "武圣", "关羽专属前期兵种。重视攻击与士气，适合突破敌军骨干。", 6, 1, 5, 4, 2, 3, 5, 4, 2),
        new Job(2, "猛将", "张飞专属前期兵种。攻击极强，精神偏低，适合正面冲阵。", 5, 1, 5, 3, 1, 3, 4, 5, 1),
        new Job(3, "游士", "简雍定位。移动灵活，偏策略与辅助，正面战斗能力有限。", 5, 1, 2, 2, 4, 4, 3, 2, 4),
        new Job(4, "辎辅", "糜竺、孙乾定位。擅长补给和支援，生存尚可，输出较弱。", 4, 1, 2, 3, 4, 2, 3, 3, 4),
        new Job(5, "义勇兵", "涿郡义勇基础兵种。成长普通，地形适应较好，是刘备军早期根基。", 5, 1, 3, 3, 2, 3, 3, 3, 2),
        new Job(6, "黄巾军", "前期敌方兵种。数量多、纪律差，攻击尚可但防御与精神不足。", 5, 1, 3, 2, 1, 2, 3, 3, 1)
    };

    var seriesTable = Find(tables, "6.5-3 兵种系");
    var series = reader.Read(project, seriesTable, tables).Data;
    foreach (var job in jobs) SetCell(FindRow(series, job.Id), "名称", job.Name);
    SaveIfChanged(project, writer, seriesTable, series);

    var detailTable = Find(tables, "6.5-4 详细兵种");
    var detail = reader.Read(project, detailTable, tables).Data;
    foreach (var job in jobs) SetCell(FindRow(detail, job.Id), "名称", job.Name);
    SaveIfChanged(project, writer, detailTable, detail);

    WriteTextTable(project, tables, reader, writer, "6.5-4-1 兵种说明", jobs.Select(j => (j.Id, j.Description)).ToArray());

    var growthTable = Find(tables, "6.5-4-2 兵种成长");
    var growth = reader.Read(project, growthTable, tables).Data;
    foreach (var job in jobs)
    {
        var row = FindRow(growth, job.Id);
        SetCell(row, "移动力", job.Move);
        SetCell(row, "攻击范围", job.Range);
        SetCell(row, "攻击", job.Attack);
        SetCell(row, "防御", job.Defense);
        SetCell(row, "精神", job.Spirit);
        SetCell(row, "爆发", job.Explosive);
        SetCell(row, "士气", job.Morale);
        SetCell(row, "HP", job.Hp);
        SetCell(row, "MP", job.Mp);
    }
    SaveIfChanged(project, writer, growthTable, growth);
}

static void DumpScenarioTextParameters(CczProject project)
{
    var dictionary = new SceneStringParser().Parse(ProjectDetector.FindSceneDictionaryPath(project));
    var legacyReader = new LegacyScenarioReader();
    foreach (var relativePath in new[] { "RS/R_00.eex", "RS/S_00.eex", "RS/R_01.eex", "RS/S_01.eex", "RS/R_02.eex", "RS/S_02.eex" })
    {
        var path = Path.Combine(project.GameRoot, relativePath);
        var document = legacyReader.Read(path, dictionary);
        Console.WriteLine($"DUMP {relativePath} {document.Summary}");
        foreach (var item in EnumerateTextParameters(document))
        {
            Console.WriteLine($"{relativePath} scene={item.Command.SceneIndex} section={item.Command.SectionIndex} cmd={item.Command.CommandIndex} id={item.Command.CommandIdHex} name={item.Command.CommandName} text={OneLine(item.Parameter.Text)}");
        }
    }
}

static void DumpBattlefieldCommands(CczProject project)
{
    var dictionary = new SceneStringParser().Parse(ProjectDetector.FindSceneDictionaryPath(project));
    var legacyReader = new LegacyScenarioReader();
    var commandIds = new HashSet<int>
    {
        0x1A, 0x25, 0x26, 0x2E, 0x36, 0x3F, 0x40, 0x41, 0x42, 0x43,
        0x1C, 0x27, 0x29, 0x2A, 0x2B, 0x2C,
        0x44, 0x45, 0x46, 0x47, 0x48, 0x49, 0x4B, 0x4C, 0x4D, 0x4E,
        0x53, 0x59, 0x5A, 0x5F, 0x60, 0x61, 0x62, 0x63, 0x64, 0x65,
        0x66, 0x68, 0x72, 0x77, 0x78, 0x79
    };

    foreach (var relativePath in new[] { "RS/S_00.eex", "RS/S_01.eex", "RS/S_02.eex" })
    {
        var path = Path.Combine(project.GameRoot, relativePath);
        var document = legacyReader.Read(path, dictionary);
        Console.WriteLine($"BATTLE_DUMP {relativePath} {document.Summary}");
        foreach (var command in document.EnumerateCommands().Where(x => commandIds.Contains(x.CommandId)))
        {
            Console.WriteLine($"{relativePath} scene={command.SceneIndex} section={command.SectionIndex} cmd={command.CommandIndex} off=0x{command.FileOffset:X6} id={command.CommandIdHex} name={command.CommandName} child={(command.ChildBlock == null ? 0 : command.ChildBlock.Commands.Count)} params={FormatParameters(command)}");
        }
    }
}

static void DumpBattlefieldSummary(CczProject project, IReadOnlyList<HexTableDefinition> tables, HexTableReader reader)
{
    _ = reader;

    var dictionary = new SceneStringParser().Parse(ProjectDetector.FindSceneDictionaryPath(project));
    var scenarios = new ScenarioFileReader()
        .ReadAllIndex(project)
        .Where(scenario => ScenarioFileReader.IsBattlefieldScriptFile(scenario.FileName))
        .ToList();
    var battlefieldService = new BattlefieldEditorService();

    Console.WriteLine($"BATTLE_SUMMARY count={scenarios.Count}");
    foreach (var scenario in scenarios)
    {
        var document = battlefieldService.Load(project, scenario, dictionary, tables);
        var deployments = document.UnitCandidates
            .Select(candidate => candidate.Category)
            .Where(category => category is "我军出场" or "友军出场" or "敌军出场")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(category => category, StringComparer.Ordinal)
            .ToList();
        var title = document.TitleEntry?.Text ?? scenario.TitleHint;
        Console.WriteLine($"BATTLE_SUMMARY_ROW file={scenario.FileName} title=\"{OneLine(title)}\" commands={document.CommandCandidates.Count} units={document.UnitCandidates.Count} deployment={string.Join("/", deployments)} condition={document.ConditionEntry?.OffsetHex ?? "-"}");
    }
}

static string FormatParameters(LegacyScenarioCommandNode command)
{
    if (command.Parameters.Count == 0) return "-";
    return string.Join(" | ", command.Parameters.Select(parameter =>
    {
        var prefix = $"p{parameter.Index}[layout={parameter.LayoutCodeHex},tag={parameter.TagHex}]";
        return parameter.Kind switch
        {
            LegacyScenarioParameterKind.Text => $"{prefix}=TEXT:{OneLine(parameter.Text)}",
            LegacyScenarioParameterKind.VariableArray => $"{prefix}=ARR({parameter.Values.Count}):{string.Join(",", parameter.Values.Select(x => x.ToString(CultureInfo.InvariantCulture)))}",
            LegacyScenarioParameterKind.Dword32 => $"{prefix}=DWORD:{parameter.IntValue}",
            _ => $"{prefix}=WORD:{parameter.IntValue}"
        };
    }));
}

static void WriteScenarioScripts(CczProject project)
{
    var dictionary = new SceneStringParser().Parse(ProjectDetector.FindSceneDictionaryPath(project));
    var legacyReader = new LegacyScenarioReader();
    var legacyWriter = new LegacyScenarioWriter();

    var scripts = BuildScenarioScriptPlans();
    foreach (var script in scripts)
    {
        var fullPath = Path.Combine(project.GameRoot, script.RelativePath);
        if (!File.Exists(fullPath))
        {
            Console.WriteLine($"LEGACY_SCRIPT_SKIP missing {script.RelativePath}");
            continue;
        }

        var document = legacyReader.Read(fullPath, dictionary);
        var textParameters = EnumerateTextParameters(document).ToList();
        var changed = ApplyScenarioScriptPlan(script, textParameters);
        if (changed == 0)
        {
            Console.WriteLine($"LEGACY_SCRIPT_SAVE_SKIP {script.RelativePath} unchanged");
            continue;
        }

        var result = legacyWriter.Save(project, script.RelativePath, document, dictionary, "刘备传前三关完整树剧本重写");
        Console.WriteLine($"LEGACY_SCRIPT_SAVE {script.RelativePath} changedText={changed} changedBytes={result.ChangedBytes} validation=\"{result.ValidationSummary}\" report={result.ReportJsonPath}");
    }
}

static IReadOnlyList<ScenarioScriptPlan> BuildScenarioScriptPlans()
{
    return new[]
    {
        ScenarioScriptPlan.R("RS/R_00.eex", "桃园举义", "涿郡", new[]
        {
            "&刘备\n黄巾四起，乡里不安。\n备虽力薄，不能坐视。",
            "&关羽\n既为义举，关某愿随兄长\n扶危济困。",
            "&张飞\n俺家庄院可招乡勇。\n哥哥一句话，便去破贼！",
            "三人焚香告天，结为兄弟。\n自此举义兵，先安涿郡。",
            "&刘备\n今日起兵，不为功名，\n只为百姓有路可走。"
        }),
        ScenarioScriptPlan.S("RS/S_00.eex",
            "胜利条件\n一、击退黄巾散兵。\n\n失败条件\n一、刘备败退。",
            "击退黄巾散兵！",
            new[]
        {
            "&刘备\n百姓在后，诸位不可恋战。",
            "&关羽\n云长守左，护住村口。",
            "&张飞\n俺从右路冲开贼阵！",
            "&黄巾兵\n粮在庄中，抢了再走！",
            "&刘备\n先救乡民，再整兵追贼。",
            "刘关张初战告捷，义勇之名\n由涿郡乡里传开。"
        }),
        ScenarioScriptPlan.R("RS/R_01.eex", "涿郡破贼", "涿郡", new[]
        {
            "&简雍\n玄德，黄巾余部逼近涿郡，\n乡民多来投你。",
            "&刘备\n既受众望，便不可负众望。",
            "&关羽\n此战当立军法，\n使义勇知进退。",
            "&张飞\n立法归立法，\n打头阵还得看俺。",
            "刘备整编乡勇，关羽定阵，\n张飞请为先锋。"
        }),
        ScenarioScriptPlan.S("RS/S_01.eex",
            "胜利条件\n一、击破黄巾渠帅。\n\n失败条件\n一、刘备败退。",
            "击破黄巾渠帅！",
            new[]
        {
            "&刘备\n义勇初成，诸位不可贪功。",
            "&张飞\n贼首在前，看俺取他！",
            "&关羽\n三弟莫孤进，阵势不可乱。",
            "&黄巾渠帅\n官军未至，先灭这乡兵！",
            "&简雍\n百姓已撤，玄德可放手一战。",
            "刘备军涿郡破贼，仁义之名\n始传乡里。"
        }),
        ScenarioScriptPlan.R("RS/R_02.eex", "青州救援", "青州道", new[]
        {
            "&糜竺\n流民被困青州道，\n若无人接应，恐为乱军所并。",
            "&孙乾\n救人须快，也须有粮。",
            "&刘备\n备兵少势弱，\n但见死不救，何谈仁义。",
            "&关羽\n此去不为争功，\n只为开一条生路。",
            "众人整军北上，赴青州救援。"
        }),
        ScenarioScriptPlan.S("RS/S_02.eex",
            "胜利条件\n一、救出被围乡民。\n\n失败条件\n一、刘备败退。\n二、乡民全灭。",
            "救出被围乡民！",
            new[]
        {
            "&刘备\n此战重在救人，不在争功。",
            "&糜竺\n粮车已备，可支撑百姓撤离。",
            "&孙乾\n援军未到，我等须稳住阵脚。",
            "&关羽\n守住渡口，勿使贼兵截路。",
            "&张飞\n谁敢近百姓一步！",
            "青州之役后，刘备明白创业之难\n不止在破敌，更在存人。"
        })
    };
}

static int ApplyScenarioScriptPlan(ScenarioScriptPlan script, IReadOnlyList<LegacyTextRef> textParameters)
{
    var changed = 0;
    var isBattle = Path.GetFileName(script.RelativePath).StartsWith("S_", StringComparison.OrdinalIgnoreCase);

    if (isBattle)
    {
        changed += ApplyAll(textParameters, x => x.Command.CommandId == 0x19, script.ConditionText);
        changed += ApplyFirst(textParameters, x => x.Command.CommandId == 0x1A, script.DisplayObjective);
        var dialogueCandidates = Ordered(textParameters)
            .Where(x => x.Command.CommandId is 0x14 or 0x16 && x.Command.SceneIndex >= 2 && IsEditableStoryText(x))
            .ToList();
        changed += ApplySequential(dialogueCandidates, script.StoryTexts, script.RelativePath);
        changed += ReplaceRemainingOldStoryText(VisibleStoryTextParameters(textParameters), script);
        return changed;
    }

    var eventText = textParameters
        .Where(x => x.Command.CommandId == 0x18 && IsEditableStoryText(x))
        .OrderBy(x => x.Command.SceneIndex)
        .ThenBy(x => x.Command.SectionIndex)
        .ThenBy(x => x.Command.CommandIndex)
        .FirstOrDefault();
    changed += ApplyText(eventText, script.EventName);

    var eventScene = eventText?.Command.SceneIndex ?? 2;
    changed += ApplyFirst(textParameters, x => x.Command.CommandId == 0x17 && x.Command.SceneIndex >= eventScene, script.PlaceName);

    var storyCandidates = Ordered(textParameters)
        .Where(x => x.Command.CommandId is 0x14 or 0x16 && x.Command.SceneIndex >= eventScene && IsEditableStoryText(x))
        .ToList();
    changed += ApplySequential(storyCandidates, script.StoryTexts, script.RelativePath);
    changed += ReplaceRemainingOldStoryText(VisibleStoryTextParameters(textParameters), script);
    return changed;
}

static IEnumerable<LegacyTextRef> Ordered(IEnumerable<LegacyTextRef> textParameters)
    => textParameters
        .OrderBy(x => x.Command.SceneIndex)
        .ThenBy(x => x.Command.SectionIndex)
        .ThenBy(x => x.Command.CommandIndex)
        .ThenBy(x => x.Parameter.Index);

static IReadOnlyList<LegacyTextRef> VisibleStoryTextParameters(IEnumerable<LegacyTextRef> textParameters)
    => Ordered(textParameters)
        .Where(x => x.Command.CommandId is 0x14 or 0x16 or 0x17 or 0x18 or 0x19 or 0x1A or 0x63 or 0x67 or 0x69)
        .Where(IsEditableStoryText)
        .ToList();

static int ApplyFirst(IEnumerable<LegacyTextRef> textParameters, Func<LegacyTextRef, bool> predicate, string? text)
{
    var target = Ordered(textParameters).FirstOrDefault(x => predicate(x) && IsEditableStoryText(x));
    return ApplyText(target, text);
}

static int ApplyAll(IEnumerable<LegacyTextRef> textParameters, Func<LegacyTextRef, bool> predicate, string? text)
{
    var changed = 0;
    foreach (var target in Ordered(textParameters).Where(x => predicate(x) && IsEditableStoryText(x)))
    {
        changed += ApplyText(target, text);
    }

    return changed;
}

static int ApplySequential(IReadOnlyList<LegacyTextRef> candidates, IReadOnlyList<string> texts, string relativePath)
{
    var changed = 0;
    for (var i = 0; i < texts.Count && i < candidates.Count; i++)
    {
        changed += ApplyText(candidates[i], texts[i]);
    }

    if (texts.Count > candidates.Count)
    {
        Console.WriteLine($"LEGACY_SCRIPT_TEXT_LIMIT {relativePath} planned={texts.Count} writable={candidates.Count}");
    }

    return changed;
}

static int ReplaceRemainingOldStoryText(IEnumerable<LegacyTextRef> candidates, ScenarioScriptPlan script)
{
    var changed = 0;
    foreach (var item in candidates)
    {
        if (!LooksLikeOldCaoCaoLine(item.Parameter.Text))
        {
            continue;
        }

        changed += ApplyText(item, BuildFallbackStoryText(item, script));
    }

    return changed;
}

static bool LooksLikeOldCaoCaoLine(string text)
    => text.Contains("曹操", StringComparison.Ordinal)
       || text.Contains("孟德", StringComparison.Ordinal)
       || text.Contains("曹将军", StringComparison.Ordinal)
       || text.Contains("骑都尉", StringComparison.Ordinal)
       || text.Contains("主公", StringComparison.Ordinal)
       || text.Contains("董卓", StringComparison.Ordinal)
       || text.Contains("汜水关", StringComparison.Ordinal)
       || text.Contains("虎牢关", StringComparison.Ordinal)
       || text.Contains("华雄", StringComparison.Ordinal)
       || text.Contains("吕布", StringComparison.Ordinal)
       || text.Contains("奉先", StringComparison.Ordinal)
       || text.Contains("袁绍", StringComparison.Ordinal)
       || text.Contains("袁术", StringComparison.Ordinal)
       || text.Contains("孙坚", StringComparison.Ordinal)
       || text.Contains("联军", StringComparison.Ordinal)
       || text.Contains("温酒", StringComparison.Ordinal)
       || text.Contains("霸王诞生", StringComparison.Ordinal)
       || text.Contains("颍川", StringComparison.Ordinal);

static string BuildFallbackStoryText(LegacyTextRef item, ScenarioScriptPlan script)
{
    var name = Path.GetFileName(script.RelativePath);
    if (item.Command.CommandId == 0x17)
    {
        return script.PlaceName ?? (name.StartsWith("S_", StringComparison.OrdinalIgnoreCase) ? "战场" : "涿郡");
    }

    if (item.Command.CommandId is 0x18 or 0x67)
    {
        return script.EventName ?? Path.GetFileNameWithoutExtension(script.RelativePath);
    }

    if (item.Command.CommandId == 0x19)
    {
        return script.ConditionText ?? "胜利条件\n一、完成当前目标。\n\n失败条件\n一、刘备败退。";
    }

    if (item.Command.CommandId == 0x1A)
    {
        return script.DisplayObjective ?? "完成当前目标！";
    }

    if (item.Command.CommandId == 0x63)
    {
        return "战场交锋告一段落。";
    }

    if (item.Command.CommandId == 0x69)
    {
        return "刘备军稳住阵脚，继续推进。";
    }

    if (name.Equals("S_00.eex", StringComparison.OrdinalIgnoreCase))
    {
        return item.Command.CommandId == 0x16
            ? "刘备军击退黄巾散兵，乡民暂得保全。"
            : "&刘备\n黄巾虽退，乱世未止。\n先护乡民离开此地。";
    }

    if (name.Equals("S_01.eex", StringComparison.OrdinalIgnoreCase))
    {
        return item.Command.CommandId == 0x16
            ? "涿郡义勇初成，军心渐定。"
            : "&关羽\n阵脚已稳，三弟不可孤进。\n先合力击破贼首。";
    }

    if (name.Equals("S_02.eex", StringComparison.OrdinalIgnoreCase))
    {
        return item.Command.CommandId == 0x16
            ? "青州救援以保全百姓为先。"
            : "&刘备\n救人要紧，诸军守住退路。\n不可恋战。";
    }

    return item.Command.CommandId == 0x16
        ? "刘备军整备完毕，继续前行。"
        : "&刘备\n乱世行事，当先问百姓生路。";
}

static int ApplyText(LegacyTextRef? textRef, string? text)
{
    if (textRef == null || string.IsNullOrWhiteSpace(text)) return 0;
    var target = text.Trim();
    if (string.Equals(textRef.Parameter.Text, target, StringComparison.Ordinal)) return 0;
    textRef.Parameter.Text = target;
    return 1;
}

static bool IsEditableStoryText(LegacyTextRef textRef)
{
    var command = textRef.Command;
    var text = textRef.Parameter.Text;
    if (string.IsNullOrWhiteSpace(text)) return false;
    var normalized = text.Trim();
    if (normalized.Length <= 1) return false;
    if (normalized.All(char.IsDigit)) return false;
    if (normalized.Contains("全局变量", StringComparison.Ordinal) || normalized.Contains("变量", StringComparison.Ordinal)) return false;
    return command.CommandId is 0x14 or 0x16 or 0x17 or 0x18 or 0x19 or 0x1A or 0x63 or 0x67 or 0x69 && normalized.Any(IsCjk);
}

static IReadOnlyList<LegacyTextRef> EnumerateTextParameters(LegacyScenarioDocument document)
{
    var result = new List<LegacyTextRef>();
    foreach (var command in document.EnumerateCommands())
    {
        foreach (var parameter in command.TextParameters)
        {
            result.Add(new LegacyTextRef(command, parameter));
        }
    }
    return result;
}

static bool IsCjk(char ch) => ch >= 0x4E00 && ch <= 0x9FFF;

static string OneLine(string text)
    => text.Replace("\r\n", "\\n", StringComparison.Ordinal).Replace('\r', '\n').Replace("\n", "\\n", StringComparison.Ordinal);

static void WriteDesignDocuments(string gameRoot)
{
    var docDir = Path.Combine(gameRoot, "LiuBeiDesign");
    Directory.CreateDirectory(docDir);
    File.WriteAllText(Path.Combine(docDir, "前3关MVP设计.md"), """
# 刘备传 6.5 前 3 关 MVP 设计

## 创作基调

演义正史融合。前期刘备不是高资源诸侯，而是以乡土、义勇和救援行动建立声望。

## 关卡 1：桃园举义

- 主题：刘关张初战，保护乡里。
- 我方：刘备、关羽、张飞。
- 敌方：黄巾散兵、盗匪。
- 胜利：击退黄巾散兵。
- 失败：刘备败退。
- 设计重点：低压教学，突出三兄弟定位。

## 关卡 2：涿郡破贼

- 主题：义勇成军。
- 我方：刘备、关羽、张飞、义勇队长。
- 敌方：黄巾渠帅、黄巾军。
- 胜利：击破黄巾渠帅。
- 失败：刘备败退。
- 设计重点：张飞冲阵、关羽控线、刘备居中支援。

## 关卡 3：青州救援

- 主题：救援而非歼灭。
- 我方：刘备、关羽、张飞、简雍、糜竺或孙乾。
- 敌方：黄巾军、流寇。
- 胜利：救出被围乡民。
- 失败：刘备败退或乡民全灭。
- 设计重点：防守转进攻，强调刘备集团的价值选择。
""", Encoding.UTF8);
}

static void Verify(CczProject project, IReadOnlyList<HexTableDefinition> tables, HexTableReader reader)
{
    var person = reader.Read(project, Find(tables, "6.5-0 人物"), tables).Data;
    foreach (var id in Enumerable.Range(0, 10))
    {
        var row = FindRow(person, id);
        Console.WriteLine($"VERIFY_ROLE id={id} name={row["名称"]} job={row["职业"]} force={row["武力"]} command={row["统帅"]} int={row["智力"]}");
    }

    var jobs = reader.Read(project, Find(tables, "6.5-4 详细兵种"), tables).Data;
    foreach (var id in Enumerable.Range(0, 7))
    {
        var row = FindRow(jobs, id);
        Console.WriteLine($"VERIFY_JOB id={id} name={row["名称"]}");
    }
}

static void WriteTextTable(CczProject project, IReadOnlyList<HexTableDefinition> tables, HexTableReader reader, HexTableWriter writer, string tableName, IReadOnlyList<(int Id, string Text)> updates)
{
    var table = Find(tables, tableName);
    var data = reader.Read(project, table, tables).Data;
    foreach (var update in updates)
    {
        if (update.Id < 0 || update.Id >= data.Rows.Count) continue;
        SetCell(FindRow(data, update.Id), "介绍", update.Text);
    }
    SaveIfChanged(project, writer, table, data);
}

static void SetCell(DataRow row, string columnName, object value)
{
    var current = Convert.ToString(row[columnName], CultureInfo.InvariantCulture) ?? string.Empty;
    var next = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    if (string.Equals(current, next, StringComparison.Ordinal))
    {
        return;
    }

    row[columnName] = value;
}

static void SaveIfChanged(CczProject project, HexTableWriter writer, HexTableDefinition table, DataTable data)
{
    if (data.GetChanges() == null)
    {
        Console.WriteLine($"SAVE_SKIP {table.TableName} unchanged");
        return;
    }

    var save = writer.Save(project, table, data);
    Console.WriteLine($"SAVE {table.TableName} changedBytes={save.ChangedBytes} report={save.ReportJsonPath}");
}

static HexTableDefinition Find(IReadOnlyList<HexTableDefinition> tables, string name)
    => tables.Single(t => t.TableName == name);

static DataRow FindRow(DataTable table, int id)
    => table.Rows.Cast<DataRow>().Single(r => Convert.ToInt32(r["ID"], CultureInfo.InvariantCulture) == id);

static string FindWorkspaceRoot()
{
    var dir = new DirectoryInfo(Environment.CurrentDirectory);
    while (dir != null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "基底")) && Directory.Exists(Path.Combine(dir.FullName, "工具整合包")))
        {
            return dir.FullName;
        }
        dir = dir.Parent;
    }
    return Environment.CurrentDirectory;
}

sealed record Role(
    int Id,
    string Name,
    int Face,
    int RetreatQuote,
    int CriticalQuote,
    int Army,
    int Force,
    int Command,
    int Intelligence,
    int Agility,
    int Morale,
    int Hp,
    int Mp,
    int Job,
    int Level,
    int Exp,
    string Bio,
    string CriticalText,
    string RetreatText);

sealed record Job(
    int Id,
    string Name,
    string Description,
    int Move,
    int Range,
    int Attack,
    int Defense,
    int Spirit,
    int Explosive,
    int Morale,
    int Hp,
    int Mp);

sealed record ScenarioScriptPlan(
    string RelativePath,
    string? EventName,
    string? PlaceName,
    string? ConditionText,
    string? DisplayObjective,
    IReadOnlyList<string> StoryTexts)
{
    public static ScenarioScriptPlan R(string relativePath, string eventName, string placeName, IReadOnlyList<string> storyTexts)
        => new(relativePath, eventName, placeName, null, null, storyTexts);

    public static ScenarioScriptPlan S(string relativePath, string conditionText, string displayObjective, IReadOnlyList<string> storyTexts)
        => new(relativePath, null, null, conditionText, displayObjective, storyTexts);
}

sealed record LegacyTextRef(LegacyScenarioCommandNode Command, LegacyScenarioCommandParameter Parameter);
