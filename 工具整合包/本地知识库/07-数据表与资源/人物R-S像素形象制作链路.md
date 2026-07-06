# 人物 R/S 像素形象制作链路

## 结论速览

- 曹操传加强版 6.5 的人物 R/S 形象制作必须走 **MCP-first、参考图输入、格式参考隔离、视觉验收优先** 的链路。
- 当用户要求不通过 RetroDiffusion、Image Studio 或外部生图模型时，当前优先主线是 **本机像素编辑 MCP**，即通过工具整合包 C# Core/MCP 对真实 R/S 条带做帧级像素编辑。
- 旧本地 Pillow/几何绘制/程序化像素脚本不得再作为 R/S 成品生产方式；格式合格、报告合格、MCP 预览合格都不能替代曹操传风格的视觉质量。
- R/S 图像资源不在 `RS\R_*.eex` / `RS\S_*.eex`。R 写入 `Pmapobj.e5`，S 写入 `Unit_mov.e5`、`Unit_atk.e5`、`Unit_spc.e5`。
- 任何有形象图的角色，形象图必须作为 MCP 参考图输入记录在 plan/report 中；只把形象图人工转成文字描述，不算真正使用形象图。
- 格式图必须来自真实曹操传 6.5 R/S 或本地基底导出，只负责动作语法、短身比例、朝向、格子占用和洋红键背景；不得复制其角色身份、武器、配色或特效形状。
- 每套成品固定输出五件套：`front.bmp`、`back.bmp`、`mov.bmp`、`atk.bmp`、`spc.bmp`。视觉确认前不得写测试副本，任何情况下不得直接写正式基底。

## 已确认事实

- 人物表中的 R/S 编号写在 `Ekd5.exe`：
  - R 指定表：`Ekd5.exe @ 0xE1000`。
  - S 指定表：`Ekd5.exe @ 0xD2800`。
- R 图像本体在 `Pmapobj.e5`：
  - `R=n -> Pmapobj.e5 #2n+1/#2n+2`。
  - `front.bmp` 和 `back.bmp` 均为 `48x1280`，即 `48x64 * 20` 帧竖条。
- S 图像本体在三套 Unit E5：
  - `mov.bmp -> Unit_mov.e5`，尺寸 `48x528`，即 `48x48 * 11` 帧。
  - `atk.bmp -> Unit_atk.e5`，尺寸 `64x768`，即 `64x64 * 12` 帧。
  - `spc.bmp -> Unit_spc.e5`，尺寸 `48x240`，即 `48x48 * 5` 帧。
- 形象类 `.e5` 图片资源按绝对偏移 `0x110` 读取 12 字节大端索引项；不得按裸扫 PNG/JPG/BMP 出现顺序定位。
- MCP R/S 预览只验证替换路径和资源映射，不证明美术质量；`RAW -> PNG` 必须作为测试副本风险记录。

## 标准目录结构

```text
CCZModStudio_Exports\RS_PixelDesign\<PackageId>\
  refs\
    design\...
    format_candidates\...
    selected_format\...
  drafts\
  materials\
    r_actor\front.bmp
    r_actor\back.bmp
    s_unit\mov.bmp
    s_unit\atk.bmp
    s_unit\spc.bmp
  reports\
    manifest.json
    reference_selection_report.json
    mcp_generation_report.json
    visual_acceptance_report.md
```

## 本机像素编辑 MCP 主线

当目标是“不使用 RetroDiffusion / Image Studio / 系统 imagegen，而是在本机制作曹操传 R/S 成品”时，优先使用本机像素编辑 MCP 主线。该路线不调用外部生图模型，也不使用 Pillow、几何脚本或仓库外脚本生成成品；所有编辑必须经由工具整合包 C# Core/MCP 服务执行，并留下结构化报告。

适用入口：

1. `create_rs_pixel_edit_workspace`
   - 输入 `package_id`、`display_name`、`unit_type`、`design_image_path`、`format_reference_root`。
   - `design_image_path` 只登记为设计观察图，用于记录身份、甲胄、冠饰、披风和气质，不读取错误武器关系。
   - `format_reference_root` 必须包含 `front.bmp`、`back.bmp`、`mov.bmp`、`atk.bmp`、`spc.bmp`，或包含 `materials\r_actor` / `materials\s_unit` 结构。
   - 工具只复制本地 BMP 到 `refs/selected_format`、`workspace/strips` 和 `materials`，不写游戏资源。
2. `build_rs_pixel_edit_plan`
   - 为已有工作区生成确定性的本机像素编辑 recipe。
   - recipe 是可审计 op 清单，不是自然语言提示词，也不会调用外部模型。
