using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio;

internal sealed class FreeImageAssignmentDialog : Form
{
    private const int FaceCardWidth = 96;
    private const int FaceCardHeight = 118;
    private const int ImageCardWidth = 96;
    private const int ImageCardHeight = 128;
    private const int CardGap = 6;
    private const int CardCreateBatchSize = 50;

    private readonly CczProject _project;
    private readonly DataTable _assignments;
    private readonly ImageAssignmentFreeIdService _freeIdService;
    private readonly ImageAssignmentResourceKind _kind;
    private readonly int _sFactionSlot;
    private readonly CheckBox _freeOnlyCheckBox = new();
    private readonly ComboBox _stanceCombo = new();
    private readonly FlowLayoutPanel _contentPanel = new();
    private readonly ToolTip _toolTip = new();
    private readonly System.Windows.Forms.Timer _cardBatchTimer = new() { Interval = 1 };
    private readonly int _maxPreviewConcurrency = Math.Max(1, Math.Min(Environment.ProcessorCount, 4));
    private readonly List<PreviewCard> _cards = [];
    private ImageAssignmentFreeIdResult _result;
    private IReadOnlyList<FreeImageAssignmentCandidate> _pendingItems = Array.Empty<FreeImageAssignmentCandidate>();
    private ImageAssignmentPreviewStance _pendingStance = ImageAssignmentPreviewStance.Front;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _queryCts;
    private int _pendingItemIndex;
    private int _generation;
    private int _activePreviewLoads;
    private bool _previewScheduleRequested;
    private bool _previewSchedulePosted;
    private bool _refreshing;
    private bool _disposed;

    public ImageAssignmentFreeIdResult CurrentResult => _result;

