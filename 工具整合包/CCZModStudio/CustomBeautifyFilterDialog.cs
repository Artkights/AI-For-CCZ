using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Drawing.Drawing2D;

namespace CCZModStudio;

public sealed class CustomBeautifyFilterDialog : Form
{
    private readonly Bitmap _previewSource;
    private readonly System.Windows.Forms.Timer _previewTimer = new() { Interval = 180 };
    private readonly PictureBox _previewBox = new();
    private readonly Label _previewLabel = new();
    private readonly Button _photoColorButton = new();
    private readonly Button _loadGlobalButton = new();
    private readonly Button _saveGlobalButton = new();
    private readonly Button _resetButton = new();
    private readonly CheckBox _preserveLuminosityCheckBox = new();
    private readonly Dictionary<string, NumericUpDown> _inputs = new(StringComparer.Ordinal);
    private bool _updating;
    private Bitmap? _previewBitmap;

    public CustomBeautifyFilterDialog(
        BeautifyCustomFilterSettings initialSettings,
        BeautifyCustomFilterSettings? globalDefault,
        Bitmap previewSource,
        int strength)
    {
        Settings = (MapDraftService.NormalizeCustomBeautifyFilter(initialSettings.Clone()) ?? BeautifyCustomFilterSettings.CreateDefault()).Clone();
        GlobalDefault = globalDefault?.Clone();
        Strength = Math.Clamp(strength, 0, 3);
        _previewSource = new Bitmap(previewSource);

        Text = "自定义美化滤镜";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.Sizable;
        Width = 920;
        Height = 640;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(10)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 430));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);

        layout.Controls.Add(BuildParameterPanel(), 0, 0);
        layout.Controls.Add(BuildPreviewPanel(), 1, 0);
        layout.Controls.Add(BuildButtonPanel(), 0, 1);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 1)!, 2);

        AcceptButton = layout.GetControlFromPosition(0, 1)!.Controls.OfType<Button>().FirstOrDefault(button => button.DialogResult == DialogResult.OK);
        CancelButton = layout.GetControlFromPosition(0, 1)!.Controls.OfType<Button>().FirstOrDefault(button => button.DialogResult == DialogResult.Cancel);

        ApplySettingsToInputs(Settings);
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            RebuildPreview();
        };
        Shown += (_, _) => QueuePreview();
        FormClosed += (_, _) =>
        {
            _previewTimer.Dispose();
            _previewBitmap?.Dispose();
            _previewSource.Dispose();
        };
    }

    public BeautifyCustomFilterSettings Settings { get; private set; }
    public BeautifyCustomFilterSettings? GlobalDefault { get; }
    public int Strength { get; }
    public bool SaveAsGlobalDefault { get; private set; }

    private Control BuildParameterPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 3,
            RowCount = 1
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74));

        AddColorRow(panel);
        AddNumericRow(panel, "PhotoDensity", "滤镜密度", 0, 100, 1, Settings.PhotoDensity * 100f, "%");
        AddNumericRow(panel, "BalanceR", "红色平衡", -100, 100, 1, Settings.BalanceR * 100f, "%");
        AddNumericRow(panel, "BalanceG", "绿色平衡", -100, 100, 1, Settings.BalanceG * 100f, "%");
        AddNumericRow(panel, "BalanceB", "蓝色平衡", -100, 100, 1, Settings.BalanceB * 100f, "%");
        AddNumericRow(panel, "Saturation", "饱和度", 0, 300, 1, Settings.Saturation * 100f, "%");
        AddNumericRow(panel, "Brightness", "亮度", -100, 100, 1, Settings.Brightness * 100f, "%");
        AddNumericRow(panel, "Contrast", "对比度", 0, 300, 1, Settings.Contrast * 100f, "%");
        AddNumericRow(panel, "HighlightCompression", "高光压缩", -100, 100, 1, Settings.HighlightCompression * 100f, "%");
        AddNumericRow(panel, "ShadowLift", "阴影提升", -100, 100, 1, Settings.ShadowLift * 100f, "%");
        AddNumericRow(panel, "MidtoneGamma", "中间调 Gamma", 20, 500, 1, Settings.MidtoneGamma * 100f, "%");

        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _preserveLuminosityCheckBox.Text = "保留明度";
        _preserveLuminosityCheckBox.AutoSize = true;
        _preserveLuminosityCheckBox.CheckedChanged += (_, _) => UpdateSettingsFromInputs();
        panel.Controls.Add(_preserveLuminosityCheckBox, 1, row);
        panel.SetColumnSpan(_preserveLuminosityCheckBox, 2);

        return panel;
    }

    private void AddColorRow(TableLayoutPanel panel)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text = "滤镜颜色", AutoSize = true, Padding = new Padding(0, 7, 0, 0) }, 0, row);
        _photoColorButton.AutoSize = false;
        _photoColorButton.Height = 28;
        _photoColorButton.Dock = DockStyle.Fill;
        _photoColorButton.Click += (_, _) =>
        {
            using var dialog = new ColorDialog
            {
                Color = Color.FromArgb(FloatToByte(Settings.PhotoR), FloatToByte(Settings.PhotoG), FloatToByte(Settings.PhotoB)),
                FullOpen = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            Settings.PhotoR = dialog.Color.R / 255f;
            Settings.PhotoG = dialog.Color.G / 255f;
            Settings.PhotoB = dialog.Color.B / 255f;
            UpdatePhotoColorButton();
            QueuePreview();
        };
        panel.Controls.Add(_photoColorButton, 1, row);
        panel.SetColumnSpan(_photoColorButton, 2);
    }

    private void AddNumericRow(TableLayoutPanel panel, string key, string label, int min, int max, int increment, float value, string suffix)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 7, 0, 0) }, 0, row);

        var input = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Increment = increment,
            DecimalPlaces = 0,
            Value = Math.Clamp((decimal)Math.Round(value), min, max),
            Dock = DockStyle.Fill,
            Width = 92
        };
        input.ValueChanged += (_, _) => UpdateSettingsFromInputs();
        _inputs[key] = input;
        panel.Controls.Add(input, 1, row);
        panel.Controls.Add(new Label { Text = suffix, AutoSize = true, Padding = new Padding(4, 7, 0, 0) }, 2, row);
    }

    private Control BuildPreviewPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(12, 0, 0, 0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _previewLabel.Text = "缩略预览";
        _previewLabel.AutoSize = true;
        _previewLabel.Padding = new Padding(0, 0, 0, 6);
        _previewBox.Dock = DockStyle.Fill;
        _previewBox.SizeMode = PictureBoxSizeMode.Zoom;
        _previewBox.BorderStyle = BorderStyle.FixedSingle;
        _previewBox.BackColor = Color.FromArgb(32, 32, 32);
        panel.Controls.Add(_previewLabel, 0, 0);
        panel.Controls.Add(_previewBox, 0, 1);
        return panel;
    }

    private Control BuildButtonPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0)
        };

        var applyButton = new Button { Text = "应用", AutoSize = true, DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        _resetButton.Text = "重置";
        _resetButton.AutoSize = true;
        _resetButton.Click += (_, _) => ApplySettingsToInputs(BeautifyCustomFilterSettings.CreateDefault());
        _loadGlobalButton.Text = "载入全局默认";
        _loadGlobalButton.AutoSize = true;
        _loadGlobalButton.Enabled = GlobalDefault != null;
        _loadGlobalButton.Click += (_, _) =>
        {
            if (GlobalDefault != null) ApplySettingsToInputs(GlobalDefault);
        };
        _saveGlobalButton.Text = "保存为全局默认";
        _saveGlobalButton.AutoSize = true;
        _saveGlobalButton.Click += (_, _) =>
        {
            UpdateSettingsFromInputs();
            SaveAsGlobalDefault = true;
            _saveGlobalButton.Enabled = false;
        };

        applyButton.Click += (_, _) => UpdateSettingsFromInputs();
        panel.Controls.AddRange(new Control[]
        {
            applyButton,
            cancelButton,
            _saveGlobalButton,
            _loadGlobalButton,
            _resetButton
        });
        return panel;
    }

    private void ApplySettingsToInputs(BeautifyCustomFilterSettings settings)
    {
        _updating = true;
        try
        {
            Settings = (MapDraftService.NormalizeCustomBeautifyFilter(settings.Clone()) ?? BeautifyCustomFilterSettings.CreateDefault()).Clone();
            SetInput("PhotoDensity", Settings.PhotoDensity * 100f);
            SetInput("BalanceR", Settings.BalanceR * 100f);
            SetInput("BalanceG", Settings.BalanceG * 100f);
            SetInput("BalanceB", Settings.BalanceB * 100f);
            SetInput("Saturation", Settings.Saturation * 100f);
            SetInput("Brightness", Settings.Brightness * 100f);
            SetInput("Contrast", Settings.Contrast * 100f);
            SetInput("HighlightCompression", Settings.HighlightCompression * 100f);
            SetInput("ShadowLift", Settings.ShadowLift * 100f);
            SetInput("MidtoneGamma", Settings.MidtoneGamma * 100f);
            _preserveLuminosityCheckBox.Checked = Settings.PreserveLuminosity;
            UpdatePhotoColorButton();
        }
        finally
        {
            _updating = false;
        }

        QueuePreview();
    }

    private void SetInput(string key, float value)
    {
        if (!_inputs.TryGetValue(key, out var input)) return;
        input.Value = Math.Clamp((decimal)Math.Round(value), input.Minimum, input.Maximum);
    }

    private void UpdateSettingsFromInputs()
    {
        if (_updating) return;
        Settings.PhotoDensity = ReadPercent("PhotoDensity");
        Settings.BalanceR = ReadPercent("BalanceR");
        Settings.BalanceG = ReadPercent("BalanceG");
        Settings.BalanceB = ReadPercent("BalanceB");
        Settings.Saturation = ReadPercent("Saturation");
        Settings.Brightness = ReadPercent("Brightness");
        Settings.Contrast = ReadPercent("Contrast");
        Settings.HighlightCompression = ReadPercent("HighlightCompression");
        Settings.ShadowLift = ReadPercent("ShadowLift");
        Settings.MidtoneGamma = ReadPercent("MidtoneGamma");
        Settings.PreserveLuminosity = _preserveLuminosityCheckBox.Checked;
        Settings = MapDraftService.NormalizeCustomBeautifyFilter(Settings) ?? BeautifyCustomFilterSettings.CreateDefault();
        QueuePreview();
    }

    private float ReadPercent(string key)
        => _inputs.TryGetValue(key, out var input) ? (float)input.Value / 100f : 0f;

    private void UpdatePhotoColorButton()
    {
        var color = Color.FromArgb(FloatToByte(Settings.PhotoR), FloatToByte(Settings.PhotoG), FloatToByte(Settings.PhotoB));
        _photoColorButton.BackColor = color;
        _photoColorButton.Text = $"RGB {color.R}, {color.G}, {color.B}";
        _photoColorButton.ForeColor = color.GetBrightness() < 0.5f ? Color.White : Color.Black;
    }

    private void QueuePreview()
    {
        if (!IsHandleCreated) return;
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void RebuildPreview()
    {
        try
        {
            using var filtered = new TerrainMapBeautifyService().ApplyCustomFilterPreview(_previewSource, Settings, Strength);
            var next = BuildPreviewBitmap(filtered, Math.Max(1, _previewBox.ClientSize.Width), Math.Max(1, _previewBox.ClientSize.Height));
            var old = _previewBitmap;
            _previewBitmap = next;
            _previewBox.Image = _previewBitmap;
            old?.Dispose();
        }
        catch (Exception ex)
        {
            _previewLabel.Text = "预览失败：" + ex.Message;
        }
    }

    private static Bitmap BuildPreviewBitmap(Image image, int width, int height)
    {
        var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.FromArgb(36, 36, 36));
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        var scale = Math.Min(width / (double)Math.Max(1, image.Width), height / (double)Math.Max(1, image.Height));
        var targetWidth = Math.Max(1, (int)Math.Round(image.Width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(image.Height * scale));
        var target = new Rectangle((width - targetWidth) / 2, (height - targetHeight) / 2, targetWidth, targetHeight);
        g.DrawImage(image, target, new Rectangle(0, 0, image.Width, image.Height), GraphicsUnit.Pixel);
        return bitmap;
    }

    private static int FloatToByte(float value)
        => Math.Clamp((int)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f), 0, 255);
}