3. `apply_rs_pixel_frame_edits`
   - 对指定条带和帧执行有限像素操作。
   - 当前支持 `recolor_palette`、`clean_face_box`、`erase_weapon_residue`、`erase_effect_residue`、`erase_rect_to_magenta`、`draw_spear_axis`、`draw_spear_tip`、`draw_spear_effect`、`repaint_armor_blocks`、`repaint_cape_blocks`、`magenta_key_cleanup`、`copy_region_from_reference`。
   - `erase_weapon_residue` 必须是选择性清理，只擦武器/亮色残留像素，不能整块抹掉人物身体；旧白灰斩击弧优先用 `erase_effect_residue`；只有明确要清空小块背景时才允许用 `erase_rect_to_magenta`。
   - 每次操作必须写入 `reports/edit_log.jsonl`，记录目标、帧号、bbox、变更像素数、用途和风险。
4. `export_rs_pixel_contact_sheets`
   - 输出原始帧顺序的 4x/8x 接触表。
   - 接触表可标注脸部 safe box 和枪轴线，用于人工检查，而不是代替视觉验收。
5. `validate_rs_pixel_edit_workspace`
   - 先调用既有 `validate_rs_pixel_material_package` 做五件套格式门禁。
   - 追加本地帧非空、近洋红、脸部风险、单枪风险检查。
   - 该工具仍是只读验证，不调用 `replace_*`。

本机像素编辑主线的标准包名示例：

```text
CCZModStudio_Exports\RS_PixelDesign\SunCe_LocalPixelEditor_SingleSpearCavalry_v1
```

孙策单枪枪骑兵在该路线中的固定边界：

- `孙策.png` 只作为设计观察图：黑金重甲、金冠束发、红黑披风、主将气质；其中枪剑混合、第二武器、剑弧都视为污染。
- 格式参考必须从本地真实 6.5 R/S 导出，作为动作骨架、短身比例、帧序和像素风格参考。
- 先擦除或隔离旧武器残留，再画唯一长枪层；不得只在旧图空白处叠一把枪。
- 脸部 safe box 内禁止暗红披风、暗红甲片、红棕阴影和武器特效污染。
- 视觉确认前不写测试副本；任何情况下不直接写正式基底。

## MCP-first 制作流程

1. 清理输入：
   - 角色形象图只负责身份、服装、主色、轮廓和气质。
   - 若形象图内有错误武器、双武器或旧特效，必须在 brief 中显式排除。
   - 武器层以 `unitType` 和文字 `weapon_brief` 为最高优先级；当 brief 明确指定“单枪/长枪/长矛”等武器时，形象图中的剑、第二武器、交叉武器和宽白剑弧一律视为污染，不参与生成。
2. 选择真实格式参考：
   - 优先从 `基底` 目录的 6.5 项目导出真实 R/S。
   - 若 `export_bmp_assets` 因缺失 `tsb` 无法导出，不接受灰图；改用已有真实导出样本或修复调色板后重试。
   - 旧失败包、程序化样例和已删除孙策包不能作为正向格式/风格参考。
3. 选择制作主线：
   - 若用户要求“本机像素编辑 MCP”或明确排除外部模型，使用上面的 `create_rs_pixel_edit_workspace` -> `build_rs_pixel_edit_plan` -> `apply_rs_pixel_frame_edits` -> `export_rs_pixel_contact_sheets` -> `validate_rs_pixel_edit_workspace`。
   - 若用户允许外部 AI 草稿，才进入 `build_rs_pixel_character_design` / `draw_ccz_image_asset` 参考图生成路线。
   - 两条路线都必须经过人工视觉验收；格式通过和 MCP 预览通过都不能替代视觉质量。
4. 构建 MCP 生成计划：
   - `build_ccz_image_prompt` / `draw_ccz_image_asset` 必须记录 `reference_image_paths` 和 `reference_roles`。
   - `reference_roles` 推荐顺序：`design`、`format_action`、`style_optional`。
   - 外部 AI 草稿路线可使用 RetroDiffusion；Image Studio 只允许非 R/S 或明确降级的草稿回退。
   - 人物整包优先调用 `build_rs_pixel_character_design`，由它统一生成 references、prompt plan、S 4x6 动作表计划、R `2列x20行` 正背动作表计划和包内报告。
   - 如果聊天客户端暴露的 MCP 工具表没有 `build_rs_pixel_character_design` 或 `reference_image_paths`，先刷新/重启 MCP；必要时通过 `工具整合包\MCP配置\start-ccz-mcp.ps1` 直连最新版 JSON-RPC。不得改走旧 schema 的纯文本生成。
