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
| 语言 | **纯中文，没有多语言** —— 2026-08-23 已把 Unity Localization / gpt-localization / LanguageManager 整套移除 |

> ⚠️ 补间只用 DOTween Pro。PrimeTween 包不在 manifest 里，工程里也没任何代码用它，
> 那份遗留的 `.claude/skills/primetween/` 已于 2026-08-20 删除。

> ⚠️ **不要再引入多语言。** 2026-08-23 整套移除了：`com.unity.localization`、
> `com.redgame.gpt-localization`、`LanguageManager` / `LocalizationFontAsset` 等框架代码、
> `Assets/Editor/Localization/`、locale 与 String Table 资产、Addressables 的
> Localization-Locales 组、`EditorBuildSettings` 里的两条 localization 配置项。
> 界面文字和配置表里**直接写中文原文**，不要再造「多语言 key」这一层。
> `CustomButton.SetLabel` / `SelectedButton.SetLabel` 现在只有 `(string)` 一个重载。

---

## 2. 程序集划分

```
Assets/Scripts/
├── Framework/          asmdef: UnityFramework          ← 自用框架，命名空间 XFramework
│   ├── Addressable/    资源加载封装（AssetsManager）
│   ├── Basic/          UI 基类：UIBase / UISystem / UIPageConfiguration / UIBackground
│   ├── Interfaces/
│   ├── Scripts/        Attribute / Game / Tools
│   └── Editor/         asmdef: UnityFramework.Editor
├── Game/               无 asmdef → 进 Assembly-CSharp   ← 游戏逻辑
│   ├── Input/
│   ├── ScriptableObject/        Odin 配置资源的类型（现在只有 Audio）
│   └── Scripts/
│       ├── AddressableKeys/   UIKeys.cs 等（生成物，见第 4 节）
│       ├── Audio/             AudioManager
│       ├── Backgrounds/
│       ├── Luban/             Tables.cs 等（生成物，见第 3 节）
│       ├── Resolution/
│       ├── Save/
│       ├── System/            GameManager / GameDataManager / LubanManager
│       ├── Tools/
│       └── UGUI/
└── Net/                无 asmdef → 进 Assembly-CSharp   ← 网络层，见第 5 节
    ├── SpacetimeConnection.cs  只管连接生命周期 + ServerLinkState
    ├── AuthManager.cs          账号门面（纯 C# 单例，UI 只跟它打交道）
    ├── AuthValidation.cs       服务端 AuthRules 的客户端镜像
    └── ModuleBindings/         生成物，不要手改
```

`Assets/Editor/` 也在 Assembly-CSharp-Editor 里（无 asmdef），包含
`AddressableTools/`、`AddressableKeyGeneratorWindow/`、`BuildTools/`、`Luban/`、
`ServerTools/`、`Tools/`、`UGUI/`、`UITools/`、`PathologicalGames/`（第三方）。

> 注意 `Framework` 有 asmdef，`Game` 和 `Net` 没有。所以 `Game`/`Net` 可以引用
> `UnityFramework`，反过来不行。往 `Framework` 里加代码时别引用 `Game` 的类型。

---

## 3. 配置表（Luban）

Excel 源表在 `ExcelTool/LubanTools/DataTables/Datas/*.xlsx`，生成的 C# 落在
`Assets/Scripts/Game/Scripts/Luban/`（`Tables.cs` 等），运行时入口是 `LubanManager`。

编辑器菜单：`Tools > XFramework > 配置 > LuaConfig`（快捷键 **F6**）。

### 一键生成配置的依赖关系

`ConfigTools` 的「一键生成配置」有 6 步，依赖只有两条：
第 3 步（LubanManager）需要第 1 步的导出产物和第 2 步的常量；第 6 步（服务端配置）需要第 1 步。
第 4 步 UIKeys 读 `UIPageConfiguration` 资产、第 5 步 AudioKeys 读 `AudioConfiguration` 资产，
**都和 Luban 无关**，前面失败也照样执行（2026-08-22 修：以前是一步失败就整体中断，
而工程里暂时没有 Luban 表类 ⇒ 第 3 步必然失败 ⇒ UIKeys 永远生成不出来）。

