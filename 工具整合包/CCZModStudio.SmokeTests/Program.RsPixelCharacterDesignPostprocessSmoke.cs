using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text.Json;
using CCZModStudio.Core;
using CCZModStudio.Models;

internal static class ProgramRsPixelCharacterDesignPostprocessSmoke
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

        var project = new CczProject
        {
            WorkspaceRoot = workspaceRoot,
            GameRoot = gameRoot,
            HexTableXmlPath = Path.Combine(workspaceRoot, "工具整合包", "CCZModStudio", "Assets", "Data", "HexTables.xml")
        };

        var smokeRoot = Path.Combine(workspaceRoot, "CCZModStudio_Exports", "Smoke", "RsPixelCharacterDesignPostprocess");
        Directory.CreateDirectory(smokeRoot);
        var rSheet = Path.Combine(smokeRoot, "r_actor_2x20_source.png");
        var sSheet = Path.Combine(smokeRoot, "s_unit_4x6_source.png");
        CreateRActorSourceSheet(rSheet);
        CreateSUnitSourceSheet(sSheet);

        var service = new AiImageAssetService();
        var rPlan = service.BuildPromptPlan(
            project,
            "r_actor",
            "postprocess smoke R actor; front/back must be cut from a 2x20 sheet",
            targetRelativePath: null,
            imageNumber: null,
            rImageId: 0,
            sImageId: null,
            faceId: null,
            jobId: null,
            factionSlot: 1,
            outputFormat: "bmp",
            width: 48,
            height: 1280);
        var sPlan = service.BuildPromptPlan(
            project,
            "s_unit",
            "postprocess smoke S unit; 4x6 action sheet",
            targetRelativePath: null,
            imageNumber: null,
            rImageId: null,
            sImageId: 64,
            faceId: null,
            jobId: null,
            factionSlot: 1,
            outputFormat: "bmp",
            width: 48,
            height: 528);

        var rPrepared = service.PrepareExistingImage(project, rPlan, rSheet);
        var sPrepared = service.PrepareExistingImage(project, sPlan, sSheet);

        var front = RequirePrepared(rPrepared, "front", 48, 1280);
        var back = RequirePrepared(rPrepared, "back", 48, 1280);
        var move = RequirePrepared(sPrepared, "move", 48, 528);
        var attack = RequirePrepared(sPrepared, "attack", 64, 768);
        var special = RequirePrepared(sPrepared, "special", 48, 240);

        AssertBmp(front.OutputPath, "front");
        AssertBmp(back.OutputPath, "back");
        AssertBmp(move.OutputPath, "move");
        AssertBmp(attack.OutputPath, "attack");
        AssertBmp(special.OutputPath, "special");
        AssertDistinctFrames(front.OutputPath, 48, 64, 20, "front");
        AssertDistinctFrames(back.OutputPath, 48, 64, 20, "back");
        if (front.OutputSha256.Equals(back.OutputSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("R front/back outputs are identical; back must be cut from its own column.");
        }

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            RPrepared = rPrepared.PreparedFiles.Select(x => new { x.Role, x.OutputPath, x.OutputWidth, x.OutputHeight, x.OutputSha256 }),
            SPrepared = sPrepared.PreparedFiles.Select(x => new { x.Role, x.OutputPath, x.OutputWidth, x.OutputHeight, x.OutputSha256 }),
            SafetyNote = "Smoke uses synthetic source sheets only to verify MCP post-processing. It is not a character asset and writes no game resources."
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static AiImagePreparedFile RequirePrepared(AiImagePrepareResult result, string role, int width, int height)
    {
        var file = result.PreparedFiles.FirstOrDefault(x => x.Role.Equals(role, StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException("Missing prepared file role: " + role);
        if (file.OutputWidth != width || file.OutputHeight != height)
        {
            throw new InvalidOperationException($"{role} dimensions mismatch: {file.OutputWidth}x{file.OutputHeight}, expected {width}x{height}.");
        }

        if (!File.Exists(file.OutputPath))
        {
            throw new FileNotFoundException("Prepared file was not written.", file.OutputPath);
        }

        return file;
    }

    private static void AssertBmp(string path, string label)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 2 || bytes[0] != (byte)'B' || bytes[1] != (byte)'M')
        {
            throw new InvalidOperationException($"{label} is not a BMP file: {path}");
        }
    }

    private static void AssertDistinctFrames(string path, int frameWidth, int frameHeight, int frameCount, string label)
    {
        using var image = new Bitmap(path);
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var frame = 0; frame < frameCount; frame++)
        {
            using var cell = image.Clone(new Rectangle(0, frame * frameHeight, frameWidth, frameHeight), PixelFormat.Format32bppArgb);
            using var stream = new MemoryStream();
            cell.Save(stream, ImageFormat.Png);
            hashes.Add(Convert.ToHexString(SHA256.HashData(stream.ToArray())));
        }

        if (hashes.Count < Math.Min(8, frameCount))
        {
            throw new InvalidOperationException($"{label} has too few distinct frames: {hashes.Count}/{frameCount}.");
        }
    }

    private static void CreateRActorSourceSheet(string path)
    {
        const int columns = 2;
        const int rows = 20;
        const int cellWidth = 48;
        const int cellHeight = 64;
        using var bitmap = new Bitmap(columns * cellWidth, rows * cellHeight, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Magenta);
        for (var row = 0; row < rows; row++)
        {
            DrawCell(graphics, 0, row, cellWidth, cellHeight, Color.FromArgb(20 + row * 8 % 200, 40, 120), row);
            DrawCell(graphics, 1, row, cellWidth, cellHeight, Color.FromArgb(120, 40 + row * 7 % 180, 20), row + 40);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bitmap.Save(path, ImageFormat.Png);
    }

    private static void CreateSUnitSourceSheet(string path)
    {
        const int columns = 4;
        const int rows = 6;
        const int cellWidth = 64;
        const int cellHeight = 64;
        using var bitmap = new Bitmap(columns * cellWidth, rows * cellHeight, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Magenta);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var index = row * columns + column;
                DrawCell(graphics, column, row, cellWidth, cellHeight, Color.FromArgb(30 + index * 7 % 200, 60 + index * 5 % 160, 90 + index * 3 % 140), index);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bitmap.Save(path, ImageFormat.Png);
    }

    private static void DrawCell(Graphics graphics, int column, int row, int cellWidth, int cellHeight, Color body, int seed)
    {
        var x = column * cellWidth;
        var y = row * cellHeight;
        using var brush = new SolidBrush(body);
        using var dark = new SolidBrush(Color.FromArgb(20, 20, 20));
        using var light = new SolidBrush(Color.FromArgb(245, 220, 90));
        graphics.FillRectangle(dark, x + 10 + seed % 5, y + 6, 24, 40);
        graphics.FillRectangle(brush, x + 12 + seed % 7, y + 10 + seed % 9, 20 + seed % 11, 30);
        graphics.FillRectangle(light, x + 16 + seed % 3, y + 4 + seed % 5, 12, 8);
        graphics.FillRectangle(dark, x + 8, y + 48, 34 + seed % 9, 4);
    }
}
