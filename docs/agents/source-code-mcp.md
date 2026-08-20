# Source Code MCP 规则

适用场景：搜索、局部读取或安全修改项目内文本与源码。

1. MCP 可用时优先使用 `source_code` 的结构化工具，避免把重复路径和大段终端输出带入上下文。路径可传项目相对路径或项目内绝对路径，返回值统一为项目相对路径。
2. 按 `search_code` → `read_code(matchId)` → `apply_patch` 顺序工作；读取范围保持最小。需要精确行号时传 `includeLineNumbers=true`；并行读取多个文件时传 `compact=true`，避免同时返回重复的 `content`。
3. 修改优先级为：`replace_symbol` 或唯一文本 `replacements` → 带 `expectedOldText` 的行编辑 → 裸行号编辑。锚点仅有缩进或行尾空白差异时，可使用 `matchMode=trimmedLines`，仍必须保持唯一命中。
4. `apply_patch` 与 `replace_symbol` 必须使用同一次 `read_code` 返回的 `sha256`。复杂修改先传 `dryRun=true`。
5. 哈希冲突默认重新读取。仅当文本锚点唯一且用户修改不会被覆盖时，才对 `replacements` 使用 `allowRebase=true`。
6. 多文件相关修改放在同一个事务中；所有行号均基于修改前的快照。
7. 修改 Unity 序列化字段前使用 `inspect_unity_code` 检查 GUID 引用和 `FormerlySerializedAs`。
8. `get_diagnostics` 只读上次 Unity 编译状态；`stale=true` 时按
   [unity-compile.md](unity-compile.md) 使用 `force_unity_compile`。
9. MCP 不可用或任务超出工具范围时，才退回 `rg`、局部文件读取和标准补丁工具。
10. `UnityMcp/SourceCodeMcp~` 是唯一实现；独立 `source_code` 与 UnityMcp 代理共用其源码、
   schema、启动器和构建缓存。源码任务默认使用独立服务；客户端只连接 Unity MCP 时才传
   `-IncludeSourceCodeTools`，并关闭独立注册，禁止同时公布两份相同工具 schema。
11. 不把 Huffman、gzip、Base64 等压缩内容直接交给模型。token 优化使用路径去重、元组、偏移量、
   分页、预算和稳定 ID，响应必须保持可直接理解。
