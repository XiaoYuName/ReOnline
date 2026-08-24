using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using XFramework;
using CharacterFormConfig = XFramework.CharacterForm;
using CharacterJobConfig = XFramework.CharacterJob;

/// <summary>
/// 创建角色界面 —— **目前只有展示逻辑，创建按钮还没接**。
///
/// 布局和数据的对应：
/// <code>
/// HeadContent     每个可创建角色一个 CharacterHeadSlot（动态生成）
/// NameImg         选中角色后显示：基础形态的 NameIconKey（名字图）
/// ArtImage        选中角色后显示：基础形态的 ArtImage（立绘）
/// RightJobsPanel  选中角色后显示，里面两张 CharacterJobSlotUI（预制体里摆好的，不动态生成）
///   上面那张      基础线：基础形态 → 一觉 → 二觉，箭头翻觉醒阶段
///   下面那张      爆发线：该角色的多个爆发形态，箭头翻不同形态
/// Fall            觉醒 / 爆发形态的**全屏立绘**（StillUnitPrefab）生成在这里
/// </code>
///
/// 立绘的规则（两者互斥，同时只显示一个）：
///   看**基础形态**  → 显示 ArtImage 那张普通立绘，Fall 清空
///   看**觉醒 / 爆发形态** → 在 Fall 里生成该形态的 StillUnitPrefab，ArtImage 隐藏
///
/// 「在看哪个形态」由两张卡里**当前选中的那张**决定：点卡片或点它的箭头都会让它成为当前卡。
/// 两张卡同时摆着但立绘只有一处，所以必须有这个「当前卡」的概念。
/// </summary>
public partial class CreatCharacterUI : UIBase
{
    /// <summary>基础线 / 爆发线，对应形态表的 FormType。</summary>
    private const int FormTypeBase = 1;
    private const int FormTypeBurst = 2;

    /// <summary>
    /// NameImg 还没被登记进 UIAutoBindGenerator 的绑定项，所以 AutoBind 里没有它，
    /// 这里手动 Get。哪天在 Inspector 里把它加进绑定项、重新生成了，
    /// 就可以删掉这个字段改用生成出来的那个。
    /// </summary>
    private Image nameImg;

    private readonly List<CharacterHeadSlot> headSlots = new List<CharacterHeadSlot>();

    /// <summary>上面那张卡：基础线。</summary>
    private CharacterJobSlotUI baseSlot;

    /// <summary>下面那张卡：爆发线。</summary>
    private CharacterJobSlotUI burstSlot;

    /// <summary>当前选中的角色。没选就是 null。</summary>
    private CharacterJobConfig selectedJob;

    /// <summary>当前正在看的那张卡（决定立绘显示哪个形态）。</summary>
    private CharacterJobSlotUI currentSlot;

    /// <summary>Fall 里当前生成的全屏立绘。换形态时要销毁，否则会越叠越多。</summary>
    private GameObject fullArt;

    public override void Init()
    {
        InitAutoBind();

        nameImg = Get<Image>("UIMask/NameImg");

        // 两张卡是预制体里摆好的，按层级顺序拿：先 BaseTitle+基础卡，再 JobTitle+爆发卡
        var slots = rightJobsPanel.GetComponentsInChildren<CharacterJobSlotUI>(true);
        baseSlot = slots.Length > 0 ? slots[0] : null;
        burstSlot = slots.Length > 1 ? slots[1] : null;

        foreach (CharacterJobSlotUI slot in slots)
        {
            slot.Init();
            slot.Changed += HandleSlotChanged;
        }
    }

    public override void Open()
    {
        base.Open();

        BuildHeadSlots();
        ClearSelection();
    }

    public override void Close()
    {
        ClearFullArt();
        base.Close();
    }

    protected override void OnDestroy()
    {
        foreach (CharacterHeadSlot slot in headSlots)
        {
            if (slot != null)
            {
                slot.Clicked -= HandleHeadSlotClicked;
            }
        }

        // baseSlot / burstSlot 是预制体自带的，Init 没跑过时是 null
        if (baseSlot != null)
        {
            baseSlot.Changed -= HandleSlotChanged;
        }

        if (burstSlot != null)
        {
            burstSlot.Changed -= HandleSlotChanged;
        }

        base.OnDestroy();
    }

    // ------------------------------------------------------------------
    // 角色头像列表
    // ------------------------------------------------------------------

