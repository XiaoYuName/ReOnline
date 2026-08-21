#if UNITY_EDITOR
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;

[CustomEditor(typeof(UIAutoBindGenerator))]
public class UIAutoBindGeneratorEditor : OdinEditor
{
    public override void OnInspectorGUI()
    {
        EditorGUI.BeginChangeCheck();

        base.OnInspectorGUI();

        if (EditorGUI.EndChangeCheck())
        {
            MarkTargetDirty((UIAutoBindGenerator)target);
        }
    }

    private static void MarkTargetDirty(UIAutoBindGenerator generator)
    {
        if (generator == null)
        {
            return;
        }

        EditorUtility.SetDirty(generator);
        if (PrefabUtility.IsPartOfPrefabInstance(generator))
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(generator);
        }

        if (generator.gameObject.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
        }
    }
}
#endif
