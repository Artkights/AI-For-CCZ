# 制作区帖子备份未决项

## 结论速览

- 本页记录自动抽取阶段无法直接升级为正式知识的项。
- `.doc` 老格式在当前环境缺少转换器时只登记来源；后续可补 Word、LibreOffice 或 antiword 后重跑脚本。
- `star175.rar` 按用户本轮范围要求忽略。

## 忽略项

- `制作区帖子备份/star175.rar`：本轮明确忽略，不进入 sources.csv。

## 待抽取或无内容

- `制作区帖子备份/5.9版更新 bystar175.doc`：待抽取；legacy .doc converter unavailable
- `制作区帖子备份/godtype的最终引擎.doc`：待抽取；legacy .doc converter unavailable
- `制作区帖子备份/star175/Star175引擎比较特殊的整形变量和剧本指令.doc`：待抽取；legacy .doc converter unavailable
- `制作区帖子备份/star175/star5.6及旧版引擎.doc`：待抽取；legacy .doc converter unavailable
- `制作区帖子备份/star175/star5.7&5.8.doc`：待抽取；legacy .doc converter unavailable
- `制作区帖子备份/star175/star5.9.doc`：待抽取；legacy .doc converter unavailable
- `制作区帖子备份/star175/star6.0.doc`：待抽取；legacy .doc converter unavailable
- `制作区帖子备份/月半教程.txt`：空来源/无内容；空txt，仅登记文件存在

## 疑似分页或重复主题

- `制作区帖子备份/star175/star6.0.doc`；`制作区帖子备份/star175/star6.1.docx`
- `制作区帖子备份/godtype/〖各种研究成果〗 - 曹操传MOD制作交流 - 轩辕春秋文化论坛.htm`；`制作区帖子备份/godtype/〖各种研究成果〗2 - 曹操传MOD制作交流 - 轩辕春秋文化论坛(2).htm`
- `制作区帖子备份/各种兵种与宝物特效（不断更新中） - 曹操传MOD制作交流 - 轩辕春秋文化论坛.htm`；`制作区帖子备份/各种兵种与宝物特效（不断更新中）2 - 曹操传MOD制作交流 - 轩辕春秋文化论坛(2).htm`
- `制作区帖子备份/解读KOEI曹操传代码 - 设计与修改 - 轩辕春秋文化论坛.htm`；`制作区帖子备份/解读KOEI曹操传代码2- 设计与修改 - 轩辕春秋文化论坛(2).htm`；`制作区帖子备份/解读KOEI曹操传代码3 - 设计与修改 - 轩辕春秋文化论坛(2).htm`；`制作区帖子备份/解读KOEI曹操传代码4 - 设计与修改 - 轩辕春秋文化论坛(2).htm`

## 后续验证队列

- 地址类来源进入 `Ekd5.exe` 静态复读和 x32dbg 动态命中队列。
- 剧本类来源进入 `.eex` 样本复读、旧剧本编辑器源码对照和实机流程验证队列。
- 资源类来源进入 E5/DLL 资源读取、编号映射和预览复核队列。
- 特效类来源进入当前 6.5 特效号、效果值、触发链和烟测候选队列。
