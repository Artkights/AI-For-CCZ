# CMF 派生功能矩阵

## 结论速览

- `老版游戏制作工具` 根目录的 7 个曹操传相关 CMF 升级为高可信旧修改器知识源。
- 当前整合包已增加 CMF 知识提取入口：先抽取签名、分段、疑似页面、地址形态和功能候选；如果能从 CheatMaker 正常导出数据/地址清单，还可以导入字段名、地址、类型和长度。
- CMF 可以直接用于补齐功能覆盖矩阵、特效名/说明来源、EXE 全局参数候选和 6.5/6.6 差异提示。
- 从 CMF 功能候选升级为可写规则时，仍必须补齐字段名、地址类型、宽度、版本匹配、测试副本写入和写后复读。

## 适用版本

- 默认目标仍是曹操传加强版 6.5 未加密基底。
- `Star6.5引擎exe修改器.cmf` 进入 6.5 引擎和全局参数候选。
- `Star6.6X 引擎.cmf` 进入 6.6/6.6X profile 和差异候选，不混入 6.5 默认写入。
- `CheatMaker配套文件_star175EXE额外修改器[6.4版].cmf` 进入 6.4/star175 差异参考。

## 已确认事实

- CMF 是 CheatMaker 生成的成熟修改器工程，不应降级为普通外部传闻。
- 当前 CMF 静态结构可稳定识别：UTF-16LE BOM、`cmf04/cmf05/cmf0a`、UTF-16 CRLF 分段。
- `Star6.5引擎exe修改器.cmf` 有 8 个分段；`Star6.6X 引擎.cmf` 有 15 个分段，6.6X 明显多出功能块。
- 当前实现新增 `CmfKnowledgeExtractor`、`CmfDerivedCapabilityService` 和 MCP 查询工具；`import_cmf_export_knowledge` 用于读取 CheatMaker 正常导出的文本/CSV/TSV 字段清单。

## 实现/使用方法

| CMF | 派生功能方向 | 当前接入模块 | 当前状态 |
|---|---|---|---|
| `Star6.5引擎exe修改器.cmf` | 6.5 EXE 修改、全局参数 | `GlobalSettingsService`、未来 `ExePatchCatalogService` | 静态候选 |
| `Star6.6X 引擎.cmf` | 6.6X EXE 修改、6.5/6.6 差异、6.6 全局参数 | `CczEngineProfileService`、`CmfManualSeedService`、`全局设定`、`宝物设定/装备类型` | `Star6.6 / ManualConfirmedWritableCandidate` |
| `CheatMaker配套文件_star175EXE额外修改器[6.4版].cmf` | 6.4/star175 EXE 差异 | 版本差异提示 | 静态候选 |
| `特效CM.cmf` | 特效修改器覆盖范围 | `EffectPackageService domain=cmf` | 静态候选 |
| `修改特效名.cmf` | 特效名修改 | `EffectPackageService domain=cmf`、未来 `EffectNameTableService` | 静态候选 |
| `剧本特效介绍（读取imsg）.cmf` | Imsg 特效说明读取 | 未来 `EffectDescriptionImsgService` | 静态候选 |
| `剧本特效名字（读取引擎）.cmf` | 引擎内特效名读取 | 未来 `EffectNameTableService` | 静态候选 |

新增 MCP 入口：

- `extract_cmf_knowledge(relative_path)`：提取单个 CMF 的结构化项目、分段和功能候选。
- `import_cmf_export_knowledge(relative_path, export_path)`：导入 CheatMaker 正常导出的数据/地址清单，将字段提升为 `ExtractedFromCheatMakerExport` 级别。
- `list_cmf_features(category?, keyword?)`：列出 CMF 派生功能候选。
- `read_cmf_feature(feature_id)`：读取单个功能候选。
- `promote_cmf_feature_candidate(feature_id)`：生成规则草案和验证缺口，不写游戏文件。

## 风险与边界

- CMF 是高可信旧工具源，但静态分段不是字段级写入规则。
- 只有完成 CheatMaker 正常导出或 UI 枚举，拿到字段名、地址、类型、宽度后，才可继续转换为工具规则；当前导入结果仍默认只读，写入前必须经过版本、映射、越界、测试副本和复读验证。
- 运行时内存地址不能伪装成离线文件偏移；必须先分类为 EXE 映像地址、文件偏移、运行时静态内存、动态指针或剧本变量。
- 6.6X 候选不得直接写入 6.5 项目。

## 证据来源

- 本地 CMF：`老版游戏制作工具/*.cmf`。
- 工具实现：`CmfKnowledgeExtractor`、`CmfDerivedCapabilityService`。
- MCP 接口：`extract_cmf_knowledge`、`list_cmf_features`、`read_cmf_feature`、`promote_cmf_feature_candidate`。
- Smoke：`--cmf-knowledge-smoke`。