**第 6 步「导出服务端配置」** 把同一份 Excel 按 `server` 目标导出成 `cs-bin` 代码 + `bin` 数据，
落进 `../ReDiv_Server/spacetimedb/`。服务端是 NativeAOT 裁剪过的 wasm，客户端那套
`cs-newtonsoft-json` 的反射在里面用不了，所以两边 codeTarget 必须不同。
字段级 group（Excel 表头的 `##group` 行）决定哪些列进客户端、哪些进服务端。
⚠️ 导出完还要 `spacetime publish` 才生效 —— bin 数据是以嵌入资源编进 wasm 的。
细节见 [../ReDiv_Server/README.md](../ReDiv_Server/README.md) 的「配置表」一节。

跑完看日志里的汇总（成功 / 失败 / 跳过各是哪几步），别只看有没有报错。

### 改 Excel 必须用 ExcelTable.ps1，不要用 openpyxl 之类

`ExcelTool/LubanTools/ExcelTable.ps1` 走 **Excel COM 自动化**。原因写在脚本头部：
表里可能有公式单元格（拼接列用 `=CONCATENATE(...)`，枚举表用 `=J45*2` 之类的自增），
而 Luban 通过 ExcelDataReader **只读公式的缓存值、不重新计算**。用不打开真正 Excel 的库
写完保存，缓存值会丢，Luban 读到空。现在的表虽然还没有公式，这条规则也照样守 ——
哪天加了公式再想起来就晚了。

代价：本机必须装 Microsoft Excel，执行期间会后台起一个不可见的 EXCEL.EXE。

调用时**用 `&` 调用操作符直接调脚本**，不要套 `powershell -File ...`，否则中文参数可能乱码：

```bash
& ExcelTool/LubanTools/ExcelTable.ps1 -Action Dump -Workbook <路径> -Sheet <sheet名> -MaxRows 20
```

Action 一共 11 个：`Dump` / `AddRows` / `UpdateRows` / `AddColumn` / `AddSheet` /
`SetHeader` / `AddEnumItems` / `AddEnumType` / `DeleteRows` / `DeleteColumn` / `DeleteSheet`。
`AddSheet` 指向不存在的文件时会新建工作簿；`SetHeader` 改分组 / 注释（表里没有 `##group`
行会自动补一行）。用法细节全在脚本头部的注释里。

### 表头是 4 行，`##group` 那行不能省

```
第 1 行  ##var     字段名
第 2 行  ##type    Luban 类型
第 3 行  ##group   这一列给谁用：c=仅客户端 / s=仅服务端 / c,s=两端都有（留空同 c,s）
第 4 行  ##        中文注释
第 5 行起          数据
```

有了 `##group` 这一行，注释里就**不用再写「仅客户端」「仅服务端」**了。
新建表（`AddSheet`）一律建 4 行；`AddColumn` 往有 `##group` 的表加列时 **`-Group` 必填**。

> ⚠️ **表头行号不要写死。** 老表可能只有 3 行（没有 `##group`），`__beans__` 那种元表
> 还会出现两行 `##var`。脚本里读写表头一律走 `Get-HeaderRows` 按标记定位 ——
> 2026-08-24 就是因为 `AddColumn` 写死了 1/2/3，给 `CharacterJob` 加
> `StartStar` / `MaxStar` / `Subtitle` 时把中文注释写进了 `##group` 行。
>
> 现在 `CharacterJob` / `CharacterForm` 两张数据表都是 4 行表头。
> `__tables__` / `__beans__` / `__enums__` 是 Luban 的**元表**，自己有 `group` **列**，
> 不适用这一行，别给它们加。
>
> （原来还有一张 `ItemData.xlsx` —— 旧工程遗留的样例表，没登记进 `__tables__`、不参与导出，
> 而且两列还引用着已移除的多语言表 `TbLocalzationKeyData`。2026-08-24 已删除。）

### Excel 就是 schema 的唯一真相源

2026-08-24 起，角色那两张表在 `__tables__.xlsx` 里的 `read_schema_from_file` 都是
**`True`** —— **字段名、类型、分组、注释全部以 Excel 表头为准**，不再需要 XML bean 定义。
`Defines/character.xml` 已经删掉了（`Defines/` 现在只剩 `builtin.xml` 的 vector2/3/4）。

实测确认（2026-08-24，之前文档里写的「Excel 表头给不了字段级 group」**是错的**）：

