# ReDiv

一款自研的在线联机游戏。美术素材参考公主连结风格，**玩法完全自研**，与公连有很大不同。

## 项目结构

```
REDIV/                  ← 唯一的 git 仓库（分支 main）
├── ReDiv_Online/       客户端 —— Unity 6000.4.8f1 工程
│   ├── Assets/Scripts/Net/          网络层：连接 / 账号门面 / 生成的绑定
│   ├── Assets/Scripts/Game/         游戏逻辑（含 UGUI 各界面）
│   ├── Assets/Scripts/Framework/    自用框架 XFramework（UI 系统 / 资源加载）
│   └── ExcelTool/LubanTools/        配置表源头（Excel + Luban）
├── ReDiv_Server/       服务端 —— SpacetimeDB 模块，C# 编译成 WebAssembly
│   └── spacetimedb/
│       ├── Auth/       账号系统
│       ├── Character/  角色系统
│       ├── Security/   口令哈希（自己写的，wasm 上 BCL crypto 不可用）
│       ├── Luban/      配置表代码 + vendored 运行时
│       └── Configs/    配置 bin 数据，以嵌入资源编进 wasm
├── .gitignore          只管根这一层；Unity / 服务端的忽略在各自子目录里
├── README.md           本文件
├── CLAUDE.md           AI 协作总纲（新开对话先读这个，进度看第 5 节）
└── .mcp.json           MCP for Unity 的注册（HTTP，指向 127.0.0.1:8848）
```

## 现在做到哪了

账号系统（注册 / 登录 / 顶号 / 免密重连）和版本校验两端都通了；
角色系统（多角色 / 创建 / 软删 / 选择，以及形态与觉醒）服务端完成、
**客户端选人界面还没写**；配置表通路（Excel → Luban → 编进 wasm）已打通，
角色配置只有一个角色、几个数值还是占位的。

详细清单和下一步见 [CLAUDE.md](CLAUDE.md) 第 5 节。

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
| 详细文档 | [ReDiv_Online/CLAUDE.md](ReDiv_Online/CLAUDE.md) | [ReDiv_Server/README.md](ReDiv_Server/README.md) |

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
cd ReDiv_Server && spacetime publish && spacetime generate
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

想真的跑一遍功能（不只是编译）：

```bash
cd ReDiv_Online && unity command editor_play
```

然后用 `unity command eval` 在运行中驱动，验完 `unity command editor_stop`。
细节见 [CLAUDE.md](CLAUDE.md) 第 2 节。

## 相关文档

- [CLAUDE.md](CLAUDE.md) —— AI 协作总纲：工具链规则、硬约束
- [ReDiv_Online/CLAUDE.md](ReDiv_Online/CLAUDE.md) —— 客户端技术文档
- [ReDiv_Online/Docs/CN资源解包与还原工作流.md](ReDiv_Online/Docs/CN资源解包与还原工作流.md) —— 国服 AA 下载、解包、复杂分类、NGUI、Spine 与 Shader 还原的完整交接记忆
- [ReDiv_Server/README.md](ReDiv_Server/README.md) —— 服务端技术文档
- [ReDiv_Server/CLAUDE.md](ReDiv_Server/CLAUDE.md) —— SpacetimeDB 2.8 官方 AI 规则（`spacetime init` 生成，勿手改）
