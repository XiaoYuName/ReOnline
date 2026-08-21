using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEngine;
using XFramework;


#if UNITY_LOCALIZATION
using UnityEngine.Localization;
#endif

[Serializable]
[InlineProperty]
[HideLabel]
public class LocalSelectedData
{
#if UNITY_EDITOR
    [HorizontalGroup("LocalRow", Width = 0.35f)]
    [LabelText("表")]
    [ValueDropdown(nameof(GetLocalTables))]
    [OnValueChanged(nameof(OnTableChanged))]
#endif
    public string Table;

#if UNITY_EDITOR
    [HorizontalGroup("LocalRow", Width = 0.65f)]
    [LabelText("Key")]
    [EnableIf(nameof(HasTable))]
    [LocalizationKeySelector(nameof(Table))]
    [ValidateInput(nameof(IsKeyValid), "当前表中不存在这个 Key")]
#endif
    public string Value;

#if UNITY_EDITOR
    [ShowInInspector]
    [ReadOnly]
    [LabelText("文本预览")]
    [MultiLineProperty(3)]
    [ShowIf(nameof(HasValue))]
    private string PreviewText => GetPreviewText();
#endif

    public bool IsValid()
    {
        return !string.IsNullOrEmpty(Table) &&
               !string.IsNullOrEmpty(Value);
    }

#if UNITY_LOCALIZATION
    public LocalizedString ToLocalizedString()
    {
        return new LocalizedString(Table, Value);
    }
#endif

    public override string ToString()
    
    
    {
        if (string.IsNullOrEmpty(Table) || string.IsNullOrEmpty(Value))
            return "Null";

        return $"{Table}/{Value}";
    }

#if UNITY_EDITOR
    private bool HasTable()
    {
        return !string.IsNullOrEmpty(Table);
    }

    private bool HasValue()
    {
        return !string.IsNullOrEmpty(Table) &&
               !string.IsNullOrEmpty(Value);
    }

    private void OnTableChanged()
    {
        Value = null;
    }

    // 下面三个都是 Odin 特性按名字回调的方法，必须留在本类里；
    // 具体的查表实现在 UnityFramework.Editor 里，通过 LocalizationEditorBridge 接进来。

    private IEnumerable GetLocalTables()
    {
        return LocalizationEditorBridge.GetTableNames?.Invoke() ?? Array.Empty<string>();
    }

    private bool IsKeyValid(string key)
    {
        if (string.IsNullOrEmpty(Table) || string.IsNullOrEmpty(key))
            return true;

        // 桥还没接上时一律放行，不然编辑器刚启动那一下所有 Key 都会被标红。
        return LocalizationEditorBridge.HasKey?.Invoke(Table, key) ?? true;
    }

    private string GetPreviewText()
    {
        if (string.IsNullOrEmpty(Table) || string.IsNullOrEmpty(Value))
            return string.Empty;

        return LocalizationEditorBridge.GetPreviewText?.Invoke(Table, Value) ?? string.Empty;
    }

    [Button("打开本地化表")]
    [ShowIf(nameof(HasTable))]
    private void OpenLocalizationTable()
    {
        LocalizationEditorBridge.PingTable?.Invoke(Table);
    }

    [Button("清空")]
    [ShowIf(nameof(HasValue))]
    private void Clear()
    {
        Value = null;
    }
#endif
}