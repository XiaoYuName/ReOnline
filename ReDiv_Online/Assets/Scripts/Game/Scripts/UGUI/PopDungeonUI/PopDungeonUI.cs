using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using XFramework;

/// <summary>
/// 副本界面 —— 城镇里踩到**副本入口触发器**（配置表 <c>TownTrigger</c> 里 <c>Kind=2</c> 的行）
/// 就打开这里，触发器的 <c>TargetId</c> 就是要显示的**副本区域** id。
///
/// 结构像 DNF（用户 2026-08-27 定的）：**一个副本区域里有多个小副本**，
/// 每个小副本可以选 1~6 星的挑战难度。
///
/// <code>
/// DungeonArea（区域，例：幽暗密林）   ← 自带一张背景，标题栏显示它的名字
///   └── Dungeon（小副本）× N          ← 界面上一个 DungeonSlot 格子，各自能选星级
/// </code>
///
/// <code>
/// PopDungeonUI
/// └── UIMask
///     ├── (区域背景)          ← **运行时塞进来的**，永远是第一个子节点（压在最底下）
///     ├── Contents            ← 副本格子摆这里（**按配置坐标摆，不能有布局组件**，见下）
///     └── Background          ← alpha 0 的容器（**不是**区域背景！美术起的名字）
///         ├── Title/TitleTex  ← 区域名
///         └── CloseButton     ← 关闭（Button 也是**代码补的**，预制体上只有 Image）
/// </code>
///
/// ⚠️⚠️ **区域背景挂在本界面自己的层级里**（`UIMask` 下第一个子节点），
/// **不走框架的 `UISystem.LoadUIBackground` 背景层** —— 用户 2026-08-27 订正的：
/// 一开始走的是那一层，结果**副本界面开着时城镇整个露在外面**（背景层的 Canvas
/// 在城镇角色下面，只盖住了城镇背景）。挂进自己层级里就自然盖住城镇了。
/// 所以框架那三个 `LoadUIBackground` / `HideUIBackground` / `ReleaseUIBackground`
/// **又回到没有调用方的状态**，别以为副本在用它们。
///
/// 本类**存着那张背景的引用**（<see cref="AreaBackground"/>，用户明确要求的），
/// 方便以后在背景上做逻辑（切区域淡入淡出、叠特效之类）。
///
/// ⚠️ **「选了哪个副本 / 几星」不存在本类里**，一律走 <see cref="DungeonSelection"/> ——
/// 那是给**组队**留的口子（以后队长改、队员跟着变，界面代码不用动），见那个类的注释。
///
/// ⚠️ 本轮（2026-08-27）副本是**纯客户端**的：服务端不认识副本，没有「进副本」的 Reducer，
/// 所以点格子只会打一条日志。战斗 / 结算 / 体力消耗定型后再接。
/// </summary>
public partial class PopDungeonUI : UIBase
{
    /// <summary>
    /// 当前显示的副本区域 id。0 = 还没指定过（<see cref="Show"/> 没被调过）。
    ///
    /// 存着它是因为 <c>Open()</c> 会**先于** <see cref="Show"/> 执行
    /// （<c>OpenUI</c> 里就调了 Open，areaId 是紧接着才传进来的），
    /// 而且界面可能被重复打开 —— 重开时要能靠它把上次那个区域再摆出来。
    /// </summary>
    private int currentAreaId;

    /// <summary>
    /// 当前那张区域背景（挂在本界面 `UIMask` 下的实例）。
    /// **用户 2026-08-27 明确要求存着这个引用**，方便以后在背景上做逻辑。
    /// </summary>
    public DungeonAreaBackground AreaBackground { get; private set; }

    /// <summary>
    /// 区域背景预制体，**只加载一次**（和 <see cref="slotPrefab"/> 一个套路）。
    /// key 换了就重新加载，所以还要记住上次加载的是哪个 key。
    /// </summary>
    private GameObject areaBackgroundPrefab;

    /// <summary>当前挂着的背景是哪个 key。空 = 没有背景。</summary>
    private string areaBackgroundKey = string.Empty;

    /// <summary>已经摆出来的格子，和当前区域下的副本一一对应。</summary>
    private readonly List<DungeonSlot> slots = new List<DungeonSlot>();

