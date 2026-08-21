using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Tables;


public sealed class LocalizationKeySelectorDrawer : OdinAttributeDrawer<LocalizationKeySelectorAttribute, string>
{
    protected override void DrawPropertyLayout(GUIContent label)
    {
        var tableName = GetTableName();

        EditorGUILayout.BeginVertical();

        EditorGUILayout.BeginHorizontal();

        ValueEntry.SmartValue = EditorGUILayout.TextField(label, ValueEntry.SmartValue);

        GUI.enabled = !string.IsNullOrEmpty(tableName);

        if (GUILayout.Button("选择", GUILayout.Width(55)))
        {
            LocalizationKeySelectorWindow.Open(
                tableName,
                ValueEntry.SmartValue,
                key =>
                {
                    ValueEntry.SmartValue = key;
                    ValueEntry.ApplyChanges();
                });
        }

        GUI.enabled = true;

        EditorGUILayout.EndHorizontal();

        DrawPreview(tableName, ValueEntry.SmartValue);

        EditorGUILayout.EndVertical();
    }

    private string GetTableName()
    {
        var parent = Property.ParentValueProperty;
        if (parent == null)
            return null;

        var parentValue = parent.ValueEntry?.WeakSmartValue;
        if (parentValue == null)
            return null;

        var type = parentValue.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        var field = type.GetField(Attribute.TableFieldName, flags);
        if (field != null && field.FieldType == typeof(string))
            return field.GetValue(parentValue) as string;

        var property = type.GetProperty(Attribute.TableFieldName, flags);
        if (property != null && property.PropertyType == typeof(string))
            return property.GetValue(parentValue) as string;

        return null;
    }

    private void DrawPreview(string tableName, string key)
    {
        if (string.IsNullOrEmpty(tableName) || string.IsNullOrEmpty(key))
            return;

        string preview = LocalizationKeySelectorUtility.GetPreviewText(tableName, key);

        if (string.IsNullOrEmpty(preview))
        {
            EditorGUILayout.HelpBox("当前 Key 没有找到对应文本", MessageType.Warning);
            return;
        }

        EditorGUILayout.HelpBox(preview, MessageType.None);
    }
}

public sealed class LocalizationKeySelectorWindow : EditorWindow
{
    private const float RowHeight = 24f;

    private string tableName;
    private string searchText;
    private string currentKey;

    private Vector2 scroll;
    private Action<string> onSelected;

    private List<LocalizationKeyItem> allItems = new();
    private List<LocalizationKeyItem> filteredItems = new();

    public static void Open(string tableName, string currentKey, Action<string> onSelected)
    {
        var window = CreateInstance<LocalizationKeySelectorWindow>();

        window.titleContent = new GUIContent("选择本地化Key");
        window.tableName = tableName;
        window.currentKey = currentKey;
        window.onSelected = onSelected;
        window.searchText = string.Empty;

        window.minSize = new Vector2(760, 520);
        window.LoadData();
        window.ShowUtility();
    }

