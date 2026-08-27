using System;
using UnityEngine;
using UnityEngine.UI;
using XFramework;

/// <summary>
/// 副本界面里的**一个副本格子**（预制体 <c>DungeonSlot.prefab</c>，360×200）。
/// 由 <see cref="PopDungeonUI"/> 按当前副本区域下的副本列表动态生成到 <c>UIMask/Contents</c> 下。
///
/// <code>
/// DungeonSlot                        ← 整块可点（Button 是**代码补的**，见下）
/// ├── Back/RawImage                  ← 副本略缩图（AspectRatioFitter + RectMask2D）
/// ├── Farme                          ← 装饰边框
/// ├── DungeonNameTex                 ← 副本名
/// └── LevelFarme
///     ├── NextArrowButton  (x=-129)  ← ⚠️ 在**左边**，是「减星」
///     ├── StarContent                ← 6 个 StarBackground(灭底)/Star(亮图)
///     └── LastArrowButton  (x=+144)  ← ⚠️ 在**右边**，是「加星」
/// </code>
///
/// ⚠️⚠️ **两个箭头的名字和位置是反的**（`LastArrowButton` 在右、`NextArrowButton` 在左），
/// 和创角界面那两个形态卡箭头同一个坑（客户端文档第 5 节坑 6）。
/// 所以这里**按 `anchoredPosition.x` 判断左右**再绑，不按名字 —— 按名字接一定接反，
/// 而且表现是「点加星变成减星」，很难往回追。以后美术挪位置也不用改代码。
///
/// ⚠️ 星级**不存在本类里**，一律走 <see cref="DungeonSelection"/> ——
/// 那是为组队留的口子（队长改、队员跟着变），见那个类的注释。
/// </summary>
public partial class DungeonSlot : UIBase
{
    /// <summary>整块格子被点了（想进这个副本）。参数是副本 id。</summary>
    public event Action<int> Clicked;

    /// <summary>这个格子显示的是哪个副本。</summary>
    public Dungeon Config { get; private set; }

    /// <summary>星级那一排的 6 个「亮星」（<c>StarBackground</c> 的子节点 <c>Star</c>）。</summary>
    private Image[] stars;

    /// <summary>左箭头（减星）/ 右箭头（加星）。**按位置认的**，不是按名字。</summary>
    private Button minusButton;
    private Button plusButton;

    /// <summary>整块格子的点击。预制体上只有 Image，所以这个 Button 是代码补的。</summary>
    private Button rootButton;

    public override void Init()
    {
        InitAutoBind();

        CollectStars();
        BindArrows();
        BindRoot();
    }

    /// <summary>
    /// 摆一个副本。**幂等**：同一个格子会被反复复用（换区域、进度变了都要重画）。
    /// </summary>
    public void SetDungeon(Dungeon config)
    {
        Config = config;

        if (config == null)
        {
            return;
        }

        SetText(dungeonNameTex, config.Name);

        // 略缩图是 RawImage（要的是 Texture 不是 Sprite）。
        // 用 UIBase.LoadAsset 而不是直接调 AssetsManager —— 它会把 key 托管起来，
        // 面板关闭时自动释放，不用手动配对（客户端文档第 5 节坑 9）
        if (rawImage != null && !string.IsNullOrEmpty(config.ThumbnailKey))
        {
            Texture texture = LoadAsset<Texture>(config.ThumbnailKey);

            if (texture != null)
            {
                rawImage.texture = texture;
            }
        }

        Refresh();
    }

