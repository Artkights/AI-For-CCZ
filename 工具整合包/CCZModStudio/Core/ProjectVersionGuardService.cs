using System.Globalization;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

/// <summary>
/// 6.X 读取 / 写入兼容报告。
/// 历史版本曾在这里按 6.5 表版本和核心文件尺寸阻止写入；当前写入保护已取消，
/// 这里仅保留体检报告和尺寸参考，不再拦截保存。
/// </summary>
public static class ProjectVersionGuardService
{
    public const long CurrentObservedHexzmapSampleSize = 44_840;
    public const long Expected65HexzmapGuardSize = 45_254;

    public static readonly IReadOnlyDictionary<string, long> Expected65CoreSizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
    {
        ["Ekd5.exe"] = 1_196_032,
        ["Data.e5"] = 61_379,
        ["Imsg.e5"] = 450_000,
        ["Star.e5"] = 6_359,
        ["Hexzmap.e5"] = Expected65HexzmapGuardSize
    };



    public static void EnsureTableCompatibleForWrite(CczProject project, HexTableDefinition table)
    {
        _ = project;
        _ = table;
    }

    public static void EnsureCoreFileCompatibleForWrite(CczProject project, string fileName)
    {
        _ = project;
        _ = fileName;
    }

    private sealed record CoreState(string FileName, long ExpectedSize, bool Exists, long ActualSize, bool SizeMatches65, string Path);
}
