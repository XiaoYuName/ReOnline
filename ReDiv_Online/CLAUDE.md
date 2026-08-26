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
│       ├── Town/              城镇背景（世界空间）/ 出生点 + 边界 / 角色与 NPC 的两层控制器 + 取用回收
│       └── UGUI/
└── Net/                无 asmdef → 进 Assembly-CSharp   ← 网络层，见第 5 节
    ├── SpacetimeConnection.cs  只管连接生命周期 + ServerLinkState，不建任何订阅
    ├── AuthManager.cs          账号门面（纯 C# 单例，UI 只跟它打交道）
    ├── CharacterManager.cs     角色门面，同上。登录成功后才订角色数据
    ├── AuthValidation.cs       服务端 AuthRules 的客户端镜像
    ├── CharacterValidation.cs  服务端 CharacterRules 的客户端镜像（角色名格式）
    ├── TownManager.cs          城镇门面：当前城镇 / 时段 / 同城镇玩家 / 坐标上报
    └── ModuleBindings/         生成物，不要手改
```

`UGUI/` 下已接好的界面：`CommonUI`（标题）、`LoginUI`、`PopDialogueUI`、`PopLoadingUI`、
`SelectCharacterUI`（选人）、`CreatCharacterUI`（创角）、`ReviseCharacterNameUI`（起名字）、
`MainCommonUI`（城镇主界面）。

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
> 现在**六张**数据表（`CharacterJob` / `CharacterForm` / `Town` / `TimeBand` / `LevelExp` /
> `TownNpc`）都是 4 行表头，`read_schema_from_file` 全是 `True`。
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

### 现在有哪些配置表

| Excel | 运行时入口 | 内容 |
|---|---|---|
| `CharacterJob.xlsx` | `TbCharacterJob` | 角色 / 职业（凯露、优衣） |
| `CharacterForm.xlsx` | `TbCharacterForm` | 形态（基础线 / 爆发线）+ **全部美术资源** |
| `Town.xlsx` | `TbTown` | 城镇 + 三个时段的背景预制体（**世界空间 SpriteRenderer**） |
| `TimeBand.xlsx` | `TbTimeBand` | 早 / 中 / 晚三段的边界（起始小时） |
| `LevelExp.xlsx` | `TbLevelExp` | 升级所需经验 + 该等级体力上限。**数值是占位的** |
| `TownNpc.xlsx` | `TbTownNpc` | 城镇 NPC：站在哪个城镇的哪个世界坐标。**纯客户端**（全 `c`），现在是空表 |

⚠️ **加了表要按顺序跑四步**：第 1 步导出 → **自动打包 Addressable**（打标）→
第 2 步 AssetKeys → 第 3 步 LubanManager。少一步就编译不过，而且报错指向的地方很误导：

- 只跑第 1 步 ⇒ `LubanManager.TbXxx` 没生成 ⇒ 「does not contain a definition for 'TbXxx'」
  （2026-08-25 加 `LevelExp` 时踩过）；
- **导出完没打标就生成 AssetKeys** ⇒ 新的 `tbxxx.json` 还没进 Addressables ⇒
  `AssetKeys.TbxxxPath` 没有 ⇒ `LubanManager.Generated.cs` 编不过
  （2026-08-26 加 `TownNpc` 时踩过）。AssetKeys 是从 **Addressables 条目**生成的，不是从文件系统。

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

资源全在 `CharacterForm`（形态表）的客户端列上，填 **Addressable 完整资源路径**。
**Excel 是唯一真相源**，但**别手打路径** —— 用
`Tools > XFramework > 配置 > 角色资源配置` 窗口拖资产，它算好路径写回 Excel。

> 2026-08-23 和 08-24 各试过一次把资源整套挪进 Odin ScriptableObject，两次都当天退回。
> **结论：数据留在 Excel，只把录入体验做成窗口。** 别再提议搬走。

形态分两条线（`FormType`）：**基础线 1** 是「基础 → 一觉 → 二觉」，按角色**星级**现算；
**爆发线 2** 一个角色可多个、不分阶段，战斗中装宝石才切。

取资源：`TbCharacterForm.Get(jobId, formId)`。基础线的 `formId` 就是服务端 `MyCharacter`
View 下发的 `FormId`，**客户端不要自己按星级算**；爆发线自己筛 `FormType=2` 按 `SortOrder` 排。

资源列（都是 `group="c"`，服务端看不到）：

| 列 | 是什么 | 基础形态 | 觉醒 / 爆发形态 |
|---|---|---|---|
| `IconKey` | 头像 | 有 | 有 |
| `UnitPlateIconKey` | 略缩图（形态卡上那张横幅） | 有 | 有 |
| `NameIconKey` | 名字图 | 有 | 有 |
| `ArtImage` | 立绘 | **有** | 空 |
| `StillUnitPrefab` | 预览图预制体（**全屏立绘**） | 空 | **有** |
| `SkeletonUI` | UI 展示预制体（选人界面的格子用它） | 有 | 有 |
| `SkeletonScreen` | 战斗 Spine 预制体 | 战斗用，选人界面不碰 | 同左 |

取不到的列是空串，**要判空**。

> 待机动画名**不进配置表** —— 编在 `SkeletonUI` 预制体的 `CharacterGraphicUI` 组件上
> （`[SpineAnimation] IdleName`，Inspector 里是下拉，选不出不存在的动画名）。
> 视频列 2026-08-24 已删。

#### 配置窗口：`Tools > XFramework > 配置 > 角色资源配置`

实现在 `Assets/Editor/CharacterTools/CharacterResourceWindow.cs`。闭环：

```
CharacterForm.xlsx --(Luban 导出)--> tbcharacterform.json --(窗口读)--> 拖资产
                   <--(ExcelTable.ps1 UpdateRows)-- 「写入 Excel」
