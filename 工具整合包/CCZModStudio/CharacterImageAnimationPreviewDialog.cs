using CCZModStudio.Core;
using System.Drawing;

namespace CCZModStudio;

internal sealed class CharacterImageAnimationPreviewDialog : Form
{
    public const int DefaultIntervalMs = 180;
    public const int MinIntervalMs = 60;
    public const int MaxIntervalMs = 1000;

    private readonly ImageAssignmentPreviewService _previewService;
    private CharacterImageAnimationPreview _preview;
    private readonly AspectRatioPictureBox _pictureBox = new();
    private readonly Button _playPauseButton = new();
    private readonly NumericUpDown _intervalInput = new();
    private readonly ComboBox _optionCombo = new();
    private readonly ComboBox _facingCombo = new();
    private readonly Label _frameStatusLabel = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly Action<int>? _intervalChanged;
    private readonly Func<int, CharacterImageAnimationPreview>? _optionPreviewFactory;
    private readonly Func<int, CancellationToken, Task<CharacterImageAnimationPreview>>? _asyncPreviewFactory;
    private readonly bool _loadInitialAsync;
    private readonly Action<int>? _viewAllFrames;
    private readonly Func<int, string> _optionTextProvider;
    private readonly IReadOnlyList<int> _optionValues;
    private readonly string _optionLabel;
    private Bitmap? _currentFrame;
    private int _frameIndex;
    private int _selectedOption;
    private bool _playing = true;
    private bool _resourcesDisposed;
    private bool _updatingOptionCombo;
    private bool _updatingFacingOptions;
    private RsFacing _selectedFacing = RsFacing.Front;
    private bool _mirrorSelectedFacing;
    private IReadOnlyList<CharacterImageAnimationPlaybackFrame> _sPlaybackFrames = Array.Empty<CharacterImageAnimationPlaybackFrame>();
    private CancellationTokenSource? _loadCts;
    private int _loadGeneration;

    public CharacterImageAnimationPreviewDialog(
        ImageAssignmentPreviewService previewService,
        CharacterImageAnimationPreview preview,
        int intervalMs,
        Action<int>? intervalChanged = null,
        string? optionLabel = null,
        Func<int, string>? optionTextProvider = null,
        int? selectedOption = null,
        Func<int, CharacterImageAnimationPreview>? optionPreviewFactory = null,
        IReadOnlyList<int>? optionValues = null,
        Func<int, CancellationToken, Task<CharacterImageAnimationPreview>>? asyncPreviewFactory = null,
        bool loadInitialAsync = false,
        Action<int>? viewAllFrames = null)
    {
        _previewService = previewService;
        _preview = preview;
        _intervalChanged = intervalChanged;
        _optionPreviewFactory = optionPreviewFactory;
        _asyncPreviewFactory = asyncPreviewFactory;
        _loadInitialAsync = loadInitialAsync && asyncPreviewFactory != null;
        _viewAllFrames = viewAllFrames;
        _optionTextProvider = optionTextProvider ?? (value => value.ToString());
        _optionLabel = string.IsNullOrWhiteSpace(optionLabel) ? string.Empty : optionLabel;
        _selectedOption = selectedOption.GetValueOrDefault(preview.StageSlot);
        if (_selectedOption <= 0) _selectedOption = 1;
        _optionValues = _optionPreviewFactory != null || _asyncPreviewFactory != null
            ? NormalizeOptionValues(optionValues, _selectedOption)
            : Array.Empty<int>();
        Text = BuildTitle(preview);
        StartPosition = FormStartPosition.CenterParent;
        Width = preview.Kind == ImageAssignmentResourceKind.S ? 520 : 320;
        Height = preview.Kind == ImageAssignmentResourceKind.S ? 480 : 280;
        MinimumSize = new Size(280, 260);
        ShowInTaskbar = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(layout);

        layout.Controls.Add(BuildToolbar(intervalMs), 0, 0);
        ConfigureSPlayback();

        _pictureBox.Dock = DockStyle.Fill;
        _pictureBox.BackColor = Color.FromArgb(24, 26, 28);
        _pictureBox.BorderStyle = BorderStyle.None;
        layout.Controls.Add(_pictureBox, 0, 1);

        _timer.Interval = NormalizeInterval(intervalMs);
        _timer.Tick += (_, _) => AdvanceFrame();
        Shown += async (_, _) =>
        {
            RenderFrame();
            if (_loadInitialAsync)
            {
                await LoadPreviewAsync(_selectedOption);
            }
            else if (GetCurrentFrameCount() > 1)
            {
                _timer.Start();
            }
        };
        FormClosed += (_, _) => DisposeAnimationResources();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeAnimationResources();
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }

