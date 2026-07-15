using CCZModStudio.Formats;
using CCZModStudio.Models;
using System.Data;
using System.Globalization;

namespace CCZModStudio.Core;

public sealed class BattlefieldUnitStatusWriteService
{
    public const int ManagedSceneIndex = 1;
    public const string Scene2PlusStatusWriteDisabledMessage = "Scene2 及之后属于剧情脚本，不受初始出场设置管理，请在左侧剧本树手动编辑。";
    public const string FriendEquipmentStatusBlockTitle = "友军装备设定";
    public const string EnemyEquipmentStatusBlockTitle = "敌军装备设定";
    public const string FriendRuntimeStatusBlockTitle = "友军战场能力设定";
    public const string EnemyRuntimeStatusBlockTitle = "敌军战场能力设定";
    public const string EquipmentStatusBlockTitle = "个人装备设定";
    public const string RuntimeStatusBlockTitle = "个人战场能力设定";
    public const string CombinedStatusBlockTitle = "个人装备与能力设定";

    private static readonly IReadOnlySet<string> EquipmentStatusBlockTitles = new HashSet<string>(StringComparer.Ordinal)
    {
        FriendEquipmentStatusBlockTitle,
        EnemyEquipmentStatusBlockTitle,
        EquipmentStatusBlockTitle,
        CombinedStatusBlockTitle
    };

    private static readonly IReadOnlySet<string> RuntimeStatusBlockTitles = new HashSet<string>(StringComparer.Ordinal)
    {
        FriendRuntimeStatusBlockTitle,
        EnemyRuntimeStatusBlockTitle,
        RuntimeStatusBlockTitle,
        CombinedStatusBlockTitle
    };

    private static readonly IReadOnlySet<string> AnyStatusBlockTitles = new HashSet<string>(StringComparer.Ordinal)
    {
        FriendEquipmentStatusBlockTitle,
        EnemyEquipmentStatusBlockTitle,
        FriendRuntimeStatusBlockTitle,
        EnemyRuntimeStatusBlockTitle,
        EquipmentStatusBlockTitle,
        RuntimeStatusBlockTitle,
        CombinedStatusBlockTitle
    };

    private static readonly IReadOnlyDictionary<int, string> AbilityNames = new Dictionary<int, string>
    {
        [10] = "武力",
        [11] = "统率",
        [12] = "智力",
        [13] = "敏捷",
        [14] = "运气"
    };

    private readonly LegacyScenarioReader _reader = new();
    private readonly LegacyScenarioWriter _writer = new();
    private readonly CczEngineProfileService _engineProfileService = new();
    private readonly BattlefieldUnitDataDefaultService _dataDefaultService = new();

    public static bool IsWritableStatusTarget(BattlefieldPlacedUnit placement)
        => TryParseLocator(placement.TargetKey, out var locator) &&
           locator.SceneIndex == ManagedSceneIndex &&
           TryParseCommandId(locator.CommandIdHex, out var commandId) &&
           commandId is 0x46 or 0x47 &&
           locator.RecordIndex >= 0;

    public static bool IsScene2PlusStatusTarget(BattlefieldPlacedUnit placement)
        => TryParseLocator(placement.TargetKey, out var locator) &&
           locator.SceneIndex > ManagedSceneIndex;

    public static string GetEquipmentStatusBlockTitle(int commandId)
        => commandId == 0x47 ? EnemyEquipmentStatusBlockTitle : FriendEquipmentStatusBlockTitle;

    public static string GetRuntimeStatusBlockTitle(int commandId)
        => commandId == 0x47 ? EnemyRuntimeStatusBlockTitle : FriendRuntimeStatusBlockTitle;

    public BattlefieldUnitStatusDraft LoadDraft(
        ScenarioFileInfo scenario,
        SceneStringDocument dictionary,
        BattlefieldPlacedUnit placement)
    {
        var document = _reader.Read(scenario.Path, dictionary);
        return LoadDraft(document, scenario.FileName, placement);
    }

    public BattlefieldUnitStatusDraft LoadDraft(
        LegacyScenarioDocument document,
        string scenarioFileName,
        BattlefieldPlacedUnit placement)
        => LoadDraft(document, scenarioFileName, placement, null, null);

    public BattlefieldUnitStatusDraft LoadDraft(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        LegacyScenarioDocument document,
        string scenarioFileName,
        BattlefieldPlacedUnit placement)
        => LoadDraft(document, scenarioFileName, placement, project, tables);

    private BattlefieldUnitStatusDraft LoadDraft(
        LegacyScenarioDocument document,
        string scenarioFileName,
        BattlefieldPlacedUnit placement,
        CczProject? project,
        IReadOnlyList<HexTableDefinition>? tables)
    {
        var locator = BuildWritableLocator(placement);
        var target = FindCommand(document, locator)
            ?? throw new InvalidOperationException("未在当前 S 剧本树中找到该单位绑定的 46/47 出场设定。");
        var definition = BattlefieldDeploymentRecordDefinition.FromCommandId(target.CommandId)
            ?? throw new InvalidOperationException("战场单位状态弹窗只支持 46/47 友军/敌军出场设定。");
        ValidateRecordRange(target, definition, locator.RecordIndex);

        var start = locator.RecordIndex * definition.GroupSize;
        var personId = GetInt(target, start + definition.PersonIndex);
        var dataDefaults = project != null && tables != null
            ? _dataDefaultService.LoadPersonDefaults(project, tables, personId)
            : null;
        var draft = new BattlefieldUnitStatusDraft
        {
            TargetKey = placement.TargetKey,
            ScenarioFileName = scenarioFileName,
            PersonId = personId,
            PersonName = !string.IsNullOrWhiteSpace(dataDefaults?.PersonName)
                ? dataDefaults.PersonName
                : string.IsNullOrWhiteSpace(placement.Name) ? $"人物{personId}" : placement.Name,
            CommandId = target.CommandId,
            RecordIndex = locator.RecordIndex,
            LevelBonus = GetInt(target, start + definition.LevelIndex),
            JobLevel = GetInt(target, start + definition.JobLevelIndex),
            AiPolicy = GetInt(target, start + definition.AiIndex),
            DataDefaults = dataDefaults,
            SourceSummary = $"{target.CommandIdHex} {target.CommandName} Scene={target.SceneIndex} Section={target.SectionIndex} Command={target.CommandIndex} Record={locator.RecordIndex}"
        };

        var sceneStatusCommands = GetManagedSceneLogicalCommands(document, target.SceneIndex);
        if (FindLastSamePersonCommand(sceneStatusCommands, 0x48, personId) is { } equipment)
        {
            draft.HasEquipmentCommand = true;
            draft.Weapon = GetIntOrNull(equipment, 1);
            draft.WeaponLevel = GetIntOrNull(equipment, 2);
            draft.Armor = GetIntOrNull(equipment, 3);
            draft.ArmorLevel = GetIntOrNull(equipment, 4);
            draft.Assist = GetIntOrNull(equipment, 5);
        }

        if (FindLastSamePersonCommand(sceneStatusCommands, 0x52, personId) is { } job)
        {
            draft.HasJobCommand = true;
            draft.JobId = GetIntOrNull(job, 1);
        }

        foreach (var ability in draft.Abilities)
        {
            ability.DataDefaultValue = dataDefaults?.GetAbility(ability.AbilityId);
            if (FindLastAbilityCommand(sceneStatusCommands, personId, ability.AbilityId) is not { } command) continue;
            ability.HasCommand = true;
            ability.Operation = GetIntOrNull(command, 2);
            ability.Value = GetIntOrNull(command, 3);
        }

        draft.CommandPreview = BuildPreview(draft);
        return draft;
    }

    public BattlefieldUnitStatusWriteResult Save(
        CczProject project,
        ScenarioFileInfo scenario,
        SceneStringDocument dictionary,
        LegacyScenarioDocument document,
        BattlefieldUnitStatusDraft draft)
    {
        if (!ScenarioFileReader.IsBattlefieldScriptFile(scenario.FileName))
        {
            throw new InvalidOperationException("Battlefield unit status write only supports RS\\S_XX.eex.");
        }

        var locator = BuildWritableLocator(draft.TargetKey);
        var target = FindCommand(document, locator)
            ?? throw new InvalidOperationException("The bound 46/47 deployment command was not found in the current S script tree.");
        var definition = BattlefieldDeploymentRecordDefinition.FromCommandId(target.CommandId)
            ?? throw new InvalidOperationException("Battlefield unit status write only supports 46/47 deployment commands.");
        ValidateRecordRange(target, definition, locator.RecordIndex);
        var start = locator.RecordIndex * definition.GroupSize;
        var personId = GetInt(target, start + definition.PersonIndex);

        var applied = Apply(project, document, draft);
        var write = _writer.Save(
            project,
            Path.Combine("RS", scenario.FileName),
            document,
            dictionary,
            $"Battlefield unit status write from current tree 46/47 + 48/52/38 person={personId}");

        var verify = _reader.Read(scenario.Path, dictionary);
        ValidateReread(verify, locator, draft, personId);

        return new BattlefieldUnitStatusWriteResult
        {
            FilePath = write.FilePath,
            BackupPath = write.BackupPath,
            ReportJsonPath = write.ReportJsonPath,
            ChangedBytes = write.ChangedBytes,
            UpdatedCommandCount = applied.UpdatedCommandCount,
            InsertedCommandCount = applied.InsertedCommandCount,
            ValidationSummary = write.ValidationSummary + $"; unit status reread OK: person={personId}",
            Changes = applied.Changes
        };
    }

