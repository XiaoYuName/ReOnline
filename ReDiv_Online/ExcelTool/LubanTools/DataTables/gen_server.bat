@echo off
REM 生成**服务端**配置：C# 代码 + bin 数据，落进 SpacetimeDB 模块工程。
REM
REM 为什么和客户端用不同的 codeTarget：
REM   客户端用 cs-newtonsoft-json（反射 + 大依赖），服务端是 NativeAOT 裁剪过的 wasm，
REM   反射那套在里面用不了。cs-bin 生成的代码零反射（构造函数按顺序读 ByteBuf），AOT 安全。
REM 数据用 -d bin：模块里没有文件系统，.bytes 会以**嵌入资源**编进 wasm（见 StdbModule.csproj）。
REM
REM 只会导出 group 含 s 的表和字段（分组在 Defines/character.xml 里按字段标）。
REM 改完必须 spacetime publish，否则线上还是旧配置。

set LUBAN_DLL=..\Tools\Luban\Luban.dll
set CONF_ROOT=.
set SERVER_ROOT=..\..\..\..\ReDiv_Server\spacetimedb

dotnet %LUBAN_DLL% ^
  -t server ^
  -c cs-bin ^
  -d bin ^
  --conf %CONF_ROOT%\luban.conf ^
  -x outputCodeDir=%SERVER_ROOT%\Luban\Generated ^
  -x outputDataDir=%SERVER_ROOT%\Configs

pause
