using CCZModStudio;
using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

internal partial class Program
{
    static void RunBattlefieldPreviewSmoke(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                using var form = new MainForm();
                var scenarios = new ScenarioFileReader().ReadAllIndex(project);
                var scenario = scenarios.FirstOrDefault(x => ScenarioFileReader.IsBattlefieldScriptFile(x.FileName))
                    ?? throw new InvalidOperationException("No battlefield S_XX.eex scenario was found.");
                var mapResources = new MapResourceIndexer().Index(project);
                var hexzmap = new HexzmapProbeReader().Read(project);
                var document = new BattlefieldEditorService().Load(project, scenario, dictionary: null, tables);

                SetPrivateField(form, "_project", project);
                SetPrivateField(form, "_currentMapResources", mapResources);
                SetPrivateField(form, "_currentHexzmapProbe", hexzmap);

                InvokePrivate(form, "RenderBattlefieldMapPreview", document, null);

                var previewBox = GetPrivateField<PictureBox>(form, "_battlefieldMapPreviewBox");
                var hintLabel = GetPrivateField<Label>(form, "_battlefieldMapHintLabel");
                if (previewBox.Image == null || previewBox.Image.Width <= 0 || previewBox.Image.Height <= 0)
                {
                    throw new InvalidOperationException("Battlefield map preview did not render an image. Hint=" + hintLabel.Text);
                }

                var colorPixels = CountColorPixels(previewBox.Image);
                if (colorPixels <= 0)
                {
                    throw new InvalidOperationException("Battlefield map preview rendered a blank image.");
                }

                Console.WriteLine($"BATTLEFIELD_PREVIEW_SMOKE_OK scenario={scenario.FileName} image={previewBox.Image.Width}x{previewBox.Image.Height} colorPixels={colorPixels} hint={hintLabel.Text}");
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
            throw new InvalidOperationException("Battlefield preview smoke failed.", failure);
        }
    }

    private static int CountColorPixels(Image image)
    {
        using var bitmap = new Bitmap(image);
        var count = 0;
        var stepX = Math.Max(1, bitmap.Width / 80);
        var stepY = Math.Max(1, bitmap.Height / 80);
        for (var y = 0; y < bitmap.Height; y += stepY)
        {
            for (var x = 0; x < bitmap.Width; x += stepX)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.A > 0 && (color.R > 8 || color.G > 8 || color.B > 8))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static void SetPrivateField<T>(MainForm form, string fieldName, T value)
    {
        var field = typeof(MainForm).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Field not found: " + fieldName);
        field.SetValue(form, value);
    }

    private static T GetPrivateField<T>(MainForm form, string fieldName)
    {
        var field = typeof(MainForm).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Field not found: " + fieldName);
        return (T)field.GetValue(form)!;
    }

    private static void InvokePrivate(MainForm form, string methodName, params object?[] args)
    {
        var method = typeof(MainForm).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Method not found: " + methodName);
        method.Invoke(form, args);
    }
}
