# 证据采集指南

`evidence_collector.py` 用于记录离线机器码、旧版配方和运行时断点样本。以下命令从源码根目录执行，路径、PID、功能名和地址均应替换为当前目标的实际值。`static`、`oracle`、`inspect` 和 `verify` 不会启动游戏；`runtime` 会附加已有进程并设置软件断点，只在允许动态采集的任务中使用。

## 状态与执行策略

证据状态与界面读写权限是两套信息：

| 状态 | 含义 |
|---|---|
| `Unavailable` | 当前功能尚未取得完整的签名、结构、映射或 ABI 证据 |
| `StaticVerified` | 当前映像的唯一签名、地址引用和结构证据已通过 |
| `RuntimeVerified` | 代码指定的全部基准样本具有同一规范化配方的受信运行时证据 |
| `Enabled` | 在受信证据之外，本次会话的功能探针也已通过 |

`FeatureCapability.available` 仅在 `Enabled` 时为真，供诊断和报告使用。默认 `access_policy=direct_compat` 时，`can_read()` / `can_write()` 按画像是否存在具体地址、布局或执行配方判断，不要求证据状态达到 `Enabled`。因此，静态签名损坏而变成 `Unavailable`，不等于兼容模式下的所有写入都会被禁用。`evidence_gated` 策略则按已解析功能的访问级别判断。

直接兼容执行不会把证据提升为已验证，也不补全不存在的远程入口或 ABI。字段编码边界、进程内存读写错误和远程调用资源清理仍由实现处理；不要将证据状态解释成普通补丁具有事务、旧字节比较或回滚保证。

## 环境

选择 Windows Python 环境后安装依赖：

```powershell
# 32 位环境
python -m pip install -r requirements-dev-32.txt

# 64 位开发环境使用 requirements-dev.txt
python evidence_collector.py --help
```

正式构建使用 32 位 Python 和 `build_release.ps1`。运行时采集在 64 位宿主上通过 WOW64 上下文 API 处理 32 位目标，在 32 位宿主上使用原生上下文 API。

## 静态采集与旧版 Oracle

读取指定 EXE，输出签名、规范化反汇编、地址和引用。`--feature` 可以重复；不传时采集器会处理当前画像支持的功能。

```powershell
python evidence_collector.py static `
  --exe "C:\Games\Example\Ekd5.exe" `
  --output .\evidence-work\engine-static `
  --feature control_all_factions `
  --feature control_auto_return `
  --feature item_consumables
```

`oracle` 按旧源码顺序离线重放补丁写入，供对照旧站点和自动回归 stub；它不会改写原 EXE。`--legacy-index` 范围为 `0..4`，应与待还原的旧版分支对应。下列战场地址和步长仅是参数示例：

```powershell
python evidence_collector.py oracle `
  --exe "C:\Games\LegacyExample\Ekd5.exe" `
  --output .\evidence-work\legacy-oracle `
  --legacy-index 0 `
  --battle-address 0x004A7B20 --battle-stride 0x30
```

当所需地址位于 PE 的虚拟未初始化区时，Oracle 会记录零填充来源，不会把其它文件区域误读为该地址的后备字节。

## 运行时采集

先独立确认当前 EXE 的断点和观察区，准备功能配方 JSON。以下 `PID=1234`、断点 `0x00430000` 和观察区都只是示意，不能直接套用于未知引擎：

```powershell
python evidence_collector.py runtime `
  --pid 1234 --feature control_all_factions `
  --output .\evidence-work\engine-runtime `
  --recipe .\recipes\control_all_factions.json `
  --breakpoint 0x00430000 --samples 6 --timeout 180 `
  --watch unit:0x004A7B20:0x30 `
  --tag phase=baseline --tag camp=ours
```

`--breakpoint`、`--watch` 和 `--tag` 可以重复。观察区语法为 `名称:地址:长度`，标签语法为 `键=值`。采集器记录寄存器、栈、下一 EIP 和观察区字节，并在单步和退出时恢复自己设置的断点字节。输出初始状态是 `captured`，不因采集成功或人工描述而成为 `verified`。

追加其它场景时，使用同一目标、功能和配方，并把 `--append` 指向前一次输出的**具体 JSON 文件**。工具不会自行展开 `*.json`：

```powershell
$captured = (Get-ChildItem .\evidence-work\engine-runtime\*.json |
  Where-Object Name -ne 'manifest.json' |
  Sort-Object LastWriteTime -Descending |
  Select-Object -First 1).FullName

python evidence_collector.py runtime `
  --pid 1234 --feature control_all_factions `
  --output .\evidence-work\engine-runtime `
  --recipe .\recipes\control_all_factions.json `
  --breakpoint 0x00430000 --samples 6 --timeout 180 `
  --watch unit:0x004A7B20:0x30 `
  --tag phase=patched --tag camp=enemy --append "$captured"
```

## 验证与受信目录

配方的 `verificationContract.checks` 必须为功能要求定义可计算的检查。支持样本数量、标签覆盖、字段存在、值比较、差异数量和观察区组间比较等谓词。`verify` 从原始样本计算结果，不接受手工 `passed=true` 代替检查。

将 `$captured` 更新为待验证的具体文件后执行：

```powershell
python evidence_collector.py inspect "$captured"
python evidence_collector.py verify "$captured" --output .\evidence\engine
python evidence_collector.py manifest --output .\evidence\engine
```

`inspect` 返回 `0` 表示运行时证据验证通过；返回 `2` 表示内容可读取但仍缺少验证，解析或操作错误返回 `1`。`verify` 要求样本非空、所有功能专用检查通过且摘要有效，才会输出 `verified` 证据。

应用默认加载根目录 `evidence/manifest.json` 中列出的文件。单独输出到 `evidence/engine/` 便于审查，该子目录不会自动合并进默认受信清单。正式导入时，应把审核后的 JSON 放入 `evidence/`，再执行：

```powershell
python evidence_collector.py manifest --output .\evidence
```

`manifest.json` 记录文件 SHA256，bundle 另有内容摘要；未登记文件不会被加载，摘要不符会报错。提升到 `RuntimeVerified` 还要求代码指定的全部基准 SHA 具有相同 `recipeId` 和配方内容。派生版名称或额外文件本身不能扩充基准集合。空清单表示未提供受信证据；兼容模式能执行某项操作，也不能据此写成已完成实机验证。
