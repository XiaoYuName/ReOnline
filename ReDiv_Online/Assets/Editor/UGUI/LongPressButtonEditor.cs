using UnityEditor;
using UnityEditor.UI;
using UnityEngine;

[CustomEditor(typeof(LongPressButton), true)]
[CanEditMultipleObjects]
public class LongPressButtonEditor : ButtonEditor
{
    private SerializedProperty longPressDelay;
    private SerializedProperty initialRepeatInterval;
    private SerializedProperty minimumRepeatInterval;
    private SerializedProperty accelerationDuration;
    private SerializedProperty useUnscaledTime;
    private SerializedProperty suppressClickAfterLongPress;
    private SerializedProperty onLongPress;

    protected override void OnEnable()
    {
        base.OnEnable();
        longPressDelay = serializedObject.FindProperty("longPressDelay");
        initialRepeatInterval = serializedObject.FindProperty("initialRepeatInterval");
        minimumRepeatInterval = serializedObject.FindProperty("minimumRepeatInterval");
        accelerationDuration = serializedObject.FindProperty("accelerationDuration");
        useUnscaledTime = serializedObject.FindProperty("useUnscaledTime");
        suppressClickAfterLongPress = serializedObject.FindProperty("suppressClickAfterLongPress");
        onLongPress = serializedObject.FindProperty("onLongPress");
    }

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        serializedObject.Update();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Long Press Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(longPressDelay, new GUIContent("首次触发延迟"));
        EditorGUILayout.PropertyField(initialRepeatInterval, new GUIContent("初始触发间隔"));
        EditorGUILayout.PropertyField(minimumRepeatInterval, new GUIContent("最小触发间隔"));
        EditorGUILayout.PropertyField(accelerationDuration, new GUIContent("加速持续时间"));
        EditorGUILayout.PropertyField(useUnscaledTime, new GUIContent("忽略时间缩放"));
        EditorGUILayout.PropertyField(suppressClickAfterLongPress, new GUIContent("长按后屏蔽点击"));

        if (minimumRepeatInterval.floatValue > initialRepeatInterval.floatValue)
        {
            EditorGUILayout.HelpBox("最小触发间隔不能大于初始触发间隔。", MessageType.Warning);
        }

        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(onLongPress, new GUIContent("长按回调"));

        serializedObject.ApplyModifiedProperties();
    }
}
