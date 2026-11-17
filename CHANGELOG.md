# Changelog

## 1.0.0 开发预览 — 新版 HUD

- 改用 menus 的 Custom HUD 侧栏，五个分类完整覆盖所有饰品入口；探员/音乐盒合并，印花/挂件/参数设置归入饰品设置。
- 新增 HUD 就绪检测，旧版 menus、classic 后端或未挂载布局时拒绝打开军械库菜单。
- 选择后原位刷新已装备标记，补充 Seed 预设及挂件移除页面，重置使用 HUD 确认样式。
- 更新 menus 共享合约依赖与配套部署说明；复用原有 Panorama addon，无需增加工坊资源。

## 1.0.0 开发预览

- 基于 SwiftlyS2 1.5.3 实现免费武器皮肤、刀具、手套、T/CT 探员、音乐盒、印花与挂件。
- 接入 sws2-cs2menus，支持中英文搜索、分组、范围菜单与直接设置命令。
- 支持 Float、Seed、StatTrak、名称标签、敌方击杀计数与音乐盒 MVP 计数。
- SteamID64 独立存档、顺序后台保存、错误存档保留、会话与实体序列号检查。
- develop / PR 代码检查及测试 ZIP，main 自动递增稳定版本并发布 GitHub Release。
- 内置 13,494 项离线目录，保留 AstraSkins MIT 许可和来源校验信息。
