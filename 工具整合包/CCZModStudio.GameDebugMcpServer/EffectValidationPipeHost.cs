using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CCZModStudio.Models;

namespace CCZModStudio.GameDebugMcpServer;

internal sealed class EffectValidationPipeHost(
    GameDebugRuntime runtime,
    string pipeName,
    string sessionToken,
    int parentProcessId)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private volatile bool _shutdown;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pipeName) || pipeName.Length > 180 ||
            string.IsNullOrWhiteSpace(sessionToken) || sessionToken.Length != 64 ||
            parentProcessId <= 0)
            throw new ArgumentException("验证宿主启动参数无效。");

        while (!_shutdown && !cancellationToken.IsCancellationRequested && IsParentAlive())
        {
            await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var acceptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            acceptTimeout.CancelAfter(TimeSpan.FromSeconds(2));
            try { await pipe.WaitForConnectionAsync(acceptTimeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { continue; }
            try
            {
                await HandleConnectionAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // The desktop client may close immediately after receiving enough data.
                // A broken connection must not terminate the validation host.
            }
            catch (ObjectDisposedException)
            {
                // Treat disposal during a client disconnect as the end of this connection.
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task HandleConnectionAsync(Stream pipe, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, 4096, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        EffectValidationPipeRequest? request = null;
        EffectValidationPipeResponse response;
        try
        {
            var json = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json) || json.Length > EffectValidationPipeProtocol.MaximumMessageCharacters)
                throw new InvalidOperationException("请求为空或超过长度上限。");
            request = JsonSerializer.Deserialize<EffectValidationPipeRequest>(json, JsonOptions)
                      ?? throw new InvalidOperationException("请求 JSON 无法解析。");
            ValidateRequest(request);
            var result = Dispatch(request.Action, request.Payload);
            response = new EffectValidationPipeResponse
            {
                RequestId = request.RequestId,
                Success = true,
                Payload = JsonSerializer.SerializeToElement(result, result.GetType(), JsonOptions)
            };
        }
        catch (Exception ex)
        {
            response = new EffectValidationPipeResponse
            {
                RequestId = request?.RequestId ?? string.Empty,
                Success = false,
                ErrorCode = ex is UnauthorizedAccessException ? "AUTHENTICATION_FAILED" : "VALIDATION_HOST_ERROR",
                ErrorZh = ex.Message,
                Payload = JsonSerializer.SerializeToElement(new { }, JsonOptions)
            };
        }
        try
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions).AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // A disconnected caller no longer needs a response; keep the host alive.
        }
        catch (ObjectDisposedException)
        {
            // The connection ended while the response was being written.
        }
    }

    private object Dispatch(string action, JsonElement payload)
    {
        switch (action)
        {
            case "ping":
                return new { ready = true, process_id = Environment.ProcessId, protocol = EffectValidationPipeProtocol.Version };
            case "start":
            {
                var value = payload.Deserialize<EffectValidationStartRequest>(JsonOptions)
                            ?? throw new ArgumentException("缺少验证启动参数。");
                var validation = runtime.CreateEffectProbeSession(value.ContractId, value.ContractHash, value.EffectId,
                    value.SandboxRoot, value.ContractCodeIdentityHash, value.ProfileId, value.NormalizedProfileIdentity,
                    value.ContractVersion, value.ValidationRecipe, value.BaseExeSha256, value.SandboxPatchSha256,
                    value.ProbePackageHash, value.ContinuationAddress, value.EvidenceDisposition);
                object? debugger = null;
                string? debuggerError = null;
                if (value.LaunchDebugger)
                {
                    try { debugger = runtime.DebugSessionStart(value.SandboxRoot, null, "127.0.0.1", 27042, 15000, hidden: false); }
                    catch (Exception ex) { debuggerError = ex.Message; }
                }
                return new { validation, debugger, debugger_error = debuggerError };
            }
            case "advance":
            {
                var value = ReadSessionRequest(payload);
                return runtime.RunEffectProbeBatch(value.SessionPath, value.BatchIndex, "127.0.0.1", 27042, clearHardwareFirst: true);
            }
            case "capture":
            {
                var value = ReadSessionRequest(payload);
                return runtime.CaptureEffectProbeScenario(value.SessionPath, value.ScenarioId, "127.0.0.1", 27042);
            }
            case "progress":
                return runtime.ReadEffectProbeProgress(ReadSessionRequest(payload).SessionPath);
            case "finalize":
            {
                var value = ReadSessionRequest(payload);
                var processId = runtime.ResolveEffectValidationProcessId(value.SessionPath);
                return runtime.FinalizeEffectEvidenceBundleV3(value.SessionPath, processId, "x32dbg-mcp", "127.0.0.1", 27042);
            }
            case "shutdown":
                _shutdown = true;
                return new { stopped = true };
            default:
                throw new ArgumentException("不支持的验证宿主动作：" + action);
        }
    }

    private static EffectValidationSessionRequest ReadSessionRequest(JsonElement payload)
        => payload.Deserialize<EffectValidationSessionRequest>(JsonOptions)
           ?? throw new ArgumentException("缺少验证会话参数。");

    private void ValidateRequest(EffectValidationPipeRequest request)
    {
        if (!request.ProtocolVersion.Equals(EffectValidationPipeProtocol.Version, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(request.RequestId))
            throw new UnauthorizedAccessException("验证管道协议或请求身份无效。");
        var expected = Encoding.ASCII.GetBytes(sessionToken);
        var actual = Encoding.ASCII.GetBytes(request.SessionToken ?? string.Empty);
        if (actual.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(actual, expected))
            throw new UnauthorizedAccessException("验证管道会话令牌无效。");
    }

    private bool IsParentAlive()
    {
        try
        {
            using var parent = Process.GetProcessById(parentProcessId);
            return !parent.HasExited;
        }
        catch { return false; }
    }
}
