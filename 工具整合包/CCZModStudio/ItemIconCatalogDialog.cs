using System.Data;
using System.Drawing.Drawing2D;
using System.Globalization;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio;

internal sealed class ItemIconCatalogDialog : Form
{
    private const int CardWidth = 112;
    private const int CardHeight = 132;
    private const int CardGap = 6;
    private const int CardCreateBatchSize = 60;
    private const int PreviewCanvasSize = 96;

    private readonly CczProject _project;
    private readonly DataTable _itemData;
    private readonly ItemIconCatalogService _service;
    private readonly CheckBox _freeOnlyCheckBox = new();
    private readonly Label _statusLabel = new();
    private readonly FlowLayoutPanel _contentPanel = new();
    private readonly ToolTip _toolTip = new();
    private readonly System.Windows.Forms.Timer _cardBatchTimer = new() { Interval = 1 };
    private readonly SemaphoreSlim _previewSemaphore = new(Math.Max(1, Math.Min(Environment.ProcessorCount, 4)));
    private readonly List<PreviewCard> _cards = [];
    private ItemIconCatalogResult? _result;
    private IReadOnlyList<ItemIconCatalogCandidate> _pendingItems = Array.Empty<ItemIconCatalogCandidate>();
    private CancellationTokenSource? _queryCts;
    private CancellationTokenSource? _previewCts;
    private int _pendingItemIndex;
    private int _generation;
    private bool _refreshing;
    private bool _disposed;

    public ItemIconCatalogDialog(CczProject project, DataTable itemData, ItemIconPreviewService? previewService = null)
    {
        _project = project;
        _itemData = itemData;
        _service = new ItemIconCatalogService(previewService);

        Text = "\u67e5\u8be2\u5b9d\u7269\u56fe\u6807";
        StartPosition = FormStartPosition.CenterParent;
        Width = 900;
        Height = 700;
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

        layout.Controls.Add(BuildToolbar(), 0, 0);
        ConfigureContentPanel();
        layout.Controls.Add(_contentPanel, 0, 1);

        _cardBatchTimer.Tick += (_, _) => AddNextCardBatch();
        FormClosing += (_, _) => CancelWork();
        Shown += (_, _) => StartQueryRefresh(freeOnly: false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            CancelWork();
            _cardBatchTimer.Stop();
            _cardBatchTimer.Dispose();
            ClearCards();
            _toolTip.Dispose();
            _previewSemaphore.Dispose();
        }

        base.Dispose(disposing);
    }

