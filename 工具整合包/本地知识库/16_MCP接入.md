# 16 MCP 接入

本页记录 CCZModStudio 接入 MCP 后的结构、工具边界和验证状态。

## 工程结构

- `CCZModStudio.Runtime`：链接 `CCZModStudio\Core`、`Formats`、`Models` 的服务层源码，供无头调用使用。
- `CCZModStudio.McpServer`：本地 stdio MCP Server，引用 `CCZModStudio.Runtime`，不启动 WinForms UI。
- `CCZModStudio`：原 Windows 桌面程序保留。
- `CCZModStudio.SmokeTests`：原烟测保留。

MCP Server 启动后只通过 stdout 发送 MCP JSON-RPC 消息；普通日志不写 stdout，避免污染 stdio 协议。

## 首批 MCP Tools

| Tool | 用途 | 写入 |
| --- | --- | --- |
| `detect_project` | 识别工作区、游戏目录、HexTable、旧工具资源路径、核心文件状态 | 否 |
| `list_tables` | 列出 HexTable 表定义和字段 | 否 |
| `read_table` | 读取指定数据表，可按行、列、关键字和 limit 过滤 | 否 |
| `write_table_rows` | 按 `row_id` 写入指定列，拒绝 `ID` 和派生列 | 是 |
| `read_scenario_texts` | 读取 `RS\*.eex` 的 GBK 文本线索 | 否 |
| `write_scenario_texts` | 按文本 `index` 原地写回，必须满足原 GBK 容量 | 是 |
| `write_hexzmap_block` | 按 `Mxxx` 和坐标写入 Hexzmap 地形格 | 是 |
| `replace_map_image` | 替换 `Map\Mxxx.jpg` 地图底图 | 是 |
| `replace_resource` | 替换非核心资源文件，禁止直接覆盖核心表文件 | 是 |
| `list_knowledge_entries` | 列出本地知识库 Markdown | 否 |
| `read_knowledge_entry` | 读取本地知识库 Markdown | 否 |

所有写入 tool 都支持 `write_mode`：

- `direct`：允许写当前检测到的项目目录。
- `test_copy`：要求目标目录存在 `_CCZModStudio_TestCopy.txt`。

## 安全边界

- `replace_resource` 禁止直接覆盖 `Ekd5.exe`、`Data.e5`、`Imsg.e5`、`Star.e5`、`Hexzmap.e5`；这些文件必须走表格或 Hexzmap 专用写入。
- 所有项目内目标路径都会做根目录边界校验，拒绝 `..` 路径逃逸。
- 所有写入继续使用现有服务层：写前备份、结构化 JSON 报告、写后复读/哈希校验、版本护栏不绕过。
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

- `dotnet build .\工具整合包\CCZModStudio.McpServer\CCZModStudio.McpServer.csproj -v:minimal`：通过，0 警告，0 错误。
- `dotnet build .\工具整合包\CCZModStudio.sln -v:minimal -p:BaseOutputPath=..\_BuildCheck\`：通过，0 警告，0 错误。
- MCP stdio `initialize` + `tools/list`：通过，返回 11 个 tools。
- MCP `detect_project`：通过，能识别当前基底、HexTable 和旧工具资源路径。
- MCP `read_table`：通过，能读取 `6.5-0 人物` 指定列。
- MCP `write_table_rows`：通过，在测试副本写入 `6.5-0 人物` 的 `级别` 字段，生成 `Data.e5` 备份和 `数据表保存_WriteOperationReport.json`。
- `--rs-smoke`：通过。
- `--rs-write-smoke`：前置写入项通过，最终在 Hexzmap 写入处被版本护栏拒绝，原因见上方安全边界。
