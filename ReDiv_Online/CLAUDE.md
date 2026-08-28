# ReDiv 客户端 —— 技术文档

Unity 客户端。服务端在 `../ReDiv_Server`（SpacetimeDB 模块）。
总纲与工具链规则见 [../CLAUDE.md](../CLAUDE.md)，先读那个。

---

## 1. 基本情况

| 项目 | 值 |
|---|---|
| Unity | 6000.4.8f1 |
| 渲染管线 | URP 17.4.0。默认 renderer 是 **`CubismURPRenderer`**（Live2D 那个），不是 `Renderer2D` —— 见第 7 节 |
| 2D 遮挡排序 | 全局 `Transparency Sort Mode = Custom Axis (0,1,0)`（站得靠下的盖住靠上的），见第 5 节 |
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
│       ├── Backgrounds/       只有 FitBackgroundToCamera（**副本区域背景不在这儿**，
│       │                      它跟着界面走：UGUI/PopDungeonUI/DungeonAreaBackground.cs）
│       ├── Dungeon/           副本：通关进度（占位）/ 选择状态源（组队口子）。界面在 UGUI/ 下
│       ├── Luban/             Tables.cs 等（生成物，见第 3 节）
│       ├── Resolution/
│       ├── Save/
│       ├── System/            GameManager / GameDataManager / LubanManager
│       ├── Tools/
│       ├── Town/              城镇背景（世界空间）/ 出生点 + 边界 / 触发器 / 角色与 NPC 的两层控制器 + 取用回收
│       └── UGUI/
└── Net/                无 asmdef → 进 Assembly-CSharp   ← 网络层，见第 5 节
    ├── SpacetimeConnection.cs  只管连接生命周期 + ServerLinkState，不建任何订阅
    ├── AuthManager.cs          账号门面（纯 C# 单例，UI 只跟它打交道）
    ├── CharacterManager.cs     角色门面，同上。登录成功后才订角色数据
    ├── AuthValidation.cs       服务端 AuthRules 的客户端镜像
    ├── CharacterValidation.cs  服务端 CharacterRules 的客户端镜像（角色名格式）
    ├── TownManager.cs          城镇门面：当前城镇 / 时段 / 同城镇玩家 / 坐标上报 / **换城镇**
    ├── ChatManager.cs          聊天门面：两个频道的消息列表 / 发言。附近订阅**跟着当前城镇换**
    ├── ChatValidation.cs       服务端 ChatRules 的客户端镜像（正文清洗 + 长度）
    └── ModuleBindings/         生成物，不要手改
```

`UGUI/` 下已接好的界面：`CommonUI`（标题）、`LoginUI`、`PopDialogueUI`、`PopLoadingUI`、
`SelectCharacterUI`（选人）、`CreatCharacterUI`（创角）、`ReviseCharacterNameUI`（起名字）、
`MainCommonUI`（城镇主界面，含左下角**聊天框** + `MessageSlot/` 消息格子）、
`PopMessageUI`（聊天弹窗：附近 / 世界两个页签 + `MessageUI/` 消息行）、
`PopDungeonUI`（副本界面 + `DungeonSlot/` 副本格子：区域背景 / 副本列表 / 星级切换已通，
**进副本没做**）。

`Assets/Editor/` 也在 Assembly-CSharp-Editor 里（无 asmdef），包含
`AddressableTools/`、`AddressableKeyGeneratorWindow/`、`BuildTools/`、`Luban/`、
`ServerTools/`、`TownTools/`（NPC 摆位 / 触发器摆位 + Excel 写回壳子）、`Tools/`、`UGUI/`、`UITools/`、
`PathologicalGames/`（第三方）。

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
> 现在**九张**数据表（`CharacterJob` / `CharacterForm` / `Town` / `TimeBand` / `LevelExp` /
> `TownNpc` / `TownTrigger` / `DungeonArea` / `Dungeon`）都是 4 行表头，
> `read_schema_from_file` 全是 `True`。
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
| `TownNpc.xlsx` | `TbTownNpc` | 城镇 NPC：站在哪个城镇的哪个世界坐标。**纯客户端**（全 `c`） |
| `TownTrigger.xlsx` | `TbTownTrigger` | 城镇触发器：矩形判定区，走进去传送 / 开副本界面。**纯客户端**（全 `c`） |
| `DungeonArea.xlsx` | `TbDungeonArea` | 副本区域（像 DNF 的格兰之森）：名字 + 一张背景。**纯客户端**（全 `c`） |
| `Dungeon.xlsx` | `TbDungeon` | 小副本：一行一个格子，含 `MaxStar` 和**格子在界面里的 UI 坐标**。**纯客户端**（全 `c`） |

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
| 一键出包 | `Assets/Editor/BuildTools/PlayerBuildWindow.cs`（Windows64 / Android，见下面「一键出包窗口」）|
| 自动打包 Addressable | `Assets/Editor/AddressableTools/AddressableBuild.cs` |
| 清空 Addressable 标签内容 | 同上 |

⚠️ 那个菜单项 2026-08-26 之前叫「Windows 一键出包」，加了安卓之后改成了「一键出包」，平台在窗口里选。

### 一键出包窗口（Windows64 / Android，2026-08-26 加的安卓）

`Assets/Editor/BuildTools/` 五个文件：

| 文件 | 职责 |
|---|---|
| `PlayerBuildConfig.cs` | 配置（`SerializedScriptableObject`）+ 校验。平台相关的派生值（BuildTarget / 扩展名 / 输出路径）都在这 |
| `PlayerBuilder.cs` | 流程：校验 → **切平台** → 写 ProjectSettings → Addressable 前置 → BuildPipeline → 收尾 |
| `PlayerBuildVersionSync.cs` | 版本号从配置写到 ProjectSettings 和**所有** Build Profile 快照，见第 9 节 |
| `AndroidToolchainCheck.cs` | Android 外部工具链（SDK / NDK / JDK / Gradle）的**飞行前检查**，见下 |
| `PlayerBuildWindow.cs` | Odin 窗口 |

流程里有**顺序约束**：**切平台必须排在 Addressable 构建之前** —— Addressable 的 bundle 是按平台打的，
顺序反了会把上一个平台的资源打进这个包里，而且不报错，只表现成真机上加载不出资源。

`ScriptingBackend` / `Il2CppConfiguration` / 裁剪等级 / 包名这些是**按平台分别写**的
（`PlayerSettings.SetXxx(NamedBuildTarget)`），所以两个平台各留各的值，切平台不用重配。

#### Android 那一页的实现约束

| 项 | 说明 |
|---|---|
| **符号表等级** | 走**反射**设 `UnityEditor.Android.UserBuildSettings.DebugSymbols.level`。旧的 `EditorUserBuildSettings.androidCreateSymbols` 在 6000.4 已废弃（编译会报废弃警告）；新 API 在平台扩展程序集 `UnityEditor.Android.Extensions` 里，**直接引用的话没装 Android 模块的机器整个 `Assembly-CSharp-Editor` 都编不过**，所以只能反射。枚举名要和 `Unity.Android.Types.DebugSymbolLevel`（`None` / `SymbolTable` / `Full`）一字不差，反射是按名字 `Enum.Parse` 的 |
| **纹理压缩** | 写 `PlayerSettings.Android.textureCompressionFormats`（数组，取第一个），不是那个已经变成 legacy 的 `EditorUserBuildSettings.androidBuildSubtarget` |
| **`BuildPlayerOptions.subtarget`** | 只有 Standalone 用得上（Player / Server）。Android 必须留 0，别把 `StandaloneBuildSubtarget` 传过去 |
| **Keystore 口令** | 存在**本机 EditorPrefs**（键里带 `Application.dataPath` 防串工程），**不写进配置资产** —— 那个资产是进 git 的。换机器要重填，校验会提示 |
| **不用自定义签名时要主动清空** | `keystoreName` / `keyaliasName` 留在 ProjectSettings 上会被当成「自定义签名没生效」来查，所以 `useCustomKeystore=false` 时把四个字段一起清掉 |
| **ARM64 必须 IL2CPP** | Mono 出不了 64 位包。校验里挡住了，不然要到 gradle 那一步才报 |
| **联网权限** | `forceInternetPermission` 校验里**强制要求为真** —— 本项目是联网游戏，关掉的话包里没有 INTERNET 权限，真机连不上服务器，而且现象是「卡在连接中」，很难往回追 |
| **`aab` / `apk` 的互斥项** | 拆分二进制(Play Asset Delivery)只有 aab 有意义、分架构出包只有 apk 有意义，写 PlayerSettings 时按格式过滤了一遍，别只靠界面隐藏 |
| **装到设备** | `adb install -r`。adb 先看 Preferences 里配的 SDK，没配就用编辑器自带的 `<EditorData>/PlaybackEngines/AndroidPlayer/SDK`。**装失败不算出包失败** —— 包已经出好了 |

#### Android 工具链飞行前检查（`AndroidToolchainCheck`）

`PlayerBuildConfig.Validate()` 里调一次，在**任何东西开始构建之前**就把
SDK / NDK / JDK / Gradle 路径不对的情况报出来。

**为什么要有这一步**：路径不对的话，要等到 `BuildPipeline.BuildPlayer` 才抛
`Android NDK not found` —— **而那时 Addressable 已经白构建了一遍**，几分钟就没了。

三条实现约束：

1. **判定不自己写**，直接反射调 Unity 自己的 `AndroidRoot.Validate(path)`，
   口径和 `Preferences > External Tools` 里画红字用的完全一样。
2. **只能走反射**：那些类型在平台扩展程序集 `UnityEditor.Android.Extensions` 里，
   直接引用会让**没装 Android 模块的机器整个 `Assembly-CSharp-Editor` 编不过**
   （和 Android 那一页的符号表等级同一个理由）。
3. **反射失败一律当作没问题**（fail open）。这只是一层提前预警，
   不能因为 Unity 换了内部类名就把出包卡死。

⚠️ 实测踩过（2026-08-26）：**Unity 自带的 NDK 解压时多套了一层**
`NDK/android-ndk-r27c/`（SDK 和 OpenJDK 都是平铺的，只有它不是），
于是 `NDK/source.properties` 找不到、被判成 invalid。

NDK 只有 IL2CPP 才用得上，所以 Mono 时不拿它挡路（`FindProblems(includeNdk)`）。

⚠️ **真机/局域网测试记得改 `RemoteLoadUrl` 和 `SpacetimeConnection` 的地址** ——
留 `127.0.0.1` 的话手机连的是它自己。局域网地址是 `http://192.168.10.226:2383`。

