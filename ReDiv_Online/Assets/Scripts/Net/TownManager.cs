using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using ReDiv.Net.Bindings;
using SpacetimeDB;
using SpacetimeDB.ClientApi;
using UnityEngine;

namespace ReDiv.Net
{
    /// <summary>
    /// 城镇里的一个**其他**玩家。
    ///
    /// 是 class 不是 struct：坐标每 ~100ms 就更新一次，用引用类型可以**原地改字段**，
    /// 不用每次都往字典里塞一个新值。渲染那边持有同一个对象，读到的永远是最新的。
    /// </summary>
    public sealed class TownPlayer
    {
        public ulong CharacterId;
        public SpacetimeDB.ConnectionId ConnectionId;
        public string Name;
        public uint JobId;

        /// <summary>当前形态，用来取城镇控制预制体（<c>CharacterForm.SkeletonTown</c>）。</summary>
        public uint FormId;

        public uint Level;

        public float X;
        public float Y;

        /// <summary>1 右 / -1 左。</summary>
        public int Facing = 1;

        /// <summary>在不在走。收到的人据此播走路 / 待机。</summary>
        public bool Moving;

        /// <summary>
        /// 收到过坐标没有。选角行先到、坐标行后到是常态
        /// （对方刚进城镇、还没走第一步就没有坐标行），
        /// 这时候不该把他画在 (0,0) —— 渲染那边要判这个。
        /// </summary>
        public bool HasTransform;
    }

    /// <summary>
    /// 城镇 + 世界时间的门面，和 <see cref="AuthManager"/> / <see cref="CharacterManager"/>
    /// 一个套路：**界面只读它、只听事件，不碰 <c>Conn</c>**。
    ///
    /// 两件事：
    ///   <see cref="CurrentBandId"/>  当前时段（早=1 中=2 晚=3），来自公开表 <c>world_time</c>。
    ///                                全服一行、服务端算好的，**客户端不要自己按本地时钟算**。
    ///   <see cref="CurrentTownId"/>  自己在哪个城镇，来自公开表 <c>character_selection</c>
    ///                                里本连接那一行（也就是「进入游戏」之后才有）。
    ///
    /// 订阅时机：两张表都在**连上时**就订。
    ///   · <c>world_time</c> 是全局的，和登录无关；
    ///   · <c>character_selection</c> 虽然要等选角才有行，但订阅必须**早于** SelectCharacter，
    ///     否则成功那一行的 OnInsert 会漏（账号系统那边踩过这个坑）。
    ///
    /// ⚠️ 表回调**必须把 OnUpdate 一起挂**：同主键的删+插如果在同一个事务里
    /// （换角色就是「删本连接旧行 + 插新行」，主键都是 ConnectionId），
    /// SpacetimeDB 会合并成一次 update，只挂 Insert/Delete 会漏。
    /// </summary>
    public sealed class TownManager
    {
        private static TownManager instance;

        public static TownManager Instance
        {
            get
            {
                instance ??= new TownManager();
                instance.Attach();
                return instance;
            }
        }

        private TownManager()
        {
        }

        // ------------------------------------------------------------------
        // 对外状态
        // ------------------------------------------------------------------

        /// <summary>订阅是否已生效。**false 时下面两个值都不可信**（还没同步完，不是「没有」）。</summary>
        public bool IsReady { get; private set; }

        /// <summary>订阅生效时触发一次。</summary>
        public event Action Ready;

        /// <summary>
        /// 当前世界时段 id，对应配置表 <c>TbTimeBand</c>（早=1 中=2 晚=3）。
        /// **0 = 还不知道**（订阅没生效，或者服务端那一行还没建起来）。
        /// </summary>
        public uint CurrentBandId { get; private set; }

        /// <summary>时段变了（服务端切段）。背景控制器听这个换图。</summary>
        public event Action WorldTimeChanged;

        /// <summary>
        /// 自己当前所在城镇 id，对应配置表 <c>TbTown</c>。
        /// **0 = 不在城镇里**（还没「进入游戏」，或者刚退回选人界面）。
        /// </summary>
        public uint CurrentTownId { get; private set; }

