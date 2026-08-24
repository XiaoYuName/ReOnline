using System.Collections.Generic;
using ReDiv.Net;
using ReDiv.Net.Bindings;
using UnityEngine;
using XFramework;

/// <summary>
/// 选人界面。
///
/// 三个按钮（AutoBind 的名字和界面上的字对应关系容易记反，这里写死）：
///   cancelButton  创建角色 —— 打开创建角色界面（那个界面本身还没做）
///   enterButton   进入游戏 —— **选中了角色才能点**，功能还没接
///   quitButton    退出     —— 关掉本界面，回主菜单 CommonUI
///
/// 数据全部来自 <see cref="CharacterManager"/>，这里不碰 Conn。
/// 格子是**预制体里摆好的固定若干个**（不是动态生成），所以刷新就是「把角色摆进前 N 个格子，
/// 其余置空」。
///
/// 选中是**单选**：格子自己不决定谁被选中，被点了只 <see cref="CharacterSlotUI.Clicked"/>
/// 喊一声，由这里统一取消别的、选中这个。
/// </summary>
public partial class SelectCharacterUI : UIBase
{
    /// <summary>当前选中的格子，没选就是 null。</summary>
    private CharacterSlotUI selectedSlot;

    public override void Init()
    {
        InitAutoBind();

        // 第三个参数是点击音效 ID，不传的话 AudioManager 会拿空 ID 去查表并报错（工程约定要传）
        Bind(cancelButton, OpenCreateCharacter, AudioKeys.CursorClick01);
        Bind(enterButton, EnterGame, AudioKeys.CursorClick01);
        Bind(quitButton, BackToCommon, AudioKeys.CursorClick01);

        foreach (CharacterSlotUI slot in content)
        {
            slot.Clicked += HandleSlotClicked;
        }
    }

    public override void Open()
    {
        base.Open();

        HookCharacterEvents();
        Refresh();
    }

    public override void Close()
    {
        UnhookCharacterEvents();
        base.Close();
    }

    protected override void OnDestroy()
    {
        UnhookCharacterEvents();

        // content 是 InitAutoBind 填的。界面预制体如果没走过 Init 就被销毁
        // （比如场景里手摆了一份、进 Play 时被清掉），这里是 null —— 必须判，
        // 否则 OnDestroy 里抛 NRE，而且那个报错和真正的原因看着毫无关系。
        if (content != null)
        {
            foreach (CharacterSlotUI slot in content)
            {
                if (slot != null)
                {
                    slot.Clicked -= HandleSlotClicked;
                }
            }
        }

        base.OnDestroy();
    }

    // ------------------------------------------------------------------
    // 数据
    // ------------------------------------------------------------------

    private bool hooked;

    /// <summary>
    /// CharacterManager 的事件是 C# 事件，重复挂会收到重复回调 —— 用一个标志位挡住。
    /// （账号那边 CommonUI 也是这么处理的。）
    /// </summary>
    private void HookCharacterEvents()
    {
        if (hooked)
        {
            return;
        }
        hooked = true;

        var characters = CharacterManager.Instance;
        characters.Ready += Refresh;
        characters.CharactersChanged += Refresh;
    }

    private void UnhookCharacterEvents()
    {
        if (!hooked)
        {
            return;
        }
        hooked = false;

        var characters = CharacterManager.Instance;
        characters.Ready -= Refresh;
        characters.CharactersChanged -= Refresh;
    }

    /// <summary>
    /// 重画所有格子。
    ///
    /// 可见格子数 = 账号已解锁的栏位数，但至少要能装下现有角色
    /// （万一以后做了「缩栏位」，也不能让已有角色凭空消失）。多出来的格子直接隐藏 ——
    /// 预制体里摆了 10 个，而账号默认只有 4 个栏位，全显出来玩家点第 5 个只会撞一句
    /// 「角色栏位已满」，不如不显。
    /// </summary>
    private void Refresh()
    {
        var manager = CharacterManager.Instance;
        IReadOnlyList<MyCharacterRow> list = manager.Characters;

        int usable = Mathf.Max((int)manager.CharacterSlots, list.Count);
        usable = Mathf.Min(usable, content.Count);

        ulong keepSelected = selectedSlot != null ? selectedSlot.CharacterId : 0;
        selectedSlot = null;

        for (int i = 0; i < content.Count; i++)
        {
            CharacterSlotUI slot = content[i];

            if (i >= usable)
            {
                slot.gameObject.SetActive(false);
                continue;
            }

            slot.gameObject.SetActive(true);

            if (i < list.Count)
            {
                slot.SetCharacter(list[i]);

                // 刷新前选中的那个角色还在的话，保持选中 —— 否则每次收到推送
                // （比如别人的等级变了触发整表重读）选中态都会被清掉
                if (keepSelected != 0 && list[i].CharacterId == keepSelected)
                {
                    selectedSlot = slot;
                }
            }
            else
            {
                slot.SetEmpty();
            }

            slot.SetSelected(selectedSlot == slot);
        }

        RefreshEnterButton();
    }

    // ------------------------------------------------------------------
    // 交互
    // ------------------------------------------------------------------

    /// <summary>单选：取消别的，选中这个。点空格子不做任何事。</summary>
    private void HandleSlotClicked(CharacterSlotUI slot)
    {
        if (slot == null || !slot.HasCharacter)
        {
            return;
        }

        foreach (CharacterSlotUI other in content)
        {
            other.SetSelected(other == slot);
        }

        selectedSlot = slot;
        RefreshEnterButton();
    }

    /// <summary>没选角色时「进入游戏」不可点。</summary>
    private void RefreshEnterButton()
    {
        enterButton.interactable = selectedSlot != null && selectedSlot.HasCharacter;
    }

    /// <summary>创建角色。那个界面本身还没做，这里只负责打开。</summary>
    private void OpenCreateCharacter()
    {
        UISystem.Instance.OpenUI(UIKeys.CreatCharacterUI);
    }

    /// <summary>
    /// 进入游戏。**功能还没接** —— 接的时候在这里调
    /// <c>SelectCharacter(selectedSlot.CharacterId)</c>，服务端写完 `character_selection`
    /// 才算进城镇（见服务端 README「角色系统」）。
    /// </summary>
    private void EnterGame()
    {
        if (selectedSlot == null || !selectedSlot.HasCharacter)
        {
            return;
        }

        Debug.Log($"[SelectCharacter] 进入游戏（未接）：characterId={selectedSlot.CharacterId}");
    }

    /// <summary>退出：关掉本界面，回主菜单。</summary>
    private void BackToCommon()
    {
        Close();
        UISystem.Instance.OpenUI(UIKeys.CommonUI);
    }
}
