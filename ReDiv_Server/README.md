# ReDiv_Server

ReDiv 服务端。C# 编写的 **SpacetimeDB 模块**，编译成 WebAssembly 跑在数据库进程内 ——
**没有独立的游戏服务器进程**。

**当前状态：账号系统 + 角色系统。** 注册 / 登录 / 会话，多角色的创建 / 删除 / 选择，
以及形态与觉醒（基础 → 一觉 → 二觉，按星级现算）都能用了（见下面两节）。
战斗、地图、背包这些玩法表还没有 —— 定型后再往里加。

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
    ├── Security/           口令哈希（自己写的，原因见「已知坑」）
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

```bash
spacetime publish
```

```bash
spacetime generate
```

两个命令都会自动读根目录的 `spacetime.json`，不用再传 `--server` / `--module-path` / `--out-dir`。

生成的 C# 绑定落在 `../ReDiv_Online/Assets/Scripts/Net/ModuleBindings`，命名空间
`ReDiv.Net.Bindings`。那个目录是**生成物，不要手改**。

**`spacetime generate` 之后必须回客户端跑一次编译验证** —— schema 变了可能让现有客户端代码编不过：

```bash
cd ../ReDiv_Online && unity command recompile && unity command recompile_status
```

注意 `recompile` 返回 `up_to_date` 不代表没错误，还要单独查控制台。完整规则见
[../CLAUDE.md](../CLAUDE.md) 第 2 节。

改了表结构导致无法自动迁移时，清库重发：

```bash
spacetime publish --delete-data=always --yes
```

看日志：

```bash
spacetime logs rediv --follow
```

查数据：

```bash
spacetime sql rediv "SELECT * FROM st_table"
```

调 Reducer（注意用 **snake_case**，见下）：

```bash
spacetime call rediv ping
```

列出本机服务器上的数据库（`spacetime list` 默认查 maincloud，必须带 `-s`）：

```bash
spacetime list -s rediv-local
```

看已发布的 schema：

```bash
spacetime describe rediv --json
```

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

**SHA-256 / HMAC / PBKDF2 都是自己写的**（`Security/`），不是调 BCL —— 原因见「已知坑」。
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

⚠️ **新字段只能追加到 struct 末尾**，插到中间会被判成 reorder（`Reordering table
character requires a manual migration`）。**删列 SpacetimeDB 根本不支持** ——
开发期直接清库重发就行，见「已知坑」里那一条。

`Account` 上有 `CharacterSlots`（默认 4，上限常量 8），栏位可扩展所以存在账号上。
⚠️ 它由 `Register` 插入时**显式赋值**，不要指望表上的 `[Default]`（那个只在迁移时
给已有行回填，对新插入的行无效 —— 踩过，见「已知坑」）。

玩法态（地图 / 坐标 / HP / 体力）**故意还没建表**。定型后以 `CharacterId` 为主键单开表，
别往 `Character` 上堆 —— 那张表只在选人界面读一次。

### Reducer

| 客户端绑定 | CLI | 行为 |
|---|---|---|
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

⚠️ 美术资源**不在配置表里**（2026-08-24 挪进 ScriptableObject 了，见「配置表」一节）。
凯露的资源已经配好（`Character/100002/` 那套图），但待机动画名还是空的、
`CharacterJob.Subtitle` 也还空着。**优衣（JobId=2）刚加进 CharacterJob 表，形态一行都还没配**
—— 自检现在就报着这条。

⚠️ 2026-08-24 因为改形态设定**清过一次库**。现在库里是 `alice` / `Carol_01` / `bob_2`
三个账号，alice 名下有「影狼」（60 级 / 6 星 / 二觉）和「祭星者」（1 级 / 1 星 / 基础）。

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
   在这个环境里是雷（和 `System.Security.Cryptography` 一个道理，见「已知坑」）。
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
| `IconKey` / `ArtKey` / `SpineKey` / `VideoStartKey` / `VideoLoopKey` | `c` | 头像 / 立绘 / 战斗 Spine / 启动视频 / 循环视频，都填 **Addressable 完整路径** |

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

### ⚠️ 两张表的结构定了，还有几个占位数值

`../ReDiv_Online/ExcelTool/LubanTools/DataTables/Datas/` 下现在是：

| 表 | 现有内容 |
|---|---|
| `CharacterJob.xlsx` | `JobId=1`（Name=`凯露`、`MaxStar=6`、`StartLevel=1`、`StartStar=1`，Subtitle 空着） |
| `CharacterForm.xlsx` | 基础线 `1` 魔法士（1 星）/ `2` 魔导士（3 星、30 级）/ `3` 黑魔法师（6 星、60 级）；爆发线 `101` 公主 / `102` 暗黑圣灵 |

