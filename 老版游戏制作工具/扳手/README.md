# 曹操传 Python 扳手

基于 Python/PyQt5 的 Windows 内存修改工具，包含控制、人物、战场、仓库、天赋、专属/套装、必杀、变量和自定义修改页面。支持旧版引擎及 6.5/6.6 派生画像，通过准星选择游戏窗口。

这是源码工程。构建产物、缓存、游戏样本、存档和历史验收报告不放入源码包。原作者项目见 [Modifier-for-CaoCaoZhuan](https://github.com/Puluomiyuhun/Modifier-for-CaoCaoZhuan)，许可证见 [LICENSE](LICENSE)。

2026-11-05运行时兼容修订及实际验收边界见[运行时兼容说明](docs/runtime-compatibility-20261105.md)。本轮增加保护引擎运行页面扫描、蜀汉6.4特效变体、异步附加、八页快照事务和发布源码一致性校验。

2026-11-07布尔变量纠错见[格式与边界说明](docs/boolean-variables-20261107.md)。每8个编号共用一个字节、按最高位优先存储是正确格式；旧“4096位／512字节纯布尔区”会包含人物字段，已废弃。本版默认 `0..1535` 可写，`1536..2047` 只读保留，`>=2048` 不作为普通布尔变量展示。1536项是依据本地剧本资料选择的保守工具政策，并非已证实的原生容量。“以和为贵1-demo”隔离副本及3关demo的现有用户进程均已只读核验解包后的真实BOOL访问链；本轮没有真实BOOL写入和玩法验收。磁盘保护态代码不直接开放固定地址回退写入。

## 源码结构

| 路径 | 用途 |
|---|---|
| `main.py`、`compatibility.py` | 程序入口、系统兼容性报告与用户数据迁移 |
| `mywindow.py` | 界面、事件及页面操作 |
| `engine_profile.py` | 引擎识别、签名、地址和功能配方 |
| `engine_layout.py`、`semantic_resolver.py` | 表结构与语义锚点解析 |
| `process_memory.py`、`hook.py` | 进程内存、代码补丁与远程调用 |
| `runtime_catalog.py` | 资源文件缺失时读取当前进程的物品目录 |
| `runtime_regions.py` | 依据实际页面属性建立连续可读扫描区域，保留不可读空洞 |
| `verify_release.py` | 对比发布EXE嵌入的业务字节码与当前源码 |
| `capability_evidence.py`、`evidence_collector.py` | 能力证据与诊断采集 |
| `untitled.ui` | Qt Designer 界面源文件，布局回归也会读取 |
| `logo.ico`、`准星.cur`、`准星.png` | 窗口及准星资源 |
| `tests/` | 无游戏依赖的模拟回归与可选本地样本测试 |
| `evidence/manifest.json` | 证据清单，默认不附带外部样本 |
| `build_release.py`、`build_release.ps1`、`banshou66.spec` | 发布构建入口、PowerShell 包装和打包配置 |
| `requirements*.txt` | 运行及开发依赖 |
| `docs/` | 当前发布说明和证据工具指南 |

## 运行

兼容发行版的构建基线为 **CPython 3.8.10 x86**，使用 `requirements-win7-x86.txt` 锁定依赖。运行源码时，以下命令中的 `python` 也应指向这个 32 位解释器：

```powershell
python -m pip install --no-index --require-hashes --find-links .\wheelhouse-win7-x86 -r requirements-win7-x86.txt
python main.py
```

保持底部“自动”，将准星拖到已运行的游戏窗口。未知派生版按功能组合画像；版本号和 SHA256 用于候选排序与名称显示，机器码、表结构及当前进程决定实际地址。可选的 `DIY.csv` 是用户自定义配置，源码不附带默认文件。

当前默认访问策略为 `direct_compat`。能力证据状态与访问权限分开处理，静态已识别不代表游戏行为已经实机验收。工具操作运行中进程；不会将单独的游戏 EXE 补成完整资源包。

## 测试

32 位环境：

```powershell
$env:QT_QPA_PLATFORM='offscreen'
python -B -m unittest discover -s tests -v
```

64 位环境可用 `requirements-dev.txt` 运行离线开发测试，但不能代替 32 位发布验收。测试中的静态样本不随源码分发；需要时设置包含“基底”目录的本地工作区：

```powershell
$env:BANSHOU_TEST_WORKSPACE='F:\path\to\workspace'
python -B -m unittest discover -s tests -v
```

没有样本时，对应用例跳过，FakeMemory、编解码与界面回归继续执行。测试使用离线文件和合成内存，不会启动游戏。

## 构建

使用 CPython 3.8.10 x86 和锁定的离线依赖。`build_release.py` 是实际构建入口，`build_release.ps1` 转交给它执行，因此不要求用户升级 Win7 默认的 PowerShell 才能构建。先在联网机器准备并审计 wheelhouse，再在发布环境执行：

```powershell
python build_release.py --prepare-wheelhouse
python build_release.py
```

Win7 的 PowerShell 2.0 可直接运行 `build_32bit.bat`；它只调用同一个 Python 构建入口。发布环境应使用官方 CPython 3.8.10 x86，不要用系统自带或 Conda 的其他版本。

默认构建目录版和单文件版：`dist-win7\onedir\6.6扳手\6.6扳手.exe` 与 `dist-win7\onefile\6.6扳手.exe`。目录版方便确认 DLL 和插件，适合作为首选发行形式；单文件版便于携带。用 `--format onedir` 或 `--format onefile` 只构建其中一种，`--dist <目录>` 更改输出根目录。产物附带 ZIP、`SHA256SUMS.txt` 和 `manifest.json`；每种形式的 DLL 导入审计、源码一致性与启动烟测 JSON 位于 `reports/<形式>/`。

脚本校验解释器版本与位数、锁定依赖、语法、单元测试、PE32/x86、Qt/pywin32/Capstone 运行库和嵌入源码一致性，并运行扳手自身的启动烟测。`--skip-smoke`（兼容 `-SkipSmoke`）跳过烟测，此时不能将构建结果记为启动通过。`--skip-install` 跳过安装，但仍核对版本；`--wheelhouse <目录>` 指定离线轮子目录。构建不会启动游戏。临时文件默认位于 `build-temp-win7/`，可用 `BANSHOU_BUILD_TEMP` 调整。

## 当前单 EXE 适配

2026-10-31 适配样本 SHA256 为 `719F710687CAE4BAEC2B3A4B028B473EA8AEB334A7ACFA6A692B46F207B5CDF8`，属于 6.6 派生引擎。支持其 16 字节 4+2 天赋、80 行五槽必杀、NOP 重算变体，以及 ID `256..511` 对应 `0x00750000` 的扩展消耗品数量区。

物品名称缺失时可从已定位的运行时目录只读加载；消耗品和必杀名称不可得时显示编号。扩展消耗品 ID 独立于装备/仓库的一字节字段。该样本本轮仅完成静态分析与模拟测试，完整游戏行为仍待实机验收。

详细变化见 [发布说明](docs/release-notes.md)，诊断命令见 [证据工具指南](docs/evidence-guide.md)。

## Windows 7 支持目标与验收边界

目标系统为 **Windows 7 SP1**（x86 或 x64）、Windows 8.1、Windows 10 和 Windows 11；x64 系统通过 WOW64 运行 32 位扳手和游戏。Win7 RTM、Vista 和 64 位目标游戏不在目标范围内。目标进程为高完整性时，扳手也需要以管理员身份运行。这是兼容性目标，当前代码测试和现代 Windows 上的启动检查不能证明 Win7 实机已通过。

Python 3.8 是最后支持 Win7 的 CPython 版本线，3.8.10 是该版本线最后提供官方 Windows 二进制安装程序的版本。Python 3.9 及以上的运行时不能用于 Win7 发行版。发布依赖及 wheel 的哈希在 `requirements-win7-x86.txt` 和 wheelhouse 清单中锁定；终端用户使用打包产物，无需另装 Python。

干净系统可能仍需要 Win7 兼容的 VC++ x86 运行库与 Universal CRT。当前启动探测记录 Windows/Python 版本、进程位数和警告；它不检查已安装的 SHA-2/UCRT 补丁，也不自动安装系统更新。若依赖 DLL 在 Python 或 Qt 载入前就失败，程序内报告无法代替 Windows 加载器诊断。构建阶段检查打包的 Qt 平台插件、pywin32 和 Capstone，并按解释器实际布局收集可用的附加 DLL；官方 CPython 与 Conda 的 DLL 布局不同，不把 Conda 的 `Library\bin` 作为唯一来源。

发布版的用户数据目录为 `%LOCALAPPDATA%\CaoCaoZhuan\Banshou`，缺少 `LOCALAPPDATA` 时回退到 `APPDATA`，两者都不可用时使用当前用户主目录的 `AppData\Local`；主目录也无法确定时，使用临时目录的 `CaoCaoZhuan\Banshou`。启动日志和兼容性 JSON 位于用户数据目录的 `logs` 下。已有 `DIY.csv` 只在目标不存在时复制，保留原文件和用户新文件；读取可回退旧位置。显式 `--diagnostic-output` 仍写入用户指定路径。中文资源路径使用 Unicode API 和 `LoadImageW`；Qt 插件路径从 Python 的实际 PyQt5 包目录查找，避免 Win32 Qt 的 `QLibraryInfo` 将中文转换为问号。

启动探测接口为 `collect_compatibility_report(check_native=False, strict=None)` 和 `format_compatibility_errors(report)`。发行包会严格检查 Win7 SP1、CPython 3.8.10 和 32 位解释器，并尝试加载 Qt、pywin32、psutil、Capstone 等依赖；错误写入日志和 JSON，Qt 导入失败后仍可使用原生消息框提示。进程附加继续使用现有 PE32/x86 识别与权限检查；`GetProcessMitigationPolicy` 等可选诊断 API 缺失或失败时只记录结果，不阻断 Win7 的普通读写流程。Python 启动前的加载器错误无法由这些代码捕获。

可用下面的命令验证程序窗口与资源成功初始化。它运行 Qt 事件循环并生成 JSON 后正常退出，不附加游戏；报告或退出码不成功都不能计作通过：

```powershell
& '.\6.6扳手.exe' --startup-smoke-output '.\startup-smoke.json' --startup-smoke-exit
```

Win7 发布验收至少覆盖：Win7 SP1 x86、Win7 SP1 x64+WOW64、Windows 8.1、Windows 10/11；普通用户和管理员；中文安装路径；没有 Python 的干净机；缺 VC/UCRT、缺 Qt DLL、目标未运行、64 位目标和拒绝访问等负例。每个平台执行启动、准星选窗、八页只读诊断、读写复读/失败回滚、远程调用、解绑和重新附加测试。只读诊断通过不代表所有引擎版本的写入和战场行为已经验收。
