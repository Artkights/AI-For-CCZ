using System.Diagnostics;
using System.Text.Json;
using CCZModStudio.Core;
using CCZModStudio;

internal partial class Program
{
    static void RunApplicationPerformanceSmoke(string? jsonPath)
    {
        PerformanceMetrics.Reset();
        var process = Process.GetCurrentProcess();
        var started = Stopwatch.StartNew();
        long constructorMs = 0;
        long handleMs = 0;
        long tabFirstMs = 0;
        long tabSecondMs = 0;
        int tabCount = 0;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var sw = Stopwatch.StartNew();
                using var form = new MainForm();
                constructorMs = sw.ElapsedMilliseconds;
                form.CreateControl();
                handleMs = sw.ElapsedMilliseconds;
                var tabs = EnumerateControls(form).OfType<TabControl>()
                    .OrderByDescending(tab => tab.TabPages.Count).First();
                tabCount = tabs.TabPages.Count;
                sw.Restart();
                foreach (TabPage page in tabs.TabPages) tabs.SelectedTab = page;
                tabFirstMs = sw.ElapsedMilliseconds;
                sw.Restart();
                foreach (TabPage page in tabs.TabPages) tabs.SelectedTab = page;
                tabSecondMs = sw.ElapsedMilliseconds;
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new InvalidOperationException("Application performance smoke failed.", failure);

        var report = new
        {
            CapturedAtUtc = DateTime.UtcNow,
            ConstructorMs = constructorMs,
            HandleCreationMs = handleMs,
            FirstTabRoundMs = tabFirstMs,
            SecondTabRoundMs = tabSecondMs,
            TabCount = tabCount,
            TotalMs = started.ElapsedMilliseconds,
            WorkingSetBytes = process.WorkingSet64,
            ManagedHeapBytes = GC.GetTotalMemory(false),
            Metrics = PerformanceMetrics.GetSnapshot()
        };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        if (!string.IsNullOrWhiteSpace(jsonPath))
        {
            var fullPath = Path.GetFullPath(jsonPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, json);
        }
        Console.WriteLine($"APPLICATION_PERFORMANCE_SMOKE_OK tabs={tabCount} constructorMs={constructorMs} handleMs={handleMs} firstTabsMs={tabFirstMs} secondTabsMs={tabSecondMs}");
    }

    private static IEnumerable<Control> EnumerateControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in EnumerateControls(child)) yield return nested;
        }
    }
}
