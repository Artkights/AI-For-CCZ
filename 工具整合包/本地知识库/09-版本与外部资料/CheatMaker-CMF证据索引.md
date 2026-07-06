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
