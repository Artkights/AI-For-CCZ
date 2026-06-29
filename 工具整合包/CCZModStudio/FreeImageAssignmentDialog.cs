using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio;

internal sealed class FreeImageAssignmentDialog : Form
{
    private readonly BindingList<FreeImageAssignmentPreviewRow> _rows = [];
    private readonly DataGridView _grid = new();
    private readonly TextBox _summaryBox = new();

    public FreeImageAssignmentDialog(
        CczProject project,
        ImageAssignmentFreeIdService freeIdService,
        ImageAssignmentFreeIdResult result,
        int sFactionSlot)
    {
        var kindText = BuildKindText(result.Kind);
        Text = result.Kind == ImageAssignmentResourceKind.Face ? "空闲头像编号" : $"空闲{kindText}形象编号";
        StartPosition = FormStartPosition.CenterParent;
        Width = 760;
        Height = 680;
        MinimizeBox = false;
        MaximizeBox = true;
        ShowInTaskbar = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(layout);

        _summaryBox.Dock = DockStyle.Top;
        _summaryBox.Multiline = true;
        _summaryBox.ReadOnly = true;
        _summaryBox.ScrollBars = ScrollBars.Vertical;
        _summaryBox.Height = 96;
        _summaryBox.WordWrap = false;
        _summaryBox.Text = BuildSummaryText(result, sFactionSlot);
        layout.Controls.Add(_summaryBox, 0, 0);

        ConfigureGrid(result.Kind);
        layout.Controls.Add(_grid, 0, 1);

        foreach (var candidate in result.FreeCandidates)
        {
            _rows.Add(new FreeImageAssignmentPreviewRow(
                candidate.Id,
                candidate.Detail,
                freeIdService.RenderCandidatePreview(project, result.Kind, candidate.Id, sFactionSlot)));
        }

        _grid.DataSource = _rows;
        ConfigureGridColumns(result.Kind);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var row in _rows)
            {
                row.Preview?.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    private void ConfigureGrid(ImageAssignmentResourceKind kind)
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.BackgroundColor = SystemColors.Window;
        _grid.BorderStyle = BorderStyle.FixedSingle;
        _grid.MultiSelect = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.RowTemplate.Height = kind == ImageAssignmentResourceKind.S ? 136 : 104;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
    }

    private void ConfigureGridColumns(ImageAssignmentResourceKind kind)
    {
        _grid.Columns.Clear();
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(FreeImageAssignmentPreviewRow.Id),
            HeaderText = "ID",
            Name = nameof(FreeImageAssignmentPreviewRow.Id),
            Width = 72,
            ReadOnly = true,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                Format = "0"
            }
        });
        _grid.Columns.Add(new DataGridViewImageColumn
        {
            DataPropertyName = nameof(FreeImageAssignmentPreviewRow.Preview),
            HeaderText = "预览",
            Name = nameof(FreeImageAssignmentPreviewRow.Preview),
            Width = kind == ImageAssignmentResourceKind.S ? 430 : 300,
            ReadOnly = true,
            ImageLayout = DataGridViewImageCellLayout.Zoom
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(FreeImageAssignmentPreviewRow.Detail),
            HeaderText = "资源映射",
            Name = nameof(FreeImageAssignmentPreviewRow.Detail),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 160,
            ReadOnly = true
        });
    }

    private static string BuildSummaryText(ImageAssignmentFreeIdResult result, int sFactionSlot)
    {
        var kindText = BuildKindText(result.Kind);
        var lines = new List<string>
        {
            result.Kind == ImageAssignmentResourceKind.Face
                ? $"空闲头像编号：{result.FreeCandidates.Count} 个    候选资源：{result.CandidateResourceCount} 个    已分配：{result.AssignedCount} 个"
                : $"空闲{kindText}形象编号：{result.FreeCandidates.Count} 个    候选资源：{result.CandidateResourceCount} 个    已分配：{result.AssignedCount} 个",
            result.AvailableIdsFromCache ? "资源扫描：已使用缓存结果。" : "资源扫描：已建立本次缓存，后续查询会复用。",
            "0号默认/普通形象已排除；结果按当前表格内容计算，未保存修改也会参与占用判断。"
        };

        if (result.Kind == ImageAssignmentResourceKind.S)
        {
            lines.Add($"S预览阵营：{CharacterImageResourceService.BuildSPreviewFactionText(sFactionSlot)}");
        }

        if (result.FreeCandidates.Count == 0)
        {
            lines.Add("当前没有可预览的空闲编号。");
        }

        if (result.Warnings.Count > 0)
        {
            lines.Add("资源提示：" + string.Join("；", result.Warnings));
        }

        return string.Join("\r\n", lines);
    }

    private static string BuildKindText(ImageAssignmentResourceKind kind) =>
        kind switch
        {
            ImageAssignmentResourceKind.Face => "头像",
            ImageAssignmentResourceKind.S => "S",
            _ => "R"
        };

    private sealed class FreeImageAssignmentPreviewRow
    {
        public FreeImageAssignmentPreviewRow(int id, string detail, Bitmap? preview)
        {
            Id = id;
            Detail = detail;
            Preview = preview;
        }

        public int Id { get; }
        public string Detail { get; }
        public Bitmap? Preview { get; }
    }
}
