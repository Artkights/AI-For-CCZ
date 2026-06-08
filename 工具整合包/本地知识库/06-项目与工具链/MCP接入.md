# 16 MCP 接入

## 结论速览

- MCP Server 当前只保留制作所需的无头读写工具：项目识别、数据表、R/S 剧本、Hexzmap、地图底图、图片资源、特效包和本地知识库。
- 非制作主线的管理、检查、留痕、发布和自动恢复类入口已移出当前 MCP 工具集。
- 写入类操作仍必须以 6.5 未加密基底、本地样本、写前备份、写后复读和结构化报告为最终依据。

## 适用版本

- 默认适用：曹操传加强版 6.5（未加密）当前项目基底。
- 6.6/6.6x 或旧版本只作为版本差异、兼容提示或外部佐证，不自动进入 6.5 写入规则。

## 已确认事实

- `CCZModStudio.McpServer` 是本地 stdio MCP Server，引用 `CCZModStudio.Runtime`，不启动 WinForms UI。
- MCP Server 启动后只通过 stdout 发送 MCP JSON-RPC 消息；普通诊断输出不得写 stdout。
- 高风险写入继续复用桌面端服务层，不绕过路径边界、版本护栏、备份、复读和报告。

## 实现/使用方法

- 推荐通过 `工具整合包\MCP配置\start-ccz-mcp.ps1` 启动，必要时设置 `CCZMODSTUDIO_GAME_ROOT` 固定项目根目录。
- 客户端配置由 `工具整合包\MCP配置\generate-mcp-config.ps1 -Build` 生成；`MCP配置\_generated` 是本机生成物，不提交。
- 修改游戏文件前必须确认目标版本、目标路径、写前备份、写后复读和结构化报告。

## 风险与边界

- MCP 只保留制作读写工具；非制作主线的管理、检查、留痕、发布和自动恢复能力不作为当前入口。
- 恢复只依赖写入服务生成的备份文件手动处理；MCP 不提供自动恢复入口。
- `replace_resource` 禁止直接覆盖 `Ekd5.exe`、`Data.e5`、`Imsg.e5`、`Star.e5`、`Hexzmap.e5` 等核心文件；这些文件必须走专用写入工具。
- 所有项目内目标路径都会做根目录边界校验，拒绝 `..` 路径逃逸。

## 证据来源

- 本地来源：当前 `CCZModStudio.McpServer\CczMcpTools.cs` 公开工具、构建验证和本地烟测记录。
- 关联来源：`CCZModStudio.McpServer\README.md`、`06-项目与工具链\验证与烟测.md`。

## 待验证项

- 每次新增或删除 MCP 工具后，必须同步更新 `CCZModStudio.McpServer\README.md` 和本页。
- 若将来恢复任何非制作主线能力，必须重新建立独立安全边界和烟测记录。

## 详细记录

#### 工程结构

- `CCZModStudio.Runtime`：链接 `CCZModStudio\Core`、`Formats`、`Models` 的服务层源码，供无头调用使用。
- `CCZModStudio.McpServer`：本地 stdio MCP Server，引用 `CCZModStudio.Runtime`。
- `CCZModStudio`：Windows 桌面程序。
- `CCZModStudio.SmokeTests`：本地烟测工程。

#### 当前 MCP Tools

| 分组 | Tools |
| --- | --- |
| 项目识别 | `detect_project` |
| 游戏设定数据 | `list_tables`, `read_table`, `write_table_rows`, `list_effects`, `read_effect`, `export_effect_package`, `preview_effect_package`, `apply_effect_package`, `list_effect_templates`, `build_effect_package_from_template`, `preview_effect_patch`, `apply_effect_patch`, `read_effect_resource`, `read_effect_prompt` |
| 游戏制作数据 | `list_scenario_files`, `read_scenario_commands`, `search_scenario_scripts`, `read_scenario_texts`, `write_scenario_texts`, `list_scenario_command_templates`, `read_scenario_command_template`, `list_hexzmap_blocks`, `read_hexzmap_block`, `write_hexzmap_block`, `replace_map_image` |
| 资源替换 | `preview_resource_replace`, `replace_resource` |
| 图片目录和预览 | `list_image_resources`, `list_image_resource_entries`, `export_image_resource_preview` |
| AI 图片素材 | `list_ccz_image_asset_presets`, `build_ccz_image_prompt`, `prepare_ccz_generated_image`, `draw_ccz_image_asset` |
| E5 图片条目 | `list_e5_image_entries`, `preview_e5_image_replace`, `replace_e5_image_entry`, `preview_e5_image_batch_replace`, `replace_e5_image_batch` |
| DLL 位图图标 | `preview_dll_icon_replace`, `replace_dll_icon`, `preview_clear_dll_icon`, `clear_dll_icon` |
| 本地知识库 | `list_knowledge_entries`, `search_knowledge_entries`, `read_knowledge_entry` |

#### 写入边界

- `write_table_rows` 按 `row_id` 写入指定列，拒绝 `ID` 和派生列。
- `write_scenario_texts` 只按文本 `index` 原地写回，必须满足原 GBK 容量。
- `write_hexzmap_block` 只按 `Mxxx` 和坐标写入 Hexzmap 地形格，继续受版本护栏约束。
- `replace_map_image` 只替换 `Map\Mxxx.jpg` 地图底图。
- `replace_e5_image_entry` / `replace_e5_image_batch` 只允许可读取 `0x110` 索引表的 E5 图片载荷文件，拒绝核心表文件。
- `replace_dll_icon` / `clear_dll_icon` 只开放 `Itemicon.dll`、`Mgcicon.dll`、`Cmdicon.dll` 的 RT_BITMAP 位图资源。
- AI 图片工具只写 `CCZModStudio_Exports\AiImageAssets` 和预览产物，不直接写游戏资源。

#### 启动方式

```powershell
cd "F:\从0开始的AI制作曹操传MOD\曹操传加强版6.5（未加密）\工具整合包"
powershell -NoProfile -ExecutionPolicy Bypass -File ".\MCP配置\generate-mcp-config.ps1" -Build
```

推荐客户端入口：

```powershell
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "F:\从0开始的AI制作曹操传MOD\曹操传加强版6.5（未加密）\工具整合包\MCP配置\start-ccz-mcp.ps1" -GameRoot "F:\从0开始的AI制作曹操传MOD\曹操传加强版6.5（未加密）\基底\加强版6.5未加密版"
```

备用直接启动：

```powershell
dotnet "F:\从0开始的AI制作曹操传MOD\曹操传加强版6.5（未加密）\工具整合包\CCZModStudio.McpServer\bin\Debug\net8.0-windows\CCZModStudio.McpServer.dll"
```

#### 本轮验证

- 2026-06-08：`dotnet build 工具整合包\CCZModStudio.McpServer\CCZModStudio.McpServer.csproj --no-restore -v:minimal` 通过，0 警告，0 错误。
- 2026-06-08：`dotnet build 工具整合包\CCZModStudio.SmokeTests\CCZModStudio.SmokeTests.csproj --no-restore -v:minimal` 通过，0 警告，0 错误。
- 2026-06-08：公开工具已收敛到当前制作主线；README 与本页按当前工具清单同步。