    public BattlefieldUnitStatusWriteResult Apply(
        CczProject project,
        LegacyScenarioDocument document,
        BattlefieldUnitStatusDraft draft)
    {
        var locator = BuildWritableLocator(draft.TargetKey);
        var target = FindCommand(document, locator)
            ?? throw new InvalidOperationException("The bound 46/47 deployment command was not found in the current S script tree.");
        var definition = BattlefieldDeploymentRecordDefinition.FromCommandId(target.CommandId)
            ?? throw new InvalidOperationException("Battlefield unit status write only supports 46/47 deployment commands.");
        var itemBoundary = ItemCategoryBoundaryService.Resolve(project);
        ValidateDraftRanges(draft, itemBoundary);
        ValidateRecordRange(target, definition, locator.RecordIndex);

        var start = locator.RecordIndex * definition.GroupSize;
        var personId = GetInt(target, start + definition.PersonIndex);
        if (draft.PersonId != 0 && draft.PersonId != personId)
        {
            throw new InvalidOperationException($"The bound deployment record changed: dialog person={draft.PersonId}, script person={personId}. Refresh and retry.");
        }

        var changes = new List<string>();
        var updatedCommandCount = 0;
        var insertedCommandCount = 0;
        var writesEquipment = HasAnyEquipmentValue(draft);
        var writesRuntimeStatus = HasAnyRuntimeStatusValue(draft);
        var equipmentBlockTitle = GetEquipmentStatusBlockTitle(target.CommandId);
        var runtimeBlockTitle = GetRuntimeStatusBlockTitle(target.CommandId);

        SetDeploymentSlotIfChanged(target, start + definition.LevelIndex, draft.LevelBonus, "Level bonus", changes);
        SetDeploymentSlotIfChanged(target, start + definition.JobLevelIndex, draft.JobLevel, "Job level", changes);
        SetDeploymentSlotIfChanged(target, start + definition.AiIndex, draft.AiPolicy, "AI policy", changes);

        if (!TryFindCommandList(document, target, out var commandList, out var targetListIndex))
        {
            throw new InvalidOperationException("Could not locate the command list containing the 46/47 deployment command.");
        }

        var deploymentSegment = FindDeploymentSegment(commandList, targetListIndex);
        var deploymentCommands = GetSegmentLogicalCommands(commandList, deploymentSegment);
        var sceneStatusCommands = GetManagedSceneLogicalCommands(document, target.SceneIndex);
        if (draft.RemoveEquipmentOverride)
        {
            updatedCommandCount += RemoveSamePersonCommands(document, sceneStatusCommands, 0x48, personId, "Remove 0x48 equipment override", changes);
            writesEquipment = false;
        }

        if (writesEquipment)
        {
            var values = new[]
            {
                personId,
                draft.Weapon ?? 0,
                draft.WeaponLevel ?? 0,
                draft.Armor ?? 0,
                draft.ArmorLevel ?? 0,
                draft.Assist ?? 0
            };
            var block = EnsureInternalInfoBlock(
                commandList,
                deploymentSegment,
                deploymentSegment.EndIndex,
                EquipmentStatusBlockTitles,
                equipmentBlockTitle,
                target.SceneIndex,
                target.SectionIndex,
                personId,
                StatusBlockContent.Equipment);
            insertedCommandCount += block.InsertedCommandCount;
            var blockCommands = GetInternalInfoBlockCommands(block.Command);

            if (FindLastSamePersonCommand(sceneStatusCommands, 0x48, personId) is { } existing)
            {
                SetCommandValues(existing, values);
                if (!TryFindCommandList(document, existing, out var existingList, out _) ||
                    !ReferenceEquals(existingList, blockCommands))
                {
                    if (TryFindCommandList(document, existing, out existingList, out _))
                    {
                        MoveCommandIntoInternalInfoBlock(existingList, existing, block.Command);
                    }
                }
                updatedCommandCount++;
                changes.Add($"Update 0x48 equipment: weapon={values[1]} Lv={values[2]} armor={values[3]} Lv={values[4]} assist={values[5]}");
            }
            else
            {
                InsertCommandIntoInternalInfoBlock(block.Command, CreateCommand(0x48, target.SceneIndex, target.SectionIndex, values));
                insertedCommandCount++;
                changes.Add($"Insert 0x48 equipment into 0x02 \"{GetInternalInfoBlockTitle(block.Command)}\": weapon={values[1]} Lv={values[2]} armor={values[3]} Lv={values[4]} assist={values[5]}");
            }
        }

        var drawingSegment = default(StatusCommandSegment);
        var drawingCommands = new List<LegacyScenarioCommandNode>();
        if (writesRuntimeStatus || draft.RemoveJobOverride || draft.RemoveAbilityOverrides.Count > 0)
        {
            drawingSegment = FindDrawingStatusSegment(document, target.SceneIndex, target.SectionIndex);
            drawingCommands = GetSegmentLogicalCommands(drawingSegment.CommandList, drawingSegment.Segment);
            sceneStatusCommands = GetManagedSceneLogicalCommands(document, target.SceneIndex);
        }

        if (draft.RemoveJobOverride)
        {
            updatedCommandCount += RemoveJobOverrides(document, sceneStatusCommands, personId, changes);
        }

        foreach (var abilityId in draft.RemoveAbilityOverrides.Distinct())
        {
            updatedCommandCount += RemoveAbilityOverrides(document, sceneStatusCommands, personId, abilityId, changes);
        }

        InternalInfoBlockResult? runtimeBlock = null;
        List<LegacyScenarioCommandNode>? runtimeBlockCommands = null;
        if (writesRuntimeStatus)
        {
            runtimeBlock = EnsureInternalInfoBlock(
                drawingSegment.CommandList,
                drawingSegment.Segment,
                drawingSegment.Segment.EndIndex,
                RuntimeStatusBlockTitles,
                runtimeBlockTitle,
                target.SceneIndex,
                target.SectionIndex,
                personId,
                StatusBlockContent.Runtime);
            insertedCommandCount += runtimeBlock.Value.InsertedCommandCount;
            runtimeBlockCommands = GetInternalInfoBlockCommands(runtimeBlock.Value.Command);
        }

        if (draft.JobId.HasValue)
        {
            var blockCommand = runtimeBlock?.Command
                ?? throw new InvalidOperationException("Could not create the 0x52/0x38 internal info block.");
            var blockCommands = runtimeBlockCommands
                ?? throw new InvalidOperationException("Could not locate the 0x52/0x38 internal info block command list.");
            var values = new[] { personId, draft.JobId.Value };
            if (FindLastSamePersonCommand(sceneStatusCommands, 0x52, personId) is { } existing)
            {
                SetCommandValues(existing, values);
                if (!TryFindCommandList(document, existing, out var existingList, out _) ||
                    !ReferenceEquals(existingList, blockCommands))
                {
                    if (TryFindCommandList(document, existing, out existingList, out _))
                    {
                        MoveJobCommandGroupIntoInternalInfoBlock(existingList, existing, blockCommand);
                    }
                }
                var insertedToggles = EnsureAbilityRecalcToggles(blockCommands, existing, target.SceneIndex, target.SectionIndex);
                if (insertedToggles > 0)
                {
                    insertedCommandCount += insertedToggles;
                }
                updatedCommandCount++;
                changes.Add(insertedToggles > 0
                    ? $"Update 0x52 job={draft.JobId.Value} and add 4081 recalc toggles in 0x02 \"{GetInternalInfoBlockTitle(blockCommand)}\""
                    : $"Update 0x52 job={draft.JobId.Value} and keep 4081 recalc toggles in 0x02 \"{GetInternalInfoBlockTitle(blockCommand)}\"");
            }
            else
            {
                InsertCommandIntoInternalInfoBlock(blockCommand, CreateVariableOperationCommand(target.SceneIndex, target.SectionIndex, 4081, 1));
                InsertCommandIntoInternalInfoBlock(blockCommand, CreateCommand(0x52, target.SceneIndex, target.SectionIndex, values));
                InsertCommandIntoInternalInfoBlock(blockCommand, CreateVariableOperationCommand(target.SceneIndex, target.SectionIndex, 4081, 0));
                insertedCommandCount += 3;
                changes.Add($"Insert 0x77/0x52/0x77 job change under 0x1C drawing block: job={draft.JobId.Value}");
            }
        }

        foreach (var ability in draft.Abilities.Where(ability => ability.Value.HasValue))
        {
            var blockCommand = runtimeBlock?.Command
                ?? throw new InvalidOperationException("Could not create the 0x52/0x38 internal info block.");
            var blockCommands = runtimeBlockCommands
                ?? throw new InvalidOperationException("Could not locate the 0x52/0x38 internal info block command list.");
            var operation = ability.Operation ?? 0;
            var value = ability.Value!.Value;
            var values = new[] { personId, ability.AbilityId, operation, value };
            if (FindLastAbilityCommand(sceneStatusCommands, personId, ability.AbilityId) is { } existing)
            {
                SetCommandValues(existing, values);
                if (!TryFindCommandList(document, existing, out var existingList, out _) ||
                    !ReferenceEquals(existingList, blockCommands))
                {
                    if (TryFindCommandList(document, existing, out existingList, out _))
                    {
                        MoveCommandIntoInternalInfoBlock(existingList, existing, blockCommand);
                    }
                }
                updatedCommandCount++;
                changes.Add($"Update 0x38 {ability.Name}: {DescribeOperation(operation)} {value}");
            }
            else
            {
                InsertCommandIntoInternalInfoBlock(blockCommand, CreateCommand(0x38, target.SceneIndex, target.SectionIndex, values));
                insertedCommandCount++;
                changes.Add($"Insert 0x38 {ability.Name} under 0x1C drawing block: {DescribeOperation(operation)} {value}");
            }
        }

        if (changes.Count > 0)
        {
            updatedCommandCount += RemoveEmptyStatusBlocks(document, target.SceneIndex, target.SectionIndex, changes);
        }

        if (changes.Count == 0)
        {
            throw new InvalidOperationException("No status fields were changed. Edit at least one deployment, equipment, job, or ability field.");
        }

        return new BattlefieldUnitStatusWriteResult
        {
            UpdatedCommandCount = updatedCommandCount,
            InsertedCommandCount = insertedCommandCount,
            ValidationSummary = $"unit status in-memory apply OK: person={personId}",
            Changes = changes
        };
    }

