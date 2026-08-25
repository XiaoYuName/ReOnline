# ReDiv_Server

ReDiv 服务端。C# 编写的 **SpacetimeDB 模块**，编译成 WebAssembly 跑在数据库进程内 ——
**没有独立的游戏服务器进程**。

**当前状态：账号系统 + 角色系统 + 城镇（含移动同步、玩家状态）与世界时间。** 注册 / 登录 / 会话，
多角色的创建 / 删除 / 选择，形态与觉醒（基础 → 一觉 → 二觉，按星级现算），
以及「角色在哪个城镇」和「现在是早/中/晚哪个时段」都能用了。
战斗、背包这些玩法表还没有 —— 定型后再往里加。

> ⚠️ 2026-08-24 改过一次形态设定：**「专职」那一层已经废弃**，
> 现在是「角色 → 形态（基础线 + 爆发线）」两层，见「角色系统」一节。

> ⚠️ 玩法是**自研**的。不要从「像某款已知游戏」去推导表结构和系统设计 —— 需要业务结构时
> 主动问。详见 [../CLAUDE.md](../CLAUDE.md) 第 0 节。

## 相关文档

- [../README.md](../README.md) —— 客户端/服务端总览与本机环境
- [../CLAUDE.md](../CLAUDE.md) —— AI 协作总纲、工具链规则（新开对话先读）
- [../ReDiv_Online/CLAUDE.md](../ReDiv_Online/CLAUDE.md) —— 客户端技术文档
- [CLAUDE.md](CLAUDE.md) / [AGENTS.md](AGENTS.md) —— SpacetimeDB 2.8 官方 AI 规则
  （`spacetime init` 生成，勿手改）

**本文件怎么读**：改业务代码看「账号系统」/「角色系统」；改配置表看「配置表」；
**动手之前先扫一眼「写代码前必须知道的」** —— 那节是 2.8 的 API 约定和这个
wasi-wasm 环境的地雷，两者都能让「编译通过的代码」在运行时炸。

---

## 环境实况

| 项目 | 值 |
|---|---|
| SpacetimeDB 服务端 | 2.8.2，Docker 容器 `spacetimedb`（`clockworklabs/spacetime:latest`） |
| CLI | 2.8.2，`%LOCALAPPDATA%\SpacetimeDB\spacetime.exe` |
| 访问地址 | `http://127.0.0.1:2383`（已绑 `0.0.0.0`，局域网走 `http://192.168.10.226:2383`） |
| 数据库名 | `rediv` |
| 数据库 identity | `c20016162580a8a82675c8d5434f5a248cfaa144c6522d1a6faf7325aa9e34c7` |
| 宿主机 .NET | 10.0.400 |
| 客户端 | `../ReDiv_Online`（Unity 6000.4.8f1，SDK 2.8.2 内嵌在 `Packages/` 下） |

CLI、服务端、Unity SDK 三者版本严格对齐在 2.8.2 —— 协议是 v2，跨版本容易出问题，
升级时三个一起升。

---

## 目录结构

```
ReDiv_Server/
├── spacetime.json          CLI 项目配置：数据库名、服务器、绑定生成目标
├── spacetime.local.json    本地覆盖（已 gitignore），各人可指向自己的库
├── CLAUDE.md / AGENTS.md   SpacetimeDB 2.8 官方 AI 规则（生成物，勿手改）
├── .cursor/rules/          同上，Cursor 用
└── spacetimedb/            模块源码（spacetime.json 的 module-path 指向这里）
    ├── StdbModule.csproj
    ├── global.json         钉 .NET 10 SDK
    ├── NuGet.Config        dotnet-experimental 源，NativeAOT-LLVM 用
    ├── Module.cs           生命周期钩子（Init / ClientConnected / ClientDisconnected）+ Ping
    ├── Version.cs          版本号常量 + CheckVersion
    ├── Auth/               账号系统
    │   ├── AuthTables.cs   Account / IdentityBinding / Session / SessionClosed
    │   ├── AuthReducers.cs Register / Login / Logout + 会话管理
    │   ├── AuthRules.cs    用户名与口令的格式规则、归一化
    │   └── AuthSelfTest.cs 手写密码学实现的测试向量自检
    ├── Character/          角色系统
    │   ├── CharacterTables.cs    Character（私有）/ CharacterSelection（公开）
    │   ├── CharacterReducers.cs  CreateCharacter / DeleteCharacter / SelectCharacter / LeaveCharacter
    │   ├── CharacterRules.cs     角色名规则（允许中文，按显示宽度限长）
    │   ├── CharacterForms.cs     形态计算（按星级现算）+ AwakenCharacter 觉醒
    │   ├── CharacterConfigSelfTest.cs  配置表自检（改完 Excel 跑一次）
    │   └── CharacterViews.cs     MyCharacter / MyAccountProfile（per-subscriber View）
    ├── Town/               城镇与世界时间
    │   ├── TownTables.cs         CharacterLocation（私有）/ WorldTime（公开）/ WorldTimeTimer（定时）
    │   ├── TownPlayerTables.cs   CharacterTransform（公开，坐标）/ AccountWallet（私有，金币钻石）
    │   ├── TownPlayerReducers.cs UpdateTransform（坐标上报）+ 体力每日重置 + 钱包
    │   ├── TownRules.cs          时段计算 + 初始城镇（纯函数，只读配置）
    │   ├── TownReducers.cs       PlaceCharacter（进城镇）+ 配置自检
    │   └── WorldTimeReducers.cs  定时重算时段 + 自检
    ├── Security/           口令哈希（自己写的，原因见「写代码前必须知道的」）
    │   ├── Sha256.cs
    │   ├── Pbkdf2Sha256.cs
    │   └── PasswordHasher.cs
    ├── Luban/              配置表（见「配置表」一节）
    │   ├── ServerConfig.cs 配置入口，从嵌入资源加载
    │   ├── Generated/      Luban 生成的 C#（生成物，勿手改）
    │   └── Runtime/        vendored 的 Luban ByteBuf 运行时（勿手改）
    └── Configs/            Luban 导出的 bin 数据，以嵌入资源编进 wasm（生成物）
```

---

## 日常循环

### 推荐：在 Unity 里用编辑器窗口

客户端有个一站式控制台：**`Tools > XFramework > 服务端 > SpacetimeDB 控制台`**

发布、生成绑定、清库重发、看日志（含实时）、跑 SQL、调 Reducer、启停 Docker 容器
都在里面，发布完会自动刷新资源库并请求重编译。实现在
`../ReDiv_Online/Assets/Editor/ServerTools/`。

### 命令行

| 干什么 | 命令 |
|---|---|
| 发布模块 | `spacetime publish` |
| 生成客户端绑定 | `spacetime generate` |
| **清库重发**（改了表结构没法自动迁移时） | `spacetime publish --delete-data=always --yes` |
| 看日志 | `spacetime logs rediv --follow` |
| 查数据 | `spacetime sql rediv "SELECT * FROM st_table"` |
| 调 Reducer（**snake_case**） | `spacetime call rediv ping` |
| 看已发布的 schema | `spacetime describe rediv --json` |
| 列出本机数据库 | `spacetime list -s rediv-local`（不带 `-s` 会去查 maincloud） |

`publish` / `generate` 都自动读根目录的 `spacetime.json`，不用传 `--server` / `--module-path`。
绑定落在 `../ReDiv_Online/Assets/Scripts/Net/ModuleBindings`（命名空间 `ReDiv.Net.Bindings`），
**那个目录是生成物，不要手改**。

⚠️ **`spacetime generate` 之后必须回客户端跑一次编译验证** —— schema 变了可能让现有客户端
代码编不过：

```bash
cd ../ReDiv_Online && unity command recompile && unity command recompile_status
```