```

- 形态行**从导出的 json 读**，不是手敲 —— 配不出表里不存在的 FormId
- 拖拽录入：图收 `Sprite`、预制体收 `GameObject`，类型不对拖不进去，路径由 `AssetDatabase` 算
- 校验：资产还在不在、有没有漏配头像 / UI 展示预制体、`SkeletonUI` 上有没有
  `CharacterGraphicUI`（没有就播不了待机动画）。这和服务端自检**互补** ——
  那边查表间引用和星级门槛，查不了资源
- 「写入 Excel」按 `JobId+FormId` 定位，**只写资源列**，不碰数值 / 名字 / 排序；
  默认勾着「写完自动重新导出配置」

⚠️ 窗口显示的是**上次导出**的内容。绕过窗口直接改 Excel 又没导出的话窗口看不到 ——
好在写回只动资源列、按联合主键定位，不会覆盖别人改的列。服务端那份改完仍要 `spacetime publish`。

要加跑 / 攻击 / 受击这类资源：给形态表 `AddColumn`（带 `-Group c`），
再在窗口的 `FormRow` 上加一对「路径字段 + 拖拽属性」，TableList 会自动多一列。

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

### 角色系统

网络层门面 `Assets/Scripts/Net/CharacterManager.cs`，和 `AuthManager` 一个套路：
**界面只读它、只听事件，不碰 `Conn`**。

| 成员 | 用途 |
|---|---|
| `IsReady` / `Ready` | 角色订阅是否生效。**false 时 `Characters` 不可信** |
| `Characters` | 角色列表（最近玩过的在前），来自 `my_character` View |
| `CharacterSlots` | 已解锁栏位数，来自 `my_account_profile` View |
| `CharactersChanged` | 列表变了，界面重画即可 |
| `IsBusy` | 有查重 / 创建 / 删除请求正在等回应，界面据此禁用按钮 |
| `Coin` / `Gem` / `WalletChanged` | 金币钻石。**账号级、全角色共享**，来自 `my_wallet` View |
| `FindCharacter(characterId)` | 按 id 取角色行（等级 / 经验 / 体力）。取不到返回 null |
| `CheckNameAsync` / `CreateCharacterAsync` / `DeleteCharacterAsync` / `SelectCharacterAsync` | 返回 `CharacterResult { Ok, Message }`，Message 是可直接显示的中文 |

两条实现约束（改的时候别破坏）：

1. **订阅必须分段建**：角色数据挂在 `AuthManager.LoginStateChanged` 上，**登录成功才订、
   登出就退订**。订阅 SQL join 不到会话，连上时就订的话 View 返回空。
2. 订阅 View **不用带 where** —— 服务端已按订阅者 Identity 过滤过了。
3. 表回调**要连 `OnUpdate` 一起挂**（同事务的删+插会被合并成 update）。列表变化统一走
   「整表重读」而不是增量维护 —— 角色最多个位数，重读很便宜，增量维护出错了表现成
   「界面上少一个角色」，很难查。

服务端契约（Reducer 名、失败文案怎么取）见
[../ReDiv_Server/README.md](../ReDiv_Server/README.md) 的「角色系统」。

#### 选人界面（`SelectCharacterUI`）

`Assets/Scripts/Game/Scripts/UGUI/SelectCharacterUI/`。已完成：

- 格子是**预制体里摆好的 10 个**，不动态生成。可见数 = `max(栏位数, 现有角色数)`，
  多的隐藏 —— 全显出来玩家点第 5 个只会撞「角色栏位已满」
- 空格子隐藏名字 / 等级 / Spine；有角色的按 `(JobId, FormId)` 取 `SkeletonUI` 预制体
  实例化到 `SkeletonPoint`，调 `CharacterGraphicUI.PlayIdle()` 播待机
- 单选，选中后「进入游戏」和「删除角色」才可点；**再点一次已选中的格子取消选中**

四个按钮（AutoBind 的名字和界面上的字容易记反）：

| AutoBind 字段 | 界面上的字 | 干什么 |
|---|---|---|
| `cancelButton` | 创建角色 | 打开 `CreatCharacterUI` |
| `deleteButton` | 删除角色 | **选中才可点**。二次确认后调 `DeleteCharacterAsync` |
| `enterButton` | 进入游戏 | **选中才可点**。调 `SelectCharacterAsync`，成功后开 `MainCommonUI` |
| `quitButton` | 退出 | 关掉本界面回 `CommonUI` |

**「进入游戏」**调 `SelectCharacterAsync(characterId)` —— 服务端写完 `character_selection`
才算进城镇，同时把角色放进它该在的城镇（新角色落初始城镇）。成功后关掉本界面、
打开 `MainCommonUI`；失败弹窗（比如角色刚被别处删了）。

**删除角色**走 `UIUtility.ShowDialogue` 二次确认（服务端是软删，但玩家这边**没有恢复入口**，
对他来说就是没了）。删完不用手动刷列表：角色从 `my_character` 消失 ⇒ `CharactersChanged`
⇒ `Refresh()`，选中态因为 `keepSelected` 对不上任何一行而自然清空，两个按钮跟着变灰。
请求期间 `busy` 会把四个按钮全禁掉防连点。

#### 创建角色界面（`CreatCharacterUI`）

`Assets/Scripts/Game/Scripts/UGUI/CreatCharacterUI/`。

```
HeadContent     每个可创建角色一个 CharacterHeadSlot（动态生成，头像取基础形态的 IconKey）
NameImg         选中角色后显示 NameIconKey
ArtImage        选中角色后显示 ArtImage（普通立绘）
RightJobsPanel  两张 CharacterJobSlotUI（预制体里摆好的）：上=基础线，下=爆发线
Fall            觉醒 / 爆发形态的全屏立绘（StillUnitPrefab）生成在这里
CreatButton     「创建」—— 没选角色时是灰的，点了打开 ReviseCharacterNameUI
```

立绘规则（互斥，同时只显示一个）：**基础形态**→ `ArtImage`，Fall 清空；
**觉醒 / 爆发形态**→ Fall 里生成 `StillUnitPrefab`，`ArtImage` 隐藏。

「在看哪个形态」由两张卡里**当前选中的那张**决定 —— 点卡片或点它的箭头都会让它成为当前卡。
两张卡同时摆着但立绘只有一处，所以必须有这个概念。

`NameImg` 和 `CreatButton` 都**没登记进 AutoBind**，代码里 `Get<T>("路径")` 手动取（见坑 4）。

#### 角色名界面（`ReviseCharacterNameUI`）

`Assets/Scripts/Game/Scripts/UGUI/ReviseCharacterNameUI/`。由创角界面的「创建」按钮打开，
**真正调 `CreateCharacter` 的是这里**。三个按钮（名字和界面上的字容易记反）：

| AutoBind 字段 | 界面上的字 | 干什么 |
|---|---|---|
| `repeatButton` | 重复 | 查重。本地先查格式，再问服务端重名，结果弹窗 |
| `actionButton` | 创建 | **查重通过前是灰的**。调 `CreateCharacterAsync` |
| `cancelButton` | 取消 | 关掉本界面，回创角界面 |

流程：创角界面点「创建」→ `SetJob(jobId)` 把要建的角色传进来并清空上次输入 →
玩家输入 → 点「重复」→ 通过后「创建」才亮 → 点「创建」→ 成功后关掉本界面**和创角界面**，
回到选人界面（那边挂着 `CharactersChanged`，新角色自己就出现了）。

两条别破坏的约束：

1. **「查重通过」绑在具体某个名字上**（`checkedName` 字段），不是一个 bool。
   玩家改一个字就必须把「创建」打回灰色，否则会拿没查过的名字去建。
2. **查重通过 ≠ 名字被占住**。服务端的 `CheckCharacterName` 一张表都不写，
   查完到点创建之间名字可能被别人抢走 ⇒ 创建失败那条路必须处理
   （现在是弹窗 + 把「创建」打回灰色让玩家重查）。

长度提示那行字（`Tips`）在 `Init` 里从 `CharacterValidation.LengthHint` 赋值，
**不用 prefab 里写死的**（prefab 原文是「$请输入1-10字」，和真实规则 2~8 汉字对不上）。

### 城镇与世界时间

网络层门面 `Assets/Scripts/Net/TownManager.cs`，和另两个门面一个套路：
**界面只读它、只听事件，不碰 `Conn`**。

| 成员 | 用途 |
|---|---|
| `IsReady` / `Ready` | 订阅是否生效。**false 时下面的值都不可信** |
| `CurrentBandId` / `WorldTimeChanged` | 当前时段（早=1 中=2 晚=3），来自公开表 `world_time`。**0 = 还不知道** |
| `CurrentTownId` / `CurrentCharacterId` / `LocationChanged` | 自己在哪个城镇 / 在玩哪个角色，来自 `character_selection` 里本连接那一行。**0 = 还没进游戏** |
| `CurrentBand` / `CurrentTown` | 对应的 Luban 配置行，**要判空** |
| `CurrentBackgroundKey` | 「当前城镇 + 当前时段」该用哪个背景预制体，取不到是空串 |
| `TownPlayers` / `TownPlayersChanged` | 同城镇的**其他**玩家（key 是 CharacterId，不含自己）。事件只在**有人进出**时触发，移动不触发 |
| `ReportTransform(x, y, facing, moving)` | 上报自己的坐标。**调用方负责节流** |

三条实现约束（改的时候别破坏）：

1. **时段是服务端算好推下来的，客户端别自己按本地时钟算。** 全服要统一，
   而且玩家改本地时钟不能生效。服务端契约见
   [../ReDiv_Server/README.md](../ReDiv_Server/README.md) 的「城镇与世界时间」。
2. **两张表都在连上时就订**：`world_time` 是全局的和登录无关；
   `character_selection` 虽然要等选角才有行，但订阅必须**早于** `SelectCharacter`，
   否则成功那一行的 `OnInsert` 会漏（账号那边踩过）。
3. **挑 `character_selection` 里 `ConnectionId == Conn.ConnectionId` 那一行**，
   不是「自己 identity 的第一行」—— 同一 identity 可能有多条连接
   （编辑器 + 真机、或者开两个客户端），拿错行会显示成别的窗口所在的城镇。

#### 城镇主界面（`MainCommonUI`）、背景与出生点

选人界面点「进入游戏」→ `SelectCharacter` 成功 → 关掉选人界面、打开
**`MainCommonUI`**（城镇主界面）。

**背景是世界空间的 `SpriteRenderer`，不是 UI**（2026-08-25 改的，之前挂在
`UIBackground` Canvas 下）。改的理由是硬的：那个 Canvas 是 ScreenSpaceCamera +
CanvasScaler，背景的实际大小会随分辨率 / 宽高比变，而角色、出生点、坐标都是世界空间的。
**两套坐标系的比例一旦随分辨率变，同一个世界坐标在不同人屏幕上就落在美术的不同位置**
—— 联机里就是「他站喷泉边、我看他站台阶上」，边界做出来之后更是各人一堵墙。
现在美术钉死在世界坐标里，适配交给相机（那一步还没做，见本节末尾）。

背景和角色**同一套两层结构**（用户 2026-08-25 定的）：

```
Games/Backgrounds                      ← 场景节点，Tag = Backgrounds
└── TownBackgroundController           ← 外层，**所有城镇共用一个预制体**（只是个壳 + 挂载点）
    └── Background                     ← 运行时按「城镇 + 时段」把背景塞进来
        └── Town_50020                 ← SpriteRenderer（材质 VariantCard，Sorting Layer Ground）
            ├── StartPoints            ← **出生点**
            └── GroundCollider         ← **可行走边界**（EdgeCollider2D，物理层 Ground）
