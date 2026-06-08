# 16 MCP 接入

## 结论速览

- 本页由旧平铺/嵌套知识库迁移到统一知识库框架，保留原有细节并补齐统一阅读口径。
- 证据等级：已验证：本地项目记录，需以样本、旧工具源码或烟测项复核。
- 写入类操作必须以 6.5 未加密基底、本地样本、备份、复读和烟测报告为最终依据。

## 适用版本

- 默认适用：曹操传加强版 6.5（未加密）当前项目基底。
- 涉及 6.6/6.6x 或旧版本时，只作为版本差异、兼容提示或外部佐证，不自动进入 6.5 写入规则。

## 已确认事实

- 本页主题已纳入根目录分层知识库，不再依赖旧平铺编号文件或嵌套 knowledge-base 入口。
- 原文中的地址、偏移、结构、工具行为和烟测记录保留在“详细记录”；实施时优先采用已由本地样本、旧工具源码、复读或烟测确认的结论。
- 外部资料只用于补充能力范围、版本边界和制作习惯，不能替代本地证据。

## 实现/使用方法

- 从根目录 README.md 进入对应专题，再按本页详细记录定位具体工具、文件、地址或流程。
- 修改游戏文件前必须确认目标版本、目标路径、写前备份、写后复读和结构化报告。
- 遇到字段语义、命令参数、资源格式或跨版本能力未完全闭环时，先记录到本页“待验证项”或 `00-总览与规范/待验证清单.md`。

## 风险与边界

- 迁移正文中的历史不确定表述已统一为“待证/待查”等口径；这些内容不得直接作为写入或实现依据。
- 6.6/6.6x 能力不得混入 6.5 默认写入路径；如需适配，必须另建版本护栏和样本验证。
- 本页保留的偏移、表结构和命令写法需要与当前基底文件交叉核对。

## 证据来源

- 本地来源：旧平铺/嵌套知识库迁移内容；具体映射见 `90-来源归档/迁移索引.md`。
- 关联来源：../90-来源归档/迁移索引.md、../09-版本与外部资料/联网深度专题.md。
- 证据等级：已验证：本地项目记录，需以样本、旧工具源码或烟测项复核。

## 待验证项

- 若详细记录中出现“待证”“待查”或跨版本能力，实施前必须补本地样本、旧工具源码、复读结果或实机验证。
- 若本页没有额外未决项，本节仅作为保守护栏保留。

## 详细记录

本页记录 CCZModStudio 接入 MCP 后的结构、工具边界和验证状态。

#### 工程结构

- `CCZModStudio.Runtime`：链接 `CCZModStudio\Core`、`Formats`、`Models` 的服务层源码，供无头调用使用。
- `CCZModStudio.McpServer`：本地 stdio MCP Server，引用 `CCZModStudio.Runtime`，不启动 WinForms UI。
- `CCZModStudio`：原 Windows 桌面程序保留。
- `CCZModStudio.SmokeTests`：原烟测保留。

MCP Server 启动后只通过 stdout 发送 MCP JSON-RPC 消息；普通日志不写 stdout，避免污染 stdio 协议。

#### MCP Tools