    public BattlefieldUnitStatusDraft BuildDeltaDraftFromEffectiveValues(
        BattlefieldUnitStatusDraft current,
        BattlefieldUnitDataDefaults dataDefaults,
        ItemCategoryBoundary itemBoundary,
        int? weaponId,
        int? weaponLevel,
        int? armorId,
        int? armorLevel,
        int? assistId,
        int? jobId,
        IReadOnlyDictionary<int, (int Operation, int? Value)> abilities)
    {
        var draft = CloneStatusDraft(current);
        draft.DataDefaults = dataDefaults;
        // This API builds an equipment/job/ability delta from the effective values shown
        // by the console. Deployment fields belong to the placement/deployment delta and
        // must not leak from the full source draft into an otherwise empty status delta.
        // Leaving these populated caused a placement-only commit to invoke Apply with
        // values that had already been written by BattlefieldDeploymentWriteService.
        draft.LevelBonus = null;
        draft.JobLevel = null;
        draft.AiPolicy = null;
        draft.Weapon = null;
        draft.WeaponLevel = null;
        draft.Armor = null;
        draft.ArmorLevel = null;
        draft.Assist = null;
        draft.JobId = null;
        draft.RemoveEquipmentOverride = false;
        draft.RemoveJobOverride = false;
        draft.RemoveAbilityOverrides.Clear();

        var scriptWeapon = BattlefieldUnitDataDefaultService.ToScriptEquipmentCode(weaponId, itemBoundary, BattlefieldEquipmentSlot.Weapon);
        var scriptArmor = BattlefieldUnitDataDefaultService.ToScriptEquipmentCode(armorId, itemBoundary, BattlefieldEquipmentSlot.Armor);
        var scriptAssist = BattlefieldUnitDataDefaultService.ToScriptEquipmentCode(assistId, itemBoundary, BattlefieldEquipmentSlot.Assist);
        var dataWeapon = BattlefieldUnitDataDefaultService.ToScriptEquipmentCode(dataDefaults.WeaponId, itemBoundary, BattlefieldEquipmentSlot.Weapon);
        var dataArmor = BattlefieldUnitDataDefaultService.ToScriptEquipmentCode(dataDefaults.ArmorId, itemBoundary, BattlefieldEquipmentSlot.Armor);
        var dataAssist = BattlefieldUnitDataDefaultService.ToScriptEquipmentCode(dataDefaults.AssistId, itemBoundary, BattlefieldEquipmentSlot.Assist);
        var currentWeapon = ResolveCurrentEquipmentSelection(
            current.HasEquipmentCommand,
            current.Weapon,
            itemBoundary,
            BattlefieldEquipmentSlot.Weapon,
            dataDefaults.WeaponId);
        var currentArmor = ResolveCurrentEquipmentSelection(
            current.HasEquipmentCommand,
            current.Armor,
            itemBoundary,
            BattlefieldEquipmentSlot.Armor,
            dataDefaults.ArmorId);
        var currentAssist = ResolveCurrentEquipmentSelection(
            current.HasEquipmentCommand,
            current.Assist,
            itemBoundary,
            BattlefieldEquipmentSlot.Assist,
            dataDefaults.AssistId);
        var currentWeaponLevel = current.HasEquipmentCommand ? current.WeaponLevel ?? 0 : dataDefaults.WeaponLevel ?? 0;
        var currentArmorLevel = current.HasEquipmentCommand ? current.ArmorLevel ?? 0 : dataDefaults.ArmorLevel ?? 0;
        var requestedWeaponLevel = weaponLevel ?? 0;
        var requestedArmorLevel = armorLevel ?? 0;

        var equipmentDiffers =
            scriptWeapon != dataWeapon ||
            requestedWeaponLevel != (dataDefaults.WeaponLevel ?? 0) ||
            scriptArmor != dataArmor ||
            requestedArmorLevel != (dataDefaults.ArmorLevel ?? 0) ||
            scriptAssist != dataAssist;
        var equipmentChanged =
            !Nullable.Equals(weaponId, currentWeapon) ||
            requestedWeaponLevel != currentWeaponLevel ||
            !Nullable.Equals(armorId, currentArmor) ||
            requestedArmorLevel != currentArmorLevel ||
            !Nullable.Equals(assistId, currentAssist);
        var requestedAllScriptDefault =
            scriptWeapon == 0 &&
            requestedWeaponLevel == 0 &&
            scriptArmor == 0 &&
            requestedArmorLevel == 0 &&
            scriptAssist == 0;
        if (equipmentChanged && current.HasEquipmentCommand && requestedAllScriptDefault)
        {
            draft.RemoveEquipmentOverride = true;
        }
        else if (equipmentChanged && equipmentDiffers)
        {
            draft.Weapon = scriptWeapon == dataWeapon ? 0 : scriptWeapon;
            draft.WeaponLevel = requestedWeaponLevel == (dataDefaults.WeaponLevel ?? 0) ? 0 : requestedWeaponLevel;
            draft.Armor = scriptArmor == dataArmor ? 0 : scriptArmor;
            draft.ArmorLevel = requestedArmorLevel == (dataDefaults.ArmorLevel ?? 0) ? 0 : requestedArmorLevel;
            draft.Assist = scriptAssist == dataAssist ? 0 : scriptAssist;
        }
        else if (equipmentChanged && current.HasEquipmentCommand)
        {
            draft.RemoveEquipmentOverride = true;
        }

        var currentJob = current.HasJobCommand ? current.JobId : dataDefaults.JobId;
        var jobChanged = !Nullable.Equals(jobId, currentJob);
        if (jobChanged && jobId.HasValue && dataDefaults.JobId.HasValue && jobId.Value != dataDefaults.JobId.Value)
        {
            draft.JobId = jobId.Value;
        }
        else if (jobChanged && current.HasJobCommand)
        {
            draft.RemoveJobOverride = true;
        }

        draft.Abilities.Clear();
        foreach (var currentAbility in current.Abilities)
        {
            var ability = new BattlefieldUnitAbilityDraft
            {
                AbilityId = currentAbility.AbilityId,
                Name = currentAbility.Name,
                HasCommand = currentAbility.HasCommand,
                DataDefaultValue = dataDefaults.GetAbility(currentAbility.AbilityId)
            };
            if (abilities.TryGetValue(currentAbility.AbilityId, out var requested) &&
                requested.Value.HasValue)
            {
                var operation = requested.Operation;
                var value = requested.Value.Value;
                var dataDefault = dataDefaults.GetAbility(currentAbility.AbilityId);
                var currentEffectiveOperation = currentAbility.HasCommand ? currentAbility.Operation ?? 0 : 0;
                var currentEffectiveValue = currentAbility.HasCommand && currentAbility.Value.HasValue
                    ? currentAbility.Value.Value
                    : dataDefault;
                if (operation == currentEffectiveOperation &&
                    currentEffectiveValue.HasValue &&
                    value == currentEffectiveValue.Value)
                {
                    draft.Abilities.Add(ability);
                    continue;
                }

                var equalsDefault = operation == 0 && dataDefault.HasValue && value == dataDefault.Value;
                if (equalsDefault)
                {
                    if (currentAbility.HasCommand)
                    {
                        ability.RemoveOverride = true;
                        draft.RemoveAbilityOverrides.Add(currentAbility.AbilityId);
                    }
                }
                else
                {
                    ability.Operation = operation;
                    ability.Value = value;
                }
            }
            else if (currentAbility.HasCommand)
            {
                ability.RemoveOverride = true;
                draft.RemoveAbilityOverrides.Add(currentAbility.AbilityId);
            }

            draft.Abilities.Add(ability);
        }

        draft.CommandPreview = BuildPreview(draft);
        return draft;
    }

    private static int? ResolveCurrentEquipmentSelection(
        bool hasEquipmentCommand,
        int? scriptCode,
        ItemCategoryBoundary itemBoundary,
        BattlefieldEquipmentSlot slot,
        int? dataDefaultItemId)
    {
        if (hasEquipmentCommand)
        {
            if (!scriptCode.HasValue || scriptCode.Value == 0)
            {
                return null;
            }

            return BattlefieldUnitDataDefaultService.FromScriptEquipmentCode(scriptCode, itemBoundary, slot, dataDefaultItemId);
        }

        return BattlefieldUnitDataDefaultService.NormalizeDataEquipmentId(dataDefaultItemId);
    }

    public BattlefieldUnitStatusWriteResult Save(
        CczProject project,
        ScenarioFileInfo scenario,
        SceneStringDocument dictionary,
        BattlefieldUnitStatusDraft draft)
    {
        if (!ScenarioFileReader.IsBattlefieldScriptFile(scenario.FileName))
        {
            throw new InvalidOperationException("战场单位状态写回只支持 RS\\S_XX.eex。");
        }

        var document = _reader.Read(scenario.Path, dictionary);
        return Save(project, scenario, dictionary, document, draft);
    }