    /// <summary>格子预制体，**只加载一次**（界面关掉时置回 null，见 <see cref="ClearSlots"/>）。</summary>
    private GameObject slotPrefab;

    private bool hooked;

    // ------------------------------------------------------------------
    // 生命周期
    // ------------------------------------------------------------------

    public override void Init()
    {
        InitAutoBind();

        BindCloseButton();
        CheckContentsLayout();
    }

    public override void Open()
    {
        base.Open();

        HookEvents();

        // 重开时把上次那个区域再摆出来（正常路径是紧接着 Show(areaId) 覆盖掉）
        if (currentAreaId != 0)
        {
            Apply(currentAreaId);
        }
    }

    public override void Close()
    {
        UnhookEvents();
        ClearSlots();
        ReleaseAreaBackground();

        base.Close();
    }

    protected override void OnDestroy()
    {
        UnhookEvents();
        ClearSlots();
        ReleaseAreaBackground();

        base.OnDestroy();
    }

    /// <summary>
    /// 显示某个副本区域。**由打开方紧跟在 <c>OpenUI</c> 后面调**
    /// （和创角界面 <c>ReviseCharacterNameUI.SetJob</c> 一个套路）：
    ///
    /// <code>
    /// UISystem.Instance.OpenUI&lt;PopDungeonUI&gt;(UIKeys.PopDungeonUI)?.Show(areaId);
    /// </code>
    /// </summary>
    public void Show(int areaId)
    {
        currentAreaId = areaId;
        Apply(areaId);
    }

    // ------------------------------------------------------------------
    // 事件
    // ------------------------------------------------------------------

    private void HookEvents()
    {
        if (hooked)
        {
            return;
        }
        hooked = true;

        // 选择变了（星级切换）/ 通关进度变了（以后结算写入）都要重画星级
        DungeonSelection.Instance.Changed += RefreshSlots;
        DungeonProgress.Changed += RefreshSlots;
    }

    private void UnhookEvents()
    {
        if (!hooked)
        {
            return;
        }
        hooked = false;

        DungeonSelection.Instance.Changed -= RefreshSlots;
        DungeonProgress.Changed -= RefreshSlots;
    }

    // ------------------------------------------------------------------
    // 摆内容
    // ------------------------------------------------------------------

    /// <summary>
    /// 按区域 id 摆一遍：区域背景 → 标题 → 副本格子。
    /// **不做幂等挡门**（每次 <see cref="Show"/> 都重摆）—— 这个界面是弹窗，
    /// 打开一次就摆一次，不像城镇那些挂在高频事件上的刷新。
    /// </summary>
    private void Apply(int areaId)
    {
        DungeonArea area = LubanManager.Instance.TbDungeonArea?.GetOrDefault(areaId);

        if (area == null)
        {
            Debug.LogError($"[PopDungeonUI] 副本区域 {areaId} 不在 DungeonArea 表里");
            SetTitle(string.Empty);
            ClearSlots();
            return;
        }

        SetTitle(area.Name);
        RefreshAreaBackground(area);
        BuildSlots(areaId);
    }

    private void SetTitle(string value)
    {
        if (titleTex != null)
        {
            titleTex.text = value ?? string.Empty;
        }
    }

