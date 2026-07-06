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

        foreach (var fileName in new[] { "Ekd5.exe", "Data.e5", "Imsg.e5", "Star.e5", "Hexzmap.e5", "Pmapobj.e5", "Unit_mov.e5", "Unit_atk.e5", "Unit_spc.e5", "Face.e5", "Itemicon.dll", "Mgcicon.dll" })
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
        RunBatchJobSImageSmoke(testProject, smokeRoot);
        RunBatchRoleFaceSmoke(testProject, smokeRoot, e5);
        RunBatchItemIconSmoke(testProject, smokeRoot);
        RunBatchStrategyIconSmoke(testProject, smokeRoot);

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
            VerifyTrueColorEntry(e5, target, imageNumber, 48 * 1280);
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
        CreateBatchSFolder(Path.Combine(materialRoot, "S1", "turn2"), "", Color.FromArgb(255, 80, 210, 230), Color.FromArgb(255, 210, 80, 120));

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
            StageSlots = new[] { 1, 2, 3 },
            WriteMode = "test_copy"
        };
        var preview = service.Preview(testProject, request);
        if (!preview.CanWrite ||
            preview.Items.Count != 5 ||
            preview.TotalOperationCount != 15 ||
            preview.FilePreviews.Count != 3 ||
            !preview.Items.Where(item => item.SImageId == 1).Select(item => item.ImageNumber).SequenceEqual(new[] { 241, 242, 243 }) ||
            preview.Items.Single(item => item.SImageId == 1 && item.StageSlot == 2).MovSourcePath.Contains("turn2", StringComparison.OrdinalIgnoreCase) == false ||
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
            VerifyTrueColorEntry(e5, CharacterImageResourceService.ResolveGameFile(testProject, "Unit_mov.e5"), imageNumber, 48 * 528);
            VerifyTrueColorEntry(e5, CharacterImageResourceService.ResolveGameFile(testProject, "Unit_atk.e5"), imageNumber, 64 * 768);
            VerifyTrueColorEntry(e5, CharacterImageResourceService.ResolveGameFile(testProject, "Unit_spc.e5"), imageNumber, 48 * 240);
        }

        var beforeS1Turn1Mov = e5.ReadEntryBytes(CharacterImageResourceService.ResolveGameFile(testProject, "Unit_mov.e5"), 241);
        var beforeS1Turn3Mov = e5.ReadEntryBytes(CharacterImageResourceService.ResolveGameFile(testProject, "Unit_mov.e5"), 243);
        var secondTurnOnlyRoot = Path.Combine(smokeRoot, "_BatchSSecondTurnOnly");
        CreateBatchSFolder(secondTurnOnlyRoot, "S1", Color.FromArgb(255, 20, 210, 80), Color.FromArgb(255, 210, 60, 210));
        var secondTurnOnly = service.Replace(testProject, new BatchSImageReplaceRequest
        {
            MaterialRoot = secondTurnOnlyRoot,
            AllowedSImageUsages = new[] { new BatchSImageUsage(1, null, 1) },
            IncludeOnlySelectedOrFiltered = true,
            StageSlots = new[] { 2 },
            WriteMode = "test_copy"
        });
        if (secondTurnOnly.TotalOperationCount != 3 ||
            secondTurnOnly.Items.Count != 1 ||
            secondTurnOnly.Items[0].ImageNumber != 242)
        {
            throw new InvalidOperationException("Batch S image second-turn-only import did not write only Unit #242.");
        }

        AssertEntryBytesEqual(e5.ReadEntryBytes(CharacterImageResourceService.ResolveGameFile(testProject, "Unit_mov.e5"), 241), beforeS1Turn1Mov, "Batch S second-turn-only import changed Unit_mov.e5 #241.");
        AssertEntryBytesEqual(e5.ReadEntryBytes(CharacterImageResourceService.ResolveGameFile(testProject, "Unit_mov.e5"), 243), beforeS1Turn3Mov, "Batch S second-turn-only import changed Unit_mov.e5 #243.");
        VerifyTrueColorEntry(e5, CharacterImageResourceService.ResolveGameFile(testProject, "Unit_mov.e5"), 242, 48 * 528);

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

    private static void RunBatchJobSImageSmoke(CczProject testProject, string smokeRoot)
    {
        if (!HasFiles(testProject, "Unit_mov.e5", "Unit_atk.e5", "Unit_spc.e5"))
        {
            Console.WriteLine("BATCH_IMAGE_IMPORT_SMOKE skip job S: Unit_*.e5 missing.");
            return;
        }

        var materialRoot = Path.Combine(smokeRoot, "_BatchJobSMaterials");
        CreateBatchSFolder(materialRoot, "Job0", Color.FromArgb(255, 220, 80, 40), Color.FromArgb(255, 40, 150, 220));
        CreateBatchSFolder(materialRoot, "Job1", Color.FromArgb(255, 90, 180, 60), Color.FromArgb(255, 220, 180, 40));
        var service = new BatchJobSImageReplaceService();
        var preview = service.Preview(testProject, new BatchJobSImageReplaceRequest
        {
            MaterialRoot = materialRoot,
            AllowedJobIds = new HashSet<int> { 0, 1 },
            IncludeOnlySelectedOrFiltered = true,
            FactionSlots = new[] { 1 },
            WriteMode = "test_copy"
        });

        if (!preview.CanWrite ||
            preview.Items.Count != 2 ||
            preview.SkippedItems.Any(item => item.Reason.StartsWith(BatchImageImportSkipReasons.InvalidName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Batch job S image preview should accept Job0/Job1 folders.");
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
            orderedResult.DllResult.NewFileSha256.Equals(orderedResult.DllResult.OldFileSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Batch item icon DLL write did not create the expected write result and aggregate report.");
        }

        var magentaBmp = Path.Combine(smokeRoot, "item_icon_2.bmp");
        var blueBmp = Path.Combine(smokeRoot, "item_icon_3.bmp");
        var alphaPng = Path.Combine(smokeRoot, "item_icon_4.png");
        CreateKeyedIconBmp(magentaBmp, DllBitmapIconCodecService.E5MagentaKey, Color.FromArgb(255, 20, 220, 90));
        CreateKeyedIconBmp(blueBmp, DllBitmapIconCodecService.DllTransparentKey, Color.FromArgb(255, 220, 120, 20));
        CreateAlphaIconPng(alphaPng, 64, 64, Color.FromArgb(255, 80, 150, 230), Color.FromArgb(255, 220, 80, 160));
        var keyedFormatsBefore = CaptureDllIconVariantFormats(itemIconPath, 2, 3, 4);
        var keyedResult = service.Replace(testProject, new BatchItemIconImportRequest
        {
            SourceFiles = new[] { magentaBmp, blueBmp, alphaPng },
            TargetRows = new[]
            {
                new BatchItemIconTargetRow(102, "Smoke Magenta Key", 2),
                new BatchItemIconTargetRow(103, "Smoke Blue Key", 3),
                new BatchItemIconTargetRow(104, "Smoke Alpha Source", 4)
            },
            MatchMode = "auto",
            WriteMode = "test_copy"
        });
        if (keyedResult.DllResult == null || keyedResult.DllResult.Items.Count != 3)
        {
            throw new InvalidOperationException("Batch item icon keyed DLL write did not process all keyed sources.");
        }

        Assert65DllIconTransparency(itemIconPath, requireTransparentCorner: true, 2, 3);
        Assert65DllIconTransparency(itemIconPath, requireTransparentCorner: false, 4);
        Assert65DllIconFormatPreserved(itemIconPath, keyedFormatsBefore);
        Assert65DllIconLargeMaskMatchesSource(itemIconPath, 2, magentaBmp);
        Assert65DllIconSmallMaskUsesHardDownsample(itemIconPath, 2, magentaBmp);
        Assert65DllPreviewPrefersCanonicalLanguage(itemIconPath);

        var pairRoot = Path.Combine(smokeRoot, "_ItemIconPairRoot");
        Directory.CreateDirectory(pairRoot);
        var pairSmall = Path.Combine(pairRoot, "item_icon_5_small.bmp");
        var pairLarge = Path.Combine(pairRoot, "item_icon_5_large.bmp");
        CreateKeyedIconBmp(pairSmall, DllBitmapIconCodecService.DllTransparentKey, Color.FromArgb(255, 180, 40, 40), 16);
        CreateKeyedIconBmp(pairLarge, DllBitmapIconCodecService.DllTransparentKey, Color.FromArgb(255, 40, 40, 180), 32);
        var pairFormatsBefore = CaptureDllIconVariantFormats(itemIconPath, 5);
        var pairPreview = service.Preview(testProject, new BatchItemIconImportRequest
        {
            SourceRoot = pairRoot,
            TargetRows = new[] { new BatchItemIconTargetRow(105, "Smoke Pair", 5) },
            MatchMode = "auto",
            WriteMode = "test_copy"
        });
        if (!pairPreview.CanWrite ||
            pairPreview.Items.Count != 1 ||
            !Path.GetFileName(pairPreview.Items[0].SourcePath).Equals("item_icon_5_large.bmp", StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(pairPreview.Items[0].SmallSourcePath).Equals("item_icon_5_small.bmp", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Batch item icon pair import should prefer item_icon_N_large.bmp and attach item_icon_N_small.bmp.");
        }

        var pairResult = service.Replace(testProject, pairPreview.Request);
        if (pairResult.DllResult == null || pairResult.DllResult.Items.Count != 1)
        {
            throw new InvalidOperationException("Batch item icon pair import did not write one paired icon.");
        }

        Assert65DllIconFormatPreserved(itemIconPath, pairFormatsBefore);
        Assert65DllIconLargeMaskMatchesSource(itemIconPath, 5, pairLarge);
        Assert65DllIconSmallMaskMatchesSource(itemIconPath, 5, pairSmall);

        var exact8Bpp = Path.Combine(smokeRoot, "item_icon_6.bmp");
        var codec = new DllBitmapIconCodecService();
        var exactResourcesBefore = codec.ParseBitmapResources(itemIconPath);
        var exactPairBefore = codec.ResolveBitmapResourcePair(exactResourcesBefore, 6);
        var exactPalette = codec.ResolveStoragePalette(exactResourcesBefore, exactPairBefore);
        CreateExact8BppItemIconStorageBmp(exact8Bpp, 32, exactPalette);
        var expectedExactStorage = codec.ClassifyItemIconBmpImport(exact8Bpp, exactPairBefore, exactResourcesBefore);
        if (!expectedExactStorage.PreserveStorage || expectedExactStorage.StoragePair == null)
        {
            throw new InvalidOperationException("Exact 8bpp storage fixture was not recognized by the strict storage classifier.");
        }

        var exactResult = service.Replace(testProject, new BatchItemIconImportRequest
        {
            SourceFiles = new[] { exact8Bpp },
            TargetRows = new[] { new BatchItemIconTargetRow(106, "Smoke Exact 8bpp", 6) },
            MatchMode = "auto",
            WriteMode = "test_copy"
        });
        if (exactResult.DllResult == null ||
            exactResult.DllResult.Items.Count != 1 ||
            !exactResult.DllResult.ResourceFormat.Contains("8bpp indexed storage", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Exact 8bpp item icon import did not use the storage-preserving path.");
        }

        AssertExact8BppStorageImport(itemIconPath, 6, expectedExactStorage.StoragePair);

        var mismatch8Bpp = Path.Combine(smokeRoot, "item_icon_7.bmp");
        CreateExact8BppItemIconStorageBmp(mismatch8Bpp, 32, exactPalette, mutatePalette: true);
        var mismatchResourcesBefore = codec.ParseBitmapResources(itemIconPath);
        var mismatchPairBefore = codec.ResolveBitmapResourcePair(mismatchResourcesBefore, 7);
        var mismatchClassification = codec.ClassifyItemIconBmpImport(mismatch8Bpp, mismatchPairBefore, mismatchResourcesBefore);
        if (mismatchClassification.PreserveStorage || !mismatchClassification.Mode.Equals("palette-mismatch-fallback", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Palette-mismatched 8bpp BMP should fall back to visual import; mode={mismatchClassification.Mode}.");
        }

        var mismatchResult = service.Replace(testProject, new BatchItemIconImportRequest
        {
            SourceFiles = new[] { mismatch8Bpp },
            TargetRows = new[] { new BatchItemIconTargetRow(107, "Smoke Palette Mismatch 8bpp", 7) },
            MatchMode = "auto",
            WriteMode = "test_copy"
        });
        if (mismatchResult.DllResult == null ||
            !mismatchResult.DllResult.ResourceFormat.Contains("palette-mismatch-fallback", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Palette-mismatched 8bpp BMP import did not report the visual fallback path.");
        }

        AssertPaletteMismatch8BppFallback(itemIconPath, 7, mismatch8Bpp);

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

        var exportRoot = Path.Combine(smokeRoot, "_ItemIconExportRoot");
        Directory.CreateDirectory(exportRoot);
        var exportedIcon0 = Path.Combine(exportRoot, "item_icon_0.png");
        var exportedIcon1 = Path.Combine(exportRoot, "item_icon_1.png");
        CreateSmokePng(exportedIcon0, 32, 32, Color.FromArgb(255, 20, 90, 200), Color.FromArgb(255, 230, 210, 20));
        CreateSmokePng(exportedIcon1, 32, 32, Color.FromArgb(255, 200, 40, 90), Color.FromArgb(255, 20, 210, 120));
        var rootPreview = service.Preview(testProject, new BatchItemIconImportRequest
        {
            SourceRoot = exportRoot,
            TargetRows = new[]
            {
                new BatchItemIconTargetRow(101, "Smoke B", 1),
                new BatchItemIconTargetRow(100, "Smoke A", 0)
            },
            MatchMode = "auto",
            WriteMode = "test_copy"
        });
        if (!rootPreview.CanWrite ||
            rootPreview.Items.Count != 2 ||
            rootPreview.Items.Any(item => !Path.GetFileName(item.SourcePath).Equals($"item_icon_{item.IconIndex}.png", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Batch item icon SourceRoot import should match canonical item_icon_N files by icon id.");
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
                SourceRoot = source.SourceRoot,
                TargetRows = source.TargetRows,
                MatchMode = matchMode,
                WriteMode = source.WriteMode
            };
    }

    private static void RunBatchStrategyIconSmoke(CczProject testProject, string smokeRoot)
    {
        var strategyIconPath = CharacterImageResourceService.ResolveGameFile(testProject, "Mgcicon.dll");
        if (!File.Exists(strategyIconPath))
        {
            Console.WriteLine("BATCH_IMAGE_IMPORT_SMOKE skip strategy icons: Mgcicon.dll missing.");
            return;
        }

        var service = new BatchStrategyIconImportService();
        var exportRoot = Path.Combine(smokeRoot, "_StrategyIconExportRoot");
        Directory.CreateDirectory(exportRoot);
        var icon0 = Path.Combine(exportRoot, "strategy_icon_0.png");
        var icon1 = Path.Combine(exportRoot, "strategy_icon_1.png");
        CreateSmokePng(icon0, 32, 32, Color.FromArgb(255, 210, 70, 40), Color.FromArgb(255, 30, 150, 220));
        CreateSmokePng(icon1, 32, 32, Color.FromArgb(255, 80, 40, 200), Color.FromArgb(255, 220, 190, 40));

        var preview = service.Preview(testProject, new BatchStrategyIconImportRequest
        {
            SourceRoot = exportRoot,
            TargetRows = new[]
            {
                new BatchStrategyIconTargetRow(11, "Strategy B", 1),
                new BatchStrategyIconTargetRow(10, "Strategy A", 0)
            },
            MatchMode = "auto",
            WriteMode = "test_copy"
        });
        if (!preview.CanWrite ||
            preview.ResourceKind != "DLL" ||
            preview.Items.Count != 2 ||
            preview.Items.Any(item => !Path.GetFileName(item.SourcePath).Equals($"strategy_icon_{item.IconIndex}.png", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Batch strategy icon SourceRoot import should match canonical strategy_icon_N files by icon id.");
        }
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

        var exportRoot = Path.Combine(smokeRoot, "_FaceExportRoot");
        Directory.CreateDirectory(exportRoot);
        var exportedFace1 = Path.Combine(exportRoot, "face_1.bmp");
        var exportedFace2 = Path.Combine(exportRoot, "face_2.bmp");
        CreateSmokeBmp(exportedFace1, 120, 120, Color.FromArgb(255, 90, 200, 40), Color.FromArgb(255, 210, 40, 120));
        CreateSmokeBmp(exportedFace2, 120, 120, Color.FromArgb(255, 40, 120, 210), Color.FromArgb(255, 230, 210, 40));
        var rootPreview = service.Preview(testProject, new BatchRoleFaceImportRequest
        {
            SourceRoot = exportRoot,
            TargetRows = new[]
            {
                new BatchRoleFaceTargetRow(2, "Smoke Face B", 2),
                new BatchRoleFaceTargetRow(1, "Smoke Face A", 1)
            },
            MatchMode = "auto",
            WriteMode = "test_copy"
        });
        if (!rootPreview.CanWrite ||
            rootPreview.Items.Count != 2 ||
            rootPreview.Items.Any(item => !Path.GetFileName(item.SourcePath).Equals($"face_{item.FaceId}.bmp", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Batch role face SourceRoot import should match canonical face_N files by face id.");
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

    private static void CreateKeyedIconBmp(string path, Color key, Color body, int size = 32)
    {
        using var bitmap = new Bitmap(size, size, PixelFormat.Format24bppRgb);
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                bitmap.SetPixel(x, y, key);
            }
        }

        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(body))
        {
            var inset = Math.Max(1, size / 4);
            graphics.FillRectangle(brush, inset, inset, Math.Max(1, size / 2), Math.Max(1, size / 2));
        }

        bitmap.Save(path, ImageFormat.Bmp);
    }

    private static void CreateAlphaIconPng(string path, int width, int height, Color primary, Color secondary)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        using var primaryBrush = new SolidBrush(primary);
        using var secondaryBrush = new SolidBrush(secondary);
        graphics.Clear(Color.Transparent);
        graphics.FillRectangle(primaryBrush, width / 4, height / 4, width / 2, height / 4);
        graphics.FillRectangle(secondaryBrush, width / 4, height / 2, width / 2, height / 4);
        bitmap.Save(path, ImageFormat.Png);
    }

    private static void Assert65DllIconTransparency(string dllPath, bool requireTransparentCorner, params int[] iconIndexes)
    {
        var codec = new DllBitmapIconCodecService();
        var resources = codec.ParseBitmapResources(dllPath);
        foreach (var iconIndex in iconIndexes)
        {
            var pair = codec.ResolveBitmapResourcePair(resources, iconIndex);
            foreach (var resource in pair.AllVariants)
            {
                using var decoded = DllBitmapIconCodecService.DecodeDib(resource.DibBytes)
                                    ?? throw new InvalidOperationException($"DLL icon #{iconIndex} ID={resource.Id} failed to decode.");
                if (decoded.Bitmap.Width != resource.Width || decoded.Bitmap.Height != resource.Height)
                {
                    throw new InvalidOperationException($"DLL icon #{iconIndex} ID={resource.Id} decoded size mismatch.");
                }

                var corner = decoded.Bitmap.GetPixel(0, 0);
                if (requireTransparentCorner && corner.A != 0)
                {
                    throw new InvalidOperationException($"DLL icon #{iconIndex} ID={resource.Id} corner is not transparent after keyed import: {corner}.");
                }

                for (var y = 0; y < decoded.Bitmap.Height; y++)
                {
                    for (var x = 0; x < decoded.Bitmap.Width; x++)
                    {
                        var pixel = decoded.Bitmap.GetPixel(x, y);
                        if (pixel.A != 0 && DllBitmapIconCodecService.IsMagentaKey(pixel))
                        {
                            throw new InvalidOperationException($"DLL icon #{iconIndex} ID={resource.Id} still has visible magenta at {x},{y}: {pixel}.");
                        }
                    }
                }
            }
        }
    }

    private static Dictionary<string, int> CaptureDllIconVariantFormats(string dllPath, params int[] iconIndexes)
    {
        var codec = new DllBitmapIconCodecService();
        var resources = codec.ParseBitmapResources(dllPath);
        var formats = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var iconIndex in iconIndexes)
        {
            var pair = codec.ResolveBitmapResourcePair(resources, iconIndex);
            foreach (var resource in pair.AllVariants)
            {
                formats[$"{iconIndex}:{resource.Id}:{resource.LanguageId}"] = resource.BitCount;
            }
        }

        return formats;
    }

    private static void Assert65DllIconFormatPreserved(string dllPath, IReadOnlyDictionary<string, int> expected)
    {
        var codec = new DllBitmapIconCodecService();
        var resources = codec.ParseBitmapResources(dllPath);
        var actual = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var key in expected.Keys)
        {
            var parts = key.Split(':');
            var iconIndex = int.Parse(parts[0], CultureInfo.InvariantCulture);
            var pair = codec.ResolveBitmapResourcePair(resources, iconIndex);
            foreach (var resource in pair.AllVariants)
            {
                actual[$"{iconIndex}:{resource.Id}:{resource.LanguageId}"] = resource.BitCount;
            }
        }

        if (!expected.Keys.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(actual.Keys.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidOperationException("6.5 DLL icon import changed the set of language variants.");
        }

        foreach (var (key, bitCount) in expected)
        {
            if (!actual.TryGetValue(key, out var actualBitCount) || actualBitCount != bitCount)
            {
                throw new InvalidOperationException($"6.5 DLL icon import changed resource format for {key}: {bitCount}bpp -> {actualBitCount}bpp.");
            }
        }
    }

    private static void Assert65DllIconLargeMaskMatchesSource(string dllPath, int iconIndex, string sourcePath)
    {
        using var source = new Bitmap(sourcePath);
        var codec = new DllBitmapIconCodecService();
        var resources = codec.ParseBitmapResources(dllPath);
        var pair = codec.ResolveBitmapResourcePair(resources, iconIndex);
        foreach (var resource in pair.LargeVariants)
        {
            using var decoded = DllBitmapIconCodecService.DecodeDib(resource.DibBytes)
                                ?? throw new InvalidOperationException($"DLL icon #{iconIndex} large ID={resource.Id} failed to decode.");
            if (decoded.Bitmap.Width != source.Width || decoded.Bitmap.Height != source.Height)
            {
                throw new InvalidOperationException($"Exact 32x32 DLL icon large changed dimensions: source={source.Width}x{source.Height}, decoded={decoded.Bitmap.Width}x{decoded.Bitmap.Height}.");
            }

            for (var y = 0; y < source.Height; y++)
            {
                for (var x = 0; x < source.Width; x++)
                {
                    var expectedVisible = IsSmokeSourceVisible(source.GetPixel(x, y));
                    var actualVisible = decoded.Bitmap.GetPixel(x, y).A != 0;
                    if (expectedVisible != actualVisible)
                    {
                        throw new InvalidOperationException($"Exact 32x32 DLL icon large alpha mask changed at {x},{y}: expectedVisible={expectedVisible}, actualVisible={actualVisible}, ID={resource.Id} LANG={resource.LanguageId}.");
                    }
                }
            }
        }
    }

    private static void Assert65DllIconSmallMaskUsesHardDownsample(string dllPath, int iconIndex, string sourcePath)
    {
        using var source = new Bitmap(sourcePath);
        var codec = new DllBitmapIconCodecService();
        var resources = codec.ParseBitmapResources(dllPath);
        var pair = codec.ResolveBitmapResourcePair(resources, iconIndex);
        foreach (var resource in pair.SmallVariants)
        {
            using var decoded = DllBitmapIconCodecService.DecodeDib(resource.DibBytes)
                                ?? throw new InvalidOperationException($"DLL icon #{iconIndex} small ID={resource.Id} failed to decode.");
            if (decoded.Bitmap.Width != DllBitmapIconCodecService.SmallIconSize ||
                decoded.Bitmap.Height != DllBitmapIconCodecService.SmallIconSize)
            {
                throw new InvalidOperationException($"DLL icon #{iconIndex} small changed dimensions: {decoded.Bitmap.Width}x{decoded.Bitmap.Height}.");
            }

            for (var y = 0; y < decoded.Bitmap.Height; y++)
            {
                for (var x = 0; x < decoded.Bitmap.Width; x++)
                {
                    var expectedVisible = PickSmokeHardDownsampleVisible(source, x * 2, y * 2);
                    var actualVisible = decoded.Bitmap.GetPixel(x, y).A != 0;
                    if (expectedVisible != actualVisible)
                    {
                        throw new InvalidOperationException($"DLL icon small hard downsample mask mismatch at {x},{y}: expectedVisible={expectedVisible}, actualVisible={actualVisible}, ID={resource.Id} LANG={resource.LanguageId}.");
                    }
                }
            }
        }
    }

    private static void Assert65DllIconSmallMaskMatchesSource(string dllPath, int iconIndex, string sourcePath)
    {
        using var source = new Bitmap(sourcePath);
        var codec = new DllBitmapIconCodecService();
        var resources = codec.ParseBitmapResources(dllPath);
        var pair = codec.ResolveBitmapResourcePair(resources, iconIndex);
        foreach (var resource in pair.SmallVariants)
        {
            using var decoded = DllBitmapIconCodecService.DecodeDib(resource.DibBytes)
                                ?? throw new InvalidOperationException($"DLL icon #{iconIndex} small ID={resource.Id} failed to decode.");
            if (decoded.Bitmap.Width != source.Width || decoded.Bitmap.Height != source.Height)
            {
                throw new InvalidOperationException($"Exact 16x16 DLL icon small changed dimensions: source={source.Width}x{source.Height}, decoded={decoded.Bitmap.Width}x{decoded.Bitmap.Height}.");
            }

            for (var y = 0; y < source.Height; y++)
            {
                for (var x = 0; x < source.Width; x++)
                {
                    var expectedVisible = IsSmokeSourceVisible(source.GetPixel(x, y));
                    var actualVisible = decoded.Bitmap.GetPixel(x, y).A != 0;
                    if (expectedVisible != actualVisible)
                    {
                        throw new InvalidOperationException($"Exact 16x16 DLL icon small alpha mask changed at {x},{y}: expectedVisible={expectedVisible}, actualVisible={actualVisible}, ID={resource.Id} LANG={resource.LanguageId}.");
                    }
                }
            }
        }
    }

    private static void Assert65DllPreviewPrefersCanonicalLanguage(string dllPath)
    {
        var codec = new DllBitmapIconCodecService();
        var resources = codec.ParseBitmapResources(dllPath);
        var duplicateGroup = resources
            .GroupBy(resource => resource.Id)
            .FirstOrDefault(group => group.Any(resource => resource.LanguageId == 0) &&
                                     group.Any(resource => resource.LanguageId == DllBitmapIconCodecService.PreferredLanguageId));
        if (duplicateGroup == null) return;

        var selected = codec.SelectDisplayVariant(duplicateGroup);
        if (selected == null || selected.LanguageId != DllBitmapIconCodecService.PreferredLanguageId)
        {
            throw new InvalidOperationException($"DLL preview selected LANG={selected?.LanguageId} for duplicated ID={duplicateGroup.Key}; expected LANG={DllBitmapIconCodecService.PreferredLanguageId}.");
        }
    }

    private static void AssertExact8BppStorageImport(string dllPath, int iconIndex, DllIconStoragePair expected)
    {
        var codec = new DllBitmapIconCodecService();
        var resources = codec.ParseBitmapResources(dllPath);
        var pair = codec.ResolveBitmapResourcePair(resources, iconIndex);
        if (pair.SmallVariants.Count != 1 || pair.LargeVariants.Count != 1)
        {
            throw new InvalidOperationException($"Exact 8bpp item icon import should leave one canonical small/large variant; actual small={pair.SmallVariants.Count}, large={pair.LargeVariants.Count}.");
        }

        var small = pair.SmallVariants.Single();
        var large = pair.LargeVariants.Single();
        if (small.LanguageId != DllBitmapIconCodecService.PreferredLanguageId ||
            large.LanguageId != DllBitmapIconCodecService.PreferredLanguageId)
        {
            throw new InvalidOperationException($"Exact 8bpp item icon import should use canonical language {DllBitmapIconCodecService.PreferredLanguageId}; actual small={small.LanguageId}, large={large.LanguageId}.");
        }

        if (small.BitCount != 8 ||
            large.BitCount != 8 ||
            small.Width != DllBitmapIconCodecService.SmallIconSize ||
            small.Height != DllBitmapIconCodecService.SmallIconSize ||
            large.Width != DllBitmapIconCodecService.LargeIconSize ||
            large.Height != DllBitmapIconCodecService.LargeIconSize)
        {
            throw new InvalidOperationException($"Exact 8bpp item icon import wrote unexpected formats: small={small.Width}x{small.Height}/{small.BitCount}, large={large.Width}x{large.Height}/{large.BitCount}.");
        }

        if (!large.DibBytes.SequenceEqual(expected.Large.DibBytes))
        {
            throw new InvalidOperationException("Exact 8bpp item icon import changed the large BMP DIB bytes.");
        }

        if (!small.DibBytes.SequenceEqual(expected.Small.DibBytes))
        {
            throw new InvalidOperationException("Exact 8bpp item icon import did not generate the expected indexed hard-downsampled small DIB.");
        }
    }

    private static void AssertPaletteMismatch8BppFallback(string dllPath, int iconIndex, string sourcePath)
    {
        var sourceDib = File.ReadAllBytes(sourcePath).Skip(14).ToArray();
        var codec = new DllBitmapIconCodecService();
        var resources = codec.ParseBitmapResources(dllPath);
        var pair = codec.ResolveBitmapResourcePair(resources, iconIndex);
        if (pair.LargeVariants.Any(resource => resource.DibBytes.SequenceEqual(sourceDib)))
        {
            throw new InvalidOperationException("Palette-mismatched 8bpp BMP was written as raw storage DIB instead of using visual normalization.");
        }

        foreach (var resource in pair.AllVariants)
        {
            using var decoded = DllBitmapIconCodecService.DecodeDib(resource.DibBytes)
                                ?? throw new InvalidOperationException($"Fallback 8bpp BMP import wrote undecodable resource ID={resource.Id}.");
            if (decoded.Bitmap.Width is not (16 or 32) ||
                decoded.Bitmap.Height is not (16 or 32))
            {
                throw new InvalidOperationException($"Fallback 8bpp BMP import wrote unexpected size {decoded.Bitmap.Width}x{decoded.Bitmap.Height}.");
            }
        }
    }

    private static void CreateExact8BppItemIconStorageBmp(
        string path,
        int size,
        IReadOnlyList<Color> palette,
        bool mutatePalette = false)
    {
        if (palette.Count < 256)
        {
            throw new InvalidOperationException("Exact 8bpp storage fixture requires a 256-color DLL palette.");
        }

        var paletteEntries = 256;
        var stride = ((size * 8 + 31) / 32) * 4;
        var imageSize = stride * size;
        var dib = new byte[40 + paletteEntries * 4 + imageSize];
        BitConverter.GetBytes(40).CopyTo(dib, 0);
        BitConverter.GetBytes(size).CopyTo(dib, 4);
        BitConverter.GetBytes(size).CopyTo(dib, 8);
        BitConverter.GetBytes((ushort)1).CopyTo(dib, 12);
        BitConverter.GetBytes((ushort)8).CopyTo(dib, 14);
        BitConverter.GetBytes(0).CopyTo(dib, 16);
        BitConverter.GetBytes(imageSize).CopyTo(dib, 20);
        BitConverter.GetBytes(0).CopyTo(dib, 24);
        BitConverter.GetBytes(0).CopyTo(dib, 28);
        BitConverter.GetBytes(0).CopyTo(dib, 32);
        BitConverter.GetBytes(0).CopyTo(dib, 36);

        for (var i = 0; i < paletteEntries; i++)
        {
            var color = palette[i];
            if (mutatePalette && i == 6)
            {
                color = Color.FromArgb(255, (color.R + 17) % 256, color.G, color.B);
            }

            WriteBmpPaletteColor(dib, i, Color.FromArgb(255, color.R, color.G, color.B));
        }

        for (var y = 0; y < size; y++)
        {
            var storedY = size - 1 - y;
            var rowOffset = 40 + paletteEntries * 4 + storedY * stride;
            for (var x = 0; x < size; x++)
            {
                byte index = 0;
                if (x >= 4 && x <= 27 && y >= 4 && y <= 27)
                {
                    index = (byte)(1 + ((x / 4 + y / 4) % 6));
                }

                if (x == y && x >= 6 && x <= 25)
                {
                    index = 6;
                }

                dib[rowOffset + x] = index;
            }
        }

        var bmp = new byte[14 + dib.Length];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BitConverter.GetBytes(bmp.Length).CopyTo(bmp, 2);
        BitConverter.GetBytes(14 + 40 + paletteEntries * 4).CopyTo(bmp, 10);
        Buffer.BlockCopy(dib, 0, bmp, 14, dib.Length);
        File.WriteAllBytes(path, bmp);
    }

    private static void WriteBmpPaletteColor(byte[] dib, int index, Color color)
    {
        var offset = 40 + index * 4;
        dib[offset] = color.B;
        dib[offset + 1] = color.G;
        dib[offset + 2] = color.R;
        dib[offset + 3] = 0;
    }

    private static bool PickSmokeHardDownsampleVisible(Bitmap source, int left, int top)
    {
        var priority = new[] { (X: 1, Y: 1), (X: 1, Y: 0), (X: 0, Y: 1), (X: 0, Y: 0) };
        foreach (var (offsetX, offsetY) in priority)
        {
            var x = Math.Min(source.Width - 1, left + offsetX);
            var y = Math.Min(source.Height - 1, top + offsetY);
            if (IsSmokeSourceVisible(source.GetPixel(x, y))) return true;
        }

        return false;
    }

    private static bool IsSmokeSourceVisible(Color pixel)
        => pixel.A != 0 &&
           !DllBitmapIconCodecService.IsDllBlueKey(pixel) &&
           !DllBitmapIconCodecService.IsMagentaKey(pixel);

}
