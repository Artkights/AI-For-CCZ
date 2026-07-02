namespace CCZModStudio.Models;

public sealed class ShopRuntimeDiagnosticResult
{
    public string GameRoot { get; init; } = string.Empty;
    public string ShopTableName { get; init; } = string.Empty;
    public int ExplicitShopIssueCount { get; init; }
    public IReadOnlyList<ShopSlotValidationIssue> ExplicitShopIssues { get; init; } = Array.Empty<ShopSlotValidationIssue>();
    public IReadOnlyList<ShopPlaceholderItemDiagnostic> PlaceholderItems { get; init; } = Array.Empty<ShopPlaceholderItemDiagnostic>();
    public ShopAutoShopDiagnostic AutoShop { get; init; } = new();
    public IReadOnlyList<ShopScenarioFileDiagnostic> ScenarioFiles { get; init; } = Array.Empty<ShopScenarioFileDiagnostic>();
    public IReadOnlyList<string> FileUseWarnings { get; init; } = Array.Empty<string>();
    public string Conclusion { get; init; } = string.Empty;
}

public sealed class ShopPlaceholderItemDiagnostic
{
    public int ItemId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
}

public sealed class ShopAutoShopDiagnostic
{
    public string ExePath { get; init; } = string.Empty;
    public uint VirtualAddress { get; init; }
    public string VirtualAddressHex => $"0x{VirtualAddress:X8}";
    public long RawOffset { get; init; } = -1;
    public string RawOffsetHex => RawOffset < 0 ? string.Empty : $"0x{RawOffset:X}";
    public string BytesHex { get; init; } = string.Empty;
    public IReadOnlyList<ShopAutoShopGroupDiagnostic> Groups { get; init; } = Array.Empty<ShopAutoShopGroupDiagnostic>();
    public int PlaceholderHitCount => Groups.Sum(group => group.PlaceholderHitCount);
    public string Error { get; init; } = string.Empty;
}

public sealed class ShopAutoShopGroupDiagnostic
{
    public int GroupIndex { get; init; }
    public int BaseItemId { get; init; }
    public bool Enabled { get; init; }
    public IReadOnlyList<ShopAutoShopItemDiagnostic> Items { get; init; } = Array.Empty<ShopAutoShopItemDiagnostic>();
    public int PlaceholderHitCount => Items.Count(item => item.IsPlaceholder);
}

public sealed class ShopAutoShopItemDiagnostic
{
    public int ItemId { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsPlaceholder { get; init; }
}

public sealed class ShopScenarioFileDiagnostic
{
    public string FileName { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public bool Exists { get; init; }
    public string Error { get; init; } = string.Empty;
    public int CandidateCount { get; init; }
    public IReadOnlyList<ShopScenarioCommandCandidate> Candidates { get; init; } = Array.Empty<ShopScenarioCommandCandidate>();
}

public sealed class ShopScenarioCommandCandidate
{
    public int CommandId { get; init; }
    public string CommandIdHex { get; init; } = string.Empty;
    public string CommandName { get; init; } = string.Empty;
    public int SceneIndex { get; init; }
    public int SectionIndex { get; init; }
    public int CommandIndex { get; init; }
    public int FileOffset { get; init; }
    public string OffsetHex { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public IReadOnlyList<int> PlaceholderItemIds { get; init; } = Array.Empty<int>();
    public bool Has4088Reference { get; init; }
    public string TextPreview { get; init; } = string.Empty;
}
