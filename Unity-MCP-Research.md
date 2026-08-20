# Unity MCP 开源方案调研（2026-07-26）

> 目标：GitHub 上比较新、维护活跃、能给 Claude Code / Cursor / Copilot 提供 Unity Editor 操作能力的 MCP 方案。
> 本项目 Unity 版本 **6000.4.8f1**，下列所有方案的版本门槛都满足（含官方方案）。
> 星数/版本为 2026-07-26 抓取，以仓库实际为准。

## 一览表

| 项目 | Star | 技术栈 | Unity 版本 | 工具数 | 许可证 | 状态 |
|---|---|---|---|---|---|---|
| [Unity 官方 Unity MCP](https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.0/manual/unity-mcp-overview.html) | 官方包 | C#，随 `com.unity.ai.assistant` | 6000.0+ | 官方内置集 | Unity 包协议 | `2.0.0-pre`，2026-05 起公开测试 |
| [CoplayDev/unity-mcp](https://github.com/CoplayDev/unity-mcp) | ~12.8k | C# + Python(uv) | 2021.3 LTS → 6.x | 47 | MIT | 活跃，v10.1.0 (2026-07-13) |
| [IvanMurzak/Unity-MCP](https://github.com/IvanMurzak/Unity-MCP) | ~3.7k | C# + CLI(npm) | 现代 Editor + **Runtime** | 70+ | Apache-2.0 | 活跃 |
| [CoderGamester/mcp-unity](https://github.com/CoderGamester/mcp-unity) | ~1.8k | C# + Node18 (WebSocket) | 6+ | 基础集 | MIT | 活跃 |
| [AnkleBreaker-Studio/unity-mcp-server](https://github.com/AnkleBreaker-Studio/unity-mcp-server) | ~346 | Node18 + Unity 插件 | 2021.3 LTS+ | **330+**（30+ 类） | AnkleBreaker Open License v1.0（需署名） | 活跃，v2.35.2 |
| [akiojin/unity-cli](https://github.com/akiojin/unity-cli) | ~79 | Rust 单二进制 + TCP | — | 101 API + 14 skills | MIT | 活跃，**已弃用 MCP** |
| [notargs/UnityNaturalMCP](https://github.com/notargs/UnityNaturalMCP) | ~163 | C# (官方 MCP SDK) | 6000.0+ | 5 内置 + 易扩展 | MIT | **2026-07-01 已归档只读** |
| [game4automation/io.realvirtual.mcp](https://github.com/game4automation/io.realvirtual.mcp) | 小众 | C# | — | 数字孪生/仿真向 | MIT | 2026 新项目 |

---

## 一、共性的坑（选哪个都要面对）

这些是"AI 经桥接操作 Unity Editor"这套机制本身的问题，不是某个项目的 bug。

### 1. Editor 必须开着，失焦会卡住
Unity 默认后台不 tick，桥接大多挂在 `EditorApplication.update` / `EditorWindow` 上。你切到浏览器时 AI 发的请求可能一直挂着不返回。
→ 开 `Edit > Preferences > General > Interactive Mode`，尽量保持 Unity 前台。

### 2. 改 C# 就要等重编译 + domain reload，桥接会断
AI 改脚本 → Unity 重编译 → domain reload → 静态状态清空 → 长连接断 → AI 拿到超时，然后"以为"失败去重试，同一个改动被做两遍。
→ 靠谱的方案会给 AI 一个"查编译状态"的工具主动等（自研桥接的 `get_unity_compile_status` 就是干这个）。CoplayDev 和 IvanMurzak 处理了，轻量方案往往没处理好。
→ **实践上：让 AI 用文件编辑工具直接改 .cs，不走 MCP 的 script 工具**，改完 refresh + 查编译状态，链路最稳。

### 3. 工具表吃 token
MCP 在会话开始把所有工具的完整 JSON Schema 注入 context。47 个还行，**330+ 工具光工具表就能吃掉几万 token**，直接压缩你能给 AI 的实际上下文。
→ 这是 akiojin 放弃 MCP 改走 CLI + Skill 的直接原因（宣称省 90%+）。
→ 只挂当下用得上的那一个，别同时挂三个。

### 4. Prefab / 序列化是通用弱项
嵌套 Prefab、Prefab Variant、`[SerializeReference]`、GUID 资源引用赋值，社区方案普遍做得浅：能建 GameObject、设基础字段，但"给某组件的某字段挂上正确的 Sprite / ScriptableObject"经常失败或静默写错。
→ 这正是本项目自研 Prefab MCP 的价值所在（`find_asset_candidates` + `assign_asset_reference` + 事务式 `edit_prefab`），**不要拿通用方案替换它**。

### 5. 不走 Undo 的写入 = 不可回滚
不少工具直接改内存对象再 `AssetDatabase.SaveAssets()`，没有 `Undo.RecordObject`。AI 改错了 Ctrl+Z 撤不回来。
→ 动 Prefab/场景前先 commit 一次，靠 git 兜底；优选带 dry-run / 自动备份的方案。

### 6. 场景和 Prefab 的 diff 会很难看
AI 批量动 GameObject 后，.unity/.prefab 的 YAML diff 动辄几百行、fileID 顺序全变，团队协作 merge 冲突几乎必然。
→ AI 动过的资源，提交前自己过一遍 diff；别让 AI 碰别人正在改的文件。

### 7. AI 看不到画面，UI 布局纯靠瞎猜
纯数据型 MCP 的通病：AI 读得到 anchoredPosition 和 sizeDelta，但不知道界面看起来错没错。
→ 带截图能力的方案价值高一档（自研桥接有 `capture_unity_screenshot`）。

---

## 二、各方案的使用特点

### CoplayDev/unity-mcp —— 社区事实标准，稳妥首选
**优势**
- 生态最大（12.8k star / 700+ fork），wiki + Discord 完整，踩坑有人陪。
- 47 个工具粒度克制，token 开销可控。
- 处理了编译等待和 reload 重连。
- Unity 2021.3 起都支持 —— 公司里多个不同版本的项目能共用一套。

**劣势**
- 要装 Python 3.10+ 和 `uv`，Windows 上环境问题是最常见的报错源。
- 脚本编辑走文本替换，大改容易出错。
- Prefab 精细操作弱。

**适合的活**：搭测试场景、批量改一批 Prefab 的公共字段、跑 EditMode 测试看哪个红了、出包。

### IvanMurzak/Unity-MCP —— 扩展性最好 + 唯一支持 Runtime
**优势**
- 加一个特性就注册成 AI 工具 —— 把项目已有的编辑器工具（LocCsv、ExcelTable、CsvConfigAutoSync）暴露给 AI 的成本最低，**这对本项目价值最大**。
- **Runtime 支持**：能在 PlayMode / 编译后的包里让 AI 读运行时状态、调数值、驱动 NPC。玩法调参和 bug 复现很好用。
- 70+ 工具覆盖 project/assets、scene/hierarchy、scripting/editor、profiling 四类。

**劣势**
- 生态和文档不如 CoplayDev。
- 工具多 → token 开销比 CoplayDev 高一截。
- Runtime 那套要额外接，注意别在正式包里带出去。

**适合的活**：AI 帮调数值平衡、进 PlayMode 复现 bug、把自己写的编辑器工具接给 AI。

### CoderGamester/mcp-unity —— 轻量
**优势**：Node + WebSocket，装完就跑，无 Python 依赖，链路短延迟低。
**劣势**：工具集偏基础，场景搭建 + 材质就到顶；编译等待/重连健壮性不如上面两个。
**适合**：个人项目，或只想"让 AI 看一眼 Console 报错、跑个测试"。

### AnkleBreaker-Studio/unity-mcp-server —— 专用重活
**优势**：唯一覆盖 Shader Graph / Amplify / Terrain / NavMesh / 粒子 / MPPM 多人 playmode / Unity Hub / 多平台出包。做地形、做联机自动化测试，别家真没有。
**劣势**
- 330+ 工具的 token 开销是硬伤，实际用起来 context 会明显吃紧。
- ⚠️ **许可证要求商业产品署名**（个人/教育免除），禁转售再授权 —— 选型前必须让负责人确认。
- star 少，踩坑没人陪。

**适合**：当专用工具按需临时挂载，不适合常驻。

### akiojin/unity-cli —— 架构参考价值 > 直接使用
**优势**：Rust 单二进制启动快，CLI + Skill 按需加载，token 效率最高。
**劣势**：不是 MCP，只服务 Claude Code；生态几乎没有。
**对我们的价值**：自研桥接工具数继续涨的话，"CLI 脚本 + Skill 按需加载"比"MCP 塞满工具表"更省 context。项目里的 `LocCsv.ps1`、`ExcelTable.ps1` 本质就是这个思路，方向是对的。

### Unity 官方 Unity MCP —— 零配置试水
**优势**：官方维护，升版本不会挂；和 Editor 内 Assistant 共享工具语义；`Project Settings > AI > Unity MCP` 开关即用，零环境配置。你这个 6000.4.8 直接能用。
**劣势**：仍是 `2.0.0-pre`；工具集封闭不好自己加；要走 Unity AI Gateway，公司网络/合规上可能有阻碍；能力覆盖目前不如社区方案。

### notargs/UnityNaturalMCP —— 已归档，仅供参考
设计干净：直接用官方 ModelContextProtocol C# SDK，stdio + Streamable HTTP 双支持，C# 侧加特性即新工具。内置只 5 个工具（RefreshAssets / Get-ClearConsoleLogs / RunEdit-PlayModeTests）。**2026-07-01 归档只读**，别新采用，但它的 C# 侧 SDK 用法值得抄。

### game4automation/io.realvirtual.mcp
面向数字孪生/工业仿真，通用游戏开发用不上。

---

## 三、落地建议（本项目）

现状：自研 **UnityMcp / Prefab MCP**（`Assets/0 Core/1 Script/Tool/Editor/UnityMcp/`），HTTP + PowerShell MCP，专注 Prefab 读改和事务式批量编辑。

1. **保留自研 Prefab MCP 作为主力** —— Prefab 精细编辑 + 事务/dry-run/备份 + 截图，这三点社区方案都不如它。
2. **补一个通用能力方案**（二选一，别都装）：
   - 想稳 → **CoplayDev**（生态最好，编译等待处理到位）
   - 想把项目已有编辑器工具批量接给 AI、或要 PlayMode 运行时调试 → **IvanMurzak**
3. **同时只挂一个通用方案**，避免工具表 token 叠加。
4. **脚本改动始终走文件编辑**，不用 MCP 的 script 工具；改完 refresh + 查编译状态。
5. **动场景/Prefab 前先 commit**，AI 改完自己过 diff。
6. AnkleBreaker 只在做地形/联机时临时挂载，且先确认署名条款。

---

## 参考链接

- [CoplayDev/unity-mcp](https://github.com/CoplayDev/unity-mcp) · [OpenUPM](https://openupm.com/packages/com.coplaydev.unity-mcp/) · [Wiki](https://github.com/CoplayDev/unity-mcp/wiki)
- [IvanMurzak/Unity-MCP](https://github.com/IvanMurzak/Unity-MCP)
- [CoderGamester/mcp-unity](https://github.com/CoderGamester/mcp-unity)
- [AnkleBreaker-Studio/unity-mcp-server](https://github.com/AnkleBreaker-Studio/unity-mcp-server)
- [akiojin/unity-cli](https://github.com/akiojin/unity-cli) · [已弃用的 unity-mcp-server](https://github.com/akiojin/unity-mcp-server)
- [notargs/UnityNaturalMCP](https://github.com/notargs/UnityNaturalMCP)（已归档）
- [game4automation/io.realvirtual.mcp](https://github.com/game4automation/io.realvirtual.mcp)
- [Unity 官方博客：Unity MCP Server 上手](https://unity.com/blog/unity-ai-mcp-how-to-get-started) · [官方文档](https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.0/manual/unity-mcp-get-started.html)
- [MCP 太贵？用 CLI 替代能省 94% token](https://www.uucode.org/blog/mcp-cli-94-token)
