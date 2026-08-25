# ReDiv —— AI 协作总纲

新开对话先读这个文件，再按需读子文档。工程结构见 [README.md](README.md)。

**想知道「现在做到哪了、接着该干什么」，直接跳第 5 节。**

---

## 0. 项目定位（最容易搞错的一点）

ReDiv 是**自研玩法**的在线联机游戏。美术素材参考公主连结风格，但**玩法完全自研，与公连差别很大**。

> ⚠️ **不要**从「像某款已知游戏」去推导数据模型、系统设计或玩法机制。
> 需要业务结构时**主动问**，不要按同类游戏的常规套路自己填。
> 服务端目前有**账号系统**和**角色系统**（`ReDiv_Server/spacetimedb/Auth/`、`Character/`）——
> 这两个是通用基础设施，不算玩法。战斗 / 地图 / 背包这类**玩法表一张都还没有**，
> 要加玩法数据结构时先问，别自己按同类游戏的套路建表。
> 角色系统的形态（多角色、选人界面）是用户明确指定「类似 DNF」的，不是我们推的。
> 形态那套（基础 → 一觉 → 二觉，外加战斗中靠宝石切的爆发形态）也是用户 2026-08-24
> 明确定的 —— **注意它 2026-08-24 之前是「专职」，已经废弃**，别照着旧文档或旧提交推。

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


### MCP 可能根本没连上，Pipeline 的 eval 是兜底

`.mcp.json` 里注册了 MCP for Unity，但**它不一定连着**（2026-08-22 的会话里
`mcp__unity__*` 一个都不可用）。所以：

- 不要把「用 `unity_reflect` 验证 API」当成唯一手段，它可能不在手上。
- 等价兜底是 Pipeline 的任意 C# eval，反射照样查得到：

```bash
unity command eval --code 'var t = System.Type.GetType("SpacetimeDB.Identity, SpacetimeDB.BSATN.Runtime"); return string.Join(",", System.Linq.Enumerable.Select(t.GetMethods(), m => m.Name));'
```

这条回路实测能查出 `Identity.ToString()` 返回的是**不带 `0x` 前缀的大写 hex**
（写订阅 SQL 时要自己补 `0x`）—— 这种细节靠猜一定错。

### 端到端验证：直接把编辑器开进 Play 模式驱动

编译通过 ≠ 功能对。Pipeline 可以进出 Play 模式并在运行中执行任意代码，
所以能把整条链路真的跑一遍（这次登录 / 顶号 / 版本弹窗 / 建角色选角色全是这么验的）：

```bash
unity command editor_play
unity command eval --code 'ReDiv.Net.AuthManager.Instance.LoginAsync("alice","secret123"); return "已发起";'
unity command eval --code 'var a = ReDiv.Net.AuthManager.Instance; return a.Username + " " + a.IsLoggedIn;'
unity command editor_stop
```

按钮也能这么点：`ui.transform.Find("路径").GetComponent<Button>().onClick.Invoke()`，
这样连 UI 绑定一起验了。两个注意：

- **eval 是一次性的，异步结果要下一次调用再查**（`await` 不会等你）。
- **`AssetDatabase.Refresh()` 触发重编译会清空控制台**，汇总日志会跟着没。
  想稳定拿到日志就去读 Unity 的 `Editor.log`（在 `%LOCALAPPDATA%/Unity/Editor/` 下，
  含中文，用 `grep -a` 当文本读）。
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

### 服务端的 API 不确定时，必须真的 publish + call 一次

**编译通过、`spacetime build` 成功、`spacetime publish` 成功，全都说明不了 API 可用。**
模块跑在 wasi-wasm + NativeAOT + 裁剪环境里，很多东西是链接得过、一调就炸。
2026-08-22 这一天就踩了四个，全靠先写个探针 Reducer 实测才没走错路：