    /// <summary>
    /// 换区域背景。**幂等**：key 没变什么都不做（重开界面 / 同一个区域重复 <see cref="Show"/>
    /// 都不该重新实例化一张 1024 的大图）。
    ///
    /// 挂在 `UIMask` 下并且**永远拉到第一个子节点**（<c>SetAsFirstSibling</c>）——
    /// 它得压在 `Contents`（副本格子）和 `Background`（标题 / 关闭按钮）下面。
    /// </summary>
    private void RefreshAreaBackground(DungeonArea area)
    {
        string key = area.BackgroundKey ?? string.Empty;

        if (key == areaBackgroundKey)
        {
            return;
        }

        ReleaseAreaBackground();

        if (string.IsNullOrEmpty(key))
        {
            // 这个区域还没配背景。**不是错误** —— 界面照样能用，只是没有底图
            return;
        }

        Transform parent = MaskTran;

        if (parent == null)
        {
            Debug.LogError("[PopDungeonUI] 找不到 UIMask，区域背景没地方挂");
            return;
        }

        if (areaBackgroundPrefab == null)
        {
            // LoadAsset 会把 key 托管给本面板，关闭时自动释放（客户端文档第 5 节坑 9）
            areaBackgroundPrefab = LoadAsset<GameObject>(key);
        }

        if (areaBackgroundPrefab == null)
        {
            Debug.LogError($"[PopDungeonUI] 区域 {area.AreaId}（{area.Name}）的背景加载不出来：{key}");
            return;
        }

        GameObject instance = Instantiate(areaBackgroundPrefab, parent);
        instance.transform.SetAsFirstSibling();

        Stretch(instance.transform as RectTransform);

        AreaBackground = instance.GetComponent<DungeonAreaBackground>();

        if (AreaBackground == null)
        {
            // 不致命（图照样显示），但存不了引用、以后没法在背景上挂逻辑，所以要报出来
            Debug.LogError($"[PopDungeonUI] {key} 上没有 DungeonAreaBackground 组件");
        }

        CheckRenderQueue(instance, key);

        areaBackgroundKey = key;
    }

    /// <summary>
    /// 收掉区域背景。
    ///
    /// 直接 <c>Destroy</c>：它是本界面自己实例化的子节点，预制体的 Addressables 引用由
    /// <c>UIBase</c> 在关闭时统一还掉（走的是 <see cref="LoadAsset{T}"/> 的托管）。
    /// **先 <c>SetActive(false)</c> 再销毁** —— Destroy 延迟到帧末，紧接着又实例化下一张时
    /// 两张会在同一帧里叠着（客户端文档第 5 节坑 3）。
    /// </summary>
    private void ReleaseAreaBackground()
    {
        if (AreaBackground != null)
        {
            AreaBackground.gameObject.SetActive(false);
            Destroy(AreaBackground.gameObject);
        }
        else if (!string.IsNullOrEmpty(areaBackgroundKey) && MaskTran != null && MaskTran.childCount > 0)
        {
            // 兜底：预制体上没挂组件时 AreaBackground 是 null，但那张图确实挂在第一个子节点上
            Transform first = MaskTran.GetChild(0);

            if (first != contents && first.GetComponent<RawImage>() != null)
            {
                first.gameObject.SetActive(false);
                Destroy(first.gameObject);
            }
        }

        AreaBackground = null;
        areaBackgroundKey = string.Empty;
        areaBackgroundPrefab = null;
    }

    /// <summary>
    /// `UIMask`（区域背景挂在它下面）。取 <c>Contents</c> 的父级 ——
    /// AutoBind 里的路径本来就是 <c>UIMask/Contents</c>，不用再写一次硬编码路径。
    /// </summary>
    private Transform MaskTran => contents != null ? contents.parent : null;

    /// <summary>
    /// 查一下背景材质的 `renderQueue`。
    ///
    /// ⚠️⚠️ **国服还原出来的 VariantCard 材质 `renderQueue` 是 2000**（从原包 bin 里解析的原值），
    /// 而 UI 默认是 **3000（Transparent）**。queue 小的先画 ⇒ **2000 的背景会被所有 UI 盖住**，
    /// 包括城镇那些 HUD（摇杆、聊天框、右上角信息栏）。
    /// 症状极具误导性：**副本界面明明开着，城镇的界面元素还在上面**，
    /// 看着像层级 / 兄弟序不对，其实是材质的 queue（2026-08-27 实测踩过，查错了两轮）。
    ///
    /// 所以区域背景材质**必须是 3000**。这里**只报错不自动改** ——
    /// 自动改会写共享材质资产（改到 git 里去），那种偷偷改美术资产的事不该由运行时代码做。
    /// 修法：选中那个 `.mat`，Inspector 右上角 → Render Queue 填 3000（或 From Shader）。
    /// </summary>
    private static void CheckRenderQueue(GameObject instance, string key)
    {
        if (!instance.TryGetComponent(out RawImage raw) || raw.material == null)
        {
            return;
        }

        const int UIRenderQueue = 3000;

        if (raw.material.renderQueue < UIRenderQueue)
        {
            Debug.LogError($"[PopDungeonUI] 区域背景材质 {raw.material.name} 的 renderQueue 是 " +
                           $"{raw.material.renderQueue}（< {UIRenderQueue}），" +
                           $"它会被所有 UI 盖住 —— 表现是「副本界面开着还能看到城镇的摇杆 / 聊天框」。" +
                           $"把那个材质的 Render Queue 改成 {UIRenderQueue}。背景：{key}");
        }
    }

