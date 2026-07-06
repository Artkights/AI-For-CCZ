﻿# 03 人物形象 / R/S / B形象指定器

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

#### 概述

#### B形象指定器（旧工具）运行逻辑（已提取为本地证据）

目标：把“老版游戏制作工具/B形象指定器内工具”的运行逻辑沉淀成可核对的证据，供后续代码/表格校验用。

工具版本与位置（本仓库）：

- 6.5：`老版游戏制作工具\\B形象指定器\\形象指定器6.5\\形象指定器65.exe`（FileVersion/ProductVersion=`1.00.0065`，Company=`Star175`）
- 6.6x：`老版游戏制作工具\\B形象指定器\\6.6x形象指定器\\形象指定器66x.exe`（FileVersion/ProductVersion=`1.00.0066`，Company=`Star175`）

##### 1) 启动依赖（同目录放置）

从程序字符串与目录内容可确认该工具为 VB6 程序，并会加载/依赖：

- `MSFLXGRD.OCX`（表格控件）
- `zlib.dll`（解压缩相关）
- `Jpg.dll`（图像导出/解码相关；仓库提供位置：`B形象指定器\\65EXE\\Jpg.dll`、`B形象指定器\\6.6xEXE\\Jpg.dll`）

工具内置的“缺少文件”提示（意味着它会强依赖/强校验这些文件存在）：

- `缺少ekd5.exe文件!`
- `缺少Data.e5文件!`
- `缺少文件Imsg.e5!`
- `缺少场景地形文件(Pmap.e5)！`
- `缺少Mark配置文件！`

##### 2) 它识别/要求的“相关文件集合”

该工具“打开文件”过滤器明确列出它认知的相关文件类型（顺序照搬其文本）：

`Ekd5.exe` / `Person.e5` / `Sv??b.E5S` / `Data.e5` / `Star.e5`

此外它还会直接引用以下资源文件名（来自 exe 内置文本）：

- `Unit_atk.e5`、`Unit_mov.e5`、`Unit_spc.e5`
- `Pmapobj.e5`
- `E5\\Face.e5`（相对路径）
- `Pmap.e5`（场景地形文件；注意不是 `Pmapobj.e5`）
- `Imsg.e5`
- `Mark.DEC`
- `Source\\AddPmap.e5`（exe 文本写的是 `AddPmap.e5`；仓库内文件名为 `addPmap.e5`）

##### 3) `System.ini` 的角色：偏移/计数/最近目录配置

6.5 与 6.6x 的 `System.ini` 都包含（或扩展包含）多组关键字段。6.5 版至少包含：

- `FileHead`：S 形象编号指定表在 `Ekd5.exe` 中的位置（原注释：请勿修改）
- `RFileHead`：R 形象编号指定表在 `Ekd5.exe` 中的位置（原注释：请勿修改）
- `UserPath1/2/3`、`UserBrowses/UserBrowsel`：文件对话框最近目录/默认目录
- `UserXK`：兵种相克数据在 `Ekd5.exe` 中的位置（原注释：请勿修改）
- `CountBZ3/CountBZ1/Three`：一转/三转/三转形象数量（用于界面与循环上限）
- `CountSV`：存档数量（原注释：如果自己增加了存档数，这里也可以改大）
- `SCount`：Star 中装备数量（注释口径）
- `SMagic`：法术数量（注释口径）
- `BzXG`：人物特效数量（注释口径）
- `DefID`、`AssID`：道具分类起始序号（用于道具界面分段/解释）

6.6x 在此基础上额外有一批“策略扩展”相关地址键（例如 `MgID/MgHit/MgHurt/...`），用于 6.4+ 策略扩展数据定位（见其 ini 中注释）。

##### 4) `FileHead/RFileHead` 的可验证含义：exe 文件偏移（十六进制）

已对照本项目现有表格定义（`CczRSX 6.5\\ConfigTable\\HexTable.xml`）：

- `RFileHead=E1000` 对应 `Ekd5.exe` 文件偏移 `E1000`（人物 R 形象编号指定表）
- `FileHead=D2800` 对应 `Ekd5.exe` 文件偏移 `D2800`（人物 S 形象编号指定表）

并且从样例 `Ekd5.exe` 读取 `E1000` 与 `D2800` 两段字节可见其数据形态为“低字节有效、高字节多为 00”的 `UInt16` 风格序列（工具内部很可能按 2 字节/条处理，数值范围在 0~250 一带）。

##### 5) `Mark.DEC` 的结构（已解读）

`Mark.DEC` 大小为 192 字节，可稳定解读为 12 条记录，每条 16 字节：

- `u32 offset`：位于 `Pmapobj.e5` 内的偏移（十六进制）
- `u16 width`：像素宽
- `u16 height`：像素高
- `u16 type`：当前样本均为 3（语义待对照 界面）
- `gbk name[6]`：GBK 编码短名称（不足 6 字节以 `00` 填充）

条目名称（从文件直接解码）：

`条状物`、`花色`、`小遮罩`、`大遮罩`、`小船`、`小兰船`、`大兰船`、`标志`、`标志动`、`气泡`、`经验`、`单挑框`

##### 6) 可见功能线索（来自 exe 内置文本）

- `R形象指定`、`S形象指定`
- `形象导出`（并提示 `选择图片保存目录`）
- `人物属性编辑`
- `道具属性编辑`（按“武器防具/辅助装备/消耗品”分段；与 `DefID/AssID` 对应）
- 兵种相关：相克/属性/特效/必杀
- 策略扩展：`扩充策略成功`、`Data已扩充过策略!`
- 存档批量更新：提示输入存档编号，`0` 表示全部
- 头像校验：`E5\\Face.e5` 与 `头像编号超过了Face.e5的图片数量`
- 场景地形校验：`Pmap.e5` 不是有效 LS 压缩文件时会报错

##### 7) 落地为工程约束（把旧工具逻辑变成可执行规则）

1. 人物 R/S 编号指定表：必须以 `System.ini` 的 `RFileHead/FileHead` 为准，并与 `HexTable.xml` 校验一致后才允许读写。
2. 禁止跨版本：`形象指定器65.exe` 的 ini 偏移只对 6.5 的 `Ekd5.exe` 有意义，6.6x 同理。
3. 若要对齐旧工具“标记块/导出”行为，优先实现 `Mark.DEC` 16 字节记录解析，并按 offset+尺寸对 `Pmapobj.e5` 解码做验证。
4. `Pmap.e5` 被旧工具视为“场景地形文件”并做 LS 格式校验；该事实对本项目地图/地形联动有参考价值（与人物 R/S 编号无直接关系）。

#### 已核实来源

文件：`B形象指定器\形象指定器6.5\System.ini`

关键配置：

```ini
[Main]
FileHead=D2800
RFileHead=E1000
CountBZ3=20
CountBZ1=20
Three=32
CountSV=900
UserPath1=F:\从0开始的AI制作曹操传MOD\曹操传加强版6.5（未加密）\加强版6.5未加密版\
UserPath2=F:\从0开始的AI制作曹操传MOD\曹操传加强版6.5（未加密）\加强版6.5未加密版\RS\
SCount=96
```

原注释要点：

- `FileHead` 是 exe 中 S 形象数据的位置，请勿修改。
- `RFileHead` 是 exe 中 R 形象数据的位置，请勿修改。
- `UserPath` 是最后一次修改的文件所在目录。
- `CountSV` 是存档数量；如果增加存档数，这里也可以改大。
- 使用时务必注意 exe 版本，不可跨版本使用。

#### 当前结论

