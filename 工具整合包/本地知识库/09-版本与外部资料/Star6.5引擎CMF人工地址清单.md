# Star6.5 引擎 CMF 人工地址清单

## 结论速览

- 本页记录 `Star6.5引擎exe修改器.cmf` 中由人工提供并确认的 6.5 引擎 EXE 字段地址。
- 本批数据作为 `ManualConfirmed` 证据源进入工具整合包；已整合进 `全局设定` 的字段可通过正式页面读写，写入仍使用版本护栏、白名单、备份和复读校验。
- 所有地址默认按 `Ekd5.exe` 的 UE 文件偏移处理，不按 OD VA、运行时动态地址或资源偏移处理。
- 未显式提供“内存值大小”的数值框首轮按 `1 byte` 录入；在 `全局设定` 正式写入时仍按白名单固定长度覆盖并生成报告。
- 机器可读种子文件为 `../../CCZModStudio/Assets/CmfManualSeeds/star65-engine-exe-manual-seed.json`。

## 适用版本

- 适用版本：曹操传加强版 6.5（未加密）。
- 来源 CMF：`老版游戏制作工具/Star6.5引擎exe修改器.cmf`。
- 目标文件：`Ekd5.exe`。
- 地址口径：`UeFileOffset`。
- 不适用于 6.6X 正式写入；6.6X 只能在后续补齐人工清单或设计器快照后做版本差异参考。

## 已确认事实

| 项目 | 值 |
|------|----|
| CMF | `Star6.5引擎exe修改器.cmf` |
| 签名 | `cmf0a` |
| 长度 | `768391` |
| SHA256 | `30D6141B45794527660A925E345B490B8AE42E99E3EE4DF2E6F4E8F70F7CABAA` |
| 真实 6.5 基底 | `基底/重生之氪金桓王传/Ekd5.exe` |
| 真实 6.5 基底 SHA256 | `03245E8B327813EC1109E9DE240856BED96346B10B56525B4843D2AEE8029C91` |
| 证据等级 | `ManualConfirmed` |
| 整合状态 | `全局设定 + 宝物设定/装备类型 / ManualConfirmedWritableCandidate` |

本批清单包含 28 个单字段地址、1 张 30 项地形策略表、1 张 15 项装备类型名称表和 1 张 13 项装备类型显示表。`全局设定` 页面承载全局 CM 字段、地形策略、全局参数和游戏标题；装备类型正式编辑入口已迁移到 `宝物设定 / 装备类型`。正式编辑页只显示名称和值或勾选状态，不显示地址、来源或证据细节。

## 实现/使用方法

### 单字段清单

