using System.Text;
using System.Text.Json;
using CCZModStudio.Core;
using CCZModStudio.Models;

namespace CCZModStudio;

internal sealed class EffectValidationDialog : Form
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly CczProject _originalProject;
    private readonly EffectProfileOnboardingPlan _onboarding;
    private readonly IReadOnlyList<HookExecutionContract> _contracts;
    private readonly Label _summary = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly TextBox _details = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill };
    private readonly Button _advance = new() { Text = "推进当前场景", AutoSize = true, Enabled = false };
    private readonly Button _capture = new() { Text = "采集当前命中", AutoSize = true, Enabled = false };
    private readonly Button _finalize = new() { Text = "签发并导入", AutoSize = true, Enabled = false };
    private readonly Button _close = new() { Text = "关闭", AutoSize = true, DialogResult = DialogResult.Cancel };
    private readonly List<EffectEvidenceBundleV3> _imported = [];
    private EffectValidationHostClient? _client;
    private EffectProbeSession? _session;
    private int _contractIndex;
    private bool _busy;

    public EffectValidationDialog(
        CczProject originalProject,
        EffectProfileOnboardingPlan onboarding,
        IReadOnlyList<HookExecutionContract> contracts)
    {
        _originalProject = originalProject;
        _onboarding = onboarding;
        _contracts = contracts;
        Text = "特效权限动态验证";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(720, 480);
        Size = new Size(880, 620);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(_summary, 0, 0);
        root.Controls.Add(_details, 0, 1);
        var commands = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        commands.Controls.AddRange([_close, _finalize, _capture, _advance]);
        root.Controls.Add(commands, 0, 2);
        Controls.Add(root);
        AcceptButton = _capture;
        CancelButton = _close;
        Shown += async (_, _) => await StartAsync();
        FormClosed += async (_, _) =>
        {
            if (_client != null) await _client.DisposeAsync();
        };
        _advance.Click += async (_, _) => await AdvanceAsync();
        _capture.Click += async (_, _) => await CaptureAsync();
        _finalize.Click += async (_, _) => await FinalizeAsync();
    }

    public bool ValidationCompleted { get; private set; }

    private async Task StartAsync()
    {
        if (_contracts.Count == 0)
        {
            ShowFailure("当前没有具备代码身份的行为契约可供动态验证。缺少静态契约的项目仍保持只读。");
            return;
        }
        await RunBusyAsync(async () =>
        {
            _summary.Text = "正在启动同批次 GameDebug 验证宿主...";
            var release = await Task.Run(() => new EffectReleaseConsistencyService().Read(forceRefresh: true));
            if (!release.CanWrite) throw new InvalidOperationException("发布组件不一致：" + release.ReasonZh);
            _client = await EffectValidationHostClient.StartAsync(release);
            await StartContractAsync();
        });
    }

    private async Task StartContractAsync()
    {
        var contract = _contracts[_contractIndex];
        _summary.Text = $"契约 {_contractIndex + 1}/{_contracts.Count}：{contract.ContractFamilyId}，正在创建沙箱会话...";
        var response = await _client!.CallAsync<EffectValidationStartRequest, JsonElement>("start", new EffectValidationStartRequest
        {
            ContractId = contract.ContractId,
            ContractHash = contract.ContractHash,
            ContractCodeIdentityHash = contract.ContractCodeIdentityHash,
            ProfileId = _onboarding.ProfileAudit.ProfileId,
            NormalizedProfileIdentity = _onboarding.ProfileAudit.NormalizedProfileIdentity,
            ContractVersion = contract.ContractVersion,
            ValidationRecipe = contract.ValidationRecipe,
            BaseExeSha256 = contract.ExeSha256,
            SandboxPatchSha256 = _onboarding.SandboxExeSha256,
            ContinuationAddress = contract.ContinuationAddress,
            SandboxRoot = _onboarding.SandboxRoot,
            LaunchDebugger = true
        });
        var validation = response.GetProperty("validation");
        var sessionPath = validation.GetProperty("session_path").GetString()
                          ?? throw new InvalidOperationException("GameDebug 没有返回验证会话路径。");
        _session = ReadSession(sessionPath);
        var debuggerError = response.TryGetProperty("debugger_error", out var error) && error.ValueKind == JsonValueKind.String
            ? error.GetString()
            : null;
        if (!string.IsNullOrWhiteSpace(debuggerError))
            AppendDetails("调试器尚未自动就绪：" + debuggerError);
        await AdvanceAsyncCore();
    }

    private async Task AdvanceAsync()
    {
        await RunBusyAsync(AdvanceAsyncCore);
    }

    private async Task AdvanceAsyncCore()
    {
        var scenario = CurrentScenario();
        if (scenario == null)
        {
            SetReadyToFinalize();
            return;
        }
        var result = await _client!.CallAsync<EffectValidationSessionRequest, JsonElement>("advance", new EffectValidationSessionRequest
        {
            SessionPath = SessionPath(),
            BatchIndex = scenario.BatchIndex,
            ScenarioId = scenario.ScenarioId
        });
        _summary.Text = $"契约 {_contractIndex + 1}/{_contracts.Count}，场景 {scenario.BatchIndex}/{_session!.Scenarios.Count}：{scenario.DisplayNameZh}";
        _details.Text = scenario.InstructionZh + Environment.NewLine + Environment.NewLine + Pretty(result);
        _advance.Enabled = true;
        _capture.Enabled = true;
        _finalize.Enabled = false;
    }

    private async Task CaptureAsync()
    {
        await RunBusyAsync(async () =>
        {
            var scenario = CurrentScenario() ?? throw new InvalidOperationException("没有等待采集的场景。");
            var result = await _client!.CallAsync<EffectValidationSessionRequest, JsonElement>("capture", new EffectValidationSessionRequest
            {
                SessionPath = SessionPath(),
                BatchIndex = scenario.BatchIndex,
                ScenarioId = scenario.ScenarioId
            });
            AppendDetails("已采集：" + Pretty(result));
            _session = ReadSession(SessionPath());
            if (CurrentScenario() == null) SetReadyToFinalize();
            else await AdvanceAsyncCore();
        });
    }

    private async Task FinalizeAsync()
    {
        await RunBusyAsync(async () =>
        {
            var response = await _client!.CallAsync<EffectValidationSessionRequest, JsonElement>("finalize", new EffectValidationSessionRequest
            {
                SessionPath = SessionPath()
            });
            var bundlePath = response.GetProperty("bundle_path").GetString()
                             ?? throw new InvalidOperationException("GameDebug 没有返回 V3 证据包路径。");
            var bundle = JsonSerializer.Deserialize<EffectEvidenceBundleV3>(await File.ReadAllTextAsync(bundlePath, Encoding.UTF8), JsonOptions)
                         ?? throw new InvalidOperationException("V3 证据包无法读取。");
            var imported = await Task.Run(() => EffectEvidenceBundleService.VerifyAndStoreV3(_originalProject, bundle));
            if (!imported.Accepted)
                throw new InvalidOperationException(imported.SummaryZh + " " + string.Join("；", imported.WarningsZh));
            _imported.Add(bundle);
            AppendDetails(imported.SummaryZh);
            _contractIndex++;
            if (_contractIndex < _contracts.Count)
            {
                await StartContractAsync();
                return;
            }

            if (_onboarding.ProfileTrustTier == EffectProfileTrustTier.SandboxCandidate)
            {
                var sandbox = new ProjectDetector().CreateProjectFromGameRoot(_onboarding.SandboxRoot);
                var promotion = await Task.Run(() => new LocalEffectProfileService().Promote(_originalProject, sandbox, _imported));
                if (!promotion.Promoted)
                    throw new InvalidOperationException(promotion.SummaryZh);
                AppendDetails(promotion.SummaryZh);
            }
            ValidationCompleted = true;
            _summary.Text = "V3 证据已签发、导入并刷新权限；正式项目仍会在每次 Apply 前重新校验。";
            _advance.Enabled = _capture.Enabled = _finalize.Enabled = false;
            _close.Text = "完成";
            _close.DialogResult = DialogResult.OK;
        });
    }

    private void SetReadyToFinalize()
    {
        _summary.Text = $"契约 {_contractIndex + 1}/{_contracts.Count} 的场景已采集完成，可以签发 V3 证据。";
        _advance.Enabled = _capture.Enabled = false;
        _finalize.Enabled = true;
    }

    private EffectProbeScenarioState? CurrentScenario() => _session?.Scenarios.FirstOrDefault(item => !item.Captured);
    private string SessionPath() => _session == null
        ? throw new InvalidOperationException("验证会话尚未创建。")
        : Path.Combine(_session.SessionRoot, "effect-probe-session.json");

    private static EffectProbeSession ReadSession(string path)
        => JsonSerializer.Deserialize<EffectProbeSession>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
           ?? throw new InvalidOperationException("验证会话文件无法读取。");

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        _advance.Enabled = _capture.Enabled = _finalize.Enabled = false;
        try { await action(); }
        catch (Exception ex) { ShowFailure(ex.Message); }
        finally { _busy = false; }
    }

    private void ShowFailure(string message)
    {
        _summary.Text = "验证尚未完成：" + message;
        AppendDetails(message);
        _advance.Enabled = _session != null && CurrentScenario() != null;
        _capture.Enabled = _session != null && CurrentScenario() != null;
        _finalize.Enabled = _session != null && CurrentScenario() == null;
    }

    private void AppendDetails(string value)
    {
        if (_details.TextLength > 0) _details.AppendText(Environment.NewLine + Environment.NewLine);
        _details.AppendText(value);
    }

    private static string Pretty(JsonElement value)
        => JsonSerializer.Serialize(value, JsonOptions);
}
