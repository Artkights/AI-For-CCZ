using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class EffectSandboxService
{
    public const string MarkerFileName = "_CCZModStudio_EffectSandbox.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string[] ExcludedDirectories =
    [
        ProjectBackupPathService.LegacyBackupDirectoryName,
        "CCZModStudio_Reports",
        "CCZModStudio_Exports",
        "CCZModStudio_TestCopies"
    ];

    public CczProject Create(CczProject source, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(source.ResolveGameFile("Ekd5.exe"))) throw new FileNotFoundException("找不到待验证的 Ekd5.exe。");
        var originalSha = EffectPatchByteService.Sha256(source.ResolveGameFile("Ekd5.exe"));
        var sandboxRoot = Path.GetFullPath(Path.Combine(source.WorkspaceRoot, "CCZModStudio_TestCopies"));
        var root = Path.GetFullPath(Path.Combine(sandboxRoot,
            "EffectValidation_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")));
        var sandboxPrefix = sandboxRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                            Path.DirectorySeparatorChar;
        if (!root.StartsWith(sandboxPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("验证副本路径必须位于工作区的 CCZModStudio_TestCopies 目录内。");
        if (Directory.Exists(root)) throw new IOException("验证副本目录已经存在：" + root);
        Directory.CreateDirectory(root);
        try
        {
            CopyTree(source.GameRoot, root, cancellationToken);
            var copiedExe = Path.Combine(root, "Ekd5.exe");
            var copiedSha = EffectPatchByteService.Sha256(copiedExe);
            if (!copiedSha.Equals(originalSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("验证副本 EXE 与原项目不一致。");
            var marker = new EffectSandboxDescriptor
            {
                SandboxId = "sandbox-" + Guid.NewGuid().ToString("N"),
                OriginalGameRoot = Path.GetFullPath(source.GameRoot),
                SandboxRoot = Path.GetFullPath(root),
                OriginalExeSha256 = originalSha,
                SandboxExeSha256 = copiedSha,
                CreatedAtUtc = DateTime.UtcNow
            };
            UserBoundSignatureService.Sign(marker, static (item, value) => item.Signature = value);
            File.WriteAllText(Path.Combine(root, MarkerFileName), JsonSerializer.Serialize(marker, JsonOptions), Encoding.UTF8);
            File.WriteAllText(Path.Combine(root, "_CCZModStudio_TestCopy.txt"),
                "Effect validation sandbox. Persistent writes to the original project are forbidden.", Encoding.UTF8);
            return new ProjectDetector().CreateProjectFromGameRoot(root);
        }
        catch
        {
            try { Directory.Delete(root, recursive: true); } catch { }
            throw;
        }
    }

    public static bool IsSandbox(CczProject project) => TryRead(project.GameRoot, out _);

    public static bool TryRead(string gameRoot, out EffectSandboxDescriptor descriptor)
    {
        descriptor = new EffectSandboxDescriptor();
        try
        {
            var markerPath = Path.Combine(Path.GetFullPath(gameRoot), MarkerFileName);
            if (!File.Exists(markerPath)) return false;
            var item = JsonSerializer.Deserialize<EffectSandboxDescriptor>(File.ReadAllText(markerPath, Encoding.UTF8), JsonOptions);
            if (item == null || !Path.GetFullPath(item.SandboxRoot).Equals(Path.GetFullPath(gameRoot), StringComparison.OrdinalIgnoreCase) ||
                Path.GetFullPath(item.OriginalGameRoot).Equals(Path.GetFullPath(gameRoot), StringComparison.OrdinalIgnoreCase) ||
                !UserBoundSignatureService.Verify(item, static value => value.Signature, static (value, signature) => value.Signature = signature))
                return false;
            descriptor = item;
            return true;
        }
        catch { return false; }
    }

    private static void CopyTree(string sourceRoot, string destinationRoot, CancellationToken cancellationToken)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceRoot, directory);
            if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => ExcludedDirectories.Contains(part, StringComparer.OrdinalIgnoreCase))) continue;
            Directory.CreateDirectory(Path.Combine(destinationRoot, relative));
        }
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceRoot, file);
            if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => ExcludedDirectories.Contains(part, StringComparer.OrdinalIgnoreCase))) continue;
            var target = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }
}