```

**出生点和边界在内层、不在外层** —— 它们是**跟着地图美术走**的，一张图一套。
（2026-08-25 一开始放在共用外层上，那样所有城镇同一个落点，2026-08-26 改成了现在这样。）
⚠️ 代价是**一个城镇三个时段就有三份**，三份要保持一致：改了一张记得另外两张一起改，
不然玩家跨时段会发现「白天能走的地方晚上走不了」。

| 文件 | 职责 |
|---|---|
| `Game/Scripts/Town/TownBackgroundController.cs` | 外层：`Bind()` / `Unbind()` 塞背景 / 摘背景，出生点**转发**给内层 |
| `Game/Scripts/Town/TownGroundController.cs` | **内层**：出生点 + 边界自检（少碰撞体、层放错都会报出来） |
| `Game/Scripts/Town/TownGround.cs` | 边界的**扫掠查询**（纯几何，不开物理模拟） |
| `Game/Scripts/Town/TownWorldRoots.cs` | 按 Tag 找两个世界空间根节点 |
| `Game/Scripts/UGUI/MainCommonUI/MainCommonUI.cs` | 听 `TownManager` 的事件，决定该显示哪张、负责取用回收 |

**场景里的两个世界空间根节点**（2026-08-25 从 `Managers` 下挪到了新的 `Games` 这一层）：

| 节点 | Tag | 挂什么 |
|---|---|---|
| `GameManager/Games/SkeletonCharacters` | `SkeletonCharacters` | 城镇角色（外层控制器 + 按形态的 Spine），走 PoolManager |
| `GameManager/Games/Backgrounds` | `Backgrounds` | 背景外层控制器（里面按时段塞背景，出生点和边界在那张背景上） |

两个节点连同 `Games` 这一层**都是原点 + 缩放 1**，所以挂上去的东西 local 坐标就是世界坐标。
查找一律走 `TownWorldRoots.Find(tag)` —— **按 Tag 不按路径**（节点在层级里挪位置不该改代码），
代价是 Tag 必须在 Tags & Layers 里登记过，没登记 `FindGameObjectWithTag` 会**抛异常**
而不是返回 null，所以那个 try/catch 收在 `TownWorldRoots` 一处，别各处再写。

```
TownManager.CurrentTownId ──┐
                            ├─→ CurrentBackgroundKey（配置表 Town 的三列之一）
