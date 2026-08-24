using SpacetimeDB;

/// <summary>
/// 账号系统的表定义。
///
/// 一共三张表 + 一张事件表，职责分开的理由写在各自注释里：
///   Account          私有。凭据（用户名 + 口令哈希），客户端永远看不到。
///   IdentityBinding  私有。Identity → 账号，用来做免密重连。
///   Session          公开。当前在线会话，客户端订阅它来判断「我登录上了没有」。
///   SessionClosed    公开事件表。会话被服务端主动关闭时通知那一端（顶号 / 登出）。
/// </summary>
public static partial class Module
{
    /// <summary>
    /// 账号（**私有表**）。
    ///
    /// 私有意味着：客户端订阅不到，<c>spacetime generate</c> 默认也不给它生成客户端绑定，
    /// 口令哈希不可能因为写错一句订阅 SQL 就漏出去。所有读写都只在 Reducer 里发生。
    ///
    /// 按访问频率而不是按实体拆表 —— 玩法数据（昵称、等级、体力……）以后单开表挂 AccountId，
    /// 别往这张表上堆：它每次登录才读一次，和高频变动的数据放一起会白白放大同步量。
    /// </summary>
    [SpacetimeDB.Table(Accessor = "Account")]
    public partial struct Account
    {
        [PrimaryKey]
        [AutoInc]
        public ulong AccountId;

        /// <summary>
        /// 归一化后的用户名，唯一索引。ASCII 小写，用来做「大小写不敏感」的重名判断和登录查找。
        /// 登录时先归一化再 Find，所以 Alice / alice / ALICE 是同一个账号。
        /// </summary>
        [Unique]
        public string UsernameKey;

        /// <summary>注册时输入的原样用户名，只用于展示。</summary>
        public string Username;

        /// <summary>PBKDF2-HMAC-SHA256 导出的密钥，Base64。</summary>
        public string PasswordHash;

        /// <summary>该账号独有的随机盐，Base64。每个账号一份，防彩虹表和撞库比对。</summary>
        public string PasswordSalt;

        /// <summary>算这行哈希时用的迭代次数。存下来才能在提高参数后渐进迁移旧账号。</summary>
        public uint HashIterations;

        public Timestamp CreatedAt;

        /// <summary>最近一次登录成功的时间。从没登录过（理论上不会，注册即登录）时为 null。</summary>
        public Timestamp? LastLoginAt;

        /// <summary>
        /// 已解锁的角色栏位数。栏位可扩展，所以存在账号上而不是写成全局常量。
        ///
        /// ⚠️ 值由 <c>Register</c> 插入时显式写 <see cref="DefaultCharacterSlots"/>。
        /// 这里**不要**加 <c>[Default]</c> 图省事 —— 那个只在迁移时给已有行回填，
        /// 对新插入的行无效，会得到一个栏位数为 0、建不出角色的账号（实测踩过）。
        /// </summary>
        public uint CharacterSlots;
    }

    /// <summary>
    /// Identity → 账号 的绑定（**私有表**），免密重连靠它。
    ///
    /// SpacetimeDB 的 Identity 是客户端凭 AuthToken 换来的长期身份，Unity 侧存在 PlayerPrefs 里
    /// （见 SpacetimeConnection.HandleConnect）。登录成功时把当前 Identity 记在这里，
    /// 之后 ClientConnected 钩子发现这条绑定就直接建会话，不用再输口令。
    ///
    /// 一个账号同一时刻只保留**一条**绑定：顶号时会把该账号其它 Identity 的绑定删掉，
    /// 否则被顶掉的那台机器一重连又会把新登录的顶下去，两边来回打。
    /// </summary>
    [SpacetimeDB.Table(Accessor = "IdentityBinding")]
    public partial struct IdentityBinding
    {
        /// <summary>一个 Identity 只能绑一个账号，所以直接拿它当主键。</summary>
        [PrimaryKey]
        public Identity Identity;

        /// <summary>反查用：顶号时要按账号找出所有绑定。</summary>
        [SpacetimeDB.Index.BTree]
        public ulong AccountId;

        public Timestamp BoundAt;
    }

    /// <summary>
    /// 在线会话（**公开表**）。客户端订阅自己那一行来判断登录状态。
    ///
    /// 主键是 ConnectionId 而不是 Identity：同一个 Identity 可能有多条连接
    /// （一台机器上跑两份同一个客户端包就是同一个 Identity，因为 AuthToken 存在同一个位置）。
    /// 一条连接一行，断开时按 ConnectionId 精确清理。
    ///
    /// 公开表意味着所有客户端都能订阅到全部行，也就是能看到当前在线的用户名列表。
    /// 这是有意的（在线列表本身有用），所以这里**只放不敏感字段**。
    /// 要收紧的话得靠 View 或 ClientVisibilityFilter，别往这张表加隐私字段。
    /// </summary>
    [SpacetimeDB.Table(Accessor = "Session", Public = true)]
    public partial struct Session
    {
        [PrimaryKey]
        public ConnectionId ConnectionId;

        /// <summary>客户端按它订阅自己的会话行；顶号时也按它区分「是不是同一台设备」。</summary>
        [SpacetimeDB.Index.BTree]
        public Identity Identity;

        /// <summary>顶号时要按账号找出所有在线会话。</summary>
        [SpacetimeDB.Index.BTree]
        public ulong AccountId;

        /// <summary>冗余一份展示用用户名，省得客户端为了显示名字再去查一次。</summary>
        public string Username;

        public Timestamp LoginAt;
    }

    /// <summary>会话被服务端主动关掉的原因。</summary>
    [SpacetimeDB.Type]
    public enum SessionCloseReason
    {
        /// <summary>这个账号在别的设备上登录了（顶号）。</summary>
        KickedByNewLogin,

        /// <summary>自己调了 Logout。</summary>
        LoggedOut,

        /// <summary>同一台设备上换成了另一个账号登录。</summary>
        SwitchedAccount,
    }

    /// <summary>
    /// 会话关闭通知（**事件表**）。
    ///
    /// 事件表的行在事务提交时推给订阅者，然后立刻删除 —— 客户端只会收到 OnInsert，
    /// 表里查不到历史。用它来区分「我的 Session 行为什么没了」：
    /// 光看 Session 的 OnDelete 分不清是被顶号还是自己登出，更分不清是断线。
    ///
    /// ⚠️ 事件表的 Event 标记**发布后不可更改**，改了迁移会失败，要改只能清库重发。
    /// </summary>
    [SpacetimeDB.Table(Accessor = "SessionClosed", Public = true, Event = true)]
    public partial struct SessionClosed
    {
        /// <summary>客户端按它过滤出发给自己的通知。</summary>
        public Identity Identity;

        /// <summary>被关掉的是哪条连接。同一 Identity 多连接时用它区分。</summary>
        public ConnectionId ConnectionId;

        public SessionCloseReason Reason;
    }
}
