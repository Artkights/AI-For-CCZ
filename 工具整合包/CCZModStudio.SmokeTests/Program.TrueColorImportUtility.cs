using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Drawing;

internal partial class Program
{
    static void RunRImageTrueColorImport(CczProject project, string[] args)
    {
        var request = new RImageReplaceRequest
        {
            RImageId = ParseRequiredIntArg(args, "--r-image-id"),
            MaterialFolder = ReadRequiredArg(args, "--material-folder"),
            WriteMode = ReadOptionalArg(args, "--write-mode") ?? "direct"
        };

        var result = new RImageReplaceService().Replace(project, request);
        var e5 = new E5ImageReplaceService();
        var targetPath = CharacterImageResourceService.ResolveGameFile(project, "Pmapobj.e5");
        VerifyPngEntry(e5, targetPath, result.Mapping.FrontImageNumber, 48, 1280);
        VerifyPngEntry(e5, targetPath, result.Mapping.BackImageNumber, 48, 1280);

        Console.WriteLine($"R_IMAGE_TRUECOLOR_IMPORT OK r={request.RImageId} images={result.Mapping.FrontImageNumber}/{result.Mapping.BackImageNumber} report={result.AggregateReportPath}");
    }

    static void RunSImageTrueColorImport(CczProject project, string[] args)
    {
        var request = new SImageReplaceRequest
        {
            SImageId = ParseRequiredIntArg(args, "--s-image-id"),
            MaterialFolder = ReadRequiredArg(args, "--material-folder"),
            JobId = TryParseOptionalIntArg(args, "--job-id"),
            FactionSlot = TryParseOptionalIntArg(args, "--faction-slot") ?? 1,
            WriteMode = ReadOptionalArg(args, "--write-mode") ?? "direct"
        };

        var result = new SImageReplaceService().Replace(project, request);
        var e5 = new E5ImageReplaceService();
        foreach (var imageNumber in result.Mapping.ImageNumbers)
        {
            VerifyPngEntry(e5, CharacterImageResourceService.ResolveGameFile(project, "Unit_mov.e5"), imageNumber, 48, 528);
            VerifyPngEntry(e5, CharacterImageResourceService.ResolveGameFile(project, "Unit_atk.e5"), imageNumber, 64, 768);
            VerifyPngEntry(e5, CharacterImageResourceService.ResolveGameFile(project, "Unit_spc.e5"), imageNumber, 48, 240);
        }

        Console.WriteLine($"S_IMAGE_TRUECOLOR_IMPORT OK s={request.SImageId} images={string.Join(",", result.Mapping.ImageNumbers)} report={result.AggregateReportPath}");
    }

    private static void VerifyPngEntry(E5ImageReplaceService service, string path, int imageNumber, int expectedWidth, int expectedHeight)
    {
        var bytes = service.ReadEntryBytes(path, imageNumber);
        if (bytes.Length < 8 ||
            bytes[0] != 0x89 ||
            bytes[1] != (byte)'P' ||
            bytes[2] != (byte)'N' ||
            bytes[3] != (byte)'G' ||
            bytes[4] != 0x0D ||
            bytes[5] != 0x0A ||
            bytes[6] != 0x1A ||
            bytes[7] != 0x0A)
        {
            throw new InvalidOperationException($"{Path.GetFileName(path)} #{imageNumber} is not a PNG entry.");
        }

        using (var memory = new MemoryStream(bytes, writable: false))
        using (var image = Image.FromStream(memory, useEmbeddedColorManagement: false, validateImageData: true))
        {
            if (image.Width != expectedWidth || image.Height != expectedHeight)
            {
                throw new InvalidOperationException($"{Path.GetFileName(path)} #{imageNumber} dimensions mismatch: {image.Width}x{image.Height} != {expectedWidth}x{expectedHeight}.");
            }
        }

        var entry = service.ReadIndex(path)[imageNumber - 1];
        if (!entry.Kind.Equals("PNG", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{Path.GetFileName(path)} #{imageNumber} was not indexed as PNG: {entry.Kind}.");
        }
    }

    private static string ReadRequiredArg(string[] args, string name)
        => ReadOptionalArg(args, name) ??
           throw new InvalidOperationException($"Missing required argument {name}.");

    private static string? ReadOptionalArg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static int ParseRequiredIntArg(string[] args, string name)
        => int.TryParse(ReadRequiredArg(args, name), out var value)
            ? value
            : throw new InvalidOperationException($"Argument {name} must be an integer.");

    private static int? TryParseOptionalIntArg(string[] args, string name)
    {
        var raw = ReadOptionalArg(args, name);
        if (raw == null) return null;
        return int.TryParse(raw, out var value)
            ? value
            : throw new InvalidOperationException($"Argument {name} must be an integer.");
    }
}
