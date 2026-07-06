# 人物 R/S 像素形象提示词模板

## 结论速览

- R/S 提示词必须服务于 MCP 参考图输入链路，而不是替代参考图。
- 角色图和格式图必须双图隔离：角色图负责身份和设计，格式图负责动作和曹操传 6.5 像素语法。
- 任何武器规则必须由 `unitType` 和 brief 决定；不得让格式图或形象图中的错误武器关系污染最终结果。
- `weapon_brief` 是武器层最高优先级；当它明确指定武器时，design 图中的其它可见武器、第二武器、交叉武器和旧特效弧都必须忽略。
- 本地程序化样例、旧孙策 v1-v4 和 `SunCe_SingleSpearCavalry_v1` 不得出现在正向提示词中。

## 通用角色 brief

```text
【角色 brief】
名称：<角色或单位名>
阵营与气质：<例如江东主将、北方铁骑、山越女射手、妖兽、攻城器械>
unitType：<剑骑兵/刀骑兵/枪骑兵/弓骑兵/步兵/弓兵/炮车/策士/女兵/动物/大型单位>
体型：<骑乘/步行/四足/器械/大体型/飞行>
主轮廓：<头盔、肩甲、披风、袍袖、车轮、鬃毛、尾巴、翼、炮管等>
武器或攻击器官：<剑/刀/枪/弓/弩/扇/杖/炮/爪/牙/尾/法器>
主色与辅色：<主色、辅色、肤色或毛色、金属色、特效色>
必须保留：<缩小到 48x48 仍要读出的身份标识>
禁止误读：<禁止出现的武器、配色、现代元素、白底、透明棋盘格>
```

## S 动作表替换型模板

适用：有角色图和曹操传 6.5 S 格式参考图。

```text
为曹操传加强版 6.5 模组绘制 S 战场角色小人动作表。

【输入图职责】
第一张参考图 role=design，只负责角色身份、体型、服装/甲胄、主色、坐骑/车体/动物身体和关键装饰。
第二张参考图 role=format_action，只负责 4x6 布局、格子顺序、朝向、动作语法、短身比例、粗轮廓、洋红键背景、占格比例和特效尺度。
不要复制第二张参考图的角色身份、武器、配色或装饰；只学习它的曹操传 6.5 R/S 格式和动作。

【角色 brief】
<粘贴角色 brief>

【输出】
输出一张 4 列 x 6 行、共 24 格的像素动作表。
每格等宽等高，无边框、无网格线、无间距、无文字。
视觉上必须像原生 48x48/64x64 低分辨率像素帧最近邻放大，而不是高清像素插画或现代像素贴纸。
背景为纯洋红键，接近 #F700FF 或 #FF00FF；不要白底、透明棋盘格、渐变、地面或场景。

【曹操传 6.5 风格】
短身战棋小人，头部/头盔略大，躯干紧凑，腿短，整体 2 到 3 头身。
深色粗轮廓，硬边像素，有限调色盘，少量明暗层次，底部保留曹操传式深色落地阴影。
角色、武器/攻击器官、披风/尾部/车体和特效应接近撑满单帧，允许贴近边界但不能裁断。
每格必须是不同姿态，不要复制粘贴同一帧。

【动作覆盖】
必须覆盖移动、待机、背面、侧面、受击、格挡、庆祝、蓄力、攻击、收招和特技姿态。
攻击帧要利用 64x64 攻击画布，必须有身体重心变化、武器/攻击器官位移和特效变化。
不要只画站立帧旁边加小光效。

【类型规则】
当前 unitType 是：<填入 unitType>。
按下面类型 profile 执行动作，不要默认画枪或剑。
<粘贴对应类型 profile>
```

## MCP 调用参数模板

适用：通过 `build_rs_pixel_character_design` 一次性建立 R/S 人物包。

```text
package_id: <PackageId>
display_name: <角色名>
unit_type: <类型，例如 spear_cavalry>
design_image_path: <角色形象图路径>
format_action_image_path: <曹操传 6.5 格式动作参考图路径>
character_brief: <角色身份、服装、主色、轮廓和气质；不要把错误武器写入角色 brief>
weapon_brief: <唯一武器或攻击方式；该字段优先级高于 design 图和 format_action 图>
forbidden_readings:
  - <禁止武器或错误读法>
  - <禁止风格或错误输出>
generate_now: true
dry_run: false
```

