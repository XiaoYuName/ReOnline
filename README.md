# ReDiv

一款自研的在线联机游戏。美术素材参考公主连结风格，**玩法完全自研**，与公连有很大不同。

## 项目结构

```
REDIV/                  ← 唯一的 git 仓库（分支 main）
├── ReDiv_Online/       客户端 —— Unity 6000.4.8f1 工程
├── ReDiv_Server/       服务端 —— SpacetimeDB 模块，C# 编译成 WebAssembly
├── .gitignore          只管根这一层；Unity / 服务端的忽略在各自子目录里
├── README.md           本文件
├── CLAUDE.md           AI 协作总纲（新开对话先读这个）
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

## 相关文档

- [CLAUDE.md](CLAUDE.md) —— AI 协作总纲：工具链规则、硬约束
- [ReDiv_Online/CLAUDE.md](ReDiv_Online/CLAUDE.md) —— 客户端技术文档
- [ReDiv_Server/README.md](ReDiv_Server/README.md) —— 服务端技术文档
- [ReDiv_Server/CLAUDE.md](ReDiv_Server/CLAUDE.md) —— SpacetimeDB 2.8 官方 AI 规则（`spacetime init` 生成，勿手改）