5. 生成与筛选：
   - 先生成 S 4x6 动作表，再由 MCP 后处理为 `mov/atk/spc`。
   - 再生成 R `2列x20行` 正背动作表：左列 `front`，右列 `back`；MCP 后处理只切成两条 `48x1280`，不得把单张立绘复制 20 帧。
   - R 背面必须单独绘制，不得正面镜像；如果上游只给单幅图或静态重复帧，必须判为草稿失败。
   - 如果输出像现代像素贴纸、程序化几何块、旧武器残留、双武器或非曹操传风格，直接废弃该轮，不做本地脚本补画。
   - 如果 RetroDiffusion/API key 缺失，生成停止在 blocked/pending；这时 `materials` 必须保持为空，不能创建占位 BMP、fake BMP 或脚本绘制 BMP。
6. 验收：
   - 先肉眼看原始尺寸和放大对比图，再做尺寸、洋红键、非空帧、类型专项和 MCP 预览。
   - 视觉验收不通过时，不得进入样例索引。

## 样例库规则

- 继续制作复杂 R/S 前，先建立对应兵种的样本学习工作区。枪骑兵当前 MVP 输出为：

```text
CCZModStudio_Exports\RS_PixelDesign\_sample_learning\spear_cavalry_mvp
```

- 样本学习阶段只允许做只读采样、归一化、指标提取、contact sheet、人工标注模板和报告；不得写游戏基底、不得写测试副本、不得生成目标角色成品、不得修改 `local_sample_index.json`。
- `build_rs_pixel_sample_learning_mvp` / `--rs-pixel-sample-learning-smoke` 的机器分类只是筛选辅助，不能直接把 `positive_candidate` 视为正样本。人工必须先看 `candidate_review_sheet_x6.png`，并在 `annotations/candidate_annotations_template.csv` 或对应 JSON 中标注。
- 孙策枪骑兵下一版启动前，必须至少引用 2 个人工确认的正样本、1 个可用的部分参考和 1 个明确反例约束。不得再把 `R100/S90` 或任意单一格式参考当成全部绘图来源。
- 旧 `SunCe_LocalPixelEditor_*`、`SunCe_MCP_*`、程序化/烟测包在样本学习中只能作为 `negative_case` 或反例，不得转入正向索引。
- 2026-07-06 枪骑兵 MVP 当前结果：64 个候选、31 个完整五件套、39 个 R 组、56 个 S 组；机器分类为 3 个 `positive_candidate`、29 个 `partial_reference`、32 个 `negative_case`，无 warning。该结果来自已有真实导出/候选目录的归一化和分析；基底目录已登记为后续扩采入口，但本 MVP 未做人物表驱动的全基底批量导出。
- 当前机器强候选为 `true_mounted_spear_rescan__selected_format_R99_S110__3b9825e074`、`true_mounted_spear_rescan__selected_format_R100_S90__e7eba1a9bf`、`huanwang__row34_R84_S64__036ea5db59`。它们仍只是人工审阅候选，未进入正向样本索引。

### 2026-07-06 枪骑兵 MVP 人工审阅状态

- 机器 `positive_candidate` 不等于人工正样本。人工审阅已确认机器 Top3 均不得作为枪骑兵正样本：
  - `true_mounted_spear_rescan__selected_format_R99_S110__3b9825e074`：典型剑骑兵正面样本，只能作为 6.5 风格轮廓观察。
  - `true_mounted_spear_rescan__selected_format_R100_S90__e7eba1a9bf`：典型弓骑兵正面样本，只能学习部分骑乘动作/气质和特效尺度。
  - `huanwang__row34_R84_S64__036ea5db59`：步兵/武术家/水兵/刀兵类样本，跟骑兵无关，只能参考红衣轮廓和特效尺度。
- 第二轮人工审阅后，当前人工确认枪骑兵正样本为 `S93`、`S83`、`S75`、`S65`。其中 `S93` 可作为 `style_reference;action_reference`，理由是武器、动作和特效属于典型曹操传 MOD 风格枪骑兵表现；`S83/S75/S65` 按用户人工判断登记为 `positive`，复用角色为 `action_reference;style_reference`，污染标记为 `none`。
- 已确认的部分参考包括 `S68`、`S106`、`S110`、`S90`、`S84`、`S85`、`S95`、`S112`、`S113`、`S114`、`S72`、`S82`。其中 `S90/S84/S85/S95` 主要是弓骑/弩骑动作或风格参考，`S68/S106/S110/S112/S113/S114/S72/S82` 主要是刀骑、戟骑、宽刃限制或风格参考。
- 旧 `refs__selected_format__*` 重复项不再作为图像反例审阅，只保留为流程约束：禁止把单一格式参考当底图微改，禁止用“格式通过 + 少量换色 + 叠枪线”冒充重绘。
- 孙策枪骑兵重启的正样本数量门槛已满足：当前已有 4 条人工确认正样本。下一步不能直接开画成品，必须先制定“基于 `S93/S83/S75/S65` 的孙策单枪枪骑兵重启方案”，明确每个样本承担动作、风格、武器或反例约束的角色。
- 本轮以用户人工审阅选择为最终标注依据；机器指标和模型视觉推断不得自动覆盖 `S83/S75/S65` 的人工正样本判断。

