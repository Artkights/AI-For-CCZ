namespace CCZModStudio.Models;

public sealed record ProjectFileStatus(
    string Name,
    string Path,
    bool Exists,
    long? SizeBytes,
    string Kind
);