| 试的东西 | 结果 |
|---|---|
| `System.Security.Cryptography`（SHA256 / PBKDF2 / FixedTimeEquals） | ❌ 链接过，运行时抛 `SystemSecurityCryptography_PlatformNotSupported` |
| `Assembly.GetManifestResourceStream`（嵌入资源） | ✅ 可用 —— 配置能编进 wasm 靠的就是它 |
| `[SpacetimeDB.View]` + `ViewContext` + 自定义行类型 + 声明主键 | ✅ 全可用，而且底层表一变会实时推给订阅者 |
| SQL `WHERE x IS NULL` | ❌ 400 `Unsupported expression` —— 可空列没法用订阅 SQL 过滤，只能走 View |

做法就是加一个临时 Reducer（`_XxxSpike.cs`）→ publish → `spacetime call` → 看日志 →
**删掉探针再 publish 一次**，别把探针留在 schema 里。

SpacetimeDB 2.8 的写法约定（1.x 老写法会直接报错或静默失效）见
[ReDiv_Server/README.md](ReDiv_Server/README.md)，官方 AI 规则见
[ReDiv_Server/CLAUDE.md](ReDiv_Server/CLAUDE.md)（`spacetime init` 生成，勿手改）。

---

## 4. 硬约束速查

- 玩法自研，**不要照抄同类游戏的数据模型**（见第 0 节）
- 客户端改完 C# **必须**跑编译验证，且**必须**单独查控制台错误
- **服务端用到不确定的 API，先写探针 Reducer 实测**（publish + call + 看日志），
  编译/发布成功不代表运行时可用。已知的坑见 [ReDiv_Server/README.md](ReDiv_Server/README.md)
  「已知坑」和本文件第 3 节
- 客户端表回调**要连 `OnUpdate` 一起挂**：同主键的删+插在同一事务里会被合并成 update，
  只挂 Insert/Delete 会漏（换号登录时界面显示旧账号，实测踩过）
- **接 UGUI 界面前先看** [ReDiv_Online/CLAUDE.md](ReDiv_Online/CLAUDE.md) 第 5 节
  「UGUI 界面开发的坑」—— 装饰图吃点击、亮/灭切的是 `Image.enabled` 不是 `SetActive`、
  `Destroy` 延迟到帧末、节点名和实际位置可能是反的，这几条都实测踩过
- **本工程 Canvas 是 `ScreenSpaceCamera`，不是 Overlay**（2026-08-25 订正）。
  在 `eval` 里算点击坐标必须传 `canvas.worldCamera`，传 `null` 会一个都打不中；
  `capture_game_view` 默认渲 `MainCamera`（UI 在 `UICamera` 上）⇒ 拍出来是纯色背景，
  要整屏得加 `--source screen`。细节见客户端文档第 5 节坑 10
- **服务端表加字段只能加在 struct 末尾**。插到中间会被判成 reorder，publish 直接要求
  手工迁移（`Reordering table xxx requires a manual migration`，实测撞过）。
  追加的字段要带 `[Default(...)]`，已有行才能拿到值、不用清库
- **开发期不做向后兼容**。第一个正式版本发布之前，表结构怎么干净怎么来 ——
  要删列 / 改语义就直接 `spacetime publish --delete-data=always --yes` 清库重发，
  **不要**为了保住开发库里那点测试数据留下废弃字段、`[Default]` 回填、
  「读到 0 就退回默认值」这类兼容分支。那些东西留下来只会误导后面看代码的人。
  正式版发布之后再谈迁移。（2026-08-24 用户明确定的）
- **`[Default(...)]` 只在迁移时给已有行回填，对新插入的行无效**。新字段要有初值，
  必须在 Insert 那里显式赋值。踩过：`Account.CharacterSlots` 只标了 `[Default(4)]`，
  清库后新注册的账号栏位数是 0，一个角色都建不出来
- **改了角色配置（那两张 Excel）跑一次自检**：
  `spacetime call rediv character_config_self_test`。两张表靠 JobId / FormId / UnlockStar
  互相引用，没有编译期检查，配错了只表现成「建不出角色 / 觉醒不了 / 客户端没资源」
