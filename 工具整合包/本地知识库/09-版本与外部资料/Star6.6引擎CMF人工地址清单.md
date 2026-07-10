# Star6.6 引擎 CMF 人工地址清单

## 结论速览

- 本页记录 `Star6.6/6.6X` 引擎 EXE 修改相关的人工确认地址，作为独立于 6.5 的 `ManualConfirmed + BaselineChecked` 种子。
- 当前正式 seed 文件为 `../../CCZModStudio/Assets/CmfManualSeeds/star66-engine-exe-manual-seed.json`，不复用 `star65-engine-exe-manual-seed.json`。
- 地址语义固定为当前项目 `Ekd5.exe` 的 `UeFileOffset`，不按 OD VA、运行时地址或资源偏移解释。
- `全局设定` 根据项目引擎版本选择 `Star6.5` 或 `Star6.6` seed；6.6 项目只显示本页确认字段，不混入 6.5 地形策略和 6.5 冲突地址。
- `宝物设定 / 装备类型` 在 6.6 下继续使用 `0x8AC70` 名称表和 `0x81827` 显示表；可装备部队写入仍由当前项目兵种成长/许可槽解析结果决定。

## 基底与证据

| 项目 | 路径 | 长度 | SHA256 | 备注 |
|---|---|---:|---|---|
| 6.6 基底 EXE | `基底\新改曹操傳6.6修正版\Ekd5.exe` | `1130496` | `4A4FD8DDBF83E5F0B769D1B97BF8F6E6431C3AB42892024A354228212D3D06A4` | 本轮 6.6 地址复核基底 |
| 6.6X CMF | `老版游戏制作工具\Star6.6X 引擎.cmf` | `1145916` | `EBE2DD8A336EB83654114A913581684C6AA62908F03E63C1E38C58020C13F297` | 正式人工 seed 的 CMF 证据源 |
| 6.6 K CMF | `基底\新改曹操傳6.6修正版\修改器與修改記錄\CheatMaker\Data\PC\Star6.6引擎K版.cmf` | `763592` | `CEBD3DF540218A37962BF293E6D8016AA9C7CA506CC10CD8499193D4064F67EB` | 同版本补充证据，不作为 seed 主来源 |

本页字段来自用户人工提供，并已按上述 6.6 基底复核关键字节。正式写入仍只能走工具白名单 key、版本识别、越界检查、保存前备份、固定长度覆盖、写后复读和 JSON 报告。

## 全局设定字段