`recompile` 返回 `up_to_date` **不代表没错误**，还要单独查控制台。完整规则见
[../CLAUDE.md](../CLAUDE.md) 第 2 节。

---

## 账号系统

用户名 + 口令注册、登录。源码在 `spacetimedb/Auth/` 和 `spacetimedb/Security/`。

### 表

| 表 | 公开性 | 说明 |
|---|---|---|
| `Account` | **私有** | 凭据：`UsernameKey`（归一化小写，唯一）/ `Username`（展示原样）/ `PasswordHash` / `PasswordSalt` / `HashIterations` |
| `IdentityBinding` | **私有** | `Identity` → `AccountId`，免密重连用。一个账号同时只保留一条 |
| `Session` | 公开 | 当前在线会话，主键是 **ConnectionId**。客户端订阅它判断登录状态 |
| `SessionClosed` | 公开**事件表** | 会话被服务端主动关闭时推一条，带原因枚举 |

私有表客户端订阅不到，`spacetime generate` 也不给它生成表句柄（只生成行类型），
所以口令哈希不可能因为写错一句订阅 SQL 漏出去。

`Session` 是公开表 ⇒ 所有客户端都能看到在线用户名列表（有意为之，在线列表本身有用）。
**别往这张表加隐私字段**，要收紧得靠 View 或 `ClientVisibilityFilter`。

### Reducer

| 客户端绑定 | CLI（snake_case） | 行为 |
|---|---|---|
| `Register(username, password)` | `register` | 注册，成功后**直接建会话**（不用再调 Login） |
| `Login(username, password)` | `login` | 登录 |
| `Logout()` | `logout` | 关掉本设备所有会话，并**解除免密绑定** |
| `AuthSelfTest()` | `auth_self_test` | 密码学自检，见下。**上线前删掉或加 owner 鉴权** |

### 结果怎么回到客户端

- **失败** → Reducer 抛异常 ⇒ 事务回滚 + 调用方在 `Conn.Reducers.OnLogin` 的
  `ctx.Event.Status` 里拿到 `Status.Failed(reason)`。`reason` 就是 `AuthRules` 里那些
  中文文案，可以直接显示给玩家。**只有调用方收到**，别的客户端收不到。
- **成功** → `Session` 表里出现自己那一行。客户端订阅
  `SELECT * FROM session WHERE identity = 0x<自己的 identity>`。
- **被动下线** → `SessionClosed` 事件表推一条，`Reason` 区分
  `KickedByNewLogin`（顶号）/ `LoggedOut` / `SwitchedAccount`。
  光看 `Session` 的 `OnDelete` 分不清是被踢还是断线，所以要这张事件表。

### 两条策略（改之前先读这段）

**顶号，粒度是 Identity（设备）而不是连接。** 新登录会关掉该账号在**其它 Identity** 上的
会话、并删掉那些 Identity 的绑定 —— 绑定留着的话，被顶的机器一重连就会免密登录回来，
把新登录顶下去，两边来回打。同一 Identity 的多条连接**允许共存**：一台机器上跑两份
同一个客户端包就是同一个 Identity（AuthToken 存在同一个位置），按连接顶号它们会互相踢。
（Unity 编辑器和打包出来的客户端**不是**同一个 Identity —— SDK 的 AuthToken 在编辑器下
会把 Application.dataPath 拼进 PlayerPrefs 的键，两边各存一份，所以拿编辑器和真机
互相测顶号是可行的。）

**免密重连绑 Identity。** 登录成功时记下当前 Identity；`ClientConnected` 钩子发现绑定
就直接建会话。客户端的 AuthToken 存在 PlayerPrefs 里（`SpacetimeConnection.HandleConnect`），
所以重启游戏还是同一个 Identity，不用再输口令。换设备或清了 PlayerPrefs 就要重新登录。
显式 `Logout` 会解绑（「别再记住我」），断线不会。

⚠️ `ClientConnected` 里抛异常会**拒绝连接**，所以免密恢复路径上一个异常都不能漏出去 ——
一条悬空绑定不该让玩家连不进来。改 `TryRestoreSession` 时守住这条。

### 口令哈希

PBKDF2-HMAC-SHA256，10000 次迭代，每账号 16 字节随机盐（`ctx.Rng`），Base64 存三列。
迭代次数存在行里，将来提高参数后，旧账号在下次登录成功时会自动用新参数重算
（`PasswordHasher.NeedsRehash`）。

**SHA-256 / HMAC / PBKDF2 都是自己写的**（`Security/`），不是调 BCL —— 原因见「写代码前必须知道的 → 环境地雷」。
自己写的哈希算错了症状很隐蔽（「密码永远验不过」，或者更糟「所有密码都验得过」），
所以拿测试向量钉住。**改过 `Security/` 下任何文件就跑一次**：

```bash
spacetime call rediv auth_self_test
```

全过会在日志里打一行 `[AuthSelfTest] PASS，17 项全部通过`，任何一条不过直接抛异常。
向量是在宿主机上用 .NET 的 `System.Security.Cryptography` 现算的，和 RFC 公开值一致。

### CLI 测试速查

```bash
spacetime call rediv register '"Alice"' '"secret123"'
```

```bash
spacetime call rediv login '"alice"' '"secret123"'
```

```bash
spacetime sql rediv "SELECT account_id, username_key, username FROM account"
```

两个坑：

- **`spacetime call` / `spacetime sql` 每次都会新建一条连接**，因此也会触发
  `ClientConnected` ⇒ 免密恢复。所以查 `session` 表时总会看到查询自己那条连接建的行，
  那不是脏数据。调用结束连接断开，行会被清掉。
- 想测顶号要有一条活着的连接：用 `spacetime subscribe rediv "SELECT * FROM session_closed"`
  挂住，再用 `spacetime call ... --anonymous`（另一个 Identity）登录同一账号。

### 有意没做的事

- **没有失败次数锁定 / 限流。** 原因是硬的：Reducer 抛异常 ⇒ 整个事务回滚，
  写在同一事务里的失败计数会**一起被吃掉**，所以「连续错 5 次锁 5 分钟」在当前
  「抛异常回报错误」的模型下根本存不下来。真要做就得改成：失败时**不抛异常**，
  把结果（成功/失败/锁定中）写进一张事件表回报。所有可预期失败都从
  `AuthRules.Reject` 抛，改的时候只用动那一处 + 各调用点。
- **口令是明文过线的**（服务端算哈希）。本机是 `http://` ⇒ ws 明文，局域网同理。
  正式环境必须上 TLS；只在客户端预哈希是**假安全**（预哈希值本身就成了口令）。
- 没有改密码 / 找回密码 / 删号，也没有昵称。昵称建议以后单开 Profile 表挂 `AccountId`，
  别动 `Account`（它每次登录只读一次，跟高频数据放一起会白白放大同步量）。

---

## 角色系统

一个账号多个角色（DNF 那种），登录后进选人界面，选完角色才进城镇。
源码在 `spacetimedb/Character/`。

### 两层结构（角色 → 形态）

```
CharacterJob      角色 / 职业（凯露）—— 建角色时选的就是这一层，之后不变
  └ CharacterForm   形态 —— 美术资源都挂在这一层，分两条线
      ├ 基础线 FormType=1  按**星级**现算当前形态，靠觉醒推进，**永久不可逆**
      │    1~2 星  基础形态（魔法士）    UnlockStar=1，建完角色就在这
      │    3~5 星  一觉形态（魔导士）    UnlockStar=3，UnlockLevel=30
      │    6   星  二觉形态（黑魔法师）  UnlockStar=6，UnlockLevel=60
      │                                  ⚠️ **部分角色没有二觉** —— 不配这行就行，
      │                                     那样的角色靠养成最高到 5 星
      └ 爆发线 FormType=2  一个角色**可以有多个**，**不分阶段**，按 SortOrder 排
           公主 / 暗黑圣灵 …  战斗中装备**爆发宝石**才切，和星级 / 等级无关
```

