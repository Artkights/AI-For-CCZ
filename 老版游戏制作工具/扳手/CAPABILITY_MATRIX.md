# 曹操传 6.6 扳手能力矩阵

## 基底身份

| 基底 | 文件长度 | SHA256 | 离线画像 |
|---|---:|---|---|
| 清儿吕布传 | 1,413,120 | `88EC708A24718868090F70256D09086E83C92FAB0613C7B8D3742EF974D3564D` | 通过 |
| 新改曹操傳6.6修正版 | 1,130,496 | `4A4FD8DDBF83E5F0B769D1B97BF8F6E6431C3AB42892024A354228212D3D06A4` | 通过 |
| 曹操传6.6-V7.16 | 见本地基底 | `34B810EE790F4BAA70EBF60AA286AA7BD246A2C17B425B36BD3825B36BA2D6DA` | 16B/4+2，高位空值无损 |
| 烈焰同盟新修版 | 1,173,504 | `D4C6EA15A527C62283CAEADEFAC112109D0B5D63D66AD25BE1D1FD2413EF6EE2` | 16B/4+2，`1024..1278` 空值无损 |
| 厚黑后宫 | 1,130,496 | `A99DDD6971669D4847444F91B9ED211CD205107E1074528CCEDB86E360FC1282` | 混合画像，指针型8B/3+1 |
| 标准6.5未加密版 | 1,196,032 | `F9A357585F4D77C273A8BADD3E4EF4062983CC81D8040C0CB74768FAB7C04F5F` | 独立6.5，指针型8B/3+1，105项消耗品 |

已知 SHA 只标注派生版名称。主程序文件名和 `FilePrivatePart` 只调整候选顺序；逐功能签名与当前进程探针决定实际访问级别。

## 状态口径

| 状态 | 含义 | 当前 UI |
|---|---|---|
| `Unavailable` | 定位、结构、映射或 ABI 未闭环 | 若没有地址、表布局或版本配方则禁用；已有 direct compatibility 配方仍可执行并显示警告 |
| `StaticVerified` | 离线唯一签名和结构通过 | 可执行但显示“未完成验证”警告 |
| `RuntimeVerified` | 受信实机证据通过 | 可执行，仍需当前会话探针 |
| `Enabled` | 当前进程探针通过 | 按画像配方开放读写 |

访问级别独立为 `Unavailable / ReadOnly / ReadWrite`。`access_policy=direct_compat` 下，证据缺失不会单独禁写；但零地址、无 wrapper、无表布局、PE32 地址越界、页不可读写和进程生命周期异常仍会拒绝执行。`turn_refresh_position` 当前没有地址和 wrapper，仍为未实现。

## 功能矩阵

| 页面/功能 | 清儿离线状态 | 新改离线状态 | 当前放行条件 |
|---|---|---|---|
| 控制：敌友军可控 | `StaticVerified` | `StaticVerified` | 六站点实机行为、三次开关和回滚证据 |
| 控制：不分敌我 | `StaticVerified` | `StaticVerified` | `0x4242AA/0x437BC0/0x454405/0x455646` 四站点实机行为和回滚证据 |
| 控制：禁止 AI | `StaticVerified` | `StaticVerified` | 烈焰已确认签名和 `0x44E9D3:74->EB`；代码页事务已覆盖 `EXECUTE_WRITECOPY`，等待实机 AI 行为与保护恢复证据 |
| 控制：待命后移动 | `StaticVerified` | `StaticVerified` | 实机移动行为和回滚证据 |
| 控制：移动力 255 | `StaticVerified` | `StaticVerified` | 实机移动范围和函数恢复证据 |
| 控制：穿越移动 | `StaticVerified` | `StaticVerified` | 实机碰撞、寻路和两站点恢复证据 |
| 控制：自动回归 | `StaticVerified` | `StaticVerified` | `0x421175` 原生二次行动函数和 `0x412D82/0x43D529/0x43D540` 三站点已静态确认；等待两个基底两角色、原地/移动后待命和可重新操作确认 |
| 控制：托管 | `StaticVerified` | `StaticVerified` | 战场表探针和实际托管行为 |
| 控制：自动复活 | `StaticVerified` | `StaticVerified` | 6.5 `0x4092C7` / 6.6 `0x4092C0` 已可直接兼容执行；仍需 ABI、复活行为、槽位复用和连续 3 次实机验收 |
| 人物读写/功勋 | `StaticVerified` | `StaticVerified` | 读写恢复、人物指针和范围探针 |
| 人物能力重算 | `StaticVerified` | `StaticVerified` | `0x4075DF` ABI 和派生能力结果 |
| 战场字段/状态/AI | `StaticVerified` | `StaticVerified` | `0x30` 结构读写恢复和实际行为 |
| 转向字段保存/即时刷新 | `StaticVerified` | `StaticVerified` | `0x4575B8`、六参数、`ret 0x18`；仍需清儿和三阵营行为证据 |
| 坐标 helper | `Unavailable` | `Unavailable` | 原 UI 没有调用，不接入战场保存 |
| 仓库普通编辑 | `StaticVerified` | `StaticVerified` | 200×3 运行时表、金钱/忠奸度/功勋池和写回恢复；不依赖物品元数据 |
| 一键全宝 | `StaticVerified` | `StaticVerified` | `Data.e5@33164` 的 104 行与 `Star.e5@0` 的 151 行通过语义探针，按 `+0x18==1` 筛选并验证仓库边界 |
| 消耗品列表/全道具 | `StaticVerified` | `StaticVerified` | 标准画像为 `0x493AF0/160..199` 40项；厚黑由独立锚点解析为 `0x510C80/150..254` 105项；只读画像可显示但不可保存 |
| 255 项天赋/专属/套装 | `StaticVerified` | `StaticVerified` | 天赋由 `DWORD[0x500C3B]` 指向 255×`0x10` 表，4角色+2兵种且每槽独立值；专属/套装保持原表 |
| 三组变量 | `StaticVerified` | `StaticVerified` | 每组地址、页权限和写回恢复 |
| 自定义 CSV | `StaticVerified` | `StaticVerified` | `1/2/4` 字节范围、页权限和写后复读 |
| 必杀五槽 | `StaticVerified` | `StaticVerified` | 运行时指针、第五槽和逐行复读 |
| 天气刷新 | `StaticVerified` | `StaticVerified` | `0x41D9D1` ABI 和画面行为 |

