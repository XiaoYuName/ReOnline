using System;
using UnityEngine;
using UnityEngine.EventSystems;
using XFramework;
using CharacterJobConfig = XFramework.CharacterJob;

/// <summary>
/// 创建角色界面上排的一个**角色头像格子**，一个角色（<c>CharacterJob</c>）一个。
/// 由 <see cref="CreatCharacterUI"/> 按可创建的角色列表动态生成到 HeadContent 下。
///
/// 显示角色头像（取该角色**基础形态**的 <c>IconKey</c>）和角色名。
/// 选中态是单选，由父面板统一管 —— 格子被点了只 <see cref="Clicked"/> 喊一声。
///
/// 点击不靠 Button：根节点下的 icon / Background 都是 raycast 目标，
/// 事件会冒泡到这里，所以直接实现 <see cref="IPointerClickHandler"/>，不用改美术搭好的预制体。
/// </summary>
public partial class CharacterHeadSlot : UIBase, IPointerClickHandler
{
    /// <summary>这个格子被点了。参数是自己，父面板据此做单选。</summary>
    public event Action<CharacterHeadSlot> Clicked;

    /// <summary>这个格子代表的角色配置。</summary>
    public CharacterJobConfig Job { get; private set; }

    public override void Init()
    {
        InitAutoBind();

        SetSelected(false);
    }

    /// <summary>
    /// 摆上一个角色。
    /// <paramref name="iconKey"/> 是该角色基础形态的头像路径 —— 头像挂在**形态**上不在角色上，
    /// 所以要父面板查好了传进来，格子自己不去翻形态表。
    /// </summary>
    public void SetJob(CharacterJobConfig job, string iconKey)
    {
        Job = job;

        name.text = job != null ? job.Name : string.Empty;

        if (string.IsNullOrEmpty(iconKey))
        {
            icon.gameObject.SetActive(false);
            return;
        }

        var sprite = LoadAsset<Sprite>(iconKey);

        if (sprite == null)
        {
            Debug.LogWarning($"[HeadSlot] 头像加载不出来：{iconKey}", this);
            icon.gameObject.SetActive(false);
            return;
        }

        icon.gameObject.SetActive(true);
        icon.sprite = sprite;
    }

    /// <summary>选中框的显隐。单选由父面板保证，这里只管画。</summary>
    public void SetSelected(bool value)
    {
        if (selected != null)
        {
            selected.gameObject.SetActive(value);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        Clicked?.Invoke(this);
    }
}
