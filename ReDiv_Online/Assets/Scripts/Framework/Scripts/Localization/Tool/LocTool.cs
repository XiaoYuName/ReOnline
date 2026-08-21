using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Settings;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Events;
#endif


public static class LocTool
{
    // 同步取本地化串（无变量）；调用时表须已加载完成（运行时格子/面板构建时机通常已就绪）
    public static string Get(string table, string key) => LocalizationSettings.StringDatabase.GetLocalizedString(table, key);
    
}
