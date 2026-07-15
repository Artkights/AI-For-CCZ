using System.Data;
using System.Drawing;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio;

internal sealed class ImageAssignmentImportPreviewDialog : Form
{
    private readonly CczProject _project;
    private readonly ImageAssignmentImportPreviewDialogModel _model;
    private readonly ImageAssignmentImportPreviewRenderer _renderer = new();
    private readonly DataGridView _grid = new();
    private readonly AspectRatioPictureBox _currentBox = new();
    private readonly AspectRatioPictureBox _outputBox = new();
    private readonly TextBox _detailBox = new();
    private readonly Button _confirmButton = new();
    private readonly Button _cancelButton = new();
    private readonly System.Windows.Forms.Timer _previewRefreshTimer = new();

    public ImageAssignmentImportPreviewDialog(CczProject project, ImageAssignmentImportPreviewDialogModel model)
    {
        _project = project;
        _model = model;
        Text = model.Title;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = true;
        ShowInTaskbar = false;
        ImportExportDialogLayout.Apply(this, new Size(1120, 720), new Size(920, 620));

        BuildLayout();
        LoadRows();
        ScheduleSelectionPreviewRefresh();
        DpiChanged += (_, _) => ScheduleSelectionPreviewRefresh();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var main = new SplitContainer
        {
            Dock = DockStyle.Fill
        };
        root.Controls.Add(main, 0, 0);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.RowHeadersVisible = false;
        _grid.SelectionChanged += (_, _) => ScheduleSelectionPreviewRefresh();
        main.Panel1.Controls.Add(_grid);

        var previewLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(8)
        };
        previewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        previewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        previewLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.Panel2.Controls.Add(previewLayout);

        previewLayout.Controls.Add(BuildPreviewTitle("当前目标"), 0, 0);
        previewLayout.Controls.Add(BuildPreviewTitle("导入后"), 1, 0);
        ConfigurePreviewPictureBox(_currentBox);
        ConfigurePreviewPictureBox(_outputBox);
        previewLayout.Controls.Add(_currentBox, 0, 1);
        previewLayout.Controls.Add(_outputBox, 1, 1);

