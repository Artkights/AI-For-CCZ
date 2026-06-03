namespace CCZModStudio.Models;

public sealed class TableReadResult
{
    public required HexTableDefinition Table { get; init; }
    public required System.Data.DataTable Data { get; init; }
    public required HexTableValidationResult Validation { get; init; }
}
