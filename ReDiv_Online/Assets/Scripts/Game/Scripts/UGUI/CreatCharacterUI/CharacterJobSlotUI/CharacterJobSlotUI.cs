using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using XFramework;
using CharacterFormConfig = XFramework.CharacterForm;

/// <summary>
/// 创建角色界面右侧的一张**形态卡**。RightJobsPanel 里摆了两张：
///   上面那张 = 基础线（基础形态 → 一觉 → 二觉），左右箭头翻的是觉醒阶段
///   下面那张 = 爆发线（一个角色可以有多个爆发形态），左右箭头翻的是不同爆发形态
///
/// 卡上显示：略缩图（<c>UnitPlateIconKey</c>）、形态名、星级（点亮 <c>UnlockStar</c> 颗）。
///
/// 卡本身也有**选中态**：两张卡同时摆着，但立绘只能显示一个形态，所以要有「现在在看哪张卡」
/// 的概念 —— 点卡片或点它的箭头都会让它变成当前卡，父面板据此决定立绘显示哪一个。
/// </summary>
public partial class CharacterJobSlotUI : UIBase, IPointerClickHandler
{
    /// <summary>这张卡的当前形态变了（翻页或被选中）。参数是自己。</summary>
    public event Action<CharacterJobSlotUI> Changed;

    /// <summary>这张卡当前指向的形态。没有任何形态时是 null。</summary>
    public CharacterFormConfig Current =>
        forms != null && index >= 0 && index < forms.Count ? forms[index] : null;

    /// <summary>这张卡上一共有几个形态可翻。</summary>
    public int Count => forms?.Count ?? 0;

    private List<CharacterFormConfig> forms;
    private int index;

    /// <summary>
    /// 选中框。Selected 这个节点没被登记进 UIAutoBindGenerator 的绑定项，
    /// 所以 AutoBind 里没有它，这里手动 Get。哪天加进绑定项重新生成了就可以删掉这行。
    /// </summary>
    private UnityEngine.UI.Image selectedFrame;

    /// <summary>视觉上在左边的那个箭头（翻上一个）。</summary>
    private UnityEngine.UI.Button leftArrow;

    /// <summary>视觉上在右边的那个箭头（翻下一个）。</summary>
    private UnityEngine.UI.Button rightArrow;

    public override void Init()
    {
        InitAutoBind();

        selectedFrame = Get<UnityEngine.UI.Image>("Selected");

        // ⚠️ 按**实际位置**决定谁是左谁是右，不按名字。
        // 预制体里这两个节点的名字和位置是反的：LastArrowButton 在右边（x=+144）、
        // NextArrowButton 在左边（x=-129）。照名字接的话，点右箭头会往回翻。
        // 按位置接还有个好处：以后在预制体里把它俩挪来挪去，这里不用跟着改。
        bool lastIsOnLeft = lastArrowButton.transform is RectTransform lastRect &&
                            nextArrowButton.transform is RectTransform nextRect &&
                            lastRect.anchoredPosition.x < nextRect.anchoredPosition.x;

        leftArrow = lastIsOnLeft ? lastArrowButton : nextArrowButton;
        rightArrow = lastIsOnLeft ? nextArrowButton : lastArrowButton;

        Bind(leftArrow, ShowPrevious, AudioKeys.CursorClick01);
        Bind(rightArrow, ShowNext, AudioKeys.CursorClick01);

        SetSelected(false);
    }

    // ------------------------------------------------------------------
    // 数据
    // ------------------------------------------------------------------

    /// <summary>
    /// 把一条形态线摆上来，默认指向第一个。
    /// <paramref name="list"/> 由父面板排好序（基础线按星级、爆发线按 SortOrder）。
    /// </summary>
    public void SetForms(List<CharacterFormConfig> list)
    {
        forms = list;
        index = 0;

        RefreshView();
    }

    /// <summary>翻到上一个形态。只有一个形态时箭头是灰的，这里也会被挡住。</summary>
    private void ShowPrevious() => Step(-1);

    /// <summary>翻到下一个形态。</summary>
    private void ShowNext() => Step(1);

    private void Step(int delta)
    {
        if (Count <= 1)
        {
            return;
        }

        // 不做循环：翻到头就停。循环的话玩家分不清「到底了」还是「又转回来了」
        int next = Mathf.Clamp(index + delta, 0, Count - 1);

        if (next == index)
        {
            return;
        }

        index = next;
        RefreshView();
        Changed?.Invoke(this);
    }

    /// <summary>点卡片本身也算「我要看这个形态」，让父面板把立绘切过来。</summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        Changed?.Invoke(this);
    }

    /// <summary>当前卡的高亮。父面板保证两张卡里只有一张亮着。</summary>
    public void SetSelected(bool value)
    {
        if (selectedFrame != null)
        {
            selectedFrame.gameObject.SetActive(value);
        }
    }

    // ------------------------------------------------------------------
    // 显示
    // ------------------------------------------------------------------

    private void RefreshView()
    {
        bool has = Current != null;

        plateImg.gameObject.SetActive(has);
        name.gameObject.SetActive(has);

        // 只有一个形态就没什么可翻的，箭头直接灰掉（别隐藏 —— 位置会跳）。
        // 注意用 leftArrow / rightArrow，不是 lastArrowButton / nextArrowButton ——
        // 那两个名字和位置是反的，见 Init 里的说明
        leftArrow.interactable = index > 0;
        rightArrow.interactable = index < Count - 1;

        if (!has)
        {
            RefreshStars(0);
            return;
        }

        name.text = Current.Name;
        SetPlate(Current.UnitPlateIconKey);
        RefreshStars(Current.UnlockStar);
    }

    private void SetPlate(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            plateImg.gameObject.SetActive(false);
            return;
        }

        var sprite = LoadAsset<Sprite>(key);

        if (sprite == null)
        {
            Debug.LogWarning($"[JobSlot] 略缩图加载不出来：{key}", this);
            plateImg.gameObject.SetActive(false);
            return;
        }

        plateImg.gameObject.SetActive(true);
        plateImg.sprite = sprite;
    }

    /// <summary>
    /// 点亮前 <paramref name="star"/> 颗星。
    ///
    /// 预制体里摆了 6 个 StarBackground（灰底，一直显示），每个下面一个 Star（亮星）。
    /// <c>starContent</c> 是 AutoBind 抓的 **StarContent 下所有 Image**，
    /// 所以里面既有 StarBackground 也有 Star，按名字挑出 Star 那些。
    ///
    /// ⚠️ 亮/不亮切的是 <c>Image.enabled</c>，**不是 gameObject 的激活状态**。
    /// 预制体里美术就是用「禁用 Image 组件」表示未点亮的（物体本身一直 active），
    /// 只切 SetActive 的话：物体是开的、但 Image 还是 disabled，第 4~6 颗永远不亮
    /// —— 表现成「二觉和爆发形态明明是 6 星却只显示 3 颗」（实测踩过）。
    /// 这里两个都管：物体保证是开的，亮不亮由 enabled 决定。
    /// </summary>
    private void RefreshStars(int star)
    {
        if (starContent == null)
        {
            return;
        }

        int lit = 0;

        foreach (var image in starContent)
        {
            if (image == null || image.name != "Star")
            {
                continue;
            }

            image.gameObject.SetActive(true);
            image.enabled = lit < star;
            lit++;
        }
    }
}