执行规则：

- `design_image_path` 和 `format_action_image_path` 必须是真实本地图片路径，不能只写文本描述。
- `weapon_brief` 必须显式覆盖参考图中的错误武器读法。
- 如果上游或 MCP 报告没有记录参考图路径/角色/hash，不得声称“使用了形象图”。
- 如果 RetroDiffusion key 不可用，停止并记录 blocked，不把 dry-run、fake upstream 或本地脚本输出放入 `materials`。

## 本机像素编辑 recipe 模板

适用：用户要求不使用 RetroDiffusion、Image Studio、系统 imagegen 或外部生图模型，而是使用本机工具整合包 MCP 像素编辑能力制作 R/S。

调用顺序：

```text
create_rs_pixel_edit_workspace
build_rs_pixel_edit_plan
apply_rs_pixel_frame_edits
export_rs_pixel_contact_sheets
validate_rs_pixel_edit_workspace
```

工作区创建参数：

```text
package_id: <PackageId>
display_name: <角色名>
unit_type: <类型，例如 spear_cavalry>
design_image_path: <角色形象图路径，只作为设计观察图>
format_reference_root: <含 front/back/mov/atk/spc 的本地真实格式参考文件夹>
overwrite_existing: false
```

编辑 recipe 必须写成可执行 op，而不是自然语言大段描述。每条 op 至少说明：

```text
operation: <操作名>
target: <front|back|mov|atk|spc>
frames: <帧号列表，0-based>
x/y/width/height 或 x/y/x2/y2: <帧内坐标>
color/secondary_color: <需要时填写 #RRGGBB>
note: <用途和风险>
```

允许操作：

```text
recolor_palette
clean_face_box
erase_weapon_residue
erase_effect_residue
erase_rect_to_magenta
draw_spear_axis
draw_spear_tip
draw_spear_effect
repaint_armor_blocks
repaint_cape_blocks
magenta_key_cleanup
copy_region_from_reference
copy_region_from_frame
```

孙策单枪枪骑兵 recipe 示例：

```text
【workspace】
package_id: SunCe_LocalPixelEditor_SingleSpearCavalry_v1
display_name: 孙策
unit_type: spear_cavalry
design_image_path: F:\从0开始的AI制作曹操传MOD\曹操传加强版6.5（未加密）\孙策.png
format_reference_root: <本地真实 6.5 枪骑/骑乘五件套参考文件夹>

【edit ops】
1. clean_face_box
   target: front
   frames: 0-19
   bbox: 按帧内脸部 safe box 设置
   note: 清除黑脸、暗红脸部污染；只保留肤色、暗肤色、眼鼻暗点和少量高光。

2. clean_face_box
   target: mov/atk/spc
   frames: 正面、侧面、攻击关键帧
   bbox: 按帧内脸部 safe box 设置
   note: 披风红、暗红甲片、枪芒不得进入脸部。

3. erase_weapon_residue
   target: atk
   frames: 0-11
   bbox: 原参考武器、宽白剑弧、第二武器风险区域
   note: 选择性擦武器/亮色残留像素，先剥离旧武器读法，不能只叠加新枪；不得整块抹掉人物身体。

3b. erase_effect_residue
   target: atk
   frames: 旧白灰/浅粉斩击弧所在帧
   bbox: 旧斩击弧和残影区域
   note: 专门清白灰/浅粉旧剑弧；保留人物、甲片、脸部和正确枪尖。

3c. erase_rect_to_magenta
   target: atk/mov/spc/front/back
   frames: 只限确认应为空的背景小块
   bbox: 明确背景区域
   note: 高风险整块擦除，只能用于小块背景修补；不能用于人物身体、脸部、坐骑或武器附近。

4. draw_spear_axis
   target: atk
   frames: 0-11
   x/y/x2/y2: 每帧唯一长枪轴线
   color: #704B2A
   note: 每帧只能有一条主长枪轴线，握持点、枪尾、枪尖必须连续可追踪。

5. draw_spear_tip
   target: atk
   frames: 0-11
   bbox: 枪尖 3-5 像素区域
   color: #F5EEB2
   note: 枪尖是攻击读点，不能被特效盖住。

6. draw_spear_effect
   target: atk
   frames: 蓄力、突刺、爆发关键帧
   color: #FFDA54
   secondary_color: #FFFFFF
   note: 枪芒只围绕枪尖，不沿披风、马身或旧剑弧发光。

7. recolor_palette / repaint_armor_blocks
   target: front/back/mov/atk/spc
   frames: 全关键帧
   color: #231F1E
   secondary_color: #DEB248
   note: 黑金甲胄与脸部分层，避免把脸压成甲片阴影。

8. repaint_cape_blocks
   target: back/front/mov/spc
   frames: 披风可见帧
   color: 暗红系
   note: 披风只在身体和背部区域出现，不侵入 face safe box。

9. magenta_key_cleanup
   target: front/back/mov/atk/spc
   frames: 全帧
   note: 背景统一为严格 #FF00FF；角色内部不得出现近洋红。

10. copy_region_from_frame
   target: front/back/mov/atk/spc
   frames: 仅限已确认的空帧、稀疏帧或原生未使用槽位
   source_frame: 同条带内相邻且视觉语法一致的原生帧
   bbox: 通常为整帧；若局部复制，必须限定到明确安全区域
   note: 只用于修复结构性空洞，并写入 `edit_log.jsonl`；不得用来伪造整套重复帧，也不得替代真正像素编辑。
```

