using System.Text;
using System.Text.Json;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class CodeCaveRegistry
{
    public CodeCaveAllocationResult Allocate(
        ExeCodeCaveScanResult scan,
        EnginePatchProfile profile,
        CodeCaveAllocationRequest request)
    {
        if (request.RequiredBytes <= 0)
        {
            return Fail("invalid-request", "RequiredBytes must be positive.", []);
        }

        var total = checked(request.RequiredBytes + Math.Max(0, request.ReserveBytes));
        if (total < 5 && !request.AllowRelayAllocation)
        {
            return Fail("relay-only", "Code cave allocations smaller than 5 bytes are relay-only and must opt in with AllowRelayAllocation.", []);
        }

        var blockers = BuildBlockers(scan, profile, request);
        var fillAllowedCandidates = scan.Candidates
            .Where(candidate => CandidateFillAllowed(candidate, request))
            .ToList();

        var freeRanges = fillAllowedCandidates
            .SelectMany(candidate => BuildFreeRanges(candidate, blockers))
            .Where(range => range.Length >= total)
            .ToList();

        freeRanges = NormalizePolicy(request.AllocatorPolicy) switch
        {
            "largest-fit" => freeRanges
                .OrderByDescending(range => range.Length)
                .ThenBy(range => range.StartVirtualAddress)
                .ToList(),
            _ => freeRanges
                .OrderBy(range => range.Length)
                .ThenBy(range => range.StartVirtualAddress)
                .ToList()
        };

        var selected = freeRanges.FirstOrDefault();
        if (selected == null)
        {
            var considered = fillAllowedCandidates
                .OrderByDescending(candidate => candidate.Length)
                .Take(20)
                .ToList();
            return Fail(
                "no-cave",
                $"No free code cave range can satisfy {total} bytes (required {request.RequiredBytes} + reserve {request.ReserveBytes}).",
                considered);
        }

        var allocation = new AllocatedCodeCaveRange
        {
            CaveId = selected.CaveId,
            StartVirtualAddress = selected.StartVirtualAddress,
            EndVirtualAddress = checked(selected.StartVirtualAddress + (uint)total - 1),
            Length = total,
            Reason = $"Allocated {total} bytes from free range {selected.CaveId}."
        };

        var sourceCandidate = scan.Candidates.FirstOrDefault(candidate =>
            candidate.CaveId.Equals(selected.SourceCaveId, StringComparison.OrdinalIgnoreCase));

        return new CodeCaveAllocationResult
        {
            Success = true,
            Status = "allocated",
            Reason = allocation.Reason,
            Candidate = sourceCandidate,
            FreeRange = selected,
            Allocation = allocation,
            ConsideredCandidates = fillAllowedCandidates
                .OrderByDescending(candidate => candidate.Length)
                .Take(20)
                .ToList(),
            ConsideredFreeRanges = freeRanges.Take(20).ToList()
        };
    }

    public void EnsureNoPatchSegmentOverlap(IEnumerable<EffectPatchSegment> segments)
    {
        var ordered = segments
            .Select(segment => new
            {
                Segment = segment,
                TargetFile = FirstNonEmpty(segment.TargetFile, "Ekd5.exe"),
                AddressKind = FirstNonEmpty(segment.AddressKind, "OdVirtualAddress"),
                Address = ResolveAddress(segment),
                Length = ParseHexByteCount(segment.BytesHex)
            })
            .Where(item => item.Length > 0)
            .OrderBy(item => item.TargetFile, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.AddressKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Address)
            .ToList();

        for (var i = 1; i < ordered.Count; i++)
        {
            var previous = ordered[i - 1];
            var current = ordered[i];
            if (!previous.TargetFile.Equals(current.TargetFile, StringComparison.OrdinalIgnoreCase) ||
                !previous.AddressKind.Equals(current.AddressKind, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var previousEndExclusive = checked(previous.Address + (uint)previous.Length);
            if (current.Address < previousEndExclusive)
            {
                throw new InvalidOperationException(
                    $"Patch segments overlap: {previous.TargetFile}/{previous.AddressKind} 0x{previous.Address:X8}+{previous.Length} and 0x{current.Address:X8}+{current.Length}.");
            }
        }
    }

    public List<AllocatedCodeCaveRange> LoadExistingAllocations(CczProject project, string targetFileName = "Ekd5.exe")
    {
        var allocations = new List<AllocatedCodeCaveRange>();
        foreach (var root in new[]
                 {
                     ProjectPatchIdentityService.EffectManifestRoot(project),
                     ProjectPatchIdentityService.CompositeManifestRoot(project),
                     ProjectPatchIdentityService.DispatcherManifestRoot(project)
                 }.Where(Directory.Exists))
        {
            foreach (var path in Directory.GetFiles(root, "*.json"))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
                    var rootElement = document.RootElement;
                    var isDispatcher = rootElement.TryGetProperty("InstallationPackage", out _);
                    var isComposite = !isDispatcher && rootElement.TryGetProperty("Draft", out _);
                    ProjectPatchIdentity? identity;
                    string legacyRoot;
                    EffectPackage package;
                    string manifestId;
                    if (isDispatcher)
                    {
                        var manifest = rootElement.Deserialize<EffectDispatcherManifestV2>();
                        if (manifest == null || manifest.StatusZh == "已移除") continue;
                        identity = manifest.ProjectIdentity;
                        legacyRoot = identity?.GameRoot ?? string.Empty;
                        package = manifest.InstallationPackage;
                        manifestId = manifest.ManifestId;
                    }
                    else if (isComposite)
                    {
                        var manifest = rootElement.Deserialize<CompositeEffectManifest>();
                        if (manifest == null || manifest.StatusZh == "已移除") continue;
                        identity = manifest.ProjectIdentity;
                        legacyRoot = identity?.GameRoot ?? string.Empty;
                        package = manifest.Package;
                        manifestId = manifest.ManifestId;
                    }
                    else
                    {
                        var manifest = rootElement.Deserialize<EffectManifest>();
                        if (manifest == null) continue;
                        identity = manifest.ProjectIdentity;
                        legacyRoot = manifest.ProjectRoot;
                        package = manifest.Package;
                        manifestId = manifest.ManifestId;
                        // 模块化语义特效的代码洞由 EffectDispatcherManifestV2 独占登记。
                        // 通用事务清单仅保留备份与审计信息，不能重复声明同一调度器载荷。
                        if (package.Metadata.GetValueOrDefault("LogicalPatchKind") is
                            "modular-semantic-effect-v2" or "modular-semantic-maintenance-v2") continue;
                        if (package.Metadata.GetValueOrDefault("AdapterStatus", "Active") == "Released") continue;
                    }
                    if (!new ProjectPatchIdentityService().Matches(project, identity, legacyRoot)) continue;
                    AddOwnedCodeCaves(allocations, package, manifestId, targetFileName);
                }
                catch
                {
                    // Malformed and unscoped manifests cannot own code caves.
                }
            }
        }

        return allocations
            .GroupBy(range => (range.StartVirtualAddress, range.EndVirtualAddress, range.CaveId))
            .Select(group => group.First())
            .OrderBy(range => range.StartVirtualAddress)
            .ToList();
    }

    private static void AddOwnedCodeCaves(
        ICollection<AllocatedCodeCaveRange> allocations,
        EffectPackage package,
        string manifestId,
        string targetFileName)
    {
        foreach (var segment in package.PatchSegments)
        {
            if (string.IsNullOrWhiteSpace(segment.CodeCaveId)) continue;
            if (!TargetFileMatches(segment.TargetFile, targetFileName)) continue;
            if (!FirstNonEmpty(segment.AddressKind, "OdVirtualAddress").Equals("OdVirtualAddress", StringComparison.OrdinalIgnoreCase)) continue;
            var length = ParseHexByteCount(segment.BytesHex);
            var start = ResolveAddress(segment);
            if (length <= 0 || start == 0) continue;
            allocations.Add(new AllocatedCodeCaveRange
            {
                CaveId = segment.CodeCaveId,
                StartVirtualAddress = start,
                EndVirtualAddress = checked(start + (uint)length - 1),
                Length = length,
                Reason = $"当前项目补丁 {manifestId}：{FirstNonEmpty(segment.Comment, segment.HookPoint, segment.CodeCaveId)}。"
            });
        }
    }

    private static List<RangeBlock> BuildBlockers(
        ExeCodeCaveScanResult scan,
        EnginePatchProfile profile,
        CodeCaveAllocationRequest request)
    {
        var blockers = new List<RangeBlock>();
        blockers.AddRange(scan.BlockedRanges.Select(range => new RangeBlock(range.StartVirtualAddress, range.EndVirtualAddress, range.CaveId, range.Reason)));
        blockers.AddRange(profile.BlockedRanges.Select(range => new RangeBlock(range.StartVirtualAddress, range.EndVirtualAddress, range.CaveId, range.Reason)));
        blockers.AddRange(profile.ReservedRanges.Select(range => new RangeBlock(range.StartVirtualAddress, range.EndVirtualAddress, range.CaveId, range.Reason)));
        blockers.AddRange(request.ExistingAllocations.Select(range => new RangeBlock(range.StartVirtualAddress, range.EndVirtualAddress, range.CaveId, range.Reason)));
        return blockers
            .Where(block => block.Start <= block.End)
            .GroupBy(block => (block.Start, block.End, block.Id))
            .Select(group => group.First())
            .OrderBy(block => block.Start)
            .ThenBy(block => block.End)
            .ToList();
    }

    private static IEnumerable<FreeCodeCaveRange> BuildFreeRanges(ExeCodeCaveCandidate candidate, IReadOnlyList<RangeBlock> blockers)
    {
        var windows = new List<(uint Start, uint End)> { (candidate.StartVirtualAddress, candidate.EndVirtualAddress) };
        foreach (var blocker in blockers.Where(blocker =>
                     RangesIntersect(candidate.StartVirtualAddress, candidate.EndVirtualAddress, blocker.Start, blocker.End)))
        {
            var next = new List<(uint Start, uint End)>();
            foreach (var window in windows)
            {
                if (!RangesIntersect(window.Start, window.End, blocker.Start, blocker.End))
                {
                    next.Add(window);
                    continue;
                }

                if (blocker.Start > window.Start)
                {
                    next.Add((window.Start, blocker.Start - 1));
                }

                if (blocker.End < window.End)
                {
                    next.Add((blocker.End + 1, window.End));
                }
            }

            windows = next;
            if (windows.Count == 0) break;
        }

        foreach (var window in windows)
        {
            var length = checked((int)(window.End - window.Start + 1));
            var offsetDelta = checked((long)(window.Start - candidate.StartVirtualAddress));
            yield return new FreeCodeCaveRange
            {
                CaveId = $"{candidate.CaveId}:free:{window.Start:X8}-{window.End:X8}",
                SourceCaveId = candidate.CaveId,
                SectionName = candidate.SectionName,
                FillKind = candidate.FillKind,
                StartVirtualAddress = window.Start,
                EndVirtualAddress = window.End,
                FileOffset = checked(candidate.FileOffset + offsetDelta),
                Length = length,
                RiskLevel = candidate.RiskLevel,
                Status = candidate.Status,
                Reason = string.IsNullOrWhiteSpace(candidate.Reason)
                    ? "Free subrange after subtracting blocked, reserved, and manifest allocation ranges."
                    : candidate.Reason
            };
        }
    }

    private static CodeCaveAllocationResult Fail(string status, string reason, List<ExeCodeCaveCandidate> considered)
        => new()
        {
            Success = false,
            Status = status,
            Reason = reason,
            ConsideredCandidates = considered
        };

    private static string NormalizePolicy(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "largest-fit" or "largest" ? "largest-fit" : "smallest-fit";
    }

    private static bool CandidateFillAllowed(ExeCodeCaveCandidate candidate, CodeCaveAllocationRequest request)
    {
        if (candidate.FillKind.Equals("nop", StringComparison.OrdinalIgnoreCase)) return true;
        if (candidate.FillKind.Equals("zero", StringComparison.OrdinalIgnoreCase)) return request.AllowZeroFillCave;
        if (candidate.FillKind.Equals("mixed", StringComparison.OrdinalIgnoreCase)) return request.AllowMixedFillCave;
        return false;
    }

    private static bool RangesIntersect(uint leftStart, uint leftEnd, uint rightStart, uint rightEnd)
        => leftStart <= rightEnd && rightStart <= leftEnd;

    private static uint ResolveAddress(EffectPatchSegment segment)
    {
        if (segment.Address != 0) return segment.Address;
        var text = (segment.AddressHex ?? string.Empty).Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var value)
            ? value
            : 0;
    }

    private static int ParseHexByteCount(string text)
    {
        var count = text.Count(Uri.IsHexDigit);
        if (count == 0) return 0;
        if (count % 2 != 0) throw new InvalidOperationException("Patch segment has odd hex length.");
        return count / 2;
    }

    private static bool TargetFileMatches(string value, string targetFileName)
        => FirstNonEmpty(value, "Ekd5.exe").Equals(FirstNonEmpty(targetFileName, "Ekd5.exe"), StringComparison.OrdinalIgnoreCase);

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private sealed record RangeBlock(uint Start, uint End, string Id, string Reason);
}
