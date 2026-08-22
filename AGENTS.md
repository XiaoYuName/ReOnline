# ReDiv —— AI 协作总纲

新开对话先读这个文件，再按需读子文档。工程结构见 [README.md](README.md)。

---

## 0. 项目定位（最容易搞错的一点）

ReDiv 是**自研玩法**的在线联机游戏。美术素材参考公主连结风格，但**玩法完全自研，与公连差别很大**。

> ⚠️ **不要**从「像某款已知游戏」去推导数据模型、系统设计或玩法机制。
> 需要业务结构时**主动问**，不要按同类游戏的常规套路自己填。
> 服务端目前只有**账号系统**（注册 / 登录 / 会话，见 `ReDiv_Server/spacetimedb/Auth/`），
> 玩法表一张都还没有。要加玩法数据结构时先问，别自己按同类游戏的套路建表。

---

## 1. 仓库地图

```
REDIV/                  ← 会话工作目录，也是**唯一的 git 仓库**（分支 main）
├── ReDiv_Online/       客户端，Unity 6000.4.8f1
└── ReDiv_Server/       服务端，SpacetimeDB C# 模块 → WASM
```

**单仓库。** 2026-08-21 从两个独立仓库合并而来（客户端 10 个提交 + 服务端 2 个提交
都用 subtree 方式保留了，注意旧提交里的路径是当时的原路径，不带 `ReDiv_Online/` 前缀）。
跨客户端/服务端的改动现在一个提交就能覆盖。

忽略规则**分三层**，各自留在原地，不要合并到根：

| 文件 | 管什么 |
|---|---|
| `.gitignore` | 只有根这一层的杂物 |
| `ReDiv_Online/.gitignore` | Unity 全套（`Library/` `Temp/` `obj/` `Build/` `Logs/` `UserSettings/` …） |
| `ReDiv_Server/.gitignore` | `bin/` `obj/` `spacetime.local.json` … |

子目录 `.gitignore` 里以 `/` 开头的模式是相对**该子目录**锚定的，所以
`ReDiv_Online/.gitignore` 里的 `/[Ll]ibrary/` 依然只匹配 `ReDiv_Online/Library/`，
**不要**改写成 `ReDiv_Online/[Ll]ibrary/`。`.gitattributes`（含 Addressables 的
`merge=union`）同理，也各自留在子目录里。

架构上没有独立的游戏服务器进程 —— 服务端是跑在数据库进程内的 WASM 模块。
详见 [README.md](README.md) 的「这套架构的特殊之处」。

---

## 2. 客户端改代码：必须走工具验证

**改完 C# 不能只靠"看起来对"就交付。** 客户端有两套工具可以驱动正在运行的 Unity 编辑器，
必须用它们做编译验证。

### 两套工具

| | Unity Pipeline | MCP for Unity |
|---|---|---|
| 来源 | `com.unity.pipeline` 0.5.0-exp.1（Unity 官方） | `com.coplaydev.unity-mcp`（服务端 v3.4.7） |
| 调用方式 | Bash 里 `unity command <名字>` | 原生 MCP 工具 `mcp__unity__*` |
| 端口 | 7800（token 在 `ReDiv_Online/Library/Pipeline/.unity-pipeline-port`） | 8848（HTTP，`.mcp.json` 已注册） |
| 命令数 | ~130 个细粒度命令 | ~50 个路由型工具（每个带 action 枚举） |

### 默认用 Pipeline 跑编译验证回路

```bash
cd ReDiv_Online
unity command set_autotick --enable true    # 编辑器失焦会停摆，headless 操作前必开
unity command recompile
unity command recompile_status              # 轮询到 completed，看 failed 和 errors[]
```

> **`recompile` 返回 `up_to_date` 不代表没有错误！** 它只表示"没有脚本需要重编"。
> 必须另外查控制台：
> ```bash
> unity command get_console_logs --severity Error --limit 40
> ```
> 或用 MCP 的 `read_console`（action=get, types=["error"]）。

这条回路已实测可靠：塞一个 CS0029 进去，`recompile_status` 会返回
`failed=true` 和带文件/行列/错误码的 `errors[]`。

### 各自的强项（互补，不是二选一）

