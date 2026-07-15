using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio;

internal sealed class PixelEditResourcePage
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required EditableImageDocument Document { get; init; }
    public int Zoom { get; set; } = 12;
    public Point ScrollPosition { get; set; } = Point.Empty;
}

internal sealed class PixelEditResourceGroup : IDisposable
{
    private readonly CczProject _project;
    private readonly EditableImageCodecService _codec;
    private readonly List<PixelEditResourcePage> _pages = new();
    private Func<IWin32Window, IReadOnlyList<EditableImageTarget>?>? _replacementTargetLoader;
    private bool _replacementScopeLoaded;

    private PixelEditResourceGroup(CczProject project, EditableImageCodecService codec)
    {
        _project = project;
        _codec = codec;
    }

    public IReadOnlyList<PixelEditResourcePage> Pages => _pages;
    public int ActiveIndex { get; set; }
    public PixelEditResourcePage ActivePage => _pages[Math.Clamp(ActiveIndex, 0, _pages.Count - 1)];
    public bool ShowTabs { get; init; }
    public string ScopeDescription { get; init; } = string.Empty;
    public string SharedUsageWarning { get; init; } = string.Empty;
    public PixelColorReplacementPreview? ColorReplacementPreview { get; set; }

    public static PixelEditResourceGroup Load(
        CczProject project,
        EditableImageCodecService codec,
        IReadOnlyList<EditableImageTarget> targets,
        bool showTabs,
        string scopeDescription,
        string sharedUsageWarning = "",
        Func<IWin32Window, IReadOnlyList<EditableImageTarget>?>? replacementTargetLoader = null,
        string? activeTargetKey = null)
    {
        if (targets.Count == 0) throw new InvalidOperationException("像素编辑资源组没有可载入的条目。");
        var group = new PixelEditResourceGroup(project, codec)
        {
            ShowTabs = showTabs,
            ScopeDescription = scopeDescription,
            SharedUsageWarning = sharedUsageWarning,
            _replacementTargetLoader = replacementTargetLoader
        };

        try
        {
            group.AddTargets(targets);
            if (!string.IsNullOrWhiteSpace(activeTargetKey))
            {
                var index = group._pages.FindIndex(page => page.Key.Equals(activeTargetKey, StringComparison.OrdinalIgnoreCase));
                if (index >= 0) group.ActiveIndex = index;
            }
            return group;
        }
        catch
        {
            group.Dispose();
            throw;
        }
    }

    public bool EnsureReplacementScope(IWin32Window owner)
    {
        if (_replacementScopeLoaded) return true;
        if (_replacementTargetLoader == null)
        {
            _replacementScopeLoaded = true;
            return true;
        }

        var targets = _replacementTargetLoader(owner);
        if (targets == null) return false;
        AddTargets(targets);
        _replacementScopeLoaded = true;
        return true;
    }

    public IReadOnlyList<PixelEditResourcePage> GetWritePages()
        => _pages.Where(IsDirty).ToArray();

    public static bool IsDirty(PixelEditResourcePage page)
        => !PixelsEqual(page.Document.Bitmap, page.Document.OriginalBitmap);

    private static bool PixelsEqual(Bitmap left, Bitmap right)
    {
        if (left.Size != right.Size) return false;
        for (var y = 0; y < left.Height; y++)
        for (var x = 0; x < left.Width; x++)
        {
            var a = left.GetPixel(x, y);
            var b = right.GetPixel(x, y);
            if (a.A == 0 && b.A == 0) continue;
            if (a.ToArgb() != b.ToArgb()) return false;
        }

        return true;
    }

    public static string BuildTargetKey(EditableImageTarget target)
    {
        var path = Path.GetFullPath(target.TargetPath);
        var id = target.Kind == EditableImageTargetKind.DllBitmapIcon
            ? $"icon:{target.IconIndex}"
            : $"image:{target.ImageNumber}";
        return $"{path}|{id}";
    }

    public void Dispose()
    {
        foreach (var page in _pages) page.Document.Dispose();
        _pages.Clear();
    }

    private void AddTargets(IEnumerable<EditableImageTarget> targets)
    {
        var pending = new List<PixelEditResourcePage>();
        try
        {
            foreach (var target in targets)
            {
                var key = BuildTargetKey(target);
                if (_pages.Any(page => page.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) ||
                    pending.Any(page => page.Key.Equals(key, StringComparison.OrdinalIgnoreCase))) continue;
                var document = _codec.Load(_project, target);
                if (!document.StorageInfo.CanEdit)
                {
                    var reason = string.IsNullOrWhiteSpace(document.StorageInfo.ReadOnlyReason)
                        ? $"当前 {document.StorageInfo.DisplayKind} 条目不支持安全像素写回。"
                        : document.StorageInfo.ReadOnlyReason;
                    document.Dispose();
                    throw new InvalidOperationException($"{target.DisplayName} 仅可查看：{reason}");
                }
                pending.Add(new PixelEditResourcePage
                {
                    Key = key,
                    Label = target.DisplayName,
                    Document = document
                });
            }
            _pages.AddRange(pending);
        }
        catch
        {
            foreach (var page in pending) page.Document.Dispose();
            throw;
        }
    }
}
