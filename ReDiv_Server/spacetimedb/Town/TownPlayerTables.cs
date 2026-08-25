using SpacetimeDB;

/// <summary>
/// 城镇里的玩家状态：坐标同步 + 账号钱包。
///
/// 两张表，公开性刚好相反，理由都写在各自注释里：
///   CharacterTransform  **公开**。同城镇的人要互相看见，所以坐标必须公开。
///   AccountWallet       **私有**。金币钻石是隐私，靠 View 只发给本人。
/// </summary>
public static partial class Module
{
    /// <summary>
    /// 城镇里角色的坐标（**公开表**）。同城镇的其他玩家靠订阅它显示出来。
    ///
    /// **为什么不把 X/Y 加到 <c>CharacterSelection</c> 上**：文档的拆表原则是
    /// 「按访问频率拆，不按实体拆」。CharacterSelection 是进城镇时写一次的低频行，
    /// 还冗余着名字 / 职业 / 等级 / 形态；坐标是移动中每 100ms 写一次的高频数据。
    /// 混在一张表里的话，**每走一步都会把名字等级那一整行重推给所有订阅者**。
    ///
    /// 主键用 ConnectionId 和 CharacterSelection 保持一致：坐标是**连接级**状态
    /// （同一账号两个端各自在城镇里跑），断开时按 ConnectionId 精确清掉。
    ///
    /// ⚠️ 坐标是**客户端上报、服务端只转发**的（用户 2026-08-25 定的）：
    /// 服务端不校验速度，改过的客户端可以瞬移。城镇里瞬移没有收益，所以先这样；
    /// 真要管就在 <c>UpdateTransform</c> 里比对上一次的 UpdatedAt 和距离。
    /// </summary>
    [SpacetimeDB.Table(Accessor = "CharacterTransform", Public = true)]
    public partial struct CharacterTransform
    {
        [PrimaryKey]
        public ConnectionId ConnectionId;

        /// <summary>
        /// 在哪个城镇。**必须有索引** —— 客户端按
        /// <c>WHERE town_id = N</c> 订阅，只同步同城镇的人。
        /// </summary>
        [SpacetimeDB.Index.BTree]
        public uint TownId;

        /// <summary>
        /// 哪个角色。客户端拿它和 <c>CharacterSelection</c> 的行对起来
        /// （名字 / 职业 / 形态在那边），所以这张表只放会动的东西。
        /// </summary>
        [SpacetimeDB.Index.BTree]
        public ulong CharacterId;

        /// <summary>客户端用它认出"哪一个是我"，好跳过自己那份（自己是本地驱动的）。</summary>
        public Identity Identity;

        public float X;

        public float Y;

        /// <summary>
        /// 朝向：1 = 朝右，-1 = 朝左。只存左右不存角度 —— 城镇是 2D 横版，
        /// Spine 靠翻 scale.x 换朝向，没有别的方向。
        /// </summary>
        public int Facing;

        /// <summary>
        /// 是不是在移动。收到的人据此播走路 / 待机动画 ——
        /// 光靠比较前后两次坐标判断会在丢包或停止那一帧抖动。
        /// </summary>
        public bool Moving;

        /// <summary>最后一次上报的时间。以后要做速度校验或超时清理就靠它。</summary>
        public Timestamp UpdatedAt;
    }

    /// <summary>
    /// 账号钱包（**私有表**）。金币和钻石**全角色共享**，所以挂账号不挂角色
    /// （用户 2026-08-25 明确说的）。
    ///
    /// **为什么不加到 <c>Account</c> 上**：Account 存的是凭据，每次登录才读一次；
    /// 钱包是会频繁变的玩法数据。文档明确反对宽表 —— 混在一起会让每次加钱
    /// 都把口令哈希那一行也重写一遍。
    ///
    /// 私有 + <c>MyWallet</c> View 下发：别人的钱包余额不该能被订阅到。
    /// </summary>
    [SpacetimeDB.Table(Accessor = "AccountWallet")]
    public partial struct AccountWallet
    {
        [PrimaryKey]
        public ulong AccountId;

        /// <summary>金币。</summary>
        public ulong Coin;

        /// <summary>钻石（付费 / 活动货币）。</summary>
        public ulong Gem;
    }
}
