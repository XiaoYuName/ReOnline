using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using ReDiv.Net.Bindings;
using SpacetimeDB;
using SpacetimeDB.ClientApi;
using UnityEngine;

namespace ReDiv.Net
{
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
        }

        private void HandleSubscriptionApplied(SubscriptionEventContext ctx)
        {
            RefreshFromCache(notify: false);

            IsReady = true;
            Debug.Log($"[Town] 已同步：时段={CurrentBandId} 城镇={CurrentTownId}");

            Ready?.Invoke();
            WorldTimeChanged?.Invoke();
            LocationChanged?.Invoke();
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
                LocationChanged?.Invoke();
            }
        }
    }
}
