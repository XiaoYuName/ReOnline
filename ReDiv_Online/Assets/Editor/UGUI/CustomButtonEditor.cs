using UnityEditor;//编辑器类在UnityEditor命名空间下。所以当使用C#脚本时，你需要在脚本前面加上 "using UnityEditor"引用。
using UnityEditor.UI;//ButtonEditor位于此命名空间下


[CustomEditor(typeof(CustomButton),true)]
//使用了 SerializedObject 和 SerializedProperty 系统，因此，可以自动处理“多对象编辑”，“撤销undo” 和 “预制覆盖prefab override”。
[CanEditMultipleObjects]
public class CustomButtonEditor : ButtonEditor
{
    //对应我们在MyButton中创建的字段
    //PS:需要注意一点，使用SerializedProperty 必须在MyButton类_newNumber字段前加[SerializeField]
    private SerializedProperty isTweenerScale;
    
    private SerializedProperty tweenerScale;
    
    private SerializedProperty tweenerDuration;
    
    private SerializedProperty tweenerEase;
    
    private SerializedProperty ButtonText;
    
    private SerializedProperty EnterColor;
    
    private SerializedProperty ExitColor;
    
    private SerializedProperty OnPointEnter;
    private SerializedProperty OnPointExit;
    private SerializedProperty OnPointUp;
    private SerializedProperty OnPointDown;
    
    
    protected override void OnEnable()
    {
        base.OnEnable();
        isTweenerScale = serializedObject.FindProperty("isTweenerScale");
        
        tweenerScale = serializedObject.FindProperty("tweenerScale");
        tweenerDuration = serializedObject.FindProperty("tweenerDuration");
        tweenerEase = serializedObject.FindProperty("tweenerEase");
        OnPointEnter = serializedObject.FindProperty("OnPointEnter");
        OnPointExit = serializedObject.FindProperty("OnPointExit");
        OnPointUp = serializedObject.FindProperty("OnPointUp");
        OnPointDown = serializedObject.FindProperty("OnPointDown");
        ButtonText = serializedObject.FindProperty("ButtonText");
        EnterColor = serializedObject.FindProperty("EnterColor");
        ExitColor = serializedObject.FindProperty("ExitColor");
        
    }
    //并且特别注意，如果用这种序列化方式，需要在 OnInspectorGUI 开头和结尾各加一句 serializedObject.Update();  serializedObject.ApplyModifiedProperties();
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        EditorGUILayout.Space();//空行
        serializedObject.Update();
        EditorGUILayout.PropertyField(isTweenerScale);//显示我们创建的属性
        EditorGUILayout.PropertyField(tweenerScale);//显示我们创建的属性
        EditorGUILayout.PropertyField(tweenerDuration);//显示我们创建的属性
        EditorGUILayout.PropertyField(tweenerEase);//显示我们创建的属性
        EditorGUILayout.PropertyField(OnPointEnter);//显示我们创建的属性
        EditorGUILayout.PropertyField(OnPointExit);//显示我们创建的属性
        EditorGUILayout.PropertyField(OnPointUp);//显示我们创建的属性
        EditorGUILayout.PropertyField(OnPointDown);//显示我们创建的属性
        EditorGUILayout.PropertyField(ButtonText);
        EditorGUILayout.PropertyField(EnterColor);
        EditorGUILayout.PropertyField(ExitColor);
        serializedObject.ApplyModifiedProperties();
    }

}
