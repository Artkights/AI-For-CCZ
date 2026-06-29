using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CCZModStudio;

public sealed partial class MainForm : Form
{
    private enum LegacyScriptEditorScope
    {
        Script,
        Battlefield,
        RScene
    }

    private sealed record JobEditorCellTarget(DataRow Row, string ColumnName);
    private sealed record JobEditorCellEdit(DataRow Row, string ColumnName, object? OldValue, object? NewValue);
    private sealed record ItemEditorCellTarget(DataRow Row, string ColumnName);
    private sealed record ItemEditorCellEdit(DataRow Row, string ColumnName, object? OldValue, object? NewValue);
    private sealed record JobStrategyIconImportTarget(int StrategyId, string StrategyName, int IconIndex, string SourcePath);
    private sealed record BattlefieldCommand25Marker(int GridX, int GridY, LegacyScenarioCommandNode Command, int Count);

    private enum ItemIconPreviewRole
    {
        Large,
        Small
    }

    private const int DefaultWindowWidth = 1280;
    private const int DefaultWindowHeight = 820;
    private const int MinimumWindowWidth = 900;
    private const int MinimumWindowHeight = 560;
    private const int AbsoluteMinimumWindowWidth = 640;
    private const int AbsoluteMinimumWindowHeight = 420;
    private const int WindowScreenMargin = 24;
    private const int ScriptCommandGridMaxRows = 800;
    private const int ScriptLegacyTreeCommandNodeLimitPerSection = 20000;
    private const int ScriptLegacyTreeMaxNestedDepth = 64;
    private const int RSceneCanvasWidth = 640;
    private const int RSceneCanvasHeight = 400;
    private const int RSceneTileWidth = 16;
    private const int RSceneTileHeight = 8;
    private const int RSceneFrameCount = 20;
    private const int RSceneCoordinateXPixelOffset = 42;
    private const int RSceneCoordinateYPixelOffset = 50;
    private const int RSceneMapFaceFallbackSize = 48;
    private const int RSceneMapFaceMaxWidth = 64;
    private const int RSceneMapFaceMaxHeight = 80;
    private const int RSceneImageCacheLimit = 256;
    private const int BattlefieldUnitAnimationIntervalMs = 800;
    private static readonly bool ShowGenericTableEditorPage = false;
    private static readonly bool ShowLegacyProbePages = false;
    private const string JobStrategyLearningPrefix = "学会等级_";
    private const string LegacyScriptClipboardFormat = "CCZModStudio.LegacyScriptCommands";
    private const string LegacyScriptClipboardBeginMarker = "-----BEGIN CCZMODSTUDIO LEGACY SCRIPT COMMANDS JSON-----";
    private const string LegacyScriptClipboardEndMarker = "-----END CCZMODSTUDIO LEGACY SCRIPT COMMANDS JSON-----";
    private static readonly IReadOnlyList<int> LegacyScriptCodeTestTable =
    [
        0, 0, 2, 1, 2, 2, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 2, 2, 0, 0, 0, 0, 0, 0, 2, 2, 0,
        0, 0, 0, 0, 0, 0, 2, 2, 0, 0, 0, 0, 2, 0, 0, 2,
        2, 2, 2, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0,
        2, 0, 0, 0, 0, 0, 2, 0, 2
    ];
    private static readonly JsonSerializerOptions LegacyScriptClipboardJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
    private static readonly string[] JobStrategyPrimaryColumns =
    [
        "名称",
        "策略类型",
        "施展对象",
        "施法范围",
        "穿透范围",
        "策略消耗",
        "策略图标"
    ];

    private static readonly (string ColumnName, string TableName)[] JobStrategyCompanionColumns =
    [
        ("大动画", "6.5-5-2 策略动画1"),
        ("小动画", "6.5-5-3 策略动画2"),
        ("是否伤血", "6.5-5-4 策略伤害类型"),
        ("伤害系数", "6.5-5-5 策略伤害比例"),
        ("命中上限", "6.5-5-6 策略命中率"),
        ("效果索引", "6.5-5-7 学会策略"),
        ("AI策略（战场）", "6.5-5-8 战场AI策略限制"),
        ("AI策略（练武）", "6.5-5-9 练武场AI策略限制")
    ];
    private static readonly string[] JobEquipmentCategoryColumns =
    [
        "普通剑",
        "特殊剑",
        "普通枪",
        "特殊枪",
        "普通弓",
        "特殊弓",
        "普通刀",
        "特殊刀",
        "普通炮车",
        "特殊炮车",
        "普通锤",
        "特殊锤",
        "普通斧",
        "特殊斧",
        "普通扇",
        "特殊扇",
        "普通宝剑",
        "特殊宝剑",
        "普通将剑",
        "特殊将剑",
        "普通铠甲",
        "特殊铠甲",
        "普通衣服",
        "特殊衣服",
        "普通袍服",
        "特殊袍服"
    ];
    private const string JobEquipmentSummaryColumn = "可装备类别";

    private static readonly IReadOnlyDictionary<int, string> JobStrategyTypeNames = new Dictionary<int, string>
    {
        [0] = "火系",
        [1] = "水系",
        [2] = "地系",
        [3] = "风系",
        [4] = "眩晕",
        [5] = "诱惑",
        [6] = "谍报",
        [7] = "压迫/威吓",
        [8] = "咒骂/挑拨",
        [9] = "钝兵/钝队",
        [10] = "虚脱/厌战",
        [11] = "士气提升",
        [12] = "防御提升",
        [13] = "攻击提升",
        [14] = "爆发提升",
        [15] = "谎报",
        [16] = "中毒",
        [17] = "定身",
        [18] = "封咒",
        [19] = "补给",
        [20] = "MP补给",
        [21] = "觉醒",
        [22] = "回归",
        [23] = "天气",
        [24] = "八阵图",
        [25] = "四神",
        [26] = "霸气",
        [27] = "强行",
        [28] = "衰气",
        [29] = "疾行",
        [30] = "诅咒",
        [31] = "精妙",
        [32] = "连击",
        [33] = "伪报",
        [34] = "纵火",
        [35] = "修筑",
        [36] = "诱敌",
        [37] = "瞬移",
        [38] = "雷系",
        [39] = "撞心",
        [40] = "扩展策略A",
        [41] = "扩展策略B",
        [42] = "扩展策略C"
    };

    private static readonly IReadOnlyDictionary<int, string> JobStrategyTargetNames = new Dictionary<int, string>
    {
        [0] = "敌方",
        [1] = "我方",
        [2] = "全屏/天气",
        [5] = "全屏己方气合",
        [6] = "全屏对方反气合"
    };

    private readonly ProjectDetector _projectDetector = new();
    private readonly CczEngineProfileService _engineProfileService = new();
    private readonly HexTableParser _tableParser = new();
    private readonly HexTableReader _tableReader = new();
    private readonly HexTableWriter _tableWriter = new();
    private readonly BackupManager _backupManager = new();    private readonly WriteOperationReportFormatter _writeOperationReportFormatter = new();
    private readonly ScenarioStructureNodeDetailService _scenarioStructureNodeDetailService = new();
    private readonly ScenarioStructureFilterService _scenarioStructureFilterService = new();
    private readonly ScenarioCommandReferenceNavigationService _scenarioCommandReferenceNavigationService = new();
    private readonly ScenarioCommandReferenceChecklistService _scenarioCommandReferenceChecklistService = new();    private readonly ScenarioCommandClipboardService _scenarioCommandClipboardService = new();
    private readonly ScenarioScriptSearchService _scenarioScriptSearchService = new();
    private readonly ScriptVariableUsageService _scriptVariableUsageService = new();
    private readonly ScriptVariableValueResolver _scriptVariableValueResolver = new();
    private LegacyMfcDialogDataSources? _legacyMfcDialogDataSources;
    private LegacyScenarioCommandDisplayFormatter? _legacyScenarioCommandDisplayFormatter;
    private readonly ResourceReplaceService _resourceReplaceService = new();
    private readonly E5ImageReplaceService _e5ImageReplaceService = new();
    private readonly IconResourceReplaceService _iconResourceReplaceService = new();
    private readonly EditableImageCodecService _editableImageCodecService = new();
    private readonly RImageReplaceService _rImageReplaceService = new();
    private readonly SImageReplaceService _sImageReplaceService = new();
    private readonly BatchRImageReplaceService _batchRImageReplaceService = new();
    private readonly BatchSImageReplaceService _batchSImageReplaceService = new();
    private readonly BmpImageExportService _bmpImageExportService = new();
    private readonly BatchItemIconImportService _batchItemIconImportService = new();
    private readonly BatchRoleFaceImportService _batchRoleFaceImportService = new();
    private readonly BatchStrategyIconImportService _batchStrategyIconImportService = new();
    private readonly BatchJobSImageReplaceService _batchJobSImageReplaceService = new();
    private readonly E5RoleRawNormalizeService _e5RoleRawNormalizeService = new();
    private readonly MapImageReplaceService _mapImageReplaceService = new();
    private readonly ImageAssignmentPreviewService _imageAssignmentPreviewService = new();
    private readonly ImageAssignmentFreeIdService _imageAssignmentFreeIdService;
    private readonly FieldAnnotationService _fieldAnnotationService = new();
    private readonly ProPatchParser _patchParser = new();
    private readonly PatchApplyService _patchService = new();    private readonly SceneStringParser _sceneStringParser = new();
    private readonly MaterialLibraryIndexer _materialLibraryIndexer = new();
    private readonly MaterialLibraryCache _materialLibraryCache;
    private readonly MapDraftService _mapDraftService = new();
    private readonly MapCanvasComposeService _mapCanvasComposeService = new();
    private readonly MapCanvasPublishService _mapCanvasPublishService = new();
    private readonly MapCanvasPreviewRenderer _mapCanvasPreviewRenderer = new();
    private readonly TerrainDrivenMapGenerationService _terrainDrivenMapGenerationService = new();
    private readonly MaterialDrivenTerrainService _materialDrivenTerrainService = new();
    private readonly MapMaterialExtractionService _mapMaterialExtractionService = new();
    private readonly MapResourceIndexer _mapResourceIndexer = new();
    private readonly ImageResourceCatalogService _imageResourceCatalogService = new();    private readonly TableReferenceLookupService _tableReferenceLookupService = new();
    private readonly RoleQuoteMappingService _roleQuoteMappingService = new();
    private readonly ImageAssignmentService _imageAssignmentService = new();
    private readonly EexArchiveReader _eexArchiveReader = new();
    private readonly EexEntryProbeReader _eexEntryProbeReader = new();
    private readonly EexByteHeatmapService _eexByteHeatmapService = new();
    private readonly EexEntryTreeDetailService _eexEntryTreeDetailService = new();
    private readonly EexCrossFileComparisonService _eexCrossFileComparisonService = new();
    private readonly ScenarioFileReader _scenarioFileReader = new();
    private readonly ScenarioCommandProbeReader _scenarioCommandProbeReader = new();
    private readonly ScenarioStructureProbeReader _scenarioStructureProbeReader = new();
    private readonly ScenarioTextReader _scenarioTextReader = new();
    private readonly ScenarioTextExportService _scenarioTextExportService = new();
    private readonly ScenarioTextWriter _scenarioTextWriter = new();
    private readonly ExclusiveSetScenarioService _exclusiveSetScenarioService = new();
    private readonly LegacyScenarioReader _legacyScenarioReader = new();
    private readonly LegacyScenarioWriter _legacyScenarioWriter = new();
    private readonly ScenarioCommandParameterTemplateService _scenarioCommandParameterTemplateService = new();
    private readonly LsResourceReader _lsResourceReader = new();
    private readonly HexzmapProbeReader _hexzmapProbeReader = new();
    private readonly HexzmapTerrainRenderService _hexzmapTerrainRenderService = new();
    private readonly LegacyHmMapReader _legacyHmMapReader = new();
    private readonly HexzmapEditorService _hexzmapEditorService = new();    private readonly BattlefieldEditorService _battlefieldEditorService = new();
    private readonly BattlefieldUnitReviewService _battlefieldUnitReviewService = new();
    private readonly BattlefieldDeploymentWriteService _battlefieldDeploymentWriteService = new();
    private readonly BattlefieldUnitStatusWriteService _battlefieldUnitStatusWriteService = new();
    private readonly BattlefieldAllyDeploymentSlotService _battlefieldAllyDeploymentSlotService = new();
    private readonly RSceneDraftService _rSceneDraftService = new();
    private readonly RSceneDialoguePreviewService _rSceneDialoguePreviewService = new();
    private readonly ItemIconPreviewService _itemIconPreviewService = new();
    private readonly ItemEffectCatalogService _itemEffectCatalogService = new();
    private readonly ItemEffectNameReader _itemEffectNameReader = new();
    private readonly ProjectEquipmentTypeProfileService _equipmentTypeProfileService = new();
    private readonly AccessoryJobGroupService _accessoryJobGroupService = new();
    private readonly ShopEditorService _shopEditorService = new();
    private readonly AttackAreaPreviewService _attackAreaPreviewService = new();
    private readonly StrategyAnimationPreviewService _strategyAnimationPreviewService = new();

