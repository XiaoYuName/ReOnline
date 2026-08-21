
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework;

public partial class CommonUI : UIBase
{
    public override void Init()
    {
        InitAutoBind();

        // 在这里写其它初始化逻辑。重新生成 UI 绑定时，这个文件不会被覆盖。
        UISystem.Instance.AddUI("CommonUI",this);
        Bind(quitButton,QuitGame,AudioKeys.CursorClick01);
        Bind(settingsButton,OpenSettings,AudioKeys.CursorClick01);
        Bind(starGameClick,StartGame,AudioKeys.CursorClick01);
    }


    private void OpenSettings()
    {
        
    }

    private void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
        Application.Quit();
    }

    private void StartGame()
    {
        StartGameAsync().Forget();
    }

    private async UniTask StartGameAsync()
    {
        await UIUtility.FadeInAsync(1,FadeLayer.All);
        Close();
        await UIUtility.FadeOutAsync(1, FadeLayer.All);
        
    }
    
}