验收口径：

- `reports/edit_log.jsonl` 必须记录每次编辑。
- 每轮编辑后必须导出 contact sheet。
- `validate_rs_pixel_edit_workspace` 通过只代表格式和风险门禁，不代表美术已经合格。
- 成功前不得写入 `local_sample_index.json` 正样本。

真实骑乘参考筛选规则：

- 不要只看单帧、侧面图或攻击瞬间来判断骑乘参考。
- 必须同时检查 `mov.bmp`、`atk.bmp`、`spc.bmp` 的完整未标注 contact sheet。
- 曹操传 6.5 原生骑乘条带中，部分正背帧可能看不到完整马身；这不必然失败。判断重点是侧面移动、冲锋攻击和整体动作语法是否读作骑乘。
- `S110` 类“局部有马、整体像步行”的参考不得再作为枪骑兵主格式源。
- 当前孙策本机像素编辑候选优先参考为 `R100/S90`，但仍是 review candidate，不是最终正样本。

## R 条带模板

```text
为曹操传加强版 6.5 模组绘制 R 剧情角色小人素材。

【输入图职责】
role=design 的角色图只负责身份、服装、主色、轮廓和气质。
role=format_action 的 R 格式参考只负责 20 帧条带、人物站位、朝向、动作节奏和像素比例。
不要复制格式参考原角色的身份、武器、配色或头盔。
武器以 brief 为准；brief 明确指定时，不要读取 design 图里的其它武器、第二武器或旧特效弧。

【角色 brief】
<粘贴角色 brief>

【输出要求】
上游必须先输出 `2列x20行` 正背 R 动作表：
- 左列：front，20 帧。
- 右列：back，20 帧。

最终由 MCP 后处理整理为：
- front.bmp：48x1280，20 帧，每帧 48x64。
- back.bmp：48x1280，20 帧，每帧 48x64。

正面必须读出脸部或关键正面读点、主色、武器或攻击器官、甲袍/毛色/车体轮廓。
背面必须单独绘制，表现披风、背甲、发束、背持武器、尾巴、车轮、炮管或动物背部关系。
不要把正面水平翻转当背面。
不要只画单帧立绘；不要把同一帧复制 20 次。
每帧是 48x64 原生低像素小人，硬边、粗轮廓、有限色、纯洋红键背景。
```

## 类型 profile

### 枪骑兵

