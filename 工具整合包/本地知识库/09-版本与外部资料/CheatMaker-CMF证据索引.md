# CheatMaker CMF 证据索引

## 结论速览

- `老版游戏制作工具` 下共有 29 个 `.cmf` 文件，根目录曹操传相关 CMF 作为高可信 CheatMaker 旧修改器知识源读取。
- 根目录 7 个 CMF 与曹操传 Star/特效工具相关；`CheatMaker/Data` 下 22 个 CMF 只作为格式样本。
- CMF 内容多为受保护或混淆工程，不解密、不绕过保护、不从字节流直接推导可写地址或表结构。
- 6.4、6.5、6.6/6.6X CMF 可直接用于建立功能覆盖矩阵和读写候选；最终写入仍必须依赖字段级导出/UI 枚举、HexTable、样本复读、PE 映射或专用资源解析器。

## 适用版本

- 默认适用：曹操传加强版 6.5（未加密）当前项目基底。
- 6.4、6.6、6.6X CMF 只进入版本差异和未来适配，不混入 6.5 默认写入路径。
- `CheatMaker/Data` 里的其它游戏 CMF 不适用于曹操传，仅用于确认 CheatMaker CMF 格式族。

## 已确认事实

- CMF 文件统一表现为 UTF-16LE BOM 后接 CheatMaker 签名，当前样本命中 `cmf04`、`cmf05`、`cmf0a`。
- 总签名统计：`cmf04=24`、`cmf05=1`、`cmf0a=4`。
- 根目录曹操传相关样本数为 7；`CheatMaker/Data` 格式样本数为 22。
- CMF 已接入 CCZModStudio 探测、静态分段分析和 MCP 查询；根目录曹操传相关 CMF 返回中标记为高可信旧工具源，但静态候选不能直接写入。

## 实现/使用方法

| 来源 | 签名 | 长度 | UTF-16 CRLF 段数 | SHA256 | 用途 |
|------|------|------|------------------|--------|------|
| `CheatMaker配套文件_star175EXE额外修改器[6.4版].cmf` | `cmf0a` | 394376 | 17 | `43A5102EAA09C47247E9F98EBA4954077AB26B52A6C168BB99CE3A385684885D` | 6.4/star175 旧工具覆盖范围线索 |
| `Star6.5引擎exe修改器.cmf` | `cmf0a` | 768391 | 8 | `30D6141B45794527660A925E345B490B8AE42E99E3EE4DF2E6F4E8F70F7CABAA` | 6.5 引擎修改器证据样本 |
| `Star6.6X 引擎.cmf` | `cmf0a` | 1145916 | 15 | `EBE2DD8A336EB83654114A913581684C6AA62908F03E63C1E38C58020C13F297` | 6.6X 引擎修改器证据样本 |
| `特效CM.cmf` | `cmf0a` | 12370 | 1 | `CCE11C630E3EDDC9B2476DD6A323D7F32DD408F8623CB6C151695F93EA06EC03` | 特效相关 CMF 线索 |
| `修改特效名.cmf` | `cmf04` | 15346 | 1 | `2B3AB502B15F0B72FF3334E1CEA8E70938A1F81764AE4AB7EB66C7A6868E22F0` | 特效名称旧工具线索 |
| `剧本特效介绍（读取imsg）.cmf` | `cmf04` | 12930 | 1 | `895D226D764F00287C37805E37DB98700AC5B88ECFE426340BEBBA089B9FC003` | Imsg 特效说明读取线索 |
| `剧本特效名字（读取引擎）.cmf` | `cmf04` | 12762 | 1 | `23C4B854EEBEC6777A44FB4E79B99604DED306D17060892C8F4B56F32A43D661` | 引擎内特效名读取线索 |

`CheatMaker/Data` 下 22 个样例只记录为格式样本：其中 `cmf04=21`、`cmf05=1`。这些样例不属于曹操传，不作为版本识别或写入规则依据。

