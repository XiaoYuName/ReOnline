# ReDiv 客户端 —— 技术文档

Unity 客户端。服务端在 `../ReDiv_Server`（SpacetimeDB 模块）。
总纲与工具链规则见 [../CLAUDE.md](../CLAUDE.md)，先读那个。

---

## 1. 基本情况

| 项目 | 值 |
|---|---|
| Unity | 6000.4.8f1 |
| 渲染管线 | URP 17.4.0 |
| 唯一场景 | `Assets/Scenes/Root/Root.unity` |
| 框架命名空间 | `XFramework`（沿用自旧的自用框架，工程改名前叫 AFramework / RAFramework / XFramework） |
| 资源加载 | Addressables 2.9.1 |
| 配置表 | Luban（Excel → C#） |
| 补间动画 | **DOTween Pro**（`Assets/Plugins/Demigiant/`） |
| 网络 | SpacetimeDB C# SDK 2.8.2（**内嵌**在 `Packages/` 下） |

> ⚠️ 补间只用 DOTween Pro。PrimeTween 包不在 manifest 里，工程里也没任何代码用它，
> 那份遗留的 `.claude/skills/primetween/` 已于 2026-08-20 删除。

---

## 2. 程序集划分

```
Assets/Scripts/
├── Framework/          asmdef: UnityFramework          ← 自用框架，命名空间 XFramework
│   ├── Addressable/    资源加载封装（AssetsManager）
│   ├── Basic/          UI 基类：UIBase / UISystem / UIPageConfiguration / UIBackground
│   ├── Interfaces/
│   ├── Scripts/        Attribute / Game / Localization / Tools
│   └── Editor/         asmdef: UnityFramework.Editor
├── Game/               无 asmdef → 进 Assembly-CSharp   ← 游戏逻辑
│   ├── Input/
│   ├── ScriptableObject/
│   └── Scripts/
│       ├── AddressableKeys/   UIKeys.cs 等（生成物，见第 4 节）
│       ├── Audio/             AudioManager
│       ├── Backgrounds/
│       ├── Localization/
│       ├── Luban/             Tables.cs 等（生成物，见第 3 节）
│       ├── Resolution/
│       ├── Save/
│       ├── System/            GameManager / GameDataManager / LubanManager
│       ├── Tools/
│       └── UGUI/
└── Net/                无 asmdef → 进 Assembly-CSharp   ← 网络层，见第 5 节
    ├── SpacetimeConnection.cs
    └── ModuleBindings/        生成物，不要手改
```

`Assets/Editor/` 也在 Assembly-CSharp-Editor 里（无 asmdef），包含
`AddressableTools/`、`BuildTools/`、`Luban/`、`Localization/`、`Tools/`、`UGUI/`。

> 注意 `Framework` 有 asmdef，`Game` 和 `Net` 没有。所以 `Game`/`Net` 可以引用
> `UnityFramework`，反过来不行。往 `Framework` 里加代码时别引用 `Game` 的类型。

---

## 3. 配置表（Luban）

Excel 源表在 `ExcelTool/LubanTools/DataTables/Datas/*.xlsx`，生成的 C# 落在
`Assets/Scripts/Game/Scripts/Luban/`（`Tables.cs` 等），运行时入口是 `LubanManager`。

编辑器菜单：`Tools > XFramework > 配置 > LuaConfig`（快捷键 **F6**）。

### 改 Excel 必须用 ExcelTable.ps1，不要用 openpyxl 之类

`ExcelTool/LubanTools/ExcelTable.ps1` 走 **Excel COM 自动化**。原因写在脚本头部：
表里大量单元格是公式（`iconName` / `NameKey` / `DescKey` 用 `=CONCATENATE(...)`，
枚举表用 `=J45*2` 之类的自增），而 Luban 通过 ExcelDataReader **只读公式的缓存值、
不重新计算**。用不打开真正 Excel 的库写完保存，缓存值会丢，Luban 读到空。

代价：本机必须装 Microsoft Excel，执行期间会后台起一个不可见的 EXCEL.EXE。

调用时**用 `&` 调用操作符直接调脚本**，不要套 `powershell -File ...`，否则中文参数可能乱码：

```bash
& ExcelTool/LubanTools/ExcelTable.ps1 -Action Dump -Workbook <路径> -Sheet <sheet名> -MaxRows 20
```

表头约定：第 1 行 `##var`（字段名）、第 2 行 `##type`（Luban 类型）、第 3 行 `##`（中文注释），
第 4 行起是数据。`__enums__.xlsx` 特殊，同一 sheet 里首尾相接堆了多个枚举块。

---