⚠️ **输出目录默认按平台分了一层**（`Build/Windows64/…`、`Build/Android/…`）。
这是配置里「按平台分子目录」那个开关，关掉之后两个平台的产物会挤在同一层 ——
文件名模板里没有 `{platform}` 的话会撞在一起。

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

### 角色美术资源在 Luban 的形态表里

资源全在 `CharacterForm`（形态表）的客户端列上，填 **Addressable 完整资源路径**。
**Excel 是唯一真相源。**

> ⚠️ **原来那个「角色资源配置」窗口 2026-08-26 按用户要求删掉了**（用着不方便），
> `Assets/Editor/CharacterTools/` 整个目录已经没了。别再在文档或代码里找它，
> 也别未经要求重新做一个。
>
> 现在填路径的做法：在 Project 面板选中资产 **右键 Copy Path**，粘进 Excel ——
> 手敲容易错，而且改名不同步，只会在运行时表现成「加载不出来」。

> 2026-08-23 和 08-24 各试过一次把资源整套挪进 Odin ScriptableObject，两次都当天退回。
> **结论：数据留在 Excel。** 别再提议搬走。

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

#### 三个摆位窗口：`Tools > XFramework > 配置 >` `NPC 摆位` / `触发器摆位` / `副本摆位`

实现在 `Assets/Editor/TownTools/TownNpcPlacementWindow.cs`（2026-08-26 加的）。
解决的是「`TownNpc` 表里的 `PosX`/`PosY` 是世界坐标，在 Excel 里只能填数字、看不到背景」。

```
TownNpc.xlsx --(Luban 导出)--> tbtownnpc.json --(窗口读)--> 场景里的预览对象（可拖）
             <--(ExcelTable.ps1 AddRows/UpdateRows/DeleteRows)-- 「写入 Excel」
```

用法：选城镇 → 「打开预览」（把那张背景和这个城镇所有 NPC 生成到场景里）→
在 **Scene 视图**里直接拖（列表里点「选中」能跳过去）→ 「写入 Excel」（默认顺手重新导出）。

- **预览对象全带 `HideFlags.DontSave`** —— 不会被存进场景，进 Play / 重开场景就没。
  关窗口时也会自动收干净，另外还按名字兜了一次底（域重载之后引用可能丢）
- 背景直接放在**原点**，和运行时挂法一致（`Games/Backgrounds` 那一串都是原点 + 缩放 1），
  所以窗口里拖出来的世界坐标和游戏里所见一致
- 「预览背景」下拉只影响**用哪张图当参照**（早/中/晚），不影响数据 ——
  NPC 表没有时段这一列，一个 NPC 三个时段都在
- Spine 预制体是**拖进去的**（`GameObject` 字段），路径由 `AssetDatabase` 算，手打不了
- 写回顺序是 **先删、再改、后加** —— 反过来新加的 id 可能和待删的撞上
- 坐标写回时**留三位小数**（世界单位下 0.001 已经是亚像素了，位数多了表里难看）

⚠️ 窗口显示的是**上次导出**的内容。有人绕过窗口直接改了 Excel 又没导出的话这里看不到，
点「重新读取」之前先跑一次配置导出（F6）。

⚠️ 成功路径**不弹框**（每写一次点一下很烦，而且模态框会卡住无人值守的自动化 ——
用 `unity command eval` 驱动窗口时实测卡死过）。只有失败才弹。

**触发器摆位窗口**（`Assets/Editor/TownTools/TownTriggerPlacementWindow.cs`，2026-08-27 加的）
和上面那个是同一套结构（同一份 `ExcelTableRunner`、同样的 `HideFlags.DontSave` 预览、
同样的「先删后改再加」写回顺序），表换成 `TownTrigger.xlsx`。两点不一样：

- **位置在场景里拖，宽高在窗口的表里填数字**（改完 Scene 视图里的框立刻跟着变）——
  拖矩形的边要写自定义 `Handles`，而宽高本来就是「门口多宽」这种一眼能定的数；
- 传送点下面还有一个**可拖的「出口点」子节点**（绿圈）—— 别人从对端传送过来时站那儿。
  做成子节点是为了能用 Unity 自己的移动工具拖，写回时读它的 `localPosition` 当偏移，
  所以**先拖中心再拖出口点**两个值都不用互相换算。运行时**不会**有这个节点（出口点只是两个数），
  `TownTriggerController.OnDrawGizmos` 里那个「有子节点就以子节点为准」的分支就是为它留的；
- 「对端 / 区域」那一列的下拉**按行的 `Kind` 分流**（两种触发器的 `TargetId` 不是一类东西）：
  传送 → **别的城镇的传送点**（同城镇互连运行时会被拒）；副本 → **副本区域**（`DungeonArea`）。
  改了 `Kind` 会把 `TargetId` **清成 0** —— 留着上一种语义的值必然配错。
  ⚠️ 2026-08-27 副本做完之前这一列对副本是「填 0」，那时下拉只有传送点，
  **用窗口写回一次就会把副本触发器的区域 id 冲掉**（补文档时发现的，当天修了）。别再退回那个形状；
  ⚠️ 它列的是**上次导出**的表内容 —— 刚在窗口里新加、还没写回 Excel 的传送点不会出现，
  先写一次再来连；
