using CCZModStudio;
using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Collections;
using System.Data;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

internal partial class Program
{
    private static void RunRsSingleFrameSmoke()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        AssertLayout("Pmapobj.e5", RsFrameResourceKind.R, 48, 64, 20);
        AssertLayout("Unit_mov.e5", RsFrameResourceKind.S, 48, 48, 11);
        AssertLayout("Unit_atk.e5", RsFrameResourceKind.S, 64, 64, 12);
        AssertLayout("Unit_spc.e5", RsFrameResourceKind.S, 48, 48, 5);
        AssertPreviewCanvasLayouts();
        AssertPreviewCanvasRendering();
        AssertStrictStripProbeAndCrop();
        AssertCardMetricsAcrossDpi();
        AssertAllShiftDirections();
        AssertPlaceholderCatalogs();
        AssertFreeCandidateEditCallbackWired();
        AssertSplitterLayoutCalculations();
        AssertViewerWindowsAndCatalogOwnership();
        RunPixelEditorShortcutSmoke();
        Console.WriteLine("RS_SINGLE_FRAME_SMOKE_OK");
    }

    private static void AssertLayout(string fileName, RsFrameResourceKind kind, int width, int height, int count)
    {
        var layout = new RsFrameLayoutResolver().Resolve(fileName);
        if (layout.ResourceKind != kind || layout.FrameWidth != width || layout.FrameHeight != height || layout.ExpectedFrameCount != count)
            throw new InvalidOperationException($"Unexpected R/S layout for {fileName}: {layout}");
    }

    private static void AssertPreviewCanvasLayouts()
    {
        var fiveTimesBounds = new Rectangle(10, 20, 320, 320);
        var attack = RsFramePreviewControl.CalculatePreviewLayout(
            fiveTimesBounds, RsFrameResourceKind.S, new Size(64, 64));
        var move = RsFramePreviewControl.CalculatePreviewLayout(
            fiveTimesBounds, RsFrameResourceKind.S, new Size(48, 48));
        var special = RsFramePreviewControl.CalculatePreviewLayout(
            fiveTimesBounds, RsFrameResourceKind.S, new Size(48, 48));

        if (attack.LogicalCanvasSize != new Size(64, 64) ||
            attack.CanvasDestination != fiveTimesBounds ||
            attack.ContentDestination != fiveTimesBounds ||
            attack.PixelScale != 5d)
            throw new InvalidOperationException($"The 64x64 S attack preview layout is incorrect: {attack}.");
        if (move.CanvasDestination != attack.CanvasDestination ||
            move.ContentDestination != new Rectangle(50, 60, 240, 240) ||
            move.PixelScale != attack.PixelScale ||
            special != move)
            throw new InvalidOperationException($"The 48x48 S preview was not centered on the shared 64x64 canvas: {move}.");

        var nonSquareBounds = new Rectangle(11, 17, 500, 340);
        var nonSquareAttack = RsFramePreviewControl.CalculatePreviewLayout(
            nonSquareBounds, RsFrameResourceKind.S, new Size(64, 64));
        var nonSquareMove = RsFramePreviewControl.CalculatePreviewLayout(
            nonSquareBounds, RsFrameResourceKind.S, new Size(48, 48));
        if (nonSquareAttack.CanvasDestination != new Rectangle(101, 27, 320, 320) ||
            nonSquareMove.CanvasDestination != nonSquareAttack.CanvasDestination ||
            nonSquareMove.ContentDestination != new Rectangle(141, 67, 240, 240))
            throw new InvalidOperationException($"The S preview is not centered in non-square bounds: {nonSquareMove}.");

        var compactBounds = new Rectangle(3, 5, 32, 40);
        var compactAttack = RsFramePreviewControl.CalculatePreviewLayout(
            compactBounds, RsFrameResourceKind.S, new Size(64, 64));
        var compactMove = RsFramePreviewControl.CalculatePreviewLayout(
            compactBounds, RsFrameResourceKind.S, new Size(48, 48));
        if (compactAttack.CanvasDestination != new Rectangle(3, 9, 32, 32) ||
            compactMove.CanvasDestination != compactAttack.CanvasDestination ||
            compactMove.ContentDestination != new Rectangle(7, 13, 24, 24) ||
            compactAttack.PixelScale != 0.5d ||
            compactMove.PixelScale != compactAttack.PixelScale)
            throw new InvalidOperationException($"Compact S previews did not retain the shared 64x64 scale: {compactMove}.");

        var rSource = new Size(48, 64);
        var rLayout = RsFramePreviewControl.CalculatePreviewLayout(
            fiveTimesBounds, RsFrameResourceKind.R, rSource);
        var legacyRDestination = RsFramePreviewControl.CalculateIntegerScaleDestination(fiveTimesBounds, rSource);
        if (rLayout.LogicalCanvasSize != rSource ||
            rLayout.CanvasDestination != legacyRDestination ||
            rLayout.ContentDestination != legacyRDestination)
            throw new InvalidOperationException($"The R preview layout changed unexpectedly: {rLayout}.");
    }

    private static void AssertPreviewCanvasRendering()
    {
        using var source = new Bitmap(48, 48, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var sourceGraphics = Graphics.FromImage(source)) sourceGraphics.Clear(Color.Magenta);
        using var rendered = new Bitmap(320, 320, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(rendered))
        {
            graphics.Clear(Color.Black);
            RsFramePreviewControl.DrawFrame(
                graphics,
                new Rectangle(Point.Empty, rendered.Size),
                RsFrameResourceKind.S,
                source);
        }

        if (rendered.GetPixel(39, 39).ToArgb() == Color.Magenta.ToArgb() ||
            rendered.GetPixel(40, 40).ToArgb() != Color.Magenta.ToArgb() ||
            rendered.GetPixel(279, 279).ToArgb() != Color.Magenta.ToArgb() ||
            rendered.GetPixel(280, 280).ToArgb() == Color.Magenta.ToArgb())
            throw new InvalidOperationException("The rendered 48x48 S frame does not occupy only the centered 240x240 content area.");

        var checkerColors = new HashSet<int>();
        for (var y = 0; y < 40; y += 10)
        for (var x = 0; x < 40; x += 10)
            checkerColors.Add(rendered.GetPixel(x, y).ToArgb());
        if (checkerColors.Count != 2 || checkerColors.Contains(Color.Black.ToArgb()) || checkerColors.Contains(Color.Magenta.ToArgb()))
            throw new InvalidOperationException("The padded S preview area was not rendered as a two-color transparency checkerboard.");
    }

    private static void AssertStrictStripProbeAndCrop()
    {
        var expectedLength = 48 * 48 * 11;
        var exact = RsStripLayoutService.Probe("Unit_mov.e5", "RAW", new byte[expectedLength]);
        if (!exact.IsSupportedLayout || exact.AvailableFrameCount != 11 || exact.TrailingByteCount != 0)
            throw new InvalidOperationException("Exact Unit_mov RAW strip should be accepted as 11 frames.");

        var withTrailer = RsStripLayoutService.Probe("Unit_mov.e5", "RAW", new byte[expectedLength + 2]);
        if (!withTrailer.IsSupportedLayout || withTrailer.AvailableFrameCount != 11 || withTrailer.TrailingByteCount != 2)
            throw new InvalidOperationException("Unit_mov RAW strip with the verified two-byte trailer should be accepted without adding pixels.");

        var partial = RsStripLayoutService.Probe("Unit_mov.e5", "RAW", new byte[expectedLength + 1]);
        if (partial.IsSupportedLayout)
            throw new InvalidOperationException("An unverified one-byte RAW suffix must be rejected.");

        using var canonicalBitmap = new Bitmap(48, 48 * 11, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var canonicalBytes = new MemoryStream();
        canonicalBitmap.Save(canonicalBytes, System.Drawing.Imaging.ImageFormat.Bmp);
        var canonicalProbe = RsStripLayoutService.Probe("Unit_mov.e5", "BMP", canonicalBytes.ToArray());
        if (!canonicalProbe.IsSupportedLayout)
            throw new InvalidOperationException("Canonical 48x528 BMP strip should be accepted: " + canonicalProbe.Detail);

        using var malformedBitmap = new Bitmap(64, 959, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using var malformedBytes = new MemoryStream();
        malformedBitmap.Save(malformedBytes, System.Drawing.Imaging.ImageFormat.Bmp);
        var malformedProbe = RsStripLayoutService.Probe("Unit_atk.e5", "BMP", malformedBytes.ToArray());
        if (malformedProbe.IsSupportedLayout || !malformedProbe.Detail.Contains("64x959", StringComparison.Ordinal))
            throw new InvalidOperationException("Malformed Unit_atk 64x959 strip must be rejected with its actual dimensions.");

        using var doubleWidthBitmap = new Bitmap(96, 48 * 11, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using var doubleWidthBytes = new MemoryStream();
        doubleWidthBitmap.Save(doubleWidthBytes, System.Drawing.Imaging.ImageFormat.Png);
        if (RsStripLayoutService.Probe("Unit_mov.e5", "PNG", doubleWidthBytes.ToArray()).IsSupportedLayout)
            throw new InvalidOperationException("Double-width Unit_mov contact sheet must not be treated as a physical frame strip.");

        using var strip = new Bitmap(48, 48 * 11, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        strip.SetPixel(47, 48 + 47, Color.Red);
        using var frame = RsStripLayoutService.CropFrame(strip, new RsFrameLayoutResolver().Resolve("Unit_mov.e5"), 1);
        if (frame.Size != new Size(48, 48) || frame.GetPixel(47, 47).ToArgb() != Color.Red.ToArgb())
            throw new InvalidOperationException("Two-dimensional physical crop did not preserve the expected right/bottom pixel.");
        try
        {
            using var _ = RsStripLayoutService.CropFrame(strip, new RsFrameLayoutResolver().Resolve("Unit_mov.e5"), 11);
            throw new InvalidOperationException("Out-of-range physical frame index should fail instead of clamping.");
        }
        catch (ArgumentOutOfRangeException)
        {
        }
    }

    private static void AssertCardMetricsAcrossDpi()
    {
        foreach (var fontName in new[] { "Microsoft YaHei UI", "SimSun" })
        using (var font = new Font(fontName, 9F, FontStyle.Regular, GraphicsUnit.Point))
        {
            foreach (var dpi in new[] { 96, 120, 144, 192 })
            {
                var metrics = FreeImageAssignmentDialog.BuildCardMetrics(
                    dpi,
                    font,
                    ImageAssignmentResourceKind.S,
                    maximumIdText: "525");
                var minimumWidth = (int)Math.Round(96 * dpi / 96d);
                var measuredId = TextRenderer.MeasureText(
                    "525",
                    font,
                    Size.Empty,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                if (metrics.CardSize.Width < minimumWidth ||
                    metrics.CardSize.Height < metrics.TitleHeight + metrics.PictureHeight + metrics.ActionsHeight ||
                    metrics.Margin.All < (int)Math.Round(6 * dpi / 96d) ||
                    measuredId.Width > metrics.CardSize.Width - 2 ||
                    measuredId.Height > metrics.TitleHeight)
                {
                    throw new InvalidOperationException($"Invalid S candidate card metrics at {dpi} DPI with {font.Name}: {metrics}.");
                }
            }
        }
    }

    private static void AssertAllShiftDirections()
    {
        using var original = BuildUnique3By3();
        using var left = RsFrameShiftService.Shift(original, RsFrameShiftDirection.Left);
        AssertArgb(original, 1, 1, left, 0, 1, "left content");
        AssertTransparentColumn(left, 2, "left transparent edge");
        using var right = RsFrameShiftService.Shift(original, RsFrameShiftDirection.Right);
        AssertArgb(original, 1, 1, right, 2, 1, "right content");
        AssertTransparentColumn(right, 0, "right transparent edge");
        using var up = RsFrameShiftService.Shift(original, RsFrameShiftDirection.Up);
        AssertArgb(original, 1, 1, up, 1, 0, "up content");
        AssertTransparentRow(up, 2, "up transparent edge");
        using var down = RsFrameShiftService.Shift(original, RsFrameShiftDirection.Down);
        AssertArgb(original, 1, 1, down, 1, 2, "down content");
        AssertTransparentRow(down, 0, "down transparent edge");

        using var exhausted = new Bitmap(original);
        for (var i = 0; i < exhausted.Width; i++)
        {
            using var shifted = RsFrameShiftService.Shift(exhausted, RsFrameShiftDirection.Left);
            using var graphics = Graphics.FromImage(exhausted);
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            graphics.DrawImageUnscaled(shifted, 0, 0);
        }
        if (Enumerable.Range(0, exhausted.Width).Any(x => Enumerable.Range(0, exhausted.Height).Any(y => exhausted.GetPixel(x, y).A != 0)))
            throw new InvalidOperationException("Shifting more than the frame width should leave a fully transparent frame.");
    }

    private static void AssertPlaceholderCatalogs()
    {
        var root = Path.Combine(Path.GetTempPath(), "CCZModStudio_RsCatalogSmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = new CCZModStudio.Models.CczProject
            {
                WorkspaceRoot = root,
                GameRoot = root,
                HexTableXmlPath = string.Empty
            };
            var service = new RsSingleFrameCatalogService();
            using var r = service.BuildR(project, 12, "catalog smoke");
            if (r.Frames.Count != 40 ||
                r.Frames.Count(frame => frame.Group == "正图") != 20 ||
                r.Frames.Count(frame => frame.Group == "反图") != 20 ||
                r.Frames[0].ImageNumber != 25 ||
                r.Frames[20].ImageNumber != 26 ||
                r.Frames.Any(frame => frame.SourceRectangle.Size != new Size(48, 64)) ||
                !r.Frames[0].DisplayLabel.Contains("普通", StringComparison.Ordinal) ||
                !r.Frames[1].DisplayLabel.Contains("移动1", StringComparison.Ordinal) ||
                !r.Frames[2].DisplayLabel.Contains("移动2", StringComparison.Ordinal) ||
                r.Frames.Any(frame => frame.IsReadable))
                throw new InvalidOperationException("Missing-resource R catalog should retain all 40 physical placeholders and verified labels.");

            using var s = service.BuildS(project, 1, jobId: null, factionSlot: 1, stageSlot: 1, "catalog smoke");
            if (s.Frames.Count != 28 ||
                s.Frames.Count(frame => frame.Group == "移动") != 11 ||
                s.Frames.Count(frame => frame.Group == "攻击") != 12 ||
                s.Frames.Count(frame => frame.Group == "特技") != 5 ||
                s.Frames.Where(frame => frame.Group == "移动").Any(frame => frame.SourceRectangle.Size != new Size(48, 48)) ||
                s.Frames.Where(frame => frame.Group == "攻击").Any(frame => frame.SourceRectangle.Size != new Size(64, 64)) ||
                s.Frames.Where(frame => frame.Group == "特技").Any(frame => frame.SourceRectangle.Size != new Size(48, 48)))
                throw new InvalidOperationException("Missing-resource S catalog should retain all 28 physical placeholders and group dimensions.");

            const int jobId = 1;
            for (var faction = 1; faction <= 3; faction++)
            {
                using var jobS = service.BuildS(project, 0, jobId, faction, stageSlot: 1, "job S catalog smoke");
                var expectedImageNumber = jobId * 3 + faction;
                if (!jobS.AvailableStages.SequenceEqual([1]) ||
                    !jobS.AvailableFactions.SequenceEqual([1, 2, 3]) ||
                    jobS.StageSlot != 1 ||
                    jobS.FactionSlot != faction ||
                    jobS.Frames.Count != 28 ||
                    jobS.Frames.Any(frame => frame.ImageNumber != expectedImageNumber))
                {
                    throw new InvalidOperationException(
                        $"Job S catalog mapping is wrong for Faction{faction}: expected Unit image #{expectedImageNumber}.");
                }
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertFreeCandidateEditCallbackWired()
    {
        var root = Path.Combine(Path.GetTempPath(), "CCZModStudio_FreeCandidateEditSmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = new CczProject { WorkspaceRoot = root, GameRoot = root, HexTableXmlPath = string.Empty };
            var preview = new ImageAssignmentPreviewService();
            var service = new ImageAssignmentFreeIdService(preview);
            var result = new ImageAssignmentFreeIdResult(
                ImageAssignmentResourceKind.R, true, 1, 0,
                new[] { new FreeImageAssignmentCandidate(1, "free R smoke") },
                Array.Empty<string>());
            Func<RsFrameDescriptor, bool> callback = _ => true;
            using var dialog = new FreeImageAssignmentDialog(project, new DataTable(), service, result, 1, callback);
            var field = typeof(FreeImageAssignmentDialog).GetField("_editFrame", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new InvalidOperationException("Free candidate dialog edit callback field is missing.");
            if (!ReferenceEquals(field.GetValue(dialog), callback))
                throw new InvalidOperationException("Free R/S candidate dialog did not retain the single-frame edit callback.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertSplitterLayoutCalculations()
    {
        var scaled = RsSingleFrameViewerDialog.CalculateSplitterLayout(
            totalLength: 1680,
            splitterWidth: 9,
            desiredDistance: 720,
            desiredPanel1Min: 450,
            desiredPanel2Min: 450,
            currentDistance: 500,
            useDesiredDistance: true);
        if (!scaled.HasUsableSpace || scaled.SplitterDistance != 720 ||
            scaled.Panel1MinSize != 450 || scaled.Panel2MinSize != 450)
            throw new InvalidOperationException("The 150%-DPI single-frame splitter calculation is incorrect.");

        var compact = RsSingleFrameViewerDialog.CalculateSplitterLayout(
            totalLength: 800,
            splitterWidth: 9,
            desiredDistance: 720,
            desiredPanel1Min: 450,
            desiredPanel2Min: 450,
            currentDistance: 700,
            useDesiredDistance: true);
        if (!compact.HasUsableSpace || compact.SplitterDistance != 720 ||
            compact.Panel1MinSize != 0 || compact.Panel2MinSize != 0)
            throw new InvalidOperationException("A compact single-frame viewer should temporarily relax both panel minimum widths.");

        var preserveUserDistance = RsSingleFrameViewerDialog.CalculateSplitterLayout(
            totalLength: 1680,
            splitterWidth: 9,
            desiredDistance: 720,
            desiredPanel1Min: 450,
            desiredPanel2Min: 450,
            currentDistance: 830,
            useDesiredDistance: false);
        if (preserveUserDistance.SplitterDistance != 830)
            throw new InvalidOperationException("A legal user-selected splitter distance should be preserved while resizing.");

        var clampUserDistance = RsSingleFrameViewerDialog.CalculateSplitterLayout(
            totalLength: 800,
            splitterWidth: 9,
            desiredDistance: 720,
            desiredPanel1Min: 450,
            desiredPanel2Min: 450,
            currentDistance: 900,
            useDesiredDistance: false);
        if (clampUserDistance.SplitterDistance != 791 ||
            clampUserDistance.Panel1MinSize != 0 || clampUserDistance.Panel2MinSize != 0)
            throw new InvalidOperationException("An out-of-range splitter distance should be clamped after a compact resize.");
    }

    private static void AssertViewerWindowsAndCatalogOwnership()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                AssertViewerWindowsOnStaThread();
                AssertFreeCandidateCardLayoutOnStaThread();
                AssertSAnimationSelectorsOnStaThread();
                AssertCatalogDisposedAfterViewerClose();
                AssertCatalogDisposedAfterConstructorFailure();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            IsBackground = true,
            Name = "R/S single-frame viewer smoke"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(120)))
            throw new TimeoutException("R/S single-frame viewer smoke did not finish within 120 seconds.");
        if (failure != null)
            throw new InvalidOperationException("R/S single-frame viewer window smoke failed.", failure);
    }

    private static void AssertViewerWindowsOnStaThread()
    {
        var root = Path.Combine(Path.GetTempPath(), "CCZModStudio_RsViewerSmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = new CczProject
            {
                WorkspaceRoot = root,
                GameRoot = root,
                HexTableXmlPath = string.Empty
            };
            var service = new RsSingleFrameCatalogService();
            AssertViewerWindow(
                () => service.BuildR(project, 12, "viewer smoke"),
                (stage, faction) => service.BuildR(project, 12, "viewer smoke"),
                expectedFrameCount: 40,
                expectedGroupCount: 2);
            AssertViewerWindow(
                () => service.BuildS(project, 1, jobId: null, factionSlot: 1, stageSlot: 1, "viewer smoke"),
                (stage, faction) => service.BuildS(project, 1, jobId: null, faction, stage, "viewer smoke"),
                expectedFrameCount: 28,
                expectedGroupCount: 3);
            AssertJobSViewerOptions(service, project);
            AssertSpecialSViewerStageOptions(service, project);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertViewerWindow(
        Func<RsFrameCatalog> initialFactory,
        Func<int, int, RsFrameCatalog> catalogFactory,
        int expectedFrameCount,
        int expectedGroupCount)
    {
        var catalog = initialFactory();
        if (catalog.Frames.Count != expectedFrameCount)
        {
            catalog.Dispose();
            throw new InvalidOperationException($"Unexpected viewer catalog frame count: {catalog.Frames.Count}/{expectedFrameCount}.");
        }

        using var dialog = new RsSingleFrameViewerDialog(catalog, catalogFactory, editFrame: null)
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-2400, -1800)
        };
        dialog.CreateControl();
        dialog.Show();
        DrainSingleFrameViewerEvents();
        dialog.PerformLayout();
        DrainSingleFrameViewerEvents();

        var split = FindControl<SplitContainer>(dialog, "RsSingleFrameMainSplit");
        var groups = FindControl<FlowLayoutPanel>(dialog, "RsSingleFrameGroupsPanel");
        AssertViewerLayout(split, groups, expectedGroupCount, "default");
        AssertViewerCommandsVisible(dialog);

        dialog.Size = dialog.MinimumSize;
        dialog.PerformLayout();
        DrainSingleFrameViewerEvents();
        AssertViewerLayout(split, groups, expectedGroupCount, "minimum");
        AssertResponsiveOrientation(dialog, split, "minimum");

        dialog.Size = new Size(Math.Max(dialog.MinimumSize.Width, 1120), Math.Max(dialog.MinimumSize.Height, 760));
        dialog.PerformLayout();
        DrainSingleFrameViewerEvents();
        AssertViewerLayout(split, groups, expectedGroupCount, "restored");
        AssertResponsiveOrientation(dialog, split, "restored");

        var maximum = split.ClientSize.Width - split.SplitterWidth - split.Panel2MinSize;
        if (maximum > split.Panel1MinSize)
        {
            split.SplitterDistance = Math.Clamp(split.SplitterDistance + 24, split.Panel1MinSize, maximum);
            var userDistance = split.SplitterDistance;
            var preferredDistanceField = typeof(RsSingleFrameViewerDialog).GetField(
                "_preferredMainSplitterDistance",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("The R/S viewer preferred splitter field was not found.");
            preferredDistanceField.SetValue(dialog, (int?)userDistance);
            dialog.Width += 80;
            dialog.PerformLayout();
            DrainSingleFrameViewerEvents();
            AssertViewerLayout(split, groups, expectedGroupCount, "user-resized");
            if (split.SplitterDistance != userDistance)
                throw new InvalidOperationException(
                    $"A legal user-selected R/S viewer splitter distance was reset during resize: {userDistance} -> {split.SplitterDistance}.");
        }

        dialog.Close();
        DrainSingleFrameViewerEvents();
    }

    private static void AssertJobSViewerOptions(RsSingleFrameCatalogService service, CczProject project)
    {
        const int jobId = 1;
        var loadedFactions = new List<int>();
        RsFrameCatalog Factory(int _, int faction)
        {
            loadedFactions.Add(faction);
            return service.BuildS(project, 0, jobId, faction, stageSlot: 1, "job S viewer smoke");
        }

        using var dialog = new RsSingleFrameViewerDialog(
            Factory(1, CharacterImageResourceService.DefaultSPreviewFactionSlot),
            Factory,
            editFrame: null)
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-2400, -1800)
        };
        dialog.CreateControl();
        dialog.Show();
        DrainSingleFrameViewerEvents();

        var stageLabel = FindControl<Label>(dialog, "RsSingleFrameStageLabel");
        var stageCombo = FindControl<ComboBox>(dialog, "RsSingleFrameStageCombo");
        var factionLabel = FindControl<Label>(dialog, "RsSingleFrameFactionLabel");
        var factionCombo = FindControl<ComboBox>(dialog, "RsSingleFrameFactionCombo");
        var metadata = FindControl<Label>(dialog, "RsSingleFrameMetadataLabel");
        if (stageLabel.Visible || stageCombo.Visible || stageCombo.Items.Count != 1)
            throw new InvalidOperationException("Job S viewer must not expose a stage selector.");
        if (!factionLabel.Visible || !factionCombo.Visible || factionCombo.Items.Count != 3 || factionCombo.SelectedIndex != 0)
            throw new InvalidOperationException("Job S viewer must expose three factions and default to Faction1.");
        AssertJobSViewerMetadata(metadata.Text, faction: 1, expectedImageNumber: 4);

        factionCombo.SelectedIndex = 1;
        DrainSingleFrameViewerEvents();
        AssertJobSViewerMetadata(metadata.Text, faction: 2, expectedImageNumber: 5);

        factionCombo.SelectedIndex = 2;
        DrainSingleFrameViewerEvents();
        AssertJobSViewerMetadata(metadata.Text, faction: 3, expectedImageNumber: 6);

        if (!loadedFactions.SequenceEqual([1, 2, 3]))
            throw new InvalidOperationException($"Job S viewer loaded unexpected factions: {string.Join(",", loadedFactions)}.");

        dialog.Close();
        DrainSingleFrameViewerEvents();
    }

    private static void AssertJobSViewerMetadata(string text, int faction, int expectedImageNumber)
    {
        var factionText = CharacterImageResourceService.BuildSPreviewFactionText(faction);
        if (text.Contains("转级", StringComparison.Ordinal) ||
            text.Contains("第一转", StringComparison.Ordinal) ||
            !text.Contains($"阵营：{factionText}", StringComparison.Ordinal) ||
            !text.Contains($"图号 #{expectedImageNumber}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Job S viewer metadata is wrong for Faction{faction}/#{expectedImageNumber}: {text}");
        }
    }

    private static void AssertSpecialSViewerStageOptions(RsSingleFrameCatalogService service, CczProject project)
    {
        RsFrameCatalog Factory(int stage, int faction)
            => service.BuildS(project, 1, jobId: null, faction, stage, "special S viewer smoke");

        using var dialog = new RsSingleFrameViewerDialog(Factory(1, 1), Factory, editFrame: null)
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-2400, -1800)
        };
        dialog.CreateControl();
        dialog.Show();
        DrainSingleFrameViewerEvents();

        var stageLabel = FindControl<Label>(dialog, "RsSingleFrameStageLabel");
        var stageCombo = FindControl<ComboBox>(dialog, "RsSingleFrameStageCombo");
        var metadata = FindControl<Label>(dialog, "RsSingleFrameMetadataLabel");
        if (!stageLabel.Visible || !stageCombo.Visible || stageCombo.Items.Count != 3 || stageCombo.SelectedIndex != 0)
            throw new InvalidOperationException("Three-stage special S viewer must retain its stage selector.");
        if (!metadata.Text.Contains("转级：第一转", StringComparison.Ordinal))
            throw new InvalidOperationException("Three-stage special S viewer must retain its stage metadata.");

        stageCombo.SelectedIndex = 2;
        DrainSingleFrameViewerEvents();
        if (!metadata.Text.Contains("转级：第三转", StringComparison.Ordinal))
            throw new InvalidOperationException("Three-stage special S viewer did not reload the selected stage.");

        dialog.Close();
        DrainSingleFrameViewerEvents();
    }

    private static void AssertViewerLayout(
        SplitContainer split,
        FlowLayoutPanel groups,
        int expectedGroupCount,
        string phase)
    {
        var totalLength = split.Orientation == Orientation.Vertical ? split.ClientSize.Width : split.ClientSize.Height;
        var maximum = totalLength - split.SplitterWidth - split.Panel2MinSize;
        if (split.SplitterDistance < split.Panel1MinSize || split.SplitterDistance > maximum)
            throw new InvalidOperationException(
                $"Illegal R/S viewer splitter during {phase}: min1={split.Panel1MinSize}, distance={split.SplitterDistance}, max={maximum}.");
        var panel1Length = split.Orientation == Orientation.Vertical ? split.Panel1.ClientSize.Width : split.Panel1.ClientSize.Height;
        var panel2Length = split.Orientation == Orientation.Vertical ? split.Panel2.ClientSize.Width : split.Panel2.ClientSize.Height;
        if (!split.Panel1.Visible || !split.Panel2.Visible || panel1Length <= 0 || panel2Length <= 0)
            throw new InvalidOperationException($"Both R/S viewer panels must remain visible during {phase}.");
        if (groups.Controls.OfType<GroupBox>().Count() != expectedGroupCount)
            throw new InvalidOperationException($"Unexpected R/S viewer group count during {phase}.");
        if (groups.Controls.OfType<GroupBox>().Any(group => group.Width <= 0 || group.MaximumSize.Width <= 0))
            throw new InvalidOperationException($"R/S viewer group width is invalid during {phase}.");
    }

    private static void AssertResponsiveOrientation(Form dialog, SplitContainer split, string phase)
    {
        var logicalWidth = dialog.ClientSize.Width * 96d / Math.Max(96, dialog.DeviceDpi);
        var expected = logicalWidth >= 900 ? Orientation.Vertical : Orientation.Horizontal;
        if (split.Orientation != expected)
            throw new InvalidOperationException($"R/S viewer orientation during {phase} should be {expected} at logical width {logicalWidth:N1}, actual {split.Orientation}.");
    }

    private static void AssertFreeCandidateCardLayoutOnStaThread()
    {
        var root = Path.Combine(Path.GetTempPath(), "CCZModStudio_FreeCardSmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = new CczProject { WorkspaceRoot = root, GameRoot = root, HexTableXmlPath = string.Empty };
            var table = new DataTable();
            table.Columns.Add("S形象编号", typeof(int));
            var result = new ImageAssignmentFreeIdResult(
                ImageAssignmentResourceKind.S,
                false,
                1,
                0,
                new[] { new FreeImageAssignmentCandidate(219, "layout smoke") },
                Array.Empty<string>());
            using var dialog = new FreeImageAssignmentDialog(
                project,
                table,
                new ImageAssignmentFreeIdService(new ImageAssignmentPreviewService()),
                result,
                1,
                editFrame: null)
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-2400, -1800)
            };
            dialog.CreateControl();
            var freeOnly = FindControl<CheckBox>(dialog, "FreeImageFreeOnlyCheckBox");
            if (freeOnly.Checked)
                throw new InvalidOperationException("R/S query must open in all-candidates mode with the free-only filter unchecked.");
            var stageCombo = FindControl<ComboBox>(dialog, "FreeImageStageCombo");
            if (stageCombo.Items.Count != 3)
                throw new InvalidOperationException("S query stage selector must always expose stage 1, 2, and 3.");
            _ = FindControl<Button>(dialog, "FreeImageRescanButton");
            _ = FindControl<Button>(dialog, "FreeImageCopyDiagnosticButton");

            var buildCard = typeof(FreeImageAssignmentDialog).GetMethod("BuildCard", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Free image candidate card factory was not found.");
            var metrics = FreeImageAssignmentDialog.BuildCardMetrics(
                144,
                dialog.Font,
                ImageAssignmentResourceKind.S,
                maximumIdText: "525");
            using var host = new Form
            {
                ShowInTaskbar = false,
                FormBorderStyle = FormBorderStyle.None,
                Opacity = 0,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(0, 0),
                ClientSize = new Size(960, 720)
            };
            using var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight
            };
            host.Controls.Add(flow);

            var manualIds = new[] { 1, 219, 525 };
            var containers = new List<Control>(manualIds.Length);
            for (var index = 0; index < manualIds.Length; index++)
            {
                var id = manualIds[index];
                var candidate = new FreeImageAssignmentCandidate(id, "layout smoke");
                var card = buildCard.Invoke(dialog, new object[]
                {
                    candidate, ImageAssignmentPreviewStance.Front, 1, 1, index, metrics
                }) ?? throw new InvalidOperationException("Free image candidate card factory returned null.");
                var container = (Control)(card.GetType().GetProperty("Container")?.GetValue(card)
                    ?? throw new InvalidOperationException("Free image candidate card container was not found."));
                containers.Add(container);
                flow.Controls.Add(container);
            }

            host.Show();
            Application.DoEvents();
            flow.PerformLayout();

            foreach (var container in containers)
            {
                container.PerformLayout();
                AssertDescendantsContained(container);
                if (container.Size != metrics.CardSize ||
                    container.MinimumSize != metrics.CardSize ||
                    container.MaximumSize != metrics.CardSize)
                {
                    throw new InvalidOperationException($"Compact R/S card changed size inside FlowLayoutPanel: {container.Size}/{metrics.CardSize}.");
                }

                var title = FindControl<Label>(container, "FreeImageCardTitle");
                if (!int.TryParse(title.Text, out _))
                    throw new InvalidOperationException($"Compact R/S card title must contain only the numeric ID, actual '{title.Text}'.");
                if (title.AutoEllipsis || title.AutoSize || title.Padding != Padding.Empty)
                    throw new InvalidOperationException($"Numeric title {title.Text} can still be auto-sized, padded, or ellipsized.");
                var measured = TextRenderer.MeasureText(
                    title.Text,
                    title.Font,
                    Size.Empty,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                if (measured.Width > title.ClientSize.Width || measured.Height > title.ClientSize.Height)
                    throw new InvalidOperationException($"Numeric title {title.Text} glyphs are clipped: measured={measured}, client={title.ClientSize}.");
                AssertNumericGlyphsRendered(title);

                if (FindControlByName(container, "FreeImageCardCoverage") != null)
                    throw new InvalidOperationException("Compact R/S card must not contain a visible coverage/status row.");
                var play = FindControl<Button>(container, "FreeImageCardPlayButton");
                var allFrames = FindControl<Button>(container, "FreeImageCardAllFramesButton");
                if (play.Width <= 0 || play.Height <= 0 ||
                    allFrames.Width <= 0 || allFrames.Height <= 0 ||
                    play.Bounds.IntersectsWith(allFrames.Bounds))
                {
                    throw new InvalidOperationException(
                        $"Both R/S card commands must have usable, non-overlapping bounds at 144 DPI metrics: play={play.Bounds}, all={allFrames.Bounds}.");
                }
            }

            var auditCard = containers[1];
            using var bitmap = new Bitmap(auditCard.Width, auditCard.Height);
            auditCard.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            var distinct = new HashSet<int>();
            for (var y = 0; y < bitmap.Height; y += Math.Max(1, bitmap.Height / 16))
            for (var x = 0; x < bitmap.Width; x += Math.Max(1, bitmap.Width / 16))
                distinct.Add(bitmap.GetPixel(x, y).ToArgb());
            if (distinct.Count < 3)
                throw new InvalidOperationException("R/S candidate card DrawToBitmap output appears blank or fully clipped.");
            host.Close();
            Console.WriteLine("RS_CARD_MANUAL_LAYOUT_OK=3");

            var cancelWork = typeof(FreeImageAssignmentDialog).GetMethod("CancelWork", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Free image query cancellation method was not found.");
            var refreshContent = typeof(FreeImageAssignmentDialog).GetMethod("RefreshContent", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Free image query refresh method was not found.");
            var resultField = typeof(FreeImageAssignmentDialog).GetField("_result", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Free image query result field was not found.");
            var contentField = typeof(FreeImageAssignmentDialog).GetField("_contentPanel", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Free image query content panel field was not found.");
            var fullResult = new ImageAssignmentFreeIdResult(
                ImageAssignmentResourceKind.S,
                false,
                525,
                0,
                Enumerable.Range(1, 525).Select(id => new FreeImageAssignmentCandidate(id, "async layout smoke")).ToArray(),
                Array.Empty<string>());
            cancelWork.Invoke(dialog, null);
            resultField.SetValue(dialog, fullResult);
            dialog.Show();
            Application.DoEvents();
            var asyncStartedAt = DateTime.UtcNow;
            refreshContent.Invoke(dialog, null);
            Console.WriteLine("RS_CARD_ASYNC_LAYOUT_STARTED=525");
            var actualContent = (FlowLayoutPanel)(contentField.GetValue(dialog)
                ?? throw new InvalidOperationException("Free image query content panel was null."));
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (actualContent.Controls.Count < 525 && DateTime.UtcNow < deadline)
            {
                Application.DoEvents();
                Thread.Sleep(1);
            }
            if (actualContent.Controls.Count != 525)
                throw new InvalidOperationException($"Actual asynchronous R/S card creation stopped at {actualContent.Controls.Count}/525 cards.");
            Console.WriteLine($"RS_CARD_ASYNC_CREATED=525 elapsedMs={(DateTime.UtcNow - asyncStartedAt).TotalMilliseconds:N0}");
            for (var index = 0; index < actualContent.Controls.Count; index++)
            {
                var asyncCard = actualContent.Controls[index];
                asyncCard.PerformLayout();
                AssertDescendantsContained(asyncCard);
                var asyncTitle = FindControl<Label>(asyncCard, "FreeImageCardTitle");
                if (asyncTitle.Text != (index + 1).ToString())
                    throw new InvalidOperationException($"Actual asynchronous R/S card order or numeric title is wrong at {index}: {asyncTitle.Text}.");
                var asyncMeasured = TextRenderer.MeasureText(
                    asyncTitle.Text,
                    asyncTitle.Font,
                    Size.Empty,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                if (asyncMeasured.Width > asyncTitle.ClientSize.Width || asyncMeasured.Height > asyncTitle.ClientSize.Height)
                    throw new InvalidOperationException($"Actual asynchronous numeric title {asyncTitle.Text} is clipped: {asyncMeasured}/{asyncTitle.ClientSize}.");
                if (index is 218 or 524) AssertNumericGlyphsRendered(asyncTitle);
            }
            Console.WriteLine($"RS_CARD_ASYNC_LAYOUT_OK=525 elapsedMs={(DateTime.UtcNow - asyncStartedAt).TotalMilliseconds:N0}");
            cancelWork.Invoke(dialog, null);
            dialog.Close();
            Application.DoEvents();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertNumericGlyphsRendered(Label title)
    {
        using var bitmap = new Bitmap(Math.Max(1, title.Width), Math.Max(1, title.Height));
        title.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        var background = bitmap.GetPixel(0, 0).ToArgb();
        var nonBackgroundPixels = 0;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            if (bitmap.GetPixel(x, y).ToArgb() != background) nonBackgroundPixels++;
        }

        if (nonBackgroundPixels < title.Text.Length * 3)
            throw new InvalidOperationException($"Numeric title {title.Text} did not render complete glyph pixels.");
    }

    private static void AssertSAnimationSelectorsOnStaThread()
    {
        var root = Path.Combine(Path.GetTempPath(), "CCZModStudio_SAnimationUiSmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = new CczProject { WorkspaceRoot = root, GameRoot = root, HexTableXmlPath = string.Empty };
            var service = new ImageAssignmentPreviewService();
            var preview = service.BuildSAnimationPreview(project, 219, jobId: null, factionSlot: 1, stageSlot: 3);
            AssertSPlaybackFrameContract(preview);

            using var dialog = new CharacterImageAnimationPreviewDialog(
                service,
                preview,
                CharacterImageAnimationPreviewDialog.DefaultIntervalMs,
                optionLabel: "转数：",
                selectedOption: 3,
                optionPreviewFactory: stage => service.BuildSAnimationPreview(project, 219, jobId: null, factionSlot: 1, stage),
                optionValues: [1, 2, 3])
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-2400, -1800)
            };
            dialog.CreateControl();
            dialog.Show();
            Application.DoEvents();

            InvokeAnimationPreviewMethod(dialog, "TogglePlaying");
            var facing = FindControl<ComboBox>(dialog, "AnimationFacingCombo");
            var option = FindControl<ComboBox>(dialog, "AnimationOptionCombo");
            var status = FindControl<Label>(dialog, "AnimationFrameStatus");
            var expectedFacings = new[] { "正面/下", "背面/上", "侧面/左", "侧面/右" };
            if (FindControlByName(dialog, "AnimationActionCombo") != null || ContainsControlText(dialog, "动作："))
                throw new InvalidOperationException("S animation must not expose an action parameter.");
            if (!facing.Enabled ||
                !facing.Items.Cast<object>().Select(item => item.ToString()).SequenceEqual(expectedFacings) ||
                !string.Equals(Convert.ToString(facing.SelectedItem), "正面/下", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("S animation must expose four fixed directions and default to front/down.");
            }
            AssertSAnimationStatus(status.Text, "Unit_mov.e5", "正面/下", physicalFrame: 0, position: "1/14");

            for (var index = 0; index < 13; index++) InvokeAnimationPreviewMethod(dialog, "AdvanceFrame");
            AssertSAnimationStatus(status.Text, "Unit_spc.e5", "正面/下", physicalFrame: 4, position: "14/14");
            InvokeAnimationPreviewMethod(dialog, "AdvanceFrame");
            AssertSAnimationStatus(status.Text, "Unit_mov.e5", "正面/下", physicalFrame: 0, position: "1/14");

            facing.SelectedIndex = 3;
            Application.DoEvents();
            AssertSAnimationStatus(status.Text, "Unit_mov.e5", "侧面/右", physicalFrame: 4, position: "1/14");

            option.SelectedIndex = 1;
            Application.DoEvents();
            if (!string.Equals(Convert.ToString(facing.SelectedItem), "侧面/右", StringComparison.Ordinal))
                throw new InvalidOperationException("S animation direction was not preserved across synchronous option reload.");
            AssertSAnimationStatus(status.Text, "Unit_mov.e5", "侧面/右", physicalFrame: 4, position: "1/14");

            dialog.Close();
            Application.DoEvents();

            var asyncPreview = service.BuildSAnimationPreview(project, 219, jobId: null, factionSlot: 1, stageSlot: 1);
            using var asyncDialog = new CharacterImageAnimationPreviewDialog(
                service,
                asyncPreview,
                CharacterImageAnimationPreviewDialog.DefaultIntervalMs,
                optionLabel: "转数：",
                selectedOption: 1,
                optionValues: [1, 2],
                asyncPreviewFactory: (stage, _) => Task.FromResult(
                    service.BuildSAnimationPreview(project, 219, jobId: null, factionSlot: 1, stage)))
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-2400, -1800)
            };
            asyncDialog.CreateControl();
            asyncDialog.Show();
            Application.DoEvents();
            InvokeAnimationPreviewMethod(asyncDialog, "TogglePlaying");
            var asyncFacing = FindControl<ComboBox>(asyncDialog, "AnimationFacingCombo");
            var asyncOption = FindControl<ComboBox>(asyncDialog, "AnimationOptionCombo");
            asyncFacing.SelectedIndex = 2;
            asyncOption.SelectedIndex = 1;
            Application.DoEvents();
            if (!string.Equals(Convert.ToString(asyncFacing.SelectedItem), "侧面/左", StringComparison.Ordinal))
                throw new InvalidOperationException("S animation direction was not preserved across asynchronous option reload.");
            asyncDialog.Close();
            Application.DoEvents();

            var loadingPreview = new CharacterImageAnimationPreview(
                ImageAssignmentResourceKind.S,
                219,
                1,
                "loading",
                3,
                3,
                64,
                64,
                Array.Empty<CharacterImageAnimationCell>(),
                ["loading"]);
            using var loadingDialog = new CharacterImageAnimationPreviewDialog(
                service,
                loadingPreview,
                CharacterImageAnimationPreviewDialog.DefaultIntervalMs);
            loadingDialog.CreateControl();
            var loadingFacing = FindControl<ComboBox>(loadingDialog, "AnimationFacingCombo");
            var loadingStatus = FindControl<Label>(loadingDialog, "AnimationFrameStatus");
            if (loadingFacing.Enabled || loadingFacing.Items.Count != 0 || loadingStatus.Text != "等待 S 形象资源")
                throw new InvalidOperationException("Loading S animation must disable direction selection and show the resource wait status.");

            var rPreview = service.BuildRAnimationPreview(project, 12);
            using var rDialog = new CharacterImageAnimationPreviewDialog(
                service,
                rPreview,
                CharacterImageAnimationPreviewDialog.DefaultIntervalMs);
            rDialog.CreateControl();
            if (InvokeAnimationPreviewMethod<int>(rDialog, "GetCurrentFrameCount") != 20 ||
                FindControlByName(rDialog, "AnimationFacingCombo") != null ||
                FindControlByName(rDialog, "AnimationActionCombo") != null)
            {
                throw new InvalidOperationException("R animation must retain its existing 20-frame playback without S selectors.");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertSPlaybackFrameContract(CharacterImageAnimationPreview preview)
    {
        var contracts = new[]
        {
            (Facing: RsFacing.Front, MirrorRight: false, Expected: new[]
            {
                "Unit_mov.e5:0", "Unit_mov.e5:1", "Unit_mov.e5:6", "Unit_mov.e5:9", "Unit_mov.e5:10",
                "Unit_atk.e5:0", "Unit_atk.e5:1", "Unit_atk.e5:2", "Unit_atk.e5:3",
                "Unit_spc.e5:0", "Unit_spc.e5:1", "Unit_spc.e5:2", "Unit_spc.e5:3", "Unit_spc.e5:4"
            }),
            (Facing: RsFacing.Back, MirrorRight: false, Expected: new[]
            {
                "Unit_mov.e5:2", "Unit_mov.e5:3", "Unit_mov.e5:7", "Unit_mov.e5:9", "Unit_mov.e5:10",
                "Unit_atk.e5:4", "Unit_atk.e5:5", "Unit_atk.e5:6", "Unit_atk.e5:7",
                "Unit_spc.e5:0", "Unit_spc.e5:1", "Unit_spc.e5:2", "Unit_spc.e5:3", "Unit_spc.e5:4"
            }),
            (Facing: RsFacing.Side, MirrorRight: false, Expected: new[]
            {
                "Unit_mov.e5:4", "Unit_mov.e5:5", "Unit_mov.e5:8", "Unit_mov.e5:9", "Unit_mov.e5:10",
                "Unit_atk.e5:8", "Unit_atk.e5:9", "Unit_atk.e5:10", "Unit_atk.e5:11",
                "Unit_spc.e5:0", "Unit_spc.e5:1", "Unit_spc.e5:2", "Unit_spc.e5:3", "Unit_spc.e5:4"
            }),
            (Facing: RsFacing.Side, MirrorRight: true, Expected: new[]
            {
                "Unit_mov.e5:4", "Unit_mov.e5:5", "Unit_mov.e5:8", "Unit_mov.e5:9", "Unit_mov.e5:10",
                "Unit_atk.e5:8", "Unit_atk.e5:9", "Unit_atk.e5:10", "Unit_atk.e5:11",
                "Unit_spc.e5:0", "Unit_spc.e5:1", "Unit_spc.e5:2", "Unit_spc.e5:3", "Unit_spc.e5:4"
            })
        };

        foreach (var contract in contracts)
        {
            var frames = CharacterImageAnimationPreviewDialog.BuildSPlaybackFrames(
                preview,
                contract.Facing,
                contract.MirrorRight);
            var actual = frames.Select(frame =>
            {
                var sequence = preview.Sequences[frame.SequenceIndex];
                var physical = sequence.Frames[frame.SequenceFrameIndex].PhysicalFrameIndex;
                return $"{sequence.Definition.SourceFile}:{physical}";
            }).ToArray();
            if (!actual.SequenceEqual(contract.Expected))
                throw new InvalidOperationException("S direction playback order is wrong: " + string.Join(",", actual));

            var expectedMirroredCount = contract.MirrorRight ? 7 : 0;
            if (frames.Count(frame => frame.MirrorHorizontal) != expectedMirroredCount)
                throw new InvalidOperationException($"S direction playback mirror count is wrong: {frames.Count(frame => frame.MirrorHorizontal)}/{expectedMirroredCount}.");
            if (frames.Where(frame => frame.MirrorHorizontal).Any(frame =>
                    preview.Sequences[frame.SequenceIndex].Definition.Facing != RsFacing.Side))
            {
                throw new InvalidOperationException("Directionless S frames must not be mirrored for right-facing playback.");
            }
        }
    }

    private static void AssertSAnimationStatus(
        string status,
        string sourceFile,
        string facing,
        int physicalFrame,
        string position)
    {
        var forbiddenActions = new[] { "待机/移动", "攻击", "防御/受击", "倒地/退场", "特技" };
        if (!status.Contains(sourceFile, StringComparison.OrdinalIgnoreCase) ||
            !status.Contains(facing, StringComparison.Ordinal) ||
            !status.Contains($"物理帧 {physicalFrame}", StringComparison.Ordinal) ||
            !status.Contains(position, StringComparison.Ordinal) ||
            forbiddenActions.Any(action => status.Contains(action, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("S animation status does not match the direction-only contract: " + status);
        }
    }

    private static void InvokeAnimationPreviewMethod(
        CharacterImageAnimationPreviewDialog dialog,
        string methodName)
        => InvokeAnimationPreviewMethod<object?>(dialog, methodName);

    private static T InvokeAnimationPreviewMethod<T>(
        CharacterImageAnimationPreviewDialog dialog,
        string methodName)
    {
        var method = typeof(CharacterImageAnimationPreviewDialog).GetMethod(
                         methodName,
                         BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new InvalidOperationException("Missing animation preview method: " + methodName);
        return (T)method.Invoke(dialog, null)!;
    }

    private static bool ContainsControlText(Control root, string text)
    {
        if (string.Equals(root.Text, text, StringComparison.Ordinal)) return true;
        foreach (Control child in root.Controls)
        {
            if (ContainsControlText(child, text)) return true;
        }
        return false;
    }

    private static void AssertDescendantsContained(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (!parent.ClientRectangle.Contains(child.Bounds))
                throw new InvalidOperationException($"Candidate card child {child.Name} is clipped: {child.Bounds} / {parent.ClientRectangle}.");
            AssertDescendantsContained(child);
        }
    }

    private static Control? FindControlByName(Control parent, string name)
    {
        if (parent.Name.Equals(name, StringComparison.Ordinal)) return parent;
        foreach (Control child in parent.Controls)
        {
            var found = FindControlByName(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private static void AssertViewerCommandsVisible(Control root)
    {
        foreach (var name in new[]
                 {
                     "RsSingleFrameEditButton",
                     "RsSingleFrameCloseButton",
                     "RsSingleFrameLargePreview"
                 })
        {
            var control = FindControl<Control>(root, name);
            if (!control.Visible || control.Width <= 0 || control.Height <= 0)
                throw new InvalidOperationException($"R/S viewer control is not visible: {name}.");
        }
    }

    private static void AssertCatalogDisposedAfterViewerClose()
    {
        var bitmap = new Bitmap(48, 64);
        var catalog = BuildOwnedBitmapCatalog(bitmap, Array.Empty<int>());
        using (var dialog = new RsSingleFrameViewerDialog(catalog, (_, _) => throw new InvalidOperationException(), editFrame: null))
        {
            dialog.Dispose();
        }
        AssertBitmapDisposed(bitmap, "normal viewer close");
    }

    private static void AssertCatalogDisposedAfterConstructorFailure()
    {
        var bitmap = new Bitmap(48, 64);
        var catalog = BuildOwnedBitmapCatalog(bitmap, new ThrowingIntList());
        try
        {
            _ = new RsSingleFrameViewerDialog(catalog, (_, _) => throw new InvalidOperationException(), editFrame: null);
            throw new InvalidOperationException("The injected R/S viewer constructor failure did not occur.");
        }
        catch (InjectedViewerSmokeException)
        {
            // Expected: ConfigureOptions enumerates the injected failing stage list.
        }
        AssertBitmapDisposed(bitmap, "viewer constructor failure");
    }

    private static RsFrameCatalog BuildOwnedBitmapCatalog(Bitmap bitmap, IReadOnlyList<int> availableStages)
        => new()
        {
            Title = "ownership smoke",
            Kind = RsFrameResourceKind.S,
            ImageId = 1,
            AvailableStages = availableStages,
            Frames =
            [
                new RsFrameDescriptor
                {
                    Kind = RsFrameResourceKind.S,
                    ImageId = 1,
                    TargetPath = "Unit_mov.e5",
                    ImageNumber = 1,
                    Group = "移动",
                    PhysicalFrameIndex = 0,
                    SourceRectangle = new Rectangle(0, 0, 48, 64),
                    DisplayLabel = "物理帧0",
                    IsReadable = true,
                    IsEditable = false,
                    Bitmap = bitmap
                }
            ]
        };

    private static void AssertBitmapDisposed(Bitmap bitmap, string phase)
    {
        try
        {
            _ = bitmap.GetPixel(0, 0);
        }
        catch (ArgumentException)
        {
            return;
        }
        throw new InvalidOperationException($"The R/S viewer did not dispose its catalog bitmap after {phase}.");
    }

    private static T FindControl<T>(Control root, string name) where T : Control
    {
        if (root is T match && root.Name == name) return match;
        foreach (Control child in root.Controls)
        {
            var found = TryFindControl<T>(child, name);
            if (found != null) return found;
        }
        throw new InvalidOperationException($"R/S viewer control was not found: {name}.");
    }

    private static T? TryFindControl<T>(Control root, string name) where T : Control
    {
        if (root is T match && root.Name == name) return match;
        foreach (Control child in root.Controls)
        {
            var found = TryFindControl<T>(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private static void DrainSingleFrameViewerEvents()
    {
        for (var i = 0; i < 6; i++)
        {
            Application.DoEvents();
            Thread.Sleep(1);
        }
    }

    private sealed class ThrowingIntList : IReadOnlyList<int>
    {
        public int Count => 1;
        public int this[int index] => 1;
        public IEnumerator<int> GetEnumerator() => throw new InjectedViewerSmokeException();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class InjectedViewerSmokeException : Exception
    {
    }

    private static Bitmap BuildUnique3By3()
    {
        var bitmap = new Bitmap(3, 3);
        for (var y = 0; y < 3; y++)
        for (var x = 0; x < 3; x++)
            bitmap.SetPixel(x, y, Color.FromArgb(255, x * 70 + 10, y * 70 + 20, x * 3 + y + 30));
        return bitmap;
    }

    private static void AssertArgb(Bitmap expected, int expectedX, int expectedY, Bitmap actual, int actualX, int actualY, string label)
    {
        if (expected.GetPixel(expectedX, expectedY).ToArgb() != actual.GetPixel(actualX, actualY).ToArgb())
            throw new InvalidOperationException($"Shift assertion failed: {label}.");
    }

    private static void AssertTransparentColumn(Bitmap bitmap, int x, string label)
    {
        if (Enumerable.Range(0, bitmap.Height).Any(y => bitmap.GetPixel(x, y).A != 0))
            throw new InvalidOperationException($"Expected transparent column: {label}.");
    }

    private static void AssertTransparentRow(Bitmap bitmap, int y, string label)
    {
        if (Enumerable.Range(0, bitmap.Width).Any(x => bitmap.GetPixel(x, y).A != 0))
            throw new InvalidOperationException($"Expected transparent row: {label}.");
    }
}