- `ReDiv_Online/Assets/Scripts/Net/ModuleBindings/` 是生成物，不要手改
- `ReDiv_Server/spacetimedb/Luban/Generated/`、`Luban/Runtime/`、`Configs/` 也不要手改
  （前者是 Luban 生成物，中间是 vendored 的上游运行时，后者是导出的 bin 数据）
- **改了服务端配置（Excel 里 group 含 s 的列/数据）要走两步**：ConfigTools 第 6 步
  「导出服务端配置」+ `spacetime publish`。配置是以嵌入资源编进 wasm 的，不发布不生效
- `ReDiv_Online/Packages/com.clockworklabs.spacetimedbsdk/` 是**内嵌的打过补丁的分叉**，
  不要"顺手同步回上游版本"，详见该目录下的 `UPSTREAM.md`
- CLI / 数据库 / Unity SDK 三者版本必须同为 2.8.2
- **客户端与服务端的游戏版本号必须一致**（服务端 `Module.ServerVersion` ↔ 客户端
  `Application.version`），不一致客户端会弹窗并禁止登录。改版本号要动四处，
  见 [ReDiv_Server/README.md](ReDiv_Server/README.md) 的「版本号」一节
- 补间动画用 **DOTween Pro**（`Assets/Plugins/Demigiant/`），**不是** PrimeTween
- **项目纯中文，不要再引入多语言**。2026-08-23 整套移除了 Unity Localization /
  gpt-localization / LanguageManager / locale 与 String Table 资产。界面文字和配置表里
  直接写中文原文，别再造「多语言 key」那一层，详见
  [ReDiv_Online/CLAUDE.md](ReDiv_Online/CLAUDE.md) 第 1 节
- 提交与推送只在用户明确要求时做（现在是单仓库，一次提交即可覆盖两边）

---

## 5. 当前进度与下一步（**新对话先看这节**）

最后更新：2026-08-24。

### 国服资源管线（素材任务必须先读）

国服 AA 下载、UnityPy/AnimeStudio 解包、复杂分类、NGUI、Spine 3.6 → 4.3、
怪物状态、VariantCard Shader/Material 与 Prefab/粒子后续路线的完整交接见
[ReDiv_Online/Docs/CN资源解包与还原工作流.md](ReDiv_Online/Docs/CN资源解包与还原工作流.md)。

当前基线是 CN Android `202608171854`；自包含原包在
`D:\AssetsStudio\Rediv\CN_分类完成\_原始数据`。角色 Spine 730 个外观已经完成
4.3，怪物只完成到 3.6。角色 Spine 以 `SpineScaleCheck\fixed_output` 的优衣样本
为基准：创建 3.8 和升级 4.3 时隐藏图片，之后恢复完整画布再导出；导入比例
`0.5` 只在 3.8 工程创建时应用一次。
原游戏 Bundle 回退版本 `6000.0.58f2` 与本工程 Unity `6000.4.8f1` 不可混用。

### 已经能用的

| 系统 | 服务端 | 客户端 | 文档 |
|---|---|---|---|
| 账号（注册 / 登录 / 登出 / 会话 / 顶号 / 免密重连） | ✅ | ✅ | [ReDiv_Server/README.md](ReDiv_Server/README.md)「账号系统」 |
| 版本校验（不一致弹窗 + 禁止登录） | ✅ | ✅ | 同上「版本号」 |
| 角色（多角色 / 创建 / 软删 / 选择 / 选角状态） | ✅ | ✅ 选人界面已完成 | 同上「角色系统」 |
| 形态与觉醒（基础 → 一觉 → 二觉，按**星级**现算；觉醒永久不可逆） | ✅ | ✅ 展示已完成 | 同上「角色系统」 |
| 爆发形态（一个角色多个，战斗中装宝石切换） | 配置就绪 | ✅ 展示已完成 | 同上「角色系统」 |
| 配置表通路（Excel → Luban → 编进 wasm / 进 Addressables） | ✅ | ✅ | 同上「配置表」 |
| 角色美术资源（头像 / 略缩图 / 名字图 / 立绘 / 预览图 / UI Spine / 战斗 Spine） | — | ✅ 两个角色都配好了 | 同上「配置表」 |