| UE 偏移 | 长度 | 数据类型 | 控件 | 字段 | 备注 |
|---------|-----:|----------|------|------|------|
| `0x3B8E4` | 1 | Decimal | 数值框 | 杀敌加能力，五维上升 1% 需求 | 用户明确提供长度 |
| `0x6678` | 1 | Decimal | 数值框 | 杀敌加能力 HP/MP 上升 1% 需求 | 用户明确提供长度 |
| `0x1F53E` | 1 | Decimal | 数值框 | 宝物质变等级 | 用户明确提供数值大小 |
| `0x1F56D` | 1 | Decimal | 数值框 | 宝物飞跃等级 | 用户明确提供数值大小 |
| `0x207A4` | 1 | Hex | 勾选框 | 攻击武器类施放策略获得经验 | 勾选 `EB`，取消 `74`，默认值记录为 `0` |
| `0x2CD9` | 1 | Hex | 勾选框 | 精神类武器物理攻击获得经验 | 勾选 `EB`，取消 `74`，默认值记录为 `0` |
| `0x2CFA` | 1 | Decimal | 数值框 | 物理命中武器获得exp | 用户明确提供数值大小 |
| `0x2D19` | 1 | Decimal | 数值框 | 物理格挡武器获得exp | 用户明确提供数值大小 |
| `0x2E8E` | 1 | Decimal | 数值框 | 命中防具获得exp | 用户明确提供数值大小 |
| `0x2EFB` | 1 | Decimal | 数值框 | 格挡防具获得exp | 用户明确提供数值大小 |
| `0x20768` | 1 | Decimal | 数值框 | 策略命中武器获得exp | 用户明确提供数值大小 |
| `0x2086C` | 1 | Decimal | 数值框 | 策略格挡武器获得exp | 真实 6.5 基底复核修正；`0x20877` 是 `push [ebp-08]` 的 `F8` 位移字节，不是经验值 |
| `0x8219` | 1 | Decimal | 数值框 | 侧面攻击倍数 | 用户明确提供长度 |
| `0x821D` | 1 | Decimal | 数值框 | 背后攻击倍数 | 用户明确提供长度 |
| `0x8220` | 1 | Decimal | 数值框 | 侧面、背后攻击基数 | 用户明确提供长度 |
| `0x3B136` | 1 | Decimal | 数值框 | 物理伤害基数 | 人工默认长度，待复读确认 |
| `0x120C7` | 1 | Decimal | 数值框 | 浮动伤害 | 人工默认长度，待复读确认 |
| `0x20740` | 1 | Decimal | 数值框 | 引导攻击次数 | 人工默认长度，待复读确认 |
| `0x35879` | 1 | Decimal | 数值框 | 奋战攻击次数 | `255=无限制`；人工默认长度，待复读确认 |
| `0x3EA17` | 1 | Hex | 数值框 | 异常能力幅度：攻击 | UI 用裸十六进制显示，如 `0x12` 显示为 `12` |
| `0x3EA43` | 1 | Hex | 数值框 | 异常能力幅度：防御 | UI 用裸十六进制显示，如 `0x12` 显示为 `12` |
| `0x3EA6F` | 1 | Hex | 数值框 | 异常能力幅度：精神 | UI 用裸十六进制显示，如 `0x12` 显示为 `12` |
| `0x3EA9B` | 1 | Hex | 数值框 | 异常能力幅度：敏捷 | UI 用裸十六进制显示，如 `0x22` 显示为 `22` |
| `0x3EAC7` | 1 | Hex | 数值框 | 异常能力幅度：士气 | UI 用裸十六进制显示，如 `0x22` 显示为 `22` |
| `0x234D1` | 1 | ShiftedTwoBitDecimal | 数值框 | 异常持续回合：中毒 | `mask=0xC0, shift=6`，UI 显示解码后的 `0..3` |
| `0x234CE` | 1 | ShiftedTwoBitDecimal | 数值框 | 异常持续回合：麻痹 | `mask=0x03, shift=0`，UI 显示解码后的 `0..3` |
| `0x234D0` | 1 | ShiftedTwoBitDecimal | 数值框 | 异常持续回合：混乱 | `mask=0x30, shift=4`，UI 显示解码后的 `0..3` |
| `0x234CF` | 1 | ShiftedTwoBitDecimal | 数值框 | 异常持续回合：禁咒 | `mask=0x0C, shift=2`，UI 显示解码后的 `0..3` |

### 异常状态真实基底复核

- 复核依据：`基底/重生之氪金桓王传/Ekd5.exe`，SHA256 为 `03245E8B327813EC1109E9DE240856BED96346B10B56525B4843D2AEE8029C91`。
- 异常能力幅度原始字节：攻击 `0x12`、防御 `0x12`、精神 `0x12`、敏捷 `0x22`、士气 `0x22`；`全局设定` 按创作习惯显示为裸十六进制 `12/12/12/22/22`。
- 异常持续回合邻域字节：`0x234CE..0x234D1 = 02 0C 20 C0`，`0x234D2..0x234D5 = 03 0C 30 C0`。
- 异常持续回合不是普通十进制字节，而是 2-bit 编码值：麻痹 `(raw & 0x03) >> 0`，禁咒 `(raw & 0x0C) >> 2`，混乱 `(raw & 0x30) >> 4`，中毒 `(raw & 0xC0) >> 6`。
- 当前真实基底解码结果：中毒 `3`、麻痹 `2`、混乱 `2`、禁咒 `3`；保存时重新编码为对应 mask 内的原始字节。

### 装备经验真实基底复核

- 复核依据同上：`基底/重生之氪金桓王传/Ekd5.exe`，SHA256 为 `03245E8B327813EC1109E9DE240856BED96346B10B56525B4843D2AEE8029C91`。
- `策略格挡武器获得exp` 原始人工地址 `0x20877` 在基底中读到 `0xF8`，显示为十进制 `248`；邻域字节为 `83 45 F8 03 ... FF 75 F8`。
- 该 `0xF8` 是 x86 指令 `FF 75 F8` 中 `[ebp-08]` 的栈变量位移字节，不是可编辑经验数值。
- 正确经验常量为 `83 45 F8 03` 的立即数 `0x03`，UE 偏移 `0x2086C`；`全局设定 / 装备经验` 已改为读取和写入 `0x2086C`。

### 装备类型名称表

