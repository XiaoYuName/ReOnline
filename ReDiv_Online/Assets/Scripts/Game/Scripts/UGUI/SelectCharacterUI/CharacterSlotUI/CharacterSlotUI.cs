using System;
using ReDiv.Net.Bindings;
using UnityEngine;
using UnityEngine.EventSystems;
using XFramework;

/// <summary>
/// 选人界面里的一个角色格子。
///
/// 两种状态：
///   **空格子** —— 名字 / 等级 / Spine 全隐藏，只剩底座。点它不做任何事（建角色走下面的按钮）。
///   **有角色** —— 显示名字和等级，按当前形态把 UISpine 预制体实例化到 SkeletonPoint 下并播待机。
///
/// 选中态由 <see cref="SelectCharacterUI"/> 统一管（单选），格子自己不决定谁被选中 ——
/// 格子只负责「被点了就喊一声」，喊的方式是 <see cref="Clicked"/>。
///
/// 点击不靠 Button：预制体根节点本来就有一张 Image（整格都是 raycast 目标），
/// 所以直接实现 <see cref="IPointerClickHandler"/>，整格可点，不用改美术搭好的预制体。
/// </summary>
public partial class CharacterSlotUI : UIBase, IPointerClickHandler
{
    /// <summary>这个格子被点了。参数是自己，父面板据此做单选。</summary>
    public event Action<CharacterSlotUI> Clicked;

    /// <summary>格子上是不是有角色。空格子点了也不该被选中。</summary>
    public bool HasCharacter { get; private set; }

    /// <summary>格子上角色的 id，空格子是 0。</summary>
    public ulong CharacterId { get; private set; }

    /// <summary>
    /// 格子上角色的名字，空格子是空串。删除时要在确认弹窗里报出名字，
    /// 单独存一份而不是去读 <c>characterName.text</c> —— 那是显示用的，
    /// 以后加了装饰字符就对不上了。
    /// </summary>
    public string CharacterName { get; private set; } = string.Empty;

    /// <summary>当前实例化出来的 UISpine。换角色 / 清空时要销毁，否则会越叠越多。</summary>
    private CharacterGraphicUI graphic;

    public override void Init()
    {
        InitAutoBind();

        SetSelected(false);
        SetEmpty();
    }

    // ------------------------------------------------------------------
    // 状态切换
    // ------------------------------------------------------------------

    /// <summary>切成空格子：名字 / 等级 / Spine 全隐藏。</summary>
    public void SetEmpty()
    {
        HasCharacter = false;
        CharacterId = 0;
        CharacterName = string.Empty;

        characterName.gameObject.SetActive(false);
        characterLevel.gameObject.SetActive(false);

        ClearGraphic();
        SetSelected(false);
    }

    /// <summary>
    /// 摆上一个角色。
    ///
    /// <paramref name="row"/> 是 <c>my_character</c> View 下发的行，
    /// 里面的 <c>FormId</c> 是**服务端按星级算好的当前形态**，客户端别自己再算一遍。
    /// </summary>
    public void SetCharacter(MyCharacterRow row)
    {
        HasCharacter = true;
        CharacterId = row.CharacterId;
        CharacterName = row.Name;

        characterName.gameObject.SetActive(true);
        characterLevel.gameObject.SetActive(true);
        characterName.text = row.Name;
        characterLevel.text = $"Lv.{row.Level}";

        LoadGraphic(row.JobId, row.FormId);
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

    // ------------------------------------------------------------------
    // UISpine
    // ------------------------------------------------------------------

    /// <summary>
    /// 按 (JobId, FormId) 从配置里取 UISpine 预制体，实例化到 SkeletonPoint 下并播待机动画。
    ///
    /// 配置里 <c>SkeletonUI</c>（「UI展示预制体」）那一列填的是**UISpine 预制体**的
    /// Addressable 完整路径，预制体上的 <see cref="CharacterGraphicUI"/> 自带待机动画名 ——
    /// 所以这里不需要知道动画叫什么，避免了「配置表里手打动画名」那一层。
    /// （形态表另外还有 SkeletonScreen「战斗Spine预制体」，那个是战斗里用的，选人界面不碰。）
    /// </summary>
    private void LoadGraphic(uint jobId, uint formId)
    {
        ClearGraphic();

        var form = LubanManager.Instance.TbCharacterForm?.Get((int)jobId, (int)formId);

        if (form == null)
        {
            Debug.LogWarning($"[CharacterSlot] 配置里没有 (JobId={jobId}, FormId={formId}) 这个形态，" +
                             "格子上不显示形象。改完 Excel 记得重新导出配置。", this);
            return;
        }

        if (string.IsNullOrEmpty(form.SkeletonUI))
        {
            // 这个形态还没配 UISpine（比如美术只做了基础形态）。不当错误，静默留空底座就行 ——
            // 角色资源配置窗口会把这条报出来，不用在运行时反复刷日志。
            return;
        }

        // LoadAsset 是 UIBase 的托管加载：面板关闭 / 销毁时自动 FreeAsset，不用手动配对释放
        var prefab = LoadAsset<GameObject>(form.SkeletonUI);

        if (prefab == null)
        {
            Debug.LogError($"[CharacterSlot] UISpine 预制体加载不出来：{form.SkeletonUI}", this);
            return;
        }

        var go = Instantiate(prefab, skeletonPoint, false);
        go.transform.localPosition = Vector3.zero;

        graphic = go.GetComponent<CharacterGraphicUI>();

        if (graphic == null)
        {
            Debug.LogError($"[CharacterSlot] {form.SkeletonUI} 上没有 CharacterGraphicUI 组件，播不了待机动画", this);
            return;
        }

        graphic.Init();
        graphic.PlayIdle();
    }

    private void ClearGraphic()
    {
        if (graphic != null)
        {
            Destroy(graphic.gameObject);
            graphic = null;
        }

        // 兜底：万一之前有没走 graphic 这条路挂上去的东西（热重载、手工塞的），一并清掉，
        // 否则换角色时旧形象会留在底座上叠着
        if (skeletonPoint == null)
        {
            return;
        }

        for (int i = skeletonPoint.childCount - 1; i >= 0; i--)
        {
            Destroy(skeletonPoint.GetChild(i).gameObject);
        }
    }
}