        /// <summary>当前选中的角色 id。0 = 没在玩任何角色。</summary>
        public ulong CurrentCharacterId { get; private set; }

        /// <summary>所在城镇 / 所选角色变了。</summary>
        public event Action LocationChanged;

        /// <summary>当前时段的配置行。取不到（配置没导出 / 段 id 对不上）返回 null，**要判空**。</summary>
        public XFramework.TimeBand CurrentBand =>
            CurrentBandId == 0 ? null : XFramework.LubanManager.Instance.TbTimeBand?.GetOrDefault((int)CurrentBandId);

        /// <summary>当前城镇的配置行。取不到返回 null，**要判空**。</summary>
        public XFramework.Town CurrentTown =>
            CurrentTownId == 0 ? null : XFramework.LubanManager.Instance.TbTown?.GetOrDefault((int)CurrentTownId);

        /// <summary>
        /// 当前城镇 + 当前时段对应的**背景控制器预制体** Addressable 路径。
        /// 两者任一还不知道、或者配置里那一格是空的，都返回空串 —— **要判空**。
        ///
        /// 三列背景（早/中/晚）和三个时段是**按 BandId 硬对应**的。段数固定 3 段就是
        /// 因为这里是三列，服务端自检会守住「时间段表必须恰好 3 行」。
        /// </summary>
        public string CurrentBackgroundKey
        {
            get
            {
                XFramework.Town town = CurrentTown;

                if (town == null)
                {
                    return string.Empty;
                }

                return CurrentBandId switch
                {
                    1 => town.BgMorning ?? string.Empty,
                    2 => town.BgNoon ?? string.Empty,
                    3 => town.BgNight ?? string.Empty,
                    _ => string.Empty,
                };
            }
        }

        /// <summary>
        /// 同城镇的**其他**玩家（不含自己）。key 是 CharacterId。
        ///
        /// 由两张公开表拼出来：<c>character_selection</c> 给名字 / 职业 / 形态，
        /// <c>character_transform</c> 给坐标。坐标那张表变得很频繁，
        /// 所以**不要**在这里做整表重建 —— 见 <see cref="TownPlayer"/> 的说明。
        /// </summary>
        public IReadOnlyDictionary<ulong, TownPlayer> TownPlayers => townPlayers;

        /// <summary>有人进城镇 / 离开城镇（**不是**每次移动都触发）。</summary>
        public event Action TownPlayersChanged;

        private readonly Dictionary<ulong, TownPlayer> townPlayers = new Dictionary<ulong, TownPlayer>();

        // ------------------------------------------------------------------
        // 内部
        // ------------------------------------------------------------------

        private DbConnection conn;
        private bool attached;
        private bool subscribed;
        private SubscriptionHandle subscription;

        private void Attach()
        {
            if (attached)
            {
                return;
            }
            attached = true;

            SpacetimeConnection.Connected += HandleConnected;
            SpacetimeConnection.Disconnected += HandleConnectionLost;
            SpacetimeConnection.ConnectFailed += HandleConnectFailed;

            if (SpacetimeConnection.IsConnected)
            {
                HandleConnected();
            }
        }

        private void HandleConnected()
        {
            conn = SpacetimeConnection.Conn;

            if (conn == null)
            {
                return;
            }

            Subscribe();
        }

        private void HandleConnectionLost(Exception ex)
        {
            Unsubscribe();
            conn = null;
        }

        private void HandleConnectFailed(Exception ex) => HandleConnectionLost(ex);

        private void Subscribe()
        {
            if (subscribed)
            {
                return;
            }
            subscribed = true;

            HookTables();

            // identity 在订阅 SQL 里是十六进制字面量，**要带 0x 前缀**
            //（Identity.ToString() 给的是不带前缀的大写 hex，这个坑账号那边踩过）
            string identityHex = "0x" + SpacetimeConnection.LocalIdentity;

            subscription = conn.SubscriptionBuilder()
                .OnApplied(HandleSubscriptionApplied)
                .OnError(HandleSubscriptionError)
                .Subscribe(new[]
                {
                    // 全服一行，不用过滤
                    "SELECT * FROM world_time",
                    // 只要自己这个 Identity 的选角行（可能有多条连接，具体挑哪条见 RefreshFromCache）
                    $"SELECT * FROM character_selection WHERE identity = {identityHex}",
                });
        }

