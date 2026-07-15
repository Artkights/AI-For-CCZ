namespace CCZModStudio.Models;

public static class ContextSlotAccess
{
    public const string Read = "Read";
    public const string Write = "Write";
    public const string ReadWrite = "ReadWrite";
}

public static class HookContractVerificationStatus
{
    public const string StaticCandidate = "StaticCandidate";
    public const string DynamicVerified = "DynamicVerified";
    public const string BytesChanged = "BytesChanged";
    public const string WrongExe = "WrongExe";
}

public static class ContextRelationshipStatus
{
    public const string Unknown = "Unknown";
    public const string EffectSubjectCandidate = "EffectSubjectCandidate";
    public const string DamageSourceCandidate = "DamageSourceCandidate";
    public const string DamageTargetCandidate = "DamageTargetCandidate";
    public const string Verified = "Verified";
}

public static class ContextSlotEvidenceKind
{
    public const string Observation = "Observation";
    public const string Relationship = "Relationship";
    public const string StateTransition = "StateTransition";
    public const string DynamicBoundary = "DynamicBoundary";
}

public static class HookContinuationPolicies
{
    public const string ReturnAfterOverwrite = "ReturnAfterOverwrite";
    public const string ChainExistingJumpTarget = "ChainExistingJumpTarget";
}

public sealed class ContextSourceExpression
{
    public string BaseRegister { get; set; } = string.Empty;
    public string IndexRegister { get; set; } = string.Empty;
    public int Scale { get; set; } = 1;
    public long SignedDisplacement { get; set; }
    public int ReadWidth { get; set; } = 4;
    public int DereferenceCount { get; set; } = 1;
    public string DerivedType { get; set; } = string.Empty;
    public uint DefinitionInstructionAddress { get; set; }
    public List<string> DataFlowChain { get; set; } = [];
    public double StaticConfidence { get; set; }
    public string RelationshipStatus { get; set; } = ContextRelationshipStatus.Unknown;

    public string ToAssemblyExpression()
    {
        var size = ReadWidth switch { 1 => "byte", 2 => "word", 4 => "dword", 8 => "qword", _ => "dword" };
        if (DereferenceCount == 0)
        {
            if (!string.IsNullOrWhiteSpace(BaseRegister)) return BaseRegister.ToLowerInvariant();
            return SignedDisplacement >= 0 ? $"0x{SignedDisplacement:X}" : $"-0x{-SignedDisplacement:X}";
        }
        var terms = new List<string>();
        if (!string.IsNullOrWhiteSpace(BaseRegister)) terms.Add(BaseRegister.ToLowerInvariant());
        if (!string.IsNullOrWhiteSpace(IndexRegister)) terms.Add(Scale == 1 ? IndexRegister.ToLowerInvariant() : $"{IndexRegister.ToLowerInvariant()}*{Scale}");
        var body = string.Join("+", terms);
        if (SignedDisplacement > 0) body += $"+0x{SignedDisplacement:X}";
        else if (SignedDisplacement < 0) body += $"-0x{-SignedDisplacement:X2}";
        if (string.IsNullOrWhiteSpace(body)) body = "0x0";
        return $"{size} [{body}]";
    }
}

public sealed class ContextPointerCandidate
{
    public ContextSourceExpression Source { get; set; } = new();
    public string UnderlyingType { get; set; } = string.Empty;
    public string RelationshipSemantic { get; set; } = ContextRelationshipStatus.Unknown;
    public string Confidence { get; set; } = "Low";
    public List<string> EvidenceZh { get; set; } = [];
}

public sealed class ContextPointerInference
{
    public uint CallAddress { get; set; }
    public string Register { get; set; } = "ecx";
    public bool IsUnique { get; set; }
    public bool CanUseForWrite { get; set; }
    public List<ContextPointerCandidate> Candidates { get; set; } = [];
    public List<string> BlockerCodes { get; set; } = [];
    public List<string> ReasonsZh { get; set; } = [];
}