- 表名：装备类型名称。
- 起始 UE 偏移：`0x8AC70`。
- 值类型：`FixedGbkText`。
- 写入长度：每项 `4 byte`，按 GBK 编码。
- 槽位步长：`5 byte`；写入时只覆盖每项前 4 字节，保留 `offset+4` 的原始分隔/结束字节。

| Key | UE 偏移 | 长度 | 名称 |
|-----|---------|-----:|------|
| `equipment-type-name:00` | `0x8AC70` | 4 | 物理武器 1 |
| `equipment-type-name:05` | `0x8AC75` | 4 | 物理武器 2 |
| `equipment-type-name:0A` | `0x8AC7A` | 4 | 物理武器 3 |
| `equipment-type-name:0F` | `0x8AC7F` | 4 | 物理武器 4 |
| `equipment-type-name:14` | `0x8AC84` | 4 | 物理武器 5 |
| `equipment-type-name:19` | `0x8AC89` | 4 | 物理武器 6 |
| `equipment-type-name:1E` | `0x8AC8E` | 4 | 物理武器 7 |
| `equipment-type-name:23` | `0x8AC93` | 4 | 策略武器 1 |
| `equipment-type-name:28` | `0x8AC98` | 4 | 策略武器 2 |
| `equipment-type-name:2D` | `0x8AC9D` | 4 | 双修武器 1 |
| `equipment-type-name:32` | `0x8ACA2` | 4 | 护具 1 |
| `equipment-type-name:37` | `0x8ACA7` | 4 | 护具 2 |
| `equipment-type-name:3C` | `0x8ACAC` | 4 | 护具 3 |
| `equipment-type-name:41` | `0x8ACB1` | 4 | 辅助宝物 |
| `equipment-type-name:46` | `0x8ACB6` | 4 | 消耗品道具 |

### 装备类型显示表

- 表名：装备类型显示。
- 起始 UE 偏移：`0x81827`。
- 值类型：`HexByte`。
- 写入长度：每项 `1 byte`。
- 计算方式：`entryOffset = 0x81827 + index`。

| Key | UE 偏移 | 默认显示值 | 名称 |
|-----|---------|------------|------|
| `equipment-type-display:00` | `0x81827` | `0x00` | 物理武器 1 |
| `equipment-type-display:01` | `0x81828` | `0x03` | 物理武器 2 |
| `equipment-type-display:02` | `0x81829` | `0x06` | 物理武器 3 |
| `equipment-type-display:03` | `0x8182A` | `0x09` | 物理武器 4 |
| `equipment-type-display:04` | `0x8182B` | `0x0C` | 物理武器 5 |
| `equipment-type-display:05` | `0x8182C` | `0x0F` | 物理武器 6 |
| `equipment-type-display:06` | `0x8182D` | `0x12` | 物理武器 7 |
| `equipment-type-display:07` | `0x8182E` | `0x15` | 策略武器 1 |
| `equipment-type-display:08` | `0x8182F` | `0x18` | 策略武器 2 |
| `equipment-type-display:09` | `0x81830` | `0x1B` | 双修武器 1 |
| `equipment-type-display:0A` | `0x81831` | `0x46` | 护具 1 |
| `equipment-type-display:0B` | `0x81832` | `0x49` | 护具 2 |
| `equipment-type-display:0C` | `0x81833` | `0x4C` | 护具 3 |

### 地形可使用策略表

- 表名：地形可使用策略。
- 起始 UE 偏移：`0x1FECC`。
- 计算方式：`entryOffset = 0x1FECC + terrainId`。
- 单项长度：`1 byte`。
- 值类型：位标志组合。
- 位标志：`0x01=火`、`0x02=水`、`0x04=风`、`0x08=地`，例如 `0x03=火+水`。

| TerrainId | UE 偏移 | 地形 |
|-----------|---------|------|
| `0x00` | `0x1FECC` | 平原 |
| `0x01` | `0x1FECD` | 草地 |
| `0x02` | `0x1FECE` | 树林 |
| `0x03` | `0x1FECF` | 荒地 |
| `0x04` | `0x1FED0` | 山地 |
| `0x05` | `0x1FED1` | 岩山 |
| `0x06` | `0x1FED2` | 山崖 |
| `0x07` | `0x1FED3` | 雪原 |
| `0x08` | `0x1FED4` | 桥梁 |
| `0x09` | `0x1FED5` | 浅滩 |
| `0x0A` | `0x1FED6` | 沼泽 |
| `0x0B` | `0x1FED7` | 池塘 |
| `0x0C` | `0x1FED8` | 小河 |
| `0x0D` | `0x1FED9` | 大河 |
| `0x0E` | `0x1FEDA` | 栅栏 |
| `0x0F` | `0x1FEDB` | 城墙 |
| `0x10` | `0x1FEDC` | 城内 |
| `0x11` | `0x1FEDD` | 城门 |
| `0x12` | `0x1FEDE` | 城池 |
| `0x13` | `0x1FEDF` | 关隘 |
| `0x14` | `0x1FEE0` | 鹿砦 |
| `0x15` | `0x1FEE1` | 村庄 |
| `0x16` | `0x1FEE2` | 兵营 |
| `0x17` | `0x1FEE3` | 民居 |
| `0x18` | `0x1FEE4` | 宝物库 |
| `0x19` | `0x1FEE5` | 水池 |
| `0x1A` | `0x1FEE6` | 火 |
| `0x1B` | `0x1FEE7` | 船 |
| `0x1C` | `0x1FEE8` | 祭坛 |
| `0x1D` | `0x1FEE9` | 地下 |