| 实测项 | 结果 |
|---|---|
| `read_schema_from_file=True` 时 `##group` 行认不认 | ✅ 认。`StartLevel`/`StartStar`(s) 只进服务端，`Name`/`Subtitle`/`SortOrder`(c) 只进客户端 |
| 联合主键（`JobId+FormId`，index 写在 `__tables__` 里） | ✅ 照常，`TbCharacterForm.Get(jobId, formId)` 还在 |
| `##` 注释行 | ✅ **会变成生成代码的 `/// <summary>`** —— XML 定义那套做不到，这是白赚的 |

所以新加配置表**直接建 Excel 就行**，`__tables__.xlsx` 里 `read_schema_from_file` 填 `True`，
不用再写 XML。

> ⚠️ 代价：schema 现在藏在**二进制 .xlsx 里，git diff 看不见**。以前改 XML 是文本 diff，
> review 时一眼能看到「谁把某列的 type 改了」，现在只能靠 `-Action Dump` 主动查。
> 改表结构时在提交信息里写清楚改了什么。

`__enums__.xlsx` 特殊，同一 sheet 里首尾相接堆了多个枚举块。

---

## 4. 资源与打包（Addressables）

编辑器菜单 `Tools > XFramework > 打包`：

| 菜单项 | 实现 |
|---|---|
| Addressable 一键打包 | `Assets/Editor/AddressableTools/AddressableBuildOdinWindow.cs` |
| Windows 一键出包 | `Assets/Editor/BuildTools/PlayerBuildWindow.cs` |
| 自动打包 Addressable | `Assets/Editor/AddressableTools/AddressableBuild.cs` |
| 清空 Addressable 标签内容 | 同上 |

`Tools > XFramework > UI > 生成 UIKeys` 从 **`UIPageConfiguration` 资产**生成
`Assets/Scripts/Game/Scripts/AddressableKeys/UIKeys.cs`，**是生成物**。
（以前读的是 Luban 导出的 `tbuipagedata.json`，UI 配置独立成 ScriptableObject 后不再依赖 Luban。）

### Addressable Key 约定：完整资源路径

`AddressableBuild` 的 `UseAssetPathAsAddress = true`，所以**地址就是资源的完整路径**
（`AssetKeys` 里全是 `Assets/AddressableAssets/Remote/...`）。加载接口收的 key 也是这个。

⚠️ 唯一的例外是 UI 配置表：`UIPageData.PagePath` 因为 Odin 的
`[FilePath(ParentFolder = "Assets/AddressableAssets/Remote")]` 存的是**相对路径**。
所以加载时必须用 `UIPageData.PageKey`（它会补上前缀），**不要直接用 PagePath** ——
直接用的话编辑器下会解析不到、拿到 null，然后在 `Instantiate` 处炸成一句和资源
毫无关系的 `ArgumentException: The Object you want to instantiate is null`。

### 角色美术资源在 Luban 的形态表里，但用窗口录入

头像 / 立绘 / 战斗 Spine / 启动视频 / 循环视频都在 `CharacterForm`（形态表）的客户端列上，
填 **Addressable 完整资源路径**（见上面的 key 约定）。**Excel 是唯一真相源。**

⚠️ 但**别手打路径** —— 用 `Tools > XFramework > 配置 > 角色资源配置` 窗口拖资产，
它算好路径再写回 Excel。手打的话打错没提示、资源改名不同步，只在运行时表现成「加载不出来」。

> 2026-08-23 曾把资源整套挪进 Odin ScriptableObject，当天回退；
> 2026-08-24 又挪过去一次，当天又退回来了。**现在的结论是：数据留在 Excel，
> 只把「录入体验」做成窗口。** 别再提议整套搬进 SO —— 来回搬过两次了。

两层的资源分布：

| 层 | 表 | 有什么资源 |
|---|---|---|
| 角色 / 职业 | `CharacterJob` | 无（只有名字 / 副标题 / 星级上限 / 排序） |
| 形态 | `CharacterForm` | 头像 / 立绘 / Spine / 启动视频 / 循环视频 |

形态分两条线，靠 `FormType` 区分：**基础线（1）** 是「基础形态 → 一觉 → 二觉」，
按角色**星级**现算当前是哪个；**爆发线（2）** 一个角色可以有多个、不分阶段，
战斗中装备爆发宝石才切。

