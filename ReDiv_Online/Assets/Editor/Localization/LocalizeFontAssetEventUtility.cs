using System;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Localization.Components;

public static class LocalizeFontAssetEventUtility
{
    private const string FontTableName = "FontAsset";
    private const string DefaultFontEntryName = "DeftualFonts";

    [MenuItem("CONTEXT/TMP_Text/Localize With Font Asset")]
    private static void LocalizeTMProText(MenuCommand command)
    {
        if (command.context is not TMP_Text target)
        {
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Localize TMP Text");

        LocalizeStringEvent stringEvent = GetOrAddComponent<LocalizeStringEvent>(target.gameObject);
        EnsurePersistentPropertyBinding(
            stringEvent,
            stringEvent.OnUpdateString,
            target,
            nameof(TMP_Text.text),
            UnityEventCallState.EditorAndRuntime);

        LocalizationFontAssetsEvent fontEvent = GetOrAddComponent<LocalizationFontAssetsEvent>(target.gameObject);
        EnsureDefaultFontReference(fontEvent);
        EnsurePersistentPropertyBinding(
            fontEvent,
            fontEvent.OnUpdateAsset,
            target,
            nameof(TMP_Text.font),
            UnityEventCallState.RuntimeOnly);

        Undo.CollapseUndoOperations(undoGroup);
    }

    private static T GetOrAddComponent<T>(GameObject gameObject) where T : Component
    {
        return gameObject.TryGetComponent(out T component)
            ? component
            : Undo.AddComponent<T>(gameObject);
    }

    private static void EnsureDefaultFontReference(LocalizationFontAssetsEvent fontEvent)
    {
        if (!fontEvent.AssetReference.IsEmpty)
        {
            return;
        }

        Undo.RecordObject(fontEvent, "Set Default Localized Font");
        fontEvent.AssetReference.SetReference(FontTableName, DefaultFontEntryName);
        EditorUtility.SetDirty(fontEvent);
    }

    private static void EnsurePersistentPropertyBinding<T>(
        Component eventComponent,
        UnityEvent<T> unityEvent,
        TMP_Text target,
        string propertyName,
        UnityEventCallState callState)
    {
        string methodName = $"set_{propertyName}";
        int listenerIndex = FindPersistentListener(unityEvent, target, methodName);

        Undo.RecordObject(eventComponent, $"Bind Localized {propertyName}");

        if (listenerIndex < 0)
        {
            var setter = target.GetType().GetProperty(propertyName)?.GetSetMethod();
            if (setter == null)
            {
                Debug.LogError($"[Localization] 无法绑定 {target.name} 的 {propertyName} 属性。", target);
                return;
            }

            var action = (UnityAction<T>)Delegate.CreateDelegate(typeof(UnityAction<T>), target, setter);
            UnityEventTools.AddPersistentListener(unityEvent, action);
            listenerIndex = unityEvent.GetPersistentEventCount() - 1;
        }

        unityEvent.SetPersistentListenerState(listenerIndex, callState);
        EditorUtility.SetDirty(eventComponent);
    }

    private static int FindPersistentListener(
        UnityEventBase unityEvent,
        UnityEngine.Object target,
        string methodName)
    {
        for (int i = 0; i < unityEvent.GetPersistentEventCount(); i++)
        {
            if (unityEvent.GetPersistentTarget(i) == target &&
                unityEvent.GetPersistentMethodName(i) == methodName)
            {
                return i;
            }
        }

        return -1;
    }
}