        _detailBox.Dock = DockStyle.Fill;
        _detailBox.Multiline = true;
        _detailBox.ReadOnly = true;
        _detailBox.ScrollBars = ScrollBars.Vertical;
        root.Controls.Add(_detailBox, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0)
        };
        _confirmButton.Text = "确认写入";
        _confirmButton.AutoSize = true;
        _confirmButton.MinimumSize = new Size(96, 32);
        _confirmButton.Enabled = _model.CanWrite && _model.Items.Count > 0;
        _confirmButton.DialogResult = DialogResult.OK;
        _cancelButton.Text = "取消";
        _cancelButton.AutoSize = true;
        _cancelButton.MinimumSize = new Size(96, 32);
        _cancelButton.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(_confirmButton);
        buttons.Controls.Add(_cancelButton);
        root.Controls.Add(buttons, 0, 2);

        AcceptButton = _confirmButton.Enabled ? _confirmButton : _cancelButton;
        CancelButton = _cancelButton;

        _previewRefreshTimer.Interval = 120;
        _previewRefreshTimer.Tick += (_, _) =>
        {
            _previewRefreshTimer.Stop();
            UpdateSelectionPreview();
        };

        Shown += (_, _) =>
        {
            ApplyMainSplitterDistanceSafely(main);
            ScheduleSelectionPreviewRefresh();
        };
    }

    private static void ApplyMainSplitterDistanceSafely(SplitContainer split)
    {
        const int desiredDistance = 360;
        const int desiredPanel1Min = 300;
        const int desiredPanel2Min = 420;

        try
        {
            if (split.IsDisposed) return;

            var totalLength = split.Orientation == Orientation.Vertical
                ? split.ClientSize.Width
                : split.ClientSize.Height;
            if (totalLength <= split.SplitterWidth + 2) return;

            split.Panel1MinSize = 0;
            split.Panel2MinSize = 0;

            var usableLength = totalLength - split.SplitterWidth;
            if (usableLength <= 0) return;

            var canUseRequestedMins = usableLength > desiredPanel1Min + desiredPanel2Min;
            var panel1Min = canUseRequestedMins ? desiredPanel1Min : 0;
            var panel2Min = canUseRequestedMins ? desiredPanel2Min : 0;
            var maxDistance = Math.Max(panel1Min, usableLength - panel2Min);
            if (maxDistance < panel1Min) return;

            var target = Math.Clamp(desiredDistance, panel1Min, maxDistance);
            if (split.SplitterDistance != target)
            {
                split.SplitterDistance = target;
            }

            if (canUseRequestedMins)
            {
                split.Panel1MinSize = desiredPanel1Min;
                split.Panel2MinSize = desiredPanel2Min;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
        {
            // SplitContainer can briefly report inconsistent bounds before WinForms finishes layout.
        }
    }

    private static Label BuildPreviewTitle(string text)
        => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
            Padding = new Padding(0, 0, 0, 6)
        };

    private static void ConfigurePreviewPictureBox(AspectRatioPictureBox box)
    {
        box.Dock = DockStyle.Fill;
        box.BackColor = Color.FromArgb(24, 26, 28);
        box.BorderStyle = BorderStyle.FixedSingle;
        box.ClientSizeChanged += (_, _) =>
        {
            if (box.FindForm() is ImageAssignmentImportPreviewDialog dialog)
            {
                dialog.ScheduleSelectionPreviewRefresh();
            }
        };
    }

    private void LoadRows()
    {
        var table = new DataTable();
        table.Columns.Add("序号", typeof(int));
        table.Columns.Add("类型", typeof(string));
        table.Columns.Add("编号", typeof(int));
        table.Columns.Add("转数", typeof(string));
        table.Columns.Add("动作", typeof(string));
        table.Columns.Add("目标", typeof(string));
        table.Columns.Add("图号", typeof(int));
        table.Columns.Add("素材", typeof(string));
        table.Columns.Add("尺寸", typeof(string));
        table.Columns.Add("状态", typeof(string));

        for (var i = 0; i < _model.Items.Count; i++)
        {
            var item = _model.Items[i];
            table.Rows.Add(
                i + 1,
                item.Kind,
                item.ResourceId,
                item.StageName,
                item.ActionName,
                item.TargetFileName,
                item.TargetImageNumber,
                Path.GetFileName(item.SourcePath),
                FormatSize(item),
                "可写入");
        }

        _grid.DataSource = table;
        ConfigureGridColumns();
        if (_grid.Rows.Count > 0)
        {
            _grid.Rows[0].Selected = true;
            _grid.CurrentCell = _grid.Rows[0].Cells[0];
        }
    }

    private void ConfigureGridColumns()
    {
        ConfigureColumn(0, 54);
        ConfigureColumn(1, 54);
        ConfigureColumn(2, 62);
        ConfigureColumn(3, 82);
        ConfigureColumn(4, 70);
        ConfigureColumn(5, 90, visible: false);
        ConfigureColumn(6, 64, visible: false);
        ConfigureColumn(7, 90, visible: false);
        ConfigureColumn(8, 90, visible: false);
        ConfigureColumn(9, 72);
    }

    private void ConfigureColumn(int index, int width, bool visible = true)
    {
        if (index < 0 || index >= _grid.Columns.Count) return;

        var column = _grid.Columns[index];
        column.Visible = visible;
        column.Width = width;
        column.MinimumWidth = Math.Min(width, 36);
        column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
    }

    private void ScheduleSelectionPreviewRefresh()
    {
        if (IsDisposed) return;
        if (!IsHandleCreated)
        {
            UpdateSelectionPreview();
            return;
        }

        _previewRefreshTimer.Stop();
        _previewRefreshTimer.Start();
    }

    private void UpdateSelectionPreview()
    {
        if (!HasUsablePreviewSize(_currentBox) || !HasUsablePreviewSize(_outputBox))
        {
            return;
        }

        var index = GetSelectedIndex();
        if (index < 0 || index >= _model.Items.Count)
        {
            SetPicture(_currentBox, _renderer.RenderPlaceholder("没有可预览项。", GetPreviewTargetSize(_currentBox)));
            SetPicture(_outputBox, _renderer.RenderPlaceholder("没有可预览项。", GetPreviewTargetSize(_outputBox)));
            _detailBox.Text = BuildProblemText();
            return;
        }

        var item = _model.Items[index];
        SetPicture(_currentBox, _renderer.RenderCurrentTarget(
            _project, item, GetPreviewTargetSize(_currentBox)));
        SetPicture(_outputBox, _renderer.RenderOutputPreview(
            item, GetPreviewTargetSize(_outputBox)));
        _detailBox.Text = BuildDetailText(item, index);
    }

    private static Size GetPreviewTargetSize(PictureBox box)
    {
        var width = box.ClientSize.Width;
        var height = box.ClientSize.Height;
        return new Size(Math.Max(1, width), Math.Max(1, height));
    }

    private static bool HasUsablePreviewSize(PictureBox box)
        => box.ClientSize.Width > 1 && box.ClientSize.Height > 1;

    private int GetSelectedIndex()
    {
        if (_grid.CurrentRow == null) return -1;
        return _grid.CurrentRow.Index;
    }

    private string BuildDetailText(ImageAssignmentImportPreviewItem item, int index)
        => $"导入项：{index + 1}/{_model.Items.Count}\r\n" +
           $"{item.Detail}\r\n" +
           $"目标：{item.TargetFileName} #{item.TargetImageNumber}\r\n" +
           $"素材：{item.SourcePath}\r\n" +
           $"尺寸：{FormatSize(item)}\r\n\r\n" +
           BuildProblemText();

    private string BuildProblemText()
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(_model.SummaryText))
        {
            lines.Add(_model.SummaryText.Trim());
        }

        if (_model.SkippedItems.Count > 0)
        {
            lines.Add("");
            lines.Add("跳过/问题：");
            lines.AddRange(_model.SkippedItems.Take(30).Select(item => $"- {item.Key}: {item.Reason} {item.SourcePath}"));
            if (_model.SkippedItems.Count > 30) lines.Add($"- ... 还有 {_model.SkippedItems.Count - 30} 项");
        }

        if (_model.Warnings.Count > 0)
        {
            lines.Add("");
            lines.Add("提示：");
            lines.AddRange(_model.Warnings.Take(30).Select(warning => "- " + warning));
            if (_model.Warnings.Count > 30) lines.Add($"- ... 还有 {_model.Warnings.Count - 30} 条");
        }

        if (!_model.CanWrite)
        {
            lines.Add("");
            lines.Add("当前预览不可写入：存在阻断项或没有可写入项。");
        }

        return string.Join("\r\n", lines);
    }

    private static string FormatSize(ImageAssignmentImportPreviewItem item)
    {
        var source = item.SourceWidth.HasValue && item.SourceHeight.HasValue
            ? $"{item.SourceWidth.Value}x{item.SourceHeight.Value}"
            : "?x?";
        return $"{source} -> {item.OutputWidth}x{item.OutputHeight}";
    }

    private static void SetPicture(PictureBox box, Image image)
    {
        var old = box.Image;
        box.Image = image;
        old?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _previewRefreshTimer.Dispose();
            _currentBox.Image?.Dispose();
            _outputBox.Image?.Dispose();
        }

        base.Dispose(disposing);
    }
}