工具整合包读取方式：

- `CmfManualSeedService` 读取内置 seed JSON，校验 SHA、地址、长度、重复偏移、地形表连续性、固定文本表长度和 HexByte 默认值。
- `CmfDerivedCapabilityService` 将人工 seed 转换为 `CmfDesignerSnapshot/CmfDesignerBinding`，并合并到 `ListDesignerFields`。
- `全局设定` 是全局 CM 字段、旧全局参数和游戏标题的正式读写入口；普通字段只显示“名称/数值”，地形策略显示“地形/火/水/风/地”，全局参数只显示“名称/当前值/新值”。
- `全局设定 / 装备经验` 混合显示 4 个旧全局数字项（普装/特装等级上限、普装/特装提升等级）和 10 个 CM 装备经验字段；旧全局数字项底层仍由 `GlobalSettingsService` 写入，CM 字段底层仍由 `CmSettingsService` 写入。
- `宝物设定 / 装备类型` 是装备类型正式读写入口：单表显示“说明/名称/显示/可装备部队”。“名称”列保存 4 字节 GBK 裸名称；`宝物设定 / data设定` 显示类型码时继续按 profile 组合为“普通剑/特殊剑”等创作习惯名称。
- `GlobalSettingsDialog -> CMF候选 -> 字段地址` 继续作为诊断入口显示“来源类型”和“信任等级”，人工字段显示为 `ManualConfirmedSeed` / `ManualConfirmed`。
- MCP 诊断入口：`list_cheatmaker_manual_seed_fields`、`validate_cheatmaker_manual_seed`；正式读写入口：`read_cm_settings`、`preview_write_cm_settings`、`write_cm_settings`。

## 风险与边界

- `全局设定` 与 `宝物设定 / 装备类型` 都只接受白名单 key，不接受任意 offset 写入。
- 写入目标固定为当前项目 `Ekd5.exe`，保存前检查版本兼容、文件存在和偏移边界。
- 装备类型名称最多 4 字节 GBK；超长文本必须拒绝，不能截断后静默写入。
- 装备类型名称写入只覆盖前 4 字节，保留第 5 字节分隔/结束值。
- 6.6X 项目不得把 6.5 seed 当作可写字段加载；只能作为版本差异参考。

## 证据来源

- 用户在 2026-07-09 人工提供的 `Star6.5引擎exe修改器.cmf` 字段、游戏内地址和控件类型说明。
- 本地文件校验：`老版游戏制作工具/Star6.5引擎exe修改器.cmf`，SHA256 为 `30D6141B45794527660A925E345B490B8AE42E99E3EE4DF2E6F4E8F70F7CABAA`。
- 工具实现：`CmfManualSeedService`、`CmfDerivedCapabilityService`、`CmfDesignerWriteVerificationService`。
- Smoke：`--cmf-manual-seed-smoke` 验证 seed 解析、地形表展开、装备类型表展开、字段列表和两个勾选框测试副本写入；`--cm-settings-smoke` 验证 `全局设定` 全局字段正式读写，并覆盖新增装备经验字段；`--item-equipment-type-smoke` 验证装备类型固定 GBK 文本写入、显示隐藏值、可装备部队配对槽写入，以及 `data设定` 保留“普通/特殊”前缀。

## 待验证项

- 对人工默认长度字段逐项复读，确认是否均为 `1 byte`。
- 对 `全局设定` 中所有人工默认长度字段继续收集游戏内效果验证。
- 对装备类型显示表确认 `0x81827..0x81833` 当前值与游戏内分类显示完全一致。
- 后续补充 `Star6.6X 引擎.cmf` 的人工清单或设计器快照后，再做 6.5/6.6X 字段差异报告。