    public FreeImageAssignmentDialog(
        CczProject project,
        DataTable assignments,
        ImageAssignmentFreeIdService freeIdService,
        ImageAssignmentFreeIdResult result,
        int sFactionSlot)
    {
        _project = project;
        _assignments = assignments;
        _freeIdService = freeIdService;
        _result = result;
        _kind = result.Kind;
        _sFactionSlot = sFactionSlot;

        Text = BuildDialogTitle(_kind);
        StartPosition = FormStartPosition.CenterParent;
        Width = 860;
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
        Shown += (_, _) => RequestPreviewSchedule();
        FormClosing += (_, _) => CancelWork();

        StartQueryRefresh(_result.FreeOnly);
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

        _freeOnlyCheckBox.Text = "空闲";
        _freeOnlyCheckBox.Checked = _result.FreeOnly;
        _freeOnlyCheckBox.AutoSize = true;
        _freeOnlyCheckBox.Margin = new Padding(0, 6, 12, 0);
        _freeOnlyCheckBox.CheckedChanged += (_, _) => RefreshContentFromOptions();
        toolbar.Controls.Add(_freeOnlyCheckBox);

        if (_kind != ImageAssignmentResourceKind.Face)
        {
            toolbar.Controls.Add(new Label
            {
                Text = "角色站位",
                AutoSize = true,
                Margin = new Padding(0, 7, 6, 0)
            });

            _stanceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _stanceCombo.Width = 86;
            _stanceCombo.Margin = new Padding(0, 2, 12, 0);
            foreach (var stance in BuildStanceItems(_kind))
            {
                _stanceCombo.Items.Add(stance);
            }
            if (_stanceCombo.Items.Count > 0) _stanceCombo.SelectedIndex = 0;
            _stanceCombo.SelectedIndexChanged += (_, _) => RefreshContent();
            toolbar.Controls.Add(_stanceCombo);
        }

        var closeButton = new Button
        {
            Text = "关闭",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 0)
        };
        closeButton.Click += (_, _) => Close();
        toolbar.Controls.Add(closeButton);

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
        _contentPanel.Scroll += (_, _) => RequestPreviewSchedule();
        _contentPanel.MouseWheel += (_, _) => RequestPreviewSchedule();
        _contentPanel.Layout += (_, _) => RequestPreviewSchedule();
        _contentPanel.Resize += (_, _) =>
        {
            _contentPanel.PerformLayout();
            RequestPreviewSchedule();
        };
    }

    private void RefreshContentFromOptions()
    {
        if (_refreshing) return;

        try
        {
            StartQueryRefresh(_freeOnlyCheckBox.Checked);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "查询人物形象编号失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void StartQueryRefresh(bool freeOnly)
    {
        CancelWork();
        _generation++;
        _cardBatchTimer.Stop();
            _pendingItems = Array.Empty<FreeImageAssignmentCandidate>();
            _pendingItemIndex = 0;
            _activePreviewLoads = 0;
            _previewScheduleRequested = false;
            _previewSchedulePosted = false;
            ClearCards();
        _contentPanel.Controls.Add(BuildStatusLabel("正在读取编号..."));

        var cts = new CancellationTokenSource();
        _queryCts = cts;
        Cursor = Cursors.WaitCursor;

        try
        {
            var result = await _freeIdService.BuildAsync(_project, _assignments, _kind, _sFactionSlot, freeOnly, cts.Token)
                .ConfigureAwait(true);
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
                _contentPanel.Controls.Add(BuildStatusLabel("编号读取失败。"));
                MessageBox.Show(this, ex.Message, "查询人物形象编号失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            _loadCts = new CancellationTokenSource();
            _generation++;
            _cardBatchTimer.Stop();
            _pendingItems = _result.Items;
            _pendingStance = GetSelectedStance();
            _pendingItemIndex = 0;
            _activePreviewLoads = 0;
            _previewScheduleRequested = false;
            _previewSchedulePosted = false;

            SuspendLayout();
            _contentPanel.SuspendLayout();
            ClearCards();

            if (_result.Items.Count == 0)
            {
                _contentPanel.Controls.Add(BuildEmptyLabel());
            }
        }
        finally
        {
            _contentPanel.ResumeLayout();
            ResumeLayout();
            _refreshing = false;
        }

        AddNextCardBatch();
    }

    private void AddNextCardBatch()
    {
        if (_disposed || _pendingItemIndex >= _pendingItems.Count)
        {
            _cardBatchTimer.Stop();
            return;
        }

        var generation = _generation;
        _contentPanel.SuspendLayout();
        try
        {
            var end = Math.Min(_pendingItemIndex + CardCreateBatchSize, _pendingItems.Count);
            while (_pendingItemIndex < end)
            {
                var card = BuildCard(_pendingItems[_pendingItemIndex], _pendingStance, generation, _pendingItemIndex);
                _cards.Add(card);
                _contentPanel.Controls.Add(card.Container);
                _pendingItemIndex++;
            }
        }
        finally
        {
            _contentPanel.ResumeLayout();
        }

        RequestPreviewSchedule();

        if (_pendingItemIndex < _pendingItems.Count)
        {
            _cardBatchTimer.Start();
        }
        else
        {
            _cardBatchTimer.Stop();
        }
    }

    private PreviewCard BuildCard(
        FreeImageAssignmentCandidate item,
        ImageAssignmentPreviewStance stance,
        int generation,
        int index)
    {
        var cardWidth = _kind == ImageAssignmentResourceKind.Face ? FaceCardWidth : ImageCardWidth;
        var cardHeight = _kind == ImageAssignmentResourceKind.Face ? FaceCardHeight : ImageCardHeight;
        var imageHeight = _kind == ImageAssignmentResourceKind.Face ? 88 : 96;

        var card = new Panel
        {
            Width = cardWidth,
            Height = cardHeight,
            Margin = new Padding(CardGap),
            BackColor = Color.FromArgb(248, 248, 248),
            BorderStyle = BorderStyle.FixedSingle
        };

        var cardLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        cardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        cardLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(cardLayout);

        var title = new Label
        {
            Dock = DockStyle.Fill,
            Text = item.Id.ToString(CultureInfo.InvariantCulture),
            TextAlign = ContentAlignment.MiddleCenter
        };
        cardLayout.Controls.Add(title, 0, 0);

        var picture = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(58, 60, 64),
            SizeMode = PictureBoxSizeMode.Zoom,
            Padding = new Padding(2)
        };
        picture.Height = imageHeight;
        cardLayout.Controls.Add(picture, 0, 1);

        var tooltip = BuildCardTooltip(item, PreviewLoadState.Pending);
        _toolTip.SetToolTip(card, tooltip);
        _toolTip.SetToolTip(cardLayout, tooltip);
        _toolTip.SetToolTip(title, tooltip);
        _toolTip.SetToolTip(picture, tooltip);

        return new PreviewCard(item.Id, index, stance, generation, card, cardLayout, title, picture);
    }

    private void RequestPreviewSchedule()
    {
        _previewScheduleRequested = true;
        if (_disposed || _previewSchedulePosted || !IsHandleCreated)
        {
            return;
        }

        _previewSchedulePosted = true;
        try
        {
            BeginInvoke((MethodInvoker)RunPendingPreviewSchedule);
        }
        catch (InvalidOperationException)
        {
            _previewSchedulePosted = false;
        }
    }

    private void RunPendingPreviewSchedule()
    {
        _previewSchedulePosted = false;
        if (!_previewScheduleRequested)
        {
            return;
        }

        _previewScheduleRequested = false;
        SchedulePreviewLoadsCore();
    }

    private void SchedulePreviewLoadsCore()
    {
        if (_disposed || !IsHandleCreated || _loadCts == null || _loadCts.IsCancellationRequested || _cards.Count == 0)
        {
            return;
        }

        var remainingCapacity = _maxPreviewConcurrency - _activePreviewLoads;
        if (remainingCapacity <= 0)
        {
            return;
        }

        var token = _loadCts.Token;
        var generation = _generation;
        var viewport = _contentPanel.RectangleToScreen(_contentPanel.ClientRectangle);
        viewport.Inflate(0, _contentPanel.ClientSize.Height);

        var selected = new HashSet<PreviewCard>();
        var visibleCards = _cards
            .Where(card =>
                card.Generation == generation &&
                card.State == PreviewLoadState.Pending &&
                card.Container.RectangleToScreen(card.Container.ClientRectangle).IntersectsWith(viewport))
            .OrderBy(card => card.Index);

        foreach (var card in visibleCards)
        {
            if (selected.Count >= remainingCapacity) break;
            selected.Add(card);
        }

        if (selected.Count < remainingCapacity)
        {
            foreach (var card in _cards
                         .Where(card =>
                             card.Generation == generation &&
                             card.State == PreviewLoadState.Pending &&
                             !selected.Contains(card))
                         .OrderBy(card => card.Index))
            {
                if (selected.Count >= remainingCapacity) break;
                selected.Add(card);
            }
        }

        foreach (var card in selected.OrderBy(card => card.Index))
        {
            StartPreviewLoad(card, generation, token);
        }
    }

    private void StartPreviewLoad(PreviewCard card, int generation, CancellationToken token)
    {
        if (card.State != PreviewLoadState.Pending)
        {
            return;
        }

        card.State = PreviewLoadState.Loading;
        _activePreviewLoads++;
        ApplyCardState(card, PreviewLoadState.Loading, null);
        _ = LoadPreviewAsync(card, generation, token);
    }

    private async Task LoadPreviewAsync(PreviewCard card, int generation, CancellationToken token)
    {
        Bitmap? bitmap = null;
        try
        {
            var cached = await _freeIdService.RenderCandidatePreviewAsync(
                _project,
                _kind,
                card.Id,
                _sFactionSlot,
                card.Stance,
                token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            bitmap = cached?.CreateBitmap();

            PostPreviewResult(card, generation, bitmap, bitmap != null);
            bitmap = null;
        }
        catch (OperationCanceledException)
        {
            bitmap?.Dispose();
            PostPreviewCanceled(card, generation);
        }
        catch (Exception ex)
        {
            bitmap?.Dispose();
            Debug.WriteLine($"查询形象预览加载失败：kind={_kind} id={card.Id} stance={card.Stance} generation={generation} {ex}");
            PostPreviewResult(card, generation, null, false);
        }
    }

    private void PostPreviewResult(PreviewCard card, int generation, Bitmap? bitmap, bool hasPreview)
    {
        if (_disposed || !IsHandleCreated)
        {
            bitmap?.Dispose();
            return;
        }

        try
        {
            BeginInvoke((MethodInvoker)(() => ApplyPreviewResult(card, generation, bitmap, hasPreview)));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"查询形象预览回填调度失败：kind={_kind} id={card.Id} stance={card.Stance} generation={generation} {ex}");
            bitmap?.Dispose();
            PostPreviewCanceled(card, generation);
        }
    }

    private void PostPreviewCanceled(PreviewCard card, int generation)
    {
        if (_disposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke((MethodInvoker)(() => CompletePreviewLoad(card, generation, resetToPending: false)));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"查询形象预览取消回填失败：kind={_kind} id={card.Id} stance={card.Stance} generation={generation} {ex}");
        }
    }

    private void ApplyPreviewResult(PreviewCard card, int generation, Bitmap? bitmap, bool hasPreview)
    {
        if (_disposed ||
            generation != _generation ||
            card.Generation != _generation ||
            card.Picture.IsDisposed ||
            !_cards.Contains(card))
        {
            bitmap?.Dispose();
            CompletePreviewLoad(card, generation, resetToPending: false);
            return;
        }

        var old = card.Picture.Image;
        card.Picture.Image = bitmap;
        card.Picture.BackColor = hasPreview ? Color.FromArgb(24, 26, 28) : Color.FromArgb(72, 48, 48);
        old?.Dispose();
        ApplyCardState(card, hasPreview ? PreviewLoadState.Loaded : PreviewLoadState.Failed, null);
        CompletePreviewLoad(card, generation, resetToPending: false);
    }

    private void CompletePreviewLoad(PreviewCard card, int generation, bool resetToPending)
    {
        if (generation == _generation && card.Generation == _generation)
        {
            _activePreviewLoads = Math.Max(0, _activePreviewLoads - 1);
            if (resetToPending && card.State == PreviewLoadState.Loading)
            {
                ApplyCardState(card, PreviewLoadState.Pending, null);
            }

            RequestPreviewSchedule();
        }
    }

    private void ApplyCardState(PreviewCard card, PreviewLoadState state, Bitmap? image)
    {
        card.State = state;
        if (!card.Picture.IsDisposed)
        {
            card.Picture.BackColor = state switch
            {
                PreviewLoadState.Loaded => Color.FromArgb(24, 26, 28),
                PreviewLoadState.Failed => Color.FromArgb(72, 48, 48),
                PreviewLoadState.Loading => Color.FromArgb(52, 62, 76),
                _ => Color.FromArgb(58, 60, 64)
            };
            if (image != null)
            {
                card.Picture.Image = image;
            }
        }

        var tooltip = BuildCardTooltip(new FreeImageAssignmentCandidate(card.Id, string.Empty), state);
        _toolTip.SetToolTip(card.Container, tooltip);
        _toolTip.SetToolTip(card.Layout, tooltip);
        _toolTip.SetToolTip(card.Title, tooltip);
        _toolTip.SetToolTip(card.Picture, tooltip);
    }

    private Label BuildEmptyLabel()
        => BuildStatusLabel(_freeOnlyCheckBox.Checked ? "当前没有可预览的空闲编号。" : "当前没有可预览的编号。");

    private static Label BuildStatusLabel(string text)
        => new()
        {
            AutoSize = true,
            Margin = new Padding(12),
            Text = text
        };

    private void ClearCards()
    {
        _cards.Clear();
        while (_contentPanel.Controls.Count > 0)
        {
            var control = _contentPanel.Controls[0];
            _contentPanel.Controls.RemoveAt(0);
            DisposeControlImages(control);
            control.Dispose();
        }
    }

    private void CancelPreviewLoading()
    {
        var cts = _loadCts;
        _loadCts = null;
        try
        {
            cts?.Cancel();
        }
        catch
        {
            // Best-effort cancellation during form shutdown.
        }
    }

    private void CancelQueryLoading()
    {
        var cts = _queryCts;
        _queryCts = null;
        try
        {
            cts?.Cancel();
        }
        catch
        {
            // Best-effort cancellation during form shutdown.
        }
    }

    private void CancelWork()
    {
        CancelQueryLoading();
        CancelPreviewLoading();
    }

    private static void DisposeControlImages(Control control)
    {
        if (control is PictureBox picture)
        {
            picture.Image?.Dispose();
            picture.Image = null;
        }

        foreach (Control child in control.Controls)
        {
            DisposeControlImages(child);
        }
    }

    private ImageAssignmentPreviewStance GetSelectedStance()
        => _stanceCombo.SelectedItem is StanceComboItem item
            ? item.Stance
            : ImageAssignmentPreviewStance.Front;

    private string BuildCardTooltip(FreeImageAssignmentCandidate item, PreviewLoadState state)
    {
        var kindText = BuildKindText(_kind);
        var stanceText = _kind == ImageAssignmentResourceKind.Face
            ? string.Empty
            : $" {ImageAssignmentFreeIdService.GetStanceDisplayText(GetSelectedStance())}";
        var status = state switch
        {
            PreviewLoadState.Loaded => "已生成预览",
            PreviewLoadState.Failed => "预览不可用",
            PreviewLoadState.Loading => "正在加载预览",
            _ => "等待加载预览"
        };
        return $"{kindText} {item.Id}{stanceText}\r\n{status}";
    }

    private static IReadOnlyList<StanceComboItem> BuildStanceItems(ImageAssignmentResourceKind kind)
    {
        if (kind == ImageAssignmentResourceKind.R)
        {
            return new[]
            {
                new StanceComboItem(ImageAssignmentPreviewStance.Front),
                new StanceComboItem(ImageAssignmentPreviewStance.Back)
            };
        }

        return new[]
        {
            new StanceComboItem(ImageAssignmentPreviewStance.Front),
            new StanceComboItem(ImageAssignmentPreviewStance.Side),
            new StanceComboItem(ImageAssignmentPreviewStance.Back)
        };
    }

    private static string BuildDialogTitle(ImageAssignmentResourceKind kind)
        => kind switch
        {
            ImageAssignmentResourceKind.Face => "查询头像",
            ImageAssignmentResourceKind.S => "查询S形象",
            _ => "查询R形象"
        };

    private static string BuildKindText(ImageAssignmentResourceKind kind)
        => kind switch
        {
            ImageAssignmentResourceKind.Face => "头像",
            ImageAssignmentResourceKind.S => "S形象",
            _ => "R形象"
        };

    private sealed record StanceComboItem(ImageAssignmentPreviewStance Stance)
    {
        public override string ToString() => ImageAssignmentFreeIdService.GetStanceDisplayText(Stance);
    }

    private enum PreviewLoadState
    {
        Pending,
        Loading,
        Loaded,
        Failed
    }

    private sealed class PreviewCard
    {
        public PreviewCard(
            int id,
            int index,
            ImageAssignmentPreviewStance stance,
            int generation,
            Control container,
            Control layout,
            Control title,
            PictureBox picture)
        {
            Id = id;
            Index = index;
            Stance = stance;
            Generation = generation;
            Container = container;
            Layout = layout;
            Title = title;
            Picture = picture;
        }

        public int Id { get; }
        public int Index { get; }
        public ImageAssignmentPreviewStance Stance { get; }
        public int Generation { get; }
        public Control Container { get; }
        public Control Layout { get; }
        public Control Title { get; }
        public PictureBox Picture { get; }
        public PreviewLoadState State { get; set; }
    }
}
