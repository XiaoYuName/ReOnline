using SpacetimeDB;

/// <summary>
/// 角色系统的表定义。
///
/// 两张表，按访问频率分：
///   Character           私有。角色档案，只在建 / 删 / 选人时读写（低频）。
///   CharacterSelection  公开。这条连接当前选了哪个角色（进城镇后才有）。
///
/// 角色列表**不靠公开表下发**，而是靠 <c>MyCharacter</c> View（见 CharacterViews.cs）——
/// 原因写在那边：AccountId 是自增整数，公开表让人一猜就能订阅到别人的角色列表。
///
/// 玩法态（地图 / 坐标 / HP / 体力）**故意还没建表**。等玩法定型后以 CharacterId 为主键
/// 单开表，别往 Character 上堆：那张表每次选人界面才读一次，和高频变动的数据放一起
/// 会白白放大同步量（官方明确反对宽表）。
/// </summary>
public static partial class Module
{
    /// <summary>新账号默认解锁的角色栏位数。</summary>
    public const uint DefaultCharacterSlots = 4;

    /// <summary>
    /// 栏位数的硬上限。扩栏位的入口（付费 / 活动）等相应系统做了再加，
    /// 但上限先钉在这里，免得将来某个 Reducer 把它写成任意值。
    /// </summary>
    public const uint MaxCharacterSlots = 8;

    /// <summary>
    /// 角色档案（**私有表**）。
    ///
    /// 选人界面需要的字段全在这张表里 —— 选人本身就是低频操作，一次读完最省事。
    /// </summary>
    [SpacetimeDB.Table(Accessor = "Character")]
    public partial struct Character
    {
        [PrimaryKey]
        [AutoInc]
        public ulong CharacterId;

        /// <summary>
        /// 属于哪个账号。**必须有索引**：View 里不能 Iter()，只能靠索引 Filter，
        /// 栏位计数也走它。删了这个索引，角色列表就下发不了。
        /// </summary>
        [SpacetimeDB.Index.BTree]
        public ulong AccountId;

        /// <summary>
        /// 归一化后的角色名，**全服唯一**。
        ///
        /// 软删时会被改写成 <c>#del#&lt;CharacterId&gt;</c> 这种保留形式，把名字立刻释放出来
        /// （<c>#</c> 不在合法字符集里，所以永远撞不上真名字）。原名留在 <see cref="Name"/>
        /// 里，将来要做恢复或审计都还在。
        /// </summary>
        [Unique]
        public string NameKey;

        /// <summary>玩家输入的原样角色名，展示用。软删后仍保留原值。</summary>
        public string Name;

        /// <summary>
        /// 职业 id（也就是「角色」那一层，凯露 / …），对应配置表 <c>TbCharacterJob</c>。
        /// 建角色时选定，之后不变。
        ///
        /// 注意这和玩家自己起的 <see cref="Name"/> 是两回事：职业名来自配置、大家都一样，
        /// 角色名是玩家输入的、全服唯一。
        /// </summary>
        public uint JobId;

        public uint Level;

        public ulong Exp;

        public Timestamp CreatedAt;

        /// <summary>最近一次选中进入游戏的时间。没进过是 null。选人界面按它排序。</summary>
        public Timestamp? LastPlayedAt;

        /// <summary>
        /// 软删标记。非 null = 已删除，选人界面看不到。
        /// 行保留是为了误删可恢复、数据可查；代价是所有查询都要带上「未删除」条件 ——
        /// 统一走 <c>IsAlive</c> 判断，别在各处手写。
        /// </summary>
        public Timestamp? DeletedAt;

        /// <summary>
        /// 当前生效的专职（配置表 <c>TbJobSpecialization</c> 的 SpecId）。
        ///
        /// 一个角色可以有多个可用专职，但同时只有一个生效，玩家可以切换（SwitchSpecialization）。
        /// 「哪些专职可用」**不存库**：由配置里的 UnlockLevel 和角色等级现算 ——
        /// 以后要改成「做完任务才解锁」就得加存储，那时再说。
        /// 形态（专职名 / 觉醒名 / 一次觉醒名）同理不存，见 CurrentFormStage。
        ///
        /// ⚠️ 这个字段**必须留在结构体末尾**：SpacetimeDB 只支持在末尾追加列，
        /// 插到中间会被判成 reorder，直接要求手工迁移（实测撞过）。
        /// <c>[Default(0u)]</c> 让已有角色行拿到 0；0 表示「老数据还没有专职」，
        /// 读到 0 就退回该职业的 DefaultSpecId（见 ResolveSpecId），不用清库。
        /// </summary>
        [Default(0u)]
        public uint SpecId;
    }

    /// <summary>
    /// 当前选中的角色（**公开表**），一条连接一行。
    ///
    /// 主键是 ConnectionId 而不是 CharacterId / AccountId：选角是**连接级**状态，
    /// 同一账号的另一条连接选了别的角色互不干扰，断开时按 ConnectionId 精确清理。
    ///
    /// 为什么不把这几个字段直接加到 Session 上：Session 是账号层的状态，
    /// 而选角是另一个生命周期 —— 玩家可以不断线地反复「进游戏 / 返回选人界面」。
    ///
    /// 公开表 ⇒ 所有客户端都能看到当前在线的角色（名字 / 职业 / 等级）。这是有意的，
    /// 在线列表、频道人数都要用。**只放不敏感字段**，要收紧得靠 View。
    /// </summary>
    [SpacetimeDB.Table(Accessor = "CharacterSelection", Public = true)]
    public partial struct CharacterSelection
    {
        [PrimaryKey]
        public ConnectionId ConnectionId;

        /// <summary>客户端按它订阅自己那一行。</summary>
        [SpacetimeDB.Index.BTree]
        public Identity Identity;

        [SpacetimeDB.Index.BTree]
        public ulong AccountId;

        /// <summary>反查用：删角色时要看它是不是正被某条连接选中。</summary>
        [SpacetimeDB.Index.BTree]
        public ulong CharacterId;

        /// <summary>冗余一份展示信息，省得为了画在线列表再去查角色表（而且角色表是私有的）。</summary>
        public string CharacterName;

        public uint JobId;

        public uint Level;

        public Timestamp EnteredAt;

        /// <summary>当前专职。在线列表要靠它显示正确的形象。末尾追加，理由同上。</summary>
        [Default(0u)]
        public uint SpecId;
    }
}