1. 人物 R/S 指定数据不应凭界面表名待证假设，必须按 B形象指定器的 `System.ini` 校对。
2. 6.5 版本：
   - S 形象数据位置：`FileHead=D2800`。
   - R 形象数据位置：`RFileHead=E1000`。
3. 当前 6.5 样本已校对为 `Ekd5.exe` 文件偏移：`R=E1000`、`S=D2800`，每条 `RowSize=2`，与 B形象指定器 `System.ini` 一致。
4. 当前程序的 `ImageAssignmentService` 通过 `HexTableNameResolver.ResolveForProject` 定位 R/S 指定表；6.5 表继续校验 `Ekd5.exe:E1000/D2800`，6.6/缺表只作为只读语义兜底，不开放写入。
5. 右侧预览应显示人物真实 R/S 形象，而不是关卡或存档的 R/S 数据。

#### 2026-05-31 代码核实结论

已核对 `CczRSX 6.5\ConfigTable\HexTable.xml`：

- `6.5-0-4 R形象`
  - `FileName=Ekd5.exe`
  - `DataPos=921600`
  - 十六进制为 `E1000`
  - 与 B形象指定器 `RFileHead=E1000` 一致。
- `6.5-0-5 S形象`
  - `FileName=Ekd5.exe`
  - `DataPos=862208`
  - 十六进制为 `D2800`
  - 与 B形象指定器 `FileHead=D2800` 一致。

因此当前人物形象编号读取路径应固定为：

`Ekd5.exe -> E1000 R形象编号表 / D2800 S形象编号表 -> (仅得到人物使用的编号 n)`

注意：这只是人物使用哪个 R/S 资源的“编号指定表”。它不是 `.E5S` 存档信息，也不应被解释为 `RS\\R_XX.eex / RS\\S_XX.eex` 的人物图像资源。

人物图像资源本体（教程口径）：

- 头像：`E5\\Face.e5`（小头像）与 `Tou.dll`（真彩头像资源，Face图号+300，语言2052）。
- R 形象：`Pmapobj.e5`（编号 n -> 正面 2n+1、反面 2n+2，1-based 图号）。
- S 形象：`Unit_atk.e5 / Unit_mov.e5 / Unit_spc.e5`（S 编号是紧凑编号，需要先映射为 Unit 图号；`S=0` 用职业和阵营取默认兵种图，`S=1..32` 对应三转特殊三张图，`S>=33` 对应一转特殊单张图）。

`RS\\*.eex` 的 R/S eex 属于剧本/场景/战场资源（脚本/命令/文本线索），不等于人物 R/S 图像封包。

#### 2026-06-03 复查：编号表对齐，错配来自裸扫预览

本次复查结论：人物 R/S 编号读取本身是对齐的；“R/S 图像和人物设定不匹配”的来源是旧预览实现把 Ls12 封包当成裸图片流扫描。

证据：

- `CczRSX 6.5\ConfigTable\HexTable.xml` 仍确认：
  - `6.5-0 人物`：`Data.e5`，`DataPos=396`，`RowSize=32`，`RowCount=1024`。
  - `6.5-0-4 R形象`：`Ekd5.exe`，`DataPos=921600 (E1000)`，`RowSize=2`，`IndexTable=6.5-0 人物`。
  - `6.5-0-5 S形象`：`Ekd5.exe`，`DataPos=862208 (D2800)`，`RowSize=2`，`IndexTable=6.5-0 人物`。
- `B形象指定器\形象指定器6.5\System.ini` 同样记录 `RFileHead=E1000`、`FileHead=D2800`。
- 直接读取前 20 行：`曹操 R=0 S=1`、`夏侯惇 R=1 S=2`、`张辽 R=2 S=3`、`关羽 R=3 S=5`、`曹彰 R=4 S=37`、`曹仁 R=5 S=6`、`夏侯渊 R=6 S=11`、`张郃 R=7 S=97`、`曹丕 R=8 S=120`、`庞德 R=9 S=17`。R 编号与人物行自然顺排，说明不是人物表错位。
- 当前 6.5 项目里 S 表分布为：`min=0 max=314 nonzero=310 unique=165`；这些值是人物使用的 S 紧凑编号，必须先按默认兵种/三转特殊/一转特殊规则映射为 Unit 图号，不能按裸 BMP 出现顺序判断异常。
- `Pmapobj.e5`、`Unit_atk.e5`、`Unit_mov.e5`、`Unit_spc.e5`、`E5\Face.e5` 头部均为 `Ls12`；真实图片目录从文件绝对偏移 `110` 开始，每张图索引 12 字节，大端读取。

错误实现：

- 旧 R 预览：在 `Pmapobj.e5` 原始字节里扫描 JPEG 魔数，再按 `id * 2` 取出现顺序。
- 旧 S 预览：在 `Unit_atk/mov/spc.e5` 原始字节里扫描 BMP 魔数，再按 `id` 取出现顺序。
- 旧越界处理还会扩大错配：R 切片越界回退到第 0 张，S 切片越界夹到最后一张；当当前项目 S 编号最高到 314 时，很多人物会被显示成同一张错误候选图。
- 这个“出现顺序”不是 Ls12 目录条目顺序，也没有经过解压、条目边界和 S 动作/朝向/帧选择验证，因此会把正确编号显示成错误人物图像。

当前可靠的部分：

- R/S 编号来源：`Ekd5.exe @ E1000/D2800`。
- 资源文件定位：`Pmapobj.e5` / `Unit_*.e5` 是否存在、大小是否合理。
- `R=0` / `S=0` 是合法值：表示使用普通形象（与兵种/初始设定相关），不应被当作“错误/缺失”。

已执行修正：

- `ImageAssignmentPreviewService` 停止按“裸 BMP/JPEG 出现顺序 = 编号”的旧逻辑渲染 R/S 候选图。
- 2026-06-03 最新实现：按 E5 `110` 索引表读取真实条目。R=n 取 `Pmapobj.e5` 第 `2n+1` 张正面图；S 先按紧凑编号映射为 Unit 图号后，再从 `Unit_mov.e5 / Unit_atk.e5 / Unit_spc.e5` 读取；头像按 `Face.e5` 索引图号读取。
- 图片条目若为 BMP/JPG/PNG，直接用图片头解码；若首字节为 `00` 且长度符合固定帧条尺寸，则按原始索引帧条裁代表帧，并套用 `tsb` 256 色调色板预览。
- `CharacterImageResourceService`、诊断提示、导出说明同步改为“按 E5 索引表取图”的口径。

后续若要继续完善：

- 原始帧条调色板已确认来自 `老版游戏制作工具\普罗-综合工具v0.3\tsb`；该文件为 1024 字节，256 项，每项 4 字节，按 `B,G,R,Reserved` 解释。
- `Pmapobj.e5 / Unit_*.e5` 当前已开放单个图片索引条目替换；界面 与 MCP 都走同一套备份、复读和结构化报告逻辑。整体重排、批量动作帧替换和通用 Ls 重封包仍需继续确认。
- S 动作、朝向、帧序列的完整选择逻辑；当前预览只裁一个代表帧。

#### 2026-06-04 MCP/E5 写入边界

- MCP 新增 `list_e5_image_entries`、`preview_e5_image_replace`、`replace_e5_image_entry`，用于无头读取和替换 E5 图片资源中的单个 `110` 索引条目。
- `replace_e5_image_entry` 仅允许 `.e5` 图片载荷文件；`Data.e5`、`Imsg.e5`、`Star.e5`、`Hexzmap.e5` 等核心文件仍必须走表格、文本或 Hexzmap 专用写入，不能被图片替换工具覆盖。
- 替换来源可以是 BMP/JPG/PNG/RAW 条目文件，也可以是备份 E5 内的指定图号。用于从备份还原时，应显式传 `source_image_number`，避免把整份 E5 当作普通图片写入。
- 2026-06-04 SDK client 验证：当前 `tools/list` 返回 32 个 tools；`list_e5_image_entries Unit_mov.e5 limit=3` 可读到 `totalEntries=556`，前 3 条均为 RAW，证明 MCP 层已经能直接定位 Unit 图号。