- 预览对象上挂的是**运行时那个** `TownTriggerController`，所以框是同一份代码画的
  （蓝框=传送、橙框=副本）—— 编辑器里所见即游戏里所得。

写回前会校验一遍，和运行时 `TownTriggers.InTown` 的校验是同一套口径、只是提前到写 Excel 之前：

| 两种都查 | id 重复、宽高 ≤ 0 |
|---|---|
| 传送（Kind=1） | 没连对端 / 连的是自己 / 对端不存在 / 对端不是传送点 / 对端在同一个城镇 / **出口点还在传送阵正中心** |
| 副本（Kind=2） | 没选副本区域 / 那个区域不在 `DungeonArea` 表里 |

#### 副本摆位窗口（`Assets/Editor/DungeonTools/DungeonSlotPlacementWindow.cs`，2026-08-27）

摆的是**副本界面里那些格子的位置**（`Dungeon.xlsx` 的 `PosX` / `PosY`）。
流程和上面两个一样（选目标 → 打开预览 → Scene 视图里拖 → 写回 Excel），
但有一条**本质区别**：

⚠️⚠️ **这里拖的是 UI 坐标，不是世界坐标。** 所以预览**必须挂在真实的 Canvas 下**
（`GameManager/UISystem/UILayout/UIPanel` —— 编辑器下场景里就有那几个节点），
而不是像 NPC / 触发器那样丢在世界原点：UI 坐标要经过 `CanvasScaler` 才有意义，
挂错地方拖出来的数字是废的。写回读的是格子根节点的 `anchoredPosition`。

预览里摆的是**真东西**：`PopDungeonUI` 预制体 + 区域背景（拉满，和运行时同一个挂法）
+ 每个副本一个真实的 `DungeonSlot` 预制体（略缩图和名字都填上），
所以 Scene 视图里看到的就是游戏里的样子。

⚠️ 预览里那些 `DungeonSlot` 的 `Init` **不会跑**（`UIBase` 没有 `[ExecuteAlways]`），
所以略缩图和名字是窗口**手动**塞进组件的；星星停在预制体的默认状态，不代表运行时的星级。

顺手带了一个「按当前顺序横排一次」按钮 —— 只是给个起点（按 `SortOrder` 均匀横排在屏幕中间），
摆成什么样还是要自己拖。

⚠️ 坐标写回时**取整**（UI 像素，小数没意义）；另外两个窗口是世界坐标、留三位小数。

跑 Excel 那一步会后台起一个 EXCEL.EXE，**秒级**，比 Pipeline 的 5 秒超时还长 ——
用 `eval` 驱动时会看到一条 `/api/exec ... timed out`，那是 Pipeline 自己的超时，
写入其实成功了，别当成失败。

### 编辑器菜单约定

项目自己的编辑器工具**全部**挂在 `Tools > XFramework/` 下，分五个子菜单：
`打包/`、`服务端/`、`配置/`、`UI/`、`实用工具/`。排序靠 `[MenuItem(path, false, priority)]`
的 priority 显式指定（不要再用 "1." "2." 这种字符串前缀）：
打包 100~121、服务端 150、配置 200~203（200 LuaConfig / 201 NPC 摆位 / 202 触发器摆位 / 203 副本摆位，下一个用 204）、UI 300、实用工具 400~423。
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

网络层门面 `Assets/Scripts/Net/TownManager.cs`，和其余几个门面一个套路：
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
| `ChangeTownAsync(townId)` / `IsChangingTown` | 传送到另一个城镇（踩到传送触发器时调）。返回 `TownResult { Ok, Message }`，Message 是可直接显示的中文 |

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

这个界面管的事按小节往下排：**背景 + 出生点**（本小节）、**可行走边界**、
**城镇 NPC**、**城镇触发器（传送 / 副本入口）**、**聊天框 + 说话气泡**、
**右上角信息栏**、**城镇角色（自己 + 同城镇其他玩家）**。

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
   ⚠️ 新做背景**一定要手动设这个** —— 新建 SpriteRenderer 的默认值是 `Default:0`，
   而工程的层序是 `Default(0) < Ground(1) < UIGround < EffectDown < Environment <
   Character(5) < EffectTop < …`。2026-08-26 就发现中午和晚上那两张一直是默认值
   （`Town_50021` / `Town_50022`），已经统一成 `Ground:-1`。
   漏配的表现很隐蔽：角色照样在前面（Character 本来就比 Default 高），
   只有等你往 `Ground` 层放东西（比如遮挡物）才会突然发现顺序不对。
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
> 但**现在整个工程没有一个调用方**：城镇背景 2026-08-25 搬去世界空间了，
> 副本区域背景 2026-08-27 试过走这一层、当天就改成挂进 `PopDungeonUI` 自己的层级
> （理由见本节「副本界面」——背景层排在城镇角色下面，盖不住城镇）。
> 要给纯 UI 界面垫背景时仍然可以用，但**先想清楚它会不会盖不住你想盖的东西**。

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
兰德索尔（`TownId=1`）现在配了两个：凯露和优衣。

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

**坐标别手填** —— 用 `Tools > XFramework > 配置 > NPC 摆位` 窗口在场景里对着背景拖，
拖完一键写回 Excel，见第 4 节「NPC 摆位窗口」。

#### 城镇触发器：传送点 / 副本入口（配置表 `TownTrigger`，2026-08-27）

一行一个**矩形触发区**，玩家走进去就生效。手感是用户 2026-08-27 定的：
**传送自动**（踩到就走，不弹确认框），**副本弹界面**（开 `PopDungeonUI`，见本节「副本界面」）。

**传送阵是成对的**（用户 2026-08-27 定的）：A 连着 B，从 A 过去就出现在 **B 的出口点**旁边
—— 不是新城镇的出生点。所以 `Kind=1` 的 `TargetId` 存的是**对端传送点的 TriggerId**，
目标城镇由对端那一行的 `TownId` 推出来。

```
兰德索尔 #1 ──TargetId=3──▶ 测试镇 #3 ──▶ 落在 #3 的位置 + #3 的 ArriveOffset
测试镇   #3 ──TargetId=1──▶ 兰德索尔 #1 ──▶ 落在 #1 的位置 + #1 的 ArriveOffset
```

**为什么目标城镇不单独存一列**：那样会出现「城镇写 B、对端传送阵却在 C」这种不一致，
而且两处都要改。现在只有一个真相：对端是哪个传送点。
（**不要求互指** —— 单向传送门是合法设计，校验只看对端存在 / 是传送点 / 在别的城镇。）

| 列 | 说明 |
|---|---|
| `TriggerId` | 全局唯一 |
| `TownId` | 在哪个城镇 |
| `Kind` | `1` 传送 / `2` 打开副本界面 |
| `TargetId` | Kind=1 → **对端传送点的 TriggerId**；Kind=2 → **副本区域 id**（配置表 `DungeonArea.AreaId`，见本节「副本界面」） |
| `PosX` / `PosY` | 矩形**中心**的世界坐标 |
| `Width` / `Height` | 矩形大小（世界单位） |
| `ArriveOffsetX/Y` | **出口点**：别人从对端传送过来时站在「本触发器中心 + 这个偏移」上。摆位窗口里拖那个绿圈 |
| `Name` | 提示文字（中文原文）。现在只在摆位窗口和日志里用 |
| `IconPrefab` | 地面标记预制体的 Addressable 完整路径，**可空**。配了就在触发器中心实例化一个 |

| 文件 | 职责 |
|---|---|
| `Game/Scripts/Town/TownTriggers.cs` | **纯数据查询 + 几何判定**（静态，和 `TownGround` 一个路子）+ 配置校验 |
| `Game/Scripts/Town/TownTriggerController.cs` | 场上那个节点：判定区的 Gizmo + 地面标记的挂载点 |
| `Game/Scripts/UGUI/MainCommonUI/MainCommonUI.cs` | `RefreshTriggers` / `TickTriggers` / `Fire` / `TeleportAsync` |
| `Net/TownManager.cs` | `ChangeTownAsync(townId)` —— 调服务端 `ChangeTown` |

