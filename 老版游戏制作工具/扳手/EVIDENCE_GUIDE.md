# 曹操传 6.6 扳手证据采集指南

## 能力状态

6.6 功能依次经过 `Unavailable -> StaticVerified -> RuntimeVerified -> Enabled`，但这条链现在描述“可信度”，不再自动等价于“能否执行”：

- `Unavailable`：签名、结构、ABI 或映射尚未定位；若同时没有 direct compatibility 配方，控件禁用。
- `StaticVerified`：当前 EXE 的唯一签名、原字节和结构引用通过，但没有双基底实机证据；有明确配方时可执行并显示警告。
- `RuntimeVerified`：清儿吕布传和新改曹操傳 6.6 修正版存在同一 `recipeId` 的受信运行时证据。
- `Enabled`：本次附加进程的模块、页权限、原字节和结构探针再次通过。

`.available` 仍仅在 `Enabled` 返回真，供证据与报告使用。运行 UI 使用 `EngineProfile.can_use()`/`can_write()` 的 direct compatibility 配方判断；证据缺失、未知 SHA 或远程 ABI 未验证会进入警告和审计记录，而不会绕过地址范围、字段宽度、页权限或远程线程清理。

## 环境

交互式 PowerShell：

```powershell
conda activate banshou
python -m pip install -r requirements-dev.txt
```

若当前 PowerShell 没有加载 Conda，可先执行：

```powershell
& "D:\Conda\shell\condabin\conda-hook.ps1"
conda activate banshou
```

`banshou` 的 64 位宿主通过 `Wow64GetThreadContext/Wow64SetThreadContext` 调试 32 位 `Ekd5.exe`；32 位 Python 使用原生 `GetThreadContext/SetThreadContext`。正式 32 位发布包仍使用 `build_32bit.bat`。

## 离线采集

采集 6.6 静态签名、规范化反汇编、风险地址和字面量引用：

```powershell
python evidence_collector.py static `
  --exe "..\..\基底\新改曹操傳6.6修正版\Ekd5.exe" `
  --output evidence-work\xg66 `
  --feature control_all_factions `
  --feature control_auto_return `
  --feature turn_refresh_direction `
  --feature turn_refresh_position `
  --feature item_consumables
```

还原 6.5 旧站点和旧自动回归 stub：

```powershell
python evidence_collector.py oracle `
  --exe "..\..\基底\加强版6.5未加密版\Ekd5.exe" `
  --output evidence-work\legacy65 `
  --legacy-index 0 --battle-address 0x004A7B20 --battle-stride 0x30
```

Oracle 会按旧源码原顺序重放重叠写入。`0x485E00` 位于未初始化 PE 区时，报告会明确记录 `loader-zero-filled-uninitialized-section`，不会跨节读取其它文件字节。

## 运行时采集

对测试副本中的候选指令设置软件断点：

```powershell
python evidence_collector.py runtime `
  --pid 1234 --feature control_all_factions `
  --output evidence-work\qinger66 `
  --recipe recipes\control_all_factions.json `
  --breakpoint 0x00430000 --samples 6 --timeout 180 `
  --watch unit:0x004A7B20:0x30 --tag phase=baseline --tag camp=ours
```

采集器记录 EAX、EBX、ECX、EDX、ESI、EDI、EBP、ESP、EIP、EFlags、栈、下一 EIP 和观察区字节。断点原字节在单步期间恢复，退出时再次复原。通用采集只生成 `captured`，不会把人工描述写成已验证结论。

后续场景使用同一个配方，并通过 `--append` 合并原始样本：

```powershell
python evidence_collector.py runtime `
  --pid 1234 --feature control_all_factions --output evidence-work\qinger66 `
  --recipe recipes\control_all_factions.json --breakpoint 0x00430000 `
  --watch unit:0x004A7B20:0x30 --tag phase=patched --tag camp=enemy `
  --append evidence-work\qinger66\control_all_factions-*.json
```

### 自动回归验证

自动回归已经确定为原生二次行动的三站点补丁，不再执行 A/B wrapper 矩阵。离线 `static` 证据必须包含 `0x412D7B/0x43D522` 两个 CALL、原生函数 `0x421175` 和三个补丁点 `0x412D82/0x43D529/0x43D540`。

运行时通过扳手原“自动回归”按钮启停补丁。需要原始断点样本时使用通用命令，不由采证器自动改补丁：

```powershell
python evidence_collector.py runtime `
  --pid 1234 --feature control_auto_return `
  --output evidence-work\qinger66-auto `
  --breakpoint 0x00412D7B --breakpoint 0x0043D522 `
  --watch battle:0x004A7B20:0x30 --samples 6 --timeout 180 `
  --tag phase=patched --tag waitMode=stationary
```

每个基底选择两名我军，分别测试原地待命和移动后待命；每名角色连续恢复三次。机器记录之外还必须确认同一单位能够重新选中并再次打开行动菜单。关闭按钮后复测原限制，并保存启用前、启用后和关闭后的三个站点字节。

配方的 `verificationContract.checks` 必须为该功能的每个要求检查定义机器谓词。支持最小样本数、标签覆盖、字段存在、全值相等、不同值数量、观察区在两个标签组间相等/不同，以及多个谓词同时满足。自动验证命令不接收 `passed=true`：

```powershell
python evidence_collector.py verify `
  evidence-work\qinger66\control_all_factions-*.json `
  --output evidence\qinger66
```

检查证据：

```powershell
python evidence_collector.py inspect evidence-work\qinger66\control_all_factions-*.json
```

输出会列出缺失的功能专用自动检查。只有验证合同从原始样本计算出的所有检查为真、样本非空、样本摘要和文件清单摘要都一致，证据仓库才接受 `verified`。

## 四项最低门槛

| 功能 | 自动检查要求 |
|---|---|
| 不分敌我 | 唯一签名、完整指令边界、旧版 Oracle 一致、三阵营矩阵、完整回滚、连续三次开关、外部改码拒绝 |
| 自动回归 | 三段唯一签名、两个 CALL 共同目标、三站点事务补丁、两基底两角色两种待命、完整回滚、连续三次开关、外部改码拒绝 |
| 转向刷新 | 唯一签名、ABI、三阵营矩阵、只改变方向、回滚 |
| 坐标刷新 | 唯一签名、ABI、三阵营矩阵、占用格、碰撞、寻路、选中框、画面刷新、回滚 |
| 消耗品 | 标准画像采集 ID 160/180/199 与40项边界；旧扩展画像采集 ID 150/200/254 与105项边界；均要求更新/读取/写入/清零锚点、获得/使用差分、写255并恢复和事务回滚 |

两个指定 SHA 仍必须拥有完全相同的规范化配方，才能把证据状态提升到 `RuntimeVerified/Enabled`。未知 6.6 和无配方功能当前仍可强制试用，但不会因此提升证据状态。

## 证据目录

正式受信证据放在 `evidence/`。`manifest.json` 列出每个 JSON 的 SHA256；目录中未列入清单、清单摘要不匹配、内容摘要不匹配或缺少第二基底的文件全部忽略。当前交付只有空清单；强制开放只改变运行入口，不得把试用结果伪造成受信实机状态。