```text
枪骑兵 profile：
单位骑马，唯一武器是长枪、长矛或长戟。
禁止剑、短刃、腰剑、背剑、第二枪、双武器、宽白剑弧、弓或炮。
攻击动作为蓄力、突刺、挑击、枪芒爆发、收枪；必须有长杆、握持点、亮枪尖和连续枪尖位移。
特效是白金枪芒、线性冲击、少量金色火花，必须围绕枪尖，不得沿披风边、马身边、旧剑弧或第二武器位置发光。
验收点：R 正背面、S 移动、S 攻击、S 特技关键帧都能读出同一把长枪。
```

### 剑骑兵

```text
剑骑兵 profile：
单位骑马，唯一武器是短到中等长度直剑，不要长杆、枪尖、弓或炮。
移动帧表现骑乘重心和马身起伏；攻击帧是近身斜斩、横斩、收剑。
剑身是短亮边，刀光不能太厚；特效是短促白银剑芒，不要画成长枪枪芒。
```

### 刀骑兵

```text
刀骑兵 profile：
单位骑马，唯一武器是宽刀、弯刀或大刀，不要细剑、长枪、弓或炮。
攻击动作为大幅劈砍、拖刀斩、回身斩，刀身要比剑更宽更厚。
特效可以使用厚重金白刀光和少量火花，但不要变成法术光圈。
```

### 弓骑兵

```text
弓骑兵 profile：
单位骑马，唯一远程武器是弓或弩，不要剑、刀、枪作为主攻击。
攻击动作为取箭、拉弓、瞄准、放箭、收弓；马身保持骑乘动作。
至少 3 个攻击帧清楚表现弓弦拉开、箭矢方向和骑射姿态。
```

### 步兵

```text
步兵 profile：
单位双脚落地，不要马、车轮或大型坐骑。
武器按 brief 指定，可以是剑、刀、枪、斧、盾、锤或徒手。
移动帧必须有左右脚步态；攻击帧身体重心明显前压或旋转。
```

### 弓兵

```text
弓兵 profile：
单位步行，远程武器是弓或弩，背部可有箭袋。
攻击动作为站稳、拉弓、放箭、收弓或弩机装填；不要改成剑斩。
特效保持低像素箭矢和小型速度线，不要大面积遮住身体。
```

### 炮车

```text
炮车 profile：
单位是古代攻城器械或火炮车，主体为木架、车轮、炮管或投石结构。
移动帧表现车轮滚动或车体颠簸；攻击帧表现装填、炮口闪光、后坐、烟火或石弹飞出。
不要画成现代坦克、现代火炮或普通马车。
```

### 策士

```text
策士 profile：
单位步行，主轮廓是袍服、冠帽、扇、杖或法器。
攻击不是物理斩击，而是举扇、挥杖、结印、法阵、光束或符咒。
特效可以明显，但必须低像素、硬边、有限色，并且不遮住头部和法器。
```

### 女兵

```text
女兵 profile：
单位可以骑乘或步行，仍按 brief 指定武器执行；不要默认无武器或装饰化。
轮廓可使用较轻甲片、发饰、裙摆或披风，但必须保持曹操传短身战棋比例。
不要现代服饰、过度细节、高清立绘比例或与战斗无关的姿态。
```

### 动物

```text
动物 profile：
单位不是人形，按 brief 指定为虎、狼、豹、熊、鸟、蛇、龙兽或其他四足/飞行动物。
移动帧必须是动物步态、奔跑、振翅或游动，不要画成人形走路。
攻击帧使用扑击、撕咬、爪击、尾击、冲撞、喷吐或振翅冲击。
```

### 大型单位

```text
大型单位 profile：
单位占格更宽更重，可以是巨兽、巨盾兵、重甲将、机关兽或大型器械。
动作应慢而有重量，攻击为重砸、冲撞、喷吐、挥臂或范围冲击。
必须控制边界，关键部位不能被裁切；不要让特效遮住整个主体。
```

## 专项修复模板

### 面部或关键读点清理

