using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using Object = UnityEngine.Object;

namespace XFramework
{
    /// <summary>
    /// Addressable automatic build utilities.
    /// </summary>
    public static class AddressableBuild
    {
        public const string ConfigPath = "Assets/Editor/AddressableTools/BuildConfiguration.asset";

        [MenuItem("Tools/4.自动打包Addressable")]
        public static void Build()
        {
            AddressableBuildReport report = BuildWithDefaultConfig();
            if (!report.Success)
            {
                Debug.LogError(report.Message);
            }
        }

        public static AddressableBuildReport BuildWithDefaultConfig()
        {
            return Build(LoadConfig());
        }

        public static AddressableBuildReport Build(BuildConfiguration config)
        {
            AddressableBuildReport report = new AddressableBuildReport();
            if (!TryValidate(config, report, out string assetRoot, out AddressableAssetSettings settings))
            {
                return report;
            }

            List<string> assetPaths = CollectAssetPaths(assetRoot, config.IncludeSubFolders).ToList();
            if (assetPaths.Count == 0)
            {
                report.Fail($"没有找到可打包资源: {config.RemoteAssetPath}");
                return report;
            }

            Dictionary<string, AddressableAssetGroup> targetGroups = GetTargetGroups(settings, config, assetPaths);
            if (targetGroups.Count == 0)
            {
                report.Fail("无法创建或获取 Addressable Group。");
                return report;
            }

            if (config.ClearTargetGroupBeforeBuild)
            {
                foreach (AddressableAssetGroup targetGroup in targetGroups.Values.Distinct())
                {
                    report.RemovedCount += ClearGroupEntries(settings, targetGroup);
                }
            }

            try
            {
                foreach (string assetPath in assetPaths)
                {
                    Object asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                    if (asset == null)
                    {
                        report.SkippedAssets.Add(assetPath);
                        continue;
                    }

                    string targetGroupName = GetTargetGroupName(config, assetPath);
                    if (!targetGroups.TryGetValue(targetGroupName, out AddressableAssetGroup targetGroup))
                    {
                        report.SkippedAssets.Add(assetPath);
                        continue;
                    }

                    AddressableAssetEntry entry = SetAddressableEntry(settings, targetGroup, assetPath, config);
                    if (entry == null)
                    {
                        report.SkippedAssets.Add(assetPath);
                        continue;
                    }

                    report.BuiltAssets.Add(assetPath);
                }

                foreach (AddressableAssetGroup targetGroup in targetGroups.Values.Distinct())
                {
                    settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, targetGroup, true, true);
                }

                AssetDatabase.SaveAssets();
                report.Success = true;
                report.GroupCount = targetGroups.Count;
                report.Message = $"Addressable 打标完成：新增/移动 {report.BuiltAssets.Count} 个资源，目标 Group {report.GroupCount} 个，跳过 {report.SkippedAssets.Count} 个资源。";
                Debug.Log(report.Message);
            }
            catch (Exception exception)
            {
                report.Fail($"Addressable 打标失败：{exception.Message}");
                Debug.LogException(exception);
            }

            return report;
        }

        public static BuildConfiguration LoadConfig()
        {
            BuildConfiguration config = AssetDatabase.LoadAssetAtPath<BuildConfiguration>(ConfigPath);
            if (config == null)
            {
                EditorUtility.DisplayDialog("提示", "Addressable 构建配置不存在。", "关闭");
            }

            return config;
        }

        public static List<string> PreviewAssetPaths(BuildConfiguration config)
        {
            if (config == null || string.IsNullOrWhiteSpace(config.RemoteAssetPath))
            {
                return new List<string>();
            }

            string assetRoot = GetAbsolutePath(config.RemoteAssetPath);
            return Directory.Exists(assetRoot)
                ? CollectAssetPaths(assetRoot, config.IncludeSubFolders).ToList()
                : new List<string>();
        }

