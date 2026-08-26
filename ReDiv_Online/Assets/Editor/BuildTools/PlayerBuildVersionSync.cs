using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// 版本号的唯一真相源是 <see cref="PlayerBuildConfig"/>，这里负责把它写到客户端其余几处：
    /// ProjectSettings 的 bundleVersion / AndroidBundleVersionCode，以及每个 Build Profile
    /// 自带的那份 PlayerSettings 快照。
    ///
    /// 为什么要管 Build Profile：`Assets/Settings/Build Profiles/*.asset` 里可能存着一份完整的
    /// PlayerSettings YAML 快照，profile 被激活时它会盖掉全局值。只改 ProjectSettings 的话，
    /// 哪天有人切到 profile 出包就会带着一个旧版本号发出去，而客户端连上服务器要按版本号
    /// 全等校验 —— 表现成"新包连不上"，很难往回追。
    ///
    /// 快照那部分只能走反射：BuildProfile 类型本身是 public，但 playerSettings /
    /// SerializePlayerSettings / HasSerializedPlayerSettings 三个成员都是 internal
    /// （2026-08-26 在 6000.4.8f1 上实测确认）。反射拿不到时只警告、不让出包失败。
    /// </summary>
    public static class PlayerBuildVersionSync
    {
        private const string BundleVersionProperty = "bundleVersion";
        private const string AndroidVersionCodeProperty = "AndroidBundleVersionCode";

        private static readonly Type ProfileType = Type.GetType("UnityEditor.Build.Profile.BuildProfile, UnityEditor");

        private static readonly PropertyInfo PlayerSettingsProperty = ProfileType?.GetProperty(
            "playerSettings",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly MethodInfo SerializeMethod = ProfileType?.GetMethod(
            "SerializePlayerSettings",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly MethodInfo HasSerializedMethod = ProfileType?.GetMethod(
            "HasSerializedPlayerSettings",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        /// <summary>
        /// 把配置里的版本号写到 ProjectSettings 和所有 Build Profile 快照，返回做过的事。
        /// </summary>
        public static List<string> Apply(PlayerBuildConfig config)
        {
            List<string> steps = new List<string>();
            if (config == null)
            {
                return steps;
            }

            if (PlayerSettings.bundleVersion != config.Version)
            {
                PlayerSettings.bundleVersion = config.Version;
                steps.Add($"ProjectSettings.bundleVersion 已同步为 {config.Version}。");
            }

            if (PlayerSettings.Android.bundleVersionCode != config.VersionCode)
            {
                PlayerSettings.Android.bundleVersionCode = config.VersionCode;
                steps.Add($"Android bundleVersionCode 已同步为 {config.VersionCode}。");
            }

            foreach (ScriptableObject profile in LoadBuildProfiles())
            {
                string profileStep = ApplyToProfile(profile, config);
                if (!string.IsNullOrEmpty(profileStep))
                {
                    steps.Add(profileStep);
                }
            }

            return steps;
        }

        /// <summary>
        /// 只读检查：哪几处和配置对不上。给窗口做提示用，不改任何值。
        /// </summary>
        public static List<string> FindMismatches(PlayerBuildConfig config)
        {
            List<string> mismatches = new List<string>();
            if (config == null)
            {
                return mismatches;
            }

            if (PlayerSettings.bundleVersion != config.Version)
            {
                mismatches.Add($"ProjectSettings 版本号是 {PlayerSettings.bundleVersion}");
            }

            if (PlayerSettings.Android.bundleVersionCode != config.VersionCode)
            {
                mismatches.Add($"Android 内部版本号是 {PlayerSettings.Android.bundleVersionCode}");
            }

            foreach (ScriptableObject profile in LoadBuildProfiles())
            {
                SerializedObject serialized = GetProfilePlayerSettings(profile);
                if (serialized == null)
                {
                    continue;
                }

                SerializedProperty version = serialized.FindProperty(BundleVersionProperty);
                if (version != null && version.stringValue != config.Version)
                {
                    mismatches.Add($"Build Profile [{profile.name}] 版本号是 {version.stringValue}");
                }

                SerializedProperty versionCode = serialized.FindProperty(AndroidVersionCodeProperty);
                if (versionCode != null && versionCode.intValue != config.VersionCode)
                {
                    mismatches.Add($"Build Profile [{profile.name}] Android 内部版本号是 {versionCode.intValue}");
                }
            }

            return mismatches;
        }

        #region 内部实现

        private static IEnumerable<ScriptableObject> LoadBuildProfiles()
        {
            List<ScriptableObject> profiles = new List<ScriptableObject>();
            if (ProfileType == null)
            {
                return profiles;
            }

            foreach (string guid in AssetDatabase.FindAssets("t:BuildProfile"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                ScriptableObject profile = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (profile != null && ProfileType.IsInstanceOfType(profile))
                {
                    profiles.Add(profile);
                }
            }

            return profiles;
        }

        /// <summary>
        /// 取 profile 自带的那份 PlayerSettings 快照。没有快照（m_Settings 为空）时返回 null。
        /// </summary>
        private static SerializedObject GetProfilePlayerSettings(ScriptableObject profile)
        {
            if (profile == null || PlayerSettingsProperty == null || HasSerializedMethod == null)
            {
                return null;
            }

            try
            {
                if (HasSerializedMethod.Invoke(profile, null) is not true)
                {
                    return null;
                }

                if (PlayerSettingsProperty.GetValue(profile) is not UnityEngine.Object playerSettings)
                {
                    return null;
                }

                return new SerializedObject(playerSettings);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[一键出包] 读取 Build Profile [{profile.name}] 的 PlayerSettings 快照失败：{exception.Message}");
                return null;
            }
        }

        private static string ApplyToProfile(ScriptableObject profile, PlayerBuildConfig config)
        {
            SerializedObject serialized = GetProfilePlayerSettings(profile);
            if (serialized == null || SerializeMethod == null)
            {
                return string.Empty;
            }

            bool changed = false;

            SerializedProperty version = serialized.FindProperty(BundleVersionProperty);
            if (version != null && version.stringValue != config.Version)
            {
                version.stringValue = config.Version;
                changed = true;
            }

            SerializedProperty versionCode = serialized.FindProperty(AndroidVersionCodeProperty);
            if (versionCode != null && versionCode.intValue != config.VersionCode)
            {
                versionCode.intValue = config.VersionCode;
                changed = true;
            }

            if (!changed)
            {
                return string.Empty;
            }

            try
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
                // 快照是 YAML 文本，改完内存里的 PlayerSettings 对象还要回写一次才落盘。
                SerializeMethod.Invoke(profile, null);
                EditorUtility.SetDirty(profile);
                return $"Build Profile [{profile.name}] 的版本号快照已同步。";
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[一键出包] 同步 Build Profile [{profile.name}] 版本号失败：{exception.Message}");
                return string.Empty;
            }
        }

        #endregion
    }
}