    /// <summary>
    /// 按配置里**可创建**的角色生成头像格子。
    ///
    /// 每次 Open 都重建：角色表是配置、进游戏后不会变，但重建一次很便宜，
    /// 省得去想「上次开界面之后配置热更过没有」。
    /// </summary>
    private void BuildHeadSlots()
    {
        ClearHeadSlots();

        var jobs = LubanManager.Instance.TbCharacterJob?.DataList;

        if (jobs == null)
        {
            Debug.LogError("[CreatCharacter] 角色配置表没加载出来", this);
            return;
        }

        var prefab = LoadAsset<GameObject>(AssetKeys.CharacterHeadSlotPath);

        if (prefab == null)
        {
            Debug.LogError($"[CreatCharacter] 头像格子预制体加载不出来：{AssetKeys.CharacterHeadSlotPath}", this);
            return;
        }

        foreach (CharacterJobConfig job in jobs.Where(j => j.Creatable).OrderBy(j => j.SortOrder))
        {
            var go = Instantiate(prefab, headContent, false);
            var slot = go.GetComponent<CharacterHeadSlot>();

            if (slot == null)
            {
                Debug.LogError("[CreatCharacter] 头像格子预制体上没有 CharacterHeadSlot 组件", this);
                Destroy(go);
                continue;
            }

            slot.Init();
            // 头像挂在形态上不在角色上，所以取该角色基础形态那一行的 IconKey
            slot.SetJob(job, BaseForm(job.JobId)?.IconKey);
            slot.Clicked += HandleHeadSlotClicked;

            headSlots.Add(slot);
        }
    }

    private void ClearHeadSlots()
    {
        foreach (CharacterHeadSlot slot in headSlots)
        {
            if (slot != null)
            {
                slot.Clicked -= HandleHeadSlotClicked;
                Destroy(slot.gameObject);
            }
        }

        headSlots.Clear();
    }

    // ------------------------------------------------------------------
    // 选角色
    // ------------------------------------------------------------------

    /// <summary>还没选角色：右侧面板、名字图、立绘全收起来。</summary>
    private void ClearSelection()
    {
        selectedJob = null;
        currentSlot = null;

        rightJobsPanel.gameObject.SetActive(false);
        artImage.gameObject.SetActive(false);

        if (nameImg != null)
        {
            nameImg.gameObject.SetActive(false);
        }

        ClearFullArt();

        foreach (CharacterHeadSlot slot in headSlots)
        {
            slot.SetSelected(false);
        }
    }

    /// <summary>单选：取消别的，选中这个，然后把右侧两张卡摆上。</summary>
    private void HandleHeadSlotClicked(CharacterHeadSlot slot)
    {
        if (slot == null || slot.Job == null)
        {
            return;
        }

        foreach (CharacterHeadSlot other in headSlots)
        {
            other.SetSelected(other == slot);
        }

        selectedJob = slot.Job;

        ShowJob(selectedJob);
    }

    /// <summary>把一个角色的名字图 / 立绘 / 两条形态线摆出来。</summary>
    private void ShowJob(CharacterJobConfig job)
    {
        List<CharacterFormConfig> forms = FormsOf(job.JobId);

        // 基础线按星级门槛从低到高：基础 → 一觉 → 二觉
        List<CharacterFormConfig> baseForms = forms
            .Where(f => f.FormType == FormTypeBase)
            .OrderBy(f => f.UnlockStar)
            .ToList();

        // 爆发线按配置里的排序，一个角色可以有多个
        List<CharacterFormConfig> burstForms = forms
            .Where(f => f.FormType == FormTypeBurst)
            .OrderBy(f => f.SortOrder)
            .ToList();

        rightJobsPanel.gameObject.SetActive(true);

        baseSlot?.SetForms(baseForms);
        burstSlot?.SetForms(burstForms);

        // 没有爆发形态的角色（配置里就没配）把下面那张卡收起来，别摆一张空卡
        if (burstSlot != null)
        {
            burstSlot.gameObject.SetActive(burstForms.Count > 0);
        }

        // 默认落在基础线的第一个形态上 —— 也就是基础形态，显示普通立绘
        SetCurrentSlot(baseSlot);
    }

    // ------------------------------------------------------------------
    // 形态卡 / 立绘
    // ------------------------------------------------------------------

    /// <summary>哪张卡被点了（或翻页了），就把它当成「正在看」的那张。</summary>
    private void HandleSlotChanged(CharacterJobSlotUI slot) => SetCurrentSlot(slot);

    private void SetCurrentSlot(CharacterJobSlotUI slot)
    {
        currentSlot = slot;

        baseSlot?.SetSelected(baseSlot == slot);
        burstSlot?.SetSelected(burstSlot == slot);

        RefreshArt();
    }