        private void Unsubscribe()
        {
            if (!subscribed)
            {
                return;
            }
            subscribed = false;

            UnhookTables();

            try
            {
                subscription?.Unsubscribe();
            }
            catch (Exception)
            {
                // 连接已经断了的话退订会抛，这时候本来也不用退了
            }

            subscription = null;

            TryUnsubscribe(townSubscription);
            townSubscription = null;
            subscribedTownId = 0;
            townPlayers.Clear();
            TownPlayersChanged?.Invoke();

            IsReady = false;
            CurrentBandId = 0;
            CurrentTownId = 0;
            CurrentCharacterId = 0;

            WorldTimeChanged?.Invoke();
            LocationChanged?.Invoke();
        }

        private void HookTables()
        {
            // OnUpdate 一定要挂，理由见类注释
            conn.Db.WorldTime.OnInsert += HandleWorldTimeInsert;
            conn.Db.WorldTime.OnUpdate += HandleWorldTimeUpdate;
            conn.Db.WorldTime.OnDelete += HandleWorldTimeDelete;

            conn.Db.CharacterSelection.OnInsert += HandleSelectionInsert;
            conn.Db.CharacterSelection.OnUpdate += HandleSelectionUpdate;
            conn.Db.CharacterSelection.OnDelete += HandleSelectionDelete;

            // 坐标：**只走单行更新**，不做整表重建 —— 这张表每 100ms 就变一次
            conn.Db.CharacterTransform.OnInsert += HandleTransformInsert;
            conn.Db.CharacterTransform.OnUpdate += HandleTransformUpdate;
            conn.Db.CharacterTransform.OnDelete += HandleTransformDelete;
        }

        private void UnhookTables()
        {
            if (conn == null)
            {
                return;
            }

            conn.Db.WorldTime.OnInsert -= HandleWorldTimeInsert;
            conn.Db.WorldTime.OnUpdate -= HandleWorldTimeUpdate;
            conn.Db.WorldTime.OnDelete -= HandleWorldTimeDelete;

            conn.Db.CharacterSelection.OnInsert -= HandleSelectionInsert;
            conn.Db.CharacterSelection.OnUpdate -= HandleSelectionUpdate;
            conn.Db.CharacterSelection.OnDelete -= HandleSelectionDelete;

            conn.Db.CharacterTransform.OnInsert -= HandleTransformInsert;
            conn.Db.CharacterTransform.OnUpdate -= HandleTransformUpdate;
            conn.Db.CharacterTransform.OnDelete -= HandleTransformDelete;
        }

        private void HandleSubscriptionApplied(SubscriptionEventContext ctx)
        {
            RefreshFromCache(notify: false);

            IsReady = true;
            Debug.Log($"[Town] 已同步：时段={CurrentBandId} 城镇={CurrentTownId}");

            SyncTownSubscription();

            Ready?.Invoke();
            WorldTimeChanged?.Invoke();
            LocationChanged?.Invoke();
        }

        // ------------------------------------------------------------------
        // 上报自己的坐标
        // ------------------------------------------------------------------

        /// <summary>
        /// 上报自己在城镇里的坐标。**调用方负责节流** —— 每帧调会把连接打满，
        /// 城镇主界面是「位置真的变了 + 距上次 ≥100ms」才调一次。
        ///
        /// 不返回结果、不等回应：这是「尽力而为」的状态同步，丢一两个包无所谓，
        /// 下一次上报就纠正回来了。所以这里不用 CharacterResult 那套等待槽。
        /// </summary>
        public void ReportTransform(float x, float y, int facing, bool moving)
        {
            if (conn == null || !conn.IsActive || CurrentCharacterId == 0)
            {
                return;
            }

            try
            {
                conn.Reducers.UpdateTransform(x, y, facing, moving);
            }
            catch (Exception ex)
            {
                // 断线的瞬间可能抛。移动上报不值得为它弹窗，记一条就够
                Debug.LogWarning($"[Town] 坐标上报失败：{ex.Message}");
            }
        }

