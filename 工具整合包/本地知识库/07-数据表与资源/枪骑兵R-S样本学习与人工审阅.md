# 枪骑兵 R/S 样本学习与人工审阅

## 目的

枪骑兵 R/S 样本学习不是为了直接生成孙策成品，而是把本地真实曹操传 6.5 R/S 资源整理成可审阅、可复用、可约束的样本体系。机器指标只负责候选筛选；是否能成为正样本，必须由人工查看 `candidate_review_sheet_x6.png` 后确认。

当前 MVP 工作区：

```text
CCZModStudio_Exports\RS_PixelDesign\_sample_learning\spear_cavalry_mvp
```

本流程只允许写审阅标注、报告和知识库；不得写游戏基底、不得写测试副本、不得生成孙策成品、不得修改 `local_sample_index.json`。

## 审阅字段

`visualClass` 表示人工视觉类别：

- `positive`：可作为枪骑兵正样本。必须能读出骑乘、单长杆/枪骑动作、曹操传 6.5 风格和完整动作语法。
- `partial`：只在某些维度可复用，如骑乘动作、风格轮廓、特效尺度或负面约束。
- `negative`：图像本身是明确反例，不应作为制作参考。
- `do_not_review_image_duplicate`：重复导入或旧包中的同一格式图，不再作为图像反例审阅，只保留流程约束。

`reuseRole` 表示后续制作中的复用角色，可多值用分号分隔：

- `style_reference`：参考 6.5 像素轮廓、色块尺度、黑边和整体风格。
- `action_reference`：参考骑乘动作骨架、帧序、重心变化和攻击节奏。
- `effect_reference`：参考特效大小、亮度层级和帧内占位。
- `weapon_reference`：参考枪杆、枪尖、枪芒轨迹；只有人工确认为枪骑兵时才允许使用。
- `negative_constraint`：作为“不应画成这样”的具体限制。
- `process_negative_case`：作为流程反例，例如禁止单参考微改。
- `do_not_use`：完全不复用。

`contamination` 表示污染或限制标签，可多值用分号分隔：

- `none`：当前审阅未发现影响该复用角色的污染。
- `bow_cavalry`：弓骑/弩骑，骑乘动作可参考，武器层不可参考。
- `blade_cavalry`：刀骑/剑骑，风格或动作可局部参考，单枪武器层不可参考。
- `wide_blade`：宽刃、宽白弧或刀剑读法明显。
- `non_mounted`：非骑乘样本。
- `single_reference_overpaint`：旧流程污染，表示把单一格式参考当底图微改。
- `old_sunce`：旧孙策失败包污染。
- `procedural`：程序化/几何绘制污染。
- `other`：需要在 `humanNotes` 中解释。

`mountedReadability`、`singleSpearReadability`、`ccz65Style`、`actionGrammar` 可在后续精审时补充为 `good`、`mixed` 或 `bad`。当前已确认但未逐项细分的候选允许留空，不得用未审阅字段伪造确定性。

## 判定标准

枪骑兵正样本必须同时满足：

- 骑乘可读：原始尺寸下能看出马、骑手和骑乘重心，而不是半步行或纯步兵。
- 单枪可读：能看出一把主长杆、握持关系、枪尖和攻击方向；不得主要读作刀、剑、弓、双武器或宽白剑弧。
- 6.5 风格可读：短身比例、粗轮廓、硬边低色数、洋红键背景和条带动作感接近曹操传 6.5。
- 动作语法完整：移动、攻击、特技帧不是静态贴图；攻击帧有重心、武器轨迹和特效变化。
- 污染可控：没有会误导孙策制作的旧武器、旧孙策失败包、程序化图或单参考微改污染。

部分参考必须明确“只学什么、不学什么”。例如弓骑只能学骑乘动作骨架，不学武器层；刀骑只能学风格轮廓或作为负例，不学枪杆/枪尖。

机器分类只用于排序和发现候选：

- `positive_candidate` 只是机器高分，不等于人工正样本。
- `partial_reference` 可能包含真正的枪骑正样本，例如当前 `S93`。
- `negative_case` 可能是旧流程重复项，应该移出图像审阅，保留为流程反例。

## 孙策重启门禁

在重启孙策单枪枪骑兵 R/S 前，必须满足：

- 至少 2 条人工确认的枪骑兵正样本。
- 至少 1 条部分参考，用于补充动作、风格或特效维度。
- 至少 1 条反例或流程约束，用于防止退化成刀骑、弓骑、双武器或单参考微改。
- 孙策制作计划必须明确引用这些样本的角色：谁负责动作骨架，谁负责风格轮廓，谁负责武器轨迹，谁负责负面约束。
- 不得再把 `R100/S90`、`R99/S110`、旧 `refs/selected_format` 或任意单一格式参考作为唯一底图或主参考。

当前状态：人工确认枪骑兵正样本为 `jiaqiang65_s_candidates__S93__5a4f102bc6`、`jiaqiang65_s_candidates__S83__367069e283`、`jiaqiang65_s_candidates__S75__2fda696834`、`jiaqiang65_s_candidates__S65__c35a392168`。正样本数量门槛已满足，但孙策制作仍需先制定样本引用方案。

## 当前人工结论