| Tool | 用途 | 写入 |
| --- | --- | --- |
| `detect_project` | 识别工作区、游戏目录、HexTable、旧工具资源路径、核心文件状态 | 否 |
| `list_tables` | 列出 HexTable 表定义和字段 | 否 |
| `read_table` | 读取指定数据表，可按行、列、关键字和 limit 过滤 | 否 |
| `write_table_rows` | 按 `row_id` 写入指定列，拒绝 `ID` 和派生列 | 是 |
| `list_scenario_files` | 列出 `RS\R_*.eex / S_*.eex` 剧本文件，可按 R/S 和关键字筛选 | 否 |
| `read_scenario_commands` | 用旧版完整树规则读取单个 R/S eex 的 Scene/Section/Command 摘要和参数 | 否 |
| `search_scenario_scripts` | 在一个或多个 R/S eex 中搜索命令名、命令号、参数和 GBK 文本 | 否 |
| `read_scenario_texts` | 读取 `RS\*.eex` 的 GBK 文本线索 | 否 |
| `write_scenario_texts` | 按文本 `index` 原地写回，必须满足原 GBK 容量 | 是 |
| `list_hexzmap_blocks` | 列出 `Hexzmap.e5` 中按 `Map\Mxxx.jpg` 分辨率/48 解析出的地形块、偏移、尺寸、主地形和可写状态 | 否 |
| `read_hexzmap_block` | 读取单个 `Mxxx` 地形块的地形格矩阵、地形频次统计和坐标边界 | 否 |
| `write_hexzmap_block` | 按 `Mxxx` 和坐标写入 Hexzmap 地形格 | 是 |
| `replace_map_image` | 替换 `Map\Mxxx.jpg` 地图底图 | 是 |
| `preview_resource_replace` | 预览非核心资源整文件替换，返回大小、SHA256、格式检查和风险说明 | 否 |
| `list_image_resources` | 列出已知图片资源目录，覆盖 E5 索引资源和 DLL 图标资源，可按关键字/可预览/可替换筛选 | 否 |
| `list_image_resource_entries` | 列出某个图片资源的条目；E5 图号为 1 基，DLL 图标编号为 0 基 | 否 |
| `export_image_resource_preview` | 将某个图片资源条目渲染为 PNG，写入 `CCZModStudio_Exports/ImagePreviews` | 是（项目侧导出） |
| `list_ccz_image_asset_presets` | 列出 AI 可绘制素材 preset、目标资源、尺寸和安全边界 | 否 |
| `build_ccz_image_prompt` | 根据自然语言描述生成提示词、负面约束和目标编号计划，不联网 | 否 |
| `prepare_ccz_generated_image` | 将已有生成图后处理为 6.5 资源尺寸并调用替换预览 | 是（项目侧导出） |
| `draw_ccz_image_asset` | 调用 Image Studio 兼容上游生成图片、后处理、导出 manifest/raw response 并调用替换预览；`dry_run=true` 只返回计划 | 是（项目侧导出） |
| `list_e5_image_entries` | 读取 `Face.e5`、`Pmapobj.e5`、`Unit_*.e5`、`Hitarea.e5`、`Effarea.e5`、`Logo.e5`、`Mmap.e5`、`U_select.e5`、`Gate.e5`、`Weather.e5`、`Tr.e5` 等 E5 图片资源的 `0x110` 索引表 | 否 |
| `preview_e5_image_replace` | 预览单个 E5 图片条目替换，支持普通图片或备份 E5 内指定图号作为来源 | 否 |
| `replace_e5_image_entry` | 替换单个 E5 图片索引条目，写前备份、写后复读、生成结构化报告 | 是 |
| `preview_e5_image_batch_replace` | 预览多个 E5 图片条目的批量替换，支持普通图片或 E5 内指定图号作为来源 | 否 |
| `replace_e5_image_batch` | 一次写入多个 E5 图片条目，生成单次备份/报告并逐条复读校验 | 是 |
| `preview_dll_icon_replace` | 预览 `Itemicon.dll`、`Mgcicon.dll`、`Cmdicon.dll` 的 RT_BITMAP 图标替换 | 否 |
| `replace_dll_icon` | 替换一个 DLL RT_BITMAP 图标资源，写前备份并生成结构化报告 | 是 |
| `preview_clear_dll_icon` | 预览将一个 DLL RT_BITMAP 图标资源清空为透明占位 | 否 |
| `clear_dll_icon` | 将一个 DLL RT_BITMAP 图标资源按原尺寸写为透明占位 | 是 |
| `list_project_resources` | 列出项目根目录、E5、Map、RS、SV、WAV、SoundTrk 等资源索引，可按分类和关键字筛选 | 否 |
| `run_resource_diagnostics` | 运行资源诊断，定位缺号、重复编号、命名不规则、格式线索异常、地图尺寸不可读等风险，可写项目侧诊断报告 | 否 |
| `replace_resource` | 替换非核心资源文件，禁止直接覆盖核心表文件 | 是 |
| `audit_project` | 运行项目体检，可选择生成体检报告 | 否 |
| `create_test_copy` | 从当前项目创建完整测试副本，供差异对比和隔离验证 | 是 |
| `diff_test_copy` | 对比测试副本与来源项目，可选择生成差异报告 | 否 |
| `create_release_copy` | 从当前项目或测试副本生成干净发布副本，排除测试标记、备份、报告和导出目录 | 是 |
| `list_workflow_guide` | 汇总制作向导、工作台状态和优先行动项，串联体检、资源诊断、差异、备份、备注和证据 | 否 |
| `list_project_evidence` | 列出最近报告、导出表、预览 PNG 和结构化写入报告等项目证据，可按分类/类型/关键字筛选 | 否 |
| `read_project_evidence` | 读取由证据服务扫描到的文本证据；PNG 只返回元数据；支持 `latest_delivery_report` 等 ASCII 别名，避免任意文件读取 | 否 |
| `write_project_delivery_report` | 生成发布前综合 Markdown 报告，写入 `CCZModStudio_Reports` | 是（项目侧报告） |
| `list_scenario_command_templates` | 读取 6.5 `CczString.ini` 字典和内置 R/S eex 命令参数模板，可按关键字、分类和覆盖状态筛选 | 否 |
| `read_scenario_command_template` | 按命令号或名称读取单条 R/S eex 命令参数模板详情 | 否 |
| `list_creator_notes` | 列出项目侧创作者备注，可按范围、关键字和 limit 筛选，并返回目标键导航解析结果 | 否 |
| `upsert_creator_note` | 创建或更新创作者备注，用于记录修改意图、风险、回滚点、待办和实机验证证据 | 是（项目侧） |
| `delete_creator_note` | 按备注 id 删除项目侧创作者备注 | 是（项目侧） |
| `export_creator_notes_csv` | 将筛选后的创作者备注导出为 CSV，便于发布前核对和知识迁移 | 是（项目侧导出） |
| `list_knowledge_entries` | 列出本地知识库 Markdown | 否 |
| `search_knowledge_entries` | 按关键字搜索本地知识库 Markdown，返回文件、行号和上下文片段 | 否 |
| `read_knowledge_entry` | 读取本地知识库 Markdown | 否 |

