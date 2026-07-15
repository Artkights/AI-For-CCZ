using System.Security.Cryptography;
using System.Text;

namespace CCZModStudio.Core;

/// <summary>Cross-process project lock shared by desktop, Runtime and MCP on Windows.</summary>
internal sealed class ProjectEffectWriteLock : IDisposable
{
    private readonly Mutex _mutex;
    private bool _owns;

    private ProjectEffectWriteLock(Mutex mutex, bool owns)
    {
        _mutex = mutex;
        _owns = owns;
    }

    public static ProjectEffectWriteLock Acquire(string gameRoot)
    {
        var normalized = ProjectPatchIdentityService.NormalizePath(gameRoot).ToUpperInvariant();
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..24];
        var mutex = new Mutex(false, "Local\\CCZModStudio.EffectWrite." + id);
        bool owns;
        try { owns = mutex.WaitOne(TimeSpan.Zero); }
        catch (AbandonedMutexException) { owns = true; }
        if (!owns)
        {
            mutex.Dispose();
            throw new InvalidOperationException("当前项目已有桌面或 MCP 特效写入事务正在执行，请等待其完成后重试。");
        }
        PerformanceMetrics.Increment("EffectTransaction.ProjectLockAcquired");
        return new ProjectEffectWriteLock(mutex, true);
    }

    public void Dispose()
    {
        if (_owns)
        {
            _owns = false;
            try { _mutex.ReleaseMutex(); } catch { }
        }
        _mutex.Dispose();
    }
}