#### 2026-06-05 6.6x 形象指定器本地解析

本地新增/复核 6.6x 文件：

- `老版游戏制作工具\B形象指定器\6.6xEXE\Ekd5 6.6.exe`
  - 大小：`1130496`
  - SHA256：`13152054F34BFA277C3170B0E596BE4A3CC483760D5F8471A7D9679BDBC8120A`
  - VersionInfo：FileVersion/ProductVersion=`1, 0, 0, 1`，Company=`Star175`
- `老版游戏制作工具\B形象指定器\6.6x形象指定器\形象指定器66x.exe`
  - 大小：`1519616`
  - SHA256：`BA12B7A0CF3BB2805B0DCB7F27BEE51669D3DC5AABCE965642A4C0C86A12C55A`
  - VersionInfo：FileVersion/ProductVersion=`1.00.0066`，Product=`形象指定器6.6修正版`，Company=`Star175`
- `6.6x形象指定器\Source\addPmap.e5` 与 `形象指定器6.5\Source\addPmap.e5` 大小同为 `20016`，SHA256 同为 `AE06D242051EFA39A2B1B6154553F59C1F816CE812BD78D43069A7A505DFB25D`。

6.6x `System.ini` 关键差异：

```ini
FileHead=D2800
RFileHead=E1000
CountBZ3=20
CountBZ1=20
Three=32
CountSV=900
SCount=368
MarkCount=32
SMagic=144
BzXG=254
DefID=70
AssID=109
MgMF=666848
MgID=666992
MgAIYN1=667136
MgAIYN2=667280
MgHit=667424
MgHurt=667712
MgHurtYN=667856
MgMeff=668000
MgMcall=668144
Mg8=667568
```

结论：

- 6.6x 的人物 R/S 指定表偏移仍与 6.5 一致：R=`E1000`，S=`D2800`，每项仍可按 UInt16 风格读取。
- 用 `Ekd5 6.6.exe` 直接读取：`E1000` 前 32 个 R 值为 `0,1,2,3,4,5,7,6,0...`；`D2800` 前 32 个 S 值均为 `0`。这说明 6.6x 引擎样本在 R/S 指定段存在可读数据，且 6.5 内置 R/S 表偏移可作为临时只读兜底。
- 6.6x 的 `SCount=368` 明显不同于 6.5 `SCount=96`，应理解为 6.6x 装备/Star 侧扩充计数口径；不能把 6.5 的物品/Star 写入边界直接套到 6.6x。
- 6.6x 增加的 `Mg*` 地址全部属于策略扩展数据定位线索，当前只归档为只读证据；后续如要开放 6.6 策略读取，应先用这些地址和 6.6 HexTable/样本交叉验证。
- 项目检测当前已改为：路径提示为 6.6/6.6x，或游戏目录 `Ekd5.exe` 大小为本地 6.6 样本 `1130496` 字节时，优先选择 `B形象指定器\6.6x形象指定器\System.ini`；路径提示或 `Ekd5.exe` 大小为 6.5 基准 `1196032` 字节时仍优先选择 `形象指定器6.5\System.ini`。

实现要求：

- `ImageAssignmentService` 必须按项目引擎 profile 解析 R/S 表名，并在读写前校验当前 6.5 R/S 表是否仍为 `Ekd5.exe:E1000/D2800`。
- `ImageAssignmentPreviewService` 必须读取 `B形象指定器\形象指定器6.5\System.ini`，展示 `FileHead/RFileHead/UserPath2`，作为界面校对证据。
- 人物形象预览应优先显示“人物头像 + R 形象定位 + S 形象定位”。其中：
  - 头像可从 `E5\\Face.e5` 做只读预览；
  - R 形象应按 `Pmapobj.e5` 的正/反图号定位（2n+1/2n+2），预览取正面图；
  - S 形象应先按紧凑编号规则解析为 Unit 图号，再读取 `Unit_atk/mov/spc.e5` 对应索引图；
  - 不得拿 `RS\\*.eex` 或裸魔数出现顺序做任何“人物帧预览”。

#### 2026-05-31 头像预览补充

- 人物表 `头像` 字段可作为创作者确认人物身份的可靠可视化入口。
- 当前已确认 `E5\Face.e5` 可按 `110` 索引表读取头像图片；不再靠 PNG 魔数出现顺序映射头像。
- 人物 R/S 界面右侧可以显示人物头像，例如曹操头像编号 `0` 可正常显示。
- 头像、R、S 预览均应走 E5 索引表；R/S 当前显示代表帧，不等于完整动作帧序列。

#### 2026-06-01 人物R/S预览界面收敛（2026-06-03 已修正）

历史方案曾把人物 R/S 形象页右侧预览区拆成三段：

- 头像预览（Face.e5）
- R 形象预览（Pmapobj.e5）
- S 形象预览（Unit_*.e5）

2026-06-03 复查后确认 R/S 裸扫候选图会错配真实人物图像，因此不能再按魔数出现顺序渲染候选图。当前预览边界为：头像、R、S 均按 E5 `110` 索引表读取；R 显示正面代表帧，S 显示移动/攻击/特技代表帧。

编号解释与路径信息保留在信息区（列表/详情）展示；完整 R/S 动作图像、动作帧序列和写回仍需继续确认。

#### 2026-06-03 E5 图片索引读取规则

用户补充的有效信息已验证并作为当前权威读取规则：

- 形象类 E5 文件的图片索引表从文件绝对偏移 `110` 开始。
- 每张图片索引占 12 字节，按大端读取：
  - 前 4 字节：图片总字节数。
  - 中间 4 字节：图片总字节数副本。
  - 后 4 字节：图片在当前 E5 文件内的起始偏移。
- 表项校验：两个 size 字段相等、size > 0、`offset + size <= fileLength`；遇到无效表项停止。
- 图片数据可能是 BMP、JPG、PNG，也可能是首字节 `00` 的原始索引帧条。BMP/JPG/PNG 通过图片头直接解码；原始帧条按文件类型使用固定宽度裁代表帧：
  - `Pmapobj.e5`：宽 48，代表帧高 64。
  - `Unit_atk.e5`：宽 64，代表帧高 64。
  - `Unit_mov.e5`：宽 48，代表帧高 48。
  - `Unit_spc.e5`：宽 48，代表帧高 48。
- 标准图像帧中的洋红底色用于透明背景；JPG 会产生压缩近似色，预览实现应按洋红键阈值剔除，不应把洋红底误认为真实人物图像内容。
- 原始帧条套用内置 `Assets\Palettes\tsb` 256 色调色板；运行时找不到内置调色板时，回退查找老版普罗工具目录下的 `tsb`，仍找不到才退回灰度预览。

人物编号映射：

- R 编号来自 `Ekd5.exe:E1000`：`R=n` 对应 `Pmapobj.e5` 图号 `2n+1`（正面）和 `2n+2`（反面），图号为 1-based；预览取正面代表帧。
- S 编号来自 `Ekd5.exe:D2800`：`S=0` 表示默认兵种形象，Unit 图号 = `职业编号 * 3 + 阵营槽`（我军=1、友军=2、敌军=3）；`S=1..32` 对应 Unit 图 `240+(S-1)*3+1/2/3`；`S>=33` 对应 Unit 图 `336+(S-32)`。预览分别显示移动、攻击、特技代表帧。
- 头像按 `Face.e5` 同样的 `110` 索引表读取；Data 头像号到 Face 图号仍沿用 `0 -> 1..8`、`n>=1 -> n+8` 的教程映射。

