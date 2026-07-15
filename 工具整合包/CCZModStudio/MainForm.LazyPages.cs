using CCZModStudio.Core;

namespace CCZModStudio;

public sealed partial class MainForm
{
    private readonly Dictionary<TabPage, LazyPageState> _lazyMainPages = new();
    private bool LazyUiDisabled => string.Equals(
        Environment.GetEnvironmentVariable("CCZMODSTUDIO_DISABLE_LAZY_UI"), "1", StringComparison.Ordinal);

    private TabPage CreateLazyMainPage(string title, Func<TabPage> factory)
    {
        if (LazyUiDisabled) return factory();
        var shell = new TabPage(title) { Tag = "LazyMainPage:" + title };
        shell.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "首次打开时初始化页面……",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.DimGray
        });
        _lazyMainPages[shell] = new LazyPageState(title, factory);
        return shell;
    }

    private void EnsureSelectedMainPageBuilt()
    {
        var shell = _mainTabs.SelectedTab;
        if (shell == null || !_lazyMainPages.TryGetValue(shell, out var state) || state.Built || state.Building) return;
        state.Building = true;
        using var operation = PerformanceMetrics.Begin("MainForm.LazyPageBuild", new Dictionary<string, string> { ["Page"] = state.Title });
        shell.SuspendLayout();
        try
        {
            var built = state.Factory();
            var controls = built.Controls.Cast<Control>().ToArray();
            built.Controls.Clear();
            shell.Controls.Clear();
            foreach (var control in controls) shell.Controls.Add(control);
            built.Dispose();
            state.Built = true;
        }
        catch (Exception ex)
        {
            shell.Controls.Clear();
            var retry = new Button { Text = "重试初始化", AutoSize = true };
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(24) };
            panel.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(900, 0), Text = $"{state.Title} 初始化失败：{ex.Message}" });
            panel.Controls.Add(retry);
            retry.Click += (_, _) => { state.Building = false; EnsureSelectedMainPageBuilt(); };
            shell.Controls.Add(panel);
        }
        finally
        {
            state.Building = false;
            shell.ResumeLayout(performLayout: true);
        }
    }

    private sealed class LazyPageState(string title, Func<TabPage> factory)
    {
        public string Title { get; } = title;
        public Func<TabPage> Factory { get; } = factory;
        public bool Building { get; set; }
        public bool Built { get; set; }
    }
}