    private Control BuildToolbar(int intervalMs)
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

        _playPauseButton.Text = "暂停";
        _playPauseButton.AutoSize = true;
        _playPauseButton.Margin = new Padding(0, 0, 8, 0);
        _playPauseButton.Click += (_, _) => TogglePlaying();
        toolbar.Controls.Add(_playPauseButton);

        toolbar.Controls.Add(new Label
        {
            Text = "播放间隔(ms)：",
            AutoSize = true,
            Margin = new Padding(0, 5, 4, 0)
        });

        _intervalInput.Minimum = MinIntervalMs;
        _intervalInput.Maximum = MaxIntervalMs;
        _intervalInput.Value = NormalizeInterval(intervalMs);
        _intervalInput.Width = 72;
        _intervalInput.Margin = new Padding(0, 0, 8, 0);
        _intervalInput.ValueChanged += (_, _) =>
        {
            var value = (int)_intervalInput.Value;
            _timer.Interval = value;
            _intervalChanged?.Invoke(value);
        };
        toolbar.Controls.Add(_intervalInput);

        if ((_optionPreviewFactory != null || _asyncPreviewFactory != null) && _optionValues.Count > 0)
        {
            toolbar.Controls.Add(new Label
            {
                Text = _optionLabel,
                AutoSize = true,
                Margin = new Padding(0, 5, 4, 0)
            });

            _optionCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _optionCombo.Name = "AnimationOptionCombo";
            _optionCombo.Width = 84;
            _optionCombo.Margin = new Padding(0, 0, 8, 0);
            foreach (var optionValue in _optionValues)
            {
                _optionCombo.Items.Add(new OptionComboItem(optionValue, _optionTextProvider(optionValue)));
            }

            _updatingOptionCombo = true;
            try
            {
                _optionCombo.SelectedIndex = FindSelectedOptionIndex();
            }
            finally
            {
                _updatingOptionCombo = false;
            }

            _optionCombo.SelectedIndexChanged += async (_, _) => await ChangeOptionFromUiAsync();
            toolbar.Controls.Add(_optionCombo);
        }

        if (_preview.Kind == ImageAssignmentResourceKind.S)
        {
            toolbar.Controls.Add(new Label
            {
                Text = "方向：",
                AutoSize = true,
                Margin = new Padding(0, 5, 4, 0)
            });
            _facingCombo.Name = "AnimationFacingCombo";
            _facingCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _facingCombo.Width = 120;
            _facingCombo.Margin = new Padding(0, 0, 8, 0);
            _facingCombo.SelectedIndexChanged += (_, _) =>
            {
                if (_updatingFacingOptions || _facingCombo.SelectedItem is not FacingComboItem item) return;
                _selectedFacing = item.Facing;
                _mirrorSelectedFacing = item.MirrorHorizontal;
                RebuildSPlaybackFrames();
                ResetSequencePlayback();
            };
            toolbar.Controls.Add(_facingCombo);

            _frameStatusLabel.Name = "AnimationFrameStatus";
            _frameStatusLabel.AutoSize = true;
            _frameStatusLabel.Margin = new Padding(4, 5, 0, 0);
            toolbar.Controls.Add(_frameStatusLabel);
        }

