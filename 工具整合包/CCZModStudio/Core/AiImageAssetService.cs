using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class AiImageAssetService
{
    public const string EnvBaseUrl = "IMAGE_STUDIO_BASE_URL";
    public const string EnvApiKey = "IMAGE_STUDIO_API_KEY";
    public const string EnvTextModel = "IMAGE_STUDIO_TEXT_MODEL";
    public const string EnvImageModel = "IMAGE_STUDIO_IMAGE_MODEL";
    public const string EnvApiMode = "IMAGE_STUDIO_API_MODE";
    public const string EnvPixelProvider = "CCZ_PIXEL_IMAGE_PROVIDER";
    public const string EnvRetroDiffusionBaseUrl = "RETRO_DIFFUSION_BASE_URL";
    public const string EnvRetroDiffusionApiKey = "RETRO_DIFFUSION_API_KEY";
    public const string EnvRetroDiffusionModel = "RETRO_DIFFUSION_MODEL";
    private const string EnvUpstreamBaseUrlAlias = "IMAGE_STUDIO_UPSTREAM_BASE_URL";
    private const string EnvTextModelIdAlias = "IMAGE_STUDIO_TEXT_MODEL_ID";
    private const string EnvImageModelIdAlias = "IMAGE_STUDIO_IMAGE_MODEL_ID";
    private const string ProviderImageStudio = "image_studio";
    private const string ProviderRetroDiffusion = "retrodiffusion";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private static readonly int[] SUnitMoveFrameOrder = [0, 1, 4, 5, 8, 9, 12, 16, 20, 2, 6];
    private static readonly int[] SUnitAttackFrameOrder = [12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23];
    private static readonly int[] SUnitSpecialFrameOrder = [2, 3, 6, 7, 11];

    private static readonly IReadOnlyList<AiImageAssetPreset> Presets =
    [
        new AiImageAssetPreset
        {
            Key = "r_background",
            DisplayName = "R背景",
            Category = "剧情背景",
            TargetKind = "e5",
            DefaultTargetRelativePath = "E5/Mmap.e5",
            DefaultWidth = 640,
            DefaultHeight = 400,
            OutputFormat = "jpg",
            GenerationSize = "1536x1024",
            Quality = "high",
            Foreground = false,
            NumberingRule = "Mmap.e5 6.5 样本 385 张，E5 图号为 1-based；目标条目存在时优先读取目标条目实际尺寸。",
            PostProcessRule = "居中裁切到目标比例，缩放到目标尺寸，保存 JPG。",
            PreviewTool = "preview_e5_image_replace",
            SafetyNote = "R 背景属于 E5/Mmap.e5，不是 Map/Mxxx.jpg 战场底图；本工具只生成并预览替换。"
        },
        new AiImageAssetPreset
        {
            Key = "dll_icon",
            DisplayName = "DLL图标",
            Category = "图标",
            TargetKind = "dll_icon",
            DefaultTargetRelativePath = "Itemicon.dll",
            DefaultWidth = 32,
            DefaultHeight = 32,
            OutputFormat = "png",
            GenerationSize = "1024x1024",
            Quality = "high",
            Foreground = true,
            NumberingRule = "Itemicon.dll/Mgcicon.dll/Cmdicon.dll 图标编号为 0-based，按 RT_BITMAP 成对资源预览。",
            PostProcessRule = "居中缩放到正方形画布，尝试将透明或纯洋红背景转为透明 PNG。",
            PreviewTool = "preview_dll_icon_replace",
            SafetyNote = "6.5 样本图标是 DLL RT_BITMAP，不按标准 .ico 图标组写入；本工具只生成并预览替换。"
        },
        new AiImageAssetPreset
        {
            Key = "face",
            DisplayName = "角色头像",
            Category = "人物头像",
            TargetKind = "e5",
            DefaultTargetRelativePath = "E5/Face.e5",
            DefaultWidth = 120,
            DefaultHeight = 120,
            OutputFormat = "png",
            GenerationSize = "1024x1024",
            Quality = "high",
            Foreground = true,
            NumberingRule = "Face.e5 图号为 1-based；Data 头像号 0 对应 #1-#8，Data 头像号 n>=1 对应 Face.e5 #(n+8)。",
            PostProcessRule = "头像居中裁切到正方形，缩放到目标尺寸，保存 PNG。",
            PreviewTool = "preview_e5_image_replace",
            SafetyNote = "默认只处理 Face.e5 小头像；Tou.dll 真彩头像不在 v1 自动写入范围内。"
        },
        new AiImageAssetPreset
        {
            Key = "r_actor",
            DisplayName = "角色R形象",
            Category = "剧情人物形象",
            TargetKind = "e5",
            DefaultTargetRelativePath = "Pmapobj.e5",
            DefaultWidth = 48,
            DefaultHeight = 1280,
            OutputFormat = "png",
            GenerationSize = "1024x1536",
            Quality = "high",
            Foreground = true,
            NumberingRule = "R=n 对应 Pmapobj.e5 正面图 2n+1、反面图 2n+2，E5 图号为 1-based。",
            PostProcessRule = "要求上游输出 2 列 x 20 行 R 正背动作表：左列 front、右列 back；后处理只裁切缩放为两条 48x1280 条带，不复制单帧补动作。",
            PreviewTool = "preview_e5_image_batch_replace",
            SafetyNote = "R 形象必须来自真实正背 20 帧动作表；本工具不会把单张立绘伪造成 20 帧成品。"
        },
        new AiImageAssetPreset
        {
            Key = "s_unit",
            DisplayName = "角色S形象",
            Category = "战场人物形象",
            TargetKind = "e5_batch",
            DefaultTargetRelativePath = "Unit_mov.e5",
            DefaultWidth = 48,
            DefaultHeight = 528,
            OutputFormat = "png",
            GenerationSize = "1024x1536",
            Quality = "high",
            Foreground = true,
            NumberingRule = "S=0 用职业和阵营映射默认 Unit 图号；S=1..32 映射 240+(S-1)*3+1/2/3；S>=33 映射 336+(S-32)。",
            PostProcessRule = "v2 要求上游输出 4x6、共 24 格动作表；后处理按格切帧并导出移动 48x528、攻击 64x768、特技 48x240。",
            PreviewTool = "preview_e5_image_batch_replace",
            SafetyNote = "S 动作表会生成不同姿态帧并预览替换；Unit_mov/atk/spc 的精确实机帧序仍需逐项验证。"
        }
    ];

    public IReadOnlyList<AiImageAssetPreset> ListPresets() => Presets;

    public AiImagePromptPlan BuildPromptPlan(
        CczProject project,
        string presetKey,
        string description,
        string? targetRelativePath,
        int? imageNumber,
        int? rImageId,
        int? sImageId,
        int? faceId,
        int? jobId,
        int factionSlot,
        string? outputFormat,
        int? width,
        int? height,
        IReadOnlyList<string>? referenceImagePaths = null,
        IReadOnlyList<string>? referenceRoles = null)
    {
        var basePreset = FindPreset(presetKey);
        var target = NormalizeTargetPath(project, basePreset, targetRelativePath);
        var preset = ResolveEffectivePreset(project, basePreset, target);
        var targetNumbers = ResolveTargetImageNumbers(project, preset, imageNumber, rImageId, sImageId, faceId, jobId, factionSlot);
        var dimensions = ResolveTargetDimensions(project, preset, target, targetNumbers, imageNumber.HasValue, width, height);
        var format = NormalizeOutputFormat(outputFormat, preset.OutputFormat);
        var mapping = BuildMappingSummary(project, preset, targetNumbers, rImageId, sImageId, faceId, jobId, factionSlot);
        var warnings = BuildPlanWarnings(project, preset, target, targetNumbers, dimensions.Width, dimensions.Height).ToArray();
        var prompt = BuildPrompt(preset, description, dimensions.Width, dimensions.Height, format, mapping);
        var negative = BuildNegativePrompt(preset);
        var references = ResolveReferenceImages(project, referenceImagePaths, referenceRoles);

        return new AiImagePromptPlan
        {
            Preset = preset,
            Description = description.Trim(),
            Prompt = prompt,
            NegativePrompt = negative,
            TargetRelativePath = target,
            TargetImageNumbers = targetNumbers,
            TargetWidth = dimensions.Width,
            TargetHeight = dimensions.Height,
            OutputFormat = format,
            GenerationSize = preset.GenerationSize,
            Quality = preset.Quality,
            MappingSummary = mapping,
            Warnings = warnings,
            ReferenceImages = references
        };
    }

    public AiImagePrepareResult PrepareExistingImage(
        CczProject project,
        AiImagePromptPlan plan,
        string sourcePath,
        Func<AiImagePromptPlan, string, object?>? previewFactory = null)
    {
        var resolvedSource = Path.GetFullPath(sourcePath);
        if (!File.Exists(resolvedSource))
        {
            throw new FileNotFoundException("来源图片不存在。", resolvedSource);
        }

        var exportRoot = GetExportRoot(project);
        Directory.CreateDirectory(exportRoot);
        var preparedFiles = PrepareFiles(project, plan, resolvedSource, previewFactory);
        var primary = preparedFiles.First();
        var outputPath = primary.OutputPath;
        var post = BuildPostProcessInfo(resolvedSource, preparedFiles);
        var sourceBytes = File.ReadAllBytes(resolvedSource);
        var outputBytes = File.ReadAllBytes(outputPath);
        var preview = primary.ReplacementPreview;
        if (Is66ItemIconPairPlan(plan))
        {
            preview = preparedFiles.Select(file => file.ReplacementPreview).ToArray();
        }
        var manifestPath = WriteManifest(project, plan, resolvedSource, outputPath, post, preparedFiles);

        return new AiImagePrepareResult
        {
            Plan = plan,
            SourcePath = resolvedSource,
            OutputPath = outputPath,
            ManifestPath = manifestPath,
            SourceWidth = post.SourceWidth,
            SourceHeight = post.SourceHeight,
            OutputWidth = post.OutputWidth,
            OutputHeight = post.OutputHeight,
            OutputFormat = plan.OutputFormat,
            SourceSha256 = ComputeSha256(sourceBytes),
            OutputSha256 = ComputeSha256(outputBytes),
            PostProcessSummary = post.Summary,
            ReplacementPreview = preview,
            PreparedFiles = preparedFiles
        };
    }

    public async Task<AiImageDrawResult> DrawAsync(
        CczProject project,
        AiImagePromptPlan plan,
        bool dryRun,
        Func<AiImagePromptPlan, string, object?>? previewFactory = null,
        CancellationToken cancellationToken = default)
    {
        var config = ReadUpstreamConfig(plan.Preset);
        if (dryRun)
        {
            return new AiImageDrawResult
            {
                DryRun = true,
                Plan = plan,
                Provider = config.Provider,
                ApiMode = config.ApiMode,
                BaseUrl = MaskBaseUrl(config.BaseUrl),
                TextModel = config.TextModel,
                ImageModel = config.ImageModel,
                Logs =
                [
                    "dry_run=true：只返回提示词和目标计划，不调用上游。",
                    $"provider={config.Provider}；R/S 形象默认使用像素专用上游，其他素材默认使用 Image Studio。"
                ]
            };
        }

        if (string.IsNullOrWhiteSpace(config.BaseUrl) || string.IsNullOrWhiteSpace(config.ApiKey))
        {
            throw new InvalidOperationException(config.Provider.Equals(ProviderRetroDiffusion, StringComparison.OrdinalIgnoreCase)
                ? $"{EnvRetroDiffusionBaseUrl} 和 {EnvRetroDiffusionApiKey} 必须配置后才能用 RetroDiffusion 生成 R/S 形象；如需临时回退 Image Studio，可设置 {EnvPixelProvider}=image_studio。"
                : $"{EnvBaseUrl} 和 {EnvApiKey} 必须配置后才能调用 draw_ccz_image_asset。");
        }

        var exportRoot = GetExportRoot(project);
        var rawRoot = Path.Combine(exportRoot, "raw");
        Directory.CreateDirectory(rawRoot);
        var generatedRoot = Path.Combine(exportRoot, "generated");
        Directory.CreateDirectory(generatedRoot);

        var request = BuildImageRequest(config, plan);
        var rawPath = Path.Combine(rawRoot, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{plan.Preset.Key}_response.json");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(8) };
        using var message = new HttpRequestMessage(HttpMethod.Post, request.Url);
        if (config.Provider.Equals(ProviderRetroDiffusion, StringComparison.OrdinalIgnoreCase))
        {
            message.Headers.TryAddWithoutValidation("X-RD-Token", config.ApiKey);
        }
        else
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        }
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Content = new StringContent(request.JsonPayload, Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(rawPath, raw, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"图像上游返回 HTTP {(int)response.StatusCode}，原始响应已保存：{rawPath}");
        }

        var imageB64 = ExtractImageBase64(raw);
        if (string.IsNullOrWhiteSpace(imageB64))
        {
            throw new InvalidOperationException("图像上游响应中没有找到 b64 图片，原始响应已保存：" + rawPath);
        }

        var generatedPath = Path.Combine(generatedRoot, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{plan.Preset.Key}_source.png");
        await File.WriteAllBytesAsync(generatedPath, Convert.FromBase64String(imageB64), cancellationToken).ConfigureAwait(false);
        var prepared = PrepareExistingImage(project, plan, generatedPath, previewFactory);

        return new AiImageDrawResult
        {
            DryRun = false,
            Plan = plan,
            Provider = config.Provider,
            ApiMode = config.ApiMode,
            BaseUrl = MaskBaseUrl(config.BaseUrl),
            TextModel = config.TextModel,
            ImageModel = config.ImageModel,
            RawResponsePath = rawPath,
            GeneratedSourcePath = generatedPath,
            Prepared = prepared,
            Logs =
            [
                $"请求 {request.Url}",
                $"上游 provider={config.Provider}",
                $"原始响应：{rawPath}",
                $"生成源图：{generatedPath}",
                $"规范化输出：{prepared.OutputPath}"
            ]
        };
    }

    private static AiImageAssetPreset FindPreset(string key)
        => Presets.FirstOrDefault(x => x.Key.Equals(key.Trim(), StringComparison.OrdinalIgnoreCase))
           ?? throw new InvalidOperationException($"未知图片素材 preset：{key}。可用值：{string.Join(", ", Presets.Select(x => x.Key))}");

    private static string NormalizeTargetPath(CczProject project, AiImageAssetPreset preset, string? targetRelativePath)
        => (string.IsNullOrWhiteSpace(targetRelativePath) ? ResolveDefaultTargetPath(project, preset) : targetRelativePath.Trim())
            .Replace('\\', '/');

    private static string ResolveDefaultTargetPath(CczProject project, AiImageAssetPreset preset)
        => preset.Key == "dll_icon" && Ccz66RevisedLayout.Is66(project)
            ? "E5/Item.e5"
            : preset.DefaultTargetRelativePath;

    private static AiImageAssetPreset ResolveEffectivePreset(CczProject project, AiImageAssetPreset preset, string targetRelativePath)
    {
        if (preset.Key != "dll_icon" ||
            !Ccz66RevisedLayout.Is66(project) ||
            !Ccz66RevisedLayout.IsE5IconResource(targetRelativePath))
        {
            return preset;
        }

        var fileName = Path.GetFileName(targetRelativePath);
        var isStrategy = fileName.Equals("Mtem.e5", StringComparison.OrdinalIgnoreCase);
        return new AiImageAssetPreset
        {
            Key = preset.Key,
            DisplayName = preset.DisplayName,
            Category = preset.Category,
            TargetKind = "e5",
            DefaultTargetRelativePath = targetRelativePath,
            DefaultWidth = preset.DefaultWidth,
            DefaultHeight = preset.DefaultHeight,
            OutputFormat = preset.OutputFormat,
            GenerationSize = preset.GenerationSize,
            Quality = preset.Quality,
            Foreground = preset.Foreground,
            NumberingRule = isStrategy
                ? "6.6 revised strategy icons use E5/Mtem.e5; table field value maps to E5 image number +1, with four strategy families usually spaced by 6."
                : "6.6 revised item icons use E5/Item.e5; table field value N maps to small image #2N+1 and large image #2N+2. #1/#2 are blank small/large icon slots.",
            PostProcessRule = preset.PostProcessRule,
            PreviewTool = "preview_e5_image_replace",
            SafetyNote = isStrategy
                ? "6.6 revised Mtem.e5 icon replacement uses the normal E5 backup, report, and reread validation path."
                : "6.6 revised Item.e5 icon replacement uses the normal E5 backup, report, and reread validation path."
        };
    }

    private static IReadOnlyList<AiImageReferenceImage> ResolveReferenceImages(
        CczProject project,
        IReadOnlyList<string>? referenceImagePaths,
        IReadOnlyList<string>? referenceRoles)
    {
        if (referenceImagePaths == null || referenceImagePaths.Count == 0)
        {
            return Array.Empty<AiImageReferenceImage>();
        }

        if (referenceImagePaths.Count > 9)
        {
            throw new InvalidOperationException("reference_image_paths supports at most 9 images.");
        }

        if (referenceRoles != null && referenceRoles.Count > 0 && referenceRoles.Count != referenceImagePaths.Count)
        {
            throw new InvalidOperationException("reference_roles must be empty or have the same length as reference_image_paths.");
        }

        var result = new List<AiImageReferenceImage>();
        for (var i = 0; i < referenceImagePaths.Count; i++)
        {
            var rawPath = referenceImagePaths[i];
            if (string.IsNullOrWhiteSpace(rawPath)) continue;
            var resolved = ResolveImageReferencePath(project, rawPath);
            if (!File.Exists(resolved))
            {
                throw new FileNotFoundException("Reference image was not found.", resolved);
            }

            var ext = Path.GetExtension(resolved).ToLowerInvariant();
            if (ext is not ".png" and not ".jpg" and not ".jpeg" and not ".bmp" and not ".webp")
            {
                throw new InvalidOperationException("Unsupported reference image extension: " + resolved);
            }

            var bytes = File.ReadAllBytes(resolved);
            result.Add(new AiImageReferenceImage
            {
                Role = NormalizeReferenceRole(referenceRoles != null && referenceRoles.Count > i ? referenceRoles[i] : null, i),
                Path = resolved,
                Sha256 = ComputeSha256(bytes)
            });
        }

        return result;
    }

    private static string ResolveImageReferencePath(CczProject project, string path)
    {
        var trimmed = path.Trim().Trim('"');
        if (Path.IsPathRooted(trimmed)) return Path.GetFullPath(trimmed);

        var candidates = new[]
        {
            Path.Combine(project.GameRoot, trimmed),
            Path.Combine(Directory.GetCurrentDirectory(), trimmed)
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }

        return Path.GetFullPath(candidates[0]);
    }

    private static string NormalizeReferenceRole(string? role, int index)
    {
        var value = string.IsNullOrWhiteSpace(role)
            ? index switch
            {
                0 => "design",
                1 => "format_action",
                2 => "style_optional",
                _ => "reference_" + (index + 1).ToString(CultureInfo.InvariantCulture)
            }
            : role.Trim();
        return value.Replace('\\', '/');
    }

    private static IReadOnlyList<int> ResolveTargetImageNumbers(
        CczProject project,
        AiImageAssetPreset preset,
        int? imageNumber,
        int? rImageId,
        int? sImageId,
        int? faceId,
        int? jobId,
        int factionSlot)
    {
        if (preset.Key == "dll_icon")
        {
            var fieldValue = imageNumber ?? 0;
            if (!Ccz66RevisedLayout.Is66(project)) return [fieldValue];
            if (Ccz66RevisedLayout.IsItemIconResource(preset.DefaultTargetRelativePath))
            {
                var (small, large) = Ccz66RevisedLayout.ResolveItemIconImageNumbers(fieldValue);
                return [small, large];
            }

            return [Ccz66RevisedLayout.ResolveStrategyIconImageNumber(fieldValue)];
        }

        if (preset.Key == "r_actor")
        {
            if (imageNumber.HasValue) return [imageNumber.Value];
            var r = rImageId ?? 0;
            return [checked(r * 2 + 1), checked(r * 2 + 2)];
        }

        if (preset.Key == "s_unit")
        {
            if (imageNumber.HasValue) return [imageNumber.Value];
            var s = sImageId ?? 1;
            var mapping = CharacterImageResourceService.ResolveSUnitImageMapping(s, jobId, factionSlot);
            return mapping.ImageNumbers.Count == 0 ? [1] : mapping.ImageNumbers;
        }

        if (preset.Key == "face")
        {
            if (imageNumber.HasValue) return [imageNumber.Value];
            var mapping = new CharacterImageResourceService().MapFaceId(faceId ?? 1);
            return [mapping.FaceImageNumbers.First()];
        }

        return [imageNumber ?? 1];
    }

    private (int Width, int Height) ResolveTargetDimensions(
        CczProject project,
        AiImageAssetPreset preset,
        string targetRelativePath,
        IReadOnlyList<int> targetNumbers,
        bool preferTargetEntryDimensions,
        int? width,
        int? height)
    {
        if (width is > 0 && height is > 0) return (width.Value, height.Value);
        if (!preferTargetEntryDimensions) return (width ?? preset.DefaultWidth, height ?? preset.DefaultHeight);

        if (preset.TargetKind.StartsWith("e5", StringComparison.OrdinalIgnoreCase) && targetNumbers.Count > 0)
        {
            var targetPath = ResolveProjectFile(project, targetRelativePath, mustExist: false);
            if (File.Exists(targetPath))
            {
                var e5 = new E5ImageReplaceService();
                var entries = e5.ReadIndex(targetPath);
                var number = targetNumbers.First();
                if (number > 0 && number <= entries.Count)
                {
                    try
                    {
                        var bytes = e5.ReadEntryBytes(targetPath, number);
                        if (TryReadImageSize(bytes, out var w, out var h))
                        {
                            return (width ?? w, height ?? h);
                        }
                    }
                    catch
                    {
                        // Fall back to preset dimensions.
                    }
                }
            }
        }

        return (width ?? preset.DefaultWidth, height ?? preset.DefaultHeight);
    }

    private static string BuildMappingSummary(
        CczProject project,
        AiImageAssetPreset preset,
        IReadOnlyList<int> numbers,
        int? rImageId,
        int? sImageId,
        int? faceId,
        int? jobId,
        int factionSlot)
    {
        var joined = string.Join("/", numbers.Select(x => "#" + x.ToString(CultureInfo.InvariantCulture)));
        return preset.Key switch
        {
            "r_actor" => $"R={rImageId ?? 0} -> Pmapobj.e5 {joined}",
            "s_unit" => CharacterImageResourceService.ResolveSUnitImageMapping(sImageId ?? 1, jobId, factionSlot).Detail + $"；目标 Unit 图 {joined}",
            "face" => new CharacterImageResourceService().MapFaceId(faceId ?? Math.Max(0, numbers.First() - 8)).Explanation + $"；目标 Face 图 {joined}",
            "dll_icon" when Ccz66RevisedLayout.Is66(project) && numbers.Count >= 2 => $"6.6 Item.e5 icon field value {(numbers[0] - 1) / 2} -> small #{numbers[0]} / large #{numbers[1]}",
            "dll_icon" when Ccz66RevisedLayout.Is66(project) => $"6.6 Mtem.e5 strategy icon field value {Math.Max(0, numbers.FirstOrDefault() - 1)} -> E5 image #{numbers.FirstOrDefault()}",
            "dll_icon" => $"DLL 图标字段编号 {numbers.FirstOrDefault()}，0-based。",
            _ => $"{preset.DefaultTargetRelativePath} 目标图 {joined}"
        };
    }

    private static IEnumerable<string> BuildPlanWarnings(CczProject project, AiImageAssetPreset preset, string target, IReadOnlyList<int> numbers, int width, int height)
    {
        if (numbers.Any(x => x < 0)) yield return "目标编号小于 0。";
        if (preset.Foreground && (width < 16 || height < 16)) yield return "前景素材目标尺寸过小，模型生成后会强烈缩放。";
        if (preset.Key == "r_actor") yield return "R 形象要求上游输出 2列x20行正背动作表；front/back 会分别切成 48x1280 条带。";
        if (preset.Key == "s_unit") yield return "S 形象要求上游输出 4x6/24 格动作表；Unit_mov/atk/spc 的精确实机帧序仍需验证。";
        if (preset.Key == "dll_icon" && Ccz66RevisedLayout.Is66(project) && Ccz66RevisedLayout.IsE5IconResource(target)) yield break;
        if (!target.Equals(preset.DefaultTargetRelativePath, StringComparison.OrdinalIgnoreCase)) yield return "目标资源路径已覆盖默认值，请确认仍属于 6.5 图片资源。";
    }

    private static string BuildPrompt(AiImageAssetPreset preset, string description, int width, int height, string format, string mapping)
    {
        var basePrompt = preset.Key switch
        {
            "r_background" =>
                $"为曹操传加强版 6.5 MOD 绘制一张 R 剧情背景图。主题：{description}。三国题材，像素感清晰但可来自高分辨率绘制，横向构图，适合 640x400 游戏背景；不要文字、不要 UI、不要水印、不要现代物品；画面留出人物站位空间。",
            "dll_icon" =>
                $"为曹操传加强版 6.5 MOD 绘制一个游戏道具/策略/命令图标。主题：{description}。单个主体居中，轮廓明确，高对比，适合缩小到 32x32；纯色背景或透明背景；不要文字、不要水印、不要复杂场景。",
            "face" =>
                $"为曹操传加强版 6.5 MOD 绘制角色头像。角色描述：{description}。三国/古代武将风格，半身或肩部以上，脸部清晰，正面或三分之二视角，适合 120x120 小头像；纯色背景；不要文字、不要水印。",
            "r_actor" =>
                BuildRActorSpriteSheetPrompt(description),
            "s_unit" =>
                BuildSUnitSpriteSheetPrompt(description),
            _ => description
        };

        return string.Join("\n", new[]
        {
            basePrompt,
            $"最终后处理目标：{width}x{height} {format}。",
            $"资源映射：{mapping}。",
            preset.Key switch
            {
                "s_unit" => "请优先保证每格动作差异、帧内不出界、角色身份一致、当前角色武器始终在手、无文字无水印。",
                "r_actor" => "请优先保证 front/back 都是 20 个不同但连贯的 R 帧；不要只画单帧，不要用正面镜像冒充背面。",
                _ => "请优先保证主体可读性、边缘干净、无文字、无水印。"
            }
        });
    }

    private static string BuildNegativePrompt(AiImageAssetPreset preset)
        => preset.Key == "s_unit"
            ? "文字, 水印, 签名, 多个角色, 背景场景, 把24格画成一张场景, 非4x6布局, 缺格, 格子尺寸不一致, 改变第二张格式图布局, 重排格子, 网格线, 边框, 角色被裁断, 当前武器被裁断, 攻击特效被裁断, 未持当前武器, 把非剑武器改成剑, 白色背景, 混入白底, 透明背景, 透明棋盘格, 大面积留白, 小图标式居中角色, 每格角色比例漂移, 高清插画, 精细立绘, 现代手游像素立绘, 写实比例, 过长身材, 过度Q版巨头, 平滑渐变, 柔边抗锯齿, 复杂光影, 现代高清像素贴纸, 低清晰度"
            : preset.Foreground
            ? "文字, 水印, 签名, 多个角色, 复杂背景, 现代物品, 模糊边缘, 低清晰度, 被裁断的主体, 过暗"
            : "文字, 水印, UI界面, 现代建筑, 科幻元素, 模糊, 过曝, 过暗, 人物特写遮挡背景";

    private static string BuildRActorSpriteSheetPrompt(string description)
        => $"""
为曹操传加强版 6.5 MOD 绘制 R 剧情人物正背动作表。角色描述：{description}
优先使用“角色替换型”工作流：第一张参考图是角色形象图，只用于角色身份、服装、发型、配色、阵营气质和关键装饰；第二张参考图是曹操传 6.5 R/S 格式参考，只用于低像素比例、粗轮廓、动作节奏、占格和洋红键。

输出一张完整 R sprite sheet：2 列 x 20 行，共 40 格；左列为 front 正面/正侧可见帧，右列为 back 背面/背侧可见帧。每格最终对应 48x64，整列后处理为 48x1280；可以输出 1024x1536 或相近高分辨率硬边放大图，但视觉必须像原生 48x64 低分辨率像素帧最近邻放大。

R 帧要求：20 行必须有站立、说话/轻微动作、转身、举武器、受击/特殊动作等差异；不得把同一帧复制 20 次。back 右列必须单独绘制，体现披风、背甲、后发束/冠背影、背持或斜持武器关系，不得由 front 镜像得到。

风格约束：必须像曹操传 6.5 Pmapobj.e5 R 人物条带，短身比例，深色粗轮廓，有限调色盘，硬边像素，纯洋红键背景。不要高清立绘缩小感，不要现代像素贴纸，不要平滑渐变，不要柔边抗锯齿。

武器与身份：当前武器必须在 front/back 关键帧中可读，且同一角色身份、主色、冠饰、披风和武器形状保持一致。背景使用纯洋红 #F700FF 或 #FF00FF；无文字、无水印、无网格线、无边框、无场景。
""";

    private static string BuildSUnitSpriteSheetPrompt(string description)
        => $"""
为曹操传加强版 6.5 MOD 绘制 S 战场角色小人动作表。角色描述：{description}
优先使用“角色替换型”工作流：第一张参考图是角色形象图，只用于角色身份、服装、发型、配色、阵营气质和关键装饰；第二张参考图是曹操传 MOD S 形象格式图，只用于 4x6 格子布局、动作、朝向、动作节奏、人物比例、像素画法、占格比例、洋红色键背景、轮廓粗细和特效尺度。武器以文字描述为最高优先级；当文字明确指定武器时，忽略第一张参考图里的任何旧武器、第二武器、交叉武器或特效弧。

替换要求：把第一张角色图中的人物替换到第二张曹操传 S 格式图中的所有动作格里；保留第二张图的格子布局、行列顺序、每格朝向、动作姿态、动作节奏、人物短身比例、像素风格和特效尺度。不要改变第二张格式图布局，不要重排格子，不要把 24 格画成一张连续场景。不要复制参考图里的背景、UI、文字、水印。

输出一张完整像素图 sprite sheet：4 列 x 6 行，共 24 格；从左上到右下为动画/GIF 播放顺序；每格等宽等高，无边框、无网格线、无间距、无裁切。可以输出 1024x1536 的 4 倍硬边参考图，但视觉上必须像原生 48x48/64x64 低分辨率像素帧最近邻放大；不能画成高清像素插画或现代手游像素立绘。

武器规则：不要默认画剑。必须优先沿用文字描述中的当前武器，例如枪、戟、刀、剑、斧、锤、弓、弩、扇、杖、法器或徒手格斗武器；只有文字没有明确武器时，才参考角色图或职业选择一种古代武器，并在 24 格中保持同一武器。所有“格挡、庆祝、蓄力、攻击、收招、支撑休息”动作都使用这个当前武器；弓弩角色用拉弓、放箭和收弓表现攻击，法师/扇杖角色用挥扇/举杖施法表现攻击，长兵器角色用突刺或横扫，重武器角色用劈砍或砸击。若文字指定“单枪/长枪/长矛”，每格只能有一条主长杆轴线，枪芒只能来自枪尖，不得出现剑、短刃、腰剑、背剑、第二枪、双武器或宽白剑弧。

曹操传 S 风格约束：必须像真实 6.5 `Unit_mov/Unit_atk/Unit_spc` 条带，而不是现代高清像素贴纸。真实移动/特技帧是 48x48，攻击帧是 64x64；角色、当前武器、裙摆和特效应接近撑满单帧，允许贴近边界但不能被裁断。不要把人物画成小图标悬在大空白里。移动帧平均应占满约 44-48 像素宽、46-48 像素高；攻击帧应利用约 58-64 像素宽和 50-64 像素高，武器轨迹或法术特效可占半个格子。每格必须像在目标低分辨率格内原生绘制，再最近邻放大展示：硬边像素、深色粗轮廓、有限调色盘、少量明暗层次、底部曹操传式深色落地阴影。人物是短身战棋小人：头盔/头部偏大但不能巨头，躯干紧凑，腿短，肩背宽，整体约 2 到 3 头身；服装细节要概括成缩小后仍可读的色块，不要画成高清插画、精细立绘、写实长身比例、现代高清像素画或现代手游像素立绘。24 格中的角色比例、武器形状和主色必须一致。

背景和输出：纯洋红色键背景，使用接近 #F700FF / RGB(247,0,255) 的单一平整背景色；不要白底、透明底、透明棋盘格、地面或场景。只保留曹操传式深色落地阴影。每格必须是不同姿态，不要复制粘贴同一帧。

如果没有第二张格式参考图，才使用下面的自由动作型格子说明；如果有第二张格式参考图，优先服从第二张图的动作、朝向和比例：
第1-4格：正面视角迈左脚行走；正面视角迈右脚行走；正面视角使用当前武器或盾牌格挡；正面视角受到攻击向后倾倒但仍握住当前武器。
第5-8格：侧面视角迈左脚行走；侧面视角迈右脚行走；侧面视角使用当前武器或盾牌格挡；正面视角举手或举起当前武器庆祝。
第9-12格：背面视角迈左脚行走；背面视角迈右脚行走；背面视角使用当前武器或盾牌格挡；正面视角用当前武器支撑或手扶武器半跪休息。
第13-16格：正面视角静止站立；正面视角举起当前武器蓄力准备攻击；正面视角使用当前武器攻击，加入低像素武器轨迹和少量光效；正面视角攻击后收回当前武器。
第17-20格：背面视角静止站立；背面视角举起当前武器蓄力准备攻击；背面视角使用当前武器攻击，加入低像素武器轨迹和少量光效；背面视角攻击后收回当前武器。
第21-24格：侧面视角静止站立；侧面视角举起当前武器蓄力准备攻击；侧面视角使用当前武器攻击，加入低像素武器轨迹和少量光效；侧面视角攻击后收回当前武器。

攻击动作必须夸张但仍像曹操传小人：身体明显位移和扭转，当前武器有大摆幅或明确施法/射击动作，轨迹/法术效果要大、粗、低像素，预备-打击-收招节奏清楚。攻击帧不要只是站立角色旁边加小特效；必须利用 64x64 攻击格的大画布。每一格里角色、当前武器和特效可以接近边缘但不能被裁断；动作之间要能连贯衔接，角色身份、服装配色和武器形状在 24 格中保持一致。

后期口径：AI 输出只作为可编辑草稿，不视为可直接入库成品；后处理会负责按 4x6 切格、最近邻缩放到 Unit_mov/Unit_atk/Unit_spc 目标尺寸、洋红色键透明化和替换预览，必要时仍需要手工修图校正比例、动作和边缘像素。
""";

    private IReadOnlyList<AiImagePreparedFile> PrepareFiles(
        CczProject project,
        AiImagePromptPlan plan,
        string sourcePath,
        Func<AiImagePromptPlan, string, object?>? previewFactory)
    {
        if (plan.Preset.Key == "r_actor")
        {
            return PrepareRActorFiles(project, plan, sourcePath, previewFactory);
        }

        if (plan.Preset.Key == "s_unit")
        {
            return PrepareSUnitFiles(project, plan, sourcePath, previewFactory);
        }

        if (Is66ItemIconPairPlan(plan))
        {
            return Prepare66ItemIconPairFiles(project, plan, sourcePath, previewFactory);
        }

        var exportRoot = GetExportRoot(project);
        Directory.CreateDirectory(exportRoot);
        var stem = MakeSafeFileStem(plan.Preset.Key + "_" + plan.Description);
        var extension = ExtensionForFormat(plan.OutputFormat);
        var outputPath = Path.Combine(exportRoot, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{stem}{extension}");
        var post = PostProcessImage(plan, sourcePath, outputPath, plan.TargetWidth, plan.TargetHeight);
        var outputBytes = File.ReadAllBytes(outputPath);
        return
        [
            new AiImagePreparedFile
            {
                Role = plan.Preset.Key,
                TargetRelativePath = plan.TargetRelativePath,
                TargetImageNumbers = plan.TargetImageNumbers,
                OutputPath = outputPath,
                OutputWidth = post.OutputWidth,
                OutputHeight = post.OutputHeight,
                OutputSha256 = ComputeSha256(outputBytes),
                ReplacementPreview = previewFactory?.Invoke(plan, outputPath)
            }
        ];
    }

    private static bool Is66ItemIconPairPlan(AiImagePromptPlan plan)
        => plan.Preset.Key == "dll_icon" &&
           Ccz66RevisedLayout.IsItemIconResource(plan.TargetRelativePath) &&
           plan.TargetImageNumbers.Count == 2;

    private IReadOnlyList<AiImagePreparedFile> Prepare66ItemIconPairFiles(
        CczProject project,
        AiImagePromptPlan plan,
        string sourcePath,
        Func<AiImagePromptPlan, string, object?>? previewFactory)
    {
        var exportRoot = Path.Combine(GetExportRoot(project), "item66");
        Directory.CreateDirectory(exportRoot);
        var stem = MakeSafeFileStem(plan.Preset.Key + "_" + plan.Description);
        var extension = ExtensionForFormat(plan.OutputFormat);
        var roles = new[]
        {
            new { Role = "small", ImageNumber = plan.TargetImageNumbers[0], Width = 16, Height = 16 },
            new { Role = "large", ImageNumber = plan.TargetImageNumbers[1], Width = 32, Height = 32 }
        };

        var files = new List<AiImagePreparedFile>();
        foreach (var role in roles)
        {
            var outputPath = Path.Combine(exportRoot, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{stem}_{role.Role}{extension}");
            var rolePlan = ClonePlanForTarget(plan, plan.TargetRelativePath, role.Width, role.Height, [role.ImageNumber]);
            var post = PostProcessImage(rolePlan, sourcePath, outputPath, role.Width, role.Height);
            var outputBytes = File.ReadAllBytes(outputPath);
            files.Add(new AiImagePreparedFile
            {
                Role = role.Role,
                TargetRelativePath = plan.TargetRelativePath,
                TargetImageNumbers = rolePlan.TargetImageNumbers,
                OutputPath = outputPath,
                OutputWidth = post.OutputWidth,
                OutputHeight = post.OutputHeight,
                OutputSha256 = ComputeSha256(outputBytes),
                ReplacementPreview = previewFactory?.Invoke(rolePlan, outputPath)
            });
        }

        return files;
    }

    private IReadOnlyList<AiImagePreparedFile> PrepareRActorFiles(
        CczProject project,
        AiImagePromptPlan plan,
        string sourcePath,
        Func<AiImagePromptPlan, string, object?>? previewFactory)
    {
        if (plan.TargetImageNumbers.Count < 2)
        {
            throw new InvalidOperationException("r_actor plans must target both front and back Pmapobj entries.");
        }

        var root = Path.Combine(GetExportRoot(project), "r_actor");
        Directory.CreateDirectory(root);
        var stem = MakeSafeFileStem(plan.Preset.Key + "_" + plan.Description);
        var extension = ExtensionForFormat(plan.OutputFormat);
        var roles = new[]
        {
            new { Role = "front", ImageNumber = plan.TargetImageNumbers[0], Column = 0 },
            new { Role = "back", ImageNumber = plan.TargetImageNumbers[1], Column = 1 }
        };

        var files = new List<AiImagePreparedFile>();
        foreach (var role in roles)
        {
            var outputPath = Path.Combine(root, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{stem}_{role.Role}{extension}");
            using var source = Image.FromFile(sourcePath);
            using var strip = BuildRActorStripFromSheet(source, role.Column, transparentForeground: true);
            SaveBitmap(strip, outputPath, plan.OutputFormat);
            var outputBytes = File.ReadAllBytes(outputPath);
            var rolePlan = ClonePlanForTarget(plan, plan.TargetRelativePath, 48, 1280, [role.ImageNumber]);
            files.Add(new AiImagePreparedFile
            {
                Role = role.Role,
                TargetRelativePath = plan.TargetRelativePath,
                TargetImageNumbers = rolePlan.TargetImageNumbers,
                OutputPath = outputPath,
                OutputWidth = strip.Width,
                OutputHeight = strip.Height,
                OutputSha256 = ComputeSha256(outputBytes),
                ReplacementPreview = previewFactory?.Invoke(rolePlan, outputPath)
            });
        }

        return files;
    }

    private IReadOnlyList<AiImagePreparedFile> PrepareSUnitFiles(
        CczProject project,
        AiImagePromptPlan plan,
        string sourcePath,
        Func<AiImagePromptPlan, string, object?>? previewFactory)
    {
        var root = Path.Combine(GetExportRoot(project), "s_unit");
        Directory.CreateDirectory(root);
        var stem = MakeSafeFileStem(plan.Preset.Key + "_" + plan.Description);
        var extension = ExtensionForFormat(plan.OutputFormat);
        var roles = new[]
        {
            new { Role = "move", Target = "Unit_mov.e5", Width = 48, Height = 528 },
            new { Role = "attack", Target = "Unit_atk.e5", Width = 64, Height = 768 },
            new { Role = "special", Target = "Unit_spc.e5", Width = 48, Height = 240 }
        };

        var files = new List<AiImagePreparedFile>();
        foreach (var role in roles)
        {
            var outputPath = Path.Combine(root, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{stem}_{role.Role}{extension}");
            var rolePlan = ClonePlanForTarget(plan, role.Target, role.Width, role.Height);
            var post = PostProcessImage(rolePlan, sourcePath, outputPath, role.Width, role.Height);
            var outputBytes = File.ReadAllBytes(outputPath);
            files.Add(new AiImagePreparedFile
            {
                Role = role.Role,
                TargetRelativePath = role.Target,
                TargetImageNumbers = rolePlan.TargetImageNumbers,
                OutputPath = outputPath,
                OutputWidth = post.OutputWidth,
                OutputHeight = post.OutputHeight,
                OutputSha256 = ComputeSha256(outputBytes),
                ReplacementPreview = previewFactory?.Invoke(rolePlan, outputPath)
            });
        }

        return files;
    }

    private static AiImagePromptPlan ClonePlanForTarget(
        AiImagePromptPlan plan,
        string targetRelativePath,
        int width,
        int height,
        IReadOnlyList<int>? targetImageNumbers = null)
        => new()
        {
            Preset = plan.Preset,
            Description = plan.Description,
            Prompt = plan.Prompt,
            NegativePrompt = plan.NegativePrompt,
            TargetRelativePath = targetRelativePath,
            TargetImageNumbers = targetImageNumbers ?? plan.TargetImageNumbers,
            TargetWidth = width,
            TargetHeight = height,
            OutputFormat = plan.OutputFormat,
            GenerationSize = plan.GenerationSize,
            Quality = plan.Quality,
            MappingSummary = plan.MappingSummary,
            Warnings = plan.Warnings,
            ReferenceImages = plan.ReferenceImages
        };

    private static PostProcessInfo BuildPostProcessInfo(string sourcePath, IReadOnlyList<AiImagePreparedFile> files)
    {
        using var source = Image.FromFile(sourcePath);
        var primary = files.First();
        var summary = files.Count == 1
            ? $"生成规范化素材：{primary.OutputWidth}x{primary.OutputHeight}。"
            : "生成多文件规范化素材：" + string.Join("；", files.Select(x => $"{x.Role}={x.OutputWidth}x{x.OutputHeight}"));
        return new PostProcessInfo(source.Width, source.Height, primary.OutputWidth, primary.OutputHeight, summary);
    }

    private static PostProcessInfo PostProcessImage(AiImagePromptPlan plan, string sourcePath, string outputPath, int targetWidth, int targetHeight)
    {
        using var source = Image.FromFile(sourcePath);
        if (plan.Preset.Key == "s_unit")
        {
            var frameOrder = SelectSUnitFrameOrder(targetWidth, targetHeight);
            using var strip = BuildSUnitStripFromSheet(source, targetWidth, targetHeight, frameOrder, transparentForeground: true);
            SaveBitmap(strip, outputPath, plan.OutputFormat);
            return new PostProcessInfo(source.Width, source.Height, strip.Width, strip.Height, $"生成 S 动作条带：从 4x6/24格动作表切出 {frameOrder.Count} 帧，输出 {strip.Width}x{strip.Height}。");
        }

        using var bitmap = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(plan.Preset.Foreground ? Color.Transparent : Color.Black);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            var srcRect = CenterCropRectangle(source.Width, source.Height, targetWidth, targetHeight);
            graphics.DrawImage(source, new Rectangle(0, 0, targetWidth, targetHeight), srcRect, GraphicsUnit.Pixel);
        }

        if (plan.Preset.Foreground)
        {
            ApplyMagentaTransparency(bitmap);
        }

        SaveBitmap(bitmap, outputPath, plan.OutputFormat);
        return new PostProcessInfo(source.Width, source.Height, bitmap.Width, bitmap.Height, $"居中裁切/缩放到 {bitmap.Width}x{bitmap.Height}，foreground={plan.Preset.Foreground}。");
    }

    private static Bitmap BuildFrameBitmap(Image source, int width, int height, bool transparentForeground)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            var scale = Math.Min(width / (float)source.Width, height / (float)source.Height);
            if (scale <= 0 || float.IsNaN(scale) || float.IsInfinity(scale)) scale = 1;
            var drawWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
            var drawHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
            var x = (width - drawWidth) / 2;
            var y = height - drawHeight;
            graphics.DrawImage(source, new Rectangle(x, y, drawWidth, drawHeight));
        }

        if (transparentForeground)
        {
            ApplyMagentaTransparency(bitmap);
        }

        return bitmap;
    }

    private static Bitmap BuildRActorStripFromSheet(Image source, int column, bool transparentForeground)
    {
        const int columns = 2;
        const int rows = 20;
        var bitmap = new Bitmap(48, 64 * rows, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.SmoothingMode = SmoothingMode.None;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;

        for (var row = 0; row < rows; row++)
        {
            var frameIndex = row * columns + Math.Clamp(column, 0, columns - 1);
            var sourceRect = GridCellRectangle(source.Width, source.Height, columns, rows, frameIndex);
            var destRect = new Rectangle(0, row * 64, 48, 64);
            graphics.DrawImage(source, destRect, sourceRect, GraphicsUnit.Pixel);
        }

        if (transparentForeground)
        {
            ApplyMagentaTransparency(bitmap);
        }

        return bitmap;
    }

    private static IReadOnlyList<int> SelectSUnitFrameOrder(int targetWidth, int targetHeight)
    {
        if (targetWidth >= 64) return SUnitAttackFrameOrder;
        return targetHeight <= 240 ? SUnitSpecialFrameOrder : SUnitMoveFrameOrder;
    }

    private static Bitmap BuildSUnitStripFromSheet(Image source, int targetWidth, int targetHeight, IReadOnlyList<int> frameOrder, bool transparentForeground)
    {
        const int columns = 4;
        const int rows = 6;
        var frameHeight = Math.Max(1, targetHeight / Math.Max(1, frameOrder.Count));
        var bitmap = new Bitmap(targetWidth, frameHeight * frameOrder.Count, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.SmoothingMode = SmoothingMode.None;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;

        for (var i = 0; i < frameOrder.Count; i++)
        {
            var frameIndex = Math.Clamp(frameOrder[i], 0, columns * rows - 1);
            var sourceRect = GridCellRectangle(source.Width, source.Height, columns, rows, frameIndex);
            var destRect = new Rectangle(0, i * frameHeight, targetWidth, frameHeight);
            graphics.DrawImage(source, destRect, sourceRect, GraphicsUnit.Pixel);
        }

        if (transparentForeground)
        {
            ApplyMagentaTransparency(bitmap);
        }

        return bitmap;
    }

    private static Rectangle GridCellRectangle(int sourceWidth, int sourceHeight, int columns, int rows, int zeroBasedIndex)
    {
        var column = zeroBasedIndex % columns;
        var row = zeroBasedIndex / columns;
        var x0 = (int)Math.Round(column * sourceWidth / (double)columns);
        var x1 = (int)Math.Round((column + 1) * sourceWidth / (double)columns);
        var y0 = (int)Math.Round(row * sourceHeight / (double)rows);
        var y1 = (int)Math.Round((row + 1) * sourceHeight / (double)rows);
        return new Rectangle(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
    }

    private static Rectangle CenterCropRectangle(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        var sourceRatio = sourceWidth / (float)sourceHeight;
        var targetRatio = targetWidth / (float)targetHeight;
        if (sourceRatio > targetRatio)
        {
            var cropWidth = (int)Math.Round(sourceHeight * targetRatio);
            return new Rectangle((sourceWidth - cropWidth) / 2, 0, cropWidth, sourceHeight);
        }

        var cropHeight = (int)Math.Round(sourceWidth / targetRatio);
        return new Rectangle(0, (sourceHeight - cropHeight) / 2, sourceWidth, cropHeight);
    }

    private static void ApplyMagentaTransparency(Bitmap bitmap)
    {
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A == 0 || IsMagentaKey(pixel) || IsNearCornerBackground(bitmap, pixel))
                {
                    bitmap.SetPixel(x, y, Color.Transparent);
                }
            }
        }
    }

    private static bool IsMagentaKey(Color pixel)
        => pixel.R >= 220 && pixel.B >= 220 && pixel.G <= 80;

    private static bool IsNearCornerBackground(Bitmap bitmap, Color pixel)
    {
        var corner = bitmap.GetPixel(0, 0);
        if (corner.A == 0) return false;
        var delta = Math.Abs(pixel.R - corner.R) + Math.Abs(pixel.G - corner.G) + Math.Abs(pixel.B - corner.B);
        var cornerLooksFlat = Math.Max(corner.R, Math.Max(corner.G, corner.B)) - Math.Min(corner.R, Math.Min(corner.G, corner.B)) < 16 || IsMagentaKey(corner);
        return cornerLooksFlat && delta < 36;
    }

    private static void SaveBitmap(Bitmap bitmap, string path, string format)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        switch (format.ToLowerInvariant())
        {
            case "jpg":
            case "jpeg":
                using (var flattened = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format24bppRgb))
                using (var graphics = Graphics.FromImage(flattened))
                {
                    graphics.Clear(Color.Black);
                    graphics.DrawImageUnscaled(bitmap, 0, 0);
                    flattened.Save(path, ImageFormat.Jpeg);
                }
                break;
            case "bmp":
                bitmap.Save(path, ImageFormat.Bmp);
                break;
            default:
                bitmap.Save(path, ImageFormat.Png);
                break;
        }
    }

    private static string WriteManifest(CczProject project, AiImagePromptPlan plan, string sourcePath, string outputPath, PostProcessInfo post, IReadOnlyList<AiImagePreparedFile> files)
    {
        var manifestRoot = Path.Combine(GetExportRoot(project), "manifests");
        Directory.CreateDirectory(manifestRoot);
        var manifestPath = Path.Combine(manifestRoot, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{plan.Preset.Key}.json");
        var payload = new
        {
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            project.GameRoot,
            Preset = plan.Preset.Key,
            plan.Description,
            plan.Prompt,
            plan.NegativePrompt,
            plan.TargetRelativePath,
            TargetImageNumbers = plan.TargetImageNumbers,
            plan.TargetWidth,
            plan.TargetHeight,
            plan.OutputFormat,
            SourcePath = sourcePath,
            OutputPath = outputPath,
            ReferenceImages = plan.ReferenceImages,
            PostProcess = post,
            PreparedFiles = files,
            SafetyNote = "AI 绘图工具只生成素材并调用 preview，不直接写入游戏资源。"
        };
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8);
        return manifestPath;
    }

    private static ImageRequest BuildImageRequest(UpstreamConfig config, AiImagePromptPlan plan)
    {
        var baseUrl = config.BaseUrl.TrimEnd('/');
        if (config.Provider.Equals(ProviderRetroDiffusion, StringComparison.OrdinalIgnoreCase))
        {
            var (width, height) = ParseGenerationSize(plan.GenerationSize, 1024, 1536);
            var payload = new Dictionary<string, object?>
            {
                ["prompt"] = plan.Prompt,
                ["negative_prompt"] = plan.NegativePrompt,
                ["model"] = config.ImageModel,
                ["width"] = width,
                ["height"] = height,
                ["num_images"] = 1,
                ["image_format"] = "png",
                ["pixel_art"] = true,
                ["style"] = "low-resolution pixel art sprite sheet",
                ["palette"] = "limited",
                ["transparent_background"] = false,
                ["background_color"] = "#F700FF",
                ["remove_background"] = false
            };
            if (plan.ReferenceImages.Count > 0)
            {
                payload["reference_images"] = plan.ReferenceImages
                    .Select(reference => Convert.ToBase64String(File.ReadAllBytes(reference.Path)))
                    .ToArray();
            }
            return new ImageRequest(baseUrl + "/v1/inferences", ToJson(payload));
        }

        if (plan.ReferenceImages.Count > 0)
        {
            throw new InvalidOperationException("reference_image_paths are currently supported only by the RetroDiffusion R/S provider.");
        }

        if (config.ApiMode.Equals("images", StringComparison.OrdinalIgnoreCase))
        {
            var payload = new Dictionary<string, object?>
            {
                ["model"] = config.ImageModel,
                ["prompt"] = plan.Prompt,
                ["n"] = 1,
                ["size"] = plan.GenerationSize,
                ["quality"] = plan.Quality,
                ["output_format"] = "png"
            };
            return new ImageRequest(baseUrl + "/v1/images/generations", ToJson(payload));
        }

        var tool = new Dictionary<string, object?>
        {
            ["type"] = "image_generation",
            ["model"] = config.ImageModel,
            ["action"] = "generate",
            ["size"] = plan.GenerationSize,
            ["quality"] = plan.Quality,
            ["output_format"] = "png",
            ["moderation"] = "low",
            ["partial_images"] = 1
        };
        var payloadResponses = new Dictionary<string, object?>
        {
            ["model"] = config.TextModel,
            ["input"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["role"] = "user",
                    ["content"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["type"] = "input_text",
                            ["text"] = plan.Prompt
                        }
                    }
                }
            },
            ["tools"] = new object[] { tool },
            ["tool_choice"] = new Dictionary<string, object?> { ["type"] = "image_generation" },
            ["instructions"] = "You are a tool runner. Pass the user prompt to image_generation verbatim. Do not rewrite it.",
            ["store"] = false,
            ["stream"] = true
        };
        return new ImageRequest(baseUrl + "/v1/responses", ToJson(payloadResponses));
    }

    private static string ExtractImageBase64(string raw)
    {
        foreach (var payload in EnumerateJsonPayloads(raw))
        {
            using var doc = JsonDocument.Parse(payload);
            var found = FindImageBase64(doc.RootElement);
            if (!string.IsNullOrWhiteSpace(found)) return found;
        }

        return string.Empty;
    }

    private static IEnumerable<string> EnumerateJsonPayloads(string raw)
    {
        var text = raw.Trim();
        if (text.StartsWith("{", StringComparison.Ordinal) || text.StartsWith("[", StringComparison.Ordinal))
        {
            yield return text;
            yield break;
        }

        foreach (var line in raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var payload = trimmed[5..].Trim();
            if (payload.Length == 0 || payload == "[DONE]") continue;
            if (payload.StartsWith("{", StringComparison.Ordinal) || payload.StartsWith("[", StringComparison.Ordinal))
            {
                yield return payload;
            }
        }
    }

    private static string? FindImageBase64(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String &&
                        property.Name is "b64_json" or "result" or "image_b64")
                    {
                        var value = property.Value.GetString();
                        if (LooksLikeBase64Image(value)) return value;
                    }

                    if (property.Value.ValueKind == JsonValueKind.String &&
                        property.Name.Contains("image", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = property.Value.GetString();
                        if (LooksLikeBase64Image(value)) return value;
                    }

                    var nested = FindImageBase64(property.Value);
                    if (!string.IsNullOrWhiteSpace(nested)) return nested;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var nested = FindImageBase64(item);
                    if (!string.IsNullOrWhiteSpace(nested)) return nested;
                }
                break;
            case JsonValueKind.String:
                var text = element.GetString();
                if (LooksLikeBase64Image(text)) return text;
                break;
        }

        return null;
    }

    private static bool LooksLikeBase64Image(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 100) return false;
        value = value.Trim();
        if (value.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
        {
            var comma = value.IndexOf(',');
            if (comma >= 0) value = value[(comma + 1)..];
        }

        return value.StartsWith("iVBOR", StringComparison.Ordinal) ||
               value.StartsWith("/9j/", StringComparison.Ordinal) ||
               value.StartsWith("UklGR", StringComparison.Ordinal);
    }

    private static UpstreamConfig ReadUpstreamConfig(AiImageAssetPreset preset)
    {
        var provider = ResolveProvider(preset);
        if (provider.Equals(ProviderRetroDiffusion, StringComparison.OrdinalIgnoreCase))
        {
            return new UpstreamConfig(
                ProviderRetroDiffusion,
                FirstNonEmpty(Environment.GetEnvironmentVariable(EnvRetroDiffusionBaseUrl), "https://api.retrodiffusion.ai"),
                Environment.GetEnvironmentVariable(EnvRetroDiffusionApiKey) ?? string.Empty,
                string.Empty,
                FirstNonEmpty(Environment.GetEnvironmentVariable(EnvRetroDiffusionModel), DefaultRetroDiffusionModel(preset)),
                "retrodiffusion");
        }

        return new UpstreamConfig(
            ProviderImageStudio,
            FirstNonEmpty(Environment.GetEnvironmentVariable(EnvBaseUrl), Environment.GetEnvironmentVariable(EnvUpstreamBaseUrlAlias), string.Empty),
            Environment.GetEnvironmentVariable(EnvApiKey) ?? string.Empty,
            FirstNonEmpty(Environment.GetEnvironmentVariable(EnvTextModel), Environment.GetEnvironmentVariable(EnvTextModelIdAlias), "gpt-5.5"),
            FirstNonEmpty(Environment.GetEnvironmentVariable(EnvImageModel), Environment.GetEnvironmentVariable(EnvImageModelIdAlias), "gpt-image-2"),
            NormalizeApiMode(Environment.GetEnvironmentVariable(EnvApiMode)));
    }

    private static string ResolveProvider(AiImageAssetPreset preset)
    {
        var configured = Environment.GetEnvironmentVariable(EnvPixelProvider);
        if (preset.Key is "r_actor" or "s_unit")
        {
            return string.IsNullOrWhiteSpace(configured) ? ProviderRetroDiffusion : NormalizeProvider(configured);
        }

        return ProviderImageStudio;
    }

    private static string NormalizeProvider(string value)
    {
        value = value.Trim().ToLowerInvariant();
        return value is "retro" or "retro_diffusion" or "retrodiffusion" ? ProviderRetroDiffusion : ProviderImageStudio;
    }

    private static string DefaultRetroDiffusionModel(AiImageAssetPreset preset)
        => preset.Key == "s_unit" ? "RD_FLUX" : "RD_FLUX";

    private static string NormalizeApiMode(string? value)
        => value?.Trim().Equals("images", StringComparison.OrdinalIgnoreCase) == true ? "images" : "responses";

    private static string FirstNonEmpty(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string FirstNonEmpty(string? first, string? second, string fallback)
        => !string.IsNullOrWhiteSpace(first)
            ? first.Trim()
            : !string.IsNullOrWhiteSpace(second)
                ? second.Trim()
                : fallback;

    private static (int Width, int Height) ParseGenerationSize(string value, int fallbackWidth, int fallbackHeight)
    {
        var parts = value.Split('x', 'X');
        if (parts.Length == 2 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height) &&
            width > 0 &&
            height > 0)
        {
            return (width, height);
        }

        return (fallbackWidth, fallbackHeight);
    }

    private static string MaskBaseUrl(string value)
        => string.IsNullOrWhiteSpace(value) ? "<未配置>" : value.TrimEnd('/');

    private static string ToJson(object value)
        => JsonSerializer.Serialize(value, JsonOptions);

    private static string NormalizeOutputFormat(string? value, string fallback)
    {
        value = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
        return value switch
        {
            "jpeg" => "jpg",
            "jpg" or "png" or "bmp" => value,
            _ => fallback
        };
    }

    private static string ExtensionForFormat(string format)
        => format.ToLowerInvariant() switch
        {
            "jpg" or "jpeg" => ".jpg",
            "bmp" => ".bmp",
            _ => ".png"
        };

    private static string GetExportRoot(CczProject project)
        => Path.Combine(project.WorkspaceRoot, "CCZModStudio_Exports", "AiImageAssets");

    private static string ResolveProjectFile(CczProject project, string relativeOrAbsolutePath, bool mustExist)
    {
        var normalized = relativeOrAbsolutePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.IsPathRooted(normalized)
            ? normalized
            : Path.Combine(project.GameRoot, normalized));
        var root = Path.GetFullPath(project.GameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("目标资源路径不在当前项目目录内：" + fullPath);
        }

        if (mustExist && !File.Exists(fullPath))
        {
            throw new FileNotFoundException("目标资源不存在。", fullPath);
        }

        return fullPath;
    }

    private static bool TryReadImageSize(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        try
        {
            using var ms = new MemoryStream(bytes);
            using var image = Image.FromStream(ms);
            width = image.Width;
            height = image.Height;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string MakeSafeFileStem(string value)
    {
        var stem = string.IsNullOrWhiteSpace(value) ? "image" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            stem = stem.Replace(invalid, '_');
        }

        stem = stem.Replace(' ', '_');
        return stem.Length <= 64 ? stem : stem[..64];
    }

    private static string ComputeSha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes));

    private sealed record UpstreamConfig(string Provider, string BaseUrl, string ApiKey, string TextModel, string ImageModel, string ApiMode);
    private sealed record ImageRequest(string Url, string JsonPayload);
    private sealed record PostProcessInfo(int SourceWidth, int SourceHeight, int OutputWidth, int OutputHeight, string Summary);
}
