using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CCZModStudio.McpServer;
using CCZModStudio.Models;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var options = ParseArgs(args);
if (!options.TryGetValue("action", out var action) ||
    !options.TryGetValue("game-root", out var gameRoot) ||
    !options.TryGetValue("package", out var packagePath))
{
    Console.Error.WriteLine("Usage: --action <preview|validate|auto-validate|apply|export-report> --game-root <path> --package <json> [--run-smokes true|false] [--report-kind name]");
    return 2;
}

var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};

var packageJson = await File.ReadAllTextAsync(packagePath, Encoding.UTF8);
var package = JsonSerializer.Deserialize<ModPackage>(packageJson, jsonOptions)
              ?? throw new InvalidOperationException("ModPackage JSON could not be parsed.");

var runtime = new CczMcpRuntime();
var result = action.ToLowerInvariant() switch
{
    "preview" => runtime.PreviewModPackage(gameRoot, package, "direct"),
    "validate" => runtime.ValidateModPackage(gameRoot, package, ParseBool(options.GetValueOrDefault("run-smokes"), defaultValue: false)),
    "auto-validate" => runtime.AutoValidateMod(gameRoot, package, ParseBool(options.GetValueOrDefault("run-smokes"), defaultValue: true)),
    "apply" => runtime.ApplyModPackage(gameRoot, package, "direct", "direct"),
    "apply-test-copy" => runtime.ApplyModPackage(gameRoot, package, "direct", "direct"),
    "promote" => runtime.PromoteTestCopyMod(gameRoot, package, confirmPromote: true),
    "export-report" => runtime.ExportModReport(gameRoot, package, options.GetValueOrDefault("report-kind") ?? "preview"),
    _ => throw new InvalidOperationException($"Unknown action: {action}")
};

Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
return 0;

static Dictionary<string, string> ParseArgs(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        var key = args[i];
        if (!key.StartsWith("--", StringComparison.Ordinal)) continue;
        if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            result[key[2..]] = "true";
            continue;
        }

        result[key[2..]] = args[++i];
    }

    return result;
}

static bool ParseBool(string? value, bool defaultValue)
{
    if (string.IsNullOrWhiteSpace(value)) return defaultValue;
    return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
}