所有写入 tool 都支持 `write_mode`：

- `direct`：允许写当前检测到的项目目录。
- `test_copy`：要求目标目录存在 `_CCZModStudio_TestCopy.txt`。

#### 安全边界

- `replace_resource` 禁止直接覆盖 `Ekd5.exe`、`Data.e5`、`Imsg.e5`、`Star.e5`、`Hexzmap.e5`；这些文件必须走表格或 Hexzmap 专用写入。
- `replace_e5_image_entry` / `replace_e5_image_batch` 只允许可读取 `0x110` 索引表的 `.e5` 图片载荷文件，且拒绝 `Data.e5`、`Imsg.e5`、`Star.e5`、`Hexzmap.e5` 等核心文件；人物 R/S 指定编号仍必须通过表格写入，不能用 E5 图片替换工具改核心表。`Mark.e5` 当前样本不是标准 `0x110` 索引封包，只定位不替换。
- `replace_dll_icon` / `clear_dll_icon` 只开放 `Itemicon.dll`、`Mgcicon.dll`、`Cmdicon.dll` 的 RT_BITMAP 位图资源写回；不重排资源 ID，不改表字段编号，写入前自动备份。
- `export_image_resource_preview` 只写 `CCZModStudio_Exports/ImagePreviews` 下的 PNG 预览图，不修改游戏资源。
- `draw_ccz_image_asset` / `prepare_ccz_generated_image` 只写 `CCZModStudio_Exports/AiImageAssets` 下的生成源图、规范化图片、manifest 和 raw response，并自动调用 E5/DLL 替换预览；上游 API Key 只能来自本机环境变量或非提交本机配置，不直接写入游戏资源。
- `create_test_copy` 拒绝从已有测试副本再次创建嵌套测试副本；`diff_test_copy` 要求目标目录存在 `_CCZModStudio_TestCopy.txt`。
- `create_release_copy` 可从当前项目或测试副本生成发布目录，排除 `_CCZModStudio_TestCopy.txt`、`_CCZModStudio_Backups`、`_CCZModStudio_Reports`、`_CCZModStudio_Exports`。
- 所有项目内目标路径都会做根目录边界校验，拒绝 `..` 路径逃逸。
- 所有写入继续使用现有服务层：写前备份、结构化 JSON 报告、写后复读/哈希校验、版本护栏不绕过。
- `list_scenario_files`、`read_scenario_commands`、`search_scenario_scripts` 只读 `RS` 目录下的 R/S eex，复用旧版完整树 Reader；它们用于定位、核对、备注和规划，不开放命令结构写回。
- `list_scenario_command_templates` / `read_scenario_command_template` 只暴露“参数槽候选、创作者提示和风险边界”，不证明完整命令长度，不作为 R/S eex 命令树写回依据。
- `list_knowledge_entries` / `search_knowledge_entries` 递归覆盖 `本地知识库` 分层目录；`read_knowledge_entry` 支持相对路径或唯一文件名，读取时复用目录边界校验，拒绝路径逃逸。
- `list_hexzmap_blocks` / `read_hexzmap_block` 只读 `Hexzmap.e5`，通过现有 `HexzmapProbeReader` 和素材库 hex 标记给出地形候选名称；实际写入仍必须用 `write_hexzmap_block`，并继续执行版本护栏、备份、复读和结构化报告。
- `list_project_resources` / `run_resource_diagnostics` 只读资源文件；诊断报告只写入 `CCZModStudio_Reports`，不修改任何游戏资源。资源替换前应先用这两个工具确认分类、格式线索、编号缺口和地图尺寸。
- `list_workflow_guide` / `list_project_evidence` / `read_project_evidence` 只读工作流状态和证据索引；`read_project_evidence` 只能读取 `ProjectEvidenceService` 扫描到的工具产物，PNG 不返回二进制内容。`write_project_delivery_report` 只写 `CCZModStudio_Reports` 下的 Markdown 报告，不修改游戏文件。
- `list_creator_notes` / `upsert_creator_note` / `delete_creator_note` / `export_creator_notes_csv` 只读写 `CCZModStudio_Notes` 和 `CCZModStudio_Exports/CreatorNotes`；它们用于制作留痕、风险记录、回滚说明、发布证据和知识迁移，不写入 Data/Imsg/Star/Hexzmap/RS/Map/EXE 等游戏文件，发布副本也会排除这些目录。
- Hexzmap 写入仍受 `ProjectVersionGuardService` 约束；当前检测到的 `基底\加强版6.5未加密版\Hexzmap.e5` 尺寸为 `44840`，与护栏中的 6.5 基准 `45254` 不一致，因此 Hexzmap 写入会被拒绝。这是防混用保护，不应在 MCP 层绕开。