> ⚠️ **2026-08-24 改过设定。** 原来中间还有一层「专职」（`JobSpecialization` /
> `Character.SpecId` / `SwitchSpecialization` / `FormStage`），觉醒是挂在专职下面的
> `Stage`。**现在没有专职了**：形态直接挂角色，靠星级推进，另外多出一条爆发线。
> 那套表、字段和 Reducer 都已删干净 —— 别照着旧提交或旧文档写。

**职业名和角色名是两回事**：职业名（凯露）来自配置、所有玩家一样；
角色名是玩家自己输入的、全服唯一。别把两者混起来。

### 星级：存的是它，形态是算出来的

| 存的 | 算的 |
|---|---|
| `Character.JobId`（建角色时定，不变） | 当前形态（基础线里 UnlockStar ≤ 星级的行中，UnlockStar 最高的那个）|
| `Character.Star`（星级 1~6，靠觉醒推进） | 星级上限（没配 6 星形态的角色就到不了 6 星）|

为什么星级**必须存库**，不像旧的专职形态那样纯靠等级现算：
觉醒的条件是「等级到 + **完成觉醒任务**」，任务完成与否从等级推不出来；
而且觉醒是**永久的、回不去**，所以进度只能落在角色行上。

4 / 5 星在配置里**没有单独的行** —— 形象跟着 3 星那行走（`CurrentBaseFormId` 取的是
「不超过当前星级的最高那档」）。所以升星养成加星级不用改形态表。

形态由**服务端**算并通过 `MyCharacter` View 的 `FormId` 下发，
**客户端别自己再算一遍** —— 两份实现迟早对不上。客户端拿 `(JobId, FormId)`
去 `TbCharacterForm.Get()` 取名字 / 立绘 / 头像 / Spine / 视频。

### 表

| 表 | 公开性 | 说明 |
|---|---|---|
| `Character` | **私有** | 角色档案。`AccountId`（索引）/ `NameKey`（唯一）/ `Name` / `JobId` / `Level` / `Exp` / `DeletedAt` / `Star` |
| `CharacterSelection` | 公开 | 这条连接当前选了哪个角色，主键 `ConnectionId`。带 `FormId`，天然就是在线角色列表 |

⚠️ 加字段 / 删列 / `[Default]` 的规矩见「写代码前必须知道的 → 环境地雷」，
字段顺序不是随便排的。

`Account` 上有 `CharacterSlots`（默认 4，上限常量 8），栏位可扩展所以存在账号上，
由 `Register` 插入时显式赋值。

玩法态（地图 / 坐标 / HP / 体力）**故意还没建表**。定型后以 `CharacterId` 为主键单开表，
别往 `Character` 上堆 —— 那张表只在选人界面读一次。

### Reducer

| 客户端绑定 | CLI | 行为 |
|---|---|---|
| `CheckCharacterName(name)` | `check_character_name` | **建角色前查重，一张表都不写**。见下面「查重怎么把答案带回客户端」 |
| `CreateCharacter(name, jobId)` | `create_character` | 校验顺序：会话 → 名字 → 栏位 → 重名 → 职业配置。星级落在配置的 `StartStar` |
| `DeleteCharacter(characterId)` | `delete_character` | **软删**：打 `DeletedAt`，同时把名字释放出来 |
| `SelectCharacter(characterId)` | `select_character` | 选完才算进城镇，写 `CharacterSelection` |
| `LeaveCharacter()` | `leave_character` | 回选人界面，只清选角状态不影响登录态 |
| `AwakenCharacter(characterId)` | `awaken_character` | 觉醒：推到基础线的下一档（星级 1~2 → 一觉、3~5 → 二觉） |

鉴权一律走**当前连接的 Session 行**（`RequireAccountId`）：必须有活会话才能动角色数据。
「角色不存在」和「不属于你」回同一句文案，否则拿 characterId 挨个试就能探出别人有哪些角色。

**觉醒现在只校验等级。** 设定上还要求「完成觉醒任务」，但任务系统还不存在 ——
那一条在 `CharacterForms.cs` 里留成一处 TODO，任务系统做好后只改那一处，
别把条件散到别的 Reducer 或客户端。觉醒**没有反向接口**，这是设定，不是漏了。

**升星（1→2、3→4→5）还没有接口。** 那是养成系统的事（材料 / 碎片来源都没定），
现在测试要调星级直接写 SQL。

### 查重怎么把答案带回客户端（没有新表）

创建角色界面的「重复」按钮要问服务端「这个名字能不能用」。Reducer **不返回数据**，
但这类问题不用为它开事件表 —— 答案就藏在 Reducer 自己的**执行状态**里：

| 服务端 | 客户端收到 |
|---|---|
| 正常返回（什么都没写） | `Status.Committed` ⇒ 可用 |
| `throw CharacterRules.Reject(...)` | `Status.Failed(reason)` ⇒ 不可用，reason 是能直接显示的中文 |

版本校验 `CheckVersion` 就是同一个形状。事件表留给「要广播给**别的**客户端」的场景 ——
查重的答案只有调用方要看。

`CheckCharacterName` 要求有活会话（不给未登录的连接当名字探测器），
复用 `CreateCharacter` 的名字校验和重名判断（共用 `RequireNameAvailable`，免得两处文案漂移）。

⚠️ **查重通过不等于名字被占住。** 它不写任何表，所以从查完到真正 `CreateCharacter`
之间名字随时可能被别人抢走 —— `CreateCharacter` 自己照样会查一次重名并可能失败，
客户端必须处理那条路径（现在的做法是把「创建」按钮打回灰色、让玩家重新查）。

### 角色列表用 View 下发，不用公开表

`Character` 是私有表，客户端通过 **per-subscriber View** 拿自己的列表：

| View | 内容 |
|---|---|
| `MyCharacter` | 自己账号下未删除的角色（PK = CharacterId），含 `Star` 和服务端算好的 `FormId` |
| `MyAccountProfile` | 账号名 + 栏位数（客户端画选人格子要） |

为什么不做成公开表让客户端按 `account_id` 订阅：**AccountId 是自增整数，太好猜**，
改一句订阅 SQL（`WHERE account_id = 2`）就能看到别人的角色列表。View 的过滤在服务端、
以订阅者自己的 Identity 为准，客户端伪造不了。实测确认了 View 的这些性质：
per-subscriber（`ctx.Sender`）、支持两跳索引查找、可返回**自定义行类型**（只暴露想给的字段）、
可声明主键、底层表一变**会实时推送**、客户端当普通表订阅即可。

⚠️ View 里**不能 `Iter()`**，只能索引 Find / Filter ⇒ `Character.AccountId` 的索引是必需的，不是优化。

还有一条更硬的理由：**SpacetimeDB 的 SQL 不支持 `IS NULL`**（实测
`SELECT * FROM character WHERE deleted_at IS NULL` 直接 400，`Unsupported expression`）。
也就是说「只订阅未删除的角色」这个条件**根本没法用订阅 SQL 表达**。View 是过程式的
C# 代码，`if (character.DeletedAt is not null) continue;` 想怎么过滤都行 ——
凡是过滤条件写不进 SQL 的场景，都得走 View。

View 用 `IdentityBinding` 定位账号（`ViewContext` 只有 Sender、没有连接概念），
所以登出后自然返回空。读列表宽松、写数据严格，这个不对称是有意的。

### 软删和唯一名字的冲突

`NameKey` 是全服唯一索引，软删后名字还占着就成了永久占用。做法是软删时把 `NameKey`
改写成 `#del#<CharacterId>`（`#` 不在合法字符集里，撞不上真名字），`Name` 保留原值。
名字立刻释放，唯一索引仍然有效，将来要恢复或客服查询数据也都还在。

### 角色名规则

