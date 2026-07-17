using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using CCZModStudio.Models;
using CCZModStudio.Core;

namespace CCZModStudio.GameDebugMcpServer;

public sealed partial class GameDebugRuntime
{
    public object CreateEffectProbeSession(string contractId, string contractHash, int effectId, string? gameRoot,
        string? contractCodeIdentityHash = null, string? profileId = null, string? normalizedProfileIdentity = null,
        int contractVersion = 2, EffectValidationRecipe? validationRecipe = null, string? baseExeSha256 = null,
        string? sandboxPatchSha256 = null, string? probePackageHash = null, uint continuationAddress = 0,
        string? evidenceDisposition = null)
    {
        var paths = ResolveGamePaths(gameRoot, requireExpectedHash: false);
        var sandbox = EnsureEffectEvidenceTarget(paths);
        var normalizedContract = NormalizeEffectContractId(contractId);
        var disposition = string.IsNullOrWhiteSpace(evidenceDisposition)
            ? EffectEvidenceDispositions.StoreAndPromote
            : evidenceDisposition.Trim();
        if (!string.IsNullOrWhiteSpace(baseExeSha256) &&
            !baseExeSha256.Equals(sandbox.OriginalExeSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("验证会话的基础 SHA 与签名沙箱来源不一致。");
        if (!string.IsNullOrWhiteSpace(sandboxPatchSha256) &&
            !sandboxPatchSha256.Equals(paths.ExeSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("验证会话的沙箱补丁 SHA 与当前副本不一致。");
        if (disposition.Equals(EffectEvidenceDispositions.ValidationOnly, StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(probePackageHash))
            throw new InvalidOperationException("仅验证行为契约必须绑定实际应用到沙箱的补丁包哈希。");
        var sessionRoot = Path.Combine(paths.GameRoot, "CCZModStudio_Notes", "EffectContractEvidence", paths.ExeSha256,
            normalizedContract, "probe-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(sessionRoot);
        var recipe = NormalizeValidationRecipe(normalizedContract, validationRecipe);
        var scenarios = recipe.Scenarios.Select(item => (item.ScenarioId, item.DisplayNameZh, item.InstructionZh)).ToList();
        var batches = BuildEffectProbeBatches(normalizedContract, effectId, scenarios, recipe);
        var plan = new
        {
            SchemaVersion = "effect-probe-plan-v1",
            ContractId = normalizedContract,
            ContractHash = contractHash,
            ContractVersion = contractVersion,
            ValidationRecipe = recipe,
            TargetExeSha256 = paths.ExeSha256,
            BaseExeSha256 = baseExeSha256 ?? string.Empty,
            SandboxPatchSha256 = sandboxPatchSha256 ?? paths.ExeSha256,
            PatchPackageHash = probePackageHash ?? string.Empty,
            ContinuationAddress = continuationAddress,
            EvidenceDisposition = disposition,
            Batches = batches
        };
        var planPath = Path.Combine(sessionRoot, "effect-probe-plan.json");
        WriteJson(planPath, plan);
        var session = new EffectProbeSession
        {
            SessionId = Path.GetFileName(sessionRoot), ContractId = normalizedContract, ContractHash = contractHash,
            ContractVersion = contractVersion, ContractCodeIdentityHash = contractCodeIdentityHash ?? string.Empty,
            ProfileId = profileId ?? EngineEffectProfileRegistry.Profile65Id,
            NormalizedProfileIdentity = normalizedProfileIdentity ?? EngineEffectProfileRegistry.Canonical65Sha256,
            ProjectId = ComputeEffectProjectId(paths), GameRoot = paths.GameRoot, ExePath = paths.ExePath,
            ExeSha256 = paths.ExeSha256, EffectId = effectId, ValidationRecipe = recipe,
            BaseExeSha256 = baseExeSha256 ?? string.Empty, SandboxPatchSha256 = sandboxPatchSha256 ?? paths.ExeSha256,
            ProbePackageHash = probePackageHash ?? string.Empty, ContinuationAddress = continuationAddress,
            EvidenceDisposition = disposition,
            SessionRoot = sessionRoot, PlanPath = planPath, CreatedAtUtc = DateTime.UtcNow,
            Scenarios = scenarios.Select((item, index) => new EffectProbeScenarioState
            {
                ScenarioId = item.ScenarioId, DisplayNameZh = item.DisplayNameZh,
                InstructionZh = item.InstructionZh, BatchIndex = index + 1
            }).ToList()
        };
        var sessionPath = Path.Combine(sessionRoot, "effect-probe-session.json");
        WriteJson(sessionPath, session);
        return new { session, session_path = sessionPath, plan_path = planPath, batches, safety_zh = "只创建当前基底临时副本的断点与证据目录，不写游戏文件或进程内存。" };
    }

    public object RunEffectProbeBatch(string sessionPath, int batchIndex, string hostName, int port, bool clearHardwareFirst)
    {
        var session = ReadEffectProbeSession(sessionPath);
        var result = DebugBreakpointPlanApply(session.PlanPath, batchIndex, hostName, port, clearHardwareFirst);
        return new { session.SessionId, batch_index = batchIndex, apply_result = result, instruction_zh = session.Scenarios.FirstOrDefault(item => item.BatchIndex == batchIndex)?.InstructionZh };
    }

    public object CaptureEffectProbeScenario(string sessionPath, string scenarioId, string hostName, int port)
    {
        GuardLocalHost(hostName);
        var session = ReadEffectProbeSession(sessionPath);
        var scenario = session.Scenarios.FirstOrDefault(item => item.ScenarioId.Equals(scenarioId, StringComparison.OrdinalIgnoreCase))
                       ?? throw new ArgumentException("探针会话中没有该场景：" + scenarioId);
        var health = InvokeX32dbg("GET", hostName, port, "/api/health");
        var state = InvokeX32dbg("GET", hostName, port, "/api/debug/state");
        var registers = InvokeX32dbg("GET", hostName, port, "/api/registers/all");
        var cip = TryReadCip(state);
        var esp = TryReadRegister(registers, "esp");
        var ebp = TryReadRegister(registers, "ebp");
        var ecx = TryReadRegister(registers, "ecx");
        if (!health.Ok || !state.Ok || !registers.Ok || string.IsNullOrWhiteSpace(cip))
            throw new InvalidOperationException("x32dbg 未连接、游戏未暂停或寄存器不可读，不能采集可信场景。");
        if (!TryParseAddress(cip, out var instructionPointer))
            throw new InvalidOperationException("调试器返回的 CIP 不是有效的 32 位地址。");
        var capturePoint = ResolveCapturePoint(session.ValidationRecipe, instructionPointer)
                           ?? throw new InvalidOperationException($"当前暂停地址 0x{instructionPointer:X8} 不属于验证配方声明的采集点。");
        var existingHits = scenario.Captures.Count(item =>
            item.CapturePointId.Equals(capturePoint.CapturePointId, StringComparison.OrdinalIgnoreCase));
        if (ScenarioDefinition(session, scenario).AllowedMaximumHits.TryGetValue(capturePoint.CapturePointId, out var maximumHits) &&
            existingHits >= maximumHits)
            throw new InvalidOperationException($"场景“{scenario.DisplayNameZh}”的采集点“{capturePoint.CapturePointId}”已达到最大命中次数 {maximumHits}。");
        var hitIndex = existingHits + 1;
        var eax = TryReadRegister(registers, "eax");
        var edx = TryReadRegister(registers, "edx");
        var normalized = BuildTrustedObservations(session, scenario.ScenarioId, capturePoint, cip, ebp, ecx, eax, edx, hostName, port);
        var battleState = SafeReadBattleState();
        var stackTrace = InvokeX32dbg("GET", hostName, port, "/api/stack/trace", new Dictionary<string, string> { ["max_depth"] = "48" });
        AddRelationshipObservations(session, normalized, battleState, stackTrace);
        var report = new
        {
            schema_version = "effect-probe-capture-v1",
            created_at = DateTimeOffset.Now.ToString("O"),
            session_id = session.SessionId,
            contract_id = session.ContractId,
            contract_hash = session.ContractHash,
            scenario_id = scenario.ScenarioId,
            capture_point_id = capturePoint.CapturePointId,
            capture_point_address = $"0x{capturePoint.Address:X8}",
            hit_index = hitIndex,
            expected_exe_sha256 = session.ExeSha256,
            cip,
            health,
            state,
            process = InvokeX32dbg("GET", hostName, port, "/api/process/info"),
            registers,
            normalized_observations = normalized,
            stack_trace = stackTrace,
            stack_read = string.IsNullOrWhiteSpace(esp) ? null : InvokeX32dbg("GET", hostName, port, "/api/stack/read", new Dictionary<string, string> { ["address"] = esp, ["size"] = "128" }),
            disasm_at_cip = InvokeX32dbg("GET", hostName, port, "/api/disasm/at", new Dictionary<string, string> { ["address"] = cip, ["count"] = "48" }),
            function_at_cip = InvokeX32dbg("GET", hostName, port, "/api/disasm/function", new Dictionary<string, string> { ["address"] = cip, ["max_instructions"] = "128" }),
            native_table_row = session.ContractId == "personal-job-binding-query-v1"
                ? InvokeX32dbg("GET", hostName, port, "/api/memory/read", new Dictionary<string, string> { ["address"] = "00507800", ["size"] = "2040" })
                : null,
            battle_state = battleState,
            safety_zh = "只读采集寄存器、栈、反汇编、原生表和战斗状态。"
        };
        var capturePath = Path.Combine(session.SessionRoot,
            $"effect-capture-{scenario.ScenarioId}-{capturePoint.CapturePointId}-hit{hitIndex}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
        WriteJson(capturePath, report);
        scenario.Captures.Add(new EffectProbeCaptureState
        {
            CapturePointId = capturePoint.CapturePointId,
            Address = capturePoint.Address,
            HitIndex = hitIndex,
            CapturePath = capturePath,
            CreatedAtUtc = DateTime.UtcNow
        });
        if (string.IsNullOrWhiteSpace(scenario.CapturePath)) scenario.CapturePath = capturePath;
        scenario.Captured = IsScenarioComplete(session, scenario);
        WriteJson(Path.GetFullPath(sessionPath), session);
        return new
        {
            capture_path = capturePath,
            scenario = scenario.ScenarioId,
            capture_point = capturePoint.CapturePointId,
            hit_index = hitIndex,
            scenario_complete = scenario.Captured,
            cip
        };
    }

    public object ReadEffectProbeProgress(string sessionPath)
    {
        var session = ReadEffectProbeSession(sessionPath);
        RefreshEffectProbeCaptures(session);
        WriteJson(Path.GetFullPath(sessionPath), session);
        return new
        {
            session.SessionId, session.ContractId, session.ExeSha256,
            completed = session.Scenarios.Count(item => item.Captured), total = session.Scenarios.Count,
            scenarios = session.Scenarios,
            ready_to_finalize = session.Scenarios.All(item => IsScenarioComplete(session, item))
        };
    }

    public object FinalizeEffectEvidenceBundle(string sessionPath, int processId, string debuggerVersion)
    {
        var session = ReadEffectProbeSession(sessionPath);
        RefreshEffectProbeCaptures(session);
        if (!session.Scenarios.All(item => item.Captured))
            throw new InvalidOperationException("四类动态场景尚未全部采集，不能签发可信证据包。");
        var exe = Path.GetFullPath(session.ExePath);
        if (!File.Exists(exe) || !ComputeSha256(exe).Equals(session.ExeSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("临时副本 EXE 已变化，不能签发证据包。");
        if (processId <= 0) throw new ArgumentOutOfRangeException(nameof(processId), "必须提供当前调试游戏进程 ID。");
        using var process = Process.GetProcessById(processId);
        var processPath = process.MainModule?.FileName ?? string.Empty;
        if (!Path.GetFullPath(processPath).Equals(exe, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("调试进程加载的 Ekd5.exe 不是当前探针会话的临时副本。");
        var rawFiles = CapturePaths(session).ToList();
        if (session.ContractVersion < 2 || string.IsNullOrWhiteSpace(session.ContractCodeIdentityHash) || string.IsNullOrWhiteSpace(session.NormalizedProfileIdentity))
            throw new InvalidOperationException("探针会话缺少契约 v2 的代码身份或规范档案身份，不能签发 v2 证据。");
        var bundle = new EffectEvidenceBundleV2
        {
            BundleId = session.SessionId + "-bundle", ContractId = session.ContractId, ContractHash = session.ContractHash,
            ContractVersion = session.ContractVersion, ContractCodeIdentityHash = session.ContractCodeIdentityHash,
            ProfileId = session.ProfileId, NormalizedProfileIdentity = session.NormalizedProfileIdentity,
            ProjectId = session.ProjectId, GameRoot = session.GameRoot, SessionRoot = session.SessionRoot,
            ProcessId = processId, ProcessPath = processPath, LoadedModulePath = processPath,
            LoadedModuleSize = new FileInfo(processPath).Length, LoadedModuleSha256 = ComputeSha256(processPath),
            DebuggerVersion = debuggerVersion,
            ToolBuildId = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                          ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
            CreatedAtUtc = DateTime.UtcNow, CompletedScenarioIds = session.Scenarios.Select(item => item.ScenarioId).ToList(),
            RawFiles = rawFiles.Select(path => new EffectEvidenceRawFile
            {
                ScenarioId = ScenarioIdForCapturePath(session, path),
                RelativePath = Path.GetRelativePath(session.SessionRoot, path), Length = new FileInfo(path).Length,
                Sha256 = EffectEvidenceBundleCrypto.ComputeFileSha256(path)
            }).ToList(),
            DerivedObservations = DeriveSignedObservations(session)
        };
        EffectEvidenceBundleCrypto.Sign(bundle);
        var bundlePath = Path.Combine(session.SessionRoot, bundle.BundleId + ".json");
        File.WriteAllText(bundlePath, EffectEvidenceBundleCrypto.Serialize(bundle), Encoding.UTF8);
        WriteJson(Path.GetFullPath(sessionPath), session);
        return new { bundle_path = bundlePath, bundle, safety_zh = "证据包已绑定原始文件摘要、当前进程、加载模块 SHA 和当前 Windows 用户签名。" };
    }

    public object FinalizeEffectEvidenceBundleV3(
        string sessionPath,
        int processId,
        string debuggerVersion,
        string hostName,
        int port)
    {
        GuardLocalHost(hostName);
        var session = ReadEffectProbeSession(sessionPath);
        var paths = ResolveGamePaths(session.GameRoot, requireExpectedHash: false);
        var sandbox = EnsureEffectEvidenceTarget(paths);
        RefreshEffectProbeCaptures(session);
        if (!session.Scenarios.All(item => item.Captured))
            throw new InvalidOperationException("验证场景尚未全部采集，不能签发 V3 证据包。");
        var ids = session.Scenarios.Select(item => item.ScenarioId).ToArray();
        var requiredRoles = new[]
        {
            EffectValidationScenarioRoles.Normal,
            EffectValidationScenarioRoles.MinimumBoundary,
            EffectValidationScenarioRoles.MaximumBoundary,
            EffectValidationScenarioRoles.Negative
        };
        if (requiredRoles.Any(role => session.ValidationRecipe.Scenarios.All(item =>
                !item.ScenarioRole.Equals(role, StringComparison.Ordinal))))
            throw new InvalidOperationException("V3 验证必须包含正常、最小、最大和负向场景。");
        if (session.ContractVersion < 2 || string.IsNullOrWhiteSpace(session.ContractCodeIdentityHash) ||
            string.IsNullOrWhiteSpace(session.NormalizedProfileIdentity))
            throw new InvalidOperationException("验证会话缺少契约 v2 的代码身份或规范化档案身份。");

        var exe = Path.GetFullPath(session.ExePath);
        if (!File.Exists(exe) || !ComputeSha256(exe).Equals(session.ExeSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("验证副本 EXE 已在会话期间发生磁盘变化。");
        if (processId <= 0) throw new ArgumentOutOfRangeException(nameof(processId));
        using var process = Process.GetProcessById(processId);
        var processPath = process.MainModule?.FileName ?? string.Empty;
        if (!Path.GetFullPath(processPath).Equals(exe, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("调试进程不是当前签名验证副本的 Ekd5.exe。");

        var addresses = EffectProbeAddresses(session);
        var restoreCommands = new List<object>();
        foreach (var address in addresses)
        {
            restoreCommands.Add(new
            {
                address = $"0x{address:X8}",
                software = InvokeX32dbg("POST", hostName, port, "/api/command/exec", null, new { command = $"bc {address:X8}" }),
                hardware = InvokeX32dbg("POST", hostName, port, "/api/command/exec", null, new { command = $"bphwc {address:X8}" })
            });
        }
        var addressChecks = addresses.Select(address =>
        {
            var memory = InvokeX32dbg("GET", hostName, port, "/api/memory/read",
                new Dictionary<string, string> { ["address"] = address.ToString("X8"), ["size"] = "1" });
            var expected = ReadExeByteAtVirtualAddress(exe, address);
            var matches = TryReadMemoryHex(memory, out var actual) && actual.Length == 1 && actual[0] == expected;
            return new { address = $"0x{address:X8}", expected = $"{expected:X2}", matches, memory };
        }).ToList();
        var breakpointList = InvokeX32dbg("GET", hostName, port, "/api/breakpoints/list");
        var breakpointJson = JsonSerializer.Serialize(breakpointList, JsonOptions);
        var restored = addressChecks.All(item => item.matches) &&
                       addresses.All(address => !breakpointJson.Contains(address.ToString("X8"), StringComparison.OrdinalIgnoreCase));
        var restorePath = Path.Combine(session.SessionRoot, "effect-probe-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".json");
        WriteJson(restorePath, new
        {
            scenario_id = "probe-restore",
            created_at = DateTimeOffset.Now.ToString("O"),
            session_id = session.SessionId,
            restored,
            commands = restoreCommands,
            address_checks = addressChecks,
            breakpoint_list = breakpointList,
            disk_sha256 = ComputeSha256(exe)
        });
        if (!restored) throw new InvalidOperationException("断点或硬件探针未完整恢复，V3 证据拒绝签发。");

        var capturePaths = CapturePaths(session)
            .Append(session.PlanPath)
            .Append(restorePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var rawFiles = capturePaths.Select(path => new EffectEvidenceRawFile
        {
            ScenarioId = path.Equals(restorePath, StringComparison.OrdinalIgnoreCase)
                ? "probe-restore"
                : path.Equals(session.PlanPath, StringComparison.OrdinalIgnoreCase)
                    ? "probe-plan"
                : ScenarioIdForCapturePath(session, path),
            RelativePath = Path.GetRelativePath(session.SessionRoot, path),
            Length = new FileInfo(path).Length,
            Sha256 = EffectEvidenceBundleCrypto.ComputeFileSha256(path)
        }).ToList();
        var observations = DeriveSignedObservations(session);
        var writableObservations = observations.Where(item => IsWritableBoundaryObservation(session, item.Key)).ToList();
        if (writableObservations.Count == 0 || writableObservations.Any(item =>
                item.VerifiedMinimum is null || item.VerifiedMaximum is null || string.IsNullOrWhiteSpace(item.BoundaryEvidenceZh)))
            throw new InvalidOperationException("场景没有形成可复算的完整上下界观测。");
        var relationships = BuildRelationshipAssertions(session, observations);
        var requiredRelationshipRules = session.ValidationRecipe.RelationshipRules.Where(item => item.Required).ToList();
        if (relationships.Count == 0 || requiredRelationshipRules.Any(rule => relationships.All(item =>
                !item.Relationship.Equals(rule.RuleId, StringComparison.OrdinalIgnoreCase) || !item.Verified)))
            throw new InvalidOperationException("验证配方要求的指针关系没有形成跨采集点、正向场景与负向场景一致的关系断言。");

        var probeOldBytesToken = string.Join("|", addresses.Select(address => $"{address:X8}:{ReadExeByteAtVirtualAddress(exe, address):X2}"));
        var probePlanHash = EffectEvidenceBundleCrypto.ComputeFileSha256(session.PlanPath);
        var bundle = new EffectEvidenceBundleV3
        {
            BundleId = session.SessionId + "-bundle-v3",
            ContractId = session.ContractId,
            ContractVersion = session.ContractVersion,
            ContractHash = session.ContractHash,
            ContractCodeIdentityHash = session.ContractCodeIdentityHash,
            ProfileId = session.ProfileId,
            NormalizedProfileIdentity = session.NormalizedProfileIdentity,
            ProjectId = ComputeOriginalEffectProjectId(sandbox, new FileInfo(exe).Length),
            OriginalGameRoot = sandbox.OriginalGameRoot,
            SandboxRoot = sandbox.SandboxRoot,
            SessionRoot = session.SessionRoot,
            OriginalExeSha256 = sandbox.OriginalExeSha256,
            BaseExeSha256 = session.BaseExeSha256,
            SandboxPatchSha256 = session.SandboxPatchSha256,
            PatchPackageHash = session.ProbePackageHash,
            ProbePlanHash = probePlanHash,
            ContinuationAddress = session.ContinuationAddress,
            EvidenceDisposition = session.EvidenceDisposition,
            LoadedModulePath = processPath,
            LoadedModuleSize = new FileInfo(processPath).Length,
            LoadedModuleSha256 = ComputeSha256(processPath),
            ProcessId = processId,
            ProcessPath = processPath,
            DebuggerVersion = debuggerVersion,
            ToolBuildId = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                          ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
            ValidationRecipeId = session.ValidationRecipe.RecipeId,
            ValidationRecipeHash = EffectEvidenceBundleCrypto.ComputeValidationRecipeHash(session.ValidationRecipe),
            ProbePackageHash = probePlanHash,
            ProbeExpectedOldBytesHash = Sha256Text(probeOldBytesToken),
            ProbeRestored = true,
            ProbeRestoreEvidencePath = Path.GetRelativePath(session.SessionRoot, restorePath),
            CreatedAtUtc = DateTime.UtcNow,
            CompletedScenarioIds = ids.ToList(),
            RawFiles = rawFiles,
            DerivedObservations = observations,
            RelationshipAssertions = relationships
        };
        EffectEvidenceBundleCrypto.Sign(bundle);
        var bundlePath = Path.Combine(session.SessionRoot, bundle.BundleId + ".json");
        File.WriteAllText(bundlePath, EffectEvidenceBundleCrypto.Serialize(bundle), Encoding.UTF8);
        WriteJson(Path.GetFullPath(sessionPath), session);
        return new { bundle_path = bundlePath, bundle, probe_restored = true };
    }

    public int ResolveEffectValidationProcessId(string sessionPath)
    {
        var session = ReadEffectProbeSession(sessionPath);
        var expected = Path.GetFullPath(session.ExePath);
        var process = FindTargetProcess() ?? throw new InvalidOperationException("验证副本 Ekd5.exe 尚未启动。");
        var actual = process.MainModule?.FileName ?? string.Empty;
        if (!Path.GetFullPath(actual).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("当前 Ekd5.exe 进程不是该验证会话的自动副本。");
        return process.Id;
    }

    private static EffectProbeSession ReadEffectProbeSession(string sessionPath)
    {
        var path = Path.GetFullPath(sessionPath);
        if (!File.Exists(path)) throw new FileNotFoundException("找不到特效探针会话。", path);
        return JsonSerializer.Deserialize<EffectProbeSession>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
               ?? throw new InvalidOperationException("特效探针会话无法读取。");
    }

    private static EffectValidationRecipe NormalizeValidationRecipe(
        string contractId,
        EffectValidationRecipe? recipe)
    {
        var fallbackScenarios = BuildEffectProbeScenarios(contractId);
        var scenarios = recipe?.Scenarios?
            .Where(item => !string.IsNullOrWhiteSpace(item.ScenarioId))
            .Select(item => new EffectValidationScenarioDefinition
            {
                ScenarioId = item.ScenarioId.Trim(),
                DisplayNameZh = item.DisplayNameZh,
                InstructionZh = item.InstructionZh,
                ExpectedTransition = item.ExpectedTransition,
                ScenarioRole = item.ScenarioRole,
                RequiredMinimumHits = new Dictionary<string, int>(item.RequiredMinimumHits, StringComparer.OrdinalIgnoreCase),
                AllowedMaximumHits = new Dictionary<string, int>(item.AllowedMaximumHits, StringComparer.OrdinalIgnoreCase)
            })
            .ToList() ?? [];
        if (scenarios.Count == 0)
        {
            scenarios = fallbackScenarios.Select(item => new EffectValidationScenarioDefinition
            {
                ScenarioId = item.Id,
                DisplayNameZh = item.NameZh,
                InstructionZh = item.InstructionZh,
                ScenarioRole = DefaultScenarioRole(contractId, item.Id)
            }).ToList();
        }

        var addresses = recipe?.BreakpointAddresses?
            .Where(address => address != 0)
            .Distinct()
            .ToList() ?? [];
        if (addresses.Count == 0)
        {
            addresses = EffectProbeAddresses(contractId).ToList();
        }

        var capturePoints = recipe?.CapturePoints?
            .Where(item => !string.IsNullOrWhiteSpace(item.CapturePointId) && item.Address != 0)
            .Select(item => new EffectValidationCapturePointDefinition
            {
                CapturePointId = item.CapturePointId.Trim(),
                Address = item.Address,
                ExtractorId = string.IsNullOrWhiteSpace(item.ExtractorId) ? "generic" : item.ExtractorId.Trim(),
                Optional = item.Optional
            })
            .GroupBy(item => item.CapturePointId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList() ?? [];
        if (capturePoints.Count == 0)
        {
            capturePoints = addresses.Select(address => new EffectValidationCapturePointDefinition
            {
                CapturePointId = $"address-{address:X8}",
                Address = address,
                ExtractorId = "generic"
            }).ToList();
        }
        addresses = capturePoints.Select(item => item.Address).Distinct().ToList();

        var requiredObservationKeys = recipe?.RequiredObservationKeys?
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        if (requiredObservationKeys.Count == 0)
        {
            requiredObservationKeys = contractId.StartsWith("strategy-damage", StringComparison.OrdinalIgnoreCase)
                ? ["strategy-effect-subject", "strategy-current-damage", "strategy-target-context"]
                : contractId.StartsWith("physical-after-damage", StringComparison.OrdinalIgnoreCase)
                    ? ["physical-context", "physical-attacker-unit", "physical-attacker-character", "physical-current-damage", "current-mp", "maximum-mp"]
                    : ["effect-id", "job-id", "effect-value"];
        }

        var requiredRelationshipSlots = recipe?.RequiredRelationshipSlots?
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        if (requiredRelationshipSlots.Count == 0)
        {
            requiredRelationshipSlots = contractId.StartsWith("strategy-damage", StringComparison.OrdinalIgnoreCase)
                ? ["strategy-effect-subject"]
                : contractId.StartsWith("physical-after-damage", StringComparison.OrdinalIgnoreCase)
                    ? ["physical-effect-subject", "physical-attacker-unit", "physical-attacker-character"]
                    : [];
        }

        var writableBoundaryKeys = recipe?.WritableBoundaryKeys?
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        if (writableBoundaryKeys.Count == 0)
        {
            writableBoundaryKeys = contractId.StartsWith("strategy-damage", StringComparison.OrdinalIgnoreCase)
                ? ["strategy-current-damage"]
                : contractId.StartsWith("physical-after-damage", StringComparison.OrdinalIgnoreCase)
                    ? ["current-mp"]
                    : [];
        }

        var relationshipRules = recipe?.RelationshipRules?
            .Where(item => !string.IsNullOrWhiteSpace(item.RuleId) &&
                           !string.IsNullOrWhiteSpace(item.LeftObservationKey) &&
                           !string.IsNullOrWhiteSpace(item.RightObservationKey))
            .Select(item => new EffectValidationRelationshipRule
            {
                RuleId = item.RuleId.Trim(),
                LeftObservationKey = item.LeftObservationKey.Trim(),
                RightObservationKey = item.RightObservationKey.Trim(),
                RelationshipKind = string.IsNullOrWhiteSpace(item.RelationshipKind) ? "PointerEquality" : item.RelationshipKind.Trim(),
                Required = item.Required
            })
            .ToList() ?? [];

        return new EffectValidationRecipe
        {
            RecipeId = string.IsNullOrWhiteSpace(recipe?.RecipeId)
                ? $"ccz65-{contractId}-probe-v1"
                : recipe.RecipeId.Trim(),
            RecipeVersion = Math.Max(1, recipe?.RecipeVersion ?? 1),
            BreakpointAddresses = addresses,
            Scenarios = scenarios,
            RequiredObservationKeys = requiredObservationKeys,
            RequiredRelationshipSlots = requiredRelationshipSlots,
            WritableBoundaryKeys = writableBoundaryKeys,
            CapturePoints = capturePoints,
            RelationshipRules = relationshipRules
        };
    }

    private static void RefreshEffectProbeCaptures(EffectProbeSession session)
    {
        var files = Directory.GetFiles(session.SessionRoot, "*.json", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path).StartsWith("effect-capture-", StringComparison.OrdinalIgnoreCase) ||
                           Path.GetFileName(path).StartsWith("internal-probe-hit-", StringComparison.OrdinalIgnoreCase))
            .OrderBy(File.GetLastWriteTimeUtc).ToList();
        foreach (var scenario in session.Scenarios)
        {
            foreach (var match in files.Where(path => Path.GetFileName(path).Contains(scenario.ScenarioId, StringComparison.OrdinalIgnoreCase)))
            {
                if (scenario.Captures.Any(item => Path.GetFullPath(item.CapturePath).Equals(Path.GetFullPath(match), StringComparison.OrdinalIgnoreCase)))
                    continue;
                var capture = ReadCaptureState(session, scenario, match);
                if (capture != null) scenario.Captures.Add(capture);
            }
            scenario.Captures = scenario.Captures
                .OrderBy(item => item.CreatedAtUtc)
                .ThenBy(item => item.CapturePointId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.HitIndex)
                .ToList();
            if (string.IsNullOrWhiteSpace(scenario.CapturePath) && scenario.Captures.Count > 0)
                scenario.CapturePath = scenario.Captures[0].CapturePath;
            scenario.Captured = IsScenarioComplete(session, scenario);
        }
    }

    private static EffectValidationScenarioDefinition ScenarioDefinition(
        EffectProbeSession session,
        EffectProbeScenarioState scenario)
        => session.ValidationRecipe.Scenarios.FirstOrDefault(item =>
               item.ScenarioId.Equals(scenario.ScenarioId, StringComparison.OrdinalIgnoreCase))
           ?? new EffectValidationScenarioDefinition { ScenarioId = scenario.ScenarioId };

    private static EffectValidationCapturePointDefinition? ResolveCapturePoint(
        EffectValidationRecipe recipe,
        uint address)
        => recipe.CapturePoints.FirstOrDefault(item => item.Address == address);

    private static bool IsScenarioComplete(EffectProbeSession session, EffectProbeScenarioState scenario)
    {
        var definition = ScenarioDefinition(session, scenario);
        if (definition.RequiredMinimumHits.Count == 0)
            return scenario.Captures.Count > 0 || (!string.IsNullOrWhiteSpace(scenario.CapturePath) && File.Exists(scenario.CapturePath));
        return definition.RequiredMinimumHits.All(requirement =>
            scenario.Captures.Count(item =>
                item.CapturePointId.Equals(requirement.Key, StringComparison.OrdinalIgnoreCase)) >= requirement.Value);
    }

    private static EffectProbeCaptureState? ReadCaptureState(
        EffectProbeSession session,
        EffectProbeScenarioState scenario,
        string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            var root = document.RootElement;
            var capturePointId = root.TryGetProperty("capture_point_id", out var pointProperty)
                ? pointProperty.GetString() ?? string.Empty
                : string.Empty;
            var address = 0u;
            if (string.IsNullOrWhiteSpace(capturePointId) && root.TryGetProperty("cip", out var cipProperty))
            {
                var cip = cipProperty.ValueKind == JsonValueKind.String ? cipProperty.GetString() : cipProperty.GetRawText();
                if (TryParseAddress(cip, out address))
                    capturePointId = ResolveCapturePoint(session.ValidationRecipe, address)?.CapturePointId ?? string.Empty;
            }
            var capturePoint = session.ValidationRecipe.CapturePoints.FirstOrDefault(item =>
                item.CapturePointId.Equals(capturePointId, StringComparison.OrdinalIgnoreCase));
            if (capturePoint == null) return null;
            address = capturePoint.Address;
            var hitIndex = root.TryGetProperty("hit_index", out var hitProperty) && hitProperty.TryGetInt32(out var parsedHit)
                ? parsedHit
                : scenario.Captures.Count(item => item.CapturePointId.Equals(capturePointId, StringComparison.OrdinalIgnoreCase)) + 1;
            return new EffectProbeCaptureState
            {
                CapturePointId = capturePointId,
                Address = address,
                HitIndex = Math.Max(1, hitIndex),
                CapturePath = path,
                CreatedAtUtc = File.GetLastWriteTimeUtc(path)
            };
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> CapturePaths(EffectProbeSession session)
        => session.Scenarios
            .SelectMany(item => item.Captures.Count > 0
                ? item.Captures.Select(capture => capture.CapturePath)
                : [item.CapturePath])
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static string ScenarioIdForCapturePath(EffectProbeSession session, string path)
        => session.Scenarios.First(item =>
                item.Captures.Any(capture => capture.CapturePath.Equals(path, StringComparison.OrdinalIgnoreCase)) ||
                item.CapturePath.Equals(path, StringComparison.OrdinalIgnoreCase))
            .ScenarioId;

    private static string DefaultScenarioRole(string contractId, string scenarioId)
        => scenarioId switch
        {
            "physical-critical" => EffectValidationScenarioRoles.Critical,
            "physical-counter" => EffectValidationScenarioRoles.Counter,
            "physical-combo" => EffectValidationScenarioRoles.Combo,
            "physical-mp-zero-boundary" or "physical-minimum-boundary" or "strategy-minimum-boundary" => EffectValidationScenarioRoles.MinimumBoundary,
            "physical-mp-maximum-boundary" or "physical-maximum-boundary" or "strategy-maximum-boundary" => EffectValidationScenarioRoles.MaximumBoundary,
            "physical-negative" or "strategy-negative" or "native-miss" => EffectValidationScenarioRoles.Negative,
            _ => EffectValidationScenarioRoles.Normal
        };

    private static List<EffectProbeBatch> BuildEffectProbeBatches(
        string contractId,
        int effectId,
        IReadOnlyList<(string Id, string NameZh, string InstructionZh)> scenarios,
        EffectValidationRecipe recipe)
    {
        var commands = recipe.BreakpointAddresses
            .Where(address => address != 0)
            .Distinct()
            .Select(address => $"bp {address:X8}")
            .ToList();
        if (contractId == "personal-job-binding-query-v1")
        {
            if (effectId is < 0 or > 0xFE) throw new ArgumentOutOfRangeException(nameof(effectId));
            var row = 0x00507800u + (uint)(effectId * 8);
            commands.Insert(0, $"bphws {row + 4:X8},r,4");
            commands.Insert(0, $"bphws {row:X8},r,4");
        }
        return scenarios.Select((item, index) => new EffectProbeBatch
        {
            BatchIndex = index + 1, ScenarioId = item.Id, DisplayNameZh = item.NameZh,
            X32dbgCommands = commands.ToList()
        }).ToList();
    }

    private static Dictionary<string, string> BuildTrustedObservations(
        EffectProbeSession session,
        string scenarioId,
        EffectValidationCapturePointDefinition capturePoint,
        string? cipText,
        string? ebpText,
        string? ecxText,
        string? eaxText,
        string? edxText,
        string hostName,
        int port)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (TryParseAddress(cipText, out var cip)) result["probe-cip"] = $"0x{cip:X8}";
        if (TryParseAddress(eaxText, out var eax)) result["register-eax"] = $"0x{eax:X8}";
        if (TryParseAddress(edxText, out var edx)) result["register-edx"] = $"0x{edx:X8}";
        result["scenario-id"] = scenarioId;
        result["capture-point-id"] = capturePoint.CapturePointId;
        if (session.ContractId.StartsWith("strategy-damage", StringComparison.OrdinalIgnoreCase) && TryParseAddress(ebpText, out var ebp))
        {
            AddMemoryDword(result, "strategy-effect-subject", checked(ebp - 0x10), hostName, port);
            AddMemoryDword(result, "strategy-current-damage", checked(ebp - 0x04), hostName, port);
            AddMemoryDword(result, "strategy-target-context", checked(ebp + 0x08), hostName, port);
        }
        else if (session.ContractId.StartsWith("physical-after-damage", StringComparison.OrdinalIgnoreCase))
        {
            var extractor = capturePoint.ExtractorId.Equals("generic", StringComparison.OrdinalIgnoreCase)
                ? PhysicalExtractorForAddress(capturePoint.Address)
                : capturePoint.ExtractorId;
            switch (extractor)
            {
                case "physical-context":
                case "legacy-effect-check":
                    AddPhysicalContextObservations(result, ebpText, hostName, port);
                    break;
                case "target-damage":
                    if (TryParseAddress(ebpText, out var damageEbp))
                    {
                        AddMemoryDword(result, "physical-target-unit", checked(damageEbp - 0x20), hostName, port);
                        AddMemoryDword(result, "physical-current-damage", checked(damageEbp - 0x14), hostName, port);
                    }
                    break;
                case "hp-write":
                    if (TryParseAddress(ecxText, out var hpWriteUnit))
                        AddPointerObservation(result, "hp-write-unit", hpWriteUnit);
                    if (TryParseAddress(edxText, out var newHp)) result["hp-write-value"] = unchecked((int)newHp).ToString();
                    break;
                case "legacy-mp-write":
                    if (TryParseAddress(edxText, out var mpWriteUnit))
                    {
                        AddPointerObservation(result, "mp-write-unit", mpWriteUnit);
                        AddMemoryDword(result, "current-mp", checked(mpWriteUnit + EngineRuntimeSemanticRegistry.TacticalUnitCurrentMpOffset), hostName, port);
                    }
                    if (TryParseAddress(eaxText, out var restoreAmount)) result["mp-restore-amount"] = unchecked((int)restoreAmount).ToString();
                    break;
            }
        }
        else if (session.ContractId == "personal-job-binding-query-v1" && session.EffectId is >= 0 and <= 0xFE)
        {
            var rowAddress = checked(0x00507800u + (uint)(session.EffectId * 8));
            var row = InvokeX32dbg("GET", hostName, port, "/api/memory/read",
                new Dictionary<string, string> { ["address"] = rowAddress.ToString("X8"), ["size"] = "8" });
            if (TryReadMemoryHex(row, out var bytes) && bytes.Length >= 8)
            {
                result["effect-id"] = session.EffectId.ToString();
                result["job-id"] = bytes[6].ToString();
                result["effect-value"] = bytes[7].ToString();
                result["native-row-person-1"] = BitConverter.ToUInt16(bytes, 0).ToString();
                result["native-row-person-2"] = BitConverter.ToUInt16(bytes, 2).ToString();
                result["native-row-person-3"] = BitConverter.ToUInt16(bytes, 4).ToString();
            }
        }
        return result;
    }

    private static void AddPhysicalContextObservations(
        IDictionary<string, string> result,
        string? ebpText,
        string hostName,
        int port)
    {
        if (!TryParseAddress(ebpText, out var ebp) ||
            !TryReadUnsigned(checked(ebp + 0x08), 4, hostName, port, out var context) ||
            context == 0)
            return;

        AddPointerObservation(result, "physical-context", context);
        if (TryReadUnsigned(checked(context + 0x0C), 4, hostName, port, out var attackerUnit) && attackerUnit != 0)
        {
            AddPointerObservation(result, "physical-attacker-unit", attackerUnit);
            AddPointerObservation(result, "physical-effect-subject", attackerUnit);
            AddMemoryDword(result, "current-mp",
                checked(attackerUnit + EngineRuntimeSemanticRegistry.TacticalUnitCurrentMpOffset), hostName, port);
            if (TryReadUnsigned(checked(attackerUnit + EngineRuntimeSemanticRegistry.TacticalUnitDataIdOffset),
                    EngineRuntimeSemanticRegistry.TacticalUnitDataIdWidth, hostName, port, out var dataId) &&
                dataId != EngineRuntimeSemanticRegistry.EmptyTacticalUnitDataId &&
                TryReadUnsigned(EngineRuntimeSemanticRegistry.RuntimeCharacterPointerSlotAddress, 4, hostName, port, out var characterBase) &&
                characterBase != 0)
            {
                var resolved = checked(characterBase + dataId * EngineRuntimeSemanticRegistry.RuntimeCharacterStride);
                AddPointerObservation(result, "physical-attacker-character-resolved", resolved);
            }
        }
        if (TryReadUnsigned(checked(context + 0x08), 4, hostName, port, out var attackerCharacter) && attackerCharacter != 0)
        {
            AddPointerObservation(result, "physical-attacker-character", attackerCharacter);
            AddMemoryUnsigned(result, "maximum-mp",
                checked(attackerCharacter + EngineRuntimeSemanticRegistry.RuntimeCharacterMaximumMpOffset),
                EngineRuntimeSemanticRegistry.RuntimeCharacterMaximumMpWidth, hostName, port);
        }
        AddMemoryDword(result, "physical-current-damage", checked(context + 0x84), hostName, port);
    }

    private static string PhysicalExtractorForAddress(uint address)
        => address switch
        {
            0x00418335 or 0x004528FC => "physical-context",
            0x0045290A => "legacy-effect-check",
            0x00452975 => "legacy-mp-write",
            0x00405AD5 => "target-damage",
            0x0043F70C => "hp-write",
            _ => "unsupported"
        };

    private void AddRelationshipObservations(
        EffectProbeSession session,
        IDictionary<string, string> values,
        object? battleState,
        X32dbgCallResult stackTrace)
    {
        var pointerKey = session.ContractId.StartsWith("strategy-damage", StringComparison.OrdinalIgnoreCase)
            ? "strategy-effect-subject"
            : "physical-effect-subject";
        values["relationship-call-chain"] = stackTrace.Ok ? "1" : "0";
        if (!values.TryGetValue(pointerKey, out var pointerText) || !int.TryParse(pointerText, out var signedPointer) ||
            battleState is not BattleStateSnapshot snapshot) return;
        var paths = ResolveGamePaths(session.GameRoot, requireExpectedHash: false);
        var profile = DetectRuntimeProfile(RequireTargetProcess(), paths).RequireBattleLayout();
        var pointer = unchecked((uint)signedPointer);
        var unit = snapshot.Units.FirstOrDefault(item =>
            checked(profile.UnitArrayAddress + (uint)(item.UnitIndex * profile.UnitStride)) == pointer);
        if (unit == null) return;
        values["relationship-pointer"] = signedPointer.ToString();
        values["relationship-unit-id"] = unit.UnitIndex.ToString();
        values["relationship-camp"] = unit.Side.ToString();
        values["relationship-hp"] = unit.HP.ToString();
    }

    private static List<EffectEvidenceDerivedObservation> DeriveSignedObservations(EffectProbeSession session)
    {
        var result = new List<EffectEvidenceDerivedObservation>();
        foreach (var scenario in session.Scenarios)
        {
            var paths = scenario.Captures.Count > 0
                ? scenario.Captures.Select(item => item.CapturePath)
                : [scenario.CapturePath];
            foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
                if (!document.RootElement.TryGetProperty("normalized_observations", out var observations) ||
                    observations.ValueKind != JsonValueKind.Object)
                    continue;
                foreach (var property in observations.EnumerateObject())
                {
                    var value = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.GetRawText();
                    var writable = session.ValidationRecipe.WritableBoundaryKeys.Contains(property.Name, StringComparer.OrdinalIgnoreCase);
                    int? numeric = writable && int.TryParse(value, out var parsed) ? parsed : null;
                    result.Add(new EffectEvidenceDerivedObservation
                    {
                        ScenarioId = scenario.ScenarioId,
                        Key = property.Name,
                        SourceRelativePath = Path.GetRelativePath(session.SessionRoot, path),
                        JsonPath = "$.normalized_observations." + property.Name,
                        Value = value,
                        Minimum = numeric,
                        Maximum = numeric
                    });
                }
            }
        }
        foreach (var boundaryKey in session.ValidationRecipe.WritableBoundaryKeys.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var group = result.Where(item => item.Key.Equals(boundaryKey, StringComparison.OrdinalIgnoreCase) && item.Minimum.HasValue).ToList();
            if (group.Count == 0) continue;
            var sampleMinimum = group.Min(item => item.Minimum!.Value);
            var sampleMaximum = group.Max(item => item.Maximum!.Value);
            var minimumScenarios = ScenarioIdsWithRole(session, EffectValidationScenarioRoles.MinimumBoundary);
            var maximumScenarios = ScenarioIdsWithRole(session, EffectValidationScenarioRoles.MaximumBoundary);
            var negativeScenarios = ScenarioIdsWithRole(session, EffectValidationScenarioRoles.Negative);
            var minimumValues = group.Where(item => minimumScenarios.Contains(item.ScenarioId)).Select(item => item.Minimum!.Value).ToList();
            var maximumValues = group.Where(item => maximumScenarios.Contains(item.ScenarioId)).Select(item => item.Maximum!.Value).ToList();
            var hasNegative = negativeScenarios.All(id => session.Scenarios.Any(item =>
                item.ScenarioId.Equals(id, StringComparison.OrdinalIgnoreCase) && IsScenarioComplete(session, item)));
            foreach (var item in group)
            {
                item.Minimum = sampleMinimum;
                item.Maximum = sampleMaximum;
                if (minimumValues.Count > 0 && maximumValues.Count > 0 && negativeScenarios.Count > 0 && hasNegative)
                {
                    item.VerifiedMinimum = minimumValues.Min();
                    item.VerifiedMaximum = maximumValues.Max();
                    item.BoundaryEvidenceZh = "验证配方声明的最小边界、最大边界和负向场景均已完成，边界只从可写槽位的签名原始 JSON 复算。";
                }
            }
        }
        return result;
    }

    private static HashSet<string> ScenarioIdsWithRole(EffectProbeSession session, string role)
        => session.ValidationRecipe.Scenarios
            .Where(item => item.ScenarioRole.Equals(role, StringComparison.Ordinal))
            .Select(item => item.ScenarioId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static void AddMemoryDword(IDictionary<string, string> values, string key, uint address, string hostName, int port)
    {
        if (TryReadUnsigned(address, 4, hostName, port, out var value))
            values[key] = unchecked((int)value).ToString();
    }

    private static void AddMemoryUnsigned(
        IDictionary<string, string> values,
        string key,
        uint address,
        int byteWidth,
        string hostName,
        int port)
    {
        if (TryReadUnsigned(address, byteWidth, hostName, port, out var value))
            values[key] = value.ToString();
    }

    private static void AddPointerObservation(IDictionary<string, string> values, string key, uint pointer)
        => values[key] = unchecked((int)pointer).ToString();

    private static bool TryReadUnsigned(uint address, int byteWidth, string hostName, int port, out uint value)
    {
        value = 0;
        if (byteWidth is not (1 or 2 or 4)) throw new ArgumentOutOfRangeException(nameof(byteWidth));
        var read = InvokeX32dbg("GET", hostName, port, "/api/memory/read",
            new Dictionary<string, string> { ["address"] = address.ToString("X8"), ["size"] = byteWidth.ToString() });
        if (!TryReadMemoryHex(read, out var bytes) || bytes.Length < byteWidth) return false;
        value = byteWidth switch
        {
            1 => bytes[0],
            2 => BitConverter.ToUInt16(bytes, 0),
            _ => BitConverter.ToUInt32(bytes, 0)
        };
        return true;
    }

    private static bool TryReadMemoryHex(X32dbgCallResult result, out byte[] bytes)
    {
        bytes = [];
        if (result.Data is not JsonElement json || !TryGetJsonProperty(json, "data", out var envelope)) return false;
        var hex = GetStringProperty(envelope, "hex", "bytesHex", "bytes_hex");
        if (string.IsNullOrWhiteSpace(hex) && TryGetJsonProperty(envelope, "data", out var nested))
            hex = GetStringProperty(nested, "hex", "bytesHex", "bytes_hex");
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var normalized = new string(hex.Where(Uri.IsHexDigit).ToArray());
        if (normalized.Length == 0 || normalized.Length % 2 != 0) return false;
        bytes = Enumerable.Range(0, normalized.Length / 2).Select(index => Convert.ToByte(normalized.Substring(index * 2, 2), 16)).ToArray();
        return true;
    }

    private static bool TryParseAddress(string? text, out uint address)
    {
        address = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var value = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
        return uint.TryParse(value, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out address);
    }

    private static List<(string Id, string NameZh, string InstructionZh)> BuildEffectProbeScenarios(string contractId)
        => contractId == "personal-job-binding-query-v1"
            ? [("character-hit", "武将槽命中", "使用原生绑定武将触发该特效后采集。"), ("job-hit", "兵种槽命中", "使用仅由兵种绑定命中的单位触发后采集。"), ("native-miss", "原生未命中", "使用不在任何原生槽的单位触发相同判定后采集。"), ("effect-value-read", "特效值读取", "使用具有非零特效值的绑定触发后采集。")]
            : contractId.StartsWith("strategy-damage", StringComparison.OrdinalIgnoreCase)
                ? [("strategy-normal", "普通策略命中", "执行一次普通伤害策略。"),
                    ("strategy-fixed-adjust", "固定值调整", "执行固定增伤或减伤验证。"),
                    ("strategy-percent-adjust", "百分比调整", "执行按当前伤害比例调整验证。"),
                    ("strategy-minimum-boundary", "最低伤害边界", "执行会触及最低伤害的减伤场景。"),
                    ("strategy-maximum-boundary", "最高伤害边界", "执行会触及最高伤害的增伤场景。"),
                    ("strategy-negative", "负向场景", "使用不应命中该特效的单位执行相同策略。")]
                : [("physical-normal", "普通攻击", "执行一次普通物理攻击。"),
                    ("physical-critical", "暴击", "执行一次物理暴击。"),
                    ("physical-counter", "反击", "触发一次反击。"),
                    ("physical-combo", "连击", "触发一次连击。"),
                    ("physical-minimum-boundary", "最低恢复边界", "执行会触及最低恢复值的场景。"),
                    ("physical-maximum-boundary", "最高恢复边界", "执行会触及最高恢复值的场景。"),
                    ("physical-negative", "负向场景", "使用不应触发恢复的单位执行攻击。")];

    private static string NormalizeEffectContractId(string contractId)
    {
        var value = (contractId ?? string.Empty).Trim();
        if (value is "personal-job-binding-query-v1" or "strategy-damage-formula-v2" or "physical-after-damage-recovery-v2") return value;
        throw new ArgumentException("不支持的特效动态契约：" + value);
    }

    private static List<EffectRelationshipAssertion> BuildRelationshipAssertions(
        EffectProbeSession session,
        IReadOnlyList<EffectEvidenceDerivedObservation> observations)
    {
        if (session.ValidationRecipe.RelationshipRules.Count > 0)
            return BuildRecipeRelationshipAssertions(session, observations);
        return BuildLegacyRelationshipAssertions(session, observations);
    }

    private static List<EffectRelationshipAssertion> BuildRecipeRelationshipAssertions(
        EffectProbeSession session,
        IReadOnlyList<EffectEvidenceDerivedObservation> observations)
    {
        var result = new List<EffectRelationshipAssertion>();
        foreach (var rule in session.ValidationRecipe.RelationshipRules)
        {
            var matchingPositive = 0;
            var matchingNegative = 0;
            var mismatches = 0;
            var evidencePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string firstPointer = string.Empty;
            foreach (var scenario in session.Scenarios)
            {
                var definition = ScenarioDefinition(session, scenario);
                var scenarioObservations = observations.Where(item =>
                    item.ScenarioId.Equals(scenario.ScenarioId, StringComparison.OrdinalIgnoreCase)).ToList();
                var leftByHit = ObservationValuesByHit(session, scenario, scenarioObservations, rule.LeftObservationKey);
                var rightByHit = ObservationValuesByHit(session, scenario, scenarioObservations, rule.RightObservationKey);
                var hits = leftByHit.Keys.Union(rightByHit.Keys).Distinct().Order().ToList();
                if (hits.Count == 0)
                {
                    if (rule.Required) mismatches++;
                    continue;
                }
                foreach (var hit in hits)
                {
                    leftByHit.TryGetValue(hit, out var left);
                    rightByHit.TryGetValue(hit, out var right);
                    var leftValues = left?.Select(item => item.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
                    var rightValues = right?.Select(item => item.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
                    var matches = leftValues.Length == 1 && rightValues.Length == 1 &&
                                  leftValues[0].Equals(rightValues[0], StringComparison.OrdinalIgnoreCase);
                    if (!matches)
                    {
                        mismatches++;
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(firstPointer)) firstPointer = leftValues[0];
                    foreach (var path in left!.Concat(right!).Select(item => item.SourceRelativePath)) evidencePaths.Add(path);
                    if (definition.ScenarioRole.Equals(EffectValidationScenarioRoles.Negative, StringComparison.Ordinal))
                        matchingNegative++;
                    else
                        matchingPositive++;
                }
            }

            if (!rule.Required && matchingPositive + matchingNegative == 0) continue;
            var callChainVerified = evidencePaths.Count > 0 && evidencePaths.All(path => observations.Any(item =>
                item.SourceRelativePath.Equals(path, StringComparison.OrdinalIgnoreCase) &&
                item.Key.Equals("relationship-call-chain", StringComparison.OrdinalIgnoreCase) && item.Value == "1"));
            var firstSample = observations.FirstOrDefault(item =>
                item.Key.Equals("relationship-pointer", StringComparison.OrdinalIgnoreCase));
            string RelatedValue(string key) => observations.FirstOrDefault(item =>
                item.ScenarioId.Equals(firstSample?.ScenarioId ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                item.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;
            var verified = mismatches == 0 && callChainVerified && matchingPositive > 0 &&
                           (!rule.Required || matchingNegative > 0);
            result.Add(new EffectRelationshipAssertion
            {
                SlotId = rule.LeftObservationKey,
                Relationship = rule.RuleId,
                LeftSlotId = rule.LeftObservationKey,
                RightSlotId = rule.RightObservationKey,
                RelationshipKind = rule.RelationshipKind,
                BattlefieldUnitId = int.TryParse(RelatedValue("relationship-unit-id"), out var unitId) ? unitId : null,
                Camp = RelatedValue("relationship-camp"),
                HpObserved = int.TryParse(RelatedValue("relationship-hp"), out var hp) ? hp : null,
                PointerHex = int.TryParse(firstPointer, out var pointer) ? $"0x{unchecked((uint)pointer):X8}" : string.Empty,
                MatchingSamples = matchingPositive,
                NegativeSamples = matchingNegative,
                CallChainVerified = callChainVerified,
                Verified = verified,
                EvidencePaths = evidencePaths.ToList()
            });
        }
        return result;
    }

    private static Dictionary<int, List<EffectEvidenceDerivedObservation>> ObservationValuesByHit(
        EffectProbeSession session,
        EffectProbeScenarioState scenario,
        IEnumerable<EffectEvidenceDerivedObservation> observations,
        string key)
        => observations.Where(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => CaptureHitIndex(session, scenario, item.SourceRelativePath))
            .ToDictionary(group => group.Key, group => group.ToList());

    private static int CaptureHitIndex(
        EffectProbeSession session,
        EffectProbeScenarioState scenario,
        string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(session.SessionRoot, relativePath));
        return scenario.Captures.FirstOrDefault(item =>
            Path.GetFullPath(item.CapturePath).Equals(fullPath, StringComparison.OrdinalIgnoreCase))?.HitIndex ?? 1;
    }

    private static List<EffectRelationshipAssertion> BuildLegacyRelationshipAssertions(
        EffectProbeSession session,
        IReadOnlyList<EffectEvidenceDerivedObservation> observations)
    {
        var samples = observations.GroupBy(item => item.ScenarioId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var items = group.ToList();
                string Value(string key) => items.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;
                return new
                {
                    ScenarioId = group.Key,
                    Pointer = Value("relationship-pointer"),
                    UnitId = Value("relationship-unit-id"),
                    Camp = Value("relationship-camp"),
                    Hp = Value("relationship-hp"),
                    CallChain = Value("relationship-call-chain"),
                    EvidencePath = items[0].SourceRelativePath
                };
            })
            .Where(item => int.TryParse(item.Pointer, out _) && int.TryParse(item.UnitId, out _) &&
                           int.TryParse(item.Camp, out _) && int.TryParse(item.Hp, out _))
            .ToList();
        var positive = samples.Where(item => !item.ScenarioId.Contains("negative", StringComparison.OrdinalIgnoreCase)).ToList();
        var negative = samples.Where(item => item.ScenarioId.Contains("negative", StringComparison.OrdinalIgnoreCase)).ToList();
        if (positive.Count == 0 || negative.Count == 0) return [];
        var first = positive[0];
        var callChainVerified = samples.All(item => item.CallChain == "1");
        var relationship = session.ContractId.StartsWith("strategy-damage", StringComparison.OrdinalIgnoreCase)
            ? "EffectSubject"
            : "DamageTarget";
        return
        [
            new EffectRelationshipAssertion
            {
                SlotId = session.ContractId.StartsWith("strategy-damage", StringComparison.OrdinalIgnoreCase)
                    ? "strategy-effect-subject"
                    : "physical-effect-subject",
                Relationship = relationship,
                BattlefieldUnitId = int.Parse(first.UnitId),
                Camp = first.Camp,
                HpObserved = int.Parse(first.Hp),
                PointerHex = $"0x{unchecked((uint)int.Parse(first.Pointer)):X8}",
                MatchingSamples = positive.Count,
                NegativeSamples = negative.Count,
                CallChainVerified = callChainVerified,
                Verified = callChainVerified,
                EvidencePaths = samples.Select(item => item.EvidencePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            }
        ];
    }

    private static bool IsWritableBoundaryObservation(EffectProbeSession session, string key)
        => session.ValidationRecipe.WritableBoundaryKeys.Contains(key, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<uint> EffectProbeAddresses(EffectProbeSession session)
    {
        var configured = session.ValidationRecipe?.BreakpointAddresses?
            .Where(address => address != 0)
            .Distinct()
            .ToArray() ?? [];
        return configured.Length > 0 ? configured : EffectProbeAddresses(session.ContractId);
    }

    private static IReadOnlyList<uint> EffectProbeAddresses(string contractId)
        => contractId.StartsWith("strategy-damage", StringComparison.OrdinalIgnoreCase)
            ? [0x0043C2A9, 0x0043C2B0, 0x0043C2B5, 0x0043C2C9]
            : contractId == "personal-job-binding-query-v1"
                ? [0x0041301E, 0x00413009]
                : [0x00418330, 0x00418335, 0x00405AD5, 0x0043F70C];

    private static byte ReadExeByteAtVirtualAddress(string exePath, uint address)
    {
        var bytes = File.ReadAllBytes(exePath);
        if (bytes.Length < 0x100) throw new InvalidOperationException("EXE 太小，无法映射探针地址。");
        var peOffset = BitConverter.ToInt32(bytes, 0x3C);
        var sectionCount = BitConverter.ToUInt16(bytes, peOffset + 6);
        var optionalSize = BitConverter.ToUInt16(bytes, peOffset + 20);
        var optional = peOffset + 24;
        var imageBase = BitConverter.ToUInt32(bytes, optional + 28);
        if (address < imageBase) throw new InvalidOperationException("探针地址小于 ImageBase。");
        var rva = address - imageBase;
        var sectionTable = optional + optionalSize;
        for (var index = 0; index < sectionCount; index++)
        {
            var offset = sectionTable + index * 40;
            var virtualSize = BitConverter.ToUInt32(bytes, offset + 8);
            var virtualAddress = BitConverter.ToUInt32(bytes, offset + 12);
            var rawSize = BitConverter.ToUInt32(bytes, offset + 16);
            var rawPointer = BitConverter.ToUInt32(bytes, offset + 20);
            if (rva < virtualAddress || rva >= virtualAddress + Math.Max(virtualSize, rawSize)) continue;
            var fileOffset = checked((int)(rawPointer + rva - virtualAddress));
            if (fileOffset < 0 || fileOffset >= bytes.Length) break;
            return bytes[fileOffset];
        }
        throw new InvalidOperationException($"无法映射探针地址 0x{address:X8}。");
    }

    private static string Sha256Text(string value)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string ComputeOriginalEffectProjectId(EffectSandboxDescriptor sandbox, long exeLength)
    {
        var root = Path.GetFullPath(sandbox.OriginalGameRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var token = $"{root}|EKD5.EXE|{exeLength}|{EngineEffectProfileRegistry.Profile65Id}";
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token)))[..24].ToLowerInvariant();
    }

    private static EffectSandboxDescriptor EnsureEffectEvidenceTarget(GamePaths paths)
    {
        var markerPath = Path.Combine(paths.GameRoot, "_CCZModStudio_EffectSandbox.json");
        if (!File.Exists(markerPath))
            throw new InvalidOperationException("特效动态验证只允许在 CCZModStudio 自动创建并签名的验证副本中运行。");
        EffectSandboxDescriptor marker;
        try
        {
            marker = JsonSerializer.Deserialize<EffectSandboxDescriptor>(File.ReadAllText(markerPath, Encoding.UTF8), JsonOptions)
                     ?? throw new InvalidOperationException("验证副本标记为空。");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("验证副本标记无法读取：" + ex.Message, ex);
        }
        if (!Path.GetFullPath(marker.SandboxRoot).Equals(Path.GetFullPath(paths.GameRoot), StringComparison.OrdinalIgnoreCase) ||
            Path.GetFullPath(marker.OriginalGameRoot).Equals(Path.GetFullPath(paths.GameRoot), StringComparison.OrdinalIgnoreCase) ||
            !marker.SandboxExeSha256.Equals(marker.OriginalExeSha256, StringComparison.OrdinalIgnoreCase) ||
            !UserBoundSignatureService.Verify(marker, static value => value.Signature, static (value, signature) => value.Signature = signature))
            throw new InvalidOperationException("验证副本标记签名、目录或 EXE 身份无效。");
        return marker;
    }

    private static string ComputeEffectProjectId(GamePaths paths)
    {
        var token = $"{Path.GetFullPath(paths.GameRoot).TrimEnd(Path.DirectorySeparatorChar)}|EKD5.EXE|{new FileInfo(paths.ExePath).Length}|{EngineEffectProfileRegistry.Profile65Id}";
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token)))[..24].ToLowerInvariant();
    }
}
