using System.Text;

namespace CCZModStudio;

internal sealed record UsageGuideSection(string Title, string Body);

internal sealed class UsageGuideDialog : Form
{
    internal const string FallbackMarkdown = """
# 普罗工具整合包使用指南

## 通用操作
- 先点击“打开项目目录”选择包含 Ekd5.exe、Data.e5、Imsg.e5、Star.e5 的 MOD 项目。
- 顶部会显示项目、模式和识别到的 6.x 版本；右侧 10/16 只影响当前页数字显示。
- 表格内可直接编辑可写单元格；保存前工具会自动备份，保存后会重新读取校验。

## 角色设定
- 读取角色后可维护人物基础数据、列传、台词和默认装备。
- “个人特效页”会跳转到兵种特效页读取可维护的特效说明与分配数据。

## 兵种设定
- 读取兵种后可维护详细兵种、兵种系地形、相克矩阵、兵种属性、兵种策略和兵种特效。
- 兵种特效页可编辑介绍、武将/兵种分配和特效值。
- “批量导入兵种S”和“导出兵种S”使用 Job<ID>/FactionN/mov.bmp、atk.bmp、spc.bmp；Faction1、Faction2、Faction3 分别是我军、友军、敌军。
- 三份兵种 S 图片规格固定为 mov.bmp=48x528、atk.bmp=64x768、spc.bmp=48x240；导出的根目录可直接回导。人物 S 使用 S<ID>/转数目录，不能与兵种 S 格式混用。
- “像素编辑兵种S”先选择我军、友军或敌军，再在移动、攻击、特技三个标签中编辑完整帧条；保存会统一预览并写回三项。
- 像素编辑器的“换色”最多配置五组精确颜色映射，所有映射并行执行，不会把前一组的结果继续交给后一组。兵种换色覆盖所选阵营的三类动作；人物 R/S 换色覆盖 R 正反面和所有有效 S 转数/动作，S=0 会在换色时选择阵营。

## 宝物设定
- 读取宝物后可维护物品基础数据、装备特效、图标预览和装备类别。
- 保存前请检查效果号、效果值和装备限制是否符合实机预期。

## 图片设定
- 图片资源页用于定位、替换、像素编辑和批量维护 E5/DLL 图片条目。
- 人物形象设定页用于查询空闲头像/R/S 编号、替换 R/S 形象和检查资源状态。
- 图片预览缓存保存在普罗工具整合包.exe 同目录的 cache\ImagePreview\v1；关闭工具后可安全删除整个 cache，之后会按需重建。

## 地图编辑
- 可读取 Map 图片、新建或载入草稿，使用素材库绘制底图和覆盖物。
- 一键发布会写入地图底图；地形层只在能匹配 Hexzmap 时同步发布。

## 剧本编辑
- 读取剧本后通过左侧树维护 Scene、Section 和命令，右侧查看参数和文本。
- 完整保存会重建必要偏移和跳转，保存后会重新读取校验。

## 写回边界
- 表格、图片、地图和剧本写回都会尽量生成备份与报告。
- 草稿不会自动修改游戏文件；必须执行保存或发布后才会写入项目。

## 日志与缓存
- 报错文本日志和 exceptions.jsonl 保存在普罗工具整合包.exe 同目录的 log。
- 可重建的图片预览与素材库缓存保存在同目录的 cache；目录不可写时缓存会退回实时处理，日志会明确提示写入失败，二者都不会回退到 C 盘。
""";

    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };

    public UsageGuideDialog(string markdown)
    {
        Text = "使用指南";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(980, 720);
        MinimumSize = new Size(720, 480);

        BuildLayout(ParseSections(markdown));
    }

    internal static IReadOnlyList<UsageGuideSection> ParseSections(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            markdown = FallbackMarkdown;
        }

        var sections = new List<UsageGuideSection>();
        var title = "总览";
        var builder = new StringBuilder();

        using var reader = new StringReader(markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'));
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                AddCurrentSection();
                title = NormalizeTitle(line[3..]);
                builder.Clear();
                continue;
            }

            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                if (builder.Length > 0) builder.AppendLine();
                builder.AppendLine(line[2..].Trim());
                continue;
            }

            builder.AppendLine(line);
        }

        AddCurrentSection();

        return sections.Count == 0
            ? ParseSections(FallbackMarkdown)
            : sections;

        void AddCurrentSection()
        {
            var body = builder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(body)) return;
            sections.Add(new UsageGuideSection(title, body));
        }
    }

    private void BuildLayout(IReadOnlyList<UsageGuideSection> sections)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);

        foreach (var section in sections)
        {
            var page = new TabPage(ShortenTabTitle(section.Title));
            var box = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = true,
                Text = section.Body,
                Font = new Font("Microsoft YaHei UI", 9F)
            };
            page.Controls.Add(box);
            _tabs.TabPages.Add(page);
        }

        var closeButton = new Button
        {
            Text = "关闭",
            AutoSize = true,
            DialogResult = DialogResult.OK,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(3, 8, 3, 3)
        };
        AcceptButton = closeButton;
        CancelButton = closeButton;

        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        buttonRow.Controls.Add(closeButton);

        layout.Controls.Add(_tabs, 0, 0);
        layout.Controls.Add(buttonRow, 0, 1);
    }

    private static string NormalizeTitle(string title)
    {
        title = title.Trim().Trim('#').Trim();
        return string.IsNullOrWhiteSpace(title) ? "未命名" : title;
    }

    private static string ShortenTabTitle(string title)
    {
        title = NormalizeTitle(title);
        return title.Length <= 12 ? title : title[..12];
    }
}
