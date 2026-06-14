using CCZModStudio.Core;
using CCZModStudio.Models;
using System.ComponentModel;
using System.Globalization;

namespace CCZModStudio;

internal sealed class BattlefieldUnitStatusDialog : Form
{
    private const int KeepValue = int.MinValue;

    private static readonly IReadOnlyList<BattlefieldUnitStatusLookupItem> JobLevelItems =
    [
        KeepItem(),
        new BattlefieldUnitStatusLookupItem { Value = 0, Text = "0：初级" },
        new BattlefieldUnitStatusLookupItem { Value = 1, Text = "1：中级" },
        new BattlefieldUnitStatusLookupItem { Value = 2, Text = "2：高级" }
    ];

    private static readonly IReadOnlyList<BattlefieldUnitStatusLookupItem> AiItems =
    [
        KeepItem(),
        new BattlefieldUnitStatusLookupItem { Value = 0, Text = "0：被动" },
        new BattlefieldUnitStatusLookupItem { Value = 1, Text = "1：主动" },
        new BattlefieldUnitStatusLookupItem { Value = 2, Text = "2：坚守" },
        new BattlefieldUnitStatusLookupItem { Value = 3, Text = "3：攻击" },
        new BattlefieldUnitStatusLookupItem { Value = 4, Text = "4：到点" },
        new BattlefieldUnitStatusLookupItem { Value = 5, Text = "5：跟随" },
        new BattlefieldUnitStatusLookupItem { Value = 6, Text = "6：逃离" }
    ];

    private static readonly IReadOnlyList<BattlefieldUnitStatusLookupItem> EquipmentLevelItems =
    [
        KeepItem(),
        new BattlefieldUnitStatusLookupItem { Value = 0, Text = "0：默认等级" },
        .. Enumerable.Range(1, 16).Select(value => new BattlefieldUnitStatusLookupItem
        {
            Value = value,
            Text = $"{value}：Lv{value}"
        })
    ];

    private readonly BattlefieldUnitStatusDraft _initial;
    private readonly ComboBox _levelCombo = new();
    private readonly ComboBox _jobLevelCombo = new();
    private readonly ComboBox _aiCombo = new();
    private readonly ComboBox _weaponCombo = new();
    private readonly ComboBox _weaponLevelCombo = new();
    private readonly ComboBox _armorCombo = new();
    private readonly ComboBox _armorLevelCombo = new();
    private readonly ComboBox _assistCombo = new();
    private readonly ComboBox _jobCombo = new();
    private readonly DataGridView _abilityGrid = new();
    private readonly TextBox _previewBox = new();
    private readonly BindingList<AbilityEditRow> _abilityRows;

    public BattlefieldUnitStatusDialog(
        BattlefieldUnitStatusDraft draft,
        IReadOnlyList<BattlefieldUnitStatusLookupItem> jobItems,
        IReadOnlyList<BattlefieldUnitStatusLookupItem> weaponItems,
        IReadOnlyList<BattlefieldUnitStatusLookupItem> armorItems,
        IReadOnlyList<BattlefieldUnitStatusLookupItem> assistItems)
    {
        _initial = CloneDraft(draft);
        _abilityRows = new BindingList<AbilityEditRow>(
            _initial.Abilities.Select(AbilityEditRow.FromDraft).ToList());
        Draft = CloneDraft(draft);

        Text = "战场单位状态";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = true;
        ShowIcon = false;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(860, 680);
        MinimumSize = new Size(720, 560);

        BuildLayout(jobItems, weaponItems, armorItems, assistItems);
        LoadValues();
        UpdatePreview();
    }

    public BattlefieldUnitStatusDraft Draft { get; private set; }

