# ReDiv

一款自研的在线联机游戏。美术素材参考公主连结风格，**玩法完全自研**，与公连有很大不同。

## 先读哪份文档

这个仓库里只有**三份**文档是维护中的项目文档，有冲突时以它们为准：

| 文档 | 管什么 |
|---|---|
| [CLAUDE.md](CLAUDE.md) | **AI 协作总纲** —— 工具链规则、硬约束、当前进度与下一步（第 5 节）。新开对话先读这个 |
| [ReDiv_Online/CLAUDE.md](ReDiv_Online/CLAUDE.md) | 客户端技术文档 |
| [ReDiv_Server/README.md](ReDiv_Server/README.md) | 服务端技术文档 |

（`ReDiv_GM/` 没有自己的文档，说明在服务端技术文档的「本地 Web GM 工具」一节。）

外加一份长期交接记忆：
[ReDiv_Online/Docs/CN资源解包与还原工作流.md](ReDiv_Online/Docs/CN资源解包与还原工作流.md)
—— 国服 AA 下载、解包、复杂分类、NGUI、Spine 与 Shader 还原。**素材相关的新对话必须先读它。**

本文件（README）只讲**不会天天变**的东西：目录结构、架构特点、本机环境、几条常用命令。
**进度和功能清单不写在这里**，一律看 [CLAUDE.md](CLAUDE.md) 第 5 节 —— 同一件事写两处必然漂移。

> 仓库里其余的 `.md` **不是项目文档**，别拿它们推断设计：
> `ReDiv_Server/{CLAUDE,AGENTS}.md`、`ReDiv_Server/.github/copilot-instructions.md`、
> `ReDiv_Server/.cursor/rules/` 是 `spacetime init` 生成的 SpacetimeDB 官方 AI 规则（同一份内容的多个副本，勿手改）；
> `Assets/AVProVideo/`、`Assets/Live2D/` 下的是第三方插件许可证与说明；
> `Packages/com.clockworklabs.spacetimedbsdk/` 下除 `UPSTREAM.md`（**这份是我们自己写的**，
> 记录内嵌分叉的补丁和升级步骤）以外都是上游 SDK 自带文档。

## 项目结构

```
REDIV/                  ← 唯一的 git 仓库（分支 main）
├── ReDiv_Online/       客户端 —— Unity 6000.4.8f1 工程
│   ├── Assets/Scripts/Net/          网络层：连接 / 门面（账号·角色·城镇）/ 生成的绑定
│   ├── Assets/Scripts/Game/         游戏逻辑（含 UGUI 各界面、城镇背景）
│   ├── Assets/Scripts/Framework/    自用框架 XFramework（UI 系统 / 资源加载）
│   ├── Docs/                        长期交接文档
│   └── ExcelTool/LubanTools/        配置表源头（Excel + Luban）
├── ReDiv_Server/       服务端 —— SpacetimeDB 模块，C# 编译成 WebAssembly
│   └── spacetimedb/
│       ├── Auth/       账号系统
│       ├── Character/  角色系统
│       ├── Town/       城镇 / 世界时间 / 玩家状态（坐标·体力·钱包）
│       ├── Security/   口令哈希（自己写的，wasm 上 BCL crypto 不可用）
│       ├── Luban/      配置表代码 + vendored 运行时
│       └── Configs/    配置 bin 数据，以嵌入资源编进 wasm
├── ReDiv_GM/           本地 Web GM 控制台（Next.js 网页 + .NET 后端）
│   ├── app/            前端
│   ├── Server/         后端，固定监听 127.0.0.1:5168
│   └── data/           审计日志运行文件（已 gitignore）
├── .gitignore          只管根这一层；Unity / 服务端 / GM 的忽略在各自子目录里
└── .mcp.json           MCP for Unity 的注册（HTTP，指向 127.0.0.1:8848）
```

`REDIV/` 就是仓库根。2026-08-21 之前客户端和服务端是两个独立仓库，根目录这几个
共用文件不在任何版本库里、换机器拉不到，所以合并成了一个；两边的提交历史都保留了。

## 这套架构的特殊之处

**没有独立的游戏服务器进程。** `ReDiv_Server` 不是一个常驻的 .NET 服务，而是一个
**数据库模块**：C# 代码编译成 WebAssembly，被 `spacetime publish` 上传进 SpacetimeDB
数据库进程内部执行。

所以：

- 写数据只能通过 **Reducer**（模块导出的事务函数），客户端调用它
- 读数据只能通过**订阅**（公开表）或 **View**（私有表），Reducer 不返回数据
- 客户端与服务端的通信不是 HTTP/RPC，而是一条长连 WebSocket 上的订阅同步
- 客户端用的类型不是手写的，而是 `spacetime generate` 从模块 schema 生成的绑定

| | 客户端 | 服务端 |
|---|---|---|
| 目录 | `ReDiv_Online/` | `ReDiv_Server/` |
| 语言 | C#（Unity） | C#（编译成 WASM） |
| 运行位置 | 玩家机器 | SpacetimeDB 数据库进程内 |
| 入口 | `Assets/Scripts/Net/SpacetimeConnection.cs` | `spacetimedb/Module.cs` |
| 构建 | Unity Build / Addressables | `spacetime publish` |

## 本机环境

| 项目 | 值 |
|---|---|
| SpacetimeDB 服务端 | 2.8.2，Docker 容器 `spacetimedb` |
| 数据库地址 | `http://127.0.0.1:2383`（局域网 `http://192.168.10.226:2383`） |
| 数据库名 | `rediv` |
| `spacetime` CLI | 2.8.2，`%LOCALAPPDATA%\SpacetimeDB\spacetime.exe` |
| Unity | 6000.4.8f1 |
| .NET SDK | 10.0.400（服务端模块需要） |
| 私有 npm registry | `http://192.168.10.226:4873`（Verdaccio，scope `com.lumino` / `com.kyrylokuzyk`） |

CLI、数据库、Unity SDK 三者版本必须**严格对齐**在同一个版本号（现在都是 2.8.2）。
协议是 v2，跨版本容易出问题，升级时三个一起升。

## 快速上手

服务端改完代码后发布 + 重新生成客户端绑定：

```bash
cd ReDiv_Server && spacetime publish --yes && spacetime generate
```

看服务端日志：

```bash
spacetime logs rediv --follow
```

客户端改完 C# 后编译验证（编辑器要开着）：

```bash
cd ReDiv_Online && unity command recompile && unity command recompile_status
```

> `up_to_date` 不代表没错误，还要单独查控制台：
> `unity command get_console_logs --severity Error --limit 40`

改了配置表（Excel）后导出服务端配置 —— Unity 里走 `Tools > XFramework > 配置` 的
第 6 步，或命令行：

```bash
cd ReDiv_Online/ExcelTool/LubanTools/DataTables && ./gen_server.bat
```

配置是以嵌入资源编进 wasm 的，**导出完必须再 `spacetime publish`** 才生效。

起本地 GM 控制台（两个终端，浏览器开 `http://localhost:3000/`）：

```bash
cd ReDiv_GM/Server && dotnet run
```

```bash
cd ReDiv_GM && npm install && npm run dev
```

> ⚠️ 它用的是本机 SpacetimeDB **owner** 的 CLI 权限，所以只监听 `127.0.0.1`，
> **不要部署到公网** —— 那等于把数据库管理权交出去。

想真的跑一遍功能（不只是编译）：

```bash
cd ReDiv_Online && unity command editor_play
```

然后用 `unity command eval` 在运行中驱动，验完 `unity command editor_stop`。
细节见 [CLAUDE.md](CLAUDE.md) 第 2 节。
