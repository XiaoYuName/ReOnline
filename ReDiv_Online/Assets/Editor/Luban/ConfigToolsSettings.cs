#if UNITY_EDITOR

using System.Collections.Generic;
using System.IO;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// ConfigTools「一键生成配置」流程的设置。
    ///
    /// 这些值以前散在编辑器看不见的地方，改不了也查不到：
    ///   - 第 1 步的输出目录写死在 ExcelTool/LubanTools/DataTables/gen_client.bat 的 -x 参数里。
    ///     它曾经指着旧工程的 Assets/Scripts/XFramework/C#/Luban，于是表 C# 全落到一个不存在的
    ///     目录里，而第 3 步在另一个目录扫表，结果 LubanManager.Generated.cs 永远是空的。
    ///   - 第 4 步 UIKeys 的输出路径写死在 UIKeysGenerator 的 const 里。
    ///
    /// 现在第 1 步由编辑器直接调 dotnet Luban.dll，参数全部取自这个资源，不再依赖 bat。
    /// 路径填**相对工程根目录**（Assets 的父目录）的路径，也接受绝对路径。
    /// </summary>
    [CreateAssetMenu(
        fileName = "ConfigToolsSettings",
        menuName = "XFramework/Luban/Config Tools Settings")]
    public class ConfigToolsSettings : SerializedScriptableObject
    {
        public const string DefaultAssetPath = "Assets/Editor/Luban/ConfigToolsSettings.asset";

        /// <summary>路径类字段被改动时触发，给 ConfigTools 窗口刷新列表和总览用。</summary>
        public static event System.Action Changed;

        // ---------------- 第 1 步：Luban 导出 ----------------

        [TitleGroup("第 1 步：Luban 导出")]
        [LabelText("dotnet 命令")]
        [InfoBox("PATH 里能找到 dotnet 就保持默认；找不到时填 dotnet.exe 的绝对路径。")]
        public string DotnetPath = "dotnet";

        [LabelText("Luban.dll 路径")]
        [InfoBox("Luban.dll 不存在，导出会直接失败。", InfoMessageType.Error, nameof(IsLubanDllMissing))]
        [OnValueChanged(nameof(RaiseChanged))]
        public string LubanDllPath = "ExcelTool/LubanTools/Tools/Luban/Luban.dll";

        [LabelText("luban.conf 路径")]
        [InfoBox("luban.conf 不存在，导出会直接失败。", InfoMessageType.Error, nameof(IsLubanConfMissing))]
        [InfoBox("conf 里的 schemaFiles / dataDir 是相对它自己所在目录解析的，所以执行 Luban 时的工作目录会设成它所在的目录。")]
        [OnValueChanged(nameof(RaiseChanged))]
        public string LubanConfPath = "ExcelTool/LubanTools/DataTables/luban.conf";

        [LabelText("生成目标 -t")]
        public string Target = "client";

        [LabelText("代码格式 -c")]
        public string CodeTarget = "cs-newtonsoft-json";

        [LabelText("数据格式 -d")]
        public string DataTarget = "json";

        [LabelText("表 C# 输出目录")]
        [FolderPath]
        [InfoBox("必须和 LubanManagerGeneratorConfig 的「Luban C#代码目录」一致，否则第 3 步扫不到表。",
            InfoMessageType.Warning, nameof(IsOutputCodeDirMismatched))]
        [OnValueChanged(nameof(RaiseChanged))]
        public string OutputCodeDir = "Assets/Scripts/Game/Scripts/Luban";

        [LabelText("Json 数据输出目录")]
        [FolderPath]
        [OnValueChanged(nameof(RaiseChanged))]
        public string OutputDataDir = "Assets/AddressableAssets/Remote/Configs/LubanJson";

        // ---------------- 服务端（SpacetimeDB 模块）----------------
        // 和客户端读同一份 Excel，但**必须用不同的 codeTarget**：服务端是 NativeAOT 裁剪过的
        // wasm，cs-newtonsoft-json 那套反射在里面用不了；cs-bin 生成的代码零反射。
        // 只会导出 group 含 s 的表和字段（分组在 Defines/character.xml 里按字段标）。
        // bin 数据以嵌入资源编进 wasm，所以导出完还要 spacetime publish 才生效。

        [LabelText("服务端生成目标 -t")]
        public string ServerTarget = "server";

        [LabelText("服务端代码格式 -c")]
        public string ServerCodeTarget = "cs-bin";

        [LabelText("服务端数据格式 -d")]
        public string ServerDataTarget = "bin";

        [LabelText("服务端表 C# 输出目录")]
        [OnValueChanged(nameof(RaiseChanged))]
        public string ServerOutputCodeDir = "../ReDiv_Server/spacetimedb/Luban/Generated";

        [LabelText("服务端 bin 数据输出目录")]
        [OnValueChanged(nameof(RaiseChanged))]
        public string ServerOutputDataDir = "../ReDiv_Server/spacetimedb/Configs";

        [LabelText("额外 -x 参数")]
        [InfoBox("每条形如 key=value，各自展开成一个 -x 参数。一般不用填。")]
        [ListDrawerSettings(ShowFoldout = true, DraggableItems = false)]
        public List<string> ExtraXArgs = new List<string>();

        [LabelText("超时（秒）")]
        [MinValue(10)]
        public int TimeoutSeconds = 300;

        // ---------------- Excel 目录 ----------------

        [TitleGroup("Excel 目录")]
        [LabelText("Excel 目录")]
        [InfoBox("只决定 ConfigTools 窗口里列出哪些 Excel，不参与导出 —— Luban 读的是 luban.conf 里的 dataDir。")]
        [InfoBox("目录不存在。", InfoMessageType.Warning, nameof(IsXlsxFolderMissing))]
        [FolderPath]
        [OnValueChanged(nameof(RaiseChanged))]
        public string XlsxFolder = "ExcelTool/LubanTools/DataTables/Datas";

        // ---------------- 第 4 步：UIKeys ----------------

        [TitleGroup("第 4 步：UIKeys")]
        [LabelText("输出路径")]
        [Sirenix.OdinInspector.FilePath(Extensions = "cs")]
        [OnValueChanged(nameof(RaiseChanged))]
        public string UIKeysOutputPath = "Assets/Scripts/Game/Scripts/AddressableKeys/UIKeys.cs";

        [LabelText("类名")]
        public string UIKeysClassName = "UIKeys";

        [LabelText("命名空间")]
        public string UIKeysNamespace = "XFramework";

        // ---------------- 工具方法 ----------------

        /// <summary>相对工程根目录的路径转绝对路径；已经是绝对路径的原样返回。</summary>
        public static string ToAbsolute(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string normalized = path.Replace("\\", "/");

            if (Path.IsPathRooted(normalized))
            {
                return normalized;
            }

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;

            return string.IsNullOrEmpty(projectRoot)
                ? normalized
                : Path.Combine(projectRoot, normalized).Replace("\\", "/");
        }

        public static ConfigToolsSettings LoadOrCreate()
        {
            var settings = AssetDatabase.LoadAssetAtPath<ConfigToolsSettings>(DefaultAssetPath);

            if (settings != null)
            {
                return settings;
            }

            string directory = Path.GetDirectoryName(DefaultAssetPath);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            settings = CreateInstance<ConfigToolsSettings>();
            AssetDatabase.CreateAsset(settings, DefaultAssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ConfigTools] 设置资源不存在，已按默认值创建: {DefaultAssetPath}");

            return settings;
        }

        private void RaiseChanged()
        {
            Changed?.Invoke();
        }

        private bool IsLubanDllMissing()
        {
            return !File.Exists(ToAbsolute(LubanDllPath));
        }

        private bool IsLubanConfMissing()
        {
            return !File.Exists(ToAbsolute(LubanConfPath));
        }

        private bool IsXlsxFolderMissing()
        {
            return !Directory.Exists(ToAbsolute(XlsxFolder));
        }

        /// <summary>
        /// 第 1 步写到哪、第 3 步就得从哪扫。两边不一致时给个显式警告，别再靠人去对。
        /// </summary>
        private bool IsOutputCodeDirMismatched()
        {
            var managerConfig = AssetDatabase.LoadAssetAtPath<LubanManagerGeneratorConfig>(
                LubanManagerGeneratorWindow.DefaultConfigPath);

            if (managerConfig == null || string.IsNullOrWhiteSpace(managerConfig.lubanCodeDirectory))
            {
                return false;
            }

            string mine = ToAbsolute(OutputCodeDir).TrimEnd('/');
            string theirs = ToAbsolute(managerConfig.lubanCodeDirectory).TrimEnd('/');

            return !mine.Equals(theirs, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}

#endif