允许**中文**（和用户名只收 ASCII 不一样，角色名是给人看的）。
白名单：汉字（U+4E00–U+9FFF）+ ASCII 字母 / 数字 / 下划线。挡掉的都是有意挡的 ——
emoji、零宽字符、RTL 控制符、全角字母数字、假名、空格，这些都会造成「看着同名却是两个角色」。

长度按**显示宽度**算（汉字 2、ASCII 1），范围 4~16 ⇒ 中文 2~8 字、英文 4~16 字。
按字符数限制的话，16 个汉字在 UI 上是 16 个字母的两倍宽，排版会炸。

唯一性归一化只做 trim + ASCII 大小写折叠，**不做 Unicode 折叠**（模块跑在
InvariantGlobalization 下不可靠），白名单已经把易混淆区段挡掉了。

没做敏感词过滤。

### CLI 测试速查

```bash
spacetime call rediv login '"alice"' '"secret123"'
```

```bash
spacetime call rediv check_character_name '"星尘旅人"'
```

```bash
spacetime call rediv create_character '"影狼"' '1'
```

```bash
spacetime call rediv awaken_character '1'
```

```bash
spacetime sql rediv "SELECT character_id, name, star, form_id, level FROM my_character"
```

```bash
spacetime call rediv character_config_self_test
```

三个坑：

- **每个 `spacetime call` / `spacetime sql` 都是一条新连接**，调完就断。所以
  `character_selection` 查出来总是空的（选角行随连接清掉），要看它有内容得用活着的客户端。
- 角色 Reducer 都要求**有活会话**，所以每轮测试先 `login` 一次。
- 想调星级看形态变化，走觉醒要先满足等级，**直接写 SQL 最省事**：
  `spacetime sql rediv "UPDATE character SET star = 5 WHERE character_id = 2"`。

### 现在的数据状态（会随开发变，以库里为准）

配置：只有 `JobId=1`（凯露，`MaxStar=6`、`StartLevel=1`、`StartStar=1`）。形态五行 ——
基础线 `FormId=1` 魔法士（1 星）/ `2` 魔导士（3 星、30 级）/ `3` 黑魔法师（6 星、60 级），
爆发线 `FormId=101` 公主 / `102` 暗黑圣灵。

⚠️ 两个角色（凯露 / 优衣）的形态和美术资源都配好了（`Character/<JobId>/` 下），
但 `CharacterJob.Subtitle` 还空着、觉醒等级（15 / 30）还是占位数字。

⚠️ 2026-08-24 因为改形态设定**清过一次库**。现在库里是 `alice` / `Carol_01` / `bob_2`
三个账号，alice 名下有「影狼」（60 级 / 6 星 / 二觉）和「祭星者」（1 级 / 1 星 / 基础）。

---

## 城镇与世界时间

源码在 `spacetimedb/Town/`。两件事：**角色在哪个城镇**、**现在是哪个时段**。

### 表

| 表 | 公开性 | 说明 |
|---|---|---|
| `CharacterLocation` | **私有** | **权威**存储：`CharacterId`（主键）/ `AccountId`（索引）/ `TownId` / `EnteredAt` |
| `WorldTime` | 公开，**全服一行** | 当前时段。`Id`（固定 1）/ `BandId` / `ChangedAt` |
| `WorldTimeControl` | **私有，全服一行** | GM 控制状态。`OverrideBandId=0` 自动，`1/2/3` 持续锁定早/中/晚 |
| `WorldTimeTimer` | 定时表 | 每 60 秒跑一次 `TickWorldTime` 重算时段 |

位置存在**角色**上不是账号上：一个账号多个角色，各自在哪个城镇是各自的进度
（用户 2026-08-25 定的）。按文档约定玩法态**单开表挂 CharacterId**，不往 `Character` 上堆。

`CharacterSelection`（公开表）上**冗余了一列 `TownId`**：客户端进游戏后本来就订着这张表，
不用再多订一个 View；顺带也是「谁在哪个城镇」的在线列表。**权威仍然是 `CharacterLocation`**。

### 时段：服务端算，公开表下发

用户 2026-08-25 明确选的这条路（另一条是客户端按服务器时间自己算）。理由：

1. 全服统一 —— 所有人同时切段，以后做「夜间刷夜行怪」这类玩法才对得上；
2. 玩家改本地时钟没用；
3. 切段是**推送**，客户端不用轮询。

**边界写在配置表里，不写死**（也是用户定的）：`TbTimeBand` 的 `StartHour` 是
「服务器本地时」的起始小时，落在「StartHour ≤ 当前小时 里 StartHour 最大的那一段」，
比所有 StartHour 都小就是跨午夜那段。时区偏移是常量
`Module.ServerUtcOffsetHours`（现在是 8）—— 那是**部署属性**不是策划要调的数值，
所以没做成配置列。

⚠️ **段数固定 3 段。** 因为城镇表是三列背景（`BgMorning` / `BgNoon` / `BgNight`）
按 BandId 硬对应。`world_time_self_test` 会守住「必须恰好 3 行」。
要加第四段（比如黄昏）就得同时改城镇表的列和客户端取图的分支。

定时器是**固定间隔轮询**而不是「精确排到下一个边界」：间隔重复是自愈的
（改了边界、或者重新 publish 过，下一跳自然就对了），排精确时刻是一次性的、边界改了要记得重排。
**段没变就不写表**，所以订阅者一天只收到 3 次推送，不是每分钟一次。

GM 需要长期预览某个时段时，修改私有 `WorldTimeControl`，再调用公开但无副作用的
`refresh_world_time` 立即重算公开行。锁定状态也参与每分钟重算，所以不会被定时器恢复；
切回 0 后才继续按服务器时间自动计算。玩家调用 `refresh_world_time` 只能重跑同一套权威计算，
改不了私有控制行。

⚠️ **`WorldTime` 那一行和定时器不能只在 `Init` 里建。** `Init` 只在首次 publish 或
`--delete-data` 清库后跑一次，而世界时间是往一个**已经有数据的库**上加的功能 ——
只写 Init 的话不清库就永远没有这一行。所以做成幂等的 `EnsureWorldTime`，
由 `ClientConnected` 兜一次（它不会抛，那个钩子里抛异常会**拒绝连接**）。

### 进城镇

位置的**写入口只有一处**：`PlaceCharacter`，由 `SelectCharacter`（进入游戏）调用。
已有位置行就沿用（顺手刷 `EnteredAt`），没有就落到配置里的初始城镇 ——
也就是**新角色第一次进游戏时才建位置行**，不是建角色时。这样「建了但从没进过游戏」
的角色不占玩法态数据。配置里那个城镇被删了会退回初始城镇而不是抛错：
让玩家能进游戏比精确保留位置重要。

**没有「玩家自己在城镇之间走」的 Reducer** —— 那要先定清楚城镇怎么解锁、能不能随便去，
玩法没定型之前不开这个口子。

### 配置表

| 表 | 列 | group | 谁用 |
|---|---|---|---|
| `Town` | `TownId` | 不标 | 两端 |
| | `IsStartTown` | `s` | 新角色落哪个城镇。**有且只能有一个 True** |
| | `Name` / `SortOrder` | `c` | 城镇名（中文原文）、排序 |
| | `BgMorning` / `BgNoon` / `BgNight` | `c` | 三个时段的**背景控制器预制体**，填 Addressable 完整路径 |
| `TimeBand` | `BandId` / `StartHour` | 不标 | 段 id（早=1 中=2 晚=3）、起始小时 |
| | `Name` | `c` | 时段名（中文原文） |

服务端**看不到那三列背景**（`group="c"`），所以自检查不了路径对不对 —— 那半边只能靠客户端跑起来。

### 自检

改完这两张表跑一次：

```bash
spacetime call rediv town_config_self_test
```

```bash
spacetime call rediv world_time_self_test
```

