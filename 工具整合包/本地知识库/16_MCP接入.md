# 16 MCP 接入

本页记录 CCZModStudio 接入 MCP 后的结构、工具边界和验证状态。

## 工程结构

- `CCZModStudio.Runtime`：链接 `CCZModStudio\Core`、`Formats`、`Models` 的服务层源码，供无头调用使用。
- `CCZModStudio.McpServer`：本地 stdio MCP Server，引用 `CCZModStudio.Runtime`，不启动 WinForms UI。
- `CCZModStudio`：原 Windows 桌面程序保留。
- `CCZModStudio.SmokeTests`：原烟测保留。

MCP Server 启动后只通过 stdout 发送 MCP JSON-RPC 消息；普通日志不写 stdout，避免污染 stdio 协议。

## MCP Tools

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
| `write_hexzmap_block` | 按 `Mxxx` 和坐标写入 Hexzmap 地形格 | 是 |
| `replace_map_image` | 替换 `Map\Mxxx.jpg` 地图底图 | 是 |
| `list_e5_image_entries` | 读取 `Face.e5`、`Pmapobj.e5`、`Unit_*.e5` 等 E5 图片资源的 `0x110` 索引表 | 否 |
| `preview_e5_image_replace` | 预览单个 E5 图片条目替换，支持普通图片或备份 E5 内指定图号作为来源 | 否 |
| `replace_e5_image_entry` | 替换单个 E5 图片索引条目，写前备份、写后复读、生成结构化报告 | 是 |
| `replace_resource` | 替换非核心资源文件，禁止直接覆盖核心表文件 | 是 |
| `audit_project` | 运行项目体检，可选择生成体检报告 | 否 |
| `create_test_copy` | 从原始项目创建完整测试副本，供安全编辑 | 是 |
| `diff_test_copy` | 对比测试副本与来源项目，可选择生成差异报告 | 否 |
| `create_release_copy` | 从测试副本生成干净发布副本，排除测试标记、备份、报告和导出目录 | 是 |
| `list_scenario_command_templates` | 读取 6.5 `CczString.ini` 字典和内置 R/S eex 命令参数模板，可按关键字、分类和覆盖状态筛选 | 否 |
| `read_scenario_command_template` | 按命令号或名称读取单条 R/S eex 命令参数模板详情 | 否 |
| `list_knowledge_entries` | 列出本地知识库 Markdown | 否 |
| `search_knowledge_entries` | 按关键字搜索本地知识库 Markdown，返回文件、行号和上下文片段 | 否 |
| `read_knowledge_entry` | 读取本地知识库 Markdown | 否 |

所有写入 tool 都支持 `write_mode`：

- `direct`：允许写当前检测到的项目目录。
- `test_copy`：要求目标目录存在 `_CCZModStudio_TestCopy.txt`。

## 安全边界

- `replace_resource` 禁止直接覆盖 `Ekd5.exe`、`Data.e5`、`Imsg.e5`、`Star.e5`、`Hexzmap.e5`；这些文件必须走表格或 Hexzmap 专用写入。
- `replace_e5_image_entry` 只允许 `.e5` 图片载荷文件，且拒绝 `Data.e5`、`Imsg.e5`、`Star.e5`、`Hexzmap.e5` 等核心文件；人物 R/S 指定编号仍必须通过表格写入，不能用 E5 图片替换工具改核心表。
- `create_test_copy` 拒绝从已有测试副本再次创建嵌套测试副本；`diff_test_copy` 与 `create_release_copy` 要求目标目录存在 `_CCZModStudio_TestCopy.txt`。
- `create_release_copy` 只从测试副本生成发布目录，排除 `_CCZModStudio_TestCopy.txt`、`_CCZModStudio_Backups`、`_CCZModStudio_Reports`、`_CCZModStudio_Exports`。
- 所有项目内目标路径都会做根目录边界校验，拒绝 `..` 路径逃逸。
- 所有写入继续使用现有服务层：写前备份、结构化 JSON 报告、写后复读/哈希校验、版本护栏不绕过。
- `list_scenario_files`、`read_scenario_commands`、`search_scenario_scripts` 只读 `RS` 目录下的 R/S eex，复用旧版完整树 Reader；它们用于定位、核对、备注和规划，不开放命令结构写回。
- `list_scenario_command_templates` / `read_scenario_command_template` 只暴露“参数槽候选、创作者提示和风险边界”，不证明完整命令长度，不作为 R/S eex 命令树写回依据。
- `search_knowledge_entries` 只搜索 `本地知识库` 顶层 Markdown，读取时复用目录边界校验，拒绝路径逃逸。
- Hexzmap 写入仍受 `ProjectVersionGuardService` 约束；当前检测到的 `基底\加强版6.5未加密版\Hexzmap.e5` 尺寸为 `44840`，与护栏中的 6.5 基准 `45254` 不一致，因此 Hexzmap 写入会被拒绝。这是防混用保护，不应在 MCP 层绕开。

