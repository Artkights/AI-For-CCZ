# 本地 Agent 生成可注入特效代码指南

本文档定义本地 agent 与 CCZModStudio 的职责边界：本地 agent 负责推理特效含义并生成结构化补丁草案；CCZModStudio 负责读取、预览、校验、备份、写入和报告；工具整合包本体不内置 AI 生成器。

## 入口与工作流

本地 agent 不应一次性读取全量知识库。推荐顺序：

1. 读取 MCP 资源 `ccz://knowledge/effects/agent-special-effect`，获取紧凑的函数语义、可调用锚点、特效模板和安全约束。
2. 需要更多背景时再按主题读取 `04-函数速查/函数速查表.md`、`01-核心引擎/核心引擎.md`、`03-机制详解/特效值机制.md`、`03-机制详解/6.5特效注入索引.md`。
3. 先调用 `scan_exe_code_caves` 和特效读取/语义扫描，确认目标 EXE、SHA、Hook 原字节、代码洞、回跳地址。
4. 分别调用 `list_effects/read_effect` 查询 `personal`/`job` 与 `item` 编号空间，确认名称、绑定和配置值；不得用一个渠道的同号名称解释另一个渠道。
5. 首先调用 `list_effect_modules` 和 `read_hook_execution_contract`；已验证模块输出 `ModularCompositeEffectBlueprint` 或 `SemanticEffectProgram`。
6. 受约束动作先调用 `compile_semantic_effect`，再由 `preview_modular_effect` 生成锁定包。执行契约未动态验证时只能取得诊断。
7. 简单四参数和任意汇编草案是高级兼容入口，不能绕过执行契约、SHA 和旧字节锁。
8. 只有预览返回的锁定 `EffectPackage` 可以进入对应 apply。

## 核心函数锚点

以下地址只适用于当前 6.5 未加密基线，必须结合语义报告里的 SHA、ImageBase、file offset 和 old-byte 锁复核：

| VA | 名称 | 用途 | 证据要求 |
| --- | --- | --- | --- |
| `004101D9` | core_effect_engine | 装备/个人双渠道特技判定核心 | 生成特技判定桩的主要可调用函数 |
| `0042518F` | ability_check_wrapper | 判定 wrapper，可能转入 `004101D9` 或 fallback | 只作语义参考，除非 Hook 模板明确要求 |
| `0041301E` | dual_channel_check | 双渠道判定 fallback | 只作语义参考 |
| `00413009` | get_effect_value | 特效值读取 | 用于理解 value effect，不直接替代 `004101D9` |
| `004927F0` | battle_data_context | 战斗上下文数据锚点 | 只作为数据读写证据，不作为随意写入点 |
| `004061F9` | unit pointer related | 单位指针相关锚点 | 需要结合函数上下文确认 |

## InlineSpecialSkillPatchDraft 首选格式

简单特技应优先生成 `InlineSpecialSkillPatchDraft`。这个格式让工具自动生成四模块结构：Hook 跳转、判定桩与功能体、个人特技号 patch point、装备特技号 patch point。

重要规则：

- `FunctionAssemblySource` 只写“判定通过后”的功能体，不要重复写 `pushad`、`call 004101D9`、`popad`、`jmp {return}`。
- 工具会自动包裹：
  - `pushad`
  - `mov ecx, UnitPointerSource`
  - `push EffectValueFlag`
  - `push StackFlag`
  - `push ItemEffectId`
  - `push PersonalEffectId`
  - `call 0x004101D9`
  - `test eax, eax`
  - `jz exit`
  - `FunctionAssemblySource`
  - `popad`
  - `jmp {return}`
- `EffectValueFlag=1` 表示只要布尔拥有判定；`EffectValueFlag=0` 表示需要使用核心返回的特效值。
- `StackFlag` 默认 `1`，除非知识库/样本明确要求其他叠加策略。
- `PersonalEffectId` 与 `ItemEffectId` 必须在 `0x00..0xFF`。
- `PersonalEffectId` 与兵种特效共用名称空间；`ItemEffectId` 是独立宝物名称空间。`ItemEffectId=0` 表示禁用宝物渠道，不得据此给特技命名；`PersonalEffectId=255` 允许作为真实扩展编号。
- 生成前读取 `scan_installed_effects` 的统一 `Effects` 和 `read_effect_instance` 渠道证据；若代码语义与目录名称冲突，草案中必须保留冲突说明并要求人工复核绑定。
- `HookAddress`、`OverwriteLength`、`ExpectedOldBytesHex`、`ReturnAddress` 必须来自当前 EXE 的扫描/模板，不能照抄旧样本地址。
- `AllowPreview` 只有在已知 6.5 profile、同一 SHA 的执行契约动态验证通过、槽位完整且旧字节匹配时才允许为 true。静态模板和连续 NOP 不能单独授权写入。