        // ------------------------------------------------------------------
        // 同城镇玩家：第二段订阅（跟着当前城镇走）
        // ------------------------------------------------------------------

        private SubscriptionHandle townSubscription;

        /// <summary>townSubscription 现在订的是哪个城镇。0 = 没订。</summary>
        private uint subscribedTownId;

        /// <summary>
        /// 让「同城镇玩家」的订阅跟上当前城镇。
        ///
        /// **为什么要第二段订阅**：第一段按自己的 identity 订 <c>character_selection</c>，
        /// 只够知道「我在哪个城镇」；要看见别人就得按 <c>town_id</c> 订。
        /// 而 town_id 是运行时才知道的，订阅 SQL 是静态字符串 ⇒ 换城镇必须重订。
        ///
        /// ⚠️ 官方建议「更新订阅时先订新的再退旧的」，避免中间出现一段没有数据的空窗。
        /// 这里就是这个顺序。
        /// </summary>
        private void SyncTownSubscription()
        {
            if (conn == null)
            {
                return;
            }

            uint townId = CurrentTownId;

            if (townId == subscribedTownId)
            {
                return;
            }

            SubscriptionHandle previous = townSubscription;

            if (townId == 0)
            {
                // 离开城镇了：先清本地列表，再退订
                townSubscription = null;
                subscribedTownId = 0;
                ClearTownPlayers();
                TryUnsubscribe(previous);
                return;
            }

            subscribedTownId = townId;

            townSubscription = conn.SubscriptionBuilder()
                .OnApplied(_ => RebuildTownPlayers())
                .OnError((_, ex) => Debug.LogError($"[Town] 同城镇玩家订阅失败：{ex.Message}"))
                .Subscribe(new[]
                {
                    $"SELECT * FROM character_selection WHERE town_id = {townId}",
                    $"SELECT * FROM character_transform WHERE town_id = {townId}",
                });

            // 先订新的、再退旧的
            TryUnsubscribe(previous);
        }

        private static void TryUnsubscribe(SubscriptionHandle handle)
        {
            if (handle == null)
            {
                return;
            }

            try
            {
                handle.Unsubscribe();
            }
            catch (Exception)
            {
                // 连接已经断了的话退订会抛，这时候本来也不用退了
            }
        }

        /// <summary>
        /// 整表重建同城镇玩家列表。**只在「进出城镇」这类低频事件上调** ——
        /// 坐标每 100ms 就变一次，那条路走 <see cref="ApplyTransform"/> 单行更新。
        /// </summary>
        private void RebuildTownPlayers()
        {
            if (conn == null)
            {
                return;
            }

            townPlayers.Clear();

            foreach (var selection in conn.Db.CharacterSelection.Iter())
            {
                if (selection.TownId != subscribedTownId)
                {
                    continue;
                }

                // 跳过自己 —— 自己的角色是本地驱动的，不能被服务端坐标拉着走
                if (selection.ConnectionId == conn.ConnectionId)
                {
                    continue;
                }

                townPlayers[selection.CharacterId] = new TownPlayer
                {
                    CharacterId = selection.CharacterId,
                    ConnectionId = selection.ConnectionId,
                    Name = selection.CharacterName,
                    JobId = selection.JobId,
                    FormId = selection.FormId,
                    Level = selection.Level,
                };
            }

            // 把已经收到的坐标补上
            foreach (var transform in conn.Db.CharacterTransform.Iter())
            {
                ApplyTransform(transform, notify: false);
            }

            TownPlayersChanged?.Invoke();
        }

        private void ClearTownPlayers()
        {
            if (townPlayers.Count == 0)
            {
                return;
            }

            townPlayers.Clear();
            TownPlayersChanged?.Invoke();
        }