前者查：城镇表非空、id 是正数且不重复、`IsStartTown` 恰好一个、有多少位置行指向了失效城镇。
后者查：段数恰好 3、`StartHour` 在 0~23、边界互不相同、段 id 不重复，并把当前算出来的时段打进日志。

### CLI 测试速查

```bash
spacetime sql rediv "SELECT * FROM world_time"
```

```bash
spacetime sql rediv "SELECT character_id, town_id, entered_at FROM character_location"
```

长期锁定到夜晚（本地数据库 owner 操作）：

```bash
spacetime sql rediv "UPDATE world_time_control SET override_band_id = 3 WHERE id = 1"
spacetime call rediv refresh_world_time
```

恢复自动：

```bash
spacetime sql rediv "UPDATE world_time_control SET override_band_id = 0 WHERE id = 1"
spacetime call rediv refresh_world_time
```

⚠️ 这条能用是因为 `EnsureWorldTime` **只保证行存在、不重算时段**。
一开始它也调了 `ApplyWorldTime`，结果每条新连接都会重算一次 ——
而 `spacetime sql` / `spacetime call` 每条命令都是一条新连接，
于是刚用 SQL 改完、下一条命令的连接钩子就给算回去了，这个调试手段当场失效。
实测踩过，别把重算加回连接钩子里。

### 本地 Web GM 工具

`../ReDiv_GM/` 是只监听本机的管理控制台：查看账号 / 有效与软删角色 / 在线会话 / 服务端日志，
修改账号角色栏位、角色等级 / 经验 / 星级，以及切换自动时间或持续锁定早 / 中 / 晚。
账号接口不会返回口令哈希或盐；所有写操作需要自定义请求头，并追加到
`../ReDiv_GM/data/gm-audit.jsonl`（该运行文件已忽略）。

它故意不部署成公网网站：后端使用当前机器的 SpacetimeDB owner CLI 权限，暴露到公网等于暴露数据库管理权。
启动时开两个终端：

```powershell
cd ReDiv_GM/Server
dotnet run
```

```powershell
cd ReDiv_GM
npm install
npm run dev
```

浏览器打开 `http://localhost:3000/`。后端固定监听 `127.0.0.1:5168`，默认数据库为 `rediv`；
需要改目标库可在启动后端前设置 `REDIV_DATABASE`，CLI 不在 PATH 时设置 `REDIV_SPACETIME_EXE`。

角色修改直接写私有 `Character` 权威表；玩家正在线且已经选角时，公开的
`CharacterSelection` 投影可能仍保留旧值，让玩家重新选角或重连即可刷新。

### 城镇里的玩家状态

| 表 | 公开性 | 说明 |
|---|---|---|
| `CharacterTransform` | 公开 | 城镇里的坐标。PK `ConnectionId`，`TownId` / `CharacterId` 带索引 |
| `AccountWallet` | **私有** | 金币 / 钻石。PK `AccountId`。靠 `MyWallet` View 下发 |

`Character` 上追加了 `Stamina` / `StaminaDay`（都带 `[Default(0)]`）。

**为什么坐标要单开一张表**、不加到 `CharacterSelection` 上：拆表按**访问频率**不按实体。
`CharacterSelection` 是进城镇写一次的低频行，还冗余着名字 / 职业 / 等级 / 形态；
坐标是移动中每 100ms 写一次的。混在一起 ⇒ **每走一步都把名字等级那一整行重推给所有订阅者**。

**为什么钱包不加到 `Account` 上**：Account 是凭据表，每次登录才读一次。
混在一起 ⇒ 每次加钱都把口令哈希那行一起重写。金币钻石**全角色共享**所以挂账号
（用户 2026-08-25 明确说的），体力是**角色级**的所以在 `Character` 上。

### 移动同步：客户端上报，服务端只转发

用户 2026-08-25 定的模型。`UpdateTransform(x, y, facing, moving)`：

- **服务端不校验速度、不算移动** —— 改过的客户端可以瞬移。城镇里瞬移没收益，先这样；
  要管就在这个 Reducer 里比对上次的 `UpdatedAt` 和距离。
- 鉴权靠「本连接有没有选角行」—— 那行是服务端写的，客户端伪造不了。
- **没有选角行时直接 return 而不是抛异常**：玩家点「返回选人界面」的瞬间
  客户端可能还有包在路上，那不是错误，抛异常只会刷日志。
- 客户端每 ~100ms 且位置真的变了才发（节流在客户端，见客户端文档）。

坐标行要在**三处**清掉，漏一处城镇里就留个不动的"幽灵"：连接断开、`LeaveCharacter`、
角色被删（那三处 `CharacterSelection` 也一起清）。

### 体力：配置上限 + 每日重置

用户 2026-08-25 定的（像 DNF 疲劳值）。上限查 `TbLevelExp.MaxStamina`（按等级）。

`Character.StaminaDay` 存的是**「服务器本地第几天」**（Unix 纪元起的天数，
`TownRules.LocalDayNumber`），和今天不一样就补满。存"哪一天"而不是"上次重置的时间戳"：
判断跨天直接比整数，不用在 Reducer 里做日历运算（那边连 `DateTime` 都不能用）。
和时段共用同一个时区偏移 —— 「今天」和「早上」得是同一个日历上的事。

两条补齐路径：

- **进城镇时惰性补**（`SelectCharacter` → `EnsureStaminaFresh`）—— 覆盖离线期间跨天；
- **定时器每分钟给在线角色刷一遍**（`TickWorldTime` → `RefreshStaminaForOnline`）——
  覆盖挂在城镇里跨零点。**只扫在线的**（`CharacterSelection` 一条连接一行，量很小），
  不扫全表 —— 那是每分钟一次的 O(全部角色)。

### 配置表 `LevelExp`

| 列 | group | 说明 |
|---|---|---|
| `Level` | 不标 | 等级（现在 1~60） |
| `ExpToNext` | 不标 | 升到下一级所需经验。**最高级填 0**，客户端按满显示 |
| `MaxStamina` | 不标 | 该等级的体力上限 |

⚠️ 里面的数值是**占位**（`ExpToNext = Level×100`、`MaxStamina = 15+(Level-1)/2`），
等养成系统定了要重配。

客户端也有这张表（`c,s`），所以 **View 只发当前值、不发上限** ——
经验条和体力条的分母客户端按等级自己查，不白占同步量。

---

## 版本号

客户端和服务端各自独立发布，很容易出现「客户端还是旧的、服务端已经改了表结构或
Reducer 语义」。那种情况下报出来的错五花八门（订阅失败、字段对不上、参数不匹配），
排查很费时间。所以开局先对一次版本号。

| 在哪 | 值来自 |
|---|---|
| 服务端 | `spacetimedb/Version.cs` 里的 `Module.ServerVersion` 常量 |
| 客户端 | `Application.version`，即 Player Settings 的 Version（`bundleVersion`） |

判定是**字符串全等**，不做 major/minor 兼容判断。流程：客户端连上后立刻调
`CheckVersion(Application.version)`；不匹配时服务端抛异常，客户端在
`Status.Failed(reason)` 里拿到带两边版本号的中文说明，弹窗给玩家，
并且**禁止登录 / 注册**（`AuthManager.BlockRequestsOnVersionMismatch`，
开发期想临时无视就改成 false）。

```bash
spacetime call rediv check_version '"0.0.1"'
```

⚠️ **改版本号要改四处**，少一处就会在某个环节对不上（客户端那三处见
[../ReDiv_Online/CLAUDE.md](../ReDiv_Online/CLAUDE.md) 第 9 节）：

1. 服务端 `Module.ServerVersion` —— 改完必须 `spacetime publish`
2. 客户端 `ProjectSettings.asset` 的 `bundleVersion`（编辑器开着就走
   `PlayerSettings.bundleVersion` API，别手改 YAML）