**坐标别手填** —— 用 `Tools > XFramework > 配置 > 触发器摆位` 窗口，见第 4 节。

九条实现约束（改的时候别破坏）：

1. **不开物理**。角色身上没有 `Rigidbody2D` / `Collider2D`（和可行走边界同一个理由），
   一个城镇的触发器个位数，所以判定就是拿**脚下那一点**做矩形包含。
   别改成 `OnTriggerEnter2D` —— 那要给角色加刚体，还会受 FixedUpdate 节奏影响。
2. **`currentTriggerId` 是整套逻辑的关键**：只在它**从别的值变成某个触发器**的那一帧才触发一次。
   不记状态的话站在门口会每帧发一次传送请求。
3. **进城镇 / 传送落地时要按当前位置「初始化」它，而不是触发**（`SyncCurrentTrigger`）——
   出生点正好压在触发器上（或者刚从这里传送过来）不该立刻被弹走。
   所以 `RefreshTriggers` 必须排在**自己落位之后**，顺序反了会拿旧坐标去判。
4. **传送成功后客户端什么都不用做**：服务端改完 `character_selection.TownId` 推回来 ⇒
   `LocationChanged` ⇒ 背景、自己、别人、NPC、触发器、聊天订阅域全都自己重来。
   ⚠️ **不要**在本地先把 `CurrentTownId` 改掉（那是本地乐观显示，被拒时还得回滚 ——
   和聊天那条「不做本地乐观显示」同一个道理）。
5. **「我是从哪个传送点过去的」必须在 `await` 之前记**（`pendingArriveTriggerId`）——
   换城镇要绕服务端走一圈回来，而**表更新（于是 `LocationChanged` → 重新落位）通常比
   Reducer 的状态回调先到**。等 `ChangeTownAsync` 返回再记就已经落到出生点上了。
   兑掉它时会核对「那个传送点确实在我要落位的这个城镇里」，所以过期值无害。
6. **出口点别指到可行走边界外面**：传送是直接写坐标、不走边界判定，落在墙外之后
   移动的扫掠查询会从「已经嵌在碰撞体里」开始，可能一步都走不动。摆位窗口里对着背景拖。
7. **传送失败不自动重试**：人还站在触发器里、`currentTriggerId` 已经记成它了，
   所以得走出去再走进来。有意的 —— 失败原因通常是「去不了」，原地重试只会每帧刷一个弹窗。
   （失败时要把 `pendingArriveTriggerId` 清掉，别留个垃圾值影响下一次进城镇。）
8. **触发器挂在 `Games/Backgrounds` 根节点下，不是背景那张图的子节点** ——
   触发器和时段无关（一个城镇一套，早/中/晚共用），塞进背景里换时段就会被一起拆掉。
9. **配错的行跳过 + 报错**（`TownTriggers.InTown` 里），不是静默忽略：
   静默的话表现成「这个传送点踩了没反应」，从现象根本看不出是配漏了。
   传送点这边查的是：对端存在、对端是传送点、对端**在别的城镇**、那个城镇在 `Town` 表里。

**落点有两种，传送优先**（都在 `PlaceSelfAtSpawn` 里）：

| 情况 | 落在哪 |
|---|---|
| 从传送阵过来 | **对端那个传送点的出口点**（它的位置 + 它自己的 `ArriveOffset`） |
| 其它（进游戏、换角色、对端配漏了） | 那张背景的 `StartPoints`（用户 2026-08-25 定的「不记住上次站的位置」） |

⚠️ **换城镇是「形态不变、位置必须重来」的情况。** `RefreshSelfCharacter` 在形态没变时会
提前返回（那是为了别把角色反复拉回原点），所以传送要靠 `selfTownId` 这个字段
+ `PlaceSelfAtSpawnIfTownChanged()` 才会重新落位。
**漏了这一步的表现是「传送过去之后站在旧城镇的坐标上」**，可能直接在可行走边界外面。

⚠️ 传送时服务端把坐标行**删掉**了（见服务端文档的 `ChangeTown`），所以落位之后
**那一次 `ReportTransform` 不是可选的** —— 不发的话新城镇的人要等我走第一步才看得见我。

⚠️ **服务端不认识触发器。** 「能不能去那个城镇」是服务端 `ChangeTown` 校验的
（现在是「都能去」）。所以这张表改目标城镇**不等于**改了权限 ——
改过的客户端可以直接对任意城镇调那个 Reducer。

⚠️ **现在没有一个触发器配了 `IconPrefab`**，所以传送点和副本入口在游戏里**是看不见的**
（Scene 视图里有 Gizmo 框：蓝=传送、橙=副本）。要让玩家看见得让美术出个地面标记，
填进那一列即可，代码不用动。

#### 副本界面（`PopDungeonUI`，2026-08-27）

结构像 DNF（用户 2026-08-27 定的）：**一个副本区域里有多个小副本**，每个小副本能选 1~6 星难度。

```
DungeonArea（区域，例：幽暗密林）   ← 自带一张背景，标题栏显示它的名字
  └── Dungeon（小副本）× N          ← 界面上一个 DungeonSlot 格子，各自能选星级
```

**入口**：城镇里踩到 `Kind=2` 的触发器，那一行的 `TargetId` 就是**副本区域 id**。
打开方式是「开完界面紧跟一次 `Show(areaId)`」（和创角界面 `SetJob` 一个套路 ——
`OpenUI` 里的 `Open()` 先执行，参数只能紧接着给）：

```csharp
UISystem.Instance.OpenUI<PopDungeonUI>(UIKeys.PopDungeonUI)?.Show(areaId);
```

| 文件 | 职责 |
|---|---|
| `UGUI/PopDungeonUI/PopDungeonUI.cs` | 区域背景 + 标题 + 副本格子列表 |
| `UGUI/PopDungeonUI/DungeonSlot/DungeonSlot.cs` | 一个格子：略缩图 / 名字 / 星级 / 左右箭头 |
| `Dungeon/DungeonSelection.cs` | **「选了哪个副本 / 几星」的状态源 —— 组队的口子在这** |
| `Dungeon/DungeonProgress.cs` | 通关进度（星级解锁靠它）。**本地占位，权威以后在服务端** |
| `UGUI/PopDungeonUI/DungeonAreaBackground.cs` | 区域背景的标记组件（普通 `MonoBehaviour`），挂在区域背景预制体上 |

##### 区域背景挂在本界面自己的层级里，**不走** `UIBackground` 层

```
PopDungeonUI/UIMask
├── (区域背景)     ← 运行时塞进来，永远 SetAsFirstSibling（压在最底下）
├── Background     ← alpha 0 的容器（**不是**区域背景！美术起的名字），里面是标题和关闭按钮
└── Contents       ← 副本格子
```

⚠️⚠️ **这里 2026-08-27 反复过一次，别改回去**：一开始走的是框架的
`UISystem.LoadUIBackground<T>()` 背景层，结果**副本界面开着时城镇整个露在外面** ——
背景层的 Canvas 排在城镇角色下面，只盖住了城镇背景。用户当天订正成「直接挂进
`PopDungeonUI` 层级」，这样整个副本界面（含背景）自然盖住城镇的角色、NPC、
摇杆、聊天框、右上角信息栏。**所以框架那三个 `LoadUIBackground` /
`HideUIBackground` / `ReleaseUIBackground` 又回到了没有调用方的状态**。

- 背景预制体走 `LoadAsset<GameObject>`（key 由 `UIBase` 托管，关闭时自动还 AA 引用）
  + `Instantiate` + `SetAsFirstSibling`，收的时候直接 `Destroy`
  （**先 `SetActive(false)` 再销毁**，见坑 3）；