取资源：`TbCharacterForm.Get(jobId, formId)`。基础线的 `formId` 就是服务端
`MyCharacter` View 下发的 `FormId`，**客户端不要自己按星级算形态**；
爆发线自己从配置里筛 `FormType=2` 按 `SortOrder` 排。

按形态分两种填法：

| 列 | 基础形态（UnlockStar 最低那行） | 觉醒形态 / 爆发形态 |
|---|---|---|
| `IconKey` 头像 | 有 | 有 |
| `ArtKey` 立绘 | **有** | 空 |
| `SpineKey` 战斗 Spine | 有 | 有 |
| `VideoStartKey` 启动视频 | 空 | 可空（播一次的入场动画） |
| `VideoLoopKey` 循环视频 | 空 | **有**（启动视频播完之后一直循环的那支） |

视频列填 AVPro 的 **MediaReference 资源**完整路径（工程里 `Video/Loading/Loading.asset`
就是一个），运行时赋给 `MediaPlayer._mediaReference`。取不到的列会是空串，**要判空**。

⚠️ 现在的视频素材每个形态只有一支（文件名后缀 `_000002`，按素材约定是**循环**那支），
所以 `VideoStartKey` 全是空的，等启动视频（`_000001`）补齐再填。

> **待机动画名（`IdleAnimation`）已经去掉了**（2026-08-24）。以后改成挂预制体引用，
> 动画名相关配置在预制体里提前编好，不再进配置表。

#### 配置窗口：`Tools > XFramework > 配置 > 角色资源配置`

实现在 `Assets/Editor/CharacterTools/CharacterResourceWindow.cs`。数据流是个闭环：

```
CharacterForm.xlsx --(Luban 导出)--> tbcharacterform.json --(窗口读)--> 界面拖资产
                   <--(ExcelTable.ps1 UpdateRows)-- 「写入 Excel」按钮
```

窗口做三件事：

1. **形态行从 Luban 导出的 json 读**，不是手敲 —— 表里有几个形态就是几行，形态名 / 形态线 /
   星级门槛都照着显示，配不出表里不存在的 FormId；
2. **拖拽录入**：头像 / 立绘收 `Sprite`（带预览图），Spine 收 `SkeletonDataAsset`，
   两个视频收 AVPro 的 `MediaReference` —— 类型不对拖不进去，路径由 `AssetDatabase` 算；
3. **校验**：路径指向的资产还在不在、有没有形态没配头像、该填立绘/循环视频的行填没填。
   这和服务端自检**互补** —— 那边查表间引用和星级门槛，查不了资源（资源列是 `group="c"`，
   服务端根本看不到）。

「写入 Excel」按 `JobId+FormId` 定位行，**只写那 5 个资源列**，不碰 Excel 里的数值 / 名字 /
排序（那些还是在 Excel 里维护）。默认勾着「写完自动重新导出配置」，会顺手跑一次
`ConfigTools.ExportForExternalTool()`（客户端 + 服务端两份）。

⚠️ 窗口显示的是**上次导出**的内容。有人绕过窗口直接改了 Excel 又没导出的话窗口看不到 ——
好在写回只动那 5 列、按联合主键定位，不会覆盖别人改的其它列。

⚠️ 服务端那份配置改完仍然要 `spacetime publish` 才生效。

要加跑 / 攻击 / 受击这类资源：给形态表 `AddColumn`（记得带 `-Group c`），
再在窗口的 `FormRow` 上加一对「路径字段 + 拖拽属性」，Odin 的 TableList 会自动多出一列。

### 编辑器菜单约定

项目自己的编辑器工具**全部**挂在 `Tools > XFramework/` 下，分五个子菜单：
`打包/`、`服务端/`、`配置/`、`UI/`、`实用工具/`。排序靠 `[MenuItem(path, false, priority)]`
的 priority 显式指定（不要再用 "1." "2." 这种字符串前缀）：
打包 100~121、服务端 150、配置 200~202、UI 300、实用工具 400~423。
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

连上后 Console 应出现（订阅由各系统自己建，所以这里没有「订阅已生效」）：