3. 客户端 `Assets/Settings/Build Profiles/PC.asset` 里那份 PlayerSettings 覆盖快照
4. 客户端 `Assets/Editor/BuildTools/PlayerBuildConfig.asset` 的 `Version`
   —— 出包时它会写回 PlayerSettings，是**出包时的真正权威**

界面右下角显示的版本号读的是 `Application.version`（不再写死在 prefab 里），
所以玩家看到的和校验用的一定是同一个值。

这是**提示性**校验，不是安全边界：拦不住改过的客户端。真要按版本卡死请求，
得把版本号存进按连接的表里，在每个业务 Reducer 里核对。

---

## 配置表（Luban → 编进 wasm）

Excel 是**唯一真相源**，客户端和服务端从同一份表各取所需：

```
ExcelTool/LubanTools/DataTables/
├── Defines/builtin.xml       只剩 vector2/3/4（角色表的结构已经不写 XML 了，见下）
├── Datas/__tables__.xlsx     表登记（read_schema_from_file=True ⇒ 结构看 Excel 表头）
├── Datas/CharacterJob.xlsx   角色表：结构 + 分组 + 数据全在这
└── Datas/CharacterForm.xlsx  形态表：同上
        │
        ├─ -t client -c cs-newtonsoft-json -d json → Unity 工程 + Addressables
        └─ -t server -c cs-bin           -d bin    → spacetimedb/Luban/Generated + spacetimedb/Configs
                                                     └─ 以嵌入资源编进 wasm
```

导出服务端配置：Unity 里 **ConfigTools 的第 6 步「导出服务端配置」**（一键流程里也有），
或命令行 `ExcelTool/LubanTools/DataTables/gen_server.bat`。

### 四个必须知道的点（都实测过）

1. **模块里没有文件系统，配置只能靠嵌入资源带进去。**
   `Assembly.GetManifestResourceStream` 在 wasi-wasm + NativeAOT + 裁剪下**可用**（实测）。
   入口是 `Luban/ServerConfig.cs`，用法 `ServerConfig.Tables.TbCharacterJob.GetOrDefault(id)`。
2. **服务端必须用 `cs-bin`，不能用客户端那套 `cs-newtonsoft-json`。**
   cs-bin 生成的代码零反射（构造函数按顺序读 ByteBuf），AOT + 裁剪安全；Newtonsoft 走反射，
   在这个环境里是雷（和 BCL 密码学一个道理，见「写代码前必须知道的」）。
3. **`Luban.Runtime` 不在 NuGet 上。** cs-bin 只需要 3 个文件（`ByteBuf` / `BeanBase` /
   `StringUtil`，零 Unity、零 Newtonsoft 依赖），已 vendored 到 `spacetimedb/Luban/Runtime/`。
   它要求 csproj 开 `<AllowUnsafeBlocks>`（ByteBuf 有两个 `*_Unsafe` 优化方法用了指针）——
   开这个开关是为了让 vendored 文件和上游**逐字节一致**，升级时直接覆盖，不用重打补丁。
   wasm 里的指针出不了线性内存沙箱，也不影响 Reducer 的确定性。
4. **字段级 group 可用。** 一张表按列分给两端。角色这两张表是这么切的
   （结构就写在各自 Excel 的 `##group` 表头行里，见下面「Excel 就是 schema」）：

**`CharacterJob`（角色 / 职业，建角色时选）**

| 列 | group | 谁用 |
|---|---|---|
| `JobId` / `Creatable` / `MaxStar` | 不标（两端都有） | 服务端校验合法性和升星上限，客户端筛选可选项、画「x/6 星」那排星 |
| `StartLevel` / `StartStar` | `s` | 建角色时的初始值，只有服务端能信 |
| `Name` / `Subtitle` / `SortOrder` | `c` | 职业名（凯露）、副标题、排序 —— **直接写中文原文** |

`MaxStar`：**有二觉填 6，没二觉填 5**。一觉是 3 星，之后靠养成升到 4、5 星（形象不变），
只有二觉才会推到 6 星，所以没二觉的角色就封顶在 5 星。

**`CharacterForm`（形态，美术资源都在这一层）**

| 列 | group | 谁用 |
|---|---|---|
| `JobId` / `FormId` / `FormType` / `UnlockStar` / `UnlockLevel` | 不标 | 服务端算当前形态、判觉醒条件；客户端把没解锁的画灰 |
| `Name` / `SortOrder` | `c` | 形态名（中文原文）、同一条线内的排序 |
| `IconKey` / `UnitPlateIconKey` / `NameIconKey` / `ArtImage` / `StillUnitPrefab` / `SkeletonUI` / `SkeletonScreen` | `c` | 头像 / 略缩图 / 名字图 / 立绘 / 预览图预制体 / UI展示预制体 / 战斗Spine预制体，都填 **Addressable 完整路径** |

⚠️ 资源列**别手打路径**：客户端有个
`Tools > XFramework > 配置 > 角色资源配置` 窗口，拖资产、算路径、写回 Excel，
详见 [../ReDiv_Online/CLAUDE.md](../ReDiv_Online/CLAUDE.md) 第 4 节。
（2026-08-23 和 08-24 各试过一次把资源整套挪进 ScriptableObject，两次都退回来了 ——
**结论是数据留在 Excel，只把录入体验做成窗口**，别再提议搬走。）

**服务端完全看不到这些资源**（它们是 `group="c"`），所以自检 Reducer 查不了它们 ——
资源那半边的校验在那个窗口里，两边互补。

`FormId` 只要求**在同一个角色内唯一**，约定基础线用 1~99、爆发线从 101 起。
两条线按 `FormType` 分，各自的填法不一样：

| 形态线 | 行 | UnlockStar / UnlockLevel | 资源（在 SO 里配，不在表里） |
|---|---|---|---|
| 基础线 `FormType=1` | 基础形态 | 1 / 0 | **填立绘**，不填视频 |
| | 一觉 | 3 / 觉醒要求的等级 | **不填立绘**，填视频 |
| | 二觉（可选） | 6 / 觉醒要求的等级 | 同上 |
| 爆发线 `FormType=2` | 一个角色可多个 | 6 / **0** | **不填立绘**，填视频 |

爆发形态**靠战斗中装备爆发宝石解锁和切换**，和星级 / 等级无关 —— 所以它的
`UnlockLevel` 必须填 0（填了非 0 自检会报），`UnlockStar` 填 6 只表示「它是 6 星形态」，
不参与解锁判定。

⚠️ 每个角色的基础线至少要有一行 `UnlockStar` 不高于 `CharacterJob.StartStar`，否则刚建出来的
角色算不出形态，客户端取不到任何资源 —— `CreateCharacter` 会直接拒绝创建。**自检 Reducer 会查这条。**

### 配置改完跑一次自检

两张表靠 `JobId` / `FormId` / `UnlockStar` 互相引用，**没有任何编译期检查** ——
配错了不报错，只表现成「建不出角色」「觉醒不了」「客户端没资源」，很难往回追。所以：

```bash
spacetime call rediv character_config_self_test
```

查的是表之间的引用和结构：初始星级接不接得住一行基础形态、`MaxStar` 够不够得着最高那档
（够不着的话那一档永远觉醒不到）、`MaxStar` 是不是 5 或 6、同一角色内 `FormId` 有没有重复、
基础线有没有两行抢同一个 `UnlockStar`、觉醒等级门槛有没有倒挂（6 星只要 30 级、3 星却要 60 级
这种）、`FormType` 合不合法、爆发形态的 `UnlockLevel` 是不是 0、形态的 `JobId` 是否悬空。

查不了的：Addressable 路径字符串对不对（那些列是 `group="c"`，服务端根本看不到），
只能靠客户端跑起来才知道。

### Excel 就是 schema（2026-08-24 起）

