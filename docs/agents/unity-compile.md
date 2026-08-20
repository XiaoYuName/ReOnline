# Unity 编译规则

适用场景：修改脚本后触发或验证 Unity 编译。

1. 使用独立的 `unity_compile` MCP 的 `force_unity_compile`，不要只依赖 Unity 后台自动编译。
2. 同一项目的编译请求已经由命名 Mutex 串行化；不要绕过 `force_unity_compile` 直接同时发送刷新或编译快捷键。
3. 有待编译脚本时工具会短暂把 Unity 切到前台并可能发送 Ctrl+R；调用前先用 commentary 明确提醒用户，默认会恢复原前台窗口。
4. 读取结果时重点检查 `compiled`、`pendingChanges`、`focused`、`errorCount` 和 `warningCount`。
5. `compiled=false` 且 `pendingChanges=true` 表示磁盘脚本仍未编译，不能当作成功。
6. 只查看上一次编译结果时使用 `read_unity_compile_log`，它不会抢焦点。
7. 详细说明参见 `Assets/0 Core/1 Script/Tool/Editor/UnityMcp/Compile/README.md`。
