# Modifier-for-CaoCaoZhuan

## 6.6 适配说明

本目录仍是原作者的 Python/PyQt5 旧扳手工程。窗口保持 `700×530`、八个原页面、原对象名、原控件几何和准星拖拽方式，不迁移到其它编辑器，也不修改磁盘上的游戏 EXE 或资源文件。

自动模式按准星取得的 PID 枚举真实主模块，支持主程序改名，不再要求文件名为 `Ekd5.exe`。附加前必须通过 PE32/x86、人物索引和战场索引两组核心指纹；版本资源、文件名和 SHA256 只用于候选排序与派生版命名。每项功能分别用唯一机器码、地址操作数、运行时指针、页权限和表语义组合画像，单项失配不会拖累其它页面。

完整能力与未闭环项目见 [CAPABILITY_MATRIX.md](CAPABILITY_MATRIX.md)，自动测试和实机待测项见 [ACCEPTANCE_66.md](ACCEPTANCE_66.md)，证据采集步骤见 [EVIDENCE_GUIDE.md](EVIDENCE_GUIDE.md)。

证据状态 `Unavailable/StaticVerified/RuntimeVerified/Enabled` 与访问级别 `Unavailable/ReadOnly/ReadWrite` 已彻底分开。当前 Python 扳手默认使用 `access_policy=direct_compat`：画像中有明确地址、表布局或版本配方的功能可以执行，证据状态、运行时探针和远程 ABI 只作为风险诊断，不再单独阻断读写或远程调用。旧配置中的 `force_unverified_66` 仅作为 `direct_compat` 兼容别名，不会恢复第二套隐式绕过逻辑。

### 直接兼容执行与战场复活

6.5 和 6.6 的战场复活入口按当前画像解析：标准 6.5 为 `0x004092C7`，标准 6.6 优先采用静态定位结果，当前基底回退为 `0x004092C0`。6.6 入口的 `abi_verified` 仍可能为假，但 `hook.py` 会保留 PE32 地址、wrapper、参数宽度、远程代码写后复读、页保护切换、线程等待和资源回收，并把调用写入画像的结构化兼容调用记录；ABI 未完成验证只显示警告。

手动复活要求槽位非空、Data ID 不是 `0xFFFF`、目标处于 `HP=0/显示状态=3`，方向必须为 `0..3`，坐标必须为 `0..255`。远程调用后会复读同一槽位，确认 Data ID 没有变化且 HP 或显示状态已经恢复。自动复活保留旧版“只处理我军”的范围，并以 `(槽位, Data ID)` 做短期去重；进程退出、解绑、重新附加、Data ID 变化或远程调用失败都会清空/停止自动状态。

直接兼容执行不等于实机验证通过。当前仍需在标准 6.5 和 6.6 测试副本完成连续复活、坐标/方向、战场切换、重新选中单位和异常恢复矩阵；坐标刷新 helper 没有可用 ABI，友军/敌军自动复活也未在本轮默认开启。

### 6.6 天赋分配布局

天赋主表同时识别三种已证明布局：旧版固定 8 字节、厚黑的 `DWORD[0x00500C3B] -> 0x00507800` 指针型 8 字节 3+1，以及标准 6.6/V7.16/烈焰的指针型 16 字节 4+2。16 字节布局中角色 ID `0..1023` 有效，所有 `>=1024` 且经查询语义证明不匹配人物的值均作为不透明空值显示为“空”；烈焰的 `1024..1278` 和 V7.16 的 `1024/1025` 都会逐槽保留原始字节。

物品元数据也按画像区分：标准 6.6 使用 15 字节名称和重排后的 25 字节行；标准 6.5 与厚黑使用 17 字节名称的旧 25 字节行。两者共同的 `row+0x18` 是宝物图鉴，但 `row+0x0F..0x14` 不能混读。标准 6.6、V7.16、烈焰和清儿使用 6.6 行；厚黑与标准 6.5 使用 6.5 行。codec 由物品索引函数提取的运行时表基址独立选择，不从消耗品 150/160 边界推断；签名不可见时禁用物品元数据，不回退固定地址。

专属/套装与天赋主表共同使用快照事务。下拉框无法表示的高位角色/物品只显示“空”，修改其它字段时原样写回；仓库槽内超出当前装备域的物品 ID 也采用同一不透明值规则。

天赋 UI 根据实际 `TableLayoutSpec` 在 3+1 与 4+2 间切换，不再根据版本整数切换。保存基于读取时的完整原始行快照，只覆盖已知字段；保存前比较当前字节并重新确认指针，冲突时要求刷新，部分写入时恢复原始行。`0x00507800` 只在机器码明确证明 `effectId×8` 且通过 `DWORD[0x500C3B]` 引用的混合引擎中使用，不能作为标准 6.6 固定地址。

### 环境与运行

推荐 32 位 Python 3.8-3.11。目标游戏和发布用 Python 都必须是 32 位。

```powershell
py -3 -m pip install -r requirements.txt
py -3 main.py
```

本机开发和 UI 测试使用已有 Conda 环境：

```powershell
conda activate banshou
python -m pip install -r requirements-dev.txt
python -m unittest discover -s tests -v
```

