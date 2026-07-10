using CCZModStudio;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

internal partial class Program
{
    private static void RunSimplifiedUiSmoke()
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
                DrainSimplifiedUiEvents();

                AssertDisplayedButton(form, "_openProjectButton", "打开项目目录");
                AssertDisplayedButton(form, "_reloadButton", "重新读取");
                AssertDisplayedButton(form, "_usageGuideButton", "使用指南");

                foreach (var fieldName in new[]
                         {
                             "_testCopyButton",
                             "_saveTableButton",
                             "_exportCsvButton",
                             "_importCsvButton",
                             "_copyTableSelectionButton",
                             "_pasteTableSelectionButton",
                             "_batchFillTableColumnButton",
                             "_batchModifyTableButton",
                             "_undoTableEditButton",
                             "_redoTableEditButton",
                             "_openPlanButton"
                         })
                {
                    AssertHiddenOrDetachedButton(form, fieldName);
                }

                var projectLabel = GetSimplifiedUiPrivateField<Label>(form, "_projectLabel");
                AssertSimplifiedUiTrue(projectLabel.Parent != null, "project label is in the top bar");
                AssertSimplifiedUiContains(projectLabel.Text, "项目：", "project label contains project");
                AssertSimplifiedUiContains(projectLabel.Text, "模式：", "project label contains mode");
                AssertSimplifiedUiContains(projectLabel.Text, "版本：", "project label contains version");

                var roleEffectButton = GetSimplifiedUiPrivateField<Button>(form, "_openRoleEffectButton");
                AssertSimplifiedUiEqual("个人特效页", roleEffectButton.Text, "role effect button text");
                foreach (var fieldName in new[] { "_importRoleFaceButton", "_batchImportRoleFaceButton", "_exportRoleFaceBmpButton" })
                {
                    AssertHiddenOrDetachedButton(form, fieldName);
                }

                foreach (var fieldName in new[]
                         {
                             "_openJobRestraintTableButton",
                             "_openJobMatrixRestraintTableButton",
                             "_openJobMatrixAttributeTableButton",
                             "_normalizeRoleRawImagesButton",
                             "_restoreImageResourceButton",
                             "_exportMissingImageResourcesButton",
                             "_mapMakerExportPreviewButton",
                             "_mapMakerExportJpgButton"
                         })
                {
                    AssertHiddenOrDetachedButton(form, fieldName);
                }

                var sourceRoot = ResolveSimplifiedUiSourceRoot();
                var eventsText = File.ReadAllText(Path.Combine(sourceRoot, "CCZModStudio", "MainForm.Events.cs"), Encoding.UTF8);
                AssertSimplifiedUiContains(eventsText, "_openRoleEffectButton.Click += (_, _) => OpenJobEffectEditor();", "role effect button routes to job effect editor");
                AssertSimplifiedUiTrue(!eventsText.Contains("_openRoleEffectButton.Click += (_, _) => OpenRolePersonalEffectTableEditor();", StringComparison.Ordinal), "role effect button no longer routes to old EXE table");

                var guideText = File.ReadAllText(ResolveUsageGuideSourcePath(sourceRoot), Encoding.UTF8);
                var sections = UsageGuideDialog.ParseSections(guideText);
                AssertSimplifiedUiTrue(sections.Count >= 8, "usage guide has multiple pages");
                using var guideDialog = new UsageGuideDialog(guideText);
                var guideTabs = FindSimplifiedUiControl<TabControl>(guideDialog)
                    ?? throw new InvalidOperationException("UsageGuideDialog is missing TabControl.");
                AssertSimplifiedUiTrue(guideTabs.TabPages.Count >= 8, "usage guide dialog has multiple tabs");
                foreach (var box in EnumerateSimplifiedUiControls<TextBox>(guideDialog))
                {
                    AssertSimplifiedUiTrue(box.ReadOnly, "usage guide text boxes are read-only");
                }