    public static string BuildPreview(BattlefieldUnitStatusDraft draft)
    {
        var parts = new List<string>
        {
            $"{(draft.CommandId == 0x46 ? "0x46 友军出场设定" : "0x47 敌军出场设定")} Record={draft.RecordIndex}",
            $"人物={draft.PersonId} {draft.PersonName}",
            $"等级加成={FormatKeep(draft.LevelBonus)}  兵种级={FormatJobLevel(draft.JobLevel)}  AI={FormatAi(draft.AiPolicy)}"
        };

        parts.Add($"装备 0x48：武器编码={FormatKeep(draft.Weapon)} Lv={FormatKeep(draft.WeaponLevel)} / 防具编码={FormatKeep(draft.Armor)} Lv={FormatKeep(draft.ArmorLevel)} / 辅助编码={FormatKeep(draft.Assist)}");
        if (!string.IsNullOrWhiteSpace(draft.EquipmentBoundarySummary))
        {
            parts.Add($"装备边界：{draft.EquipmentBoundarySummary}；0=默认，1=卸去，2+=分类内相对编号+2，写回时不转换为物品ID。");
        }
        parts.Add($"兵种 0x52：{FormatKeep(draft.JobId)}（脚本运行命令；新插入到同一 Section 的 0x1C 绘图下方，并包裹 4081=1/0）");
        foreach (var ability in draft.Abilities)
        {
            parts.Add($"五维 0x38：{ability.Name} {DescribeOperation(ability.Operation ?? 0)} {FormatKeep(ability.Value)}");
        }
        parts.Add("说明：0x52/0x38 是脚本运行指令，不是 46/47 出场记录或 Data.e5 人物表字段；保存时会放到同一 Section 的 0x1C 绘图下方，保存复读只能证明脚本字节写入正确。");

        return string.Join(Environment.NewLine, parts);
    }

    public IReadOnlyList<BattlefieldUnitStatusLookupItem> BuildPersonItems(CczProject project, IReadOnlyList<HexTableDefinition> tables)
        => BuildNameItems(project, tables, _engineProfileService.Detect(project).TableHints.PersonTable, fallbackPrefix: "人物", maxFallback: 1024);

    public IReadOnlyList<BattlefieldUnitStatusLookupItem> BuildJobItems(CczProject project, IReadOnlyList<HexTableDefinition> tables)
        => BuildNameItems(project, tables, _engineProfileService.Detect(project).TableHints.DetailedJobTable, fallbackPrefix: "兵种", maxFallback: 80);

    public IReadOnlyList<BattlefieldUnitStatusLookupItem> BuildItemItems(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        int start,
        int count,
        string categoryName,
        string defaultText,
        string unequipText)
    {
        var itemNames = BuildItemNameMap(project, tables);
        var classifications = new ItemClassificationService().BuildLookup(project, tables);
        var result = new List<BattlefieldUnitStatusLookupItem>
        {
            new() { Value = 0, Text = $"0：{defaultText}" },
            new() { Value = 1, Text = $"1：{unequipText}" }
        };

        for (var relative = 0; relative < Math.Max(0, count); relative++)
        {
            var itemId = start + relative;
            var name = itemNames.TryGetValue(itemId, out var itemName) && !string.IsNullOrWhiteSpace(itemName)
                ? itemName
                : $"物品{itemId}";
            var displayCategory = BuildBattlefieldItemCategoryLabel(categoryName, itemId, classifications);
            result.Add(new BattlefieldUnitStatusLookupItem
            {
                Value = relative + 2,
                Text = $"{relative + 2} -> ID{itemId} {name} [{displayCategory}]"
            });
        }

        return result;
    }

    private static string BuildBattlefieldItemCategoryLabel(
        string fallbackCategoryName,
        int itemId,
        IReadOnlyDictionary<int, ItemClassification> classifications)
    {
        if (!classifications.TryGetValue(itemId, out var classification))
        {
            return fallbackCategoryName;
        }

        if (classification.Kind == ItemKind.Consumable)
        {
            return "道具/消耗品-不可装备";
        }

        if (classification.Kind == ItemKind.AccessoryEquipment)
        {
            return "辅助装备";
        }

        if (classification.Kind == ItemKind.Reserved)
        {
            return "预留/空位";
        }

        if (classification.Kind == ItemKind.Unknown)
        {
            return fallbackCategoryName + "-未知";
        }

        return classification.DisplayName;
    }

    public static void ValidateDraftRanges(BattlefieldUnitStatusDraft draft, ItemCategoryBoundary itemBoundary)
    {
        ValidateRange(draft.LevelBonus, -99, 99, "等级加成");
        ValidateRange(draft.JobLevel, 0, 2, "兵种级");
        ValidateRange(draft.AiPolicy, 0, 6, "AI方针");
        ValidateEquipmentCode(draft.Weapon, itemBoundary.WeaponCount, "武器编码");
        ValidateEquipmentCode(draft.Armor, itemBoundary.DefenseCount, "防具编码");
        ValidateEquipmentCode(draft.Assist, itemBoundary.AccessoryCount, "辅助编码");
        ValidateRange(draft.WeaponLevel, 0, 16, "武器等级");
        ValidateRange(draft.ArmorLevel, 0, 16, "防具等级");
        ValidateRange(draft.JobId, 0, 79, "兵种");
        foreach (var ability in draft.Abilities)
        {
            ValidateRange(ability.Operation, 0, 2, ability.Name + "操作");
            ValidateRange(ability.Value, short.MinValue, ushort.MaxValue, ability.Name);
        }
    }

    private static void ValidateEquipmentCode(int? value, int itemCount, string label)
    {
        if (!value.HasValue) return;
        var max = itemCount + 1;
        if (value.Value < 0 || value.Value > max)
        {
            throw new InvalidDataException($"{label}={value.Value} 超出当前项目装备分段范围 0..{max}。0=默认，1=卸去，2+=分类内相对编号+2。");
        }
    }

    private static void ValidateRange(int? value, int min, int max, string label)
    {
        if (!value.HasValue) return;
        if (value.Value < min || value.Value > max)
        {
            throw new InvalidDataException($"{label}={value.Value} 超出有效范围 {min}..{max}。");
        }
    }

    private static void ValidateReread(
        LegacyScenarioDocument document,
        ScriptCommandLocator locator,
        BattlefieldUnitStatusDraft draft,
        int personId)
    {
        var target = FindCommand(document, locator)
            ?? throw new InvalidDataException("战场单位状态复读失败：找不到原 46/47 命令。");
        var definition = BattlefieldDeploymentRecordDefinition.FromCommandId(target.CommandId)
            ?? throw new InvalidDataException("战场单位状态复读失败：原命令不再是 46/47。");
        ValidateRecordRange(target, definition, locator.RecordIndex);
        var start = locator.RecordIndex * definition.GroupSize;

        AssertOptional(target, start + definition.LevelIndex, draft.LevelBonus, "等级加成");
        AssertOptional(target, start + definition.JobLevelIndex, draft.JobLevel, "兵种级");
        AssertOptional(target, start + definition.AiIndex, draft.AiPolicy, "AI方针");

        if (!TryFindCommandList(document, target, out var commandList, out var targetListIndex))
        {
            throw new InvalidDataException("战场单位状态复读失败：找不到原 46/47 所在命令列表。");
        }

        var sceneStatusCommands = GetManagedSceneLogicalCommands(document, target.SceneIndex);
        if (draft.RemoveEquipmentOverride &&
            FindLastSamePersonCommand(sceneStatusCommands, 0x48, personId) != null)
        {
            throw new InvalidDataException("战场单位状态复读失败：0x48 装备覆盖仍然存在。");
        }

        var hasRuntimeStatusValue = HasAnyRuntimeStatusValue(draft);
        if (hasRuntimeStatusValue || draft.RemoveJobOverride || draft.RemoveAbilityOverrides.Count > 0)
        {
            sceneStatusCommands = GetManagedSceneLogicalCommands(document, target.SceneIndex);
        }

        if (draft.RemoveJobOverride &&
            FindLastSamePersonCommand(sceneStatusCommands, 0x52, personId) != null)
        {
            throw new InvalidDataException("战场单位状态复读失败：0x52 兵种覆盖仍然存在。");
        }

        foreach (var abilityId in draft.RemoveAbilityOverrides)
        {
            if (FindLastAbilityCommand(sceneStatusCommands, personId, abilityId) != null)
            {
                var name = AbilityNames.TryGetValue(abilityId, out var abilityName)
                    ? abilityName
                    : abilityId.ToString(CultureInfo.InvariantCulture);
                throw new InvalidDataException($"战场单位状态复读失败：0x38 {name} 覆盖仍然存在。");
            }
        }

        if (HasAnyEquipmentValue(draft))
        {
            var equipment = FindLastSamePersonCommand(sceneStatusCommands, 0x48, personId)
                ?? throw new InvalidDataException("战场单位状态复读失败：找不到 0x48 装备设定。");
            AssertValue(equipment, 1, draft.Weapon ?? 0, "武器");
            AssertValue(equipment, 2, draft.WeaponLevel ?? 0, "武器等级");
            AssertValue(equipment, 3, draft.Armor ?? 0, "防具");
            AssertValue(equipment, 4, draft.ArmorLevel ?? 0, "防具等级");
            AssertValue(equipment, 5, draft.Assist ?? 0, "辅助");
        }

        if (draft.JobId.HasValue)
        {
            var job = FindLastSamePersonCommand(sceneStatusCommands, 0x52, personId)
                ?? throw new InvalidDataException("战场单位状态复读失败：找不到 0x52 兵种改变。");
            AssertValue(job, 1, draft.JobId.Value, "兵种");
            if (!TryFindCommandList(document, job, out var jobList, out _))
            {
                throw new InvalidDataException("战场单位状态复读失败：找不到 0x52 所在命令列表。");
            }

            AssertAbilityRecalcToggles(jobList, job);
        }

        foreach (var ability in draft.Abilities.Where(ability => ability.Value.HasValue))
        {
            var command = FindLastAbilityCommand(sceneStatusCommands, personId, ability.AbilityId)
                ?? throw new InvalidDataException($"战场单位状态复读失败：找不到 0x38 {ability.Name}。");
            AssertValue(command, 2, ability.Operation ?? 0, ability.Name + "操作");
            AssertValue(command, 3, ability.Value!.Value, ability.Name);
        }
    }

