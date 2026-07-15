using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class EffectValidationHostClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _pipeName;
    private readonly string _sessionToken;
    private readonly Process _hostProcess;
    private int _disposed;

    private EffectValidationHostClient(string pipeName, string sessionToken, Process hostProcess)
    {
        _pipeName = pipeName;
        _sessionToken = sessionToken;
        _hostProcess = hostProcess;
    }

    public static async Task<EffectValidationHostClient> StartAsync(
        EffectReleaseConsistencyReport release,
        CancellationToken cancellationToken = default)
    {
        var executable = ResolveGameDebugExecutable(release);
        var pipeName = "ccz-effect-validation-" + Guid.NewGuid().ToString("N");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        start.ArgumentList.Add("--effect-validation-host");
        start.ArgumentList.Add("--pipe");
        start.ArgumentList.Add(pipeName);
        start.ArgumentList.Add("--token");
        start.ArgumentList.Add(token);
        start.ArgumentList.Add("--parent-pid");
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动同批次 GameDebug 验证宿主。");
        var client = new EffectValidationHostClient(pipeName, token, process);
        try
        {
            var timeout = DateTime.UtcNow.AddSeconds(15);
            Exception? last = null;
            while (DateTime.UtcNow < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (process.HasExited)
                    throw new InvalidOperationException($"GameDebug 验证宿主提前退出，退出码 {process.ExitCode}。");
                try
                {
                    await client.CallAsync<object, JsonElement>("ping", new { }, cancellationToken).ConfigureAwait(false);
                    return client;
                }
                catch (Exception ex) when (ex is IOException or TimeoutException)
                {
                    last = ex;
                    await Task.Delay(150, cancellationToken).ConfigureAwait(false);
                }
            }
            throw new TimeoutException("等待 GameDebug 验证宿主超时。", last);
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<TResponse> CallAsync<TRequest, TResponse>(
        string action,
        TRequest payload,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var request = new EffectValidationPipeRequest
        {
            SessionToken = _sessionToken,
            Action = action,
            Payload = JsonSerializer.SerializeToElement(payload, JsonOptions)
        };
        using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(3000, cancellationToken).ConfigureAwait(false);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, 4096, leaveOpen: true);
        var json = JsonSerializer.Serialize(request, JsonOptions);
        await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        var responseJson = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(responseJson) || responseJson.Length > EffectValidationPipeProtocol.MaximumMessageCharacters)
            throw new IOException("GameDebug 验证宿主返回了空响应或超长响应。");
        var response = JsonSerializer.Deserialize<EffectValidationPipeResponse>(responseJson, JsonOptions)
                       ?? throw new IOException("GameDebug 验证宿主响应无法解析。");
        if (!response.ProtocolVersion.Equals(EffectValidationPipeProtocol.Version, StringComparison.Ordinal) ||
            !response.RequestId.Equals(request.RequestId, StringComparison.Ordinal))
            throw new IOException("GameDebug 验证宿主响应身份不匹配。");
        if (!response.Success)
            throw new InvalidOperationException($"[{response.ErrorCode}] {response.ErrorZh}");
        return response.Payload.Deserialize<TResponse>(JsonOptions)
               ?? throw new IOException("GameDebug 验证宿主响应缺少结果。");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try
        {
            if (!_hostProcess.HasExited)
            {
                var request = new EffectValidationPipeRequest
                {
                    SessionToken = _sessionToken,
                    Action = "shutdown",
                    Payload = JsonSerializer.SerializeToElement(new { }, JsonOptions)
                };
                using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await pipe.ConnectAsync(1000, timeout.Token).ConfigureAwait(false);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
                await writer.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions).AsMemory(), timeout.Token).ConfigureAwait(false);
            }
        }
        catch { }
        finally { _hostProcess.Dispose(); }
    }

    private static string ResolveGameDebugExecutable(EffectReleaseConsistencyReport release)
    {
        var configured = Environment.GetEnvironmentVariable("CCZ_GAMEDEBUG_VALIDATION_HOST");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return Path.GetFullPath(configured);
        if (release.IsConsistent && release.HasReleaseManifest)
        {
            var component = release.Components.FirstOrDefault(item => item.ComponentId.Equals("gamedebug", StringComparison.OrdinalIgnoreCase));
            if (component != null)
            {
                var dll = Path.GetFullPath(Path.Combine(release.ReleaseRoot, component.RelativePath));
                var exe = Path.ChangeExtension(dll, ".exe");
                if (File.Exists(exe)) return exe;
            }
        }
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; current != null && depth < 8; depth++, current = current.Parent)
        {
            foreach (var configuration in new[] { "Debug", "Release" })
            {
                var candidate = Path.Combine(current.FullName, "CCZModStudio.GameDebugMcpServer", "bin", configuration,
                    "net8.0-windows", "CCZModStudio.GameDebugMcpServer.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }
        throw new FileNotFoundException("找不到与桌面同批次的 GameDebug 验证宿主；请从 v7 Active 发布启动桌面。 ");
    }
}
