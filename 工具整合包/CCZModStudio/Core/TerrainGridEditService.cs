using CCZModStudio.Models;
using System.Drawing;

namespace CCZModStudio.Core;

public sealed class TerrainGridEditService
{
    public IReadOnlyList<int> BuildInterpolatedBrushIndexes(
        int width,
        int height,
        Point start,
        Point end,
        int brushSize)
    {
        ValidateDimensions(width, height);
        brushSize = brushSize is 1 or 3 or 5 ? brushSize : 1;
        var result = new HashSet<int>();
        foreach (var point in RasterizeLine(start, end))
        {
            AddBrush(result, width, height, point.X, point.Y, brushSize);
        }

        return result.OrderBy(index => index).ToList();
    }

    public IReadOnlyList<int> BuildRectangleIndexes(int width, int height, Point first, Point second)
    {
        ValidateDimensions(width, height);
        var left = Math.Clamp(Math.Min(first.X, second.X), 0, width - 1);
        var right = Math.Clamp(Math.Max(first.X, second.X), 0, width - 1);
        var top = Math.Clamp(Math.Min(first.Y, second.Y), 0, height - 1);
        var bottom = Math.Clamp(Math.Max(first.Y, second.Y), 0, height - 1);
        var result = new List<int>((right - left + 1) * (bottom - top + 1));
        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++) result.Add(y * width + x);
        }

        return result;
    }

    public IReadOnlyList<int> BuildLineIndexes(int width, int height, Point start, Point end, int brushSize = 1)
        => BuildInterpolatedBrushIndexes(width, height, start, end, brushSize);

    public IReadOnlyList<int> BuildFloodFillIndexes(byte[] cells, int width, int height, Point start)
    {
        ValidateCells(cells, width, height);
        if (start.X < 0 || start.Y < 0 || start.X >= width || start.Y >= height) return Array.Empty<int>();
        var startIndex = start.Y * width + start.X;
        var expected = cells[startIndex];
        var visited = new bool[cells.Length];
        var queue = new Queue<int>();
        var result = new List<int>();
        visited[startIndex] = true;
        queue.Enqueue(startIndex);
        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            result.Add(index);
            var x = index % width;
            var y = index / width;
            TryAdd(x - 1, y);
            TryAdd(x + 1, y);
            TryAdd(x, y - 1);
            TryAdd(x, y + 1);
        }

        return result;

        void TryAdd(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return;
            var index = y * width + x;
            if (visited[index] || cells[index] != expected) return;
            visited[index] = true;
            queue.Enqueue(index);
        }
    }

    public TerrainCellEditCommand CreateSetCommand(
        byte[] target,
        IEnumerable<int> indexes,
        byte value,
        string description)
        => CreateCommand(target, indexes, _ => value, description);

    public TerrainCellEditCommand CreateRestoreCommand(
        byte[] target,
        byte[] original,
        IEnumerable<int> indexes,
        string description)
    {
        if (target.Length != original.Length) throw new ArgumentException("当前地形与原始地形长度不一致。", nameof(original));
        return CreateCommand(target, indexes, index => original[index], description);
    }

    public TerrainClipboardData Copy(byte[] cells, int width, int height, Rectangle selection)
    {
        ValidateCells(cells, width, height);
        var clipped = Rectangle.Intersect(selection, new Rectangle(0, 0, width, height));
        if (clipped.IsEmpty) return new TerrainClipboardData();
        var values = new byte[clipped.Width * clipped.Height];
        for (var y = 0; y < clipped.Height; y++)
        {
            Array.Copy(cells, (clipped.Y + y) * width + clipped.X, values, y * clipped.Width, clipped.Width);
        }

        return new TerrainClipboardData { Width = clipped.Width, Height = clipped.Height, Cells = values };
    }

    public TerrainPastePlan PlanPaste(TerrainClipboardData clipboard, int width, int height, Point destination)
    {
        ValidateDimensions(width, height);
        if (clipboard.Width <= 0 || clipboard.Height <= 0 || clipboard.Cells.Length != clipboard.Width * clipboard.Height)
        {
            return new TerrainPastePlan();
        }

        var values = new Dictionary<int, byte>();
        var clippedCount = 0;
        for (var y = 0; y < clipboard.Height; y++)
        {
            for (var x = 0; x < clipboard.Width; x++)
            {
                var targetX = destination.X + x;
                var targetY = destination.Y + y;
                if (targetX < 0 || targetY < 0 || targetX >= width || targetY >= height)
                {
                    clippedCount++;
                    continue;
                }

                values[targetY * width + targetX] = clipboard.Cells[y * clipboard.Width + x];
            }
        }

        return new TerrainPastePlan
        {
            Destination = destination,
            SourceWidth = clipboard.Width,
            SourceHeight = clipboard.Height,
            ValuesByIndex = values,
            ClippedCellCount = clippedCount
        };
    }

    public TerrainCellEditCommand CreatePasteCommand(byte[] target, TerrainPastePlan plan)
        => CreateCommand(target, plan.ValuesByIndex.Keys, index => plan.ValuesByIndex[index], "粘贴地形选区");

    public TerrainCellEditCommand CreateChangeCommand(
        byte[] target,
        IEnumerable<TerrainCellValueChange> changes,
        string description)
    {
        var normalized = changes
            .DistinctBy(change => change.Index)
            .Where(change => (uint)change.Index < (uint)target.Length)
            .Where(change => change.OldValue != change.NewValue)
            .OrderBy(change => change.Index)
            .ToList();
        return new TerrainCellEditCommand(target, normalized, description);
    }

    private static TerrainCellEditCommand CreateCommand(
        byte[] target,
        IEnumerable<int> indexes,
        Func<int, byte> valueFactory,
        string description)
    {
        var changes = indexes
            .Distinct()
            .Where(index => (uint)index < (uint)target.Length)
            .Select(index => new TerrainCellValueChange(index, target[index], valueFactory(index)))
            .Where(change => change.OldValue != change.NewValue)
            .OrderBy(change => change.Index)
            .ToList();
        return new TerrainCellEditCommand(target, changes, description);
    }

    private static IEnumerable<Point> RasterizeLine(Point start, Point end)
    {
        var x = start.X;
        var y = start.Y;
        var dx = Math.Abs(end.X - start.X);
        var sx = start.X < end.X ? 1 : -1;
        var dy = -Math.Abs(end.Y - start.Y);
        var sy = start.Y < end.Y ? 1 : -1;
        var error = dx + dy;
        while (true)
        {
            yield return new Point(x, y);
            if (x == end.X && y == end.Y) yield break;
            var twiceError = error * 2;
            if (twiceError >= dy)
            {
                error += dy;
                x += sx;
            }

            if (twiceError <= dx)
            {
                error += dx;
                y += sy;
            }
        }
    }

    private static void AddBrush(HashSet<int> result, int width, int height, int centerX, int centerY, int brushSize)
    {
        var radius = brushSize / 2;
        for (var y = centerY - radius; y <= centerY + radius; y++)
        {
            for (var x = centerX - radius; x <= centerX + radius; x++)
            {
                if (x < 0 || y < 0 || x >= width || y >= height) continue;
                result.Add(y * width + x);
            }
        }
    }

    private static void ValidateCells(byte[] cells, int width, int height)
    {
        ValidateDimensions(width, height);
        if (cells.Length != checked(width * height)) throw new ArgumentException("地形格数量与网格尺寸不一致。", nameof(cells));
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "地形网格尺寸必须大于零。");
    }
}

