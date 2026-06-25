using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using CCZModStudio;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

internal partial class Program
{
    static void RunE5ImageReplaceSmoke(CczProject project)
    {
        var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "E5ImageReplaceSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(smokeRoot);
        foreach (var coreFile in new[] { "Ekd5.exe", "Data.e5", "Imsg.e5", "Star.e5", "Hexzmap.e5" })
        {
            var source = Path.Combine(project.GameRoot, coreFile);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(smokeRoot, coreFile), overwrite: false);
            }
        }
    
        var unitMovSource = CharacterImageResourceService.ResolveGameFile(project, "Unit_mov.e5");
        if (!File.Exists(unitMovSource))
        {
            throw new FileNotFoundException("E5 图片条目替换烟测需要 Unit_mov.e5。", unitMovSource);
        }
    
        var unitMovTarget = Path.Combine(smokeRoot, "Unit_mov.e5");
        File.Copy(unitMovSource, unitMovTarget, overwrite: false);
        var smokeE5Dir = Path.Combine(smokeRoot, "E5");
        Directory.CreateDirectory(smokeE5Dir);
        foreach (var e5File in new[] { "Face.e5", "Effarea.e5", "Hitarea.e5", "Logo.e5", "Mmap.e5", "U_select.e5", "Weather.e5", "Gate.e5", "Mark.e5", "Meff.e5" })
        {
            var source = CharacterImageResourceService.ResolveGameFile(project, e5File);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(smokeE5Dir, e5File), overwrite: false);
            }
        }

        var mcallSource = CharacterImageResourceService.ResolveGameFile(project, "Mcall00.e5");
        var mcallTarget = Path.Combine(smokeE5Dir, "Mcall00.e5");
        if (File.Exists(mcallSource))
        {
            File.Copy(mcallSource, mcallTarget, overwrite: false);
        }
        else
        {
            File.WriteAllBytes(mcallTarget, BuildSmokeLsBytes());
        }
    
        foreach (var rootE5File in new[] { "Pmapobj.e5", "Unit_atk.e5", "Unit_spc.e5" })
        {
            var source = CharacterImageResourceService.ResolveGameFile(project, rootE5File);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(smokeRoot, rootE5File), overwrite: false);
            }
        }
    
        foreach (var iconDll in new[] { "Itemicon.dll", "Mgcicon.dll", "Cmdicon.dll" })
        {
            var source = Path.Combine(project.GameRoot, iconDll);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(smokeRoot, iconDll), overwrite: false);
            }
        }
    
        File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=E5 image replace smoke\r\n");
    
        var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
        AssertDibPreviewRowDirection();
        var service = new E5ImageReplaceService();
        var catalogService = new ImageResourceCatalogService();
        var catalog = catalogService.BuildCatalog(testProject);
        foreach (var required in new[] { "Face.e5", "Pmapobj.e5", "Unit_mov.e5", "Hitarea.e5", "Effarea.e5", "Logo.e5", "Mmap.e5", "U_select.e5", "Gate.e5", "Weather.e5" })
        {
            var item = catalog.FirstOrDefault(x => x.FileName.Equals(required, StringComparison.OrdinalIgnoreCase));
            if (item == null || !item.Exists || item.EntryCount <= 0)
            {
                throw new InvalidOperationException($"图片资源目录烟测未能读取 {required} 的 110 图片索引。");
            }
        }
    
        var mark = catalog.FirstOrDefault(x => x.FileName.Equals("Mark.e5", StringComparison.OrdinalIgnoreCase));
        if (mark == null || !mark.Exists || mark.SupportsE5Index || mark.CanReplace)
        {
            throw new InvalidOperationException("图片资源目录烟测应将 Mark.e5 标记为非 110 索引资源且不可替换。");
        }

        var mcall = catalog.FirstOrDefault(x => x.Key.Equals("Mcall", StringComparison.OrdinalIgnoreCase));
        if (mcall == null ||
            !mcall.Exists ||
            mcall.SupportsE5Index ||
            mcall.SupportsPreview ||
            mcall.CanReplace ||
            !mcall.ResourceFormat.Equals("LS状态", StringComparison.OrdinalIgnoreCase) ||
            !mcall.Status.Contains("LS 状态", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("图片资源目录烟测应将 Mcall*.e5 标记为只读 LS 状态资源，不能作为 110 图片索引预览或替换。");
        }
    
        var face = catalog.Single(x => x.FileName.Equals("Face.e5", StringComparison.OrdinalIgnoreCase));
        var faceEntry = catalogService.ReadEntries(face).FirstOrDefault(x => x.ImageNumber == 1);
        if (faceEntry == null)
        {
            throw new InvalidOperationException("图片资源目录烟测未能读取 Face.e5 #1。");
        }
    
        using (var facePreview = catalogService.RenderEntryPreview(testProject, faceEntry))
        {
            if (facePreview == null || facePreview.Width <= 0 || facePreview.Height <= 0)
            {
                throw new InvalidOperationException("图片资源目录烟测未能渲染 Face.e5 #1 预览。");
            }
        }
    
        var itemIcon = catalog.Single(x => x.FileName.Equals("Itemicon.dll", StringComparison.OrdinalIgnoreCase));
        var mgcIcon = catalog.Single(x => x.FileName.Equals("Mgcicon.dll", StringComparison.OrdinalIgnoreCase));
        var cmdIcon = catalog.Single(x => x.FileName.Equals("Cmdicon.dll", StringComparison.OrdinalIgnoreCase));
        if (!itemIcon.Exists || itemIcon.EntryCount <= 0 || !itemIcon.CanReplace || !itemIcon.SupportsPreview)
        {
            throw new InvalidOperationException("图片资源目录烟测未能把 Itemicon.dll 对齐为可替换图标资源。");
        }
    
        if (!mgcIcon.Exists || mgcIcon.EntryCount <= 0 || !mgcIcon.CanReplace || !mgcIcon.SupportsPreview ||
            !cmdIcon.Exists || cmdIcon.EntryCount <= 0 || !cmdIcon.CanReplace || !cmdIcon.SupportsPreview)
        {
            throw new InvalidOperationException("图片资源目录烟测未能把策略/命令 DLL 图标对齐为可替换资源。");
        }
    
        var itemIconEntry = catalogService.ReadEntries(itemIcon).FirstOrDefault(x => x.ImageNumber == 0);
        if (itemIconEntry == null || !itemIconEntry.Kind.Equals("DLL图标", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("图片资源目录烟测未能读取 Itemicon.dll #0 图标条目。");
        }
    
        using (var iconPreview = catalogService.RenderEntryPreview(testProject, itemIconEntry))
        {
            if (iconPreview == null || iconPreview.Width <= 0 || iconPreview.Height <= 0)
            {
                throw new InvalidOperationException("图片资源目录烟测未能渲染 Itemicon.dll #0 图标预览。");
            }
        }
    
        Console.WriteLine($"IMAGE_RESOURCE_CATALOG files={catalog.Count} face={face.EntryCount} markIndex={mark.SupportsE5Index} hit={catalog.First(x => x.FileName.Equals("Hitarea.e5", StringComparison.OrdinalIgnoreCase)).EntryCount} eff={catalog.First(x => x.FileName.Equals("Effarea.e5", StringComparison.OrdinalIgnoreCase)).EntryCount} itemIcons={itemIcon.EntryCount} mgcIcons={mgcIcon.EntryCount} cmdIcons={cmdIcon.EntryCount}");
    
        var entries = service.ReadIndex(unitMovTarget);
        if (entries.Count < 554)
        {
            throw new InvalidOperationException($"Unit_mov.e5 图片索引表条目不足，无法替换 #554：entries={entries.Count}。");
        }
    
        var replacementPng = Path.Combine(smokeRoot, "Smoke_E5_Replacement.png");
        using (var bitmap = new System.Drawing.Bitmap(12, 12, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.Clear(System.Drawing.Color.Transparent);
            using var redBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 220, 32, 64));
            using var blueBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 32, 96, 220));
            graphics.FillRectangle(redBrush, 1, 1, 5, 10);
            graphics.FillRectangle(blueBrush, 6, 1, 5, 10);
            bitmap.Save(replacementPng, System.Drawing.Imaging.ImageFormat.Png);
        }
    
        var preview = service.PreviewReplacement(testProject, unitMovTarget, 554, replacementPng);
        if (preview.ImageNumber != 554 ||
            preview.OldSizeBytes <= 0 ||
            preview.NewKind != "PNG" ||
            preview.SourceWidth != 12 ||
            preview.SourceHeight != 12)
        {
            throw new InvalidOperationException("E5 图片条目替换预览断言失败。");
        }
    
        var result = service.Replace(testProject, unitMovTarget, 554, replacementPng);
        if (!File.Exists(result.BackupPath) ||
            !File.Exists(result.ReportPath) ||
            !File.Exists(result.ReportJsonPath) ||
            !File.ReadAllText(result.ReportJsonPath).Contains("E5图片条目替换", StringComparison.Ordinal) ||
            !service.ReadEntryBytes(unitMovTarget, 554).SequenceEqual(File.ReadAllBytes(replacementPng)))
        {
            throw new InvalidOperationException("E5 图片条目替换写入、复读或报告断言失败。");
        }
    
        Console.WriteLine($"E5_IMAGE_REPLACE_SMOKE OK target={Path.GetFileName(result.TargetPath)} image={result.ImageNumber} kind={result.OldKind}->{result.NewKind} size={result.OldSizeBytes}->{result.NewSizeBytes} backup={Path.GetFileName(result.BackupPath)} json={Path.GetFileName(result.ReportJsonPath)}");
    
        var batchPng1 = Path.Combine(smokeRoot, "Batch_552.png");
        var batchPng2 = Path.Combine(smokeRoot, "Batch_553.png");
        using (var bitmap = new System.Drawing.Bitmap(10, 10, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.Clear(System.Drawing.Color.Transparent);
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 20, 180, 80));
            graphics.FillEllipse(brush, 1, 1, 8, 8);
            bitmap.Save(batchPng1, System.Drawing.Imaging.ImageFormat.Png);
        }
        using (var bitmap = new System.Drawing.Bitmap(11, 11, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.Clear(System.Drawing.Color.Transparent);
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 180, 120, 20));
            graphics.FillRectangle(brush, 1, 1, 9, 9);
            bitmap.Save(batchPng2, System.Drawing.Imaging.ImageFormat.Png);
        }
    
        var batchPreview = service.PreviewBatchReplacement(testProject, unitMovTarget, new[]
        {
            new E5ImageBatchReplaceRequest { ImageNumber = 552, SourcePath = batchPng1, OperationKind = "烟测批量导入" },
            new E5ImageBatchReplaceRequest { ImageNumber = 553, SourcePath = batchPng2, OperationKind = "烟测批量导入" }
        });
        if (batchPreview.OperationCount != 2 || batchPreview.Operations.Any(x => x.NewKind != "PNG"))
        {
            throw new InvalidOperationException("E5 图片批量替换预览断言失败。");
        }
    
        var batchResult = service.ReplaceBatch(testProject, unitMovTarget, new[]
        {
            new E5ImageBatchReplaceRequest { ImageNumber = 552, SourcePath = batchPng1, OperationKind = "烟测批量导入" },
            new E5ImageBatchReplaceRequest { ImageNumber = 553, SourcePath = batchPng2, OperationKind = "烟测批量导入" }
        });
        if (!File.Exists(batchResult.BackupPath) ||
            !File.Exists(batchResult.ReportJsonPath) ||
            !service.ReadEntryBytes(unitMovTarget, 552).SequenceEqual(File.ReadAllBytes(batchPng1)) ||
            !service.ReadEntryBytes(unitMovTarget, 553).SequenceEqual(File.ReadAllBytes(batchPng2)))
        {
            throw new InvalidOperationException("E5 图片批量替换写入、复读或报告断言失败。");
        }
    
        byte[] clearPng;
        using (var bitmap = new System.Drawing.Bitmap(1, 1, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            bitmap.SetPixel(0, 0, System.Drawing.Color.Transparent);
            using var memory = new MemoryStream();
            bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
            clearPng = memory.ToArray();
        }
    
        var clearResult = service.ReplaceBatch(testProject, unitMovTarget, new[]
        {
            new E5ImageBatchReplaceRequest { ImageNumber = 551, SourceBytes = clearPng, SourceLabel = "<1x1透明PNG>", OperationKind = "烟测清空" }
        });
        if (!service.ReadEntryBytes(unitMovTarget, 551).SequenceEqual(clearPng) ||
            !File.ReadAllText(clearResult.ReportJsonPath).Contains("E5图片条目批量替换", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("E5 图片批量清空断言失败。");
        }
    
        var strategyAnimationService = new StrategyAnimationPreviewService();
        var smallMeffPreview = strategyAnimationService.BuildAnimatedPreview(testProject, StrategyAnimationPreviewKind.SmallMeff, 0);
        if (smallMeffPreview.ImageNumber != 1 ||
            smallMeffPreview.Frames.Count <= 1 ||
            smallMeffPreview.FrameIntervalMs <= 0 ||
            !smallMeffPreview.RenderMode.Equals("Meff 8bpp 动画", StringComparison.Ordinal) ||
            !smallMeffPreview.Message.Contains("64x64", StringComparison.Ordinal) ||
            !smallMeffPreview.Message.Contains("Meff.e5 图号 #1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("策略小动画 Meff 映射断言失败：字段值 0 应定位到 Meff.e5 #1 并生成结构化多帧预览。");
        }

        var extraPlaneMeffPreview = strategyAnimationService.BuildAnimatedPreview(testProject, StrategyAnimationPreviewKind.SmallMeff, 11);
        if (extraPlaneMeffPreview.ImageNumber != 12 ||
            extraPlaneMeffPreview.Frames.Count <= 20 ||
            !extraPlaneMeffPreview.Message.Contains("全部平面", StringComparison.Ordinal) ||
            !extraPlaneMeffPreview.RenderMode.Equals("Meff 8bpp 动画", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("策略小动画 Meff #12 应识别额外完整帧平面并按全部平面预览。");
        }

        var noSmallAnimationPreview = strategyAnimationService.BuildAnimatedPreview(testProject, StrategyAnimationPreviewKind.SmallMeff, 255);
        if (noSmallAnimationPreview.Frames.Count != 0 ||
            !noSmallAnimationPreview.Message.Contains("255", StringComparison.Ordinal) ||
            !noSmallAnimationPreview.RenderMode.Contains("无动画", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("策略小动画 255 无动画预览断言失败。");
        }

        var noBigAnimationPreview = strategyAnimationService.BuildAnimatedPreview(testProject, StrategyAnimationPreviewKind.BigMcall, 0);
        if (noBigAnimationPreview.Frames.Count != 0 ||
            Path.GetFileName(noBigAnimationPreview.SourcePath).Equals("Meff.e5", StringComparison.OrdinalIgnoreCase) ||
            !noBigAnimationPreview.RenderMode.Contains("无 Mcall 大动画", StringComparison.Ordinal) ||
            !noBigAnimationPreview.Message.Contains("字段值 0", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("策略大动画字段值 0 应按无 Mcall 大动画处理，不能回退到 Meff.e5 预览。");
        }

        var bigMcallPreview = strategyAnimationService.BuildAnimatedPreview(testProject, StrategyAnimationPreviewKind.BigMcall, 100);
        if (bigMcallPreview.Frames.Count <= 1 ||
            !Path.GetFileName(bigMcallPreview.SourcePath).Equals("Mcall00.e5", StringComparison.OrdinalIgnoreCase) ||
            !bigMcallPreview.RenderMode.Equals("Mcall 8bpp 动画", StringComparison.Ordinal) ||
            !bigMcallPreview.Message.Contains("Mcall 编号 0", StringComparison.Ordinal) ||
            !bigMcallPreview.Message.Contains("240x240", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("策略大动画字段值 100 应定位到 Mcall00.e5 并生成结构化多帧预览。");
        }

        var missingMcallPreview = strategyAnimationService.BuildAnimatedPreview(testProject, StrategyAnimationPreviewKind.BigMcall, 199);
        if (missingMcallPreview.Frames.Count != 0 ||
            !missingMcallPreview.RenderMode.Contains("Mcall 缺失", StringComparison.Ordinal) ||
            !missingMcallPreview.Message.Contains("未找到", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("策略大动画缺失 Mcall 文件时应返回清晰缺失状态，不能抛异常。");
        }

        var outOfRangeValue = Math.Max(250, smallMeffPreview.EntryCount);
        var outOfRangePreview = strategyAnimationService.BuildAnimatedPreview(testProject, StrategyAnimationPreviewKind.SmallMeff, outOfRangeValue);
        if (outOfRangePreview.Frames.Count != 0 ||
            !outOfRangePreview.Message.Contains("没有匹配", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("策略动画越界预览断言失败。");
        }

        DisposeFrames(smallMeffPreview);
        DisposeFrames(extraPlaneMeffPreview);
        DisposeFrames(noSmallAnimationPreview);
        DisposeFrames(noBigAnimationPreview);
        DisposeFrames(bigMcallPreview);
        DisposeFrames(missingMcallPreview);
        DisposeFrames(outOfRangePreview);
    
        var iconReplaceService = new IconResourceReplaceService();
        var iconReplacement = Path.Combine(smokeRoot, "Smoke_Icon_Replacement.png");
        using (var bitmap = new System.Drawing.Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.Clear(System.Drawing.Color.Transparent);
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 60, 150, 230));
            graphics.FillEllipse(brush, 4, 4, 24, 24);
            bitmap.Save(iconReplacement, System.Drawing.Imaging.ImageFormat.Png);
        }
    
        var itemIconTarget = Path.Combine(smokeRoot, "Itemicon.dll");
        var iconPreviewBefore = iconReplaceService.PreviewReplaceBitmapIcon(testProject, itemIconTarget, 0, iconReplacement);
        if (iconPreviewBefore.ResourceIds.Count == 0)
        {
            throw new InvalidOperationException("DLL 图标替换预览未定位到 RT_BITMAP 资源 ID。");
        }
    
        var iconReplaceResult = iconReplaceService.ReplaceBitmapIcon(testProject, itemIconTarget, 0, iconReplacement);
        if (!File.Exists(iconReplaceResult.BackupPath) ||
            !File.Exists(iconReplaceResult.ReportJsonPath) ||
            iconReplaceResult.NewFileSha256.Equals(iconReplaceResult.OldFileSha256, StringComparison.OrdinalIgnoreCase) ||
            !iconReplaceResult.ReadbackVerified ||
            iconReplaceResult.ReadbackItems.Count == 0)
        {
            throw new InvalidOperationException("DLL 图标替换写入或报告断言失败。");
        }
    
        var iconClearResult = iconReplaceService.ClearBitmapIcon(testProject, itemIconTarget, 1);
        if (!File.Exists(iconClearResult.BackupPath) ||
            !File.Exists(iconClearResult.ReportJsonPath) ||
            iconClearResult.NewFileSha256.Equals(iconClearResult.OldFileSha256, StringComparison.OrdinalIgnoreCase) ||
            !iconClearResult.ReadbackVerified ||
            iconClearResult.ReadbackItems.Count == 0)
        {
            throw new InvalidOperationException("DLL 图标清空写入或报告断言失败。");
        }

        var mgcIconTarget = Path.Combine(smokeRoot, "Mgcicon.dll");
        var mgcBatchPng1 = Path.Combine(smokeRoot, "Mgc_Batch_0.png");
        var mgcBatchPng2 = Path.Combine(smokeRoot, "Mgc_Batch_1.png");
        using (var bitmap = new System.Drawing.Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            using var topBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 230, 70, 80));
            using var bottomBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 40, 190, 80));
            graphics.FillRectangle(topBrush, 0, 0, 32, 16);
            graphics.FillRectangle(bottomBrush, 0, 16, 32, 16);
            bitmap.Save(mgcBatchPng1, System.Drawing.Imaging.ImageFormat.Png);
        }
        using (var bitmap = new System.Drawing.Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.Clear(System.Drawing.Color.Transparent);
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 80, 210, 120));
            graphics.FillEllipse(brush, 4, 4, 24, 24);
            bitmap.Save(mgcBatchPng2, System.Drawing.Imaging.ImageFormat.Png);
        }

        var backupRoot = Path.Combine(smokeRoot, "_CCZModStudio_Backups");
        var mgcBackupsBefore = Directory.Exists(backupRoot)
            ? Directory.GetFiles(backupRoot, "*Mgcicon.dll*", SearchOption.TopDirectoryOnly).Length
            : 0;
        var mgcBatchRequests = new[]
        {
            new IconResourceBatchReplaceRequest { IconIndex = 0, SourcePath = mgcBatchPng1, OperationKind = "烟测策略图标批量导入" },
            new IconResourceBatchReplaceRequest { IconIndex = 1, SourcePath = mgcBatchPng2, OperationKind = "烟测策略图标批量导入" }
        };
        var mgcBatchPreview = iconReplaceService.PreviewReplaceBitmapIcons(testProject, mgcIconTarget, mgcBatchRequests);
        if (mgcBatchPreview.Items.Count != 2 ||
            mgcBatchPreview.Items.Any(item => item.ResourceIds.Count == 0) ||
            mgcBatchPreview.OldFileSizeBytes <= 0)
        {
            throw new InvalidOperationException("DLL 策略图标批量替换预览断言失败。");
        }

        var mgcBatchResult = iconReplaceService.ReplaceBitmapIcons(testProject, mgcIconTarget, mgcBatchRequests);
        var mgcBackupsAfter = Directory.GetFiles(backupRoot, "*Mgcicon.dll*", SearchOption.TopDirectoryOnly).Length;
        if (mgcBatchResult.Items.Count != 2 ||
            !File.Exists(mgcBatchResult.BackupPath) ||
            !File.Exists(mgcBatchResult.ReportJsonPath) ||
            !File.ReadAllText(mgcBatchResult.ReportJsonPath).Contains("DLL图标RT_BITMAP批量替换", StringComparison.Ordinal) ||
            mgcBatchResult.NewFileSha256.Equals(mgcBatchResult.OldFileSha256, StringComparison.OrdinalIgnoreCase) ||
            mgcBackupsAfter != mgcBackupsBefore + 1 ||
            !mgcBatchResult.ReadbackVerified ||
            mgcBatchResult.ReadbackItems.Count == 0)
        {
            throw new InvalidOperationException("DLL 策略图标批量替换写入、单次备份或报告断言失败。");
        }

        AssertDllBitmapTopFirstRows(mgcIconTarget, mgcBatchResult.Items[0].ResourceIds, Color.FromArgb(255, 230, 70, 80), Color.FromArgb(255, 40, 190, 80));
        var mgcPreviewAfterWrite = new ItemIconPreviewService().BuildPreview(testProject, 0, "Mgcicon.dll", "策略图标", 96);
        if (mgcPreviewAfterWrite.Bitmap == null ||
            !mgcPreviewAfterWrite.RenderMode.Equals("DLL RT_BITMAP", StringComparison.Ordinal) ||
            mgcPreviewAfterWrite.LargeBitmap == null ||
            mgcPreviewAfterWrite.SmallBitmap == null)
        {
            throw new InvalidOperationException("DLL 策略图标写入后预览未生成。");
        }

        using (mgcPreviewAfterWrite.Bitmap)
        using (mgcPreviewAfterWrite.NativeBitmap)
        using (mgcPreviewAfterWrite.LargeBitmap)
        using (mgcPreviewAfterWrite.SmallBitmap)
        {
            AssertBitmapPreviewTopBottom(
                mgcPreviewAfterWrite.LargeBitmap,
                Color.FromArgb(255, 230, 70, 80),
                Color.FromArgb(255, 40, 190, 80),
                "DLL 策略图标写入后 BuildPreview");
        }
    
        Console.WriteLine($"E5_IMAGE_BATCH_AND_DLL_ICON_SMOKE OK batch={batchResult.OperationCount} clear={clearResult.OperationCount} strategyPreview=Meff#{smallMeffPreview.ImageNumber}/{smallMeffPreview.RenderMode},Mcall={bigMcallPreview.RenderMode} iconIds={string.Join(',', iconReplaceResult.ResourceIds)} mgcBatch={mgcBatchResult.Items.Count} mgcBackup={Path.GetFileName(mgcBatchResult.BackupPath)} iconBackup={Path.GetFileName(iconReplaceResult.BackupPath)}");
    }

    static void AssertDibPreviewRowDirection()
    {
        var top = Color.FromArgb(255, 230, 70, 80);
        var bottom = Color.FromArgb(255, 40, 190, 80);
        using var standard8Bpp = ItemIconPreviewService.RenderDibForSmoke(BuildSmoke8BppStandardBottomUpDib(top, bottom), 96)
            ?? throw new InvalidOperationException("8bpp 标准 bottom-up DIB 预览未生成。");
        AssertBitmapPreviewTopBottom(standard8Bpp, top, bottom, "8bpp 标准 bottom-up DIB");

        using var ccz32Bpp = ItemIconPreviewService.RenderDibForSmoke(BuildSmoke32BppCczTopFirstDib(top, bottom), 96)
            ?? throw new InvalidOperationException("32bpp CCZ top-first DIB 预览未生成。");
        AssertBitmapPreviewTopBottom(ccz32Bpp, top, bottom, "32bpp CCZ top-first DIB");
    }

    static byte[] BuildSmoke8BppStandardBottomUpDib(Color top, Color bottom)
    {
        const int width = 4;
        const int height = 4;
        const int headerSize = 40;
        const int paletteEntries = 256;
        const int stride = 4;
        var dib = new byte[headerSize + paletteEntries * 4 + stride * height];
        BitConverter.GetBytes(headerSize).CopyTo(dib, 0);
        BitConverter.GetBytes(width).CopyTo(dib, 4);
        BitConverter.GetBytes(height).CopyTo(dib, 8);
        BitConverter.GetBytes((ushort)1).CopyTo(dib, 12);
        BitConverter.GetBytes((ushort)8).CopyTo(dib, 14);
        BitConverter.GetBytes(paletteEntries).CopyTo(dib, 32);
        WritePaletteColor(dib, 1, top);
        WritePaletteColor(dib, 2, bottom);

        var pixelOffset = headerSize + paletteEntries * 4;
        for (var storedY = 0; storedY < height; storedY++)
        {
            var visualY = height - 1 - storedY;
            var colorIndex = visualY < height / 2 ? (byte)1 : (byte)2;
            for (var x = 0; x < width; x++)
            {
                dib[pixelOffset + storedY * stride + x] = colorIndex;
            }
        }

        return dib;
    }

    static byte[] BuildSmoke32BppCczTopFirstDib(Color top, Color bottom)
    {
        const int width = 4;
        const int height = 4;
        const int headerSize = 40;
        const int stride = width * 4;
        var dib = new byte[headerSize + stride * height];
        BitConverter.GetBytes(headerSize).CopyTo(dib, 0);
        BitConverter.GetBytes(width).CopyTo(dib, 4);
        BitConverter.GetBytes(height).CopyTo(dib, 8);
        BitConverter.GetBytes((ushort)1).CopyTo(dib, 12);
        BitConverter.GetBytes((ushort)32).CopyTo(dib, 14);

        for (var y = 0; y < height; y++)
        {
            var color = y < height / 2 ? top : bottom;
            for (var x = 0; x < width; x++)
            {
                WriteBgraPixel(dib, headerSize + y * stride + x * 4, color);
            }
        }

        return dib;
    }

    static void WritePaletteColor(byte[] dib, int index, Color color)
    {
        var offset = 40 + index * 4;
        dib[offset] = color.B;
        dib[offset + 1] = color.G;
        dib[offset + 2] = color.R;
        dib[offset + 3] = 0;
    }

    static void WriteBgraPixel(byte[] bytes, int offset, Color color)
    {
        bytes[offset] = color.B;
        bytes[offset + 1] = color.G;
        bytes[offset + 2] = color.R;
        bytes[offset + 3] = color.A;
    }

    static void AssertBitmapPreviewTopBottom(Bitmap bitmap, Color expectedTop, Color expectedBottom, string label)
    {
        var actualTop = DominantOpaqueColor(bitmap, 0, bitmap.Height / 2);
        var actualBottom = DominantOpaqueColor(bitmap, bitmap.Height / 2, bitmap.Height);
        if (!CloseColor(actualTop, expectedTop) || !CloseColor(actualBottom, expectedBottom))
        {
            throw new InvalidOperationException($"{label} 行方向断言失败：top={actualTop} bottom={actualBottom}。");
        }
    }

    static Color DominantOpaqueColor(Bitmap bitmap, int yStart, int yEnd)
    {
        var counts = new Dictionary<int, int>();
        for (var y = Math.Max(0, yStart); y < Math.Min(bitmap.Height, yEnd); y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A < 128) continue;
                var key = pixel.ToArgb();
                counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
            }
        }

        if (counts.Count == 0)
        {
            throw new InvalidOperationException("预览图没有可用于行方向断言的不透明像素。");
        }

        return Color.FromArgb(counts.OrderByDescending(x => x.Value).First().Key);
    }

    static byte[] BuildSmokeLsBytes()
    {
        var bytes = new byte[16 + 64];
        Encoding.ASCII.GetBytes("Ls12").CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes("CCZSMOKE").CopyTo(bytes, 4);
        for (var i = 16; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(i - 16);
        }

        return bytes;
    }

    static void DisposeFrames(StrategyAnimationAnimatedPreviewResult result)
    {
        foreach (var frame in result.Frames)
        {
            frame.Dispose();
        }
    }

    static void AssertDllBitmapTopFirstRows(string dllPath, IReadOnlyList<int> resourceIds, Color expectedTop, Color expectedBottom)
    {
        var sawBitmap = false;
        foreach (var resourceId in resourceIds)
        {
            var dib = ReadRtBitmapResourceDib(dllPath, resourceId);
            if (dib.Length < 40) continue;

            var headerSize = BitConverter.ToInt32(dib, 0);
            var width = BitConverter.ToInt32(dib, 4);
            var height = BitConverter.ToInt32(dib, 8);
            var bitCount = BitConverter.ToUInt16(dib, 14);
            if (headerSize != 40 || width <= 0 || height <= 0 || bitCount != 32)
            {
                continue;
            }

            sawBitmap = true;
            var top = ReadBgraPixel(dib, 40 + (width / 2) * 4);
            var bottom = ReadBgraPixel(dib, 40 + ((height - 1) * width + width / 2) * 4);
            if (!CloseColor(top, expectedTop) || !CloseColor(bottom, expectedBottom))
            {
                throw new InvalidOperationException(
                    $"DLL 策略图标写入行方向断言失败：RT_BITMAP ID={resourceId} top={top} bottom={bottom}。预期顶部保持来源顶部颜色，底部保持来源底部颜色。");
            }
        }

        if (!sawBitmap)
        {
            throw new InvalidOperationException("DLL 策略图标写入行方向断言未读取到 32bpp RT_BITMAP。");
        }
    }

    static Color ReadBgraPixel(byte[] bytes, int offset)
        => Color.FromArgb(bytes[offset + 3], bytes[offset + 2], bytes[offset + 1], bytes[offset]);

    static bool CloseColor(Color actual, Color expected)
        => Math.Abs(actual.R - expected.R) <= 2 &&
           Math.Abs(actual.G - expected.G) <= 2 &&
           Math.Abs(actual.B - expected.B) <= 2 &&
           Math.Abs(actual.A - expected.A) <= 2;

    static byte[] ReadRtBitmapResourceDib(string dllPath, int resourceId)
    {
        var data = File.ReadAllBytes(dllPath);
        if (data.Length < 0x40 || data[0] != 'M' || data[1] != 'Z') return Array.Empty<byte>();
        var peOffset = BitConverter.ToInt32(data, 0x3C);
        var sectionCount = BitConverter.ToUInt16(data, peOffset + 6);
        var optionalHeaderSize = BitConverter.ToUInt16(data, peOffset + 20);
        var optionalHeaderOffset = peOffset + 24;
        var magic = BitConverter.ToUInt16(data, optionalHeaderOffset);
        var dataDirectoryOffset = magic == 0x20B ? optionalHeaderOffset + 112 : optionalHeaderOffset + 96;
        var resourceRva = BitConverter.ToInt32(data, dataDirectoryOffset + 2 * 8);
        var sectionOffset = optionalHeaderOffset + optionalHeaderSize;
        var sections = new List<SmokePeSection>();
        for (var i = 0; i < sectionCount; i++)
        {
            var offset = sectionOffset + i * 40;
            sections.Add(new SmokePeSection(
                BitConverter.ToInt32(data, offset + 12),
                Math.Max(BitConverter.ToInt32(data, offset + 8), BitConverter.ToInt32(data, offset + 16)),
                BitConverter.ToInt32(data, offset + 20)));
        }

        var resourceBaseOffset = SmokeRvaToFileOffset(resourceRva, sections);
        if (resourceBaseOffset < 0) return Array.Empty<byte>();
        return ReadRtBitmapResourceDib(data, sections, resourceBaseOffset, resourceBaseOffset, 0, new List<int>(), resourceId) ?? Array.Empty<byte>();
    }

    static byte[]? ReadRtBitmapResourceDib(
        byte[] data,
        IReadOnlyList<SmokePeSection> sections,
        int resourceBaseOffset,
        int directoryOffset,
        int level,
        List<int> path,
        int resourceId)
    {
        if (directoryOffset < 0 || directoryOffset + 16 > data.Length || level > 3) return null;
        var namedCount = BitConverter.ToUInt16(data, directoryOffset + 12);
        var idCount = BitConverter.ToUInt16(data, directoryOffset + 14);
        var entriesOffset = directoryOffset + 16;
        for (var i = 0; i < namedCount + idCount; i++)
        {
            var entryOffset = entriesOffset + i * 8;
            if (entryOffset + 8 > data.Length) return null;
            var nameRaw = BitConverter.ToInt32(data, entryOffset);
            var valueRaw = BitConverter.ToInt32(data, entryOffset + 4);
            if ((nameRaw & unchecked((int)0x80000000)) != 0) continue;
            var id = nameRaw & 0x7FFFFFFF;
            var valueOffset = valueRaw & 0x7FFFFFFF;
            var isDirectory = (valueRaw & unchecked((int)0x80000000)) != 0;
            if (isDirectory)
            {
                path.Add(id);
                var found = ReadRtBitmapResourceDib(data, sections, resourceBaseOffset, resourceBaseOffset + valueOffset, level + 1, path, resourceId);
                path.RemoveAt(path.Count - 1);
                if (found != null) return found;
                continue;
            }

            if (path.Count < 2 || path[0] != 2 || path[1] != resourceId) continue;
            var dataEntryOffset = resourceBaseOffset + valueOffset;
            if (dataEntryOffset + 16 > data.Length) return null;
            var dataRva = BitConverter.ToInt32(data, dataEntryOffset);
            var size = BitConverter.ToInt32(data, dataEntryOffset + 4);
            var fileOffset = SmokeRvaToFileOffset(dataRva, sections);
            if (fileOffset < 0 || size <= 0 || fileOffset + size > data.Length) return null;
            var bytes = new byte[size];
            Buffer.BlockCopy(data, fileOffset, bytes, 0, size);
            return bytes;
        }

        return null;
    }

    static int SmokeRvaToFileOffset(int rva, IReadOnlyList<SmokePeSection> sections)
    {
        foreach (var section in sections)
        {
            if (rva >= section.VirtualAddress && rva < section.VirtualAddress + section.Size)
            {
                return section.RawPointer + (rva - section.VirtualAddress);
            }
        }

        return -1;
    }

    sealed record SmokePeSection(int VirtualAddress, int Size, int RawPointer);

    static void RunAiImageAssetSmokeDetailed(CczProject project)
    {
        var smokeRoot = Path.Combine(project.WorkspaceRoot, "CCZModStudio_TestCopies", "AiImageAssetSmoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(smokeRoot);
        foreach (var coreFile in new[] { "Ekd5.exe", "Data.e5", "Imsg.e5", "Star.e5", "Hexzmap.e5" })
        {
            var source = Path.Combine(project.GameRoot, coreFile);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(smokeRoot, coreFile), overwrite: false);
            }
        }

        var smokeE5Dir = Path.Combine(smokeRoot, "E5");
        Directory.CreateDirectory(smokeE5Dir);
        foreach (var e5File in new[] { "Face.e5", "Mmap.e5" })
        {
            var source = CharacterImageResourceService.ResolveGameFile(project, e5File);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(smokeE5Dir, e5File), overwrite: false);
            }
        }

        foreach (var rootE5File in new[] { "Pmapobj.e5", "Unit_mov.e5", "Unit_atk.e5", "Unit_spc.e5" })
        {
            var source = CharacterImageResourceService.ResolveGameFile(project, rootE5File);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("AI 绘图素材烟测需要图片资源文件。", source);
            }

            File.Copy(source, Path.Combine(smokeRoot, rootE5File), overwrite: false);
        }

        foreach (var iconDll in new[] { "Itemicon.dll", "Mgcicon.dll", "Cmdicon.dll" })
        {
            var source = Path.Combine(project.GameRoot, iconDll);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(smokeRoot, iconDll), overwrite: false);
            }
        }

        File.WriteAllText(Path.Combine(smokeRoot, "_CCZModStudio_TestCopy.txt"),
            $"CreatedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nSource={project.GameRoot}\r\nPurpose=AI image asset smoke\r\n");

        var testProject = new ProjectDetector().CreateProjectFromGameRoot(smokeRoot);
        var service = new AiImageAssetService();
        var presetKeys = service.ListPresets().Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in new[] { "r_background", "dll_icon", "face", "r_actor", "s_unit" })
        {
            if (!presetKeys.Contains(key))
            {
                throw new InvalidOperationException("AI 绘图素材 preset 缺失：" + key);
            }
        }

        var sourcePng = Path.Combine(smokeRoot, "Smoke_AI_Source.png");
        using (var bitmap = new Bitmap(512, 512, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Magenta);
            using var body = new SolidBrush(Color.FromArgb(255, 48, 112, 200));
            using var armor = new SolidBrush(Color.FromArgb(255, 230, 180, 60));
            using var skin = new SolidBrush(Color.FromArgb(255, 232, 184, 140));
            graphics.FillEllipse(skin, 190, 88, 132, 132);
            graphics.FillRectangle(body, 176, 210, 160, 210);
            graphics.FillRectangle(armor, 208, 228, 96, 132);
            graphics.FillRectangle(armor, 238, 32, 36, 72);
            bitmap.Save(sourcePng, System.Drawing.Imaging.ImageFormat.Png);
        }

        var spriteSheetPng = Path.Combine(smokeRoot, "Smoke_AI_SUnit_4x6.png");
        using (var sheet = new Bitmap(384, 576, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            using var graphics = Graphics.FromImage(sheet);
            graphics.Clear(Color.Magenta);
            for (var frame = 0; frame < 24; frame++)
            {
                var col = frame % 4;
                var row = frame / 4;
                var x = col * 96;
                var y = row * 96;
                using var body = new SolidBrush(Color.FromArgb(255, 32 + frame * 7 % 180, 80 + frame * 11 % 150, 210 - frame * 5 % 120));
                using var sword = new Pen(Color.FromArgb(255, 250, 230, 80), 5);
                graphics.FillEllipse(Brushes.Bisque, x + 34, y + 16, 28, 28);
                graphics.FillRectangle(body, x + 30 + frame % 3, y + 44, 36, 34);
                graphics.DrawLine(sword, x + 58, y + 44, x + 74 - frame % 5, y + 18 + frame % 7);
                graphics.FillRectangle(Brushes.Black, x + 34, y + 78, 8, 12);
                graphics.FillRectangle(Brushes.Black, x + 54, y + 78, 8, 12);
            }

            sheet.Save(spriteSheetPng, System.Drawing.Imaging.ImageFormat.Png);
        }

        var unitMovPath = Path.Combine(smokeRoot, "Unit_mov.e5");
        var unitMovHashBefore = WriteOperationReportService.ComputeSha256(unitMovPath);

        object Preview(AiImagePromptPlan plan, string outputPath)
        {
            if (plan.Preset.TargetKind == "dll_icon")
            {
                return new IconResourceReplaceService().PreviewReplaceBitmapIcon(testProject, Path.Combine(smokeRoot, plan.TargetRelativePath), plan.TargetImageNumbers.First(), outputPath);
            }

            var targetPath = Path.Combine(smokeRoot, plan.TargetRelativePath.Replace('/', Path.DirectorySeparatorChar));
            var e5 = new E5ImageReplaceService();
            if (plan.TargetImageNumbers.Count > 1 || plan.Preset.TargetKind == "e5_batch")
            {
                return e5.PreviewBatchReplacement(testProject, targetPath, plan.TargetImageNumbers.Select(imageNumber => new E5ImageBatchReplaceRequest
                {
                    ImageNumber = imageNumber,
                    SourcePath = outputPath,
                    OperationKind = "AI绘图烟测预览"
                }));
            }

            return e5.PreviewReplacement(testProject, targetPath, plan.TargetImageNumbers.First(), outputPath);
        }

        var facePlan = service.BuildPromptPlan(
            testProject,
            "face",
            "冷峻的魏国青年武将头像",
            targetRelativePath: null,
            imageNumber: null,
            rImageId: null,
            sImageId: null,
            faceId: 1,
            jobId: null,
            factionSlot: 1,
            outputFormat: null,
            width: null,
            height: null);
        if (!facePlan.TargetImageNumbers.SequenceEqual(new[] { 9 }))
        {
            throw new InvalidOperationException("Face 头像映射断言失败：" + string.Join("/", facePlan.TargetImageNumbers));
        }

        var dryRun = service.DrawAsync(testProject, facePlan, dryRun: true).GetAwaiter().GetResult();
        if (!dryRun.DryRun ||
            dryRun.Provider != "image_studio" ||
            !dryRun.Plan.TargetImageNumbers.SequenceEqual(new[] { 9 }))
        {
            throw new InvalidOperationException("AI 绘图 dry_run 未返回完整提示词计划。");
        }

        VerifyAiImageResponseExtraction();

        var facePrepared = service.PrepareExistingImage(testProject, facePlan, sourcePng, Preview);
        AssertPreparedDimensions(facePrepared, 120, 120, ".png");
        if (facePrepared.ReplacementPreview is not E5ImageReplacePreviewResult facePreview ||
            facePreview.ImageNumber != 9 ||
            facePreview.SourceWidth != 120 ||
            facePreview.SourceHeight != 120)
        {
            throw new InvalidOperationException("Face 后处理或替换预览断言失败。");
        }

        var backgroundPlan = service.BuildPromptPlan(
            testProject,
            "r_background",
            "洛阳宫城黄昏背景",
            targetRelativePath: null,
            imageNumber: 1,
            rImageId: null,
            sImageId: null,
            faceId: null,
            jobId: null,
            factionSlot: 1,
            outputFormat: null,
            width: null,
            height: null);
        var backgroundPrepared = service.PrepareExistingImage(testProject, backgroundPlan, sourcePng, Preview);
        AssertPreparedDimensions(backgroundPrepared, 640, 400, ".jpg");
        if (backgroundPrepared.ReplacementPreview is not E5ImageReplacePreviewResult backgroundPreview ||
            backgroundPreview.ImageNumber != 1 ||
            backgroundPreview.NewKind != "JPG")
        {
            throw new InvalidOperationException("R 背景后处理或替换预览断言失败。");
        }

        var iconPlan = service.BuildPromptPlan(
            testProject,
            "dll_icon",
            "青釭剑道具图标",
            targetRelativePath: "Itemicon.dll",
            imageNumber: 0,
            rImageId: null,
            sImageId: null,
            faceId: null,
            jobId: null,
            factionSlot: 1,
            outputFormat: null,
            width: null,
            height: null);
        var iconPrepared = service.PrepareExistingImage(testProject, iconPlan, sourcePng, Preview);
        AssertPreparedDimensions(iconPrepared, 32, 32, ".png");
        if (iconPrepared.ReplacementPreview is not IconResourceReplacePreviewResult iconPreview ||
            iconPreview.IconIndex != 0 ||
            iconPreview.ResourceIds.Count == 0 ||
            iconPreview.SourceWidth != 32 ||
            iconPreview.SourceHeight != 32)
        {
            throw new InvalidOperationException("DLL 图标后处理或替换预览断言失败。");
        }

        var rPlan = service.BuildPromptPlan(
            testProject,
            "r_actor",
            "红袍青年谋士剧情小人",
            targetRelativePath: null,
            imageNumber: null,
            rImageId: 2,
            sImageId: null,
            faceId: null,
            jobId: null,
            factionSlot: 1,
            outputFormat: null,
            width: null,
            height: null);
        if (!rPlan.TargetImageNumbers.SequenceEqual(new[] { 5, 6 }))
        {
            throw new InvalidOperationException("R 形象映射断言失败：" + string.Join("/", rPlan.TargetImageNumbers));
        }

        var rPrepared = service.PrepareExistingImage(testProject, rPlan, sourcePng, Preview);
        AssertPreparedDimensions(rPrepared, 48, 1280, ".png");
        if (rPrepared.ReplacementPreview is not E5ImageBatchReplacePreviewResult rPreview ||
            rPreview.OperationCount != 2 ||
            !rPreview.Operations.Select(x => x.ImageNumber).SequenceEqual(new[] { 5, 6 }))
        {
            throw new InvalidOperationException("R 形象条带后处理或批量预览断言失败。");
        }

        var sPlan = service.BuildPromptPlan(
            testProject,
            "s_unit",
            "银甲骑兵战场小人",
            targetRelativePath: null,
            imageNumber: null,
            rImageId: null,
            sImageId: 1,
            faceId: null,
            jobId: null,
            factionSlot: 1,
            outputFormat: null,
            width: null,
            height: null);
        if (!sPlan.TargetImageNumbers.SequenceEqual(new[] { 241, 242, 243 }))
        {
            throw new InvalidOperationException("S 形象映射断言失败：" + string.Join("/", sPlan.TargetImageNumbers));
        }
        if (!sPlan.Prompt.Contains("当前武器", StringComparison.Ordinal) ||
            !sPlan.Prompt.Contains("角色替换型", StringComparison.Ordinal) ||
            !sPlan.Prompt.Contains("第一张参考图", StringComparison.Ordinal) ||
            !sPlan.Prompt.Contains("第二张参考图", StringComparison.Ordinal) ||
            !sPlan.Prompt.Contains("保留第二张图的格子布局", StringComparison.Ordinal) ||
            !sPlan.Prompt.Contains("4 倍硬边参考图", StringComparison.Ordinal) ||
            !sPlan.Prompt.Contains("AI 输出只作为可编辑草稿", StringComparison.Ordinal) ||
            !sPlan.Prompt.Contains("手工修图", StringComparison.Ordinal) ||
            !sPlan.Prompt.Contains("48x48", StringComparison.Ordinal) ||
            !sPlan.Prompt.Contains("#F700FF", StringComparison.Ordinal) ||
            !sPlan.Prompt.Contains("接近撑满单帧", StringComparison.Ordinal) ||
            !sPlan.Prompt.Contains("必须利用 64x64 攻击格", StringComparison.Ordinal) ||
            !sPlan.Prompt.Contains("2 到 3 头身", StringComparison.Ordinal) ||
            sPlan.Prompt.Contains("白色或纯洋红", StringComparison.Ordinal) ||
            sPlan.Prompt.Contains("用剑格挡", StringComparison.Ordinal) ||
            sPlan.Prompt.Contains("举剑", StringComparison.Ordinal) ||
            sPlan.Prompt.Contains("挥剑", StringComparison.Ordinal) ||
            sPlan.Prompt.Contains("收剑", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("S 形象提示词应使用通用当前武器、洋红色键、撑满格和曹操传原生低分辨率比例约束，不能写死为剑士动作。");
        }
        if (!sPlan.NegativePrompt.Contains("把非剑武器改成剑", StringComparison.Ordinal) ||
            !sPlan.NegativePrompt.Contains("改变第二张格式图布局", StringComparison.Ordinal) ||
            !sPlan.NegativePrompt.Contains("重排格子", StringComparison.Ordinal) ||
            !sPlan.NegativePrompt.Contains("把24格画成一张场景", StringComparison.Ordinal) ||
            !sPlan.NegativePrompt.Contains("混入白底", StringComparison.Ordinal) ||
            !sPlan.NegativePrompt.Contains("每格角色比例漂移", StringComparison.Ordinal) ||
            !sPlan.NegativePrompt.Contains("现代手游像素立绘", StringComparison.Ordinal) ||
            !sPlan.NegativePrompt.Contains("高清插画", StringComparison.Ordinal) ||
            !sPlan.NegativePrompt.Contains("白色背景", StringComparison.Ordinal) ||
            !sPlan.NegativePrompt.Contains("小图标式居中角色", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("S 形象负面约束应禁止武器漂移、白底大留白和高清插画风。");
        }
        var sDryRun = service.DrawAsync(testProject, sPlan, dryRun: true).GetAwaiter().GetResult();
        if (!sDryRun.DryRun ||
            sDryRun.Provider != "retrodiffusion" ||
            !sDryRun.BaseUrl.Contains("retrodiffusion", StringComparison.OrdinalIgnoreCase) ||
            !sDryRun.Logs.Any(x => x.Contains("provider=retrodiffusion", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("R/S 形象 dry_run 应默认选择 RetroDiffusion 像素专用上游。");
        }

        var sPrepared = service.PrepareExistingImage(testProject, sPlan, spriteSheetPng, Preview);
        if (sPrepared.PreparedFiles.Count != 3)
        {
            throw new InvalidOperationException("S 形象应输出移动/攻击/特技三条带。");
        }

        AssertPreparedFile(sPrepared.PreparedFiles.Single(x => x.Role == "move"), "Unit_mov.e5", 48, 528);
        AssertPreparedFile(sPrepared.PreparedFiles.Single(x => x.Role == "attack"), "Unit_atk.e5", 64, 768);
        AssertPreparedFile(sPrepared.PreparedFiles.Single(x => x.Role == "special"), "Unit_spc.e5", 48, 240);
        if (sPrepared.PreparedFiles.Any(x => x.ReplacementPreview is not E5ImageBatchReplacePreviewResult batch || batch.OperationCount != 3))
        {
            throw new InvalidOperationException("S 形象三条带替换预览断言失败。");
        }

        var unitMovHashAfter = WriteOperationReportService.ComputeSha256(unitMovPath);
        if (!unitMovHashBefore.Equals(unitMovHashAfter, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("AI 绘图素材 smoke 不应修改 Unit_mov.e5。");
        }

        Console.WriteLine($"AI_IMAGE_ASSET_SMOKE_OK presets={presetKeys.Count} face={facePrepared.OutputWidth}x{facePrepared.OutputHeight} background={backgroundPrepared.OutputWidth}x{backgroundPrepared.OutputHeight} iconIds={string.Join(',', iconPreview.ResourceIds)} r={rPrepared.OutputWidth}x{rPrepared.OutputHeight} s={string.Join(',', sPrepared.PreparedFiles.Select(x => x.Role + ':' + x.OutputWidth + 'x' + x.OutputHeight))}");
    }

    static void AssertPreparedDimensions(AiImagePrepareResult result, int width, int height, string extension)
    {
        if (result.OutputWidth != width ||
            result.OutputHeight != height ||
            !Path.GetExtension(result.OutputPath).Equals(extension, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(result.OutputPath) ||
            !File.Exists(result.ManifestPath))
        {
            throw new InvalidOperationException($"AI 绘图素材尺寸断言失败：expected={width}x{height}{extension}, actual={result.OutputWidth}x{result.OutputHeight} {result.OutputPath}");
        }

        using var image = Image.FromFile(result.OutputPath);
        if (image.Width != width || image.Height != height)
        {
            throw new InvalidOperationException($"AI 绘图素材文件尺寸断言失败：expected={width}x{height}, actual={image.Width}x{image.Height}");
        }
    }

    static void AssertPreparedFile(AiImagePreparedFile file, string targetRelativePath, int width, int height)
    {
        if (!file.TargetRelativePath.Equals(targetRelativePath, StringComparison.OrdinalIgnoreCase) ||
            file.OutputWidth != width ||
            file.OutputHeight != height ||
            !File.Exists(file.OutputPath))
        {
            throw new InvalidOperationException($"AI 绘图素材多文件断言失败：role={file.Role}, target={file.TargetRelativePath}, size={file.OutputWidth}x{file.OutputHeight}");
        }

        using var image = Image.FromFile(file.OutputPath);
        if (image.Width != width || image.Height != height)
        {
            throw new InvalidOperationException($"AI 绘图素材多文件尺寸断言失败：role={file.Role}, expected={width}x{height}, actual={image.Width}x{image.Height}");
        }
    }

    static void VerifyAiImageResponseExtraction()
    {
        var method = typeof(AiImageAssetService).GetMethod("ExtractImageBase64", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(AiImageAssetService), "ExtractImageBase64");
        var json = """{"data":[{"b64_json":"iVBOR_fake_image_payload_for_smoke_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}]}""";
        var sse = "data: {\"type\":\"response.output_item.done\",\"item\":{\"type\":\"image_generation_call\",\"result\":\"iVBOR_fake_image_payload_for_smoke_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\"}}\n\ndata: [DONE]\n";
        var jsonResult = Convert.ToString(method.Invoke(null, new object[] { json }), CultureInfo.InvariantCulture);
        var sseResult = Convert.ToString(method.Invoke(null, new object[] { sse }), CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(jsonResult) || string.IsNullOrWhiteSpace(sseResult))
        {
            throw new InvalidOperationException("AI 绘图上游响应解析未兼容 JSON/SSE。");
        }
    }
}
