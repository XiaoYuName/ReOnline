using System;
using XFramework;

public partial class PopDialogueUI : UIBase
{
    public override void Init()
    {
        InitAutoBind();
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
    public void ShowDialogue(string content, string title = "提示", string actionTex = "确定", string cancelTex = "取消",
        Action action = null, Action cancel = null)
    {
        desc.text = content;
        this.title.text = title;
        this.actionTex.text = actionTex;
        this.cancelTex.text  = cancelTex;
        cancelButton.gameObject.SetActive(true);
        Bind(actionButton, () =>
        {
            action?.Invoke();
            Close();
        },AudioKeys.CursorClick01);
        Bind(cancelButton, () =>
        {
            cancel?.Invoke();
            Close();
        },AudioKeys.CursorClick01);
    }

    /// <summary>
    /// 显示提示框
    /// </summary>
    /// <param name="content">文本内容</param>
    /// <param name="title">标题</param>
    /// <param name="actionTex">确定文本</param>
    /// <param name="action">确定回调</param>
    public void ShowWindow(string content, string title = "提示", string actionTex = "确定", Action action = null)
    {
        desc.text = content;
        this.title.text = title;
        this.actionTex.text = actionTex;
        cancelButton.gameObject.SetActive(false);
        Bind(actionButton, () =>
        {
            action?.Invoke();
            Close();
        },AudioKeys.CursorClick01);
        Bind(cancelButton, Close,AudioKeys.CursorClick01);
    }
}