    private static void AssertAbilityRecalcToggles(
        IReadOnlyList<LegacyScenarioCommandNode> segmentCommands,
        LegacyScenarioCommandNode jobCommand)
    {
        var index = FindCommandIndex(segmentCommands, jobCommand);
        if (index <= 0 || index >= segmentCommands.Count - 1)
        {
            throw new InvalidDataException("战场单位状态复读失败：0x52 兵种改变缺少前后 4081 能力重算开关。");
        }

        if (!IsAbilityRecalcToggle(segmentCommands[index - 1], expectedValue: 1))
        {
            throw new InvalidDataException("战场单位状态复读失败：0x52 兵种改变前缺少 0x77 整型变量 4081 = 1。");
        }

        if (!IsAbilityRecalcToggle(segmentCommands[index + 1], expectedValue: 0))
        {
            throw new InvalidDataException("战场单位状态复读失败：0x52 兵种改变后缺少 0x77 整型变量 4081 = 0。");
        }
    }

    private static int FindCommandIndex(IReadOnlyList<LegacyScenarioCommandNode> commands, LegacyScenarioCommandNode target)
    {
        for (var i = 0; i < commands.Count; i++)
        {
            if (ReferenceEquals(commands[i], target)) return i;
        }

        return -1;
    }

    private static bool HasAnyEquipmentValue(BattlefieldUnitStatusDraft draft)
        => draft.Weapon.HasValue ||
           draft.WeaponLevel.HasValue ||
           draft.Armor.HasValue ||
           draft.ArmorLevel.HasValue ||
           draft.Assist.HasValue;

    private static bool HasAnyRuntimeStatusValue(BattlefieldUnitStatusDraft draft)
        => draft.JobId.HasValue ||
           draft.Abilities.Any(ability => ability.Value.HasValue);