## Codex Desktop 接入方式

本地 stdio 启动命令：

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

## 本轮验证

- 2026-06-04：`dotnet build .\CCZModStudio.McpServer\CCZModStudio.McpServer.csproj -v:minimal`：通过，0 警告，0 错误。
- 2026-06-04：`dotnet build .\CCZModStudio.SmokeTests\CCZModStudio.SmokeTests.csproj -v:minimal`：通过，0 警告，0 错误。
- 2026-06-04：`dotnet run --project .\CCZModStudio.SmokeTests\CCZModStudio.SmokeTests.csproj -- --e5-image-replace-smoke`：通过，输出 `E5_IMAGE_REPLACE_SMOKE OK target=Unit_mov.e5 image=554 kind=RAW->PNG`。
- 2026-06-04：MCP 知识检索与剧本指令模板接入：新增 `search_knowledge_entries`、`list_scenario_command_templates`、`read_scenario_command_template`；临时 SDK client 验证 `tools/list` 返回 21 个 tools，并成功调用知识库 `4076` 关键词搜索、`变量` 指令模板筛选和 `0x72` 指令模板详情。
- 2026-06-04：MCP 补齐 R/S 剧本结构只读入口：新增 `list_scenario_files`、`read_scenario_commands`、`search_scenario_scripts`；临时 SDK client 验证 `tools/list` 返回 24 个 tools，`list_scenario_files kind=S limit=3` 可见 S 剧本总数 59，`read_scenario_commands RS/S_00.eex command_filter=0x5A` 返回 `Scene=3 / Section=17 / Command=819`，`search_scenario_scripts keyword=胜利 relative_path=RS/S_00.eex` 返回 4 条命中。
- 2026-06-04：MCP 继续扩展安全制作闭环，新增 `audit_project`、`create_test_copy`、`diff_test_copy`、`create_release_copy`；该阶段 SDK client 验证 `tools/list` 返回 18 个 tools，`audit_project` 返回 `totalItems=22`，`diff_test_copy` 可读取既有 E5 测试副本差异，`create_release_copy` 可生成发布目录和 `_CCZModStudio_ReleaseManifest.txt`。
- 2026-06-04：使用 SDK `StdioClientTransport` 连接本地 MCP Server，当前 `tools/list` 返回 24 个 tools，并确认 E5 图片条目、项目体检、测试副本、差异、发布闭环、知识库搜索、剧本指令模板和 R/S 剧本结构只读工具均可见；调用 `list_e5_image_entries target_relative_path=Unit_mov.e5 limit=3` 成功返回 `totalEntries=556`。
- `dotnet build .\工具整合包\CCZModStudio.McpServer\CCZModStudio.McpServer.csproj -v:minimal`：通过，0 警告，0 错误。
- `dotnet build .\工具整合包\CCZModStudio.sln -v:minimal -p:BaseOutputPath=..\_BuildCheck\`：通过，0 警告，0 错误。
- 2026-06-03：MCP stdio `initialize` + `tools/list`：通过，返回 11 个 tools。
- 2026-06-03：MCP `detect_project`：通过，能识别当前基底、HexTable 和旧工具资源路径。
- 2026-06-03：MCP `read_table`：通过，能读取 `6.5-0 人物` 指定列。
- 2026-06-03：MCP `write_table_rows`：通过，在测试副本写入 `6.5-0 人物` 的 `级别` 字段，生成 `Data.e5` 备份和 `数据表保存_WriteOperationReport.json`。
- `--rs-smoke`：通过。
- `--rs-write-smoke`：前置写入项通过，最终在 Hexzmap 写入处被版本护栏拒绝，原因见上方安全边界。
