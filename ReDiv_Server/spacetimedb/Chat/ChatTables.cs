using SpacetimeDB;

/// <summary>
/// 聊天消息的表定义。
///
/// 一张公开表 <c>ChatMessage</c> 承载所有频道，靠 <see cref="ChatMessage.TownId"/> 分域：
///   · **附近消息**：<c>TownId</c> = 发送者所在城镇 ⇒ 只有同城镇的人订阅得到；
///   · **世界消息**：<c>TownId</c> = <see cref="ChatWorldScopeTownId"/>（0，不存在的城镇 id）
///     ⇒ 所有人在任何地方都订阅得到。
///
/// **为什么用「一张表 + TownId 分域」而不是两张表、也不是 <c>WHERE channel = x AND town_id = y</c>**：
/// 订阅 SQL 是静态字符串，条件越少越不容易出错；把「世界」编码成 0 号域之后，
/// 两个频道的订阅都退化成同一个形状 <c>WHERE town_id = N</c>，客户端只要换个数字。
/// <see cref="ChatMessage.Channel"/> 仍然留着 —— 客户端要按频道上色 / 加前缀，
/// 而这个信息**不能**从 TownId 反推（以后要是加「队伍」「公会」频道就更是）。
///
/// **为什么是持久表而不是事件表**（<c>Event = true</c> 那种推完就删的）：
/// 玩家进城镇要能看到最近几条对话（用户 2026-08-26 定的）。事件表零存储、零裁剪，
/// 但进城镇时聊天框是空的，而且订阅切换那一瞬间发的消息会丢、`spacetime sql` 也查不到。
/// 代价是要**滚动裁剪**，见 <see cref="ChatHistoryPerScope"/>。
/// </summary>
public static partial class Module
{
    /// <summary>附近频道。只有同城镇的人看得到。</summary>
    public const uint ChatChannelNearby = 1;

    /// <summary>世界频道。所有人在任何地方都看得到。</summary>
    public const uint ChatChannelWorld = 2;

    /// <summary>
    /// 世界消息占用的「域 id」。城镇 id 是正数（配置自检守着「id 必须是正数」），
    /// 所以 0 永远不会和某个真城镇撞上。
    /// </summary>
    public const uint ChatWorldScopeTownId = 0;

    /// <summary>
    /// 每个域（一个城镇 / 世界频道）保留的消息条数上限，超了就删最旧的。
    ///
    /// 这个数同时决定了三件事，改的时候一起想：
    ///   1. 玩家进城镇能回看多少条；
    ///   2. 每发一条消息要扫多少行（裁剪时按域 Filter 一遍）；
    ///   3. 客户端订阅生效时一次性收到多少行。
    /// 50 条对「聊天框能看到最近的对话」够用，也不至于让每条消息都变成一次大扫描。
    /// </summary>
    public const int ChatHistoryPerScope = 50;

    /// <summary>
    /// 同一个角色两条消息之间的最小间隔（微秒）。1 秒。
    ///
    /// ⚠️ 这个限流**能**做，而登录失败次数锁定做不了 —— 区别在于写的时机：
    /// 这里只在**成功**的那条路上留下痕迹（消息本身就是痕迹），
    /// 被拒的那次抛异常回滚，什么都不用存。账号那边要存的恰恰是「失败」，
    /// 而失败必然回滚，所以存不下来（见服务端 README「有意没做的事」）。
    /// </summary>
    public const long ChatCooldownMicros = 1_000_000;

    /// <summary>
    /// 一条聊天消息（**公开表**）。
    ///
    /// 客户端按 <c>WHERE town_id = N</c> 订阅：附近消息填自己所在城镇，
    /// 世界消息填 <see cref="ChatWorldScopeTownId"/>。
    /// </summary>
    [SpacetimeDB.Table(Accessor = "ChatMessage", Public = true)]
    public partial struct ChatMessage
    {
        /// <summary>
        /// 主键，自增。
        ///
        /// ⚠️ **不要单独拿它当时序用。** 官方规则明确写着自增 id 不保证连续、
        /// 也不保证单调（<c>ReDiv_Server/CLAUDE.md</c> 的 Critical Rules 第 4 条：
        /// 「Auto-increment IDs are not sequential. Gaps are normal, do not use for ordering」）。
        /// 排序的权威是 <see cref="SentAt"/>，MessageId 只当**平局裁判** ——
        /// 两条消息时间戳一模一样时得有个稳定顺序，否则客户端每次重排结果都可能不同。
        /// </summary>
        [PrimaryKey]
        [AutoInc]
        public ulong MessageId;

        /// <summary>
        /// 频道：<see cref="ChatChannelNearby"/> / <see cref="ChatChannelWorld"/>。
        /// 给客户端做区分显示用，**不参与订阅过滤**（那是 TownId 的活）。
        /// </summary>
        public uint Channel;

        /// <summary>
        /// 可见域。附近消息 = 城镇 id，世界消息 = <see cref="ChatWorldScopeTownId"/>。
        /// **必须有索引** —— 客户端靠 <c>WHERE town_id = N</c> 订阅，
        /// 裁剪也要按域 Filter。
        /// </summary>
        [SpacetimeDB.Index.BTree]
        public uint TownId;

        /// <summary>
        /// 谁发的。**必须有索引** —— 冷却检查要按发送者找他最近一条消息。
        /// </summary>
        [SpacetimeDB.Index.BTree]
        public ulong SenderCharacterId;

        /// <summary>
        /// 发送者的角色名，**发送那一刻的快照**。
        ///
        /// 为什么冗余而不让客户端去 join：<c>Character</c> 是私有表，客户端根本订阅不到；
        /// <c>CharacterSelection</c> 虽然有名字，但那个人一离开城镇 / 下线，行就没了 ——
        /// 于是历史消息会变成「无名氏说了句话」。名字存下来才是对的。
        /// </summary>
        public string SenderName;

        /// <summary>消息正文。已经过 <c>ChatRules.NormalizeContent</c> 清洗。</summary>
        public string Content;

        /// <summary>
        /// 发送时间，也是**排序的权威**（见 <see cref="MessageId"/> 为什么不能拿 id 排）。
        /// 客户端按 <c>(SentAt, MessageId)</c> 升序排就是发言顺序。
        /// </summary>
        public Timestamp SentAt;

        /// <summary>
        /// 发送者的角色 / 职业，**发送那一刻的快照**。客户端拿
        /// <c>(SenderJobId, SenderFormId)</c> 去配置表 <c>CharacterForm</c> 取
        /// <c>IconKey</c> 当聊天列表里的头像。
        ///
        /// **为什么也要冗余**：和 <see cref="SenderName"/> 同一个理由，而且世界频道更硬 ——
        /// 说话的人可能在**另一个城镇**，本地根本没订阅到他的 <c>CharacterSelection</c>，
        /// 想 join 也 join 不到。
        ///
        /// ⚠️ 这两列是 2026-08-26 追加的，所以只能加在 **struct 末尾** 并带
        /// <c>[Default(0)]</c>（服务端 README「环境地雷」：插到中间会被判成列重排，
        /// publish 直接拒）。
        /// </summary>
        [Default(0)]
        public uint SenderJobId;

        /// <summary>发送者当时的形态。觉醒之后头像会变，所以存形态不只存角色。</summary>
        [Default(0)]
        public uint SenderFormId;
    }
}