| 分组 | Key | 字段 | UE 偏移 | 长度 | 类型 | 备注 |
|---|---|---|---:|---:|---|---|
| 成长 | `kill-ability-five-dim-demand` | 杀敌加能力，五维上升1%需求 | `0x3B8E4` | 1 | DecimalByte | 基底值 `0x1E=30` |
| 成长 | `kill-ability-hp-demand` | 杀敌加能力HP上升1%需求 | `0x6678` | 1 | DecimalByte | 基底值 `0x0F=15` |
| 成长 | `kill-ability-mp-demand` | 杀敌加能力MP上升1%需求 | `0x66A4` | 1 | DecimalByte | 基底值 `0x0F=15` |
| 装备经验 | `treasure-mutation-level` | 宝物质变等级 | `0x1F50F` | 1 | DecimalByte | 基底值 `0x07=7` |
| 装备经验 | `treasure-leap-level` | 宝物飞跃等级 | `0x1F557` | 1 | DecimalByte | 基底值 `0x0A=10` |
| 装备经验 | `weapon-strategy-exp` | 攻击武器类施放策略获得经验 | `0x207A4` | 1 | ToggleByte | 勾选 `EB`，取消 `74` |
| 装备经验 | `spirit-weapon-physical-exp` | 精神类武器物理攻击获得经验 | `0x2CD9` | 1 | ToggleByte | 勾选 `EB`，取消 `74` |
| 装备经验 | `physical-hit-weapon-exp` | 物理命中武器获得exp | `0x2CFA` | 1 | DecimalByte | 基底值 `0x03=3` |
| 装备经验 | `physical-block-weapon-exp` | 物理格挡武器获得exp | `0x2D19` | 1 | DecimalByte | 基底值 `0x01=1` |
| 装备经验 | `hit-armor-exp` | 命中防具获得exp | `0x2E8E` | 1 | DecimalByte | 基底值 `0x04=4` |
| 装备经验 | `block-armor-exp` | 格挡防具获得exp | `0x2EFB` | 1 | DecimalByte | 基底值 `0x03=3` |
| 装备经验 | `strategy-hit-weapon-exp` | 策略命中武器获得exp | `0x20768` | 1 | DecimalByte | 基底值 `0x03=3` |
| 装备经验 | `strategy-block-weapon-exp` | 策略格挡武器获得exp | `0x2086C` | 1 | DecimalByte | 基底值 `0x04=4`；不得回退到 `0x20877` |
| 战斗公式 | `physical-damage-base` | 物理伤害基数 | `0x3B136` | 1 | DecimalByte | 基底值 `0x19=25` |
| 战斗公式 | `guided-attack-count` | 引导攻击次数 | `0x20740` | 1 | DecimalByte | 基底值 `0x02=2` |
| 战斗公式 | `furious-attack-count` | 奋战攻击次数 | `0x35879` | 1 | DecimalByte | 基底值 `0x02=2` |
| 异常状态 | `abnormal-ability-attack` | 异常能力幅度：攻击 | `0x3EA17` | 1 | HexByteBare | 基底值 `0x22`，UI 显示 `22` |
| 异常状态 | `abnormal-ability-defense` | 异常能力幅度：防御 | `0x3EA43` | 1 | HexByteBare | 基底值 `0x22`，UI 显示 `22` |
| 异常状态 | `abnormal-ability-spirit` | 异常能力幅度：精神 | `0x3EA6F` | 1 | HexByteBare | 基底值 `0x22`，UI 显示 `22` |
| 异常状态 | `abnormal-ability-agility` | 异常能力幅度：敏捷 | `0x3EA9B` | 1 | HexByteBare | 基底值 `0x22`，UI 显示 `22` |
| 异常状态 | `abnormal-ability-morale` | 异常能力幅度：士气 | `0x3EAC7` | 1 | HexByteBare | 基底值 `0x22`，UI 显示 `22` |
| 异常状态 | `abnormal-turn-poison` | 异常持续回合：中毒 | `0x234D1` | 1 | ShiftedTwoBitDecimal | `mask=0xC0 shift=6`，基底 `0xC0 -> 3` |
| 异常状态 | `abnormal-turn-paralysis` | 异常持续回合：麻痹 | `0x234CE` | 1 | ShiftedTwoBitDecimal | `mask=0x03 shift=0`，基底 `0x01 -> 1` |
| 异常状态 | `abnormal-turn-seal` | 异常持续回合：禁咒 | `0x234CF` | 1 | ShiftedTwoBitDecimal | `mask=0x0C shift=2`，基底 `0x08 -> 2` |
| 异常状态 | `abnormal-turn-confusion` | 异常持续回合：混乱 | `0x234D0` | 1 | ShiftedTwoBitDecimal | `mask=0x30 shift=4`，基底 `0x10 -> 1` |

6.6 本批未提供地形策略地址，因此 `全局设定` 在 6.6 项目中不显示或不保存 `地形策略`。任何地形策略地址不得从 6.5 seed 猜测迁移。

## 异常持续回合编码

`异常持续回合` 虽然在人工记录中写作“十进制”，但本地 6.6 基底仍是 2-bit 位段编码。UI 显示解码后的 `0..3`，保存时写回 mask 内编码值。