- **换区域是幂等的**：key 没变什么都不做 —— 重开界面 / 同一个区域重复 `Show` 都不该
  重新实例化一张 1024 的大图；
- ⚠️ **必须拉满整屏**（代码里 `Stretch`：anchors 0~1、offset 全 0）。
  贴图是**压扁的方图**（1024×1024，和城镇背景一个套路），按原尺寸摆会又方又小；
- ⚠️⚠️ **区域背景材质的 `renderQueue` 必须是 3000。** 国服还原出来的 VariantCard 材质
  是 **2000**（原包 bin 里的原值），而 UI 默认 3000 —— queue 小的先画 ⇒
  **2000 的背景会被所有 UI 盖住**，包括城镇的摇杆 / 聊天框 / 右上角信息栏。
  症状极具误导性：**副本界面明明开着、城镇的界面元素还在上面**，看着像层级或兄弟序不对。
  2026-08-27 实测踩过，查错了两轮（先怀疑兄弟序、又怀疑截图是过期帧）。
  `bg_500170_mat.mat` 已经改成 3000 了；`PopDungeonUI.CheckRenderQueue` 会在挂背景时
  **报错但不自动改**（偷偷改美术资产不该由运行时代码做）。
  新做区域背景记得在那个 `.mat` 上把 Render Queue 填 3000。
  ⚠️ 这条和素材文档「材质参数要和原始 bin 字段级一致」有冲突 —— 那条是**还原验证**的要求，
  而这张材质现在的用途是 UI 背景，用途决定它必须 3000。
  ⚠️ **只对 UGUI 成立。** 城镇背景那张 `bg_500020_mat` 也是 2000，但它是**世界空间
  `SpriteRenderer`** —— 2000 恰好让它比角色（Sprite 默认 3000）先画、排在角色后面，
  正是想要的效果，**别去把它也改成 3000**。
- ⚠️ 区域背景预制体上要挂 `DungeonAreaBackground` 组件 —— 它只是个**标记组件**
  （普通 `MonoBehaviour`，不是 `UIBackground` 派生类了），作用是让本界面能存一个
  有类型的引用（`PopDungeonUI.AreaBackground`，用户明确要求的）。没挂只会报一条错，
  图照样显示。`bg_500170_Preview.prefab` 已经挂上了。
  它是 **UI 的 `RawImage`**，**不是**世界空间的 —— 和城镇背景正好相反，别搞混。

##### 格子位置来自配置，**不是平铺**

用户 2026-08-27 定的：格子按配好的位置摆（`Dungeon.PosX` / `PosY`），
不要 `LayoutGroup` 那种自动排列 —— 副本界面的格子摆法是**美术设计的一部分**
（错落、分组、跟着背景里的地形走），平铺表达不了。

| 列 | 说明 |
|---|---|
| `PosX` / `PosY` | 格子的 **UI 坐标**（`anchoredPosition`，相对 `Contents` 中心 = 屏幕中心） |

格子预制体根节点的 anchor 和 pivot 都是 `(0.5, 0.5)`，所以配的就是「相对屏幕中心的偏移」，
和分辨率无关（canvas 参考分辨率 1920×1080）。

⚠️⚠️ **`Contents` 上不能有任何 `LayoutGroup`。** 布局组件会在下一次布局阶段把
`anchoredPosition` 全部覆盖掉，表现是「坐标配了但格子还是排成一行」——
从现象完全看不出是布局组件干的。`PopDungeonUI.CheckContentsLayout()` 会在
`Init` 时**报错并把它禁用**（2026-08-27 之前这里是主动加一个 `HorizontalLayoutGroup`
做平铺的，那个检查也顺带兜住「照着旧行为在预制体里加了布局组」这种情况）。

⚠️ **UI 坐标和世界坐标是两套，别混**：城镇 NPC / 触发器配的是**世界坐标**，
这里配的是 **UI 坐标**。两边的摆位窗口也因此不一样（见第 4 节）。

⚠️ 现在也**没有 ScrollRect** —— 副本多到摆不下就得让美术在预制体里加滚动，
或者分区域拆开。配置坐标这套本身不管溢出。

##### 星级：可选上限 = 已通关 + 1

用户 2026-08-27 定的（像 DNF 的难度递进）：**打过 N 星才能选 N+1 星**。

```
可选上限 = clamp(已通关最高星 + 1, 1, Dungeon.MaxStar)
默认停在可选上限   ← 玩家每次都想打能打的最高难度，停在 1 星等于每次都要点一堆箭头
```

⚠️⚠️ **`DungeonProgress` 是本地占位实现（PlayerPrefs），权威以后在服务端。**
本轮副本是纯客户端的：没有战斗、没有结算，也就没有「谁来写通关记录」。
改过存档的玩家能把星级全解开 —— **不是漏洞，是本轮就没有服务端那一半**。
接结算时把那个类的内部换成服务端表的 View，`MaxClearedStar` / `MaxSelectableStar`
两个签名不用动，界面一行都不用改；那时 `Dungeon.MaxStar` 这一列要开给服务端
（现在整表 `group=c`）。

本轮想试星级解锁只能用调试入口：

```bash
unity command eval --code 'DungeonProgress.DebugSetCleared(31006, 4); return "ok";'
```

##### ⚠️ 组队的口子：选择状态一律走 `DungeonSelection`

用户 2026-08-27 提前交代的：**以后组队时队长在这个界面上的操作要同步给队员**。
所以「选了哪个副本 / 几星」**不能是界面自己的字段**，一律走 `DungeonSelection`：

| 现在（单人） | 以后（组队） |
|---|---|
| 状态在本地内存，`CanEdit` 恒 true | `Select` / `SetStar` 改成调 Reducer + 订阅队伍那张表，队员 `CanEdit` 返回 false |

界面只做三件事：读属性、听 `Changed`、点之前问一句 `CanEdit` ——
**所以接队伍那天界面代码一行都不用改**。别在界面里再存一份「当前几星」。

`DungeonSelection.Reset()` 要在**换角色 / 关界面**时调：星级的默认值和上限都跟着
角色的通关进度走，留着上一个角色的选择会显示成「他解到 5 星」。

##### 三处**代码补的**组件（美术搭的时候没有，别以为是配漏了）

| 补的 | 为什么 |
|---|---|
| `CloseButton` 上的 `Button` | 预制体里只有 `Image`（和 `PopMessageUI` 那两个页签同一个情况） |
| `DungeonSlot` 根节点上的 `Button` | 同上 —— 整块格子要可点 |
| 两个箭头**按位置认左右** | 见下 |

⚠️⚠️ **`DungeonSlot` 那两个箭头的名字和位置是反的**：
`LastArrowButton` 在 **x=+144（右边）**、`NextArrowButton` 在 **x=-129（左边）**。
所以代码**按 `anchoredPosition.x` 判断左右**再绑（左=减星、右=加星），不按名字 ——
按名字接一定接反，表现是「点加星变成减星」。这和创角界面那两个形态卡箭头是同一类坑（见坑 6）。

⚠️ 星星那一排是 6 个 `StarBackground`（灭底）各带一个子 `Star`（亮图），**同名**，
所以只能**按子节点顺序**认，不能按名字找。点亮/熄灭切的是 `Star` 的 `SetActive` ——
第 6 颗的亮图是特殊的 `common_icon_star_6_on`，所以只切显隐、**不换 sprite**。

⚠️ 预制体里 `UIMask` 的子节点顺序是 `Background`（标题 + 关闭按钮）→ `Contents`（格子），
也就是**格子渲染在标题栏之上**。现在位置不重叠所以没事，但格子铺满之后会挡住
右上角的关闭按钮。要调就在预制体里把 `Contents` 挪到 `Background` 前面。
（区域背景由代码保证永远是第 0 个，不受这个顺序影响。）

##### 本轮没做（副本本身）

