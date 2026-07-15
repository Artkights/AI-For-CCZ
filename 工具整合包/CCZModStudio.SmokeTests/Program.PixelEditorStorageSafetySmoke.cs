using CCZModStudio;
using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;

internal partial class Program
{
    private const int SyntheticE5IndexOffset = 0x110;
    private const int SyntheticE5IndexEntrySize = 12;

    private static void RunPixelEditorStorageSafetySmoke(CczProject project)
    {
        var root = Path.Combine(project.GameRoot, "PixelEditorStorageSafety");
        Directory.CreateDirectory(root);
        RunRawTransparentAliasSafety(project);
        RunArchiveTopologyRefusalSmoke(project, root);
        RunArchiveSnapshotAndRollbackSmoke(project, root);
        RunItemE5StorageContractSmoke(project, root);
        RunMultiFileGroupRollbackSmoke(project);
        RunPaletteSnapshotSmoke(project);
        Console.WriteLine("PIXEL_EDITOR_STORAGE_SAFETY_OK");
    }

    private static void RunPixelEditorRawIdentitySmoke(CczProject project)
    {
        var e5 = new E5ImageReplaceService();
        var rawCodec = new E5RawImageCodec();
        var palette = new E5RawPaletteService().Load(project);
        if (palette.Colors.Count < 256)
            throw new InvalidOperationException("RAW identity smoke requires a 256-color project palette.");
        var total = 0;
        var trailing = 0;
        foreach (var fileName in new[] { "Pmapobj.e5", "Unit_mov.e5", "Unit_atk.e5", "Unit_spc.e5" })
        {
            var path = CharacterImageResourceService.ResolveGameFile(project, fileName);
            if (!File.Exists(path)) continue;
            var spec = rawCodec.ResolveSpec(path);
            var archiveBytes = File.ReadAllBytes(path);
            var checkedInFile = 0;
            foreach (var entry in e5.ReadIndex(path).Where(entry => entry.Kind.Equals("RAW", StringComparison.OrdinalIgnoreCase)))
            {
                var decoded = archiveBytes.AsSpan(entry.DataOffset, entry.StoredLength).ToArray();
                var probe = RsStripLayoutService.Probe(path, entry.Kind, decoded);
                if (!probe.IsSupportedLayout) continue;
                using var bitmap = rawCodec.DecodeRawBytes(project, decoded, $"{fileName} #{entry.ImageNumber}", spec);
                var originalArgb = BitmapArgbSnapshot.Capture(bitmap);
                var encoded = rawCodec.EncodeBitmapPreservingIndices(
                    bitmap,
                    $"{fileName} #{entry.ImageNumber}",
                    spec,
                    palette.Colors,
                    palette.Path,
                    decoded,
                    originalArgb,
                    probe.TrailingByteCount);
                if (!encoded.RawBytes.SequenceEqual(decoded))
                    throw new InvalidOperationException($"Unchanged RAW identity failed: {fileName} #{entry.ImageNumber}.");
                checkedInFile++;
                total++;
                if (probe.TrailingByteCount == 2) trailing++;
            }
            Console.WriteLine($"RAW_IDENTITY_FILE_OK {fileName} entries={checkedInFile}");
        }
        if (total == 0) throw new InvalidOperationException("RAW identity smoke found no strictly valid RAW entries.");
        Console.WriteLine($"PIXEL_EDITOR_RAW_IDENTITY_OK entries={total} rawPlus2={trailing} root={project.GameRoot}");
    }

