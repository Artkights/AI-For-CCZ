# 地址语义与 Hook 契约

任何地址必须同时记录目标 EXE SHA、ImageBase、VA、RVA、文件偏移、PE 节、完整指令、寄存器/内存读写、控制流目标和 xref。VA 与文件偏移只能按 PE section 规则换算，不能用固定减法猜测。

本地 agent 可调用 `explain_exe_address(address=0x004101D9)`，或读取 `ccz://knowledge/effects/address/004101D9`。

| VA | 名称 | 用途 |
|---|---|---|
| `004101D9` | `core_effect_engine` | 双渠道特技判定，EAX 返回布尔值或特效值 |
| `0042518F` | `ability_check_wrapper` | 包装判定，可能进入核心引擎或 fallback |
| `0041301E` | `dual_channel_check` | 个人与装备双渠道检查 |
| `00413009` | `get_effect_value` | 读取特效值 |
| `004061E4` | `data_id_to_runtime_character` | Data 号转人物运行时记录，基址来自 `[004CEA00]`，步长 `48H` |
| `004061F9` | `battlefield_id_to_unit` | 战场编号转单位指针 |
| `0040658F` | `unit_to_runtime_character` | 从 `[unit+00]` 取 Data 号并转人物运行时记录 |
| `00484002` | `strategy_id_to_record` | 策略号转 `004A3E77 + id * 61H` 策略记录；不是人物 Data 转换 |
| `004927F0` | `battle_context_base` | 战斗上下文数据锚点，不是可随意调用函数 |
| `00497AF8` | `strategy_context_base` | 策略上下文数据锚点，不是通用暴击倍率变量 |
| `00497750` | `item_context_base` | 道具使用上下文候选，完整字段仍需当前 6.5 动态验证 |

战术单位 `unit+00` 的低 WORD 是 Data 号，`unit+04` 是出场/显示编号载体。旧工具/报告若把 `unit+04` 命名为 Data 号，必须先降级为历史误名；不得据此生成单位身份或人物指针契约。

## HookContract

Hook 契约至少包含当前 EXE SHA、Hook 地址、触发阶段、单位指针来源、冲突组、完整指令覆盖长度、旧字节、返回地址、原指令策略、EFLAGS/寄存器/栈要求、已知符号和动态验证场景。

内存字段证据只解决“从哪里取值”，不能替代 Hook 契约。即使 `4927F0/497AF8/4A7B20` 字段正确，缺少触发点语义、原指令迁移、返回路径或动态正负样本时，行为补丁仍不得应用。

非 NOP Hook 没有当前 EXE 的契约时必须拒绝预览。旧文档地址和旧版本帖子只能用于检索候选，不能直接成为可写契约。

Hook 至少需要 5 字节，但覆盖长度必须扩大到完整指令边界。被覆盖的真实指令通过 Iced BlockEncoder 重编码到代码洞，重新计算相对 call/jmp/jcc，然后在恢复 Hook 现场后执行并回跳。

覆盖范围含 `ret`、间接跳转、无法完整解码或无法重编码的控制流时，默认禁止自动搬迁。旧字节锁、SHA、契约或代码洞占用不一致时也必须阻断。
