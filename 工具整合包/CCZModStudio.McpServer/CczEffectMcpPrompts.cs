using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using ModelContextProtocol.Server;

namespace CCZModStudio.McpServer;

[McpServerPromptType]
public sealed class CczEffectMcpPrompts(CczMcpRuntime runtime)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    [McpServerPrompt(Name = "make_ccz_effect", Title = "Make CCZ EffectPackage")]
    [Description("Turn a natural-language CCZ 6.5 effect request into a safe EffectPackage draft.")]
    public string MakeCczEffect(
        [Description("Natural-language request for the effect to make.")]
        string request)
        => BuildPrompt("make_ccz_effect", request);

    [McpServerPrompt(Name = "import_ccz_effect", Title = "Import CCZ EffectPackage")]
    [Description("Import an external EffectPackage JSON or patch segment with preview-first safety rules.")]
    public string ImportCczEffect(
        [Description("External EffectPackage JSON, patch text, or import request.")]
        string input)
        => BuildPrompt("import_ccz_effect", input);

    [McpServerPrompt(Name = "delete_ccz_effect", Title = "Delete CCZ Effect")]
    [Description("Disable or delete a fixed-slot CCZ 6.5 effect while clearing references safely.")]
    public string DeleteCczEffect(
        [Description("Effect domain/id and delete intent.")]
        string request)
        => BuildPrompt("delete_ccz_effect", request);

    private string BuildPrompt(string name, string userInput)
    {
        var prompt = runtime.ReadEffectPrompt(name);
        return string.Join(Environment.NewLine, new[]
        {
            "Use the following CCZModStudio effect prompt instructions.",
            JsonSerializer.Serialize(prompt, JsonOptions),
            string.Empty,
            "User input:",
            userInput ?? string.Empty,
            string.Empty,
            "Required workflow:",
            "1. Read ccz://effects/schema and ccz://knowledge/effects.",
            "2. Use list_effects/list_effect_templates before creating or changing a package.",
            "3. Prefer item/job/personal configuration packages. Use patch packages only when existing effect ids cannot express the request.",
            "4. Always call preview_effect_package or preview_effect_patch before apply_effect_package/apply_effect_patch.",
            "5. For patch bytes, require address mapping, expected old bytes, conflict checks, manifest output, and rollback plan."
        });
    }
}
