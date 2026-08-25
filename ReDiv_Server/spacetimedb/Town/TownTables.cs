using SpacetimeDB;

/// <summary>
/// 城镇与世界时间的表定义。
///
/// 三张表，职责各不相同：
///   CharacterLocation  私有。**权威**存储：某个角色在哪个城镇。跨会话保留。
///   WorldTime          公开，**全服一行**。当前是哪个时段（早/中/晚），客户端订阅它换背景。
///   WorldTimeTimer     定时表。到点跑 TickWorldTime 重算时段。
///
/// 为什么位置存在**角色**上而不是账号上：一个账号多个角色（DNF 那种），
/// 各自在哪个城镇是各自的进度。用户 2026-08-25 明确定的。
///
/// 为什么时段要落成一张公开表、而不是让客户端自己按本地时钟算：
///   1. 全服统一 —— 所有人同时切时段，以后做「夜间刷夜行怪」这类玩法才对得上；
///   2. 玩家改本地时钟没用；
///   3. 切段是**推送**，客户端不用轮询。
/// 用户 2026-08-25 明确选的这条路。
/// </summary>
public static partial class Module
{
    /// <summary>
    /// 配置表 <c>TbTimeBand.StartHour</c> 用的是「服务器本地时」，这里是它相对 UTC 的偏移。
    ///
    /// <c>ctx.Timestamp</c> 是 UTC，所以算时段前要先加这个偏移。
    /// 这不是策划要调的数值（那是 StartHour 的事），而是这个服的部署属性 ——
    /// 所以是常量而不是配置列。改它等于改「这个服按哪个时区过日子」。
    /// </summary>
    public const int ServerUtcOffsetHours = 8;

    /// <summary>
    /// <see cref="WorldTime"/> 只有一行，主键固定用这个值。
    /// 用「单行表 + 固定主键」而不是别的花样：客户端一句
    /// <c>SELECT * FROM world_time</c> 就订完了，而且带主键才有 OnUpdate。
    /// </summary>
    public const uint WorldTimeRowId = 1;

    /// <summary>
    /// 角色当前所在城镇（**私有表**）。这是权威存储。
    ///
    /// 主键是 CharacterId 而不是 AccountId —— 位置是角色级的。
    /// 按文档约定，玩法态**单开表挂 CharacterId**，不往 <c>Character</c> 上堆：
    /// 那张表只在选人界面读一次，和会频繁变动的位置放一起会白白放大同步量。
    /// </summary>
    [SpacetimeDB.Table(Accessor = "CharacterLocation")]
    public partial struct CharacterLocation
    {
        [PrimaryKey]
        public ulong CharacterId;

        /// <summary>
        /// 冗余一份账号 id。**必须有索引**：以后要按账号批量查位置（比如选人界面
        /// 显示「上次在哪」）只能靠索引 Filter —— View 里不能 Iter()。
        /// </summary>
        [SpacetimeDB.Index.BTree]
        public ulong AccountId;

        /// <summary>所在城镇，对应配置表 <c>TbTown</c>。</summary>
        public uint TownId;

        /// <summary>最近一次进入这个城镇的时间。</summary>
        public Timestamp EnteredAt;
    }

    /// <summary>
    /// 当前世界时段（**公开表，全服一行**）。
    ///
    /// 只放「现在是哪一段」，**不放当前时间戳** —— 放了的话每次 Tick 都会推一次全量更新，
    /// 而客户端真正关心的只有「段变了没」。现在的写法是段没变就不写表，
    /// 于是订阅者一天只会收到 3 次推送。
    /// </summary>
    [SpacetimeDB.Table(Accessor = "WorldTime", Public = true)]
    public partial struct WorldTime
    {
        /// <summary>固定是 <see cref="WorldTimeRowId"/>。</summary>
        [PrimaryKey]
        public uint Id;

        /// <summary>当前时段 id，对应配置表 <c>TbTimeBand</c>（早=1 中=2 晚=3）。</summary>
        public uint BandId;

        /// <summary>这一段是什么时候切进来的。客户端可以用它做「刚切段」的表现。</summary>
        public Timestamp ChangedAt;
    }

    /// <summary>
    /// 时段重算的定时器。
    ///
    /// 用**固定间隔轮询**而不是「精确排到下一个边界」：
    ///   · 间隔重复是自愈的 —— 改了配置边界、或者重新 publish 过，下一跳自然就对了；
    ///   · 排到精确时刻是一次性的，边界改了就得记得重排，容易漏。
    /// 一分钟一跳对「换个背景」这种表现层需求完全够，而且段没变就不写表，
    /// 所以绝大多数跳是纯读、什么都不推。
    /// </summary>
    [SpacetimeDB.Table(
        Accessor = "WorldTimeTimer",
        Scheduled = nameof(TickWorldTime),
        ScheduledAt = nameof(ScheduledAt)
    )]
    public partial struct WorldTimeTimer
    {
        [PrimaryKey]
        [AutoInc]
        public ulong ScheduledId;

        public ScheduleAt ScheduledAt;
    }
}