点格子只会打一条日志 —— **副本一张玩法表都还没有**（关卡内容、战斗、结算、
体力消耗、掉落、需求等级）。服务端也不认识副本，没有「进副本」的 Reducer。
接战斗时把 `HandleSlotClicked` 那一处换成「调服务端进副本」即可，
选中的副本和星级都能从 `DungeonSelection` 拿到。**要动手前先问**副本的数据结构。

#### 聊天框（左下角，2026-08-26）

**显示两个频道混在一起的滚动日志，输入框只发附近消息。** 服务端契约见
[../ReDiv_Server/README.md](../ReDiv_Server/README.md) 的「聊天系统」。

世界消息在这里前面加 `[世界]` 前缀（那一行的格子只有「名字 + 正文」两段文字，
没有第三个地方放频道标记）。**不要把世界消息从这里藏起来** ——
「所有人在任何地方都能看到」是用户定的，藏了就不叫世界频道了。
按频道分开看是 `PopMessageUI` 的活（见下面「聊天弹窗」）。

```
UIMask/Message
├── Scroll View                 ScrollRect（只竖向）
│   └── Viewport/Content        VerticalLayoutGroup + ContentSizeFitter ← 消息格子摆这里
├── InputField (TMP)            单行（LineType = SingleLine），所以回车会触发 onSubmit
└── SendButton                  「发送」
```

一条消息一个 `MessageSlot` 预制体（`AssetKeys.MessageSlotPath`），
上面 `Name` + `Message` 两个 TMP，**定高一行、正文超出省略**。
所以正文有长度上限（`ChatValidation.MaxChars` = **20 个字**，按字符数不按显示宽度，
用户 2026-08-26 定的），否则长消息在这一行里会被直接截没。
输入框的 `characterLimit` 也在 `Init` 里设成同一个数 —— 超了根本打不进去，
比让人打完一大段再弹「太长了」友好。

⚠️ **长度规则客户端和服务端故意不一样**（不是漂移）：客户端 20 个字是**策划规则**，
服务端 `ChatRules.MaxDisplayWidth = 60`（中文 30 字）是**防滥用天花板**。
客户端严于服务端 ⇒ 正常客户端不会被服务端拒。清洗规则（折空白、挡不可见字符）
两边还是一字不差的镜像。

⚠️ **`MessageSlot.prefab` 上没有 `UIAutoBindGenerator`**，两个字段是在
`MessageSlot.Init()` 里用 `Get<T>("路径")` 手动取的 —— 那是既有做法（见第 5 节坑 4），
不是漏了配。

七条实现约束（改的时候别破坏）：

1. **输入框和按钮在 `Init` 里接一次，不在 `Open` 里。** Open 每次进城镇都会调，
   而 `onSubmit` 是直接 `AddListener` 的（`Bind` 内部会 RemoveAllListeners，
   所以按钮无所谓）—— 重复挂会让一次回车发出去好几条。
2. **回车用 `onSubmit` 不用 `onEndEdit`。** 后者失焦也会触发，点一下别处就把消息发出去了。
3. **不做本地乐观显示。** 成功后服务端会把消息推回来（**自己发的那条也在里面**），
   本地先塞一条会重复；被拒了还得再抠出来。
4. **等回应期间就把输入框清空**，别等结果 —— 玩家看到字还在会以为没发出去而重复点。
   发失败时再把原文填回去，这样重试不用重打。
5. **复用已有格子，只补 / 收差额**，不是每次全拆重建：一条新消息就重新实例化 50 个
   预制体会明显卡顿，而且正在滚动的话会被打断。
6. **只有玩家本来就贴着底看，才自动滚到底。** 他滚上去翻历史时，新消息不该把他拽回去。
   判断「贴着底」要**先处理「内容还没长到超过视口」这种情况** —— 那时
   `verticalNormalizedPosition` 的值没意义（ScrollRect 会夹住它），拿它判断会得出
   「玩家在翻历史」的错误结论，于是头几条消息就不自动滚了。
7. **滚到底之前必须 `LayoutRebuilder.ForceRebuildLayoutImmediate(content)`。**
   VerticalLayoutGroup + ContentSizeFitter 算高度排在下一次布局阶段，不重算的话
   这里用的是**上一帧**的高度 ⇒ 刚加进来那条还没算进去 ⇒ 停在倒数第二条上。

排序按 `(SentAt, MessageId)` 升序，最新的在最后。⚠️ **不能只按 MessageId 排** ——
官方规则说自增 id 不保证连续也不保证单调，MessageId 只当平局裁判（同一事务插多条时
时间戳完全相同）。**和服务端裁剪用的是同一把尺子**，两边口径不一致会出现
「客户端认为还该显示的那条被服务端当成最旧删了」。

`ChatManager` 的订阅**跟着当前城镇走**（附近消息的可见域就是城镇 id，而订阅 SQL 是
静态字符串 ⇒ 换城镇必须重订），挂在 `TownManager.LocationChanged` 上，
和 TownManager 自己那段「同城镇玩家」订阅同一个模式（**先订新的再退旧的**）。
世界频道那句 `WHERE town_id = 0` **和附近放在同一段订阅里**：换城镇时它会跟着白重订一次，
换来的是「只有一处管订阅生命周期」。
⚠️ 因此 `RefreshFromCache` **必须按当前域过滤**，不能把 `Iter()` 全收下 ——
换城镇那一小段时间里两个城镇的消息会同时在缓存里。
⚠️ `IsVisible` 里那个 `subscribedTownId != 0` 的前提**不能省**：离开城镇后 townId 归 0，
「附近」那个条件会退化成 `row.TownId == 0`，正好和世界域撞上 ⇒ 明明退订了还显示着世界消息。

⚠️ **`ChatManager.Ready` 会反复触发**（每换一个城镇一次），不是只在开局响一下。

右下角的 `openMessageUIButton`（「世界聊天」）→ 打开 `PopMessageUI`，见下面「聊天弹窗」。

⚠️ 原来 AutoBind 里有个 `modeButton`（`UIMask/Message/ModeButton`，界面上的「附近」），
**用户 2026-08-26 把那个节点删了**，悬空的登记项（`Target: {fileID: 0}`）和生成的字段
都已经清掉。别再找它。

#### 说话气泡（角色头上那个，2026-08-26）

有人说话就在他头上冒一个气泡，5 秒后自己收掉。**两个频道都会冒** ——
但只有说话的人正好在你这个城镇时才看得到；他在别的城镇发世界消息时
`FindTownCharacter` 找不到人，就只进聊天框不冒泡（**这不是错误，别加报错**）。

节点在 `TownCharacterController` 预制体上（见本节「城镇角色：两层结构」）：

```
TownCharacterController
└── MessageFarme  [SpriteRenderer，drawMode = Sliced]   ← 气泡框，默认隐藏
    └── Message   [TextMeshPro]                        ← 正文
```

**自己和别人走同一条路**：都是等服务端把消息推回来（`ChatManager.MessageArrived`）
才显示。所以气泡里一定是**服务端真的收下了**的那句话 —— 被冷却挡掉、被判超长的
不会冒泡，而且自己看到的时序和别人看到的一致。**不要**改成本地乐观显示。

##### ⚠️ 判断「这条是不是刚发生的」只能看**是不是订阅回填**

进城镇时那几十条历史也是走 `OnInsert` 下来的，不挡住会满屏气泡。但**挡的方式很容易写错**。
2026-08-27 实测过 `ctx.Event` 的取值：

| 这一行怎么来的 | `ctx.Event` |
|---|---|
| **自己**发的 | `Reducer` |
| **别人**发的 | `Transaction` |
| 进城镇拉历史 | `SubscribeApplied` |

别人发的拿不到 `Reducer` 变体，因为 **2.x 起没有全局 Reducer 回调** ——
别的客户端调 Reducer 你收不到参数，只知道「有个事务改了你订阅的行」
（见 [../ReDiv_Server/README.md](../ReDiv_Server/README.md)「API 约定」）。

