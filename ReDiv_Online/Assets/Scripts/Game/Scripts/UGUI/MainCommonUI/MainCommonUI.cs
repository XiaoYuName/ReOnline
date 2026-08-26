using System.Collections.Generic;
using ReDiv.Net;
using ReDiv.Net.Bindings;
using UnityEngine;
using XFramework;

/// <summary>
/// 城镇主界面 —— 选人界面点「进入游戏」之后进的就是这里。
///
/// 四件事：
///   1. **背景 + 出生点** —— 背景是**世界空间**的，按「当前城镇 + 当前时段」换；
///      出生点在背景外层控制器的 <c>StartPoint</c> 上；
///   2. **右上角信息** —— 等级 / 经验 / 体力（角色级）+ 金币 / 钻石（账号级，全角色共享）；
///   3. **自己的角色** —— 按 (JobId, FormId) 取城镇控制预制体，摇杆驱动移动并上报坐标；
///   4. **同城镇的其他玩家** —— 按服务端推的坐标插值跟随；
///   5. **NPC** —— 按配置表 <c>TownNpc</c> 摆在固定坐标上（纯客户端，服务端不知道它们）。
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
        // ⚠️ 背景必须在角色之前刷：**出生点在背景外层控制器身上**，
        // 反过来的话第一次进城镇会落在原点
        RefreshBackground();
        RefreshInfo();
        RefreshSelfCharacter();
        RefreshOtherCharacters();
        RefreshNpcs();
    }

    public override void Close()
    {
        UnhookEvents();

        ClearCharacters();
        ReleaseBackground();

        base.Close();
    }

    protected override void OnDestroy()
    {
        UnhookEvents();
        ClearCharacters();
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
        RefreshNpcs();
    }

    /// <summary>换城镇 / 换角色：背景、自己的形象、别人都要重来。</summary>
    private void HandleLocationChanged()
    {
        RefreshBackground();
        RefreshInfo();
        RefreshSelfCharacter();
        RefreshOtherCharacters();
        RefreshNpcs();
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

        // 进城镇的落点：**出生点**，来自城镇区域预制体（配置表 Town 的 AreaPrefab 列）。
        // 服务端只记「在哪个城镇」、从来不存坐标，所以每次进城镇都是从出生点开始走
        Vector2 spawn = CurrentSpawnPosition();

        selfCharacter.Teleport(spawn);
        lastReportedPosition = spawn;
        lastReportTime = 0f;
        lastReportedMoving = false;

        // 立刻上报一次，别人才能马上看见我
        town.ReportTransform(spawn.x, spawn.y, selfCharacter.Facing, false);
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