public sealed class ContextSourcePath
{
    public ContextSourceExpression Root { get; set; } = new();
    public List<ContextPathStep> Steps { get; set; } = [];

    public string ToDisplayExpression()
    {
        var text = Root.ToAssemblyExpression();
        foreach (var step in Steps)
            text += $" -> {(step.ReadWidth == 1 ? "byte" : step.ReadWidth == 2 ? "word" : "dword")} [+0x{step.Offset:X}] ({step.ResultType})";
        return text;
    }
}

public sealed class ContextPathStep
{
    public int Offset { get; set; }
    public int ReadWidth { get; set; } = 4;
    public bool Dereference { get; set; } = true;
    public string ResultType { get; set; } = string.Empty;
}

public sealed class ContextSlotContract
{
    public string SlotId { get; set; } = string.Empty;
    public string DisplayNameZh { get; set; } = string.Empty;
    public string SourceExpression { get; set; } = string.Empty;
    public ContextSourceExpression? StructuredSource { get; set; }
    public ContextSourcePath? StructuredPath { get; set; }
    public int ByteWidth { get; set; } = 4;
    public bool IsSigned { get; set; } = true;
    public string Access { get; set; } = ContextSlotAccess.Read;
    public string EvidenceKind { get; set; } = ContextSlotEvidenceKind.Observation;
    public string ClampMaximumSlotId { get; set; } = string.Empty;
    public string LifetimeZh { get; set; } = string.Empty;
    public int? Minimum { get; set; }
    public int? Maximum { get; set; }
    public List<string> AllowedActions { get; set; } = [];
    public List<string> StaticEvidenceZh { get; set; } = [];
    public List<string> DynamicEvidenceZh { get; set; } = [];
    public bool IsStaticallyResolved { get; set; }
    public bool IsDynamicallyVerified { get; set; }
}