实现边界：

- `ImageAssignmentPreviewService.TryRenderCharacterResourceImage(project, "R", 0)` 应返回 `Pmapobj.e5 #1` 预览。
- `ImageAssignmentPreviewService.TryRenderCharacterResourceImage(project, "S", 1, null, 1)` 应返回 Unit 图 241/242/243 的三转特殊组合预览。
- `ImageAssignmentPreviewService.TryRenderCharacterResourceImage(project, "S", 250, null, 1)` 应返回 Unit 图 554 的一转特殊组合预览。

#### 2026-05-31 教程口径补充：头像、R 形象、S 形象的资源链

这一段来自用户提供的制作教程口径，用于修正“只看 RS\\*.eex”的过度简化。

##### 头像（Face.e5 / Tou.dll / Data 头像号）

字段来源：`Data.e5` 人物表字段 `头像`（Data 头像号）。

小头像：`E5\\Face.e5`

- Data 头像号 0 实际对应 Face.e5 的 1-8 号头像（原版曹操多表情遗留规则）。
- Data 头像号 1 对应 Face.e5 第 9 张；2 对应第 10 张，依此类推。
- 总结：当 Data 头像号 >= 1 时，`Face.e5 小头像号 = Data头像号 + 8`；当 Data 头像号=0 时使用 1-8 候选。

真彩头像：`Tou.dll`

- Tou.dll 真彩头像资源号 = Face.e5 小头像号 + 300
- 资源语言号 = 2052

当前项目实现边界：

- 工具当前只做 Face.e5 PNG 只读预览，并按上述映射解释 Data 头像号。
- 工具当前不对 Face.e5/Tou.dll 做重封包写入。

##### R 形象（Pmapobj.e5 / Data 的 R 剧本形象号）

教程规则：Pmapobj.e5 按“正反两张”成对存放。

- 若人物 R 形象号为 n：
  - 正面图 = 2n + 1
  - 反面图 = 2n + 2
- 上述图号为 RPGViewer 等工具中看到的 1-based 图片号。

当前项目实现边界：

- 工具当前对 R 形象编号做只读解释与资源定位（检查 Pmapobj.e5 是否存在并给出正/反图号）；预览按 `110` 索引表取正面图 `2n+1`，已停止按裸 JPEG 魔数扫描做可视化预览。
- 工具当前可对 `Pmapobj.e5` 的单个图片索引条目做替换写入：按 `110 + (图号-1)*12` 更新 size/size副本/offset，并写入 BMP/JPG/PNG 或备份 E5 中同图号条目字节；不做整体重排或通用重封包。

##### S 形象（Unit_*.e5 / 普通与特殊形象）

S 编号不是 Unit 直接图号，而是人物指定表里的紧凑编号。当前已确认的映射：

- `Unit_*.e5` 图号整体分段：`#1..#180` 为三转兵种普通形象，`#181..#240` 为一转兵种普通形象，`#241..#336` 为三转特殊形象，`#337` 起为一转特殊形象。
- `S=0`：默认兵种形象，Unit 图号 = `职业编号 * 3 + 阵营槽`；阵营槽为我军=1、友军=2、敌军=3，预览页提供阵营下拉框。
- `S=1..32`：三转特殊形象，每个 S 对应 3 张 Unit 图；`S=1 -> 241/242/243`，`S=32 -> 334/335/336`。
- `S>=33`：一转特殊形象，每个 S 对应 1 张 Unit 图；`S=33 -> 337`，`S=250 -> 554`，`S=252 -> 556`。
- 当前 Unit 三文件索引表均为 556 张；映射到 557 及以后时严格显示越界，不回退旧直读。

资源封包候选：`Unit_atk.e5`、`Unit_mov.e5`、`Unit_spc.e5`。

当前项目实现边界：

- 按上述紧凑编号规则解析 Unit 图号后，再读取 `Unit_atk.e5 / Unit_mov.e5 / Unit_spc.e5` 的对应索引图。
- 索引图可能是 BMP/JPG/PNG，也可能是原始帧条；原始帧条当前按固定尺寸裁代表帧，并套用 `tsb` 256 色调色板预览。
- 这不是恢复旧“裸扫出现顺序 = 全部 S 编号”的逻辑：必须按 `110` 索引表读取条目。
- S 预览图片本体只显示动作帧，不在图片内叠加 `移动#图号/攻击#图号/特技#图号` 等文字；图号映射和读取说明放在信息区、诊断输出或烟测输出中。
- `导入/替换E5` 只替换用户选择的具体 E5 文件与图号，例如 `Unit_mov.e5 #554`；若来源图片大于原条目，工具会把新图片追加到文件末尾并更新该图号索引，不移动其它图片。写入前备份目标 E5，写入后复读索引和条目字节并生成结构化报告。

#### 必须修正/校验的实现点

- 检查 `HexTable.xml` 中：
  - `6.5-0-4 R形象`
  - `6.5-0-5 S形象`
  是否指向 `Ekd5.exe` 且偏移与 `RFileHead=E1000`、`FileHead=D2800` 匹配。
- 检查 `Pmapobj.e5 / Unit_*.e5 / Face.e5` 的 `110` 索引表是否可读，表项是否满足 size 副本一致和 offset 范围校验。
- 人物形象保存必须沿用形象指定器逻辑：写入前备份 `Ekd5.exe`，写入后复读并和表格校验。

#### 待用户/工具补充

- 如果 B形象指定器有使用说明或源码，请加入本知识库。
- 需要用户确认：形象指定器界面中 R/S 编号是否从 0、1 还是其它基准开始显示。
- 需要样例：选择某个人物在 B形象指定器中修改 R/S 后，记录修改前后 exe 字节差异，用于验证偏移和字段宽度。

#### 2026-06-05 R 场景帧切分与预览口径

- R 形象编号仍按人物表 `6.5-0-4 R形象` 读取；`R=n` 映射到 `Pmapobj.e5` 的 1-based 图号 `2n+1` / `2n+2`，其中 `2n+1` 用作正面/常规预览入口。
- 当前 6.5 样本中，`Pmapobj.e5` 解出的 R 图像条带可按 `48x64` 帧切分；实测常见条带为 `48x1280`，即 20 帧。
- R 场景制作页的角色预览采用 `ImageAssignmentPreviewService.TryRenderRSceneFrameByIndex(...)`：先取 `Pmapobj.e5 #2n+1`，再把 20 帧全部列成缩略图，创作者可直接选择 `0..19` 的动作帧；`右` 方向仍使用所选帧水平翻转。
- 这是 R 场景编辑器的只读切帧预览规则，不等于 R 形象重封包规则；替换 `Pmapobj.e5` 单图仍必须走 E5 索引表写入、备份和复读校验。

#### 2026-06-07 人工智能 头像/R/S 草稿

- `人工智能绘图-图像工作室-模型上下文协议.md` 已新增 `face`、`r_actor`、`s_unit` 预设，用于从自然语言描述生成头像、R 形象和 S 形象草稿。
- `face` 默认输出 `120x120 png`；若显式指定目标 E5 图号，可读取目标条目实际尺寸作为输出尺寸。
- `r_actor` 按 `R=n -> Pmapobj.e5 #2n+1/#2n+2` 生成同一静态帧纵向复制的 `48x1280 png` 条带。
- `s_unit` 按 S 紧凑编号映射 Unit 图号，并生成移动 `48x528`、攻击 `64x768`、特技 `48x240` 三条静态草稿。
- 该 人工智能 流程只做导出和替换预览；完整动作帧、朝向、帧序和实机表现仍是待验证项，不能把 v1 静态条带当作完整形象制作器。
#### 2026-05-31 最高优先级校正：R/S 指定表不等于 R/S eex 人物图像

