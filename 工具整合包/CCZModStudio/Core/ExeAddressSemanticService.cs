using System.Globalization;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using CCZModStudio.Formats;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class ExeAddressSemanticService
{
    private static readonly ConcurrentDictionary<string, X86ScanResult> ScanCache = new(StringComparer.OrdinalIgnoreCase);

    public AddressSemanticRecord Explain(CczProject project, uint address, string targetFile = "Ekd5.exe")
    {
        var targetPath = project.ResolveGameFile(targetFile);
        if (!File.Exists(targetPath)) throw new FileNotFoundException("找不到目标 EXE。", targetPath);
        var executable = ExecutableAnalysisSnapshotCache.Shared.GetBase(targetPath);
        var pe = executable.PeImage;
        var mapper = PeAddressMapper.Load(targetPath);
        var offset = mapper.VirtualAddressToFileOffset(address);
        var section = pe.Sections.FirstOrDefault(item =>
            address >= pe.ImageBase + item.VirtualAddress &&
            address < pe.ImageBase + item.VirtualAddress + Math.Max(item.VirtualSize, item.RawSize))
            ?? throw new InvalidOperationException($"地址 0x{address:X8} 不在任何 PE 节中。");
        var sectionEndOffset = checked((int)Math.Min(pe.Bytes.LongLength, section.RawPointer + section.RawSize));
        var available = Math.Min(32, Math.Max(0, sectionEndOffset - checked((int)offset)));
        if (available <= 0) throw new InvalidOperationException("地址处没有可解码字节。");

        var decoded = new X86InstructionScanner().DecodeBlock(
            pe.Bytes.AsSpan(checked((int)offset), available).ToArray(),
            address,
            section.Name);
        var instruction = decoded.FirstOrDefault() ?? throw new InvalidOperationException("地址处无法解码 x86 指令。");
        var profile = new EnginePatchProfileService().Build(project);
        var known = BuildKnownAddressCatalog(profile);
        known.TryGetValue(address, out var knownInfo);
        var cacheKey = targetPath + "|" + executable.Sha256;
        var scan = ScanCache.GetOrAdd(cacheKey, _ => executable.InstructionScan);
        var xrefs = scan.Instructions
            .Where(item => item.BranchTarget == address)
            .Take(64)
            .Select(item => $"0x{item.Address:X8} {item.Mnemonic}")
            .ToList();

        return new AddressSemanticRecord
        {
            TargetFilePath = targetPath,
            ExeSha256 = executable.Sha256,
            EngineVersion = profile.EngineVersion,
            Address = address,
            Rva = checked(address - pe.ImageBase),
            FileOffset = offset,
            SectionName = section.Name,
            FunctionName = knownInfo?.Name ?? string.Empty,
            TriggerPhase = knownInfo?.Phase ?? string.Empty,
            InstructionText = FormatInstruction(instruction),
            ChineseExplanation = ExplainInstruction(instruction, knownInfo),
            FlowControl = TranslateFlowControl(instruction),
            BranchTarget = instruction.BranchTarget,
            RegistersRead = instruction.RegistersRead.ToList(),
            RegistersWritten = instruction.RegistersWritten.ToList(),
            MemoryReads = instruction.MemoryReads.ToList(),
            MemoryWrites = instruction.MemoryWrites.ToList(),
            CrossReferences = xrefs,
            Evidence =
            {
                $"PE 映射：ImageBase=0x{pe.ImageBase:X8}，RVA=0x{address - pe.ImageBase:X8}，文件偏移=0x{offset:X}",
                knownInfo == null ? "未命中当前 6.5 已知函数目录。" : "命中当前 6.5 已知函数目录：" + knownInfo.Description
            }
        };
    }

    public string BuildGenerationContext(
        CczProject project,
        string? keyword,
        string? phase,
        string? semanticKind,
        string? hookContractId,
        int characterBudget = 12000)
    {
        var inventory = new EffectInventoryService().Scan(project);
        var profile = new EnginePatchProfileService().Build(project);
        var contracts = new HookContractService().BuildContracts(project);
        var selectedContracts = contracts.Where(contract =>
                string.IsNullOrWhiteSpace(hookContractId) || contract.ContractId.Equals(hookContractId, StringComparison.OrdinalIgnoreCase))
            .Where(contract => string.IsNullOrWhiteSpace(phase) || contract.TriggerPhase.Contains(phase, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var effects = inventory.Effects
            .Where(effect => string.IsNullOrWhiteSpace(keyword) ||
                             effect.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                             effect.NaturalLanguageDescription.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .Where(effect => string.IsNullOrWhiteSpace(phase) || effect.TriggerPhase.Contains(phase, StringComparison.OrdinalIgnoreCase))
            .Where(effect => string.IsNullOrWhiteSpace(semanticKind) ||
                             effect.Parameters.Any(parameter => parameter.MeaningKind.Equals(semanticKind, StringComparison.OrdinalIgnoreCase)))
            .Take(20)
            .ToList();
        var moduleCatalog = new EffectModuleCatalogService().Build(project);
        var locationIndex = new EffectIdLocationIndexService().Scan(project, exportReports: false);

        var builder = new System.Text.StringBuilder();
        builder.AppendLine("用途：本地 agent 生成可注入特效草案；CCZModStudio/MCP 负责预览和写入。");
        builder.AppendLine($"目标：{inventory.TargetFilePath}");
        builder.AppendLine($"版本：{inventory.EngineVersion}；SHA256：{inventory.ExeSha256}");
        builder.AppendLine("核心 ABI：ECX=单位指针；依次 push 效果值模式、叠加方式、装备特效号、个人特技号；call 0x004101D9；EAX 返回布尔值或特效值。");
        builder.AppendLine("渠道规则：个人特技号与兵种特效共用 6.X-7 名称空间；装备/宝物特效是独立编号空间；装备号 00 表示不使用宝物渠道，不得据此命名；个人号 FF 可作为真实扩展编号。");
        builder.AppendLine("安全规则：不得截断指令；非 NOP Hook 必须绑定 HookContract 并重放搬迁后的原指令；必须先 preview，apply 只接受 preview 返回的包。");
        builder.AppendLine($"物理位置：已索引 {locationIndex.Locations.Count} 处；直接可改 {locationIndex.CountsByWriteCapability.GetValueOrDefault(EffectIdWriteCapability.DirectWritable)} 处；一个逻辑特效号可以有多个位置，必须按 LocationId 和旧字节锁修改。");
        builder.AppendLine("可调用符号：");
        foreach (var pair in profile.PublicFunctions.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            builder.AppendLine($"- {pair.Key} = {pair.Value}");
        }

        builder.AppendLine("Hook 契约：");
        foreach (var contract in selectedContracts.Take(12))
        {
            builder.AppendLine($"- {contract.ContractId}: {contract.TriggerPhase}，hook={contract.HookAddressHex}，单位={contract.UnitPointerSource}，原指令={contract.OriginalInstructionPolicy}/{contract.OriginalInstructionPlacement}，冲突组={contract.ConflictGroup}");
        }

        builder.AppendLine("可用模块配方：");
        foreach (var recipe in moduleCatalog.Recipes)
        {
            builder.AppendLine($"- {recipe.RecipeId}: {recipe.DisplayNameZh}；{(recipe.IsAvailable ? "可生成" : "待验证")}；{recipe.ReasonZh}");
        }

        builder.AppendLine("当前渠道目录摘要：");
        foreach (var effect in effects.Take(12))
        {
            if (effect.PersonalChannel != null)
            {
                builder.AppendLine($"- 个人/兵种 {effect.PersonalChannel.EffectId:X2}: {effect.PersonalChannel.Name}，已配置={effect.PersonalChannel.IsConfigured}");
            }
            if (effect.ItemChannel?.IsEnabled == true)
            {
                builder.AppendLine($"- 宝物 {effect.ItemChannel.EffectId:X2}: {effect.ItemChannel.Name}，已配置={effect.ItemChannel.IsConfigured}");
            }
        }

        builder.AppendLine("相关特技：");
        foreach (var effect in effects)
        {
            builder.AppendLine($"- {effect.Name}: {effect.NaturalLanguageDescription} 参数：{effect.CurrentParameterSummary}");
        }

        builder.AppendLine("草案字段：HookContractId、HookAddress、ExpectedOldBytesHex、OverwriteLength、OriginalInstructionPolicy、OriginalInstructionPlacement、PreserveFlags、ExpectedStackDelta、RequiredSymbols、AssemblySource/FunctionAssemblySource。");
        var text = builder.ToString();
        var budget = Math.Clamp(characterBudget, 2000, 12000);
        return text.Length <= budget ? text : text[..budget] + "\n[上下文已按预算截断]";
    }

    private static Dictionary<uint, KnownAddressInfo> BuildKnownAddressCatalog(EnginePatchProfile profile)
    {
        var result = new Dictionary<uint, KnownAddressInfo>
        {
            [0x004101D9] = new("core_effect_engine", "特技判定阶段", "装备与个人双渠道特技判定核心；EAX 返回是否拥有或配置的特效值。"),
            [0x0042518F] = new("ability_check_wrapper", "包装判定阶段", "包装型特技判定，预检后进入核心引擎或双渠道回退。"),
            [0x0041301E] = new("dual_channel_check", "特技判定阶段", "检查个人与装备两个渠道。"),
            [0x00413009] = new("get_effect_value", "特效值读取阶段", "从当前单位及配置中读取特效值。"),
            [EngineRuntimeSemanticRegistry.BattlefieldIdToTacticalUnitAddress] = new("battlefield_id_to_tactical_unit", "单位定位阶段", "把战场编号转换为战术单位指针。"),
            [EngineRuntimeSemanticRegistry.DataIdToRuntimeCharacterAddress] = new("data_id_to_runtime_character", "人物定位阶段", "把 16 位 Data号转换为 48H 人物运行时记录。"),
            [EngineRuntimeSemanticRegistry.TacticalUnitToRuntimeCharacterAddress] = new("tactical_unit_to_runtime_character", "人物定位阶段", "读取 unit+00 Data号并取得人物运行时记录。"),
            [EngineRuntimeSemanticRegistry.ItemIdToRecordAddress] = new("item_id_to_record", "道具定位阶段", "按道具号计算 19H 字节运行时道具记录地址。"),
            [EngineRuntimeSemanticRegistry.DetailedJobIdToRecordAddress] = new("detailed_job_id_to_record", "兵种定位阶段", "按详细兵种号计算 23H 字节记录地址。"),
            [EngineRuntimeSemanticRegistry.JobFamilyIdToTerrainRecordAddress] = new("job_family_id_to_terrain_record", "兵种系定位阶段", "按兵种系号计算 3CH 字节地形适应与移动消耗记录地址。"),
            [EngineRuntimeSemanticRegistry.ConsumableItemIdToCountAddress] = new("consumable_item_id_to_count", "道具数量读取阶段", "把 150..254 道具号映射到 00510C80 数量字节并只读返回。"),
            [EngineRuntimeSemanticRegistry.StrategyIdToRecordAddress] = new("strategy_id_to_record", "策略定位阶段", "把策略号转换为 61H 策略记录；不是人物记录。"),
            [EngineRuntimeSemanticRegistry.PhysicalAttackContextAddress] = new("physical_attack_context", "物理战斗上下文", "物理攻击结算上下文数据锚点，不是可直接调用函数。"),
            [EngineRuntimeSemanticRegistry.StrategyContextAddress] = new("strategy_context", "策略上下文", "策略施放与结算上下文；当前只读。"),
            [EngineRuntimeSemanticRegistry.ItemContextAddress] = new("item_context", "道具上下文", "道具使用与结算上下文；当前只读。")
        };

        foreach (var pair in profile.HookPoints)
        {
            if (TryParseHex(pair.Value, out var address)) result.TryAdd(address, new(pair.Key, "补丁触发点", "当前引擎 profile 中的 Hook 候选。"));
        }

        return result;
    }

    private static string FormatInstruction(X86InstructionInfo instruction)
    {
        var operands = instruction.Operands.Select(FormatOperand);
        return $"{instruction.Mnemonic} {string.Join(", ", operands)}".TrimEnd();
    }

    private static string FormatOperand(X86OperandInfo operand)
        => operand.Kind switch
        {
            "Register" => operand.Register,
            "Immediate" when operand.Immediate.HasValue => $"0x{unchecked((uint)operand.Immediate.Value):X}",
            "Branch" when operand.BranchTarget.HasValue => $"0x{operand.BranchTarget.Value:X8}",
            "Memory" => operand.MemoryText,
            _ => operand.Kind
        };

    private static string ExplainInstruction(X86InstructionInfo instruction, KnownAddressInfo? known)
    {
        var operation = instruction.Mnemonic switch
        {
            "mov" => "复制数据，目标操作数会被覆盖",
            "lea" => "计算地址但不读取该地址中的数据",
            "push" => "把一个参数或临时值压入栈顶",
            "pop" => "从栈顶取值",
            "call" => instruction.BranchTarget.HasValue ? $"调用 0x{instruction.BranchTarget.Value:X8}" : "通过寄存器或内存间接调用函数",
            "jmp" => instruction.BranchTarget.HasValue ? $"无条件跳转到 0x{instruction.BranchTarget.Value:X8}" : "通过寄存器或内存间接跳转",
            "ret" => "从当前函数返回",
            "test" => "按位测试操作数并更新条件标志，常用于随后判断是否为零",
            "cmp" => "比较两个值并更新条件标志",
            "add" => "执行加法并更新目标值和条件标志",
            "sub" => "执行减法并更新目标值和条件标志",
            _ when instruction.IsConditionalBranch => "根据上一条比较或测试结果选择是否跳转",
            _ => "执行 x86 指令 " + instruction.Mnemonic
        };
        var context = known == null ? string.Empty : $" 当前地址标记为 {known.Name}：{known.Description}";
        var usage = $"读取寄存器 {JoinOrNone(instruction.RegistersRead)}；写入寄存器 {JoinOrNone(instruction.RegistersWritten)}。";
        return operation + "。" + usage + context;
    }

    private static string TranslateFlowControl(X86InstructionInfo instruction)
        => instruction.FlowControl switch
        {
            "Next" => "顺序执行",
            "Call" => "直接调用",
            "IndirectCall" => "间接调用",
            "UnconditionalBranch" => "无条件跳转",
            "ConditionalBranch" => "条件跳转",
            "Return" => "函数返回",
            "IndirectBranch" => "间接跳转",
            _ => instruction.FlowControl
        };

    private static string JoinOrNone(IEnumerable<string> values)
    {
        var text = string.Join("、", values);
        return string.IsNullOrWhiteSpace(text) ? "无" : text;
    }

    private static bool TryParseHex(string value, out uint address)
    {
        value = value.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = value[2..];
        return uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
    }

    private sealed record KnownAddressInfo(string Name, string Phase, string Description);
}
