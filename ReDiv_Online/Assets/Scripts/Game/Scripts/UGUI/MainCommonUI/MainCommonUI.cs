using System.Collections.Generic;
using ReDiv.Net;
using ReDiv.Net.Bindings;
using UnityEngine;
using XFramework;

/// <summary>
/// 城镇主界面 —— 选人界面点「进入游戏」之后进的就是这里。
///
/// 四件事：
///   1. **背景** —— 按「当前城镇 + 当前时段」走 <c>UISystem.LoadUIBackground</c>；
///   2. **右上角信息** —— 等级 / 经验 / 体力（角色级）+ 金币 / 钻石（账号级，全角色共享）；
///   3. **自己的角色** —— 按 (JobId, FormId) 取城镇控制预制体，摇杆驱动移动并上报坐标；
///   4. **同城镇的其他玩家** —— 按服务端推的坐标插值跟随。
///
/// 数据全部来自三个门面（<see cref="TownManager"/> / <see cref="CharacterManager"/>），
/// **本界面不碰 Conn、不自己算时段**。
///
/// 城镇角色是**世界空间**的，挂在场景的 <c>SkeletonCharacters</c> 节点下
/// （<see cref="TownCharacterSpawner"/> 负责取用和回收，走工程内置的 PoolManager），
/// 不在 UI 的 Canvas 里 —— 所以移动是改 <c>transform.position</c>，不是 anchoredPosition。
///
/// 角色是**两层**的：外层 <see cref="TownCharacterController"/>（名字、以后的血条称号）
/// 套着按形态取的 <see cref="TownSkeletonController"/>（Spine）。本界面只跟外层打交道。
/// </summary>
public partial class MainCommonUI : UIBase
{
    /// <summary>
    /// 坐标上报的最小间隔（秒）。**别改小** —— 这是整个模块里调用最频繁的 Reducer，
    /// 每帧发会把连接打满。100ms 对城镇走路足够，远端还有插值兜平滑。
    /// </summary>
    private const float TransformReportInterval = 0.1f;

    /// <summary>位置变化小于这个距离就不上报，省掉站着不动时的无效包。</summary>
    private const float TransformReportEpsilon = 0.01f;

    // ------------------------------------------------------------------
    // 状态
    // ------------------------------------------------------------------

    private TownBackgroundController townBackground;
    private string townBackgroundKey = string.Empty;

    private readonly TownCharacterSpawner spawner = new TownCharacterSpawner();

    /// <summary>自己的角色。没进城镇 / 配置没配城镇预制体时是 null。</summary>
    private TownCharacterController selfCharacter;

    /// <summary>
    /// <see cref="selfCharacter"/> 现在用的是哪个形态。用来让 <see cref="RefreshSelfCharacter"/>
    /// **幂等** —— 它挂在 CharactersChanged 这种会频繁触发的事件上，
    /// 每次都回收重建的话角色会不停闪、位置也会被拉回原点。
    /// </summary>
    private uint selfJobId;
    private uint selfFormId;

    /// <summary>已经摆出来的其他玩家，key 是 CharacterId。</summary>
    private readonly Dictionary<ulong, TownCharacterController> otherCharacters =
        new Dictionary<ulong, TownCharacterController>();

    /// <summary>上一次上报的坐标和时间，用来节流。</summary>
    private Vector2 lastReportedPosition;
    private float lastReportTime;
    private bool lastReportedMoving;

    private bool hooked;

    // ------------------------------------------------------------------
    // 生命周期
    // ------------------------------------------------------------------

    public override void Init()
    {
        InitAutoBind();
    }

    public override void Open()
    {
        base.Open();

        HookEvents();
        RefreshBackground();
        RefreshInfo();
        RefreshSelfCharacter();
        RefreshOtherCharacters();
    }

    public override void Close()
    {
        UnhookEvents();

        ClearCharacters();
        HideBackground();

        base.Close();
    }

    protected override void OnDestroy()
    {
        UnhookEvents();
        ClearCharacters();

        base.OnDestroy();
    }

    private void Update()
    {
        // 界面没打开就别跑移动逻辑（Close 之后 GameObject 还在，Update 照样会被调）
        if (!isOpen)
        {
            return;
        }

        TickSelfMovement();
        TickOtherCharacters();
    }

    // ------------------------------------------------------------------
    // 事件
    // ------------------------------------------------------------------

    /// <summary>门面的事件是 C# 事件，重复挂会收到重复回调 —— 用标志位挡住。</summary>
    private void HookEvents()
    {
        if (hooked)
        {
            return;
        }
        hooked = true;

        var town = TownManager.Instance;
        town.Ready += HandleTownReady;
        town.WorldTimeChanged += RefreshBackground;
        town.LocationChanged += HandleLocationChanged;
        town.TownPlayersChanged += RefreshOtherCharacters;

        // ⚠️ 角色数据可能**比本界面打开得晚**（订阅还在路上）。所以角色列表一到位
        // 就要重试「摆自己的形象」，不能只刷右上角数字 —— 否则城镇里没有自己。
        // RefreshSelfCharacter 是幂等的（形态没变就不重建），所以挂在高频事件上也安全。
        var characters = CharacterManager.Instance;
        characters.CharactersChanged += HandleCharacterDataChanged;
        characters.WalletChanged += RefreshInfo;
        characters.Ready += HandleCharacterDataChanged;
    }