```
[Stdb] 正在连接 http://127.0.0.1:2383 / rediv
[Stdb] 已连接，identity=...
[Auth] 未登录，等玩家输入账号        ← 或「[Auth] 免密恢复登录：<用户名>」
[Version] 版本一致：0.0.1
```

> `SpacetimeConnection` **不再调 `SubscribeToAllTables`**。订阅按官方建议「按生命周期分组」，
> 谁要数据谁订阅（账号相关的在 `AuthManager` 里）。

### 账号系统

用户名 + 口令的注册 / 登录 / 登出已经打通，服务端在 `../ReDiv_Server/spacetimedb/Auth/`。

```
Assets/Scripts/Net/
├── SpacetimeConnection.cs   只管连接生命周期 + ServerLinkState，**不建立任何订阅**
├── AuthManager.cs           账号门面（纯 C# 单例，不用挂场景）
├── AuthValidation.cs        服务端 AuthRules 的客户端镜像
└── ModuleBindings/          生成物
```

UI：`Game/Scripts/UGUI/LoginUI`（登录/注册）、`Game/Scripts/UGUI/CommonUI`
（服务器状态 + 账号栏 + 版本号 + 点屏幕的三种走向：已登录进游戏 / 未登录弹 LoginUI /
连不上弹重试）。

`AuthManager` 是 UI 唯一要打交道的类，不要在界面里碰 `Conn`：

| 成员 | 用途 |
|---|---|
| `LinkState` / `LinkStateChanged` | 服务器连接状态 |
| `IsAuthReady` / `AuthReady` | 订阅是否生效。**为 false 时 `IsLoggedIn` 不可信** |
| `IsLoggedIn` / `Username` / `AccountId` / `LoginStateChanged` | 登录态 |
| `SessionClosedByServer` | 被顶号 / 被登出的通知（带原因枚举） |
| `VersionMismatch` / `VersionMessage` / `VersionMismatched` | 版本校验，见第 9 节 |
| `RegisterAsync` / `LoginAsync` / `LogoutAsync` | 返回 `AuthResult { Ok, Message }`，Message 是可直接显示的中文 |
| `RetryConnect()` | 断线重连 |

四条实现约束，改的时候别破坏（踩过）：

1. **订阅必须在调 Login 之前建立**，否则成功那一行的 `OnInsert` 会漏。
   `AuthManager` 在连上时就订阅了 `session` / `session_closed` 里自己 identity 的行。
   identity 在订阅 SQL 里是十六进制字面量，**要带 `0x` 前缀**
   （`Identity.ToString()` 给的是不带前缀的大写 hex）。
2. **登录成功看 `Session` 表里有没有自己这条连接的行**，判断用
   `ConnectionId == Conn.ConnectionId`，不是 identity —— 同一 identity 可能有多条连接。
3. **失败文案从 `ctx.Event.Status` 的 `Status.Failed(reason)` 取**，
   reason 是服务端抛的中文原文，直接显示。
4. **连上后别直接弹登录界面**：服务端会按 Identity 免密恢复登录态，
   `AuthReady` 时 `IsLoggedIn` 已经是 true 就直接进游戏。
5. **表回调要把 `OnUpdate` 一起挂上**，别只挂 Insert/Delete。同一主键的删+插如果发生在
   **同一个事务**里（比如同设备换号登录：删掉本连接的旧会话行 + 插入新行，主键都是
   ConnectionId），SpacetimeDB 会把它合并成一次 **update**，Insert / Delete 都不触发。
   实测踩过：换号后界面一直显示上一个账号。带主键的 View 同理。

> ⚠️ 改 `companyName` / `productName` 会换掉 PlayerPrefs 位置 ⇒ AuthToken 丢 ⇒ 拿到新
> Identity ⇒ 免密登录失效，得重新输口令。见第 8 节。版本号见第 9 节。

> `Logout` 会同时解除服务端的免密绑定。prefab 里还没有登出按钮，
> 加了之后 Bind 到 `CommonUI.Logout` 即可。

### 角色系统（客户端还没做）

服务端已经能用：多角色、创建 / 删除（软删）/ 选择 / 觉醒，选完角色才进城镇。
契约见 [../ReDiv_Server/README.md](../ReDiv_Server/README.md) 的「角色系统」一节。接的时候注意：

- 角色列表走 **View**（`my_character`），订阅时**不用带 where** —— 服务端已按订阅者过滤。
  栏位数在 `my_account_profile` 里。