    private CczProject? _project;
    private IReadOnlyList<HexTableDefinition> _tables = Array.Empty<HexTableDefinition>();
    private TableReadResult? _currentTableResult;
    private PatchDocument? _currentPatchDocument;
    private PatchPreviewResult? _currentPatchPreview;    private SceneStringDocument? _currentSceneStringDocument;
    private IReadOnlyList<MaterialAsset> _currentMaterialAssets = Array.Empty<MaterialAsset>();
    private IReadOnlyList<MapResourceItem> _currentMapResources = Array.Empty<MapResourceItem>();
    private IReadOnlyList<ImageResourceFileInfo> _currentImageResourceFiles = Array.Empty<ImageResourceFileInfo>();
    private IReadOnlyList<ImageResourceEntryInfo> _currentImageResourceEntries = Array.Empty<ImageResourceEntryInfo>();    private IReadOnlyList<EexArchiveInfo> _currentEexArchives = Array.Empty<EexArchiveInfo>();
    private IReadOnlyList<EexEntryProbeRow> _currentEexEntryProbeRows = Array.Empty<EexEntryProbeRow>();
    private EexCrossFileComparisonResult? _currentEexCrossFileComparison;
    private EexByteHeatmapResult? _currentEexByteHeatmap;
    private IReadOnlyList<ScenarioFileInfo> _currentScenarioFiles = Array.Empty<ScenarioFileInfo>();
    private IReadOnlyList<ScenarioCommandProbeRow> _currentScenarioCommandProbeRows = Array.Empty<ScenarioCommandProbeRow>();
    private ScenarioStructureProbeResult? _currentScenarioStructureResult;
    private IReadOnlyList<ScenarioTextEntry> _currentScenarioTextEntries = Array.Empty<ScenarioTextEntry>();
    private IReadOnlyList<ScenarioCommandReferenceTarget> _currentScenarioCommandReferenceTargets = Array.Empty<ScenarioCommandReferenceTarget>();
    private IReadOnlyList<ScenarioCommandTemplateCatalogItem> _currentScenarioCommandTemplateItems = Array.Empty<ScenarioCommandTemplateCatalogItem>();
    private IReadOnlyList<LsResourceInfo> _currentLsResources = Array.Empty<LsResourceInfo>();
    private EexByteHeatmapResult? _currentLsResourceHeatmap;
    private HexzmapProbeResult? _currentHexzmapProbe;
    private ScenarioStructureProbeResult? _currentScriptStructure;
    private LegacyScenarioDocument? _currentLegacyScriptDocument;
    private IReadOnlyList<ScenarioTextEntry> _currentScriptTextEntries = Array.Empty<ScenarioTextEntry>();
    private IReadOnlyList<ScenarioSearchResultRow> _currentScriptSearchResults = Array.Empty<ScenarioSearchResultRow>();
    private string _currentScriptSearchKeyword = string.Empty;
    private int _currentScriptSearchResultIndex = -1;
    private ScenarioFileInfo? _currentScriptScenario;
    private ScenarioStructureRow? _selectedScriptCommandRow;
    private ScenarioTextEntry? _selectedScriptTextEntry;
    private ScenarioCommandClipboardItem? _scriptCommandClipboardItem;
    private LegacyScenarioCommandNode? _legacyScriptCommandClipboard;
    private IReadOnlyList<LegacyScenarioCommandNode> _legacyScriptCommandClipboardItems = Array.Empty<LegacyScenarioCommandNode>();
    private IReadOnlyList<LegacyScenarioScene> _legacyScriptSceneClipboardItems = Array.Empty<LegacyScenarioScene>();
    private IReadOnlyList<LegacyScenarioSection> _legacyScriptSectionClipboardItems = Array.Empty<LegacyScenarioSection>();
    private string _legacyScriptCommandClipboardScenarioName = string.Empty;
    private string _legacyScriptCommandClipboardGameRoot = string.Empty;
    private bool _updatingScriptTreeChecks;
    private int _nextLegacyScriptSyntheticOffset = -1;
    private readonly Dictionary<string, LegacyScenarioCommandNode> _legacyScriptCommandByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ScenarioStructureRow> _legacyScriptRowByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<LegacyScenarioCommandNode, LegacyScenarioItemData> _legacyScriptItemDataByCommand = new();
    private readonly Dictionary<ScenarioStructureRow, LegacyScenarioItemData> _legacyScriptItemDataByRow = new();
    private readonly Dictionary<int, (LegacyScenarioCommandNode Command, LegacyScenarioCommandParameter Parameter)> _legacyScriptTextByOffset = new();
    private readonly Dictionary<int, ScenarioTextEntry> _legacyScriptTextEntryByOffset = new();
    private HexzmapBlockInfo? _terrainEditorBlock;
    private byte[] _terrainEditorCells = Array.Empty<byte>();
    private byte[] _terrainEditorOriginalCells = Array.Empty<byte>();
    private IReadOnlyDictionary<byte, string> _terrainEditorTerrainLookup = new Dictionary<byte, string>();    private BattlefieldEditorDocument? _currentBattlefieldDocument;
    private ScenarioStructureProbeResult? _currentBattlefieldScriptStructure;
    private LegacyScenarioDocument? _currentBattlefieldLegacyScriptDocument;
    private IReadOnlyList<ScenarioTextEntry> _currentBattlefieldScriptTextEntries = Array.Empty<ScenarioTextEntry>();
    private string _currentBattlefieldScriptSearchKeyword = string.Empty;
    private int _currentBattlefieldScriptSearchResultIndex = -1;
    private ScenarioTextEntry? _selectedBattlefieldScriptTextEntry;
    private ScenarioStructureRow? _selectedBattlefieldScriptCommandRow;
    private readonly Dictionary<string, LegacyScenarioCommandNode> _battlefieldScriptCommandByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<LegacyScenarioCommandNode, LegacyScenarioItemData> _battlefieldScriptItemDataByCommand = new();
    private readonly Dictionary<ScenarioStructureRow, LegacyScenarioItemData> _battlefieldScriptItemDataByRow = new();
    private readonly Dictionary<int, (LegacyScenarioCommandNode Command, LegacyScenarioCommandParameter Parameter)> _battlefieldScriptTextByOffset = new();
    private readonly Dictionary<int, ScenarioTextEntry> _battlefieldScriptTextEntryByOffset = new();
    private IReadOnlyList<BattlefieldUnitPaletteItem> _battlefieldUnitPaletteItems = Array.Empty<BattlefieldUnitPaletteItem>();
    private readonly List<BattlefieldPlacedUnit> _battlefieldPlacedUnits = new();
    private IReadOnlyList<BattlefieldAllyDeploymentSlot> _battlefieldAllyDeploymentSlots = Array.Empty<BattlefieldAllyDeploymentSlot>();
    private readonly Dictionary<string, BattlefieldUnitCandidate> _battlefieldUnitCandidatePreviewOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BattlefieldCommandCandidate> _battlefieldCommandCandidatePreviewOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BattlefieldPlacedUnit> _battlefieldScriptPreviewPlacementsByTargetKey = new(StringComparer.OrdinalIgnoreCase);
    private BattlefieldPlacedUnit? _selectedBattlefieldPlacedUnit;
    private BattlefieldPlacedUnit? _editingBattlefieldPlacedUnit;
    private BattlefieldPlacedUnit? _draggingBattlefieldPlacedUnit;
    private Point? _battlefieldPlacedUnitDragStart;
    private Point _battlefieldPlacedUnitOriginalGrid;
    private bool _battlefieldPlacedUnitDragMoved;
    private BattlefieldUnitPaletteItem? _selectedBattlefieldPaletteItem;
    private Point? _battlefieldUnitDragStart;
    private BattlefieldUnitPaletteItem? _battlefieldUnitDragItem;
    private readonly Dictionary<string, Bitmap> _battlefieldUnitFrameCache = new(StringComparer.Ordinal);
    private Bitmap? _battlefieldMapStaticPreviewImage;
    private (int Width, int Height) _battlefieldMapStaticGridSize;
    private BattlefieldUnitCandidate? _battlefieldMapPreviewSelectedUnit;
    private int _battlefieldHoverGridX = -1;
    private int _battlefieldHoverGridY = -1;
    private readonly List<BattlefieldCommand25Marker> _battlefieldCommand25Markers = [];
    private bool _battlefieldCommand25PreviewEnabled;
    private readonly System.Windows.Forms.Timer _battlefieldUnitAnimationTimer = new() { Interval = BattlefieldUnitAnimationIntervalMs };
    private int _battlefieldUnitAnimationPhase;
    private readonly System.Windows.Forms.Timer _rScenePlaybackTimer = new();
    private readonly System.Windows.Forms.Timer _jobStrategyAnimationTimer = new();
    private IReadOnlyList<Bitmap> _jobStrategyAnimationFrames = Array.Empty<Bitmap>();
    private int _jobStrategyAnimationFrameIndex;
    private ScenarioFileInfo? _currentRSceneScenario;
    private LegacyScenarioDocument? _currentRSceneLegacyScriptDocument;
    private IReadOnlyList<LegacyScenarioDocument> _currentRScenePrecedingVariableDocuments = Array.Empty<LegacyScenarioDocument>();
    private ScenarioStructureProbeResult? _currentRSceneScriptStructure;
    private IReadOnlyList<ScenarioTextEntry> _currentRSceneScriptTextEntries = Array.Empty<ScenarioTextEntry>();
    private readonly Dictionary<string, LegacyScenarioCommandNode> _rSceneScriptCommandByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<LegacyScenarioCommandNode, LegacyScenarioItemData> _rSceneScriptItemDataByCommand = new();
    private readonly Dictionary<ScenarioStructureRow, LegacyScenarioItemData> _rSceneScriptItemDataByRow = new();
    private readonly Stack<LegacyScenarioHistorySnapshot> _scriptUndoStack = new();
    private readonly Stack<LegacyScenarioHistorySnapshot> _scriptRedoStack = new();
    private readonly Stack<LegacyScenarioHistorySnapshot> _battlefieldScriptUndoStack = new();
    private readonly Stack<LegacyScenarioHistorySnapshot> _battlefieldScriptRedoStack = new();
    private readonly Stack<LegacyScenarioHistorySnapshot> _rSceneScriptUndoStack = new();
    private readonly Stack<LegacyScenarioHistorySnapshot> _rSceneScriptRedoStack = new();
    private IReadOnlyList<RSceneCommandCandidate> _currentRSceneCommandCandidates = Array.Empty<RSceneCommandCandidate>();
    private IReadOnlyList<RSceneStateCandidate> _currentRSceneStateCandidates = Array.Empty<RSceneStateCandidate>();
    private IReadOnlyList<ImageResourceEntryInfo> _currentRSceneBackgroundEntries = Array.Empty<ImageResourceEntryInfo>();
    private RSceneBackgroundReference? _currentRSceneBackgroundReference;
    private IReadOnlyList<RSceneActorPaletteItem> _rSceneActorPaletteItems = Array.Empty<RSceneActorPaletteItem>();
    private bool _bindingRSceneScriptTree;
    private LegacyScenarioCommandNode? _currentRSceneDialoguePreviewCommand;
    private string _currentRSceneDialoguePreviewMessage = string.Empty;
    private bool _rScenePreviewLocked;
    private ScenarioStructureRow? _rScenePreviewLockedRow;
    private LegacyScenarioCommandNode? _rScenePreviewLockedCommand;
    private ScenarioStructureRow? _rScenePreviewCurrentRow;
    private readonly List<RScenePlacedActor> _rScenePlacedActors = [];
    private RSceneActorPaletteItem? _selectedRScenePaletteItem;
    private RScenePlacedActor? _selectedRScenePlacedActor;
    private RScenePlacedActor? _editingRScenePlacedActor;
    private RScenePlacedActor? _draggingRScenePlacedActor;
    private Point? _rSceneFrameDragStart;
    private RSceneFrameDragPayload? _rSceneFrameDragPayload;
    private Point? _rScenePlacedActorDragStart;
    private Point _rScenePlacedActorOriginalGrid;
    private bool _rScenePlacedActorDragMoved;
    private RSceneFrameDragPayload? _rSceneDragPreviewPayload;
    private Point? _rSceneDragPreviewGrid;
    private RScenePlacedActor? _rSceneMovePreviewActor;
    private Point? _rSceneMovePreviewGrid;
    private readonly List<RSceneMapFaceState> _rSceneMapFaces = [];
    private readonly Dictionary<string, Bitmap> _rSceneImageCache = new(StringComparer.Ordinal);
    private IReadOnlyList<ScenarioStructureRow> _rScenePlaybackRows = Array.Empty<ScenarioStructureRow>();
    private int _rScenePlaybackIndex = -1;    private DataTable? _currentRoleEditorData;
    private DataTable? _roleEditorJobLookup;
    private IReadOnlyDictionary<int, string> _roleEditorJobNames = new Dictionary<int, string>();
    private TableReadResult? _roleBiographyRead;
    private TableReadResult? _roleCriticalQuoteRead;
    private TableReadResult? _roleRetreatQuoteRead;
    private DataTable? _currentJobEditorData;
    private TableReadResult? _jobNameRead;
    private TableReadResult? _jobDescriptionRead;
    private TableReadResult? _jobGrowthRead;
    private TableReadResult? _jobPierceRead;
    private ProjectEquipmentTypeProfile? _currentEquipmentTypeProfile;
    private IReadOnlyList<JobEquipmentPermissionSlotDefinition> _jobEquipmentPermissionSlots = Array.Empty<JobEquipmentPermissionSlotDefinition>();
    private AccessoryJobGroupProfile? _currentAccessoryJobGroupProfile;
    private IReadOnlyDictionary<int, string> _jobSeriesNames = new Dictionary<int, string>();
    private readonly Stack<List<JobEditorCellEdit>> _jobEditorUndoStack = new();
    private readonly Stack<List<JobEditorCellEdit>> _jobEditorRedoStack = new();
    private List<JobEditorCellTarget> _jobEditorSelectionSnapshotTargets = [];
    private List<JobEditorCellEdit> _jobEditorPendingCellEditOriginals = [];
    private bool _applyingJobEditorHistory;
    private bool _jobEditorSelectionChangeStartedByMouse;
    private DataRow? _jobEquipmentEditorBoundRow;
    private bool _bindingJobEquipmentEditor;
    private string _jobEquipmentEditorSlotSignature = string.Empty;
    private DataTable? _currentJobTerrainData;
    private TableReadResult? _jobSeriesRead;
    private TableReadResult? _jobTerrainPowerRead;
    private TableReadResult? _jobMoveCostRead;
    private TableReadResult? _jobRestraintRead;
    private DataTable? _currentJobStrategyData;
    private TableReadResult? _jobStrategyRead;
    private readonly Dictionary<string, TableReadResult> _jobStrategyCompanionReads = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<int, string> _jobStrategyJobNames = new Dictionary<int, string>();
    private readonly Dictionary<int, JobStrategyLearningDialog> _jobStrategyLearningDialogs = new();
    private DataRow? _jobStrategyLearningEditorBoundRow;
    private bool _bindingJobStrategyLearningEditor;
    private readonly DataTable _jobStrategyLearningEditorData = new("兵种策略学习等级");
    private int _jobStrategyConfiguredMagicCount;
    private string _jobStrategyConfiguredMagicSource = string.Empty;
    private DataTable? _currentJobEffectData;
    private HexTableDefinition? _jobEffectNameTable;
    private TableReadResult? _jobEffectDescriptionRead;
    private TableReadResult? _jobEffectAssignmentRead;
    private IReadOnlyDictionary<int, string> _jobEffectNames = new Dictionary<int, string>();
    private IReadOnlyDictionary<int, string> _jobEffectPersonNames = new Dictionary<int, string>();
    private IReadOnlyDictionary<int, string> _jobEffectJobNames = new Dictionary<int, string>();
    private TableReadResult? _rolePersonalEffectRead;
    private ExclusiveSetScenarioReadResult? _currentExclusiveSetScenarioRead;
    private IReadOnlyDictionary<int, string> _rolePersonalEffectNames = new Dictionary<int, string>();
    private IReadOnlyDictionary<int, string> _rolePersonalEffectPersonNames = new Dictionary<int, string>();
    private IReadOnlyDictionary<int, string> _rolePersonalEffectItemNames = new Dictionary<int, string>();
    private DataTable? _currentItemEditorData;
    private TableReadResult? _itemBaseLowRead;
    private TableReadResult? _itemBaseHighRead;
    private TableReadResult? _itemDescriptionLowRead;
    private TableReadResult? _itemDescriptionHighRead;
    private IReadOnlyDictionary<int, string> _itemEffectNames = new Dictionary<int, string>();
    private readonly Stack<List<ItemEditorCellEdit>> _itemEditorUndoStack = new();
    private readonly Stack<List<ItemEditorCellEdit>> _itemEditorRedoStack = new();
    private List<ItemEditorCellTarget> _itemEditorSelectionSnapshotTargets = [];
    private List<ItemEditorCellEdit> _itemEditorPendingCellEditOriginals = [];
    private bool _applyingItemEditorHistory;
    private bool _itemEditorSelectionChangeStartedByMouse;
    private DataTable? _currentShopEditorData;
    private TableReadResult? _shopCampaignNameRead;
    private TableReadResult? _shopDataRead;
    private IReadOnlyDictionary<int, string> _shopEditorPersonNames = new Dictionary<int, string>();
    private IReadOnlyDictionary<int, string> _shopEditorItemNames = new Dictionary<int, string>();
    private IReadOnlyDictionary<int, ShopItemInfo> _shopEditorItemInfos = new Dictionary<int, ShopItemInfo>();
    private DataTable? _currentImageAssignments;
    private string _imageAssignmentSummaryText = string.Empty;
    private MapResourceItem? _currentMapMakerItem;
    private MapWorkbenchDraft? _currentMapWorkbenchDraft;
    private MapWorkbenchSettings _mapWorkbenchSettings = new();
    private MaterialAsset? _mapMakerSelectedMaterial;
    private string _currentMaterialRoot = string.Empty;
    private bool _mapWorkbenchMaterialBrowserPopulated;
    private bool _populatingMapWorkbenchMaterialBrowser;
    private readonly Dictionary<string, Bitmap> _mapWorkbenchMaterialThumbnailCache = new(StringComparer.OrdinalIgnoreCase);
    private MapWorkbenchBrushMode _mapWorkbenchBrushMode = MapWorkbenchBrushMode.TerrainBrush;
    private readonly Stack<List<MapWorkbenchCellChange>> _mapMakerMapUndoStack = new();
    private readonly Stack<List<MapWorkbenchCellChange>> _mapMakerMapRedoStack = new();
    private readonly Stack<List<TerrainEditorCellChange>> _mapMakerTerrainUndoStack = new();
    private readonly Stack<List<TerrainEditorCellChange>> _mapMakerTerrainRedoStack = new();
    private readonly List<MapWorkbenchCellChange> _mapMakerPendingMapPaintChanges = new();
    private readonly HashSet<int> _mapMakerPendingMapPaintIndexes = new();
    private readonly List<TerrainEditorCellChange> _mapMakerPendingTerrainPaintChanges = new();
    private readonly HashSet<int> _mapMakerPendingTerrainPaintIndexes = new();
    private readonly Dictionary<int, MapCellOverride> _mapMakerMapCellOverrideLookup = new();
    private byte[] _mapMakerOriginalTerrainCells = Array.Empty<byte>();
    private Bitmap? _mapViewerRenderedImage;
    private Point? _mapMakerSelectionStartCell;
    private Point? _mapMakerSelectionEndCell;
    private Rectangle _mapMakerSelectedCellRange = Rectangle.Empty;
    private bool _mapMakerSelectingCells;
    private Point _mapViewerContextMenuCell = new(-1, -1);
    private string _selectedSceneryOverlayId = string.Empty;
    private MapSceneryOverlay? _sceneryDragOriginalOverlay;
    private PointF _sceneryDragStartImagePoint;
    private MapSceneryOverlayHitKind _sceneryDragHitKind = MapSceneryOverlayHitKind.None;
    private bool _sceneryOverlayDragging;
    private int _mapMakerTerrainChangedCellCount;
    private readonly System.Windows.Forms.Timer _mapMakerDirtyBaseRefreshTimer = new() { Interval = 200 };
    private readonly HashSet<int> _mapMakerDirtyTerrainPreviewIndexes = new();
    private System.Threading.CancellationTokenSource? _mapMakerBeautifyCts;
    private int _mapMakerBeautifyRequestId;
    private bool _mapMakerBeautifyRunning;
    private bool _mapMakerBeautifyStale;
    private bool _updatingMapMakerBeautifyFilterSelection;
    private long _mapMakerLastBaseRefreshMs;
    private long _mapMakerLastBeautifyMs;
    private int _mapMakerLastMaterialHitPercent;
    private TableReferenceNavigationTarget? _currentTableReferenceTarget;
    private UiLayoutSettings _uiLayoutSettings = new();
    private readonly Dictionary<string, SplitContainer> _uiLayoutSplits = new(StringComparer.Ordinal);
    private string _loadedProjectSessionKey = string.Empty;
    private string _lastMainTabText = string.Empty;
    private bool _updatingScenarioStructureSelection;
    private bool _updatingScenarioCommandTemplateFilters;
    private bool _updatingMapMakerPresetSelection;
    private bool _updatingBattlefieldScenarioSelection;
    private bool _updatingScriptScenarioSelection;
    private bool _loadingScriptScenarioList;
    private bool _loadingScriptScenarioDocument;
    private bool _bindingScriptDocument;
    private bool _loadingBattlefieldScenarioList;
    private bool _loadingBattlefieldScenarioDocument;
    private bool _reloadBattlefieldScenarioAfterCurrentLoad;
    private bool _bindingBattlefieldUnits;
    private bool _bindingBattlefieldControlPanel;
    private bool _bindingBattlefieldScriptEditor;
    private bool _updatingBattlefieldScriptSelection;
    private bool _editingBattlefieldLegacyCommandDialog;
    private bool _updatingRSceneScenarioSelection;
    private bool _loadingRSceneScenarioList;
    private bool _loadingRSceneScenarioDocument;
    private bool _bindingRSceneControlPanel;
    private bool _bindingRSceneFrameSelection;
    private bool _bindingRSceneCommandSelection;
    private bool _suppressRSceneCanvasRender;
    private ImageList? _rSceneFrameImageList;
    private int _battlefieldMapZoomPercent = 100;
    private int _rSceneCanvasZoomPercent = 100;
    private DateTime _lastMapMakerRenderUtc = DateTime.MinValue;
    private bool _mapMakerRenderDeferred;
    private string _battlefieldManualMarkerTargetKey = string.Empty;
    private int _battlefieldManualMarkerX = -1;
    private int _battlefieldManualMarkerY = -1;
    private bool _mapMakerPainting;
    private readonly List<TerrainEditorCellChange> _mapMakerPendingPaintChanges = new();
    private readonly HashSet<int> _mapMakerPendingPaintIndexes = new();

