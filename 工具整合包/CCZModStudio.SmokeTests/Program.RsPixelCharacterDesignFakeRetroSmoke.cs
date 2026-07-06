using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CCZModStudio.Core;
using CCZModStudio.Models;

internal static class ProgramRsPixelCharacterDesignFakeRetroSmoke
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

        var smokeRoot = Path.Combine(workspaceRoot, "CCZModStudio_Exports", "Smoke", "RsPixelCharacterDesignFakeRetro");
        Directory.CreateDirectory(smokeRoot);
        var designImage = Path.Combine(smokeRoot, "design_reference.png");
        var formatImage = Path.Combine(smokeRoot, "format_action_reference.png");
        CreateReferenceImage(designImage, Color.FromArgb(18, 18, 22), Color.FromArgb(232, 190, 80));
        CreateReferenceImage(formatImage, Color.FromArgb(40, 70, 120), Color.FromArgb(245, 0, 255));

        using var fakeServer = new FakeRetroDiffusionServer(smokeRoot);
        fakeServer.Start();

        var oldBaseUrl = Environment.GetEnvironmentVariable(AiImageAssetService.EnvRetroDiffusionBaseUrl);
        var oldApiKey = Environment.GetEnvironmentVariable(AiImageAssetService.EnvRetroDiffusionApiKey);
        var oldModel = Environment.GetEnvironmentVariable(AiImageAssetService.EnvRetroDiffusionModel);
        var oldProvider = Environment.GetEnvironmentVariable(AiImageAssetService.EnvPixelProvider);

        try
        {
            Environment.SetEnvironmentVariable(AiImageAssetService.EnvRetroDiffusionBaseUrl, fakeServer.BaseUrl);
            Environment.SetEnvironmentVariable(AiImageAssetService.EnvRetroDiffusionApiKey, "fake-test-token");
            Environment.SetEnvironmentVariable(AiImageAssetService.EnvRetroDiffusionModel, "FAKE_RD_MODEL");
            Environment.SetEnvironmentVariable(AiImageAssetService.EnvPixelProvider, null);

            var project = new CczProject
            {
                WorkspaceRoot = workspaceRoot,
                GameRoot = gameRoot,
                HexTableXmlPath = Path.Combine(workspaceRoot, "工具整合包", "CCZModStudio", "Assets", "Data", "HexTables.xml")
            };

            var result = new RsPixelCharacterDesignService().Build(project, new RsPixelCharacterDesignRequest
            {
                PackageId = "Smoke_MCP_FakeRetro_SingleSpearCavalry",
                DisplayName = "Fake RetroDiffusion R/S workflow smoke",
                UnitType = "spear_cavalry",
                DesignImagePath = designImage,
                FormatActionImagePath = formatImage,
                CharacterBrief = "Sun Ce identity smoke reference; black-and-gold armor, crown, cloak, mounted CCZ 6.5 sprite.",
                WeaponBrief = "Only one long spear or lance; no sword or second weapon.",
                ForbiddenReadings = "No sword, no short blade, no second spear, no dual weapon, no white sword arc.",
                GenerateNow = true,
                DryRun = false,
                RImageId = 100,
                SImageId = 64
            });

            if (result.GenerationStatus != "generated_pending_visual_acceptance")
            {
                throw new InvalidOperationException("Unexpected generation status: " + result.GenerationStatus);
            }

            if (fakeServer.RequestCount != 2)
            {
                throw new InvalidOperationException("Expected exactly 2 fake RetroDiffusion calls, got " + fakeServer.RequestCount + ".");
            }

            if (fakeServer.Requests.Any(request => request.ReferenceImageCount != 2))
            {
                throw new InvalidOperationException("Every RetroDiffusion request must include exactly 2 reference_images.");
            }

            var packageRoot = result.PackageRoot;
            var materials = new[]
            {
                RequireMaterial(packageRoot, "materials/r_actor/front.bmp", 48, 1280),
                RequireMaterial(packageRoot, "materials/r_actor/back.bmp", 48, 1280),
                RequireMaterial(packageRoot, "materials/s_unit/mov.bmp", 48, 528),
                RequireMaterial(packageRoot, "materials/s_unit/atk.bmp", 64, 768),
                RequireMaterial(packageRoot, "materials/s_unit/spc.bmp", 48, 240)
            };

            AssertDistinctFrames(materials[0].Path, 48, 64, 20, "front");
            AssertDistinctFrames(materials[1].Path, 48, 64, 20, "back");
            if (materials[0].Sha256.Equals(materials[1].Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Front and back material strips are identical.");
            }

            Console.WriteLine(JsonSerializer.Serialize(new
            {
                result.PackageId,
                result.GenerationStatus,
                result.PackageRoot,
                FakeRetroRequests = fakeServer.Requests,
                Materials = materials,
                SafetyNote = "This smoke uses a fake local RetroDiffusion server to verify MCP reference-image generation and post-processing only. The generated files are not Sun Ce final art and must not enter the sample index."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            Environment.SetEnvironmentVariable(AiImageAssetService.EnvRetroDiffusionBaseUrl, oldBaseUrl);
            Environment.SetEnvironmentVariable(AiImageAssetService.EnvRetroDiffusionApiKey, oldApiKey);
            Environment.SetEnvironmentVariable(AiImageAssetService.EnvRetroDiffusionModel, oldModel);
            Environment.SetEnvironmentVariable(AiImageAssetService.EnvPixelProvider, oldProvider);
        }
    }

    private static MaterialRecord RequireMaterial(string packageRoot, string relativePath, int width, int height)
    {
        var path = Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Required material was not generated.", path);
        }

        using var image = new Bitmap(path);
        if (image.Width != width || image.Height != height)
        {
            throw new InvalidOperationException($"{relativePath} dimensions mismatch: {image.Width}x{image.Height}, expected {width}x{height}.");
        }

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 2 || bytes[0] != (byte)'B' || bytes[1] != (byte)'M')
        {
            throw new InvalidOperationException(relativePath + " is not a BMP file.");
        }

        return new MaterialRecord(relativePath, path, width, height, Convert.ToHexString(SHA256.HashData(bytes)));
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

    private static void CreateReferenceImage(string path, Color primary, Color accent)
    {
        using var bitmap = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(247, 0, 255));
        using var primaryBrush = new SolidBrush(primary);
        using var accentBrush = new SolidBrush(accent);
        graphics.FillRectangle(primaryBrush, 72, 60, 112, 152);
        graphics.FillRectangle(accentBrush, 104, 32, 48, 40);
        graphics.FillRectangle(accentBrush, 48, 176, 160, 14);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bitmap.Save(path, ImageFormat.Png);
    }

    private sealed class FakeRetroDiffusionServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly string _root;
        private Task? _task;
        private int _requestCount;

        public FakeRetroDiffusionServer(string root)
        {
            _root = root;
            var port = 18765 + Random.Shared.Next(0, 1000);
            BaseUrl = $"http://127.0.0.1:{port}";
            _listener.Prefixes.Add(BaseUrl + "/");
        }

        public string BaseUrl { get; }
        public int RequestCount => _requestCount;
        public List<RequestRecord> Requests { get; } = [];

        public void Start()
        {
            _listener.Start();
            _task = Task.Run(ListenAsync);
        }

        private async Task ListenAsync()
        {
            while (!_cts.IsCancellationRequested && _listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch when (_cts.IsCancellationRequested)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (HttpListenerException)
                {
                    return;
                }

                _ = Task.Run(() => HandleAsync(context), _cts.Token);
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            if (context.Request.Url?.AbsolutePath.Equals("/v1/inferences", StringComparison.OrdinalIgnoreCase) != true)
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                return;
            }

            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
            var body = await reader.ReadToEndAsync().ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);
            var prompt = document.RootElement.TryGetProperty("prompt", out var promptElement)
                ? promptElement.GetString() ?? string.Empty
                : string.Empty;
            var referenceCount = document.RootElement.TryGetProperty("reference_images", out var refsElement) && refsElement.ValueKind == JsonValueKind.Array
                ? refsElement.GetArrayLength()
                : 0;
            var callIndex = Interlocked.Increment(ref _requestCount);
            var isRActor = prompt.Contains("R-actor", StringComparison.OrdinalIgnoreCase)
                           || prompt.Contains("2 columns x 20 rows", StringComparison.OrdinalIgnoreCase)
                           || prompt.Contains("2 列", StringComparison.OrdinalIgnoreCase);
            var sourcePath = Path.Combine(_root, isRActor ? $"fake_r_source_{callIndex}.png" : $"fake_s_source_{callIndex}.png");
            if (isRActor)
            {
                CreateRActorSourceSheet(sourcePath);
            }
            else
            {
                CreateSUnitSourceSheet(sourcePath);
            }

            Requests.Add(new RequestRecord(callIndex, isRActor ? "r_actor" : "s_unit", referenceCount));
            var imageB64 = Convert.ToBase64String(await File.ReadAllBytesAsync(sourcePath).ConfigureAwait(false));
            var responseBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { image_b64 = imageB64 }));
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = responseBytes.Length;
            await context.Response.OutputStream.WriteAsync(responseBytes).ConfigureAwait(false);
            context.Response.Close();
        }

        public void Dispose()
        {
            _cts.Cancel();
            if (_listener.IsListening)
            {
                _listener.Stop();
            }

            _listener.Close();
            try
            {
                _task?.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Best effort shutdown for smoke-test local listener.
            }

            _cts.Dispose();
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
            DrawCell(graphics, 0, row, cellWidth, cellHeight, Color.FromArgb(30 + row * 5 % 170, 30, 95), row, spearDirection: 1);
            DrawCell(graphics, 1, row, cellWidth, cellHeight, Color.FromArgb(95, 45 + row * 4 % 130, 25), row + 50, spearDirection: -1);
        }

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
                DrawCell(graphics, column, row, cellWidth, cellHeight, Color.FromArgb(35 + index * 4 % 160, 35, 110 + index * 3 % 110), index, spearDirection: column % 2 == 0 ? 1 : -1);
            }
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    private static void DrawCell(Graphics graphics, int column, int row, int cellWidth, int cellHeight, Color body, int seed, int spearDirection)
    {
        var x = column * cellWidth;
        var y = row * cellHeight;
        using var outline = new SolidBrush(Color.FromArgb(15, 15, 18));
        using var bodyBrush = new SolidBrush(body);
        using var gold = new SolidBrush(Color.FromArgb(230, 190, 70));
        using var skin = new SolidBrush(Color.FromArgb(190, 135, 82));
        using var spear = new Pen(Color.FromArgb(218, 205, 165), 2);
        using var spearTip = new SolidBrush(Color.White);
        var offset = seed % 7 - 3;
        graphics.FillRectangle(outline, x + cellWidth / 2 - 12 + offset, y + 16, 24, 31);
        graphics.FillRectangle(bodyBrush, x + cellWidth / 2 - 9 + offset, y + 20, 18, 24);
        graphics.FillRectangle(skin, x + cellWidth / 2 - 5 + offset, y + 10, 10, 8);
        graphics.FillRectangle(gold, x + cellWidth / 2 - 8 + offset, y + 6, 16, 5);
        graphics.FillEllipse(outline, x + cellWidth / 2 - 14 + offset, y + 43, 28, 10);
        var y0 = y + 18 + seed % 9;
        var x0 = x + cellWidth / 2 - spearDirection * 14 + offset;
        var x1 = x + cellWidth / 2 + spearDirection * (cellWidth / 2 - 5);
        var y1 = y + 7 + seed % 5;
        graphics.DrawLine(spear, x0, y0, x1, y1);
        graphics.FillRectangle(spearTip, x1 - 2, y1 - 2, 4, 4);
    }

    private sealed record RequestRecord(int Index, string Kind, int ReferenceImageCount);
    private sealed record MaterialRecord(string Role, string Path, int Width, int Height, string Sha256);
}