| 字段 | Offset | Raw | Shift | Mask | UI |
|---|---:|---:|---:|---:|---:|
| 麻痹 | `0x234CE` | `0x01` | 0 | `0x03` | 1 |
| 禁咒 | `0x234CF` | `0x08` | 2 | `0x0C` | 2 |
| 混乱 | `0x234D0` | `0x10` | 4 | `0x30` | 1 |
| 中毒 | `0x234D1` | `0xC0` | 6 | `0xC0` | 3 |

读取时必须检查 `raw & ~mask == 0`；若出现 mask 外 bit，本字段不得保存，需先复核地址语义。

## 装备类型表

6.6 继续沿用 6.5 已接入的装备类型表地址：

- `equipment-type-name-table`：`0x8AC70` 起 15 项，4 字节 GBK 名称，槽位步长 5，写入只覆盖前 4 字节并保留第 5 字节。
- `equipment-type-display-table`：`0x81827` 起 13 项，1 byte 显示值；勾选写正常值，取消写 `FF`。

显示表正常值固定为：

| Index | 说明 | 正常值 |
|---:|---|---:|
| `00` | 物理武器 1 | `0x00` |
| `01` | 物理武器 2 | `0x03` |
| `02` | 物理武器 3 | `0x06` |
| `03` | 物理武器 4 | `0x09` |
| `04` | 物理武器 5 | `0x0C` |
| `05` | 物理武器 6 | `0x0F` |
| `06` | 物理武器 7 | `0x12` |
| `07` | 策略武器 1 | `0x15` |
| `08` | 策略武器 2 | `0x18` |
| `09` | 双修武器 1 | `0x1B` |
| `0A` | 护具 1 | `0x46` |
| `0B` | 护具 2 | `0x49` |
| `0C` | 护具 3 | `0x4C` |

`辅助宝物` 和 `消耗品道具` 只有名称槽，本批没有显示表地址。可装备部队写入必须由当前项目的兵种成长/许可槽解析通过后开放，不把 6.5 许可槽规则强套到未解析的 6.6 项目。

## 冲突与拒绝项

| 地址 | 用户原始列表含义 | 6.6 基底复核 | 处理 |
|---:|---|---|---|
| `0x1F53E` | 宝物质变等级 | 基底字节 `0x3C`，处于 `3C 46` 比较指令附近 | 记录为冲突候选，不进入 6.6 seed，不进入正式 UI |
| `0x1F56D` | 宝物飞跃等级 | 基底附近为 `68 59 05 00 00` 一类指令立即数区域，当前字节 `0x05` | 记录为冲突候选，不进入 6.6 seed，不进入正式 UI |

6.6 正式使用 `宝物质变等级=0x1F50F`、`宝物飞跃等级=0x1F557`。

## 工具接入

- `CmfManualSeedService` 同时加载 6.5 和 6.6 seed，并按 `versionScope` 生成 `star65` / `star66` 绑定 ID。
- `CmSettingsService` 通过 `CczEngineProfileService.Detect(project).VersionHint` 选择 `Star6.5` 或 `Star6.6` seed。
- 6.6 `全局设定` 显示 25 个 CM 字段：成长 3、装备经验 10、战斗公式 3、异常状态 9；不显示 6.5 地形策略。
- `宝物设定 / 装备类型` 继续读取名称表和显示表；保存仍只接受白名单 key，不接受任意 offset。
- smoke 覆盖：`--cm-settings-66-smoke`、`--cm-settings-smoke`、`--cmf-manual-seed-smoke`。

## 风险边界

- 本页只覆盖 Star6.6/6.6X 已人工确认并基底复核的字段；6.6 未提供的地址不得从 6.5 复制。
- 6.6 地形策略地址未确认，本轮不开放。
- `地址(HEX)` 默认是 `Ekd5.exe` 文件偏移；如后续发现个别项是 OD VA 或运行时地址，必须另开映射记录并更新 seed。
- 写入测试必须优先使用测试副本；正式写入仍要求备份、固定长度覆盖、复读校验和报告。