    /// <summary>
    /// 按当前选择和通关进度重画星级和箭头。<see cref="DungeonSelection.Changed"/> /
    /// <see cref="DungeonProgress.Changed"/> 一响就调，所以要便宜且幂等。
    /// </summary>
    public void Refresh()
    {
        if (Config == null)
        {
            return;
        }

        int star = DungeonSelection.Instance.StarOf(Config);
        int max = DungeonProgress.MaxSelectableStar(Config);

        // 亮星 = 前 star 颗。灭掉的只是把「亮图」藏起来，底下那张 off 底一直在
        //（第 6 颗的亮图是特殊的 common_icon_star_6_on，所以这里只切显隐、不换 sprite）
        for (int i = 0; i < stars.Length; i++)
        {
            if (stars[i] != null)
            {
                stars[i].gameObject.SetActive(i < star);
            }
        }

        // 到头的箭头画灰。⚠️ 还要看 CanEdit —— 以后组队时队员不能改
        bool canEdit = DungeonSelection.Instance.CanEdit;

        SetArrowEnabled(minusButton, canEdit && star > 1);
        SetArrowEnabled(plusButton, canEdit && star < max);
    }

    // ------------------------------------------------------------------
    // 接线
    // ------------------------------------------------------------------

    /// <summary>
    /// 收集 6 颗亮星。走 <c>StarContent</c> 的子节点顺序，**不按名字找** ——
    /// 6 个 <c>StarBackground</c> 同名，只能按顺序认。
    /// </summary>
    private void CollectStars()
    {
        if (starContent == null)
        {
            stars = Array.Empty<Image>();
            Debug.LogError($"[DungeonSlot] 找不到 StarContent，星级画不出来");
            return;
        }

        int count = starContent.childCount;
        stars = new Image[count];

        for (int i = 0; i < count; i++)
        {
            Transform background = starContent.GetChild(i);

            // 每个 StarBackground 下面挂一个 Star（亮图）
            stars[i] = background.childCount > 0
                ? background.GetChild(0).GetComponent<Image>()
                : null;
        }
    }

    /// <summary>
    /// 按 <c>anchoredPosition.x</c> 认左右再绑（见类注释里那个「名字和位置是反的」）。
    /// </summary>
    private void BindArrows()
    {
        if (lastArrowButton == null || nextArrowButton == null)
        {
            Debug.LogError("[DungeonSlot] 箭头按钮没绑上，星级切不了");
            return;
        }

        float lastX = ((RectTransform)lastArrowButton.transform).anchoredPosition.x;
        float nextX = ((RectTransform)nextArrowButton.transform).anchoredPosition.x;

        // x 小的在左边 = 减星
        bool lastIsLeft = lastX < nextX;

        minusButton = lastIsLeft ? lastArrowButton : nextArrowButton;
        plusButton = lastIsLeft ? nextArrowButton : lastArrowButton;

        Bind(minusButton, () => StepStar(-1), AudioKeys.CursorClick01);
        Bind(plusButton, () => StepStar(1), AudioKeys.CursorClick01);
    }

    private void BindRoot()
    {
        // 预制体根节点上只有 Image，没有 Button —— 补一个（和 PopMessageUI 那两个页签一个做法）。
        // transition 留 None：格子没有做按下态的美术，加一层 ColorTint 会让整块图变色
        rootButton = gameObject.GetComponent<Button>();

        if (rootButton == null)
        {
            rootButton = gameObject.AddComponent<Button>();
            rootButton.transition = Selectable.Transition.None;
        }

        Bind(rootButton, HandleRootClicked, AudioKeys.CursorClick01);
    }

    private void StepStar(int delta)
    {
        if (Config == null)
        {
            return;
        }

        DungeonSelection.Instance.SetStar(Config, DungeonSelection.Instance.StarOf(Config) + delta);
    }

    private void HandleRootClicked()
    {
        if (Config == null)
        {
            return;
        }

        DungeonSelection.Instance.Select(Config.DungeonId);
        Clicked?.Invoke(Config.DungeonId);
    }

    private static void SetArrowEnabled(Button button, bool enabled)
    {
        if (button == null)
        {
            return;
        }

        button.interactable = enabled;

        // 箭头本身就是一张图，到头了直接调暗，比只禁用 Button 看得出来
        if (button.TryGetComponent(out Image image))
        {
            Color color = image.color;
            color.a = enabled ? 1f : 0.35f;
            image.color = color;
        }
    }

    private static void SetText(TMPro.TextMeshProUGUI text, string value)
    {
        if (text != null)
        {
            text.text = value ?? string.Empty;
        }
    }
}
