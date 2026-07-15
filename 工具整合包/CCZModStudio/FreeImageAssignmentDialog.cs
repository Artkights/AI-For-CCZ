using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Text;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio;

internal sealed class FreeImageAssignmentDialog : Form
{
    private const int FaceCardWidth = 96;
    private const int FaceCardHeight = 118;
    private const int CardGap = 6;
    private const int CardCreateBatchSize = 50;

    private readonly CczProject _project;
    private readonly DataTable _assignments;
    private readonly ImageAssignmentFreeIdService _freeIdService;
    private readonly ImageAssignmentPreviewService _animationPreviewService;
    private readonly ImageAssignmentResourceKind _kind;
    private readonly int _sFactionSlot;
    private readonly Func<RsFrameDescriptor, bool>? _editFrame;
    private readonly int _animationIntervalMs;
    private readonly CheckBox _freeOnlyCheckBox = new();
    private readonly ComboBox _stanceCombo = new();
    private readonly ComboBox _stageCombo = new();
    private readonly Button _rescanButton = new();
    private readonly Button _copyDiagnosticButton = new();
    private readonly FlowLayoutPanel _contentPanel = new();
    private readonly ToolTip _toolTip = new();
    private readonly System.Windows.Forms.Timer _cardBatchTimer = new() { Interval = 1 };
    private readonly int _maxPreviewConcurrency = Math.Max(1, Math.Min(Environment.ProcessorCount, 4));
    private readonly List<PreviewCard> _cards = [];
    private ImageAssignmentFreeIdResult _result;
    private IReadOnlyList<FreeImageAssignmentCandidate> _pendingItems = Array.Empty<FreeImageAssignmentCandidate>();
    private ImageAssignmentPreviewStance _pendingStance = ImageAssignmentPreviewStance.Front;
    private int _pendingStageSlot = 1;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _queryCts;
    private int _pendingItemIndex;
    private int _generation;
    private int _activePreviewLoads;
    private bool _previewScheduleRequested;
    private bool _previewSchedulePosted;
    private bool _refreshing;
    private bool _updatingStageOptions;
    private bool _disposed;

    public ImageAssignmentFreeIdResult CurrentResult => _result;