**只有 Pipeline 有：**
- **热重载** —— `[HotReload]` 标注方法，游戏运行中改方法体不触发域重载（`reload_file`）
- **Project Auditor 静态扫描** —— `audit` → CSV，带 Severity / Recommendation
- **烘焙全套** —— lighting / navmesh / occlusion 各自 bake + status + cancel + clear
- **`set_autotick`** —— 保持编辑器在失焦时 tick，headless 自动化的前提
- ProjectSettings 拆得很细（audio / input / time / physics / quality / graphics / player / tags_layers 各自独立命令）
- Timeline 编辑
- 在 Bash 里直接跑，不依赖 MCP 连接

**只有 MCP 有：**
- **`unity_reflect`** —— 反射查活的 Unity API，**写不确定的 API 之前先用它验证存不存在**
- `unity_docs` —— 拉官方文档
- **结构化脚本编辑** —— `script_apply_edits`（`replace_method` / `insert_method` /
  `anchor_insert` 等）、`apply_text_edits`、`validate_script`、`get_sha`。
  带 SHA256 校验，多方并发改同一文件时防覆盖
- AI 生成资产 —— `generate_image` / `generate_audio` / `generate_model`、Sketchfab 导入
- `manage_camera`（含 Cinemachine）、`manage_ui`（UI Toolkit）、`manage_probuilder`、
  `manage_vfx`、`manage_texture`
- `batch_execute` —— 一次多命令（上限 25），省往返
- 丰富的只读 resource：`mcpforunity://editor/state`、`project/info`、`menu-items`、
  `scene/cameras`、`project/tags`、`project/layers`、`tests`……

**两边都有**（功能相当，用手边顺的那个）：资产 / 场景 / GameObject / 组件 / 预制体 /
材质 / 构建 / 包管理 / 测试 / 控制台 / 菜单执行 / 任意 C# eval。

### 使用注意

- **同一时间只用一条路。** 两者驱动的是同一个编辑器，别让它们同时触发编译或域重载。
  域重载期间 Pipeline 会返回 `Network error` / `No Unity Editor instances found`，
  MCP 会返回 session 相关错误 —— **这是预期行为，重试即可**，不是坏了。
- **MCP 调用前先看 `mcpforunity://editor/state`**，检查 `data.advice.ready_for_tools`。
- MCP 的 `manage_*` 是路由型工具，`action` 只接受固定枚举，猜不中会报错并列出合法值。
  `execute_code` 除了 `code` 还**必须**传 `action`。
- 编辑器开着的时候**不要**跑 `Unity.exe -batchmode`，会抢项目锁。
- 用 `unity command menu` 或 `mcpforunity://menu-items` 可以验证 `[MenuItem]` 是否生效；
  用 `execute_code` / `unity command eval` 查 `System.Type.GetType(...)` 可以验证类型是否真的编进了程序集。

---

## 3. 服务端改代码

服务端不需要 Unity。改完 `ReDiv_Server/spacetimedb/*.cs` 后：

```bash
cd ReDiv_Server
spacetime publish        # 编译 WASM + 上传，自动读 spacetime.json
spacetime generate       # 重新生成客户端 C# 绑定
```

`spacetime generate` 会覆盖写入 `ReDiv_Online/Assets/Scripts/Net/ModuleBindings/`，
**那个目录不要手改**。生成完记得回客户端跑一次编译验证（见第 2 节）。

SpacetimeDB 2.8 的写法约定（1.x 老写法会直接报错或静默失效）见
[ReDiv_Server/README.md](ReDiv_Server/README.md)，官方 AI 规则见
[ReDiv_Server/AGENTS.md](ReDiv_Server/AGENTS.md)（`spacetime init` 生成，勿手改）。

---

## 4. 硬约束速查

- 玩法自研，**不要照抄同类游戏的数据模型**（见第 0 节）
- 客户端改完 C# **必须**跑编译验证，且**必须**单独查控制台错误
- `ReDiv_Online/Assets/Scripts/Net/ModuleBindings/` 是生成物，不要手改
- `ReDiv_Online/Packages/com.clockworklabs.spacetimedbsdk/` 是**内嵌的打过补丁的分叉**，
  不要"顺手同步回上游版本"，详见该目录下的 `UPSTREAM.md`
- CLI / 数据库 / Unity SDK 三者版本必须同为 2.8.2
- 补间动画用 **DOTween Pro**（`Assets/Plugins/Demigiant/`），**不是** PrimeTween
- 提交与推送只在用户明确要求时做（现在是单仓库，一次提交即可覆盖两边）