- `B形象指定器\形象指定器6.5\System.ini` 提供的可靠信息：`RFileHead=E1000`、`FileHead=D2800`，即 Ekd5.exe 中人物 R/S 编号指定表位置。
- 不能再把 `RS\R_XX.eex` / `RS\S_XX.eex` 当作人物 R/S 形象帧资源展示；当前应视为 R/S 剧本/战场主线资源。
- `Face.e5` 是 `Ls12` 资源；头像读取必须走 `110` 索引表，不得仅扫描 PNG 顺序。
- 待补充：R/S 完整动作帧序列、朝向选择和重封包写回方式。

#### 2026-06-03 复查结论：人物 R/S 读取方式再次固定

本轮按“本地旧工具 + 公开教程 + 当前代码”三路复查，结论如下：

1. 人物 R/S 编号读取来源仍是 `Ekd5.exe` 指定表：
   - R 编号表：`6.5-0-4 R形象`，`Ekd5.exe:E1000`，对应 `B形象指定器` 的 `RFileHead=E1000`。
   - S 编号表：`6.5-0-5 S形象`，`Ekd5.exe:D2800`，对应 `B形象指定器` 的 `FileHead=D2800`。
   - 5.8 以后教程口径也说明“R形象、S形象已改为两字节，并转移到 exe 中”，这与当前 6.5 偏移护栏一致。
2. 人物 R 图像本体不在 `RS\R_XX.eex`：
   - 当前规则固定为 `R 编号 n -> Pmapobj.e5 图 2n+1 / 2n+2`。
   - 这里的图号是 1-based 图号；代码按 `Pmapobj.e5` 的 `110` 索引表读取第 `2n+1` 张作为正面预览。
   - 公开问题汇总也把 `Pmapobj.e5` 列为“R形象”的位置。
3. 人物 S 图像本体不在 `RS\S_XX.eex`：
   - 当前规则固定为 `Unit_atk.e5 / Unit_mov.e5 / Unit_spc.e5` 三个封包候选。
   - S 编号需要先按紧凑编号规则映射为 Unit 图号，再读取三套 Unit 文件的 `110` 索引图。
   - 公开问题汇总把 `Unit_atk.e5`、`Unit_mov.e5`、`Unit_spc.e5` 分别列为 S 攻击、移动、特技形象位置。
4. `RS\R_*.eex / RS\S_*.eex` 的正确语义：
   - 属于 R/S 剧本、场景、战场 EEX 文件。
   - 可以用于剧本制作、战场制作、EEX 探针、文本写回和命令结构研究。
   - 不得作为人物 R/S 图像封包、人物帧预览或人物图像整文件替换目标。

##### 本轮代码落实

- `EexArchiveReader`：`RS\R_*.eex/S_*.eex` 分类改为 `R剧本EEX/S剧本EEX`，注释明确“不是人物图像封包”。
- 图片处理和 EEX 探针：R/S eex 的缺号、命名、魔数核对改用 `R剧本EEX/S剧本EEX`，不再使用 `R形象/S形象`。
- `R剧本EEX/S剧本EEX` 核对不再因文件名里有 `R_` / `S_` 而误跳到人物 R/S 页。
- 删除旧的 `ImageResourceReplaceService`：停止维护“把 RS EEX 当人物 R/S 资源整文件替换”的错误链路。
- `ImageAssignmentPreviewService`：头像/R/S 预览改为读取 E5 `110` 索引表；BMP/JPG/PNG 直接解码，原始帧条按固定尺寸裁代表帧并套用 `tsb` 256 色调色板。
- `ExportMissingImageResourceReport` 与 README/烟测同步改为 `Pmapobj.e5 / Unit_*.e5` 索引表口径。

##### 联网复核来源

- 轩辕春秋 `宝物修改教程、人物R、S形象 头像修改教程、data详解、e5文件位置介绍、战场动画形象位置介绍`：记录 `Pmapobj.e5` 是 R 形象，`Unit_atk.e5 / Unit_mov.e5 / Unit_spc.e5` 是 S 攻击/移动/特技形象。链接：https://www.xycq.org.cn/forum/viewthread.php?page=1&showoldetails=yes&tid=140085
- 轩辕春秋 `5.8 引擎跟新说明`：记录 R 形象、S 形象改为两字节并转移到 exe。链接：https://www.xycq.org.cn/forum/viewthread.php?highlight=&tid=215919
- 轩辕春秋 `5.6修改教程`：记录 R 形象在 `Pmapobj.e5` 中按正反两张成对计算，`R形象号 n -> 正面 2n+1，反面 2n+2`。链接：https://www.xycq.org.cn/forum/redirect.php?fid=18&goto=nextoldset&tid=140085

#### 2026-06-08 全局设定入口与写回边界

目标：把旧 B 形象指定器里的“全局设定”能力迁入 CCZModStudio，并保持 6.5 写回护栏。

已落地功能：

- `核心创作/角色设定` 卡片新增 `全局设定` 入口；`角色设定` 页工具栏同样新增入口。
- 新增 `GlobalSettingsDialog`，不复刻旧 VB6 布局，而是按 `全局参数 / 兵种名 / 职业名 / 游戏标题 / 证据` 分页。
- `兵种名修改`：
  - 使用 `6.5-3 兵种系`。
  - 目标：`Ekd5.exe @ 8B010`，`40 x 9B`。
  - 证据：`HexTable.xml`、本地样本、现有兵种系写回 smoke。
- `职业名修改`：
  - 使用 `6.5-4 详细兵种`。
  - 目标：`Ekd5.exe @ D18D0`，`80 x 9B`。
  - 证据：`HexTable.xml`、本地样本、现有详细兵种写回 smoke。
- `游戏标题修改`：
  - 当前 6.5 样本 `Ekd5.exe` 中 GBK 文本 `【三国志曹操传 加强版】` 位于 `8D3C4`。
  - 当前工具按 `32B` 定长 GBK 区读取/写回，保存前校验容量，保存后复读。
- 写回统一生成自动备份和结构化 JSON 报告。
- 新增 `--global-settings-write-smoke`：在测试副本中验证兵种名、职业名、游戏标题三项保存、备份、报告和复读。

2026-06-08 撤回核实：全局参数数字项回调为不可读取/不可写。

原因：此前把联网地址线索和本地机器码上下文过早解释为旧全局设定字段，证据等级不足。当前结论恢复为：旧窗口左侧全局数值项只能作为功能清单展示，不能读取当前值，不能保存写回。

暂不开放写回的旧窗口左侧全局参数：

- 能力显示（单数/双数）
- 能力条件
- 转职等级
- 等级上限 / 升级经验
- 普装/特装升级经验
- 普装/特装等级上限
- 新加/敌武将功勋
- 普装/特装提升等级
- 中级装备出现等级

当前依据：旧 B 形象指定器 6.5 当前只有 exe、`System.ini` 和资源文件，没有源码；`System.ini` 只提供 `FileHead/RFileHead/UserXK/BzXG/SMagic/SCount/DefID/AssID` 等配置，不包含这些全局参数偏移。联网资料可作为旧功能线索，但不能直接当 6.5 写回证据。

后续若要补齐左侧全局参数写回，需要至少满足一项证据：

- 从旧形象指定器 exe 逆向出对应偏移、长度、编码和取值范围。
- 用旧工具修改单一字段后，对比 `Ekd5.exe/Data.e5/Imsg.e5/Star.e5` 字节差异，并用多字段、多样本排除误判。
- 在测试副本中完成保存、复读、实机或调试器验证。

