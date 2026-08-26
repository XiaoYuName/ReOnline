using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// Android 外部工具链（SDK / NDK / JDK / Gradle）的飞行前检查。
    ///
    /// 为什么要有这一步：这几样任何一个路径不对，`BuildPipeline.BuildPlayer` 才会抛
    /// `UnityException: Android NDK not found`——**而那时 Addressable 已经白构建了一遍**，
    /// 几分钟就没了。所以在 `PlayerBuildConfig.Validate()` 里先问一句。
    ///
    /// 判定不是我们自己写的：直接调 Unity 自己的 <c>AndroidRoot.Validate(path)</c>，
    /// 口径和它在 Preferences &gt; External Tools 里画红字用的完全一样。
    /// 这些类型在平台扩展程序集 UnityEditor.Android.Extensions 里，直接引用会让
    /// 没装 Android 模块的机器整个 Assembly-CSharp-Editor 编不过，所以走反射。
    ///
    /// ⚠️ **反射失败一律当作没问题**（fail open）。这只是一层提前预警，
    /// 不能因为 Unity 换了内部类名就把出包卡死。
    ///
    /// 2026-08-26 实测踩过：Unity 自带的 NDK 解压时多套了一层
    /// `NDK/android-ndk-r27c/`（SDK 和 OpenJDK 都是平铺的，只有它不是），
    /// 于是 `NDK/source.properties` 找不到，判定 invalid。
    /// </summary>
    public static class AndroidToolchainCheck
    {
        private const string AssemblyName = "UnityEditor.Android.Extensions";

        private const BindingFlags MemberFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        /// <summary>
        /// 返回每个有问题的外部工具的描述，全都正常（或查不了）时返回空列表。
        /// </summary>
        /// <param name="includeNdk">NDK 只有 IL2CPP 才用得上，Mono 时别拿它挡路。</param>
        public static List<string> FindProblems(bool includeNdk)
        {
            List<string> problems = new List<string>();

            Type rootType = Type.GetType($"UnityEditor.Android.AndroidRoot, {AssemblyName}");
            if (rootType == null)
            {
                return problems;
            }

            foreach (Type toolType in rootType.Assembly.GetTypes())
            {
                if (toolType.IsAbstract || !rootType.IsAssignableFrom(toolType) || toolType == rootType)
                {
                    continue;
                }

                if (!includeNdk && toolType.Name == "AndroidNDKRoot")
                {
                    continue;
                }

                string problem = CheckOne(toolType);
                if (!string.IsNullOrEmpty(problem))
                {
                    problems.Add(problem);
                }
            }

            return problems;
        }

        private static string CheckOne(Type toolType)
        {
            try
            {
                object instance = toolType.GetMethod("GetInstance", MemberFlags)?.Invoke(null, null);
                if (instance == null)
                {
                    return string.Empty;
                }

                // 用户在 Preferences 里关掉校验时不要多嘴。
                MethodInfo disabledMethod = toolType.GetMethod("ValidationIsDisabled", MemberFlags);
                if (disabledMethod != null && Invoke(disabledMethod, instance) is true)
                {
                    return string.Empty;
                }

                string path = ResolvePath(toolType, instance);
                MethodInfo validateMethod = toolType.GetMethod("Validate", MemberFlags);
                if (validateMethod == null)
                {
                    return string.Empty;
                }

                object result = validateMethod.Invoke(validateMethod.IsStatic ? null : instance, new object[] { path });
                if (result == null)
                {
                    return string.Empty;
                }

                Type resultType = result.GetType();
                if (resultType.GetProperty("Success", MemberFlags)?.GetValue(result) is not false)
                {
                    return string.Empty;
                }

                string toolName = toolType.GetProperty("Name", MemberFlags)?.GetValue(instance) as string ?? toolType.Name;
                string error = resultType.GetProperty("ErrorMessage", MemberFlags)?.GetValue(result) as string;

                return $"Android {toolName} 路径无效：{error}\n" +
                       $"    当前路径：{path}\n" +
                       "    去 Edit > Preferences > External Tools 改，或用 Unity Hub 重装 Android 模块。";
            }
            catch (Exception exception)
            {
                // 查不了就当没问题 —— 这只是预警，不该反过来挡住出包。
                Debug.LogWarning($"[一键出包] 检查 {toolType.Name} 时出错，已跳过：{exception.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 勾了「installed with Unity」就用内嵌路径，否则用用户自己填的那个。
        /// </summary>
        private static string ResolvePath(Type toolType, object instance)
        {
            bool useEmbedded = toolType.GetProperty("UseEmbedded", MemberFlags)?.GetValue(instance) is true;

            if (!useEmbedded)
            {
                return toolType.GetProperty("CustomDirectory", MemberFlags)?.GetValue(instance) as string ?? string.Empty;
            }

            MethodInfo defaultMethod = toolType.GetMethod("GetDefaultDirectory", MemberFlags);
            return defaultMethod == null ? string.Empty : Invoke(defaultMethod, instance) as string ?? string.Empty;
        }

        private static object Invoke(MethodInfo method, object instance)
        {
            return method.Invoke(method.IsStatic ? null : instance, null);
        }
    }
}
