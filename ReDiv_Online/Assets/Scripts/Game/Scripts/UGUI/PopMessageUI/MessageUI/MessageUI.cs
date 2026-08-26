using UnityEngine;
using UnityEngine.UI;
using XFramework;

/// <summary>
/// 聊天界面（<see cref="PopMessageUI"/>）列表里的**一条消息**。
/// 预制体 <c>Assets/AddressableAssets/Remote/Prefabs/UGUI/PopMessageUI/MessageUI.prefab</c>，
/// 由 <see cref="PopMessageUI"/> 按当前页签的消息动态生成到 <c>Scroll View/Viewport/Content</c> 下。
///
/// 和城镇主界面底部那个 <c>MessageSlot</c> 的区别：这个是**带头像的大条目**
/// （头像 + 名字 + 气泡框里的正文），那个是定高一行的滚动日志。两边都要，不要互相替代。
///
/// ⚠️ AutoBind 里那个字段叫 <c>name</c>，**遮住了 <c>Component.name</c>** ——
/// 在这个类里写 <c>name</c> 拿到的是那个 TMP，不是物体名字。要物体名字得写
/// <c>gameObject.name</c>。（这是 AutoBind 按节点名生成字段的副作用，别在这里"顺手改好"，
/// 重新生成又会变回来。）
/// </summary>
public partial class MessageUI : UIBase
{
    public override void Init()
    {
        InitAutoBind();

        // 在这里写其它初始化逻辑。重新生成 UI 绑定时，这个文件不会被覆盖。
    }

    /// <summary>
    /// 摆一条消息。
    ///
    /// <paramref name="avatar"/> 传 null 是合法的：那个角色的形态没配头像、
    /// 或者配置表里那一格是空的。这时候把头像**隐藏**而不是留一张空白 Image ——
    /// 空 Image 会显示成一块白色方块，比没有更难看。
    /// </summary>
    public void SetMessage(string sender, string content, Sprite avatar)
    {
        if (name != null)
        {
            name.text = sender ?? string.Empty;
        }

        if (desc != null)
        {
            desc.text = content ?? string.Empty;
        }

        if (icon == null)
        {
            return;
        }

        icon.gameObject.SetActive(avatar != null);

        if (avatar != null)
        {
            icon.sprite = avatar;
        }
    }
}