    private readonly Label _projectLabel = new();
    private readonly DataGridView _fileGrid = new();
    private readonly TextBox _projectFileSummaryBox = new();
    private readonly Button _refreshProjectFileStatusButton = new();
    private readonly ComboBox _tableList = new();
    private readonly TabControl _mainTabs = new();
    private TabControl? _jobEditorTabs;
    private readonly DataGridView _dataGrid = new();
    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _statusLabel = new();
    private readonly CheckBox _showAllTables = new();
    private readonly RadioButton _currentPageDecimalButton = new();
    private readonly RadioButton _currentPageHexButton = new();
    private readonly HashSet<Control> _numberBaseTrackedControls = new();
    private readonly HashSet<DataGridView> _numberBaseGrids = new();
    private readonly Button _openProjectButton = new();
    private readonly Button _reloadButton = new();
    private readonly Button _testCopyButton = new();
    private readonly Button _saveTableButton = new();
    private readonly Button _exportCsvButton = new();
    private readonly Button _importCsvButton = new();
    private readonly Button _copyTableSelectionButton = new();
    private readonly Button _pasteTableSelectionButton = new();
    private readonly Button _batchFillTableColumnButton = new();
    private readonly Button _batchModifyTableButton = new();
    private readonly Button _undoTableEditButton = new();
    private readonly Button _redoTableEditButton = new();
    private readonly Button _openPlanButton = new();
    private readonly Button _loadRoleEditorButton = new();
    private readonly Button _saveRoleEditorButton = new();
    private readonly Button _importRoleFaceButton = new();
    private readonly Button _batchImportRoleFaceButton = new();
    private readonly Button _exportRoleFaceBmpButton = new();
    private readonly Button _openRoleInTableEditorButton = new();
    private readonly Button _openRolePersonalEffectButton = new();
    private readonly Button _openRoleEffectButton = new();
    private readonly Button _openGlobalSettingsButton = new();
    private readonly Button _exportRoleEditorCsvButton = new();
    private readonly Button _importRoleEditorCsvButton = new();
    private readonly Button _copyRoleEditorSelectionButton = new();
    private readonly Button _pasteRoleEditorSelectionButton = new();
    private readonly Button _batchFillRoleEditorColumnButton = new();
    private readonly Button _filterRoleEditorButton = new();
    private readonly Button _clearRoleEditorFilterButton = new();
    private readonly Button _saveRoleTextDetailButton = new();
    private readonly TextBox _roleEditorSearchBox = new();
    private readonly DataGridView _roleEditorGrid = new();
    private readonly TextBox _roleEditorInfoBox = new();
    private readonly TextBox _roleBiographyBox = new();
    private readonly Label[] _roleCriticalQuoteLabels = Enumerable.Range(0, RoleQuoteMappingService.CriticalGenericGroupSize).Select(_ => new Label()).ToArray();
    private readonly TextBox[] _roleCriticalQuoteBoxes = Enumerable.Range(0, RoleQuoteMappingService.CriticalGenericGroupSize).Select(_ => new TextBox()).ToArray();
    private readonly TextBox _roleRetreatQuoteBox = new();
    private readonly TextBox _roleTextDetailInfoBox = new();
    private readonly Button _loadJobEditorButton = new();
    private readonly Button _saveJobEditorButton = new();
    private readonly Button _editAccessoryJobGroupsButton = new();
    private readonly Button _replaceJobSImageButton = new();
    private readonly Button _batchReplaceJobSImageButton = new();
    private readonly Button _exportJobSImageBmpButton = new();
    private readonly Button _openJobSeriesTableButton = new();
    private readonly Button _openJobEffectTableButton = new();
    private readonly Button _exportJobEditorCsvButton = new();
    private readonly Button _importJobEditorCsvButton = new();
    private readonly Button _copyJobEditorSelectionButton = new();
    private readonly Button _pasteJobEditorSelectionButton = new();
    private readonly Button _batchFillJobEditorColumnButton = new();
    private readonly Button _undoJobEditorButton = new();
    private readonly Button _redoJobEditorButton = new();
    private readonly Button _filterJobEditorButton = new();
    private readonly Button _clearJobEditorFilterButton = new();
    private readonly TextBox _jobEditorSearchBox = new();
    private readonly DataGridView _jobEditorGrid = new();
    private readonly PictureBox _jobAreaPreviewBox = new();
    private readonly TextBox _jobAreaPreviewInfoBox = new();
    private readonly Panel _jobEquipmentEditorPanel = new();
    private readonly TableLayoutPanel _jobEquipmentCheckGrid = new();
    private readonly Label _jobEquipmentEditorTitleLabel = new();
    private readonly Label _jobEquipmentEditorStatusLabel = new();
    private readonly ToolTip _jobEquipmentEditorToolTip = new();
    private readonly Dictionary<string, CheckBox> _jobEquipmentEditorChecks = new(StringComparer.Ordinal);
    private readonly TextBox _jobEditorInfoBox = new();
    private readonly Button _loadJobTerrainButton = new();
    private readonly Button _saveJobTerrainButton = new();
    private readonly Button _filterJobTerrainButton = new();
    private readonly Button _clearJobTerrainFilterButton = new();
    private readonly Button _openJobRestraintTableButton = new();
    private readonly TextBox _jobTerrainSearchBox = new();
    private readonly DataGridView _jobTerrainGrid = new();
    private readonly TextBox _jobTerrainInfoBox = new();
    private readonly Button _loadJobMatrixButton = new();
    private readonly Button _saveJobMatrixButton = new();
    private readonly Button _openJobMatrixRestraintTableButton = new();
    private readonly DataGridView _jobRestraintGrid = new();
    private readonly TextBox _jobMatrixInfoBox = new();
    private readonly Button _loadJobStrategyEditorButton = new();
    private readonly Button _saveJobStrategyEditorButton = new();
    private readonly Button _importJobStrategyIconButton = new();
    private readonly Button _editJobStrategyIconButton = new();
    private readonly Button _exportJobStrategyIconBmpButton = new();
    private readonly Button _openJobStrategyTableButton = new();
    private readonly Button _filterJobStrategyEditorButton = new();
    private readonly Button _clearJobStrategyEditorFilterButton = new();
    private readonly TextBox _jobStrategyEditorSearchBox = new();
    private readonly DataGridView _jobStrategyEditorGrid = new();
    private readonly TextBox _jobStrategyEditorInfoBox = new();
    private readonly PictureBox _jobStrategyPreviewBox = new();
    private readonly TextBox _jobStrategyPreviewInfoBox = new();
    private readonly Panel _jobStrategyLearningEditorPanel = new();
    private readonly DataGridView _jobStrategyLearningEditorGrid = new();
    private readonly Label _jobStrategyLearningEditorTitleLabel = new();
    private readonly Label _jobStrategyLearningEditorStatusLabel = new();
    private readonly Button _loadJobEffectEditorButton = new();
    private readonly Button _saveJobEffectEditorButton = new();
    private readonly Button _openJobExclusiveEffectTableButton = new();
    private readonly Button _filterJobEffectEditorButton = new();
    private readonly Button _clearJobEffectEditorFilterButton = new();
    private readonly TextBox _jobEffectEditorSearchBox = new();
    private readonly DataGridView _jobEffectEditorGrid = new();
    private readonly TextBox _jobEffectEditorInfoBox = new();
    private readonly Button _loadItemEditorButton = new();
    private readonly Button _saveItemEditorButton = new();
    private readonly Button _openItemEffectCatalogButton = new();
    private readonly Button _exportItemEditorCsvButton = new();
    private readonly Button _importItemEditorCsvButton = new();
    private readonly Button _copyItemEditorSelectionButton = new();
    private readonly Button _pasteItemEditorSelectionButton = new();
    private readonly Button _batchFillItemEditorColumnButton = new();
    private readonly Button _batchImportItemIconButton = new();
    private readonly Button _editItemIconButton = new();
    private readonly Button _exportItemIconBmpButton = new();
    private readonly Button _undoItemEditorButton = new();
    private readonly Button _redoItemEditorButton = new();
    private readonly Button _filterItemEditorButton = new();
    private readonly Button _clearItemEditorFilterButton = new();
    private readonly TextBox _itemEditorSearchBox = new();
    private readonly DataGridView _itemEditorGrid = new();
    private readonly TextBox _itemEditorInfoBox = new();
    private readonly SplitContainer _itemIconPreviewSplit = new();
    private readonly Panel _itemIconLargePreviewScrollPanel = new();
    private readonly Panel _itemIconSmallPreviewScrollPanel = new();
    private readonly Label _itemIconLargePreviewTitle = new();
    private readonly Label _itemIconSmallPreviewTitle = new();
    private readonly PictureBox _itemIconPreviewBox = new();
    private readonly PictureBox _itemIconSmallPreviewBox = new();
    private readonly TextBox _itemIconPreviewInfoBox = new();
    private Bitmap? _itemIconLargeSourceBitmap;
    private Bitmap? _itemIconSmallSourceBitmap;
    private int _itemIconLargeZoomPercent;
    private int _itemIconSmallZoomPercent;
    private readonly Button _loadShopEditorButton = new();
    private readonly Button _saveShopEditorButton = new();
    private readonly Button _exportShopEditorCsvButton = new();
    private readonly Button _importShopEditorCsvButton = new();
    private readonly Button _copyShopEditorSelectionButton = new();
    private readonly Button _pasteShopEditorSelectionButton = new();
    private readonly Button _batchFillShopEditorColumnButton = new();
    private readonly Button _filterShopEditorButton = new();
    private readonly Button _clearShopEditorFilterButton = new();
    private readonly TextBox _shopEditorSearchBox = new();
    private readonly ComboBox _shopBatchScopeCombo = new();
    private readonly ComboBox _shopBatchSlotCombo = new();
    private readonly ComboBox _shopBatchSetItemCombo = new();
    private readonly ComboBox _shopBatchFindItemCombo = new();
    private readonly ComboBox _shopBatchReplaceItemCombo = new();
    private readonly Button _shopBatchSetButton = new();
    private readonly Button _shopBatchClearButton = new();
    private readonly Button _shopBatchReplaceButton = new();
    private readonly DataGridView _shopEditorGrid = new();
    private readonly TextBox _shopEditorInfoBox = new();
    private readonly ComboBox _chartColumnCombo = new();
    private readonly Button _renderChartButton = new();
    private readonly PictureBox _tableChartBox = new();
    private readonly TextBox _tableChartInfoBox = new();
    private readonly TextBox _fieldAnnotationBox = new();
    private readonly TextBox _tableColumnFilterBox = new();
    private readonly Button _filterTableColumnsButton = new();
    private readonly Button _clearTableColumnFilterButton = new();
    private readonly CheckBox _dangerTableColumnsOnly = new();
    private readonly Button _exportFieldAnnotationsButton = new();
    private readonly Button _exportVisibleColumnsCsvButton = new();
    private readonly CheckBox _visibleColumnsCsvWithNotes = new();
    private readonly Button _jumpTableReferenceButton = new();
    private readonly TextBox _tableReferenceNavigationBox = new();
    private readonly TextBox _tableRowFilterBox = new();
    private readonly Button _filterTableRowsButton = new();
    private readonly Button _clearTableRowFilterButton = new();
    private readonly CheckBox _changedTableRowsOnly = new();
    private readonly CheckBox _tableRowSearchVisibleColumnsOnly = new();
    private readonly TextBox _patchPathBox = new();
    private readonly ComboBox _patchTargetCombo = new();
    private readonly Button _selectPatchButton = new();
    private readonly Button _previewPatchButton = new();
    private readonly Button _applyPatchButton = new();
    private readonly DataGridView _patchGrid = new();
    private readonly TextBox _patchInfoBox = new();
    private readonly TextBox _movePathBox = new();
    private readonly Button _selectMoveButton = new();
    private readonly Button _previewMoveButton = new();
    private readonly DataGridView _moveGrid = new();
    private readonly TextBox _moveInfoBox = new();
    private readonly Button _loadSceneDictionaryButton = new();
    private readonly TextBox _sceneDictionaryInfoBox = new();
    private readonly DataGridView _sceneCommandGrid = new();
    private readonly DataGridView _sceneGroupGrid = new();
    private readonly Button _indexMaterialLibraryButton = new();
    private readonly DataGridView _materialGrid = new();
    private readonly PictureBox _materialPreview = new();
    private readonly TextBox _materialInfoBox = new();
    private readonly Button _indexGameResourcesButton = new();
    private readonly Button _openGameResourceButton = new();
    private readonly Button _replaceGameResourceButton = new();
    private readonly Button _restoreGameResourceBackupButton = new();
    private readonly Button _exportGameResourcesCsvButton = new();
    private readonly ComboBox _gameResourceCategoryFilterCombo = new();
    private readonly TextBox _gameResourceSearchBox = new();
    private readonly Button _filterGameResourcesButton = new();
    private readonly Button _clearGameResourceFilterButton = new();
    private readonly DataGridView _gameResourceGrid = new();
    private readonly PictureBox _gameResourcePreview = new();
    private readonly TextBox _gameResourceInfoBox = new();
    private readonly Button _loadMapImagesButton = new();
    private readonly ListBox _mapImageList = new();
    private readonly PictureBox _mapViewerBox = new();
    private readonly Label _mapViewerCellPreviewLabel = new();
    private readonly TextBox _mapViewerInfoBox = new();
    private readonly TrackBar _mapZoomTrackBar = new();
    private readonly Button _mapFitButton = new();
    private readonly Button _mapActualButton = new();
    private readonly Button _mapMakerNewDraftButton = new();
    private readonly Button _mapMakerLoadLastDraftButton = new();
    private readonly Button _mapMakerSaveDraftButton = new();
    private readonly NumericUpDown _mapMakerGridWidthInput = new();
    private readonly NumericUpDown _mapMakerGridHeightInput = new();
    private readonly ComboBox _mapMakerBrushModeCombo = new();
    private readonly Button _mapMakerSelectMaterialRootButton = new();
    private readonly ComboBox _mapMakerMaterialCategoryCombo = new();
    private readonly TextBox _mapMakerMaterialSearchBox = new();
    private readonly Button _mapMakerFilterMaterialsButton = new();
    private readonly Button _mapMakerClearMaterialFilterButton = new();
    private readonly DataGridView _mapMakerMaterialGrid = new();
    private readonly PictureBox _mapMakerMaterialPreview = new();
    private readonly TextBox _mapMakerMaterialInfoBox = new();
    private readonly TreeView _mapMakerMaterialTree = new();
    private readonly ListView _mapMakerMaterialListView = new();
    private readonly ImageList _mapMakerMaterialImageList = new();
    private readonly Button _mapMakerRollbackBeautifyButton = new();
    private readonly CheckBox _mapMakerShowTerrainCheckBox = new();
    private readonly CheckBox _mapMakerShowGridCheckBox = new();
    private readonly CheckBox _mapMakerEditTerrainCheckBox = new();
    private readonly CheckBox _mapMakerAutoGenerateCheckBox = new();
    private readonly Button _mapMakerBeautifyCheckBox = new();
    private readonly ComboBox _mapMakerBeautifyFilterCombo = new();
    private readonly NumericUpDown _mapMakerBeautifyStrengthInput = new();
    private readonly NumericUpDown _mapMakerFeatherRadiusInput = new();
    private readonly TrackBar _mapMakerTerrainOpacityTrackBar = new();
    private readonly Label _mapMakerTerrainOpacityLabel = new();
    private readonly ComboBox _mapMakerTerrainPresetCombo = new();
    private readonly NumericUpDown _mapMakerTerrainBrushInput = new();
    private readonly Label _mapMakerBrushNameLabel = new();
    private readonly Button _mapMakerSaveTerrainButton = new();
    private readonly Button _mapMakerUndoTerrainButton = new();
    private readonly Button _mapMakerRedoTerrainButton = new();
    private readonly Button _mapMakerReplaceMapImageButton = new();
    private readonly Button _mapMakerExportPreviewButton = new();
    private readonly Button _mapMakerExportJpgButton = new();
    private readonly Button _mapMakerExtractMaterialButton = new();
    private readonly Button _mapMakerMaterialPlanButton = new();
    private readonly Button _mapMakerPublishMapButton = new();
    private readonly Button _mapMakerPublishTerrainButton = new();
    private readonly Button _mapMakerPublishAllButton = new();
    private readonly ContextMenuStrip _mapViewerContextMenu = new();
    private readonly Button _loadImageResourcesButton = new();
    private readonly Button _openImageResourceButton = new();
    private readonly Button _replaceImageResourceEntryButton = new();
    private readonly Button _editImageResourceEntryButton = new();
    private readonly Button _restoreImageResourceEntryButton = new();
    private readonly Button _batchImportImageResourceEntriesButton = new();
    private readonly Button _batchClearImageResourceEntriesButton = new();
    private readonly Button _normalizeRoleRawImagesButton = new();
    private readonly Button _exportImageResourceEntriesButton = new();
    private readonly ComboBox _imageResourceCategoryFilterCombo = new();
    private readonly TextBox _imageResourceSearchBox = new();
    private readonly Button _filterImageResourcesButton = new();
    private readonly Button _clearImageResourceFilterButton = new();
    private readonly DataGridView _imageResourceFileGrid = new();
    private readonly DataGridView _imageResourceEntryGrid = new();
    private readonly PictureBox _imageResourcePreviewBox = new();
    private readonly TextBox _imageResourceInfoBox = new();
    private readonly TextBox _imageResourceEntryInfoBox = new();
    private readonly Button _loadImageAssignmentsButton = new();
    private readonly Button _saveImageAssignmentsButton = new();
    private readonly Button _queryFreeFaceIdsButton = new();
    private readonly Button _queryFreeRImageIdsButton = new();
    private readonly Button _queryFreeSImageIdsButton = new();
    private readonly Button _openRsDirectoryButton = new();
    private readonly TextBox _imageAssignmentSearchBox = new();
    private readonly CheckBox _imageAssignmentMissingOnlyCheckBox = new();
    private readonly Button _filterImageAssignmentsButton = new();
    private readonly Button _clearImageAssignmentFilterButton = new();
    private readonly Button _locateImageResourceButton = new();
    private readonly Button _replaceImageResourceButton = new();
    private readonly Button _editRImageResourceButton = new();
    private readonly Button _editSImageResourceButton = new();
    private readonly Button _replaceRImageSetButton = new();
    private readonly Button _replaceSImageSetButton = new();
    private readonly Button _batchReplaceRImageSetButton = new();
    private readonly Button _batchReplaceSImageSetButton = new();
    private readonly Button _importImageAssignmentFaceButton = new();
    private readonly Button _batchImportImageAssignmentFaceButton = new();
    private readonly Button _exportRImageBmpButton = new();
    private readonly Button _exportSImageBmpButton = new();
    private readonly Button _exportImageAssignmentFaceBmpButton = new();
    private readonly Button _restoreImageResourceButton = new();
    private readonly Button _exportMissingImageResourcesButton = new();
    private readonly DataGridView _imageAssignmentGrid = new();
    private readonly TextBox _imageAssignmentInfoBox = new();
    private readonly PictureBox _imageAssignmentFacePreviewBox = new();
    private readonly PictureBox _imageAssignmentRPreviewBox = new();
    private readonly PictureBox _imageAssignmentSPreviewBox = new();
    private readonly ComboBox _imageAssignmentSPreviewFactionCombo = new();
    private readonly TextBox _imageAssignmentPreviewInfoBox = new();
    private readonly Button _loadEexArchivesButton = new();
    private readonly Button _openEexArchiveButton = new();
    private readonly Button _exportEexArchivesCsvButton = new();
    private readonly Button _probeEexEntriesButton = new();
    private readonly Button _exportEexEntryProbeCsvButton = new();
    private readonly Button _compareEexCrossFilesButton = new();
    private readonly Button _renderEexHeatmapButton = new();
    private readonly Button _exportEexHeatmapPngButton = new();
    private readonly ComboBox _eexArchiveCategoryFilterCombo = new();
    private readonly TextBox _eexArchiveSearchBox = new();
    private readonly Button _filterEexArchivesButton = new();
    private readonly Button _clearEexArchiveFilterButton = new();
    private readonly DataGridView _eexArchiveGrid = new();
    private readonly DataGridView _eexEntryProbeGrid = new();
    private readonly TreeView _eexEntryTree = new();
    private readonly TextBox _eexEntryTreeInfoBox = new();
    private readonly DataGridView _eexCrossFileGrid = new();
    private readonly TextBox _eexCrossFileInfoBox = new();
    private readonly PictureBox _eexByteHeatmapBox = new();
    private readonly TextBox _eexByteHeatmapInfoBox = new();
    private readonly TextBox _eexArchiveInfoBox = new();
    private readonly Button _loadScenarioFilesButton = new();
    private readonly Button _openScenarioFileButton = new();
    private readonly Button _exportScenarioFileIndexCsvButton = new();
    private readonly ComboBox _scenarioKindFilterCombo = new();
    private readonly TextBox _scenarioFileSearchBox = new();
    private readonly Button _filterScenarioFilesButton = new();
    private readonly Button _clearScenarioFileFilterButton = new();
    private readonly CheckBox _scenarioFilesWithTextOnly = new();
    private readonly Button _probeScenarioCommandsButton = new();
    private readonly Button _buildScenarioStructureButton = new();
    private readonly Button _exportScenarioStructureXmlButton = new();
    private readonly Button _exportScenarioCommandTemplateCatalogButton = new();
    private readonly TextBox _scenarioStructureFilterBox = new();
    private readonly Button _filterScenarioStructureButton = new();
    private readonly Button _clearScenarioStructureFilterButton = new();
    private readonly CheckBox _scenarioStructureTemplatesOnly = new();
    private readonly CheckBox _scenarioStructureTextOnly = new();
    private readonly CheckBox _scenarioStructureMapOnly = new();
    private readonly CheckBox _scenarioStructureHighRiskOnly = new();
    private readonly ComboBox _scenarioCommandReferenceCombo = new();
    private readonly Button _jumpScenarioCommandReferenceButton = new();
    private readonly Button _exportScenarioCommandReferenceChecklistButton = new();
    private readonly Button _probeScenarioTextsButton = new();
    private readonly Button _exportScenarioTextsButton = new();
    private readonly Button _saveScenarioTextsButton = new();
    private readonly TextBox _scenarioTextFilterBox = new();
    private readonly Button _scenarioTextFilterButton = new();
    private readonly Button _scenarioTextFilterClearButton = new();
    private readonly CheckBox _scenarioTextChangedOnly = new();
    private readonly Button _loadBattlefieldButton = new();
    private readonly ComboBox _battlefieldScenarioCombo = new();
    private readonly Button _saveBattlefieldTextsButton = new();
    private readonly Button _saveBattlefieldUnitReviewsButton = new();
    private readonly Button _writeBattlefieldDeploymentButton = new();
    private readonly Button _jumpBattlefieldMapButton = new();
    private readonly Button _jumpBattlefieldScenarioButton = new();
    private readonly TextBox _battlefieldTitleBox = new();
    private readonly Label _battlefieldTitleBytesLabel = new();
    private readonly TextBox _battlefieldConditionsBox = new();
    private readonly Label _battlefieldConditionsBytesLabel = new();
    private readonly PictureBox _battlefieldMapPreviewBox = new();
    private readonly Label _battlefieldMapHintLabel = new();
    private readonly Label _battlefieldMapZoomLabel = new();
    private readonly Button _battlefieldMapZoomResetButton = new();
    private readonly Button _markBattlefieldCommand25Button = new();
    private readonly Panel _battlefieldMapScrollPanel = new();
    private readonly TabControl _battlefieldLeftTabs = new();
    private readonly TreeView _battlefieldScriptTree = new();
    private readonly TextBox _battlefieldScriptSearchBox = new();
    private readonly Button _battlefieldScriptSearchButton = new();
    private readonly Button _battlefieldScriptClearSearchButton = new();
    private readonly TextBox _battlefieldScriptTextBox = new();
    private readonly Label _battlefieldScriptTextCapacityLabel = new();
    private readonly Button _saveBattlefieldScriptTextButton = new();
    private readonly Button _saveBattlefieldScriptStructureButton = new();
    private readonly Button _showBattlefieldVariablesButton = new();
    private readonly DataGridView _battlefieldScriptParameterGrid = new();
    private readonly TextBox _battlefieldScriptParameterValueBox = new();
    private readonly Button _applyBattlefieldScriptParameterButton = new();
    private readonly Button _editBattlefieldScriptParametersButton = new();
    private readonly TextBox _battlefieldScriptDetailBox = new();
    private readonly ListBox _battlefieldUnitListBox = new();
    private readonly PictureBox _battlefieldUnitPreviewBox = new();
    private readonly TextBox _battlefieldUnitPreviewInfoBox = new();
    private readonly RadioButton _battlefieldFactionAllyRadio = new();
    private readonly RadioButton _battlefieldFactionFriendRadio = new();
    private readonly RadioButton _battlefieldFactionEnemyRadio = new();
    private readonly CheckBox _battlefieldHiddenCheckBox = new();
    private readonly NumericUpDown _battlefieldLevelOffsetInput = new();
    private readonly ComboBox _battlefieldLevelModeCombo = new();
    private readonly ComboBox _battlefieldAiModeCombo = new();
    private readonly ComboBox _battlefieldDirectionCombo = new();
    private readonly Button _battlefieldRemovePlacedUnitButton = new();
    private readonly Button _battlefieldClearPlacedUnitsButton = new();
    private readonly TextBox _battlefieldUnitPaletteFilterBox = new();
    private readonly TextBox _battlefieldUnitFilterBox = new();
    private readonly ComboBox _battlefieldUnitCategoryFilterCombo = new();
    private readonly Button _filterBattlefieldUnitsButton = new();
    private readonly Button _clearBattlefieldUnitFilterButton = new();
    private readonly Button _markBattlefieldUnitReviewedButton = new();
    private readonly Button _markBattlefieldUnitNeedsChangeButton = new();
    private readonly Button _jumpBattlefieldUnitScriptButton = new();
    private readonly DataGridView _battlefieldUnitGrid = new();
    private readonly DataGridView _battlefieldCommandGrid = new();
    private readonly TextBox _battlefieldInfoBox = new();
    private readonly Button _loadRSceneButton = new();
    private readonly ComboBox _rSceneScenarioCombo = new();
    private readonly Button _saveRSceneDraftButton = new();
    private readonly Button _saveRSceneScriptStructureButton = new();
    private readonly Button _showRSceneVariablesButton = new();
    private readonly Button _jumpRSceneScriptButton = new();
    private readonly TreeView _rSceneScriptTree = new();
    private readonly TextBox _rSceneScriptSearchBox = new();
    private string _currentRSceneScriptSearchKeyword = string.Empty;
    private int _currentRSceneScriptSearchResultIndex = -1;
    private readonly TextBox _rSceneScriptDetailBox = new();
    private readonly LegacyMfcDialogHostControl _rSceneInlineDialogHost = new();
    private readonly Button _applyRSceneInlineDialogButton = new();
    private readonly Button _resetRSceneInlineDialogButton = new();
    private readonly DataGridView _rSceneCommandGrid = new();
    private readonly TextBox _rSceneActorFilterBox = new();
    private readonly ListBox _rSceneActorListBox = new();
    private readonly ListView _rSceneFrameListView = new();
    private readonly Panel _rSceneCanvasScrollPanel = new();
    private readonly PictureBox _rSceneCanvasBox = new();
    private readonly Label _rSceneCanvasHintLabel = new();
    private readonly Label _rSceneZoomLabel = new();
    private readonly Button _rSceneZoomResetButton = new();
    private readonly Button _rScenePreviewLockButton = new();
    private readonly ComboBox _rSceneBackgroundCombo = new();
    private readonly NumericUpDown _rSceneGridSizeInput = new();
    private readonly CheckBox _rSceneShowGridCheckBox = new();
    private readonly CheckBox _rSceneDialoguePreviewCheckBox = new();
    private readonly ComboBox _rSceneFacingCombo = new();
    private readonly NumericUpDown _rSceneStanceInput = new();
    private readonly Button _rScenePlaybackButton = new();
    private readonly NumericUpDown _rScenePlaybackDelayInput = new();
    private readonly Label _rScenePlaybackStatusLabel = new();
    private readonly Button _loadScriptButton = new();
    private readonly ComboBox _scriptScenarioCombo = new();
    private readonly TextBox _scriptSearchBox = new();
    private readonly Button _scriptSearchButton = new();
    private readonly Button _scriptClearSearchButton = new();
    private readonly Button _showScriptVariablesButton = new();
    private readonly Button _locateScriptCommandButton = new();
    private readonly Button _copyScriptCommandButton = new();
    private readonly Button _cutScriptCommandButton = new();
    private readonly Button _previewPasteScriptCommandButton = new();
    private readonly ComboBox _scriptNewCommandCombo = new();
    private readonly Button _appendScriptCommandToSectionButton = new();
    private readonly Button _insertScriptCommandBeforeButton = new();
    private readonly Button _insertScriptCommandAfterButton = new();
    private readonly Button _appendScriptCommandToChildBlockButton = new();
    private readonly Button _deleteScriptCommandButton = new();
    private readonly Button _pasteScriptCommandBeforeButton = new();
    private readonly Button _pasteScriptCommandAfterButton = new();
    private readonly Button _moveScriptCommandUpButton = new();
    private readonly Button _moveScriptCommandDownButton = new();
    private readonly Button _saveScriptTextButton = new();
    private readonly Button _saveScriptStructureButton = new();
    private readonly Button _jumpScriptBattlefieldButton = new();
    private readonly TreeView _scriptTree = new();
    private readonly ContextMenuStrip _scriptTreeContextMenu = new();
    private readonly ToolStripMenuItem _scriptContextAppendSectionItem = new("添加到本节正文末尾");
    private readonly ToolStripMenuItem _scriptContextInsertBeforeItem = new("在此命令前插入");
    private readonly ToolStripMenuItem _scriptContextInsertAfterItem = new("在此命令后插入");
    private readonly ToolStripMenuItem _scriptContextAppendChildItem = new("追加到子块");
    private readonly ToolStripMenuItem _scriptContextDeleteItem = new("删除命令");
    private readonly ToolStripMenuItem _scriptContextEditItem = new("编辑当前对象");
    private readonly ToolStripMenuItem _scriptContextApplyParameterItem = new("修改参数...");
    private readonly ToolStripMenuItem _scriptContextSaveTextItem = new("保存当前文本");
    private readonly ToolStripMenuItem _scriptContextCopyItem = new("复制命令");
    private readonly ToolStripMenuItem _scriptContextPreviewPasteItem = new("粘贴预览");
    private readonly ToolStripMenuItem _scriptContextPasteBeforeItem = new("粘贴到前面");
    private readonly ToolStripMenuItem _scriptContextPasteAfterItem = new("粘贴到后面");
    private readonly ToolStripMenuItem _scriptContextMoveUpItem = new("上移命令");
    private readonly ToolStripMenuItem _scriptContextMoveDownItem = new("下移命令");
    private readonly ContextMenuStrip _legacyScriptTreeContextMenu = new();
    private readonly ToolStripMenuItem _legacyScriptContextEditItem = new("修改(&E)\tCtrl+E");
    private readonly ToolStripMenuItem _legacyScriptContextAddBeforeItem = new("在上方添加(&A)\tCtrl+Shift+I");
    private readonly ToolStripMenuItem _legacyScriptContextAddItem = new("在下方添加(&I)\tCtrl+I");
    private readonly ToolStripMenuItem _legacyScriptContextAddSubEventBeforeItem = new("在上方添加子事件(&B)\tCtrl+Shift+O");
    private readonly ToolStripMenuItem _legacyScriptContextAddSubEventItem = new("在下方添加子事件(&S)\tCtrl+O");
    private readonly ToolStripMenuItem _legacyScriptContextDuplicateItem = new("步进复制(&D)\tCtrl+D");
    private readonly ToolStripMenuItem _legacyScriptContextDeleteItem = new("删除(&D)\tDelete");
    private readonly ToolStripMenuItem _legacyScriptContextMoveUpItem = new("上移(&U)\tCtrl+Up");
    private readonly ToolStripMenuItem _legacyScriptContextMoveDownItem = new("下移(&D)\tCtrl+Down");
    private readonly ToolStripMenuItem _legacyScriptContextUndoItem = new("撤销(&Z)\tCtrl+Z");
    private readonly ToolStripMenuItem _legacyScriptContextRedoItem = new("前进(&Y)\tCtrl+Y");
    private readonly ToolStripMenuItem _legacyScriptContextCutItem = new("剪切(&T)\tCtrl+X");
    private readonly ToolStripMenuItem _legacyScriptContextCopyItem = new("复制(&C)\tCtrl+C");
    private readonly ToolStripMenuItem _legacyScriptContextPasteItem = new("粘贴(&P)\tCtrl+V");
    private readonly ToolStripMenuItem _legacyScriptContextTextImportItem = new("文本导入...") { Tag = "TextImport" };
    private readonly ToolStripMenuItem _legacyScriptContextExpandItem = new("全部展开\tCtrl+Q");
    private readonly ToolStripMenuItem _legacyScriptContextJumpItem = new("跳转到...");
    private readonly ContextMenuStrip _battlefieldScriptTreeContextMenu = new();
    private readonly ContextMenuStrip _rSceneScriptTreeContextMenu = new();
    private readonly DataGridView _scriptCommandGrid = new();
    private readonly DataGridView _scriptParameterGrid = new();
    private readonly TextBox _scriptParameterValueBox = new();
    private readonly Button _applyScriptParameterValueButton = new();
    private readonly Button _editScriptParametersButton = new();
    private readonly LegacyMfcDialogHostControl _scriptInlineDialogHost = new();
    private readonly Button _applyScriptInlineDialogButton = new();
    private readonly Button _resetScriptInlineDialogButton = new();
    private readonly DataGridView _scriptTextGrid = new();
    private readonly DataGridView _scriptSearchResultGrid = new();
    private readonly TabControl _scriptLowerLeftTabs = new();
    private readonly TextBox _scriptTextEditorBox = new();
    private readonly Label _scriptTextCapacityLabel = new();
    private readonly Label _scriptHeaderLabel = new();
    private readonly Panel _scriptHintPanel = new();
    private readonly Button _scriptToggleHintButton = new();
    private readonly TextBox _scriptPreviewBox = new();
    private readonly PictureBox _scriptImagePreviewBox = new();
    private readonly TextBox _scriptImagePreviewInfoBox = new();
    private readonly TextBox _scriptDetailBox = new();
    private ScriptVariableUsageDialog? _scriptVariableUsageDialog;
    private LegacyScriptEditorScope _scriptVariableUsageScope = LegacyScriptEditorScope.Script;
    private ScriptVariableProjectScanResult? _scriptVariableProjectCache;
    private string _scriptVariableProjectCacheKey = string.Empty;
    private readonly DataGridView _scenarioFileGrid = new();
    private readonly DataGridView _scenarioCommandProbeGrid = new();
    private readonly DataGridView _scenarioStructureGrid = new();
    private readonly TreeView _scenarioStructureTree = new();
    private readonly TextBox _scenarioStructureNodeInfoBox = new();
    private readonly TextBox _scenarioStructureXmlBox = new();
    private readonly DataGridView _scenarioCommandTemplateGrid = new();
    private readonly TextBox _scenarioCommandTemplateInfoBox = new();
    private readonly TextBox _scenarioCommandTemplateSearchBox = new();
    private readonly ComboBox _scenarioCommandTemplateCategoryCombo = new();
    private readonly ComboBox _scenarioCommandTemplateStatusCombo = new();
    private readonly Button _refreshScenarioCommandTemplatesButton = new();
    private readonly Button _filterScenarioCommandTemplatesButton = new();
    private readonly Button _clearScenarioCommandTemplateFilterButton = new();
    private readonly Button _showScenarioCommandTemplateInStructureButton = new();
    private readonly DataGridView _scenarioTextGrid = new();
    private readonly TextBox _scenarioFileInfoBox = new();
    private readonly Button _loadLsResourcesButton = new();
    private readonly Button _openLsResourceButton = new();
    private readonly Button _exportLsResourcesCsvButton = new();
    private readonly Button _renderLsResourceHeatmapButton = new();
    private readonly Button _exportLsResourceHeatmapPngButton = new();
    private readonly ComboBox _lsResourceCategoryFilterCombo = new();
    private readonly TextBox _lsResourceSearchBox = new();
    private readonly Button _filterLsResourcesButton = new();
    private readonly Button _clearLsResourceFilterButton = new();
    private readonly DataGridView _lsResourceGrid = new();
    private readonly TextBox _lsResourceInfoBox = new();
    private readonly TextBox _lsResourceHeatmapInfoBox = new();
    private readonly PictureBox _lsResourceHeatmapBox = new();
    private readonly Button _loadHexzmapProbeButton = new();
    private readonly Button _exportHexzmapProbeCsvButton = new();
    private readonly Button _exportHexzmapOverlayPngButton = new();
    private readonly CheckBox _hexzmapOverlayMapCheckBox = new();
    private readonly TrackBar _hexzmapOverlayOpacityTrackBar = new();
    private readonly Label _hexzmapOverlayOpacityLabel = new();
    private readonly DataGridView _hexzmapGrid = new();
    private readonly PictureBox _hexzmapPreviewBox = new();
    private readonly Label _hexzmapCellPreviewLabel = new();
    private readonly TextBox _hexzmapInfoBox = new();

