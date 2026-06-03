namespace CCZModStudio.Models;

public sealed class ResourceDiagnosticItem
{
    public string Severity { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Rule { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string Suggestion { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
}