当前 smoke：

```text
GLOBAL_SETTINGS_WRITE_SMOKE_OK series=君主->烟测全局 detailed=群雄->烟测职业 title=【三国志曹操传 加强版】->全局设定烟测
```

#### 2026-07-04 MCP 官方形象指定器 Oracle 接入

本轮把 `B形象指定器` 从知识库参考升级为 MCP 可调用的官方修改器判定标准。它不替代 CCZModStudio 写入器，而是作为 referee 校验当前工具是否写到了官方工具认可的位置。

已接入 MCP 工具：

- `detect_image_assigner_oracle`：定位 `形象指定器65.exe` / `形象指定器66x.exe`、`System.ini`、依赖文件和版本兼容状态。
- `read_image_assigner_oracle_config`：只读返回 `FileHead/RFileHead/UserXK/DefID/AssID/SMagic/SCount` 等键值。
- `compare_image_assigner_oracle`：对比当前 HexTable 与官方配置，重点校验 `RFileHead=E1000`、`FileHead=D2800`、`UserXK=A3280`、`DefID=70`、`AssID=109`、`SMagic=144`。
- `plan_image_assigner_validation`：为 R/S、头像或全局参数实验生成测试副本验证计划。
- `run_image_assigner_oracle_smoke`：默认 `static` 只做配置/依赖/表定义对照；`launch_only` 和 `ui_probe` 只读启动，不执行保存。
- `compare_image_assigner_output`：比较 `before / official_after / ccz_after` 三个测试副本目录的核心文件字节差异。

新的自动化判定口径：

- `detect_project` 返回 `ImageAssignerOracle` 摘要。
- `read_table/write_table_rows` 对 R/S 形象表返回 `OracleStatus`：`MatchedOfficialImageAssigner`、`ConfigMissing`、`OffsetMismatch` 或 `CrossVersionOracle`。
- R/S 写入仍走 CCZModStudio 的备份、报告、复读链路；官方工具只作为验证标准。
- 左侧全局数字参数继续标为 `NeedsUiOrDiffExtraction`。只有通过官方工具输出 diff、地址分类、CCZ 测试副本写回、复读和运行时验证后，才可升级为可写设置。

#### 2026-07-04 官方 Oracle 写入实验结果

本轮新增 `run_image_assigner_assignment_experiment` / `--image-assigner-oracle-experiment-smoke`，用于自动化验证“官方形象指定器配置偏移”和“CCZModStudio HexTable 写入偏移”是否一致。实验只写入自动生成的测试副本，不修改正式基底。

实验方法：

- 生成三份测试副本：`before`、`official_case`、`ccz_case`。
- `official_case` 不驱动旧 VB6 界面保存，而是严格按形象指定器 `System.ini` 中的 `RFileHead/FileHead` 计算官方 oracle 偏移并写入 UInt16。
- `ccz_case` 通过 CCZModStudio `HexTableWriter` 写入同一人物行、同一新值。
- 比较 `before -> official_case` 与 `before -> ccz_case` 的 `Ekd5.exe` 变更偏移、变更字节和 CCZ 复读结果。

实测结果：

```text
R 行 0：official offset=0E1000，ccz offset=0E1000，原值 0 -> 新值 1，字节一致，复读一致。
S 行 0：official offset=0D2800，ccz offset=0D2800，原值 1 -> 新值 2，字节一致，复读一致。
IMAGE_ASSIGNER_ORACLE_EXPERIMENT_SMOKE_OK
```

本次测试副本路径：

```text
R before   = CCZModStudio_TestCopies\ImageAssignerOracle_r_image_assignment_0_20260704_215037_087\before
R official = CCZModStudio_TestCopies\ImageAssignerOracle_r_image_assignment_0_20260704_215037_087\official_case
R ccz      = CCZModStudio_TestCopies\ImageAssignerOracle_r_image_assignment_0_20260704_215037_087\ccz_case
S before   = CCZModStudio_TestCopies\ImageAssignerOracle_s_image_assignment_0_20260704_215041_771\before
S official = CCZModStudio_TestCopies\ImageAssignerOracle_s_image_assignment_0_20260704_215041_771\official_case
S ccz      = CCZModStudio_TestCopies\ImageAssignerOracle_s_image_assignment_0_20260704_215041_771\ccz_case
```

结论：当前 6.5 基底下，人物 R/S 形象指定表的 CCZModStudio 写入地址与官方形象指定器 `System.ini` 完全一致：

- R 形象指定：`Ekd5.exe @ 0xE1000 + rowId * 2`
- S 形象指定：`Ekd5.exe @ 0xD2800 + rowId * 2`

该实验闭环可以作为后续 R/S 形象指定写入正确性的自动化判定标准。若未来扩展到头像或全局参数，仍需先建立对应的官方 oracle 偏移来源；不能把本次 R/S 结论外推到未验证字段。

#### 2026-07-04 全局数字参数继续采证结果

本轮继续测试旧 B 形象指定器“全局设置”左侧数字项，结论是：功能存在性比 2026-06-08 时更明确，但仍没有任何数字项达到可写标准，因此 CCZModStudio 继续保持这些项目只读，不放开保存权限。

已确认事实：

- `形象指定器65.exe` 中静态命中 `Form8 / 全局设置` 资源和相关标签，说明这些功能确实由官方工具提供，不只是截图或外部资料传闻。
- 已命中的标签包括：`能力显示`、`能力条件`、`转职等级`、`等级上限`、`升级经验`、`普装升级经验`、`特装升级经验`、`普装等级上限`、`特装等级上限`、`新加武将功勋`、`敌友武将功勋`、`普装提升等级`、`特装提升等级`、`中级装备出现等级`、`游戏标题`、`单数`、`双数`。
- `System.ini` 仍只提供 `FileHead/RFileHead/UserXK/BzXG/SMagic/SCount/DefID/AssID` 等配置，不包含这些左侧全局数字项的文件偏移、运行时地址或字段宽度。
- 官方工具可只读启动；在测试副本工具目录中，`System.ini` 可指向自动复制出的 `official_case`。但旧 VB6 工具按钮事件不能稳定用同步 `SendMessage/BM_CLICK` 驱动，`打开文件/全局设置` 自动保存 diff 本轮未闭环。
- `CMF_KNOWLEDGE_SMOKE` 曾使用过示例导出行 `等级上限 0048D3C4 Byte 1B Ekd5.exe / 升级经验 0048D3C5 Word 2B Ekd5.exe`。本轮用当前 6.5 基底 `Ekd5.exe` 重新映射验证：`VA 0048D3C4 -> 文件偏移 0x8BDC4`，读取到的是 GBK 文本片段，首字节 `D0-B6-AF`，不是 `60/73`，因此该示例不能作为等级上限/升级经验写回规则。
- 对当前 `Ekd5.exe` 搜索默认值组合：完整序列 `135,144,144,144,20,40,60,73,150,200,5,9,25,25,4,6,20` 未命中；`60,73`、`20,40`、`5,9`、`25,25` 等短序列存在但落在代码或噪声上下文中，不能作为字段地址。

当前九个数字项状态：