    private enum MapWorkbenchBrushMode
    {
        Browse,
        MapBrush,
        TerrainBrush,
        BuildingBrush,
        SceneryBrush
    }

    private enum MapSceneryOverlayHitKind
    {
        None,
        Body,
        ScaleNorthWest,
        ScaleNorthEast,
        ScaleSouthEast,
        ScaleSouthWest,
        Rotate
    }

    private sealed record TerrainEditorCellChange(int Index, byte OldValue, byte NewValue);

    private sealed record MapWorkbenchCellChange(
        int Index,
        MapCellOverride? OldValue,
        MapCellOverride? NewValue,
        MapSceneryOverlay? OldSceneryOverlay = null,
        MapSceneryOverlay? NewSceneryOverlay = null);

    private sealed record MapMaterialExtractionTargetComboItem(MapMaterialExtractionTargetType TargetType, string DisplayText)
    {
        public override string ToString() => DisplayText;
    }

    private sealed record RSceneBackgroundComboItem(ImageResourceEntryInfo Entry)
    {
        public int ImageNumber => Entry.ImageNumber;
        public string DisplayText => $"{Entry.ImageNumber:D3} {Entry.Kind} {Entry.Usage}";
    }

    private sealed record TerrainEditorPreset(byte Id, string Name)
    {
        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? HexDisplayFormatter.Format(Id, 2) : $"{HexDisplayFormatter.Format(Id, 2)}  {Name}";
    }

