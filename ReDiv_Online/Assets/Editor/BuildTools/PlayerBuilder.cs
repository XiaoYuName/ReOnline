using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace XFramework
{
    /// <summary>
    /// 一键出包流程：校验 → 切平台 → 应用 ProjectSettings → Addressable 前置 → BuildPipeline → 收尾。
    /// 支持 Windows64 与 Android，平台相关的差异都收在 <see cref="PlayerBuildConfig"/> 与本文件的
    /// ApplyWindowsSettings / ApplyAndroidSettings 两处。
    ///
    /// ⚠️ 切平台必须排在 Addressable 构建之前 —— Addressable 的 bundle 是按平台打的，
    /// 顺序反了会把上一个平台的资源打进这个包里。
    /// </summary>
    public static class PlayerBuilder
    {
        private static readonly string[] DebugFolderPatterns =
        {
            "*_BurstDebugInformation_DoNotShip",
            "*_BackUpThisFolder_ButDontShipItWithYourGame"
        };

        public static PlayerBuildReport Build(PlayerBuildConfig config)
        {
            PlayerBuildReport report = new PlayerBuildReport();

            if (config == null)
            {
                report.Fail("出包配置为空。");
                return report;
            }

            List<string> errors = config.Validate();
            if (errors.Count > 0)
            {
                report.Fail("配置校验失败：\n" + string.Join("\n", errors.Select(error => "· " + error)));
                return report;
            }

            string progressTitle = $"{config.PlatformDisplayName} 一键出包";

            try
            {
                if (config.SaveAssetsBeforeBuild && !SaveDirtyAssets())
                {
                    report.Fail("已取消：场景未保存。");
                    return report;
                }

                // 切平台放在最前面：后面的 PlayerSettings 写入和 Addressable 构建都跟着活动平台走。
                if (!EnsureActivePlatform(config, report, progressTitle))
                {
                    return report;
                }

                EditorUtility.DisplayProgressBar(progressTitle, "应用 ProjectSettings...", 0.15f);
                ApplyPlayerSettings(config, report);

                if (config.MarkAddressables && !MarkAddressables(config, report, progressTitle))
                {
                    return report;
                }

                if (config.BuildAddressableContent && !BuildAddressableContent(config, report, progressTitle))
                {
                    return report;
                }

                EditorUtility.DisplayProgressBar(progressTitle, "调用 BuildPipeline...", 0.5f);
                return BuildPlayer(config, report, progressTitle);
            }
            catch (Exception exception)
            {
                report.Fail($"出包异常：{exception.Message}");
                Debug.LogException(exception);
                return report;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        #region 步骤

        private static bool SaveDirtyAssets()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return false;
            }

            AssetDatabase.SaveAssets();
            return true;
        }

        /// <summary>
        /// 把编辑器切到目标平台。已经在目标平台时什么都不做。
        /// 切换会按新平台重新导入资源，第一次可能要几分钟 —— 这是 Unity 的行为，不是卡死。
        /// </summary>
        private static bool EnsureActivePlatform(PlayerBuildConfig config, PlayerBuildReport report, string progressTitle)
        {
            if (EditorUserBuildSettings.activeBuildTarget == config.BuildTarget)
            {
                return true;
            }

            EditorUtility.DisplayProgressBar(
                progressTitle,
                $"切换编辑器平台到 {config.BuildTarget}（要按新平台重新导入资源，可能很慢）...",
                0.05f);

            bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(config.BuildTargetGroup, config.BuildTarget);
            if (!switched || EditorUserBuildSettings.activeBuildTarget != config.BuildTarget)
            {
                report.Fail($"切换编辑器平台到 {config.BuildTarget} 失败，当前仍是 {EditorUserBuildSettings.activeBuildTarget}。");
                return false;
            }

            report.Steps.Add($"已把编辑器平台切换到 {config.BuildTarget}。");
            return true;
        }

        /// <summary>
        /// 把配置写进 ProjectSettings。宏定义不走这里，改用 extraScriptingDefines，避免触发域重载打断出包。
        /// </summary>
        private static void ApplyPlayerSettings(PlayerBuildConfig config, PlayerBuildReport report)
        {
            PlayerSettings.productName = config.ProductName;
            PlayerSettings.companyName = config.CompanyName;

            // 版本号以本配置为准：一次写全 ProjectSettings 和所有 Build Profile 快照。
            report.Steps.AddRange(PlayerBuildVersionSync.Apply(config));

            PlayerSettings.SetApplicationIdentifier(config.NamedBuildTarget, config.BundleIdentifier);
            PlayerSettings.SetScriptingBackend(config.NamedBuildTarget, config.ScriptingImplementation);
            PlayerSettings.SetIl2CppCompilerConfiguration(config.NamedBuildTarget, config.Il2CppConfiguration);
            PlayerSettings.SetManagedStrippingLevel(config.NamedBuildTarget, config.StrippingLevel);

            if (config.IsAndroid)
            {
                ApplyAndroidSettings(config, report);
            }
            else
            {
                ApplyWindowsSettings(config);
            }

            AssetDatabase.SaveAssets();
        }

        private static void ApplyWindowsSettings(PlayerBuildConfig config)
        {
            PlayerSettings.fullScreenMode = config.FullScreenMode;
            PlayerSettings.defaultScreenWidth = config.DefaultResolution.x;
            PlayerSettings.defaultScreenHeight = config.DefaultResolution.y;
        }

        /// <summary>
        /// 符号表开关在 6000.4 已经从 <c>EditorUserBuildSettings.androidCreateSymbols</c>（已废弃）
        /// 挪到了 <c>UnityEditor.Android.UserBuildSettings.DebugSymbols.level</c>。
        /// 新 API 在平台扩展程序集 UnityEditor.Android.Extensions 里 —— 直接引用的话，
        /// 没装 Android 模块的机器整个 Assembly-CSharp-Editor 都编不过，所以走反射按名字设值。
        /// 反射失败只警告，不让出包挂掉。
        /// </summary>
        private static string ApplyAndroidDebugSymbols(AndroidSymbolLevel level)
        {
            try
            {
                Type settingsType = Type.GetType("UnityEditor.Android.UserBuildSettings, UnityEditor.Android.Extensions");
                Type symbolsType = settingsType?.GetNestedType(
                    "DebugSymbols",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                System.Reflection.PropertyInfo levelProperty = symbolsType?.GetProperty(
                    "level",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                if (levelProperty == null)
                {
                    return "没找到 Android 符号表设置的 API，这一项已跳过。";
                }

                levelProperty.SetValue(null, Enum.Parse(levelProperty.PropertyType, level.ToString()));
                return string.Empty;
            }
            catch (Exception exception)
            {
                return $"设置 Android 符号表等级失败，已跳过：{exception.Message}";
            }
        }

        private static void ApplyAndroidSettings(PlayerBuildConfig config, PlayerBuildReport report)
        {
            PlayerSettings.Android.targetArchitectures = config.Architectures;
            PlayerSettings.Android.minSdkVersion = config.MinSdkVersion;
            PlayerSettings.Android.targetSdkVersion = config.TargetSdkVersion;
            PlayerSettings.Android.forceInternetPermission = config.ForceInternetPermission;
            PlayerSettings.Android.optimizedFramePacing = config.OptimizedFramePacing;
            PlayerSettings.Android.minifyRelease = config.MinifyRelease;
            PlayerSettings.Android.textureCompressionFormats = new[] { (TextureCompressionFormat)config.TextureFormat };
            PlayerSettings.defaultInterfaceOrientation = config.Orientation;

            bool appBundle = config.PackageFormat == AndroidPackageFormat.AppBundle;
            EditorUserBuildSettings.buildAppBundle = appBundle;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = config.ExportGradleProject;
            EditorUserBuildSettings.androidBuildType = config.DevelopmentBuild
                ? AndroidBuildType.Development
                : AndroidBuildType.Release;

            // 这两个开关是互斥语义：拆分二进制只有 aab 才有意义，分架构出包只有 apk 才有意义。
            PlayerSettings.Android.splitApplicationBinary = appBundle && config.SplitApplicationBinary;
            PlayerSettings.Android.buildApkPerCpuArchitecture = !appBundle && config.BuildApkPerCpuArchitecture;

            string symbolsWarning = ApplyAndroidDebugSymbols(config.SymbolLevel);
            if (!string.IsNullOrEmpty(symbolsWarning))
            {
                report.Steps.Add(symbolsWarning);
            }

            ApplyAndroidKeystore(config);
        }

        private static void ApplyAndroidKeystore(PlayerBuildConfig config)
        {
            PlayerSettings.Android.useCustomKeystore = config.UseCustomKeystore;

            if (!config.UseCustomKeystore)
            {
                // 不清掉的话上一次填的 keystore 还挂在 ProjectSettings 上，会被当成"自定义签名没生效"来查。
                // 本来就是空的时候别写 —— Unity 会把空串序列化成 '{inproject}: '，白白弄脏 ProjectSettings 的 diff。
                if (!string.IsNullOrEmpty(PlayerSettings.Android.keystoreName))
                {
                    PlayerSettings.Android.keystoreName = string.Empty;
                }

                if (!string.IsNullOrEmpty(PlayerSettings.Android.keyaliasName))
                {
                    PlayerSettings.Android.keyaliasName = string.Empty;
                }

                PlayerSettings.Android.keystorePass = string.Empty;
                PlayerSettings.Android.keyaliasPass = string.Empty;
                return;
            }

            PlayerSettings.Android.keystoreName = PlayerBuildConfig.GetAbsolutePath(config.KeystorePath);
            PlayerSettings.Android.keyaliasName = config.KeyaliasName;
            PlayerSettings.Android.keystorePass = config.KeystorePassword;
            PlayerSettings.Android.keyaliasPass = config.KeyaliasPassword;
        }

        private static bool MarkAddressables(PlayerBuildConfig config, PlayerBuildReport report, string progressTitle)
        {
            EditorUtility.DisplayProgressBar(progressTitle, "Addressable 打标...", 0.25f);

            BuildConfiguration markConfig = config.AddressableMarkConfig != null
                ? config.AddressableMarkConfig
                : AssetDatabase.LoadAssetAtPath<BuildConfiguration>(AddressableBuild.ConfigPath);

            if (markConfig == null)
            {
                report.Fail($"Addressable 打标配置不存在：{AddressableBuild.ConfigPath}");
                return false;
            }

            AddressableBuildReport markReport = AddressableBuild.Build(markConfig);
            report.Steps.Add(markReport.Success ? markReport.Message : $"Addressable 打标失败：{markReport.Message}");

            if (!markReport.Success)
            {
                report.Fail(markReport.Message);
                return false;
            }

            return true;
        }

        private static bool BuildAddressableContent(PlayerBuildConfig config, PlayerBuildReport report, string progressTitle)
        {
            EditorUtility.DisplayProgressBar(progressTitle, "构建 Addressable 资源...", 0.35f);

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                report.Fail("无法获取 AddressableAssetSettings。");
                return false;
            }

            if (!ApplyAddressableProfile(config, settings, report))
            {
                return false;
            }

            if (config.CleanAddressableBuildCache)
            {
                AddressableAssetSettings.CleanPlayerContent();
            }

            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
            if (!string.IsNullOrEmpty(result.Error))
            {
                report.Fail($"Addressable 资源构建失败：{result.Error}");
                return false;
            }

            report.Steps.Add($"Addressable 资源构建完成，耗时 {result.Duration:F1} 秒。");
            return true;
        }

        private static bool ApplyAddressableProfile(PlayerBuildConfig config, AddressableAssetSettings settings, PlayerBuildReport report)
        {
            if (!string.IsNullOrWhiteSpace(config.AddressableProfileName))
            {
                string profileId = settings.profileSettings.GetProfileId(config.AddressableProfileName.Trim());
                if (string.IsNullOrEmpty(profileId))
                {
                    report.Fail($"Addressable Profile 不存在：{config.AddressableProfileName}");
                    return false;
                }

                settings.activeProfileId = profileId;
                report.Steps.Add($"已切换 Addressable Profile：{config.AddressableProfileName}");
            }

            if (!string.IsNullOrWhiteSpace(config.RemoteLoadUrl))
            {
                settings.profileSettings.SetValue(settings.activeProfileId, "RemoteLoadPath", config.RemoteLoadUrl.Trim());
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                report.Steps.Add($"已设置 RemoteLoadPath：{config.RemoteLoadUrl}");
            }

            return true;
        }

        private static PlayerBuildReport BuildPlayer(PlayerBuildConfig config, PlayerBuildReport report, string progressTitle)
        {
            string outputPath = config.GetOutputPath();
            string outputDirectory = PlayerBuildConfig.GetAbsolutePath(config.GetOutputDirectory());
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = config.GetScenePaths(),
                locationPathName = outputPath,
                target = config.BuildTarget,
                targetGroup = config.BuildTargetGroup,
                options = config.GetBuildOptions(),
                extraScriptingDefines = config.GetDefineSymbols().ToArray()
            };

            // subtarget 只有 Standalone 用得上（Player / Server）。Android 留 0，
            // 纹理压缩走 PlayerSettings.Android.textureCompressionFormats。
            if (config.IsWindows)
            {
                options.subtarget = (int)StandaloneBuildSubtarget.Player;
            }

            BuildReport buildReport = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = buildReport.summary;

            report.OutputPath = outputPath;
            report.Duration = summary.totalTime;
            report.TotalSize = summary.totalSize;

            if (summary.result != BuildResult.Succeeded)
            {
                report.Fail($"出包失败({summary.result})：{summary.totalErrors} 个错误。详见 Console 与 Editor.log。");
                return report;
            }

            EditorUtility.DisplayProgressBar(progressTitle, "收尾处理...", 0.95f);
            AfterBuild(config, report);

            report.Success = true;
            report.Message =
                $"出包成功（{config.PlatformDisplayName}）：{outputPath}\n" +
                $"版本 {config.Version}({config.VersionCode})  渠道 {config.Channel}\n" +
                $"体积 {summary.totalSize / 1024f / 1024f:F1} MB  耗时 {summary.totalTime.TotalMinutes:F1} 分钟";
            Debug.Log(report.Message);
            return report;
        }

        private static void AfterBuild(PlayerBuildConfig config, PlayerBuildReport report)
        {
            string outputDirectory = PlayerBuildConfig.GetAbsolutePath(config.GetOutputDirectory());

            if (config.IsWindows && config.DeleteDebugFolders)
            {
                report.Steps.Add($"已清理 {DeleteDebugFolders(outputDirectory)} 个调试目录。");
            }

            if (config.GenerateVersionFile)
            {
                WriteVersionFile(config, outputDirectory);
                report.Steps.Add("已生成 version.json。");
            }

            if (config.AutoIncreaseVersionCode)
            {
                config.VersionCode++;
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
                report.Steps.Add($"内部版本号已递增至 {config.VersionCode}。");
            }

            if (config.RevealInExplorer)
            {
                EditorUtility.RevealInFinder(PlayerBuildConfig.GetAbsolutePath(report.OutputPath));
            }

            if (config.IsWindows && config.RunAfterBuild)
            {
                RunPlayer(PlayerBuildConfig.GetAbsolutePath(report.OutputPath));
            }

            if (config.CanInstallToDevice)
            {
                report.Steps.Add(InstallToDevice(PlayerBuildConfig.GetAbsolutePath(report.OutputPath)));
            }
        }

        private static int DeleteDebugFolders(string outputDirectory)
        {
            if (!Directory.Exists(outputDirectory))
            {
                return 0;
            }

            int deletedCount = 0;
            foreach (string pattern in DebugFolderPatterns)
            {
                foreach (string directory in Directory.GetDirectories(outputDirectory, pattern, SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        Directory.Delete(directory, true);
                        deletedCount++;
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning($"删除调试目录失败 {directory}：{exception.Message}");
                    }
                }
            }

            return deletedCount;
        }

        private static void WriteVersionFile(PlayerBuildConfig config, string outputDirectory)
        {
            PlayerBuildVersionInfo versionInfo = new PlayerBuildVersionInfo
            {
                productName = config.ProductName,
                platform = config.PlatformFolderName,
                version = config.Version,
                versionCode = config.VersionCode,
                channel = config.Channel.ToString(),
                bundleIdentifier = config.BundleIdentifier,
                scriptingBackend = config.ScriptingBackend.ToString(),
                androidArchitectures = config.IsAndroid ? config.Architectures.ToString() : string.Empty,
                buildTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                unityVersion = Application.unityVersion,
                remoteLoadUrl = config.RemoteLoadUrl
            };

            File.WriteAllText(Path.Combine(outputDirectory, "version.json"), JsonUtility.ToJson(versionInfo, true));
        }

        private static void RunPlayer(string executablePath)
        {
            if (!File.Exists(executablePath))
            {
                Debug.LogWarning($"可执行文件不存在，无法运行：{executablePath}");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(executablePath)
                {
                    WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty,
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"启动游戏失败：{exception.Message}");
            }
        }

        /// <summary>
        /// adb install -r 装到已连接的设备。装不上只报告、不让整个出包算失败 —— 包已经出好了。
        /// </summary>
        private static string InstallToDevice(string apkPath)
        {
            string adbPath = FindAdb();
            if (string.IsNullOrEmpty(adbPath))
            {
                return "没找到 adb，跳过安装（Preferences > External Tools 里配 Android SDK，或自己 adb install）。";
            }

            if (!File.Exists(apkPath))
            {
                return $"apk 不存在，跳过安装：{apkPath}";
            }

            try
            {
                using Process process = new Process
                {
                    StartInfo = new ProcessStartInfo(adbPath, $"install -r \"{apkPath}\"")
                    {
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = System.Text.Encoding.UTF8,
                        StandardErrorEncoding = System.Text.Encoding.UTF8
                    }
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                string tail = (output + error).Replace("\r", " ").Replace("\n", " ").Trim();
                return process.ExitCode == 0
                    ? "已安装到设备。"
                    : $"安装失败(adb 退出码 {process.ExitCode})：{tail}";
            }
            catch (Exception exception)
            {
                return $"安装失败：{exception.Message}";
            }
        }

        /// <summary>
        /// 先看 Preferences 里配的 SDK，没配就用编辑器自带的那份。
        /// </summary>
        private static string FindAdb()
        {
            List<string> roots = new List<string>();

            string preferenceRoot = EditorPrefs.GetString("AndroidSdkRoot", string.Empty);
            if (!string.IsNullOrWhiteSpace(preferenceRoot))
            {
                roots.Add(preferenceRoot);
            }

            roots.Add(Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/AndroidPlayer/SDK"));

            foreach (string root in roots)
            {
                string adbPath = Path.Combine(root, "platform-tools", Application.platform == RuntimePlatform.WindowsEditor ? "adb.exe" : "adb");
                if (File.Exists(adbPath))
                {
                    return adbPath;
                }
            }

            return string.Empty;
        }

        #endregion

        [Serializable]
        private class PlayerBuildVersionInfo
        {
            public string productName;
            public string platform;
            public string version;
            public int versionCode;
            public string channel;
            public string bundleIdentifier;
            public string scriptingBackend;
            public string androidArchitectures;
            public string buildTime;
            public string unityVersion;
            public string remoteLoadUrl;
        }
    }

    public class PlayerBuildReport
    {
        public bool Success;
        public string Message;
        public string OutputPath;
        public TimeSpan Duration;
        public ulong TotalSize;
        public readonly List<string> Steps = new List<string>();

        public void Fail(string message)
        {
            Success = false;
            Message = message;
            Debug.LogError($"[一键出包] {message}");
        }
    }
}