    private static void RunRawTransparentAliasSafety(CczProject project)
    {
        var codec = new EditableImageCodecService();
        var e5 = new E5ImageReplaceService();
        foreach (var fileName in new[] { "Pmapobj.e5", "Unit_mov.e5", "Unit_atk.e5", "Unit_spc.e5" })
        {
            var path = CharacterImageResourceService.ResolveGameFile(project, fileName);
            if (!File.Exists(path)) continue;
            var spec = EditableImageCodecService.TryResolveRawFrameSpec(path)!.Value;
            foreach (var entry in e5.ReadIndex(path).Where(entry => entry.Kind.Equals("RAW", StringComparison.OrdinalIgnoreCase)))
            {
                EditableImageDocument? document = null;
                try
                {
                    document = codec.Load(project, BuildRawSmokeTarget(path, entry.ImageNumber, spec));
                    var original = document.SourceSnapshot.DecodedBytes;
                    var aliasIndex = Enumerable.Range(0, document.SourceSnapshot.OriginalArgbPixels.Length)
                        .FirstOrDefault(index => original[index] != 0 &&
                            ((uint)document.SourceSnapshot.OriginalArgbPixels[index] >> 24) == 0, -1);
                    var opaqueIndex = Enumerable.Range(0, document.SourceSnapshot.OriginalArgbPixels.Length)
                        .FirstOrDefault(index => ((uint)document.SourceSnapshot.OriginalArgbPixels[index] >> 24) != 0, -1);
                    if (aliasIndex < 0 || opaqueIndex < 0) continue;

                    document.Bitmap.SetPixel(aliasIndex % document.Bitmap.Width, aliasIndex / document.Bitmap.Width,
                        Color.FromArgb(0, 13, 29, 47));
                    document.Bitmap.SetPixel(opaqueIndex % document.Bitmap.Width, opaqueIndex / document.Bitmap.Width,
                        Color.Transparent);
                    var bytes = codec.PrepareE5Write(project, document.Target, document.Bitmap).Requests.Single().SourceBytes
                                ?? throw new InvalidOperationException("RAW alias smoke did not produce source bytes.");
                    if (bytes[aliasIndex] != original[aliasIndex])
                        throw new InvalidOperationException("A visually unchanged transparent RAW palette alias was rewritten.");
                    var changed = original.Zip(bytes).Count(pair => pair.First != pair.Second);
                    if (changed != 1 || bytes[opaqueIndex] != 0)
                        throw new InvalidOperationException($"RAW alias smoke changed {changed} bytes instead of the one edited opaque pixel.");
                    Console.WriteLine($"RAW_TRANSPARENT_ALIAS_OK {fileName} #{entry.ImageNumber} aliasIndex={aliasIndex}");
                    return;
                }
                catch (InvalidOperationException)
                {
                    document?.Dispose();
                    document = null;
                }
                finally
                {
                    document?.Dispose();
                }
            }
        }
        throw new InvalidOperationException("No transparent nonzero RAW palette alias was found for the preservation smoke.");
    }

    private static void RunArchiveTopologyRefusalSmoke(CczProject project, string root)
    {
        var payload = new byte[32];
        payload[4] = 7;
        var sharedPath = Path.Combine(root, "shared.e5");
        var indexEnd = SyntheticE5IndexOffset + 2 * SyntheticE5IndexEntrySize;
        WritePixelSafetySyntheticE5(sharedPath, [payload, payload], [indexEnd, indexEnd]);
        AssertE5PreviewRejected(project, sharedPath, "shared payload", "shares its payload");

        var overlapPath = Path.Combine(root, "overlap.e5");
        WritePixelSafetySyntheticE5(overlapPath, [new byte[32], new byte[32]], [indexEnd, indexEnd + 16]);
        AssertE5PreviewRejected(project, overlapPath, "overlapping payload", "overlaps its payload range");
        Console.WriteLine("E5_TOPOLOGY_REFUSAL_OK shared+partial-overlap");
    }

