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
    /// Windows 一键出包流程：校验 → 应用 ProjectSettings → Addressable 前置 → BuildPipeline → 收尾。
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

            try
            {
                if (config.SaveAssetsBeforeBuild && !SaveDirtyAssets())
                {
                    report.Fail("已取消：场景未保存。");
                    return report;
                }

                EditorUtility.DisplayProgressBar("Windows 一键出包", "应用 ProjectSettings...", 0.1f);
                ApplyPlayerSettings(config);

                if (config.MarkAddressables && !MarkAddressables(config, report))
                {
                    return report;
                }

                if (config.BuildAddressableContent && !BuildAddressableContent(config, report))
                {
                    return report;
                }

                EditorUtility.DisplayProgressBar("Windows 一键出包", "调用 BuildPipeline...", 0.5f);
                return BuildPlayer(config, report);
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
        /// 把配置写进 ProjectSettings。宏定义不走这里，改用 extraScriptingDefines，避免触发域重载打断出包。
        /// </summary>
        private static void ApplyPlayerSettings(PlayerBuildConfig config)
        {
            PlayerSettings.productName = config.ProductName;
            PlayerSettings.companyName = config.CompanyName;
            PlayerSettings.bundleVersion = config.Version;
            PlayerSettings.SetApplicationIdentifier(config.NamedBuildTarget, config.BundleIdentifier);
            PlayerSettings.SetScriptingBackend(config.NamedBuildTarget, config.ScriptingImplementation);
            PlayerSettings.SetIl2CppCompilerConfiguration(config.NamedBuildTarget, config.Il2CppConfiguration);
            PlayerSettings.SetManagedStrippingLevel(config.NamedBuildTarget, config.StrippingLevel);
            PlayerSettings.fullScreenMode = config.FullScreenMode;
            PlayerSettings.defaultScreenWidth = config.DefaultResolution.x;
            PlayerSettings.defaultScreenHeight = config.DefaultResolution.y;

            AssetDatabase.SaveAssets();
        }

        private static bool MarkAddressables(PlayerBuildConfig config, PlayerBuildReport report)
        {
            EditorUtility.DisplayProgressBar("Windows 一键出包", "Addressable 打标...", 0.2f);

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

        private static bool BuildAddressableContent(PlayerBuildConfig config, PlayerBuildReport report)
        {
            EditorUtility.DisplayProgressBar("Windows 一键出包", "构建 Addressable 资源...", 0.3f);

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

        private static PlayerBuildReport BuildPlayer(PlayerBuildConfig config, PlayerBuildReport report)
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
                subtarget = (int)StandaloneBuildSubtarget.Player,
                options = config.GetBuildOptions(),
                extraScriptingDefines = config.GetDefineSymbols().ToArray()
            };

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

            EditorUtility.DisplayProgressBar("Windows 一键出包", "收尾处理...", 0.95f);
            AfterBuild(config, report);

            report.Success = true;
            report.Message =
                $"出包成功：{outputPath}\n版本 {config.Version}({config.VersionCode})  渠道 {config.Channel}\n" +
                $"体积 {summary.totalSize / 1024f / 1024f:F1} MB  耗时 {summary.totalTime.TotalMinutes:F1} 分钟";
            Debug.Log(report.Message);
            return report;
        }

        private static void AfterBuild(PlayerBuildConfig config, PlayerBuildReport report)
        {
            string outputDirectory = PlayerBuildConfig.GetAbsolutePath(config.GetOutputDirectory());

            if (config.DeleteDebugFolders)
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

            if (config.RunAfterBuild)
            {
                RunPlayer(PlayerBuildConfig.GetAbsolutePath(report.OutputPath));
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
                version = config.Version,
                versionCode = config.VersionCode,
                channel = config.Channel.ToString(),
                bundleIdentifier = config.BundleIdentifier,
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

        #endregion

        [Serializable]
        private class PlayerBuildVersionInfo
        {
            public string productName;
            public string version;
            public int versionCode;
            public string channel;
            public string bundleIdentifier;
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
