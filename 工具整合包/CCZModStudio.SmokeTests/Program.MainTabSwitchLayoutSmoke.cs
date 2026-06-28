using CCZModStudio;
using System.Reflection;
using System.Windows.Forms;

internal partial class Program
{
    static void RunMainTabSwitchLayoutSmoke()
    {
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
                DrainMainTabSwitchLayoutEvents();

                var tabs = GetPrivateFieldForMainTabSwitchLayoutSmoke<TabControl>(form, "_mainTabs");
                if (tabs.TabPages.Count < 6)
                {
                    throw new InvalidOperationException("Main tab switch smoke needs the primary feature tabs.");
                }

                var initialRequests = GetPrivateStaticIntForMainTabSwitchLayoutSmoke("CompactToolbarLayoutRequestCount");
                var initialFlushes = GetPrivateStaticIntForMainTabSwitchLayoutSmoke("CompactToolbarLayoutFlushCount");

                var round1 = SwitchMainTabsForLayoutSmoke(form, tabs);
                var round2 = SwitchMainTabsForLayoutSmoke(form, tabs);
                var finalFlushes = GetPrivateStaticIntForMainTabSwitchLayoutSmoke("CompactToolbarLayoutFlushCount");
                var lastProcessed = GetPrivateStaticIntForMainTabSwitchLayoutSmoke("CompactToolbarLayoutLastFlushProcessedCount");
                var lastSkipped = GetPrivateStaticIntForMainTabSwitchLayoutSmoke("CompactToolbarLayoutLastFlushSkippedCount");

                var allowedRound2 = Math.Max(round1 + 40, round1 * 2 + 20);
                if (round2 > allowedRound2)
                {
                    throw new InvalidOperationException($"Compact toolbar layout requests grew across tab switch rounds. round1={round1} round2={round2} allowed={allowedRound2}");
                }

                if (finalFlushes <= initialFlushes)
                {
                    throw new InvalidOperationException($"Compact toolbar layout queue did not flush. initial={initialFlushes} final={finalFlushes}");
                }

                var afterIdle = GetPrivateStaticIntForMainTabSwitchLayoutSmoke("CompactToolbarLayoutFlushCount");
                DrainMainTabSwitchLayoutEvents();
                var afterSecondIdle = GetPrivateStaticIntForMainTabSwitchLayoutSmoke("CompactToolbarLayoutFlushCount");
                if (afterSecondIdle > afterIdle + 2)
                {
                    throw new InvalidOperationException($"Compact toolbar layout queue kept appending after idle. before={afterIdle} after={afterSecondIdle}");
                }

                form.Close();
                Console.WriteLine($"MAIN_TAB_SWITCH_LAYOUT_SMOKE_OK tabs={tabs.TabPages.Count} initialRequests={initialRequests} round1={round1} round2={round2} flushes={finalFlushes - initialFlushes} last={lastProcessed}/{lastSkipped}");
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
            throw new InvalidOperationException("Main tab switch layout smoke failed.", failure);
        }
    }

    private static int SwitchMainTabsForLayoutSmoke(MainForm form, TabControl tabs)
    {
        var before = GetPrivateStaticIntForMainTabSwitchLayoutSmoke("CompactToolbarLayoutRequestCount");
        foreach (TabPage page in tabs.TabPages)
        {
            tabs.SelectedTab = page;
            Application.DoEvents();
            form.PerformLayout();
            DrainMainTabSwitchLayoutEvents();
        }

        return GetPrivateStaticIntForMainTabSwitchLayoutSmoke("CompactToolbarLayoutRequestCount") - before;
    }

    private static void DrainMainTabSwitchLayoutEvents()
    {
        for (var i = 0; i < 4; i++)
        {
            Application.DoEvents();
            Thread.Sleep(1);
        }
    }

    private static T GetPrivateFieldForMainTabSwitchLayoutSmoke<T>(object instance, string name)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing field {name}.");
        return (T)field.GetValue(instance)!;
    }

    private static int GetPrivateStaticIntForMainTabSwitchLayoutSmoke(string name)
    {
        var property = typeof(MainForm).GetProperty(name, BindingFlags.Static | BindingFlags.NonPublic);
        if (property != null)
        {
            return (int)property.GetValue(null)!;
        }

        var field = typeof(MainForm).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing static counter {name}.");
        return (int)field.GetValue(null)!;
    }
}