**结构是最终设计，可以照着往下写代码。** 资源路径也填好了（用的是
`Assets/AddressableAssets/Remote/Character/100002/` 那套图：`0Common` 给基础形态、
`201` 给觉醒线、`202` 给爆发线）。但这些还没定：

- **待机动画名全是空的**（在 SO 里，不在表里）
- **觉醒等级是占位数字**
- `CharacterJob.Subtitle`（选人界面职业名下面那行小字）还空着
- **`JobId=2`（优衣）刚加进 CharacterJob 表，形态一行都还没配** —— 自检现在就报这条。
  美术资源在 `Character/100001/` 下已经有了
- **启动视频全是空的** —— 现在的素材每个形态只有一支（文件名后缀 `_000002`，是循环那支）

填表约定：`Name` / `Subtitle` **直接写中文原文**（项目纯中文，没有多语言这一层）；
资源列填 Addressable **完整路径**，但**别手打** —— 用客户端的
`Tools > XFramework > 配置 > 角色资源配置` 窗口拖资产，它会写回 Excel。

改完跑一次 `spacetime call rediv character_config_self_test`。

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

## 已知坑（踩过并已修）

### csproj 缺 `OutputType`

`spacetime init` 针对 .NET 10 生成的模板里，AOT 那个 `PropertyGroup` 少了
`<OutputType>Library</OutputType>`。缺了会编译失败：

```
error CS8899: 无法使用 "UnmanagedCallersOnly" 对应用程序入口点进行特性化
```

原因：源生成器在 AOT 模式下把 `Main` 标成 `[UnmanagedCallersOnly(EntryPoint = "__preinit__10_init_csharp")]`
当 preinit 导出用，而 `SelfContained=true` 会让 SDK 把 `OutputType` 推成 `Exe`，
`UnmanagedCallersOnly` 不允许标在入口点上。已在 csproj 里显式压回 `Library`。

### CLI 调 Reducer 要用 snake_case，客户端绑定用 PascalCase

C# 里写 `public static void Ping(...)`，规范名（canonical name）会被转成 `ping`：

```
spacetime call rediv Ping   →  Error: No such reducer `Ping` ... 相似的名字: `ping`
spacetime call rediv ping   →  OK
```

而生成的 C# 客户端绑定里仍然是 `Conn.Reducers.Ping()`。表名同理：
`Accessor = "Xxx"` 决定代码里的 `ctx.Db.Xxx`，SQL / CLI 里用的是规范名。
写 RLS 过滤器或裸 SQL 之前，先用 `spacetime describe rediv --json` 核对真实名字。

### `System.Security.Cryptography` 在 wasi-wasm 上运行时不可用

**链接得过，一调就抛。** 2026-08-22 实测（SpacetimeDB 2.8.2 / .NET 10 / NativeAOT-LLVM）：

```
Error: Response text: SystemSecurityCryptography_PlatformNotSupported
```

`SHA256.HashData` 和 `Rfc2898DeriveBytes.Pbkdf2` 都是这个结果 —— 编译和 `spacetime build`
全绿，`spacetime publish` 也成功，只有真正 `call` 到那个 Reducer 才炸。
`CryptographicOperations.FixedTimeEquals` 同理不能用。

所以口令哈希用的是 `spacetimedb/Security/` 下自己写的纯托管实现
（SHA-256 + HMAC + PBKDF2），正确性靠 `auth_self_test` 的测试向量守。
以后要用别的哈希 / 签名 / 加密，先假定 BCL 那套用不了，**并且必须真的 call 一次验证**，
光看编译通过说明不了任何问题。

### 表加字段只能加在 struct 末尾

插到中间会被判成列重排，publish 直接拒绝：

```
Reordering table character requires a manual migration
```

实测：给 `Character` 加字段时放在 `Exp` 后面就撞了，挪到 struct 末尾就过。
所以那些字段的顺序**不是随便排的**，别为了「看起来整齐」挪它。

**删列干脆不支持**（`Removing a column spec_id from table character requires a manual
migration`，2026-08-24 改形态设定时撞的）。**开发期的做法就是清库重发**：

```bash
spacetime publish --delete-data=always --yes
```

⚠️ 别为了保住开发库里那点测试数据去留废弃字段、`[Default]` 回填、「读到 0 就退回默认值」
这类兼容分支 —— 那些东西留下来只会误导后面看代码的人。正式版发布之后再谈迁移。

