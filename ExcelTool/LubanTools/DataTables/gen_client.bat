@echo off

set LUBAN_DLL=..\Tools\Luban\Luban.dll
set CONF_ROOT=.

dotnet %LUBAN_DLL% ^
  -t client ^
  -c cs-newtonsoft-json ^
  -d json ^
  --conf %CONF_ROOT%\luban.conf ^
  -x outputCodeDir=..\..\..\Assets\Scripts\XFramework\C#\Luban ^
  -x outputDataDir=..\..\..\Assets\AddressableAssets\Remote\Configs\LubanJson

pause