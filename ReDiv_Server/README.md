# ReDiv_Server

ReDiv 服务端。C# 编写的 **SpacetimeDB 模块**，编译成 WebAssembly 跑在数据库进程内 ——
**没有独立的游戏服务器进程**。

**当前状态：只有账号系统。** 注册 / 登录 / 会话已经能用（见下面「账号系统」一节），
玩法表一张都还没有 —— 玩法定型后再往里加。

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
    ├── Auth/               账号系统
    │   ├── AuthTables.cs   Account / IdentityBinding / Session / SessionClosed
    │   ├── AuthReducers.cs Register / Login / Logout + 会话管理
    │   ├── AuthRules.cs    用户名与口令的格式规则、归一化
    │   └── AuthSelfTest.cs 手写密码学实现的测试向量自检
    └── Security/           口令哈希（自己写的，原因见「已知坑」）
        ├── Sha256.cs
        ├── Pbkdf2Sha256.cs
        └── PasswordHasher.cs
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
[../ReDiv_Online/CLAUDE.md](../ReDiv_Online/CLAUDE.md) 第 8 节）：

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
  见「账号系统 → 有意没做的事」）、昵称（单开 Profile 表）
- 战斗由谁裁定：服务端全权模拟 / 服务端发种子+校验结果
- Luban 配置怎么进服务端（模块里没有文件系统，配置要么编进 wasm，要么 Init 时灌进表）
