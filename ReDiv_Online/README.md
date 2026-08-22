# ReDiv

Unity 客户端。服务端在同级目录 `../ReDiv_Server`（SpacetimeDB 模块）。

- Unity 6000.4.8f1
- 框架代码在 `Assets/Scripts/Framework`（命名空间 `XFramework`，沿用自旧的自用框架）
- 游戏代码在 `Assets/Scripts/Game`
- 网络层在 `Assets/Scripts/Net`：`SpacetimeConnection`（连接）+ `AuthManager`（账号门面，
  UI 只跟它打交道）+ `ModuleBindings/`（由 `spacetime generate` 生成，**不要手改**）
- 编辑器工具统一在菜单 `Tools > XFramework` 下
- 配置表走 Luban，工具在 `ExcelTool/`。同一份 Excel 按 group 分别导出给客户端和服务端，
  服务端那份要走 `Tools > XFramework > 配置` 的第 6 步（导出完还得 `spacetime publish`）

已接好的界面：`CommonUI`（标题：服务器状态 / 账号栏 / 版本号 / 点屏幕）、`LoginUI`、
`PopDialogueUI`。**选人界面还没做** —— 服务端接口已就绪，契约见
[CLAUDE.md](CLAUDE.md) 第 5 节「角色系统」。

技术细节看 [CLAUDE.md](CLAUDE.md)；整体进度和下一步看 [../CLAUDE.md](../CLAUDE.md) 第 5 节。