        public static string GetTargetGroupName(BuildConfiguration config, string assetPath)
        {
            string baseGroupName = string.IsNullOrWhiteSpace(config?.GroupName) ? "Remote" : config.GroupName.Trim();
            if (config == null || config.GroupPackingMode == AddressableGroupPackingMode.SingleGroup)
            {
                return config != null && config.RemoteAssetSettings != null ? config.RemoteAssetSettings.Name : baseGroupName;
            }

            string folderName = GetTopLevelFolderName(config, assetPath);
            return string.IsNullOrEmpty(folderName) ? baseGroupName : $"{baseGroupName}-{folderName}";
        }

        public static int ClearTargetGroup(BuildConfiguration config)
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null || config == null)
            {
                return 0;
            }

            List<string> assetPaths = PreviewAssetPaths(config);
            List<string> groupNames = assetPaths.Count > 0
                ? assetPaths.Select(path => GetTargetGroupName(config, path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string> { GetTargetGroupName(config, string.Empty) };

            int removedCount = 0;
            foreach (string groupName in groupNames)
            {
                AddressableAssetGroup group = settings.FindGroup(groupName);
                if (group == null)
                {
                    continue;
                }

                removedCount += ClearGroupEntries(settings, group);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"已清空目标 Addressable Group，移除 {removedCount} 个 Entry。");
            return removedCount;
        }

        [MenuItem("Tools/6.清空Addressable标签内容")]
        public static void ClearBuild()
        {
            if (!EditorUtility.DisplayDialog("清空 Addressable 标签", "该操作会清空所有 Addressable Group 中的 Entry，是否继续？", "清空", "取消"))
            {
                return;
            }

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("无法获取 AddressableAssetSettings。");
                return;
            }

            int removedCount = 0;
            foreach (AddressableAssetGroup group in settings.groups)
            {
                if (group == null)
                {
                    continue;
                }

                removedCount += ClearGroupEntries(settings, group);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"已清空所有 Addressable Group，移除 {removedCount} 个 Entry。");
        }

        private static bool TryValidate(
            BuildConfiguration config,
            AddressableBuildReport report,
            out string assetRoot,
            out AddressableAssetSettings settings)
        {
            assetRoot = string.Empty;
            settings = AddressableAssetSettingsDefaultObject.Settings;

            if (config == null)
            {
                report.Fail("Addressable 构建配置为空。");
                return false;
            }

            if (settings == null)
            {
                report.Fail("无法获取 AddressableAssetSettings。");
                return false;
            }

            if (string.IsNullOrWhiteSpace(config.RemoteAssetPath))
            {
                report.Fail("远程资源路径为空。");
                return false;
            }

            assetRoot = GetAbsolutePath(config.RemoteAssetPath);
            if (!Directory.Exists(assetRoot))
            {
                report.Fail($"远程资源路径不存在: {config.RemoteAssetPath}");
                return false;
            }

            return true;
        }

        private static Dictionary<string, AddressableAssetGroup> GetTargetGroups(
            AddressableAssetSettings settings,
            BuildConfiguration config,
            IEnumerable<string> assetPaths)
        {
            Dictionary<string, AddressableAssetGroup> groups = new Dictionary<string, AddressableAssetGroup>(StringComparer.OrdinalIgnoreCase);
            foreach (string assetPath in assetPaths)
            {
                string groupName = GetTargetGroupName(config, assetPath);
                if (groups.ContainsKey(groupName))
                {
                    continue;
                }

                AddressableAssetGroup group = GetOrCreateGroup(settings, config, groupName);
                if (group != null)
                {
                    groups.Add(groupName, group);
                }
            }

            return groups;
        }

        private static AddressableAssetGroup GetOrCreateGroup(AddressableAssetSettings settings, BuildConfiguration config, string groupName)
        {
            AddressableAssetGroup group = settings.FindGroup(groupName);
            if (group == null && config.GroupPackingMode == AddressableGroupPackingMode.SingleGroup && config.RemoteAssetSettings != null)
            {
                group = config.RemoteAssetSettings;
            }

            if (group != null)
            {
                ApplyBundleMode(group, config);
                return group;
            }

            AddressableAssetGroup template = config.RemoteAssetSettings != null ? config.RemoteAssetSettings : settings.DefaultGroup;
            group = settings.CreateGroup(groupName, false, false, false, template.Schemas);
            ApplyBundleMode(group, config);
            return group;
        }

        private static AddressableAssetEntry SetAddressableEntry(
            AddressableAssetSettings settings,
            AddressableAssetGroup group,
            string assetPath,
            BuildConfiguration config)
        {
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid))
            {
                return null;
            }

            AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group);
            entry.address = config.UseAssetPathAsAddress
                ? assetPath
                : Path.GetFileNameWithoutExtension(assetPath);

            if (!string.IsNullOrWhiteSpace(config.Label))
            {
                entry.SetLabel(config.Label, true, true);
            }

            return entry;
        }

        private static IEnumerable<string> CollectAssetPaths(string rootPath, bool includeSubFolders)
        {
            SearchOption searchOption = includeSubFolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            return Directory.EnumerateFiles(rootPath, "*", searchOption)
                .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .Where(path => !Directory.Exists(path))
                .Select(ToUnityPath)
                .Where(path => path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        }

        private static int ClearGroupEntries(AddressableAssetSettings settings, AddressableAssetGroup group)
        {
            List<AddressableAssetEntry> entriesToRemove = new List<AddressableAssetEntry>(group.entries);
            foreach (AddressableAssetEntry entry in entriesToRemove)
            {
                group.RemoveAssetEntry(entry);
            }

            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryRemoved, group, true, true);
            return entriesToRemove.Count;
        }

        private static void ApplyBundleMode(AddressableAssetGroup group, BuildConfiguration config)
        {
            BundledAssetGroupSchema schema = group.GetSchema<BundledAssetGroupSchema>();
            if (schema == null)
            {
                return;
            }

            schema.BundleMode = config.BundleMode;
            EditorUtility.SetDirty(schema);
        }

        private static string GetTopLevelFolderName(BuildConfiguration config, string assetPath)
        {
            if (config == null || string.IsNullOrWhiteSpace(assetPath))
            {
                return string.Empty;
            }

            string rootPath = ToUnityPath(GetAbsolutePath(config.RemoteAssetPath)).TrimEnd('/');
            string normalizedAssetPath = NormalizePath(assetPath);
            if (!normalizedAssetPath.StartsWith(rootPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            string relativePath = normalizedAssetPath.Substring(rootPath.Length + 1);
            int slashIndex = relativePath.IndexOf('/');
            return slashIndex <= 0 ? string.Empty : SanitizeGroupName(relativePath.Substring(0, slashIndex));
        }

        private static string SanitizeGroupName(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName))
            {
                return string.Empty;
            }

            char[] invalidChars = Path.GetInvalidFileNameChars();
            string sanitized = new string(groupName.Select(character => invalidChars.Contains(character) ? '_' : character).ToArray());
            return sanitized.Trim();
        }

        private static string GetAbsolutePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string normalizedPath = NormalizePath(path);
            if (Path.IsPathRooted(normalizedPath))
            {
                return normalizedPath;
            }

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            return string.IsNullOrEmpty(projectRoot)
                ? normalizedPath
                : NormalizePath(Path.Combine(projectRoot, normalizedPath));
        }

        private static string ToUnityPath(string path)
        {
            string normalizedPath = NormalizePath(path);
            int assetsIndex = normalizedPath.IndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
            return assetsIndex >= 0 ? normalizedPath.Substring(assetsIndex) : normalizedPath;
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace("\\", "/");
        }
    }

    public class AddressableBuildReport
    {
        public bool Success;
        public string Message;
        public int RemovedCount;
        public int GroupCount;
        public readonly List<string> BuiltAssets = new List<string>();
        public readonly List<string> SkippedAssets = new List<string>();

        public void Fail(string message)
        {
            Success = false;
            Message = message;
        }
    }
}
