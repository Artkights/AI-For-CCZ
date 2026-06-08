# CCZModStudio MCP 配置

本目录保存 CCZModStudio 本地 stdio MCP Server 的客户端接入配置、模板和生成脚本。

## 文件说明

- `start-ccz-mcp.ps1`：稳定启动入口。脚本会切到 `工具整合包` 目录，必要时构建 `CCZModStudio.McpServer`，再启动 stdio MCP Server。
- `generate-mcp-config.ps1`：按当前机器路径生成可直接复制的客户端配置。
- `templates/`：可迁移模板，使用 `<TOOL_ROOT>` 和 `<GAME_ROOT>` 占位。
- `_generated/`：本机生成结果目录，由脚本创建，不纳入 Git。

## 一键生成

在仓库根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\工具整合包\MCP配置\generate-mcp-config.ps1" -Build
```

如果要固定到其他游戏目录：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\工具整合包\MCP配置\generate-mcp-config.ps1" -GameRoot "F:\...\基底\刘备传加强版6.5" -Build
```

生成结果位于：

```text
工具整合包\MCP配置\_generated
```

## Codex 接入

把 `_generated\codex-config-snippet.toml` 的内容合并到 Codex 的 `config.toml`，或放入项目 `.codex\config.toml` 后重启 Codex。

配置形态：

```toml
[mcp_servers.cczmodstudio]
command = 'powershell.exe'
args = ['-NoLogo', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', '<TOOL_ROOT>\MCP配置\start-ccz-mcp.ps1', '-GameRoot', '<GAME_ROOT>']
startup_timeout_sec = 120

[mcp_servers.cczmodstudio.env]
CCZMODSTUDIO_TOOL_ROOT = '<TOOL_ROOT>'
CCZMODSTUDIO_GAME_ROOT = '<GAME_ROOT>'
```

## 通用 MCP JSON 接入

支持 `mcpServers` JSON 的客户端可使用：

```text
工具整合包\MCP配置\_generated\mcp-servers.json
```

Claude Desktop 可使用同形态 JSON：

```text
工具整合包\MCP配置\_generated\claude_desktop_config.json
```

如果客户端能可靠设置工作目录和环境变量，也可以使用：

```text
工具整合包\MCP配置\_generated\mcp-servers-direct-dotnet.json
```

推荐默认使用 PowerShell 启动脚本版本，减少 `cwd` 差异。

## 可用工具分组

- 项目流程：`detect_project`、`audit_project`、`create_test_copy`、`diff_test_copy`、`create_release_copy`、`list_workflow_guide`、`list_project_evidence`、`read_project_evidence`、`write_project_delivery_report`
- 数据表：`list_tables`、`read_table`、`write_table_rows`
- R/S 剧本：`list_scenario_files`、`read_scenario_commands`、`search_scenario_scripts`、`read_scenario_texts`、`write_scenario_texts`
- 指令模板与知识库：`list_scenario_command_templates`、`read_scenario_command_template`、`list_knowledge_entries`、`search_knowledge_entries`、`read_knowledge_entry`
- 地图和资源：`list_project_resources`、`run_resource_diagnostics`、`list_hexzmap_blocks`、`read_hexzmap_block`、`write_hexzmap_block`、`replace_map_image`、`preview_resource_replace`、`replace_resource`
- 图片资源目录/预览：`list_image_resources`、`list_image_resource_entries`、`export_image_resource_preview`
- AI 绘图素材：`list_ccz_image_asset_presets`、`build_ccz_image_prompt`、`prepare_ccz_generated_image`、`draw_ccz_image_asset`
- E5 图片条目：`list_e5_image_entries`、`preview_e5_image_replace`、`replace_e5_image_entry`、`preview_e5_image_batch_replace`、`replace_e5_image_batch`
- DLL 图标：`preview_dll_icon_replace`、`replace_dll_icon`、`preview_clear_dll_icon`、`clear_dll_icon`
- 制作留痕：`list_creator_notes`、`upsert_creator_note`、`delete_creator_note`、`export_creator_notes_csv`

## 验证

先构建：

```powershell
dotnet build ".\工具整合包\CCZModStudio.McpServer\CCZModStudio.McpServer.csproj" -v:minimal
```

再运行本地协议验证：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\工具整合包\MCP配置\validate-mcp-config.ps1"
```

应输出：

```text
MCP_VALIDATE_OK server=CCZModStudio.McpServer protocol=2025-06-18 tools=62
```

客户端重启后可继续调用：

```text
detect_project
list_knowledge_entries
list_tables
```

`detect_project` 应返回工作区、游戏根目录、`HexTable.xml` 和核心文件状态。

## 安全边界

- MCP Server 只通过 stdout 输出 JSON-RPC，普通日志不得写 stdout。
- `replace_resource` 禁止覆盖 `Ekd5.exe`、`Data.e5`、`Imsg.e5`、`Star.e5`、`Hexzmap.e5` 等核心文件。
- E5 图片写入只开放可读取 `0x110` 索引表的图片载荷资源；批量写入使用单次备份和逐条复读校验。
- DLL 图标写入只开放 `Itemicon.dll`、`Mgcicon.dll`、`Cmdicon.dll` 的 RT_BITMAP 位图资源。
- AI 绘图素材只写 `CCZModStudio_Exports\AiImageAssets`，并调用 E5/DLL 预览工具，不直接写入游戏资源；上游 API Key 只能通过本机环境变量或非提交配置提供。
- `export_image_resource_preview` 只写 `CCZModStudio_Exports\ImagePreviews`，不修改游戏文件。
- `list_workflow_guide`、`list_project_evidence`、`read_project_evidence` 只读工作流状态和已生成证据；`read_project_evidence` 可用 `latest_delivery_report` 读取最新综合报告，避免中文路径在非 UTF-8 脚本客户端里乱码；`write_project_delivery_report` 只写 `CCZModStudio_Reports` 下的 Markdown 综合报告，不修改游戏文件。
- 写入工具继续走备份、结构化报告、写后复读/哈希校验和版本护栏。
- `write_mode=test_copy` 要求目标目录存在 `_CCZModStudio_TestCopy.txt`。
- `CCZMODSTUDIO_GAME_ROOT` 用于固定游戏目录；未设置时按工作区自动检测。