未知派生版按功能组合画像。零匹配、多匹配、函数指纹变化、原字节变化或结构探针失败只降级受影响功能；可证明读取但写语义不完整时为 `ReadOnly`，不能证明地址时为 `Unavailable`。

## 地址对齐结论

- `0x4242B0` 是 `03 4D F8` 指令的一部分，已彻底移除该写入；“不分敌我”现在只修改四个完整分支，并使用会话所有权回滚。
- 自动回归保留 `0x412D7B/0x43D522` 对原生二次行动函数 `0x421175` 的调用，只事务 NOP `0x412D82/0x43D529/0x43D540` 三个限制分支；不再生成动态 wrapper 或修改旧 A/B CALL。
- 6.6 转向真实入口为 `0x4575B8`，参数为 `dataId, 0xFFFF, direction, 0, 1, 1`，由被调函数清理 24 字节。新改实机已完成方向 `3 -> 0 -> 3` 恢复。
- 标准 6.6 装备候选严格为 ID `0..159`，消耗品为 ID `160..199`，四组签名共同确认 `BYTE[0x493AF0+ID-160]`。`0x510C80` 不是全局脏地址：厚黑、6.4 和标准 6.5 只有在 `itemId-0x96` 的更新/读取/写入与 `0x69` 清零循环全部一致时，才使用 `150..254` 的 105 项配方；`0x4B09DB` 仍不得回退强写。
- 布尔变量从 `0x492FC8` 起按 MSB-first bitset 存储，共 512 字节；`0x492FCC` 承载变量 32..39，不是数据首地址。整型和指针变量仍为 UInt32×4096。
- 6.6 的 `0x4A1140` 在当前实机为全零页，不能作为 255×`0x19` 物品表；物品元数据必须从只读的 `Data.e5`/`Star.e5` 分段加载，短文件、全零表或名称语义失败时只禁用物品相关动作。
- 标准 6.6 查询函数使用 `effectId<<4`、四次三字节角色循环和两次二字节兵种循环；厚黑则由独立机器码证明使用 `effectId×8`、三个 UInt16 角色、一个 UInt8 兵种和共享值，并通过 `DWORD[0x500C3B]` 指向 `0x507800`。两者必须按实际签名选择，不能互相套地址。
- 控制补丁写入不再依据“页看起来可写”跳过保护切换。所有 `PatchSpec` 和动态 CALL 都使用逐页代码事务；`PAGE_EXECUTE_WRITECOPY` 必须先切换为 RWX，完成后恢复原保护。
- 人物和特效画像已升级为语义描述符：初始化、读取、写入锚点共同证明指针、记录数、步长和字段宽度。只有读取锚点时最多为 `ReadOnly`，锚点冲突或多匹配为 `Unavailable`。
- 空角色高位值不是分隔符。表范围只能由运行时指针与 `record_count×stride` 计算；烈焰的 `1024..1278` 继续逐槽无损保存。
- 物品25字节行同样按画像组合：标准6.6/清儿/V7.16/烈焰为15字节名称的6.6重排行；厚黑和标准6.5为17字节名称的旧行。`row+0x18` 图鉴位置相同不能证明其它字段也相同。
- 物品行 codec 与消耗品数量配方是两个独立维度。前者必须由 `itemId*0x19+runtimeBase` 索引函数唯一提取 `0x49F820/0x4A1140`，后者由数量更新、读取、直接写入和清零循环共同证明；任一维度缺证只禁用对应能力。
- 专属/套装 16 字节行与仓库 3 字节槽均保留 UI 域外的原始 ID。只改效果值、等级、金钱等相邻字段时不得把作者自定义高位值改为 1024/255。
