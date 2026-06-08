using System.Data;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.McpServer;

public sealed partial class CczMcpRuntime
{
    public object ListEffects(string? gameRoot, string domain, string? keyword, int limit)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        var effectiveLimit = NormalizeLimit(limit, 100, 1000);
        var effects = _effectPackageService.ListEffects(project, tables, domain, keyword, effectiveLimit);
        return new
        {
            project.GameRoot,
            Domain = domain,
            Keyword = keyword ?? string.Empty,
            ReturnedEffects = effects.Count,
            Effects = effects,
            SafetyNote = "Read-only effect catalog. Use preview_effect_package before apply_effect_package."
        };
    }

    public object ReadEffect(string? gameRoot, string domain, int effectId)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        return new
        {
            project.GameRoot,
            Effect = _effectPackageService.ReadEffect(project, tables, domain, effectId)
        };
    }

    public object ExportEffectPackage(string? gameRoot, string domain, int effectId)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        return new
        {
            project.GameRoot,
            Package = _effectPackageService.ExportPackage(project, tables, domain, effectId),
            SafetyNote = "Exported packages can be passed to preview_effect_package/apply_effect_package."
        };
    }

    public object PreviewEffectPackage(string? gameRoot, EffectPackage package, string? mode)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        return new
        {
            project.GameRoot,
            Preview = _effectPackageService.Preview(project, tables, package, mode ?? "import")
        };
    }

    public object ApplyEffectPackage(string? gameRoot, EffectPackage package, string? mode, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        var tables = LoadTables(project);
        return new
        {
            project.GameRoot,
            Result = _effectPackageService.Apply(project, tables, package, mode ?? "import"),
            SafetyNote = "Effect package writes use table/catalog services with backups, reports, and a manifest under CCZModStudio_Notes/EffectManifests."
        };
    }

    public object ListEffectTemplates()
        => new
        {
            Templates = _effectPackageService.ListTemplates(),
            SafetyNote = "Templates are declarative. Patch templates are draft-only unless explicit bytes are provided and preview_effect_patch passes."
        };

    public object BuildEffectPackageFromTemplate(string templateId, Dictionary<string, string>? parameters)
        => new
        {
            Package = _effectPackageService.BuildPackageFromTemplate(templateId, parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            SafetyNote = "Generated packages are drafts. Always preview before applying."
        };

    public object PreviewEffectPatch(string? gameRoot, EffectPackage package)
    {
        var project = LoadProject(gameRoot);
        return new
        {
            project.GameRoot,
            Preview = _effectPackageService.PreviewPatch(project, package)
        };
    }

    public object ApplyEffectPatch(string? gameRoot, EffectPackage package, string? writeMode)
    {
        var project = LoadProject(gameRoot);
        EnsureWriteMode(project, writeMode);
        return new
        {
            project.GameRoot,
            Result = _effectPackageService.ApplyPatch(project, package),
            SafetyNote = "Patch writes use PatchApplyService. A manifest records the generated backup files for manual recovery."
        };
    }

    public object ReadEffectResource(string? gameRoot, string uri)
    {
        var project = LoadProject(gameRoot);
        var tables = LoadTables(project);
        uri = (uri ?? string.Empty).Trim();
        if (uri.Equals("ccz://effects/schema", StringComparison.OrdinalIgnoreCase))
        {
            return _effectPackageService.GetSchemaResource();
        }

        if (uri.StartsWith("ccz://effects/catalog/", StringComparison.OrdinalIgnoreCase))
        {
            var domain = uri["ccz://effects/catalog/".Length..];
            return new
            {
                Uri = uri,
                Effects = _effectPackageService.ListEffects(project, tables, domain, null, 1000)
            };
        }

        if (uri.Equals("ccz://effects/templates", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                Uri = uri,
                Templates = _effectPackageService.ListTemplates()
            };
        }

        if (uri.Equals("ccz://effects/manifests", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                Uri = uri,
                Effects = _effectPackageService.ListEffects(project, tables, "patch", null, 1000)
            };
        }

        if (uri.Equals("ccz://knowledge/effects", StringComparison.OrdinalIgnoreCase))
        {
            return SearchKnowledgeEntries("特效", 80, 1);
        }

        throw new InvalidOperationException("Unsupported effect resource URI: " + uri);
    }

    public object ReadEffectPrompt(string name)
    {
        name = (name ?? string.Empty).Trim();
        var prompts = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["make_ccz_effect"] = new
            {
                Name = "make_ccz_effect",
                Description = "把自然语言特效需求转为 EffectPackage 草案。",
                Instructions = "先调用 search_knowledge_entries/list_effects/list_effect_templates；优先生成配置级包。需要新逻辑时只使用白名单模板或生成 patch draft，并要求 preview_effect_package/preview_effect_patch。"
            },
            ["import_ccz_effect"] = new
            {
                Name = "import_ccz_effect",
                Description = "导入外部 EffectPackage JSON 或补丁段。",
                Instructions = "校验 domain/effect_id/bindings/patch_segments；调用 preview_effect_package；只有预览通过后才能 apply_effect_package。"
            },
            ["delete_ccz_effect"] = new
            {
                Name = "delete_ccz_effect",
                Description = "删除或禁用特效并处理引用。",
                Instructions = "固定行表不缩表：宝物清特效号和值，兵种清武将=1024/兵种=255/值=0，个人/套装行清 0；补丁删除不提供自动入口，按 manifest 里的备份文件手动处理。"
            }
        };

        if (prompts.TryGetValue(name, out var prompt)) return prompt;
        return new
        {
            Prompts = prompts.Values
        };
    }
}
