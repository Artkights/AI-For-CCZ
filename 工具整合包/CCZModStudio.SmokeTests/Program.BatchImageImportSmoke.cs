using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;

internal partial class Program
{
    static void RunBatchImageImportSmoke(CczProject project)
    {
        var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "BatchImageImportSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(smokeRoot);

        foreach (var fileName in new[] { "Ekd5.exe", "Data.e5", "Imsg.e5", "Star.e5", "Hexzmap.e5", "Pmapobj.e5", "Unit_mov.e5", "Unit_atk.e5", "Unit_spc.e5", "Face.e5", "Itemicon.dll" })
        {
            var source = CharacterImageResourceService.ResolveGameFile(project, fileName);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(smokeRoot, fileName), overwrite: false);
            }
        }

        File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=batch image import smoke\r\n");
        var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
        var e5 = new E5ImageReplaceService();

        RunBatchRImageSmoke(testProject, smokeRoot, e5);
        RunBatchSImageSmoke(testProject, smokeRoot, e5);
        RunBatchRoleFaceSmoke(testProject, smokeRoot, e5);
        RunBatchItemIconSmoke(testProject, smokeRoot);

        Console.WriteLine($"BATCH_IMAGE_IMPORT_SMOKE OK root={smokeRoot}");
    }

    private static void RunBatchRImageSmoke(CczProject testProject, string smokeRoot, E5ImageReplaceService e5)
    {
        var materialRoot = Path.Combine(smokeRoot, "_BatchRMaterials");
        CreateBatchRFolder(materialRoot, "R1", Color.FromArgb(255, 220, 40, 60), Color.FromArgb(255, 40, 160, 220));
        CreateBatchRFolder(materialRoot, "R_2", Color.FromArgb(255, 40, 180, 90), Color.FromArgb(255, 220, 160, 40));
        CreateBatchRFolder(materialRoot, "99", Color.FromArgb(255, 160, 90, 220), Color.FromArgb(255, 230, 200, 60));

        var service = new BatchRImageReplaceService();
        var request = new BatchRImageReplaceRequest
        {
            MaterialRoot = materialRoot,
            AllowedRImageIds = new HashSet<int> { 1, 2 },
            IncludeOnlySelectedOrFiltered = true,
            WriteMode = "test_copy"
        };
        var preview = service.Preview(testProject, request);
        if (!preview.CanWrite ||
            preview.Items.Count != 2 ||
            preview.TotalOperationCount != 4 ||
            !preview.Items.SelectMany(item => new[] { item.FrontImageNumber, item.BackImageNumber }).SequenceEqual(new[] { 3, 4, 5, 6 }) ||
            !preview.SkippedItems.Any(item => item.Key == "99" && item.Reason.StartsWith(BatchImageImportSkipReasons.Unused, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Batch R image preview did not match the expected selected-id mapping.");
        }

        var result = service.Replace(testProject, request);
        if (result.WriteResult == null ||
            result.WriteResult.OperationCount != 4 ||
            !File.Exists(result.WriteResult.BackupPath) ||
            !File.Exists(result.AggregateReportPath))
        {
            throw new InvalidOperationException("Batch R image write did not create the expected single E5 write result and aggregate report.");
        }

        var target = CharacterImageResourceService.ResolveGameFile(testProject, "Pmapobj.e5");
        foreach (var imageNumber in new[] { 3, 4, 5, 6 })
        {
            VerifyRawEntry(e5, target, imageNumber, 48 * 1280);
        }

        var missingRoot = Path.Combine(smokeRoot, "_BatchRMissing");
        var missingFolder = Path.Combine(missingRoot, "R3");
        Directory.CreateDirectory(missingFolder);
        CreateSmokeBmp(Path.Combine(missingFolder, "front.bmp"), 48, 1280, Color.FromArgb(255, 90, 180, 230), Color.FromArgb(255, 230, 80, 120));
        var missingPreview = service.Preview(testProject, new BatchRImageReplaceRequest
        {
            MaterialRoot = missingRoot,
            IncludeOnlySelectedOrFiltered = false,
            WriteMode = "test_copy"
        });
        if (missingPreview.CanWrite ||
            missingPreview.SkippedItems.All(item => !item.Reason.StartsWith(BatchImageImportSkipReasons.MissingFile, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Batch R image preview should block when back.bmp is missing.");
        }
    }

    private static void RunBatchSImageSmoke(CczProject testProject, string smokeRoot, E5ImageReplaceService e5)
    {
        var materialRoot = Path.Combine(smokeRoot, "_BatchSMaterials");
        CreateBatchSFolder(materialRoot, "S1", Color.FromArgb(255, 220, 30, 80), Color.FromArgb(255, 30, 160, 220));
        CreateBatchSFolder(materialRoot, "S250", Color.FromArgb(255, 30, 180, 90), Color.FromArgb(255, 220, 140, 30));
        CreateBatchSFolder(materialRoot, "0", Color.FromArgb(255, 140, 80, 220), Color.FromArgb(255, 240, 220, 40));

        var service = new BatchSImageReplaceService();
        var request = new BatchSImageReplaceRequest
        {
            MaterialRoot = materialRoot,
            AllowedSImageUsages = new[]
            {
                new BatchSImageUsage(1, null, 1),
                new BatchSImageUsage(250, null, 1),
                new BatchSImageUsage(0, 7, 2)
            },
            IncludeOnlySelectedOrFiltered = true,
            FactionSlot = 1,
            WriteMode = "test_copy"
        };
        var preview = service.Preview(testProject, request);
        if (!preview.CanWrite ||
            preview.Items.Count != 3 ||
            preview.TotalOperationCount != 15 ||
            preview.FilePreviews.Count != 3 ||
            preview.Items.Single(item => item.SImageId == 1).ImageNumbers.SequenceEqual(new[] { 241, 242, 243 }) == false ||
            preview.Items.Single(item => item.SImageId == 250).ImageNumbers.SequenceEqual(new[] { 554 }) == false ||
            preview.Items.Single(item => item.SImageId == 0).ImageNumbers.SequenceEqual(new[] { 23 }) == false)
        {
            throw new InvalidOperationException("Batch S image preview did not match regular, staged, and S=0 mappings.");
        }

        var result = service.Replace(testProject, request);
        if (result.WriteResults.Count != 3 ||
            result.TotalOperationCount != 15 ||
            result.WriteResults.Values.Any(write => !File.Exists(write.BackupPath) || !File.Exists(write.ReportJsonPath)) ||
            !File.Exists(result.AggregateReportPath))
        {
            throw new InvalidOperationException("Batch S image write did not create grouped write results and aggregate report.");
        }

        foreach (var imageNumber in new[] { 23, 241, 242, 243, 554 })
        {
            VerifyRawEntry(e5, CharacterImageResourceService.ResolveGameFile(testProject, "Unit_mov.e5"), imageNumber, 48 * 528);
            VerifyRawEntry(e5, CharacterImageResourceService.ResolveGameFile(testProject, "Unit_atk.e5"), imageNumber, 64 * 768);
            VerifyRawEntry(e5, CharacterImageResourceService.ResolveGameFile(testProject, "Unit_spc.e5"), imageNumber, 48 * 240);
        }

        var duplicatePreview = service.Preview(testProject, new BatchSImageReplaceRequest
        {
            MaterialRoot = materialRoot,
            AllowedSImageUsages = new[]
            {
                new BatchSImageUsage(250, null, 1),
                new BatchSImageUsage(250, 1, 1)
            },
            IncludeOnlySelectedOrFiltered = true,
            WriteMode = "test_copy"
        });
        if (duplicatePreview.CanWrite ||
            duplicatePreview.SkippedItems.All(item => !item.Reason.StartsWith(BatchImageImportSkipReasons.DuplicateTarget, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Batch S image preview should block duplicate target image numbers.");
        }

        var missingRoot = Path.Combine(smokeRoot, "_BatchSMissing");
        var missingFolder = Path.Combine(missingRoot, "S249");
        Directory.CreateDirectory(missingFolder);
        CreateSmokeBmp(Path.Combine(missingFolder, "mov.bmp"), 48, 528, Color.FromArgb(255, 90, 180, 230), Color.FromArgb(255, 230, 90, 150));
        CreateSmokeBmp(Path.Combine(missingFolder, "atk.bmp"), 64, 768, Color.FromArgb(255, 190, 80, 220), Color.FromArgb(255, 80, 220, 180));
        var missingPreview = service.Preview(testProject, new BatchSImageReplaceRequest
        {
            MaterialRoot = missingRoot,
            IncludeOnlySelectedOrFiltered = false,
            WriteMode = "test_copy"
        });
        if (missingPreview.CanWrite ||
            missingPreview.SkippedItems.All(item => !item.Reason.StartsWith(BatchImageImportSkipReasons.MissingFile, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Batch S image preview should block when spc.bmp is missing.");
        }
    }

    private static void RunBatchItemIconSmoke(CczProject testProject, string smokeRoot)
    {
        var itemIconPath = CharacterImageResourceService.ResolveGameFile(testProject, "Itemicon.dll");
        if (!File.Exists(itemIconPath))
        {
            throw new FileNotFoundException("Batch item icon smoke requires Itemicon.dll.", itemIconPath);
        }

        var service = new BatchItemIconImportService();
        var iconA = Path.Combine(smokeRoot, "item_icon_0.png");
        var iconB = Path.Combine(smokeRoot, "item_icon_1.png");
        var iconC = Path.Combine(smokeRoot, "item_icon_12.png");
        var invalid = Path.Combine(smokeRoot, "item_icon_no_number.png");
        CreateSmokePng(iconA, 32, 32, Color.FromArgb(255, 230, 70, 80), Color.FromArgb(255, 40, 190, 80));
        CreateSmokePng(iconB, 32, 32, Color.FromArgb(255, 70, 110, 230), Color.FromArgb(255, 220, 180, 40));
        CreateSmokePng(iconC, 32, 32, Color.FromArgb(255, 160, 80, 220), Color.FromArgb(255, 50, 210, 180));
        CreateSmokePng(invalid, 32, 32, Color.FromArgb(255, 180, 180, 180), Color.FromArgb(255, 80, 80, 80));

        var orderedRequest = new BatchItemIconImportRequest
        {
            SourceFiles = new[] { iconA, iconB },
            TargetRows = new[]
            {
                new BatchItemIconTargetRow(100, "Smoke A", 0),
                new BatchItemIconTargetRow(101, "Smoke B", 1)
            },
            MatchMode = "auto",
            WriteMode = "test_copy"
        };
        var orderedPreview = service.Preview(testProject, orderedRequest);
        if (!orderedPreview.CanWrite ||
            orderedPreview.ResourceKind != "DLL" ||
            orderedPreview.Items.Count != 2 ||
            orderedPreview.TotalOperationCount != 2 ||
            orderedPreview.Items[0].IconIndex != 0 ||
            orderedPreview.Items[1].IconIndex != 1 ||
            orderedPreview.Items.Any(item => item.ResourceIds.Count == 0))
        {
            throw new InvalidOperationException("Batch item icon ordered preview did not map selected rows to source files.");
        }

        var orderedResult = service.Replace(testProject, orderedRequest);
        if (orderedResult.DllResult == null ||
            orderedResult.DllResult.Items.Count != 2 ||
            !File.Exists(orderedResult.DllResult.BackupPath) ||
            !File.Exists(orderedResult.AggregateReportPath) ||
            orderedResult.DllResult.NewFileSha256.Equals(orderedResult.DllResult.OldFileSha256, StringComparison.OrdinalIgnoreCase) ||
            !orderedResult.DllResult.ReadbackVerified ||
            orderedResult.DllResult.ReadbackItems.Count == 0)
        {
            throw new InvalidOperationException("Batch item icon DLL write did not create the expected write result and aggregate report.");
        }

        var itemPreviewAfterWrite = new ItemIconPreviewService().BuildPreview(testProject, 0, "Itemicon.dll", "物品图标", 96);
        if (!itemPreviewAfterWrite.RenderMode.Equals("DLL RT_BITMAP", StringComparison.Ordinal) ||
            itemPreviewAfterWrite.Bitmap == null ||
            itemPreviewAfterWrite.LargeBitmap == null ||
            itemPreviewAfterWrite.SmallBitmap == null)
        {
            throw new InvalidOperationException("Batch item icon preview after write did not use reread DLL RT_BITMAP data.");
        }

        using (itemPreviewAfterWrite.Bitmap)
        using (itemPreviewAfterWrite.NativeBitmap)
        using (itemPreviewAfterWrite.LargeBitmap)
        using (itemPreviewAfterWrite.SmallBitmap)
        {
            AssertBitmapPreviewTopBottom(
                itemPreviewAfterWrite.LargeBitmap,
                Color.FromArgb(255, 230, 70, 80),
                Color.FromArgb(255, 40, 190, 80),
                "Batch item icon reread large RT_BITMAP");
        }

        var magentaIcon = Path.Combine(smokeRoot, "item_icon_magenta_key.png");
        using (var source = CreateMagentaKeyIconSource(32, 32))
        {
            source.Save(magentaIcon, System.Drawing.Imaging.ImageFormat.Png);
        }

        var magentaRequest = new BatchItemIconImportRequest
        {
            SourceFiles = new[] { magentaIcon },
            TargetRows = new[]
            {
                new BatchItemIconTargetRow(102, "Smoke Magenta", 3)
            },
            MatchMode = "auto",
            WriteMode = "test_copy"
        };
        var magentaResult = service.Replace(testProject, magentaRequest);
        if (magentaResult.DllResult == null ||
            !magentaResult.DllResult.ReadbackVerified ||
            magentaResult.DllResult.ReadbackItems.All(item => item.MagentaKeyPixelCount == 0))
        {
            throw new InvalidOperationException("Batch item icon magenta-key import did not convert magenta background to transparent RT_BITMAP data.");
        }

        var magentaPreview = new ItemIconPreviewService().BuildPreview(testProject, 3, "Itemicon.dll", "物品图标", 96);
        if (magentaPreview.LargeBitmap == null || magentaPreview.SmallBitmap == null)
        {
            throw new InvalidOperationException("Batch item icon magenta-key preview did not produce large/small bitmaps.");
        }

        using (magentaPreview.Bitmap)
        using (magentaPreview.NativeBitmap)
        using (magentaPreview.LargeBitmap)
        using (magentaPreview.SmallBitmap)
        {
            AssertTransparentPixel(magentaPreview.LargeBitmap, 1, 0, "Batch item icon magenta-key large background");
            AssertNoVisibleMagenta(magentaPreview.LargeBitmap, "Batch item icon magenta-key large");
            AssertNoVisibleMagenta(magentaPreview.SmallBitmap, "Batch item icon magenta-key small");
        }

        var filenameRequest = new BatchItemIconImportRequest
        {
            SourceFiles = new[] { iconC },
            TargetRows = new[]
            {
                new BatchItemIconTargetRow(112, "Smoke #12", 12),
                new BatchItemIconTargetRow(113, "Smoke #13", 13)
            },
            MatchMode = "auto",
            WriteMode = "test_copy"
        };
        var filenamePreview = service.Preview(testProject, filenameRequest);
        if (!filenamePreview.CanWrite ||
            filenamePreview.Items.Count != 1 ||
            filenamePreview.Items[0].IconIndex != 12 ||
            filenamePreview.SkippedItems.Any(item => item.Reason.StartsWith(BatchImageImportSkipReasons.CountMismatch, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Batch item icon auto mode should fall back to file-name icon number matching.");
        }

        var strictPreview = service.Preview(testProject, WithMatchMode(filenameRequest, "selected-row-order"));
        if (strictPreview.CanWrite ||
            strictPreview.SkippedItems.All(item => !item.Reason.StartsWith(BatchImageImportSkipReasons.CountMismatch, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Batch item icon selected-row-order mode should block count mismatches.");
        }

        var invalidPreview = service.Preview(testProject, new BatchItemIconImportRequest
        {
            SourceFiles = new[] { invalid },
            MatchMode = "auto",
            WriteMode = "test_copy"
        });
        if (invalidPreview.CanWrite ||
            invalidPreview.SkippedItems.All(item => !item.Reason.StartsWith(BatchImageImportSkipReasons.InvalidName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Batch item icon preview should block files without a numeric icon id.");
        }

        static BatchItemIconImportRequest WithMatchMode(BatchItemIconImportRequest source, string matchMode)
            => new()
            {
                SourceFiles = source.SourceFiles,
                TargetRows = source.TargetRows,
                MatchMode = matchMode,
                WriteMode = source.WriteMode
            };
    }

    private static void RunBatchRoleFaceSmoke(CczProject testProject, string smokeRoot, E5ImageReplaceService e5)
    {
        var facePath = CharacterImageResourceService.ResolveFaceFile(testProject);
        if (facePath == null || !File.Exists(facePath))
        {
            throw new FileNotFoundException("Batch role face smoke requires Face.e5.", facePath ?? Path.Combine(testProject.GameRoot, "Face.e5"));
        }

        var service = new BatchRoleFaceImportService();
        var faceA = Path.Combine(smokeRoot, "face_1.png");
        var faceB = Path.Combine(smokeRoot, "face_2.png");
        var faceC = Path.Combine(smokeRoot, "face_12.png");
        var face0 = Path.Combine(smokeRoot, "face_0.png");
        var invalid = Path.Combine(smokeRoot, "face_no_number.png");
        var invalidSize = Path.Combine(smokeRoot, "face_14.png");
        var bmpFace = Path.Combine(smokeRoot, "face_15.bmp");
        CreateSmokePng(faceA, 120, 120, Color.FromArgb(255, 230, 80, 80), Color.FromArgb(255, 40, 180, 90));
        CreateSmokePng(faceB, 120, 120, Color.FromArgb(255, 70, 120, 230), Color.FromArgb(255, 220, 190, 40));
        CreateSmokePng(faceC, 120, 120, Color.FromArgb(255, 150, 80, 220), Color.FromArgb(255, 50, 210, 180));
        CreateSmokePng(face0, 120, 120, Color.FromArgb(255, 220, 220, 80), Color.FromArgb(255, 80, 110, 220));
        CreateSmokePng(invalid, 120, 120, Color.FromArgb(255, 180, 180, 180), Color.FromArgb(255, 80, 80, 80));
        CreateSmokePng(invalidSize, 80, 80, Color.FromArgb(255, 120, 120, 220), Color.FromArgb(255, 220, 120, 120));
        CreateSmokeBmp(bmpFace, 120, 120, Color.FromArgb(255, 80, 200, 230), Color.FromArgb(255, 230, 80, 140));

        var orderedRequest = new BatchRoleFaceImportRequest
        {
            SourceFiles = new[] { faceA, faceB },
            TargetRows = new[]
            {
                new BatchRoleFaceTargetRow(1, "Smoke Face A", 1),
                new BatchRoleFaceTargetRow(2, "Smoke Face B", 2)
            },
            MatchMode = "auto",
            WriteMode = "test_copy"
        };
        var orderedPreview = service.Preview(testProject, orderedRequest);
        if (!orderedPreview.CanWrite ||
            orderedPreview.Items.Count != 2 ||
            orderedPreview.TotalOperationCount != 2 ||
            !orderedPreview.Items.SelectMany(item => item.TargetImageNumbers).SequenceEqual(new[] { 9, 10 }))
        {
            throw new InvalidOperationException("Batch role face ordered preview did not map selected rows to Face.e5 image numbers.");
        }

        var orderedResult = service.Replace(testProject, orderedRequest);
        if (orderedResult.E5Result == null ||
            orderedResult.E5Result.OperationCount != 2 ||
            !File.Exists(orderedResult.E5Result.BackupPath) ||
            !File.Exists(orderedResult.AggregateReportPath) ||
            !VerifyFacePngEntry(e5, facePath, 9) ||
            !VerifyFacePngEntry(e5, facePath, 10))
        {
            throw new InvalidOperationException("Batch role face write did not create the expected E5 result and 120x120 PNG entries.");
        }

        var filenameRequest = new BatchRoleFaceImportRequest
        {
            SourceFiles = new[] { faceC },
            TargetRows = new[]
            {
                new BatchRoleFaceTargetRow(12, "Smoke Face #12", 12),
                new BatchRoleFaceTargetRow(13, "Smoke Face #13", 13)
            },
            MatchMode = "auto",
            WriteMode = "test_copy"
        };
        var filenamePreview = service.Preview(testProject, filenameRequest);
        if (!filenamePreview.CanWrite ||
            filenamePreview.Items.Count != 1 ||
            filenamePreview.Items[0].FaceId != 12 ||
            filenamePreview.Items[0].TargetImageNumbers.Single() != 20 ||
            filenamePreview.SkippedItems.Any(item => item.Reason.StartsWith(BatchImageImportSkipReasons.CountMismatch, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Batch role face auto mode should fall back to file-name face id matching.");
        }

        var faceZeroPreview = service.Preview(testProject, new BatchRoleFaceImportRequest
        {
            SourceFiles = new[] { face0 },
            TargetRows = new[] { new BatchRoleFaceTargetRow(0, "Smoke Face #0", 0) },
            MatchMode = "auto",
            WriteMode = "test_copy"
        });
        if (!faceZeroPreview.CanWrite ||
            faceZeroPreview.Items.Single().TargetImageNumbers.SequenceEqual(new[] { 1 }) == false ||
            faceZeroPreview.Warnings.All(warning => !warning.Contains("头像号 0", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Batch role face preview should map face id 0 to Face.e5 #1 and warn about the legacy multi-expression slot.");
        }

        var duplicatePreview = service.Preview(testProject, new BatchRoleFaceImportRequest
        {
            SourceFiles = new[] { faceA, faceB },
            TargetRows = new[]
            {
                new BatchRoleFaceTargetRow(21, "Duplicate A", 21),
                new BatchRoleFaceTargetRow(22, "Duplicate B", 21)
            },
            MatchMode = "auto",
            WriteMode = "test_copy"
        });
        if (duplicatePreview.CanWrite ||
            duplicatePreview.SkippedItems.All(item => !item.Reason.StartsWith(BatchImageImportSkipReasons.DuplicateTarget, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Batch role face preview should block duplicate target face ids.");
        }

        var invalidPreview = service.Preview(testProject, new BatchRoleFaceImportRequest
        {
            SourceFiles = new[] { invalid },
            MatchMode = "auto",
            WriteMode = "test_copy"
        });
        if (invalidPreview.CanWrite ||
            invalidPreview.SkippedItems.All(item => !item.Reason.StartsWith(BatchImageImportSkipReasons.InvalidName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Batch role face preview should block files without a numeric face id.");
        }

        var resizedPreview = service.Preview(testProject, new BatchRoleFaceImportRequest
        {
            SourceFiles = new[] { invalidSize },
            TargetRows = new[] { new BatchRoleFaceTargetRow(14, "Smoke Face #14", 14) },
            MatchMode = "auto",
            WriteMode = "test_copy"
        });
        if (!resizedPreview.CanWrite ||
            resizedPreview.Items.Single().SourceWidth != 80 ||
            resizedPreview.Items.Single().OutputWidth != 120 ||
            resizedPreview.Warnings.All(warning => !warning.Contains("转换为 120x120 PNG", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Batch role face preview should accept arbitrary image sizes and normalize them to 120x120 PNG.");
        }

        var bmpPreview = service.Preview(testProject, new BatchRoleFaceImportRequest
        {
            SourceFiles = new[] { bmpFace },
            TargetRows = new[] { new BatchRoleFaceTargetRow(15, "Smoke Face #15", 15) },
            MatchMode = "auto",
            WriteMode = "test_copy"
        });
        if (!bmpPreview.CanWrite ||
            bmpPreview.Items.Single().SourceKind != "BMP" ||
            bmpPreview.Items.Single().OutputKind != "PNG")
        {
            throw new InvalidOperationException("Batch role face preview should accept BMP and normalize it to PNG.");
        }
    }

    private static bool VerifyFacePngEntry(E5ImageReplaceService e5, string facePath, int imageNumber)
    {
        var bytes = e5.ReadEntryBytes(facePath, imageNumber);
        if (bytes.Length < 8 ||
            bytes[0] != 0x89 ||
            bytes[1] != (byte)'P' ||
            bytes[2] != (byte)'N' ||
            bytes[3] != (byte)'G')
        {
            return false;
        }

        using var memory = new MemoryStream(bytes, writable: false);
        using var image = Image.FromStream(memory, useEmbeddedColorManagement: false, validateImageData: true);
        return image.Width == 120 && image.Height == 120;
    }

    private static void CreateBatchRFolder(string root, string folderName, Color primary, Color secondary)
    {
        var folder = Path.Combine(root, folderName);
        Directory.CreateDirectory(folder);
        CreateSmokeBmp(Path.Combine(folder, "front.bmp"), 48, 1280, primary, secondary);
        CreateSmokeBmp(Path.Combine(folder, "back.bmp"), 48, 1280, secondary, primary);
    }

    private static void CreateBatchSFolder(string root, string folderName, Color primary, Color secondary)
    {
        var folder = Path.Combine(root, folderName);
        Directory.CreateDirectory(folder);
        CreateSmokeBmp(Path.Combine(folder, "mov.bmp"), 48, 528, primary, secondary);
        CreateSmokeBmp(Path.Combine(folder, "atk.bmp"), 64, 768, secondary, primary);
        CreateSmokeBmp(Path.Combine(folder, "spc.bmp"), 48, 240, primary, secondary);
    }

    private static void CreateSmokePng(string path, int width, int height, Color primary, Color secondary)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        using var primaryBrush = new SolidBrush(primary);
        using var secondaryBrush = new SolidBrush(secondary);
        graphics.Clear(Color.Transparent);
        graphics.FillRectangle(primaryBrush, 0, 0, width, Math.Max(1, height / 2));
        graphics.FillRectangle(secondaryBrush, 0, Math.Max(1, height / 2), width, Math.Max(1, height - height / 2));
        bitmap.Save(path, ImageFormat.Png);
    }
}