#### Codex Desktop 接入方式

推荐通过 `工具整合包\MCP配置` 管理客户端配置，不再手工在各客户端里散写绝对路径。

生成当前机器可用配置：

```powershell
cd "F:\从0开始的AI制作曹操传MOD\曹操传加强版6.5（未加密）\工具整合包"
powershell -NoProfile -ExecutionPolicy Bypass -File ".\MCP配置\generate-mcp-config.ps1" -Build
```

生成目录：

```text
F:\从0开始的AI制作曹操传MOD\曹操传加强版6.5（未加密）\工具整合包\MCP配置\_generated
```

生成内容：

- `codex-config-snippet.toml`：合并到 Codex `config.toml` 或项目 `.codex\config.toml`。
- `mcp-servers.json`：通用 MCP JSON 片段。
- `claude_desktop_config.json`：Claude Desktop 同形态配置。
- `mcp-servers-direct-dotnet.json`：直接 `dotnet <dll>` 启动的备用配置。

推荐客户端入口统一指向：

```powershell
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "F:\从0开始的AI制作曹操传MOD\曹操传加强版6.5（未加密）\工具整合包\MCP配置\start-ccz-mcp.ps1" -GameRoot "F:\从0开始的AI制作曹操传MOD\曹操传加强版6.5（未加密）\基底\加强版6.5未加密版"
```

