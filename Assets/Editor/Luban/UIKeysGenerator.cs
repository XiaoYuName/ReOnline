#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// 从 <see cref="UIPageConfiguration"/> 资产生成 UI 界面 ID 常量类。
    /// 避免代码里手写 "CommonUI" 这类字符串 —— 拼错编译期发现不了，只能在运行时才暴露。
    /// (UI 配置以前是 Luban 表，现在改成了 ScriptableObject，所以数据源换成资产。)
    /// </summary>
    public static class UIKeysGenerator
    {
        [MenuItem("Tools/XFramework/UI/生成 UIKeys", false, 300)]
        private static void GenerateMenuItem()
        {
            Generate();
        }

        /// <summary>
        /// 读取 UI 配置表资产并生成 UIKeys.cs。
        /// </summary>
        /// <returns>生成成功返回 true。</returns>
        public static bool Generate()
        {
            // 输出路径 / 类名 / 命名空间以前是这里的 const，改不了也看不见，现在统一放在
            // ConfigToolsSettings 资源里（ConfigTools 窗口的「设置」里就能改）。
            ConfigToolsSettings settings = ConfigToolsSettings.LoadOrCreate();

            if (settings == null)
            {
                Debug.LogError("[UIKeys] 读不到 ConfigToolsSettings 设置资源。");
                return false;
            }

            string outputPath = settings.UIKeysOutputPath;
            string className = settings.UIKeysClassName;
            string namespaceName = settings.UIKeysNamespace;

            if (string.IsNullOrWhiteSpace(outputPath) ||
                string.IsNullOrWhiteSpace(className) ||
                string.IsNullOrWhiteSpace(namespaceName))
            {
                Debug.LogError($"[UIKeys] 输出路径 / 类名 / 命名空间不能为空，请在 {ConfigToolsSettings.DefaultAssetPath} 里补全。");
                return false;
            }

            if (!TryReadEntries(out var entries))
            {
                return false;
            }

            if (entries.Count == 0)
            {
                Debug.LogWarning("[UIKeys] UI 配置表里没有任何记录");
                return false;
            }

            string scriptContent = BuildScript(entries, className, namespaceName);
            string directory = Path.GetDirectoryName(outputPath);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(outputPath, scriptContent, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(outputPath);

            Debug.Log($"[UIKeys] 生成成功: {outputPath}\n共 {entries.Count} 条 UI 界面 ID 常量。");
            return true;
        }

        private static bool TryReadEntries(out List<UIPageEntry> entries)
        {
            entries = new List<UIPageEntry>();

            if (!TryFindConfiguration(out UIPageConfiguration configuration))
            {
                return false;
            }

            if (configuration.DataList == null)
            {
                return true;
            }

            var seenIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (UIPageData data in configuration.DataList)
            {
                if (data == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(data.PageID))
                {
                    Debug.LogError("[UIKeys] 有记录的 PageID 为空，请先补全 UI 配置表。", configuration);
                    return false;
                }

                // PageID 是配置表的字典键，重复会让界面打开的是另一个预制体，
                // 这里提前拦下来，报错比生成一份错的常量类好。
                if (!seenIds.Add(data.PageID))
                {
                    Debug.LogError($"[UIKeys] PageID 重复: {data.PageID}，请改成唯一值。", configuration);
                    return false;
                }

                entries.Add(new UIPageEntry
                {
                    PageId = data.PageID,
                    Description = data.Description
                });
            }

            return true;
        }

        /// <summary>
        /// 全工程找 UI 配置表资产。约定只有一份，多份的话不知道该按哪份生成，直接报错。
        /// </summary>
        private static bool TryFindConfiguration(out UIPageConfiguration configuration)
        {
            configuration = null;
            string[] guids = AssetDatabase.FindAssets($"t:{nameof(UIPageConfiguration)}");

            if (guids.Length == 0)
            {
                Debug.LogError($"[UIKeys] 工程里没有 {nameof(UIPageConfiguration)} 资产。"
                               + "\n请右键 Create > Configs > UI > UIPageConfiguration 创建一份，"
                               + "并把 UISystem 的“UI配置表路径”指向它。");
                return false;
            }

            if (guids.Length > 1)
            {
                string paths = string.Join("\n", Array.ConvertAll(guids, AssetDatabase.GUIDToAssetPath));
                Debug.LogError($"[UIKeys] 找到多份 {nameof(UIPageConfiguration)} 资产，不确定该用哪份:\n{paths}");
                return false;
            }

            string assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            configuration = AssetDatabase.LoadAssetAtPath<UIPageConfiguration>(assetPath);

            if (configuration == null)
            {
                Debug.LogError($"[UIKeys] UI 配置表资产加载失败: {assetPath}");
                return false;
            }

            return true;
        }

        private static string BuildScript(List<UIPageEntry> entries, string className, string namespaceName)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("// ------------------------------------------------------------------------------");
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("//     此文件由 UIKeysGenerator 自动生成。");
            sb.AppendLine("//     请不要手动修改，重新生成会覆盖内容。");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine("// ------------------------------------------------------------------------------");
            sb.AppendLine();
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");
            sb.AppendLine($"    public static class {className}");
            sb.AppendLine("    {");

            var usedNames = new HashSet<string>(StringComparer.Ordinal);

            // 保持配置表顺序，生成结果和表格逐行对应，diff 更好读。
            foreach (UIPageEntry entry in entries)
            {
                string constName = MakeUniqueName(ToConstName(entry.PageId), usedNames);
                string summary = string.IsNullOrWhiteSpace(entry.Description)
                    ? entry.PageId
                    : $"{entry.Description} ({entry.PageId})";

                sb.AppendLine("        /// <summary>");
                sb.AppendLine($"        /// {EscapeXmlDoc(summary)}");
                sb.AppendLine("        /// </summary>");
                sb.AppendLine($"        public const string {constName} = \"{EscapeString(entry.PageId)}\";");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>
        /// 把 PageID 转成合法的 C# 标识符，例如 Pop_Loading_UI -> PopLoadingUI。
        /// </summary>
        private static string ToConstName(string pageId)
        {
            StringBuilder sb = new StringBuilder();
            bool nextUpper = true;

            foreach (char c in pageId)
            {
                if (char.IsLetterOrDigit(c))
                {
                    // 标识符不能以数字开头。
                    if (sb.Length == 0 && char.IsDigit(c))
                    {
                        sb.Append('_');
                    }

                    sb.Append(nextUpper ? char.ToUpperInvariant(c) : c);
                    nextUpper = false;
                }
                else
                {
                    nextUpper = true;
                }
            }

            return sb.Length == 0 ? "Unnamed" : sb.ToString();
        }

        private static string MakeUniqueName(string baseName, HashSet<string> usedNames)
        {
            if (usedNames.Add(baseName))
            {
                return baseName;
            }

            int index = 2;

            while (!usedNames.Add(baseName + index))
            {
                index++;
            }

            return baseName + index;
        }

        private static string EscapeString(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }

        private static string EscapeXmlDoc(string value)
        {
            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }

        private class UIPageEntry
        {
            public string PageId;
            public string Description;
        }
    }
}

#endif
