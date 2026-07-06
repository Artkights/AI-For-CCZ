using System.Data;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class TableDerivedDisplayService
{
    public IReadOnlyList<string> RefreshRow(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        HexTableDefinition table,
        DataRow row)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(row);

        // The derived display refresh implementation is intentionally disabled until
        // the corrupted source file is restored. Returning an empty change set keeps
        // table editing functional and avoids blocking unrelated MCP image tools.
        return Array.Empty<string>();
    }
}