- `local_sample_index.json` 只允许登记通过以下全部条件的样例：
  - MCP 参考图输入链路生成。
  - 五件套格式通过。
  - 类型专项报告通过。
  - MCP 只读预览通过。
  - 人工视觉验收确认像曹操传 6.5 R/S。
- `sourceMode=procedural_pixel_reference` 的样例不得进入正向样例库。
- fake upstream / smoke-test 输出只用于验证工具链协议和后处理；即使它们生成了五件套 BMP，也不得进入样例索引、不得复制进正式素材包。
- 旧 `SunCe_from_HuanWang_v1/v2/v2_1/v3/v4` 和 `SunCe_SingleSpearCavalry_v1` 已作废并删除；不得在知识库、规划卡或提示词中作为正向复用依据。
- 旧经验只保留为原则：程序化图、局部改色、叠新武器、格式通过报告，都不能代表曹操传 R/S 风格成品。

## 孙策 MCP 单枪枪骑兵 v1 经验

- 新包：`CCZModStudio_Exports\RS_PixelDesign\SunCe_MCP_SingleSpearCavalry_v1`。
- `孙策.png` 被复制到 `refs/design/sunce_design.png`，只作为角色身份、黑金重甲、冠饰、披风和主将气质参考。
- 选定格式参考：`refs/selected_format/S64_huanwang_format_action`，只作为骑乘/短身/动作语法参考；旧 S67 孙策污染样例不用作主参考。
- 本次已为 MCP 源码补齐 `reference_image_paths` / `reference_roles`，RetroDiffusion 请求会携带 `reference_images` base64 数组；需要重启 MCP server 后工具元数据才会暴露新参数。
- 本次已关闭旧 `r_actor` 静态条带后门：新源码要求上游输出 `2列x20行` R 正背动作表，后处理输出 `front.bmp/back.bmp`；不会再把单张立绘缩放后纵向复制成假 R 成品。
- 本次新增 fake RetroDiffusion 端到端烟测，已验证两次生成调用都会携带两张 `reference_images`，并能通过同一 MCP 后处理路径产出五个 BMP 角色素材位；该烟测只验证协议和后处理，输出已删除，不能视为孙策成品。
- 本次修正武器优先级：`weapon_brief` 明确指定单枪时，`孙策.png` 里的枪剑混合、第二武器、剑弧式白光都不再作为武器输入。
- 当前环境未配置 `RETRO_DIFFUSION_API_KEY`，因此生成被正确阻断；不得回退本地 Pillow/程序化绘制。
- 2026-07-05 再验证：通过 `start-ccz-mcp.ps1` 启动最新版 MCP 后，`tools/list` 返回 154 个工具并确认存在 `build_rs_pixel_character_design`。随后以 `generate_now=true`、`dry_run=false`、`design=孙策.png`、`format_action=S64_all_frames_x4.png` 发起真实生成请求；因运行环境缺少 RetroDiffusion 凭据，调用失败且没有产出五件套。此状态必须记录为 blocked，不得视为“模型已画坏”或“可本地补画”。

## 枪骑兵专项规则

- 只允许一把长枪/长矛。
- 禁止剑、短刃、腰剑、背剑、第二枪、双武器和宽白剑弧。
- 每个关键帧只能有一条主长枪轴线。
- 枪必须能读出枪尾、握持点、枪尖。
- 枪芒必须围绕枪尖，不得沿披风边、马身边、旧剑弧或第二武器位置发光。
- 攻击帧必须表现蓄力、突刺、挑击、枪芒爆发、收枪的连续枪尖位移。

## 风险与边界