    private static void AssertE5PreviewRejected(CczProject project, string path, string label, string expectedText)
    {
        var e5 = new E5ImageReplaceService();
        var entry = e5.ReadIndex(path)[0];
        var source = e5.ReadStoredEntryBytes(path, 1);
        source[5] ^= 0x7F;
        try
        {
            e5.PreviewBatchReplacement(project, path,
            [
                new E5ImageBatchReplaceRequest
                {
                    ImageNumber = 1,
                    SourceBytes = source,
                    SourceBytesAreRaw = true,
                    ExpectedTargetKind = entry.Kind,
                    ExpectedTargetSha256 = Convert.ToHexString(SHA256.HashData(e5.ReadStoredEntryBytes(path, 1))),
                    ExpectedArchiveSha256 = e5.ComputeArchiveSha256(path),
                    ExpectedIndexSha256 = e5.ComputeIndexSha256(path),
                    PlacementPolicy = E5ImageWritePlacementPolicy.RequireExactInPlace
                }
            ]);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains(expectedText, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        throw new InvalidOperationException($"The {label} target was not rejected before pixel writeback.");
    }

    private static void RunArchiveSnapshotAndRollbackSmoke(CczProject project, string root)
    {
        var path = Path.Combine(root, "snapshot.e5");
        WritePixelSafetySyntheticE5(path, [new byte[32], new byte[32]]);
        var e5 = new E5ImageReplaceService();
        var archiveSha = e5.ComputeArchiveSha256(path);
        var indexSha = e5.ComputeIndexSha256(path);
        var timestamp = File.GetLastWriteTimeUtc(path);
        var request = BuildExactRawRequest(e5, path, archiveSha, indexSha);
        var bytes = File.ReadAllBytes(path);
        bytes[0x100] ^= 0x5A;
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, timestamp);
        AssertThrows(() => e5.PreviewBatchReplacement(project, path, [request]), "same-length/same-timestamp archive mutation");

        var indexPath = Path.Combine(root, "index-snapshot.e5");
        WritePixelSafetySyntheticE5(indexPath, [new byte[32], new byte[32]]);
        var expectedIndexSha = e5.ComputeIndexSha256(indexPath);
        var indexBytes = File.ReadAllBytes(indexPath);
        BinaryPrimitives.WriteUInt32BigEndian(indexBytes.AsSpan(SyntheticE5IndexOffset + 4, 4), 31);
        File.WriteAllBytes(indexPath, indexBytes);
        var indexRequest = BuildExactRawRequest(e5, indexPath, string.Empty, expectedIndexSha);
        AssertThrows(() => e5.PreviewBatchReplacement(project, indexPath, [indexRequest]), "index snapshot mutation");

        var preCommitPath = Path.Combine(root, "pre-commit-race.e5");
        WritePixelSafetySyntheticE5(preCommitPath, [new byte[32], new byte[32]]);
        var preCommitSha = e5.ComputeArchiveSha256(preCommitPath);
        var preCommitRequest = BuildExactRawRequest(e5, preCommitPath, preCommitSha, e5.ComputeIndexSha256(preCommitPath));
        var preCommitService = new E5ImageReplaceService
        {
            PreReplaceVerificationTestHook = candidate =>
            {
                var candidateBytes = File.ReadAllBytes(candidate);
                candidateBytes[0x100] ^= 0x6C;
                File.WriteAllBytes(candidate, candidateBytes);
            }
        };
        AssertThrows(() => preCommitService.ReplaceBatch(project, preCommitPath, [preCommitRequest]), "pre-commit external mutation");
        if (preCommitService.ComputeArchiveSha256(preCommitPath).Equals(preCommitSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A pre-commit external E5 mutation was overwritten by the pixel editor backup.");

        var rollbackPath = Path.Combine(root, "rollback.e5");
        WritePixelSafetySyntheticE5(rollbackPath, [new byte[32], new byte[32]]);
        var rollbackSha = e5.ComputeArchiveSha256(rollbackPath);
        var rollbackRequest = BuildExactRawRequest(e5, rollbackPath, rollbackSha, e5.ComputeIndexSha256(rollbackPath));
        var rollbackService = new E5ImageReplaceService
        {
            PostReplaceVerificationTestHook = candidate =>
            {
                var candidateBytes = File.ReadAllBytes(candidate);
                candidateBytes[0x100] ^= 0x33;
                File.WriteAllBytes(candidate, candidateBytes);
            }
        };
        AssertThrows(() => rollbackService.ReplaceBatch(project, rollbackPath, [rollbackRequest]), "post-write verification rollback");
        if (!rollbackService.ComputeArchiveSha256(rollbackPath).Equals(rollbackSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("E5 post-write verification failure did not restore the pre-save archive SHA.");
        Console.WriteLine("E5_SNAPSHOT_AND_ROLLBACK_OK archive+index+pre-commit-race+post-verify");
    }

    private static E5ImageBatchReplaceRequest BuildExactRawRequest(
        E5ImageReplaceService e5,
        string path,
        string archiveSha,
        string indexSha)
    {
        var source = e5.ReadStoredEntryBytes(path, 1);
        source[5] ^= 0x7A;
        return new E5ImageBatchReplaceRequest
        {
            ImageNumber = 1,
            SourceBytes = source,
            SourceBytesAreRaw = true,
            ExpectedTargetKind = e5.ReadIndex(path)[0].Kind,
            ExpectedTargetSha256 = Convert.ToHexString(SHA256.HashData(e5.ReadStoredEntryBytes(path, 1))),
            ExpectedArchiveSha256 = archiveSha,
            ExpectedIndexSha256 = indexSha,
            PlacementPolicy = E5ImageWritePlacementPolicy.RequireExactInPlace
        };
    }

    private static void RunItemE5StorageContractSmoke(CczProject project, string root)
    {
        RunItemPairWriteCase(project, root, "bmp-bmp", "BMP", "BMP");
        RunItemPairWriteCase(project, root, "png-png", "PNG", "PNG");
        RunItemPairWriteCase(project, root, "bmp-png", "BMP", "PNG");
        RunItemPairUnchangedDerivedSmoke(project, root);
        RunItemPairOversizedPngSmoke(project, root);
        RunItemPairReadOnlySmoke(project, root);
        Console.WriteLine("ITEM_E5_STORAGE_CONTRACT_OK bmp/png/mixed/readonly/oversize");
    }

    private static void RunItemPairWriteCase(CczProject project, string root, string name, string smallKind, string largeKind)
    {
        var path = Path.Combine(root, name, "Item.e5");
        var payloads = BuildItemPairPayloads(smallKind, largeKind, padPng: true);
        WritePixelSafetySyntheticE5(path, payloads);
        var codec = new EditableImageCodecService();
        var e5 = new E5ImageReplaceService();
        var beforeEntries = e5.ReadIndex(path);
        var beforeLength = new FileInfo(path).Length;
        using var document = codec.Load(project, BuildItemPairTarget(path));
        for (var y = 0; y < 4; y++)
        for (var x = 0; x < 4; x++)
            document.Bitmap.SetPixel(x, y, Color.FromArgb(255, 211, 37, 89));
        var prepared = codec.PrepareE5Write(project, document.Target, document.Bitmap);
        if (prepared.Requests.Count != 2)
            throw new InvalidOperationException($"Item.e5 {name} should update both derived icon entries, actual={prepared.Requests.Count}.");
        foreach (var request in prepared.Requests)
        {
            var expected = request.ImageNumber == 1 ? smallKind : largeKind;
            if (!request.ExpectedTargetKind.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Item.e5 {name} #{request.ImageNumber} changed its storage contract.");
        }
        codec.Write(project, document.Target, document.Bitmap);
        var afterEntries = e5.ReadIndex(path);
        for (var index = 0; index < 2; index++)
        {
            if (!afterEntries[index].Kind.Equals(beforeEntries[index].Kind, StringComparison.OrdinalIgnoreCase) ||
                afterEntries[index].DataOffset != beforeEntries[index].DataOffset)
                throw new InvalidOperationException($"Item.e5 {name} #{index + 1} changed format or offset.");
        }
        if (new FileInfo(path).Length != beforeLength)
            throw new InvalidOperationException($"Item.e5 {name} pixel editing changed the archive length.");
    }

    private static void RunItemPairUnchangedDerivedSmoke(CczProject project, string root)
    {
        var path = Path.Combine(root, "derived-unchanged", "Item.e5");
        WritePixelSafetySyntheticE5(path, BuildItemPairPayloads("BMP", "BMP", padPng: false));
        var codec = new EditableImageCodecService();
        using var document = codec.Load(project, BuildItemPairTarget(path));
        document.Bitmap.SetPixel(0, 0, Color.FromArgb(0, 91, 37, 13));
        try
        {
            codec.PreviewWrite(project, document.Target, document.Bitmap);
            throw new InvalidOperationException("Hidden RGB changes under full transparency must be treated as an unchanged Item.e5 canvas.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No pixel changes", StringComparison.OrdinalIgnoreCase))
        {
        }

        for (var y = 0; y < document.Bitmap.Height; y++)
        for (var x = 0; x < document.Bitmap.Width; x++)
        {
            var original = document.OriginalBitmap.GetPixel(x, y);
            document.Bitmap.SetPixel(x, y, original.A == 0 ? Color.Red : Color.Transparent);
            var prepared = codec.PrepareE5Write(project, document.Target, document.Bitmap);
            document.Bitmap.SetPixel(x, y, original);
            if (prepared.Requests.Count == 1 && prepared.Requests[0].ImageNumber == 2)
                return;
        }
        throw new InvalidOperationException("Item.e5 did not omit an unchanged derived small icon from the write set.");
    }

    private static void RunItemPairOversizedPngSmoke(CczProject project, string root)
    {
        var path = Path.Combine(root, "png-oversize", "Item.e5");
        WritePixelSafetySyntheticE5(path, BuildItemPairPayloads("PNG", "PNG", padPng: false));
        var codec = new EditableImageCodecService();
        var e5 = new E5ImageReplaceService();
        var beforeSha = e5.ComputeArchiveSha256(path);
        using var document = codec.Load(project, BuildItemPairTarget(path));
        var random = new Random(314159);
        for (var y = 0; y < document.Bitmap.Height; y++)
        for (var x = 0; x < document.Bitmap.Width; x++)
            document.Bitmap.SetPixel(x, y, Color.FromArgb(255, random.Next(256), random.Next(256), random.Next(256)));
        AssertThrows(() => codec.PreviewWrite(project, document.Target, document.Bitmap), "Item.e5 oversized PNG");
        if (!e5.ComputeArchiveSha256(path).Equals(beforeSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Rejected oversized Item.e5 PNG changed the archive.");
    }

    private static void RunItemPairReadOnlySmoke(CczProject project, string root)
    {
        var path = Path.Combine(root, "jpg-readonly", "Item.e5");
        WritePixelSafetySyntheticE5(path, BuildItemPairPayloads("JPG", "BMP", padPng: false));
        var codec = new EditableImageCodecService();
        AssertThrows(() =>
        {
            using var _ = codec.Load(project, BuildItemPairTarget(path));
        }, "Item.e5 JPG pair read-only");
    }

    private static IReadOnlyList<byte[]> BuildItemPairPayloads(string smallKind, string largeKind, bool padPng)
    {
        using var source = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(source))
        {
            graphics.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(255, 50, 140, 220));
            graphics.FillRectangle(brush, 8, 7, 16, 19);
        }
        var pair = new ItemIconRasterNormalizeService().NormalizePair(source, "synthetic Item.e5");
        using var small = pair.Small.CreateTransparentBitmap();
        using var large = pair.Large.CreateTransparentBitmap();
        return [EncodeItemPayload(small, smallKind, padPng), EncodeItemPayload(large, largeKind, padPng)];
    }

    private static byte[] EncodeItemPayload(Bitmap bitmap, string kind, bool padPng)
    {
        if (kind.Equals("BMP", StringComparison.OrdinalIgnoreCase))
            return ItemIconRasterNormalizeService.EncodeTransparentBitmapToGameBmp(bitmap);
        using var memory = new MemoryStream();
        if (kind.Equals("PNG", StringComparison.OrdinalIgnoreCase))
        {
            bitmap.Save(memory, ImageFormat.Png);
            var bytes = memory.ToArray();
            if (!padPng) return bytes;
            Array.Resize(ref bytes, Math.Max(4096, bytes.Length + 1024));
            return bytes;
        }
        if (kind.Equals("JPG", StringComparison.OrdinalIgnoreCase))
        {
            using var flattened = ItemIconRasterNormalizeService.CompositeTransparentToGameMagenta(bitmap);
            flattened.Save(memory, ImageFormat.Jpeg);
            return memory.ToArray();
        }
        throw new InvalidOperationException("Unsupported synthetic Item.e5 payload kind: " + kind);
    }

    private static EditableImageTarget BuildItemPairTarget(string path)
        => new()
        {
            Kind = EditableImageTargetKind.E5Standard,
            DisplayName = "Synthetic Item.e5 pair",
            TargetPath = path,
            ImageNumber = 2,
            IsItemIconPair = true,
            SmallImageNumber = 1,
            LargeImageNumber = 2,
            OperationKind = "Item.e5 storage safety smoke"
        };

    private static void RunMultiFileGroupRollbackSmoke(CczProject project)
    {
        var codec = new EditableImageCodecService();
        var files = new[] { "Unit_mov.e5", "Unit_atk.e5", "Unit_spc.e5" }
            .Select(name => CharacterImageResourceService.ResolveGameFile(project, name))
            .Where(File.Exists)
            .ToArray();
        var counts = files.Select(path => new E5ImageReplaceService().ReadIndex(path).Count).ToArray();
        IReadOnlyList<EditableImageTarget>? targets = null;
        for (var imageNumber = 1; imageNumber <= counts.Min() && targets == null; imageNumber++)
        {
            var candidates = files.Take(2).Select(path =>
            {
                var spec = EditableImageCodecService.TryResolveRawFrameSpec(path)!.Value;
                return BuildRawSmokeTarget(path, imageNumber, spec);
            }).ToArray();
            try
            {
                foreach (var candidate in candidates)
                {
                    using var document = codec.Load(project, candidate);
                    if (!document.StorageInfo.CanEdit) throw new InvalidOperationException();
                }
                targets = candidates;
            }
            catch
            {
            }
        }
        if (targets == null) throw new InvalidOperationException("No two-file editable S group was found for rollback smoke.");

        using var group = PixelEditResourceGroup.Load(project, codec, targets, true, "multi-file rollback smoke");
        foreach (var page in group.Pages)
        {
            var point = FindFirstOpaquePixel(page.Document.Bitmap);
            page.Document.Bitmap.SetPixel(point.X, point.Y, Color.Transparent);
        }
        var before = targets.ToDictionary(target => target.TargetPath,
            target => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target.TargetPath))),
            StringComparer.OrdinalIgnoreCase);
        var writer = new PixelEditResourceGroupWriteService(codec)
        {
            BeforeFileWriteTestHook = (_, index) =>
            {
                if (index == 1) throw new IOException("Injected second-file commit failure.");
            }
        };
        AssertThrows(() => writer.Write(project, group), "multi-file group rollback");
        foreach (var pair in before)
        {
            var after = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pair.Key)));
            if (!after.Equals(pair.Value, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Multi-file rollback did not restore {Path.GetFileName(pair.Key)}.");
        }
        Console.WriteLine("PIXEL_GROUP_ROLLBACK_OK files=2");
    }

    private static void RunPaletteSnapshotSmoke(CczProject project)
    {
        var path = Path.Combine(project.GameRoot, "Spalet.e5");
        var existed = File.Exists(path);
        var original = existed ? File.ReadAllBytes(path) : null;
        var originalTime = existed ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        try
        {
            var paletteBytes = new byte[256 * 3];
            for (var index = 0; index < 256; index++)
            {
                paletteBytes[index * 3] = (byte)index;
                paletteBytes[index * 3 + 1] = (byte)(255 - index);
                paletteBytes[index * 3 + 2] = (byte)((index * 17) & 0xFF);
            }
            WritePixelSafetySyntheticE5(path, [paletteBytes]);
            var codec = new EditableImageCodecService();
            var e5 = new E5ImageReplaceService();
            var rawPath = CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5");
            var entry = e5.ReadIndex(rawPath).First(item => item.Kind.Equals("RAW", StringComparison.OrdinalIgnoreCase));
            var spec = EditableImageCodecService.TryResolveRawFrameSpec(rawPath)!.Value;
            using var document = codec.Load(project, BuildRawSmokeTarget(rawPath, entry.ImageNumber, spec));
            var timestamp = File.GetLastWriteTimeUtc(path);
            var changed = File.ReadAllBytes(path);
            changed[^1] ^= 0x51;
            File.WriteAllBytes(path, changed);
            File.SetLastWriteTimeUtc(path, timestamp);
            var point = FindFirstOpaquePixel(document.Bitmap);
            document.Bitmap.SetPixel(point.X, point.Y, Color.Transparent);
            AssertThrows(() => codec.PrepareE5Write(project, document.Target, document.Bitmap), "same-length/same-timestamp palette mutation");
            Console.WriteLine("RAW_PALETTE_SNAPSHOT_OK content-sha");
        }
        finally
        {
            if (existed)
            {
                File.WriteAllBytes(path, original!);
                File.SetLastWriteTimeUtc(path, originalTime);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static EditableImageTarget BuildRawSmokeTarget(
        string path,
        int imageNumber,
        (int Width, int FrameHeight) spec)
        => new()
        {
            Kind = EditableImageTargetKind.E5RawStrip,
            DisplayName = $"RAW safety #{imageNumber}",
            TargetPath = path,
            ImageNumber = imageNumber,
            FrameWidth = spec.Width,
            FrameHeight = spec.FrameHeight,
            OperationKind = "RAW storage safety smoke"
        };

    private static void WritePixelSafetySyntheticE5(
        string path,
        IReadOnlyList<byte[]> payloads,
        IReadOnlyList<int>? explicitOffsets = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (payloads.Count == 0) throw new InvalidOperationException("Synthetic E5 requires at least one payload.");
        var indexEnd = checked(SyntheticE5IndexOffset + payloads.Count * SyntheticE5IndexEntrySize);
        var offsets = explicitOffsets?.ToArray() ?? new int[payloads.Count];
        if (explicitOffsets == null)
        {
            var cursor = indexEnd;
            for (var index = 0; index < payloads.Count; index++)
            {
                offsets[index] = cursor;
                cursor = checked(cursor + payloads[index].Length);
            }
        }
        if (offsets.Length != payloads.Count) throw new InvalidOperationException("Synthetic E5 offset count mismatch.");
        var length = Math.Max(indexEnd, payloads.Select((payload, index) => checked(offsets[index] + payload.Length)).Max());
        var bytes = new byte[length];
        Encoding.ASCII.GetBytes("Ls12").CopyTo(bytes, 0);
        for (var index = 0; index < payloads.Count; index++)
        {
            var indexOffset = SyntheticE5IndexOffset + index * SyntheticE5IndexEntrySize;
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(indexOffset, 4), checked((uint)payloads[index].Length));
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(indexOffset + 4, 4), checked((uint)payloads[index].Length));
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(indexOffset + 8, 4), checked((uint)offsets[index]));
            payloads[index].CopyTo(bytes, offsets[index]);
        }
        File.WriteAllBytes(path, bytes);
    }

    private static void AssertThrows(Action action, string label)
    {
        try
        {
            action();
        }
        catch
        {
            return;
        }
        throw new InvalidOperationException($"Expected failure was not raised: {label}.");
    }
}