    /// <summary>
    /// 按当前形态刷立绘。
    ///
    /// 基础形态（基础线里星级门槛最低那行）用 <c>ArtImage</c> 那张普通立绘；
    /// 觉醒和爆发形态都用 <c>StillUnitPrefab</c>，在 Fall 里生成一张全屏的。
    /// </summary>
    private void RefreshArt()
    {
        ClearFullArt();

        CharacterFormConfig form = currentSlot != null ? currentSlot.Current : null;

        if (form == null)
        {
            artImage.gameObject.SetActive(false);
            ShowNameImage(null);
            return;
        }

        // 名字图是角色级的，挂在形态行上但各形态填的是同一张，跟着当前形态取就行
        ShowNameImage(form.NameIconKey);

        bool isBaseForm = currentSlot == baseSlot && IsLowestStar(form);

        if (isBaseForm)
        {
            ShowArtImage(form.ArtImage);
        }
        else
        {
            artImage.gameObject.SetActive(false);
            ShowFullArt(form.StillUnitPrefab);
        }
    }

    /// <summary>是不是基础线里星级门槛最低的那一行（＝建完角色的样子）。</summary>
    private bool IsLowestStar(CharacterFormConfig form)
    {
        if (selectedJob == null)
        {
            return false;
        }

        int lowest = FormsOf(selectedJob.JobId)
            .Where(f => f.FormType == FormTypeBase)
            .Select(f => f.UnlockStar)
            .DefaultIfEmpty(int.MaxValue)
            .Min();

        return form.UnlockStar == lowest;
    }

    private void ShowNameImage(string key)
    {
        if (nameImg == null)
        {
            return;
        }

        if (string.IsNullOrEmpty(key))
        {
            nameImg.gameObject.SetActive(false);
            return;
        }

        var sprite = LoadAsset<Sprite>(key);

        if (sprite == null)
        {
            Debug.LogWarning($"[CreatCharacter] 名字图加载不出来：{key}", this);
            nameImg.gameObject.SetActive(false);
            return;
        }

        nameImg.gameObject.SetActive(true);
        nameImg.sprite = sprite;
    }

    private void ShowArtImage(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            artImage.gameObject.SetActive(false);
            return;
        }

        var sprite = LoadAsset<Sprite>(key);

        if (sprite == null)
        {
            Debug.LogWarning($"[CreatCharacter] 立绘加载不出来：{key}", this);
            artImage.gameObject.SetActive(false);
            return;
        }

        artImage.gameObject.SetActive(true);
        artImage.sprite = sprite;
    }

    /// <summary>在 Fall 下生成全屏立绘。预制体是个 RawImage + AspectRatioFitter，铺满即可。</summary>
    private void ShowFullArt(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            // 这个形态还没配预览图预制体。不当错误 —— 角色资源配置窗口会把它报出来，
            // 运行时反复刷日志没意义
            return;
        }

        var prefab = LoadAsset<GameObject>(key);

        if (prefab == null)
        {
            Debug.LogError($"[CreatCharacter] 全屏立绘预制体加载不出来：{key}", this);
            return;
        }

        fullArt = Instantiate(prefab, fall, false);

        // 预制体自己带 AspectRatioFitter 控高宽比，这里只把锚点拉满让它按 Fall 的尺寸走
        if (fullArt.transform is RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }
    }

    private void ClearFullArt()
    {
        fullArt = null;

        if (fall == null)
        {
            return;
        }

        // ⚠️ 销毁前必须先 SetActive(false)。Unity 的 Destroy 是**延迟到帧末**执行的，
        // 同一帧里紧接着 Instantiate 新立绘的话，旧的还在、两张会叠着显示
        // （连点箭头时实测叠出过 3 张）。置灰再销毁，视觉上立刻就没了。
        //
        // 顺带把 Fall 下所有子物体都清掉，而不只是 fullArt —— 热重载或手工塞进去的
        // 也一起收拾干净。
        for (int i = fall.childCount - 1; i >= 0; i--)
        {
            GameObject child = fall.GetChild(i).gameObject;
            child.SetActive(false);
            Destroy(child);
        }
    }

    // ------------------------------------------------------------------
    // 配置查询
    // ------------------------------------------------------------------

    private List<CharacterFormConfig> FormsOf(int jobId)
    {
        var all = LubanManager.Instance.TbCharacterForm?.DataList;

        return all == null
            ? new List<CharacterFormConfig>()
            : all.Where(f => f.JobId == jobId).ToList();
    }

    /// <summary>某个角色的基础形态（基础线里星级门槛最低那行）。头像 / 默认立绘都取它。</summary>
    private CharacterFormConfig BaseForm(int jobId)
    {
        return FormsOf(jobId)
            .Where(f => f.FormType == FormTypeBase)
            .OrderBy(f => f.UnlockStar)
            .FirstOrDefault();
    }
}