        var stepButton = new Button
        {
            Text = "单帧",
            AutoSize = true,
            Margin = new Padding(0)
        };
        stepButton.Click += (_, _) =>
        {
            _timer.Stop();
            _playing = false;
            _playPauseButton.Text = "播放";
            AdvanceFrame();
        };
        toolbar.Controls.Add(stepButton);
        if (_viewAllFrames != null)
        {
            var viewFramesButton = new Button
            {
                Text = "查看全部单帧",
                AutoSize = true,
                Margin = new Padding(8, 0, 0, 0)
            };
            viewFramesButton.Click += (_, _) => _viewAllFrames(_selectedOption);
            toolbar.Controls.Add(viewFramesButton);
        }

        return toolbar;
    }

    private void ConfigureSPlayback()
    {
        if (_preview.Kind != ImageAssignmentResourceKind.S) return;
        _updatingFacingOptions = true;
        try
        {
            _facingCombo.Items.Clear();
            if (_preview.Sequences.Count == 0)
            {
                _facingCombo.Enabled = false;
                _frameStatusLabel.Text = "等待 S 形象资源";
                _sPlaybackFrames = Array.Empty<CharacterImageAnimationPlaybackFrame>();
                return;
            }

            foreach (var item in new[]
                     {
                         new FacingComboItem(RsFacing.Front, "正面/下", false),
                         new FacingComboItem(RsFacing.Back, "背面/上", false),
                         new FacingComboItem(RsFacing.Side, "侧面/左", false),
                         new FacingComboItem(RsFacing.Side, "侧面/右", true)
                     })
            {
                _facingCombo.Items.Add(item);
            }

            _facingCombo.Enabled = true;
            var selectedIndex = _facingCombo.Items.Cast<FacingComboItem>().ToList().FindIndex(item =>
                item.Facing == _selectedFacing && item.MirrorHorizontal == _mirrorSelectedFacing);
            _facingCombo.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
            var selectedItem = (FacingComboItem)_facingCombo.SelectedItem!;
            _selectedFacing = selectedItem.Facing;
            _mirrorSelectedFacing = selectedItem.MirrorHorizontal;
        }
        finally
        {
            _updatingFacingOptions = false;
        }

        RebuildSPlaybackFrames();
    }

    private void RebuildSPlaybackFrames()
    {
        _sPlaybackFrames = BuildSPlaybackFrames(_preview, _selectedFacing, _mirrorSelectedFacing);
    }

    internal static IReadOnlyList<CharacterImageAnimationPlaybackFrame> BuildSPlaybackFrames(
        CharacterImageAnimationPreview preview,
        RsFacing facing,
        bool mirrorRight)
    {
        if (preview.Kind != ImageAssignmentResourceKind.S || preview.Sequences.Count == 0)
            return Array.Empty<CharacterImageAnimationPlaybackFrame>();

        var frames = new List<CharacterImageAnimationPlaybackFrame>();
        var sourceFiles = RsActionSequenceCatalog.SDefinitions
            .Select(definition => definition.SourceFile)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceFile in sourceFiles)
        {
            var sequences = preview.Sequences
                .Select((sequence, sequenceIndex) => (Sequence: sequence, SequenceIndex: sequenceIndex))
                .Where(item => item.Sequence.Definition.SourceFile.Equals(sourceFile, StringComparison.OrdinalIgnoreCase) &&
                               (item.Sequence.Definition.Facing == facing || item.Sequence.Definition.Facing == null))
                .OrderBy(item => item.Sequence.Definition.PhysicalFrameIndices.Count == 0
                    ? int.MaxValue
                    : item.Sequence.Definition.PhysicalFrameIndices.Min());
            foreach (var item in sequences)
            {
                var mirrorSequence = mirrorRight &&
                                     item.Sequence.Definition.Facing == RsFacing.Side &&
                                     item.Sequence.Definition.MirrorForRight;
                foreach (var frame in item.Sequence.Frames
                             .Select((value, frameIndex) => (Frame: value, FrameIndex: frameIndex))
                             .OrderBy(value => value.Frame.PhysicalFrameIndex))
                {
                    frames.Add(new CharacterImageAnimationPlaybackFrame(
                        item.SequenceIndex,
                        frame.FrameIndex,
                        mirrorSequence));
                }
            }
        }

        return frames;
    }

    private int GetCurrentFrameCount()
    {
        if (_preview.Kind == ImageAssignmentResourceKind.S)
            return Math.Max(1, _sPlaybackFrames.Count);
        return Math.Max(1, _preview.MaxFrameCount);
    }

    private void ResetSequencePlayback()
    {
        _frameIndex = 0;
        RenderFrame();
        if (_playing && GetCurrentFrameCount() > 1) _timer.Start();
        else _timer.Stop();
    }

    private void TogglePlaying()
    {
        _playing = !_playing;
        if (_playing)
        {
            _playPauseButton.Text = "暂停";
            if (GetCurrentFrameCount() > 1) _timer.Start();
        }
        else
        {
            _playPauseButton.Text = "播放";
            _timer.Stop();
        }
    }

    private void AdvanceFrame()
    {
        _frameIndex = (_frameIndex + 1) % GetCurrentFrameCount();
        RenderFrame();
    }

    private void RenderFrame()
    {
        if (_resourcesDisposed)
        {
            return;
        }

        Bitmap frame;
        if (_preview.Kind == ImageAssignmentResourceKind.S && _sPlaybackFrames.Count > 0)
        {
            var normalized = ((_frameIndex % _sPlaybackFrames.Count) + _sPlaybackFrames.Count) % _sPlaybackFrames.Count;
            var playbackFrame = _sPlaybackFrames[normalized];
            frame = _previewService.BuildAnimationSequenceFrame(
                _preview,
                playbackFrame.SequenceIndex,
                playbackFrame.SequenceFrameIndex,
                playbackFrame.MirrorHorizontal);
        }
        else
        {
            frame = _previewService.BuildAnimationCanvas(_preview, _frameIndex);
        }
        var old = _currentFrame;
        _currentFrame = frame;
        _pictureBox.Image = _currentFrame;
        _pictureBox.Invalidate();
        old?.Dispose();
        UpdateFrameStatus();
    }

    private void UpdateFrameStatus()
    {
        if (_preview.Kind != ImageAssignmentResourceKind.S)
            return;
        if (_sPlaybackFrames.Count == 0)
        {
            _frameStatusLabel.Text = "等待 S 形象资源";
            return;
        }

        var normalized = ((_frameIndex % _sPlaybackFrames.Count) + _sPlaybackFrames.Count) % _sPlaybackFrames.Count;
        var playbackFrame = _sPlaybackFrames[normalized];
        var sequence = _preview.Sequences[playbackFrame.SequenceIndex];
        var physical = sequence.Frames[playbackFrame.SequenceFrameIndex].PhysicalFrameIndex;
        var facing = _facingCombo.SelectedItem is FacingComboItem item ? item.Text : "正面/下";
        _frameStatusLabel.Text =
            $"{Path.GetFileName(sequence.SourcePath)} #{sequence.ImageNumber} · {facing} · " +
            $"物理帧 {physical} · {normalized + 1}/{_sPlaybackFrames.Count}";
    }

    private async Task ChangeOptionFromUiAsync()
    {
        if (_updatingOptionCombo ||
            (_optionPreviewFactory == null && _asyncPreviewFactory == null) ||
            _optionCombo.SelectedItem is not OptionComboItem item ||
            item.Value == _selectedOption)
        {
            return;
        }

        if (_asyncPreviewFactory != null)
        {
            await LoadPreviewAsync(item.Value);
            return;
        }

        CharacterImageAnimationPreview nextPreview;
        try
        {
            nextPreview = _optionPreviewFactory!(item.Value);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "播放S形象失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var oldPreview = _preview;
        _timer.Stop();
        _preview = nextPreview;
        _selectedOption = item.Value;
        _frameIndex = 0;
        Text = BuildTitle(_preview);
        oldPreview.Dispose();
        ConfigureSPlayback();
        RenderFrame();
        if (_playing && GetCurrentFrameCount() > 1)
        {
            _timer.Start();
        }
    }

    private async Task LoadPreviewAsync(int option)
    {
        if (_asyncPreviewFactory == null || _resourcesDisposed) return;
        var generation = ++_loadGeneration;
        var oldCts = _loadCts;
        _loadCts = new CancellationTokenSource();
        try { oldCts?.Cancel(); } catch { }
        oldCts?.Dispose();

        _timer.Stop();
        _playPauseButton.Enabled = false;
        _optionCombo.Enabled = false;
        Text = $"{BuildTitle(_preview)} - 正在加载第{option}转...";
        try
        {
            var next = await _asyncPreviewFactory(option, _loadCts.Token).ConfigureAwait(true);
            if (_resourcesDisposed || generation != _loadGeneration || _loadCts.IsCancellationRequested)
            {
                next.Dispose();
                return;
            }

            var old = _preview;
            _preview = next;
            _selectedOption = option;
            _frameIndex = 0;
            Text = BuildTitle(_preview);
            old.Dispose();
            ConfigureSPlayback();
            RenderFrame();
            if (_playing && GetCurrentFrameCount() > 1) _timer.Start();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_resourcesDisposed && generation == _loadGeneration)
            {
                Text = BuildTitle(_preview) + " - 加载失败";
                MessageBox.Show(this, ex.Message, "播放S形象失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            if (!_resourcesDisposed && generation == _loadGeneration)
            {
                _playPauseButton.Enabled = true;
                _optionCombo.Enabled = true;
            }
        }
    }

    private void DisposeAnimationResources()
    {
        if (_resourcesDisposed)
        {
            return;
        }

        _resourcesDisposed = true;
        try { _loadCts?.Cancel(); } catch { }
        _loadCts?.Dispose();
        _loadCts = null;
        _timer.Stop();
        _pictureBox.Image = null;
        _currentFrame?.Dispose();
        _currentFrame = null;
        _preview.Dispose();
    }

    private static int NormalizeInterval(int intervalMs)
        => Math.Clamp(intervalMs <= 0 ? DefaultIntervalMs : intervalMs, MinIntervalMs, MaxIntervalMs);

    private int FindSelectedOptionIndex()
    {
        for (var i = 0; i < _optionValues.Count; i++)
        {
            if (_optionValues[i] == _selectedOption)
            {
                return i;
            }
        }

        return 0;
    }

    private static IReadOnlyList<int> NormalizeOptionValues(IReadOnlyList<int>? optionValues, int selectedOption)
    {
        var values = (optionValues == null || optionValues.Count == 0 ? new[] { selectedOption } : optionValues)
            .Concat(new[] { selectedOption })
            .Where(value => value > 0)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        return values.Length == 0 ? new[] { 1 } : values;
    }

    private static string BuildTitle(CharacterImageAnimationPreview preview)
        => preview.Kind == ImageAssignmentResourceKind.S
            ? $"播放 S 形象 {preview.ImageId}"
            : $"播放 R 形象 {preview.ImageId}";

    private sealed record OptionComboItem(int Value, string Text)
    {
        public override string ToString() => Text;
    }

    private sealed record FacingComboItem(RsFacing Facing, string Text, bool MirrorHorizontal)
    {
        public override string ToString() => Text;
    }
}

internal readonly record struct CharacterImageAnimationPlaybackFrame(
    int SequenceIndex,
    int SequenceFrameIndex,
    bool MirrorHorizontal);