两张角色表在 `__tables__.xlsx` 里的 `read_schema_from_file` 都是 **`True`**，
**字段名 / 类型 / 分组 / 注释全部以 Excel 表头为准**，XML bean 定义已经删掉
（`Defines/` 现在只剩 `builtin.xml` 的 vector 类型）。

⚠️ 这推翻了本文件之前写的「分组只能写在 XML 里，Excel 表头给不了字段级 group」——
**那句话是错的**，2026-08-24 实测确认：

| 实测项 | 结果 |
|---|---|
| `read_schema_from_file=True` 时 `##group` 行认不认 | ✅ 认。`s` 列只进服务端 bin，`c` 列只进客户端 json |
| 联合主键（`JobId+FormId`，index 写在 `__tables__` 里） | ✅ 照常，`TbCharacterForm.Get(jobId, formId)` 还在 |
| `##` 注释行 | ✅ **会变成生成代码的 `/// <summary>`**，XML 那套做不到 |

新加配置表直接建 Excel、在 `__tables__.xlsx` 里把 `read_schema_from_file` 填 `True` 就行。

⚠️ 代价：schema 藏在**二进制 .xlsx 里，git diff 看不见**。以前改 XML 是文本 diff，
review 时一眼看得到「谁改了某列的 type」，现在只能靠 `ExcelTable.ps1 -Action Dump` 主动查 ——
改表结构时在提交信息里写清楚。

形态表是 `mode = list` + 联合主键（`__tables__.xlsx` 的 index 列写 `JobId+FormId`）。
⚠️ 联合主键的表**不能用 `mode = map`**，Luban 会报「是单主键表，index 不能包含多个 key」。
代码里访问是 `TbCharacterForm.Get(jobId, formId)`。

形态拆成行而不是在角色表上开 `Form1Art` / `Form2Art` … 一排列：客户端字段一多横向会爆，
而且加一个爆发形态只是多一行，不用改表结构。

### 限制与约定

- **改了配置要走两步**：重新导出（ConfigTools 第 6 步）+ `spacetime publish`。
  只改 Excel 不发布，线上还是旧配置。这既是限制也是优点：配置和代码是同一份产物，不可能错配。
- 真需要热更数值（不重发就调平衡）时再改成「配置进表 + 导入 Reducer 推进去」，
  那时客户端可以直接订阅配置表，两端彻底同源。现阶段没必要。
- 建议把「改了 `s` 组配置」纳入版本号语义，bump 一下版本 —— 版本校验就能挡住配置不一致的旧客户端。
- 改 Excel **必须走 `ExcelTool/LubanTools/ExcelTable.ps1`**（Excel COM 自动化，原因见脚本头部）。
  它有 `Dump` / `AddRows` / `UpdateRows` / `AddColumn` / `AddSheet` / `SetHeader` /
  `AddEnumItems` / `AddEnumType` / `DeleteRows` / `DeleteColumn` / `DeleteSheet` 这些 Action。
- **Excel 表头是 4 行**：`##var` / `##type` / `##group` / `##`，数据从第 5 行起。
  `##group` 那一行写 `c` / `s` / `c,s`，所以注释里不用再写「仅客户端」「仅服务端」。
  `##group` 行就是**权威定义**（`read_schema_from_file=True`），不用再去改 XML。

### 现在的配置内容（会随开发变，以表里为准）

`../ReDiv_Online/ExcelTool/LubanTools/DataTables/Datas/` 下两张表：

| 表 | 内容 |
|---|---|
| `CharacterJob.xlsx` | `1` 凯露 / `2` 优衣，都是 `MaxStar=6`、`StartLevel=1`、`StartStar=1` |
| `CharacterForm.xlsx` | 每个角色 4 行：基础线 3 个（1★/3★/6★）+ 爆发线 1 个（6★） |

美术资源都填好了（在 `Assets/AddressableAssets/Remote/Character/<JobId>/` 下，
`0Common` 给基础形态、另两个子目录给觉醒线和爆发线）。还没定的：

- **觉醒等级是占位数字**（现在 15 / 30）
- `CharacterJob.Subtitle`（选人界面职业名下面那行小字）还空着

填表约定：`Name` / `Subtitle` **直接写中文原文**（项目纯中文，没有多语言这一层）；
资源列填 Addressable **完整路径**，但**别手打** —— 用客户端的
`Tools > XFramework > 配置 > 角色资源配置` 窗口拖资产，它会写回 Excel。
改完跑一次 `spacetime call rediv character_config_self_test`。

---

## 写代码前必须知道的

分两块：**API 约定**是 SpacetimeDB 2.8 的写法规则（照 1.x 写会报错或静默失效），
**环境地雷**是这个 wasi-wasm + NativeAOT 环境里实测踩过的。

### API 约定（2.8）

- 表属性是 `Accessor = "Xxx"`，**不是** 1.x 的 `Name =`。`Name` 现在只用来覆盖 SQL 规范名
- 索引必须写全 `[SpacetimeDB.Index.BTree]`，裸 `Index` 会和 `System.Index` 撞名。
  多列索引用 `Columns = new[] { nameof(A), nameof(B) }`，属性里**不能用集合表达式** `[...]`
- 多列索引 `(a, b)` 已覆盖 `a` 的前缀查询，**不要**再为 `a` 单独建索引
- 只有 `[PrimaryKey]` 才有 `Update` 方法，`[Unique]` 没有了
- 客户端连接用 `WithDatabaseName`（不是 `WithModuleName`）；`light_mode`、`CallReducerFlags` 已删
- **全局 reducer 回调没了**。别的客户端调 Reducer 你收不到参数。要广播「发生了什么」用
  **事件表** `[Table(Public = true, Event = true)]`：插入的行事务提交时推给订阅者然后立即删除，
  客户端只有 `OnInsert`。事件表的 `Event` 标记**发布后不可更改**，改了迁移会失败
- Reducer 里禁止 `DateTime.Now` / `new Random()` / 网络 IO / 可变 static，
  时间和随机只能取 `ctx.Timestamp` 和 `ctx.Rng`（事务可能被重放，必须确定性）
- 定时 Reducer 默认私有，不用再自己校验 sender
- `spacetime generate` 默认**不生成**私有表的绑定，需要就加 `--include-private`
- confirmed reads 默认开启（等落盘才推给客户端）。要低延迟可在客户端 `WithConfirmedReads(false)`
- 行级安全（RLS）是实验特性，官方建议用 **View** 做访问控制。
  `ViewContext`（读 `ctx.Sender`）是 per-subscriber 计算，`AnonymousViewContext` 全服共享
  一份物化 —— 能用后者就别用前者
- View 里**不能 `.Iter()`**，只能索引 `.Find()` / `.Filter()` / `.Count`
- 订阅查询只能返回**单表整行**，不能投影列；`JOIN` 最多两表且两侧 join 列都要有索引
- **SQL 不支持 `IS NULL`**（`WHERE x IS NULL` 直接 400 `Unsupported expression`）。
  可空列没法用订阅 SQL 过滤 —— 这是角色列表必须走 View 的原因之一

### 环境地雷（都实测踩过）

**BCL 的密码学在 wasi-wasm 上链接得过、一调就抛。** `SHA256.HashData`、
`Rfc2898DeriveBytes.Pbkdf2`、`CryptographicOperations.FixedTimeEquals` 全是
`SystemSecurityCryptography_PlatformNotSupported` —— 编译、`spacetime build`、
`spacetime publish` 全绿，只有真正 `call` 到才炸。所以口令哈希是 `Security/` 下自己写的
纯托管实现。**以后用到任何不确定的 API，先假定它用不了，写个探针 Reducer 真 call 一次**
（同理还有 Newtonsoft 那套反射，见「配置表」第 2 点）。