## 4. 资源与打包（Addressables）

编辑器菜单 `Tools > XFramework > 打包`：

| 菜单项 | 实现 |
|---|---|
| Addressable 一键打包 | `Assets/Editor/AddressableTools/AddressableBuildOdinWindow.cs` |
| Windows 一键出包 | `Assets/Editor/BuildTools/PlayerBuildWindow.cs` |
| 自动打包 Addressable | `Assets/Editor/AddressableTools/AddressableBuild.cs` |
| 清空 Addressable 标签内容 | 同上 |

`Tools > XFramework > UI > 生成 UIKeys` 从 Addressables 条目生成
`Assets/Scripts/Game/Scripts/AddressableKeys/UIKeys.cs`，**是生成物**。

### 编辑器菜单约定

项目自己的编辑器工具**全部**挂在 `Tools > XFramework/` 下，分五个子菜单：
`打包/`、`服务端/`、`配置/`、`UI/`、`实用工具/`。排序靠 `[MenuItem(path, false, priority)]`
的 priority 显式指定（不要再用 "1." "2." 这种字符串前缀）：
打包 100~121、服务端 150、配置 200~201、UI 300、实用工具 400~423。
相邻 priority 差 >10 时 Unity 会自动插分隔线。

新加编辑器工具请遵守这个约定，不要再开新的顶层菜单。

### 所有编辑器窗口一律用 Odin

不要写原生 `EditorWindow` + `EditorGUILayout`。约定：

- 窗口继承 `OdinEditorWindow`，配置资源继承 `SerializedScriptableObject`
- 每个暴露字段加 `[LabelText("中文名")]`
- 分组用 `[TitleGroup]` / `[BoxGroup("父/子")]` / `[HorizontalGroup]` / `[FoldoutGroup]`
- 按钮用 `[Button("中文名", ButtonSizes.Large)]` + `[GUIColor(...)]`
- 说明用 `[InfoBox("...")]`，只读展示用 `[ShowInInspector] [ReadOnly]`
- 危险操作 `[GUIColor(0.95f, 0.35f, 0.35f)]` 标红 + `EditorUtility.DisplayDialog` 二次确认
- 忙碌时禁用按钮用 `[DisableIf(nameof(IsBusy))]`

参考实现：`Assets/Editor/ServerTools/SpacetimeServerWindow.cs`、
`Assets/Editor/AddressableTools/AddressableBuildOdinWindow.cs`。

---

## 5. 网络层

```
Assets/Scripts/Net/
├── SpacetimeConnection.cs     连接管理器（命名空间 ReDiv.Net）
└── ModuleBindings/            spacetime generate 生成，命名空间 ReDiv.Net.Bindings
```

### 用法

新建空 GameObject，挂 `SpacetimeConnection`，Inspector 里确认地址（本机
`http://127.0.0.1:2383`，真机/局域网 `http://192.168.10.226:2383`）和库名 `rediv`。

它会在 `Awake` 里自动补一个 `SpacetimeDBNetworkManager`（SDK 靠它在 `Update` 里驱动
`FrameTick()`，WebGL 下还靠它跑消息解析协程，是**必需**组件）。
**不要再手动挂第二个** —— 它是单例，重复挂会抛异常。

连上后 Console 应出现：

```
[Stdb] 正在连接 http://127.0.0.1:2383 / rediv
[Stdb] 已连接，identity=...
[Stdb] 订阅已生效
```

### 服务端操作面板

**`Tools > XFramework > 服务端 > SpacetimeDB 控制台`** —— 一站式：发布模块、生成绑定、
清库重发、看日志（含实时）、跑 SQL、调 Reducer、启停 Docker 容器。
发布完会自动 `AssetDatabase.Refresh()` 并请求重编译。

实现在 `Assets/Editor/ServerTools/`：

| 文件 | 职责 |
|---|---|
| `SpacetimeServerConfig.cs` | 配置（`SerializedScriptableObject`），资源在同目录 `.asset` |
| `SpacetimeCli.cs` | 外部命令串行执行器 |
| `SpacetimeServerWindow.cs` | Odin 窗口 |

命令执行器里有三个不能省的处理，改之前先看注释：
Process 输出在**后台线程**触发（要经 `ConcurrentQueue` + `EditorApplication.update`
回主线程）、CLI 输出带 **ANSI 颜色转义**（要剥掉，否则 Unity GUI 显示成乱码）、
中文输出要显式指定 **UTF8 编码**。

所有命令都在服务端工程目录下执行，服务器地址和数据库名由那边的 `spacetime.json` 决定，
窗口**不**额外传 `--server` / `--database`，避免和配置文件打架。