最小示例：

```json
{
  "Prompt": "拥有特技后在已审核伤害 Hook 中增加固定伤害",
  "TargetFile": "Ekd5.exe",
  "EngineVersion": "6.5",
  "EffectId": 128,
  "Mode": "damage-adjust",
  "TemplateId": "conditional-damage-up",
  "HookPoint": "conditional-damage-up",
  "HookAddressHex": "0x00400000",
  "OverwriteLength": 5,
  "ExpectedOldBytesHex": "AA BB CC DD EE",
  "ReturnAddressHex": "0x00400005",
  "PersonalEffectId": 128,
  "ItemEffectId": 0,
  "EffectValueFlag": 1,
  "StackFlag": 1,
  "UnitPointerSource": "dword [ebp-04]",
  "FunctionAssemblySource": "add dword [ebp-10], 10",
  "RequiredCodeCaveBytes": 96,
  "AllowPreview": true
}
```

上例里的地址和旧字节是占位；本地 agent 必须用当前 profile 或 MCP draft/scan 输出替换。

## AssemblyPatchDraft 格式

当特效无法表达为简单四参数特技时，才使用 `AssemblyPatchDraft`。它要求本地 agent 自己写完整的代码洞逻辑。

必填项：

- `TargetFile` 默认 `Ekd5.exe`。
- `HookAddress` 或 `HookAddressHex`。
- `OverwriteLength >= 5`。
- `ExpectedOldBytesHex` 的字节数必须等于 `OverwriteLength`。
- `ReturnAddress` 或 `ReturnAddressHex`，未填时工具按 `HookAddress + OverwriteLength` 处理。
- `AssemblySource` 必须最终跳回 `{return}` 或明确终止流程。
- `RequiredCodeCaveBytes` 应大于编译后代码体长度并留出余量。

汇编约束：

- 可以使用 `{return}`、`{hook}`、`{cave}` 占位符。
- 外部工具优先使用 NASM；没有 NASM 时，内置 tiny assembler 只支持极少数形式，例如 `nop`、`ret`、`db`、直接 `jmp/call`。
- 生成器必须显式保护会破坏原流程的寄存器和 flags。简单特技尽量走 `InlineSpecialSkillPatchDraft`，让工具统一包 `pushad/popad`。
- 不允许把数据表地址、代码洞地址、`rel32` 偏移写死到知识库模板里；必须由 preview 根据当前 EXE 重算。

## MCP 调用范式

### 类型化语义特技（首选）

1. `list_effect_modules(authoring_only=true)`
2. `read_hook_execution_contract(contract_id)`
3. 若契约未开放，调用 `create_effect_contract_probe_plan` 并完成实机采集；不要伪造证据。
4. 生成 `ModularCompositeEffectBlueprint` 或 `SemanticEffectProgram`。
5. `compile_semantic_effect(program)` 检查中文含义、槽位和数值边界。
6. `preview_modular_effect(blueprint)`
7. 预览通过后显式调用 `apply_modular_effect(package)`。

首批动作编号：

- `AddDamageFixed` / `SubtractDamageFixed`
- `AddDamagePercent` / `SubtractDamagePercent`
- `RestoreHpFixed` / `RestoreMpFixed`
- `RestoreHpMaxPercent` / `RestoreMpMaxPercent`

恢复动作只有在物理伤害后恢复契约补齐受益单位、当前/最大值和刷新顺序后才开放。

### 简单四参数特技（高级兼容）

1. `read_effect_resource("ccz://knowledge/effects/agent-special-effect")`
2. `draft_special_skill_patch(...)` 获取当前 profile 的 Hook spec、old bytes、默认草案。
3. 本地 agent 修改 `FunctionAssemblySource`、特效号、模式和说明。
4. `preview_special_skill_patch(draft)`
5. preview 通过后，把返回的 `Package` 传给 `apply_special_skill_patch(package)`。

当前策略伤害静态模板不会直接通过预览，因为 `0043C2B0/0043C2B5` 是连续填充区，尚需动态证明其自然执行路径。

### 自定义汇编补丁

1. `read_effect_resource("ccz://knowledge/effects/agent-special-effect")`
2. `scan_exe_code_caves(...)`
3. 生成 `AssemblyPatchDraft`，必须带 old-byte 锁。
4. `preview_assembly_patch(draft)`
5. preview 通过后，把返回的 `Package` 传给 `apply_assembly_patch(package)`。

### 已有 EffectPackage 补丁段