**表加字段只能加在 struct 末尾，删列根本不支持。** 插到中间会被判成列重排，
publish 直接拒绝（`Reordering table ... requires a manual migration`）；
删列是 `Removing a column ... requires a manual migration`。**开发期的做法就是清库重发**
`spacetime publish --delete-data=always --yes`，别为了保住测试数据留兼容分支。
⚠️ `[Default(...)]` **只在迁移时给已有行回填，对新插入的行无效** —— 新字段要有初值必须在
Insert 处显式赋值（`Account.CharacterSlots` 只标了 `[Default(4)]`，清库后新账号栏位数是 0，
一个角色都建不出来）。

**CLI 用 snake_case，客户端绑定用 PascalCase。** C# 写 `public static void Ping(...)`，
规范名会转成 `ping`：`spacetime call rediv Ping` 报 `No such reducer`，`ping` 才对；
而生成的绑定里仍是 `Conn.Reducers.Ping()`。表名同理（`Accessor` 决定 `ctx.Db.Xxx`，
SQL / CLI 用规范名）。写裸 SQL 前先 `spacetime describe rediv --json` 核对真名。

**`spacetime sql` 能写。** owner 身份可以直接 `UPDATE` / `DELETE`，调试改数据很方便：
`spacetime sql rediv "UPDATE character SET level = 30 WHERE character_id = 9"`。

**csproj 必须显式写 `<OutputType>Library</OutputType>`。** `spacetime init` 的 .NET 10 模板
漏了它，缺了会 `error CS8899`：源生成器在 AOT 下把 `Main` 标成 `[UnmanagedCallersOnly]`
当 preinit 导出用，而 `SelfContained=true` 会把 `OutputType` 推成 `Exe`，两者冲突。已修。

**构建环境的四个小事实：**

- 首次 publish 会下载 **535MB** 的 WASI SDK 到 `~/.wasi-sdk/`，之后不再下
- `dotnet build` 只做语法检查，真正的 wasm 产物要 `dotnet publish -c Release`（或
  `spacetime build`），出在 `bin/Release/net10.0/wasi-wasm/publish/StdbModule.wasm`，约 6.5MB
- 没装 wasm-opt，每次 build 都提示 `Could not find wasm-opt` —— 只是产物偏大，功能不受影响。
  要消掉从 <https://github.com/WebAssembly/binaryen/releases> 下 binaryen 丢进 PATH
- `spacetime.json` 里的 `"native-aot": true` 在 .NET 10 下是多余的（本来就走 NativeAOT-LLVM），
  已删掉

---

## 客户端接线

Unity 侧的详细文档在 [../ReDiv_Online/CLAUDE.md](../ReDiv_Online/CLAUDE.md) 第 5 节，
这里只记**服务端契约相关**的部分。

四个门面，界面只跟它们打交道、不碰 `Conn`：

| 文件（`../ReDiv_Online/Assets/Scripts/Net/`） | 职责 |
|---|---|
| `SpacetimeConnection.cs` | 只管连接生命周期 + `ServerLinkState`，**不建立任何订阅** |
| `AuthManager.cs` | 账号：`session` / `session_closed` 的订阅、登录态、Register/Login/Logout |
| `CharacterManager.cs` | 角色：`my_character` / `my_account_profile` 的订阅、角色列表、查重/建/删 |
| `TownManager.cs` | 城镇与世界时间：`world_time` / `character_selection` / `character_transform` 的订阅、同城镇玩家、坐标上报 |

已接好的界面：`CommonUI`（标题）、`LoginUI`、`SelectCharacterUI`（选人 / 删角）、
`CreatCharacterUI`（创角）、`ReviseCharacterNameUI`（起名字 / 查重 / 创建）、
`MainCommonUI`（城镇主界面，按城镇 + 时段显示背景）。

**四条契约，改的时候别破坏：**

1. **订阅必须在调 Reducer 之前建立**，否则成功那一行的 `OnInsert` 会漏。
   identity 在订阅 SQL 里是十六进制字面量，**要带 `0x` 前缀**（`Identity.ToString()` 不带）。
2. **订阅要分段**：连上订 `session` → **登录成功后**才订 `my_character` /
   `my_account_profile`。订阅 SQL join 不到会话，连上就订的话 View 返回空。
   两个 View 都是 per-subscriber 的，订阅时**不用带 where**。
3. **成功与否看表，不看 Reducer 有没有报错**：登录看 `Session` 表里有没有自己这条连接的行
   （判断用 `ConnectionId == Conn.ConnectionId`，不是 identity —— 同一 identity 可能有多条连接）；
   建角色看 `my_character` 多出一行。
4. **失败文案从 `ctx.Event.Status` 的 `Status.Failed(reason)` 取**，reason 就是服务端抛的
   中文原文（「角色栏位已满（4/4）」这种），直接显示给玩家。

⚠️ 表回调**要连 `OnUpdate` 一起挂**：同主键的删+插在同一事务里会被合并成一次 update，
只挂 Insert/Delete 会漏。带主键的 View 同理。

Unity SDK 不走 manifest 依赖，而是**内嵌**在
`../ReDiv_Online/Packages/com.clockworklabs.spacetimedbsdk/`（v2.8.2，带两处本地补丁，
原因和升级步骤见该目录下的 `UPSTREAM.md`）。挂 `SpacetimeConnection` 时它会自动补一个
`SpacetimeDBNetworkManager`（SDK 靠它驱动 `FrameTick()`，是必需组件），
**不要再手动挂第二个** —— 那是单例，重复挂会抛异常。

真机 / 局域网调试把 Inspector 里的地址改成 `http://192.168.10.226:2383`。

---

## 一次性准备（新机器）

```bash
powershell -c "iwr https://windows.spacetimedb.com -useb | iex"
```

```bash
spacetime server add rediv-local --url http://127.0.0.1:2383
```

```bash
spacetime server ping rediv-local
```

**关于登录**：不用跑 `spacetime login`。第一次 `spacetime publish` 时，CLI 会自动向目标
服务器申请一个「服务器直发身份」（`We have logged in directly to your target server`），
存在 CLI 配置里。这个身份只对这台服务器有效，但对本地开发足够，也不需要浏览器。

⚠️ 这个身份是**发布者身份**，也就是模块的 owner。换电脑或清了 CLI 配置就拿不回来了，
届时无法覆盖发布同一个数据库，只能 `--delete-data=always` 重来。

---

## 待定（等玩法定型）

- 表结构：按**访问频率**拆表，而不是按实体拆（官方明确反对宽表）。
  账号系统已经按这个来了：凭据在 `Account`（每次登录才读一次），
  玩法数据以后单开表挂 `AccountId`，别往 `Account` 上堆
- 哪些数据公开、哪些私有 + View
- 登录相关的补齐项：改密码 / 找回密码 / 删号、失败次数锁定（要改错误回报方式，
  见「账号系统 → 有意没做的事」）
- 角色相关的补齐项：改名、软删角色的恢复入口、扩栏位的入口（付费 / 活动）、敏感词过滤
- **角色配置的觉醒等级还是占位数字**，`CharacterJob.Subtitle` 还空着
- 待机动画名不进配置表：编在 `SkeletonUI` 预制体的 `CharacterGraphicUI` 组件上
- **升星（1→2、3→4→5）还没有接口** —— 那是养成系统的事（材料 / 碎片来源都没定）。
  现在只有觉醒能推星级，测试时用 SQL 直接改
- **爆发宝石**：爆发形态的配置已就绪，但「装备宝石切形态」是战斗内行为，
  装备 / 背包 / 战斗表一张都还没有
- 「觉醒任务」：现在 `AwakenCharacter` 只校验等级。任务系统做好后在
  `CharacterForms.cs` 那一处 TODO 加条件，表结构不用动
- Luban 配置怎么进服务端 —— **已解决**，见「配置表」一节
- 战斗由谁裁定：服务端全权模拟 / 服务端发种子+校验结果