- 订阅要分段：连上订 `session` → 登录成功后订 `my_character` / `my_account_profile`
  → 选角后订角色态（还没有）。订阅 SQL 不能 join 到会话，所以只能分段。
- `character_selection` 是公开表，订阅自己那行判断「是否已进城镇」；
  它同时也是以后做在线列表 / 频道人数的数据源。
- 失败文案和账号系统一个套路：从 Reducer 回调的 `ctx.Event.Status` 取
  `Status.Failed(var reason)`，reason 是服务端抛的中文原文（「角色栏位已满（4/4）」
  「这个角色名已经被使用了」「需要等级 30 才能觉醒成「3 星形态」」这种），直接显示给玩家。
- 成功不看回调，看表：建角色看 `my_character` 多出一行，选角色看 `character_selection`
  出现自己那行，觉醒看 `my_character` 那行的 `Star` / `FormId` 变了。
  带主键的 View 更新会走 **OnUpdate**，别只挂 OnInsert/OnDelete（见上一条第 5 点）。
- **现在只能传 `jobId = 1`**（凯露）。角色列表还只有这一行，界面上的选项别硬编码。
- 可调的 Reducer：`CreateCharacter(name, jobId)` / `DeleteCharacter(id)` /
  `SelectCharacter(id)` / `LeaveCharacter()` / `AwakenCharacter(id)`。

两层结构（配置在 `ExcelTool/LubanTools/DataTables/`）：

```
CharacterJob（角色 / 职业，凯露）—— 建角色时选，之后不变
  └ CharacterForm（形态）—— ★ 美术资源全在这一层，分两条线
      ├ 基础线 FormType=1  按**星级**现算当前形态，觉醒推进，永久不可逆
      │    1~2 星  基础形态（魔法士）    UnlockStar=1，建完角色就在这
      │    3~5 星  一觉形态（魔导士）    UnlockStar=3，UnlockLevel=30
      │    6   星  二觉形态（黑魔法师）  UnlockStar=6，UnlockLevel=60（部分角色没有二觉）
      └ 爆发线 FormType=2  一个角色**可以有多个**，不分阶段，按 SortOrder 排
           公主 / 暗黑圣灵 …  战斗中装备**爆发宝石**才切，和星级 / 等级无关
```

⚠️ 2026-08-24 改过设定：原来中间还有一层「专职」（`JobSpecialization` / `SpecId` /
`Stage` / `SwitchSpecialization` / `FormStage`），**现在没有专职了**，那套已删干净。

界面要怎么取数据：

| 要什么 | 从哪取 |
|---|---|
| 职业名（凯露）/ 副标题 / 排序 | `TbCharacterJob` |
| 星级上限（画「3/6 星」那排星的分母） | `TbCharacterJob.MaxStar`（有二觉 6、没二觉 5）|
| 当前星级 | `MyCharacter` View 的 `Star` |
| 形态名 / 排序 / 星级门槛 / 觉醒等级 | `TbCharacterForm.Get(JobId, FormId)`，`FormId` 取 View 下发的 |
| 该角色有哪些爆发形态 | 从 `TbCharacterForm` 筛 `JobId` 相同且 `FormType == 2`，按 `SortOrder` 排 |
| 头像 / 立绘 / Spine / 启动视频 / 循环视频 | 同上，也在 `TbCharacterForm` 的行上（填 Addressable 完整路径）|

⚠️ 资源列**别手打路径** —— 用 `Tools > XFramework > 配置 > 角色资源配置` 窗口拖资产，
它算好路径写回 Excel。详见第 4 节。

按形态分两种填法：

| 列 | 基础形态（UnlockStar 最低那行） | 觉醒形态 / 爆发形态 |
|---|---|---|
| `IconKey` 头像 | 有 | 有 |
| `ArtKey` 立绘 | **有** | 空 |
| `SpineKey` 战斗 Spine | 有 | 有 |
| `VideoStartKey` 启动视频 | 空 | 可空（播一次的入场动画） |
| `VideoLoopKey` 循环视频 | 空 | **有** |

视频列指向 AVPro 的 **MediaReference 资源**（工程里 `Video/Loading/Loading.asset`
就是一个），运行时赋给 `MediaPlayer._mediaReference`。没配的列是空串，**要判空**。

