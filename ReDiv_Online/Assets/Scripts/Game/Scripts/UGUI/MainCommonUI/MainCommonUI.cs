using ReDiv.Net;
using XFramework;

/// <summary>
/// 城镇主界面 —— 选人界面点「进入游戏」之后进的就是这里。
///
/// 现在只做一件事：**按「当前城镇 + 当前时段」显示背景**。
/// 玩家移动、功能按钮那些等做的时候往这里加。
///
/// 背景怎么来：
/// <code>
/// TownManager.CurrentTownId ──┐
///                            ├─→ CurrentBackgroundKey（配置表 Town 的三列之一）
/// TownManager.CurrentBandId ──┘         │
///                                       ↓
///          UISystem.LoadUIBackground&lt;TownBackgroundController&gt;(key)
/// </code>
///
/// 两个值都来自服务端（城镇来自选角行、时段来自 <c>world_time</c> 公开表），
/// **本界面不碰 Conn、也不自己算时段**。换城镇 / 服务端切时段都会触发
/// <see cref="TownManager"/> 的事件，这里统一走 <see cref="RefreshBackground"/> 重刷。
///
/// 回收用 <c>HideUIBackground</c>（进对象池）而不是 <c>ReleaseUIBackground</c>（真销毁）：
/// 早↔中↔晚来回切、以及「回选人界面再进来」都很常见，留在池里下次直接复用。
/// 确定某个城镇短期不会再进了才值得 Release。
/// </summary>
public partial class MainCommonUI : UIBase
{
    /// <summary>当前挂着的背景。没有就是 null。</summary>
    private TownBackgroundController townBackground;

    /// <summary>当前背景用的 Addressable key。和新算出来的一样就不用换。</summary>
    private string townBackgroundKey = string.Empty;

    private bool hooked;

    public override void Init()
    {
        InitAutoBind();
    }

    public override void Open()
    {
        base.Open();

        HookTownEvents();
        RefreshBackground();
    }

    public override void Close()
    {
        UnhookTownEvents();
        HideBackground();

        base.Close();
    }

    protected override void OnDestroy()
    {
        UnhookTownEvents();

        base.OnDestroy();
    }

    // ------------------------------------------------------------------
    // 城镇 / 时段事件
    // ------------------------------------------------------------------

    /// <summary>
    /// TownManager 的事件是 C# 事件，重复挂会收到重复回调 —— 用一个标志位挡住
    ///（账号和角色那边也是这么处理的）。
    /// </summary>
    private void HookTownEvents()
    {
        if (hooked)
        {
            return;
        }
        hooked = true;

        var town = TownManager.Instance;
        town.Ready += RefreshBackground;
        town.WorldTimeChanged += RefreshBackground;
        town.LocationChanged += RefreshBackground;
    }

    private void UnhookTownEvents()
    {
        if (!hooked)
        {
            return;
        }
        hooked = false;

        var town = TownManager.Instance;
        town.Ready -= RefreshBackground;
        town.WorldTimeChanged -= RefreshBackground;
        town.LocationChanged -= RefreshBackground;
    }

    // ------------------------------------------------------------------
    // 背景
    // ------------------------------------------------------------------

    /// <summary>
    /// 按当前状态刷背景。**幂等** —— 该显示的 key 没变就什么都不做，
    /// 所以时段推送和位置推送先后到达（会连着触发两次）也不会重建两遍。
    /// </summary>
    private void RefreshBackground()
    {
        string key = TownManager.Instance.CurrentBackgroundKey;

        if (key == townBackgroundKey)
        {
            return;
        }

        HideBackground();
        townBackgroundKey = key;

        if (string.IsNullOrEmpty(key))
        {
            // 不在城镇里（订阅还没生效），或者这个城镇的这个时段还没配背景。
            // 都不是错误 —— 服务端自检和配置侧会把没配的报出来，运行时别反复刷日志
            return;
        }

        townBackground = UISystem.Instance.LoadUIBackground<TownBackgroundController>(key);

        if (townBackground == null)
        {
            // LoadUIBackground 内部已经报过错（找不到挂载层 / 预制体上没有那个组件）
            townBackgroundKey = string.Empty;
            return;
        }

        UnityEngine.Debug.Log($"[MainCommonUI] 城镇={TownManager.Instance.CurrentTownId}" +
                  $"（{TownManager.Instance.CurrentTown?.Name}）" +
                  $" 时段={TownManager.Instance.CurrentBandId}" +
                  $"（{TownManager.Instance.CurrentBand?.Name}） 背景={key}");
    }

    private void HideBackground()
    {
        if (townBackground != null)
        {
            // Hide 而不是 Release：回收进对象池，同一个 key 下次直接复用
            UISystem.Instance.HideUIBackground(townBackground);
            townBackground = null;
        }

        townBackgroundKey = string.Empty;
    }
}
