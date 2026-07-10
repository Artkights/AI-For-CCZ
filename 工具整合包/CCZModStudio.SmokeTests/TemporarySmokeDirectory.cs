internal sealed class TemporarySmokeDirectory : IDisposable
{
    public TemporarySmokeDirectory(string name)
    {
        var safeName = string.IsNullOrWhiteSpace(name) ? "Smoke" : name;
        foreach (var invalid in System.IO.Path.GetInvalidFileNameChars())
        {
            safeName = safeName.Replace(invalid, '_');
        }

        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "CCZModStudio_" + safeName + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch
        {
            // Smoke temp cleanup is best effort.
        }
    }
}
