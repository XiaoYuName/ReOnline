# ReDiv

Unity 客户端。服务端在同级目录 `../ReDiv_Server`（SpacetimeDB 模块）。

- Unity 6000.4.8f1
- 框架代码在 `Assets/Scripts/Framework`（命名空间 `XFramework`，沿用自旧的自用框架）
- 游戏代码在 `Assets/Scripts/Game`
- 网络层在 `Assets/Scripts/Net`（`ModuleBindings` 由 `spacetime generate` 生成，不要手改）
- 编辑器工具统一在菜单 `Tools > XFramework` 下
- 配置表走 Luban，工具在 `ExcelTool/`
