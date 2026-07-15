using System.Diagnostics;
using System.Reflection;
using CCZModStudio;
using CCZModStudio.Core;
using CCZModStudio.Models;

internal partial class Program
{
    private static void RunEffectWorkbenchUiSmoke(CczProject project)
    {
        var source = ResolveEffectInjectionDiscoverySmokeSourceProject(project);
        AssertActiveReleaseResolution(source);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                using var form = new MainForm();
                typeof(MainForm).GetField("_project", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(form, source);

                var mainTabs = (TabControl)(typeof(MainForm).GetField("_mainTabs", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(form) ?? throw new InvalidOperationException("找不到主页面容器。"));
                var effectPage = mainTabs.TabPages.Cast<TabPage>().Single(page => page.Text == "特效注入");
                mainTabs.TabPages.Remove(effectPage);
                using var host = new Form { Text = "特效工作台烟测", Size = new Size(1280, 720) };
                effectPage.Dock = DockStyle.Fill;
                var hostTabs = new TabControl { Dock = DockStyle.Fill };
                hostTabs.TabPages.Add(effectPage);
                host.Controls.Add(hostTabs);
                host.Show();
                Application.DoEvents();
                var workbenchTabs = effectPage.Controls.OfType<TabControl>().Single();
                var names = workbenchTabs.TabPages.Cast<TabPage>().Select(page => page.Text).ToArray();
                if (!names.SequenceEqual(["特效清单", "复合制作"]))
                    throw new InvalidOperationException("特效注入没有收敛为特效清单和复合制作两个页面：" + string.Join(",", names));
                if (FindControls(effectPage).Any(control => control.Text is "扫描并开放" or "原生配置"))
                    throw new InvalidOperationException("特效工作台仍暴露旧权限条或独立原生配置入口。");

                foreach (var size in new[] { new Size(1280, 720), new Size(1920, 1080), new Size(2048, 1014) })
                {
                    host.Size = size;
                    host.PerformLayout();
                    workbenchTabs.PerformLayout();
                    Application.DoEvents();
                    if (workbenchTabs.ClientSize.Width <= 0 || workbenchTabs.ClientSize.Height <= 0)
                        throw new InvalidOperationException($"特效工作台在 {size.Width}x{size.Height} 下没有有效布局。 ");
                }

                var listPage = workbenchTabs.TabPages.Cast<TabPage>().Single(page => page.Text == "特效清单");
                var filter = FindControls(listPage).OfType<ComboBox>().FirstOrDefault(box => box.Items.Cast<object>().Any(item => Equals(item, "研究中")));
                if (filter == null || filter.SelectedIndex != 0)
                    throw new InvalidOperationException("特效清单没有默认显示全部的来源筛选器。");

                var compositePage = workbenchTabs.TabPages.Cast<TabPage>().Single(page => page.Text == "复合制作");
                workbenchTabs.SelectedTab = compositePage;
                Application.DoEvents();
                var scan = FindControls(compositePage).OfType<Button>().Single(button => button.Text == "刷新可用特效");
                var heartbeat = Stopwatch.StartNew();
                var previousTick = heartbeat.ElapsedMilliseconds;
                long maximumGap = 0;
                var ticks = 0;
                using var timer = new System.Windows.Forms.Timer { Interval = 25 };
                timer.Tick += (_, _) =>
                {
                    var now = heartbeat.ElapsedMilliseconds;
                    maximumGap = Math.Max(maximumGap, now - previousTick);
                    previousTick = now;
                    ticks++;
                };
                timer.Start();
                scan.PerformClick();
                Application.DoEvents();
                if (scan.Enabled)
                    throw new InvalidOperationException("复合扫描按钮没有进入后台加载状态。");
                var timeout = DateTime.UtcNow.AddSeconds(90);
                while (!scan.Enabled && DateTime.UtcNow < timeout)
                {
                    Application.DoEvents();
                    Thread.Sleep(5);
                }
                timer.Stop();
                Application.DoEvents();
                if (!scan.Enabled) throw new TimeoutException("复合特效后台扫描在 90 秒内没有完成。");
                var timings = PerformanceMetrics.GetSnapshot().Timings;
                var timingSummary = string.Join(", ", new[]
                {
                    "EffectWorkbench.CompositeUiCommit", "EffectWorkbench.CompositeModuleBind",
                    "EffectWorkbench.CompositeRowBuild", "EffectWorkbench.CompositeGridBind",
                    "EffectWorkbench.CompositeFreeIdBind", "EffectWorkbench.CompositeRecipeBind",
                    "EffectWorkbench.CompositeTriggerBind", "EffectWorkbench.CompositeSubjectBind",
                    "EffectWorkbench.CompositeTargetBind", "EffectWorkbench.CompositeActionBind",
                    "EffectWorkbench.CompositeValueBind", "EffectWorkbench.CompositeIdText",
                    "EffectWorkbench.CompositeStatusText"
                }.Select(name => $"{name}={timings.GetValueOrDefault(name)?.MaximumMilliseconds ?? 0:F1}ms"));
                if (ticks < 5 || maximumGap > 250)
                    throw new InvalidOperationException($"复合扫描阻塞 UI：ticks={ticks}, maxGap={maximumGap}ms；{timingSummary}。");
                var memberGrid = FindControls(compositePage).OfType<DataGridView>()
                    .First(grid => grid.Columns.Contains("兼容性"));
                if (memberGrid.Rows.Count == 0)
                    throw new InvalidOperationException("复合扫描完成后成员列表仍为空。");
                host.Hide();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromMinutes(2)))
            throw new TimeoutException("特效工作台 UI 烟测超时。");
        if (failure != null) throw new InvalidOperationException("特效工作台 UI 烟测失败。", failure);
        Console.WriteLine("EFFECT_WORKBENCH_UI_SMOKE_OK");
    }

    private static void AssertActiveReleaseResolution(CczProject source)
    {
        var toolRoot = Path.Combine(source.WorkspaceRoot, "工具整合包");
        var active = Path.Combine(toolRoot, "CCZModStudio_Releases", "Active", "普罗工具整合包.exe");
        var direct = Path.Combine(toolRoot, "CCZModStudio", "bin", "Release", "net8.0-windows", "win-x64", "普罗工具整合包.exe");
        var resolved = ActiveReleaseStartupService.FindActiveLauncher(direct, Path.GetDirectoryName(direct)!);
        if (!File.Exists(active) || !Path.GetFullPath(active).Equals(Path.GetFullPath(resolved ?? string.Empty), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("源码 Release 启动路径没有解析到 Active 发布。 ");
        if (!ActiveReleaseStartupService.IsRunningFromActive(active) ||
            !ActiveReleaseStartupService.IsDeveloperBuildRequested(["-DeveloperBuild"]))
            throw new InvalidOperationException("Active/开发构建启动边界判断不正确。");
    }

    private static IEnumerable<Control> FindControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in FindControls(child)) yield return descendant;
        }
    }
}
