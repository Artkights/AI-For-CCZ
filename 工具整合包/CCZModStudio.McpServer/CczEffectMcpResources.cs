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
    [Description("Declarative templates used by local agents to draft EffectPackage objects.")]
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

    [McpServerResource(UriTemplate = "ccz://knowledge/effects/agent-special-effect", Name = "ccz_agent_special_effect_knowledge", Title = "CCZ local-agent special effect knowledge", MimeType = "application/json")]
    [Description("Compact semantic knowledge pack for local agents that generate injectable special-effect drafts.")]
    public string ReadAgentSpecialEffectKnowledge()
        => ToJson(runtime.ReadEffectResource(null, "ccz://knowledge/effects/agent-special-effect"));

    [McpServerResource(UriTemplate = "ccz://effects/installed/{instanceId}", Name = "ccz_installed_effect", Title = "CCZ installed logical effect", MimeType = "application/json")]
    [Description("One aggregated injected or native logical effect instance.")]
    public string ReadInstalledEffect(string instanceId)
        => ToJson(runtime.ReadEffectResource(null, "ccz://effects/installed/" + instanceId));

    [McpServerResource(UriTemplate = "ccz://knowledge/effects/address/{address}", Name = "ccz_effect_address_semantics", Title = "CCZ executable address semantics", MimeType = "application/json")]
    [Description("PE/x86 semantic explanation for one hexadecimal virtual address.")]
    public string ReadAddressSemantics(string address)
        => ToJson(runtime.ReadEffectResource(null, "ccz://knowledge/effects/address/" + address));

    [McpServerResource(UriTemplate = "ccz://effects/locations/{locationId}", Name = "ccz_effect_id_location", Title = "CCZ effect id physical location", MimeType = "application/json")]
    [Description("One physical effect-id location with encoding, provenance and write capability.")]
    public string ReadEffectIdLocation(string locationId)
        => ToJson(runtime.ReadEffectIdLocation(null, locationId, "Ekd5.exe"));

    [McpServerResource(UriTemplate = "ccz://effects/modules/{moduleId}", Name = "ccz_effect_module", Title = "CCZ typed effect module", MimeType = "application/json")]
    [Description("One typed effect module with context, contract, value range and authoring availability.")]
    public string ReadEffectModule(string moduleId)
        => ToJson(runtime.ReadEffectModule(null, moduleId));

    [McpServerResource(UriTemplate = "ccz://effects/contracts/{contractId}", Name = "ccz_effect_execution_contract", Title = "CCZ effect execution contract", MimeType = "application/json")]
    [Description("One SHA-bound effect execution contract with context slots and verification state.")]
    public string ReadEffectExecutionContract(string contractId)
        => ToJson(runtime.ReadHookExecutionContract(null, contractId));

    [McpServerResource(UriTemplate = "ccz://effects/contracts/{contractId}/probe-plan", Name = "ccz_effect_contract_probe_plan", Title = "CCZ effect contract probe plan", MimeType = "application/json")]
    [Description("Read-only GameDebug/x32dbg evidence capture plan for one execution contract.")]
    public string ReadEffectContractProbePlan(string contractId)
        => ToJson(runtime.CreateEffectContractProbePlan(null, contractId));

    private static string ToJson(object value)
        => JsonSerializer.Serialize(value, JsonOptions);
}