    private sealed class TerrainMaterialPlanRow
    {
        public byte TerrainId { get; init; }
        public string Terrain { get; init; } = string.Empty;
        public string VisualFamily { get; init; } = string.Empty;
        public string CurrentMaterial { get; init; } = string.Empty;
        public string SelectionMode { get; init; } = string.Empty;
        public int CandidateCount { get; init; }
    }

    private sealed class TerrainMaterialCandidateRow
    {
        public required MaterialAsset Asset { get; init; }
        public string DisplayText => $"{Asset.Category}/{Asset.FileName}    HexTag={Asset.HexTag}    {Asset.Description}";
        public override string ToString() => DisplayText;
    }

    private sealed record BeautifyFilterComboItem(string Profile, string DisplayText)
    {
        public override string ToString() => DisplayText;
    }

    public MainForm()
    {
        _imageAssignmentFreeIdService = new ImageAssignmentFreeIdService(_imageAssignmentPreviewService);
        _materialLibraryCache = new MaterialLibraryCache(_materialLibraryIndexer);
        Text = "CCZModStudio 6.5 - V0.6 集成原型";
        Icon = LoadApplicationIcon();
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScroll = true;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        StartPosition = FormStartPosition.CenterScreen;
        ApplyAdaptiveDefaultWindowLayout();

        EncodingService.EnsureCodePages();
        LoadUiLayoutSettings();
        ApplyWindowLayoutSettings();
        BuildLayout();
        WireEvents();
    }

    private static Icon? LoadApplicationIcon()
    {
        var associatedIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (associatedIcon != null)
        {
            return associatedIcon;
        }

        var bundledIconPath = PortableInstallPaths.AboutAsset("Doro-white.ico");
        if (File.Exists(bundledIconPath))
        {
            return new Icon(bundledIconPath);
        }

        return null;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        RunPackageSelfCheck();
        LoadDefaultProject();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.S &&
            keyData.HasFlag(Keys.Control) &&
            !keyData.HasFlag(Keys.Alt) &&
            !keyData.HasFlag(Keys.Shift))
        {
            _ = SaveCurrentScenarioByShortcutAsync();
            return true;
        }

        if ((keyData & Keys.KeyCode) == Keys.L &&
            keyData.HasFlag(Keys.Control) &&
            !keyData.HasFlag(Keys.Alt) &&
            !keyData.HasFlag(Keys.Shift) &&
            TryShowScriptVariableUsageDialogForCurrentPage())
        {
            return true;
        }

        if ((keyData & Keys.KeyCode) == Keys.Z &&
            keyData.HasFlag(Keys.Control) &&
            !keyData.HasFlag(Keys.Alt))
        {
            if (!IsLegacyScenarioEditorPageSelected())
            {
                return base.ProcessCmdKey(ref msg, keyData);
            }

            if (keyData.HasFlag(Keys.Shift))
            {
                RedoCurrentScenarioEdit();
            }
            else
            {
                UndoCurrentScenarioEdit();
            }

            return true;
        }

        if ((keyData & Keys.KeyCode) == Keys.Y &&
            keyData.HasFlag(Keys.Control) &&
            !keyData.HasFlag(Keys.Alt) &&
            !keyData.HasFlag(Keys.Shift))
        {
            if (!IsLegacyScenarioEditorPageSelected())
            {
                return base.ProcessCmdKey(ref msg, keyData);
            }

            RedoCurrentScenarioEdit();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private bool _savingScenarioByShortcut;

    private bool IsLegacyScenarioEditorPageSelected()
        => _mainTabs.SelectedTab?.Text is "剧本编辑" or "战场编辑" or "场景编辑";

    private bool TryShowScriptVariableUsageDialogForCurrentPage()
    {
        switch (_mainTabs.SelectedTab?.Text)
        {
            case "剧本编辑":
                ShowScriptVariableUsageDialog(LegacyScriptEditorScope.Script);
                return true;
            case "战场编辑":
                ShowScriptVariableUsageDialog(LegacyScriptEditorScope.Battlefield);
                return true;
            case "场景编辑":
                ShowScriptVariableUsageDialog(LegacyScriptEditorScope.RScene);
                return true;
            default:
                return false;
        }
    }

    private async Task SaveCurrentScenarioByShortcutAsync()
    {
        if (_savingScenarioByShortcut)
        {
            SetStatus("剧本保存仍在进行。");
            return;
        }

        _savingScenarioByShortcut = true;
        try
        {
            switch (_mainTabs.SelectedTab?.Text)
            {
                case "剧本编辑":
                    await SaveCurrentLegacyScriptStructureAsync();
                    break;
                case "战场编辑":
                    await SaveCurrentBattlefieldLegacyScriptStructureAsync();
                    break;
                case "场景编辑":
                    await SaveCurrentRSceneLegacyScriptStructureAsync();
                    break;
                default:
                    SetStatus("Ctrl+S：请切换到剧本编辑、战场编辑或场景编辑页保存剧本。");
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Ctrl+S 保存剧本失败：" + ex);
            MessageBox.Show(this, ex.Message, "保存剧本失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _savingScenarioByShortcut = false;
        }
    }

    private void BuildLayout()
    {
        using var perf = TracePerf("BuildLayout");
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var topBar = new Panel
        {
            Dock = DockStyle.Fill,
            Height = 34,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        var toolbar = new FlowLayoutPanel
        {
            AutoSize = false,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Height = 34
        };

        _openProjectButton.Text = "打开项目目录";
        _openProjectButton.AutoSize = true;
        _reloadButton.Text = "重新读取";
        _reloadButton.AutoSize = true;
        _testCopyButton.Text = "创建测试副本";
        _testCopyButton.AutoSize = true;
        _saveTableButton.Text = "保存当前表";
        _saveTableButton.AutoSize = true;
        _saveTableButton.Enabled = false;
        _exportCsvButton.Text = "导出CSV";
        _exportCsvButton.AutoSize = true;
        _exportCsvButton.Enabled = false;
        _importCsvButton.Text = "导入CSV";
        _importCsvButton.AutoSize = true;
        _importCsvButton.Enabled = false;
        _copyTableSelectionButton.Text = "复制";
        _copyTableSelectionButton.AutoSize = true;
        _pasteTableSelectionButton.Text = "粘贴";
        _pasteTableSelectionButton.AutoSize = true;
        _batchFillTableColumnButton.Text = "批量填列";
        _batchFillTableColumnButton.AutoSize = true;
        _batchModifyTableButton.Text = "Batch modify";
        _batchModifyTableButton.AutoSize = true;
        _batchModifyTableButton.Enabled = false;
        _undoTableEditButton.Text = "Undo";
        _undoTableEditButton.AutoSize = true;
        _undoTableEditButton.Enabled = false;
        _redoTableEditButton.Text = "Redo";
        _redoTableEditButton.AutoSize = true;
        _redoTableEditButton.Enabled = false;
        _openPlanButton.Text = "打开 plan.md";
        _openPlanButton.AutoSize = true;
        _showAllTables.Text = "显示全部版本/禁用表";
        _showAllTables.AutoSize = true;
        _showAllTables.Margin = new Padding(18, 8, 3, 3);

        toolbar.Controls.AddRange(new Control[]
        {
            _openProjectButton,
            _reloadButton,
            _testCopyButton,
            _saveTableButton,
            _exportCsvButton,
            _importCsvButton,
            _copyTableSelectionButton,
            _pasteTableSelectionButton,
            _batchFillTableColumnButton,
            _batchModifyTableButton,
            _undoTableEditButton,
            _redoTableEditButton,
            _openPlanButton
        });

        var numberBasePanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 2, 0, 0)
        };
        var numberBaseLabel = new Label
        {
            Text = "数字：",
            AutoSize = true,
            Padding = new Padding(0, 7, 0, 0)
        };
        _currentPageDecimalButton.Text = "10";
        _currentPageDecimalButton.AutoSize = true;
        _currentPageDecimalButton.Checked = true;
        _currentPageDecimalButton.Margin = new Padding(0, 3, 0, 3);
        _currentPageHexButton.Text = "16";
        _currentPageHexButton.AutoSize = true;
        _currentPageHexButton.Margin = new Padding(4, 3, 0, 3);
        numberBasePanel.Controls.AddRange(new Control[]
        {
            numberBaseLabel,
            _currentPageDecimalButton,
            _currentPageHexButton
        });
        topBar.Controls.Add(toolbar);
        topBar.Controls.Add(numberBasePanel);
        void LayoutTopBar()
        {
            var numberWidth = numberBasePanel.PreferredSize.Width;
            var numberHeight = Math.Min(topBar.ClientSize.Height, Math.Max(1, numberBasePanel.PreferredSize.Height));
            var numberLeft = Math.Max(0, topBar.ClientSize.Width - numberWidth);
            numberBasePanel.SetBounds(numberLeft, 0, numberWidth, numberHeight);
            toolbar.SetBounds(0, 0, Math.Max(0, numberLeft - 8), topBar.ClientSize.Height);
        }

        topBar.Resize += (_, _) => LayoutTopBar();
        numberBasePanel.SizeChanged += (_, _) => LayoutTopBar();
        LayoutTopBar();
        root.Controls.Add(topBar, 0, 0);

        _projectLabel.AutoSize = true;
        _projectLabel.Dock = DockStyle.Fill;
        _projectLabel.Padding = new Padding(0, 6, 0, 6);
        _projectLabel.Text = "项目：未加载";
        root.Controls.Add(_projectLabel, 0, 1);

        _mainTabs.Dock = DockStyle.Fill;
        root.Controls.Add(_mainTabs, 0, 2);

        _mainTabs.TabPages.Add(BuildXiaoAnMessagePage());
        _mainTabs.TabPages.Add(BuildRoleEditorPage());
        _mainTabs.TabPages.Add(BuildJobEditorPage());
        _mainTabs.TabPages.Add(BuildItemEditorPage());

        if (ShowGenericTableEditorPage)
        {
            var tablePage = new TabPage("数据表编辑");
            var tableGridLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1
            };
            tableGridLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tableGridLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var tableSelectToolbar = CreateToolbarRow();
            _tableList.DropDownStyle = ComboBoxStyle.DropDownList;
            ConfigureToolbarInput(_tableList, 360, 220);
            _tableList.DropDownWidth = 560;
            _tableList.MaxDropDownItems = 24;
            ConfigureToolbarCheckBox(_showAllTables);
            tableSelectToolbar.Controls.AddRange(new Control[]
            {
                CreateToolbarLabel("数据表：", 0),
                _tableList,
                _showAllTables
            });
            tableGridLayout.Controls.Add(tableSelectToolbar, 0, 0);
            var tableSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
            };
            ConfigureSplitContainerDistanceAfterLayout("BuildLayout.GenericTableChart", tableSplit, desiredDistance: 500, desiredPanel1Min: 25, desiredPanel2Min: 25);
            _dataGrid.Dock = DockStyle.Fill;
            _dataGrid.ReadOnly = true;
            _dataGrid.AllowUserToAddRows = false;
            _dataGrid.AllowUserToDeleteRows = false;
            _dataGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
            _dataGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _dataGrid.MultiSelect = true;
            tableGridLayout.Controls.Add(_dataGrid, 0, 1);
            AddCollapsibleSplitPanel(tableSplit, 1, "数据表", tableGridLayout, "BuildLayout.GenericTableChart.Table");
            var chartLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1
            };
            chartLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            chartLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var chartToolbar = CreateToolbarStack(3);
            _chartColumnCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            ConfigureToolbarInput(_chartColumnCombo, 180, 130);
            _renderChartButton.Text = "绘制分布图";
            ConfigureToolbarButton(_renderChartButton, 104);
            _renderChartButton.Enabled = false;
            ConfigureToolbarInput(_tableColumnFilterBox, 150, 120);
            _tableColumnFilterBox.PlaceholderText = "按字段/注释筛选列";
            _filterTableColumnsButton.Text = "筛选列";
            ConfigureToolbarButton(_filterTableColumnsButton, 72);
            _filterTableColumnsButton.Enabled = false;
            _clearTableColumnFilterButton.Text = "显示全部列";
            ConfigureToolbarButton(_clearTableColumnFilterButton, 104);
            _clearTableColumnFilterButton.Enabled = false;
            _dangerTableColumnsOnly.Text = "仅高风险字段";
            ConfigureToolbarCheckBox(_dangerTableColumnsOnly);
            _dangerTableColumnsOnly.Enabled = false;
            _exportFieldAnnotationsButton.Text = "导出字段注释";
            ConfigureToolbarButton(_exportFieldAnnotationsButton, 118);
            _exportFieldAnnotationsButton.Enabled = false;
            _exportVisibleColumnsCsvButton.Text = "导出可见行列CSV";
            ConfigureToolbarButton(_exportVisibleColumnsCsvButton, 140);
            _exportVisibleColumnsCsvButton.Enabled = false;
            _visibleColumnsCsvWithNotes.Text = "含字段说明行";
            ConfigureToolbarCheckBox(_visibleColumnsCsvWithNotes);
            _visibleColumnsCsvWithNotes.Checked = true;
            _visibleColumnsCsvWithNotes.Enabled = false;
            _jumpTableReferenceButton.Text = "跳到引用目标";
            ConfigureToolbarButton(_jumpTableReferenceButton, 118);
            _jumpTableReferenceButton.Enabled = false;
            ConfigureToolbarInput(_tableReferenceNavigationBox, 360, 200);
            _tableReferenceNavigationBox.ReadOnly = true;
            _tableReferenceNavigationBox.PlaceholderText = "选中引用字段后显示可跳转目标";
            ConfigureToolbarInput(_tableRowFilterBox, 150, 120);
            _tableRowFilterBox.PlaceholderText = "按行内容筛选";
            _filterTableRowsButton.Text = "筛选行";
            ConfigureToolbarButton(_filterTableRowsButton, 72);
            _filterTableRowsButton.Enabled = false;
            _clearTableRowFilterButton.Text = "显示全部行";
            ConfigureToolbarButton(_clearTableRowFilterButton, 104);
            _clearTableRowFilterButton.Enabled = false;
            _changedTableRowsOnly.Text = "仅已改动行";
            ConfigureToolbarCheckBox(_changedTableRowsOnly);
            _changedTableRowsOnly.Enabled = false;
            _tableRowSearchVisibleColumnsOnly.Text = "只搜可见列";
            ConfigureToolbarCheckBox(_tableRowSearchVisibleColumnsOnly);
            _tableRowSearchVisibleColumnsOnly.Checked = true;
            _tableRowSearchVisibleColumnsOnly.Enabled = false;
            AddToolbarRow(chartToolbar, 0,
                CreateToolbarLabel("数值列：", 0),
                _chartColumnCombo,
                _renderChartButton,
                CreateToolbarLabel("列筛选："),
                _tableColumnFilterBox,
                _filterTableColumnsButton,
                _clearTableColumnFilterButton,
                _dangerTableColumnsOnly);
            AddToolbarRow(chartToolbar, 1,
                _exportFieldAnnotationsButton,
                _exportVisibleColumnsCsvButton,
                _visibleColumnsCsvWithNotes,
                CreateToolbarLabel("关联："),
                _jumpTableReferenceButton,
                _tableReferenceNavigationBox);
            AddToolbarRow(chartToolbar, 2,
                CreateToolbarLabel("行筛选：", 0),
                _tableRowFilterBox,
                _filterTableRowsButton,
                _clearTableRowFilterButton,
                _changedTableRowsOnly,
                _tableRowSearchVisibleColumnsOnly);
            _tableChartInfoBox.Width = 520;
            _tableChartInfoBox.ReadOnly = true;
            chartLayout.Controls.Add(chartToolbar, 0, 0);
            _fieldAnnotationBox.Dock = DockStyle.Fill;
            _fieldAnnotationBox.Multiline = true;
            _fieldAnnotationBox.Height = 84;
            _fieldAnnotationBox.ReadOnly = true;
            _fieldAnnotationBox.ScrollBars = ScrollBars.Vertical;
            _fieldAnnotationBox.WordWrap = true;
            _fieldAnnotationBox.Text = "字段说明：请选择数据表和单元格。";
            _tableChartBox.Dock = DockStyle.Fill;
            _tableChartBox.BorderStyle = BorderStyle.FixedSingle;
            _tableChartBox.SizeMode = PictureBoxSizeMode.Zoom;
            chartLayout.Controls.Add(_tableChartBox, 0, 1);
            AddCollapsibleSplitPanel(tableSplit, 2, "字段/图表", chartLayout, "BuildLayout.GenericTableChart.Chart");
            tablePage.Controls.Add(tableSplit);
            _mainTabs.TabPages.Add(tablePage);
        }
        var imagePage = new TabPage("图片设定");
        var imageTabs = new TabControl
        {
            Dock = DockStyle.Fill
        };
        imagePage.Controls.Add(imageTabs);