    public FreeImageAssignmentDialog(
        CczProject project,
        DataTable assignments,
        ImageAssignmentFreeIdService freeIdService,
        ImageAssignmentFreeIdResult result,
        int sFactionSlot,
        Func<RsFrameDescriptor, bool>? editFrame,
        int animationIntervalMs = CharacterImageAnimationPreviewDialog.DefaultIntervalMs)
    {
        _project = project;
        _assignments = assignments;
        _freeIdService = freeIdService;
        _animationPreviewService = freeIdService.PreviewService;
        _result = result;
        _kind = result.Kind;
        _sFactionSlot = sFactionSlot;
        _editFrame = editFrame;
        _animationIntervalMs = Math.Clamp(
            animationIntervalMs,
            CharacterImageAnimationPreviewDialog.MinIntervalMs,
            CharacterImageAnimationPreviewDialog.MaxIntervalMs);

        Text = BuildDialogTitle(_kind);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Width = 960;
        Height = 720;
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
        UpdateStageOptions();
        ConfigureContentPanel();
        layout.Controls.Add(_contentPanel, 0, 1);

        _cardBatchTimer.Tick += (_, _) => AddNextCardBatch();
        Shown += (_, _) =>
        {
            ConstrainToWorkingArea();
            ReapplyCardMetrics();
            RequestPreviewSchedule();
        };
        DpiChanged += (_, _) => ScheduleCardRelayout();
        FontChanged += (_, _) => ScheduleCardRelayout();
        FormClosing += (_, _) => CancelWork();

        StartQueryRefresh(_result.FreeOnly, forceFresh: true);
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
        _freeOnlyCheckBox.Name = "FreeImageFreeOnlyCheckBox";
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

        if (_kind == ImageAssignmentResourceKind.S)
        {
            toolbar.Controls.Add(new Label
            {
                Text = "转数",
                AutoSize = true,
                Margin = new Padding(0, 7, 6, 0)
            });

            _stageCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _stageCombo.Name = "FreeImageStageCombo";
            _stageCombo.Width = 86;
            _stageCombo.Margin = new Padding(0, 2, 12, 0);
            _stageCombo.SelectedIndexChanged += (_, _) =>
            {
                if (!_updatingStageOptions) RefreshContent();
            };
            toolbar.Controls.Add(_stageCombo);
        }

        _rescanButton.Name = "FreeImageRescanButton";
        _rescanButton.Text = "重新扫描";
        _rescanButton.AutoSize = true;
        _rescanButton.Margin = new Padding(0, 0, 8, 0);
        _rescanButton.Click += (_, _) => StartQueryRefresh(_freeOnlyCheckBox.Checked, forceFresh: true);
        toolbar.Controls.Add(_rescanButton);

        _copyDiagnosticButton.Name = "FreeImageCopyDiagnosticButton";
        _copyDiagnosticButton.Text = "复制诊断";
        _copyDiagnosticButton.AutoSize = true;
        _copyDiagnosticButton.Enabled = false;
        _copyDiagnosticButton.Margin = new Padding(0, 0, 8, 0);
        _copyDiagnosticButton.Click += (_, _) => CopyAvailabilityDiagnostic();
        toolbar.Controls.Add(_copyDiagnosticButton);

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
            StartQueryRefresh(_freeOnlyCheckBox.Checked, forceFresh: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "查询人物形象编号失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void StartQueryRefresh(bool freeOnly, bool forceFresh)
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
            var result = await _freeIdService.BuildAsync(
                    _project,
                    _assignments,
                    _kind,
                    _sFactionSlot,
                    freeOnly,
                    forceFresh,
                    cts.Token)
                .ConfigureAwait(true);
            if (_disposed || cts.IsCancellationRequested || !ReferenceEquals(_queryCts, cts))
            {
                return;
            }

            _result = result;
            _copyDiagnosticButton.Enabled = result.AvailabilityReport != null;
            var availabilityTooltip = BuildAvailabilityTooltip(result.AvailabilityReport);
            _toolTip.SetToolTip(_rescanButton, availabilityTooltip);
            _toolTip.SetToolTip(_copyDiagnosticButton, availabilityTooltip);
            UpdateStageOptions();
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
            _pendingStageSlot = GetSelectedStageSlot();
            _pendingItemIndex = 0;
            _activePreviewLoads = 0;
            _previewScheduleRequested = false;
            _previewSchedulePosted = false;

            SuspendLayout();
            _contentPanel.SuspendLayout();
            ClearCards();

            if (_result.AvailabilityReport is { IsArchiveIntegrityValid: false } damaged)
            {
                _contentPanel.Controls.Add(BuildStatusLabel(
                    "档案索引损坏，已禁用预览编辑和导入。\r\n" +
                    string.Join("\r\n", damaged.IntegrityDiagnostics) +
                    "\r\n请先使用“修复/整理 R/S 档案”，修复后点击“重新扫描”。"));
            }
            else if (_result.Items.Count == 0)
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
            var metrics = BuildCurrentCardMetrics();
            while (_pendingItemIndex < end)
            {
                var card = BuildCard(
                    _pendingItems[_pendingItemIndex],
                    _pendingStance,
                    _pendingStageSlot,
                    generation,
                    _pendingItemIndex,
                    metrics);
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
        int stageSlot,
        int generation,
        int index,
        FreeImageCardMetrics metrics)
    {
        var hasAnimationButton = _kind != ImageAssignmentResourceKind.Face;

        var card = new Panel
        {
            Size = metrics.CardSize,
            MinimumSize = metrics.CardSize,
            MaximumSize = metrics.CardSize,
            AutoSize = false,
            Margin = metrics.Margin,
            BackColor = Color.FromArgb(248, 248, 248),
            BorderStyle = BorderStyle.FixedSingle
        };

        var cardLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = hasAnimationButton ? 3 : 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            AutoSize = false,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };
        cardLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Math.Max(1, metrics.CardSize.Width - 2)));
        cardLayout.SizeChanged += (_, _) =>
        {
            var clientWidth = Math.Max(1, cardLayout.ClientSize.Width);
            if (Math.Abs(cardLayout.ColumnStyles[0].Width - clientWidth) < 0.5f) return;
            cardLayout.ColumnStyles[0].Width = clientWidth;
            cardLayout.PerformLayout();
        };
        cardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, metrics.TitleHeight));
        cardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, metrics.PictureHeight));
        if (hasAnimationButton)
        {
            cardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, metrics.ActionsHeight));
        }
        card.Controls.Add(cardLayout);

        var title = new Label
        {
            Name = "FreeImageCardTitle",
            Dock = DockStyle.Fill,
            Text = item.Id.ToString(CultureInfo.InvariantCulture),
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = false,
            AutoSize = false,
            Padding = Padding.Empty,
            UseCompatibleTextRendering = false
        };
        cardLayout.Controls.Add(title, 0, 0);

        var picture = new AspectRatioPictureBox
        {
            Name = "FreeImageCardPicture",
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(58, 60, 64),
            ShowCheckerboard = hasAnimationButton,
            Padding = Padding.Empty
        };
        cardLayout.Controls.Add(picture, 0, 1);

        TableLayoutPanel? actions = null;
        Button? playButton = null;
        Button? framesButton = null;
        if (hasAnimationButton)
        {
            actions = new TableLayoutPanel
            {
                Name = "FreeImageCardActions",
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty
            };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            playButton = new Button
            {
                Name = "FreeImageCardPlayButton",
                Dock = DockStyle.Fill,
                Text = "播放",
                Margin = new Padding(metrics.ContentPadding + 1, metrics.ContentPadding, metrics.ContentPadding, metrics.ContentPadding + 1)
            };
            playButton.Click += (_, _) => PlayCardAnimation(item.Id);
            framesButton = new Button
            {
                Name = "FreeImageCardAllFramesButton",
                Dock = DockStyle.Fill,
                Text = "全部帧",
                Margin = new Padding(metrics.ContentPadding, metrics.ContentPadding, metrics.ContentPadding + 1, metrics.ContentPadding + 1)
            };
            framesButton.Click += (_, _) => OpenCardSingleFrames(item.Id);
            actions.Controls.Add(playButton, 0, 0);
            actions.Controls.Add(framesButton, 1, 0);
            cardLayout.Controls.Add(actions, 0, 2);
            var menu = new ContextMenuStrip();
            menu.Items.Add("查看全部单帧", null, (_, _) => OpenCardSingleFrames(item.Id));
            card.ContextMenuStrip = menu;
            cardLayout.ContextMenuStrip = menu;
            picture.ContextMenuStrip = menu;
            card.Disposed += (_, _) => menu.Dispose();
        }

        var tooltip = BuildCardTooltip(item, PreviewLoadState.Pending);
        _toolTip.SetToolTip(card, tooltip);
        _toolTip.SetToolTip(cardLayout, tooltip);
        _toolTip.SetToolTip(title, tooltip);
        _toolTip.SetToolTip(picture, tooltip);
        if (playButton != null) _toolTip.SetToolTip(playButton, $"播放 {BuildKindText(_kind)} {item.Id}");

        var previewCard = new PreviewCard(
            item.Id,
            index,
            stance,
            stageSlot,
            generation,
            card,
            cardLayout,
            title,
            picture,
            actions,
            playButton,
            framesButton);
        ApplyCardMetrics(previewCard, metrics);
        return previewCard;
    }

    private void PlayCardAnimation(int id)
    {
        if (_kind == ImageAssignmentResourceKind.Face)
        {
            return;
        }

        if (id < 0)
        {
            MessageBox.Show(this, $"{BuildKindText(_kind)} 编号不能小于 0：{id}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var stageSlot = GetSelectedStageSlot();
            var preview = _kind == ImageAssignmentResourceKind.S
                ? new CharacterImageAnimationPreview(
                    ImageAssignmentResourceKind.S,
                    id,
                    stageSlot,
                    "正在后台读取 S 形象动画...",
                    3,
                    3,
                    64,
                    64,
                    Array.Empty<CharacterImageAnimationCell>(),
                    new[] { "正在后台读取 S 形象动画..." })
                : _animationPreviewService.BuildRAnimationPreview(_project, id);
            var dialog = new CharacterImageAnimationPreviewDialog(
                _animationPreviewService,
                preview,
                _animationIntervalMs,
                optionLabel: _kind == ImageAssignmentResourceKind.S ? "转数：" : null,
                optionTextProvider: _kind == ImageAssignmentResourceKind.S ? CharacterImageResourceService.BuildSImageStageText : null,
                selectedOption: _kind == ImageAssignmentResourceKind.S ? stageSlot : null,
                optionPreviewFactory: _kind == ImageAssignmentResourceKind.S
                    ? nextStageSlot => _animationPreviewService.BuildSAnimationPreview(_project, id, jobId: null, _sFactionSlot, nextStageSlot)
                    : null,
                optionValues: _kind == ImageAssignmentResourceKind.S
                    ? new[] { 1, 2, 3 }
                    : null,
                asyncPreviewFactory: _kind == ImageAssignmentResourceKind.S
                    ? (nextStageSlot, token) => _animationPreviewService.LoadAnimationAsync(
                        _project,
                        ImageAssignmentResourceKind.S,
                        id,
                        jobId: null,
                        _sFactionSlot,
                        nextStageSlot,
                        token)
                    : null,
                loadInitialAsync: _kind == ImageAssignmentResourceKind.S,
                viewAllFrames: requestedStage => OpenCardSingleFrames(id, requestedStage));
            dialog.Show(this);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Play free image animation failed: " + ex);
            MessageBox.Show(this, ex.Message, "播放R/S形象失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenCardSingleFrames(int id, int? requestedStageOverride = null)
    {
        if (_kind == ImageAssignmentResourceKind.Face) return;
        var service = new RsSingleFrameCatalogService();
        var requestedStage = requestedStageOverride ?? GetSelectedStageSlot();
        var stageResolution = _kind == ImageAssignmentResourceKind.S
            ? CharacterImageResourceService.ResolveSPreviewStage(_project, id, jobId: null, _sFactionSlot, requestedStage)
            : null;
        var stage = stageResolution?.EffectiveStageSlot ?? requestedStage;
        var sourceLabel = string.Join("；", new[] { "R/S 编号候选", stageResolution?.FallbackDetail }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        RsFrameCatalog Factory(int stageSlot, int factionSlot) => _kind == ImageAssignmentResourceKind.R
            ? service.BuildR(_project, id, sourceLabel)
            : service.BuildS(_project, id, jobId: null, factionSlot, stageSlot, sourceLabel);
        var edited = false;
        Func<RsFrameDescriptor, bool>? editFrame = _editFrame == null
            ? null
            : descriptor =>
            {
                if (!_editFrame(descriptor)) return false;
                edited = true;
                return true;
            };
        RsSingleFrameViewerDialog.TryShowOwned(
            this,
            () => Factory(stage, _sFactionSlot),
            Factory,
            editFrame,
            "查看候选单帧失败",
            "Open free-ID single-frame viewer");
        if (edited) RefreshContent();
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
            // Deliberately leave spare workers idle. Loading distant cards here made
            // opening a query silently decode the complete archive even when the user
            // never scrolled beyond the first page.
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
            var preview = await _freeIdService.RenderCandidateDetailsAsync(
                _project,
                _kind,
                card.Id,
                _sFactionSlot,
                card.Stance,
                card.StageSlot,
                token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            bitmap = preview.Representative?.CreateBitmap();

            PostPreviewResult(card, generation, bitmap, preview);
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
            PostPreviewResult(card, generation, null, null);
        }
    }

    private void PostPreviewResult(
        PreviewCard card,
        int generation,
        Bitmap? bitmap,
        ImageAssignmentCandidatePreview? preview)
    {
        if (_disposed || !IsHandleCreated)
        {
            bitmap?.Dispose();
            return;
        }

        try
        {
            BeginInvoke((MethodInvoker)(() => ApplyPreviewResult(card, generation, bitmap, preview)));
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

    private void ApplyPreviewResult(
        PreviewCard card,
        int generation,
        Bitmap? bitmap,
        ImageAssignmentCandidatePreview? preview)
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

        var hasPreview = bitmap != null;
        SetPictureImage(card.Picture, bitmap);
        ApplyCardState(card, hasPreview || preview is { SelectedStageAvailable: false }
            ? PreviewLoadState.Loaded
            : PreviewLoadState.Failed, null);
        if (preview != null)
        {
            card.Title.Text = preview.RepresentativeLabel;
            if (card.PlayButton != null) card.PlayButton.Enabled = preview.SelectedStageAvailable;
            var tooltip = string.Join("\r\n", new[] { $"{BuildKindText(_kind)} {card.Id}", preview.StatusText }
                .Where(text => !string.IsNullOrWhiteSpace(text)));
            _toolTip.SetToolTip(card.Container, tooltip);
            _toolTip.SetToolTip(card.Picture, tooltip);
        }
        card.Picture.BackColor = hasPreview ? Color.FromArgb(24, 26, 28) : Color.FromArgb(72, 48, 48);
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
                SetPictureImage(card.Picture, image);
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
        if (_contentPanel.Controls.Count == 0) return;

        var controls = _contentPanel.Controls.Cast<Control>().ToArray();
        _contentPanel.SuspendLayout();
        try
        {
            _contentPanel.Controls.Clear();
            foreach (var control in controls)
            {
                DisposeControlImages(control);
                control.Dispose();
            }
        }
        finally
        {
            _contentPanel.ResumeLayout(performLayout: !_disposed);
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
            SetPictureImage(picture, null);
        }

        foreach (Control child in control.Controls)
        {
            DisposeControlImages(child);
        }
    }

    private static void SetPictureImage(PictureBox picture, Image? image)
    {
        var old = picture.Image;
        if (ReferenceEquals(old, image)) return;
        picture.Image = null;
        picture.Image = image;
        old?.Dispose();
    }

    private ImageAssignmentPreviewStance GetSelectedStance()
        => _stanceCombo.SelectedItem is StanceComboItem item
            ? item.Stance
            : ImageAssignmentPreviewStance.Front;

    private int GetSelectedStageSlot()
        => _stageCombo.SelectedItem is StageComboItem item
            ? item.StageSlot
            : 1;

    private void UpdateStageOptions()
    {
        if (_kind != ImageAssignmentResourceKind.S) return;
        var selected = GetSelectedStageSlot();
        var stages = new[] { 1, 2, 3 };

        _updatingStageOptions = true;
        try
        {
            _stageCombo.Items.Clear();
            foreach (var stage in stages) _stageCombo.Items.Add(new StageComboItem(stage));
            var selectedIndex = Array.IndexOf(stages, selected);
            _stageCombo.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
        }
        finally
        {
            _updatingStageOptions = false;
        }
    }

    internal static FreeImageCardMetrics BuildCardMetrics(
        int dpi,
        Font font,
        ImageAssignmentResourceKind kind,
        string? maximumIdText = null)
    {
        dpi = Math.Max(96, dpi);
        int Scale(int value) => Math.Max(1, (int)Math.Round(value * dpi / 96d));
        if (kind == ImageAssignmentResourceKind.Face)
        {
            return new FreeImageCardMetrics(
                new Size(Scale(FaceCardWidth), Scale(FaceCardHeight)),
                new Padding(Scale(CardGap)),
                Scale(22),
                Scale(96),
                0,
                Scale(2));
        }

        var playWidth = TextRenderer.MeasureText("播放", font, Size.Empty, TextFormatFlags.NoPadding).Width + Scale(20);
        var framesWidth = TextRenderer.MeasureText("全部帧", font, Size.Empty, TextFormatFlags.NoPadding).Width + Scale(20);
        var numericSize = string.IsNullOrWhiteSpace(maximumIdText)
            ? Size.Empty
            : TextRenderer.MeasureText(maximumIdText, font, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        var numericWidth = numericSize.IsEmpty ? 0 : numericSize.Width + Scale(16);
        var width = Math.Max(Math.Max(Scale(96), playWidth + framesWidth + Scale(12)), numericWidth);
        var titleHeight = Math.Max(Scale(22), numericSize.Height + Scale(2));
        var pictureHeight = Scale(96);
        var actionsHeight = Scale(36);
        return new FreeImageCardMetrics(
            new Size(width, titleHeight + pictureHeight + actionsHeight + Scale(2)),
            new Padding(Scale(CardGap)),
            titleHeight,
            pictureHeight,
            actionsHeight,
            Scale(2));
    }

    private FreeImageCardMetrics BuildCurrentCardMetrics()
    {
        var maximumIdText = _pendingItems
            .Select(item => item.Id.ToString(CultureInfo.InvariantCulture))
            .OrderByDescending(text => TextRenderer.MeasureText(
                text,
                Font,
                Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width)
            .FirstOrDefault();
        return BuildCardMetrics(DeviceDpi, Font, _kind, maximumIdText);
    }

    private void ApplyCardMetrics(PreviewCard card, FreeImageCardMetrics metrics)
    {
        card.Container.Size = metrics.CardSize;
        card.Container.MinimumSize = metrics.CardSize;
        card.Container.MaximumSize = metrics.CardSize;
        card.Container.Margin = metrics.Margin;
        card.Layout.ColumnStyles[0].SizeType = SizeType.Absolute;
        card.Layout.RowStyles[0].Height = metrics.TitleHeight;
        card.Layout.RowStyles[1].Height = metrics.PictureHeight;
        card.Picture.Padding = new Padding(metrics.ContentPadding);
        if (card.Actions != null)
        {
            card.Layout.RowStyles[2].Height = metrics.ActionsHeight;
            card.PlayButton!.Margin = new Padding(metrics.ContentPadding + 1, metrics.ContentPadding, metrics.ContentPadding, metrics.ContentPadding + 1);
            card.FramesButton!.Margin = new Padding(metrics.ContentPadding, metrics.ContentPadding, metrics.ContentPadding + 1, metrics.ContentPadding + 1);
        }
        SynchronizeCardColumnWidth(card);
        EnsureNumericTitleFits(card, metrics);
        AssertCardContentsContained(card);
    }

    private void EnsureNumericTitleFits(PreviewCard card, FreeImageCardMetrics metrics)
    {
        if (_kind == ImageAssignmentResourceKind.Face || string.IsNullOrWhiteSpace(card.Title.Text)) return;
        var measured = TextRenderer.MeasureText(
            card.Title.Text,
            card.Title.Font,
            Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        if (measured.Width <= card.Title.ClientSize.Width && measured.Height <= card.Title.ClientSize.Height) return;

        var requiredWidth = Math.Max(card.Container.Width, measured.Width + Math.Max(8, metrics.ContentPadding * 4));
        var requiredHeight = Math.Max(metrics.TitleHeight, measured.Height + Math.Max(2, metrics.ContentPadding));
        var corrected = new Size(
            requiredWidth,
            requiredHeight + metrics.PictureHeight + metrics.ActionsHeight + Math.Max(1, metrics.ContentPadding));
        Debug.WriteLine(
            $"R/S 编号标签自动纠正：id={card.Title.Text} dpi={DeviceDpi} font={card.Title.Font.Name}/{card.Title.Font.SizeInPoints:F1} " +
            $"measured={measured} label={card.Title.ClientSize} card={card.Container.Size} corrected={corrected}");
        card.Container.MinimumSize = corrected;
        card.Container.MaximumSize = corrected;
        card.Container.Size = corrected;
        card.Layout.RowStyles[0].Height = requiredHeight;
        SynchronizeCardColumnWidth(card);
    }

    private static void SynchronizeCardColumnWidth(PreviewCard card)
    {
        card.Container.PerformLayout();
        card.Layout.ColumnStyles[0].SizeType = SizeType.Absolute;
        card.Layout.ColumnStyles[0].Width = Math.Max(1, card.Layout.ClientSize.Width);
        card.Layout.PerformLayout();
    }

    private void ReapplyCardMetrics()
    {
        if (_disposed) return;
        var scrollPosition = new Point(
            Math.Abs(_contentPanel.AutoScrollPosition.X),
            Math.Abs(_contentPanel.AutoScrollPosition.Y));
        var metrics = BuildCurrentCardMetrics();
        foreach (var card in _cards) ApplyCardMetrics(card, metrics);
        _contentPanel.Padding = new Padding(Math.Max(1, (int)Math.Round(CardGap * DeviceDpi / 96d)));
        _contentPanel.PerformLayout();
        _contentPanel.AutoScrollPosition = scrollPosition;
    }

    private void ScheduleCardRelayout()
    {
        if (_disposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke((MethodInvoker)(() =>
            {
                ConstrainToWorkingArea();
                ReapplyCardMetrics();
                RequestPreviewSchedule();
            }));
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void ConstrainToWorkingArea()
    {
        if (!IsHandleCreated) return;
        var working = Screen.FromControl(this).WorkingArea;
        var scale = DeviceDpi / 96d;
        MinimumSize = new Size(
            Math.Min(working.Width, Math.Max(480, (int)Math.Round(640 * scale))),
            Math.Min(working.Height, Math.Max(360, (int)Math.Round(480 * scale))));
        var width = Math.Min(Width, working.Width);
        var height = Math.Min(Height, working.Height);
        var x = Math.Clamp(Left, working.Left, working.Right - width);
        var y = Math.Clamp(Top, working.Top, working.Bottom - height);
        Bounds = new Rectangle(x, y, width, height);
    }

    [Conditional("DEBUG")]
    private static void AssertCardContentsContained(PreviewCard card)
    {
        card.Layout.PerformLayout();
        foreach (Control child in card.Layout.Controls)
            Debug.Assert(card.Layout.ClientRectangle.Contains(child.Bounds), $"Card child {child.Name} is clipped: {child.Bounds} / {card.Layout.ClientRectangle}");
        if (card.Actions == null) return;
        card.Actions.PerformLayout();
        foreach (Control child in card.Actions.Controls)
            Debug.Assert(card.Actions.ClientRectangle.Contains(child.Bounds), $"Card action {child.Text} is clipped: {child.Bounds} / {card.Actions.ClientRectangle}");
    }

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

    private static string BuildAvailabilityTooltip(ImageAssignmentAvailabilityReport? report)
    {
        if (report == null) return "重新读取 R/S 编号和资源布局。";
        if (!report.IsArchiveIntegrityValid)
            return "档案索引损坏：" + string.Join("；", report.IntegrityDiagnostics) + "。全部写入入口已禁用。";
        var source = report.FromCache ? "缓存快照" : "新鲜扫描";
        var rejected = report.RejectedCandidates.Count == 0
            ? "无被排除候选"
            : $"排除 {report.RejectedCandidates.Count} 个候选；可复制完整诊断";
        return $"{source}：可用 {report.AvailableIds.Count} 个，{rejected}。";
    }

    private void CopyAvailabilityDiagnostic()
    {
        var report = _result.AvailabilityReport;
        if (report == null) return;
        var text = new StringBuilder();
        text.AppendLine($"查询类型：{BuildKindText(_kind)}");
        text.AppendLine($"可用编号：{report.AvailableIds.Count}");
        text.AppendLine($"来源：{(report.FromCache ? "缓存快照" : "新鲜扫描")}");
        text.AppendLine($"索引完整：{report.IsArchiveIntegrityValid}");
        if (report.IntegrityDiagnostics.Count > 0)
        {
            text.AppendLine("索引完整性诊断：");
            foreach (var diagnostic in report.IntegrityDiagnostics) text.AppendLine("- " + diagnostic);
        }
        text.AppendLine("资源指纹：");
        foreach (var source in report.SourceFingerprints)
        {
            text.AppendLine(
                $"- {source.Path} | length={source.Length} | ticks={source.LastWriteTimeUtcTicks} | entries={source.EntryCount} | index={source.IndexSha256}");
        }
        text.AppendLine("被排除编号：");
        foreach (var rejected in report.RejectedCandidates)
        {
            text.AppendLine($"- {rejected.Id}：{string.Join("；", rejected.Reasons)}");
        }
        Clipboard.SetText(text.ToString());
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

    private sealed record StageComboItem(int StageSlot)
    {
        public override string ToString() => CharacterImageResourceService.BuildSImageStageText(StageSlot);
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
            int stageSlot,
            int generation,
            Control container,
            TableLayoutPanel layout,
            Control title,
            PictureBox picture,
            TableLayoutPanel? actions,
            Button? playButton,
            Button? framesButton)
        {
            Id = id;
            Index = index;
            Stance = stance;
            StageSlot = stageSlot;
            Generation = generation;
            Container = container;
            Layout = layout;
            Title = title;
            Picture = picture;
            Actions = actions;
            PlayButton = playButton;
            FramesButton = framesButton;
        }

        public int Id { get; }
        public int Index { get; }
        public ImageAssignmentPreviewStance Stance { get; }
        public int StageSlot { get; }
        public int Generation { get; }
        public Control Container { get; }
        public TableLayoutPanel Layout { get; }
        public Control Title { get; }
        public PictureBox Picture { get; }
        public TableLayoutPanel? Actions { get; }
        public Button? PlayButton { get; }
        public Button? FramesButton { get; }
        public PreviewLoadState State { get; set; }
    }
}

internal readonly record struct FreeImageCardMetrics(
    Size CardSize,
    Padding Margin,
    int TitleHeight,
    int PictureHeight,
    int ActionsHeight,
    int ContentPadding);