public sealed class TerrainCellEditCommand : IMapEditCommand
{
    private readonly byte[] _target;
    private readonly IReadOnlyList<TerrainCellValueChange> _changes;

    internal TerrainCellEditCommand(byte[] target, IReadOnlyList<TerrainCellValueChange> changes, string description)
    {
        _target = target;
        _changes = changes;
        Description = description;
        CreatedAtUtc = DateTime.UtcNow;
        AffectedCellIndexes = changes.Select(change => change.Index).ToArray();
    }

    public string Description { get; }
    public DateTime CreatedAtUtc { get; }
    public IReadOnlyCollection<int> AffectedCellIndexes { get; }
    public IReadOnlyList<TerrainCellValueChange> Changes => _changes;
    public int ChangeCount => _changes.Count;

    public void Execute()
    {
        foreach (var change in _changes) _target[change.Index] = change.NewValue;
    }

    public void Undo()
    {
        for (var i = _changes.Count - 1; i >= 0; i--) _target[_changes[i].Index] = _changes[i].OldValue;
    }
}

public sealed record TerrainCellValueChange(int Index, byte OldValue, byte NewValue);

public sealed class TerrainClipboardData
{
    public int Width { get; init; }
    public int Height { get; init; }
    public byte[] Cells { get; init; } = Array.Empty<byte>();
}

public sealed class TerrainPastePlan
{
    public Point Destination { get; init; }
    public int SourceWidth { get; init; }
    public int SourceHeight { get; init; }
    public IReadOnlyDictionary<int, byte> ValuesByIndex { get; init; } = new Dictionary<int, byte>();
    public int ClippedCellCount { get; init; }
    public Rectangle AffectedBounds
    {
        get
        {
            if (ValuesByIndex.Count == 0) return Rectangle.Empty;
            return new Rectangle(Destination.X, Destination.Y, SourceWidth, SourceHeight);
        }
    }
}