⚠️ 另外：`[Default(...)]` **只在迁移时给已有行回填，对新插入的行无效**。
新字段要有初值必须在 Insert 那里显式赋值。踩过：`Account.CharacterSlots` 只标了
`[Default(4)]`，清库后新注册的账号栏位数是 0，一个角色都建不出来。

### Luban 联合主键的表不能用 `mode = map`

形态表用 `JobId+FormId` 联合主键，`mode` 写 `map` 会报：

```
是单主键表，index:'JobId+FormId' 不能包含多个 key
```

改成 `mode = list` + index 写 `JobId+FormId` 就对了，生成的访问器是
`TbCharacterForm.Get(jobId, formId)`。

### `spacetime sql` 能写，但不支持 `IS NULL`

owner 身份可以直接 `UPDATE` / `DELETE`，调试改数据很方便：

```bash
spacetime sql rediv "UPDATE character SET level = 30 WHERE character_id = 9"
```

但 `WHERE x IS NULL` 会 400（`Unsupported expression`）—— 可空列没法用 SQL 过滤，
这也是角色列表必须走 View 的原因之一（见「角色系统」）。

### 首次 publish 会下载 535MB 的 WASI SDK

NativeAOT-LLVM 需要 wasi-sdk，第一次构建会自动下到 `~/.wasi-sdk/`。之后不再下载。

### `dotnet build` 只做语法检查

真正的 wasm 产物要 `dotnet publish -c Release`（或 `spacetime build`），
出在 `bin/Release/net10.0/wasi-wasm/publish/StdbModule.wasm`，约 6.5MB。

### 没装 wasm-opt，模块未经优化

每次 build 都会提示 `Could not find wasm-opt to optimise the module`。功能不受影响，
只是产物偏大、运行稍慢。要消掉就从
<https://github.com/WebAssembly/binaryen/releases> 下 binaryen 丢进 PATH。

### `native-aot` 配置项在 .NET 10 下是多余的

`spacetime init` 会往 `spacetime.json` 里塞 `"native-aot": true`，
但 .NET 10 本来就走 NativeAOT-LLVM，CLI 每次都会提示一句。已删掉。

---

## SpacetimeDB 2.8 写代码时必须记住的

来自官方文档，1.x 的老写法在 2.8 会直接报错或静默失效：

- 表属性是 `Accessor = "Xxx"`，**不是** 1.x 的 `Name =`。`Name` 现在只用来覆盖 SQL 规范名
- 索引必须写全 `[SpacetimeDB.Index.BTree]`，裸 `Index` 会和 `System.Index` 撞名。
  多列索引用 `Columns = new[] { nameof(A), nameof(B) }`，属性里**不能用集合表达式** `[...]`
- 多列索引 `(a, b)` 已覆盖 `a` 的前缀查询，**不要**再为 `a` 单独建索引
- 只有 `[PrimaryKey]` 才有 `Update` 方法，`[Unique]` 没有了
- 客户端连接用 `WithDatabaseName`（不是 `WithModuleName`）；`light_mode`、`CallReducerFlags` 已删
- **全局 reducer 回调没了**。别的客户端调 Reducer 你收不到参数。
  要广播「发生了什么」，用**事件表** `[Table(Public = true, Event = true)]`：
  插入的行事务提交时推给订阅者然后立即删除，客户端只有 `OnInsert`
- 事件表的 `Event` 标记发布后**不可更改**，改了迁移会失败
- Reducer 里禁止 `DateTime.Now` / `new Random()` / 网络 IO / 可变 static，
  时间和随机只能取 `ctx.Timestamp` 和 `ctx.Rng`（事务可能被重放，必须确定性）
- 定时 Reducer 默认私有，不用再自己校验 sender
- `spacetime generate` 默认**不生成**私有表的绑定，需要就加 `--include-private`
- confirmed reads 默认开启（等落盘才推给客户端）。要低延迟可在客户端
  `WithConfirmedReads(false)`
- 行级安全（RLS）是实验特性，官方建议用 **View** 做访问控制。
  `ViewContext`（读 `ctx.Sender`）是 per-subscriber 计算，
  `AnonymousViewContext` 全服共享一份物化 —— 能用后者就别用前者
- View 里**不能 `.iter()`**，只能索引 `.Find()` / `.Filter()` / `.Count`
- 订阅查询只能返回**单表整行**，不能投影列；`JOIN` 最多两表且两侧 join 列都要有索引

---

## 客户端接线

Unity 侧已经就位：