| candidateId | visualClass | reuseRole | contamination | humanNotes |
| --- | --- | --- | --- | --- |
| `true_mounted_spear_rescan__selected_format_R99_S110__3b9825e074` | `partial` | `style_reference` | `none` | 典型剑骑兵正面样本；图像可用于 6.5 风格轮廓观察，但不得作为单枪枪骑兵主参考。 |
| `true_mounted_spear_rescan__selected_format_R100_S90__e7eba1a9bf` | `partial` | `action_reference;style_reference` | `bow_cavalry` | 典型弓骑兵正面样本；特效可以参考，但枪骑兵只能学习部分骑乘/气质内容。 |
| `huanwang__row34_R84_S64__036ea5db59` | `partial` | `effect_reference;style_reference` | `non_mounted` | 典型步兵/武术家/水兵/刀兵类样本，跟骑兵无关；可参考红衣轮廓和特效尺度。 |
| `jiaqiang65_s_candidates__S93__5a4f102bc6` | `positive` | `style_reference;action_reference` | `none` | 枪骑兵正面样本；武器、动作和特效属于典型曹操传 MOD 风格枪骑兵表现。 |
| `jiaqiang65_s_candidates__S68__a4f0250d1f` | `partial` | `style_reference` | `blade_cavalry` | 典型刀骑兵，武器为刀；可参考风格轮廓，不可参考单枪武器。 |
| `jiaqiang65_s_candidates__S106__39319790f0` | `partial` | `style_reference` | `blade_cavalry` | 刀骑兵/宽弧读法明显；可参考黑金甲和披风风格。 |
| `jiaqiang65_s_candidates__S110__a53166beac` | `partial` | `negative_constraint` | `blade_cavalry;wide_blade` | 刀骑/宽刃限制样本；用于约束孙策不要退化成刀骑。 |
| `jiaqiang65_s_candidates__S90__732876a11e` | `partial` | `action_reference` | `bow_cavalry` | 弓骑兵限制样本；可学习骑乘动作骨架，不可学习武器层。 |
| `jiaqiang65_s_candidates__S84__8301fc9319` | `partial` | `action_reference` | `bow_cavalry` | 弓骑/弩骑系，绿色披风版；骑乘动作可参考，非枪骑正样本。 |
| `jiaqiang65_s_candidates__S85__9362a6522b` | `partial` | `action_reference` | `bow_cavalry` | 弓骑/弩骑系，红披风版；骑乘动作可参考，非枪骑正样本。 |
| `jiaqiang65_s_candidates__S95__ebd507f2da` | `partial` | `action_reference;style_reference` | `bow_cavalry` | 标准弓骑兵正样例；枪骑兵只能学习骑乘/风格，不学习武器层。 |
| `jiaqiang65_s_candidates__S112__3f98089313` | `partial` | `style_reference;negative_constraint` | `blade_cavalry;wide_blade` | 骑乘黑金披风风格强，但刀骑/宽刃明显；不学武器和攻击白弧。 |
| `jiaqiang65_s_candidates__S113__1228e11ab6` | `partial` | `style_reference;negative_constraint` | `blade_cavalry;wide_blade` | 绿色系刀骑/宽刃变体；可参考骑乘披风风格，不学武器。 |
| `jiaqiang65_s_candidates__S114__8513c735c4` | `partial` | `style_reference;negative_constraint` | `halberd_cavalry;wide_blade` | 骑乘黑金/黄披风风格可参考，但戟/叉刃和宽白斩击明显，不能直接学武器层。 |
| `jiaqiang65_s_candidates__S72__a89ca6cdf6` | `partial` | `style_reference;negative_constraint` | `blade_cavalry;wide_blade` | 红黑金骑将风格强，但刀斧/宽刃明显；不学武器和白弧。 |
| `jiaqiang65_s_candidates__S83__367069e283` | `positive` | `action_reference;style_reference` | `none` | 人工确认可作为枪骑兵正样本。 |
| `jiaqiang65_s_candidates__S82__10fa7d3a57` | `partial` | `action_reference;negative_constraint` | `blade_cavalry;wide_blade` | 骑乘动作可参考，但钩刀/宽刃明显；不学武器。 |
| `jiaqiang65_s_candidates__S75__2fda696834` | `positive` | `action_reference;style_reference` | `none` | 人工确认可作为枪骑兵正样本。 |
| `jiaqiang65_s_candidates__S65__c35a392168` | `positive` | `action_reference;style_reference` | `none` | 人工确认可作为枪骑兵正样本。 |
| `refs__selected_format__*` | `do_not_review_image_duplicate` | `process_negative_case` | `single_reference_overpaint` | 移出图像反例；仅保留流程约束：禁止把单一格式参考当底图微改。 |

## 逐候选问答流程

继续审阅时每次只看一个候选，问题固定为三项：

1. `visualClass`：`positive` / `partial` / `negative` / `do_not_review_image_duplicate`
2. `reuseRole`：从 `style_reference`、`action_reference`、`effect_reference`、`weapon_reference`、`negative_constraint`、`process_negative_case`、`do_not_use` 中选择，可多选。
3. `contamination` 与 `humanNotes`：说明是否有弓骑、刀骑、宽刃、非骑乘、单参考微改等限制，以及后续制作应该如何使用或避开。

第二轮已审候选：

```text
S95, S112, S113, S114, S72, S83, S82, S75, S65
```

已找到足够的人工确认正样本。下一步应制定孙策重启样本引用方案，而不是继续用单一格式参考直接开画。
