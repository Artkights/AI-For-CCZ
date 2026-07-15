using CCZModStudio.Core;
using System.Drawing;

internal partial class Program
{
    static void RunTerrainEditToolsSmoke()
    {
        var service = new TerrainGridEditService();
        var line = service.BuildInterpolatedBrushIndexes(8, 6, new Point(0, 0), new Point(7, 5), 1);
        if (!line.Contains(0) || !line.Contains(5 * 8 + 7) || line.Count < 8)
        {
            throw new InvalidOperationException("拖拽插值没有覆盖线段首尾或存在明显漏格。");
        }

        var broad = service.BuildInterpolatedBrushIndexes(8, 6, new Point(4, 3), new Point(4, 3), 3);
        if (broad.Count != 9) throw new InvalidOperationException($"3 格笔刷应覆盖 9 格，实际 {broad.Count} 格。");

        var cells = new byte[]
        {
            1, 1, 2, 2,
            1, 2, 2, 3,
            1, 1, 3, 3
        };
        var fill = service.BuildFloodFillIndexes(cells, 4, 3, new Point(0, 0));
        if (!fill.OrderBy(index => index).SequenceEqual(new[] { 0, 1, 4, 8, 9 }))
        {
            throw new InvalidOperationException("连通填充越过不同值边界或漏掉同值连通格。");
        }

        var rectangle = service.BuildRectangleIndexes(4, 3, new Point(1, 1), new Point(3, 2));
        if (rectangle.Count != 6) throw new InvalidOperationException("矩形工具影响范围错误。");

        var original = cells.ToArray();
        var paint = service.CreateSetCommand(cells, fill, 9, "填充");
        paint.Execute();
        if (fill.Any(index => cells[index] != 9)) throw new InvalidOperationException("地形命令执行失败。");
        paint.Undo();
        if (!cells.SequenceEqual(original)) throw new InvalidOperationException("地形命令撤销没有恢复原值。");

        var changed = original.ToArray();
        changed[0] = 8;
        changed[5] = 8;
        var restore = service.CreateRestoreCommand(changed, original, new[] { 0, 5 }, "恢复");
        restore.Execute();
        if (!changed.SequenceEqual(original)) throw new InvalidOperationException("右键恢复没有使用 OriginalTerrainCells。");

        var clipboard = service.Copy(original, 4, 3, new Rectangle(1, 0, 3, 2));
        var paste = service.PlanPaste(clipboard, 4, 3, new Point(3, 2));
        if (paste.ValuesByIndex.Count != 1 || paste.ClippedCellCount != 5)
        {
            throw new InvalidOperationException($"选区粘贴裁剪报告错误：written={paste.ValuesByIndex.Count}, clipped={paste.ClippedCellCount}。");
        }

        Console.WriteLine("TERRAIN_EDIT_TOOLS_SMOKE_OK interpolation=ok brush=ok fill=ok rectangle=ok restore=ok pasteClip=ok undo=ok");
    }
}
