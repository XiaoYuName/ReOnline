using TMPro;
using XFramework;

/// <summary>
/// 聊天框里的**一条消息**。由 <see cref="MainCommonUI"/> 按当前可见的消息列表
/// 动态生成到 <c>Message/Scroll View/Content</c> 下（那上面有
/// VerticalLayoutGroup + ContentSizeFitter，所以摆进去就自动从上往下排）。
///
/// 预制体：<c>Assets/AddressableAssets/Remote/Prefabs/UGUI/MainCommonUI/MessageSlot.prefab</c>
/// —— 根节点定高一行，子节点 <c>Name</c>（左侧，固定宽）+ <c>Message</c>（占满剩下的宽度）。
/// 两个都是 TextMeshProUGUI，都开了自动缩放，正文是**超出省略**（单行显示）。
/// 所以正文有长度上限（<see cref="ReDiv.Net.ChatValidation.MaxDisplayWidth"/>），
/// 否则长消息在这一行里会被直接截没。
///
/// ⚠️ **这个预制体上没有 UIAutoBindGenerator**，所以没有生成的 AutoBind 文件，
/// 两个字段是在 <see cref="Init"/> 里用框架自带的 <c>Get&lt;T&gt;(路径)</c> 手动取的
/// —— 那是既有做法（客户端文档第 5 节坑 4），不是漏了配。
/// </summary>
public class MessageSlot : UIBase
{
    /// <summary>发言者名字（预制体里带着冒号的那个样式：「西琳酱:」）。</summary>
    private TextMeshProUGUI senderText;

    /// <summary>消息正文。</summary>
    private TextMeshProUGUI contentText;

    public override void Init()
    {
        senderText = Get<TextMeshProUGUI>("Name");
        contentText = Get<TextMeshProUGUI>("Message");
    }

    /// <summary>
    /// 摆一条消息。冒号在这里补，**不要求调用方自己拼进名字里** ——
    /// 拼在名字里的话，名字和正文就没法各自上色 / 各自省略了。
    ///
    /// 两个字段都判空：<c>Get&lt;T&gt;</c> 取不到时返回 null 并已经报过一次错，
    /// 这里再抛一次 NRE 只会盖掉那句真正有用的报错。
    /// </summary>
    public void SetMessage(string sender, string content)
    {
        if (senderText != null)
        {
            senderText.text = string.IsNullOrEmpty(sender) ? string.Empty : sender + "：";
        }

        if (contentText != null)
        {
            contentText.text = content ?? string.Empty;
        }
    }
}