    private void UnhookEvents()
    {
        if (!hooked)
        {
            return;
        }
        hooked = false;

        var town = TownManager.Instance;
        town.Ready -= HandleTownReady;
        town.WorldTimeChanged -= RefreshBackground;
        town.LocationChanged -= HandleLocationChanged;
        town.TownPlayersChanged -= RefreshOtherCharacters;

        var characters = CharacterManager.Instance;
        characters.CharactersChanged -= HandleCharacterDataChanged;
        characters.WalletChanged -= RefreshInfo;
        characters.Ready -= HandleCharacterDataChanged;
    }

    private void HandleCharacterDataChanged()
    {
        RefreshInfo();
        RefreshSelfCharacter();
    }

    private void HandleTownReady()
    {
        RefreshBackground();
        RefreshInfo();
        RefreshSelfCharacter();
        RefreshOtherCharacters();
    }

    /// <summary>换城镇 / 换角色：背景、自己的形象、别人都要重来。</summary>
    private void HandleLocationChanged()
    {
        RefreshBackground();
        RefreshInfo();
        RefreshSelfCharacter();
        RefreshOtherCharacters();
    }

    // ------------------------------------------------------------------
    // 右上角信息
    // ------------------------------------------------------------------

    /// <summary>
    /// 刷等级 / 经验 / 体力 / 金币 / 钻石。
    ///
    /// 等级、经验、体力是**当前这个角色**的（来自 <c>my_character</c>）；
    /// 金币钻石是**账号级、全角色共享**的（来自 <c>my_wallet</c>）。
    ///
    /// 经验条和体力条的**分母都来自配置表 <c>TbLevelExp</c>**，不占同步量 ——
    /// 服务端只发当前值，上限客户端按等级自己查。
    /// </summary>
    private void RefreshInfo()
    {
        var characters = CharacterManager.Instance;
        MyCharacterRow row = characters.FindCharacter(TownManager.Instance.CurrentCharacterId);

        if (row == null)
        {
            // 还没进城镇 / 订阅还没生效：清成 0，别显示上一个角色的残留
            SetText(levelValueTex, "-");
            SetSlider(expSlider, 0f);
            SetSlider(strengthSlider, 0f);
            SetText(strengthValue, "-");
            SetText(coinValue, characters.Coin.ToString());
            SetText(gemValue, characters.Gem.ToString());
            return;
        }

        SetText(levelValueTex, row.Level.ToString());

        LevelExp levelConfig = LubanManager.Instance.TbLevelExp?.GetOrDefault((int)row.Level);

        // 经验条：满级（ExpToNext=0）按满显示，否则「当前经验 / 升到下一级所需」
        int expToNext = levelConfig?.ExpToNext ?? 0;
        SetSlider(expSlider, expToNext <= 0 ? 1f : Mathf.Clamp01((float)row.Exp / expToNext));

        // 体力条：当前 / 该等级上限
        int maxStamina = levelConfig?.MaxStamina ?? 0;
        SetSlider(strengthSlider, maxStamina <= 0 ? 0f : Mathf.Clamp01((float)row.Stamina / maxStamina));
        SetText(strengthValue, $"{row.Stamina}/{maxStamina}");

        SetText(coinValue, characters.Coin.ToString());
        SetText(gemValue, characters.Gem.ToString());
    }

    private static void SetText(TMPro.TextMeshProUGUI target, string value)
    {
        if (target != null)
        {
            target.text = value;
        }
    }

    private static void SetSlider(UnityEngine.UI.Slider target, float value01)
    {
        if (target != null)
        {
            target.value = value01;
        }
    }

    // ------------------------------------------------------------------
    // 自己的角色 + 摇杆移动
    // ------------------------------------------------------------------