- 本机像素编辑 MCP 是当前“无外部生图”请求的主线；RetroDiffusion / Image Studio 只是可选 AI 草稿路线，不是 R/S 成品唯一入口。
- RetroDiffusion 输出仍是草稿，不保证一次成品；使用外部生图时，生产入口必须是 MCP 图像资产链路。
- 若当前 MCP 未暴露参考图参数，先升级/重启 MCP；不能把“文本描述形象图”当作等价替代。
- 若 RetroDiffusion/API key 不可用，外部生图路线停止在 pending 状态；只有用户明确选择本机像素编辑 MCP 主线时，才允许用 `apply_rs_pixel_frame_edits` 等 MCP 像素编辑工具继续制作。
- MCP 的 R/S 替换预览会把目标条目从 `RAW` 变为 `PNG`；正式采用前必须在测试副本实机验证。

## 孙策本机像素编辑 v2.1 经验

- `SunCe_LocalPixelEditor_SingleSpearCavalry_v2` 证明了整块 `erase_weapon_residue` 会把攻击帧人物抠出空洞；该用法不得再作为默认修武器方式。
- `SunCe_LocalPixelEditor_SingleSpearCavalry_v2_1` 改为选择性擦亮色武器残留，并新增 `erase_effect_residue` 清白灰/浅粉旧斩击弧，能保留人物主体和 6.5 原生像素感。
- v2.1 仍不是合格枪骑兵成品：选用的格式参考更像步行长杆武将，不提供真实马身/骑乘轮廓；后续枪骑兵必须先重新筛选“有马身、有长杆、旧剑弧少”的真实 6.5 格式参考。
- 不要为了让验证报告变绿而用大矩形 `clean_face_box`、大面积 `recolor_palette` 或整块擦除；视觉验收优先于粗糙启发式分数。

## 孙策真实骑乘参考重筛 v2 经验

- 当前本机像素编辑候选包：`CCZModStudio_Exports\RS_PixelDesign\SunCe_LocalPixelEditor_TrueMountedSpear_v2`。
- 该包只使用本地 CCZModStudio MCP 像素编辑工具，不使用 RetroDiffusion、Image Studio、系统 imagegen、Pillow 或程序化几何脚本制造成品。
- `S110` 已判定为失败/局部参考：它在少数侧面帧看似骑乘，但完整 `mov/atk/spc` 检查后发现大量正背和攻击帧读作步行武将，导致输出“半骑乘、半步行”。枪骑兵格式参考不得只凭单帧或局部侧面帧判断。
- 重筛 S 候选时必须看完整未标注 contact sheet，重点看 `mov/atk/spc` 的整体骑乘语法。曹操传原生骑乘条带允许部分正背帧看不到完整马身，不能机械要求每帧都有整匹马；应重点判断侧面、冲锋和攻击节奏是否为骑乘。
- 本轮强候选为 `S84`、`S85`、`S90`、`S99`；当前选定 `S90`。理由是它比 `S110` 更像真实骑乘动作，黑红骑将气质更接近孙策，且比偏冷色的 `S99` 更适合黑金/红黑设计。
- R 参考当前选定 `R100`，优先于 `R99`。证据是 `R100` 空/稀疏帧略少，红黑披甲将领气质更接近孙策。
- 选定参考五件套放在 `CCZModStudio_Exports\RS_PixelDesign\_reference_samples\true_mounted_spear_rescan\selected_format_R100_S90`。
- `copy_region_from_frame` 已加入本机像素编辑 MCP，只允许用于填补已确认的空/稀疏原生槽位，并必须写入 `edit_log.jsonl`。它不得用于伪造整套重复帧，也不得替代真正像素编辑。
- 本轮发现并修复了 R 背面 `11/12` 只剩斜枪的硬错误：通过 MCP `copy_region_from_frame` 分别从 `back:13` 和 `back:16` 复制完整原生背面帧，再做 `magenta_key_cleanup`。修复后空帧数为 `0`。
- 未标注 `x6` contact sheet 才是视觉判断基准；带标注图包含脸框和枪轴辅助线，不能当作实际像素看。
- 当前校验结果为格式通过、空帧通过，但 `SingleSpearRiskFrames` 和 `FaceRiskFrames` 仍有启发式噪声。金甲、白马、枪芒会被亮色簇检测误报，固定脸框也会误报骑乘侧背帧；不要为了让报告变绿而大擦、大矩形补脸或破坏原生像素感。
- 该包当前是 review candidate，不得进入 `local_sample_index.json` 正向样例，直到人工视觉确认、单枪读法确认、MCP 只读预览和后续实机测试路径完成。

## 关联文档

- `07-数据表与资源/人工智能绘图-图像工作室-模型上下文协议.md`
- `07-数据表与资源/人物R-S像素形象提示词模板.md`
- `07-数据表与资源/人物形象-RS-形象指定器.md`
