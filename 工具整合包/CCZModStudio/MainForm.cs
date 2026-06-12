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
    private sealed record BattlefieldCommand25Marker(int GridX, int GridY, LegacyScenarioCommandNode Command, int Count);

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
    private const string JobStrategyIconResourceFileName = "Mgcicon.dll";
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
        ("小动画", "6.5-5-2 策略动画1"),
        ("大动画", "6.5-5-3 策略动画2"),
        ("是否伤血", "6.5-5-4 策略伤害类型"),
        ("伤害系数", "6.5-5-5 策略伤害比例"),
        ("命中上限", "6.5-5-6 策略命中率"),
        ("效果索引", "6.5-5-7 学会策略"),
        ("AI策略（战场）", "6.5-5-8 战场AI策略限制"),
        ("AI策略（练武）", "6.5-5-9 练武场AI策略限制")
    ];

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
    private readonly MapImageReplaceService _mapImageReplaceService = new();
    private readonly ImageAssignmentPreviewService _imageAssignmentPreviewService = new();
    private readonly FieldAnnotationService _fieldAnnotationService = new();
    private readonly ProPatchParser _patchParser = new();
    private readonly PatchApplyService _patchService = new();    private readonly SceneStringParser _sceneStringParser = new();
    private readonly MaterialLibraryIndexer _materialLibraryIndexer = new();
    private readonly MapDraftService _mapDraftService = new();
    private readonly MapCanvasComposeService _mapCanvasComposeService = new();
    private readonly MapCanvasPublishService _mapCanvasPublishService = new();
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
    private readonly HexzmapEditorService _hexzmapEditorService = new();    private readonly BattlefieldEditorService _battlefieldEditorService = new();
    private readonly BattlefieldUnitReviewService _battlefieldUnitReviewService = new();
    private readonly BattlefieldDeploymentWriteService _battlefieldDeploymentWriteService = new();
    private readonly BattlefieldAllyDeploymentSlotService _battlefieldAllyDeploymentSlotService = new();
    private readonly RSceneDraftService _rSceneDraftService = new();
    private readonly RSceneDialoguePreviewService _rSceneDialoguePreviewService = new();
    private readonly ItemIconPreviewService _itemIconPreviewService = new();
    private readonly ItemEffectCatalogService _itemEffectCatalogService = new();
    private readonly ItemEffectNameReader _itemEffectNameReader = new();
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
    private ScenarioFileInfo? _currentScriptScenario;
    private ScenarioStructureRow? _selectedScriptCommandRow;
    private ScenarioTextEntry? _selectedScriptTextEntry;
    private ScenarioCommandClipboardItem? _scriptCommandClipboardItem;
    private LegacyScenarioCommandNode? _legacyScriptCommandClipboard;
    private IReadOnlyList<LegacyScenarioCommandNode> _legacyScriptCommandClipboardItems = Array.Empty<LegacyScenarioCommandNode>();
    private IReadOnlyList<LegacyScenarioSection> _legacyScriptSectionClipboardItems = Array.Empty<LegacyScenarioSection>();
    private string _legacyScriptCommandClipboardScenarioName = string.Empty;
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
    private IReadOnlyList<RSceneActorPaletteItem> _rSceneActorPaletteItems = Array.Empty<RSceneActorPaletteItem>();
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
    private readonly Stack<List<JobEditorCellEdit>> _jobEditorUndoStack = new();
    private readonly Stack<List<JobEditorCellEdit>> _jobEditorRedoStack = new();
    private List<JobEditorCellTarget> _jobEditorSelectionSnapshotTargets = [];
    private List<JobEditorCellEdit> _jobEditorPendingCellEditOriginals = [];
    private bool _applyingJobEditorHistory;
    private bool _jobEditorSelectionChangeStartedByMouse;
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
    private MapWorkbenchBrushMode _mapWorkbenchBrushMode = MapWorkbenchBrushMode.Browse;
    private readonly Stack<List<MapWorkbenchCellChange>> _mapMakerMapUndoStack = new();
    private readonly Stack<List<MapWorkbenchCellChange>> _mapMakerMapRedoStack = new();
    private readonly Stack<List<TerrainEditorCellChange>> _mapMakerTerrainUndoStack = new();
    private readonly Stack<List<TerrainEditorCellChange>> _mapMakerTerrainRedoStack = new();
    private readonly List<MapWorkbenchCellChange> _mapMakerPendingMapPaintChanges = new();
    private readonly HashSet<int> _mapMakerPendingMapPaintIndexes = new();
    private readonly List<TerrainEditorCellChange> _mapMakerPendingTerrainPaintChanges = new();
    private readonly HashSet<int> _mapMakerPendingTerrainPaintIndexes = new();
    private byte[] _mapMakerOriginalTerrainCells = Array.Empty<byte>();
    private Image? _mapViewerImage;
    private Bitmap? _mapViewerRenderedImage;
    private TableReferenceNavigationTarget? _currentTableReferenceTarget;
    private UiLayoutSettings _uiLayoutSettings = new();
    private readonly Dictionary<string, SplitContainer> _uiLayoutSplits = new(StringComparer.Ordinal);
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
    private readonly Button _openPlanButton = new();
    private readonly Button _loadRoleEditorButton = new();
    private readonly Button _saveRoleEditorButton = new();
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
    private readonly TextBox _roleCriticalQuoteBox = new();
    private readonly TextBox _roleRetreatQuoteBox = new();
    private readonly TextBox _roleTextDetailInfoBox = new();
    private readonly Button _loadJobEditorButton = new();
    private readonly Button _saveJobEditorButton = new();
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
    private readonly Button _openJobStrategyTableButton = new();
    private readonly Button _filterJobStrategyEditorButton = new();
    private readonly Button _clearJobStrategyEditorFilterButton = new();
    private readonly TextBox _jobStrategyEditorSearchBox = new();
    private readonly DataGridView _jobStrategyEditorGrid = new();
    private readonly TextBox _jobStrategyEditorInfoBox = new();
    private readonly PictureBox _jobStrategyPreviewBox = new();
    private readonly TextBox _jobStrategyPreviewInfoBox = new();
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
    private readonly Button _undoItemEditorButton = new();
    private readonly Button _redoItemEditorButton = new();
    private readonly Button _filterItemEditorButton = new();
    private readonly Button _clearItemEditorFilterButton = new();
    private readonly TextBox _itemEditorSearchBox = new();
    private readonly DataGridView _itemEditorGrid = new();
    private readonly TextBox _itemEditorInfoBox = new();
    private readonly PictureBox _itemIconPreviewBox = new();
    private readonly TextBox _itemIconPreviewInfoBox = new();
    private readonly Button _loadShopEditorButton = new();
    private readonly Button _saveShopEditorButton = new();
    private readonly Button _openShopDataTableButton = new();
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
    private readonly CheckBox _mapMakerShowTerrainCheckBox = new();
    private readonly CheckBox _mapMakerShowGridCheckBox = new();
    private readonly CheckBox _mapMakerEditTerrainCheckBox = new();
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
    private readonly Button _mapMakerPublishMapButton = new();
    private readonly Button _mapMakerPublishTerrainButton = new();
    private readonly Button _loadImageResourcesButton = new();
    private readonly Button _openImageResourceButton = new();
    private readonly Button _replaceImageResourceEntryButton = new();
    private readonly Button _restoreImageResourceEntryButton = new();
    private readonly Button _batchImportImageResourceEntriesButton = new();
    private readonly Button _batchClearImageResourceEntriesButton = new();
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
    private readonly Button _openRsDirectoryButton = new();
    private readonly TextBox _imageAssignmentSearchBox = new();
    private readonly CheckBox _imageAssignmentMissingOnlyCheckBox = new();
    private readonly Button _filterImageAssignmentsButton = new();
    private readonly Button _clearImageAssignmentFilterButton = new();
    private readonly Button _locateImageResourceButton = new();
    private readonly Button _replaceImageResourceButton = new();
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
    private readonly ToolStripMenuItem _legacyScriptContextAddItem = new("添加(&I)\tCtrl+I");
    private readonly ToolStripMenuItem _legacyScriptContextAddSubEventItem = new("添加子事件(&S)\tCtrl+O");
    private readonly ToolStripMenuItem _legacyScriptContextDuplicateItem = new("步进复制(&D)\tCtrl+D");
    private readonly ToolStripMenuItem _legacyScriptContextBatchEditItem = new("批量修改(&R)\tCtrl+R");
    private readonly ToolStripMenuItem _legacyScriptContextDeleteItem = new("删除(&D)\tDelete");
    private readonly ToolStripMenuItem _legacyScriptContextMoveUpItem = new("上移(&U)\tCtrl+Up");
    private readonly ToolStripMenuItem _legacyScriptContextMoveDownItem = new("下移(&D)\tCtrl+Down");
    private readonly ToolStripMenuItem _legacyScriptContextUndoItem = new("撤销(&Z)\tCtrl+Z");
    private readonly ToolStripMenuItem _legacyScriptContextRedoItem = new("前进(&Y)\tCtrl+Y");
    private readonly ToolStripMenuItem _legacyScriptContextCutItem = new("剪切(&T)\tCtrl+X");
    private readonly ToolStripMenuItem _legacyScriptContextCopyItem = new("复制(&C)\tCtrl+C");
    private readonly ToolStripMenuItem _legacyScriptContextPasteItem = new("粘贴(&P)\tCtrl+V");
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
        TerrainBrush
    }

    private sealed record TerrainEditorCellChange(int Index, byte OldValue, byte NewValue);

    private sealed record MapWorkbenchCellChange(int Index, MapCellOverride? OldValue, MapCellOverride? NewValue);

    private sealed record RSceneBackgroundComboItem(ImageResourceEntryInfo Entry)
    {
        public int ImageNumber => Entry.ImageNumber;
        public string DisplayText => $"{Entry.ImageNumber:D3} {Entry.Kind} {Entry.Usage}";
    }

    private sealed record TerrainEditorPreset(byte Id, string Name)
    {
        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"0x{Id:X2}" : $"0x{Id:X2}  {Name}";
    }

    public MainForm()
    {
        Text = "CCZModStudio 6.5 - V0.6 集成原型";
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

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
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
            var tableSelectToolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Padding = new Padding(0, 0, 0, 4)
            };
            _tableList.DropDownStyle = ComboBoxStyle.DropDownList;
            _tableList.Width = 360;
            _tableList.DropDownWidth = 560;
            _tableList.MaxDropDownItems = 24;
            _showAllTables.Margin = new Padding(12, 6, 0, 0);
            tableSelectToolbar.Controls.AddRange(new Control[]
            {
                new Label { Text = "数据表：", AutoSize = true, Padding = new Padding(0, 7, 0, 0) },
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
            var chartToolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true
            };
            _chartColumnCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _chartColumnCombo.Width = 180;
            _renderChartButton.Text = "绘制分布图";
            _renderChartButton.AutoSize = true;
            _renderChartButton.Enabled = false;
            _tableColumnFilterBox.Width = 150;
            _tableColumnFilterBox.PlaceholderText = "按字段/注释筛选列";
            _filterTableColumnsButton.Text = "筛选列";
            _filterTableColumnsButton.AutoSize = true;
            _filterTableColumnsButton.Enabled = false;
            _clearTableColumnFilterButton.Text = "显示全部列";
            _clearTableColumnFilterButton.AutoSize = true;
            _clearTableColumnFilterButton.Enabled = false;
            _dangerTableColumnsOnly.Text = "仅高风险字段";
            _dangerTableColumnsOnly.AutoSize = true;
            _dangerTableColumnsOnly.Enabled = false;
            _exportFieldAnnotationsButton.Text = "导出字段注释";
            _exportFieldAnnotationsButton.AutoSize = true;
            _exportFieldAnnotationsButton.Enabled = false;
            _exportVisibleColumnsCsvButton.Text = "导出可见行列CSV";
            _exportVisibleColumnsCsvButton.AutoSize = true;
            _exportVisibleColumnsCsvButton.Enabled = false;
            _visibleColumnsCsvWithNotes.Text = "含字段说明行";
            _visibleColumnsCsvWithNotes.AutoSize = true;
            _visibleColumnsCsvWithNotes.Checked = true;
            _visibleColumnsCsvWithNotes.Enabled = false;
            _jumpTableReferenceButton.Text = "跳到引用目标";
            _jumpTableReferenceButton.AutoSize = true;
            _jumpTableReferenceButton.Enabled = false;
            _tableReferenceNavigationBox.Width = 360;
            _tableReferenceNavigationBox.ReadOnly = true;
            _tableReferenceNavigationBox.PlaceholderText = "选中引用字段后显示可跳转目标";
            _tableRowFilterBox.Width = 150;
            _tableRowFilterBox.PlaceholderText = "按行内容筛选";
            _filterTableRowsButton.Text = "筛选行";
            _filterTableRowsButton.AutoSize = true;
            _filterTableRowsButton.Enabled = false;
            _clearTableRowFilterButton.Text = "显示全部行";
            _clearTableRowFilterButton.AutoSize = true;
            _clearTableRowFilterButton.Enabled = false;
            _changedTableRowsOnly.Text = "仅已改动行";
            _changedTableRowsOnly.AutoSize = true;
            _changedTableRowsOnly.Enabled = false;
            _tableRowSearchVisibleColumnsOnly.Text = "只搜可见列";
            _tableRowSearchVisibleColumnsOnly.AutoSize = true;
            _tableRowSearchVisibleColumnsOnly.Checked = true;
            _tableRowSearchVisibleColumnsOnly.Enabled = false;
            chartToolbar.Controls.AddRange(new Control[]
            {
                new Label { Text = "数值列：", AutoSize = true, Padding = new Padding(0, 7, 0, 0) },
                _chartColumnCombo,
                _renderChartButton,
                new Label { Text = "列筛选：", AutoSize = true, Padding = new Padding(14, 7, 0, 0) },
                _tableColumnFilterBox,
                _filterTableColumnsButton,
                _clearTableColumnFilterButton,
                _dangerTableColumnsOnly,
                _exportFieldAnnotationsButton,
                _exportVisibleColumnsCsvButton,
                _visibleColumnsCsvWithNotes,
                new Label { Text = "关联：", AutoSize = true, Padding = new Padding(14, 7, 0, 0) },
                _jumpTableReferenceButton,
                _tableReferenceNavigationBox,
                new Label { Text = "行筛选：", AutoSize = true, Padding = new Padding(14, 7, 0, 0) },
                _tableRowFilterBox,
                _filterTableRowsButton,
                _clearTableRowFilterButton,
                _changedTableRowsOnly,
                _tableRowSearchVisibleColumnsOnly
            });
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

        var imageResourceToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _loadImageResourcesButton.Text = "读取图片资源";
        _loadImageResourcesButton.AutoSize = true;
        _openImageResourceButton.Text = "定位文件";
        _openImageResourceButton.AutoSize = true;
        _replaceImageResourceEntryButton.Text = "替换E5条目";
        _replaceImageResourceEntryButton.AutoSize = true;
        _restoreImageResourceEntryButton.Text = "从备份还原";
        _restoreImageResourceEntryButton.AutoSize = true;
        _batchImportImageResourceEntriesButton.Text = "批量导入";
        _batchImportImageResourceEntriesButton.AutoSize = true;
        _batchClearImageResourceEntriesButton.Text = "批量删除";
        _batchClearImageResourceEntriesButton.AutoSize = true;
        _exportImageResourceEntriesButton.Text = "导出条目CSV";
        _exportImageResourceEntriesButton.AutoSize = true;
        _imageResourceCategoryFilterCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _imageResourceCategoryFilterCombo.Width = 112;
        _imageResourceSearchBox.Width = 210;
        _imageResourceSearchBox.PlaceholderText = "筛选文件/别名/用途";
        _filterImageResourcesButton.Text = "筛选";
        _filterImageResourcesButton.AutoSize = true;
        _clearImageResourceFilterButton.Text = "显示全部";
        _clearImageResourceFilterButton.AutoSize = true;
        imageResourceToolbar.Controls.AddRange(new Control[]
        {
            _loadImageResourcesButton,
            _openImageResourceButton,
            _replaceImageResourceEntryButton,
            _restoreImageResourceEntryButton,
            _batchImportImageResourceEntriesButton,
            _batchClearImageResourceEntriesButton,
            _exportImageResourceEntriesButton,
            new Label { Text = "分类：", AutoSize = true, Padding = new Padding(10, 7, 0, 0) },
            _imageResourceCategoryFilterCombo,
            _imageResourceSearchBox,
            _filterImageResourcesButton,
            _clearImageResourceFilterButton
        });
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

        var imageAssignmentPage = new TabPage("人物R/S指定");
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
        var imageToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _loadImageAssignmentsButton.Text = "读取人物R/S";
        _loadImageAssignmentsButton.AutoSize = true;
        _saveImageAssignmentsButton.Text = "保存R/S";
        _saveImageAssignmentsButton.AutoSize = true;
        _saveImageAssignmentsButton.Enabled = false;
        _openRsDirectoryButton.Text = "打开RS目录";
        _openRsDirectoryButton.AutoSize = true;
        _imageAssignmentSearchBox.Width = 220;
        _imageAssignmentSearchBox.PlaceholderText = "筛选人物/职业/R编号/S编号/资源状态";
        _imageAssignmentMissingOnlyCheckBox.Text = "仅缺失资源";
        _imageAssignmentMissingOnlyCheckBox.AutoSize = true;
        _imageAssignmentSPreviewFactionCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _imageAssignmentSPreviewFactionCombo.Width = 92;
        _imageAssignmentSPreviewFactionCombo.Items.AddRange(new object[] { "我军", "友军", "敌军" });
        _imageAssignmentSPreviewFactionCombo.SelectedIndex = 0;
        _filterImageAssignmentsButton.Text = "筛选";
        _filterImageAssignmentsButton.AutoSize = true;
        _clearImageAssignmentFilterButton.Text = "显示全部";
        _clearImageAssignmentFilterButton.AutoSize = true;
        _clearImageAssignmentFilterButton.Enabled = false;
        _locateImageResourceButton.Text = "\u5b9a\u4f4d\u9009\u4e2d\u8d44\u6e90";
        _locateImageResourceButton.AutoSize = true;
        _replaceImageResourceButton.Text = "导入/替换E5";
        _replaceImageResourceButton.AutoSize = true;
        _restoreImageResourceButton.Text = "还原E5条目";
        _restoreImageResourceButton.AutoSize = true;
        _exportMissingImageResourcesButton.Text = "\u5bfc\u51fa\u7f3a\u5931\u62a5\u544a";
        _exportMissingImageResourcesButton.AutoSize = true;
        imageToolbar.Controls.AddRange(new Control[]
        {
            _loadImageAssignmentsButton,
            _saveImageAssignmentsButton,
            _openRsDirectoryButton,
            _imageAssignmentSearchBox,
            _imageAssignmentMissingOnlyCheckBox,
            new Label { Text = "S预览阵营：", AutoSize = true, Padding = new Padding(10, 7, 0, 0) },
            _imageAssignmentSPreviewFactionCombo,
            _filterImageAssignmentsButton,
            _clearImageAssignmentFilterButton,
            _locateImageResourceButton,
            _replaceImageResourceButton,
            _restoreImageResourceButton,
            _exportMissingImageResourcesButton
        });
        imageLayout.Controls.Add(imageToolbar, 0, 0);
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
        AddCollapsibleSplitPanel(imageSplit, 1, "人物R/S表", _imageAssignmentGrid, "BuildImageAssignmentPage.GridPreview.Grid");

        var imagePreviewLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 5,
            Padding = new Padding(6)
        };
        imagePreviewLayout.RowStyles.Clear();
        imagePreviewLayout.ColumnStyles.Clear();
        imagePreviewLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
        imagePreviewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        imagePreviewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        // 0: left spacer, 1: R strip, 2: gap, 3: S strip, 4: right spacer
        imagePreviewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        imagePreviewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        imagePreviewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));
        imagePreviewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        imagePreviewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        // 右侧预览区显示头像；R/S 按 E5 0x110 索引表取图，不再按裸扫出现顺序取候选图。
        _imageAssignmentFacePreviewBox.Dock = DockStyle.Fill;
        _imageAssignmentFacePreviewBox.BackColor = Color.FromArgb(32, 32, 36);
        _imageAssignmentFacePreviewBox.SizeMode = PictureBoxSizeMode.Zoom;
        _imageAssignmentFacePreviewBox.BorderStyle = BorderStyle.FixedSingle;
        imagePreviewLayout.Controls.Add(_imageAssignmentFacePreviewBox, 0, 0);
        imagePreviewLayout.SetColumnSpan(_imageAssignmentFacePreviewBox, 5);

        _imageAssignmentRPreviewBox.Dock = DockStyle.Fill;
        _imageAssignmentRPreviewBox.BackColor = Color.FromArgb(32, 32, 36);
        _imageAssignmentRPreviewBox.SizeMode = PictureBoxSizeMode.Zoom;
        _imageAssignmentRPreviewBox.BorderStyle = BorderStyle.FixedSingle;
        _imageAssignmentRPreviewBox.Margin = new Padding(0);
        imagePreviewLayout.Controls.Add(_imageAssignmentRPreviewBox, 1, 1);

        _imageAssignmentSPreviewBox.Dock = DockStyle.Fill;
        _imageAssignmentSPreviewBox.BackColor = Color.FromArgb(32, 32, 36);
        _imageAssignmentSPreviewBox.SizeMode = PictureBoxSizeMode.Zoom;
        _imageAssignmentSPreviewBox.BorderStyle = BorderStyle.FixedSingle;
        _imageAssignmentSPreviewBox.Margin = new Padding(0);
        imagePreviewLayout.Controls.Add(_imageAssignmentSPreviewBox, 3, 1);

        // Keep R/S boxes as portrait strips even when Panel2 is wide (typical "横长方形" preview area).
        imagePreviewLayout.SizeChanged += (_, _) =>
        {
            if (imagePreviewLayout.ColumnStyles.Count < 5) return;
            var w = imagePreviewLayout.ClientSize.Width - imagePreviewLayout.Padding.Horizontal;
            var h = imagePreviewLayout.ClientSize.Height - imagePreviewLayout.Padding.Vertical;
            if (w <= 0 || h <= 0) return;

            // avatar row is fixed; compute bottom row height.
            var bottomH = Math.Max(0, h - 150);
            if (bottomH <= 0) return;

            // portrait strip width: keep ~3:4 (w:h) for each R/S, but never exceed half width.
            var desiredStripW = (int)Math.Round(bottomH * 0.75);
            // Ensure we leave at least a small gutter in very tight widths.
            desiredStripW = Math.Max(80, desiredStripW);

            // Available width for 2 strips plus gap after removing spacers.
            var gap = (int)Math.Round(imagePreviewLayout.ColumnStyles[2].Width);
            var maxStripW = Math.Max(80, (w - gap) / 2);
            desiredStripW = Math.Min(desiredStripW, maxStripW);

            imagePreviewLayout.ColumnStyles[1].SizeType = SizeType.Absolute;
            imagePreviewLayout.ColumnStyles[3].SizeType = SizeType.Absolute;
            imagePreviewLayout.ColumnStyles[1].Width = desiredStripW;
            imagePreviewLayout.ColumnStyles[3].Width = desiredStripW;
        };

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
        var eexToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _loadEexArchivesButton.Text = "读取 RS/Map .eex";
        _loadEexArchivesButton.AutoSize = true;
        _openEexArchiveButton.Text = "定位选中文件";
        _openEexArchiveButton.AutoSize = true;
        _exportEexArchivesCsvButton.Text = "导出EEX索引CSV";
        _exportEexArchivesCsvButton.AutoSize = true;
        _probeEexEntriesButton.Text = "解析选中EEX区段";
        _probeEexEntriesButton.AutoSize = true;
        _exportEexEntryProbeCsvButton.Text = "导出区段CSV";
        _exportEexEntryProbeCsvButton.AutoSize = true;
        _compareEexCrossFilesButton.Text = "跨文件对比";
        _compareEexCrossFilesButton.AutoSize = true;
        _renderEexHeatmapButton.Text = "生成字节热力图";
        _renderEexHeatmapButton.AutoSize = true;
        _exportEexHeatmapPngButton.Text = "导出热力图PNG";
        _exportEexHeatmapPngButton.AutoSize = true;
        var eexCategoryLabel = new Label { Text = "分类", AutoSize = true, Padding = new Padding(8, 5, 0, 0) };
        _eexArchiveCategoryFilterCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _eexArchiveCategoryFilterCombo.Width = 110;
        var eexSearchLabel = new Label { Text = "关键字", AutoSize = true, Padding = new Padding(8, 5, 0, 0) };
        _eexArchiveSearchBox.Width = 150;
        _filterEexArchivesButton.Text = "筛选";
        _filterEexArchivesButton.AutoSize = true;
        _clearEexArchiveFilterButton.Text = "显示全部";
        _clearEexArchiveFilterButton.AutoSize = true;
        eexToolbar.Controls.AddRange(new Control[]
        {
            _loadEexArchivesButton,
            _openEexArchiveButton,
            _exportEexArchivesCsvButton,
            _probeEexEntriesButton,
            _exportEexEntryProbeCsvButton,
            _compareEexCrossFilesButton,
            _renderEexHeatmapButton,
            _exportEexHeatmapPngButton,
            eexCategoryLabel,
            _eexArchiveCategoryFilterCombo,
            eexSearchLabel,
            _eexArchiveSearchBox,
            _filterEexArchivesButton,
            _clearEexArchiveFilterButton
        });
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
        var scenarioToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _loadScenarioFilesButton.Text = "读取 RS/*.eex";
        _loadScenarioFilesButton.AutoSize = true;
        _openScenarioFileButton.Text = "\u5b9a\u4f4d\u9009\u4e2d\u6587\u4ef6";
        _openScenarioFileButton.AutoSize = true;
        _exportScenarioFileIndexCsvButton.Text = "\u5bfc\u51faRS\u7d22\u5f15CSV";
        _exportScenarioFileIndexCsvButton.AutoSize = true;
        var scenarioKindLabel = new Label { Text = "\u7c7b\u578b", AutoSize = true, Padding = new Padding(8, 7, 0, 0) };
        _scenarioKindFilterCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _scenarioKindFilterCombo.Width = 110;
        var scenarioFileSearchLabel = new Label { Text = "\u6587\u4ef6\u7b5b\u9009", AutoSize = true, Padding = new Padding(8, 7, 0, 0) };
        _scenarioFileSearchBox.Width = 140;
        _filterScenarioFilesButton.Text = "\u7b5b\u9009";
        _filterScenarioFilesButton.AutoSize = true;
        _clearScenarioFileFilterButton.Text = "\u663e\u793a\u5168\u90e8";
        _clearScenarioFileFilterButton.AutoSize = true;
        _scenarioFilesWithTextOnly.Text = "\u4ec5\u6709\u6587\u672c";
        _scenarioFilesWithTextOnly.AutoSize = true;
        _probeScenarioCommandsButton.Text = "探测选中命令";
        _probeScenarioCommandsButton.AutoSize = true;
        _probeScenarioCommandsButton.Enabled = false;
        _buildScenarioStructureButton.Text = "生成结构草图";
        _buildScenarioStructureButton.AutoSize = true;
        _buildScenarioStructureButton.Enabled = false;
        _exportScenarioStructureXmlButton.Text = "导出结构XML";
        _exportScenarioStructureXmlButton.AutoSize = true;
        _exportScenarioStructureXmlButton.Enabled = false;
        _exportScenarioCommandTemplateCatalogButton.Text = "导出命令模板目录";
        _exportScenarioCommandTemplateCatalogButton.AutoSize = true;
        _exportScenarioCommandTemplateCatalogButton.Enabled = false;
        _refreshScenarioCommandTemplatesButton.Text = "刷新模板目录";
        _refreshScenarioCommandTemplatesButton.AutoSize = true;
        _filterScenarioCommandTemplatesButton.Text = "筛选";
        _filterScenarioCommandTemplatesButton.AutoSize = true;
        _clearScenarioCommandTemplateFilterButton.Text = "显示全部";
        _clearScenarioCommandTemplateFilterButton.AutoSize = true;
        _showScenarioCommandTemplateInStructureButton.Text = "筛出当前R/S命令";
        _showScenarioCommandTemplateInStructureButton.AutoSize = true;
        _showScenarioCommandTemplateInStructureButton.Enabled = false;
        _scenarioCommandTemplateSearchBox.Width = 180;
        _scenarioCommandTemplateSearchBox.PlaceholderText = "命令名/槽位/用途/风险";
        _scenarioCommandTemplateCategoryCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _scenarioCommandTemplateCategoryCombo.Width = 140;
        _scenarioCommandTemplateStatusCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _scenarioCommandTemplateStatusCombo.Width = 92;
        _probeScenarioTextsButton.Text = "提取选中文本";
        _probeScenarioTextsButton.AutoSize = true;
        _probeScenarioTextsButton.Enabled = false;
        _exportScenarioTextsButton.Text = "导出文本CSV/TXT";
        _exportScenarioTextsButton.AutoSize = true;
        _exportScenarioTextsButton.Enabled = false;
        _saveScenarioTextsButton.Text = "保存文本到项目";
        _saveScenarioTextsButton.AutoSize = true;
        _saveScenarioTextsButton.Enabled = false;
        _scenarioTextFilterBox.Width = 160;
        _scenarioTextFilterBox.PlaceholderText = "筛选文本/注释";
        _scenarioTextFilterButton.Text = "筛选";
        _scenarioTextFilterButton.AutoSize = true;
        _scenarioTextFilterButton.Enabled = false;
        _scenarioTextFilterClearButton.Text = "清除筛选";
        _scenarioTextFilterClearButton.AutoSize = true;
        _scenarioTextFilterClearButton.Enabled = false;
        _scenarioTextChangedOnly.Text = "仅改动";
        _scenarioTextChangedOnly.AutoSize = true;
        _scenarioTextChangedOnly.Enabled = false;
        scenarioToolbar.Controls.AddRange(new Control[]
        {
            _loadScenarioFilesButton,
            _openScenarioFileButton,
            _exportScenarioFileIndexCsvButton,
            scenarioKindLabel,
            _scenarioKindFilterCombo,
            scenarioFileSearchLabel,
            _scenarioFileSearchBox,
            _filterScenarioFilesButton,
            _clearScenarioFileFilterButton,
            _scenarioFilesWithTextOnly,
            _probeScenarioCommandsButton,
            _buildScenarioStructureButton,
            _exportScenarioStructureXmlButton,
            _probeScenarioTextsButton,
            _exportScenarioTextsButton,
            _saveScenarioTextsButton,
            new Label { Text = "文本筛选：", AutoSize = true, Padding = new Padding(14, 7, 0, 0) },
            _scenarioTextFilterBox,
            _scenarioTextFilterButton,
            _scenarioTextFilterClearButton,
            _scenarioTextChangedOnly
        });
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
        var structureFilterToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _scenarioStructureFilterBox.Width = 180;
        _scenarioStructureFilterBox.PlaceholderText = "命令/偏移/注释筛选";
        _filterScenarioStructureButton.Text = "筛选结构";
        _filterScenarioStructureButton.AutoSize = true;
        _filterScenarioStructureButton.Enabled = false;
        _clearScenarioStructureFilterButton.Text = "显示全部结构";
        _clearScenarioStructureFilterButton.AutoSize = true;
        _clearScenarioStructureFilterButton.Enabled = false;
        _scenarioStructureTemplatesOnly.Text = "仅有模板";
        _scenarioStructureTemplatesOnly.AutoSize = true;
        _scenarioStructureTemplatesOnly.Enabled = false;
        _scenarioStructureTextOnly.Text = "文本/剧情";
        _scenarioStructureTextOnly.AutoSize = true;
        _scenarioStructureTextOnly.Enabled = false;
        _scenarioStructureMapOnly.Text = "地图/坐标";
        _scenarioStructureMapOnly.AutoSize = true;
        _scenarioStructureMapOnly.Enabled = false;
        _scenarioStructureHighRiskOnly.Text = "高风险/需核对";
        _scenarioStructureHighRiskOnly.AutoSize = true;
        _scenarioStructureHighRiskOnly.Enabled = false;
        _scenarioCommandReferenceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _scenarioCommandReferenceCombo.Width = 360;
        _scenarioCommandReferenceCombo.Enabled = false;
        _jumpScenarioCommandReferenceButton.Text = "跳到命令引用";
        _jumpScenarioCommandReferenceButton.AutoSize = true;
        _jumpScenarioCommandReferenceButton.Enabled = false;
        _exportScenarioCommandReferenceChecklistButton.Text = "导出命令引用清单";
        _exportScenarioCommandReferenceChecklistButton.AutoSize = true;
        _exportScenarioCommandReferenceChecklistButton.Enabled = false;
        structureFilterToolbar.Controls.AddRange(new Control[]
        {
            new Label { Text = "结构筛选：", AutoSize = true, Padding = new Padding(0, 7, 0, 0) },
            _scenarioStructureFilterBox,
            _filterScenarioStructureButton,
            _clearScenarioStructureFilterButton,
            _scenarioStructureTemplatesOnly,
            _scenarioStructureTextOnly,
            _scenarioStructureMapOnly,
            _scenarioStructureHighRiskOnly,
            new Label { Text = "命令引用：", AutoSize = true, Padding = new Padding(14, 7, 0, 0) },
            _scenarioCommandReferenceCombo,
            _jumpScenarioCommandReferenceButton,
            _exportScenarioCommandReferenceChecklistButton
        });
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
        var commandTemplateToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        commandTemplateToolbar.Controls.AddRange(new Control[]
        {
            _refreshScenarioCommandTemplatesButton,
            new Label { Text = "关键字：", AutoSize = true, Padding = new Padding(14, 7, 0, 0) },
            _scenarioCommandTemplateSearchBox,
            new Label { Text = "分类：", AutoSize = true, Padding = new Padding(8, 7, 0, 0) },
            _scenarioCommandTemplateCategoryCombo,
            new Label { Text = "状态：", AutoSize = true, Padding = new Padding(8, 7, 0, 0) },
            _scenarioCommandTemplateStatusCombo,
            _filterScenarioCommandTemplatesButton,
            _clearScenarioCommandTemplateFilterButton,
            _showScenarioCommandTemplateInStructureButton,
            _exportScenarioCommandTemplateCatalogButton
        });
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
        var lsToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _loadLsResourcesButton.Text = "读取 Ls/E5 资源";
        _loadLsResourcesButton.AutoSize = true;
        _openLsResourceButton.Text = "定位选中文件";
        _openLsResourceButton.AutoSize = true;
        _exportLsResourcesCsvButton.Text = "导出CSV";
        _exportLsResourcesCsvButton.AutoSize = true;
        _renderLsResourceHeatmapButton.Text = "生成字节热力图";
        _renderLsResourceHeatmapButton.AutoSize = true;
        _exportLsResourceHeatmapPngButton.Text = "导出热力图PNG";
        _exportLsResourceHeatmapPngButton.AutoSize = true;
        _exportLsResourceHeatmapPngButton.Enabled = false;
        var lsCategoryLabel = new Label { Text = "分类", AutoSize = true, Padding = new Padding(8, 5, 0, 0) };
        _lsResourceCategoryFilterCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _lsResourceCategoryFilterCombo.Width = 120;
        var lsSearchLabel = new Label { Text = "关键字", AutoSize = true, Padding = new Padding(8, 5, 0, 0) };
        _lsResourceSearchBox.Width = 180;
        _filterLsResourcesButton.Text = "筛选";
        _filterLsResourcesButton.AutoSize = true;
        _clearLsResourceFilterButton.Text = "显示全部";
        _clearLsResourceFilterButton.AutoSize = true;
        lsToolbar.Controls.AddRange(new Control[]
        {
            _loadLsResourcesButton,
            _openLsResourceButton,
            _exportLsResourcesCsvButton,
            _renderLsResourceHeatmapButton,
            _exportLsResourceHeatmapPngButton,
            lsCategoryLabel,
            _lsResourceCategoryFilterCombo,
            lsSearchLabel,
            _lsResourceSearchBox,
            _filterLsResourcesButton,
            _clearLsResourceFilterButton
        });
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
        var hexzmapToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _loadHexzmapProbeButton.Text = "读取 Hexzmap.e5";
        _loadHexzmapProbeButton.AutoSize = true;
        _exportHexzmapProbeCsvButton.Text = "导出地形探针CSV";
        _exportHexzmapProbeCsvButton.AutoSize = true;
        _exportHexzmapProbeCsvButton.Enabled = false;
        _exportHexzmapOverlayPngButton.Text = "导出当前叠加PNG";
        _exportHexzmapOverlayPngButton.AutoSize = true;
        _exportHexzmapOverlayPngButton.Enabled = false;
        _hexzmapOverlayMapCheckBox.Text = "叠加地图底图";
        _hexzmapOverlayMapCheckBox.AutoSize = true;
        _hexzmapOverlayMapCheckBox.Checked = true;
        _hexzmapOverlayMapCheckBox.Margin = new Padding(16, 6, 3, 3);
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
        public override string ToString() => $"0x{Id:X2} {Name}";
    }

    private sealed record JobStrategyComboItem(int Value, string Text);

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