| 项目 | 旧工具默认/显示值 | 官方 UI 标签 | System.ini 偏移 | CMF/地址线索 | 当前权限 |
| --- | --- | --- | --- | --- | --- |
| 能力显示（单数/双数） | 单数 | 已确认 | 无 | 无字段级地址 | 只读 |
| 能力条件 | `135 / 144 / 144 / 144` | 已确认 | 无 | 默认值完整序列未命中 | 只读 |
| 转职等级（一转/二转） | `20 / 40` | 已确认 | 无 | 短序列存在但不能定位 | 只读 |
| 等级上限 / 升级经验 | `60 / 73` | 已确认 | 无 | `0048D3C4/0048D3C5` 已证伪为当前基底规则 | 只读 |
| 普装/特装升级经验 | `150 / 200` | 已确认 | 无 | 未取得可靠偏移 | 只读 |
| 普装/特装等级上限 | `5 / 9` | 已确认 | 无 | 短序列存在但不能定位 | 只读 |
| 新加/敌武将功勋 | `25 / 25` | 已确认 | 无 | 功勋字段另有动态调试缺口 | 只读 |
| 普装/特装提升等级 | `4 / 6` | 已确认 | 无 | 短序列存在但不能定位 | 只读 |
| 中级装备出现等级 | `20` | 已确认 | 无 | 单值无法排除误报 | 只读 |

本轮生成的证据目录：

```text
CCZModStudio_Reports\DebugEvidence\global-settings-static-scan
CCZModStudio_Reports\DebugEvidence\global-settings-value-cluster
CCZModStudio_Reports\DebugEvidence\global-settings-official-ui-20260704_221058_311
```

新增代码与 smoke：

- `GlobalSettingsService` 的九个数字项继续 `CanEdit=false`，但说明改为“官方 UI 标签已确认；仍需官方输出 diff/偏移闭环”。
- 全局参数证据页新增 `B形象指定器 Form8 全局设置页` 与 `CMF 示例地址 0048D3C4/0048D3C5` 两条证据。
- `GlobalSettingsDialog` 的全局参数表新增 `Oracle覆盖` 列。
- 新增 `--global-numeric-evidence-smoke`，当前输出：

```text
GLOBAL_NUMERIC_EVIDENCE_SMOKE_OK locked=9 cmfExampleVA=0048D3C4 fileOffset=0x8BDC4 firstBytes=D0-B6-AF ...
```

升级为可写的最低条件保持不变：

- 官方工具或 CheatMaker 正常导出拿到字段名、地址、类型、长度。
- 地址能分类到 `Ekd5.exe` 文件偏移、PE 映射地址或其它明确资源文件偏移。
- 当前 6.5 样本原值能复读为旧工具显示值。
- 测试副本写入后能由 CCZModStudio 复读一致。
- 最好再用官方工具输出 diff 或 x32dbg/runtime 观察闭环。

在这些条件满足前，不得把任何左侧全局数字项从 `Pending/NeedsUiOrDiffExtraction` 升级为可写。

#### 2026-07-06 全局数字参数两项闭环与查询入口

本轮通过人工操作旧 `形象指定器65.exe`，补齐了两个左侧全局数字项的官方单字段 diff、测试副本写回和复读闭环。结论只适用于当前 6.5 未加密基底 `Ekd5.exe`，SHA256 为 `F13D275C8F4CF32C93B06C6B754D14C2AC577F626E62CECF7780E62322238813`。

关键经验：

- 旧工具“保存”即使不改字段，也会规范化若干字节；本次必须先做 `noop_case`，再用 `noop_case -> 单字段修改 case` 分类真实字段变化。
- 静态默认值搜索只能作为断点和 diff 对照线索，不能作为可写地址来源。最新查询报告中，`等级上限` 默认值 `60` 相关静态命中达 `7746`，`升级经验` 默认值 `73` 相关静态命中达 `2956`，若没有官方单字段 diff 会产生大量误报。
- 可写定义必须记录所有官方 diff 命中的目标，而不是只写主读取偏移。`等级上限` 需要同步写 6 处，其中 `0xB7D6` 为 UI 值 `+1`；`升级经验` 需要同步写 7 处。
- 功勋字段即使后续 diff 找到偏移，也继续保持锁定，直到运行时确认“新加入武将”和“敌友武将”实际使用同一组字段。

本轮已开放写回项：

| Key | 旧工具字段 | 当前值 | 编码 | 主读取偏移 | 运行时 VA | 写回目标 | 状态 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `LevelLimit` | 等级上限 | `60` | Byte | `Ekd5.exe@0x68F1` | `0x4074F1` | 6 处：`0x68F1/0x7CD3/0xB7D6(+1)/0x116C8/0x117CE/0x1B98E` | 已验证，可写 |
| `UpgradeExperience` | 升级经验 | `73` | Byte | `Ekd5.exe@0x7CD6` | `0x4088D6` | 7 处：`0x7CD6/0x4F45A/0x4FF33/0x4FF48/0x5001F/0x5BAA3/0x78958` | 已验证，可写 |

官方 diff 证据目录：

```text
CCZModStudio_Reports\DebugEvidence\global-numeric-discovery\20260706_211950_213
```

其中：

- `before`：未操作基线。
- `noop_case`：用户手工选择测试副本后只保存，不改字段，用于扣除旧工具保存噪声。
- `official_case`：只改 `等级上限 60 -> 61`。
- `exp_case`：只改 `升级经验 73 -> 74`。
- `validated-fields\validated-global-numeric-fields.json`：字段级确认报告。

本轮新增只读查询入口：

- `GlobalNumericQueryService`：按 `GlobalSettingsService` 的全局数字项定义，扫描 `Ekd5.exe/Data.e5/Imsg.e5/Star.e5` 的默认值候选，输出 JSON 证据报告。
- MCP 工具 `query_global_numeric_definitions`：只读生成候选报告，不改变任何 `CanEdit`。
- GUI 中“生成定位证据”按钮会同时生成官方 diff 实验目录和静态查询报告。

最新静态查询报告：

```text
CCZModStudio_Reports\DebugEvidence\global-numeric-query\20260706_220601_636\global-numeric-query-report.json
```

查询摘要：

| Key | 字段 | 权限 | 静态候选计数 | 当前结论 |
| --- | --- | --- | ---: | --- |
| `AbilityDisplay` | 能力显示（单数/双数） | 只读 | 0 | 没有数字默认值可形成静态模式，必须继续官方单字段 diff |
| `AbilityThreshold` | 能力条件 | 只读 | 337216 | 只有短序列/单值噪声，误报极高 |
| `PromotionLevel` | 转职等级（一转/二转） | 只读 | 3906 | 完整默认序列有命中，但缺官方单字段 diff |
| `LevelLimit` | 等级上限 | 可写 | 7746 | 已由官方 diff 和写回复读确认；静态查询只作复核 |
| `UpgradeExperience` | 升级经验 | 可写 | 2956 | 已由官方 diff 和写回复读确认；静态查询只作复核 |
| `EquipmentExp` | 普装/特装升级经验 | 只读 | 274 | 只有短序列/单值噪声，保持只读 |
| `EquipmentLevelLimit` | 普装/特装等级上限 | 只读 | 5882 | 完整默认序列有命中，但缺官方单字段 diff |
| `Merit` | 新加/敌武将功勋 | 只读 | 676 | 完整默认序列有命中，但还缺官方 diff 和运行时功勋语义验证 |
| `EquipmentLevelRaise` | 普装/特装提升等级 | 只读 | 10448 | 完整默认序列有命中，但缺官方单字段 diff |
| `MiddleEquipmentLevel` | 中级装备出现等级 | 只读 | 5014 | 完整默认序列有命中，但缺官方单字段 diff |

验证入口：

```text
dotnet _BuildCheck\SmokeTestsGlobalNumeric\CCZModStudio.SmokeTests.dll --global-numeric-query-smoke
dotnet _BuildCheck\SmokeTestsGlobalNumeric\CCZModStudio.SmokeTests.dll --global-numeric-evidence-smoke
dotnet _BuildCheck\SmokeTestsGlobalNumeric\CCZModStudio.SmokeTests.dll --global-numeric-discovery-smoke
dotnet _BuildCheck\SmokeTestsGlobalNumeric\CCZModStudio.SmokeTests.dll --global-numeric-write-smoke
```