1. 生成 patch-domain `EffectPackage`。
2. `preview_effect_patch(package)`
3. 预览通过后 `apply_effect_patch(package)`。

## 读取诊断如何参与生成

读取扫描报告中的分类不能直接升级为可注入特效：

- `coreCallUnparsed`：发现 `004101D9` 相关调用但参数切片失败。必须补参数解析或人工复核。
- `hookTargetNoCfg`：发现 Hook，但目标代码洞无法形成可信 CFG。不能自动生成同类写入。
- `partialSignatureOnly`：样本局部相似。只能作为候选命名和人工复核线索。
- `indirectPatchCandidate`：可能是函数指针、间接 call/jmp 或表项改写。默认走自定义汇编/人工复核。
- `complexPatchGrouped`：复杂多 Hook 或共享 helper。不能拆成单个简单四模块特技。

只有 `VerifiedStatic`、`VerifiedDynamic` 或高分 `KnownSample` 可作为 agent 生成草案的正向依据；`ExternalCorroboration` 和 `Hypothesis` 只能进入注释和待验证项。

## 禁止自动化的类型

以下类型不得作为一键简单注入：

- 护卫、大杀四方等复杂多 Hook 补丁。
- 整型变量 `4003`、信息传送 `29` 等引擎扩展补丁。
- 函数指针改写、表项指针改写、间接跳转链。
- 没有 old-byte 锁、没有回跳地址、没有可用代码洞的 Hook。
- 只来自联网资料或旧版本文档、未被当前 EXE 静态/动态证据复核的候选。

## 本地 agent 输出检查清单

提交 preview 前必须检查：

- 已确认 `Ekd5.exe` SHA 和 ImageBase。
- `HookAddress` 能 VA 到 file offset 往返。
- `ExpectedOldBytesHex` 与当前 EXE 原字节一致。
- `OverwriteLength` 不截断半条指令。
- `ReturnAddress` 是覆盖区后一条有效指令，或已明确解释特殊回流。
- 代码洞由 `scan_exe_code_caves` 或 preview allocator 分配。
- 个人/装备特技号不冲突，且参数 slot 后续可 rebind。
- preview 失败时不得 apply；必须把失败原因写入报告。
- 对 `push imm8` 高位编号必须调用 `preview_effect_id_update`，由工具决定原位写入或宽参数适配，不得自行覆盖一个字节。

## 新方案落地分工

CCZModStudio 需要做的事情：

- 保持并增强特效读取、语义扫描、诊断摘要。
- 导出 `agent_special_effect_knowledge.json/.md`。
- 通过 UI 支持导入/粘贴本地 agent 生成的结构化草案并预览。
- 通过 MCP 暴露 `ccz://knowledge/effects/agent-special-effect`、`preview_special_skill_patch`、`apply_special_skill_patch`、`preview_assembly_patch`、`apply_assembly_patch`。
- 所有写入继续走 SHA 锁、old-byte 锁、备份、manifest 和报告。

本地 agent 需要做的事情：

- 使用紧凑知识包和按需知识库文件推导特效代码。
- 输出结构化 `InlineSpecialSkillPatchDraft`、`AssemblyPatchDraft` 或 `EffectPackage`。
- 先 preview，后 apply；不得直接改 EXE。
- 对复杂/低置信候选输出人工复核草案，而不是可写入包。
# 真实复合特效入口

需要把多个现有特效组合成一个真实新编号时，不要生成普通汇编草案，也不要批量修改绑定。请先阅读 [真实复合特效机制](../03-机制详解/真实复合特效机制.md)，并使用：

`scan_installed_effects -> read_engine_effect_mechanism -> find_free_effect_ids -> draft_composite_effect -> preview_composite_effect -> apply_composite_effect`

成员选择优先调用 `search_compatible_effect_members`。返回的兼容类型含义：

- `DirectCoreCall`：直接核心调用。
- `VerifiedWrapper`：包装链已在当前 SHA 上复读，可保留包装语义生成适配器。
- `VerifiedComplexFamily`：复杂补丁家族的必要入口和判定调用已验证。
- `Unsupported`：只能读取，不能强制加入。

需要解释契约时，分别调用 `read_wrapper_contract` 和 `read_complex_effect_family_contract`。已安装复合特效的维护顺序为：

`read_composite_effect -> preview_update_composite_effect / preview_toggle_composite_effect / preview_repair_composite_effect -> 对应 apply`

不得自行放宽 SHA、旧字节和 manifest 锁。修复只用于恢复为安装前字节的已知位置，不能覆盖第三方补丁。

只有预览返回的锁定包可以写入。工具不提供“组合配置”或不占编号的组合预设。