public sealed class HookExecutionContract
{
    public string ContractId { get; set; } = string.Empty;
    public string ContractFamilyId { get; set; } = string.Empty;
    public int ContractVersion { get; set; } = 1;
    public string DisplayNameZh { get; set; } = string.Empty;
    public string ExeSha256 { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string NormalizedProfileIdentity { get; set; } = string.Empty;
    public string ContractCodeIdentityHash { get; set; } = string.Empty;
    public uint HookAddress { get; set; }
    public string ExpectedOldBytesHex { get; set; } = string.Empty;
    public string NormalizedLocatorSignature { get; set; } = string.Empty;
    public string TriggerPhaseZh { get; set; } = string.Empty;
    public string CallingConventionZh { get; set; } = string.Empty;
    public string OriginalOperationOrderZh { get; set; } = string.Empty;
    public string ClampOrderZh { get; set; } = string.Empty;
    public string UnitPointerSlotId { get; set; } = string.Empty;
    public string TargetPointerSlotId { get; set; } = string.Empty;
    public List<ContextSlotContract> Slots { get; set; } = [];
    public List<string> AllowedActions { get; set; } = [];
    public List<string> AllowedCallSymbols { get; set; } = [];
    public List<string> PreservedRegisters { get; set; } = [];
    public bool PreserveFlags { get; set; } = true;
    public int ExpectedStackDelta { get; set; }
    public string ConflictGroup { get; set; } = string.Empty;
    public string ContinuationPolicy { get; set; } = HookContinuationPolicies.ReturnAfterOverwrite;
    public uint ContinuationAddress { get; set; }
    public EffectValidationRecipe ValidationRecipe { get; set; } = new();
    public string VerificationStatus { get; set; } = HookContractVerificationStatus.StaticCandidate;
    public string VerificationStatusZh { get; set; } = "静态候选";
    public bool AllowSemanticPreview { get; set; }
    public List<string> MissingEvidenceZh { get; set; } = [];
    public List<string> DynamicValidationPlanZh { get; set; } = [];
    public string ContractHash { get; set; } = string.Empty;
    public ContextPointerInference PointerInference { get; set; } = new();
}

public sealed class EffectContractProbePlan
{
    public string PlanId { get; set; } = string.Empty;
    public string ContractId { get; set; } = string.Empty;
    public string ExeSha256 { get; set; } = string.Empty;
    public List<uint> BreakpointAddresses { get; set; } = [];
    public List<string> RequiredCapturesZh { get; set; } = [];
    public List<string> ScenariosZh { get; set; } = [];
    public List<EffectProbeBatch> Batches { get; set; } = [];
    public string SummaryZh { get; set; } = string.Empty;
}

public sealed class EffectContractEvidence
{
    public string EvidenceId { get; set; } = string.Empty;
    public string ContractId { get; set; } = string.Empty;
    public string ExeSha256 { get; set; } = string.Empty;
    public DateTime CapturedAt { get; set; } = DateTime.Now;
    public int HookHitCount { get; set; }
    public List<string> CompletedScenariosZh { get; set; } = [];
    public Dictionary<string, string> ObservedSlots { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> ObservedSlotMinimums { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> ObservedSlotMaximums { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> CallStackZh { get; set; } = [];
    public string SourceTool { get; set; } = string.Empty;
    public string NotesZh { get; set; } = string.Empty;
}

public sealed class EffectContractEvidenceImportResult
{
    public bool Accepted { get; set; }
    public bool ContractPromoted { get; set; }
    public string SummaryZh { get; set; } = string.Empty;
    public List<string> WarningsZh { get; set; } = [];
    public string SavedPath { get; set; } = string.Empty;
    public HookExecutionContract Contract { get; set; } = new();
}

public static class SemanticEffectAction
{
    public const string AddDamageFixed = "AddDamageFixed";
    public const string SubtractDamageFixed = "SubtractDamageFixed";
    public const string AddDamagePercent = "AddDamagePercent";
    public const string SubtractDamagePercent = "SubtractDamagePercent";
    public const string RestoreHpFixed = "RestoreHpFixed";
    public const string RestoreMpFixed = "RestoreMpFixed";
    public const string RestoreHpMaxPercent = "RestoreHpMaxPercent";
    public const string RestoreMpMaxPercent = "RestoreMpMaxPercent";
}

public static class SemanticEffectValueSource
{
    public const string Constant = "Constant";
    public const string CoreReturnValue = "CoreReturnValue";
    public const string ParameterBlock = "ParameterBlock";
}

public sealed class SemanticEffectProgram
{
    public string SchemaVersion { get; set; } = "2.0";
    public string ProgramId { get; set; } = string.Empty;
    public string HookContractId { get; set; } = string.Empty;
    public string Channel { get; set; } = CompositeEffectChannel.PersonalJob;
    public int PersonalEffectId { get; set; }
    public int ItemEffectId { get; set; }
    public int EffectValueMode { get; set; } = 1;
    public int StackingMode { get; set; } = 1;
    public string SubjectSlotId { get; set; } = string.Empty;
    public string TargetSlotId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string ValueSource { get; set; } = SemanticEffectValueSource.Constant;
    public int Value { get; set; }
    public int ExecutionOrder { get; set; }
    public bool Enabled { get; set; } = true;
    public string BoundaryPolicy { get; set; } = string.Empty;
}

public sealed class CompiledSemanticBody
{
    public bool CanCompile { get; set; }
    public bool CanPreview { get; set; }
    public string AssemblySource { get; set; } = string.Empty;
    public string MeaningZh { get; set; } = string.Empty;
    public List<string> ReadSlots { get; set; } = [];
    public List<string> WrittenSlots { get; set; } = [];
    public List<string> RequiredSymbols { get; set; } = [];
    public List<string> WarningsZh { get; set; } = [];
    public HookExecutionContract Contract { get; set; } = new();
    public SemanticPatchValidationResult Validation { get; set; } = new();
}

public sealed class SemanticPatchValidationResult
{
    public bool IsValid { get; set; }
    public int StackDelta { get; set; }
    public List<string> RegistersWritten { get; set; } = [];
    public List<string> MemoryWrites { get; set; } = [];
    public List<string> CallTargets { get; set; } = [];
    public List<string> WarningsZh { get; set; } = [];
    public string SummaryZh { get; set; } = string.Empty;
}

public sealed class EffectDispatcherEntry
{
    public string EntryId { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int PersonalEffectId { get; set; }
    public int ItemEffectId { get; set; }
    public int EffectValueMode { get; set; }
    public int StackingMode { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ValueSource { get; set; } = string.Empty;
    public int Value { get; set; }
    public int ExecutionOrder { get; set; }
}

public sealed class EffectTriggerDispatcherDraft
{
    public string DispatcherId { get; set; } = string.Empty;
    public string HookContractId { get; set; } = string.Empty;
    public int RegistryVersion { get; set; } = 2;
    public int Capacity { get; set; } = 16;
    public uint HookAddress { get; set; }
    public uint CodeAddress { get; set; }
    public uint RegistryAddress { get; set; }
    public List<EffectDispatcherEntry> Entries { get; set; } = [];
}

public sealed class CompiledEffectDispatcher
{
    public bool CanCompile { get; set; }
    public bool CanPreview { get; set; }
    public string AssemblySource { get; set; } = string.Empty;
    public byte[] RegistryBytes { get; set; } = [];
    public HookExecutionContract Contract { get; set; } = new();
    public List<string> WarningsZh { get; set; } = [];
    public string SummaryZh { get; set; } = string.Empty;
}

public sealed class ModularEffectManifestV2
{
    public string SchemaVersion { get; set; } = "2.0";
    public string ManifestId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string ExeSha256Before { get; set; } = string.Empty;
    public string ExeSha256After { get; set; } = string.Empty;
    public ProjectPatchIdentity? ProjectIdentity { get; set; }
    public string DispatcherManifestId { get; set; } = string.Empty;
    public string DispatcherEntryId { get; set; } = string.Empty;
    public uint RegistryAddress { get; set; }
    public ModularCompositeEffectBlueprint Blueprint { get; set; } = new();
    public List<SemanticEffectProgram> Programs { get; set; } = [];
    public List<EffectTriggerDispatcherDraft> Dispatchers { get; set; } = [];
    public Dictionary<string, int> ContractVersions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ContractHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public EffectPackage Package { get; set; } = new();
    public string InstallationStatus { get; set; } = CompositeInstallationStatus.Complete;
    public string StatusZh { get; set; } = "已安装";
}

public sealed class EffectDispatcherManifestV2
{
    public string SchemaVersion { get; set; } = "2.0";
    public string ManifestId { get; set; } = string.Empty;
    public string DispatcherId { get; set; } = string.Empty;
    public ProjectPatchIdentity? ProjectIdentity { get; set; }
    public string ContractId { get; set; } = string.Empty;
    public string ContractHash { get; set; } = string.Empty;
    public int ContractVersion { get; set; }
    public HookExecutionContract? ContractSnapshot { get; set; }
    public uint HookAddress { get; set; }
    public uint CodeAddress { get; set; }
    public uint RegistryAddress { get; set; }
    public int RegistryLength { get; set; }
    public int Capacity { get; set; } = 16;
    public string ExeSha256Before { get; set; } = string.Empty;
    public string ExeSha256After { get; set; } = string.Empty;
    public EffectPackage InstallationPackage { get; set; } = new();
    public List<EffectDispatcherEntry> Entries { get; set; } = [];
    public string InstallationStatus { get; set; } = CompositeInstallationStatus.Complete;
    public string StatusZh { get; set; } = "完整";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

public sealed class ModularEffectMaintenanceDraft
{
    public string ManifestId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int? Value { get; set; }
    public int? ExecutionOrder { get; set; }
    public bool ReplaceBindings { get; set; }
    public List<EffectPackageBinding> Bindings { get; set; } = [];
}