                form.Close();
                Console.WriteLine($"SIMPLIFIED_UI_SMOKE_OK guideTabs={guideTabs.TabPages.Count}");
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
            throw new InvalidOperationException("Simplified UI smoke failed.", failure);
        }
    }

    private static void RunUsageGuideDocumentSmoke()
    {
        var sourceRoot = ResolveSimplifiedUiSourceRoot();
        var path = ResolveUsageGuideSourcePath(sourceRoot);
        var markdown = File.ReadAllText(path, Encoding.UTF8);
        var sections = UsageGuideDialog.ParseSections(markdown);
        AssertSimplifiedUiTrue(sections.Count >= 12, "usage guide markdown has release page sections");

        foreach (var heading in new[]
                 {
                     "## 通用操作",
                     "## 角色设定",
                     "## 兵种设定",
                     "## 宝物设定",
                     "## 特效注入",
                     "## 全局设定",
                     "## 图片设定",
                     "## 地图编辑",
                     "## 剧本编辑",
                     "## 场景编辑",
                     "## 战场编辑",
                     "## 商店编辑",
                     "## 写回与备份边界"
                 })
        {
            AssertSimplifiedUiContains(markdown, heading, "usage guide contains " + heading);
        }

        foreach (var forbidden in new[]
                 {
                     "创建测试副本",
                     "打开 plan.md",
                     "个人特效(EXE表)",
                     "导出PNG",
                     "导出美化JPG",
                     "角色RAW统一",
                     "还原E5条目",
                     "导出缺失报告",
                     "一键导入头像",
                     "批量导入头像",
                     "导出头像BMP"
                 })
        {
            AssertSimplifiedUiTrue(!markdown.Contains(forbidden, StringComparison.Ordinal), "usage guide omits hidden entry " + forbidden);
        }

        Console.WriteLine("USAGE_GUIDE_DOC_SMOKE_OK sections=" + sections.Count);
    }

    private static void AssertDisplayedButton(MainForm form, string fieldName, string expectedText)
    {
        var button = GetSimplifiedUiPrivateField<Button>(form, fieldName);
        AssertSimplifiedUiEqual(expectedText, button.Text, fieldName + " text");
        AssertSimplifiedUiTrue(button.Visible && button.Parent != null, fieldName + " is visible in layout");
    }

    private static void AssertHiddenOrDetachedButton(MainForm form, string fieldName)
    {
        var button = GetSimplifiedUiPrivateField<Button>(form, fieldName);
        AssertSimplifiedUiTrue(!button.Visible || button.Parent == null, fieldName + " is hidden or detached");
    }

    private static T GetSimplifiedUiPrivateField<T>(object instance, string name)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing field " + name);
        return (T)field.GetValue(instance)!;
    }

    private static T? FindSimplifiedUiControl<T>(Control root) where T : Control
        => EnumerateSimplifiedUiControls<T>(root).FirstOrDefault();

    private static IEnumerable<T> EnumerateSimplifiedUiControls<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T typed) yield return typed;
            foreach (var nested in EnumerateSimplifiedUiControls<T>(child))
            {
                yield return nested;
            }
        }
    }

    private static void DrainSimplifiedUiEvents()
    {
        for (var i = 0; i < 4; i++)
        {
            Application.DoEvents();
            Thread.Sleep(1);
        }
    }

    private static string ResolveSimplifiedUiSourceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; current != null && i < 8; i++, current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "CCZModStudio.sln")))
            {
                return current.FullName;
            }
        }

        current = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (var i = 0; current != null && i < 8; i++, current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "CCZModStudio.sln")))
            {
                return current.FullName;
            }
        }

        throw new InvalidOperationException("Unable to locate CCZModStudio.sln.");
    }

    private static string ResolveUsageGuideSourcePath(string sourceRoot)
        => Path.Combine(sourceRoot, "CCZModStudio", "Assets", "Package", "RootDocs", "普罗工具整合包使用说明.md");

    private static void AssertSimplifiedUiTrue(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Assertion failed: " + description);
        }
    }

    private static void AssertSimplifiedUiEqual<T>(T expected, T actual, string description)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{description}: expected={expected}, actual={actual}");
        }
    }

    private static void AssertSimplifiedUiContains(string text, string expected, string description)
    {
        if (!text.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{description}: missing {expected}");
        }
    }
}