    /// <summary>
    /// 按当前角色的 (JobId, FormId) 摆出自己的城镇形象。
    /// 换角色 / 觉醒改了形态都会走到这里，所以先回收旧的。
    /// </summary>
    private void RefreshSelfCharacter()
    {
        var town = TownManager.Instance;
        MyCharacterRow found = CharacterManager.Instance.FindCharacter(town.CurrentCharacterId);

        if (town.CurrentTownId == 0 || found == null)
        {
            // 还没进城镇，或者角色数据还没同步下来。**不是错误** ——
            // 角色列表一到位 CharactersChanged 就会把这里再调一遍
            RecycleSelf();
            return;
        }

        // 形态没变就什么都不做，别把角色拉回原点
        if (selfCharacter != null && selfJobId == found.JobId && selfFormId == found.FormId)
        {
            return;
        }

        RecycleSelf();

        selfCharacter = spawner.Acquire(found.JobId, found.FormId);

        if (selfCharacter == null)
        {
            return;
        }

        selfJobId = found.JobId;
        selfFormId = found.FormId;

        selfCharacter.SetName(found.Name);

        // 进城镇的落点：服务端还没有坐标的概念（第一次进来），先落在原点。
        // 以后城镇配置里加「出生点」的话在这里取
        selfCharacter.Teleport(Vector2.zero);
        lastReportedPosition = Vector2.zero;
        lastReportTime = 0f;
        lastReportedMoving = false;

        // 立刻上报一次，别人才能马上看见我
        town.ReportTransform(0f, 0f, selfCharacter.Facing, false);
    }

    /// <summary>每帧按摇杆走，并按节流上报坐标。</summary>
    private void TickSelfMovement()
    {
        if (selfCharacter == null || fixedJoystick == null)
        {
            return;
        }

        Vector2 direction = fixedJoystick.Direction;
        bool moved = selfCharacter.MoveByInput(direction, Time.deltaTime);

        Vector2 position = selfCharacter.transform.position;

        // 节流：位置几乎没变、且「在不在走」这个状态也没变，就不发
        bool movingChanged = moved != lastReportedMoving;
        bool positionChanged = (position - lastReportedPosition).sqrMagnitude >
                               TransformReportEpsilon * TransformReportEpsilon;

        if (!movingChanged && !positionChanged)
        {
            return;
        }

        if (Time.time - lastReportTime < TransformReportInterval)
        {
            return;
        }

        lastReportTime = Time.time;
        lastReportedPosition = position;
        lastReportedMoving = moved;

        TownManager.Instance.ReportTransform(position.x, position.y, selfCharacter.Facing, moved);
    }

    // ------------------------------------------------------------------
    // 其他玩家
    // ------------------------------------------------------------------

    /// <summary>
    /// 同步「有哪些人在这个城镇」。只在有人进出时调（<c>TownPlayersChanged</c>），
    /// **不是每帧** —— 每帧要做的是插值，那在 <see cref="TickOtherCharacters"/>。
    /// </summary>
    private void RefreshOtherCharacters()
    {
        var players = TownManager.Instance.TownPlayers;

        // 走了的：回收
        var gone = new List<ulong>();
        foreach (var pair in otherCharacters)
        {
            if (!players.ContainsKey(pair.Key))
            {
                gone.Add(pair.Key);
            }
        }

        foreach (ulong characterId in gone)
        {
            spawner.Recycle(otherCharacters[characterId]);
            otherCharacters.Remove(characterId);
        }

        // 新来的：摆出来
        foreach (var pair in players)
        {
            if (otherCharacters.ContainsKey(pair.Key))
            {
                continue;
            }

            TownPlayer player = pair.Value;
            TownCharacterController controller = spawner.Acquire(player.JobId, player.FormId);

            if (controller == null)
            {
                continue;
            }

            // ⚠️ 还没收到坐标就先别摆到 (0,0) —— 那会让他在原点闪一下再跳走。
            // 摆到自己看不见的地方不行（要能看见他站在原点也是合理的），
            // 所以：有坐标就用坐标，没坐标就用原点，但下一帧插值会立刻纠正
            controller.SetName(player.Name);
            controller.Teleport(player.HasTransform ? new Vector2(player.X, player.Y) : Vector2.zero);
            controller.SetFacing(player.Facing);
            otherCharacters[pair.Key] = controller;
        }
    }

    /// <summary>每帧把其他玩家插值到服务端报来的坐标。</summary>
    private void TickOtherCharacters()
    {
        var players = TownManager.Instance.TownPlayers;

        foreach (var pair in otherCharacters)
        {
            if (!players.TryGetValue(pair.Key, out TownPlayer player) || !player.HasTransform)
            {
                continue;
            }

            pair.Value.MoveTowards(new Vector2(player.X, player.Y), player.Moving, Time.deltaTime);
            pair.Value.SetFacing(player.Facing);
        }
    }

    private void RecycleSelf()
    {
        if (selfCharacter != null)
        {
            spawner.Recycle(selfCharacter);
            selfCharacter = null;
        }

        selfJobId = 0;
        selfFormId = 0;
    }

    /// <summary>回收所有城镇角色并拆池子。离开城镇时调。</summary>
    private void ClearCharacters()
    {
        RecycleSelf();

        foreach (var pair in otherCharacters)
        {
            spawner.Recycle(pair.Value);
        }

        otherCharacters.Clear();
        spawner.Release();
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

        Debug.Log($"[MainCommonUI] 城镇={TownManager.Instance.CurrentTownId}" +
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
