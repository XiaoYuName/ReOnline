# ReDiv_Server

ReDiv 服务端。C# 编写的 **SpacetimeDB 模块**，编译成 WebAssembly 跑在数据库进程内 ——
**没有独立的游戏服务器进程**。

**当前状态：空壳。** 只有生命周期钩子和一个 `Ping`，没有任何业务表。玩法定型后再往里加。

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
    └── Module.cs           生命周期钩子
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

---

## 待定（等玩法定型）

- 表结构：按**访问频率**拆表，而不是按实体拆（官方明确反对宽表）
- 哪些数据公开、哪些私有 + View
- 战斗由谁裁定：服务端全权模拟 / 服务端发种子+校验结果
- Luban 配置怎么进服务端（模块里没有文件系统，配置要么编进 wasm，要么 Init 时灌进表）