⚠️ 一开始写成了「只放行 `Event.Reducer`」，症状是**联机下只有自己头上冒泡、
别人的永远不冒，而聊天记录一切正常**（记录是在这道门之前刷的）。
**单客户端根本测不出来** —— 自己发自己看，永远是 `Reducer`。
所以判据写成 `IsSubscriptionBackfill(...)`（排除订阅生命周期事件），
不是枚举「什么算实时」。

尺寸是**算出来的**：`GetPreferredValues(文本, 最大宽度, 0)` 问 TMP「这段话要多大」，
超过最大宽度就在那个宽度上换行、改往高长，然后把九宫格框设成「文字 + 内边距」。
实测数值（供调参参考）：一个汉字宽约 **0.50**、行高约 **0.71**（都是 MessageFarme
的局部单位），最大宽度 6.57 一行放得下约 13 个汉字，所以 20 字上限最多两行。

Inspector 上四个可调项（都在 `TownCharacterController` 的「对话气泡」组里）：
`显示秒数`（5）、`文本最大宽度`（6.57）、`内边距`（**x=1.0** 左右各空一个字，y=0.08）。

五条实现约束（改的时候别破坏）：

1. **`drawMode` 必须是 `Sliced`（九宫格）。** 靠缩放 transform 拉大气泡会把圆角和
   尾巴一起拉变形。而且框不能小于「四条边框加起来」（从 sprite 的 `border` 现算，
   不写死），小于它 Unity 会把边框自己压扁。
2. **钉住左边缘往右长，不是以中心为基准两边一起长。** 尾巴在气泡左侧指着角色 ——
   两边一起长的话尾巴会随着话变长越跑越远，最后戳到角色身上。左边缘在第一次
   `ShowMessage` 时从预制体量一次并**缓存**（量完之后 `localPosition` 就被代码改过了，
   再量就是错的）。
3. **`Message` 的 TMP 必须关掉 `enableAutoSizing`。** 那是「字缩进框里」，
   和这里要的「框跟着字长」正好互相打；两个一起开会算不出稳定尺寸。
4. **框的 Sorting Layer 要在 Spine 之上、气泡文字之下** —— Spine 是 `Character:0`、
   文字是 `Character:100`，所以框是 `Character:99`。⚠️ 它原来在 `Default:0`（默认值），
   那样会被背景和角色压住，游戏里只看得到一句飘在空中的字。
5. **`Bind()` 和 `Unbind()` 都要 `HideMessage()`。** 对象池 Despawn 会
   `SetActive(false)`，那会直接杀掉隐藏用的协程 —— 不收的话下次从池里取出来
   还挂着上一个人的话。

⚠️ **已知不足：气泡只会往右长，所以贴着屏幕右边的角色说长话会出画。**
要修就得在靠右时翻到左侧显示（尾巴也要镜像），这次没做。

⚠️ **气泡竖直方向比美术原来摆的紧**：预制体里框高 1.15、而一行文字实测只有 0.71，
所以按「文字 + 内边距 y=0.08」算出来是 0.79。想回到美术原来的比例把内边距 y 调到
约 0.44 就行（一个 Inspector 数值，没改是因为用户只说了左右）。

#### 聊天弹窗（`PopMessageUI`，2026-08-26）

城镇主界面右下角「世界聊天」按钮打开。两个页签（**附近** / **世界**）共用一个列表 +
一个输入框 + 一个发送按钮；页签只决定两件事：**列表显示哪个频道**、**发送走哪个频道**。

```
PopMessageUI
└── UIMask
    ├── CloseButton                      ← 铺满整屏的透明按钮，压在 Background **底下**
    └── Background
        ├── Left/MemuButtons
        │   ├── WordChatButton  ← 文字是「附近」（⚠️ 名字和文字是反的）
        │   │   ├── Selected               选中高亮图
        │   │   └── Text                   选中时染 SelectedColor，否则 NormalColor
        │   └── BearbyButton   ← 文字是「世界」（⚠️ 同上）
        └── Panel
            ├── Scroll View/Viewport/Content   ← MessageUI 行摆这里
            ├── InputField (TMP)
            └── SendButton
```

⚠️⚠️ **两个页签节点的名字和它们的文字是反的**：`WordChatButton`（听起来是"世界"）
上面写的是「附近」，`BearbyButton`（听起来是"附近"）上面写的是「世界」。
所以频道是**按按钮上的文字判定的，不是按节点名** —— 按节点名接一定接反，
而且表现是「两个页签点起来行为对调」，很难往回追。
`BuildTabs()` 里还有个自检：两个页签没覆盖到两个频道就报错（美术改了文字就会触发）。
这和形态卡那两个箭头是同一类坑（见本节坑 6）。

两处**代码补上去**的组件（美术搭的时候没有，别以为是配漏了）：

| 补的 | 为什么 |
|---|---|
| 两个页签节点上的 `Button`（`transition = None`） | 预制体里只有 `Image`。transition 设 None 是因为选中态已经由 `Selected` 那张图表达，再来一层 ColorTint 会和它打架 |
| `Content` 上的 `ContentSizeFitter`（Unconstrained / PreferredSize） | 只有 VerticalLayoutGroup 的话 content 不会随内容长高 ⇒ **ScrollRect 根本滚不动** |

行预制体是 `MessageUI.prefab`（`AssetKeys.MessageUIPath`），头像 + 名字 + 气泡框正文。
和城镇主界面底部那个 `MessageSlot`（定高一行的滚动日志）**是两套，不要互相替代**。
⚠️ `MessageUI` 的 AutoBind 里有个字段叫 `name`，**遮住了 `Component.name`** ——
在那个类里写 `name` 拿到的是 TMP，要物体名字得写 `gameObject.name`。

**头像**按 `(SenderJobId, SenderFormId)` 查配置表 `CharacterForm` 的 `IconKey`。
这两个字段是服务端在发送那一刻快照进消息行的 —— **不能去 join 在线玩家**：
世界频道里说话的人可能在另一个城镇，本地根本没订阅到他。取不到就把头像**隐藏**
（留一张空 Image 会显示成白方块，比没有更难看）。头像有 `Dictionary` 缓存，
一个 key 只 `LoadAsset` 一次（`AssetReleaser.Track` 不去重）。

**实现约束和上面「聊天框」那七条完全一样**（`Init` 里接一次输入框、不做本地乐观显示、
等回应期间清空输入框、复用行只补收差额、只在贴底时自动滚、滚之前强制重算布局），
**别在这里再写一遍** —— 两处漂开就是灾难。改一处记得看另一处。

**默认停在「世界」页** —— 入口按钮上写的就是「世界聊天」，打开却停在附近页很别扭；
附近消息在城镇主界面底部本来就一直看得到。要改就动 `currentChannel` 的初值。

左下角那个 `SettingsButton` **还没接**（用户没提要求）。

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
├── NameAnchor  [BoneFollower]   ← 跟随头部骨骼
│   └── Name    [TextMeshPro]    ← localPosition.y 就是头顶偏移
└── MessageFarme [SpriteRenderer 九宫格]  ← 说话气泡，尺寸跟着文字变，默认隐藏
    └── Message  [TextMeshPro]            ← 见本节「说话气泡」
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

#### 角色互相遮挡按 Y 排（2026-08-27）

站得靠下的角色要盖住站得靠上的。所有城镇角色的 Spine 都在 **`Character:0`**（同层同 order），
所以它们之间的先后完全靠**透明排序**决定 —— 默认是按 Z 距离排，而大家 z 都是 0，
于是顺序基本随机。

**设置在这里：`Edit > Project Settings > Graphics`（全局）**

| 项 | 值 |
|---|---|
| Transparency Sort Mode | `Custom Axis` |
| Transparency Sort Axis | `(0, 1, 0)` |

