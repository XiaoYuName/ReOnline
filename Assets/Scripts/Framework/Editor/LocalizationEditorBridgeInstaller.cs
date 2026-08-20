using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Localization;

namespace XFramework.Editor
{
    /// <summary>
    /// 把 <see cref="LocalizationEditorBridge"/> 的委托接到 Unity 本地化包的编辑器 API 上。
    /// 运行时程序集引用不到 Unity.Localization.Editor,所以查表这一段只能放在编辑器程序集里。
    /// </summary>
    [InitializeOnLoad]
    internal static class LocalizationEditorBridgeInstaller
    {
        static LocalizationEditorBridgeInstaller()
        {
            LocalizationEditorBridge.GetTableNames = GetTableNames;
            LocalizationEditorBridge.HasKey = HasKey;
            LocalizationEditorBridge.GetPreviewText = GetPreviewText;
            LocalizationEditorBridge.PingTable = PingTable;
        }

        private static void PingTable(string table)
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection(table);

            if (collection != null)
            {
                Selection.activeObject = collection;
                EditorGUIUtility.PingObject(collection);
            }
        }

        private static IReadOnlyList<string> GetTableNames()
        {
            return LocalizationEditorSettings
                .GetStringTableCollections()
                .Where(c => c != null)
                .Select(c => c.TableCollectionName)
                .Distinct()
                .OrderBy(x => x)
                .ToList();
        }

        private static bool HasKey(string table, string key)
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection(table);

            if (collection == null || collection.SharedData == null)
                return false;

            return collection.SharedData.Entries.Any(e => e.Key == key);
        }

        private static string GetPreviewText(string table, string key)
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection(table);

            if (collection == null)
                return "未找到本地化表";

            // 多语言表顺序不固定，FirstOrDefault 可能拿到非中文表；优先选简体中文表。
            var tables = collection.StringTables;
            var stringTable =
                tables.FirstOrDefault(t => t.LocaleIdentifier.Code.Equals("zh-CN", StringComparison.OrdinalIgnoreCase))
                ?? tables.FirstOrDefault(t => t.LocaleIdentifier.Code.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                ?? tables.FirstOrDefault();

            if (stringTable == null)
                return "当前表没有语言内容";

            var entry = stringTable.GetEntry(key);

            if (entry == null)
                return "未找到 Key 对应文本";

            return entry.LocalizedValue;
        }
    }
}