    private void BuildLayout(
        IReadOnlyList<BattlefieldUnitStatusLookupItem> jobItems,
        IReadOnlyList<BattlefieldUnitStatusLookupItem> weaponItems,
        IReadOnlyList<BattlefieldUnitStatusLookupItem> armorItems,
        IReadOnlyList<BattlefieldUnitStatusLookupItem> assistItems)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 8),
            Text = $"{_initial.PersonId} {_initial.PersonName}    {_initial.SourceSummary}"
        }, 0, 0);

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 5,
            AutoSize = true
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.Controls.Add(fields, 0, 1);

        AddField(fields, 0, "等级加成", ConfigureCombo(_levelCombo, BuildLevelItems()));
        AddField(fields, 1, "兵种级", ConfigureCombo(_jobLevelCombo, JobLevelItems));
        AddField(fields, 2, "AI方针", ConfigureCombo(_aiCombo, AiItems));
        AddField(fields, 3, "兵种", ConfigureCombo(_jobCombo, WithKeep(jobItems)));
        AddField(fields, 4, "武器", ConfigureCombo(_weaponCombo, WithKeep(weaponItems)));
        AddField(fields, 5, "武器等级", ConfigureCombo(_weaponLevelCombo, EquipmentLevelItems));
        AddField(fields, 6, "防具", ConfigureCombo(_armorCombo, WithKeep(armorItems)));
        AddField(fields, 7, "防具等级", ConfigureCombo(_armorLevelCombo, EquipmentLevelItems));
        AddField(fields, 8, "辅助", ConfigureCombo(_assistCombo, WithKeep(assistItems)));

        ConfigureAbilityGrid();
        root.Controls.Add(_abilityGrid, 0, 2);

        _previewBox.Dock = DockStyle.Fill;
        _previewBox.Multiline = true;
        _previewBox.ScrollBars = ScrollBars.Vertical;
        _previewBox.ReadOnly = true;
        _previewBox.BorderStyle = BorderStyle.FixedSingle;
        _previewBox.BackColor = Color.FromArgb(250, 250, 250);
        root.Controls.Add(_previewBox, 0, 3);

        var okButton = new Button { Text = "确定写回", AutoSize = true };
        okButton.Click += (_, _) => CommitAndClose();
        var cancelButton = new Button
        {
            Text = "取消",
            AutoSize = true,
            DialogResult = DialogResult.Cancel
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0)
        };
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(okButton);
        root.Controls.Add(buttons, 0, 4);

        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    private static IReadOnlyList<BattlefieldUnitStatusLookupItem> BuildLevelItems()
    {
        var rows = new List<BattlefieldUnitStatusLookupItem> { KeepItem() };
        for (var value = -99; value <= 99; value++)
        {
            rows.Add(new BattlefieldUnitStatusLookupItem
            {
                Value = value,
                Text = value >= 0 ? $"{value}：+{value}级" : $"{value}：{value}级"
            });
        }

        return rows;
    }

    private static IReadOnlyList<BattlefieldUnitStatusLookupItem> WithKeep(IReadOnlyList<BattlefieldUnitStatusLookupItem> items)
    {
        if (items.Count > 0 && items[0].Value == KeepValue) return items;
        return [KeepItem(), .. items];
    }

    private static BattlefieldUnitStatusLookupItem KeepItem()
        => new() { Value = KeepValue, Text = "保持原值" };

    private static void AddField(TableLayoutPanel panel, int fieldIndex, string label, Control editor)
    {
        var row = fieldIndex / 2;
        var col = (fieldIndex % 2) * 2;
        while (panel.RowStyles.Count <= row)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        panel.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(0, 5, 0, 5)
        }, col, row);
        panel.Controls.Add(editor, col + 1, row);
    }

    private ComboBox ConfigureCombo(ComboBox combo, IReadOnlyList<BattlefieldUnitStatusLookupItem> items)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.Dock = DockStyle.Fill;
        combo.Margin = new Padding(0, 2, 12, 2);
        combo.Items.Clear();
        foreach (var item in items)
        {
            combo.Items.Add(item);
        }

        combo.SelectedIndexChanged += (_, _) => UpdatePreview();
        return combo;
    }

    private void ConfigureAbilityGrid()
    {
        _abilityGrid.Dock = DockStyle.Fill;
        _abilityGrid.AllowUserToAddRows = false;
        _abilityGrid.AllowUserToDeleteRows = false;
        _abilityGrid.AllowUserToResizeRows = false;
        _abilityGrid.AutoGenerateColumns = false;
        _abilityGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _abilityGrid.BorderStyle = BorderStyle.FixedSingle;
        _abilityGrid.MultiSelect = false;
        _abilityGrid.RowHeadersVisible = false;
        _abilityGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _abilityGrid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        _abilityGrid.StandardTab = true;

        _abilityGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(AbilityEditRow.Write),
            HeaderText = "写回",
            FillWeight = 42
        });
        _abilityGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(AbilityEditRow.Name),
            HeaderText = "五维",
            ReadOnly = true,
            FillWeight = 70
        });
        _abilityGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            DataPropertyName = nameof(AbilityEditRow.OperationText),
            HeaderText = "操作",
            DataSource = new[] { "=", "+", "-" },
            FillWeight = 58
        });
        _abilityGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(AbilityEditRow.ValueText),
            HeaderText = "数值",
            FillWeight = 90
        });
        _abilityGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(AbilityEditRow.SourceText),
            HeaderText = "来源",
            ReadOnly = true,
            FillWeight = 120
        });

        _abilityGrid.DataSource = _abilityRows;
        _abilityGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_abilityGrid.IsCurrentCellDirty)
            {
                _abilityGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _abilityGrid.CellValueChanged += (_, _) => UpdatePreview();
        _abilityGrid.CellEndEdit += (_, _) => UpdatePreview();
        _abilityGrid.DataError += (_, e) => e.ThrowException = false;
    }

    private void LoadValues()
    {
        SelectComboValue(_levelCombo, _initial.LevelBonus);
        SelectComboValue(_jobLevelCombo, _initial.JobLevel);
        SelectComboValue(_aiCombo, _initial.AiPolicy);
        SelectComboValue(_weaponCombo, _initial.HasEquipmentCommand ? _initial.Weapon : null);
        SelectComboValue(_weaponLevelCombo, _initial.HasEquipmentCommand ? _initial.WeaponLevel : null);
        SelectComboValue(_armorCombo, _initial.HasEquipmentCommand ? _initial.Armor : null);
        SelectComboValue(_armorLevelCombo, _initial.HasEquipmentCommand ? _initial.ArmorLevel : null);
        SelectComboValue(_assistCombo, _initial.HasEquipmentCommand ? _initial.Assist : null);
        SelectComboValue(_jobCombo, _initial.HasJobCommand ? _initial.JobId : null);
    }

    private static void SelectComboValue(ComboBox combo, int? value)
    {
        var wanted = value ?? KeepValue;
        for (var i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is BattlefieldUnitStatusLookupItem item && item.Value == wanted)
            {
                combo.SelectedIndex = i;
                return;
            }
        }

        if (value.HasValue)
        {
            combo.Items.Add(new BattlefieldUnitStatusLookupItem
            {
                Value = value.Value,
                Text = $"{value.Value}：自定义"
            });
            combo.SelectedIndex = combo.Items.Count - 1;
            return;
        }

        combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;
    }

    private void UpdatePreview()
    {
        try
        {
            var draft = BuildDraft(validate: false);
            _previewBox.Text = BattlefieldUnitStatusWriteService.BuildPreview(draft);
        }
        catch
        {
            _previewBox.Text = "当前输入尚未完成，确定时会做完整校验。";
        }
    }

    private void CommitAndClose()
    {
        try
        {
            Validate();
            _abilityGrid.EndEdit();
            Draft = BuildDraft(validate: true);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "状态字段无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private BattlefieldUnitStatusDraft BuildDraft(bool validate)
    {
        var draft = CloneDraft(_initial);
        draft.LevelBonus = SelectedNullableValue(_levelCombo);
        draft.JobLevel = SelectedNullableValue(_jobLevelCombo);
        draft.AiPolicy = SelectedNullableValue(_aiCombo);
        draft.Weapon = SelectedNullableValue(_weaponCombo);
        draft.WeaponLevel = SelectedNullableValue(_weaponLevelCombo);
        draft.Armor = SelectedNullableValue(_armorCombo);
        draft.ArmorLevel = SelectedNullableValue(_armorLevelCombo);
        draft.Assist = SelectedNullableValue(_assistCombo);
        draft.JobId = SelectedNullableValue(_jobCombo);

        draft.Abilities.Clear();
        foreach (var row in _abilityRows)
        {
            var ability = new BattlefieldUnitAbilityDraft
            {
                AbilityId = row.AbilityId,
                Name = row.Name,
                HasCommand = row.HasCommand,
                Operation = OperationFromText(row.OperationText)
            };
            if (row.Write)
            {
                var valueText = (row.ValueText ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(valueText))
                {
                    draft.Abilities.Add(ability);
                    continue;
                }

                if (!int.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                {
                    if (validate)
                    {
                        throw new InvalidOperationException($"{row.Name} 的数值必须是整数。");
                    }
                }
                else
                {
                    ability.Value = value;
                }
            }

            draft.Abilities.Add(ability);
        }

        draft.CommandPreview = BattlefieldUnitStatusWriteService.BuildPreview(draft);
        return draft;
    }

    private static int? SelectedNullableValue(ComboBox combo)
        => combo.SelectedItem is BattlefieldUnitStatusLookupItem item && item.Value != KeepValue
            ? item.Value
            : null;

    private static int OperationFromText(string text)
        => text switch
        {
            "+" => 1,
            "-" => 2,
            _ => 0
        };

    private static string OperationToText(int? operation)
        => operation switch
        {
            1 => "+",
            2 => "-",
            _ => "="
        };

    private static BattlefieldUnitStatusDraft CloneDraft(BattlefieldUnitStatusDraft source)
    {
        var clone = new BattlefieldUnitStatusDraft
        {
            TargetKey = source.TargetKey,
            ScenarioFileName = source.ScenarioFileName,
            PersonId = source.PersonId,
            PersonName = source.PersonName,
            CommandId = source.CommandId,
            RecordIndex = source.RecordIndex,
            LevelBonus = source.LevelBonus,
            JobLevel = source.JobLevel,
            AiPolicy = source.AiPolicy,
            Weapon = source.Weapon,
            WeaponLevel = source.WeaponLevel,
            Armor = source.Armor,
            ArmorLevel = source.ArmorLevel,
            Assist = source.Assist,
            JobId = source.JobId,
            HasEquipmentCommand = source.HasEquipmentCommand,
            HasJobCommand = source.HasJobCommand,
            EquipmentBoundarySummary = source.EquipmentBoundarySummary,
            SourceSummary = source.SourceSummary,
            CommandPreview = source.CommandPreview
        };
        clone.Abilities.Clear();
        foreach (var ability in source.Abilities)
        {
            clone.Abilities.Add(new BattlefieldUnitAbilityDraft
            {
                AbilityId = ability.AbilityId,
                Name = ability.Name,
                Operation = ability.Operation,
                Value = ability.Value,
                HasCommand = ability.HasCommand
            });
        }

        return clone;
    }

    private sealed class AbilityEditRow
    {
        public int AbilityId { get; init; }
        public string Name { get; init; } = string.Empty;
        public bool Write { get; set; }
        public string OperationText { get; set; } = "=";
        public string ValueText { get; set; } = string.Empty;
        public bool HasCommand { get; init; }
        public string SourceText => HasCommand ? "已有0x38" : "未设置";

        public static AbilityEditRow FromDraft(BattlefieldUnitAbilityDraft source)
            => new()
            {
                AbilityId = source.AbilityId,
                Name = source.Name,
                Write = true,
                OperationText = OperationToText(source.Operation),
                ValueText = source.Value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                HasCommand = source.HasCommand
            };
    }
}