    private static BattlefieldUnitStatusDraft CloneStatusDraft(BattlefieldUnitStatusDraft source)
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
            DataDefaults = source.DataDefaults,
            RemoveEquipmentOverride = source.RemoveEquipmentOverride,
            RemoveJobOverride = source.RemoveJobOverride,
            EquipmentBoundarySummary = source.EquipmentBoundarySummary,
            SourceSummary = source.SourceSummary,
            CommandPreview = source.CommandPreview
        };
        clone.RemoveAbilityOverrides.AddRange(source.RemoveAbilityOverrides);
        clone.Abilities.Clear();
        foreach (var ability in source.Abilities)
        {
            clone.Abilities.Add(new BattlefieldUnitAbilityDraft
            {
                AbilityId = ability.AbilityId,
                Name = ability.Name,
                Operation = ability.Operation,
                Value = ability.Value,
                HasCommand = ability.HasCommand,
                DataDefaultValue = ability.DataDefaultValue,
                RemoveOverride = ability.RemoveOverride
            });
        }

        return clone;
    }

    private static void SetDeploymentSlot(
        LegacyScenarioCommandNode command,
        int parameterIndex,
        int? value,
        string label,
        List<string> changes)
    {
        if (!value.HasValue) return;
        SetParameterValue(command, parameterIndex, value.Value);
        changes.Add($"更新 {command.CommandIdHex} {label}={value.Value}");
    }

    private static void SetDeploymentSlotIfChanged(
        LegacyScenarioCommandNode command,
        int parameterIndex,
        int? value,
        string label,
        List<string> changes)
    {
        if (!value.HasValue) return;
        if (parameterIndex < 0 || parameterIndex >= command.Parameters.Count)
        {
            throw new InvalidDataException($"{command.CommandIdHex} {command.CommandName} 缺少参数槽 {parameterIndex}。");
        }

        if (command.Parameters[parameterIndex].IntValue == value.Value)
        {
            return;
        }

        SetParameterValue(command, parameterIndex, value.Value);
        changes.Add($"更新 {command.CommandIdHex} {label}={value.Value}");
    }

    private static int RemoveSamePersonCommands(
        LegacyScenarioDocument document,
        IEnumerable<LegacyScenarioCommandNode> commands,
        int commandId,
        int personId,
        string label,
        List<string> changes)
    {
        var removed = 0;
        foreach (var command in commands
                     .Where(command => command.CommandId == commandId &&
                                       command.Parameters.Count > 0 &&
                                       command.Parameters[0].IntValue == personId)
                     .ToList())
        {
            if (!TryFindCommandList(document, command, out var list, out _))
            {
                continue;
            }

            if (list.Remove(command))
            {
                removed++;
            }
        }

        if (removed > 0)
        {
            changes.Add($"{label}: person={personId}, count={removed}");
        }

        return removed;
    }

    private static int RemoveJobOverrides(
        LegacyScenarioDocument document,
        IEnumerable<LegacyScenarioCommandNode> commands,
        int personId,
        List<string> changes)
    {
        var removed = 0;
        foreach (var command in commands
                     .Where(command => command.CommandId == 0x52 &&
                                       command.Parameters.Count > 0 &&
                                       command.Parameters[0].IntValue == personId)
                     .ToList())
        {
            if (!TryFindCommandList(document, command, out var list, out var index))
            {
                continue;
            }

            var removeIndexes = new SortedSet<int> { index };
            if (index > 0 && IsAbilityRecalcToggle(list[index - 1], expectedValue: 1))
            {
                removeIndexes.Add(index - 1);
            }

            if (index < list.Count - 1 && IsAbilityRecalcToggle(list[index + 1], expectedValue: 0))
            {
                removeIndexes.Add(index + 1);
            }

            foreach (var removeIndex in removeIndexes.Reverse())
            {
                list.RemoveAt(removeIndex);
                removed++;
            }
        }

        if (removed > 0)
        {
            changes.Add($"移除 0x52 兵种覆盖和 4081 重算包裹：person={personId}, count={removed}");
        }

        return removed;
    }

    private static int RemoveAbilityOverrides(
        LegacyScenarioDocument document,
        IEnumerable<LegacyScenarioCommandNode> commands,
        int personId,
        int abilityId,
        List<string> changes)
    {
        var removed = 0;
        foreach (var command in commands
                     .Where(command => command.CommandId == 0x38 &&
                                       command.Parameters.Count > 1 &&
                                       command.Parameters[0].IntValue == personId &&
                                       command.Parameters[1].IntValue == abilityId)
                     .ToList())
        {
            if (!TryFindCommandList(document, command, out var list, out _))
            {
                continue;
            }

            if (list.Remove(command))
            {
                removed++;
            }
        }

        if (removed > 0)
        {
            var name = AbilityNames.TryGetValue(abilityId, out var abilityName) ? abilityName : abilityId.ToString(CultureInfo.InvariantCulture);
            changes.Add($"移除 0x38 {name} 覆盖：person={personId}, count={removed}");
        }

        return removed;
    }

    private static int RemoveEmptyStatusBlocks(
        LegacyScenarioDocument document,
        int sceneIndex,
        int sectionIndex,
        List<string> changes)
    {
        var removed = 0;
        foreach (var section in document.Scenes
                     .Where(scene => scene.SceneIndex == sceneIndex)
                     .SelectMany(scene => scene.Sections)
                     .Where(section => section.SceneIndex == sceneIndex && section.SectionIndex == sectionIndex))
        {
            removed += RemoveEmptyStatusBlocksFromList(section.Commands);
        }

        if (removed > 0)
        {
            changes.Add($"移除空状态子块：count={removed}");
        }

        return removed;
    }

    private static int RemoveEmptyStatusBlocksFromList(List<LegacyScenarioCommandNode> commands)
    {
        var removed = 0;
        for (var index = commands.Count - 1; index >= 0; index--)
        {
            var command = commands[index];
            if (command.ChildBlock != null)
            {
                removed += RemoveEmptyStatusBlocksFromList(command.ChildBlock.Commands);
            }

            if (!IsStatusInternalInfoBlock(command, includeEquipment: true) ||
                !InternalInfoBlockIsEmpty(command))
            {
                continue;
            }

            commands.RemoveAt(index);
            removed++;
            if (index > 0 && commands[index - 1].CommandId == 0x01)
            {
                commands.RemoveAt(index - 1);
                removed++;
            }
        }

        return removed;
    }

    private static bool InternalInfoBlockIsEmpty(LegacyScenarioCommandNode blockCommand)
    {
        if (blockCommand.ChildBlock == null) return true;
        var logicalCommands = new List<LegacyScenarioCommandNode>();
        AddLogicalCommands(blockCommand, logicalCommands);
        return logicalCommands.Count == 0;
    }

    private static void SetCommandValues(LegacyScenarioCommandNode command, IReadOnlyList<int> values)
    {
        for (var i = 0; i < values.Count; i++)
        {
            SetParameterValue(command, i, values[i]);
        }
    }

    private static void SetParameterValue(LegacyScenarioCommandNode command, int parameterIndex, int value)
    {
        if (parameterIndex < 0 || parameterIndex >= command.Parameters.Count)
        {
            throw new InvalidDataException($"{command.CommandIdHex} {command.CommandName} 缺少参数槽 {parameterIndex}。");
        }

        var parameter = command.Parameters[parameterIndex];
        if (parameter.Kind == LegacyScenarioParameterKind.Word16 && (value < short.MinValue || value > ushort.MaxValue))
        {
            throw new InvalidDataException($"{command.CommandIdHex} 参数 {parameterIndex} 的 16 位值越界：{value}。");
        }

        if (parameter.Kind is not (LegacyScenarioParameterKind.Word16 or LegacyScenarioParameterKind.Dword32))
        {
            throw new InvalidDataException($"{command.CommandIdHex} 参数 {parameterIndex} 不是数值参数，不能写入 {value}。");
        }

        parameter.IntValue = value;
    }

    private static LegacyScenarioCommandNode CreateCommand(
        int commandId,
        int sceneIndex,
        int sectionIndex,
        IReadOnlyList<int> values,
        string? commandName = null)
    {
        var command = new LegacyScenarioCommandNode
        {
            SceneIndex = sceneIndex,
            SectionIndex = sectionIndex,
            CommandId = commandId,
            CommandName = commandName ?? HexDisplayFormatter.Format(commandId, 2),
            FileOffset = 0
        };

        var instructions = ScenarioStructureProbeReader.LegacyCommandInstructionTable[commandId];
        var valueIndex = 0;
        for (var index = 0; index < 13; index++)
        {
            var layoutCode = instructions[index];
            if (layoutCode == -1) break;

            var kind = layoutCode switch
            {
                0x05 => LegacyScenarioParameterKind.Text,
                0x35 => LegacyScenarioParameterKind.VariableArray,
                0x04 => LegacyScenarioParameterKind.Dword32,
                _ => LegacyScenarioParameterKind.Word16
            };
            var parameter = new LegacyScenarioCommandParameter
            {
                Index = command.Parameters.Count,
                LayoutCode = layoutCode,
                Tag = layoutCode,
                FileOffset = 0,
                Kind = kind,
                ByteLength = kind switch
                {
                    LegacyScenarioParameterKind.Dword32 => 4,
                    LegacyScenarioParameterKind.Text => 1,
                    LegacyScenarioParameterKind.VariableArray => 2,
                    _ => 2
                }
            };

            if (kind is LegacyScenarioParameterKind.Word16 or LegacyScenarioParameterKind.Dword32)
            {
                parameter.IntValue = valueIndex < values.Count ? values[valueIndex++] : 0;
            }

            command.Parameters.Add(parameter);
        }

        return command;
    }

    private static LegacyScenarioCommandNode CreateVariableOperationCommand(int sceneIndex, int sectionIndex, int variableId, int value)
        => CreateCommand(0x77, sceneIndex, sectionIndex, new[] { 2, variableId, 2, 0, value });

    private static int EnsureAbilityRecalcToggles(
        List<LegacyScenarioCommandNode> commandList,
        LegacyScenarioCommandNode jobCommand,
        int sceneIndex,
        int sectionIndex)
    {
        var index = commandList.IndexOf(jobCommand);
        if (index < 0)
        {
            throw new InvalidDataException("未能定位 0x52 兵种改变命令，无法补齐 4081 能力重算开关。");
        }

        var inserted = 0;
        if (index == 0 || !IsAbilityRecalcToggle(commandList[index - 1], expectedValue: 1))
        {
            commandList.Insert(index, CreateVariableOperationCommand(sceneIndex, sectionIndex, 4081, 1));
            inserted++;
            index++;
        }

        if (index >= commandList.Count - 1 || !IsAbilityRecalcToggle(commandList[index + 1], expectedValue: 0))
        {
            commandList.Insert(index + 1, CreateVariableOperationCommand(sceneIndex, sectionIndex, 4081, 0));
            inserted++;
        }

        return inserted;
    }

    private static InternalInfoBlockResult EnsureInternalInfoBlock(
        List<LegacyScenarioCommandNode> commandList,
        DeploymentSegment searchSegment,
        int insertIndex,
        IReadOnlySet<string> acceptedTitles,
        string title,
        int sceneIndex,
        int sectionIndex,
        int personId,
        StatusBlockContent content)
    {
        for (var index = searchSegment.StartIndex; index < Math.Min(searchSegment.EndIndex, commandList.Count); index++)
        {
            var command = commandList[index];
            if (IsExactInternalInfoBlock(command, title) &&
                InternalInfoBlockContainsPersonStatus(command, personId, content))
            {
                var insertedMarkerCount = EnsureSubEventMarkerBefore(commandList, index, sceneIndex, sectionIndex);
                return new InternalInfoBlockResult(command, insertedMarkerCount);
            }
        }

        for (var index = searchSegment.StartIndex; index < Math.Min(searchSegment.EndIndex, commandList.Count); index++)
        {
            var command = commandList[index];
            if (IsExactInternalInfoBlock(command, title))
            {
                var insertedMarkerCount = EnsureSubEventMarkerBefore(commandList, index, sceneIndex, sectionIndex);
                return new InternalInfoBlockResult(command, insertedMarkerCount);
            }
        }

        var marker = CreateStructuralCommand(0x01, sceneIndex, sectionIndex);
        var block = CreateInternalInfoBlock(sceneIndex, sectionIndex, title);
        var safeInsertIndex = Math.Clamp(insertIndex, 0, commandList.Count);
        commandList.Insert(safeInsertIndex, marker);
        commandList.Insert(safeInsertIndex + 1, block);
        return new InternalInfoBlockResult(block, 2);
    }

    private static int EnsureSubEventMarkerBefore(
        List<LegacyScenarioCommandNode> commandList,
        int blockIndex,
        int sceneIndex,
        int sectionIndex)
    {
        if (blockIndex > 0 && commandList[blockIndex - 1].CommandId == 0x01)
        {
            return 0;
        }

        commandList.Insert(blockIndex, CreateStructuralCommand(0x01, sceneIndex, sectionIndex));
        return 1;
    }

    private static LegacyScenarioCommandNode CreateInternalInfoBlock(int sceneIndex, int sectionIndex, string title)
    {
        var command = CreateCommand(0x02, sceneIndex, sectionIndex, Array.Empty<int>(), "内部信息");
        command.OpensSubEventBlock = true;
        if (command.Parameters.Count == 0)
        {
            throw new InvalidDataException("0x02 内部信息命令缺少文本参数布局。");
        }

        var parameter = command.Parameters[0];
        parameter.Kind = LegacyScenarioParameterKind.Text;
        parameter.Text = title;
        parameter.ByteLength = EncodingService.Gbk.GetByteCount(title) + 1;
        command.ChildBlock = new LegacyScenarioCommandBlock
        {
            Kind = "SubEvent"
        };
        command.ChildBlock.Commands.Add(CreateStructuralCommand(0x00, sceneIndex, sectionIndex));
        return command;
    }

    private static LegacyScenarioCommandNode CreateStructuralCommand(int commandId, int sceneIndex, int sectionIndex)
    {
        var commandName = commandId switch
        {
            0x00 => "事件结束",
            0x01 => "子事件设定",
            _ => HexDisplayFormatter.Format(commandId, 2)
        };
        var command = CreateCommand(commandId, sceneIndex, sectionIndex, Array.Empty<int>(), commandName);
        command.IsSubEventMarker = commandId == 0x01;
        command.EndsSubEventBlock = commandId == 0x00;
        return command;
    }

    private static void InsertCommandIntoInternalInfoBlock(
        LegacyScenarioCommandNode blockCommand,
        LegacyScenarioCommandNode command)
    {
        var commands = GetInternalInfoBlockCommands(blockCommand);
        var insertIndex = GetInternalInfoBlockAppendIndex(commands);
        commands.Insert(insertIndex, command);
    }

    private static void MoveCommandIntoInternalInfoBlock(
        List<LegacyScenarioCommandNode> sourceList,
        LegacyScenarioCommandNode command,
        LegacyScenarioCommandNode blockCommand)
    {
        if (ReferenceEquals(sourceList, GetInternalInfoBlockCommands(blockCommand)))
        {
            return;
        }

        if (!sourceList.Remove(command))
        {
            throw new InvalidDataException($"未能迁移 {command.CommandIdHex} 到内部信息子块。");
        }

        InsertCommandIntoInternalInfoBlock(blockCommand, command);
    }

    private static void MoveJobCommandGroupIntoInternalInfoBlock(
        List<LegacyScenarioCommandNode> sourceList,
        LegacyScenarioCommandNode jobCommand,
        LegacyScenarioCommandNode blockCommand)
    {
        var blockCommands = GetInternalInfoBlockCommands(blockCommand);
        if (ReferenceEquals(sourceList, blockCommands))
        {
            return;
        }

        var index = sourceList.IndexOf(jobCommand);
        if (index < 0)
        {
            throw new InvalidDataException("未能迁移 0x52 兵种改变到内部信息子块。");
        }

        var commandsToMove = new List<LegacyScenarioCommandNode>();
        if (index > 0 && IsAbilityRecalcToggle(sourceList[index - 1], expectedValue: 1))
        {
            commandsToMove.Add(sourceList[index - 1]);
        }
        commandsToMove.Add(jobCommand);
        if (index < sourceList.Count - 1 && IsAbilityRecalcToggle(sourceList[index + 1], expectedValue: 0))
        {
            commandsToMove.Add(sourceList[index + 1]);
        }

        foreach (var command in commandsToMove)
        {
            if (!sourceList.Remove(command))
            {
                throw new InvalidDataException($"未能迁移 {command.CommandIdHex} 到内部信息子块。");
            }
        }

        var insertIndex = GetInternalInfoBlockAppendIndex(blockCommands);
        foreach (var command in commandsToMove)
        {
            blockCommands.Insert(insertIndex++, command);
        }
    }

    private static List<LegacyScenarioCommandNode> GetInternalInfoBlockCommands(LegacyScenarioCommandNode blockCommand)
        => blockCommand.ChildBlock?.Commands
           ?? throw new InvalidDataException("内部信息命令缺少可折叠子块。");

    private static int GetInternalInfoBlockAppendIndex(IReadOnlyList<LegacyScenarioCommandNode> commands)
    {
        for (var i = commands.Count - 1; i >= 0; i--)
        {
            if (commands[i].CommandId == 0x00)
            {
                return i;
            }
        }

        return commands.Count;
    }

    private static bool IsAcceptedInternalInfoBlock(
        LegacyScenarioCommandNode command,
        IReadOnlySet<string> acceptedTitles)
        => command.CommandId == 0x02 &&
           command.ChildBlock != null &&
           acceptedTitles.Contains(GetInternalInfoBlockTitle(command));

    private static bool IsExactInternalInfoBlock(
        LegacyScenarioCommandNode command,
        string title)
        => command.CommandId == 0x02 &&
           command.ChildBlock != null &&
           string.Equals(GetInternalInfoBlockTitle(command), title, StringComparison.Ordinal);

    private static bool InternalInfoBlockContainsPersonStatus(
        LegacyScenarioCommandNode blockCommand,
        int personId,
        StatusBlockContent content)
    {
        var logicalCommands = new List<LegacyScenarioCommandNode>();
        AddLogicalCommands(blockCommand, logicalCommands);
        return content switch
        {
            StatusBlockContent.Equipment => logicalCommands.Any(command =>
                command.CommandId == 0x48 &&
                command.Parameters.Count > 0 &&
                command.Parameters[0].IntValue == personId),
            StatusBlockContent.Runtime => logicalCommands.Any(command =>
                command.CommandId is 0x52 or 0x38 &&
                command.Parameters.Count > 0 &&
                command.Parameters[0].IntValue == personId),
            _ => false
        };
    }

    private static string GetInternalInfoBlockTitle(LegacyScenarioCommandNode command)
        => command.TextParameters.FirstOrDefault()?.Text.Trim() ?? string.Empty;

    private static DeploymentSegment FindDeploymentSegment(IReadOnlyList<LegacyScenarioCommandNode> commandList, int sourceIndex)
    {
        var start = sourceIndex;
        while (start > 0 && IsDeploymentCommand(commandList[start - 1].CommandId))
        {
            start--;
        }

        var end = sourceIndex + 1;
        while (end < commandList.Count && IsDeploymentCommand(commandList[end].CommandId))
        {
            end++;
        }

        while (end < commandList.Count && IsStatusSequenceStart(commandList, end, includeEquipment: true))
        {
            end += GetStatusSequenceLength(commandList, end, includeEquipment: true);
        }

        return new DeploymentSegment(start, end);
    }

    private static StatusCommandSegment FindDrawingStatusSegment(
        LegacyScenarioDocument document,
        int sceneIndex,
        int sectionIndex)
        => TryFindDrawingStatusSegment(document, sceneIndex, sectionIndex, out var segment)
            ? segment
            : throw new InvalidOperationException($"当前 Scene {sceneIndex} / Section {sectionIndex} 找不到 0x1C 绘图命令，无法把 0x52/0x38 插入到绘图下方。");

    private static bool TryFindDrawingStatusSegment(
        LegacyScenarioDocument document,
        int sceneIndex,
        int sectionIndex,
        out StatusCommandSegment segment)
    {
        segment = default;
        var drawing = document.EnumerateCommands()
            .Where(command =>
                command.SceneIndex == sceneIndex &&
                command.SectionIndex == sectionIndex &&
                command.CommandId == 0x1C)
            .OrderBy(command => command.CommandIndex)
            .LastOrDefault();
        if (drawing == null)
        {
            return false;
        }

        if (!TryFindCommandList(document, drawing, out var drawingList, out var drawingIndex))
        {
            throw new InvalidDataException("未能定位 0x1C 绘图所在命令列表，无法插入 0x52/0x38。");
        }

        var end = drawingIndex + 1;
        while (end < drawingList.Count && IsStatusSequenceStart(drawingList, end, includeEquipment: false))
        {
            end += GetStatusSequenceLength(drawingList, end, includeEquipment: false);
        }

        segment = new StatusCommandSegment(drawingList, new DeploymentSegment(drawingIndex + 1, end));
        return true;
    }

    private static bool IsDeploymentCommand(int commandId)
        => commandId is 0x46 or 0x47 or 0x4B;

    private static bool IsStatusCommand(LegacyScenarioCommandNode command)
        => command.CommandId is 0x38 or 0x48 or 0x4E or 0x52 ||
           IsAbilityRecalcToggle(command);

    private static bool IsDrawingStatusCommand(LegacyScenarioCommandNode command)
        => command.CommandId is 0x38 or 0x4E or 0x52 ||
           IsAbilityRecalcToggle(command);

    private static bool IsStatusSequenceStart(
        IReadOnlyList<LegacyScenarioCommandNode> commandList,
        int index,
        bool includeEquipment)
    {
        if (index < 0 || index >= commandList.Count)
        {
            return false;
        }

        var command = commandList[index];
        if (includeEquipment ? IsStatusCommand(command) : IsDrawingStatusCommand(command))
        {
            return true;
        }

        if (command.CommandId == 0x01 &&
            index + 1 < commandList.Count &&
            IsStatusInternalInfoBlock(commandList[index + 1], includeEquipment))
        {
            return true;
        }

        return IsStatusInternalInfoBlock(command, includeEquipment);
    }

    private static bool IsStatusInternalInfoBlock(LegacyScenarioCommandNode command, bool includeEquipment)
    {
        if (command.CommandId != 0x02 || command.ChildBlock == null)
        {
            return false;
        }

        var title = GetInternalInfoBlockTitle(command);
        return includeEquipment
            ? AnyStatusBlockTitles.Contains(title)
            : RuntimeStatusBlockTitles.Contains(title);
    }

    private static int GetStatusSequenceLength(
        IReadOnlyList<LegacyScenarioCommandNode> commandList,
        int index,
        bool includeEquipment)
        => index >= 0 &&
           index + 1 < commandList.Count &&
           commandList[index].CommandId == 0x01 &&
           IsStatusInternalInfoBlock(commandList[index + 1], includeEquipment)
            ? 2
            : 1;

    private static bool IsAbilityRecalcToggle(LegacyScenarioCommandNode command)
        => IsAbilityRecalcToggle(command, expectedValue: null);

    private static bool IsAbilityRecalcToggle(LegacyScenarioCommandNode command, int? expectedValue)
        => command.CommandId == 0x77 &&
           command.Parameters.Count >= 5 &&
           command.Parameters[0].IntValue == 2 &&
           command.Parameters[1].IntValue == 4081 &&
           command.Parameters[2].IntValue == 2 &&
           command.Parameters[3].IntValue == 0 &&
           (expectedValue.HasValue
               ? command.Parameters[4].IntValue == expectedValue.Value
               : command.Parameters[4].IntValue is 0 or 1);

    private static List<LegacyScenarioCommandNode> GetSegmentCommands(
        IReadOnlyList<LegacyScenarioCommandNode> commandList,
        DeploymentSegment segment)
        => commandList
            .Skip(segment.StartIndex)
            .Take(Math.Max(0, segment.EndIndex - segment.StartIndex))
            .ToList();

    private static List<LegacyScenarioCommandNode> GetSegmentLogicalCommands(
        IReadOnlyList<LegacyScenarioCommandNode> commandList,
        DeploymentSegment segment)
    {
        var result = new List<LegacyScenarioCommandNode>();
        foreach (var command in GetSegmentCommands(commandList, segment))
        {
            AddLogicalCommands(command, result);
        }

        return result;
    }

    private static List<LegacyScenarioCommandNode> GetManagedSceneLogicalCommands(
        LegacyScenarioDocument document,
        int sceneIndex)
        => document.EnumerateCommands()
            .Where(command => command.SceneIndex == sceneIndex && IsStatusCommand(command))
            .ToList();

    private static void AddLogicalCommands(LegacyScenarioCommandNode command, List<LegacyScenarioCommandNode> result)
    {
        if (command.CommandId != 0x01 && command.CommandId != 0x02 && command.CommandId != 0x00)
        {
            result.Add(command);
        }

        if (command.ChildBlock == null)
        {
            return;
        }

        foreach (var child in command.ChildBlock.Commands)
        {
            AddLogicalCommands(child, result);
        }
    }

    private static bool TryFindCommandList(
        LegacyScenarioDocument document,
        LegacyScenarioCommandNode target,
        out List<LegacyScenarioCommandNode> list,
        out int index)
    {
        foreach (var section in document.Scenes.SelectMany(scene => scene.Sections))
        {
            if (TryFindCommandList(section.Commands, target, out list, out index))
            {
                return true;
            }
        }

        list = null!;
        index = -1;
        return false;
    }

    private static bool TryFindCommandList(
        List<LegacyScenarioCommandNode> commands,
        LegacyScenarioCommandNode target,
        out List<LegacyScenarioCommandNode> list,
        out int index)
    {
        for (var i = 0; i < commands.Count; i++)
        {
            var command = commands[i];
            if (ReferenceEquals(command, target))
            {
                list = commands;
                index = i;
                return true;
            }

            if (command.ChildBlock != null &&
                TryFindCommandList(command.ChildBlock.Commands, target, out list, out index))
            {
                return true;
            }
        }

        list = null!;
        index = -1;
        return false;
    }

    private static LegacyScenarioCommandNode? FindCommand(LegacyScenarioDocument document, ScriptCommandLocator locator)
        => document.EnumerateCommands().FirstOrDefault(command =>
            command.SceneIndex == locator.SceneIndex &&
            command.SectionIndex == locator.SectionIndex &&
            command.CommandIndex == locator.CommandIndex &&
            (string.IsNullOrWhiteSpace(locator.OffsetHex) || HexDisplayFormatter.EqualsText(HexDisplayFormatter.FormatOffset(command.FileOffset), locator.OffsetHex)) &&
            (string.IsNullOrWhiteSpace(locator.CommandIdHex) || string.Equals(command.CommandIdHex, locator.CommandIdHex, StringComparison.OrdinalIgnoreCase)));

    private static LegacyScenarioCommandNode? FindLastSamePersonCommand(
        IEnumerable<LegacyScenarioCommandNode> commands,
        int commandId,
        int personId)
        => commands.LastOrDefault(command =>
            command.CommandId == commandId &&
            command.Parameters.Count > 0 &&
            command.Parameters[0].IntValue == personId);

    private static LegacyScenarioCommandNode? FindLastAbilityCommand(
        IEnumerable<LegacyScenarioCommandNode> commands,
        int personId,
        int abilityId)
        => commands.LastOrDefault(command =>
            command.CommandId == 0x38 &&
            command.Parameters.Count > 3 &&
            command.Parameters[0].IntValue == personId &&
            command.Parameters[1].IntValue == abilityId);

    private static ScriptCommandLocator BuildWritableLocator(BattlefieldPlacedUnit placement)
        => BuildWritableLocator(placement.TargetKey);

    private static ScriptCommandLocator BuildWritableLocator(string targetKey)
    {
        if (!TryParseLocator(targetKey, out var locator))
        {
            throw new InvalidOperationException("该单位没有可写回的 S 剧本 TargetKey。");
        }

        if (locator.SceneIndex != ManagedSceneIndex)
        {
            throw new InvalidOperationException(Scene2PlusStatusWriteDisabledMessage);
        }

        if (!TryParseCommandId(locator.CommandIdHex, out var commandId) || commandId is not (0x46 or 0x47))
        {
            throw new InvalidOperationException("战场单位状态弹窗只对 0x46 友军出场设定和 0x47 敌军出场设定开放写回。");
        }

        if (locator.RecordIndex < 0)
        {
            throw new InvalidOperationException("TargetKey 缺少 Record=N，不能定位 46/47 记录。");
        }

        return locator;
    }

    private static bool TryParseLocator(string targetKey, out ScriptCommandLocator locator)
    {
        locator = default;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in (targetKey ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var index = part.IndexOf('=');
            if (index <= 0) continue;
            values[part[..index].Trim()] = part[(index + 1)..].Trim();
        }

        if (!TryGetInt(values, "Scene", out var scene) ||
            !TryGetInt(values, "Section", out var section) ||
            !TryGetInt(values, "Command", out var command) ||
            !TryGetInt(values, "Record", out var record))
        {
            return false;
        }

        values.TryGetValue("Offset", out var offsetHex);
        values.TryGetValue("Id", out var commandIdHex);
        locator = new ScriptCommandLocator(scene, section, command, offsetHex ?? string.Empty, commandIdHex ?? string.Empty, record);
        return true;
    }

    private static bool TryGetInt(IReadOnlyDictionary<string, string> values, string key, out int value)
    {
        value = 0;
        return values.TryGetValue(key, out var text) &&
               int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseCommandId(string commandIdHex, out int commandId)
    {
        commandId = 0;
        var text = (commandIdHex ?? string.Empty).Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        return int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out commandId);
    }

    private static void ValidateRecordRange(LegacyScenarioCommandNode command, BattlefieldDeploymentRecordDefinition definition, int recordIndex)
    {
        if (recordIndex < 0 || recordIndex >= definition.RecordCount)
        {
            throw new InvalidDataException($"{command.CommandIdHex} Record={recordIndex} 超出有效范围。");
        }

        var start = recordIndex * definition.GroupSize;
        if (start + definition.GroupSize > command.Parameters.Count)
        {
            throw new InvalidDataException($"{command.CommandIdHex} 参数数量不足，无法读取 Record={recordIndex}。");
        }
    }

    private static int GetInt(LegacyScenarioCommandNode command, int index)
    {
        if (index < 0 || index >= command.Parameters.Count)
        {
            throw new InvalidDataException($"{command.CommandIdHex} 缺少参数槽 {index}。");
        }

        return command.Parameters[index].IntValue;
    }

    private static int? GetIntOrNull(LegacyScenarioCommandNode command, int index)
        => index >= 0 && index < command.Parameters.Count ? command.Parameters[index].IntValue : null;

    private static void AssertOptional(LegacyScenarioCommandNode command, int parameterIndex, int? expected, string label)
    {
        if (!expected.HasValue) return;
        AssertValue(command, parameterIndex, expected.Value, label);
    }

    private static void AssertValue(LegacyScenarioCommandNode command, int parameterIndex, int expected, string label)
    {
        var actual = GetInt(command, parameterIndex);
        if (actual != expected)
        {
            throw new InvalidDataException($"战场单位状态复读失败：{command.CommandIdHex} {label} expected={expected}, actual={actual}。");
        }
    }

    private IReadOnlyList<BattlefieldUnitStatusLookupItem> BuildNameItems(
        CczProject project,
        IReadOnlyList<HexTableDefinition> tables,
        string tableName,
        string fallbackPrefix,
        int maxFallback)
    {
        var result = new Dictionary<int, string>();
        try
        {
            if (HexTableNameResolver.TryResolveForProject(project, tables, tableName, out var table))
            {
                var read = new HexTableReader().Read(project, table, tables);
                if (read.Validation.IsUsable && read.Data.Columns.Contains("ID"))
                {
                    var nameColumn = FindNameColumn(read.Data);
                    foreach (DataRow row in read.Data.Rows)
                    {
                        var id = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
                        var name = !string.IsNullOrWhiteSpace(nameColumn) && read.Data.Columns.Contains(nameColumn)
                            ? Convert.ToString(row[nameColumn], CultureInfo.InvariantCulture)?.Trim()
                            : string.Empty;
                        result[id] = string.IsNullOrWhiteSpace(name) ? $"{fallbackPrefix}{id}" : name!;
                    }
                }
            }
        }
        catch
        {
            result.Clear();
        }

        if (result.Count == 0)
        {
            for (var i = 0; i < maxFallback; i++)
            {
                result[i] = $"{fallbackPrefix}{i}";
            }
        }

        return result
            .OrderBy(pair => pair.Key)
            .Select(pair => new BattlefieldUnitStatusLookupItem
            {
                Value = pair.Key,
                Text = $"{pair.Key}：{pair.Value}"
            })
            .ToList();
    }

    private Dictionary<int, string> BuildItemNameMap(CczProject project, IReadOnlyList<HexTableDefinition> tables)
    {
        var result = new Dictionary<int, string>();
        var profile = _engineProfileService.Detect(project);
        foreach (var tableName in new[] { profile.TableHints.ItemLowTable, profile.TableHints.ItemHighTable })
        {
            try
            {
                if (!HexTableNameResolver.TryResolveForProject(project, tables, tableName, out var table)) continue;
                var read = new HexTableReader().Read(project, table, tables);
                if (!read.Validation.IsUsable || !read.Data.Columns.Contains("ID")) continue;
                var nameColumn = FindNameColumn(read.Data);
                foreach (DataRow row in read.Data.Rows)
                {
                    var id = Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture);
                    var name = !string.IsNullOrWhiteSpace(nameColumn) && read.Data.Columns.Contains(nameColumn)
                        ? Convert.ToString(row[nameColumn], CultureInfo.InvariantCulture)?.Trim()
                        : string.Empty;
                    if (!string.IsNullOrWhiteSpace(name)) result[id] = name!;
                }
            }
            catch
            {
                // Keep any names already collected from the other item table.
            }
        }

        return result;
    }

    private static string FindNameColumn(DataTable data)
    {
        foreach (DataColumn column in data.Columns)
        {
            if (column.ColumnName.Contains("名称", StringComparison.Ordinal) ||
                column.ColumnName.Contains("名字", StringComparison.Ordinal) ||
                column.ColumnName.Contains("姓名", StringComparison.Ordinal))
            {
                return column.ColumnName;
            }
        }

        return data.Columns.Count > 1 ? data.Columns[1].ColumnName : string.Empty;
    }

    private static string FormatKeep(int? value)
        => value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "保持原值";

    private static string FormatJobLevel(int? value)
        => value switch
        {
            0 => "0 初级",
            1 => "1 中级",
            2 => "2 高级",
            null => "保持原值",
            _ => value.Value.ToString(CultureInfo.InvariantCulture)
        };

    private static string FormatAi(int? value)
        => value switch
        {
            0 => "0 被动",
            1 => "1 主动",
            2 => "2 坚守",
            3 => "3 攻击",
            4 => "4 到点",
            5 => "5 跟随",
            6 => "6 逃离",
            null => "保持原值",
            _ => value.Value.ToString(CultureInfo.InvariantCulture)
        };

    private static string DescribeOperation(int operation)
        => operation switch
        {
            1 => "+",
            2 => "-",
            _ => "="
        };

    private readonly record struct ScriptCommandLocator(
        int SceneIndex,
        int SectionIndex,
        int CommandIndex,
        string OffsetHex,
        string CommandIdHex,
        int RecordIndex);

    private readonly record struct DeploymentSegment(int StartIndex, int EndIndex);

    private readonly record struct StatusCommandSegment(
        List<LegacyScenarioCommandNode> CommandList,
        DeploymentSegment Segment);

    private readonly record struct InternalInfoBlockResult(
        LegacyScenarioCommandNode Command,
        int InsertedCommandCount);

    private enum StatusBlockContent
    {
        Equipment,
        Runtime
    }

}
