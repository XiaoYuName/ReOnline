using UnityEngine;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.SmartFormat.PersistentVariables;
using UnityEngine.UI;
using XFramework;

public class CommonUI : UIBase
{
    public override void Init()
    {
        UISystem.Instance.AddUI("CommonUI",this);
    }
    

    private void QuitButtonOnClick()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
        Application.Quit();
    }
}