本轮输出：

```text
GLOBAL_NUMERIC_QUERY_SMOKE_OK fields=10 editable=2 levelCandidates=7746 expCandidates=2956
GLOBAL_NUMERIC_EVIDENCE_SMOKE_OK editable=2 locked=8
GLOBAL_NUMERIC_DISCOVERY_SMOKE_OK status=NeedsManualOfficialDiff fields=10
GLOBAL_NUMERIC_WRITE_SMOKE_OK verified=LevelLimit,UpgradeExperience
```

后续继续定位其它全局数字项时，必须沿用本轮流程：为同一测试副本建立 `noop_case`，每次只改一个字段，扣除 no-op 差异后再分类偏移、宽度和编码，最后用 CCZ 测试副本写回、复读和必要运行时验证闭环。未完成前不允许把待验证字段改为 `CanEdit=true`。

#### 2026-07-06 低风险等级类 leaf 字段采证入口

本轮将低风险等级类组合字段拆成 leaf key，并接入批量人工 diff 实验目录。由于尚未取得这些 leaf 字段的官方单字段 diff，本轮不新增可写字段；当前可写仍只有 `LevelLimit` 和 `UpgradeExperience`。

新增 leaf key：

| 父级组合项 | leaf key | 字段 | 实验值 |
| --- | --- | --- | --- |
| `PromotionLevel` | `PromotionLevelFirst` | 转职等级（一转） | `20 -> 21` |
| `PromotionLevel` | `PromotionLevelSecond` | 转职等级（二转） | `40 -> 41` |
| `EquipmentLevelLimit` | `EquipmentLevelLimitNormal` | 普装等级上限 | `5 -> 6` |
| `EquipmentLevelLimit` | `EquipmentLevelLimitSpecial` | 特装等级上限 | `9 -> 10` |
| `EquipmentLevelRaise` | `EquipmentLevelRaiseNormal` | 普装提升等级 | `4 -> 5` |
| `EquipmentLevelRaise` | `EquipmentLevelRaiseSpecial` | 特装提升等级 | `6 -> 7` |
| - | `MiddleEquipmentLevel` | 中级装备出现等级 | `20 -> 21` |

父级组合 key `PromotionLevel`、`EquipmentLevelLimit`、`EquipmentLevelRaise` 只用于 UI 展示，MCP/API 写入时必须失败并提示使用 leaf key，不能静默忽略或把组合值拆写。

新增 MCP/服务入口：

- `run_global_numeric_low_risk_discovery`：生成 `noop_case`、7 个 low-risk case 和对应 `official_tool_<caseKey>`。
- `compare_global_numeric_low_risk_diffs`：比较 `noop_case` 与每个 case，输出 `low-risk-case-diff-report.json`。

最新 smoke 生成的低风险实验目录：

```text
CCZModStudio_Reports\DebugEvidence\global-numeric-discovery\20260706_222400_150\low-risk-experiment-report.json
```

初始 compare 状态为 `NeedsManualOfficialDiff`，因为 7 个 case 尚未经过人工旧工具保存。晋级规则保持保守：必须是 `Ekd5.exe` only、1 字节 `old+1=new`、无跨 leaf 共享 offset，并且后续写入 `VerifiedDefinition` 后通过 CCZ 测试副本 round-trip，才可把对应 leaf 设为 `CanEdit=true`。

本轮 smoke 输出已更新：

```text
GLOBAL_NUMERIC_QUERY_SMOKE_OK fields=16 editable=2
GLOBAL_NUMERIC_EVIDENCE_SMOKE_OK editable=2 locked=14
GLOBAL_NUMERIC_DISCOVERY_SMOKE_OK status=NeedsManualOfficialDiff fields=16 lowRiskCases=7
GLOBAL_NUMERIC_WRITE_SMOKE_OK verified=LevelLimit,UpgradeExperience
```

#### 2026-07-06 低风险等级类字段写回闭环

用户完成低风险批量实验目录 `CCZModStudio_Reports\DebugEvidence\global-numeric-discovery\20260706_223300_608` 的人工旧工具操作后，继续比较 `noop_case -> leaf case`。本轮确认可晋级 5 个 leaf 字段，另 2 个保持锁定：

- `PromotionLevelSecond`：旧工具强制二转等级为一转等级的两倍，不能独立编辑；CCZ 只开放 `PromotionLevelFirst`，并同步写二转派生常量。
- `MiddleEquipmentLevel`：旧工具固定为 `20`，不能编辑；本轮 case 只出现旧工具保存噪声，没有字段级变更，继续只读。

本轮必须先剔除旧工具保存时共同出现的升级经验规范化噪声：`Ekd5.exe@0x4F45A/0x4FF33/0x4FF48/0x5001F/0x5BAA3/0x78958 -> 0x49`。这些偏移来自旧工具写回已有 `UpgradeExperience=73`，不是低风险 leaf 字段自身，不能归入本批新字段。

已开放写回项：

| Key | 字段 | 主读取偏移 | 写回目标 | 派生规则 |
| --- | --- | --- | --- | --- |
| `PromotionLevelFirst` | 转职等级（一转） | `Ekd5.exe@0x7E67` | `0x7E67/0xB7BD/0x1C7E3/0x41D03/0x41D39/0x680B8`，以及 `0xB7AE/0x41D21` | 后两处写 `一转值 * 2`，对应二转派生常量 |
| `EquipmentLevelLimitNormal` | 普装等级上限 | `Ekd5.exe@0x71D9` | `0x71D9/0x7409/0x744C/0x74E6/0x772B/0x1F5D2`，以及 `0x71A9/0x73DE` | 后两处写 `普装等级上限 * 普装提升等级` |
| `EquipmentLevelLimitSpecial` | 特装等级上限 | `Ekd5.exe@0x71AC` | `0x71AC/0x7727/0x1F5E1`，以及 `0x1F5CE` | `0x1F5CE` 写 `特装等级上限 + 1` |
| `EquipmentLevelRaiseNormal` | 普装提升等级 | `Ekd5.exe@0x73A3` | `0x73A3`，以及 `0x71A9/0x73DE` | 后两处写 `普装等级上限 * 普装提升等级` |
| `EquipmentLevelRaiseSpecial` | 特装提升等级 | `Ekd5.exe@0x7392` | `0x7392` | 直接写 UI 值 |

因此全局数字写回目标不再只有 `value + delta`，还支持 `value * multiplier + delta` 和 `value * 其它已读字段 + delta`。目前派生目标只用于：

- `PromotionLevelFirst -> 二转派生常量`
- `EquipmentLevelLimitNormal/EquipmentLevelRaiseNormal -> 普装等级上限 * 普装提升等级`

当前权限：

```text
GLOBAL_NUMERIC_QUERY_SMOKE_OK fields=16 editable=7
GLOBAL_NUMERIC_EVIDENCE_SMOKE_OK editable=7 locked=9
GLOBAL_NUMERIC_WRITE_SMOKE_OK verified=EquipmentLevelLimitNormal,EquipmentLevelLimitSpecial,EquipmentLevelRaiseNormal,EquipmentLevelRaiseSpecial,LevelLimit,PromotionLevelFirst,UpgradeExperience
```

写回 smoke 已扩展为对所有 `CanEdit=true` 数字项执行测试副本 round-trip，并逐字节校验 preview 中列出的每个目标偏移。父级组合 key 仍不接受写入，`AbilityThreshold/EquipmentExp/Merit/PromotionLevelSecond/MiddleEquipmentLevel` 继续锁定。