    /// <summary>
    /// 把背景拉满整屏。**贴图是压扁的方图**（1024×1024，和城镇背景一个套路），
    /// 所以必须铺满 16:9 才是正确比例 —— 按原尺寸摆会又方又小。
    /// </summary>
    private static void Stretch(RectTransform rect)
    {
        if (rect == null)
        {
            return;
        }

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localPosition = new Vector3(0f, 0f, rect.localPosition.z);
    }

    /// <summary>
    /// 摆这个区域下的所有副本格子。
    ///
    /// **复用已有格子、只补收差额**（和聊天那两处一个做法）：区域之间副本数差不多，
    /// 每次全拆重建会白白实例化一堆预制体。
    /// </summary>
    private void BuildSlots(int areaId)
    {
        List<Dungeon> dungeons = DungeonsOf(areaId);

        if (contents == null)
        {
            Debug.LogError("[PopDungeonUI] 找不到 Contents，副本格子没地方摆");
            return;
        }

        // 多出来的收掉
        for (int i = slots.Count - 1; i >= dungeons.Count; i--)
        {
            DungeonSlot extra = slots[i];
            slots.RemoveAt(i);

            if (extra != null)
            {
                extra.Clicked -= HandleSlotClicked;
                extra.gameObject.SetActive(false);
                Destroy(extra.gameObject);
            }
        }

        // 不够的补上
        while (slots.Count < dungeons.Count)
        {
            DungeonSlot slot = CreateSlot();

            if (slot == null)
            {
                return;
            }

            slots.Add(slot);
        }

        for (int i = 0; i < dungeons.Count; i++)
        {
            slots[i].SetDungeon(dungeons[i]);

            // 位置来自配置（Dungeon.PosX/PosY），不是平铺 —— 见 ApplySlotPosition
            ApplySlotPosition(slots[i], dungeons[i]);
        }
    }

    private DungeonSlot CreateSlot()
    {
        if (slotPrefab == null)
        {
            // LoadAsset 会把 key 托管给本面板，关闭时自动释放（第 5 节坑 9）
            slotPrefab = LoadAsset<GameObject>(AssetKeys.DungeonSlotPath);
        }

        if (slotPrefab == null)
        {
            Debug.LogError($"[PopDungeonUI] 副本格子预制体加载不出来：{AssetKeys.DungeonSlotPath}");
            return null;
        }

        GameObject instance = Instantiate(slotPrefab, contents);
        DungeonSlot slot = instance.GetComponent<DungeonSlot>();

        if (slot == null)
        {
            Debug.LogError($"[PopDungeonUI] {AssetKeys.DungeonSlotPath} 上没有 DungeonSlot 组件");
            Destroy(instance);
            return null;
        }

        slot.Init();
        slot.Clicked += HandleSlotClicked;
        return slot;
    }

    private void RefreshSlots()
    {
        foreach (DungeonSlot slot in slots)
        {
            if (slot != null)
            {
                slot.Refresh();
            }
        }
    }

    private void ClearSlots()
    {
        foreach (DungeonSlot slot in slots)
        {
            if (slot == null)
            {
                continue;
            }

            slot.Clicked -= HandleSlotClicked;
            slot.gameObject.SetActive(false);
            Destroy(slot.gameObject);
        }

        slots.Clear();

        // 预制体的 AA 引用由 UIBase 在关闭时统一还掉，这里只把缓存清空 ——
        // 留着就是个悬空引用（AudioManager 踩过同类问题）
        slotPrefab = null;
    }

    /// <summary>
    /// 这个区域下的副本，按 <c>SortOrder</c> 排。
    /// </summary>
    private static List<Dungeon> DungeonsOf(int areaId)
    {
        var result = new List<Dungeon>();
        TbDungeon table = LubanManager.Instance.TbDungeon;

        if (table == null)
        {
            return result;
        }

        foreach (Dungeon row in table.DataList)
        {
            if (row.AreaId == areaId)
            {
                result.Add(row);
            }
        }

        result.Sort((a, b) => a.SortOrder != b.SortOrder
            ? a.SortOrder.CompareTo(b.SortOrder)
            : a.DungeonId.CompareTo(b.DungeonId));

        return result;
    }