当前工具使用方式：

- `CheatMakerCmfProbe`：识别签名、长度、SHA256、段落数、分段分析、关键词命中和 protected/encoded 风险。
- `CmfKnowledgeExtractor`：把 CMF 静态结构转换为 `CmfProject -> Pages -> Controls -> DataBindings -> AddressEntries -> FeatureCandidates` 候选结构。
- `list_cmf_features` / `extract_cmf_knowledge` / `read_cmf_feature` / `promote_cmf_feature_candidate`：MCP 查询 CMF 派生功能和生成规则草案。
- `list_cmf_evidence`：MCP 返回 CMF 语料统计和每个样本摘要。
- `read_cmf_evidence`：MCP 读取单个 CMF evidence。
- `compare_cmf_evidence`：MCP 比较两个 CMF 的长度、段落数、相同前缀和可比区间相似度。

### 2026-07-09 人工确认字段

- `Star6.5引擎exe修改器.cmf` 新增一批用户人工确认字段，证据等级为 `ManualConfirmed`。
- 地址语义固定为 `Ekd5.exe` 的 `UeFileOffset`；不与 OD VA、运行时地址或资源偏移混写。
- 已落库 28 个单字段地址、1 张 30 项地形策略表、1 张 15 项装备类型名称表和 1 张 13 项装备类型显示表，机器可读种子位于 `../../CCZModStudio/Assets/CmfManualSeeds/star65-engine-exe-manual-seed.json`。
- 知识库明细页：`Star6.5引擎CMF人工地址清单.md`。
- 未显式提供内存值大小的数值框按 `1 byte` 录入；正式写入只通过 `全局设定` 或 `宝物设定 / 装备类型` 白名单 key、固定长度覆盖、备份和复读校验执行。
- 工具侧状态为 `全局设定 + 宝物设定/装备类型 / ManualConfirmedWritableCandidate`：主 UI 只显示名称、数值和勾选状态；地址、来源、证据和校验细节保留在 CMF 诊断页、MCP 诊断工具和写入报告中。
- 装备类型名称按 4 字节 GBK 固定文本处理，槽位步长 5，写入时保留第 5 字节分隔/结束值；装备类型显示按 `0x81827` 起连续 13 个 HexByte 处理。
- `宝物设定 / 装备类型` 的名称列保存裸名称；`宝物设定 / data设定` 的类型码显示仍通过装备类型 profile 生成“普通剑/特殊剑”等带前缀名称。
- 2026-07-09 已用真实 6.5 基底 `基底/重生之氪金桓王传/Ekd5.exe` 复核异常状态：基底 SHA256 为 `03245E8B327813EC1109E9DE240856BED96346B10B56525B4843D2AEE8029C91`；异常能力幅度按裸十六进制显示，异常持续回合按 2-bit 位段 `ShiftedTwoBitDecimal` 解码读写。
- 2026-07-09 追加 8 个装备经验人工确认字段：宝物质变等级、宝物飞跃等级、物理/策略命中与格挡获得经验、防具命中与格挡获得经验；均为 `Ekd5.exe` 的 `UeFileOffset`、1 byte、Decimal，并进入 `全局设定 / 装备经验`。同页同时承载普装/特装等级上限与提升等级，但这些旧全局数字项仍由 `GlobalSettingsService` 写入。
- 2026-07-09 基于真实 6.5 基底复核 `策略格挡武器获得exp`：原人工地址 `0x20877` 读到 `0xF8=248`，实际是 `push [ebp-08]` 指令位移；正确经验立即数为 `0x2086C=0x03`，seed 与 `全局设定 / 装备经验` 已修正。

### 2026-07-09 Star6.6 人工确认字段