⚠️ **别去 Renderer 资产里找这个字段。** 它在 `Renderer2D`（2D Renderer Data）的面板上有，
但本工程 URP 的默认 renderer 是 **`CubismURPRenderer`**（Live2D 那个 UniversalRendererData，
带 `CubismRenderPassFeature`，Cubism 遮罩靠它），而 **UniversalRendererData 没有这个字段** ——
`Renderer2D.asset` 里其实早就填了 `CustomAxis (0,1,0)`，只是那个 renderer 不是默认的，压根没生效。

这个字段本来就不是 renderer 的属性，是**相机 / 全局图形设置**的属性，2D Renderer 只是
顺手在自己面板上代管了一份。2026-08-27 实测确认：**URP 的 Universal Renderer 照样认它**
（把轴翻成 `(0,-1,0)`，前后关系整个反过来 —— 用真实 Spine 角色验的）。

**为什么用全局而不是逐相机**：`Camera.transparencySortMode` **不是序列化字段**
（运行时属性，默认继承全局），逐相机就必须写运行时代码、而且以后新加相机容易漏。
全局那份存在 `ProjectSettings/GraphicsSettings.asset`，一次设好，一行代码都不用。

不影响现有的层级关系：排序永远是 **Sorting Layer → Order in Layer → 透明排序**，
所以背景（`Ground:-1`）、气泡框（`Character:99`）、名字（`Character:100`）都还在原位，
Y 排序只在**同层同 order** 的那一堆角色之间打破平手。

⚠️ **排序键是渲染器的包围盒中心，不是脚下坐标**（2026-08-27 实测：把一个角色放大 2.5 倍、
脚下明显更低，它照样被排到后面去了，因为包围盒中心更高）。
现在城镇角色都是同一套 Q 版比例，中心高度差不多，所以按中心排和按脚下排结果一致。
**以后要是加了体型差很多的角色 / Boss NPC，这里会排错** —— 那时候的解法是不靠透明排序，
改成按 Y 现算每个角色的 `sortingOrder`（`Renderer.sortingOrder = -(int)(y * 100)` 那一类）。

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

- **算点击坐标必须传 canvas 的相机**：`RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, ...)`。
  传 `null`（Overlay 的写法）拿到的是世界坐标，`RaycastAll` **一个都打不中**，
  表现成「点击派发了但界面毫无反应」，很容易误判成事件被谁吃了。
  ⚠️ **而且第二个参数别用 `rt.position`** —— 那是 **pivot** 的世界坐标，pivot 不一定在中心
  （`PopDungeonUI` 的 `CloseButton` pivot 就在右上角，算出来的点正好压在矩形边界上，
  `RaycastAll` 命中的是它**父节点**那张铺满屏的透明图，看着就像「关闭按钮被谁挡住了」）。
  用四角平均值当中心：`GetWorldCorners(c); center = (c[0] + c[2]) * 0.5f;`
  —— 2026-08-27 验副本界面时踩的，排查方向差点跑偏。
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
Febucci Text Animator、DamageNumbersPro、PathologicalGames（对象池）、IngameDebugConsole

**`Assets/` 下另外几个第三方目录**（不在 Plugins 里，容易漏看）：

| 目录 | 是什么 | 用在哪 |
|---|---|---|
| `Live2D/Cubism/` | Live2D Cubism SDK | **模型一个都还没用上**，但它的 `CubismURPRenderer` 是 URP 的**默认 renderer**，见下 |
| `AVProVideo/` | 视频播放 | `CommonUI`（标题界面那段视频）、加载界面的 `LoadIngVideo.mkv` |
| `TLNEXUS/Editor/TraceWeave/` | 一个编辑器 dll | 只有 dll，没有源码 |
| `Shader/ReDiv/` | **自己写的** VariantCard shader 与还原工具 | 见第 10 节 |

⚠️ **URP 的默认 renderer 是 Live2D 的那个**（`UniversalRP.asset` 的
`m_DefaultRendererIndex: 0` → `CubismURPRenderer`，一个带 `CubismRenderPassFeature`
的 UniversalRendererData）；列表里第 1 个才是 `Renderer2D`。
明明现在一个 Cubism 模型都没用上却让它当默认，是因为 Cubism 的遮罩要靠那个 renderer feature，
换掉的话以后接 Live2D 会挂。**这件事有个直接后果** —— Y 轴遮挡排序不能在 renderer 资产里设，
见第 5 节「角色互相遮挡按 Y 排」。

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
2. `Assets/Settings/Build Profiles/PC.asset` —— **只有这一个** profile 自带一份
   PlayerSettings 覆盖（YAML 文本快照，900 多行）。同级还有 `Windows64.asset` 和
   `IQOO.asset`（Android），那两个的 `m_PlayerSettingsYaml` 是空壳、没有覆盖
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
  `Version`**（拿全局 `bundleVersion`），只想改名字时别按它 —— 版本号的方向现在是
  **配置 → ProjectSettings**（见第 9 节），按这个按钮等于把方向倒过来。

---

## 9. 版本号（客户端只改一处 + 服务端一处）

客户端连上服务器后会立刻调 `CheckVersion(Application.version)` 对一次版本号，
不一致就弹窗提示并禁止登录（详见 [../ReDiv_Server/README.md](../ReDiv_Server/README.md) 的「版本号」一节）。

### 客户端只在一个地方改（2026-08-26 起）

**唯一真相源是 `Assets/Editor/BuildTools/PlayerBuildConfig.asset` 的 `Version`**，
也就是一键出包窗口「基础信息」页上那个「版本号」输入框。
它由 `PlayerBuildVersionSync` 往下写，客户端其余几处都是它的投影：

| 位置 | 谁写的 |
|---|---|
| `ProjectSettings/ProjectSettings.asset` 的 `bundleVersion` | 出包时自动同步。`Application.version` 读的就是它，**校验用的是这个值** |
| `ProjectSettings` 的 `AndroidBundleVersionCode` | 出包时自动同步成配置里的**内部版本号**（Android 上架要求它逐次变大） |
| `Assets/Settings/Build Profiles/*.asset` 里那份 PlayerSettings 快照 | 出包时自动同步 |

同步时机有两个：**每次出包**（`PlayerBuilder.ApplyPlayerSettings` 里），
以及窗口上那个「把版本号写回 ProjectSettings 与 Build Profiles」按钮（想立刻生效时手动按）。
窗口「版本号一致性」那一行是只读检查，不一致会把差在哪列出来。

⚠️ **别再手改 ProjectSettings 或 profile 快照** —— 下次出包会被配置盖回去，
中间那段时间两边不一致，很容易误判成「改了没生效」。

> 为什么要管 Build Profile 快照：`Assets/Settings/Build Profiles/PC.asset` 里存着一份
> **完整的 PlayerSettings YAML 快照**，profile 被激活时它会盖掉全局值。
> 只改 ProjectSettings 的话，哪天有人切到 profile 出包就会带着一个旧版本号发出去 ——
> 而版本校验是**字符串全等**，表现成「新包连不上服务器」。
> （本工程现在没有激活任何 build profile，走的是经典平台设置，但快照还在，所以照样同步。）
>
> 同步是**扫整个 `Build Profiles/` 目录**的，不是写死某几个名字 ——
> 所以现在这三个（`PC` / `Windows64` / `IQOO`）和以后新加的都自动覆盖到，不用改代码。

实现上那三个成员（`BuildProfile.playerSettings` / `SerializePlayerSettings` /
`HasSerializedPlayerSettings`）都是 **internal，只能反射**（`BuildProfile` 类型本身是 public）。
反射拿不到时只警告、不让出包失败。

### 服务端那一处要手改

`ReDiv_Server/spacetimedb/Version.cs` 的 `Module.ServerVersion`，**改完要 `spacetime publish`**。
工具管不到服务端，所以升版本号的动作是：窗口里改 → 改 `Version.cs` → publish。
2026-08-22 之前四处互不相同（`1.0` / `0.1` / `0.1` / 无，界面上还写死显示 `0.0.1`），
现在统一成 `0.0.1`。

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