- **当前形态别自己算** —— `MyCharacter` View 的 `FormId` 就是服务端按星级算好的，
  客户端再算一遍两边迟早对不上。注意 4 / 5 星在配置里**没有单独的行**，
  形象跟着 3 星那行走，服务端已经处理了这个回退。
- 觉醒调 `AwakenCharacter(characterId)`：把角色推到基础线的下一档（1~2 星 → 一觉，
  3~5 星 → 二觉）。服务端**现在只校验等级**，设定上还要求「完成觉醒任务」——
  任务系统做好后在服务端那一处加，客户端不用管。
- 觉醒**不可逆**，没有反向接口，界面上别做「切回上一形态」。

两处校验，各管一半，都要跑：

- `spacetime call rediv character_config_self_test` —— 查 Excel 里表间的引用和星级门槛，
  **查不了资源**（那些已经不在服务端能看到的列里了）。
- `Tools > XFramework > 配置 > 角色资源配置` 窗口的「重新校验」 —— 查资源：
  路径指向的资产还在不在、有没有形态没配头像、该填立绘/循环视频的行填没填。

⚠️ 凯露（JobId=1）的资源都配好了（`Character/100002/` 那套图），但**启动视频全是空的**
（素材只有循环那一支）、`CharacterJob.Subtitle` 也还空着。
**优衣（JobId=2）刚加进 CharacterJob 表，形态和资源都还没配** —— 服务端自检现在就报着这一条。

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
InputSystem 1.19.0、Timeline 1.8.13、Test Framework 1.6.0、
Recorder、MemoryProfiler、Luban、UIEffect、UnmaskForUGUI、UniTask、Spine 4.3（4 个包）、
`com.unity.pipeline` 0.5.0-exp.1、`com.coplaydev.unity-mcp`

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

---

## 9. 版本号（客户端三处 + 服务端一处必须一致）

客户端连上服务器后会立刻调 `CheckVersion(Application.version)` 对一次版本号，
不一致就弹窗提示并禁止登录（详见 [../ReDiv_Server/README.md](../ReDiv_Server/README.md) 的「版本号」一节）。
所以版本号改一处不够，客户端这**三处**要一起改：

| 位置 | 作用 |
|---|---|
| `ProjectSettings/ProjectSettings.asset` 的 `bundleVersion` | `Application.version` 读的就是它，**校验用的是这个值** |
| `Assets/Settings/Build Profiles/PC.asset` | 这个 profile 自带一份 PlayerSettings 覆盖快照 |
| `Assets/Editor/BuildTools/PlayerBuildConfig.asset` 的 `Version` | 出包时会写回 PlayerSettings，是**出包时的真正权威** |

加上服务端 `ReDiv_Server/spacetimedb/Version.cs` 的 `Module.ServerVersion`（改完要 publish），
一共四处。2026-08-22 之前这四处是 `1.0` / `0.1` / `0.1` / 无，界面上还写死显示 `0.0.1`，
四个值互不相同 —— 现在统一成 `0.0.1`。

编辑器开着时**不要手改** `ProjectSettings.asset` 和 profile 的 YAML 快照（会被编辑器内存值盖回），
走 API：`PlayerSettings.bundleVersion`；profile 的覆盖用
`SerializedObject(profile.playerSettings)` 改完再调 `SerializePlayerSettings()`（都是 internal，用反射）。

界面右下角的版本号由 `CommonUI.RefreshVersion()` 从 `Application.version` 刷，
**不要再往 prefab 里写死**，否则又会和校验值对不上。

---

## 10. 国服资源解包与还原

国服 AA 下载、UnityPy/AnimeStudio 解包、复杂分类目录、JSON/二进制伴随数据、
NGUI Border/Padding、Spine 3.6 → 3.8.99 → 4.3.23、怪物状态以及
VariantCard Shader/Material 的长期交接文档在：

[Docs/CN资源解包与还原工作流.md](Docs/CN资源解包与还原工作流.md)

素材相关的新对话必须先读该文档。尤其不要忘记：原 Bundle 的解析回退版本是
`6000.0.58f2`，本 Unity 工程版本是 `6000.4.8f1`；Spine 的 Scale `0.5`
只在 3.8 工程创建阶段应用一次。

---