- `Star6.6X 引擎.cmf` 新增独立人工确认 seed，证据等级为 `ManualConfirmed + BaselineChecked`。
- 6.6 基底为 `基底\新改曹操傳6.6修正版\Ekd5.exe`，长度 `1130496`，SHA256=`4A4FD8DDBF83E5F0B769D1B97BF8F6E6431C3AB42892024A354228212D3D06A4`。
- 6.6X CMF 主证据为 `老版游戏制作工具\Star6.6X 引擎.cmf`，长度 `1145916`，SHA256=`EBE2DD8A336EB83654114A913581684C6AA62908F03E63C1E38C58020C13F297`；`Star6.6引擎K版.cmf` 只作为同版本补充证据。
- 已落库 25 个单字段地址、1 张 15 项装备类型名称表和 1 张 13 项装备类型显示表，机器可读种子位于 `../../CCZModStudio/Assets/CmfManualSeeds/star66-engine-exe-manual-seed.json`。
- 知识库明细页：`Star6.6引擎CMF人工地址清单.md`。
- 6.6 `全局设定` 根据引擎版本选择 `Star6.6` seed，显示成长 3 项、装备经验 10 项、战斗公式 3 项、异常状态 9 项；本批未提供 6.6 地形策略地址，因此 6.6 不显示或不保存地形策略。
- 6.6 异常能力幅度仍按裸十六进制显示；异常持续回合虽然人工表述为十进制，但基底复核仍是 2-bit 位段编码，UI 显示解码后的 `0..3`。
- 6.6 装备类型名称/显示表复用 `0x8AC70` 与 `0x81827`；正式编辑入口仍在 `宝物设定 / 装备类型`。
- 用户列表中重复出现的 `0x1F53E/0x1F56D` 已记录为 6.6 冲突候选，不进入 6.6 seed，不进入正式 UI。6.6 正式使用 `0x1F50F/0x1F557` 作为宝物质变/飞跃等级。

## 风险与边界

- CMF 不是运行时版本主依据；运行时识别仍优先使用 `Ekd5.exe` 尺寸、SHA、版本资源、核心文件和项目路径。
- `Star6.5引擎exe修改器.cmf`、`Star6.6X 引擎.cmf`、`star175EXE额外修改器[6.4版].cmf` 可提示旧工具覆盖的引擎功能方向，并生成 EXE/全局参数候选；字段级地址、宽度和版本未验证前不能开放 EXE 写入。
- 6.6 项目若解析到 6.5 兜底表，应标记 `CrossVersionFallback`；默认只读/预览，MCP 表写入必须显式 `write_mode=CrossVersionFallbackWrite`。
- 任何从 CMF 升级为可写功能的规则，都必须追加三方闭环：明文导出或旧工具源码证据、本地样本复读、写前/写后 smoke。

## 证据来源

- 本地扫描：`老版游戏制作工具/**/*.cmf`。
- 工具证据：`CCZModStudio.Formats.CheatMakerCmfProbe`、`CmfKnowledgeExtractor`、`CCZModStudio.SmokeTests --cmf-corpus-smoke`、`--cmf-knowledge-smoke`。
- 关联页面：`6X版本差异与适配.md`、`CMF派生功能矩阵.md`、`../90-来源归档/散落资料整合索引.md`。
- 证据等级：CMF 文件存在性、签名、长度、SHA256 和段落数为已验证；静态功能候选为高可信旧工具源候选；字段级写入语义仍需导出/UI 枚举和复读验证。

## 待验证项

- 若能通过 CheatMaker 正常打开并导出明文配置，可把明文配置转为独立待证条目，再逐项做样本复读。
- 对 `特效CM.cmf`、`修改特效名.cmf`、`剧本特效介绍（读取imsg）.cmf`、`剧本特效名字（读取引擎）.cmf` 的页面含义，需要结合旧工具明文配置或源码继续确认。
- 6.4/star175、6.5、6.6X CMF 之间的段落差异已经作为优先排查清单和功能候选来源；解释为新增表或新增地址前必须补字段级导出或 UI 枚举证据。