### 命令行生成绑定

```bash
cd ../ReDiv_Server && spacetime generate
```

`ModuleBindings/` 是**生成物，不要手改**。

### 命名差异（容易踩）

服务端 C# 写 `public static void Ping(...)`，规范名会转成 snake_case：
CLI 调用是 `spacetime call rediv ping`，而客户端绑定里是 `Conn.Reducers.Ping()`。
表名同理：`Accessor = "Xxx"` 决定代码里的 `ctx.Db.Xxx`，SQL / CLI 用的是规范名。

---

## 6. 内嵌的 SpacetimeDB SDK

`Packages/com.clockworklabs.spacetimedbsdk/` 是**内嵌的、打过补丁的分叉**，
不是 manifest 依赖（manifest 里没有它）。

上游 v2.8.2 / commit `ab7646ad9861`，删掉了两个写在泛型类里、因而从未生效的
`[RuntimeInitializeOnLoadMethod] ResetStaticFields`（Unity 每次域重载都会为它们报错）。

> ⚠️ **不要"顺手同步回上游版本"** —— 补丁会没。来源、补丁理由、升级步骤全在
> [Packages/com.clockworklabs.spacetimedbsdk/UPSTREAM.md](Packages/com.clockworklabs.spacetimedbsdk/UPSTREAM.md)。

SDK 编进程序集 `com.clockworklabs.spacetimedbsdk`（来自 `src/*.asmdef`），
不是 `SpacetimeDB.ClientSDK`。

---

## 7. 第三方依赖

**Packages（UPM）**：Addressables 2.9.1、Cinemachine 3.1.7、URP 17.4.0、
Localization 1.5.12、InputSystem 1.19.0、Timeline 1.8.13、Test Framework 1.6.0、
Recorder、MemoryProfiler、Luban、UIEffect、UnmaskForUGUI、UniTask、Spine 4.3（4 个包）、
`com.unity.pipeline` 0.5.0-exp.1、`com.coplaydev.unity-mcp`

**本地包**：`com.redgame.gpt-localization`（`file:` 引用，在仓库内，可以改）

**私有 registry**：`http://192.168.10.226:4873`（Verdaccio），scope `com.lumino` / `com.kyrylokuzyk`

**Assets/Plugins**：DOTween Pro（Demigiant）、Odin Inspector（Sirenix）、
Febucci Text Animator、DamageNumbersPro、PathologicalGames

---

## 8. 工程标识（companyName / productName / 包名）

2026-08-20 从旧工程的值改成了现在这套（旧值：`com.LuminoInc.AFramework` / `剧情游戏`
/ `com.DefaultCompany.*`）：

| 项 | 值 |
|---|---|
| `companyName` | `LuminoInc` |
| `productName` | `ReDiv` |
| `applicationIdentifier`（Standalone 与 Android） | `com.LuminoInc.ReDiv` |

这四处必须同时保持一致，改一处不够：

1. `ProjectSettings/ProjectSettings.asset` —— 全局值
2. `Assets/Settings/Build Profiles/PC.asset` —— 这个 profile **自带一份 PlayerSettings
   覆盖**（YAML 文本快照），注意同级的 `Windows64.asset` 没有覆盖
3. `Assets/Editor/BuildTools/PlayerBuildConfig.asset` —— 出包时
   `PlayerBuilder.cs` 会把它里的值写回 PlayerSettings，所以它是出包的真正权威
4. `Assets/Editor/BuildTools/PlayerBuildConfig.cs` —— 字段初始化器里的默认值（只影响新建的 asset）

### 改这两个值的注意事项

- `companyName` / `productName` 决定 `persistentDataPath`
  （`%USERPROFILE%\AppData\LocalLow\<company>\<product>`）、`Player.log` 路径和
  PlayerPrefs 存储位置。改完**已存的 PlayerPrefs 全部读不到**，
  包括 SpacetimeDB SDK 存的 auth token —— 下次连接会拿到**新 identity**。
- 编辑器开着的时候**不要手改** `ProjectSettings.asset` 和 build profile 的 YAML 快照，
  会被编辑器内存里的值盖回。走 API：
  `PlayerSettings.companyName` / `SetApplicationIdentifier(NamedBuildTarget.X, ...)`；
  build profile 的覆盖用 `SerializedObject(profile.playerSettings)` 改完再调
  `SerializePlayerSettings()`（两者都是 internal，用反射）。
- `PlayerBuildConfig` 里那个「从 ProjectSettings 读取当前设置」按钮会**连带覆盖
  `Version`**（拿全局 `bundleVersion`），只想改名字时别按它。
