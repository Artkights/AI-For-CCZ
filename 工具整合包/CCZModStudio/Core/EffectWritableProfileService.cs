using System.Text.Json;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class EffectWritableProfileService
{
    public EffectWritableProfileStatus Evaluate(CczProject project)
    {
        var audit = new ExecutableProfileAuditService().Audit(project);
        return EvaluateFromAudit(project, audit);
    }

    public EffectWritableProfileStatus EvaluateFromAudit(CczProject project, ExecutableProfileAuditResult audit)
    {
        var currentSha = audit.CurrentSha256;
        var result = new EffectWritableProfileStatus
        {
            CurrentExeSha256 = currentSha,
            ProfileId = audit.ProfileId,
            ProfileAudit = CloneAudit(audit),
            IsOriginalBaseline = currentSha.Equals(
                EffectWritableProfileStatus.Current65BaselineSha256,
                StringComparison.OrdinalIgnoreCase)
        };
        if (audit.CanWriteRegisteredData)
        {
            result.CanWrite = true;
            result.ReasonZh = audit.TrustStatus == ExecutableProfileTrustStatus.ExactCanonical
                ? "当前 EXE 与已验证的 6.5 未加密特效写入基底一致。"
                : "当前 EXE 只有已登记定宽数据字段发生合法变化，可复用相同代码契约。";
            return result;
        }

        var localProfile = new LocalEffectProfileService().FindVerified(project, currentSha);
        if (localProfile != null &&
            localProfile.FileLength == new FileInfo(project.ResolveGameFile("Ekd5.exe")).Length &&
            localProfile.NormalizedProfileIdentity.Equals(audit.NormalizedProfileIdentity, StringComparison.OrdinalIgnoreCase))
        {
            result.CanWrite = true;
            result.ProfileId = localProfile.ProfileId;
            result.ProfileAudit.ProfileId = localProfile.ProfileId;
            result.ProfileAudit.TrustStatus = ExecutableProfileTrustStatus.LocalVerified;
            result.ReasonZh = "当前 EXE 与本机签名的 6.5 特效档案完全匹配。";
            return result;
        }

        var manifests = EnumerateEffectManifests(project).ToList();
        result.EvidenceManifestPaths.AddRange(manifests.Select(item => item.Path));
        var knownShas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            EffectWritableProfileStatus.Current65BaselineSha256
        };
        var pending = manifests.ToList();
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var manifest in pending.ToList())
            {
                if (string.IsNullOrWhiteSpace(manifest.BeforeSha) || string.IsNullOrWhiteSpace(manifest.AfterSha) ||
                    !knownShas.Contains(manifest.BeforeSha)) continue;
                changed |= knownShas.Add(manifest.AfterSha);
                pending.Remove(manifest);
            }
        }

        result.IsTrackedDescendant = knownShas.Contains(currentSha);
        result.CanWrite = result.IsTrackedDescendant;
        if (result.IsTrackedDescendant && !audit.CanWriteRegisteredData)
            result.ProfileAudit.TrustStatus = ExecutableProfileTrustStatus.TrackedDescendant;
        result.ReasonZh = result.CanWrite
            ? "当前 EXE 可由本工具的特效事务记录追溯到已验证基底。"
            : audit.SummaryZh;
        return result;
    }

    private static IEnumerable<ManifestShaLink> EnumerateEffectManifests(CczProject project)
    {
        var roots = new[]
        {
            ProjectPatchIdentityService.CompositeManifestRoot(project),
            ProjectPatchIdentityService.EffectManifestRoot(project)
        };
        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var path in Directory.EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly))
            {
                ManifestShaLink? link = null;
                try
                {
                    var json = File.ReadAllText(path);
                    var signedManifest = JsonSerializer.Deserialize<EffectManifest>(json);
                    if (signedManifest == null || string.IsNullOrWhiteSpace(signedManifest.Signature) ||
                        !UserBoundSignatureService.Verify(signedManifest,
                            static value => value.Signature,
                            static (value, signature) => value.Signature = signature))
                        continue;
                    using var document = JsonDocument.Parse(json);
                    var rootElement = document.RootElement;
                    if (!ManifestBelongsToProject(project, rootElement)) continue;
                    var before = ReadString(rootElement, "ExeSha256Before") ??
                                 ReadMetadata(rootElement, "EngineProfileSha256Before") ?? string.Empty;
                    var after = ReadString(rootElement, "ExeSha256After") ??
                                ReadMetadata(rootElement, "EngineProfileSha256After") ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(after))
                    {
                        after = ReadString(rootElement, "CurrentExeSha256") ?? string.Empty;
                    }
                    link = new ManifestShaLink(path, before, after);
                }
                catch
                {
                    // Broken manifests are ignored as trust evidence.
                }
                if (link != null) yield return link;
            }
        }
    }

    private static bool ManifestBelongsToProject(CczProject project, JsonElement element)
    {
        ProjectPatchIdentity? identity = null;
        if (element.TryGetProperty("ProjectIdentity", out var identityElement) && identityElement.ValueKind == JsonValueKind.Object)
        {
            try { identity = identityElement.Deserialize<ProjectPatchIdentity>(); }
            catch { }
        }
        var legacyRoot = ReadString(element, "ProjectRoot");
        return new ProjectPatchIdentityService().Matches(project, identity, legacyRoot);
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadMetadata(JsonElement element, string name)
    {
        if (!element.TryGetProperty("Metadata", out var metadata) || metadata.ValueKind != JsonValueKind.Object)
            return null;
        return metadata.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private sealed record ManifestShaLink(string Path, string BeforeSha, string AfterSha);

    private static ExecutableProfileAuditResult CloneAudit(ExecutableProfileAuditResult source)
        => new()
        {
            ProfileId = source.ProfileId,
            EngineVersion = source.EngineVersion,
            CanonicalSha256 = source.CanonicalSha256,
            CurrentSha256 = source.CurrentSha256,
            NormalizedProfileIdentity = source.NormalizedProfileIdentity,
            TrustStatus = source.TrustStatus,
            CanWriteRegisteredData = source.CanWriteRegisteredData,
            CanReuseCodeContracts = source.CanReuseCodeContracts,
            ChangedByteCount = source.ChangedByteCount,
            ChangedRangeCount = source.ChangedRangeCount,
            RegisteredDifferences = source.RegisteredDifferences.ToList(),
            UnknownDifferences = source.UnknownDifferences.ToList(),
            BlockerCodes = source.BlockerCodes.ToList(),
            ReasonsZh = source.ReasonsZh.ToList(),
            AuditSummaryHash = source.AuditSummaryHash,
            SummaryZh = source.SummaryZh
        };
}