    private FlowLayoutPanel BuildToolbar()
    {
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 0, 0, 8)
        };

        _freeOnlyCheckBox.Text = "\u7a7a\u95f2";
        _freeOnlyCheckBox.Checked = false;
        _freeOnlyCheckBox.AutoSize = true;
        _freeOnlyCheckBox.Margin = new Padding(0, 6, 12, 0);
        _freeOnlyCheckBox.CheckedChanged += (_, _) => RefreshContentFromOptions();
        toolbar.Controls.Add(_freeOnlyCheckBox);

        var closeButton = new Button
        {
            Text = "\u5173\u95ed",
            AutoSize = true,
            Margin = new Padding(0, 0, 12, 0)
        };
        closeButton.Click += (_, _) => Close();
        toolbar.Controls.Add(closeButton);

        _statusLabel.AutoSize = true;
        _statusLabel.Margin = new Padding(0, 7, 0, 0);
        _statusLabel.Text = "\u5c1a\u672a\u8bfb\u53d6\u56fe\u6807\u3002";
        toolbar.Controls.Add(_statusLabel);

        return toolbar;
    }

    private void ConfigureContentPanel()
    {
        _contentPanel.Dock = DockStyle.Fill;
        _contentPanel.AutoScroll = true;
        _contentPanel.WrapContents = true;
        _contentPanel.FlowDirection = FlowDirection.LeftToRight;
        _contentPanel.BackColor = SystemColors.Window;
        _contentPanel.BorderStyle = BorderStyle.FixedSingle;
        _contentPanel.Padding = new Padding(CardGap);
        _contentPanel.Scroll += (_, _) => ScheduleVisiblePreviews();
        _contentPanel.MouseWheel += (_, _) => ScheduleVisiblePreviews();
        _contentPanel.Resize += (_, _) => ScheduleVisiblePreviews();
    }

    private void RefreshContentFromOptions()
    {
        if (_refreshing) return;
        StartQueryRefresh(_freeOnlyCheckBox.Checked);
    }

    private async void StartQueryRefresh(bool freeOnly)
    {
        CancelWork();
        _generation++;
        _cardBatchTimer.Stop();
        _pendingItems = Array.Empty<ItemIconCatalogCandidate>();
        _pendingItemIndex = 0;
        ClearCards();
        AddStatusCard("\u6b63\u5728\u8bfb\u53d6\u5b9d\u7269\u56fe\u6807...");
        _statusLabel.Text = "\u6b63\u5728\u8bfb\u53d6\u5b9d\u7269\u56fe\u6807...";

        var cts = new CancellationTokenSource();
        _queryCts = cts;
        Cursor = Cursors.WaitCursor;

        try
        {
            var result = await _service.BuildAsync(_project, _itemData, freeOnly, cts.Token).ConfigureAwait(true);
            if (_disposed || cts.IsCancellationRequested || !ReferenceEquals(_queryCts, cts))
            {
                return;
            }

            _result = result;
            RefreshContent();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                ClearCards();
                AddStatusCard("\u5b9d\u7269\u56fe\u6807\u8bfb\u53d6\u5931\u8d25\u3002");
                _statusLabel.Text = "\u8bfb\u53d6\u5931\u8d25";
                MessageBox.Show(this, ex.Message, "\u67e5\u8be2\u5b9d\u7269\u56fe\u6807\u5931\u8d25", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            if (ReferenceEquals(_queryCts, cts))
            {
                _queryCts = null;
            }

            cts.Dispose();
            if (!_disposed)
            {
                Cursor = Cursors.Default;
            }
        }
    }

    private void RefreshContent()
    {
        if (_refreshing) return;

        _refreshing = true;
        try
        {
            CancelPreviewLoading();
            _generation++;
            _cardBatchTimer.Stop();
            ClearCards();

            var result = _result;
            if (result == null)
            {
                AddStatusCard("\u6ca1\u6709\u53ef\u663e\u793a\u7684\u5b9d\u7269\u56fe\u6807\u3002");
                _statusLabel.Text = "\u672a\u8bfb\u53d6";
                return;
            }

            _statusLabel.Text = BuildSummary(result);
            if (result.Warnings.Count > 0)
            {
                _toolTip.SetToolTip(_statusLabel, string.Join(Environment.NewLine, result.Warnings));
            }
            else
            {
                _toolTip.SetToolTip(_statusLabel, null);
            }

            _pendingItems = result.Items;
            _pendingItemIndex = 0;
            if (_pendingItems.Count == 0)
            {
                AddStatusCard(result.FreeOnly ? "\u6ca1\u6709\u7a7a\u95f2\u5b9d\u7269\u56fe\u6807\u3002" : "\u6ca1\u6709\u53ef\u663e\u793a\u7684\u5b9d\u7269\u56fe\u6807\u3002");
                return;
            }

            AddNextCardBatch();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void AddNextCardBatch()
    {
        _cardBatchTimer.Stop();
        if (_pendingItemIndex >= _pendingItems.Count) return;

        _contentPanel.SuspendLayout();
        try
        {
            var generation = _generation;
            var token = _previewCts?.Token ?? CancellationToken.None;
            var end = Math.Min(_pendingItems.Count, _pendingItemIndex + CardCreateBatchSize);
            while (_pendingItemIndex < end)
            {
                var candidate = _pendingItems[_pendingItemIndex++];
                var card = CreateCard(candidate);
                _cards.Add(card);
                _contentPanel.Controls.Add(card.Panel);
            }
        }
        finally
        {
            _contentPanel.ResumeLayout();
        }

        if (_pendingItemIndex < _pendingItems.Count)
        {
            _cardBatchTimer.Start();
        }
        ScheduleVisiblePreviews();
    }

    private PreviewCard CreateCard(ItemIconCatalogCandidate candidate)
    {
        var panel = new Panel
        {
            Width = CardWidth,
            Height = CardHeight,
            Margin = new Padding(CardGap),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        var title = new Label
        {
            Text = $"编号 {candidate.IconId.ToString(CultureInfo.InvariantCulture)}",
            Dock = DockStyle.Top,
            Height = 24,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true
        };

        var picture = new PictureBox
        {
            Dock = DockStyle.Top,
            Height = 78,
            BackColor = Color.FromArgb(44, 44, 48),
            SizeMode = PictureBoxSizeMode.Zoom
        };

        var detail = new Label
        {
            Text = candidate.Detail,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopCenter,
            AutoEllipsis = true,
            Font = new Font(Font.FontFamily, Math.Max(7.5f, Font.Size - 1.0f))
        };

        panel.Controls.Add(detail);
        panel.Controls.Add(picture);
        panel.Controls.Add(title);
        _toolTip.SetToolTip(panel, $"{title.Text}{Environment.NewLine}{candidate.Detail}");
        _toolTip.SetToolTip(title, $"{title.Text}{Environment.NewLine}{candidate.Detail}");
        _toolTip.SetToolTip(picture, $"{title.Text}{Environment.NewLine}{candidate.Detail}");
        _toolTip.SetToolTip(detail, $"{title.Text}{Environment.NewLine}{candidate.Detail}");

        return new PreviewCard(candidate.IconId, panel, picture);
    }

    private void ScheduleVisiblePreviews()
    {
        if (_disposed || _previewCts == null || _previewCts.IsCancellationRequested) return;
        var viewport = _contentPanel.RectangleToScreen(_contentPanel.ClientRectangle);
        viewport.Inflate(0, _contentPanel.ClientSize.Height);
        foreach (var card in _cards.Where(card => !card.LoadStarted &&
                     card.Panel.RectangleToScreen(card.Panel.ClientRectangle).IntersectsWith(viewport)))
        {
            card.LoadStarted = true;
            _ = LoadCardPreviewAsync(card, _generation, _previewCts.Token);
        }
    }

    private async Task LoadCardPreviewAsync(PreviewCard card, int generation, CancellationToken cancellationToken)
    {
        try
        {
            await _previewSemaphore.WaitAsync(cancellationToken).ConfigureAwait(true);
            Bitmap? bitmap = null;
            try
            {
                var cached = await _service.LoadThumbnailAsync(
                    _project,
                    card.IconId,
                    PreviewCanvasSize,
                    cancellationToken).ConfigureAwait(true);
                bitmap = cached?.CreateBitmap();
            }
            finally
            {
                _previewSemaphore.Release();
            }

            if (_disposed || cancellationToken.IsCancellationRequested || generation != _generation)
            {
                bitmap?.Dispose();
                return;
            }

            SetPreviewImage(card.PictureBox, bitmap);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (!_disposed && generation == _generation)
            {
                SetPreviewImage(card.PictureBox, BuildMissingPreview());
            }
        }
    }

    private static string BuildSummary(ItemIconCatalogResult result)
    {
        var mode = result.FreeOnly ? "\u7a7a\u95f2" : "\u5168\u90e8";
        var warningText = result.Warnings.Count == 0 ? string.Empty : $"，警告 {result.Warnings.Count}";
        return $"{mode}：显示 {result.DisplayedCount} / 可用 {result.AvailableCount}；已使用 {result.UsedCount}{warningText}";
    }

    private void AddStatusCard(string text)
    {
        _contentPanel.Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(8),
            ForeColor = SystemColors.ControlText
        });
    }

    private void CancelWork()
    {
        _queryCts?.Cancel();
        if (_disposed)
        {
            _previewCts?.Cancel();
            _previewCts?.Dispose();
            _previewCts = null;
        }
        else
        {
            CancelPreviewLoading();
        }
    }

    private void CancelPreviewLoading()
    {
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = new CancellationTokenSource();
    }

    private void ClearCards()
    {
        foreach (var card in _cards)
        {
            if (card.PictureBox.Image is Image image)
            {
                card.PictureBox.Image = null;
                image.Dispose();
            }
        }

        _cards.Clear();
        _contentPanel.Controls.Clear();
    }

    private static void SetPreviewImage(PictureBox pictureBox, Bitmap? bitmap)
    {
        var old = pictureBox.Image;
        pictureBox.Image = bitmap;
        old?.Dispose();
    }

    private static Bitmap BuildMissingPreview()
    {
        var bitmap = new Bitmap(PreviewCanvasSize, PreviewCanvasSize);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(44, 44, 48));
        graphics.SmoothingMode = SmoothingMode.None;
        using var pen = new Pen(Color.FromArgb(180, 180, 180), 2);
        graphics.DrawLine(pen, 24, 24, PreviewCanvasSize - 24, PreviewCanvasSize - 24);
        graphics.DrawLine(pen, PreviewCanvasSize - 24, 24, 24, PreviewCanvasSize - 24);
        return bitmap;
    }

    private sealed class PreviewCard
    {
        public PreviewCard(int iconId, Panel panel, PictureBox pictureBox)
        {
            IconId = iconId;
            Panel = panel;
            PictureBox = pictureBox;
        }

        public int IconId { get; }
        public Panel Panel { get; }
        public PictureBox PictureBox { get; }
        public bool LoadStarted { get; set; }
    }
}
