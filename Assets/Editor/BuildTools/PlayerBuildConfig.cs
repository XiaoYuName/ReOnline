using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// 一键出包(Player Build)配置：版本号、包名、Windows 打包参数、输出规则、前置流程。
    /// 本项目固定为 Windows64 平台。
    /// </summary>
    [CreateAssetMenu(fileName = "PlayerBuildConfig", menuName = "Configs/Project/PlayerBuildConfig")]
    public class PlayerBuildConfig : SerializedScriptableObject
    {
        public const string ConfigPath = "Assets/Editor/BuildTools/PlayerBuildConfig.asset";
        private const string VersionPattern = @"^\d+\.\d+(\.\d+)?$";

        #region 基础信息

        [TabGroup("Build", "基础信息")]
        [BoxGroup("Build/基础信息/产品")]
        [LabelText("产品名称")]
        [Required]
        public string ProductName = "剧情游戏";

        [BoxGroup("Build/基础信息/产品")]
        [LabelText("公司名称")]
        public string CompanyName = "LuminoInc";

        [BoxGroup("Build/基础信息/产品")]
        [LabelText("包名")]
        [Required]
        public string BundleIdentifier = "com.LuminoInc.AFramework";

        [BoxGroup("Build/基础信息/产品")]
        [LabelText("渠道/环境")]
        [EnumToggleButtons]
        public BuildChannel Channel = BuildChannel.Development;

        [BoxGroup("Build/基础信息/版本")]
        [LabelText("版本号")]
        [ValidateInput(nameof(ValidateVersion), "版本号格式应为 1.0 或 1.0.0")]
        [HorizontalGroup("Build/基础信息/版本/Row", 0.55f)]
        public string Version = "1.0.0";

        [HorizontalGroup("Build/基础信息/版本/Row")]
        [Button("+ 修订号", ButtonSizes.Medium)]
        private void IncreasePatch()
        {
            Version = BumpVersion(2);
        }

        [HorizontalGroup("Build/基础信息/版本/Row")]
        [Button("+ 次版本", ButtonSizes.Medium)]
        private void IncreaseMinor()
        {
            Version = BumpVersion(1);
        }

        [BoxGroup("Build/基础信息/版本")]
        [LabelText("内部版本号(BuildNumber)")]
        [MinValue(1)]
        [InfoBox("同一版本重复出包时递增，写入 version.json 与输出文件名。")]
        public int VersionCode = 1;

        [BoxGroup("Build/基础信息/版本")]
        [LabelText("打包后自动递增内部版本号")]
        public bool AutoIncreaseVersionCode = true;

        [BoxGroup("Build/基础信息/同步")]
        [Button("从 ProjectSettings 读取当前设置", ButtonSizes.Large)]
        [GUIColor(0.45f, 0.7f, 1f)]
        private void SyncFromProjectSettings()
        {
            ProductName = PlayerSettings.productName;
            CompanyName = PlayerSettings.companyName;
            Version = PlayerSettings.bundleVersion;
            BundleIdentifier = PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone);
            EditorUtility.SetDirty(this);
        }

        #endregion

        #region 打包设置

        [TabGroup("Build", "打包设置")]
        [BoxGroup("Build/打包设置/场景")]
        [LabelText("使用 Build Settings 中的场景")]
        public bool UseEditorBuildSettingsScenes = true;

        [BoxGroup("Build/打包设置/场景")]
        [LabelText("场景列表")]
        [HideIf(nameof(UseEditorBuildSettingsScenes))]
        [ListDrawerSettings(ShowFoldout = true)]
        [AssetsOnly]
        public List<SceneAsset> Scenes = new List<SceneAsset>();

        [BoxGroup("Build/打包设置/脚本")]
        [LabelText("脚本后端")]
        [EnumToggleButtons]
        public ScriptBackend ScriptingBackend = ScriptBackend.IL2CPP;

        [BoxGroup("Build/打包设置/脚本")]
        [LabelText("IL2CPP 编译配置")]
        [EnumToggleButtons]
        [ShowIf(nameof(IsIL2Cpp))]
        public Il2CppCompilerConfiguration Il2CppConfiguration = Il2CppCompilerConfiguration.Release;

        [BoxGroup("Build/打包设置/脚本")]
        [LabelText("代码裁剪等级")]
        [EnumToggleButtons]
        [ShowIf(nameof(IsIL2Cpp))]
        [InfoBox("IL2CPP 下 Unity 会强制至少 Minimal 裁剪，没有 Disabled 选项。")]
        public Il2CppStrippingLevel Il2CppStripping = Il2CppStrippingLevel.Low;

        [BoxGroup("Build/打包设置/脚本")]
        [LabelText("代码裁剪等级")]
        [EnumToggleButtons]
        [HideIf(nameof(IsIL2Cpp))]
        public ManagedStrippingLevel MonoStripping = ManagedStrippingLevel.Disabled;

        [BoxGroup("Build/打包设置/宏定义")]
        [LabelText("追加渠道宏")]
        [InfoBox("勾选后会按渠道追加 BUILD_DEV / BUILD_TEST / BUILD_RELEASE 宏。")]
        public bool AppendChannelDefine = true;

        [BoxGroup("Build/打包设置/宏定义")]
        [LabelText("额外宏定义")]
        [ListDrawerSettings(ShowFoldout = true)]
        public List<string> ExtraDefineSymbols = new List<string>();

        [BoxGroup("Build/打包设置/调试")]
        [LabelText("Development Build")]
        public bool DevelopmentBuild;

        [BoxGroup("Build/打包设置/调试")]
        [LabelText("允许脚本调试")]
        [ShowIf(nameof(DevelopmentBuild))]
        public bool AllowDebugging;

        [BoxGroup("Build/打包设置/调试")]
        [LabelText("自动连接 Profiler")]
        [ShowIf(nameof(DevelopmentBuild))]
        public bool ConnectProfiler;

        [BoxGroup("Build/打包设置/调试")]
        [LabelText("深度 Profiling")]
        [ShowIf(nameof(DevelopmentBuild))]
        public bool DeepProfiling;

        [BoxGroup("Build/打包设置/其它")]
        [LabelText("压缩方式")]
        [EnumToggleButtons]
        public BuildCompressionMode Compression = BuildCompressionMode.Default;

        [BoxGroup("Build/打包设置/其它")]
        [LabelText("全屏模式")]
        [EnumToggleButtons]
        public FullScreenMode FullScreenMode = FullScreenMode.FullScreenWindow;

        [BoxGroup("Build/打包设置/其它")]
        [LabelText("默认分辨率")]
        public Vector2Int DefaultResolution = new Vector2Int(1920, 1080);

        [BoxGroup("Build/打包设置/其它")]
        [LabelText("清理构建缓存")]
        public bool CleanBuildCache;

        [BoxGroup("Build/打包设置/其它")]
        [LabelText("打包前强制保存场景与资源")]
        public bool SaveAssetsBeforeBuild = true;

        #endregion

        #region 前置流程

        [TabGroup("Build", "前置流程")]
        [BoxGroup("Build/前置流程/Addressable")]
        [LabelText("打包前执行 Addressable 打标")]
        public bool MarkAddressables;

        [BoxGroup("Build/前置流程/Addressable")]
        [LabelText("Addressable 打标配置")]
        [ShowIf(nameof(MarkAddressables))]
        [AssetsOnly]
        [InfoBox("留空则使用 Assets/Editor/AddressableTools/BuildConfiguration.asset。")]
        public BuildConfiguration AddressableMarkConfig;

        [BoxGroup("Build/前置流程/Addressable")]
        [LabelText("打包前构建 Addressable 资源")]
        public bool BuildAddressableContent = true;

        [BoxGroup("Build/前置流程/Addressable")]
        [LabelText("先清理 Addressable 构建缓存")]
        [ShowIf(nameof(BuildAddressableContent))]
        public bool CleanAddressableBuildCache;

        [BoxGroup("Build/前置流程/Addressable")]
        [LabelText("Addressable Profile")]
        [ShowIf(nameof(BuildAddressableContent))]
        [InfoBox("留空则使用 Addressable 当前激活的 Profile。")]
        public string AddressableProfileName;

        [BoxGroup("Build/前置流程/Addressable")]
        [LabelText("远程资源地址(RemoteLoadPath)")]
        [ShowIf(nameof(BuildAddressableContent))]
        [InfoBox("留空则不修改 Profile 中的远程地址。")]
        public string RemoteLoadUrl;

        #endregion

        #region 输出

        [TabGroup("Build", "输出")]
        [BoxGroup("Build/输出/路径")]
        [LabelText("输出根目录")]
        [FolderPath]
        [Required]
        public string OutputRoot = "Build/Windows64";

        [BoxGroup("Build/输出/路径")]
        [LabelText("文件名模板")]
        [InfoBox("可用占位符：{product} {version} {code} {channel} {date} {time}")]
        public string FileNameFormat = "{product}_{channel}_v{version}.{code}_{date}";

        [BoxGroup("Build/输出/路径")]
        [LabelText("exe 文件名")]
        [InfoBox("留空则与输出目录同名。")]
        public string ExecutableName;

        [BoxGroup("Build/输出/路径")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("输出预览")]
        private string OutputPreview => GetOutputPath();

        [BoxGroup("Build/输出/收尾")]
        [LabelText("生成版本信息文件(version.json)")]
        public bool GenerateVersionFile = true;

        [BoxGroup("Build/输出/收尾")]
        [LabelText("删除 *_BurstDebugInformation 等调试目录")]
        public bool DeleteDebugFolders = true;

        [BoxGroup("Build/输出/收尾")]
        [LabelText("打包完成后打开目录")]
        public bool RevealInExplorer = true;

        [BoxGroup("Build/输出/收尾")]
        [LabelText("打包完成后运行")]
        public bool RunAfterBuild;

        #endregion

        #region 派生数据

        public bool IsIL2Cpp => ScriptingBackend == ScriptBackend.IL2CPP;

        /// <summary>
        /// 写入 PlayerSettings 用的脚本后端。
        /// </summary>
        public ScriptingImplementation ScriptingImplementation =>
            ScriptingBackend == ScriptBackend.Mono2x ? ScriptingImplementation.Mono2x : ScriptingImplementation.IL2CPP;

        /// <summary>
        /// 写入 PlayerSettings 用的裁剪等级，按脚本后端取对应字段。
        /// </summary>
        public ManagedStrippingLevel StrippingLevel =>
            IsIL2Cpp ? (ManagedStrippingLevel)Il2CppStripping : MonoStripping;

        public BuildTarget BuildTarget => UnityEditor.BuildTarget.StandaloneWindows64;

        public BuildTargetGroup BuildTargetGroup => UnityEditor.BuildTargetGroup.Standalone;

        public NamedBuildTarget NamedBuildTarget => UnityEditor.Build.NamedBuildTarget.Standalone;

        /// <summary>
        /// 最终参与打包的场景路径。
        /// </summary>
        public string[] GetScenePaths()
        {
            if (UseEditorBuildSettingsScenes)
            {
                return EditorBuildSettings.scenes
                    .Where(scene => scene.enabled)
                    .Select(scene => scene.path)
                    .ToArray();
            }

            return Scenes
                .Where(scene => scene != null)
                .Select(AssetDatabase.GetAssetPath)
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct()
                .ToArray();
        }

        /// <summary>
        /// 渠道宏 + 额外宏。
        /// </summary>
        public List<string> GetDefineSymbols()
        {
            List<string> symbols = ExtraDefineSymbols
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                .Select(symbol => symbol.Trim())
                .ToList();

            if (AppendChannelDefine)
            {
                symbols.Add(GetChannelDefine());
            }

            return symbols.Distinct().ToList();
        }

        public string GetChannelDefine()
        {
            switch (Channel)
            {
                case BuildChannel.Development:
                    return "BUILD_DEV";
                case BuildChannel.Testing:
                    return "BUILD_TEST";
                default:
                    return "BUILD_RELEASE";
            }
        }

        /// <summary>
        /// 不含扩展名的输出文件夹名。
        /// </summary>
        public string GetFolderName()
        {
            string format = string.IsNullOrWhiteSpace(FileNameFormat)
                ? "{product}_v{version}"
                : FileNameFormat;

            DateTime now = DateTime.Now;
            string fileName = format
                .Replace("{product}", ProductName)
                .Replace("{version}", Version)
                .Replace("{code}", VersionCode.ToString())
                .Replace("{channel}", Channel.ToString())
                .Replace("{date}", now.ToString("yyyyMMdd"))
                .Replace("{time}", now.ToString("HHmmss"));

            return SanitizeFileName(fileName);
        }

        /// <summary>
        /// 本次出包的目录：输出根目录 / 版本文件夹。
        /// </summary>
        public string GetOutputDirectory()
        {
            string root = string.IsNullOrWhiteSpace(OutputRoot) ? "Build/Windows64" : OutputRoot;
            return CombinePath(root, GetFolderName());
        }

        /// <summary>
        /// BuildPipeline 需要的 locationPathName。
        /// </summary>
        public string GetOutputPath()
        {
            string folderName = GetFolderName();
            string executableName = string.IsNullOrWhiteSpace(ExecutableName)
                ? folderName
                : SanitizeFileName(ExecutableName);

            return CombinePath(GetOutputDirectory(), executableName + ".exe");
        }

        public BuildOptions GetBuildOptions()
        {
            BuildOptions options = BuildOptions.None;

            if (DevelopmentBuild)
            {
                options |= BuildOptions.Development;

                if (AllowDebugging)
                {
                    options |= BuildOptions.AllowDebugging;
                }

                if (ConnectProfiler)
                {
                    options |= BuildOptions.ConnectWithProfiler;
                }

                if (DeepProfiling)
                {
                    options |= BuildOptions.EnableDeepProfilingSupport;
                }
            }

            switch (Compression)
            {
                case BuildCompressionMode.Lz4:
                    options |= BuildOptions.CompressWithLz4;
                    break;
                case BuildCompressionMode.Lz4HC:
                    options |= BuildOptions.CompressWithLz4HC;
                    break;
            }

            if (CleanBuildCache)
            {
                options |= BuildOptions.CleanBuildCache;
            }

            return options;
        }

        /// <summary>
        /// 打包前校验，返回所有错误信息。
        /// </summary>
        public List<string> Validate()
        {
            List<string> errors = new List<string>();

            if (string.IsNullOrWhiteSpace(ProductName))
            {
                errors.Add("产品名称为空。");
            }

            if (!ValidateVersion(Version))
            {
                errors.Add($"版本号格式非法: {Version}");
            }

            if (string.IsNullOrWhiteSpace(BundleIdentifier) || !BundleIdentifier.Contains('.'))
            {
                errors.Add($"包名非法: {BundleIdentifier}");
            }

            if (GetScenePaths().Length == 0)
            {
                errors.Add("没有可打包的场景。");
            }

            if (string.IsNullOrWhiteSpace(OutputRoot))
            {
                errors.Add("输出根目录为空。");
            }

            if (DefaultResolution.x <= 0 || DefaultResolution.y <= 0)
            {
                errors.Add($"默认分辨率非法: {DefaultResolution.x}x{DefaultResolution.y}");
            }

            return errors;
        }

        #endregion

        #region 资产读写

        public static PlayerBuildConfig LoadOrCreate()
        {
            PlayerBuildConfig config = AssetDatabase.LoadAssetAtPath<PlayerBuildConfig>(ConfigPath);
            if (config != null)
            {
                return config;
            }

            string directory = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            config = CreateInstance<PlayerBuildConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"已创建出包配置: {ConfigPath}");
            return config;
        }

        #endregion

        #region 工具方法

        private bool ValidateVersion(string version)
        {
            return !string.IsNullOrWhiteSpace(version) && Regex.IsMatch(version.Trim(), VersionPattern);
        }

        private string BumpVersion(int partIndex)
        {
            if (!ValidateVersion(Version))
            {
                return Version;
            }

            List<int> parts = Version.Trim().Split('.').Select(int.Parse).ToList();
            while (parts.Count <= partIndex)
            {
                parts.Add(0);
            }

            parts[partIndex]++;
            for (int index = partIndex + 1; index < parts.Count; index++)
            {
                parts[index] = 0;
            }

            return string.Join(".", parts);
        }

        private static string SanitizeFileName(string fileName)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            string sanitized = new string(fileName.Select(character => invalidChars.Contains(character) ? '_' : character).ToArray());
            return sanitized.Trim();
        }

        private static string CombinePath(params string[] parts)
        {
            return Path.Combine(parts).Replace("\\", "/");
        }

        public static string GetAbsolutePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string normalizedPath = path.Replace("\\", "/");
            if (Path.IsPathRooted(normalizedPath))
            {
                return normalizedPath;
            }

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            return string.IsNullOrEmpty(projectRoot)
                ? normalizedPath
                : Path.Combine(projectRoot, normalizedPath).Replace("\\", "/");
        }

        #endregion
    }

    /// <summary>
    /// 只暴露 Windows 平台实际可用的脚本后端。
    /// 枚举值与 ScriptingImplementation 对齐（Mono2x = 0，IL2CPP = 1）。
    /// </summary>
    public enum ScriptBackend
    {
        [LabelText("Mono")]
        Mono2x = 0,

        [LabelText("IL2CPP")]
        IL2CPP = 1
    }

    /// <summary>
    /// IL2CPP 可用的裁剪等级（没有 Disabled）。
    /// 枚举值与 ManagedStrippingLevel 对齐，可直接强转。
    /// </summary>
    public enum Il2CppStrippingLevel
    {
        [LabelText("Minimal")]
        Minimal = 4,

        [LabelText("Low")]
        Low = 1,

        [LabelText("Medium")]
        Medium = 2,

        [LabelText("High")]
        High = 3
    }

    public enum BuildChannel
    {
        [LabelText("开发")]
        Development,

        [LabelText("测试")]
        Testing,

        [LabelText("正式")]
        Release
    }

    public enum BuildCompressionMode
    {
        [LabelText("默认")]
        Default,

        [LabelText("LZ4")]
        Lz4,

        [LabelText("LZ4HC")]
        Lz4HC
    }
}