`banshou` 为 64 位时，证据采集器会通过 WOW64 API 读取 32 位目标线程上下文；正式发布仍优先使用 32 位解释器。

推荐的 Release 打包方式：

```powershell
conda activate banshou32
powershell -ExecutionPolicy Bypass -File .\build_release.ps1
```

`banshou32` 必须是 32 位 Python 3.8-3.11。脚本会安装锁定的开发/Release 依赖、拒绝 64 位解释器、运行语法编译和单元测试，然后使用 `banshou66.spec` 生成 `dist\6.6扳手.exe` 和对应 `.sha256`。spec 会从当前 Conda 环境的 `Library\bin` 显式收集 `_ctypes/_lzma/_bz2` 所需的 `ffi.dll/liblzma.dll/libbz2.dll`，避免 PyInstaller 5.13 漏收 Conda 运行库。构建完成后脚本会启动产物并拒绝任何 `Unhandled exception` 或 `Failed to execute script` 窗口。构建临时文件、pip 缓存和 PyInstaller 缓存默认写入源码目录的 `build-temp`，不依赖系统盘剩余空间；需要改到其它磁盘时先设置 `$env:BANSHOU_BUILD_TEMP='F:\banshou-build-temp'`。原 `build_32bit.bat` 继续保留为兼容入口，但新脚本和 spec 不依赖 CMD 中文代码页。`DIY.csv` 仍是可选用户文件；源码没有提供默认内容，因此不会自动生成。

PyInstaller 5.13 仍依赖 `pkg_resources`，因此 `requirements.txt` 将 setuptools 限制为 `<81`。常规开发环境由 `requirements-dev.txt` 使用 Capstone 5.0.3；32 位 Windows 没有该版本的预编译 wheel，`requirements-dev-32.txt` 因此固定使用 API 兼容的 Capstone 4.0.2。正式扳手不打包仅供开发使用的 Capstone。

### 安全边界

- 进程以查询、读、写和内存操作的最小常用权限附加；仅远程调用期间临时申请创建线程权限。
- 1/2/4 字节读写使用独立的零初始化宽度，所有写入检查范围、API 字节数和写后复读。
- 代码补丁先匹配原字节或本工具补丁字节，多站点失败会回滚；关闭时只恢复本工具实际取得所有权的补丁。
- 动态 stub 先以 RW 页写入和复读，再切换为 RX 并刷新指令缓存；释放前暂停目标线程并确认没有 EIP 落在 stub 内。外部改码时不恢复调用点、不释放可能仍被引用的 stub。
- 远程 wrapper 超时会把当前 `MemorySession` 标记为熔断，保留可能仍在执行的远程代码/参数分配并关闭线程句柄；同一会话后续远程调用会被拒绝，必须解绑并重新附加目标进程。
- 证据 JSON 同时校验内容摘要、原始样本摘要和目录清单摘要；两个指定基底必须提供同一配方，通用采集结果不能靠人工布尔值升级。
- 进程重新附加、退出和窗口关闭时释放句柄；锁血、锁蓝和自动复活由 UI 线程的 1 秒 `QTimer` 执行。
- 兼容读写失败会把具体地址、宽度和错误显示到原状态标签，不再静默表现为全零数据。
- “不分敌我”使用 6.6 的四个完整分支站点，转向使用 `0x4575B8` 的六参数 ABI；不再写 `0x4242B0` 或调用旧 `0x457428`。
- 自动回归复用 `0x421175` 原生二次行动特技，并事务 NOP `0x412D82/0x43D529/0x43D540` 三个限制分支；不修改旧 A/B CALL，也不会写旧代码洞 `0x485E00`。6.6 布尔变量使用 `0x492FC8..0x4931C7` 的 512 字节 MSB-first bitset。
- 版本选择独立显示 `自动、6.6、6.5、6.4、6.3、6.2、6.1`；自动检测不会再切换到手动选项。6.4 特效为固定 `0x508998` 的 8B/3+1，6.5 为 `DWORD[0x500C3B]` 指针型 8B/3+1，标准 6.6 为指针型 16B/4+2，厚黑由机器码识别为指针型 8B/3+1。
- 消耗品按独立语义配方选择：标准 6.6 为 `0x493AF0/160..199` 共 40 项；6.4、标准 6.5 和厚黑为 `0x510C80/150..254` 共 105 项。6.5 PL 的减 ID 语义未证明时保持不可写，不回退固定地址。
- 战场保存不调用旧 UI 从未使用的坐标和天气 helper；方向变化仍按原按钮行为即时刷新。

### 代码补丁页保护与语义锚点