        var imageResourcePage = new TabPage("图片资源");
        var imageResourceLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(6)
        };
        imageResourceLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        imageResourceLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        imageResourcePage.Controls.Add(imageResourceLayout);

        var imageResourceToolbar = CreateToolbarStack(2);
        _loadImageResourcesButton.Text = "读取图片资源";
        ConfigureToolbarButton(_loadImageResourcesButton, 118);
        _openImageResourceButton.Text = "定位文件";
        ConfigureToolbarButton(_openImageResourceButton, 88);
        _replaceImageResourceEntryButton.Text = "替换E5条目";
        ConfigureToolbarButton(_replaceImageResourceEntryButton, 104);
        _editImageResourceEntryButton.Text = "像素编辑";
        ConfigureToolbarButton(_editImageResourceEntryButton, 88);
        _restoreImageResourceEntryButton.Text = "从备份还原";
        ConfigureToolbarButton(_restoreImageResourceEntryButton, 104);
        _batchImportImageResourceEntriesButton.Text = "批量导入";
        ConfigureToolbarButton(_batchImportImageResourceEntriesButton, 88);
        _batchClearImageResourceEntriesButton.Text = "批量删除";
        ConfigureToolbarButton(_batchClearImageResourceEntriesButton, 88);
        _normalizeRoleRawImagesButton.Text = "角色RAW统一";
        ConfigureToolbarButton(_normalizeRoleRawImagesButton, 104);
        _exportImageResourceEntriesButton.Text = "导出条目CSV";
        ConfigureToolbarButton(_exportImageResourceEntriesButton, 118);
        _imageResourceCategoryFilterCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        ConfigureToolbarInput(_imageResourceCategoryFilterCombo, 112, 100);
        ConfigureToolbarInput(_imageResourceSearchBox, 210, 150);
        _imageResourceSearchBox.PlaceholderText = "筛选文件/别名/用途";
        _filterImageResourcesButton.Text = "筛选";
        ConfigureToolbarButton(_filterImageResourcesButton, 72);
        _clearImageResourceFilterButton.Text = "显示全部";
        ConfigureToolbarButton(_clearImageResourceFilterButton, 88);
        AddToolbarRow(imageResourceToolbar, 0,
            _loadImageResourcesButton,
            _openImageResourceButton,
            _replaceImageResourceEntryButton,
            _editImageResourceEntryButton,
            _restoreImageResourceEntryButton,
            _batchImportImageResourceEntriesButton,
            _batchClearImageResourceEntriesButton,
            _normalizeRoleRawImagesButton,
            _exportImageResourceEntriesButton);
        AddToolbarRow(imageResourceToolbar, 1,
            CreateToolbarLabel("分类：", 0),
            _imageResourceCategoryFilterCombo,
            _imageResourceSearchBox,
            _filterImageResourcesButton,
            _clearImageResourceFilterButton);
        imageResourceLayout.Controls.Add(imageResourceToolbar, 0, 0);

        var imageResourceSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };
        ConfigureSplitContainerDistanceAfterLayout("BuildImageResourcePage.ListPreview", imageResourceSplit, desiredDistance: 720, desiredPanel1Min: 420, desiredPanel2Min: 320);

        var imageResourceLeft = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };
        ConfigureSplitContainerDistanceAfterLayout("BuildImageResourcePage.FileEntryList", imageResourceLeft, desiredDistance: 260, desiredPanel1Min: 150, desiredPanel2Min: 150);
        _imageResourceFileGrid.Dock = DockStyle.Fill;
        _imageResourceFileGrid.ReadOnly = true;
        _imageResourceFileGrid.AllowUserToAddRows = false;
        _imageResourceFileGrid.AllowUserToDeleteRows = false;
        _imageResourceFileGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _imageResourceFileGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _imageResourceEntryGrid.Dock = DockStyle.Fill;
        _imageResourceEntryGrid.ReadOnly = true;
        _imageResourceEntryGrid.AllowUserToAddRows = false;
        _imageResourceEntryGrid.AllowUserToDeleteRows = false;
        _imageResourceEntryGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _imageResourceEntryGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        AddCollapsibleSplitPanel(imageResourceLeft, 1, "资源文件", _imageResourceFileGrid, "BuildImageResourcePage.FileEntryList.Files");
        AddCollapsibleSplitPanel(imageResourceLeft, 2, "资源条目", _imageResourceEntryGrid, "BuildImageResourcePage.FileEntryList.Entries");

        _imageResourcePreviewBox.Dock = DockStyle.Fill;
        _imageResourcePreviewBox.BackColor = Color.FromArgb(32, 32, 36);
        _imageResourcePreviewBox.BorderStyle = BorderStyle.None;
        _imageResourcePreviewBox.SizeMode = PictureBoxSizeMode.Zoom;
        imageResourceSplit.Panel1.Controls.Add(imageResourceLeft);
        AddCollapsibleSplitPanel(imageResourceSplit, 2, "图片预览", _imageResourcePreviewBox, "BuildImageResourcePage.ListPreview.Preview");
        imageResourceLayout.Controls.Add(imageResourceSplit, 0, 1);
        imageTabs.TabPages.Add(imageResourcePage);

        var imageAssignmentPage = new TabPage("人物形象设定");
        var imageLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(6)
        };
        imageLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        imageLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        imageAssignmentPage.Controls.Add(imageLayout);
        var imageToolbarLayout = CreateToolbarStack(2);
        _loadImageAssignmentsButton.Text = "读取人物形象";
        ConfigureToolbarButton(_loadImageAssignmentsButton, 104);
        _saveImageAssignmentsButton.Text = "保存形象";
        ConfigureToolbarButton(_saveImageAssignmentsButton, 72);
        _saveImageAssignmentsButton.Enabled = false;
        _queryFreeFaceIdsButton.Text = "查询空闲头像";
        ConfigureToolbarButton(_queryFreeFaceIdsButton, 118);
        _queryFreeFaceIdsButton.Enabled = false;
        _queryFreeRImageIdsButton.Text = "查询空闲R形象编号";
        ConfigureToolbarButton(_queryFreeRImageIdsButton, 150);
        _queryFreeRImageIdsButton.Enabled = false;
        _queryFreeSImageIdsButton.Text = "查询空闲S形象编号";
        ConfigureToolbarButton(_queryFreeSImageIdsButton, 150);
        _queryFreeSImageIdsButton.Enabled = false;
        _openRsDirectoryButton.Text = "打开RS目录";
        ConfigureToolbarButton(_openRsDirectoryButton, 104);
        _openRsDirectoryButton.Visible = false;
        ConfigureToolbarInput(_imageAssignmentSearchBox, 220, 160);
        _imageAssignmentSearchBox.PlaceholderText = "筛选人物/职业/R编号/S编号/资源状态";
        _imageAssignmentMissingOnlyCheckBox.Text = "仅缺失资源";
        ConfigureToolbarCheckBox(_imageAssignmentMissingOnlyCheckBox);
        _imageAssignmentSPreviewFactionCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        ConfigureToolbarInput(_imageAssignmentSPreviewFactionCombo, 92, 88);
        _imageAssignmentSPreviewFactionCombo.Items.AddRange(new object[] { "我军", "友军", "敌军" });
        _imageAssignmentSPreviewFactionCombo.SelectedIndex = 0;
        _filterImageAssignmentsButton.Text = "筛选";
        ConfigureToolbarButton(_filterImageAssignmentsButton, 72);
        _clearImageAssignmentFilterButton.Text = "显示全部";
        ConfigureToolbarButton(_clearImageAssignmentFilterButton, 88);
        _clearImageAssignmentFilterButton.Enabled = false;
        _locateImageResourceButton.Text = "\u5b9a\u4f4d\u9009\u4e2d\u8d44\u6e90";
        ConfigureToolbarButton(_locateImageResourceButton, 104);
        _replaceImageResourceButton.Text = "导入/替换E5";
        ConfigureToolbarButton(_replaceImageResourceButton, 104);
        _editRImageResourceButton.Text = "编辑R形象";
        ConfigureToolbarButton(_editRImageResourceButton, 104);
        _editSImageResourceButton.Text = "编辑S形象";
        ConfigureToolbarButton(_editSImageResourceButton, 104);
        _replaceRImageSetButton.Text = "一键替换R形象";
        ConfigureToolbarButton(_replaceRImageSetButton, 118);
        _replaceSImageSetButton.Text = "一键替换S形象";
        ConfigureToolbarButton(_replaceSImageSetButton, 118);
        _batchReplaceRImageSetButton.Text = "批量导入R形象";
        ConfigureToolbarButton(_batchReplaceRImageSetButton, 118);
        _batchReplaceSImageSetButton.Text = "批量导入S形象";
        ConfigureToolbarButton(_batchReplaceSImageSetButton, 118);
        _importImageAssignmentFaceButton.Text = "一键导入头像";
        ConfigureToolbarButton(_importImageAssignmentFaceButton, 118);
        _importImageAssignmentFaceButton.Enabled = false;
        _batchImportImageAssignmentFaceButton.Text = "批量导入头像";
        ConfigureToolbarButton(_batchImportImageAssignmentFaceButton, 118);
        _batchImportImageAssignmentFaceButton.Enabled = false;
        _exportRImageBmpButton.Text = "\u5bfc\u51faR BMP";
        ConfigureToolbarButton(_exportRImageBmpButton, 104);
        _exportRImageBmpButton.Enabled = false;
        _exportSImageBmpButton.Text = "\u5bfc\u51faS BMP";
        ConfigureToolbarButton(_exportSImageBmpButton, 104);
        _exportSImageBmpButton.Enabled = false;
        _exportImageAssignmentFaceBmpButton.Text = "\u5bfc\u51fa\u5934\u50cfBMP";
        ConfigureToolbarButton(_exportImageAssignmentFaceBmpButton, 118);
        _exportImageAssignmentFaceBmpButton.Enabled = false;
        _restoreImageResourceButton.Text = "还原E5条目";
        ConfigureToolbarButton(_restoreImageResourceButton, 104);
        _exportMissingImageResourcesButton.Text = "\u5bfc\u51fa\u7f3a\u5931\u62a5\u544a";
        ConfigureToolbarButton(_exportMissingImageResourcesButton, 118);
        AddToolbarRow(imageToolbarLayout, 0,
            _loadImageAssignmentsButton,
            _saveImageAssignmentsButton,
            _queryFreeFaceIdsButton,
            _queryFreeRImageIdsButton,
            _queryFreeSImageIdsButton,
            _imageAssignmentSearchBox,
            _imageAssignmentMissingOnlyCheckBox,
            CreateToolbarLabel("S预览阵营："),
            _imageAssignmentSPreviewFactionCombo,
            _filterImageAssignmentsButton,
            _clearImageAssignmentFilterButton);
        AddToolbarRow(imageToolbarLayout, 1,
            _locateImageResourceButton,
            _replaceImageResourceButton,
            _editRImageResourceButton,
            _editSImageResourceButton,
            _replaceRImageSetButton,
            _replaceSImageSetButton,
            _batchReplaceRImageSetButton,
            _batchReplaceSImageSetButton,
            _importImageAssignmentFaceButton,
            _batchImportImageAssignmentFaceButton,
            _exportRImageBmpButton,
            _exportSImageBmpButton,
            _exportImageAssignmentFaceBmpButton,
            _restoreImageResourceButton,
            _exportMissingImageResourcesButton);
        imageLayout.Controls.Add(imageToolbarLayout, 0, 0);
        _imageAssignmentInfoBox.Dock = DockStyle.Fill;
        _imageAssignmentInfoBox.Multiline = true;
        _imageAssignmentInfoBox.Height = 70;
        _imageAssignmentInfoBox.ReadOnly = true;
        _imageAssignmentInfoBox.ScrollBars = ScrollBars.Vertical;
        _imageAssignmentInfoBox.WordWrap = false;

        var imageSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };
        ConfigureSplitContainerDistanceAfterLayout("BuildImageAssignmentPage.GridPreview", imageSplit, desiredDistance: 760, desiredPanel1Min: 420, desiredPanel2Min: 300);
        _imageAssignmentGrid.Dock = DockStyle.Fill;
        _imageAssignmentGrid.AllowUserToAddRows = false;
        _imageAssignmentGrid.AllowUserToDeleteRows = false;
        _imageAssignmentGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _imageAssignmentGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _imageAssignmentGrid.MultiSelect = true;
        AddCollapsibleSplitPanel(imageSplit, 1, "人物形象表", _imageAssignmentGrid, "BuildImageAssignmentPage.GridPreview.Grid");

        var imagePreviewLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 2,
            Padding = new Padding(6)
        };
        imagePreviewLayout.RowStyles.Clear();
        imagePreviewLayout.ColumnStyles.Clear();
        imagePreviewLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
        imagePreviewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 24));
        imagePreviewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 76));
        imagePreviewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        imagePreviewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));

        // 右侧预览区显示头像；R/S 按 E5 0x110 索引表取图，不再按裸扫出现顺序取候选图。
        _imageAssignmentFacePreviewBox.Dock = DockStyle.Fill;
        _imageAssignmentFacePreviewBox.BackColor = Color.FromArgb(32, 32, 36);
        _imageAssignmentFacePreviewBox.SizeMode = PictureBoxSizeMode.Zoom;
        _imageAssignmentFacePreviewBox.BorderStyle = BorderStyle.FixedSingle;
        _imageAssignmentFacePreviewBox.Margin = new Padding(4);
        imagePreviewLayout.Controls.Add(_imageAssignmentFacePreviewBox, 0, 0);

        _imageAssignmentRPreviewBox.Dock = DockStyle.Fill;
        _imageAssignmentRPreviewBox.BackColor = Color.FromArgb(32, 32, 36);
        _imageAssignmentRPreviewBox.SizeMode = PictureBoxSizeMode.Zoom;
        _imageAssignmentRPreviewBox.BorderStyle = BorderStyle.FixedSingle;
        _imageAssignmentRPreviewBox.Margin = new Padding(4);
        imagePreviewLayout.Controls.Add(_imageAssignmentRPreviewBox, 0, 1);

        _imageAssignmentSPreviewBox.Dock = DockStyle.Fill;
        _imageAssignmentSPreviewBox.BackColor = Color.FromArgb(32, 32, 36);
        _imageAssignmentSPreviewBox.SizeMode = PictureBoxSizeMode.Zoom;
        _imageAssignmentSPreviewBox.BorderStyle = BorderStyle.FixedSingle;
        _imageAssignmentSPreviewBox.Margin = new Padding(4);
        imagePreviewLayout.Controls.Add(_imageAssignmentSPreviewBox, 1, 0);
        imagePreviewLayout.SetRowSpan(_imageAssignmentSPreviewBox, 2);

        AddCollapsibleSplitPanel(imageSplit, 2, "形象预览", imagePreviewLayout, "BuildImageAssignmentPage.GridPreview.Preview");
        imageLayout.Controls.Add(imageSplit, 0, 1);
        imageTabs.TabPages.Add(imageAssignmentPage);
        _mainTabs.TabPages.Add(imagePage);
        _mainTabs.TabPages.Add(BuildMapEditorPage());
        _mainTabs.TabPages.Add(BuildScriptEditorPage());
        _mainTabs.TabPages.Add(BuildRSceneEditorPage());
        _mainTabs.TabPages.Add(BuildBattlefieldEditorPage());
        _mainTabs.TabPages.Add(BuildShopEditorPage());

        if (ShowLegacyProbePages)
        {
        var eexPage = new TabPage("EEX资源探针");
        var eexLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(6)
        };
        eexLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        eexLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        eexPage.Controls.Add(eexLayout);
        var eexToolbar = CreateToolbarStack(2);
        _loadEexArchivesButton.Text = "读取 RS/Map .eex";
        ConfigureToolbarButton(_loadEexArchivesButton, 128);
        _openEexArchiveButton.Text = "定位选中文件";
        ConfigureToolbarButton(_openEexArchiveButton, 118);
        _exportEexArchivesCsvButton.Text = "导出EEX索引CSV";
        ConfigureToolbarButton(_exportEexArchivesCsvButton, 138);
        _probeEexEntriesButton.Text = "解析选中EEX区段";
        ConfigureToolbarButton(_probeEexEntriesButton, 142);
        _exportEexEntryProbeCsvButton.Text = "导出区段CSV";
        ConfigureToolbarButton(_exportEexEntryProbeCsvButton, 118);
        _compareEexCrossFilesButton.Text = "跨文件对比";
        ConfigureToolbarButton(_compareEexCrossFilesButton, 104);
        _renderEexHeatmapButton.Text = "生成字节热力图";
        ConfigureToolbarButton(_renderEexHeatmapButton, 128);
        _exportEexHeatmapPngButton.Text = "导出热力图PNG";
        ConfigureToolbarButton(_exportEexHeatmapPngButton, 128);
        _eexArchiveCategoryFilterCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        ConfigureToolbarInput(_eexArchiveCategoryFilterCombo, 110, 100);
        ConfigureToolbarInput(_eexArchiveSearchBox, 150, 120);
        _filterEexArchivesButton.Text = "筛选";
        ConfigureToolbarButton(_filterEexArchivesButton, 72);
        _clearEexArchiveFilterButton.Text = "显示全部";
        ConfigureToolbarButton(_clearEexArchiveFilterButton, 88);
        AddToolbarRow(eexToolbar, 0,
            _loadEexArchivesButton,
            _openEexArchiveButton,
            _exportEexArchivesCsvButton,
            _probeEexEntriesButton,
            _exportEexEntryProbeCsvButton,
            _compareEexCrossFilesButton,
            _renderEexHeatmapButton,
            _exportEexHeatmapPngButton);
        AddToolbarRow(eexToolbar, 1,
            CreateToolbarLabel("分类", 0),
            _eexArchiveCategoryFilterCombo,
            CreateToolbarLabel("关键字"),
            _eexArchiveSearchBox,
            _filterEexArchivesButton,
            _clearEexArchiveFilterButton);
        eexLayout.Controls.Add(eexToolbar, 0, 0);
        _eexArchiveInfoBox.Dock = DockStyle.Fill;
        _eexArchiveInfoBox.Multiline = true;
        _eexArchiveInfoBox.Height = 92;
        _eexArchiveInfoBox.ReadOnly = true;
        _eexArchiveInfoBox.ScrollBars = ScrollBars.Vertical;
        _eexArchiveInfoBox.WordWrap = false;
        var eexSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
        };
        ConfigureSplitContainerDistanceAfterLayout("BuildEexProbePage.ArchiveDetail", eexSplit, desiredDistance: 610, desiredPanel1Min: 25, desiredPanel2Min: 25);
        _eexArchiveGrid.Dock = DockStyle.Fill;
        _eexArchiveGrid.ReadOnly = true;
        _eexArchiveGrid.AllowUserToAddRows = false;
        _eexArchiveGrid.AllowUserToDeleteRows = false;
        _eexArchiveGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _eexArchiveGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _eexEntryProbeGrid.Dock = DockStyle.Fill;
        _eexEntryProbeGrid.ReadOnly = true;
        _eexEntryProbeGrid.AllowUserToAddRows = false;
        _eexEntryProbeGrid.AllowUserToDeleteRows = false;
        _eexEntryProbeGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _eexEntryProbeGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _eexEntryTree.Dock = DockStyle.Fill;
        _eexEntryTree.HideSelection = false;
        _eexEntryTree.ShowNodeToolTips = true;
        _eexEntryTree.AfterSelect += (_, e) => SelectEexEntryProbeRowFromTree(e.Node?.Tag as EexEntryProbeRow);
        _eexEntryTreeInfoBox.Dock = DockStyle.Fill;
        _eexEntryTreeInfoBox.Multiline = true;
        _eexEntryTreeInfoBox.ReadOnly = true;
        _eexEntryTreeInfoBox.ScrollBars = ScrollBars.Vertical;
        _eexEntryTreeInfoBox.WordWrap = false;
        _eexEntryTreeInfoBox.Text = "EEX 区段树：解析 EEX 区段后，点击树节点可查看头字段、文本线索、动作参数/帧表候选、图像/压缩载荷候选和安全建议。";
        _eexCrossFileInfoBox.Dock = DockStyle.Fill;
        _eexCrossFileInfoBox.Multiline = true;
        _eexCrossFileInfoBox.ReadOnly = true;
        _eexCrossFileInfoBox.ScrollBars = ScrollBars.Vertical;
        _eexCrossFileInfoBox.WordWrap = false;
        _eexCrossFileInfoBox.Text = "EEX 跨文件对比：请选择一个 R/S/Map EEX 后点击“跨文件对比”，查看同编号 R/S 与同分类邻近文件的区段长度、00占比、小整数比例和文本线索差异。";
        _eexCrossFileGrid.Dock = DockStyle.Fill;
        _eexCrossFileGrid.ReadOnly = true;
        _eexCrossFileGrid.AllowUserToAddRows = false;
        _eexCrossFileGrid.AllowUserToDeleteRows = false;
        _eexCrossFileGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _eexCrossFileGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        var eexProbeHeatmapSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
        };
        ConfigureSplitContainerDistanceAfterLayout("BuildEexProbePage.ProbeHeatmap", eexProbeHeatmapSplit, desiredDistance: 300, desiredPanel1Min: 25, desiredPanel2Min: 25);
        var eexHeatmapLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 1,
            ColumnCount = 1
        };
        eexHeatmapLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _eexByteHeatmapInfoBox.Dock = DockStyle.Fill;
        _eexByteHeatmapInfoBox.Multiline = true;
        _eexByteHeatmapInfoBox.Height = 72;
        _eexByteHeatmapInfoBox.ReadOnly = true;
        _eexByteHeatmapInfoBox.ScrollBars = ScrollBars.Vertical;
        _eexByteHeatmapInfoBox.WordWrap = false;
        _eexByteHeatmapInfoBox.Text = "EEX 字节热力图：请选择左侧 EEX 文件，或先解析区段后选择右侧候选区段，再点击“生成字节热力图”。该预览只读，不解压、不写入。";
        _eexByteHeatmapBox.Dock = DockStyle.Fill;
        _eexByteHeatmapBox.BorderStyle = BorderStyle.FixedSingle;
        _eexByteHeatmapBox.BackColor = Color.Black;
        _eexByteHeatmapBox.SizeMode = PictureBoxSizeMode.Zoom;
        eexHeatmapLayout.Controls.Add(_eexByteHeatmapBox, 0, 0);
        AddCollapsibleSplitPanel(eexSplit, 1, "EEX文件", _eexArchiveGrid, "BuildEexProbePage.ArchiveDetail.Archives");
        var eexProbeTabs = new TabControl { Dock = DockStyle.Fill };
        var eexProbeGridPage = new TabPage("区段表格");
        var eexProbeTreePage = new TabPage("区段/动作候选树");
        var eexCrossFilePage = new TabPage("跨文件对比");
        eexProbeGridPage.Controls.Add(_eexEntryProbeGrid);
        eexProbeTreePage.Controls.Add(_eexEntryTree);
        eexCrossFilePage.Controls.Add(_eexCrossFileGrid);
        eexProbeTabs.TabPages.Add(eexProbeGridPage);
        eexProbeTabs.TabPages.Add(eexProbeTreePage);
        eexProbeTabs.TabPages.Add(eexCrossFilePage);
        AddCollapsibleSplitPanel(eexProbeHeatmapSplit, 1, "区段解析", eexProbeTabs, "BuildEexProbePage.ProbeHeatmap.Probe");
        AddCollapsibleSplitPanel(eexProbeHeatmapSplit, 2, "热力图", eexHeatmapLayout, "BuildEexProbePage.ProbeHeatmap.Heatmap");
        eexSplit.Panel2.Controls.Add(eexProbeHeatmapSplit);
        eexLayout.Controls.Add(eexSplit, 0, 1);
        _mainTabs.TabPages.Add(eexPage);

        var scenarioPage = new TabPage("R/S eex高级探针");
        var scenarioLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(6)
        };
        scenarioLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        scenarioLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        scenarioPage.Controls.Add(scenarioLayout);
        var scenarioToolbar = CreateToolbarStack(3);
        _loadScenarioFilesButton.Text = "读取 RS/*.eex";
        ConfigureToolbarButton(_loadScenarioFilesButton, 118);
        _openScenarioFileButton.Text = "\u5b9a\u4f4d\u9009\u4e2d\u6587\u4ef6";
        ConfigureToolbarButton(_openScenarioFileButton, 118);
        _exportScenarioFileIndexCsvButton.Text = "\u5bfc\u51faRS\u7d22\u5f15CSV";
        ConfigureToolbarButton(_exportScenarioFileIndexCsvButton, 128);
        _scenarioKindFilterCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        ConfigureToolbarInput(_scenarioKindFilterCombo, 110, 100);
        ConfigureToolbarInput(_scenarioFileSearchBox, 140, 120);
        _filterScenarioFilesButton.Text = "\u7b5b\u9009";
        ConfigureToolbarButton(_filterScenarioFilesButton, 72);
        _clearScenarioFileFilterButton.Text = "\u663e\u793a\u5168\u90e8";
        ConfigureToolbarButton(_clearScenarioFileFilterButton, 88);
        _scenarioFilesWithTextOnly.Text = "\u4ec5\u6709\u6587\u672c";
        ConfigureToolbarCheckBox(_scenarioFilesWithTextOnly);
        _probeScenarioCommandsButton.Text = "探测选中命令";
        ConfigureToolbarButton(_probeScenarioCommandsButton, 118);
        _probeScenarioCommandsButton.Enabled = false;
        _buildScenarioStructureButton.Text = "生成结构草图";
        ConfigureToolbarButton(_buildScenarioStructureButton, 118);
        _buildScenarioStructureButton.Enabled = false;
        _exportScenarioStructureXmlButton.Text = "导出结构XML";
        ConfigureToolbarButton(_exportScenarioStructureXmlButton, 118);
        _exportScenarioStructureXmlButton.Enabled = false;
        _exportScenarioCommandTemplateCatalogButton.Text = "导出命令模板目录";
        ConfigureToolbarButton(_exportScenarioCommandTemplateCatalogButton, 150);
        _exportScenarioCommandTemplateCatalogButton.Enabled = false;
        _refreshScenarioCommandTemplatesButton.Text = "刷新模板目录";
        ConfigureToolbarButton(_refreshScenarioCommandTemplatesButton, 118);
        _filterScenarioCommandTemplatesButton.Text = "筛选";
        ConfigureToolbarButton(_filterScenarioCommandTemplatesButton, 72);
        _clearScenarioCommandTemplateFilterButton.Text = "显示全部";
        ConfigureToolbarButton(_clearScenarioCommandTemplateFilterButton, 88);
        _showScenarioCommandTemplateInStructureButton.Text = "筛出当前R/S命令";
        ConfigureToolbarButton(_showScenarioCommandTemplateInStructureButton, 142);
        _showScenarioCommandTemplateInStructureButton.Enabled = false;
        ConfigureToolbarInput(_scenarioCommandTemplateSearchBox, 180, 140);
        _scenarioCommandTemplateSearchBox.PlaceholderText = "命令名/槽位/用途/风险";
        _scenarioCommandTemplateCategoryCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        ConfigureToolbarInput(_scenarioCommandTemplateCategoryCombo, 140, 120);
        _scenarioCommandTemplateStatusCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        ConfigureToolbarInput(_scenarioCommandTemplateStatusCombo, 92, 88);
        _probeScenarioTextsButton.Text = "提取选中文本";
        ConfigureToolbarButton(_probeScenarioTextsButton, 118);
        _probeScenarioTextsButton.Enabled = false;
        _exportScenarioTextsButton.Text = "导出文本CSV/TXT";
        ConfigureToolbarButton(_exportScenarioTextsButton, 138);
        _exportScenarioTextsButton.Enabled = false;
        _saveScenarioTextsButton.Text = "保存文本到项目";
        ConfigureToolbarButton(_saveScenarioTextsButton, 128);
        _saveScenarioTextsButton.Enabled = false;
        ConfigureToolbarInput(_scenarioTextFilterBox, 160, 120);
        _scenarioTextFilterBox.PlaceholderText = "筛选文本/注释";
        _scenarioTextFilterButton.Text = "筛选";
        ConfigureToolbarButton(_scenarioTextFilterButton, 72);
        _scenarioTextFilterButton.Enabled = false;
        _scenarioTextFilterClearButton.Text = "清除筛选";
        ConfigureToolbarButton(_scenarioTextFilterClearButton, 88);
        _scenarioTextFilterClearButton.Enabled = false;
        _scenarioTextChangedOnly.Text = "仅改动";
        ConfigureToolbarCheckBox(_scenarioTextChangedOnly);
        _scenarioTextChangedOnly.Enabled = false;
        AddToolbarRow(scenarioToolbar, 0,
            _loadScenarioFilesButton,
            _openScenarioFileButton,
            _exportScenarioFileIndexCsvButton,
            CreateToolbarLabel("类型"),
            _scenarioKindFilterCombo,
            CreateToolbarLabel("文件筛选"),
            _scenarioFileSearchBox,
            _filterScenarioFilesButton,
            _clearScenarioFileFilterButton,
            _scenarioFilesWithTextOnly);
        AddToolbarRow(scenarioToolbar, 1,
            _probeScenarioCommandsButton,
            _buildScenarioStructureButton,
            _exportScenarioStructureXmlButton,
            _exportScenarioCommandTemplateCatalogButton);
        AddToolbarRow(scenarioToolbar, 2,
            _probeScenarioTextsButton,
            _exportScenarioTextsButton,
            _saveScenarioTextsButton,
            CreateToolbarLabel("文本筛选："),
            _scenarioTextFilterBox,
            _scenarioTextFilterButton,
            _scenarioTextFilterClearButton,
            _scenarioTextChangedOnly);
        scenarioLayout.Controls.Add(scenarioToolbar, 0, 0);
        _scenarioFileInfoBox.Dock = DockStyle.Fill;
        _scenarioFileInfoBox.Multiline = true;
        _scenarioFileInfoBox.Height = 112;
        _scenarioFileInfoBox.ReadOnly = true;
        _scenarioFileInfoBox.ScrollBars = ScrollBars.Vertical;
        _scenarioFileInfoBox.WordWrap = false;
        var scenarioSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
        };
        ConfigureSplitContainerDistanceAfterLayout("BuildScenarioProbePage.FileDetail", scenarioSplit, desiredDistance: 330, desiredPanel1Min: 25, desiredPanel2Min: 25);
        _scenarioFileGrid.Dock = DockStyle.Fill;
        _scenarioFileGrid.ReadOnly = true;
        _scenarioFileGrid.AllowUserToAddRows = false;
        _scenarioFileGrid.AllowUserToDeleteRows = false;
        _scenarioFileGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _scenarioFileGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        var scenarioDetailTabs = new TabControl { Dock = DockStyle.Fill };
        var commandProbePage = new TabPage("命令候选");
        var structureProbePage = new TabPage("结构草图/XML");
        var commandTemplatePage = new TabPage("命令模板目录");
        var textProbePage = new TabPage("文本线索");
        _scenarioCommandProbeGrid.Dock = DockStyle.Fill;
        _scenarioCommandProbeGrid.ReadOnly = true;
        _scenarioCommandProbeGrid.AllowUserToAddRows = false;
        _scenarioCommandProbeGrid.AllowUserToDeleteRows = false;
        _scenarioCommandProbeGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _scenarioCommandProbeGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        var structureSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
        };
        ConfigureSplitContainerDistanceAfterLayout("BuildScenarioProbePage.StructurePreview", structureSplit, desiredDistance: 650, desiredPanel1Min: 25, desiredPanel2Min: 25);
        _scenarioStructureGrid.Dock = DockStyle.Fill;
        _scenarioStructureGrid.ReadOnly = true;
        _scenarioStructureGrid.AllowUserToAddRows = false;
        _scenarioStructureGrid.AllowUserToDeleteRows = false;
        _scenarioStructureGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _scenarioStructureGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        var structureLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1
        };
        structureLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        structureLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var structureFilterToolbar = CreateToolbarStack(2);
        ConfigureToolbarInput(_scenarioStructureFilterBox, 180, 140);
        _scenarioStructureFilterBox.PlaceholderText = "命令/偏移/注释筛选";
        _filterScenarioStructureButton.Text = "筛选结构";
        ConfigureToolbarButton(_filterScenarioStructureButton, 88);
        _filterScenarioStructureButton.Enabled = false;
        _clearScenarioStructureFilterButton.Text = "显示全部结构";
        ConfigureToolbarButton(_clearScenarioStructureFilterButton, 118);
        _clearScenarioStructureFilterButton.Enabled = false;
        _scenarioStructureTemplatesOnly.Text = "仅有模板";
        ConfigureToolbarCheckBox(_scenarioStructureTemplatesOnly);
        _scenarioStructureTemplatesOnly.Enabled = false;
        _scenarioStructureTextOnly.Text = "文本/剧情";
        ConfigureToolbarCheckBox(_scenarioStructureTextOnly);
        _scenarioStructureTextOnly.Enabled = false;
        _scenarioStructureMapOnly.Text = "地图/坐标";
        ConfigureToolbarCheckBox(_scenarioStructureMapOnly);
        _scenarioStructureMapOnly.Enabled = false;
        _scenarioStructureHighRiskOnly.Text = "高风险/需核对";
        ConfigureToolbarCheckBox(_scenarioStructureHighRiskOnly);
        _scenarioStructureHighRiskOnly.Enabled = false;
        _scenarioCommandReferenceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        ConfigureToolbarInput(_scenarioCommandReferenceCombo, 360, 220);
        _scenarioCommandReferenceCombo.Enabled = false;
        _jumpScenarioCommandReferenceButton.Text = "跳到命令引用";
        ConfigureToolbarButton(_jumpScenarioCommandReferenceButton, 118);
        _jumpScenarioCommandReferenceButton.Enabled = false;
        _exportScenarioCommandReferenceChecklistButton.Text = "导出命令引用清单";
        ConfigureToolbarButton(_exportScenarioCommandReferenceChecklistButton, 150);
        _exportScenarioCommandReferenceChecklistButton.Enabled = false;
        AddToolbarRow(structureFilterToolbar, 0,
            CreateToolbarLabel("结构筛选：", 0),
            _scenarioStructureFilterBox,
            _filterScenarioStructureButton,
            _clearScenarioStructureFilterButton,
            _scenarioStructureTemplatesOnly,
            _scenarioStructureTextOnly,
            _scenarioStructureMapOnly,
            _scenarioStructureHighRiskOnly);
        AddToolbarRow(structureFilterToolbar, 1,
            CreateToolbarLabel("命令引用：", 0),
            _scenarioCommandReferenceCombo,
            _jumpScenarioCommandReferenceButton,
            _exportScenarioCommandReferenceChecklistButton);
        structureLayout.Controls.Add(structureFilterToolbar, 0, 0);
        _scenarioStructureTree.Dock = DockStyle.Fill;
        _scenarioStructureTree.HideSelection = false;
        _scenarioStructureTree.ShowNodeToolTips = true;
        _scenarioStructureTree.AfterSelect += (_, e) => SelectScenarioStructureRowFromTree(e.Node?.Tag as ScenarioStructureRow);
        _scenarioStructureNodeInfoBox.Dock = DockStyle.Fill;
        _scenarioStructureNodeInfoBox.Multiline = true;
        _scenarioStructureNodeInfoBox.ReadOnly = true;
        _scenarioStructureNodeInfoBox.ScrollBars = ScrollBars.Vertical;
        _scenarioStructureNodeInfoBox.WordWrap = false;
        _scenarioStructureNodeInfoBox.Text = "事件树节点详情：生成结构草图后，点击右侧 Scene/Section/Command 节点，可查看中文说明和同文件文本线索。";
        _scenarioStructureXmlBox.Dock = DockStyle.Fill;
        _scenarioStructureXmlBox.Multiline = true;
        _scenarioStructureXmlBox.ReadOnly = true;
        _scenarioStructureXmlBox.ScrollBars = ScrollBars.Both;
        _scenarioStructureXmlBox.WordWrap = false;
        _scenarioTextGrid.Dock = DockStyle.Fill;
        _scenarioTextGrid.ReadOnly = true;
        _scenarioTextGrid.AllowUserToAddRows = false;
        _scenarioTextGrid.AllowUserToDeleteRows = false;
        _scenarioTextGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _scenarioTextGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _scenarioTextGrid.CellValidating += (_, e) => ValidateScenarioTextCell(e);

        var commandTemplateLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1
        };
        commandTemplateLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        commandTemplateLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var commandTemplateToolbar = CreateToolbarStack(2);
        AddToolbarRow(commandTemplateToolbar, 0,
            _refreshScenarioCommandTemplatesButton,
            CreateToolbarLabel("关键字："),
            _scenarioCommandTemplateSearchBox,
            CreateToolbarLabel("分类："),
            _scenarioCommandTemplateCategoryCombo,
            CreateToolbarLabel("状态："),
            _scenarioCommandTemplateStatusCombo,
            _filterScenarioCommandTemplatesButton,
            _clearScenarioCommandTemplateFilterButton);
        AddToolbarRow(commandTemplateToolbar, 1,
            _showScenarioCommandTemplateInStructureButton,
            _exportScenarioCommandTemplateCatalogButton);
        commandTemplateLayout.Controls.Add(commandTemplateToolbar, 0, 0);
        _scenarioCommandTemplateGrid.Dock = DockStyle.Fill;
        _scenarioCommandTemplateGrid.ReadOnly = true;
        _scenarioCommandTemplateGrid.AllowUserToAddRows = false;
        _scenarioCommandTemplateGrid.AllowUserToDeleteRows = false;
        _scenarioCommandTemplateGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _scenarioCommandTemplateGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _scenarioCommandTemplateInfoBox.Dock = DockStyle.Fill;
        _scenarioCommandTemplateInfoBox.Multiline = true;
        _scenarioCommandTemplateInfoBox.ReadOnly = true;
        _scenarioCommandTemplateInfoBox.ScrollBars = ScrollBars.Both;
        _scenarioCommandTemplateInfoBox.WordWrap = false;
        _scenarioCommandTemplateInfoBox.Text =
            "R/S eex 命令模板目录：用于把常见命令参数槽位变成创作者可读的中文工作台。\r\n" +
            "请点击“刷新模板目录”加载内置模板与 CczString.ini 字典覆盖情况；当前页只读，不写任何游戏文件。";
        commandTemplateLayout.Controls.Add(_scenarioCommandTemplateGrid, 0, 1);
        commandTemplatePage.Controls.Add(commandTemplateLayout);

        AddCollapsibleSplitPanel(scenarioSplit, 1, "剧本文件", _scenarioFileGrid, "BuildScenarioProbePage.FileDetail.Files");
        commandProbePage.Controls.Add(_scenarioCommandProbeGrid);
        var structureDetailTabs = new TabControl { Dock = DockStyle.Fill };
        var structureTreePage = new TabPage("事件树");
        var structureXmlPage = new TabPage("XML");
        structureTreePage.Controls.Add(_scenarioStructureTree);
        structureXmlPage.Controls.Add(_scenarioStructureXmlBox);
        structureDetailTabs.TabPages.Add(structureTreePage);
        structureDetailTabs.TabPages.Add(structureXmlPage);
        AddCollapsibleSplitPanel(structureSplit, 1, "结构表", _scenarioStructureGrid, "BuildScenarioProbePage.StructurePreview.Structure");
        AddCollapsibleSplitPanel(structureSplit, 2, "结构详情", structureDetailTabs, "BuildScenarioProbePage.StructurePreview.Detail");
        structureLayout.Controls.Add(structureSplit, 0, 1);
        structureProbePage.Controls.Add(structureLayout);
        textProbePage.Controls.Add(_scenarioTextGrid);
        scenarioDetailTabs.TabPages.Add(commandProbePage);
        scenarioDetailTabs.TabPages.Add(structureProbePage);
        scenarioDetailTabs.TabPages.Add(commandTemplatePage);
        scenarioDetailTabs.TabPages.Add(textProbePage);
        scenarioSplit.Panel2.Controls.Add(scenarioDetailTabs);
        scenarioLayout.Controls.Add(scenarioSplit, 0, 1);
        _mainTabs.TabPages.Add(scenarioPage);

        var lsPage = new TabPage("Ls/E5地图资源探针");
        var lsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(6)
        };
        lsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        lsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        lsPage.Controls.Add(lsLayout);
        var lsToolbar = CreateToolbarStack(2);
        _loadLsResourcesButton.Text = "读取 Ls/E5 资源";
        ConfigureToolbarButton(_loadLsResourcesButton, 128);
        _openLsResourceButton.Text = "定位选中文件";
        ConfigureToolbarButton(_openLsResourceButton, 118);
        _exportLsResourcesCsvButton.Text = "导出CSV";
        ConfigureToolbarButton(_exportLsResourcesCsvButton, 88);
        _renderLsResourceHeatmapButton.Text = "生成字节热力图";
        ConfigureToolbarButton(_renderLsResourceHeatmapButton, 128);
        _exportLsResourceHeatmapPngButton.Text = "导出热力图PNG";
        ConfigureToolbarButton(_exportLsResourceHeatmapPngButton, 128);
        _exportLsResourceHeatmapPngButton.Enabled = false;
        _lsResourceCategoryFilterCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        ConfigureToolbarInput(_lsResourceCategoryFilterCombo, 120, 100);
        ConfigureToolbarInput(_lsResourceSearchBox, 180, 130);
        _filterLsResourcesButton.Text = "筛选";
        ConfigureToolbarButton(_filterLsResourcesButton, 72);
        _clearLsResourceFilterButton.Text = "显示全部";
        ConfigureToolbarButton(_clearLsResourceFilterButton, 88);
        AddToolbarRow(lsToolbar, 0,
            _loadLsResourcesButton,
            _openLsResourceButton,
            _exportLsResourcesCsvButton,
            _renderLsResourceHeatmapButton,
            _exportLsResourceHeatmapPngButton);
        AddToolbarRow(lsToolbar, 1,
            CreateToolbarLabel("分类", 0),
            _lsResourceCategoryFilterCombo,
            CreateToolbarLabel("关键字"),
            _lsResourceSearchBox,
            _filterLsResourcesButton,
            _clearLsResourceFilterButton);
        lsLayout.Controls.Add(lsToolbar, 0, 0);
        _lsResourceInfoBox.Dock = DockStyle.Fill;
        _lsResourceInfoBox.Multiline = true;
        _lsResourceInfoBox.Height = 112;
        _lsResourceInfoBox.ReadOnly = true;
        _lsResourceInfoBox.ScrollBars = ScrollBars.Vertical;
        _lsResourceInfoBox.WordWrap = false;
        _lsResourceGrid.Dock = DockStyle.Fill;
        _lsResourceGrid.ReadOnly = true;
        _lsResourceGrid.AllowUserToAddRows = false;
        _lsResourceGrid.AllowUserToDeleteRows = false;
        _lsResourceGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _lsResourceGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        var lsResourceSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
        };
        ConfigureSplitContainerDistanceAfterLayout("BuildLsResourcePage.ResourceHeatmap", lsResourceSplit, desiredDistance: 310, desiredPanel1Min: 25, desiredPanel2Min: 25);
        var lsHeatmapLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 1,
            ColumnCount = 1
        };
        lsHeatmapLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _lsResourceHeatmapInfoBox.Dock = DockStyle.Fill;
        _lsResourceHeatmapInfoBox.Multiline = true;
        _lsResourceHeatmapInfoBox.Height = 72;
        _lsResourceHeatmapInfoBox.ReadOnly = true;
        _lsResourceHeatmapInfoBox.ScrollBars = ScrollBars.Vertical;
        _lsResourceHeatmapInfoBox.WordWrap = false;
        _lsResourceHeatmapInfoBox.Text = "Ls/E5 字节热力图：请选择一个 Ls/E5 资源后点击“生成字节热力图”。该预览只读，仅观察 16 字节头之后的载荷分布，不解压、不重封包、不写入。";
        _lsResourceHeatmapBox.Dock = DockStyle.Fill;
        _lsResourceHeatmapBox.BorderStyle = BorderStyle.FixedSingle;
        _lsResourceHeatmapBox.BackColor = Color.Black;
        _lsResourceHeatmapBox.SizeMode = PictureBoxSizeMode.Zoom;
        lsHeatmapLayout.Controls.Add(_lsResourceHeatmapBox, 0, 0);
        AddCollapsibleSplitPanel(lsResourceSplit, 1, "Ls/E5资源", _lsResourceGrid, "BuildLsResourcePage.ResourceHeatmap.Resources");
        AddCollapsibleSplitPanel(lsResourceSplit, 2, "热力图", lsHeatmapLayout, "BuildLsResourcePage.ResourceHeatmap.Heatmap");
        lsLayout.Controls.Add(lsResourceSplit, 0, 1);
        _mainTabs.TabPages.Add(lsPage);

        var hexzmapPage = new TabPage("Hexzmap地形探针");
        var hexzmapLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(6)
        };
        hexzmapLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        hexzmapLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        hexzmapPage.Controls.Add(hexzmapLayout);
        var hexzmapToolbar = CreateToolbarRow();
        _loadHexzmapProbeButton.Text = "读取 Hexzmap.e5";
        ConfigureToolbarButton(_loadHexzmapProbeButton, 128);
        _exportHexzmapProbeCsvButton.Text = "导出地形探针CSV";
        ConfigureToolbarButton(_exportHexzmapProbeCsvButton, 138);
        _exportHexzmapProbeCsvButton.Enabled = false;
        _exportHexzmapOverlayPngButton.Text = "导出当前叠加PNG";
        ConfigureToolbarButton(_exportHexzmapOverlayPngButton, 138);
        _exportHexzmapOverlayPngButton.Enabled = false;
        _hexzmapOverlayMapCheckBox.Text = "叠加地图底图";
        ConfigureToolbarCheckBox(_hexzmapOverlayMapCheckBox);
        _hexzmapOverlayMapCheckBox.Checked = true;
        _hexzmapOverlayOpacityLabel.Text = "地形透明度 45%";
        _hexzmapOverlayOpacityLabel.AutoSize = true;
        _hexzmapOverlayOpacityLabel.Margin = new Padding(8, 8, 0, 0);
        _hexzmapOverlayOpacityTrackBar.Minimum = 10;
        _hexzmapOverlayOpacityTrackBar.Maximum = 90;
        _hexzmapOverlayOpacityTrackBar.TickFrequency = 10;
        _hexzmapOverlayOpacityTrackBar.Value = 45;
        _hexzmapOverlayOpacityTrackBar.Width = 130;
        _hexzmapOverlayOpacityTrackBar.AutoSize = false;
        _hexzmapOverlayOpacityTrackBar.Height = 30;
        hexzmapToolbar.Controls.AddRange(new Control[]
        {
            _loadHexzmapProbeButton,
            _exportHexzmapProbeCsvButton,
            _exportHexzmapOverlayPngButton,
            _hexzmapOverlayMapCheckBox,
            _hexzmapOverlayOpacityLabel,
            _hexzmapOverlayOpacityTrackBar
        });
        hexzmapLayout.Controls.Add(hexzmapToolbar, 0, 0);
        _hexzmapInfoBox.Dock = DockStyle.Fill;
        _hexzmapInfoBox.Multiline = true;
        _hexzmapInfoBox.Height = 86;
        _hexzmapInfoBox.ReadOnly = true;
        _hexzmapInfoBox.ScrollBars = ScrollBars.Vertical;
        _hexzmapInfoBox.WordWrap = false;
        var hexzmapSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
        };
        ConfigureSplitContainerDistanceAfterLayout("BuildHexzmapPage.GridPreview", hexzmapSplit, desiredDistance: 620, desiredPanel1Min: 25, desiredPanel2Min: 25);
        _hexzmapGrid.Dock = DockStyle.Fill;
        _hexzmapGrid.ReadOnly = true;
        _hexzmapGrid.AllowUserToAddRows = false;
        _hexzmapGrid.AllowUserToDeleteRows = false;
        _hexzmapGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _hexzmapGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        var hexzmapPreviewLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1
        };
        hexzmapPreviewLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        hexzmapPreviewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _hexzmapCellPreviewLabel.Dock = DockStyle.Fill;
        _hexzmapCellPreviewLabel.AutoSize = false;
        _hexzmapCellPreviewLabel.Height = 30;
        _hexzmapCellPreviewLabel.Padding = new Padding(8, 6, 8, 0);
        _hexzmapCellPreviewLabel.BackColor = Color.FromArgb(245, 245, 245);
        _hexzmapCellPreviewLabel.ForeColor = Color.FromArgb(32, 32, 32);
        _hexzmapCellPreviewLabel.BorderStyle = BorderStyle.FixedSingle;
        _hexzmapCellPreviewLabel.TextAlign = ContentAlignment.MiddleLeft;
        _hexzmapCellPreviewLabel.Text = "地形：-    坐标：-";
        _hexzmapPreviewBox.Dock = DockStyle.Fill;
        _hexzmapPreviewBox.BackColor = Color.Black;
        _hexzmapPreviewBox.SizeMode = PictureBoxSizeMode.Zoom;
        hexzmapPreviewLayout.Controls.Add(_hexzmapCellPreviewLabel, 0, 0);
        hexzmapPreviewLayout.Controls.Add(_hexzmapPreviewBox, 0, 1);
        AddCollapsibleSplitPanel(hexzmapSplit, 1, "地形块", _hexzmapGrid, "BuildHexzmapPage.GridPreview.Grid");
        AddCollapsibleSplitPanel(hexzmapSplit, 2, "地形预览", hexzmapPreviewLayout, "BuildHexzmapPage.GridPreview.Preview");
        hexzmapLayout.Controls.Add(hexzmapSplit, 0, 1);
        _mainTabs.TabPages.Add(hexzmapPage);
        }

        ReorderMainCreationTabs();

        _statusStrip.Items.Add(_statusLabel);
        _statusLabel.Text = "就绪";
        root.Controls.Add(_statusStrip, 0, 3);
    }

    private void OpenPlan()
    {
        try
        {
            var workspace = _project?.WorkspaceRoot ?? Directory.GetCurrentDirectory();
            var plan = Path.Combine(workspace, "plan.md");
            if (!File.Exists(plan))
            {
                MessageBox.Show(this, "找不到 plan.md：" + plan, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Process.Start(new ProcessStartInfo { FileName = plan, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("打开 plan.md 失败：" + ex.Message);
        }
    }

    private void SetStatus(string message)
    {
        _statusLabel.Text = message;
        _statusStrip.Refresh();
    }

    private sealed record ScriptCommandComboItem(int Id, string Name)
    {
        public override string ToString() => $"{HexDisplayFormatter.Format(Id, 2)} {Name}";
    }

    private sealed record JobStrategyComboItem(int Value, string Text);

    private sealed record JobAreaComboItem(int Value, string Text)
    {
        public override string ToString() => Text;
    }

    private static readonly IReadOnlyDictionary<int, string> JobAttackRangeNames = new Dictionary<int, string>
    {
        [0] = "四格",
        [1] = "九宫",
        [2] = "近中程弓形",
        [3] = "中程弓形",
        [4] = "远程弓形",
        [5] = "原版少见",
        [6] = "没羽箭",
        [7] = "一转炮车",
        [8] = "二三转炮车",
        [9] = "一转弓骑",
        [10] = "全屏"
    };

    private static readonly IReadOnlyDictionary<int, string> JobPierceRangeNames = new Dictionary<int, string>
    {
        [0] = "不穿透",
        [1] = "十字穿透",
        [2] = "九宫穿透",
        [3] = "大没羽箭",
        [4] = "蛇矛",
        [5] = "长蛇矛/穿六",
        [6] = "大大没羽箭"
    };

    private sealed record LegacyScriptCommandPasteTarget(
        List<LegacyScenarioCommandNode> Commands,
        int InsertIndex,
        int SceneIndex,
        int SectionIndex,
        string TargetText,
        string StatusActionText);

    private sealed class LegacyScriptClipboardEnvelope
    {
        public string Format { get; init; } = LegacyScriptClipboardFormat;
        public int Version { get; init; } = 1;
        public string SourceProjectName { get; init; } = string.Empty;
        public string SourceGameRoot { get; init; } = string.Empty;
        public string SourceScenarioName { get; init; } = string.Empty;
        public string CreatedAtLocal { get; init; } = string.Empty;
        public List<LegacyScriptClipboardCommand> Commands { get; init; } = [];
        public List<LegacyScriptClipboardScene> Scenes { get; init; } = [];
        public List<LegacyScriptClipboardSection> Sections { get; init; } = [];
    }

    private sealed class LegacyScriptClipboardScene
    {
        public int SceneIndex { get; init; }
        public List<LegacyScriptClipboardSection> Sections { get; init; } = [];
    }

    private sealed class LegacyScriptClipboardSection
    {
        public int SceneIndex { get; init; }
        public int SectionIndex { get; init; }
        public int DeclaredLength { get; init; }
        public List<LegacyScriptClipboardCommand> Commands { get; init; } = [];
    }

    private sealed class LegacyScriptClipboardCommand
    {
        public int SceneIndex { get; init; }
        public int SectionIndex { get; init; }
        public int CommandIndex { get; init; }
        public int CommandOrdinal { get; init; }
        public int CommandId { get; init; }
        public string CommandName { get; init; } = string.Empty;
        public bool StartsBodyBlock { get; init; }
        public bool IsSubEventMarker { get; init; }
        public bool OpensSubEventBlock { get; init; }
        public bool EndsSubEventBlock { get; init; }
        public int? JumpTargetOrdinal { get; init; }
        public int? JumpTargetCommandIndex { get; init; }
        public int? OriginalJumpDisplacement { get; init; }
        public List<LegacyScriptClipboardParameter> Parameters { get; init; } = [];
        public LegacyScriptClipboardBlock? ChildBlock { get; init; }
    }

    private sealed class LegacyScriptClipboardBlock
    {
        public string Kind { get; init; } = string.Empty;
        public List<LegacyScriptClipboardCommand> Commands { get; init; } = [];
    }

    private sealed class LegacyScriptClipboardParameter
    {
        public int Index { get; init; }
        public int LayoutCode { get; init; }
        public int Tag { get; init; }
        public LegacyScenarioParameterKind Kind { get; init; }
        public int IntValue { get; init; }
        public string Text { get; init; } = string.Empty;
        public List<int> Values { get; init; } = [];
        public int ByteLength { get; init; }
    }

    private sealed record E5ImageReplacementTarget(
        int Index,
        string Prefix,
        string Label,
        string FilePath,
        int ImageNumber,
        int IndexOffset,
        int OldDataOffset,
        int OldSizeBytes,
        string Kind,
        string Detail);
}
