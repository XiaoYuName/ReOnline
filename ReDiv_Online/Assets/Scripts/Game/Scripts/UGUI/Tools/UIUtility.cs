using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework;

public static class UIUtility
{
    public static void FadeIn(float time,FadeLayer layer = FadeLayer.All,int OrderInLayer = 60)
    {
        var loadingUI = UISystem.Instance.OpenUI<PopLoadingUI>("PopLoadingUI");
        loadingUI.FadeIn(time,layer,OrderInLayer);
    }
    
    public static void FadeOut(float time,FadeLayer layer = FadeLayer.All,int OrderInLayer = 60)
    {
        var loadingUI = UISystem.Instance.OpenUI<PopLoadingUI>("PopLoadingUI");
        loadingUI.FadeOut(time,layer, OrderInLayer);
    }
    
    public static async UniTask FadeInAsync(float time,FadeLayer layer = FadeLayer.All,int OrderInLayer = 60)
    {
        var loadingUI = UISystem.Instance.OpenUI<PopLoadingUI>("PopLoadingUI");
        await loadingUI.FadeInAsync(time,layer, OrderInLayer);
    }
    public static async UniTask FadeOutAsync(float time,FadeLayer layer = FadeLayer.All,int OrderInLayer = 60)
    {
        var loadingUI = UISystem.Instance.OpenUI<PopLoadingUI>("PopLoadingUI");
        await loadingUI.FadeOutAsync(time,layer, OrderInLayer);
    }
    
    
    public static async UniTask FadeAsync(float time, Func<UniTask> action,FadeLayer layer = FadeLayer.All,int OrderInLayer = 60)
    {
        var loadingUI = UISystem.Instance.OpenUI<PopLoadingUI>("PopLoadingUI");
        await loadingUI.FadeAsync(time, action,layer, OrderInLayer);
    }
    
    public static async UniTask FadeAsync(float time, Action action,FadeLayer layer = FadeLayer.All,int OrderInLayer = 60)
    {
        var loadingUI = UISystem.Instance.OpenUI<PopLoadingUI>("PopLoadingUI");
        await loadingUI.FadeAsync(time, () => action(),layer, OrderInLayer);
    }
    
    public static async UniTask FadeAsync(float time, List<UniTask> actions,FadeLayer layer = FadeLayer.All,int OrderInLayer = 60)
    {
        var loadingUI = UISystem.Instance.OpenUI<PopLoadingUI>("PopLoadingUI");
        await loadingUI.FadeAsync(time, actions, layer, OrderInLayer);
    }
    
    
    /// <summary>
    /// 显示对话框
    /// </summary>
    /// <param name="content">文本内容</param>
    /// <param name="title">标题</param>
    /// <param name="actionTex">确定按钮</param>
    /// <param name="cancelTex">取消按钮</param>
    /// <param name="action">确定按钮回调</param>
    /// <param name="cancel">取消按钮回调</param>
    public static void ShowDialogue(string content, string title = "提示", string actionTex = "确定", string cancelTex = "取消",
        Action action = null, Action cancel = null)
    {
       var ui =  UISystem.Instance.OpenUI<PopDialogueUI>(UIKeys.PopDialogueUI);
       ui?.ShowDialogue(content, title, actionTex, cancelTex, action, cancel);
    }

    /// <summary>
    /// 显示提示框
    /// </summary>
    /// <param name="content">文本内容</param>
    /// <param name="title">标题</param>
    /// <param name="actionTex">确定文本</param>
    /// <param name="action">确定回调</param>
    public static void ShowWindow(string content, string title = "提示", string actionTex = "确定", Action action = null)
    {
        var ui =  UISystem.Instance.OpenUI<PopDialogueUI>(UIKeys.PopDialogueUI);
        ui?.ShowWindow(content, title, actionTex, action);
    }
    

    
    
    

}
