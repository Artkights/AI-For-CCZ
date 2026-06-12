using CCZModStudio;
using System.Reflection;
using System.Windows.Forms;

internal partial class Program
{
    static void RunUiLayoutSettingsSmoke()
    {
        var smokePath = Path.Combine(Path.GetTempPath(), "CCZModStudio_UiLayoutSmoke_" + Guid.NewGuid().ToString("N"), "ui-layout.json");
        Environment.SetEnvironmentVariable("CCZMODSTUDIO_UI_LAYOUT_PATH", smokePath);

        var mainSettings = new UiLayoutSettings
        {
            WindowLeft = 11,
            WindowTop = 22,
            WindowWidth = 1234,
            WindowHeight = 777,
            WindowMaximized = false
        };
        mainSettings.SplitRatios["Smoke.MainSplit"] = 0.25;
        mainSettings.SplitRatios["BuildLayout.mapSplit"] = 0.42;
        UiLayoutSettingsStore.Save(mainSettings);

        UiLayoutSettingsStore.SaveSplitRatio("Smoke.DialogSplit", 0.73);

        var loaded = UiLayoutSettingsStore.Load();
        var mainRatio = UiLayoutSettingsStore.GetSplitRatio(loaded, "Smoke.MainSplit");
        var dialogRatio = UiLayoutSettingsStore.GetSplitRatio(loaded, "Smoke.DialogSplit");
        var migratedRatio = UiLayoutSettingsStore.GetSplitRatio(loaded, "BuildMapEditorPage.MapListEditor");
        if (mainRatio is null || dialogRatio is null ||
            Math.Abs(mainRatio.Value - 0.25) > 0.0001 ||
            Math.Abs(dialogRatio.Value - 0.73) > 0.0001 ||
            migratedRatio is null ||
            Math.Abs(migratedRatio.Value - 0.42) > 0.0001 ||
            loaded.WindowLeft != 11 ||
            loaded.WindowTop != 22 ||
            loaded.WindowWidth != 1234 ||
            loaded.WindowHeight != 777)
        {
            throw new InvalidOperationException(
                $"UI layout settings smoke failed: main={mainRatio?.ToString() ?? "<null>"} dialog={dialogRatio?.ToString() ?? "<null>"} migrated={migratedRatio?.ToString() ?? "<null>"} window={loaded.WindowLeft},{loaded.WindowTop},{loaded.WindowWidth},{loaded.WindowHeight}");
        }

        Console.WriteLine($"UI_LAYOUT_SETTINGS_SMOKE_OK path={UiLayoutSettingsStore.GetPath()}");
    }

    static void RunUiLayoutApplySmoke()
    {
        var smokePath = Path.Combine(Path.GetTempPath(), "CCZModStudio_UiLayoutApplySmoke_" + Guid.NewGuid().ToString("N"), "ui-layout.json");
        Environment.SetEnvironmentVariable("CCZMODSTUDIO_UI_LAYOUT_PATH", smokePath);

        var settings = new UiLayoutSettings
        {
            WindowLeft = 100,
            WindowTop = 100,
            WindowWidth = 1400,
            WindowHeight = 900
        };
        settings.SplitRatios["BuildMapEditorPage.MapListEditor"] = 0.80;
        UiLayoutSettingsStore.Save(settings);

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                using var form = new MainForm();
                form.Show();
                Application.DoEvents();
                form.PerformLayout();
                Application.DoEvents();

                var splits = GetUiLayoutSplits(form);
                if (!splits.TryGetValue("BuildMapEditorPage.MapListEditor", out var split))
                {
                    throw new InvalidOperationException("找不到 BuildMapEditorPage.MapListEditor 页框。");
                }

                split.Width = 1000;
                split.PerformLayout();
                Application.DoEvents();

                var usable = split.Width - split.SplitterWidth;
                var expected = (int)Math.Round(usable * 0.80);
                if (Math.Abs(split.SplitterDistance - expected) > 3)
                {
                    var loadedRatio = UiLayoutSettingsStore.GetSplitRatio(UiLayoutSettingsStore.Load(), "BuildMapEditorPage.MapListEditor");
                    var formRatio = UiLayoutSettingsStore.GetSplitRatio(GetUiLayoutSettings(form), "BuildMapEditorPage.MapListEditor");
                    throw new InvalidOperationException(
                        $"页框未按本地配置恢复：actual={split.SplitterDistance}, expected={expected}, width={split.Width}, splitter={split.SplitterWidth}, storePath={UiLayoutSettingsStore.GetPath()}, loadedRatio={loadedRatio?.ToString() ?? "<null>"}, formRatio={formRatio?.ToString() ?? "<null>"}");
                }

                form.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
        {
            throw new InvalidOperationException("UI layout apply smoke failed.", failure);
        }

        Console.WriteLine($"UI_LAYOUT_APPLY_SMOKE_OK path={UiLayoutSettingsStore.GetPath()}");
    }

    private static Dictionary<string, SplitContainer> GetUiLayoutSplits(MainForm form)
    {
        var field = typeof(MainForm).GetField("_uiLayoutSplits", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 MainForm._uiLayoutSplits 字段。");
        return (Dictionary<string, SplitContainer>)field.GetValue(form)!;
    }

    private static UiLayoutSettings GetUiLayoutSettings(MainForm form)
    {
        var field = typeof(MainForm).GetField("_uiLayoutSettings", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 MainForm._uiLayoutSettings 字段。");
        return (UiLayoutSettings)field.GetValue(form)!;
    }
}