```text
只修复脸部/眼鼻/动物头部关键读点，不改动全身动作和格式。
在每个正面和侧面关键帧中建立很小的 safe box。
safe box 内只允许肤色/毛色亮部、暗部、眼鼻黑点和极少量高光。
禁止披风红、暗红甲片、红棕阴影、武器特效和背景色进入 safe box。
不要把脸涂成矩形色块；保留 1 到 2 个眼鼻暗点和曹操传像素感。
```

### 武器或攻击方式纠偏

```text
只修复当前 unitType 的武器和攻击读法，不改变角色主色、体型和帧序。
当前 unitType 是 <类型>，唯一武器或攻击方式是 <武器/器官>。
先剥离错误旧武器层和旧特效层，包括错误轮廓、亮边、黑色外框、宽白弧和残留轨迹；不要只在空白处叠加新武器。
再生成唯一正确武器层：每帧只能有一条主武器轴线，武器的手持点、末端、尖端或攻击器官必须连成同一个动作。
特效必须围绕正确武器的尖端、刃口、箭矢、炮口或施法法器展开，不能沿旧武器轨迹发光。
```

## 通用负面约束

```text
文字, 水印, 签名, 多个无关角色, 背景场景, 白底, 透明棋盘格, 渐变背景, 地面场景, 非洋红键背景, 缺格, 非4x6布局, 格子尺寸不一致, 网格线, 边框, 角色被裁断, 武器被裁断, 特效被裁断, 武器类型漂移, 把弓改成剑, 把刀改成剑, 把枪改成剑, 把动物画成人形, 把炮车画成现代机械, 高清插画, 现代像素贴纸, 写实长身比例, 过度Q版巨头, 柔边抗锯齿, 半透明特效, 平滑渐变, 复杂光影, 每格复制粘贴, 攻击只是站立加光效, 身体比例漂移, 脚底漂浮, 近洋红色进入角色内部, 本地程序化几何绘制感, 旧孙策v1-v4武器关系
```

## 孙策单枪枪骑兵固定 brief

```text
【角色 brief】
名称：孙策
阵营与气质：江东孙氏主将，锐利、豪勇、骑乘突击
unitType：枪骑兵
体型：曹操传 6.5 短身骑乘战棋小人
主轮廓：黑金重甲、夸张肩甲、金冠束发、红黑披风、马
武器或攻击器官：唯一一把长枪/长矛
主色与辅色：黑、金、暗红披风、清晰肤色、白金枪芒
必须保留：金冠/束发、黑金重甲、披风、马、单枪枪尖
禁止误读：剑、短刃、腰剑、背剑、第二枪、双武器、宽白剑弧、旧孙策包像素、程序化几何图
```

推荐 MCP 参数：

```text
package_id: SunCe_MCP_SingleSpearCavalry_v1
display_name: 孙策
unit_type: spear_cavalry
design_image_path: F:\从0开始的AI制作曹操传MOD\曹操传加强版6.5（未加密）\孙策.png
format_action_image_path: F:\从0开始的AI制作曹操传MOD\曹操传加强版6.5（未加密）\CCZModStudio_Exports\RS_PixelDesign\SunCe_MCP_SingleSpearCavalry_v1\refs\selected_format\S64_huanwang_format_action\S64_all_frames_x4.png
character_brief: 孙策，江东孙氏主将，黑金重甲，金冠束发，红黑披风，骑乘突击气质。孙策.png 只作为身份、甲胄、冠饰、披风、主将气质参考，不提供武器层。
weapon_brief: 唯一武器是一把长枪/长矛；枪骑兵；攻击为蓄力、突刺、挑击、枪尖白金枪芒爆发、收枪。全套 R/S 每帧只能追踪同一把长枪。
forbidden_readings: 剑、短刃、腰剑、背剑、第二枪、双武器、交叉武器、宽白剑弧、旧孙策包像素、程序化几何绘制感、现代像素贴纸、黑脸、暗红脸部污染
generate_now: true
dry_run: false
```

## 风险与边界

- 提示词不能替代 MCP 参考图输入；`reference_image_paths` 为空时不得声称使用了形象图。
- 若 RetroDiffusion/API key 不可用，停止生成，不用本地脚本兜底。
- 通过 MCP 预览不等于视觉合格；视觉验收失败不得进 `local_sample_index.json`。
