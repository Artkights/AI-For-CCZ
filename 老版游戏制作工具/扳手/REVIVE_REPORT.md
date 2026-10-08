# Python 扳手战场复活专项报告

## 根因

6.6 画像已经能够从两套标准基底定位战场复活入口 `0x004092C0`，但旧调用链在 `hook.py` 先检查 `can_write("revive")`，随后又要求 `RemoteCallSpec.abi_verified`。由于 6.6 的 ABI 只有静态签名和 wrapper 结构、没有双基底实机证据，`abi_verified=False`，因此线程注入在创建前就被拒绝。界面上的自动复活和“战场复活”按钮还分别引用能力状态，形成同一问题的第二层门禁。

## 当前画像与 wrapper

| 版本 | 复活入口 | wrapper 参数 | 栈清理 | 当前执行策略 |
|---|---:|---|---:|---|
| 标准 6.5 未加密版 | `0x004092C7` | `direction, x, y, data_id` 四个 `u32` | `ret 0x10` | `direct_compat`，入口和参数可执行 |
| 标准 6.6 清儿吕布传 | `0x004092C0` | `direction, x, y, data_id` 四个 `u32` | `ret 0x10` | `direct_compat`，ABI 警告但不阻断 |
| 标准 6.6 新改曹操傳 | `0x004092C0` | `direction, x, y, data_id` 四个 `u32` | `ret 0x10` | `direct_compat`，ABI 警告但不阻断 |

shellcode 仍按旧版顺序把四个参数压栈，调用画像入口，返回 `C2 04 00`。远程代码和数据块都经过分配、完整写入、写后复读、代码页切换为 `PAGE_EXECUTE_READ`、指令缓存刷新、创建线程、等待、关闭线程和释放；普通异常释放资源，超时则保留可能仍在执行的分配、关闭线程句柄并熔断当前会话，必须重新附加后才能继续远程调用。

## 已实现的闭环

- `EngineProfile.access_policy` 默认 `direct_compat`，旧 `force_unverified_66` 映射为同一策略。
- `can_read()`/`can_write()` 忽略证据等级，但要求功能存在明确远程入口、补丁站点、表布局、地址或版本配方；无地址的 `turn_refresh_position` 仍不可用。
- `hook.py` 删除 `abi_verified` 硬阻断，保留 PE32 用户态入口、wrapper、会话写权限和参数边界检查，并把未验证调用写入 `metadata["compatibility_calls"]` 与日志。
- `onLife()` 校验非空槽、`Data ID != 0xFFFF`、`HP=0`、显示状态 `3`、方向 `0..3`、坐标 `0..255`，调用后复读 Data ID、HP 和显示状态。
- `LockThread()` 只扫描我军槽位，按 `(slot, Data ID)` 短期去重；Data ID 变化、目标未恢复、远程线程错误或进程退出会清理/停止自动复活。
- UI 只依据死亡单位状态启用单次复活；未验证状态改为 tooltip/status 警告，不再禁用按钮。

## 尚未完成

- 没有在真实标准 6.5/6.6 进程中完成三次连续手动复活和三名我军自动复活。
- 没有完成原地、改坐标、改方向、战场切换、重新选择单位和行动菜单恢复矩阵。
- 没有建立 6.6 ABI 的运行时寄存器/栈证据，因此 direct compatibility 仍属于风险模式。
- `turn_refresh_position` 没有地址和 wrapper，未实现坐标即时刷新 helper。
- 自动复活仍只处理我军；友军/敌军需要独立策略和 UI 选择。
- 真实进程退出、重复附加、战斗切换和远程超时下的复活专用验收尚未完成。

## 与 C++ 原生版对照

本轮没有修改 `工具整合包/Banshou.Native`。对照结果如下：

- `src/remote.cpp` 的 `RemoteServices::revive()` 与 Python `my_hook.life()` 使用同一 20 字节参数块、四个 `u32` 参数、相同的压栈顺序和 `ret 0x10` wrapper；这是 Python 保留旧行为的主要依据。
- `src/window.cpp::doLife()` 读取选中战场槽位和方向/X/Y 后直接调用远程复活，未做 Python 本轮新增的死亡状态、Data ID、调用后 HP/显示状态复读。
- 原生 `timer()` 只扫描我军，遇到 `Data ID != 0xffff && HP == 0 && visibility == 3` 就提交调用；它没有 Python 新增的 `(槽位, Data ID)` 去重和调用后复读，但发生异常会停止自动复活。
- `src/profile.cpp` 的 legacy 表把 6.5/部分旧版本复活地址列为 `0x004092C7`；Python 对标准 6.6 以本地静态画像的 `0x004092C0` 为准，并禁止跨版本借用地址。C++ 地址表与 Python 6.6 画像未在本轮强行同步，后续应单独做双线 profile 契约迁移。

## 建议验收顺序

1. 复制标准 6.5 和 6.6 游戏目录及存档，分别附加 Python 扳手，保存画像摘要和 `compatibility_calls`。
2. 对同一我军单位执行原地复活、改坐标复活、改方向复活各三次，复读 Data ID、HP、显示状态、坐标和 MP。
3. 开启自动复活，依次制造至少三个我军死亡槽，确认每个槽只提交一次且恢复后可重新选择并打开行动菜单。
4. 切换战场、解绑、重新附加和退出游戏，确认定时器停止、句柄关闭、没有遗留远程分配。
5. 人为让远程线程超时或复读失败，确认 UI 停止自动复活并报告版本、入口、槽位、Data ID 和失败阶段。