        /// <summary>
        /// 把一行坐标写进对应玩家。**不触发 TownPlayersChanged** ——
        /// 移动是高频的，每帧发事件会让界面白刷。渲染那边每帧自己读 <see cref="TownPlayers"/>。
        /// </summary>
        private void ApplyTransform(CharacterTransform row, bool notify = true)
        {
            if (conn == null || row.ConnectionId == conn.ConnectionId)
            {
                return;
            }

            if (!townPlayers.TryGetValue(row.CharacterId, out TownPlayer player))
            {
                return;
            }

            player.X = row.X;
            player.Y = row.Y;
            player.Facing = row.Facing;
            player.Moving = row.Moving;
            player.HasTransform = true;

            if (notify)
            {
                // 只有「第一次拿到坐标」才值得通知一次：渲染那边要在这一刻把角色摆到位
                TownPlayersChanged?.Invoke();
            }
        }

        private void HandleSubscriptionError(ErrorContext ctx, Exception ex)
        {
            Debug.LogError($"[Town] 城镇 / 世界时间订阅失败：{ex.Message}");

            IsReady = false;
            subscribed = false;
        }

        private void HandleWorldTimeInsert(EventContext ctx, WorldTime row) => RefreshFromCache();

        private void HandleWorldTimeUpdate(EventContext ctx, WorldTime oldRow, WorldTime newRow) =>
            RefreshFromCache();

        private void HandleWorldTimeDelete(EventContext ctx, WorldTime row) => RefreshFromCache();

        private void HandleSelectionInsert(EventContext ctx, CharacterSelection row) => RefreshFromCache();

        private void HandleSelectionUpdate(EventContext ctx, CharacterSelection oldRow, CharacterSelection newRow) =>
            RefreshFromCache();

        private void HandleSelectionDelete(EventContext ctx, CharacterSelection row) => RefreshFromCache();

        private void HandleTransformInsert(EventContext ctx, CharacterTransform row) => ApplyTransform(row);

        private void HandleTransformUpdate(EventContext ctx, CharacterTransform oldRow, CharacterTransform newRow) =>
            ApplyTransform(newRow, notify: false);

        private void HandleTransformDelete(EventContext ctx, CharacterTransform row)
        {
            if (townPlayers.TryGetValue(row.CharacterId, out TownPlayer player))
            {
                player.HasTransform = false;
            }
        }

        /// <summary>
        /// 整表重读，不做增量维护 —— 这两张表一共就两行，重读比想清楚
        /// 「同事务删+插会被合并成 update」便宜得多（角色列表那边也是这么处理的）。
        /// </summary>
        private void RefreshFromCache(bool notify = true)
        {
            uint bandBefore = CurrentBandId;
            uint townBefore = CurrentTownId;
            ulong characterBefore = CurrentCharacterId;

            if (conn == null)
            {
                CurrentBandId = 0;
                CurrentTownId = 0;
                CurrentCharacterId = 0;
            }
            else
            {
                CurrentBandId = conn.Db.WorldTime.Iter().FirstOrDefault().BandId;

                // ⚠️ 挑**本连接**那一行，不是「自己 identity 的第一行」——
                // 同一 identity 可能有多条连接（编辑器 + 真机、或者开两个客户端），
                // 拿错行会显示成别的窗口所在的城镇。账号系统那边是同一个道理。
                var mine = conn.Db.CharacterSelection.Iter()
                    .Where(s => s.ConnectionId == conn.ConnectionId)
                    .ToList();

                if (mine.Count > 0)
                {
                    CurrentTownId = mine[0].TownId;
                    CurrentCharacterId = mine[0].CharacterId;
                }
                else
                {
                    CurrentTownId = 0;
                    CurrentCharacterId = 0;
                }
            }

            if (!notify)
            {
                return;
            }

            if (CurrentBandId != bandBefore)
            {
                Debug.Log($"[Town] 时段切换：{bandBefore} -> {CurrentBandId}");
                WorldTimeChanged?.Invoke();
            }

            if (CurrentTownId != townBefore || CurrentCharacterId != characterBefore)
            {
                // 自己换了城镇 ⇒ 「同城镇玩家」那段订阅要跟着换
                SyncTownSubscription();
                LocationChanged?.Invoke();
            }
            else if (subscribedTownId != 0)
            {
                // 城镇没变，但可能是**别人**进出了这个城镇（他们的选角行增删）
                RebuildTownPlayers();
            }
        }
    }
}
