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
    [Description("Guide a local agent to turn a CCZ 6.5 effect request into a safe EffectPackage or injectable patch draft.")]
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
            "Use the following CCZModStudio effect instructions. The local agent generates drafts; CCZModStudio validates, previews, applies, backs up, and reports.",
            JsonSerializer.Serialize(prompt, JsonOptions),
            string.Empty,
            "User input:",
            userInput ?? string.Empty,
            string.Empty,
            "Required workflow:",
            "1. Read ccz://effects/schema, ccz://knowledge/effects, and ccz://knowledge/effects/agent-special-effect.",
            "2. Call list_effect_modules and prefer a validated ModularCompositeEffectBlueprint when the request can be expressed by verified modules.",
            "3. Use scan_effect_id_locations for summaries, then read_effect_id_location only for the selected LocationId; do not load every diagnostic location into context.",
            "4. Prefer item/job/personal configuration packages. Use patch packages only when existing effect ids cannot express the request.",
            "5. For modular composites, call validate_effect_blueprint then preview_modular_effect. For custom assembly, call preview_assembly_patch.",
            "6. Apply only a compiled EffectPackage returned by a successful preview tool.",
            "7. For patch bytes, require address mapping, exact operand provenance, expected old bytes, conflict checks, manifest output, and manual recovery notes."
        });
    }
}
