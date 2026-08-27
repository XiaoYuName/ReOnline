using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using ReDiv.Net;
using ReDiv.Net.Bindings;
using UnityEngine;
using XFramework;

/// <summary>
/// 城镇主界面 —— 选人界面点「进入游戏」之后进的就是这里。
///
/// 六件事：
///   1. **背景 + 出生点** —— 背景是**世界空间**的，按「当前城镇 + 当前时段」换；
///      出生点在背景外层控制器的 <c>StartPoint</c> 上；
///   2. **右上角信息** —— 等级 / 经验 / 体力（角色级）+ 金币 / 钻石（账号级，全角色共享）；
///   3. **自己的角色** —— 按 (JobId, FormId) 取城镇控制预制体，摇杆驱动移动并上报坐标；
///   4. **同城镇的其他玩家** —— 按服务端推的坐标插值跟随；
///   5. **NPC** —— 按配置表 <c>TownNpc</c> 摆在固定坐标上（纯客户端，服务端不知道它们）；
///   6. **聊天** —— 左下角的聊天框（**两个频道混在一起**的滚动日志，世界消息带 `[世界]` 前缀），
///      输入框 + 发送按钮只发**附近消息**；收到消息还要在说话人头上**冒气泡**；
///      右下角「世界聊天」按钮打开 <c>PopMessageUI</c>（那边才有按频道分开的页签）。
///
/// 数据全部来自三个门面（<see cref="TownManager"/> / <see cref="CharacterManager"/> /
/// <see cref="ChatManager"/>），**本界面不碰 Conn、不自己算时段**。
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

    /// <summary>
    /// 背景的**外层控制器**（世界空间，挂在 <c>Games/Backgrounds</c> 下）。
    /// 所有城镇共用一个预制体，里面按时段塞背景，**出生点也在它身上**。
    /// </summary>
    private TownBackgroundController townBackground;

    /// <summary>当前塞在外层里的背景 key（配置表 Town 的三列之一）。空 = 没有背景。</summary>
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

    /// <summary>已经摆出来的 NPC。它们站着不动，所以只要能收回去就行，不用按 id 索引。</summary>
    private readonly List<TownCharacterController> npcs = new List<TownCharacterController>();

    /// <summary>NPC 现在摆的是哪个城镇的，用来让 <see cref="RefreshNpcs"/> 幂等。0 = 还没摆。</summary>
    private uint npcTownId;

    /// <summary>
    /// <see cref="selfCharacter"/> 现在落位在哪个城镇。0 = 还没落位。
    ///
    /// **换城镇时靠它把人挪到新城镇的出生点** —— 形态没变时
    /// <see cref="RefreshSelfCharacter"/> 会提前返回（那是为了别把角色拉回原点），
    /// 而传送恰恰是「形态不变但必须重新落位」的情况。漏了这个字段的表现是
    /// 「传送过去之后站在旧城镇的坐标上」，可能直接在可行走边界外面。
    /// </summary>
    private uint selfTownId;

    /// <summary>
    /// 当前城镇的触发器（配置表 <c>TbTownTrigger</c>）。换城镇时整批换。
    /// </summary>
    private readonly List<TownTriggerController> triggers = new List<TownTriggerController>();

    /// <summary>触发器现在摆的是哪个城镇的，用来让 <see cref="RefreshTriggers"/> 幂等。</summary>
    private uint triggerTownId;

    /// <summary>
    /// 传送成功之后要落在**哪个传送点**旁边（0 = 没有，落回城镇出生点）。
    ///
    /// 传送阵是**成对**的：从 A 过去要出现在对端 B 的旁边，而不是新城镇的出生点。
    /// 但「换城镇」这件事是绕服务端走一圈才回来的（`ChangeTown` → `character_selection`
    /// 推回来 → `LocationChanged` → 重新落位），中间隔着一次往返，所以得先把
    /// 「我是从哪个传送点过去的」记下来，等落位那一步再兑掉。
    ///
    /// ⚠️ **必须在调 `ChangeTownAsync` 之前就记**，不能等它 await 返回 ——
    /// 表更新（于是 LocationChanged）通常比 Reducer 的状态回调**先**到，
    /// 等返回再记就已经落位完了，人会站在出生点上。
    /// </summary>
    private int pendingArriveTriggerId;

    /// <summary>
    /// 玩家**当前站在**哪个触发器里（0 = 不在任何触发器里）。
    ///
    /// ⚠️ 这个字段是整套触发逻辑的关键：只在它**从别的值变成某个触发器**的那一帧才触发一次。
    /// 不记状态的话站在门口会每帧发一次传送请求。
    /// 进城镇 / 传送落地时它由 <see cref="RefreshTriggers"/> 按当前位置**初始化**（不触发），
    /// 所以出生点正好压在触发器上也不会立刻被弹走。
    /// </summary>
    private int currentTriggerId;

    /// <summary>上一次上报的坐标和时间，用来节流。</summary>
    private Vector2 lastReportedPosition;
    private float lastReportTime;
    private bool lastReportedMoving;

    private bool hooked;

    /// <summary>聊天框里已经摆出来的格子，和可见消息一一对应（第 0 个是最旧的那条）。</summary>
    private readonly List<MessageSlot> messageSlots = new List<MessageSlot>();

    /// <summary>
    /// 消息格子预制体，**只加载一次**（见 <see cref="MessageSlotPrefab"/> 为什么要缓存）。
    /// 界面关闭时置回 null —— <c>Release()</c> 会把 AA 引用还掉，留着就是个悬空引用。
    /// </summary>
    private GameObject messageSlotPrefab;

    // ------------------------------------------------------------------
    // 生命周期
    // ------------------------------------------------------------------

    public override void Init()
    {
        InitAutoBind();

        InitChat();
    }

    public override void Open()
    {
        base.Open();

        HookEvents();
        // ⚠️ 背景必须在角色之前刷：**出生点在背景外层控制器身上**，
        // 反过来的话第一次进城镇会落在原点
        RefreshBackground();
        RefreshInfo();
        RefreshSelfCharacter();
        RefreshOtherCharacters();
        RefreshNpcs();
        RefreshTriggers();
        RefreshMessages();
    }

    public override void Close()
    {
        UnhookEvents();

        ClearCharacters();
        ClearTriggers();
        ClearMessageSlots();
        ReleaseBackground();

        base.Close();
    }

    protected override void OnDestroy()
    {
        UnhookEvents();
        ClearCharacters();
        ClearTriggers();
        ClearMessageSlots();
        ReleaseBackground();

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
        TickTriggers();
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

        // 聊天订阅是**跟着城镇换**的，所以 Ready 会反复触发（每换一个城镇一次），
        // 不是只在开局响一下 —— RefreshMessages 得能被反复调用
        var chat = ChatManager.Instance;
        chat.MessagesChanged += RefreshMessages;
        chat.Ready += RefreshMessages;
        chat.MessageArrived += HandleChatMessageArrived;
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

        var chat = ChatManager.Instance;
        chat.MessagesChanged -= RefreshMessages;
        chat.Ready -= RefreshMessages;
        chat.MessageArrived -= HandleChatMessageArrived;
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
        RefreshNpcs();
        RefreshTriggers();
    }

    /// <summary>换城镇 / 换角色：背景、自己的形象、别人都要重来。</summary>
    private void HandleLocationChanged()
    {
        RefreshBackground();
        RefreshInfo();
        RefreshSelfCharacter();
        RefreshOtherCharacters();
        RefreshNpcs();
        // ⚠️ 触发器必须排在**自己落位之后**：它会按自己当前的位置初始化
        // currentTriggerId（见那个字段的注释），顺序反了会拿旧坐标去判
        RefreshTriggers();
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

        // 形态没变就不重建形象，别把角色拉回原点。
        // ⚠️ 但**换城镇是形态不变、位置必须重来**的情况（传送），所以这里还要问一次落位
        if (selfCharacter != null && selfJobId == found.JobId && selfFormId == found.FormId)
        {
            PlaceSelfAtSpawnIfTownChanged();
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

        PlaceSelfAtSpawn(town.CurrentTownId);
    }

    /// <summary>
    /// 换城镇了就把自己挪到新城镇的出生点。没换（或者还没落位过）就什么都不做。
    ///
    /// 传送走的就是这条路：服务端改完 <c>character_selection.TownId</c> 推回来 ⇒
    /// LocationChanged ⇒ RefreshSelfCharacter ⇒ 形态没变、城镇变了 ⇒ 重新落位。
    /// </summary>
    private void PlaceSelfAtSpawnIfTownChanged()
    {
        uint townId = TownManager.Instance.CurrentTownId;

        if (townId == 0 || townId == selfTownId)
        {
            return;
        }

        PlaceSelfAtSpawn(townId);
    }

    /// <summary>
    /// 把自己落到当前城镇该站的位置上，并立刻上报一次。
    ///
    /// 落点有两种，**传送优先**：
    ///   1. 从传送阵过来 → 站在**对端那个传送点的出口点**上（见 <see cref="pendingArriveTriggerId"/>）；
    ///   2. 其它情况（进游戏、换角色、配置漏了）→ 那张背景的 `StartPoints`
    ///      （用户 2026-08-25 定的「不记住上次站的位置」）。
    ///
    /// 服务端从来不存坐标，所以这两条都是客户端自己算的。
    ///
    /// ⚠️ 必须在 <see cref="RefreshBackground"/> **之后**调：出生点在那张背景上，
    /// 背景还没换的话拿到的是上一个城镇的落点。
    /// </summary>
    private void PlaceSelfAtSpawn(uint townId)
    {
        if (selfCharacter == null)
        {
            return;
        }

        selfTownId = townId;

        Vector2 spawn = TakeArrivePosition(townId) ?? CurrentSpawnPosition();

        selfCharacter.Teleport(spawn);
        lastReportedPosition = spawn;
        lastReportTime = 0f;
        lastReportedMoving = false;

        // 立刻上报一次，别人才能马上看见我。
        // ⚠️ 传送时服务端把坐标行**删掉**了（见 ChangeTown），所以这一次上报不是可选的：
        // 不发的话新城镇的人要等我走第一步才看得见我
        TownManager.Instance.ReportTransform(spawn.x, spawn.y, selfCharacter.Facing, false);
    }

    /// <summary>
    /// 兑掉 <see cref="pendingArriveTriggerId"/>：返回对端传送点的出口坐标，没有就返回 null
    /// （调用方退回城镇出生点）。**不管成不成都会清掉**，免得一个过期的值一直挂着。
    ///
    /// 会核对「那个传送点确实在我要落位的这个城镇里」—— 传送失败、中途换角色、
    /// 或者服务端把我放到了别的城镇时，这个值就是过期的，那就老老实实走出生点。
    /// </summary>
    private Vector2? TakeArrivePosition(uint townId)
    {
        int triggerId = pendingArriveTriggerId;
        pendingArriveTriggerId = 0;

        if (triggerId == 0)
        {
            return null;
        }

        TownTrigger arrive = TownTriggers.Find(triggerId);

        if (arrive == null || arrive.TownId != townId)
        {
            return null;
        }

        Vector2 position = TownTriggers.ArrivePosition(arrive);

        Debug.Log($"[MainCommonUI] 从传送阵到达城镇 {townId}，落在 #{arrive.TriggerId}" +
                  $"（{arrive.Name}）的出口点 {position}");

        return position;
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

            // ⚠️ 还没收到坐标的人别摆到 (0,0) —— 那会让他在画面正中闪一下再跳走。
            // 有坐标就用坐标，没坐标就用**出生点**（他刚进城镇，那儿最接近真值），
            // 下一帧插值会立刻纠正
            controller.SetName(player.Name);
            controller.Teleport(player.HasTransform
                ? new Vector2(player.X, player.Y)
                : CurrentSpawnPosition());
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
        selfTownId = 0;
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

        // ⚠️ 必须在 Release() 之前收 —— 那一步会把池子整个拆掉
        ClearNpcs();

        spawner.Release();
    }

    // ------------------------------------------------------------------
    // NPC（配置表 TownNpc，站在固定坐标上）
    // ------------------------------------------------------------------

    /// <summary>
    /// 按配置表把这个城镇的 NPC 摆出来。**幂等** —— 城镇没变就什么都不做
    /// （它挂在几个会频繁触发的事件上，不挡一下会每次都拆了重摆）。
    ///
    /// NPC 是**纯客户端表现**：服务端没有 NPC 概念，不上报、不同步、不参与
    /// <c>TownPlayers</c>。所以它们也不需要每帧 tick —— 站着不动，摆完就不管了。
    /// </summary>
    private void RefreshNpcs()
    {
        uint townId = TownManager.Instance.CurrentTownId;

        if (townId == npcTownId)
        {
            return;
        }

        ClearNpcs();
        npcTownId = townId;

        if (townId == 0)
        {
            // 还没进城镇（订阅没生效 / 回选人界面了）
            return;
        }

        var table = LubanManager.Instance.TbTownNpc;

        if (table == null)
        {
            return;
        }

        int count = 0;

        foreach (TownNpc row in table.DataList)
        {
            if (row.TownId != townId)
            {
                continue;
            }

            // 没配 SkeletonTown 也照样摆（外层还在，名字能显示）—— 和玩家角色一个待遇
            TownCharacterController npc = spawner.Acquire(row.SkeletonTown);

            if (npc == null)
            {
                continue;
            }

            npc.SetName(row.Name);
            npc.Teleport(new Vector2(row.PosX, row.PosY));
            npc.SetFacing(row.Facing >= 0 ? 1 : -1);

            npcs.Add(npc);
            count++;
        }

        if (count > 0)
        {
            Debug.Log($"[MainCommonUI] 城镇 {townId} 摆了 {count} 个 NPC");
        }
    }

    /// <summary>把 NPC 全收回去。换城镇 / 离开城镇时调。</summary>
    private void ClearNpcs()
    {
        foreach (TownCharacterController npc in npcs)
        {
            spawner.Recycle(npc);
        }

        npcs.Clear();
        npcTownId = 0;
    }

    // ------------------------------------------------------------------
    // 触发器（传送点 / 副本入口）
    // ------------------------------------------------------------------

    /// <summary>
    /// 按当前城镇摆一批触发器。**幂等**（按 <see cref="triggerTownId"/> 挡住）——
    /// 它挂在几个高频事件上，不挡的话每次都会把触发器拆了重摆。
    ///
    /// ⚠️ 顺序上必须排在「自己落位」之后：末尾那一步要按**自己当前的位置**初始化
    /// <see cref="currentTriggerId"/>，拿旧坐标去判就白初始化了。
    /// </summary>
    private void RefreshTriggers()
    {
        uint townId = TownManager.Instance.CurrentTownId;

        if (townId == triggerTownId)
        {
            return;
        }

        ClearTriggers();
        triggerTownId = townId;

        if (townId == 0)
        {
            // 还没进城镇（订阅没生效 / 回选人界面了）
            return;
        }

        GameObject root = TownWorldRoots.Find(TownWorldRoots.BackgroundsTag);

        if (root == null)
        {
            // TownWorldRoots 内部已经报过错
            return;
        }

        foreach (TownTrigger row in TownTriggers.InTown(townId))
        {
            TownTriggerController trigger = CreateTrigger(root.transform, row);

            if (trigger != null)
            {
                triggers.Add(trigger);
            }
        }

        // 站在触发器上进城镇（出生点正好压在门口，或者刚从这里传送过来）
        // **不该立刻触发** —— 先把「现在站在哪」记下来，等玩家走出去再走进来才算一次
        SyncCurrentTrigger();

        if (triggers.Count > 0)
        {
            Debug.Log($"[MainCommonUI] 城镇 {townId} 摆了 {triggers.Count} 个触发器" +
                      $"，当前站在 #{currentTriggerId}（0=没站在任何一个上）");
        }
    }

    /// <summary>
    /// 建一个触发器节点。**没有预制体** —— 它自己没有美术，只是判定区 + 地面标记的挂载点
    /// （见 <see cref="TownTriggerController"/> 的注释）。
    /// </summary>
    private TownTriggerController CreateTrigger(Transform parent, TownTrigger row)
    {
        var holder = new GameObject($"Trigger_{row.TriggerId}");
        holder.transform.SetParent(parent, false);

        TownTriggerController trigger = holder.AddComponent<TownTriggerController>();
        trigger.Bind(row);

        // 地面标记是可选的：没配就只有一个看不见的判定区（现在两个触发器都没配图）
        if (!string.IsNullOrEmpty(row.IconPrefab))
        {
            GameObject icon = AssetsManager.Instance.Instantiate(row.IconPrefab);

            if (icon == null)
            {
                Debug.LogError($"[MainCommonUI] 触发器 {row.TriggerId} 的标记预制体加载不出来：{row.IconPrefab}");
            }
            else
            {
                icon.transform.SetParent(holder.transform, false);
                icon.transform.localPosition = Vector3.zero;
            }
        }

        return trigger;
    }

    /// <summary>
    /// 把触发器全收掉。换城镇 / 离开城镇时调。
    ///
    /// 标记是子节点，所以**先把标记还给 AssetsManager 再销毁本体** ——
    /// 反过来的话子节点跟着本体一起没了，AA 的引用就漏了。
    /// </summary>
    private void ClearTriggers()
    {
        foreach (TownTriggerController trigger in triggers)
        {
            if (trigger == null)
            {
                continue;
            }

            // 只有配了 IconPrefab 的才有子节点
            for (int i = trigger.transform.childCount - 1; i >= 0; i--)
            {
                GameObject icon = trigger.transform.GetChild(i).gameObject;
                icon.SetActive(false);
                AssetsManager.Instance.ReleaseGameObject(icon);
            }

            Destroy(trigger.gameObject);
        }

        triggers.Clear();
        triggerTownId = 0;
        currentTriggerId = 0;
    }

    /// <summary>
    /// 每帧看自己站在哪个触发器里，**只在踩进去的那一帧**触发一次。
    ///
    /// 判定点是角色外层节点的位置，也就是**脚下**（和可行走边界用的是同一点）。
    /// </summary>
    private void TickTriggers()
    {
        if (selfCharacter == null || triggers.Count == 0)
        {
            return;
        }

        // 正在传送的往返途中不判：那会儿位置还在旧城镇，判出来的东西没意义
        if (TownManager.Instance.IsChangingTown)
        {
            return;
        }

        TownTrigger hit = FindTriggerAtSelf();
        int hitId = hit?.TriggerId ?? 0;

        if (hitId == currentTriggerId)
        {
            // 还站在同一个里面（或者还是哪个都不在）——「进入」只算一次
            return;
        }

        currentTriggerId = hitId;

        if (hit == null)
        {
            // 只是走出去了
            return;
        }

        Fire(hit);
    }

    /// <summary>
    /// 按自己**当前的位置**记下站在哪个触发器里，**不触发**。
    /// 进城镇 / 传送落地时用（见 <see cref="currentTriggerId"/> 的注释）。
    /// </summary>
    private void SyncCurrentTrigger()
    {
        currentTriggerId = FindTriggerAtSelf()?.TriggerId ?? 0;
    }

    private TownTrigger FindTriggerAtSelf()
    {
        if (selfCharacter == null)
        {
            return null;
        }

        Vector2 position = selfCharacter.transform.position;

        // triggers 里的 Row 就是配置行，判定走 TownTriggers 的纯几何
        for (int i = 0; i < triggers.Count; i++)
        {
            TownTrigger row = triggers[i]?.Row;

            if (row != null && TownTriggers.Contains(row, position))
            {
                return row;
            }
        }

        return null;
    }

    /// <summary>
    /// 踩到触发器了。
    ///
    /// 两种类型（用户 2026-08-27 定的手感）：
    ///   Kind=1 传送 → **自动**，踩到就走，不弹确认框
    ///   Kind=2 副本 → 打开副本界面，让玩家自己选
    /// </summary>
    private void Fire(TownTrigger row)
    {
        switch (row.Kind)
        {
            case TownTriggers.KindChangeTown:
                TeleportAsync(row).Forget();
                break;

            case TownTriggers.KindDungeon:
                Debug.Log($"[MainCommonUI] 踩到副本入口 #{row.TriggerId}（{row.Name}），打开副本界面");
                UISystem.Instance.OpenUI(UIKeys.PopDungeonUI);
                break;

            default:
                // 走不到：InTown 已经把 Kind 不认识的行滤掉了
                Debug.LogError($"[MainCommonUI] 触发器 {row.TriggerId} 的 Kind={row.Kind} 不认识");
                break;
        }
    }

    /// <summary>
    /// 传送到对端传送点所在的城镇。**传送阵是成对的**：目标城镇和落点都来自对端那一行。
    ///
    /// **成功之后这里什么都不用做**：服务端改完选角行推回来 ⇒ LocationChanged ⇒
    /// 背景、自己（落到**对端传送点的出口点**）、别人、NPC、触发器、聊天订阅域全都自己重来。
    ///
    /// 失败就弹一句服务端的原文。⚠️ 失败之后**不会自动重试** —— 玩家还站在触发器里，
    /// <see cref="currentTriggerId"/> 已经记成它了，所以得走出去再走进来。
    /// 这是有意的：失败原因通常是「去不了」（以后加解锁规则时更是如此），
    /// 原地重试只会每帧刷一个弹窗。
    /// </summary>
    private async UniTask TeleportAsync(TownTrigger row)
    {
        TownTrigger pair = TownTriggers.PairOf(row);

        if (pair == null)
        {
            // 走不到：InTown 的校验已经把连不上对端的行滤掉了
            Debug.LogError($"[MainCommonUI] 传送点 #{row.TriggerId}（{row.Name}）找不到对端 {row.TargetId}");
            return;
        }

        uint targetTownId = (uint)pair.TownId;

        Debug.Log($"[MainCommonUI] 踩到传送点 #{row.TriggerId}（{row.Name}）→ 城镇 {targetTownId} 的 " +
                  $"#{pair.TriggerId}（{pair.Name}）");

        // ⚠️ **必须在 await 之前记**：表更新（于是 LocationChanged → 重新落位）通常比
        // Reducer 的状态回调先到，等下面 await 返回再记就已经落到出生点上了
        pendingArriveTriggerId = pair.TriggerId;

        TownResult result = await TownManager.Instance.ChangeTownAsync(targetTownId);

        if (!result.Ok)
        {
            // 没走成，那个待落位的值就是垃圾了，别留着影响下一次进城镇
            pendingArriveTriggerId = 0;
            UIUtility.ShowWindow(result.Message, "传送失败");
        }
    }

    // ------------------------------------------------------------------
    // 聊天（左下角的聊天框）
    // ------------------------------------------------------------------

    /// <summary>
    /// 聊天框最多摆多少条格子。和服务端每个域的保留窗口对齐
    /// （<c>Module.ChatHistoryPerScope</c>，现在是 50）——
    /// 服务端只会推这么多，摆更多也没内容，摆更少就白丢了能看的历史。
    /// </summary>
    private const int MaxMessageSlots = 50;

    /// <summary>
    /// 滚动条离底多近就算「玩家正在看最新消息」。
    /// <c>verticalNormalizedPosition</c> 是 0=底 / 1=顶，所以这是个很小的数。
    /// </summary>
    private const float StickToBottomThreshold = 0.05f;

    /// <summary>
    /// 接输入框和发送按钮。**在 Init 里做一次**，不在 Open 里 ——
    /// Open 每次进城镇都会调，重复 AddListener 会让一次回车发出去好几条。
    /// （<c>Bind</c> 内部会 RemoveAllListeners 所以按钮那边无所谓，
    /// 但 <c>onSubmit</c> 是直接 AddListener 的，会累加。）
    /// </summary>
    private void InitChat()
    {
        if (sendButton != null)
        {
            Bind(sendButton, SendNearbyMessage, AudioKeys.CursorClick01);
        }

        if (inputFieldTMP != null)
        {
            // 直接把输入框卡在同一个上限上，超了根本打不进去 ——
            // 比让人打完一大段再弹「太长了」友好。ChatValidation 那道校验还是要有：
            // 粘贴 / 清洗（折空白）之后长度可能又变了
            inputFieldTMP.characterLimit = ReDiv.Net.ChatValidation.MaxChars;

            // 输入框是单行的（预制体里 LineType = SingleLine），所以回车会触发 onSubmit。
            // 用 onSubmit 而不是 onEndEdit：后者失焦也会触发，点一下别处就把消息发出去了
            inputFieldTMP.onSubmit.AddListener(HandleInputSubmit);
        }

        // 右下角「世界聊天」→ 打开聊天界面（附近 / 世界两个页签都在那里）。
        // 只开不关：PopMessageUI 自己有 CloseButton（铺满整屏的透明按钮，点空白处关）
        if (openMessageUIButton != null)
        {
            Bind(openMessageUIButton, OpenChatWindow, AudioKeys.CursorClick01);
        }
    }

    private static void OpenChatWindow() => UISystem.Instance.OpenUI(UIKeys.PopMessageUI);

    private void HandleInputSubmit(string _) => SendNearbyMessage();

    /// <summary>
    /// 刚有人说了句话 —— 除了进聊天框，还要在那个人**头上冒个气泡**。
    ///
    /// **自己和别人走同一条路**：都是等服务端把消息推回来才显示。所以气泡里
    /// 显示的一定是服务端真的收下了的那句话（被冷却挡掉 / 超长拒了的不会冒泡），
    /// 而且自己看到的时序和别人看到的一致。
    ///
    /// 找不到人是正常的：说话的人可能刚走出这个城镇、或者他的形态还没配城镇预制体。
    /// 那就只进聊天框、不冒泡，**不报错**。
    /// </summary>
    private void HandleChatMessageArrived(ChatMessage row)
    {
        FindTownCharacter(row.SenderCharacterId)?.ShowMessage(row.Content);
    }

    /// <summary>按角色 id 找场上的那个城镇角色。自己也在内。找不到返回 null。</summary>
    private TownCharacterController FindTownCharacter(ulong characterId)
    {
        if (characterId == 0)
        {
            return null;
        }

        if (characterId == TownManager.Instance.CurrentCharacterId)
        {
            return selfCharacter;
        }

        return otherCharacters.TryGetValue(characterId, out TownCharacterController found) ? found : null;
    }

    /// <summary>
    /// 把输入框里的内容作为**附近消息**发出去。
    ///
    /// 三件必须这么做的事：
    ///   1. **不做本地乐观显示** —— 成功后服务端会把消息推回来（自己发的那条也在里面），
    ///      本地先塞一条会重复；被拒了还得再抠出来。
    ///   2. **等回应期间就把输入框清空**，别等结果 —— 玩家看到字还在会以为没发出去
    ///      而重复点。发失败时再把原文填回去（下面那句 SetText），这样重试不用重打。
    ///   3. 失败一律弹窗，用服务端抛的中文原文（「说话太快了，稍等一下再发」这种）。
    /// </summary>
    private void SendNearbyMessage()
    {
        if (inputFieldTMP == null)
        {
            return;
        }

        string draft = inputFieldTMP.text;

        if (string.IsNullOrWhiteSpace(draft))
        {
            // 空输入不值得弹窗打断（回车连按很常见），静默忽略
            return;
        }

        inputFieldTMP.text = string.Empty;

        SendNearbyMessageAsync(draft).Forget();
    }

    private async UniTaskVoid SendNearbyMessageAsync(string draft)
    {
        ChatResult result = await ChatManager.Instance.SendNearbyAsync(draft);

        if (result.Ok)
        {
            return;
        }

        // 界面可能在等回应的这段时间里被关掉了（回选人界面），那就别再动它
        if (!isOpen)
        {
            return;
        }

        // 把原文填回去，玩家改一改就能重发，不用重打
        if (inputFieldTMP != null && string.IsNullOrEmpty(inputFieldTMP.text))
        {
            inputFieldTMP.text = draft;
        }

        UIUtility.ShowWindow(result.Message, "发送失败");
    }

    /// <summary>
    /// 按当前可见的消息重画聊天框。挂在 <c>MessagesChanged</c> / <c>Ready</c> 上，
    /// 所以会被反复调用 —— **必须能重复调**。
    ///
    /// **复用已有格子，只补 / 收差额**，不是每次全拆重建：一条新消息就重新
    /// 实例化 50 个预制体会明显卡顿，而且正在滚动的话会被打断。
    /// </summary>
    private void RefreshMessages()
    {
        RectTransform content = MessageContent();

        if (content == null)
        {
            return;
        }

        var all = ChatManager.Instance.Messages;

        // 只显示最后 MaxMessageSlots 条（列表是升序，最新的在最后）
        int start = Mathf.Max(0, all.Count - MaxMessageSlots);
        int visible = all.Count - start;

        // 重画前先记住玩家是不是正贴着底看最新消息 —— 他要是滚上去翻历史，
        // 新消息不该把他拽回底部
        bool stickToBottom = IsScrolledToBottom();

        while (messageSlots.Count > visible)
        {
            int last = messageSlots.Count - 1;
            MessageSlot slot = messageSlots[last];
            messageSlots.RemoveAt(last);

            if (slot != null)
            {
                // 先 SetActive(false) 再 Destroy —— Destroy 延迟到帧末，
                // 不关掉的话这一帧里旧格子还占着布局位置
                slot.gameObject.SetActive(false);
                Destroy(slot.gameObject);
            }
        }

        while (messageSlots.Count < visible)
        {
            MessageSlot slot = CreateMessageSlot(content);

            if (slot == null)
            {
                // 预制体加载不出来，报错已经打过了，别在这循环里刷屏
                return;
            }

            messageSlots.Add(slot);
        }

        for (int i = 0; i < visible; i++)
        {
            ChatMessage row = all[start + i];

            // 底部这个框是**两个频道混在一起**的滚动日志（世界消息「所有人在任何地方
            // 都能看到」，藏起来就不叫世界频道了）。加个前缀区分 —— 这一行的格子只有
            // 「名字 + 正文」两段文字，没有第三个地方放频道标记。
            // 想改成只显示附近，把这里换成 `if (row.Channel != ChatManager.ChannelNearby) continue;`
            // 那一类过滤即可（顺带 visible 的算法也要跟着改）
            string sender = row.Channel == ChatManager.ChannelWorld
                ? $"[世界]{row.SenderName}"
                : row.SenderName;

            messageSlots[i].SetMessage(sender, row.Content);
        }

        if (stickToBottom)
        {
            ScrollToBottom();
        }
    }

    /// <summary>
    /// 取消息格子预制体，**加载一次就缓存住**。
    ///
    /// 为什么不每次都调 <c>LoadAsset</c>：那个方法会把 key 记进 <c>AssetReleaser</c>
    /// 托管列表，而 <c>Track</c> **不去重** —— 一条消息一次调用，一屏 50 条就往列表里
    /// 堆 50 条同样的 key。引用计数上是配平的（Release 会一一还掉），
    /// 但纯属白折腾，而且列表会随着「格子拆了又建」一直长。
    /// </summary>
    private GameObject MessageSlotPrefab()
    {
        if (messageSlotPrefab == null)
        {
            messageSlotPrefab = LoadAsset<GameObject>(AssetKeys.MessageSlotPath);
        }

        return messageSlotPrefab;
    }

    private MessageSlot CreateMessageSlot(RectTransform content)
    {
        GameObject prefab = MessageSlotPrefab();

        if (prefab == null)
        {
            Debug.LogError($"[MainCommonUI] 消息格子预制体加载不出来：{AssetKeys.MessageSlotPath}", this);
            return null;
        }

        var go = Instantiate(prefab, content, false);
        var slot = go.GetComponent<MessageSlot>();

        if (slot == null)
        {
            Debug.LogError("[MainCommonUI] MessageSlot 预制体上没有 MessageSlot 组件", this);
            Destroy(go);
            return null;
        }

        slot.Init();

        return slot;
    }

    /// <summary>
    /// 把聊天格子全清掉。离开城镇（本界面关闭 / 销毁）时调。
    ///
    /// ⚠️ 顺带把缓存的预制体引用置回 null：紧接着 <c>base.Close()</c> 会调
    /// <c>Release()</c> 把 AA 引用还掉，留着这个引用下次开界面就是个悬空对象。
    /// </summary>
    private void ClearMessageSlots()
    {
        foreach (MessageSlot slot in messageSlots)
        {
            if (slot != null)
            {
                Destroy(slot.gameObject);
            }
        }

        messageSlots.Clear();
        messageSlotPrefab = null;
    }

    /// <summary>
    /// 聊天格子的父节点 —— ScrollRect 的 <c>content</c>（上面有
    /// VerticalLayoutGroup + ContentSizeFitter，所以摆进去自动从上往下排）。
    /// **不要按路径去 Find** —— content 是 ScrollRect 自己的字段，
    /// 美术把节点改名 / 挪位置都不该让代码坏掉。
    /// </summary>
    private RectTransform MessageContent() => scrollView != null ? scrollView.content : null;

    /// <summary>
    /// 玩家是不是正贴着底看最新消息。
    ///
    /// 内容还没长到超过视口时也算「在底部」—— 那种情况下
    /// <c>verticalNormalizedPosition</c> 的值是没意义的（ScrollRect 会把它夹住），
    /// 拿它判断会得出「玩家在翻历史」的错误结论，于是头几条消息就不自动滚了。
    /// </summary>
    private bool IsScrolledToBottom()
    {
        if (scrollView == null)
        {
            return false;
        }

        RectTransform content = scrollView.content;
        RectTransform viewport = scrollView.viewport != null
            ? scrollView.viewport
            : scrollView.transform as RectTransform;

        if (content == null || viewport == null)
        {
            return true;
        }

        if (content.rect.height <= viewport.rect.height)
        {
            return true;
        }

        return scrollView.verticalNormalizedPosition <= StickToBottomThreshold;
    }

    /// <summary>
    /// 滚到底（最新那条）。
    ///
    /// ⚠️ **必须先强制重算一次布局**：VerticalLayoutGroup + ContentSizeFitter 算高度
    /// 是排在下一次布局阶段的，不重算的话这里用的是**上一帧**的高度 ⇒
    /// 刚加进来的那条还没被算进去 ⇒ 滚动停在倒数第二条上。
    /// </summary>
    private void ScrollToBottom()
    {
        if (scrollView == null || scrollView.content == null)
        {
            return;
        }

        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(scrollView.content);

        // 0 = 底部
        scrollView.verticalNormalizedPosition = 0f;
    }

    // ------------------------------------------------------------------
    // 背景（**世界空间**）+ 出生点
    // ------------------------------------------------------------------

    /// <summary>
    /// 按当前状态刷背景。**幂等** —— 该显示的 key 没变就什么都不做，
    /// 所以时段推送和位置推送先后到达（会连着触发两次）也不会重建两遍。
    ///
    /// 两层结构和角色一样：**外层控制器**所有城镇共用（出生点在它身上），
    /// 里面按「城镇 + 时段」塞一张背景。所以换时段只换里面那张，外层不动。
    /// </summary>
    private void RefreshBackground()
    {
        string key = TownManager.Instance.CurrentBackgroundKey;

        if (key == townBackgroundKey)
        {
            return;
        }

        if (string.IsNullOrEmpty(key))
        {
            // 不在城镇里（订阅还没生效），或者这个城镇的这个时段还没配背景。
            // 都不是错误 —— 服务端自检和配置侧会把没配的报出来，运行时别反复刷日志。
            // ⚠️ 外层**不收**：出生点还要用，而且下一次推送马上就会把背景补回来
            ReleaseBackgroundView();
            townBackgroundKey = string.Empty;
            return;
        }

        if (!EnsureController())
        {
            townBackgroundKey = string.Empty;
            return;
        }

        ReleaseBackgroundView();
        townBackgroundKey = key;

        GameObject view = AssetsManager.Instance.Instantiate(key);

        if (view == null)
        {
            Debug.LogError($"[MainCommonUI] 背景预制体加载不出来：{key}");
            townBackgroundKey = string.Empty;
            return;
        }

        townBackground.Bind(view);

        Debug.Log($"[MainCommonUI] 城镇={TownManager.Instance.CurrentTownId}" +
                  $"（{TownManager.Instance.CurrentTown?.Name}）" +
                  $" 时段={TownManager.Instance.CurrentBandId}" +
                  $"（{TownManager.Instance.CurrentBand?.Name}） 背景={key}" +
                  $" 出生点={townBackground.SpawnPosition}");
    }

    /// <summary>
    /// 确保外层控制器在场上。所有城镇共用同一个预制体，所以进城镇建一次就够，
    /// 换城镇 / 换时段都不用重建。
    /// </summary>
    private bool EnsureController()
    {
        if (townBackground != null)
        {
            return true;
        }

        GameObject root = TownWorldRoots.Find(TownWorldRoots.BackgroundsTag);

        if (root == null)
        {
            // TownWorldRoots 内部已经报过错
            return false;
        }

        GameObject instance = AssetsManager.Instance.Instantiate(TownBackgroundController.PrefabKey);

        if (instance == null)
        {
            Debug.LogError($"[MainCommonUI] 背景外层预制体加载不出来：{TownBackgroundController.PrefabKey}");
            return false;
        }

        // 挂到 Games/Backgrounds 下（它和上面几层都是原点 + 缩放 1，
        // 所以预制体里摆的坐标就是世界坐标）。⚠️ 不能挂 UI 的 Canvas 下
        instance.transform.SetParent(root.transform, false);
        instance.transform.localPosition = Vector3.zero;

        townBackground = instance.GetComponent<TownBackgroundController>();

        if (townBackground == null)
        {
            Debug.LogError($"[MainCommonUI] {TownBackgroundController.PrefabKey} 上没有 TownBackgroundController 组件");
            instance.SetActive(false);
            AssetsManager.Instance.ReleaseGameObject(instance);
            return false;
        }

        return true;
    }

    /// <summary>只回收里面那张背景，外层留着（出生点还要用）。换时段走这条。</summary>
    private void ReleaseBackgroundView()
    {
        GameObject view = townBackground != null ? townBackground.Unbind() : null;

        if (view == null)
        {
            return;
        }

        // 先 SetActive(false) 再销毁 —— Destroy 延迟到帧末，紧接着又实例化下一个时段的背景时
        // 两张会在同一帧里叠着
        view.SetActive(false);
        AssetsManager.Instance.ReleaseGameObject(view);
    }

    /// <summary>
    /// 里外都收。离开城镇（本界面关闭 / 销毁）时调。
    ///
    /// 用 <c>ReleaseGameObject</c>（销毁 + 卸掉 Addressables 引用）而不是回池：
    /// 背景一张就满屏，留在池里白占内存和引用。
    /// </summary>
    private void ReleaseBackground()
    {
        ReleaseBackgroundView();

        if (townBackground != null)
        {
            GameObject instance = townBackground.gameObject;
            townBackground = null;

            instance.SetActive(false);
            AssetsManager.Instance.ReleaseGameObject(instance);
        }

        townBackgroundKey = string.Empty;
    }

    /// <summary>
    /// 当前城镇的出生坐标 —— 来自背景外层控制器的 <c>StartPoint</c>。
    /// 外层还没建起来就退回原点（那是加出生点之前的老行为），
    /// **配漏了要退化，不能让人进不了城镇**。
    /// </summary>
    private Vector2 CurrentSpawnPosition() =>
        townBackground != null ? townBackground.SpawnPosition : Vector2.zero;
}