    private void OnGUI()
    {
        DrawToolbar();
        DrawInfo();
        DrawList();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        GUILayout.Label("搜索", GUILayout.Width(35));

        GUI.SetNextControlName("SearchField");
        string newSearch = GUILayout.TextField(searchText, EditorStyles.toolbarSearchField);

        if (newSearch != searchText)
        {
            searchText = newSearch;
            RefreshFilter();
        }

        if (GUILayout.Button("清空", EditorStyles.toolbarButton, GUILayout.Width(50)))
        {
            searchText = string.Empty;
            RefreshFilter();
            GUI.FocusControl("SearchField");
        }

        if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(50)))
        {
            LoadData();
        }

        EditorGUILayout.EndHorizontal();

        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.FocusTextInControl("SearchField");
        }
    }

    private void DrawInfo()
    {
        EditorGUILayout.Space(4);

        EditorGUILayout.LabelField("当前表", tableName, EditorStyles.boldLabel);
        EditorGUILayout.LabelField("数量", $"{filteredItems.Count} / {allItems.Count}");

        if (!string.IsNullOrEmpty(currentKey))
        {
            EditorGUILayout.LabelField("当前Key", currentKey);
        }

        EditorGUILayout.Space(4);
    }

    private void DrawList()
    {
        if (filteredItems.Count <= 0)
        {
            EditorGUILayout.HelpBox("没有找到匹配的 Key。可以搜索 Key 或文本内容。", MessageType.Info);
            return;
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);

        foreach (var item in filteredItems)
        {
            DrawRow(item);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawRow(LocalizationKeyItem item)
    {
        Rect rect = EditorGUILayout.GetControlRect(false, RowHeight);

        bool isCurrent = item.Key == currentKey;

        if (isCurrent)
        {
            EditorGUI.DrawRect(rect, new Color(0.25f, 0.45f, 0.85f, 0.35f));
        }
        else if (rect.Contains(Event.current.mousePosition))
        {
            EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.08f));
        }

        Rect keyRect = new Rect(rect.x + 6, rect.y + 3, 220, rect.height);
        Rect textRect = new Rect(rect.x + 235, rect.y + 3, rect.width - 300, rect.height);
        Rect buttonRect = new Rect(rect.xMax - 58, rect.y + 2, 52, rect.height - 4);

        EditorGUI.LabelField(keyRect, item.Key, EditorStyles.boldLabel);
        EditorGUI.LabelField(textRect, item.Preview);

        if (GUI.Button(buttonRect, "选择"))
        {
            Select(item.Key);
        }

        if (Event.current.type == EventType.MouseDown &&
            Event.current.clickCount == 2 &&
            rect.Contains(Event.current.mousePosition))
        {
            Select(item.Key);
            Event.current.Use();
        }
    }

    private void Select(string key)
    {
        onSelected?.Invoke(key);
        Close();
    }

    private void LoadData()
    {
        allItems.Clear();

        var collection = LocalizationEditorSettings.GetStringTableCollection(tableName);

        if (collection == null || collection.SharedData == null)
        {
            RefreshFilter();
            return;
        }

        var tables = collection.StringTables.ToList();
        var previewTable = LocalizationKeySelectorUtility.GetPreferredTable(tables);

        foreach (var sharedEntry in collection.SharedData.Entries)
        {
            if (sharedEntry == null || string.IsNullOrEmpty(sharedEntry.Key))
                continue;

            string preview = LocalizationKeySelectorUtility.FormatPreview(
                previewTable?.GetEntry(sharedEntry.Key)?.LocalizedValue ?? string.Empty);

            // 搜索需要匹配所有语言的文本（而非只有预览用的那一张表），
            // 否则例如预览取到繁体表时，输入简体中文会搜不到。
            string allTexts = string.Join(" ", tables
                .Select(t => t.GetEntry(sharedEntry.Key)?.LocalizedValue)
                .Where(v => !string.IsNullOrEmpty(v)));

            allItems.Add(new LocalizationKeyItem
            {
                Key = sharedEntry.Key,
                Preview = preview,
                SearchBlob = (sharedEntry.Key + " " + allTexts).ToLower()
            });
        }

        allItems = allItems
            .OrderBy(x => x.Key)
            .ToList();

        RefreshFilter();
    }

    private void RefreshFilter()
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            filteredItems = allItems;
            Repaint();
            return;
        }

        string lower = searchText.ToLower();

        filteredItems = allItems
            .Where(x => x.SearchBlob.Contains(lower))
            .ToList();

        Repaint();
    }

    private sealed class LocalizationKeyItem
    {
        public string Key;
        public string Preview;
        public string SearchBlob;
    }
}

public static class LocalizationKeySelectorUtility
{
    public static string GetPreviewText(string tableName, string key)
    {
        var collection = LocalizationEditorSettings.GetStringTableCollection(tableName);

        if (collection == null)
            return null;

        var table = GetPreferredTable(collection.StringTables.ToList());

        if (table == null)
            return null;

        var entry = table.GetEntry(key);

        return entry == null
            ? null
            : FormatPreview(entry.LocalizedValue);
    }

    /// <summary>
    /// 表内多语言的顺序不固定，直接取 FirstOrDefault 可能拿到非中文的表，
    /// 导致预览/搜索用的文本和中文输入对不上。优先取简体中文，其次任意中文，最后兜底第一张表。
    /// </summary>
    public static StringTable GetPreferredTable(List<StringTable> tables)
    {
        if (tables == null || tables.Count == 0)
            return null;

        return tables.FirstOrDefault(t => t.LocaleIdentifier.Code.Equals("zh-CN", StringComparison.OrdinalIgnoreCase))
            ?? tables.FirstOrDefault(t => t.LocaleIdentifier.Code.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            ?? tables.FirstOrDefault();
    }

    public static string FormatPreview(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        value = value
            .Replace("\r", "")
            .Replace("\n", " ")
            .Trim();

        if (value.Length > 80)
            value = value.Substring(0, 80) + "...";

        return value;
    }
}
