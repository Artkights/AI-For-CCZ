using System.Text.Json;
using CCZModStudio.Core;
using CCZModStudio.Models;

internal static class ProgramRsPixelCharacterDesignWorkflowSmoke
{
    public static void Run(string[] args)
    {
        var values = args
            .Where(arg => !arg.StartsWith("--", StringComparison.Ordinal))
            .ToArray();
        var workspaceRoot = values.Length > 0 ? Path.GetFullPath(values[0]) : Directory.GetCurrentDirectory();
        var gameRoot = values.Length > 1
            ? Path.GetFullPath(values[1])
            : Path.Combine(workspaceRoot, "基底", "重生之氪金桓王传");
        var designImage = values.Length > 2
            ? Path.GetFullPath(values[2])
            : Path.Combine(workspaceRoot, "孙策.png");

        var project = new CczProject
        {
            WorkspaceRoot = workspaceRoot,
            GameRoot = gameRoot,
            HexTableXmlPath = Path.Combine(workspaceRoot, "工具整合包", "CCZModStudio", "Assets", "Data", "HexTables.xml")
        };

        var result = new RsPixelCharacterDesignService().Build(project, new RsPixelCharacterDesignRequest
        {
            PackageId = "SunCe_MCP_SingleSpearCavalry_v1",
            DisplayName = "孙策 MCP 单枪枪骑兵 v1",
            UnitType = "spear_cavalry",
            DesignImagePath = designImage,
            CharacterBrief = "孙策；江东主将；黑金重甲；金冠束发；红黑披风；主将气质；曹操传 6.5 短身骑乘战棋小人。",
            WeaponBrief = "唯一武器是一把长枪/长矛；攻击为蓄力、突刺、挑击、枪尖白金枪芒爆发、收枪。",
            ForbiddenReadings = "禁止剑、短刃、腰剑、背剑、第二枪、双武器、宽白剑弧、现代像素贴纸、程序化几何块。",
            GenerateNow = false,
            DryRun = true
        });

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            result.PackageId,
            result.GenerationStatus,
            result.PackageRoot,
            result.DesignImagePath,
            result.FormatActionImagePath,
            ReportCount = result.Reports.Count,
            ReferenceCountS = result.SUnitPlan.ReferenceImages.Count,
            ReferenceCountR = result.RActorPlan.ReferenceImages.Count,
            result.Warnings,
            result.Errors
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