TownManager.CurrentBandId ──┘         │
                                      ↓
    AssetsManager.Instantiate(key) → TownBackgroundController.Bind(view)
```

- **外层进城镇建一次**（`EnsureController`），换时段只换里面那张（`ReleaseBackgroundView`）。
  外层是共用预制体，所以换城镇也不用重建。
- 回收统一用 `AssetsManager.ReleaseGameObject`（销毁 + 卸 AA 引用），不回池 ——
  背景一张就满屏，留在池里白占内存。**先 `SetActive(false)` 再销毁**，
  否则 `Destroy` 延迟到帧末会和下一张叠着（坑 3）。
- `RefreshBackground()` 是**幂等**的：key 没变什么都不做，所以时段推送和位置推送
  连着到达也不会重建两遍。
- **背景必须在角色之前刷** —— 出生点在背景外层身上，反过来第一次进城镇会落在原点。
  `Open()` / `HandleTownReady()` / `HandleLocationChanged()` 里的顺序都是 背景 → 信息 → 自己 → 别人。

三条美术侧的约定（都在预制体里，代码不碰）：

1. **比例烘在内层的 `localScale` 里**。贴图是 1024×1024 的方图（美术压扁存的），
   靠 `scale.x ≈ 1.7778` 拉成 16:9。**代码不去覆盖它** —— 想改画面大小就改那个预制体。
2. **Sorting Layer `Ground`**（order -1），角色在 `Character` ⇒ 角色永远在背景前面。
   以后要做遮挡物（房子、栏杆挡住角色）就在内层加子 `SpriteRenderer` 放 `EffectTop`。
3. 三列（`BgMorning` / `BgNoon` / `BgNight`）和三个时段**按 BandId 硬对应**，
   段数固定 3 段（服务端自检守住）。某列为空 ⇒ 那个时段没背景，不报错。

**出生点**：进城镇的落点规则只有一条 —— **每次进城镇都站到那张图的 `StartPoints`**。
服务端只记「在哪个城镇」、从来不存坐标（`CharacterTransform` 是连接级、断线就清），
所以新角色进初始城镇、以后换城镇走的都是同一条路。用户 2026-08-25 明确定的
**不记住上次站的位置**。背景还没塞进来 / 那张图没挂地面组件时退回原点（老行为）：
**配漏了要退化，不能让人进不了城镇**。

##### 可行走边界（2026-08-26）

美术在每张背景预制体里画一条 `EdgeCollider2D`（节点 `GroundCollider`），围出能走的地面
（兰德索尔那条是 14 个点、首尾闭合、`edgeRadius 0.07`）。判定在
[`TownGround`](Assets/Scripts/Game/Scripts/Town/TownGround.cs)：

- **不开物理模拟、角色身上没有 `Rigidbody2D` / `Collider2D`**。移动照旧直接写
  `transform.position`，只是写之前先 `Physics2D.CircleCast` 问一句「这一步会不会穿过边界」。
  不用物理是因为那要改成 `MovePosition`、受 FixedUpdate 节奏影响，还得管远端玩家的刚体互推。
- **碰撞体必须在物理层 `Ground`**（不是 Sorting Layer 那个 Ground，两回事）。
  层放错的表现是「边界完全不起作用」，`TownGroundController` 在 `Awake` 里会报出来。
- **只夹自己不夹别人**：远端玩家的坐标是服务端转发的权威值，夹回来只会让他在我屏幕上
  和他自己看到的位置不一致。
- 没配边界 / 工程里没有 `Ground` 层 ⇒ **退化成一律放行**，不能变成「一步都走不了」。

三个实测踩过的坑（改这段代码前必读，都写在 `TownGround` 的注释里了）：

| 坑 | 现象 | 正确做法 |
|---|---|---|
| **按轴分离滑不动斜坡** | 先试 X 再试 Y 那种写法，贴着微微上升的地面底边往右走，X 直接撞斜面，人在 x=3.23 卡死 | 用 **collide & slide**：撞了就走到贴墙为止，把剩下的位移**投影到表面切线**再走一次 |
| **`hit.distance` 不是圆心能走的距离** | 对 `CircleCast` 它是「起点到接触点」的距离，比圆心位移多约一个半径；拿它当位移用等于每次贴墙都把人往墙里塞，塞进去之后（`queriesStartInColliders` 默认 true）所有方向都返回 0，人彻底卡死 | 用 **`hit.fraction * distance`** |
| **「朝离开墙的方向就整步放行」是免检** | 起点只要接触就 fraction=0，条件太容易满足 ⇒ 角色直接穿过边界飞到 x=53 | 嵌进去时只**沿法线挪一丁点**（`SkinWidth`），这一帧位移一点都不放行 |

角色那边只多了一个字段：`TownCharacterController.blockRadius`（默认 `0.05`，扫掠半径，
0 就退化成射线）。判定点是**外层节点自己的位置**，也就是角色脚下。

⚠️ 边界画得比 16:9 可视范围略宽（实测右墙在 x≈9.03，而 16:9 只看得到 ±8.89），
所以人能走出画面一点点。等相机适配（固定视野盒）做完再让美术收一下就行。

> ⚠️ **背景不认识网络层。** 它不订阅任何东西、不碰 `Conn`、不自己算时段 ——
> 「该显示哪张」全由 `MainCommonUI` 从 `TownManager` 算好再喂给它。

> `UISystem` 的 `LoadUIBackground` / `HideUIBackground` / `UIBackground` 基类**还留着**，
> 只是城镇背景不再走它了（现在没有调用方）。纯 UI 界面要垫背景仍然可以用。

##### 还没做：相机适配（固定视野盒 + 黑边）

现在**没有任何适配** —— 相机 `orthoSize` 恒定 5，可视高 10 个世界单位、宽 = 10×宽高比。
所以窗口宽高比一变，看到的世界范围就跟着变（编辑器里把 Game 面板拖窄就能看到被"放大"的效果）。

2026-08-25 定下的方向（**别再提 cover / 按比例缩放美术**，那会让不同宽高比的玩家
看到的范围和距离都不一样）：

- 固定视野盒 = **17.7778 × 10 世界单位**（16:9 那一档），所有人看到同一块世界；
- 实现方式：算出屏幕内最大的居中 16:9 像素区，**同时赋给 MainCamera 和 UICamera 的
  `Camera.rect`** ⇒ 相机 aspect 恒为 16:9，`orthoSize` 保持 5，一行缩放公式都不用；
  视口外的像素相机根本不渲染，不会出现「角色走出盒子还被看见」；
- 宽屏两边是空的 —— 视野一致和填满屏幕本质冲突。要好看就让美术把背景画宽（画框式装饰），
  视野盒不变；
- **固定的是"看到多少世界"，不是"1 单位等于多少像素"** —— 1080p 和 1440p 的
  px/unit 必然不同，那只是分辨率，两点距离**占画面的比例**是一致的；
- 长期方向是「城镇地图比一屏宽 + 相机跟随角色」（DNF 那类游戏就是这样），
  那时宽屏多看到的是地图内容，黑边可以撤掉，相机组件之外的东西都不用改。

HUD（右上角信息栏 / 摇杆）要不要一起压进 16:9 盒子里**还没定**。

#### 城镇 NPC（配置表 `TownNpc`）

一行一个 NPC，站在某个城镇的**固定世界坐标**上。2026-08-26 加的，通路已经跑通，
**表现在是空的**，等策划填。

| 列 | 说明 |
|---|---|
| `NpcId` | 全局唯一 |
| `TownId` | 站在哪个城镇，对应 `Town.TownId` |
| `Name` | 头顶显示的名字（中文原文） |
| `PosX` / `PosY` | **世界坐标**。画面中心是 0，地面大概在 y = -3 ~ -5 之间 |
| `Facing` | `1` 朝右 / `-1` 朝左 |
| `SkeletonTown` | 城镇 Spine 预制体的 Addressable 完整路径（预制体上要有 `TownSkeletonController`） |

**NPC 复用玩家角色那套两层结构** —— 外层还是 `TownCharacterController`，
所以名字、朝向、头顶跟骨骼全是白拿的；区别只在于 Spine 路径不是按 `(JobId, FormId)`
查形态表，而是直接写在 NPC 表里，所以 `TownCharacterSpawner` 多了一个
`Acquire(string skeletonKey)` 重载。

四条约束：

1. **纯客户端表现**：服务端没有 NPC 概念，不上报、不同步、不进 `TownPlayers`。
   表的 `group` 全是 `c`，所以改完 **不用 `spacetime publish`**。
2. `MainCommonUI.RefreshNpcs()` **幂等**，按 `CurrentTownId` 挡住 —— 它挂在几个
   高频事件上，不挡的话每次都会把 NPC 拆了重摆。
3. **NPC 不需要每帧 tick**：站着不动，摆完就不管了。别学远端玩家那套插值。
4. `ClearNpcs()` 必须在 `spawner.Release()` **之前**调 —— 那一步会把对象池整个拆掉。

⚠️ 坐标只能在 Excel 里填数字、**看不到背景**，得反复导出试位置（和出生点当初一样的问题）。
NPC 多起来之后值得做个「NPC 摆位」窗口：场景里拖空节点 → 写回 Excel，
和 `角色资源配置` 窗口一个套路。

#### 右上角信息栏

| AutoBind 字段 | 显示什么 | 数据来源 |
|---|---|---|
| `levelValueTex` | 角色等级 | `my_character`（**角色级**） |
| `expSlider` | 经验条 | 当前经验 ÷ `TbLevelExp.ExpToNext` |
| `strengthSlider` / `strengthValue` | 体力条 + `当前/上限` | 当前体力 ÷ `TbLevelExp.MaxStamina` |
| `coinValue` / `gemValue` | 金币 / 钻石 | `my_wallet`（**账号级，全角色共享**） |

**两条条的分母都来自配置表 `TbLevelExp`**，服务端只发当前值 —— 上限按等级客户端自己查，
不白占同步量。满级（`ExpToNext=0`）经验条按满显示。

#### 城镇角色：两层结构

城镇角色是**世界空间**的（不是 Canvas 下的 `SkeletonGraphic`），实例挂在
**`Games/SkeletonCharacters`** 节点下（靠 Tag 找，见上面「场景里的两个世界空间根节点」）。
它是**两层**的（用户 2026-08-25 定的）：

```
TownCharacterController          ← 所有角色共用一个预制体。位置都作用在这层
├── SkeletonTown                 ← 运行时按形态把 Spine 塞进来
│   └── TownSkeletonController   ← 按 (JobId, FormId) 取，形态不同预制体不同
└── NameAnchor  [BoneFollower]   ← 跟随头部骨骼
    └── Name    [TextMeshPro]    ← localPosition.y 就是头顶偏移
