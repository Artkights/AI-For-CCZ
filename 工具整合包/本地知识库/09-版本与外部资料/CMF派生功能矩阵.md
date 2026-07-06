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
| `Star6.6X 引擎.cmf` | 6.6X EXE 修改、6.5/6.6 差异 | `CczEngineProfileService`、6.6 profile | 静态候选 |
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

## 详细记录

当前实现先提供“静态候选”能力：能证明 CMF 覆盖了某类功能，并在整合包里显示这些候选。下一阶段的关键任务是获取 CheatMaker 工程的设计态或导出态字段数据。字段数据闭环后，才能生成 `HexTable`、`ExePatchPoint`、`EffectNameTable` 或 `EffectDescriptionImsg` 的正式读写规则。
