# UnityMcp Prefab 规则

适用场景：修改 Unity Prefab、Prefab Inspector 字段、Prefab 组件引用或 UI 结构。

1. 优先使用项目内的 `UnityMcp/Prefab`，不要直接手改 Prefab YAML。
2. 详细说明参见 `Assets/0 Core/1 Script/Tool/Editor/UnityMcp/Prefab/README.md`。
3. Unity Editor 必须打开当前项目并完成脚本编译。Prefab Bridge 默认监听 `127.0.0.1:58732`，端口被占时会顺延，并把实际端口写进 `Library/PrefabMcpPort.txt`。
4. 改完脚本要用独立运行的 `unity_compile` MCP 的 `force_unity_compile` 让 Unity 真实编译；只查看上次结果时使用 `read_unity_compile_log`。详见 `Assets/0 Core/1 Script/Tool/Editor/UnityMcp/Compile/README.md`。
5. 使用 MCP 前先确认 `unity_prefab_status`，再按顺序使用 `find_prefabs`、`get_prefab_tree`、`get_component_fields`；需要进入 Prefab 编辑态时使用 `open_prefab_stage`。
6. 修改 Prefab 时优先先用 `edit_prefab` 的 `dryRun=true` 预演；响应带 `planId` 时，正式提交只传
   `planId` 与调用方生成的稳定 `idempotencyKey`，无需重发操作。`targetMode=prefabStage`/`openScene`
   不支持预演，但批次失败会自动 Undo 回滚。
7. 精确读写 Inspector 字段时，读走 `get_component_fields`（多个组件用 `targets` 一次读完，配合 `compact=true`），写走 `edit_prefab` 的 `setValue`；`propertyPath` 必须与序列化字段名完全一致。
8. `objectId` 使用 sibling index 路径，例如 `0/2/1`；层级变化后必须重新获取 `get_prefab_tree`，不要复用旧路径。
9. 提交 `planId` 或直接写入前检查目标 Prefab 路径和设置中的写入目录白名单；涉及场景的修改只标记为脏，需由用户在 Unity 中保存。
10. UnityMcp 的 MCP 配置可在 Unity 的 `Tools > Unity MCP > 设置` 中一键生成 `.codex/config.toml` 和 `.mcp.json`；配置更新后重启客户端会话。