```

分两层的原因：城镇角色不只有 Spine，还要挂名字、以后还有血条 / 称号 / 气泡。
那些和「用哪个 Spine」无关，所以放外层，Spine 只当可替换的子件。

| 文件 | 职责 |
|---|---|
| `Town/TownCharacterController.cs` | **外层**：位置（走路 / 传送 / 远端插值）、名字、组装 Spine |
| `Town/TownSkeletonController.cs` | **Spine 层**：动画（同名不重播）、朝向。移动速度两个数值也在这（按角色微调） |
| `Town/TownCharacterSpawner.cs` | 取用 / 回收**两个**实例再组装。PoolManager：一个 SpawnPool + 每个预制体一个 PrefabPool |

Spine 预制体路径在配置表 **`CharacterForm.SkeletonTown`** 列（**按形态分**，觉醒后城镇形象也变）；
外层预制体是常量 `TownCharacterSpawner.ControllerPrefabKey`（所有角色共用，不进配置表）。

三条别破坏的约束：

1. **位置作用在外层，朝向翻在 Spine 层。** 反过来的话：位置放 Spine 层名字不会跟着走；
   朝向翻外层名字文字会**镜像**。
2. **回收时先 `Unbind()` 摘 Spine 再 Despawn 外层**。不摘的话 Spine 会跟着外层一起被
   Despawn 到池子根节点下，下次取外层时里面还挂着上一个形态。
3. **`BoneFollower.SkeletonRenderer` 必须运行时接**（`TownCharacterController.Bind`）——
   Spine 是动态塞进来的，预制体里那个引用只能是空的。Inspector 里显示
   「SkeletonRenderer is unassigned」是**预期的**，不是配错了；预制体里也因此把
   `initializeOnAwake` 关了。

#### 名字跟随头顶骨骼（BoneFollower 没有 offset 参数怎么办）

用 Spine SDK 的 `BoneFollower` 跟骨骼很好用（Inspector 里能选骨骼），但它**没有偏移参数**。
解法是**加一层父物体**：`NameAnchor` 挂 `BoneFollower` 精确贴在骨骼上，
文字作为**子节点**带一个 `localPosition.y` 偏移 —— BoneFollower 只写自己那个 transform，
碰不到子节点，所以偏移稳定不会被覆盖，而且编辑器里所见即所得。

> ⚠️ **不要派生 `BoneFollower` 来加偏移。** 它的 `LateUpdate()` 虽然是 `virtual`，
> 但 `BoneFollowerInspector` 是 `[CustomEditor(typeof(BoneFollower))]` **没开
> `editorForChildClasses`** —— 派生子类会丢掉那个骨骼下拉框，`boneName` 退化成纯文本框。

BoneFollower 的设置（预制体里已经这么配了）：

| 开关 | 值 | 为什么 |
|---|---|---|
| `followXYPosition` | ✔ | 就是要跟位置 |
| `followZPosition` | ✘ | Z 由排序层管，别让骨骼带着跑 |
| `followBoneRotation` | ✘ | 名字要一直朝上，不跟头骨转 |
| `followSkeletonFlip` | ✘ | 朝左时文字**不能镜像** |
| `initializeOnAwake` | ✘ | 骨架是运行时塞的，Awake 时还没有 |

⚠️ **名字的 SortingLayer 要在角色之上。** 本工程的层序是
`Default < Ground < UIGround < EffectDown < Environment < Character < EffectTop < ...`，
TextMeshPro 默认落在 `Default` ⇒ 会被 `Character` 层的 Spine **压住**（实测过：
名字被头发挡了一半）。预制体里已改成 `Character / order 100`。

实测确认了它真的解决了原始需求 —— **角色站着不动、只播待机动画时**：
根节点坐标恒为 `(0,0,0)`，而 `hairB` 骨骼的 WorldY 在 `1.580 ↔ 1.617` 之间呼吸，
锚点精确跟上，名字的 local 一直是 `(0, 0.5, 0)`，world 跟着变。

自己和别人**用同一套两层结构**，区别只在谁驱动：

- **自己** —— `MainCommonUI.Update()` 每帧读摇杆（`FixedJoystick.Direction`，Joystick Pack），
  本地立刻移动，然后**节流上报**：位置真的变了（或「在不在走」变了）且距上次 ≥100ms 才发一次。
  ⚠️ **别把 `TransformReportInterval` 改小** —— 这是整个模块调用最频繁的 Reducer。
- **别人** —— 按服务端推的坐标 `Lerp` 追过去。**不能每帧重建列表**：
  `TownPlayersChanged` 只在有人进出时触发，坐标走单行更新（`TownManager.ApplyTransform`），
  渲染那边每帧自己读 `TownPlayers`。

两条实现约束（改的时候别破坏）：

1. **`RefreshSelfCharacter` 必须幂等**。它挂在 `CharactersChanged` 上（角色数据可能
   **比本界面打开得晚**，不重试的话城镇里没有自己），而那个事件会频繁触发 ——
   所以用 `selfJobId`/`selfFormId` 记住当前形态，没变就什么都不做。
   不然角色会不停闪、位置被反复拉回原点。
2. **自己那一行要从 `TownPlayers` 里排除**（按 `ConnectionId == Conn.ConnectionId`）。
   不排除的话自己会被服务端坐标拉着走，和本地输入打架。

> ⚠️ `TownCharacterSpawner.Release()` 里**先拆池子再还 AA 引用**，顺序不能反 ——
> 反过来池子里还留着已经卸掉的预制体，下次 Spawn 拿到空引用（AudioManager 踩过同类问题）。

#### Spine 在 UI 里的播法

Spine 4.3 把渲染和动画拆开了：`SkeletonGraphic` 只负责在 Canvas 下渲染，
**动画在同物体的 `SkeletonAnimation` 上**（`SkeletonGraphic` 自己没有 `AnimationState`）。
所以播动画要驱动 `SkeletonAnimation`；它的 `AnimationState` getter 会自己 `Initialize`，
预制体刚实例化还没走过一帧也能直接用。

### UGUI 界面开发的坑（接选人 / 创角 / 起名字界面时**实测踩过**的，不是推测）

**1. 装饰图会吃掉点击。** 边框、选中框、标题文本这些盖在按钮上面（兄弟序在后 = 渲染在上层）
且 `raycastTarget=true` 的话，按钮点不动。表现是「按钮完全没反应」，很难往回追。
纯展示的元素一律把 `raycastTarget` 关掉。
（形态卡的 `Farme` / `Selected` / `NameFarme/Name` 三个都挡过箭头。）
查的办法：运行时对按钮中心做一次 `EventSystem.RaycastAll`，看最上层命中的是谁。

**2. 「亮/不亮」可能切的是 `Image.enabled`，不是 `SetActive`。** 美术摆预制体时
两种做法都有。星级那排就是用**禁用 Image 组件**表示未点亮的（GameObject 一直 active）——
只切 `SetActive` 的话物体是开的但 Image 还禁着，怎么都不亮。
⚠️ 排查时**光看 `activeSelf` 不够**，要连 `Image.enabled` 一起看（这条让我误判过一轮）。

**3. `Destroy` 延迟到帧末。** 同一帧里先 `Destroy` 旧的再 `Instantiate` 新的，
旧的还在，两个会叠着显示（连点箭头实测叠出过 3 张全屏立绘）。
销毁前先 `SetActive(false)`，视觉上立刻消失。

**4. AutoBind 只绑登记过的节点。** `UIAutoBindGenerator` 的绑定项是在 Inspector 里手选的，
没登记的节点（比如 `NameImg`、卡上的 `Selected`）生成的 AutoBind 里没有，
代码里 `Get<T>("路径")` 手动取即可 —— 那是框架自带的方法，重新生成 AutoBind 也不会被覆盖。

**5. 预制体上的组件可能没挂。** 生成器的 `AttachGeneratedComponent=true` 只在**脚本已编译**
时才挂得上；脚本刚生成那一次挂不上，之后也不会自动补。发现「预制体上只有
UIAutoBindGenerator 没有业务组件」就手动补一下（挂到预制体上，实例会继承）。

**6. 节点名可能和实际位置对不上。** 形态卡的 `LastArrowButton` 在**右边**、
`NextArrowButton` 在**左边**。按名字接线会导致方向反。
所以那两个箭头是**按 `anchoredPosition.x` 判断左右**再绑的 —— 以后挪位置也不用改代码。

**7. `Bind` 的第三个参数是点击音效 ID，要传。** 不传的话 `AudioManager` 会拿空 ID 查表并报错。
工程里统一用 `AudioKeys.CursorClick01`。

**8. `OnDestroy` 里用 AutoBind 的字段要判空。** 界面预制体没走过 `Init` 就被销毁
（场景里手摆了一份、进 Play 时被清掉）时那些字段是 null，不判会抛 NRE，
而且报错内容和真正原因看着毫无关系。

**9. 加载资源用 `UIBase.LoadAsset<T>(key)`**，不要直接调 `AssetsManager` ——
前者会把 key 托管起来，面板关闭 / 销毁时自动 `FreeAsset`，不用手动配对释放。

**10. 验界面别只看编译过。** 用 `unity command editor_play` 进 Play，
`eval` 里对按钮中心 `RaycastAll` + `ExecuteEvents.ExecuteHierarchy` 派发点击，
就是玩家真实的点击路径。

⚠️ **本工程的 Canvas 全是 `ScreenSpaceCamera`，不是 Overlay**（2026-08-25 实测订正，
以前这里写的 Overlay 是错的）。场景里三个根 Canvas：

| 根 Canvas | renderMode | worldCamera |
|---|---|---|
| `UIBackground` | ScreenSpaceCamera | `MainCamera`（depth -1，clear Skybox）。**城镇背景已经不用它了**，见本节「城镇主界面、背景与出生点」 |
| `UILayout`（所有界面都在这下面） | ScreenSpaceCamera | `UICamera`（depth 0，clear Nothing） |
| `PopLoadingUI` | ScreenSpaceCamera | `UICamera` |

由此有两条直接影响调试的后果：

- **算点击坐标必须传 canvas 的相机**：`RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, rt.position)`。
  传 `null`（Overlay 的写法）拿到的是世界坐标，`RaycastAll` **一个都打不中**，
  表现成「点击派发了但界面毫无反应」，很容易误判成事件被谁吃了。
- **`capture_game_view` 默认拍不到 UI，但原因不是 Overlay**：它默认 `source=camera`，
  渲的是 `MainCamera`，而 UI 挂在 `UICamera` 上 ⇒ **UI 一个都没有**。
  （2026-08-25 起 `MainCamera` 上有世界空间的城镇背景和角色了，所以 `camera` 源现在能拍到
  城镇画面、只是缺 UI；在那之前拍出来是一张纯色背景。）
  加 `--source screen` 抓合成后的 backbuffer 就整屏都有（**仅 Play 模式**）：

```bash
unity command capture_game_view --source screen --save_path "Temp/shots/screen.png"
```

⚠️ `save_path` 必须在项目根内（给项目外的绝对路径会 400 `outside the project root`），
而且**相对路径是相对 `Assets/` 解析的** —— 上面这条实际存到了 `Assets/Temp/shots/screen.png`，
会进资源库，用完记得删。

要在运行时代码里截图仍可用 `ScreenCapture.CaptureScreenshot`。

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
只在 3.8 工程创建阶段应用一次。角色工程以 `SpineScaleCheck\fixed_output` 的
优衣样本为基准，创建 3.8/升级 4.3 时隐藏图片，之后恢复完整画布再导出。

---