- 控制页补丁使用独立的代码事务。补丁前逐页记录 `State/Protect/Type`，即使目标页为 `PAGE_EXECUTE_WRITECOPY` 也会临时切换到 `PAGE_EXECUTE_READWRITE`；写入、复读和指令缓存刷新完成后恢复每页原保护。
- 多站点任一写入失败会在页仍可写时恢复全部原字节。页保护恢复失败时回滚补丁、锁定本会话后续代码修改并要求重启游戏；数据表写入不进入 RWX 路径。
- 特效和人物表使用初始化、读取、写入三类 x86 语义锚点生成 `ResolvedTableDescriptor`。表起止由指针、记录数和步长共同确定；`1024/1025/0xFFFF/255` 只用于字段语义，不作为表分隔符。
- 清儿、新改、V7.16、烈焰解析为指针型 `16B/4+2` 特效表，厚黑解析为指针型 `8B/3+1`；五个基准的人物表均解析为 `1024×0x48`。正式 Release 包含 Capstone，用于规范化 x86 指令指纹。

### 外部依据

- 原作者源码：https://github.com/Puluomiyuhun/Modifier-for-CaoCaoZhuan
- 6.3/6.4 系统笔记：https://xycq.org.cn/forum/thread-309777-1-1.html
- 6.5 发布帖：https://xycq.org.cn/forum/thread-309778-1-1.html
- 6.6 修正版发布帖：https://www.xycq.org.cn/forum/thread-310256-1-1.html

考虑到曹操传基本只有国人玩，所以readme文档我就用中文写了。<br>
这是一个曹操传的通用调试器的源码，里面实现了游戏的各种修改项，包括逻辑控制、角色属性、战场信息、仓库道具、人物天赋、必杀分配、变量修改、自定义修改项等功能。<br>
发布帖请见：http://www.xycq.online/forum/viewthread.php?tid=310288&extra=page%3D1<br>
修改器全部由python完成，界面由pyQt5完成，简单介绍下各个文件的功用：<br>
untitled.ui：这个文件是ui界面的文件，可以有qt designer打开，可以通过"pyuic5 -o untitled.py untitled.ui"指令将ui文件编译为python格式；<br>
main.py：这个文件是外挂的入口，主要用来呼出窗体；<br>
mywindow.py：这个文件是外挂的核心程序，里面包含了窗体的构造、用户输入消息和槽以及外挂的核心功能实现；<br>
hook.py：这个文件是用来做线程注入的，原理是将要执行的代码和栈信息注入到游戏本体，再创建一个新线程去执行该代码。<br>
<br>
版本信息：<br>
2022.5.21<br>
1、复刻旧扳手全部功能<br>
<br>
2022.5.23<br>
1、将天赋做成独立页面，加入专属、套装修改功能<br>
2、加入变量监控页面<br>
3、加入自定义修改页面<br>
<br>
2022.6.3<br>
1、加入必杀修改页面<br>
<br>
2022.6.26<br>
1、修复6.2自动回归闪退的bug<br>
<br>
2022.7.1<br>
1、合并6.3mp+和6.3，扳手自行判断是否扩展过mp上限<br>
<br>
v0.11   2022.7.3<br>
1、修复“一键全宝”会得到普通装备的bug<br>
2、设定“一键全宝”可以根据等级、经验输入框的数值，统一所有获得宝物的等级、经验<br>
3、修复“清空仓库”不能清零道具的bug<br>
<br>
v0.12   2022.7.4<br>
1、修复“人物”页面只要点保存就会变成我军的bug<br>
<br>
v0.13   2022.7.13<br>
1、修复变量范围错误的bug<br>
2、开放变量保存功能<br>
<br>
v0.14   2022.8.16<br>
1、修复天气显示、修改错误的bug<br>
<br>
v0.15   2022.9.10<br>
1、托管时可控友军可一并托管<br>
2、R场景手动单挑时只保存hp、mp，避免R剧情无限循环<br>
3、修正了扳手buff一回合就掉的问题<br>
4、新增功能：自动复活<br>
5、“全灭敌军”更改为“全灭范围内敌军”，可手动设置全灭范围<br>
6、新增扳手智能判断版本功能<br>
<br>
v0.16  2023.4.7<br>
1、不良状态扩展的适配<br>
<br>
<img src="http://www.xycq.online/forum/attachments/forumid_76/20220701_6f772e487acc4f7d1e04Bd9liJiml8Ny.png" width = "500"><br>
<img src="http://www.xycq.online/forum/attachments/forumid_76/20220701_082a5692646123490ea8alJAfMCcYAn4.png" width = "500"><br>
<img src="http://www.xycq.online/forum/attachments/forumid_76/20220701_71318ea037f7b5979ac3k3UykS7X0hpa.png" width = "500"><br>
<img src="http://www.xycq.online/forum/attachments/forumid_76/20220701_253b26a22083acc82497sYeBud1lQygA.png" width = "500"><br>
<img src="http://www.xycq.online/forum/attachments/forumid_76/20220701_e384f269358e65ed1a94Zvin44nZOlRp.png" width = "500"><br>
<img src="http://www.xycq.online/forum/attachments/forumid_76/20220701_7057966f420d4903ce19etfXCM8ASVBI.png" width = "500"><br>
<img src="http://www.xycq.online/forum/attachments/forumid_76/20220701_b6cb750d1b0c320b7c8aYFIj0rJpUYwR.png" width = "500"><br>
<img src="http://www.xycq.online/forum/attachments/forumid_76/20220701_6de0e07619753bdd2f3asRAg4sB86hIl.png" width = "500"><br>
