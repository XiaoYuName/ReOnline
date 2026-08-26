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
    /// 一键出包(Player Build)配置：目标平台、版本号、包名、打包参数、输出规则、前置流程。
    /// 支持 Windows64 与 Android 两个平台，平台相关的设置各自成组。
    ///
    /// ⚠️ 版本号以本配置为准。出包时会由 <see cref="PlayerBuildVersionSync"/> 写回
    /// ProjectSettings 和所有 Build Profile 快照，不要再去那几处手改。
    /// </summary>
    [CreateAssetMenu(fileName = "PlayerBuildConfig", menuName = "Configs/Project/PlayerBuildConfig")]
    public class PlayerBuildConfig : SerializedScriptableObject
    {
        public const string ConfigPath = "Assets/Editor/BuildTools/PlayerBuildConfig.asset";
        private const string VersionPattern = @"^\d+\.\d+(\.\d+)?$";

        /// <summary>Android 官方支持的最低 API 等级（6000.4.8f1 实测：低于 25 编译期就报废弃错误）。</summary>
        private const int MinimumSupportedAndroidApi = 25;

        #region 基础信息

        [TabGroup("Build", "基础信息")]
        [BoxGroup("Build/基础信息/平台")]
        [LabelText("目标平台")]
        [EnumToggleButtons]
        [OnValueChanged(nameof(OnPlatformChanged))]
        [InfoBox("切换平台后，输出路径、脚本后端等设置会按新平台的那一组生效。")]
        public BuildPlatform Platform = BuildPlatform.Windows64;

        [BoxGroup("Build/基础信息/平台")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("编辑器当前平台")]
        [InfoBox("编辑器平台和目标平台不一致，打包时会先切换（资源要按新平台重新导入，可能很慢）。",
            InfoMessageType.Warning,
            VisibleIf = nameof(NeedsPlatformSwitch))]
        private string ActiveEditorPlatform => EditorUserBuildSettings.activeBuildTarget.ToString();

        [BoxGroup("Build/基础信息/产品")]
        [LabelText("产品名称")]
        [Required]
        public string ProductName = "ReDiv";

        [BoxGroup("Build/基础信息/产品")]
        [LabelText("公司名称")]
        public string CompanyName = "LuminoInc";

        [BoxGroup("Build/基础信息/产品")]
        [LabelText("包名")]
        [Required]
        public string BundleIdentifier = "com.LuminoInc.ReDiv";

        [BoxGroup("Build/基础信息/产品")]
        [LabelText("渠道/环境")]
        [EnumToggleButtons]
        public BuildChannel Channel = BuildChannel.Development;

        [BoxGroup("Build/基础信息/版本")]
        [LabelText("版本号")]
        [ValidateInput(nameof(ValidateVersion), "版本号格式应为 1.0 或 1.0.0")]
        [HorizontalGroup("Build/基础信息/版本/Row", 0.55f)]
        [InfoBox("这里就是客户端版本号的唯一来源：打包时写进 ProjectSettings 与全部 Build Profile 快照，" +
                 "游戏里 Application.version 读到的、连服务器校验用的都是它。\n" +
                 "⚠️ 服务端 ReDiv_Server/spacetimedb/Version.cs 的 ServerVersion 要跟着一起改并 publish，" +
                 "两边不一致客户端会弹窗并禁止登录。")]
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
        [InfoBox("同一版本重复出包时递增，写入 version.json 与输出文件名。Android 下它就是 bundleVersionCode，" +
                 "上架时必须比上一个包大，否则应用商店会拒。")]
        public int VersionCode = 1;

        [BoxGroup("Build/基础信息/版本")]
        [LabelText("打包后自动递增内部版本号")]
        public bool AutoIncreaseVersionCode = true;

        [BoxGroup("Build/基础信息/版本")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("版本号一致性")]
        private string VersionSyncState
        {
            get
            {
                List<string> mismatches = PlayerBuildVersionSync.FindMismatches(this);
                return mismatches.Count == 0
                    ? "一致（ProjectSettings 与 Build Profile 都是这个值）"
                    : "不一致：" + string.Join("；", mismatches) + "  —— 打包时会按本配置改回来";
            }
        }

        [BoxGroup("Build/基础信息/同步")]
        [Button("把版本号写回 ProjectSettings 与 Build Profiles", ButtonSizes.Large)]
        [GUIColor(0.45f, 0.7f, 1f)]
        private void PushVersionToProjectSettings()
        {
            List<string> steps = PlayerBuildVersionSync.Apply(this);
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog(
                "同步版本号",
                steps.Count == 0 ? "本来就是一致的，没有需要改的地方。" : string.Join("\n", steps.Select(step => "· " + step)),
                "确定");
        }

        [BoxGroup("Build/基础信息/同步")]
        [Button("从 ProjectSettings 读取当前设置", ButtonSizes.Medium)]
        [PropertyTooltip("会连版本号一起覆盖回来。只想改产品名时别按。")]
        private void SyncFromProjectSettings()
        {
            ProductName = PlayerSettings.productName;
            CompanyName = PlayerSettings.companyName;
            Version = PlayerSettings.bundleVersion;
            BundleIdentifier = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget);
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
        [InfoBox("Android 的 ARM64 必须用 IL2CPP，Mono 只能出 ARMv7 包。",
            InfoMessageType.Warning,
            VisibleIf = nameof(IsAndroidMonoWithArm64))]
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
        [ShowIf(nameof(IsWindows))]
        public FullScreenMode FullScreenMode = FullScreenMode.FullScreenWindow;

        [BoxGroup("Build/打包设置/其它")]
        [LabelText("默认分辨率")]
        [ShowIf(nameof(IsWindows))]
        public Vector2Int DefaultResolution = new Vector2Int(1920, 1080);

        [BoxGroup("Build/打包设置/其它")]
        [LabelText("清理构建缓存")]
        public bool CleanBuildCache;

        [BoxGroup("Build/打包设置/其它")]
        [LabelText("打包前强制保存场景与资源")]
        public bool SaveAssetsBeforeBuild = true;

        #endregion

        #region Android

        [TabGroup("Build", "Android")]
        [BoxGroup("Build/Android/包体")]
        [LabelText("产物格式")]
        [EnumToggleButtons]
        [InfoBox("目标平台不是 Android，这一页的设置这次打包不会用到。",
            InfoMessageType.Info,
            VisibleIf = nameof(IsWindows))]
        public AndroidPackageFormat PackageFormat = AndroidPackageFormat.Apk;

        [BoxGroup("Build/Android/包体")]
        [LabelText("导出 Gradle 工程（不直接出包）")]
        [InfoBox("勾选后输出的是一个 Android Studio 工程目录，不是 apk/aab。要在 Android Studio 里接 SDK 时才用。")]
        public bool ExportGradleProject;

        [BoxGroup("Build/Android/包体")]
        [LabelText("CPU 架构")]
        [InfoBox("上架国内外应用商店都要求 64 位，所以至少要勾 ARM64。ARM64 必须配 IL2CPP。")]
        public AndroidArchitecture Architectures = AndroidArchitecture.ARM64;

        [BoxGroup("Build/Android/包体")]
        [LabelText("按架构分开出包")]
        [ShowIf(nameof(IsApkFormat))]
        [PropertyTooltip("勾选后每个架构出一个独立 apk，体积小但要分发多个文件。")]
        public bool BuildApkPerCpuArchitecture;

        [BoxGroup("Build/Android/包体")]
        [LabelText("拆分应用二进制(Play Asset Delivery)")]
        [ShowIf(nameof(IsAppBundleFormat))]
        public bool SplitApplicationBinary;

        [BoxGroup("Build/Android/系统")]
        [LabelText("最低 SDK 版本")]
        public AndroidSdkVersions MinSdkVersion = AndroidSdkVersions.AndroidApiLevel25;

        [BoxGroup("Build/Android/系统")]
        [LabelText("目标 SDK 版本")]
        [InfoBox("Auto = 用当前编辑器支持的最高版本。上架前按商店要求钉死一个具体值。")]
        public AndroidSdkVersions TargetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

        [BoxGroup("Build/Android/系统")]
        [LabelText("屏幕方向")]
        [EnumToggleButtons]
        public UIOrientation Orientation = UIOrientation.LandscapeLeft;

        [BoxGroup("Build/Android/系统")]
        [LabelText("强制申请联网权限")]
        [InfoBox("本项目是联网游戏，必须为真 —— 关掉的话包里没有 INTERNET 权限，真机连不上服务器。")]
        public bool ForceInternetPermission = true;

        [BoxGroup("Build/Android/渲染")]
        [LabelText("纹理压缩格式")]
        [EnumToggleButtons]
        [InfoBox("ASTC 是现代安卓机的通用选择；ETC2 兼容更老的机器但同体积画质差一档。")]
        public AndroidTextureFormat TextureFormat = AndroidTextureFormat.ASTC;

        [BoxGroup("Build/Android/渲染")]
        [LabelText("优化帧步调(Frame Pacing)")]
        public bool OptimizedFramePacing = true;

        [BoxGroup("Build/Android/发布")]
        [LabelText("Release 包做代码混淆/压缩(R8)")]
        [PropertyTooltip("会明显拖慢打包，开发期不用开。")]
        public bool MinifyRelease;

        [BoxGroup("Build/Android/发布")]
        [LabelText("生成符号表")]
        [EnumToggleButtons]
        [PropertyTooltip("上传应用商店做崩溃还原用。IL2CPP 下才有意义，会额外产出一个 symbols.zip。")]
        public AndroidSymbolLevel SymbolLevel = AndroidSymbolLevel.None;

        [BoxGroup("Build/Android/签名")]
        [LabelText("使用自定义 Keystore")]
        [InfoBox("不勾就用 Unity 的调试签名 —— 只能自己装着测，不能上架，而且换机器出的包签名不同、" +
                 "会被安卓当成两个应用（覆盖安装会失败）。")]
        public bool UseCustomKeystore;

        [BoxGroup("Build/Android/签名")]
        [LabelText("Keystore 文件")]
        [ShowIf(nameof(UseCustomKeystore))]
        [Sirenix.OdinInspector.FilePath(Extensions = "keystore,jks", AbsolutePath = true)]
        public string KeystorePath;

        [BoxGroup("Build/Android/签名")]
        [LabelText("Key Alias")]
        [ShowIf(nameof(UseCustomKeystore))]
        public string KeyaliasName;

        [BoxGroup("Build/Android/签名")]
        [ShowInInspector]
        [LabelText("Keystore 口令")]
        [ShowIf(nameof(UseCustomKeystore))]
        [InfoBox("两个口令存在本机 EditorPrefs 里，**不写进配置资产、不进 git**。换机器要重填。")]
        public string KeystorePassword
        {
            get => EditorPrefs.GetString(KeystorePasswordKey, string.Empty);
            set => EditorPrefs.SetString(KeystorePasswordKey, value ?? string.Empty);
        }

        [BoxGroup("Build/Android/签名")]
        [ShowInInspector]
        [LabelText("Key Alias 口令")]
        [ShowIf(nameof(UseCustomKeystore))]
        public string KeyaliasPassword
        {
            get => EditorPrefs.GetString(KeyaliasPasswordKey, string.Empty);
            set => EditorPrefs.SetString(KeyaliasPasswordKey, value ?? string.Empty);
        }

        [BoxGroup("Build/Android/收尾")]
        [LabelText("打包完成后安装到已连接设备")]
        [HideIf(nameof(IsAppBundleFormat))]
        [InfoBox("走 adb install -r。要求设备开了 USB 调试并已授权；aab 装不了，所以只在 apk 下可用。")]
        public bool InstallAfterBuild;

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
        [InfoBox("Addressable 资源是按平台打的，所以打包流程会先切编辑器平台、再构建资源，顺序不能反。")]
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
        [InfoBox("留空则不修改 Profile 中的远程地址。真机/局域网测试时记得填成局域网地址，别留 127.0.0.1。")]
        public string RemoteLoadUrl;

        #endregion

        #region 输出

        [TabGroup("Build", "输出")]
        [BoxGroup("Build/输出/路径")]
        [LabelText("输出根目录")]
        [FolderPath]
        [Required]
        public string OutputRoot = "Build";

        [BoxGroup("Build/输出/路径")]
        [LabelText("按平台分子目录")]
        [InfoBox("勾选后是 <根目录>/Windows64/... 和 <根目录>/Android/...。" +
                 "关掉的话两个平台的输出会挤在同一层，文件名模板里没有 {platform} 就会撞在一起。")]
        public bool PlatformSubFolder = true;

        [BoxGroup("Build/输出/路径")]
        [LabelText("文件名模板")]
        [InfoBox("可用占位符：{product} {version} {code} {channel} {platform} {date} {time}")]
        public string FileNameFormat = "{product}_{channel}_v{version}.{code}_{date}";

        [BoxGroup("Build/输出/路径")]
        [LabelText("可执行文件名")]
        [InfoBox("留空则与输出目录同名。Windows 是 .exe，Android 是 .apk / .aab。")]
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
        [ShowIf(nameof(IsWindows))]
        public bool DeleteDebugFolders = true;

        [BoxGroup("Build/输出/收尾")]
        [LabelText("打包完成后打开目录")]
        public bool RevealInExplorer = true;

        [BoxGroup("Build/输出/收尾")]
        [LabelText("打包完成后运行")]
        [ShowIf(nameof(IsWindows))]
        public bool RunAfterBuild;

        #endregion

        #region 派生数据

        public bool IsIL2Cpp => ScriptingBackend == ScriptBackend.IL2CPP;

        public bool IsWindows => Platform == BuildPlatform.Windows64;

        public bool IsAndroid => Platform == BuildPlatform.Android;

        public bool IsApkFormat => IsAndroid && PackageFormat == AndroidPackageFormat.Apk && !ExportGradleProject;

        public bool IsAppBundleFormat => IsAndroid && PackageFormat == AndroidPackageFormat.AppBundle && !ExportGradleProject;

        public bool IsAndroidMonoWithArm64 => IsAndroid && !IsIL2Cpp && Architectures.HasFlag(AndroidArchitecture.ARM64);

        public bool NeedsPlatformSwitch => EditorUserBuildSettings.activeBuildTarget != BuildTarget;

        /// <summary>安装到设备这一步实际可用（apk 且没导 Gradle 工程）。</summary>
        public bool CanInstallToDevice => InstallAfterBuild && IsApkFormat;

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

        public BuildTarget BuildTarget =>
            IsAndroid ? UnityEditor.BuildTarget.Android : UnityEditor.BuildTarget.StandaloneWindows64;

        public BuildTargetGroup BuildTargetGroup =>
            IsAndroid ? UnityEditor.BuildTargetGroup.Android : UnityEditor.BuildTargetGroup.Standalone;

        public NamedBuildTarget NamedBuildTarget =>
            IsAndroid ? UnityEditor.Build.NamedBuildTarget.Android : UnityEditor.Build.NamedBuildTarget.Standalone;

        /// <summary>标题栏、进度条、日志里用的平台名。</summary>
        public string PlatformDisplayName => IsAndroid ? "Android" : "Windows";

        /// <summary>输出目录里的平台层名字。</summary>
        public string PlatformFolderName => IsAndroid ? "Android" : "Windows64";

        /// <summary>
        /// 产物扩展名。导出 Gradle 工程时没有扩展名（输出的是目录）。
        /// </summary>
        public string GetOutputExtension()
        {
            if (!IsAndroid)
            {
                return ".exe";
            }

            if (ExportGradleProject)
            {
                return string.Empty;
            }

            return PackageFormat == AndroidPackageFormat.AppBundle ? ".aab" : ".apk";
        }

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
                .Replace("{platform}", PlatformFolderName)
                .Replace("{date}", now.ToString("yyyyMMdd"))
                .Replace("{time}", now.ToString("HHmmss"));

            return SanitizeFileName(fileName);
        }

        /// <summary>
        /// 本次出包的目录：输出根目录 /（平台）/ 版本文件夹。
        /// </summary>
        public string GetOutputDirectory()
        {
            string root = string.IsNullOrWhiteSpace(OutputRoot) ? "Build" : OutputRoot;
            if (PlatformSubFolder)
            {
                root = CombinePath(root, PlatformFolderName);
            }

            return CombinePath(root, GetFolderName());
        }

        /// <summary>
        /// BuildPipeline 需要的 locationPathName。导出 Gradle 工程时它是一个目录。
        /// </summary>
        public string GetOutputPath()
        {
            string outputDirectory = GetOutputDirectory();
            string extension = GetOutputExtension();
            if (string.IsNullOrEmpty(extension))
            {
                return outputDirectory;
            }

            string folderName = GetFolderName();
            string executableName = string.IsNullOrWhiteSpace(ExecutableName)
                ? folderName
                : SanitizeFileName(ExecutableName);

            return CombinePath(outputDirectory, executableName + extension);
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

            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup, BuildTarget))
            {
                errors.Add($"当前 Unity 没有装 {PlatformDisplayName} 构建模块，去 Unity Hub 里补装。");
            }

            if (IsWindows)
            {
                ValidateWindows(errors);
            }
            else
            {
                ValidateAndroid(errors);
            }

            return errors;
        }

        private void ValidateWindows(List<string> errors)
        {
            if (DefaultResolution.x <= 0 || DefaultResolution.y <= 0)
            {
                errors.Add($"默认分辨率非法: {DefaultResolution.x}x{DefaultResolution.y}");
            }
        }

        private void ValidateAndroid(List<string> errors)
        {
            // 外部工具链先查 —— 路径不对的话 BuildPipeline 才抛异常，那时 Addressable 已经白构建了一遍。
            // NDK 只有 IL2CPP 用得上，Mono 时别拿它挡路。
            errors.AddRange(AndroidToolchainCheck.FindProblems(IsIL2Cpp));

            if (Architectures == AndroidArchitecture.None)
            {
                errors.Add("没有勾选任何 CPU 架构。");
            }

            if (IsAndroidMonoWithArm64)
            {
                errors.Add("ARM64 必须用 IL2CPP，Mono 出不了 64 位包。");
            }

            if (MinSdkVersion != AndroidSdkVersions.AndroidApiLevelAuto && (int)MinSdkVersion < MinimumSupportedAndroidApi)
            {
                errors.Add($"最低 SDK 版本不能低于 {MinimumSupportedAndroidApi}。");
            }

            if (TargetSdkVersion != AndroidSdkVersions.AndroidApiLevelAuto
                && MinSdkVersion != AndroidSdkVersions.AndroidApiLevelAuto
                && (int)TargetSdkVersion < (int)MinSdkVersion)
            {
                errors.Add($"目标 SDK 版本({(int)TargetSdkVersion})低于最低 SDK 版本({(int)MinSdkVersion})。");
            }

            if (!ForceInternetPermission)
            {
                errors.Add("本项目是联网游戏，「强制申请联网权限」必须勾上，否则真机连不上服务器。");
            }

            if (!UseCustomKeystore)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(KeystorePath) || !File.Exists(GetAbsolutePath(KeystorePath)))
            {
                errors.Add($"Keystore 文件不存在: {KeystorePath}");
            }

            if (string.IsNullOrWhiteSpace(KeyaliasName))
            {
                errors.Add("Key Alias 为空。");
            }

            if (string.IsNullOrEmpty(KeystorePassword) || string.IsNullOrEmpty(KeyaliasPassword))
            {
                errors.Add("Keystore 口令或 Key Alias 口令为空（口令存在本机 EditorPrefs，换机器要重填）。");
            }
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

        private void OnPlatformChanged()
        {
            EditorUtility.SetDirty(this);
        }

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

        /// <summary>
        /// 口令的 EditorPrefs 键带上工程路径，免得同一台机器上多个工程互相串。
        /// </summary>
        private static string KeystorePasswordKey => $"XFramework.PlayerBuild.KeystorePass.{Application.dataPath}";

        private static string KeyaliasPasswordKey => $"XFramework.PlayerBuild.KeyaliasPass.{Application.dataPath}";

        #endregion
    }

    /// <summary>
    /// 本项目实际会出的两个平台。
    /// </summary>
    public enum BuildPlatform
    {
        [LabelText("Windows64")]
        Windows64 = 0,

        [LabelText("Android")]
        Android = 1
    }

    /// <summary>
    /// Android 产物格式。apk 能直接装机，aab 是上架 Google Play 用的。
    /// </summary>
    public enum AndroidPackageFormat
    {
        [LabelText("APK")]
        Apk = 0,

        [LabelText("AAB (App Bundle)")]
        AppBundle = 1
    }

    /// <summary>
    /// 符号表等级。名字必须和 <c>Unity.Android.Types.DebugSymbolLevel</c> 一字不差 ——
    /// 那个枚举在 Android 平台扩展程序集里，我们按名字反射设值（见 PlayerBuilder）。
    /// </summary>
    public enum AndroidSymbolLevel
    {
        [LabelText("不生成")]
        None = 0,

        [LabelText("符号表")]
        SymbolTable = 1,

        [LabelText("完整")]
        Full = 2
    }

    /// <summary>
    /// 只暴露 Android 上有意义的纹理压缩格式。
    /// 枚举值与 <see cref="TextureCompressionFormat"/> 对齐（ETC=1，ETC2=2，ASTC=3），可直接强转。
    /// </summary>
    public enum AndroidTextureFormat
    {
        [LabelText("ASTC")]
        ASTC = 3,

        [LabelText("ETC2")]
        ETC2 = 2,

        [LabelText("ETC")]
        ETC = 1
    }

    /// <summary>
    /// 只暴露实际可用的脚本后端。
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