    // ------------------------------------------------------------------
    // 交互
    // ------------------------------------------------------------------

    /// <summary>
    /// 点了某个副本格子。
    ///
    /// ⚠️ **本轮到这儿就停了**：副本本身（关卡 / 战斗 / 结算 / 体力消耗）一张表都还没有，
    /// 服务端也不认识副本。所以这里只打日志 —— 接战斗时把这一处换成
    /// 「调服务端进副本」即可，选中的副本和星级都能从 <see cref="DungeonSelection"/> 拿到。
    /// </summary>
    private void HandleSlotClicked(int dungeonId)
    {
        Dungeon config = LubanManager.Instance.TbDungeon?.GetOrDefault(dungeonId);

        if (config == null)
        {
            return;
        }

        int star = DungeonSelection.Instance.StarOf(config);

        Debug.Log($"[PopDungeonUI] 要挑战副本 {config.DungeonId}（{config.Name}） {star} 星 —— " +
                  $"战斗还没做，本轮到此为止");
    }

    private void BindCloseButton()
    {
        if (closeButton == null)
        {
            Debug.LogError("[PopDungeonUI] 找不到 CloseButton");
            return;
        }

        // 预制体上只有 Image，没有 Button —— 补一个（和 PopMessageUI 那两个页签一个做法）。
        // transition 留 None：那张返回图自己就是按钮样式，再来一层 ColorTint 会打架
        if (!closeButton.TryGetComponent(out Button button))
        {
            button = closeButton.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
        }

        Bind(button, () => UISystem.Instance.CloseUI(UIKeys.PopDungeonUI), AudioKeys.CursorClick01);
    }

    /// <summary>
    /// 格子的位置**来自配置表**（`Dungeon.PosX` / `PosY`，用户 2026-08-27 定的：
    /// 不要平铺，按配好的位置摆），所以 <c>Contents</c> 上**不能有布局组件** ——
    /// `LayoutGroup` 会在下一次布局阶段把 `anchoredPosition` 全部覆盖掉，
    /// 表现是「坐标配了但格子还是排成一行」，而且从现象完全看不出是布局组件干的。
    ///
    /// ⚠️ 2026-08-27 之前这里是**主动加**一个 `HorizontalLayoutGroup`（那时是平铺的），
    /// 所以这个检查也顺带兜住「美术照着旧行为在预制体里加了布局组」这种情况。
    /// </summary>
    private void CheckContentsLayout()
    {
        if (contents == null)
        {
            return;
        }

        if (contents.TryGetComponent(out LayoutGroup layout))
        {
            Debug.LogError($"[PopDungeonUI] Contents 上有 {layout.GetType().Name}，" +
                           $"它会覆盖掉按配置摆的格子坐标（Dungeon.PosX/PosY）——" +
                           $"把那个组件从预制体上删掉。已在运行时禁用它。");

            layout.enabled = false;
        }
    }

    /// <summary>
    /// 把格子摆到配置里那个位置上。
    ///
    /// 坐标是 **UI 坐标**（`anchoredPosition`，相对 <c>Contents</c> 中心）——
    /// 格子预制体根节点的 anchor 和 pivot 都是 (0.5, 0.5)，所以配的就是「相对屏幕中心的偏移」。
    /// 别拿它当世界坐标（城镇那些 NPC / 触发器才是世界坐标，两套别混）。
    ///
    /// **别在这里改 anchor / pivot / sizeDelta** —— 格子是 360×200 的定尺寸美术，
    /// 尺寸和锚点都由预制体说话，代码只管往哪儿摆。
    /// </summary>
    private static void ApplySlotPosition(DungeonSlot slot, Dungeon config)
    {
        if (slot == null || config == null)
        {
            return;
        }

        if (slot.transform is RectTransform rect)
        {
            rect.anchoredPosition = new Vector2(config.PosX, config.PosY);
        }
    }
}
