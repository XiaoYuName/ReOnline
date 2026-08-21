@echo off

REM Unity 编辑器已不再调用这个 bat ——「Tools > XFramework > 配置 > LuaConfig」里的
REM 第 1 步现在直接调 dotnet Luban.dll，参数取自 Assets/Editor/Luban/ConfigToolsSettings.asset。
REM 这个 bat 只留给不开 Unity 时手动导出用，改了输出目录记得两边一起改。

set LUBAN_DLL=..\Tools\Luban\Luban.dll
set CONF_ROOT=.

dotnet %LUBAN_DLL% ^
  -t client ^
  -c cs-newtonsoft-json ^
  -d json ^
  --conf %CONF_ROOT%\luban.conf ^
  -x outputCodeDir=..\..\..\Assets\Scripts\Game\Scripts\Luban ^
  -x outputDataDir=..\..\..\Assets\AddressableAssets\Remote\Configs\LubanJson

pause