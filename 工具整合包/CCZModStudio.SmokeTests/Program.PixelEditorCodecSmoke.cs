using CCZModStudio;
using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Drawing;
using System.Globalization;

internal partial class Program
{
    static void RunPixelEditorCodecSmoke(CczProject project)
    {
        var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "PixelEditorCodecSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(smokeRoot);

        foreach (var fileName in new[] { "Ekd5.exe", "Data.e5", "Imsg.e5", "Star.e5", "Hexzmap.e5", "Pmapobj.e5", "Unit_mov.e5", "Unit_atk.e5", "Unit_spc.e5", "Itemicon.dll" })
        {
            var source = fileName.EndsWith(".e5", StringComparison.OrdinalIgnoreCase)
                ? CharacterImageResourceService.ResolveGameFile(project, fileName)
                : Path.Combine(project.GameRoot, fileName);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(smokeRoot, fileName), overwrite: false);
            }
        }

        File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=pixel editor codec smoke\r\n");
        var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
        var codec = new EditableImageCodecService();
        var e5 = new E5ImageReplaceService();
        AssertDllDibDecodeRowDirection();
        RunPixelEditorShortcutSmoke();
        RunPixelEditResourceGroupSmoke(testProject);

        var rawPath = CharacterImageResourceService.ResolveGameFile(testProject, "Pmapobj.e5");
        if (!File.Exists(rawPath))
        {
            throw new FileNotFoundException("Pixel editor codec smoke requires Pmapobj.e5.", rawPath);
        }

        var rawImageNumber = e5.ReadIndex(rawPath).First(entry => entry.Kind.Equals("RAW", StringComparison.OrdinalIgnoreCase)).ImageNumber;
        var rawTarget = new EditableImageTarget
        {
            Kind = EditableImageTargetKind.E5RawStrip,
            DisplayName = "Smoke R RAW",
            TargetPath = rawPath,
            ImageNumber = rawImageNumber,
            FrameWidth = 48,
            FrameHeight = 64,
            OperationKind = "PixelEditorCodecSmoke RAW"
        };
        var rawFileLengthBefore = new FileInfo(rawPath).Length;
        var rawEntryBefore = e5.ReadIndex(rawPath)[rawTarget.ImageNumber - 1];
        var rawBytesBefore = e5.ReadStoredEntryBytes(rawPath, rawTarget.ImageNumber);
        long lengthAfterFirstWrite;
        using (var document = codec.Load(testProject, rawTarget))
        {
            if (document.Bitmap.Width != 48 || document.Bitmap.Height % 64 != 0)
            {
                throw new InvalidOperationException($"RAW load dimensions are unexpected: {document.Bitmap.Width}x{document.Bitmap.Height}");
            }

            var changedIndex = FindFirstOpaquePixel(document.Bitmap);
            document.Bitmap.SetPixel(changedIndex.X, changedIndex.Y, Color.Transparent);
            var preview = codec.PreviewWrite(testProject, document.Target, document.Bitmap);
            if (preview.E5Preview == null || preview.E5Preview.OperationCount != 1)
            {
                throw new InvalidOperationException("RAW pixel editor preview did not use E5 batch writeback.");
            }

            var result = codec.Write(testProject, document.Target, document.Bitmap);
            if (result.E5Result == null || !File.Exists(result.BackupPath) || !File.Exists(result.ReportPath))
            {
                throw new InvalidOperationException("RAW pixel editor writeback result is missing backup or report.");
            }

            lengthAfterFirstWrite = new FileInfo(rawPath).Length;
        }

        using (var secondDocument = codec.Load(testProject, rawTarget))
        {
            var secondPoint = FindFirstOpaquePixel(secondDocument.Bitmap);
            secondDocument.Bitmap.SetPixel(secondPoint.X, secondPoint.Y, Color.Transparent);
            codec.Write(testProject, secondDocument.Target, secondDocument.Bitmap);
        }
        if (new FileInfo(rawPath).Length != lengthAfterFirstWrite)
            throw new InvalidOperationException("Repeated RAW pixel editing must not grow the E5 archive.");

        var rawBytes = e5.ReadStoredEntryBytes(rawPath, rawTarget.ImageNumber);
        var rawEntry = e5.ReadIndex(rawPath)[rawTarget.ImageNumber - 1];
        if (!rawEntry.Kind.Equals("RAW", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"RAW pixel editor writeback must preserve RAW, got {rawEntry.Kind}.");
        }
        if (rawEntry.DataOffset != rawEntryBefore.DataOffset || rawEntry.StoredLength != rawEntryBefore.StoredLength ||
            rawBytes.Length != rawBytesBefore.Length || new FileInfo(rawPath).Length != rawFileLengthBefore)
        {
            throw new InvalidOperationException("RAW pixel editor writeback changed offset, entry length, or archive length.");
        }
        var changedRawBytes = rawBytesBefore.Zip(rawBytes).Count(pair => pair.First != pair.Second);
        if (changedRawBytes is < 1 or > 2)
            throw new InvalidOperationException($"Two one-pixel RAW edits changed {changedRawBytes} stored bytes instead of at most two.");

        RunBmpRsRoundTripIfAvailable(testProject, e5);
        RunNativePngStableRoundTripIfAvailable(testProject, codec, e5);
        RunArchiveRepairRoundTrip(testProject, codec, e5, rawTarget);
        RunPixelEditorStorageSafetySmoke(testProject);

        var iconPath = Path.Combine(testProject.GameRoot, "Itemicon.dll");
        if (File.Exists(iconPath))
        {
            var iconTarget = new EditableImageTarget
            {
                Kind = EditableImageTargetKind.DllBitmapIcon,
                DisplayName = "Smoke Item Icon",
                TargetPath = iconPath,
                IconIndex = 0,
                OperationKind = "PixelEditorCodecSmoke DLL"
            };
            using var iconDocument = codec.Load(testProject, iconTarget);
            iconDocument.Bitmap.SetPixel(0, 0, Color.Transparent);
            iconDocument.Bitmap.SetPixel(10, 10, Color.FromArgb(255, 12, 34, 56));
            var iconPreview = codec.PreviewWrite(testProject, iconDocument.Target, iconDocument.Bitmap);
            if (iconPreview.DllPreview == null || iconPreview.DllPreview.Items.Count != 1)
            {
                throw new InvalidOperationException("DLL icon pixel editor preview did not use DLL batch writeback.");
            }

            var iconResult = codec.Write(testProject, iconDocument.Target, iconDocument.Bitmap);
            if (iconResult.DllResult == null || !File.Exists(iconResult.BackupPath) || !File.Exists(iconResult.ReportPath))
            {
                throw new InvalidOperationException("DLL icon pixel editor writeback result is missing backup or report.");
            }

            AssertDllIconPixelEditorWriteback(iconPath, iconTarget.IconIndex);

            using var iconGroup = PixelEditResourceGroup.Load(
                testProject,
                codec,
                new[] { iconTarget },
                showTabs: false,
                scopeDescription: "DLL icon group fallback smoke");
            var originalIconPixel = iconGroup.Pages[0].Document.Bitmap.GetPixel(0, 0);
            iconGroup.Pages[0].Document.Bitmap.SetPixel(0, 0,
                originalIconPixel.A == 0 ? Color.White : Color.Transparent);
            var groupWriter = new PixelEditResourceGroupWriteService(codec);
            var groupPreview = groupWriter.Preview(testProject, iconGroup);
            if (groupPreview.SinglePreview?.DllPreview == null)
            {
                throw new InvalidOperationException("Pixel resource-group writer should preserve the DLL icon preview path.");
            }
            var groupResult = groupWriter.Write(testProject, iconGroup);
            if (groupResult.SingleResult?.DllResult == null || string.IsNullOrWhiteSpace(groupResult.SingleResult.BackupPath))
            {
                throw new InvalidOperationException("Pixel resource-group writer should preserve the DLL icon write path.");
            }
        }

        Console.WriteLine($"PIXEL_EDITOR_CODEC_SMOKE OK root={smokeRoot}");
    }

    private static Point FindFirstOpaquePixel(Bitmap bitmap)
    {
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
            if (bitmap.GetPixel(x, y).A != 0) return new Point(x, y);
        throw new InvalidOperationException("RAW smoke entry contains no opaque pixel to edit.");
    }

    private static void RunBmpRsRoundTripIfAvailable(CczProject project, E5ImageReplaceService e5)
    {
        var path = CharacterImageResourceService.ResolveGameFile(project, "Unit_mov.e5");
        if (!File.Exists(path)) return;
        var entries = e5.ReadIndex(path);
        if (entries.Count < 508) return;
        if (entries[507].Kind.Equals("RAW", StringComparison.OrdinalIgnoreCase))
        {
            RunRawS204RoundTrip(project, e5, path, entries[507]);
            return;
        }
        if (!entries[507].Kind.Equals("BMP", StringComparison.OrdinalIgnoreCase)) return;

        var fileLength = new FileInfo(path).Length;
        var entryBefore = entries[507];
        var bytesBefore = e5.ReadStoredEntryBytes(path, 508);
        var catalogService = new RsSingleFrameCatalogService();
        using var catalog = catalogService.BuildResourceEntry(project, path, 508, "S204 Unit_mov #508 BMP round-trip");
        var descriptor = catalog.Frames.First(frame => frame.IsEditable && frame.Bitmap != null &&
            Enumerable.Range(0, frame.Bitmap.Width).Any(x => Enumerable.Range(0, frame.Bitmap.Height).Any(y => frame.Bitmap.GetPixel(x, y).A != 0)));
        using var edited = new Bitmap(descriptor.Bitmap!);
        var point = FindFirstOpaquePixel(edited);
        edited.SetPixel(point.X, point.Y, Color.Transparent);
        var writer = new RsSingleFrameEditWriteService();
        using var preview = writer.Preview(project, descriptor, edited);
        if (preview.E5Preview.Operations.Single().NewKind != "BMP" || preview.E5Preview.FileSizeDeltaBytes != 0)
            throw new InvalidOperationException("S204 Unit_mov #508 preview must remain BMP with zero archive growth.");
        writer.Write(project, preview);

        var entryAfter = e5.ReadIndex(path)[507];
        var bytesAfter = e5.ReadStoredEntryBytes(path, 508);
        if (entryAfter.Kind != "BMP" || entryAfter.DataOffset != entryBefore.DataOffset ||
            entryAfter.StoredLength != entryBefore.StoredLength || new FileInfo(path).Length != fileLength)
            throw new InvalidOperationException("S204 Unit_mov #508 BMP round-trip changed format, offset, length, or archive size.");
        if (!bytesBefore.AsSpan(0, 54).SequenceEqual(bytesAfter.AsSpan(0, 54)))
            throw new InvalidOperationException("S204 Unit_mov #508 BMP round-trip changed the BMP header.");
        using var rereadCatalog = catalogService.BuildResourceEntry(project, path, 508, "S204 Unit_mov #508 reread");
        var rereadFrame = rereadCatalog.Frames[descriptor.PhysicalFrameIndex].Bitmap
                          ?? throw new InvalidOperationException("S204 Unit_mov #508 reread frame is missing.");
        if (rereadFrame.GetPixel(point.X, point.Y).A != 0)
            throw new InvalidOperationException("S204 Unit_mov #508 BMP magenta background key did not reread as transparency.");
        Console.WriteLine("BMP_RS_ROUNDTRIP_OK Unit_mov.e5 #508 format=BMP offset/entry/archive length unchanged");
    }

    private static void RunRawS204RoundTrip(
        CczProject project,
        E5ImageReplaceService e5,
        string path,
        E5ImageEntryInfo entryBefore)
    {
        var fileLength = new FileInfo(path).Length;
        var bytesBefore = e5.ReadStoredEntryBytes(path, 508);
        var catalogService = new RsSingleFrameCatalogService();
        using var catalog = catalogService.BuildResourceEntry(project, path, 508, "S204 Unit_mov #508 RAW round-trip");
        var descriptor = catalog.Frames.First(frame => frame.IsEditable && frame.Bitmap != null &&
            Enumerable.Range(0, frame.Bitmap.Width).Any(x =>
                Enumerable.Range(0, frame.Bitmap.Height).Any(y => frame.Bitmap.GetPixel(x, y).A != 0)));
        using var edited = new Bitmap(descriptor.Bitmap!);
        var point = FindFirstOpaquePixel(edited);
        edited.SetPixel(point.X, point.Y, Color.Transparent);
        var writer = new RsSingleFrameEditWriteService();
        using var preview = writer.Preview(project, descriptor, edited);
        var operation = preview.E5Preview.Operations.Single();
        if (operation.NewKind != "RAW" || preview.E5Preview.FileSizeDeltaBytes != 0)
            throw new InvalidOperationException("S204 Unit_mov #508 preview must remain RAW with zero archive growth.");
        writer.Write(project, preview);

        var entryAfter = e5.ReadIndex(path)[507];
        var bytesAfter = e5.ReadStoredEntryBytes(path, 508);
        if (entryAfter.Kind != "RAW" || entryAfter.DataOffset != entryBefore.DataOffset ||
            entryAfter.StoredLength != entryBefore.StoredLength || new FileInfo(path).Length != fileLength)
            throw new InvalidOperationException("S204 Unit_mov #508 RAW round-trip changed format, offset, length, or archive size.");
        var changed = bytesBefore.Select((value, index) => (value, index))
            .Where(item => item.value != bytesAfter[item.index])
            .Select(item => item.index)
            .ToArray();
        var expectedIndex = descriptor.PhysicalFrameIndex * 48 * 48 + point.Y * 48 + point.X;
        if (changed.Length != 1 || changed[0] != expectedIndex || bytesAfter[expectedIndex] != 0)
            throw new InvalidOperationException(
                $"S204 Unit_mov #508 RAW single-frame edit changed unexpected bytes: {string.Join(",", changed.Take(8))}.");
        using var reread = catalogService.BuildResourceEntry(project, path, 508, "S204 Unit_mov #508 RAW reread");
        var rereadFrame = reread.Frames[descriptor.PhysicalFrameIndex].Bitmap
                          ?? throw new InvalidOperationException("S204 Unit_mov #508 RAW reread frame is missing.");
        if (rereadFrame.GetPixel(point.X, point.Y).A != 0)
            throw new InvalidOperationException("S204 Unit_mov #508 RAW edited pixel did not reread as transparency.");
        Console.WriteLine("RAW_RS_ROUNDTRIP_OK Unit_mov.e5 #508 one target-frame byte changed; offset/entry/archive length unchanged");
    }

    private static void RunNativePngStableRoundTripIfAvailable(CczProject project, EditableImageCodecService codec, E5ImageReplaceService e5)
    {
        foreach (var fileName in new[] { "Unit_mov.e5", "Unit_atk.e5", "Unit_spc.e5", "Pmapobj.e5" })
        {
            var path = CharacterImageResourceService.ResolveGameFile(project, fileName);
            if (!File.Exists(path)) continue;
            var entry = e5.ReadIndex(path).FirstOrDefault(item => item.Kind.Equals("PNG", StringComparison.OrdinalIgnoreCase));
            if (entry == null) continue;
            var spec = EditableImageCodecService.TryResolveRawFrameSpec(path)!.Value;
            var target = new EditableImageTarget
            {
                Kind = EditableImageTargetKind.E5RawStrip,
                DisplayName = "native PNG compact smoke",
                TargetPath = path,
                ImageNumber = entry.ImageNumber,
                FrameWidth = spec.Width,
                FrameHeight = spec.FrameHeight,
                OperationKind = "native PNG compact smoke"
            };
            using var document = codec.Load(project, target);
            if (document.StorageInfo.Kind != EditableImageStorageKind.Png) continue;
            var beforeLength = new FileInfo(path).Length;
            var beforeSha = e5.ComputeArchiveSha256(path);
            var point = FindFirstOpaquePixel(document.Bitmap);
            document.Bitmap.SetPixel(point.X, point.Y, Color.FromArgb(255, 17, 91, 173));
            try
            {
                var preview = codec.PreviewWrite(project, document.Target, document.Bitmap);
                var operation = preview.E5Preview?.Operations.Single()
                                ?? throw new InvalidOperationException("Native PNG preview did not produce one E5 operation.");
                if (operation.NewKind != "PNG" || operation.NewDataOffset != entry.DataOffset ||
                    preview.E5Preview!.FileSizeDeltaBytes != 0)
                    throw new InvalidOperationException("Native PNG pixel editing must keep PNG format, offset, and archive length.");
                codec.Write(project, document.Target, document.Bitmap);
                if (new FileInfo(path).Length != beforeLength || e5.ReadIndex(path)[entry.ImageNumber - 1].Kind != "PNG")
                    throw new InvalidOperationException("Stable-offset PNG editing changed the archive length or format.");
                Console.WriteLine($"NATIVE_PNG_STABLE_OK {fileName} #{entry.ImageNumber}");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("exceeds its stable slot", StringComparison.Ordinal))
            {
                if (new FileInfo(path).Length != beforeLength || e5.ComputeArchiveSha256(path) != beforeSha)
                    throw new InvalidOperationException("Rejected oversized PNG editing still changed the E5 archive.", ex);
                Console.WriteLine($"NATIVE_PNG_OVERSIZE_REJECTED_OK {fileName} #{entry.ImageNumber}");
            }
            return;
        }
    }

    private static void RunArchiveRepairRoundTrip(
        CczProject project,
        EditableImageCodecService codec,
        E5ImageReplaceService e5,
        EditableImageTarget rawTarget)
    {
        using var document = codec.Load(project, rawTarget);
        var random = new Random(19780713);
        for (var y = 0; y < document.Bitmap.Height; y++)
        for (var x = 0; x < document.Bitmap.Width; x++)
            document.Bitmap.SetPixel(x, y, Color.FromArgb(255, random.Next(256), random.Next(256), random.Next(256)));
        using var png = new MemoryStream();
        document.Bitmap.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        var before = e5.ReadIndex(rawTarget.TargetPath)[rawTarget.ImageNumber - 1];
        var oldArchiveSize = new FileInfo(rawTarget.TargetPath).Length;
        if (png.Length <= before.StoredLength)
            throw new InvalidOperationException("Archive repair smoke could not create a PNG larger than the original RAW slot.");
        var conversion = e5.ReplaceBatch(project, rawTarget.TargetPath,
        [
            new E5ImageBatchReplaceRequest
            {
                ImageNumber = rawTarget.ImageNumber,
                SourceBytes = png.ToArray(),
                SourceLabel = "synthetic legacy RAW to PNG append",
                OperationKind = "archive repair smoke setup"
            }
        ]);
        var converted = e5.ReadIndex(rawTarget.TargetPath)[rawTarget.ImageNumber - 1];
        if (converted.Kind != "PNG" || converted.DataOffset == before.DataOffset || new FileInfo(rawTarget.TargetPath).Length <= oldArchiveSize)
            throw new InvalidOperationException("Archive repair smoke setup did not create an appended RAW→PNG entry.");

        var repair = new RsArchiveRepairService();
        var preview = repair.Scan(project);
        var archive = preview.Archives.Single(item => item.TargetPath.Equals(rawTarget.TargetPath, StringComparison.OrdinalIgnoreCase));
        var candidate = archive.Candidates.SingleOrDefault(item => item.ImageNumber == rawTarget.ImageNumber && item.RestoreKind == "RAW")
                        ?? throw new InvalidOperationException("Archive repair scan did not prove the synthetic RAW→PNG candidate from the backup chain.");
        var result = repair.Execute(project, preview, RsArchiveRepairMode.PreservePixelsAndRestoreFormat);
        var repaired = e5.ReadIndex(rawTarget.TargetPath)[rawTarget.ImageNumber - 1];
        if (repaired.Kind != "RAW" || repaired.StoredLength != before.StoredLength ||
            new FileInfo(rawTarget.TargetPath).Length >= converted.DataOffset + converted.StoredLength ||
            result.ChangedArchives.Count == 0 || !File.Exists(result.ReportPath))
            throw new InvalidOperationException("Archive repair did not restore RAW, compact the orphaned PNG, and produce a report.");
        Console.WriteLine($"RS_ARCHIVE_REPAIR_OK {Path.GetFileName(rawTarget.TargetPath)} #{rawTarget.ImageNumber} PNG->RAW compacted");
    }

    static void AssertDllDibDecodeRowDirection()
    {
        var top = Color.FromArgb(255, 230, 70, 80);
        var bottom = Color.FromArgb(255, 40, 190, 80);

        using var standard8Bpp = EditableImageCodecService.DecodeDibForSmoke(BuildSmoke8BppStandardBottomUpDib(top, bottom))
            ?? throw new InvalidOperationException("8bpp 标准 bottom-up DIB 编辑器解码未生成。");
        AssertBitmapPreviewTopBottom(standard8Bpp, top, bottom, "8bpp 标准 bottom-up DIB 编辑器解码");

        using var ccz32Bpp = EditableImageCodecService.DecodeDibForSmoke(BuildSmoke32BppCczTopFirstDib(top, bottom))
            ?? throw new InvalidOperationException("32bpp CCZ top-first DIB 编辑器解码未生成。");
        AssertBitmapPreviewTopBottom(ccz32Bpp, top, bottom, "32bpp CCZ top-first DIB 编辑器解码");
    }

    private static void RunPixelEditResourceGroupSmoke(CczProject project)
    {
        var resolver = new PixelEditResourceGroupResolver();
        var jobTargets = resolver.BuildJobSGroup(project, 0, 1);
        if (jobTargets.Count != 3 ||
            jobTargets[0].FrameWidth != 48 || jobTargets[0].FrameHeight != 48 ||
            jobTargets[1].FrameWidth != 64 || jobTargets[1].FrameHeight != 64 ||
            jobTargets[2].FrameWidth != 48 || jobTargets[2].FrameHeight != 48)
        {
            throw new InvalidOperationException("Detailed-job S pixel group should contain mov/atk/spc frame strips.");
        }

        var s0OneFaction = resolver.BuildCharacterGroup(project, 0, 0, 0, new[] { 1 });
        var s0TwoFactions = resolver.BuildCharacterGroup(project, 0, 0, 0, new[] { 1, 2 });
        var s0AllFactions = resolver.BuildCharacterGroup(project, 0, 0, 0, new[] { 1, 2, 3 });
        if (s0OneFaction.Count != 5 || s0TwoFactions.Count != 8 || s0AllFactions.Count != 11)
        {
            throw new InvalidOperationException($"S=0 character groups should contain 5/8/11 entries, actual={s0OneFaction.Count}/{s0TwoFactions.Count}/{s0AllFactions.Count}.");
        }
        var threeStage = resolver.BuildCharacterGroup(project, 0, 1, 0, new[] { 1 });
        if (threeStage.Count != 11)
        {
            throw new InvalidOperationException($"Three-stage character group should contain R2 + S9 entries, actual={threeStage.Count}.");
        }
        var oneStage = resolver.BuildCharacterGroup(project, 0, 33, 0, new[] { 1 });
        if (oneStage.Count != 5)
        {
            throw new InvalidOperationException($"One-stage character group should contain R2 + S3 entries, actual={oneStage.Count}.");
        }

        var codec = new EditableImageCodecService();
        var editableRGroup = Enumerable.Range(0, 256)
            .Select(id => resolver.BuildRGroup(project, id))
            .FirstOrDefault(targets => targets.Count == 2 && targets.All(target =>
            {
                try { using var document = codec.Load(project, target); return document.StorageInfo.CanEdit; }
                catch { return false; }
            })) ?? throw new InvalidOperationException("No editable RAW/BMP/PNG R front/back group was found for resource-group smoke.");
        using (var unchangedGroup = PixelEditResourceGroup.Load(
                   project,
                   codec,
                   editableRGroup,
                   showTabs: false,
                   scopeDescription: "unchanged R group smoke"))
        {
            var unchangedPreview = new PixelEditResourceGroupWriteService(codec).Preview(project, unchangedGroup);
            if (unchangedPreview.EntryCount != 0)
                throw new InvalidOperationException("An unchanged pixel resource group must produce zero write requests.");
        }
        using var group = PixelEditResourceGroup.Load(
            project,
            codec,
            editableRGroup,
            showTabs: false,
            scopeDescription: "R group smoke");
        group.Pages[0].Document.Bitmap.SetPixel(0, 0, Color.Red);
        group.Pages[1].Document.Bitmap.SetPixel(0, 0, Color.Blue);
        var replacement = new PixelColorReplacementService().Apply(
            group.Pages.Select(page => (page.Key, page.Label, page.Document.Bitmap)).ToArray(),
            [
                new PixelColorReplacementRule(Color.Red, Color.Blue),
                new PixelColorReplacementRule(Color.Blue, Color.Green)
            ]);
        group.ColorReplacementPreview = replacement.Preview;
        var writer = new PixelEditResourceGroupWriteService(codec);
        var preview = writer.Preview(project, group);
        if (preview.Files.Count != 1 || preview.EntryCount != 2 || preview.ColorReplacementPreview?.Rules.Count != 2)
        {
            throw new InvalidOperationException("R front/back group should be merged into one Pmapobj.e5 batch preview.");
        }
        var result = writer.Write(project, group);
        if (result.Files.Count != 1 || result.EntryCount != 2 ||
            string.IsNullOrWhiteSpace(result.Files[0].BackupPath) ||
            string.IsNullOrWhiteSpace(result.AggregateReportPath) ||
            !File.ReadAllText(result.AggregateReportPath).Contains("SourceArgb", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("R front/back group write should produce one backup and an aggregate report.");
        }
    }

    static void AssertDllIconPixelEditorWriteback(string dllPath, int iconIndex)
    {
        var codec = new DllBitmapIconCodecService();
        var resources = codec.ParseBitmapResources(dllPath);
        var pair = codec.ResolveBitmapResourcePair(resources, iconIndex);
        if (pair.AllVariants.Count == 0)
        {
            throw new InvalidOperationException("DLL icon pixel editor writeback did not find reread variants.");
        }

        foreach (var resource in pair.AllVariants)
        {
            using var decoded = DllBitmapIconCodecService.DecodeDib(resource.DibBytes)
                                ?? throw new InvalidOperationException($"DLL icon pixel editor reread failed for ID={resource.Id}.");
            var corner = decoded.Bitmap.GetPixel(0, 0);
            if (corner.A != 0)
            {
                throw new InvalidOperationException($"DLL icon pixel editor transparent corner was not preserved for ID={resource.Id}: {corner}.");
            }

            for (var y = 0; y < decoded.Bitmap.Height; y++)
            {
                for (var x = 0; x < decoded.Bitmap.Width; x++)
                {
                    var pixel = decoded.Bitmap.GetPixel(x, y);
                    if (pixel.A != 0 && DllBitmapIconCodecService.IsMagentaKey(pixel))
                    {
                        throw new InvalidOperationException($"DLL icon pixel editor left visible magenta in ID={resource.Id} at {x},{y}: {pixel}.");
                    }
                }
            }
        }
    }
}