- Unity SDK 不走 manifest 依赖，而是**内嵌**在 `../ReDiv_Online/Packages/com.clockworklabs.spacetimedbsdk/`
  （v2.8.2，带两处本地补丁，原因和升级步骤见该目录下的 `UPSTREAM.md`）
- 生成的绑定在 `../ReDiv_Online/Assets/Scripts/Net/ModuleBindings/`（命名空间
  `ReDiv.Net.Bindings`，由 `spacetime generate` 覆盖写入，**不要手改**）
- 连接管理器 `../ReDiv_Online/Assets/Scripts/Net/SpacetimeConnection.cs`（命名空间 `ReDiv.Net`）

用法：新建一个空 GameObject，挂上 `SpacetimeConnection`，Inspector 里确认地址和库名。
它会自动补一个 `SpacetimeDBNetworkManager`（SDK 靠它在 Update 里驱动 `FrameTick()`，
WebGL 下还靠它跑消息解析协程，是必需组件），所以**不要再手动挂第二个** —— 那是单例，
重复挂会抛异常。

连上后 Console 应该出现：

```
[Stdb] 正在连接 http://127.0.0.1:2383 / rediv
[Stdb] 已连接，identity=...
[Stdb] 订阅已生效
```

服务端 `spacetime logs rediv -f` 同时应能看到 `[Connect] ...`。

真机 / 局域网调试记得把 Inspector 里的地址改成 `http://192.168.10.226:2383`。

### 客户端已经接好了

Unity 侧的登录逻辑已经完成，入口都在 `../ReDiv_Online/Assets/Scripts/Net/`：

| 文件 | 职责 |
|---|---|
| `SpacetimeConnection.cs` | 只管连接生命周期 + `ServerLinkState`。**不再建立任何订阅** |
| `AuthManager.cs` | 账号门面：订阅、表回调、Reducer 回调、登录态、`RegisterAsync/LoginAsync/LogoutAsync` |
| `AuthValidation.cs` | `AuthRules.cs` 的客户端镜像，少一次白跑的往返（服务端仍是权威） |

界面在 `Assets/Scripts/Game/Scripts/UGUI/`：`LoginUI`（登录/注册）、
`CommonUI`（服务器状态 + 账号栏 + 点屏幕的三种走向）。

契约上有三条容易踩的，客户端已经按这个实现，改的时候别破坏：

1. **订阅必须在调 Login 之前建立**，否则成功那一行的 `OnInsert` 会漏。
   `AuthManager` 在 `OnConnect` 里就订阅了
   `session` / `session_closed` 里自己 identity 的行。
   identity 在 SQL 里是十六进制字面量，要带 `0x` 前缀（`Identity.ToString()` 不带）。
2. **登录成功看 `Session` 表里有没有自己这条连接的行**，不是看 Reducer 有没有报错。
   判断用 `ConnectionId == Conn.ConnectionId` 而不是 identity —— 同一 identity 可能有多条连接。
3. **失败文案从 Reducer 回调的 `Status.Failed(reason)` 取**，reason 就是服务端抛的中文原文。

---

---

## 待定（等玩法定型）

- 表结构：按**访问频率**拆表，而不是按实体拆（官方明确反对宽表）。
  账号系统已经按这个来了：凭据在 `Account`（每次登录才读一次），
  玩法数据以后单开表挂 `AccountId`，别往 `Account` 上堆
- 哪些数据公开、哪些私有 + View
- 登录相关的补齐项：改密码 / 找回密码 / 删号、失败次数锁定（要改错误回报方式，
  见「账号系统 → 有意没做的事」）
- 角色相关的补齐项：改名、软删角色的恢复入口、扩栏位的入口（付费 / 活动）、敏感词过滤
- **角色配置的启动视频和觉醒等级还是空的 / 占位的**，`JobId=2` 整个还没配，见「配置表」最后一节
- **待机动画名那一列已删**（2026-08-24）：以后改成挂预制体引用，动画名在预制体里预先编好
- **升星（1→2、3→4→5）还没有接口** —— 那是养成系统的事（材料 / 碎片来源都没定）。
  现在只有觉醒能推星级，测试时用 SQL 直接改
- **爆发宝石**：爆发形态的配置已就绪，但「装备宝石切形态」是战斗内行为，
  装备 / 背包 / 战斗表一张都还没有
- 「觉醒任务」：现在 `AwakenCharacter` 只校验等级。任务系统做好后在
  `CharacterForms.cs` 那一处 TODO 加条件，表结构不用动
- Luban 配置怎么进服务端 —— **已解决**，见「配置表」一节
- 战斗由谁裁定：服务端全权模拟 / 服务端发种子+校验结果