客户端界面：`CommonUI`（标题）、`LoginUI`、`PopDialogueUI`、`PopLoadingUI`、
`SelectCharacterUI`（选人：格子 / 单选 / Spine 待机）、`CreatCharacterUI`（创角：
头像列表 / 立绘 / 形态卡翻页 / 全屏立绘）。细节见
[ReDiv_Online/CLAUDE.md](ReDiv_Online/CLAUDE.md) 第 5 节。

### 下一步大概率是这些

1. **两个按钮还没接**：选人界面的「进入游戏」（调 `SelectCharacter`，服务端写完
   `character_selection` 才算进城镇）、创角界面的「创建」（调 `CreateCharacter`，
   还缺一个名字输入框）。
2. **补配置的占位值**：`CharacterJob.Subtitle` 还空着，觉醒等级（15 / 30）是占位数字。
   资源列**别手打路径** —— 用 `Tools > XFramework > 配置 > 角色资源配置` 窗口拖资产写回 Excel。
   改完跑 `spacetime call rediv character_config_self_test` 自检。
3. **升星**：现在只有觉醒（1→3→6 星），4 / 5 星没有来源 —— 要靠养成系统（材料 / 碎片），
   那套还没定，**要动手前先问**。
4. **爆发宝石**：配置已就绪，但「装备宝石切形态」是战斗内行为，
   装备 / 背包 / 战斗表一张都还没有，**要动手前先问**。
5. 城镇 / 地图 / 角色玩法态表 —— 还没设计，**要动手前先问**。

### 本地测试数据（开发库 `rediv` 里现成的）

2026-08-24 因为改形态设定清过一次库，下面这些是重建后的：

| 账号 | 口令 | 名下存活角色 |
|---|---|---|
| `alice` | `secret123` | 影狼（60 级 / 6 星 / 二觉）、祭星者（1 级 / 1 星 / 基础） |
| `Carol_01` | `carol123` | 无 |
| `bob_2` | `密码123带空格 ok` | 无。用来验证中文 + 空格口令能过 |

角色配置：只有 `JobId=1`（凯露，MaxStar=6）。形态五行 ——
基础线 `FormId=1` 魔法士（1 星）/ `2` 魔导士（3 星，30 级觉醒）/ `3` 黑魔法师（6 星，60 级觉醒），
爆发线 `FormId=101` 公主 / `102` 暗黑圣灵。
「影狼」已经二觉到 6 星，可以直接拿来看形态效果。

调星级看形态变化最省事的办法是直接写 SQL（不用走觉醒的等级校验）：

```bash
spacetime sql rediv "UPDATE character SET star = 5 WHERE character_id = 2"
```

（5 星没有单独配行，形象会跟着 3 星那行走 —— 这是设计如此，实测确认过。）

清库重来：`spacetime publish --delete-data=always --yes`（会清掉上面所有数据）。

改测试数据不用写 Reducer，owner 身份可以直接跑 SQL：

```bash
spacetime sql rediv "UPDATE character SET level = 30 WHERE character_id = 9"
```

（这是调形态 / 解锁条件时最省事的办法。注意 SQL **不支持 `IS NULL`**，
可空列没法用 SQL 过滤。）

### 哪些东西是「有意没做」，别当成漏掉了

- 登录失败次数锁定（做不了，原因是事务回滚，见服务端 README「有意没做的事」）
- 改密码 / 找回密码 / 删号 / 改角色名 / 软删恢复 / 扩栏位入口 / 敏感词过滤
- 玩法态表（战斗、地图、背包）—— 玩法未定型，**不要自己建**
- 角色资源用 Odin ScriptableObject 配置 —— 2026-08-23 做过又按需求回退到 Luban 了，
  别再找 `CharacterResourceConfiguration`，那套已删干净