该脚本会切到 `工具整合包` 作为工作目录，必要时构建 MCP Server，并设置 `CCZMODSTUDIO_GAME_ROOT`。这样可规避部分客户端不支持 `cwd` 或工作目录不稳定的问题。

本地 stdio 直接启动命令仍可作为备用：

```powershell
dotnet "F:\从0开始的AI制作曹操传MOD\曹操传加强版6.5（未加密）\工具整合包\CCZModStudio.McpServer\bin\Debug\net8.0-windows\CCZModStudio.McpServer.dll"
```

建议 `cwd` 设置为：

```text
F:\从0开始的AI制作曹操传MOD\曹操传加强版6.5（未加密）\工具整合包
```

如需固定项目目录，可设置环境变量：

```text
CCZMODSTUDIO_GAME_ROOT=F:\从0开始的AI制作曹操传MOD\曹操传加强版6.5（未加密）\基底\加强版6.5未加密版
```

`MCP配置\_generated` 属于本机生成物，已加入 `.gitignore`；跨设备迁移时重新运行生成脚本，不提交本机绝对路径。

#### 本轮验证

- 2026-06-08：MCP 补齐制作闭环和证据链能力，新增 `list_workflow_guide`、`list_project_evidence`、`read_project_evidence`、`write_project_delivery_report`；`validate-mcp-config.ps1` 最低工具数更新为 50，本机 stdio 验证返回 `tools=62`。
- 2026-06-07：MCP 补齐 AI 绘图素材能力，新增 `list_ccz_image_asset_presets`、`build_ccz_image_prompt`、`prepare_ccz_generated_image`、`draw_ccz_image_asset`；`validate-mcp-config.ps1` 最低工具数更新为 46。
- 2026-06-07：MCP 补齐资源制作能力，新增 `preview_resource_replace`、图片资源目录/条目/PNG 预览导出、E5 批量预览/批量替换、DLL RT_BITMAP 图标预览/替换/清空；知识库工具改为递归覆盖分层 Markdown，并支持按唯一文件名读取。`validate-mcp-config.ps1` 最低工具数更新为 42。
- 2026-06-05：补齐 `工具整合包\MCP配置`。`generate-mcp-config.ps1 -Build` 可生成 Codex TOML、通用 `mcpServers` JSON、Claude Desktop JSON 和 direct-dotnet 备用 JSON；`MCP配置\_generated` 为本机绝对路径生成物，已加入 `.gitignore`。
- 2026-06-05：新增 `validate-mcp-config.ps1`，通过 `MCP配置\start-ccz-mcp.ps1` 启动 stdio MCP Server 并发送 `initialize` + `notifications/initialized` + `tools/list`，验证通过，输出 `MCP_VALIDATE_OK server=CCZModStudio.McpServer protocol=2025-06-18 tools=32`。启动脚本已兼容 Windows PowerShell 5.1，缺失 DLL 时构建输出只写 stderr，避免污染 MCP stdout。
- 2026-06-04：`dotnet build .\CCZModStudio.McpServer\CCZModStudio.McpServer.csproj -v:minimal`：通过，0 警告，0 错误。
- 2026-06-04：`dotnet build .\CCZModStudio.SmokeTests\CCZModStudio.SmokeTests.csproj -v:minimal`：通过，0 警告，0 错误。
- 2026-06-04：`dotnet run --project .\CCZModStudio.SmokeTests\CCZModStudio.SmokeTests.csproj -- --e5-image-replace-smoke`：通过，输出 `E5_IMAGE_REPLACE_SMOKE OK target=Unit_mov.e5 image=554 kind=RAW->PNG`。
- 2026-06-04：MCP 知识检索与剧本指令模板接入：新增 `search_knowledge_entries`、`list_scenario_command_templates`、`read_scenario_command_template`；临时 SDK client 验证 `tools/list` 返回 21 个 tools，并成功调用知识库 `4076` 关键词搜索、`变量` 指令模板筛选和 `0x72` 指令模板详情。
- 2026-06-04：MCP 补齐 R/S 剧本结构只读入口：新增 `list_scenario_files`、`read_scenario_commands`、`search_scenario_scripts`；临时 SDK client 验证 `tools/list` 返回 24 个 tools，`list_scenario_files kind=S limit=3` 可见 S 剧本总数 59，`read_scenario_commands RS/S_00.eex command_filter=0x5A` 返回 `Scene=3 / Section=17 / Command=819`，`search_scenario_scripts keyword=胜利 relative_path=RS/S_00.eex` 返回 4 条命中。
- 2026-06-04：MCP 补齐 Hexzmap 读取入口：新增 `list_hexzmap_blocks`、`read_hexzmap_block`；临时 SDK client 验证 `tools/list` 返回 26 个 tools，`list_hexzmap_blocks keyword=M000 limit=3` 可返回地形块列表和素材库地形名称候选，`read_hexzmap_block map_id=M000 max_rows=2` 可返回地形格矩阵、`cellCount`、`topTerrains` 和坐标边界。
- 2026-06-04：MCP 补齐资源索引和资源诊断入口：新增 `list_project_resources`、`run_resource_diagnostics`；临时 SDK client 验证 `tools/list` 返回 28 个 tools，`list_project_resources category=地图图片 keyword=M000 limit=3` 可返回地图资源索引，`run_resource_diagnostics severity=Warn,Error limit=5` 可返回资源风险摘要和诊断项。
- 2026-06-04：MCP 补齐项目侧制作留痕入口：新增 `list_creator_notes`、`upsert_creator_note`、`delete_creator_note`、`export_creator_notes_csv`；临时 SDK client 验证 `tools/list` 返回 32 个 tools，并完成一条 `mcp-smoke-*` 备注的创建、关键字查询、CSV 导出和删除。该流程只写 `CCZModStudio_Notes` 与 `CCZModStudio_Exports/CreatorNotes`。
- 2026-06-04：MCP 继续扩展安全制作闭环，新增 `audit_project`、`create_test_copy`、`diff_test_copy`、`create_release_copy`；该阶段 SDK client 验证 `tools/list` 返回 18 个 tools，`audit_project` 返回 `totalItems=22`，`diff_test_copy` 可读取既有 E5 测试副本差异，`create_release_copy` 可生成发布目录和 `_CCZModStudio_ReleaseManifest.txt`。
- 2026-06-04：使用 SDK `StdioClientTransport` 连接本地 MCP Server，当前 `tools/list` 返回 32 个 tools，并确认 E5 图片条目、项目体检、测试副本、差异、发布闭环、知识库搜索、剧本指令模板、R/S 剧本结构只读工具、Hexzmap 读取工具、资源索引、资源诊断工具和制作留痕工具均可见；调用 `list_e5_image_entries target_relative_path=Unit_mov.e5 limit=3` 成功返回 `totalEntries=556`。
- `dotnet build .\工具整合包\CCZModStudio.McpServer\CCZModStudio.McpServer.csproj -v:minimal`：通过，0 警告，0 错误。
- `dotnet build .\工具整合包\CCZModStudio.sln -v:minimal -p:BaseOutputPath=..\_BuildCheck\`：通过，0 警告，0 错误。
- 2026-06-03：MCP stdio `initialize` + `tools/list`：通过，返回 11 个 tools。
- 2026-06-03：MCP `detect_project`：通过，能识别当前基底、HexTable 和旧工具资源路径。
- 2026-06-03：MCP `read_table`：通过，能读取 `6.5-0 人物` 指定列。
- 2026-06-03：MCP `write_table_rows`：通过，在测试副本写入 `6.5-0 人物` 的 `级别` 字段，生成 `Data.e5` 备份和 `数据表保存_WriteOperationReport.json`。
- `--rs-smoke`：通过。
- `--rs-write-smoke`：前置写入项通过，最终在 Hexzmap 写入处被版本护栏拒绝，原因见上方安全边界。