## 待验证项

- 通过 CheatMaker 正常打开 CMF 后导出地址/数据项，并用 `import_cmf_export_knowledge` 补齐字段级 metadata。
- 对 `Star6.5` 与 `Star6.6X` 做页面/字段级 diff，确认 6.6X 多出的功能块。
- 把确认后的特效名表、Imsg 说明表、EXE 全局参数逐项升级为可读写规则。

## 2026-07-09 Star6.5 人工确认种子

| CMF | 派生功能方向 | 当前接入模块 | 当前状态 |
|---|---|---|---|
| `Star6.5引擎exe修改器.cmf` | 6.5 EXE 修改、全局战斗参数、异常状态、地形策略可用性、装备类型 | `CmfManualSeedService`、`CmfDerivedCapabilityService`、`全局设定`、`宝物设定/装备类型`、MCP/诊断工具 | `全局设定 + 宝物设定/装备类型 / ManualConfirmedWritableCandidate` |

本次新增 `star65-engine-exe-manual-seed.json`，把用户人工提供的 28 个单字段地址、30 项地形策略表、15 项装备类型名称表和 13 项装备类型显示表展开为 `CmfDesignerBinding`。诊断 UI/MCP 中显示为 `ManualConfirmedSeed` / `ManualConfirmed`；正式编辑分为 `全局设定`（全局 CM 字段、地形策略、全局参数、游戏标题）和 `宝物设定 / 装备类型`（装备类型名称、显示/隐藏、可装备部队）。主 UI 只显示名称、数值和勾选状态。

`全局设定 / 装备经验` 当前集中承载装备经验相关字段：8 个新增 1 byte Decimal CM 地址、2 个原有经验开关，以及迁移自旧全局参数页的普装/特装等级上限和普装/特装提升等级。迁移后的 4 个 `EquipmentLevel*` leaf key 仍以 `GlobalSettingsService` 为写入真值，不复制到 CM 地址表。

写入边界：

- `全局设定` 与 `宝物设定 / 装备类型` 只接受白名单 key，不接受任意 offset。
- 保存目标固定为当前项目 `Ekd5.exe`，写入前执行版本兼容、文件存在、越界检查和自动备份。
- 单字段和装备类型显示使用固定 1 byte 覆盖；装备类型名称使用 4 字节 GBK 覆盖并保留第 5 字节分隔/结束值；地形表写入 `0x00..0x0F` 位标志组合；装备类型名称保存裸名称，`data设定` 类型码显示继续保留“普通/特殊”前缀。
- 主 UI 隐藏地址、来源、证据和校验状态；这些信息保留在 CMF 诊断页、MCP 诊断工具和写入报告中。

## 2026-07-09 Star6.6 人工确认种子

| CMF | 派生功能方向 | 当前接入模块 | 当前状态 |
|---|---|---|---|
| `Star6.6X 引擎.cmf` | 6.6 EXE 修改、全局 CM 字段、异常状态、装备经验、装备类型 | `CmfManualSeedService`、`CmSettingsService`、`CczEngineProfileService`、`全局设定`、`宝物设定/装备类型`、MCP/诊断工具 | `Star6.6 / ManualConfirmedWritableCandidate` |

本次新增 `star66-engine-exe-manual-seed.json`，按 `versionScope=Star6.6` 独立承载 25 个单字段地址、15 项装备类型名称表和 13 项装备类型显示表。`CmSettingsService` 根据 `CczEngineProfileService.Detect(project).VersionHint` 选择 6.5 或 6.6 seed；6.6 项目不显示 6.5 的地形策略表，也不加载 6.5 的 HP/MP 合并字段。

6.6 `全局设定` 当前分组为：成长 3 项、装备经验 10 项、战斗公式 3 项、异常状态 9 项。异常能力幅度按裸十六进制显示；异常持续回合按 2-bit 位段解码显示 `0..3`。6.6 `宝物设定 / 装备类型` 继续使用 `0x8AC70` 名称表和 `0x81827` 显示表；可装备部队写入仅在当前项目兵种成长/许可槽解析通过时开放。

冲突处理：

- `0x1F53E` 和 `0x1F56D` 在 6.6 基底中不是本轮宝物质变/飞跃等级，记录为冲突候选，不进入 6.6 seed。
- 6.6 `策略格挡武器获得exp` 固定为 `0x2086C`，不得回退到旧误读地址。
- 本批没有 6.6 地形策略地址，不从 6.5 seed 猜测迁移。

## 详细记录

当前实现先提供“静态候选”能力：能证明 CMF 覆盖了某类功能，并在整合包里显示这些候选。下一阶段的关键任务是获取 CheatMaker 工程的设计态或导出态字段数据。字段数据闭环后，才能生成 `HexTable`、`ExePatchPoint`、`EffectNameTable` 或 `EffectDescriptionImsg` 的正式读写规则。
