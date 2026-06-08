using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using ModelContextProtocol.Server;

namespace CCZModStudio.McpServer;

[McpServerResourceType]
public sealed class CczEffectMcpResources(CczMcpRuntime runtime)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    [McpServerResource(UriTemplate = "ccz://effects/schema", Name = "ccz_effects_schema", Title = "CCZ EffectPackage schema", MimeType = "application/json")]
    [Description("EffectPackage schema and fixed delete semantics for CCZ 6.5 effect management.")]
    public string ReadEffectSchema()
        => ToJson(runtime.ReadEffectResource(null, "ccz://effects/schema"));

    [McpServerResource(UriTemplate = "ccz://effects/catalog/{domain}", Name = "ccz_effects_catalog", Title = "CCZ effect catalog", MimeType = "application/json")]
    [Description("Catalog resource for item, job, personal, or patch effects.")]
    public string ReadEffectCatalog(string domain)
        => ToJson(runtime.ReadEffectResource(null, "ccz://effects/catalog/" + domain));

    [McpServerResource(UriTemplate = "ccz://effects/templates", Name = "ccz_effects_templates", Title = "CCZ effect templates", MimeType = "application/json")]
    [Description("Declarative templates used by AI to draft EffectPackage objects.")]
    public string ReadEffectTemplates()
        => ToJson(runtime.ReadEffectResource(null, "ccz://effects/templates"));

    [McpServerResource(UriTemplate = "ccz://effects/manifests", Name = "ccz_effects_manifests", Title = "CCZ effect manifests", MimeType = "application/json")]
    [Description("Effect write manifests available for review and manual recovery.")]
    public string ReadEffectManifests()
        => ToJson(runtime.ReadEffectResource(null, "ccz://effects/manifests"));

    [McpServerResource(UriTemplate = "ccz://knowledge/effects", Name = "ccz_effects_knowledge", Title = "CCZ effect knowledge", MimeType = "application/json")]
    [Description("Local knowledge-base search results for effect creation and 6.5 constraints.")]
    public string ReadEffectKnowledge()
        => ToJson(runtime.ReadEffectResource(null, "ccz://knowledge/effects"));

    private static string ToJson(object value)
        => JsonSerializer.Serialize(value, JsonOptions);
}
