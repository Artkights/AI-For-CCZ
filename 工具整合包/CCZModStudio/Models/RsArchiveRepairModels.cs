namespace CCZModStudio.Models;

public enum RsArchiveRepairMode
{
    PreservePixelsAndRestoreFormat,
    CompactOnly,
    RestoreWholeBackup,
    LegacyIndexShiftRecovery
}

public sealed class RsLegacyIndexShiftRecoveryCandidate
{
    public string FileName { get; init; } = string.Empty;
    public string TargetPath { get; init; } = string.Empty;
    public string BaselineBackupPath { get; init; } = string.Empty;
    public string EvidenceReportPath { get; init; } = string.Empty;
    public int DamagedPhysicalImageNumber { get; init; }
    public int CorrectPhysicalImageNumber { get; init; }
    public int WrongOffsetDelta { get; init; }
    public int ExpectedEntryCount { get; init; }
    public int ParsedEntryCount { get; init; }
    public int EditedPayloadLength { get; init; }
    public string CurrentArchiveSha256 { get; init; } = string.Empty;
    public string HistoricalSha256 { get; init; } = string.Empty;
    public string ExpectedHistoricalSha256 { get; init; } = string.Empty;
    public string RepairedSha256 { get; init; } = string.Empty;
    public string ExpectedRepairedSha256 { get; init; } = string.Empty;
    public bool IsVerified { get; init; }
    public string Diagnostic { get; init; } = string.Empty;
    internal byte[] HistoricalBytes { get; init; } = Array.Empty<byte>();
    internal byte[] RepairedBytes { get; init; } = Array.Empty<byte>();
    internal byte[] EditedPayload { get; init; } = Array.Empty<byte>();
}

public sealed class RsArchiveRepairCandidate
{
    public int ImageNumber { get; init; }
    public string CurrentKind { get; init; } = string.Empty;
    public string RestoreKind { get; init; } = string.Empty;
    public int CurrentLength { get; init; }
    public int RestoredLength { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int NearestPalettePixels { get; init; }
    public string BackupPath { get; init; } = string.Empty;
    public string Warning { get; init; } = string.Empty;
    public bool Selected { get; set; } = true;
    internal byte[] RestoredBytes { get; init; } = Array.Empty<byte>();
    internal string CurrentStoredSha256 { get; init; } = string.Empty;
}

public sealed class RsArchiveRepairArchivePreview
{
    public string FileName { get; init; } = string.Empty;
    public string TargetPath { get; init; } = string.Empty;
    public long CurrentSize { get; init; }
    public long CompactSize { get; init; }
    public long OrphanBytes => Math.Max(0, CurrentSize - CompactSize);
    public int EntryCount { get; init; }
    public IReadOnlyList<string> CompatibleBackups { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> EvidenceReports { get; init; } = Array.Empty<string>();
    public IReadOnlyList<RsArchiveRepairCandidate> Candidates { get; init; } = Array.Empty<RsArchiveRepairCandidate>();
    public E5ArchiveTopologySnapshot Topology { get; init; } = new();
    public IReadOnlyList<string> IntegrityFindings { get; init; } = Array.Empty<string>();
    public string Diagnostic { get; init; } = string.Empty;
    public RsLegacyIndexShiftRecoveryCandidate? LegacyIndexShiftRecovery { get; init; }
}

public sealed class RsArchiveRepairPreview
{
    public IReadOnlyList<RsArchiveRepairArchivePreview> Archives { get; init; } = Array.Empty<RsArchiveRepairArchivePreview>();
    public int CandidateCount => Archives.Sum(archive => archive.Candidates.Count);
    public long TotalOrphanBytes => Archives.Sum(archive => archive.OrphanBytes);
    public IReadOnlyList<RsLegacyIndexShiftRecoveryCandidate> LegacyIndexShiftRecoveries => Archives
        .Select(archive => archive.LegacyIndexShiftRecovery)
        .Where(candidate => candidate != null)
        .Cast<RsLegacyIndexShiftRecoveryCandidate>()
        .ToArray();
    public bool CanExecuteLegacyIndexShiftRecovery =>
        LegacyIndexShiftRecoveries.Count == 3 && LegacyIndexShiftRecoveries.All(candidate => candidate.IsVerified);
}

public sealed class RsArchiveRepairResult
{
    public IReadOnlyList<string> ChangedArchives { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> BackupPaths { get; init; } = Array.Empty<string>();
    public long OldTotalSize { get; init; }
    public long NewTotalSize { get; init; }
    public string ReportPath { get; init; } = string.Empty;
}
